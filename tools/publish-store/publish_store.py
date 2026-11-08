"""Publish folder changes for HTTP or AList without maintaining release file lists."""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time
import tomllib



def quote(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def atomic(path: Path, content: bytes) -> bool:
    if path.exists() and path.stat().st_size == len(content) and path.read_bytes() == content:
        return False
    temporary = path.with_suffix(path.suffix + ".tmp")
    reject_links(path)
    reject_links(temporary)
    with temporary.open("wb") as output:
        output.write(content)
        output.flush()
        os.fsync(output.fileno())
    os.replace(temporary, path)
    return True


def reject_links(path: Path) -> None:
    for current in (path, *path.parents):
        if current.is_symlink() or (hasattr(current, "is_junction") and current.is_junction()):
            raise ValueError(f"Links are not allowed: {current}")


def stamp(path: Path) -> list[int]:
    info = path.stat()
    return [info.st_size, info.st_mtime_ns, info.st_ctime_ns, info.st_ino]


def safe_path(path: str, *, literal: bool = False) -> None:
    forbidden = ':\\?<>"|*' if literal else ':\\%?#<>"|*'
    if len(path) > 240 or any(ord(c) < 32 for c in path) or any(c in path for c in forbidden):
        raise ValueError(f"Unsupported client file path: {path}")
    for part in path.split("/"):
        base = part.split(".")[0].upper()
        if not part or part in (".", "..") or part.endswith((".", " ")) or base in ("CON", "PRN", "AUX", "NUL") or re.fullmatch(r"(?:COM|LPT)[1-9¹²³]", base):
            raise ValueError(f"Unsupported client file path: {path}")


def identifier(value: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"[a-z0-9][a-z0-9._-]{0,127}", value):
        raise ValueError("Invalid store, game, build or package identifier")
    return value


def server_version(value: str) -> str:
    if not isinstance(value, str) or len(value) > 64:
        raise ValueError("Server package version must be SemVer")
    match = re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z.-]+))?(?:\+([0-9A-Za-z.-]+))?", value)
    if not match or any(int(part) > 4294967295 for part in match.groups()[:3]):
        raise ValueError("Server package version must be SemVer")
    for index, identifiers in enumerate(match.groups()[3:]):
        if identifiers is not None and any(not part or index == 0 and part.isdigit() and len(part) > 1 and part.startswith("0") for part in identifiers.split(".")):
            raise ValueError("Server package version must be SemVer")
    return value


def file_tree(directory: Path, exclusions: list[str]) -> list[tuple[str, Path, list[int]]]:
    result: list[tuple[str, Path, list[int]]] = []
    if not directory.is_dir():
        raise ValueError(f"Client directory does not exist: {directory}")
    reject_links(directory)
    for root, dirs, files in os.walk(directory, followlinks=False):
        for name in sorted([*dirs, *files]):
            path = Path(root) / name
            relative = path.relative_to(directory).as_posix()
            excluded = (name.startswith((".fantasytale", "fthook-status-")) or name in (".git", "session.toml", "instance.toml") or
                        any(fnmatch.fnmatchcase(relative, pattern) for pattern in exclusions))
            if excluded:
                if name in dirs:
                    dirs.remove(name)
                continue
            reject_links(path)
            safe_path(relative, literal=True)
            if path.is_dir():
                continue
            if not path.is_file():
                raise ValueError(f"Not a regular file: {path}")
            metadata = stamp(path)
            result.append((relative, path, metadata))
    names = [item[0].casefold() for item in result]
    if not result or len(names) != len(set(names)):
        raise ValueError("Client folder is empty or has conflicting Windows file names")
    return sorted(result)


def archive_tree(first: Path) -> tuple[str, list[tuple[str, Path, list[int]]]]:
    """Discover a complete neighboring volume family from one archive path."""
    reject_links(first)
    if not first.is_file():
        raise ValueError("Archive input does not exist")
    numeric = re.fullmatch(r"(.+\.(7z|zip))\.(\d+)", first.name, re.IGNORECASE)
    rar_parts = re.fullmatch(r"(.+)\.part(\d+)\.rar", first.name, re.IGNORECASE)
    rar_old = re.fullmatch(r"(.+)\.r(\d+)", first.name, re.IGNORECASE)
    zip_parts = re.fullmatch(r"(.+)\.z(\d+)", first.name, re.IGNORECASE)
    numbered, final = [], None
    if numeric:
        kind, base, start = numeric[2].lower(), numeric[1], 1
        pattern = re.compile(re.escape(base) + r"\.(\d+)", re.IGNORECASE)
    elif rar_parts:
        kind, base, start = "rar", rar_parts[1], 1
        pattern = re.compile(re.escape(base) + r"\.part(\d+)\.rar", re.IGNORECASE)
    elif zip_parts or first.suffix.lower() == ".zip" and any(first.parent.glob(first.stem + ".z[0-9]*")):
        kind, base, start = "zip", zip_parts[1] if zip_parts else first.stem, 1
        pattern = re.compile(re.escape(base) + r"\.z(\d+)", re.IGNORECASE)
        final = first.parent / (base + ".zip")
    elif first.suffix.lower() == ".rar" and any(first.parent.glob(first.stem + ".r[0-9]*")):
        kind, base, start = "rar", first.stem, 0
        pattern = re.compile(re.escape(base) + r"\.r(\d+)", re.IGNORECASE)
        numbered.append((-1, first))
    elif rar_old:
        kind, base, start = "rar", rar_old[1], 0
        pattern = re.compile(re.escape(base) + r"\.r(\d+)", re.IGNORECASE)
        numbered.append((-1, first.parent / (base + ".rar")))
    else:
        kind = first.suffix.lower().lstrip(".")
        if kind not in ("zip", "7z", "rar"):
            raise ValueError("Select a ZIP, 7z or RAR archive")
        safe_path(first.name)
        return kind, [(first.name, first, stamp(first))]
    matches = []
    for path in first.parent.iterdir():
        match = pattern.fullmatch(path.name)
        if match:
            matches.append((int(match[1]), path))
    matches.sort(key=lambda item: item[0])
    if not matches or [n for n, _ in matches] != list(range(start, start + len(matches))):
        raise ValueError("Archive volumes must be consecutive with no duplicate numbers")
    numbered.extend(matches)
    if final:
        numbered.append((len(numbered) + 1, final))
    tree = []
    for _, path in numbered:
        reject_links(path)
        safe_path(path.name)
        if not path.is_file():
            raise ValueError("Missing archive volume")
        tree.append((path.name, path, stamp(path)))
    if len({row[0].casefold() for row in tree}) != len(tree):
        raise ValueError("Archive aliases another volume")
    return kind, tree


def publish(config_path: Path, full_check: bool = False) -> dict[str, int]:
    config = tomllib.loads(config_path.read_text(encoding="utf-8-sig"))
    if "store" not in config or set(config) - {"store", "clients", "servers"}:
        raise ValueError("Publisher configuration requires store and client or server packages")
    store = config["store"]
    clients, servers = config.get("clients", []), config.get("servers", [])
    if not isinstance(clients, list) or not isinstance(servers, list):
        raise ValueError("Client and server packages must be arrays of tables")
    if set(store) - {"id", "name", "output", "storage_gib"} or not clients and not servers:
        raise ValueError("Invalid publisher settings")
    store_id = identifier(store["id"])
    root = Path(store["output"])
    if not root.is_absolute():
        root = config_path.parent / root
    reject_links(root)
    root = root.resolve()
    root.mkdir(parents=True, exist_ok=True)
    lock_path = root / ".publisher.lock"
    reject_links(lock_path)
    try:
        lock = lock_path.open("x")
    except FileExistsError as error:
        raise ValueError("Another publication is active. Remove .publisher.lock only after verifying that no publisher is running.") from error
    try:
        with lock:
            lock.write(str(os.getpid()))
        metadata = root / "FantasyTale"
        objects = metadata / "files"
        snapshots = metadata / "snapshots"
        for directory in (metadata, objects, snapshots):
            reject_links(directory)
            directory.mkdir(exist_ok=True)
        state_path = root / ".publisher-cache.json"
        reject_links(state_path)
        cache = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {}
        next_cache = {}
        object_files = list(objects.iterdir())
        snapshot_files = list(snapshots.iterdir())
        for path in [*object_files, *snapshot_files]:
            reject_links(path)
        stats = {"hashed_files": 0, "cached_files": 0, "changed_catalog": 0}
        catalog = f"schema = 1\n[catalog]\nid = {quote(store_id)}\nname = {{ en = {quote(store['name'])} }}\n"
        seen = set()
        for kind, client in [("client", entry) for entry in clients] + [("server", entry) for entry in servers]:
            allowed = {"id", "game", "directory", "name", "exclude"} | ({"build", "label", "archive", "archive_root", "password"} if kind == "client" else {"builds", "version"})
            if set(client) - allowed:
                raise ValueError("Unknown package publisher option")
            game = identifier(client["game"])
            if kind == "server":
                builds = client["builds"]
                if not isinstance(builds, list) or not builds:
                    raise ValueError("Server packages require unique client builds")
                builds = [identifier(build) for build in builds]
                if len(set(builds)) != len(builds):
                    raise ValueError("Duplicate server client build")
                package = identifier(client["id"])
            else:
                builds = [identifier(client["build"])]
                package = identifier(client.get("id", game + "." + builds[0]))
            if package in seen:
                raise ValueError("Duplicate package identity")
            seen.add(package)
            is_archive = "archive" in client
            if is_archive and ("directory" in client or "exclude" in client):
                raise ValueError("Select either a directory or an archive")
            if not is_archive and ("password" in client or "archive_root" in client):
                raise ValueError("Password and archive_root require an archive")
            archive_path = Path(client["archive"]) if is_archive else None
            if archive_path is not None and not archive_path.is_absolute():
                archive_path = config_path.parent / archive_path
            directory = archive_path.parent if archive_path is not None else Path(client["directory"])
            if not directory.is_absolute():
                directory = config_path.parent / directory
            reject_links(directory)
            directory = directory.resolve()
            if directory == root or directory in root.parents or root in directory.parents:
                raise ValueError("Client input and published output must be separate folders")
            exclusions = client.get("exclude", [])
            if not isinstance(exclusions, list) or len(exclusions) > 64 or any(not isinstance(p, str) or len(p) > 240 for p in exclusions):
                raise ValueError("Invalid client exclude patterns")
            if kind == "server":
                # Deployment overrides and player data never belong to a distributable server package.
                exclusions = exclusions + ["local.patch.yaml", "*/local.patch.yaml", ".env", ".env.*", "*/.env", "*/.env.*",
                                            "*.sqlite", "*.sqlite-*", "*.db", "*.db-*", "*.log", "logs", "logs/*", "*/logs", "*/logs/*",
                                            "*.pfx", "*.p12", "*.pem", "*.key"]
            archive_format, tree = archive_tree(archive_path) if archive_path is not None else (None, file_tree(directory, exclusions))
            build_text = f"build = {quote(builds[0])}" if kind == "client" else "builds = [" + ", ".join(quote(b) for b in builds) + "]"
            document = f"schema = 1\n[release]\nid = {quote(package)}\ngame = {quote(game)}\n{build_text}\n"
            if is_archive:
                document += f'format = "archive"\narchive_format = {quote(archive_format)}\n'
                if "archive_root" in client:
                    safe_path(client["archive_root"], literal=True)
                    document += f'archive_root = {quote(client["archive_root"])}\n'
                if "password" in client:
                    password = client["password"]
                    if not isinstance(password, str) or len(password) > 256 or any(ord(c) < 32 for c in password):
                        raise ValueError("Invalid archive password")
                    document += f'password = {quote(password)}\n'
            label = client.get("label", "") if kind == "client" else server_version(client["version"])
            if label:
                if len(label) > 64 or any(ord(c) < 32 for c in label):
                    raise ValueError("Invalid optional update label")
                document += f"version = {quote(label)}\n"
            total = 0
            for volume, (relative, path, before) in enumerate(tree, 1):
                key = package + "/" + relative
                old = cache.get(key)
                target = objects / old["sha256"] if isinstance(old, dict) and re.fullmatch(r"[a-f0-9]{64}", old.get("sha256", "")) else None
                if target:
                    reject_links(target)
                if not full_check and old and old.get("input") == str(directory) and old.get("stamp") == before and target and target.is_file() and old.get("object_stamp") == stamp(target):
                    digest = old["sha256"]
                    stats["cached_files"] += 1
                else:
                    temporary = objects / ".publishing.tmp"
                    reject_links(temporary)
                    try:
                        digest_state = hashlib.sha256()
                        written = 0
                        with path.open("rb") as source, temporary.open("wb") as output:
                            while block := source.read(1024 * 1024):
                                written += len(block)
                                if written > before[0]:
                                    raise ValueError(f"File is being changed: {relative}")
                                digest_state.update(block)
                                output.write(block)
                            output.flush()
                            os.fsync(output.fileno())
                        if stamp(path) != before or written != before[0]:
                            raise ValueError(f"File is being changed: {relative}")
                        digest = digest_state.hexdigest()
                        target = objects / digest
                        reject_links(target)
                        if not target.exists():
                            os.replace(temporary, target)
                        else:
                            # Shared content objects retain their timestamps so duplicate inputs do not invalidate each other's cache.
                            with target.open("rb") as existing:
                                intact = target.stat().st_size == written and hashlib.file_digest(existing, "sha256").hexdigest() == digest
                            if not intact:
                                os.replace(temporary, target)
                    finally:
                        temporary.unlink(missing_ok=True)
                    stats["hashed_files"] += 1
                next_cache[key] = {"input": str(directory), "stamp": before, "sha256": digest, "object_stamp": stamp(target)}
                total += before[0]
                document += f"\n[[files]]\npath = {quote(relative)}\nsource_path = {quote('FantasyTale/files/' + digest)}\nsize = {before[0]}\nsha256 = {quote(digest)}\n"
                if is_archive:
                    document += f"volume = {volume}\n"
            # Files replaced during the scan must not make a mixed snapshot visible.
            if tree != (archive_tree(archive_path)[1] if archive_path is not None else file_tree(directory, exclusions)):
                raise ValueError("Client folder changed during publication; retry after copying finishes")
            raw = document.encode("utf-8")
            snapshot_hash = hashlib.sha256(raw).hexdigest()
            atomic(snapshots / (snapshot_hash + ".toml"), raw)
            update_mode = "files" if kind == "client" else "release"
            catalog += f"\n[[packages]]\nid = {quote(package)}\nkind = {quote(kind)}\nupdate_mode = {quote(update_mode)}\ngame = {quote(game)}\nbuilds = [" + ", ".join(quote(b) for b in builds) + "]\n"
            if label:
                catalog += f"version = {quote(label)}\n"
            catalog += f"name = {{ en = {quote(client['name'])} }}\npublisher = {quote(store['name'])}\nsize = {total}\nmanifest = {quote('FantasyTale/snapshots/' + snapshot_hash + '.toml')}\nmanifest_sha256 = {quote(snapshot_hash)}\n"
        raw_catalog = catalog.encode("utf-8")
        raw_cache = json.dumps(next_cache, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        atomic(state_path, raw_cache)
        stats["changed_catalog"] = int(atomic(metadata / "source.toml", raw_catalog))
        return stats
    finally:
        lock_path.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--watch", type=int, default=0, metavar="SECONDS", help="Watch folders automatically; minimum 10 seconds")
    parser.add_argument("--full-check", action="store_true", help="Rehash all source files instead of trusting cached metadata")
    args = parser.parse_args()
    if args.watch and args.watch < 10:
        parser.error("--watch requires at least 10 seconds")
    while True:
        try:
            result = publish(args.config.absolute(), args.full_check)
            print(json.dumps(result), flush=True)
        except (OSError, ValueError, KeyError, TypeError) as error:
            print(str(error), file=sys.stderr, flush=True)
            if not args.watch:
                raise SystemExit(1) from error
        if not args.watch:
            return
        time.sleep(args.watch)


if __name__ == "__main__":
    main()
