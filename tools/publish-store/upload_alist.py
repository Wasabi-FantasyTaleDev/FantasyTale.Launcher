"""Upload a generated store or launcher graph to AList, verifying public bytes before publishing its index."""
from __future__ import annotations
import argparse
from concurrent.futures import FIRST_COMPLETED, ThreadPoolExecutor, wait
import hashlib
import json
import os
import sys
import time
from threading import Event, Lock, Thread, local
from pathlib import Path
import tomllib
from urllib.parse import quote, urlsplit
import requests
from publish_store import reject_links, safe_path
from server_profiles import server_profiles


def network_failure_summary(error):
    """Describe transport causes and OS codes without rendering request URLs or exception messages."""
    pending, visited, causes = [error], set(), []
    while pending:
        current = pending.pop()
        if id(current) in visited:
            continue
        visited.add(id(current))
        detail = type(current).__name__
        for field in ("errno", "winerror"):
            value = getattr(current, field, None)
            if type(value) is int:
                detail += f" {field}={value}"
        if detail not in causes:
            causes.append(detail)
        for nested in (current.__cause__, current.__context__, *current.args):
            if isinstance(nested, BaseException):
                pending.append(nested)
    return "; ".join(causes)


class UploadProgress:
    """Report file reads separately from the server response and public integrity verification."""
    def __init__(self, stream, size, name):
        self.stream, self.size, self.name = stream, size, name
        self.stopped = Event()
        self.thread = None

    def __enter__(self):
        print(f"Uploading {self.name}: {self.size} bytes", flush=True)
        if self.size >= 8 * 1024 * 1024:
            self.thread = Thread(target=self.report, name="alist-upload-progress", daemon=True)
            self.thread.start()
        return self

    def report(self):
        started = time.monotonic()
        while not self.stopped.wait(5):
            position = self.stream.tell()
            stage = "waiting for AList response" if position == self.size else "sending payload"
            print(f"Upload {self.name}: {position}/{self.size} bytes; {stage}; {time.monotonic() - started:.0f}s", flush=True)

    def __exit__(self, *_error):
        self.stopped.set()
        if self.thread is not None:
            self.thread.join()


def retry_network(operation):
    """Retry transient transport failures with fresh metadata; permanent errors remain immediate failures."""
    for attempt in range(4):
        try:
            return operation()
        except requests.RequestException as error:
            status = error.response.status_code if error.response is not None else None
            transient = isinstance(error, (requests.ConnectionError, requests.Timeout, requests.exceptions.JSONDecodeError, requests.exceptions.ChunkedEncodingError)) or status in (408, 429, 500, 502, 503, 504)
            if not transient or attempt == 3:
                raise
            print(f"Transient network failure ({network_failure_summary(error)}, {status or 'no HTTP response'}); retry {attempt + 1}/3", flush=True)
            time.sleep(2**attempt)


def verify_parallel(items, operation, workers):
    """Keep a bounded worker window busy without waiting for each slow batch member."""
    remaining = iter(items)
    with ThreadPoolExecutor(max_workers=workers) as pool:
        pending = {pool.submit(operation, item) for item in list_items(remaining, workers)}
        try:
            while pending:
                completed, pending = wait(pending, return_when=FIRST_COMPLETED)
                # Observe every completed failure before scheduling any additional work.
                for future in completed:
                    future.result()
                for _ in completed:
                    yield None
                    item = next(remaining, None)
                    if item is not None:
                        pending.add(pool.submit(operation, item))
        finally:
            for future in pending:
                future.cancel()


def list_items(items, count):
    """Take at most one worker window without materializing the publication graph."""
    for _ in range(count):
        item = next(items, None)
        if item is None:
            return
        yield item


def graph(root: Path, catalog: str) -> list[tuple[str, str]]:
    """Resolve only catalog-referenced immutable snapshots and content objects."""
    safe_path(catalog)
    reject_links(root / catalog)
    document = tomllib.loads((root / catalog).read_text(encoding="utf-8"))
    selected: dict[str, str] = {}
    if document.get("product") == "fantasytale-launcher":
        for row in document.get("files", []):
            path = row["source_path"]
            safe_path(path)
            if path in selected and selected[path] != row["sha256"]:
                raise ValueError("Conflicting launcher content identity")
            selected[path] = row["sha256"]
    for package in document.get("packages", []):
        if package.get("github_asset") is not None:
            continue
        manifest = package["manifest"]
        safe_path(manifest)
        reject_links(root / manifest)
        data = (root / manifest).read_bytes()
        if hashlib.sha256(data).hexdigest() != package["manifest_sha256"]:
            raise ValueError("Catalog manifest digest mismatch")
        manifest_document = tomllib.loads(data.decode("utf-8"))
        for row in manifest_document["files"]:
            path = row["source_path"]
            safe_path(path)
            if path in selected and selected[path] != row["sha256"]:
                raise ValueError("Conflicting content identity")
            selected[path] = row["sha256"]
        selected[manifest] = package["manifest_sha256"]
        if archive := package.get("plugin_archive"):
            if package.get("kind") != "plugin":
                raise ValueError("Only plugin packages may offer import ZIPs")
            path = archive["source_path"]
            safe_path(path)
            digest = archive["sha256"]
            if path in selected and selected[path] != digest:
                raise ValueError("Conflicting plugin ZIP identity")
            reject_links(root / path)
            if (root / path).stat().st_size != archive["size"]:
                raise ValueError("Plugin ZIP size mismatch")
            selected[path] = digest
    if not selected and not (document.get("schema") == 2 and isinstance(document.get("catalog"), dict) and any(document.get(key) for key in ("servers", "profiles", "directory_sources", "github_releases"))):
        raise ValueError("Publication requires a nonempty package graph")
    for path, digest in selected.items():
        source = root / path
        reject_links(source)
        with source.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
                raise ValueError("Publication content digest mismatch")
    return sorted(selected.items(), key=lambda row: (row[0].endswith(".toml"), row[0]))


def feed_aliases(root: Path, catalog: str, objects: list[tuple[str, str]]) -> list[str]:
    """Read generated publication-only aliases without changing the client index schema."""
    path = root / ".fantasytale-feed-aliases.json"
    if not path.exists():
        return []
    reject_links(path)
    record = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(record, dict) or set(record) not in ({"feed_path", "aliases", "sha256"}, {"feed_path", "aliases", "sha256", "manual_download"}):
        raise ValueError("Invalid feed alias metadata")
    if record["feed_path"] != catalog:
        return []
    content = (root / catalog).read_bytes()
    if tomllib.loads(content.decode("utf-8")).get("product") != "fantasytale-launcher":
        raise ValueError("Feed aliases require a launcher index")
    if hashlib.sha256(content).hexdigest() != record["sha256"]:
        raise ValueError("Feed alias metadata refers to another index")
    aliases = record["aliases"]
    if not isinstance(aliases, list) or any(not isinstance(alias, str) for alias in aliases):
        raise ValueError("Invalid bounded feed alias list")
    if len(set(aliases)) != len(aliases):
        raise ValueError("Duplicate feed aliases")
    forbidden = {catalog, *(item[0] for item in objects)}
    for alias in aliases:
        safe_path(alias)
        if alias in forbidden or "objects" in alias.split("/"):
            raise ValueError("Feed alias conflicts with publication content")
        reject_links(root / alias)
        if (root / alias).stat().st_size != len(content) or (root / alias).read_bytes() != content:
            raise ValueError("Feed alias must contain the exact canonical index")
    return aliases


def manual_download(root: Path, catalog: str) -> tuple[str, str] | None:
    """Return the first verified portable volume for callers using the legacy single-file contract."""
    definition = manual_archive_volumes(root, catalog)
    return None if definition is None else definition[1][0][:2]


def signed_metadata(root: Path, catalog: str, objects: list[tuple[str, str]], aliases: list[str], archives=()) -> list[tuple[str, str]]:
    """Bind publisher-generated signature artifacts to the exact local index before connecting."""
    metadata = root / ".fantasytale-signature.json"
    reject_links(metadata)
    if not metadata.exists():
        return []
    record = json.loads(metadata.read_text(encoding="utf-8"))
    fields = {"feed_path", "sha256", "signature_path", "signature_sha256", "public_key_path", "public_key_sha256"}
    if not isinstance(record, dict) or set(record) != fields or record["feed_path"] != catalog:
        raise ValueError("Signature publication metadata requires its canonical launcher index")
    content = (root / catalog).read_bytes()
    if tomllib.loads(content.decode("utf-8")).get("product") != "fantasytale-launcher" or hashlib.sha256(content).hexdigest() != record["sha256"]:
        raise ValueError("Signature publication metadata refers to another launcher index")
    forbidden = {path.casefold() for path in [catalog, *aliases, *(item[0] for item in objects), *(item[0] for item in archives), *(feed + ".archive.toml" for feed in [catalog, *aliases])]}
    result = []
    for field in ("signature", "public_key"):
        path, digest = record[field + "_path"], record[field + "_sha256"]
        if not isinstance(path, str) or not isinstance(digest, str) or len(digest) != 64 or any(char not in "0123456789abcdef" for char in digest):
            raise ValueError("Invalid signature publication identity")
        safe_path(path)
        if "objects" in path.split("/") or any(part.startswith(".fantasytale") for part in path.split("/")) or any(path.casefold() == item or path.casefold().startswith(item + "/") or item.startswith(path.casefold() + "/") for item in forbidden):
            raise ValueError("Signature publication path conflicts with release content")
        forbidden.add(path.casefold())
        source = root / path
        reject_links(source)
        with source.open("rb") as stream:
            if source.stat().st_size == 0 or hashlib.file_digest(stream, "sha256").hexdigest() != digest:
                raise ValueError("Signature publication artifact differs from its declared identity")
        result.append((path, digest))
    return result


def published_filelist(content: bytes) -> dict[str, tuple[str, int]]:
    """Reuse the published launcher index as the immutable object inventory."""
    document = tomllib.loads(content.decode("utf-8"))
    if document.get("product") != "fantasytale-launcher":
        return {}
    rows = document.get("files", [])
    if not isinstance(rows, list):
        raise ValueError("Invalid published file list")
    result = {}
    for row in rows:
        path, digest, size = row["source_path"], row["sha256"], row["size"]
        safe_path(path)
        if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise ValueError("Invalid published file digest")
        if type(size) is not int or size < 0:
            raise ValueError("Invalid published file size")
        # Only content-addressed objects are reusable; mutable names are never cached.
        if "/objects/" not in path or path.rsplit("/", 1)[-1] != digest:
            continue
        identity = digest, size
        if path in result and result[path] != identity:
            raise ValueError("Conflicting published file identity")
        result[path] = identity
    return result


def manual_archive_volumes(root: Path, catalog: str):
    """Verify ordered ZIP, 7z or RAR volumes before advertising archive fallback."""
    metadata = root / ".fantasytale-feed-aliases.json"
    if not metadata.exists():
        return None
    record = json.loads(metadata.read_text(encoding="utf-8"))
    if record["feed_path"] != catalog or "manual_download" not in record:
        return None
    archive = record["manual_download"]
    if not isinstance(archive, dict):
        raise ValueError("Invalid manual download metadata")
    if set(archive) == {"path", "sha256", "size"}:
        format_name, rows = "zip", [archive]
    elif set(archive) == {"format", "volumes"}:
        format_name, rows = archive["format"], archive["volumes"]
    else:
        raise ValueError("Invalid manual download metadata")
    if format_name not in ("zip", "7z", "rar") or not isinstance(rows, list) or not rows:
        raise ValueError("Invalid manual archive volumes")
    result, names, total = [], set(), 0
    for row in rows:
        if not isinstance(row, dict) or set(row) != {"path", "sha256", "size"}:
            raise ValueError("Invalid manual download metadata")
        if not isinstance(row["path"], str) or not isinstance(row["sha256"], str) or len(row["sha256"]) != 64 or any(char not in "0123456789abcdef" for char in row["sha256"]):
            raise ValueError("Invalid manual download identity")
        safe_path(row["path"])
        name = Path(row["path"]).name.casefold()
        if "objects" in row["path"].split("/") or row["path"] == catalog or row["path"] in record["aliases"] or name in names:
            raise ValueError("Manual download path conflicts with update metadata")
        names.add(name)
        if type(row["size"]) is not int or row["size"] <= 0:
            raise ValueError("Manual download size exceeds publication bounds")
        total += row["size"]
        path = root / row["path"]
        reject_links(path)
        with path.open("rb") as stream:
            if path.stat().st_size != row["size"] or hashlib.file_digest(stream, "sha256").hexdigest() != row["sha256"]:
                raise ValueError("Manual download differs from its declared identity")
        result.append((row["path"], row["sha256"], row["size"]))
    return format_name, result


class AList:
    """Keep administrator authentication on API requests and verify anonymous raw downloads separately."""

    def __init__(self, base: str, username: str, password: str, verify_timeout_seconds: int = 900):
        if type(verify_timeout_seconds) is not int or not 30 <= verify_timeout_seconds <= 3600:
            raise ValueError("Verification timeout must be between 30 and 3600 seconds")
        self.verify_timeout_seconds = verify_timeout_seconds
        if urlsplit(base).scheme not in ("http", "https") or urlsplit(base).username:
            raise ValueError("Use an HTTP(S) AList API root without credentials")
        self.base = base.rstrip("/")
        self.public_transport = local()
        self.api = requests.Session()
        result = self.api.post(self.base + "/api/auth/login", json={"username": username, "password": password}, timeout=30).json()
        if result.get("code") != 200:
            raise ValueError("AList administrator authentication failed")
        self.api.headers["Authorization"] = result["data"]["token"]
        self.directories: set[str] = set()
        self.directory_lock = Lock()

    def public_session(self):
        """Reuse bounded anonymous connection pools without sharing mutable sessions between workers."""
        session = getattr(self.public_transport, "session", None)
        if session is None:
            session = requests.Session()
            # One request per worker; retain only two origin pools for provider redirects.
            for scheme in ("http://", "https://"):
                session.mount(scheme, requests.adapters.HTTPAdapter(pool_connections=2, pool_maxsize=1))
            self.public_transport.session = session
        return session

    def metadata(self, path: str, authenticated: bool = False, refresh: bool = False):
        transport = self.api if authenticated else self.public_session()
        response = transport.post(self.base + "/api/fs/get", json={"path": "/" + path, "password": "", "refresh": refresh},
                                 headers=self.api.headers if authenticated else {}, timeout=30)
        response.raise_for_status()
        result = response.json()
        if result.get("code") == 200:
            return result.get("data")
        message = str(result.get("message", "")).strip().lower()
        # Missing ancestors are wrapped by directory lookup; other provider errors must still fail.
        parts = message.split(": ")
        missing = parts[-1] == "object not found" and all(part in ("failed get parent list", "failed get dir") for part in parts[:-1])
        if result.get("code") == 404 or (result.get("code") == 500 and missing):
            return None
        if result.get("code") == 500:
            error = requests.HTTPError("AList provider returned a transient metadata error")
            error.response = response
            error.response.status_code = 500
            raise error
        raise ValueError(f"AList metadata resolution failed (code {result.get('code')})")

    def open_download(self, path: str, metadata: dict, **options):
        """Renew one expired anonymous provider URL without changing the selected object's identity or sending API credentials."""
        def validate_url(value):
            if not isinstance(value, str):
                raise ValueError("AList metadata has an invalid raw download URL")
            parsed = urlsplit(value)
            if parsed.scheme not in ("http", "https") or parsed.username or parsed.password or not parsed.netloc:
                raise ValueError("AList metadata has an invalid raw download URL")
            return value

        raw_url = validate_url(metadata.get("raw_url"))
        response = self.public_session().get(raw_url, **options)
        if response.status_code == 403:
            response.close()
            renewed = self.metadata(path, refresh=True)
            if renewed is not None and renewed.get("size") != metadata.get("size"):
                raise ValueError("Remote publication object changed during URL renewal")
            if renewed is not None:
                renewed_url = validate_url(renewed.get("raw_url"))
                if renewed_url != raw_url:
                    response = self.public_session().get(renewed_url, **options)
        try:
            response.raise_for_status()
            return response
        except BaseException:
            response.close()
            raise

    def remote_hash(self, path: str, *, expected_size: int | None = None) -> str | None:
        """Stream anonymous bytes matching the advertised and optional expected size."""
        if expected_size is not None and (type(expected_size) is not int or expected_size < 0):
            raise ValueError("Invalid publication verification size")
        meta = self.metadata(path)
        if meta is None:
            return None
        size = meta.get("size")
        if type(size) is not int or size < 0:
            raise ValueError("Remote publication object has an invalid size")
        if expected_size is not None and size != expected_size:
            raise ValueError("Remote publication object size differs from the verified local object")
        digest = hashlib.sha256()
        count = 0
        started = time.monotonic()
        with self.open_download(path, meta, stream=True, timeout=(30, 120)) as response:
            response.raise_for_status()
            for chunk in response.iter_content(128 * 1024):
                count += len(chunk)
                if count > size:
                    raise ValueError("Remote publication object exceeds its advertised size")
                # A read timeout alone resets for each chunk and permits an endless trickle.
                if time.monotonic() - started > self.verify_timeout_seconds:
                    raise requests.Timeout("Remote publication verification exceeded its time budget")
                digest.update(chunk)
        if count != size:
            raise ValueError("Public object size mismatch")
        return digest.hexdigest()

    def read_filelist(self, path: str, expected_digest: str) -> bytes:
        """Read one published inventory and bind it to the concurrency baseline."""
        meta = self.metadata(path)
        if meta is None or type(meta.get("size")) is not int or meta["size"] < 0:
            raise ValueError("Published file list is missing or has an invalid size")
        content = bytearray()
        with self.open_download(path, meta, stream=True, timeout=(30, 30)) as response:
            for chunk in response.iter_content(128 * 1024):
                content.extend(chunk)
                if len(content) > meta["size"]:
                    raise ValueError("Published file list exceeds its advertised size")
        if len(content) != meta["size"] or hashlib.sha256(content).hexdigest() != expected_digest:
            raise ValueError("Published file list changed during discovery")
        return bytes(content)

    def change_stamp(self, path: str):
        """Detect changes to a mutable archive without treating provider metadata as download integrity evidence."""
        metadata = self.metadata(path, refresh=True)
        if metadata is None:
            return None
        size = metadata.get("size")
        if type(size) is not int or size < 0:
            raise ValueError("Remote publication object has an invalid size")
        info = metadata.get("hash_info") or {}
        if isinstance(info, str):
            info = json.loads(info)
        md5 = info.get("md5") if isinstance(info, dict) else None
        if isinstance(md5, str) and len(md5) == 32 and all(char in "0123456789abcdefABCDEF" for char in md5):
            return "provider-md5", size, md5.lower()
        return "sha256", self.remote_hash(path)

    def ensure_directories(self, path: str):
        parts = path.split("/")[:-1]
        for index in range(1, len(parts) + 1):
            directory = "/".join(parts[:index])
            if directory in self.directories:
                continue
            meta = self.metadata(directory, authenticated=True)
            if meta is None:
                result = self.api.post(self.base + "/api/fs/mkdir", json={"path": "/" + directory}, timeout=30).json()
                if result.get("code") != 200:
                    raise ValueError("AList publication directory creation failed")
            elif not meta.get("is_dir"):
                raise ValueError("Publication directory conflicts with a file")
            self.directories.add(directory)

    def put(self, path: str, source: Path):
        tasks = self.__dict__.setdefault("upload_tasks", {})
        identity = (str(source.resolve()), source.stat().st_size, source.stat().st_mtime_ns)
        if path in tasks:
            if tasks[path][1] != identity:
                raise ValueError("AList upload task belongs to another local object")
            self.wait_upload_task(path, source.name)
            return
        with self.directory_lock:
            self.ensure_directories(path)
        background = getattr(self, "background_uploads", False) and identity[1] >= 8 * 1024 * 1024
        with source.open("rb") as stream, UploadProgress(stream, source.stat().st_size, source.name):
            response = requests.put(self.base + "/api/fs/put", data=stream,
                                    headers={**self.api.headers, "File-Path": quote("/" + path, safe=""), "As-Task": str(background).lower(), "Content-Type": "application/octet-stream"}, timeout=(30, self.verify_timeout_seconds))
            response.raise_for_status()
            # Some storage drivers complete a successful stream upload with an empty
            # body (or a non-JSON gateway response). The anonymous hash check below
            # is the authoritative confirmation, so only a JSON error code rejects
            # the transfer here.
            result = None
            body = response.text.strip()
            if body:
                try:
                    result = response.json()
                except ValueError:
                    result = None
                if result is not None and (not isinstance(result, dict) or result.get("code") != 200):
                    raise ValueError("AList upload failed")
            if response.status_code < 200 or response.status_code >= 300:
                raise ValueError("AList upload failed")
            task = (result.get("data") or {}).get("task") if isinstance(result, dict) and isinstance(result.get("data"), dict) else None
            if task is not None:
                if not isinstance(task, dict) or not isinstance(task.get("id"), str) or not task["id"]:
                    raise ValueError("AList upload returned an invalid task identity")
                tasks[path] = (task["id"], identity, time.monotonic())
            following = "storage task completion and public integrity verification follow" if path in tasks else "public integrity verification follows"
            print(f"Upload request completed for {source.name}; {following}", flush=True)
        if path in tasks:
            self.wait_upload_task(path, source.name)

    def wait_upload_task(self, path, name):
        """Follow an accepted server task without uploading its bytes again after a polling failure."""
        task_id, _, started = self.upload_tasks[path]
        states = ("pending", "running", "succeeded", "canceling", "canceled", "retrying", "failing", "failed", "waiting-retry", "before-retry")
        while True:
            def status():
                remaining = self.verify_timeout_seconds - (time.monotonic() - started)
                if remaining <= 0:
                    raise requests.Timeout("AList upload task exceeded its time budget")
                response = self.api.post(self.base + "/api/task/upload/info", params={"tid": task_id}, timeout=(min(30, remaining), min(30, remaining)))
                response.raise_for_status()
                return response.json()
            reply = retry_network(status)
            if time.monotonic() - started >= self.verify_timeout_seconds:
                raise requests.Timeout("AList upload task exceeded its time budget")
            task = reply.get("data") if isinstance(reply, dict) and reply.get("code") == 200 else None
            if not isinstance(task, dict) or task.get("id") != task_id or type(task.get("state")) is not int or not 0 <= task["state"] < len(states):
                raise ValueError("AList upload task status is invalid or unavailable")
            state = task["state"]
            # Progress and provider error text cannot establish successful storage commit.
            print(f"AList storage task for {name}: {states[state]}", flush=True)
            if state == 2:
                del self.upload_tasks[path]
                return
            if state in (3, 4, 7):
                raise ValueError("AList storage upload task failed or was canceled")
            time.sleep(5)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--catalog", default="FantasyTale/source.toml")
    parser.add_argument("--workers", type=int, choices=range(1, 5), default=2)
    parser.add_argument("--phase", choices=("all", "incremental", "archive"), default="all",
                        help="Publish verified update objects and feed first, or upload only the portable archive after that feed is live.")
    parser.add_argument("--verify-timeout-seconds", type=int, choices=range(30, 3601), metavar="30..3600", default=900,
                        help="Total streaming verification budget per object; a blocked read is separately limited to 120 seconds.")
    parser.add_argument("--no-proxy", action="store_true", help="Connect directly for this upload process without changing system proxy settings.")
    parser.add_argument("--background-uploads", action="store_true", help="Submit large files as AList background tasks, then verify their completed public bytes.")
    args = parser.parse_args()
    if args.no_proxy:
        os.environ["NO_PROXY"] = "*"
        os.environ["no_proxy"] = "*"
    reject_links(args.input.absolute())
    root = args.input.resolve()
    safe_path(args.catalog)
    catalog = root / args.catalog
    reject_links(catalog)
    with catalog.open("rb") as stream:
        expected = hashlib.file_digest(stream, "sha256").hexdigest()
    objects = graph(root, args.catalog)
    profiles = server_profiles(root, args.catalog, objects)
    aliases = feed_aliases(root, args.catalog, objects + profiles)
    archive_definition = manual_archive_volumes(root, args.catalog)
    archive_format, archive_volumes = archive_definition or ("zip", [])
    signatures = signed_metadata(root, args.catalog, objects + profiles, aliases, archive_volumes)
    portable = archive_volumes[0][:2] if archive_volumes else None
    launcher = tomllib.loads((root / args.catalog).read_text(encoding="utf-8")).get("product") == "fantasytale-launcher"
    if args.phase != "all" and not launcher:
        raise ValueError("Split publication phases apply only to launcher releases")
    with catalog.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
            raise ValueError("Local catalog changed during preflight; publication refused")
    alist = retry_network(lambda: AList(args.base_url, os.environ["FT_ALIST_USERNAME"], os.environ["FT_ALIST_PASSWORD"], args.verify_timeout_seconds))
    alist.background_uploads = args.background_uploads
    initial = retry_network(lambda: alist.remote_hash(args.catalog))
    if args.phase == "archive" and initial != expected:
        raise ValueError("Publish the matching incremental feed before its manual archive")
    # A private existing catalog must never be mistaken for a missing public one.
    if initial is None and alist.metadata(args.catalog, authenticated=True) is not None:
        raise ValueError("Existing catalog is not anonymously readable")
    inventory = {}
    if launcher and initial is not None and args.phase != "archive":
        inventory = published_filelist(retry_network(lambda: alist.read_filelist(args.catalog, initial)))
        print(f"Loaded published file list: {len(inventory)} reusable objects", flush=True)
    alias_initial = {}
    for alias in aliases if args.phase != "archive" else ():
        alias_initial[alias] = retry_network(lambda: alist.remote_hash(alias))
        if alias_initial[alias] is None and alist.metadata(alias, authenticated=True) is not None:
            raise ValueError("Existing legacy feed is not anonymously readable")
    signature_initial = {}
    for path, digest in signatures:
        signature_initial[path] = retry_network(lambda: alist.remote_hash(path))
        if signature_initial[path] is None and alist.metadata(path, authenticated=True) is not None:
            raise ValueError("Existing signature artifact is not anonymously readable")
        if args.phase == "archive" and signature_initial[path] != digest:
            raise ValueError("Publish the matching signed incremental feed before its manual archive")
    profile_initial = {}
    for path, _digest in profiles:
        profile_initial[path] = retry_network(lambda: alist.remote_hash(path))
        if profile_initial[path] is None and alist.metadata(path, authenticated=True) is not None:
            raise ValueError("Existing server profile is not anonymously readable")
    def archive_baselines():
        baselines = {}
        for path, _, _ in archive_volumes:
            baselines[path] = retry_network(lambda: alist.change_stamp(path))
            if baselines[path] is None and alist.metadata(path, authenticated=True) is not None:
                raise ValueError("Existing manual download is not anonymously readable")
        return baselines

    portable_initials = archive_baselines() if portable and not launcher else {}
    def verify_object(item):
        path, digest = item
        size = (root / path).stat().st_size
        if inventory.get(path) == (digest, size):
            return
        existing = alist.remote_hash(path, expected_size=size)
        if existing is not None and existing != digest:
            raise ValueError("Remote immutable object conflict")
        if existing is None:
            alist.put(path, root / path)
            if alist.remote_hash(path, expected_size=size) != digest:
                raise ValueError("Uploaded object failed public integrity verification")
    verified = 0
    # Finish content before publishing even immutable manifests; the mutable catalog is always last.
    for metadata in (False, True) if args.phase != "archive" else ():
        phase = [item for item in objects if item[0].endswith(".toml") == metadata]
        for _ in verify_parallel(phase, lambda item: retry_network(lambda: verify_object(item)), args.workers):
            verified += 1
            print(f"Verified {verified}/{len(objects)}", flush=True)
    def publish_catalog(path, baseline):
        with catalog.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
                raise ValueError("Local catalog changed during upload; publication refused")
        current = alist.remote_hash(path)
        if current == expected:
            return
        if current != baseline:
            raise ValueError("Catalog changed during upload; publication refused")
        alist.put(path, catalog)
        if alist.remote_hash(path) != expected:
            raise ValueError("Published catalog failed public verification")

    def publish_metadata(items, baselines, label):
        for path, digest in items:
            def publish_one():
                if alist.remote_hash(args.catalog) not in (initial, expected):
                    raise ValueError(f"Catalog changed before {label.lower()} publication")
                source = root / path
                reject_links(source)
                with source.open("rb") as stream:
                    if hashlib.file_digest(stream, "sha256").hexdigest() != digest:
                        raise ValueError(f"Local {label.lower()} artifact changed during upload")
                current = alist.remote_hash(path)
                if current == digest:
                    return
                if current != baselines[path]:
                    raise ValueError(f"{label} artifact changed during upload; publication refused")
                alist.put(path, source)
                if alist.remote_hash(path) != digest:
                    raise ValueError(f"{label} artifact failed public integrity verification")
            retry_network(publish_one)

    def publish_feeds():
        publish_metadata(profiles, profile_initial, "Server profile")
        publish_metadata(signatures, signature_initial, "Signature")
        for path, digest in profiles + signatures:
            if retry_network(lambda: alist.remote_hash(path)) != digest:
                raise ValueError("Published metadata changed before catalog publication")
        retry_network(lambda: publish_catalog(args.catalog, initial))
        for alias in aliases:
            retry_network(lambda: publish_catalog(alias, alias_initial[alias]))
        print("Published verified public catalog; incremental update is available", flush=True)

    if launcher and args.phase != "archive":
        publish_feeds()
    if portable and args.phase != "incremental":
        # Snapshot every volume before any archive write so later-volume changes
        # cannot be adopted as a new baseline while an earlier volume uploads.
        companion_initials = {}
        if launcher:
            portable_initials = archive_baselines()
            for feed in [args.catalog, *aliases]:
                if retry_network(lambda: alist.remote_hash(feed)) != expected:
                    raise ValueError("Launcher feed changed before archive publication")
                path = feed + ".archive.toml"
                companion_initials[path] = retry_network(lambda: alist.remote_hash(path))
                if companion_initials[path] is None and alist.metadata(path, authenticated=True) is not None:
                    raise ValueError("Existing archive fallback is not anonymously readable")
        for archive_path, archive_digest, _archive_size in archive_volumes:
            portable = archive_path, archive_digest
            portable_initial = portable_initials[archive_path]
            def publish_portable():
                current = alist.change_stamp(portable[0])
                if current == ("sha256", portable[1]): return
                archive = root / portable[0]
                with archive.open("rb") as stream:
                    candidate = "provider-md5", archive.stat().st_size, hashlib.file_digest(stream, "md5").hexdigest()
                if current == candidate:
                    if alist.remote_hash(portable[0]) == portable[1]: return
                    raise ValueError("Manual download failed public verification")
                if current != portable_initial:
                    # A timed-out upload can already have committed its bytes. Only
                    # complete anonymous SHA-256 verification accepts that retry.
                    if alist.remote_hash(portable[0]) == portable[1]: return
                    raise ValueError("Manual download changed during upload; publication refused")
                alist.put(portable[0], root / portable[0])
                if alist.remote_hash(portable[0]) != portable[1]: raise ValueError("Manual download failed public verification")
            retry_network(publish_portable)
        print("Published verified complete manual-download archive", flush=True)
        if launcher:
            if retry_network(lambda: alist.remote_hash(args.catalog)) != expected:
                raise ValueError("Launcher feed changed before archive fallback publication")
            lines = [f'schema = 1\nmanifest_sha256 = "{expected}"\nformat = "{archive_format}"\n']
            for number, (archive_path, archive_digest, archive_size) in enumerate(archive_volumes, 1):
                lines.append(f'[[files]]\npath = {json.dumps(Path(archive_path).name)}\nvolume = {number}\n'
                             f'source_path = {json.dumps(archive_path)}\nsize = {archive_size}\n'
                             f'sha256 = "{archive_digest}"\n')
            companion = ''.join(lines).encode("utf-8")
            companion_hash = hashlib.sha256(companion).hexdigest()
            for feed in [args.catalog, *aliases]:
                path = feed + ".archive.toml"
                if any(item[0] == path for item in objects):
                    raise ValueError("Archive fallback metadata conflicts with immutable content")
                local = root / path
                reject_links(local)
                local.write_bytes(companion)
                def publish_companion():
                    if alist.remote_hash(feed) != expected:
                        raise ValueError("Launcher feed changed before archive fallback publication")
                    current = alist.remote_hash(path)
                    if current == companion_hash:
                        return
                    if current != companion_initials[path]:
                        raise ValueError("Archive fallback changed during upload; publication refused")
                    alist.put(path, local)
                    if alist.remote_hash(path) != companion_hash:
                        raise ValueError("Archive fallback metadata failed public verification")
                retry_network(publish_companion)
            print("Published verified automatic archive fallback", flush=True)
    if not launcher:
        publish_feeds()
    print("Published verified public catalog", flush=True)


if __name__ == "__main__":
    try:
        main()
    except requests.RequestException as error:
        # Raw download URLs can contain access signatures; never print exception URLs.
        status = error.response.status_code if error.response is not None else "no HTTP response"
        print(f"AList network request failed ({network_failure_summary(error)}, {status}); the catalog was not accepted as published", file=sys.stderr)
        raise SystemExit(1) from None
    except (OSError, ValueError, KeyError) as error:
        print(str(error) if isinstance(error, ValueError) else "Publication input or environment could not be read", file=sys.stderr)
        raise SystemExit(1) from None
