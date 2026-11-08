"""Package the missing pirate timelines from reviewed client inventories and verified archive reads."""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import subprocess
import tomllib
import zipfile


def safe_path(value: str) -> str:
    """Reject member and archive paths that could leave their declared root."""
    path = PurePosixPath(value)
    if not value or path.is_absolute() or any(part in ("", ".", "..") for part in value.split("/")) or any(c in value for c in "\\:\0"):
        raise ValueError("Unsafe resource path: " + value)
    return value


def index_hash(rows: list[dict]) -> str:
    """Bind the reviewed set to exact member paths, decoded lengths and content digests."""
    text = "".join(f"{row['path']}\t{row['size']}\t{row['sha256']}\n" for row in sorted(rows, key=lambda row: row["path"]))
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def require_index(rows: list[dict], count: int, digest: str, label: str) -> None:
    """Refuse partial or changed evidence before reading any distributable payload."""
    if len(rows) != count or index_hash(rows) != digest:
        raise ValueError("Client member evidence differs from the reviewed " + label)


def fingerprint(root: Path) -> int:
    """Match the client image timestamp rather than trusting its folder's label."""
    image = next((root / name for name in ("BubbleFighterU.exe", "BubbleFighter.exe") if (root / name).is_file()), None)
    if image is None:
        raise ValueError("Client executable is missing: " + str(root))
    with image.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError("Invalid client executable")
        stream.seek(60)
        offset = struct.unpack("<I", stream.read(4))[0]
        stream.seek(offset)
        if stream.read(4) != b"PE\0\0":
            raise ValueError("Invalid PE signature")
        stream.seek(offset + 8)
        return struct.unpack("<I", stream.read(4))[0]


def references(raw: bytes) -> set[str]:
    """Read the native CSV selector without changing its original event order or duplicate ticks."""
    rows = list(csv.reader(io.StringIO(raw.decode("utf-8-sig"))))
    if not rows or rows[0] != ["StartTick", "ResourcePath"]:
        raise ValueError("Invalid facial timeline header")
    result = set()
    for row in rows[1:]:
        if not row:
            continue
        if len(row) != 2 or not row[0].isdigit() or int(row[0]) < 1:
            raise ValueError("Invalid facial timeline event")
        result.add(safe_path(row[1].replace("\\", "/")).lower())
    return result


def build(args: argparse.Namespace) -> None:
    """Create only the missing timeline additions, retaining all target meshes and textures."""
    rules = tomllib.loads(args.rules.read_text(encoding="utf-8"))
    if rules["schema"] != 1:
        raise ValueError("Unsupported pirate evidence schema")
    rows = json.loads(args.inventory.read_text(encoding="utf-8"))
    clients = {row["build"]: row for row in rows}
    if len(clients) != len(rows):
        raise ValueError("Duplicate client inventory")
    expected = {rules["source_build"], *(row["build"] for row in rules["targets"])}
    if set(clients) != expected:
        raise ValueError("Inventory must contain exactly the source and supported Chinese targets")
    manifests = {}
    for identity, client in clients.items():
        files = {row["path"].lower(): row for row in client["files"]}
        if len(files) != len(client["files"]):
            raise ValueError("Duplicate client member: " + identity)
        for row in files.values():
            safe_path(row["path"])
            safe_path(row["archive"])
            if len(row["sha256"]) != 64 or any(c not in "0123456789abcdef" for c in row["sha256"]):
                raise ValueError("Invalid member digest")
        manifests[identity] = files
    source = clients[rules["source_build"]]
    if fingerprint(Path(source["root"])) != rules["source_timestamp"]:
        raise ValueError("Source client fingerprint differs from kr-812")
    timelines = [row for row in source["files"] if row["path"].startswith(rules["source_prefix"])]
    require_index(timelines, rules["timeline_count"], rules["timeline_index_sha256"], "pirate timelines")
    assets = [row for row in source["files"] if row["path"].startswith(rules["asset_prefix"])]
    require_index(assets, rules["asset_count"], rules["asset_index_sha256"], "pirate face assets")
    definition = tomllib.loads((args.definition / "plugin.toml").read_text(encoding="utf-8"))
    if set(definition["compatibility"]["builds"]) != expected - {rules["source_build"]}:
        raise ValueError("Plugin compatibility differs from the reviewed target set")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    verified = output / "verified-inputs"
    request_rows = []
    for identity, client in clients.items():
        for row in client["files"]:
            request_rows.append(dict(root=client["root"], **{key: row[key] for key in ("archive", "path", "size", "sha256")}))
    request_file = output / "verification-requests.json"
    request_file.write_text(json.dumps(request_rows, indent=2), encoding="utf-8")
    subprocess.run([str(args.materializer), str(request_file), str(verified)], check=True)
    dependencies = set()
    for row in timelines:
        if not row["path"].endswith(".face"):
            raise ValueError("Timeline inventory contains a model or texture")
        dependencies.update(references((verified / row["sha256"]).read_bytes()))
    dependency_rows = [manifests[rules["source_build"]][path] for path in dependencies]
    require_index(dependency_rows, rules["dependency_count"], rules["dependency_index_sha256"], "timeline textures")
    package = output / definition["plugin"]["id"]
    (package / "payload").mkdir(parents=True)
    (package / "plans").mkdir()
    for filename in ("plugin.toml", "README.md", "README.zh.md"):
        shutil.copyfile(args.definition / filename, package / filename)
    for row in timelines:
        destination = package / "payload" / row["sha256"]
        if not destination.exists():
            shutil.copyfile(verified / row["sha256"], destination)
    index = "schema = 1\n"
    plans = {}
    report = []
    for target in rules["targets"]:
        identity = target["build"]
        client = clients[identity]
        root = Path(client["root"])
        if fingerprint(root) != target["timestamp"]:
            raise ValueError("Target client fingerprint differs: " + identity)
        target_assets = [row for row in client["files"] if row["path"].startswith(rules["asset_prefix"])]
        require_index(target_assets, rules["asset_count"], rules["asset_index_sha256"], identity + " pirate face assets")
        target_dependencies = [manifests[identity][path] for path in dependencies]
        require_index(target_dependencies, rules["dependency_count"], rules["dependency_index_sha256"], identity + " timeline textures")
        if any(row["path"].lower().startswith(rules["source_prefix"].lower()) for row in client["files"]):
            raise ValueError("Target inventory already contains pirate timelines: " + identity)
        archive = safe_path(target["archive"])
        if (root / archive).exists() == target["create"]:
            raise ValueError("Target archive does not match its creation declaration: " + identity)
        key = archive, target["create"]
        if key not in plans:
            reference = f"plans/{len(plans) + 1}.toml"
            plans[key] = reference
            text = f'schema = 1\n\n[[archives]]\npath = {json.dumps(archive)}\ncreate = {str(target["create"]).lower()}\n'
            for row in sorted(timelines, key=lambda row: row["path"]):
                text += "\n[[archives.members]]\n"
                for field, value in dict(path=row["path"], source="payload/" + row["sha256"], sha256=row["sha256"], original_sha256="").items():
                    text += f"{field} = {json.dumps(value)}\n"
            (package / reference).write_text(text, encoding="utf-8", newline="\n")
        index += f'\n[[plans]]\nbuild = {json.dumps(identity)}\npath = {json.dumps(plans[key])}\n'
        report.append(dict(build=identity, root=str(root), archive=archive, create=target["create"], timelines=len(timelines),
            unchanged_face_assets=len(target_assets), verified_texture_dependencies=len(target_dependencies)))
    (package / "expressions.toml").write_text(index, encoding="utf-8", newline="\n")
    zip_path = output / f'{package.name}-{definition["plugin"]["version"]}.zip'
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(package.rglob("*")):
            if path.is_file():
                entry = zipfile.ZipInfo(path.relative_to(package).as_posix(), (2000, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, path.read_bytes())
    (output / "package-report.json").write_text(json.dumps(dict(targets=report, timeline_index_sha256=index_hash(timelines),
        zip=zip_path.name, zip_sha256=hashlib.sha256(zip_path.read_bytes()).hexdigest()), indent=2), encoding="utf-8")
    print(f"PASS {len(report)} Chinese targets; {len(timelines)} timeline additions; {len(dependencies)} unchanged texture dependencies")
    print(zip_path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--materializer", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--rules", type=Path, default=Path(__file__).with_name("pirate-expression-fix.toml"))
    parser.add_argument("--definition", type=Path, default=Path(__file__).resolve().parents[1] / "games/BubbleFighter/plugins/bubblefighter.fantasy.pirate-expression-fix")
    build(parser.parse_args())
