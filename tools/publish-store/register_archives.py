"""Register already uploaded client archives without uploading or downloading their payloads again."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tomllib

import requests

from publish_store import atomic, reject_links, safe_path
from upload_alist import AList, retry_network


def document(path: Path):
    """Read publisher metadata, retaining the exact bytes used for its digest."""
    reject_links(path)
    with path.open("rb") as stream:
        raw = stream.read()
    return raw, tomllib.loads(raw.decode("utf-8-sig"))


def plan(root: Path, candidate: Path, baseline: Path):
    """Preserve published rows and admit only new client archive packages and advancing profiles."""
    content, after = document(candidate)
    previous, before = document(baseline)
    if after.get("schema") != 2 or before.get("schema") not in (1, 2) or after.get("catalog") != before.get("catalog"):
        raise ValueError("Archive registration requires the same catalog identity")
    old = before.get("packages", [])
    new = after.get("packages", [])
    if not new:
        raise ValueError("Invalid package count")
    for row in old:
        if new.count(row) != 1:
            raise ValueError("Archive registration cannot alter or remove a published package")
    old_ids = {row["id"] for row in old}
    additions = [row for row in new if row not in old]
    if not additions or len({row["id"] for row in additions}) != len(additions):
        raise ValueError("Register unique new client packages")
    for old_profile in before.get("profiles", []):
        current = [row for row in after.get("profiles", []) if (row["game"], row["build"]) == (old_profile["game"], old_profile["build"])]
        if len(current) != 1 or current[0] != old_profile and current[0]["revision"] <= old_profile["revision"]:
            raise ValueError("Published profile revisions cannot regress or change in place")
    manifests = {}
    archives = {}
    for row in additions:
        if row["kind"] != "client" or row["id"] in old_ids or len(row["builds"]) != 1:
            raise ValueError("Only new client archive packages can be registered")
        path = row["manifest"]
        safe_path(path)
        raw, release = document(root / path)
        if hashlib.sha256(raw).hexdigest() != row["manifest_sha256"]:
            raise ValueError("Client archive manifest digest mismatch")
        definition = release["release"]
        if (release.get("schema") != 1 or definition.get("format") != "archive"
                or definition.get("archive_format") not in ("zip", "7z", "rar")
                or definition.get("id") != row["id"] or definition.get("game") != row["game"]
                or definition.get("build") != row["builds"][0] or definition.get("version", "") != row.get("version", "")):
            raise ValueError("New package does not match its archive manifest")
        files = release["files"]
        if not files or [file.get("volume") for file in files] != list(range(1, len(files) + 1)):
            raise ValueError("Archive volumes must be complete and ordered")
        if sum(file["size"] for file in files) != row["size"]:
            raise ValueError("Archive package size mismatch")
        for file in files:
            source = file["source_path"]
            safe_path(source)
            safe_path(file["path"])
            digest = file["sha256"]
            if (type(file["size"]) is not int or file["size"] <= 0
                    or len(digest) != 64 or any(char not in "0123456789abcdef" for char in digest)):
                raise ValueError("Invalid archive size or SHA-256")
            identity = (digest, file["size"])
            if source in archives and archives[source] != identity:
                raise ValueError("Conflicting archive identities")
            archives[source] = identity
        if path in manifests and manifests[path] != raw:
            raise ValueError("Conflicting archive manifests")
        manifests[path] = raw
    if set(manifests) & set(archives):
        raise ValueError("Archive and metadata paths conflict")
    return content, hashlib.sha256(previous).hexdigest(), manifests, archives


def provider_identity(metadata):
    """Compare local bytes with the storage provider's digest without presenting it as a public SHA-256 proof."""
    if not isinstance(metadata, dict) or metadata.get("is_dir") or type(metadata.get("size")) is not int:
        raise ValueError("Uploaded archive is missing or has invalid provider metadata")
    info = metadata.get("hash_info") or {}
    if isinstance(info, str):
        info = json.loads(info)
    md5 = info.get("md5") if isinstance(info, dict) else None
    if not isinstance(md5, str) or len(md5) != 32 or any(char not in "0123456789abcdefABCDEF" for char in md5):
        raise ValueError("Storage provider must supply an archive MD5 for reference registration")
    return metadata["size"], md5.lower()


def local_identity(path: Path):
    """Compute both digests in one read and refuse files changed during that read."""
    reject_links(path)
    before = path.stat()
    sha256, md5 = hashlib.sha256(), hashlib.md5(usedforsecurity=False)
    count = 0
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            count += len(chunk)
            if count > before.st_size:
                raise ValueError("Local archive grew during hashing")
            sha256.update(chunk)
            md5.update(chunk)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns, before.st_ctime_ns, before.st_ino) != (after.st_size, after.st_mtime_ns, after.st_ctime_ns, after.st_ino):
        raise ValueError("Local archive changed during hashing")
    return after.st_size, sha256.hexdigest(), md5.hexdigest()


def publish(alist, root: Path, candidate: Path, baseline: Path, references: Path, catalog: str, check_only: bool = False):
    """Publish new small snapshots then the catalog, preserving its baseline and all remote archive identities."""
    safe_path(catalog)
    content, expected_baseline, manifests, archives = plan(root, candidate, baseline)
    _, config = document(references)
    if set(config) != {"archives"} or not config["archives"]:
        raise ValueError("Configure archive references")
    local = {}
    for row in config["archives"]:
        if set(row) != {"source_path", "local_path"} or row["source_path"] in local:
            raise ValueError("Archive references require unique source_path and local_path pairs")
        local[row["source_path"]] = references.parent / row["local_path"]
    if set(local) != set(archives):
        raise ValueError("Local archive references must exactly cover new manifest payloads")
    if catalog in archives or catalog in manifests:
        raise ValueError("Catalog cannot overlap referenced content")
    current = retry_network(lambda: alist.remote_hash(catalog))
    target = hashlib.sha256(content).hexdigest()
    if current not in (expected_baseline, target):
        raise ValueError("Published catalog changed; prepare a new baseline")
    # Existing package rows are unchanged; their pinned payloads remain the accepted published inventory.
    remote_stamps = {}
    receipts = []
    for path, (sha256, size) in archives.items():
        actual_size, actual_sha256, md5 = local_identity(local[path])
        if (actual_sha256, actual_size) != (sha256, size):
            raise ValueError("Local archive does not match the candidate SHA-256 and size")
        metadata = retry_network(lambda: alist.metadata(path))
        stamp = provider_identity(metadata)
        if stamp != (size, md5):
            raise ValueError("Uploaded archive differs from its local source")
        # One byte proves anonymous raw access without transferring a complete large archive.
        with alist.open_download(path, metadata, stream=True, headers={"Range": "bytes=0-0"}, timeout=(30, 30)) as response:
            response.raise_for_status()
            if not next(response.iter_content(1), b""):
                raise ValueError("Archive is not anonymously downloadable")
        remote_stamps[path] = stamp
        receipts.append({"path": path, "size": size, "sha256": sha256, "remote_evidence": "provider-md5-and-anonymous-access"})
        print(f"Matched uploaded archive {len(receipts)}/{len(archives)}", flush=True)
    if check_only:
        return {"catalog": catalog, "sha256": target, "archives": receipts, "published": False}
    for path, raw in manifests.items():
        digest = hashlib.sha256(raw).hexdigest()
        existing = retry_network(lambda: alist.remote_hash(path))
        if existing is not None and existing != digest:
            raise ValueError("Remote immutable manifest conflict")
        if existing is None:
            retry_network(lambda: alist.put(path, root / path))
        if retry_network(lambda: alist.remote_hash(path)) != digest:
            raise ValueError("Published archive manifest failed public verification")
    for path, stamp in remote_stamps.items():
        if provider_identity(retry_network(lambda: alist.metadata(path, authenticated=True, refresh=True))) != stamp:
            raise ValueError("Uploaded archive changed before catalog publication")
    current = retry_network(lambda: alist.remote_hash(catalog))
    if current != target:
        if current != expected_baseline:
            raise ValueError("Catalog changed during registration; publication refused")
        retry_network(lambda: alist.put(catalog, candidate))
    if retry_network(lambda: alist.remote_hash(catalog)) != target:
        raise ValueError("Published catalog failed public verification")
    return {"catalog": catalog, "sha256": target, "archives": receipts, "published": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--references", required=True, type=Path)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--catalog", default="FantasyTale/source.toml")
    parser.add_argument("--receipt", required=True, type=Path)
    parser.add_argument("--check-only", action="store_true", help="Verify references and anonymous access without publishing metadata")
    parser.add_argument("--validator", required=True, type=Path, help="Built FantasyTale.Launcher.Publish.dll catalog validator")
    args = parser.parse_args()
    reject_links(args.receipt.absolute())
    if args.receipt.resolve() in {path.resolve() for path in (args.candidate, args.baseline, args.references, args.validator)}:
        raise ValueError("Receipt must not replace a publication input")
    args.receipt.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["dotnet", str(args.validator), "--validate-catalog", str(args.candidate)], check=True)
    alist = retry_network(lambda: AList(args.base_url, os.environ["FT_ALIST_USERNAME"], os.environ["FT_ALIST_PASSWORD"]))
    receipt = publish(alist, args.input, args.candidate, args.baseline, args.references, args.catalog, args.check_only)
    atomic(args.receipt, json.dumps(receipt, indent=2).encode("utf-8"))
    print(f"{'Checked' if args.check_only else 'Registered'} {len(receipt['archives'])} uploaded archives; no payload was uploaded", flush=True)


if __name__ == "__main__":
    try:
        main()
    except requests.RequestException as error:
        status = error.response.status_code if error.response is not None else "no HTTP response"
        print(f"Archive registration network failure ({type(error).__name__}, {status})", file=sys.stderr)
        raise SystemExit(1) from None
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(str(error) if isinstance(error, ValueError) else "Archive registration input or environment could not be read", file=sys.stderr)
        raise SystemExit(1) from None
