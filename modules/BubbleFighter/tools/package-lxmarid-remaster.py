"""Build the optional LXMarid plugin from locally available client archives."""
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

IDENTITY = "bubblefighter.fantasy.lxmarid-remaster"
PREFIX = "Models/Characters/LXMarid/Default/"
BFZ_TARGET = PREFIX + "Default.bfz"
PKN_TARGET = "Packs/ModelsChrPlLXMarid_00003.pkn"


def digest(path: Path) -> str:
    """Hash an archive or member without retaining its contents in memory."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def members(root: Path) -> dict[str, str]:
    """Exclude only the extractor's external provenance report."""
    return {p.relative_to(root).as_posix(): digest(p) for p in sorted(root.rglob("*"))
            if p.is_file() and p.relative_to(root).as_posix() != "sources.tsv"}


def default_members(tree: dict[str, str]) -> dict[str, str]:
    """Select the exact Default directory, excluding similarly named paths."""
    return {name: value for name, value in tree.items() if name.startswith(PREFIX)}


def timestamp(client: Path) -> int:
    """Read the build fingerprint rather than trusting installation folder names."""
    executables = [p for p in client.iterdir() if p.name.lower() in
                   ("bubblefighter.exe", "bubblefighteru.exe")]
    if not executables:
        raise ValueError("Expected a BubbleFighter executable in the client root")
    executable = next((p for p in executables if p.name.lower() == "bubblefighter.exe"), executables[0])
    with executable.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError("Invalid client executable")
        stream.seek(60)
        pe = struct.unpack("<I", stream.read(4))[0]
        stream.seek(pe)
        if stream.read(4) != b"PE\0\0":
            raise ValueError("Invalid PE header")
        stream.seek(pe + 8)
        return struct.unpack("<I", stream.read(4))[0]


def run(tool: Path, *args: str | Path) -> None:
    """Fail immediately if archive decoding, packing or verification fails."""
    subprocess.run([str(tool), *map(str, args)], check=True)


def extract_family(tool: Path, client: Path, destination: Path) -> None:
    """Read all shards because Default can span multiple packs."""
    packs = sorted((client / "Packs").glob("ModelsChrPlLXMarid_*.pkn"))
    if not packs:
        raise ValueError("The client has no LXMarid archive family")
    for archive in packs:
        run(tool, "extract", archive, destination, "--filter", PREFIX)


def build(args: argparse.Namespace) -> Path:
    """Preserve unrelated target resources and verify every emitted member."""
    for client, expected in ((args.source_client, 0x5CEF8FF9),
                             (args.reference_client, 0x69FD90C8),
                             (args.pkn_client, 0x5767E697)):
        if timestamp(client) != expected:
            raise ValueError("Client fingerprint does not match the resource profile")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    source, reference = output / "source", output / "reference"
    extract_family(args.archive_tool, args.source_client, source)
    extract_family(args.archive_tool, args.reference_client, reference)
    shared = default_members(members(source))
    comparison = default_members(members(reference))
    # kr-1256 adds a 40-byte Head.smf whose purpose is unverified.
    extra = PREFIX + "Head.smf"
    if len(shared) != 46 or set(comparison) - set(shared) != {extra} or any(
            comparison.get(name) != value for name, value in shared.items()):
        raise ValueError("CN and KR remaster members disagree")
    if (reference / extra).stat().st_size != 40:
        raise ValueError("Unexpected KR-only Head.smf")
    package = output / IDENTITY
    payload = package / "payload"
    payload.mkdir(parents=True)
    clean = output / "default-tree"
    for name in shared:
        target = clean / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / name, target)
    bfz = payload / "Default.bfz"
    run(args.archive_tool, "pack", clean, bfz, "--path", BFZ_TARGET)
    run(args.archive_tool, "verify", bfz, "--path", BFZ_TARGET)
    roundtrip = output / "bfz-roundtrip"
    run(args.archive_tool, "extract", bfz, roundtrip, "--path", BFZ_TARGET)
    if members(roundtrip) != shared:
        raise ValueError("BFZ round-trip changed remaster members")

    original = args.pkn_client / PKN_TARGET
    tree = output / "pkn-tree"
    run(args.archive_tool, "extract", original, tree)
    before = members(tree)
    if len(before) != 1029 or len(default_members(before)) != 44:
        raise ValueError("Unexpected cn-735 pack layout")
    family = output / "pkn-default-family"
    extract_family(args.archive_tool, args.pkn_client, family)
    if members(family) != default_members(before):
        raise ValueError("Target Default members occur in another pack")
    for name in default_members(before):
        (tree / name).unlink()
    (tree / "sources.tsv").unlink(missing_ok=True)
    for name in shared:
        target = tree / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / name, target)
    pkn = payload / "ModelsChrPlLXMarid_cn735_00003.pkn"
    run(args.archive_tool, "pack", tree, pkn)
    run(args.archive_tool, "verify", pkn)
    decoded = output / "pkn-roundtrip"
    run(args.archive_tool, "extract", pkn, decoded)
    expected = {name: value for name, value in before.items() if not name.startswith(PREFIX)} | shared
    if members(decoded) != expected:
        raise ValueError("PKN round-trip changed unrelated members or remaster resources")

    definition = Path(__file__).resolve().parents[1] / "games/BubbleFighter/plugins" / IDENTITY
    manifest = (definition / "plugin.toml").read_text(encoding="utf-8")
    for archive in (bfz, pkn):
        pattern = (r'(source = "payload/' + re.escape(archive.name) +
                   r'"\s*\ntarget = "[^"]+"\s*\n)size = \d+\s*\nsha256 = "[a-f0-9]{64}"')
        manifest, count = re.subn(pattern, lambda match:
            match[1] + f'size = {archive.stat().st_size}\nsha256 = "{digest(archive)}"', manifest)
        if count != 1:
            raise ValueError("Expected one manifest row per payload")
    (package / "plugin.toml").write_text(manifest, encoding="utf-8", newline="\n")
    for name in ("file-install.toml", "README.md", "README.zh.md"):
        shutil.copyfile(definition / name, package / name)
    for archive in (bfz, pkn):
        archive.with_name(archive.name + ".sources.tsv").unlink(missing_ok=True)
    report = dict(default_members=len(shared), pkn_members=len(expected),
                  preserved_pkn_members=len(before) - 44, source_crosscheck=True,
                  bfz_roundtrip=True, pkn_roundtrip=True, original_pkn_sha256=digest(original),
                  payloads={p.name: dict(size=p.stat().st_size, sha256=digest(p)) for p in (bfz, pkn)})
    (output / "verification.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    version = tomllib.loads(manifest)["plugin"]["version"]
    with zipfile.ZipFile(output / f"{IDENTITY}-{version}.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(package.rglob("*")):
            if file.is_file():
                entry = zipfile.ZipInfo(file.relative_to(package).as_posix(), (2000, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, file.read_bytes())
    return package


def main() -> None:
    """Accept explicit paths without committing client resources or machine locations."""
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("archive-tool", "source-client", "reference-client", "pkn-client", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    print("Verified package:", build(parser.parse_args()))


if __name__ == "__main__":
    main()
