"""Validate and bundle the reviewed Gateway artifacts without executing them."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import tomllib


def load_pin(path: Path) -> dict:
    """Read the repository-owned provenance and file identities."""
    with path.open("rb") as stream:
        pin = tomllib.load(stream)
    if pin.get("schema") != 1 or len(pin.get("source_commit", "")) != 40:
        raise ValueError("Invalid Gateway release pin")
    paths = set()
    for entry in pin["files"]:
        relative = PurePosixPath(entry["path"])
        if relative.is_absolute() or '\\' in entry["path"] or any(part in ("", ".", "..") for part in entry["path"].split("/")) or entry["path"] in paths:
            raise ValueError("Unsafe or duplicate Gateway payload path")
        paths.add(entry["path"])
    return pin


def verify_files(root: Path, pin: dict) -> None:
    """Hash the complete binaries and notices, including each PE architecture."""
    root = root.resolve()
    for entry in pin["files"]:
        path = root / entry["path"]
        if not path.resolve().is_relative_to(root) or path.is_symlink() or not path.is_file():
            raise ValueError("Gateway payload is missing or linked: " + entry["path"])
        with path.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
            if path.stat().st_size != entry["size"] or digest != entry["sha256"]:
                raise ValueError("Gateway payload identity mismatch: " + entry["path"])
            if "machine" in entry:
                stream.seek(0)
                if stream.read(2) != b"MZ":
                    raise ValueError("Gateway payload is not a PE executable")
                stream.seek(0x3C)
                offset = struct.unpack("<I", stream.read(4))[0]
                stream.seek(offset)
                if stream.read(4) != b"PE\0\0" or struct.unpack("<H", stream.read(2))[0] != entry["machine"]:
                    raise ValueError("Gateway executable architecture mismatch")


def verify_artifacts(root: Path, pin: dict) -> None:
    """Require the peer artifact report to name the exact reviewed source and capability."""
    report = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
    notices = json.loads((root / "notices/manifest.json").read_text(encoding="utf-8-sig"))
    if report.get("source_commit") != pin["source_commit"] or report.get("capability") != pin["capability"] or notices.get("source_commit") != pin["source_commit"]:
        raise ValueError("Gateway artifact provenance differs from the reviewed release pin")
    if any(notices.get(key) != pin[key] for key in ("runtime_version", "extensions_version")):
        raise ValueError("Gateway notice provenance differs from the runtime pin")
    verify_files(root, pin)
    if "capabilities_sha256" in pin:
        descriptor = Path(__file__).resolve().parents[1] / "src/FantasyTale.Launcher.Games.BubbleFighter/gateway-capabilities.json"
        compiled = json.loads(descriptor.read_text(encoding="utf-8-sig"))
        for runtime in ("win-x64", "win-arm64"):
            path = root / runtime / "FantasyTale.Gateway.capabilities.json"
            if hashlib.sha256(path.read_bytes()).hexdigest() != pin["capabilities_sha256"]:
                raise ValueError("Gateway capabilities differ between the reviewed architectures")
            if json.loads(path.read_text(encoding="utf-8-sig")) != compiled:
                raise ValueError("Embedded Gateway capabilities differ from the reviewed artifacts")


def bundle(root: Path, package: Path, pin: dict) -> None:
    """Copy only verified release files into a fresh Gateway package directory."""
    verify_artifacts(root, pin)
    destination = package / "gateway"
    if destination.exists():
        raise FileExistsError("Gateway package destination must be new")
    for entry in pin["files"]:
        path = destination / entry["path"]
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / entry["path"], path)
    (destination / "manifest.json").write_text(json.dumps(pin, indent=2) + "\n", encoding="utf-8")
    verify_package(package, pin)


def verify_package(package: Path, pin: dict) -> None:
    """Reject a package with changed Gateway payloads or falsely relabeled provenance."""
    root = package / "gateway"
    if json.loads((root / "manifest.json").read_text(encoding="utf-8")) != pin:
        raise ValueError("Packaged Gateway provenance differs from the release pin")
    verify_files(root, pin)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--package", type=Path)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    pinned = load_pin(Path(__file__).with_name("gateway-release.toml"))
    if args.verify_only:
        if args.package is None:
            parser.error("--verify-only requires --package")
        verify_package(args.package, pinned)
    elif args.artifacts is None:
        parser.error("--artifacts is required before packaging Gateway")
    elif args.package is None:
        verify_artifacts(args.artifacts, pinned)
    else:
        bundle(args.artifacts, args.package, pinned)
    print("PASS Gateway: exact source pin, full hashes, PE architectures and redistribution notices")
