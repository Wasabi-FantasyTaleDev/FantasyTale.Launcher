"""Stage an assets package: copy its manifest folder and fill the declared data folder from a generated pack.

The generated game art never lives in the repository. Run this once per release, then publish the staged
folder with publish_plugins.py --package <output>.
"""
from __future__ import annotations
import argparse
import json
import re
import shutil
import tomllib
from pathlib import Path

ATLAS_FILE = re.compile(r"^[0-9a-f]{16,}\.webp$")
BUILD_FILE = re.compile(r"^[a-z0-9][a-z0-9.-]{0,95}\.json$")
ALLOWED_TOP_LEVEL = {"pack.json", "atlas", "builds"}


def _reject_links(path: Path) -> None:
    """Refuse symbolic links and junctions anywhere in the staged inputs."""
    if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
        raise ValueError(f"Linked paths are not supported: {path}")


def _references(value, found: set[str]) -> None:
    """Collect every string that names a pack file: atlas/..., builds/... or any *.webp name."""
    if isinstance(value, str):
        if value.startswith(("atlas/", "builds/")) or value.endswith(".webp"):
            found.add(value)
    elif isinstance(value, list):
        for item in value:
            _references(item, found)
    elif isinstance(value, dict):
        for item in value.values():
            _references(item, found)


def validate_pack(pack: Path) -> list[str]:
    """Check the pack layout and that every file named inside its JSON exists; returns warnings."""
    if not pack.is_dir():
        raise ValueError(f"Pack directory does not exist: {pack}")
    _reject_links(pack)
    for entry in pack.rglob("*"):
        _reject_links(entry)
    unexpected = sorted(entry.name for entry in pack.iterdir() if entry.name not in ALLOWED_TOP_LEVEL)
    if unexpected:
        raise ValueError("Unexpected entries in the pack: " + ", ".join(unexpected))
    index = pack / "pack.json"
    if not index.is_file():
        raise ValueError("The pack has no pack.json")
    atlas, builds = pack / "atlas", pack / "builds"
    for folder, pattern in ((atlas, ATLAS_FILE), (builds, BUILD_FILE)):
        if folder.exists():
            if not folder.is_dir():
                raise ValueError(f"{folder.name} must be a folder")
            for entry in folder.iterdir():
                if not entry.is_file() or not pattern.match(entry.name):
                    raise ValueError(f"Unexpected file name in {folder.name}/: {entry.name}")
    documents = [index, *sorted(builds.glob("*.json"))] if builds.exists() else [index]
    referenced: set[str] = set()
    for document in documents:
        try:
            _references(json.loads(document.read_text(encoding="utf-8")), referenced)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"{document.relative_to(pack).as_posix()} is not valid JSON: {error}") from None
    used_atlas: set[str] = set()
    for reference in sorted(referenced):
        relative = reference if "/" in reference else "atlas/" + reference
        target = pack / relative
        if ".." in relative.split("/") or not target.is_file():
            raise ValueError(f"pack.json references a missing file: {reference}")
        if relative.startswith("atlas/"):
            used_atlas.add(relative[len("atlas/"):])
    warnings = []
    if atlas.exists():
        warnings = [f"atlas/{entry.name} is not referenced by any JSON file" for entry in sorted(atlas.iterdir()) if entry.name not in used_atlas]
    return warnings


UI_TOP_LEVEL = {"ui.json", "fonts", "sounds", "v1", "v2", "v3"}


def validate_ui_resources(pack: Path) -> list[str]:
    """Check a game UI resource set from tools/game-ui/pack-resources.py: theme manifests, ui.json and the font map."""
    if not pack.is_dir():
        raise ValueError(f"Resource directory does not exist: {pack}")
    _reject_links(pack)
    for entry in pack.rglob("*"):
        _reject_links(entry)
    unexpected = sorted(entry.name for entry in pack.iterdir() if entry.name not in UI_TOP_LEVEL)
    if unexpected:
        raise ValueError("Unexpected entries in the resource set: " + ", ".join(unexpected))
    themes = [name for name in ("v1", "v2", "v3") if (pack / name).is_dir()]
    if not themes:
        raise ValueError("The resource set has no theme folder")
    for theme in themes:
        try:
            json.loads((pack / theme / "manifest.json").read_text(encoding="utf-8"))
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"{theme}/manifest.json is missing or invalid: {error}") from None
    fonts = pack / "fonts" / "fonts.json"
    if fonts.is_file():
        for original, stored in json.loads(fonts.read_text(encoding="utf-8")).items():
            if "/" in stored or "\\" in stored or not (pack / "fonts" / stored).is_file():
                raise ValueError(f"fonts/fonts.json maps {original} to a missing file: {stored}")
    return []


def validate(pack: Path) -> list[str]:
    """Choose the validator by the generated layout: an item preview pack or a game UI resource set."""
    if (pack / "pack.json").is_file():
        return validate_pack(pack)
    if (pack / "ui.json").is_file():
        return validate_ui_resources(pack)
    raise ValueError("The folder is neither an item preview pack (pack.json) nor a game UI resource set (ui.json)")


def stage(package: Path, pack: Path, output: Path) -> list[str]:
    """Copy `package` (without its data folder) to `output` and fill the data folder from `pack`."""
    manifest = tomllib.loads((package / "plugin.toml").read_text(encoding="utf-8"))
    if manifest.get("plugin", {}).get("kind") != "assets":
        raise ValueError("The package is not an assets package")
    root = manifest.get("assets", {}).get("root")
    if not isinstance(root, str) or not root or root.startswith("/") or ".." in root.split("/") or "\\" in root:
        raise ValueError("The package declares no safe [assets] root")
    warnings = validate(pack)
    if output.exists() and any(output.iterdir()):
        raise ValueError(f"The staging folder is not empty: {output}")
    _reject_links(package)
    for source in sorted(package.rglob("*")):
        _reject_links(source)
        relative = source.relative_to(package)
        # Any data folder already inside the source package is replaced by the generated pack, never merged.
        if relative.as_posix() == root or relative.as_posix().startswith(root + "/"):
            continue
        target = output / relative
        if source.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
    data = output / root
    for source in sorted(pack.rglob("*")):
        relative = source.relative_to(pack)
        target = data / relative
        if source.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
    return warnings


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path, help="Assets package folder holding plugin.toml, for example store-plugins/BubbleFighter/bubblefighter.fantasy.item-previews")
    parser.add_argument("--pack", required=True, type=Path, help="Generated folder: an item preview pack (pack.json, atlas/, builds/) or a game UI resource set (ui.json, themes, fonts/)")
    parser.add_argument("--output", required=True, type=Path, help="Empty or new staging folder for the complete package")
    args = parser.parse_args()
    for warning in stage(args.package.resolve(), args.pack.resolve(), args.output.resolve()):
        print("warning:", warning)
    print("Staged", args.output)


if __name__ == "__main__":
    try:
        main()
    except ValueError as error:
        raise SystemExit(f"error: {error}") from None
