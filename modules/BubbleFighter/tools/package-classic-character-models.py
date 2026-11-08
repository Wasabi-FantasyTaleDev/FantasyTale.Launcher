"""Build two reversible classic-model plugins while preserving each target pack's other resources."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tomllib
import zipfile

CHARACTERS = {"Dao": "dao", "Dizni": "dizni"}
FINGERPRINTS = {"cn-0-92-1": 0x5BDC5A1F, "cn-774": 0x5CEF8FF9,
                "tw-454": 0x5C0F4941, "kr-1256": 0x69FD90C8}


def digest(path: Path) -> str:
    """Hash a member or payload without keeping the complete archive in memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def members(root: Path) -> dict[str, str]:
    """Ignore only the archive tool's external provenance file."""
    return {path.relative_to(root).as_posix(): digest(path) for path in sorted(root.rglob("*"))
            if path.is_file() and path.relative_to(root).as_posix() != "sources.tsv"}


def fingerprint(root: Path) -> int:
    """Bind inputs to their executable timestamp instead of their directory labels."""
    with (root / "BubbleFighter.exe").open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError("Invalid client executable")
        stream.seek(60)
        offset = struct.unpack("<I", stream.read(4))[0]
        stream.seek(offset)
        if stream.read(4) != b"PE\0\0":
            raise ValueError("Invalid executable PE signature")
        stream.seek(offset + 8)
        return struct.unpack("<I", stream.read(4))[0]


def run(tool: Path, *args: str | Path) -> None:
    """Archive errors stop packaging before any distributable is accepted."""
    subprocess.run([str(tool), *map(str, args)], check=True)


def paths(tool: Path, archive: Path) -> list[str]:
    """Use decoded archive members to identify shards with exact directory scope."""
    result = subprocess.check_output([str(tool), "list", str(archive)], encoding="utf-8")
    names = [match[1] for line in result.splitlines() if (match := re.match(r"\s*\d+\s{2}(.*)", line))]
    if not names or len({name.casefold() for name in names}) != len(names):
        raise ValueError("Archive member inventory is empty or contains duplicate paths")
    return names


def family(root: Path) -> list[Path]:
    """Read both character families because raccoon Default members cross their boundary."""
    return sorted([*(root / "Packs").glob("ModelsChrPlDao_*.pkn"),
                   *(root / "Packs").glob("ModelsChrPlDizni_*.pkn")])


def replacements(before: dict[str, str], classic: dict[str, str], prefix: str,
                 companions: bool = False) -> dict[str, str]:
    """Keep existing shard ownership while restoring matching Dizni facial and motion resources."""
    source_names = {name.casefold(): name for name in classic}
    character_prefix = prefix.removesuffix("Default/").casefold()
    return {name: source_names[name.casefold()] for name, stamp in before.items()
            if (name.casefold().startswith(prefix.casefold()) or companions
                and name.casefold().startswith(character_prefix) and Path(name).suffix.lower() in (".face", ".mtn"))
            and name.casefold() in source_names
            and stamp != classic[source_names[name.casefold()]]}


def face_references(tree: Path, names: dict[str, str], prefix: str) -> set[str]:
    """Resolve facial texture dependencies without treating unrelated character references as local."""
    result: set[str] = set()
    for name in names:
        if not name.casefold().startswith(prefix.casefold()) or not name.lower().endswith(".face"):
            continue
        lines = (tree / name).read_text(encoding="utf-8-sig").splitlines()
        if not lines or lines[0].strip() != "StartTick,ResourcePath":
            raise ValueError("Invalid face selector header: " + name)
        for line in lines[1:]:
            if not line.strip():
                continue
            tick, separator, resource = line.partition(",")
            if not separator or not tick.strip().isdigit() or not resource.strip():
                raise ValueError("Invalid face selector entry: " + name)
            resource = resource.strip().replace("\\", "/").casefold()
            if resource.startswith(prefix.casefold()):
                result.add(resource)
    return result


def source_tree(tool: Path, root: Path, character: str, output: Path) -> dict[str, str]:
    """Read Dizni's matching expressions and animations alongside its classic Default resources."""
    prefix = f"Models/Characters/{character}/Default/"
    scope = f"Models/Characters/{character}/" if character == "Dizni" else prefix
    seen: set[str] = set()
    for archive in family(root):
        selected = [name for name in paths(tool, archive) if name.casefold().startswith(scope.casefold())]
        lowered = {name.casefold() for name in selected}
        if len(lowered) != len(selected) or seen & lowered:
            raise ValueError("Source has duplicate Default member paths")
        if selected:
            run(tool, "extract", archive, output, "--filter", scope)
        seen.update(lowered)
    result = members(output)
    expected = 90 if character == "Dao" else 75
    if sum(name.casefold().startswith(prefix.casefold()) for name in result) != expected or any(
            not name.casefold().startswith(scope.casefold()) for name in result):
        raise ValueError("Unexpected classic Default source member set")
    return result


def write_package(definition: Path, package: Path, rows: list[dict]) -> None:
    """Keep manifest resources pinned to the verified emitted payloads."""
    text = (definition / "plugin.toml").read_text(encoding="utf-8").split("\n[[files]]", 1)[0].rstrip() + "\n"
    for row in rows:
        text += "\n[[files]]\n" + "".join(f"{key} = {json.dumps(row[key])}\n" for key in ("source", "target", "size", "sha256", "builds"))
    (package / "plugin.toml").write_text(text, encoding="utf-8", newline="\n")
    for name in ("file-install.toml", "README.md", "README.zh.md"):
        shutil.copyfile(definition / name, package / name)
    declared = tomllib.loads(text)["compatibility"]["builds"]
    if set(declared) != {build for row in rows for build in row["builds"]}:
        raise ValueError("Package compatibility differs from its verified target rows")
    version = tomllib.loads(text)["plugin"]["version"]
    with zipfile.ZipFile(package.parent / f"{package.name}-{version}.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(package.rglob("*")):
            if path.is_file():
                entry = zipfile.ZipInfo(path.relative_to(package).as_posix(), (2000, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, path.read_bytes())


def build(args: argparse.Namespace) -> None:
    """Restore complete known classic resources without altering newer-only or unrelated members."""
    clients = {}
    for value in args.client:
        identity, path = value.split("=", 1)
        if identity in clients or identity not in FINGERPRINTS:
            raise ValueError("Unknown or duplicate target build")
        clients[identity] = Path(path).resolve()
    if set(clients) != set(FINGERPRINTS):
        raise ValueError("Supply every target client for the initial cross-build verification")
    for identity, root in clients.items():
        if fingerprint(root) != FINGERPRINTS[identity]:
            raise ValueError("Target fingerprint differs from its declared build")
    if fingerprint(args.dao_source) != FINGERPRINTS["tw-454"] or fingerprint(args.dizni_source) != 0x582C1AC7:
        raise ValueError("Classic source fingerprints do not match TW2019 Dao and CN2017 Dizni")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    report = {"sources": {}, "builds": {}, "reuse": [], "omitted_unchanged": [], "face_dependencies": {}}
    repository = Path(__file__).resolve().parents[1]
    for character, slug in CHARACTERS.items():
        if args.character and character != args.character:
            continue
        prefix = f"Models/Characters/{character}/Default/"
        scope = f"Models/Characters/{character}/" if character == "Dizni" else prefix
        source = output / "work" / f"source-{slug}"
        source_root = args.dao_source if character == "Dao" else args.dizni_source
        classic = source_tree(args.archive_tool, source_root, character, source)
        report["sources"][character] = classic
        package = output / f"bubblefighter.fantasy.{slug}-classic-model"
        package.mkdir()
        rows: list[dict] = []
        emitted: list[tuple[str, dict[str, str], dict]] = []
        for identity, root in clients.items():
            if identity == "tw-454" and character == "Dao":
                continue
            total_changes = 0
            seen: set[str] = set()
            original_references: set[str] = set()
            patched_references: set[str] = set()
            for original in family(root):
                selected = [name for name in paths(args.archive_tool, original) if name.casefold().startswith(scope.casefold())]
                if not selected:
                    continue
                lowered = {name.casefold() for name in selected}
                if seen & lowered:
                    raise ValueError("Target has duplicate Default members; restore originals first")
                seen.update(lowered)
                tree = output / "work" / identity / slug / original.stem
                run(args.archive_tool, "extract", original, tree)
                before = members(tree)
                original_references.update(face_references(tree, before, scope))
                expected = dict(before)
                changed = replacements(before, classic, prefix, companions=character == "Dizni")
                for name, source_name in changed.items():
                    shutil.copyfile(source / source_name, tree / name)
                    expected[name] = classic[source_name]
                patched_references.update(face_references(tree, expected, scope))
                if not changed:
                    report["omitted_unchanged"].append(f"{identity}/{original.name}")
                    continue
                total_changes += len(changed)
                (tree / "sources.tsv").unlink(missing_ok=True)
                target = "Packs/" + original.name
                reusable = next((row for uri, content, row in emitted if uri == target and content == expected), None)
                if reusable is not None:
                    reusable["builds"].append(identity)
                    report["reuse"].append(dict(build=identity, source=reusable["source"], target=target))
                    row = reusable
                else:
                    relative = f"payload/{identity}/{original.name}"
                    payload = package / relative
                    payload.parent.mkdir(parents=True, exist_ok=True)
                    run(args.archive_tool, "pack", tree, payload, "--path", target)
                    run(args.archive_tool, "verify", payload, "--path", target)
                    if len(paths(args.archive_tool, payload)) != len(expected):
                        raise ValueError("Repacked archive member count changed")
                    decoded = output / "roundtrip" / identity / slug / original.stem
                    run(args.archive_tool, "extract", payload, decoded, "--path", target)
                    if members(decoded) != expected:
                        raise ValueError("Round trip changed a member or introduced duplicate/missing paths")
                    row = dict(source=relative, target=target, size=payload.stat().st_size,
                               sha256=digest(payload), builds=[identity])
                    rows.append(row)
                    emitted.append((target, expected, row))
                    payload.with_name(payload.name + ".sources.tsv").unlink(missing_ok=True)
                report["builds"].setdefault(identity, {})[target] = dict(original_sha256=digest(original),
                    members=len(before), changed=list(changed), preserved=len(before) - len(changed),
                    source=row["source"], payload_sha256=row["sha256"], roundtrip=True)
            if not total_changes:
                raise ValueError("Supported target has no proven model changes")
            if sum(name.startswith(prefix.casefold()) for name in seen) != 90:
                raise ValueError("Target Default member coverage differs from the researched inventory")
            missing_before = original_references - seen
            missing_after = patched_references - seen
            if missing_after - missing_before:
                raise ValueError("Replacement introduces missing facial textures: " + str(sorted(missing_after - missing_before)))
            report["face_dependencies"][identity + "/" + character] = dict(
                original_missing=sorted(missing_before), patched_missing=sorted(missing_after))
        write_package(repository / "games/BubbleFighter/plugins" / package.name, package, rows)
    (output / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("Verified classic character packages:", output, flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("archive-tool", "dao-source", "dizni-source", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--client", action="append", required=True, metavar="BUILD=ROOT")
    parser.add_argument("--character", choices=tuple(CHARACTERS), help="Build only the selected independent plugin.")
    build(parser.parse_args())
