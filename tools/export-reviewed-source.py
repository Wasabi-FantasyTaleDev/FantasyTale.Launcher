"""Export only explicitly reviewed Git blobs into a new directory, without history."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


EMPTY_PREVIEW = b"{}\n"


def safe_path(value: str) -> list[str]:
    """Accept portable relative filenames, never Git metadata or local review folders."""
    if not isinstance(value, str) or not value or re.search(r'[\\:\x00-\x1f<>"|?*]', value):
        raise ValueError("Invalid export path")
    parts = value.split("/")
    for part in parts:
        if part in ("", ".", "..") or part.endswith((".", " ")):
            raise ValueError("Invalid export path")
        if part.casefold() in {".git", ".hg", ".svn", ".claude", ".codex", "artifacts", "private", "reference", "node_modules"}:
            raise ValueError("Private or repository metadata is not exportable")
        if re.fullmatch(r"(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", part):
            raise ValueError("Reserved export filename")
    return parts


def git(repository: Path, *arguments: str) -> bytes:
    """Read repository objects without invoking a shell or consulting working files."""
    return subprocess.check_output(["git", "-C", str(repository), *arguments])


def validate_manifest(repository: Path, manifest: dict) -> list[tuple[str, str, str, str, str]]:
    """Bind every allowed path and content hash to one immutable commit."""
    if manifest.get("schema") != 1 or not isinstance(manifest.get("files"), list) or not manifest["files"]:
        raise ValueError("Expected a nonempty schema-one reviewed file list")
    revision = manifest.get("revision", "")
    if not re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", revision):
        raise ValueError("Use a full immutable commit id")
    if git(repository, "rev-parse", "--verify", revision + "^{commit}").decode().strip() != revision:
        raise ValueError("The revision must identify a commit directly")
    tree = {}
    for row in git(repository, "ls-tree", "-r", "-z", revision).split(b"\0"):
        if row:
            metadata, path = row.split(b"\t", 1)
            tree[path.decode("utf-8")] = metadata.decode("ascii").split()
    normalized, files, accepted = {}, set(), []
    for entry in manifest["files"]:
        if not isinstance(entry, dict) or set(entry) - {"path", "sha256", "action"}:
            raise ValueError("Unsupported export file fields")
        path, digest, action = entry.get("path"), entry.get("sha256", ""), entry.get("action", "copy")
        parts = safe_path(path)
        if path in files:
            raise ValueError("Duplicate export file")
        files.add(path)
        for index in range(1, len(parts) + 1):
            prefix = "/".join(parts[:index])
            existing = normalized.setdefault(prefix.casefold(), prefix)
            if existing != prefix:
                raise ValueError("Case-alias export paths")
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("Every output file needs a SHA-256 digest")
        if path not in tree or tree[path][0] not in ("100644", "100755") or tree[path][1] != "blob":
            raise ValueError("Export entries must be regular files from the selected commit")
        if action not in ("copy", "empty-preview"):
            raise ValueError("Unsupported export action")
        if action == "empty-preview" and (path != "web/src/preview-levels.json" or digest != hashlib.sha256(EMPTY_PREVIEW).hexdigest()):
            raise ValueError("The only replacement is an explicitly selected empty preview catalog")
        if path == "web/src/preview-levels.json" and action != "empty-preview":
            raise ValueError("Exported preview catalogs must not contain private game data")
        accepted.append((path, digest, action, tree[path][2], tree[path][0]))
    if any("/".join(path.split("/")[:index]) in files for path in files for index in range(1, len(path.split("/")))):
        raise ValueError("Export file and directory collide")
    return accepted


def export(repository: Path, manifest: dict, output: Path) -> int:
    """Write an exact allowlist into a new non-Git directory, leaving failures for inspection."""
    accepted = validate_manifest(repository, manifest)
    if output.exists() or output.is_symlink():
        raise ValueError("Export output must be a new directory")
    output.mkdir(parents=True, exist_ok=False)
    for path, digest, action, blob, mode in accepted:
        destination = output.joinpath(*path.split("/"))
        destination.parent.mkdir(parents=True, exist_ok=True)
        hasher = hashlib.sha256()
        with destination.open("xb") as target:
            if action == "empty-preview":
                target.write(EMPTY_PREVIEW)
                hasher.update(EMPTY_PREVIEW)
            else:
                with subprocess.Popen(["git", "-C", str(repository), "cat-file", "blob", blob], stdout=subprocess.PIPE) as process:
                    while chunk := process.stdout.read(1024 * 1024):
                        hasher.update(chunk)
                        target.write(chunk)
                    if process.wait() != 0:
                        raise ValueError("Failed to read a reviewed Git blob")
        if hasher.hexdigest() != digest:
            raise ValueError(f"Reviewed output hash mismatch: {path}; do not publish the partial export")
        if os.name != "nt":
            destination.chmod(0o755 if mode == "100755" else 0o644)
    return len(accepted)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=Path.cwd())
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    count = export(args.repository.resolve(), json.loads(args.manifest.read_text(encoding="utf-8")), args.output.absolute())
    print(f"Exported {count} reviewed files; no Git history, ignored files or nested repositories were copied.")
