"""Copy prepared game UI resources into a shared assets package, storing sprites as lossless WebP.

The input is a prepared resource root (theme folders with manifest.json, fonts/, optional sounds/ and ui.json), such as
the Ban Pick demo's private preparation output. Item previews are not copied: they come from the item preview package.
Every PNG is converted losslessly and decoded again to prove identical RGBA pixels; each converted theme manifest and
every ui.json appearance records imageFormat "webp", which the library uses to resolve the same paths. Fonts are
stored as WOFF2 (same outlines, tables and metrics, compressed) and fonts/fonts.json maps each original file name to its
stored name; a font with a browser-normalized "<name>-web" copy is replaced by that copy.

usage: python -X utf8 tools/game-ui/pack-resources.py <prepared root> <package data folder> [--format png|webp]
"""
from __future__ import annotations

import argparse
import io
import json
import shutil
import sys
from pathlib import Path

from PIL import Image
from fontTools.ttLib import TTFont

THEMES = ("v1", "v2", "v3")
SHARED = ("fonts", "sounds", "cursors")
# Cursor images stay PNG: CSS cursor url() decoding of WebP is not guaranteed.
KEEP_PNG = ("cursors",)


def convert(source: Path, target: Path) -> None:
    with Image.open(source) as image:
        rgba = image.convert("RGBA")
    buffer = io.BytesIO()
    # exact keeps the colour under fully transparent pixels, so scaled sprites blend exactly as the PNG did.
    rgba.save(buffer, "WEBP", lossless=True, quality=100, method=6, exact=True)
    with Image.open(io.BytesIO(buffer.getvalue())) as check:
        if check.convert("RGBA").tobytes() != rgba.tobytes():
            raise SystemExit(f"lossless WebP changed pixels: {source}")
    target.with_suffix(".webp").write_bytes(buffer.getvalue())


def pack_fonts(source: Path, target: Path) -> tuple[int, int]:
    target.mkdir(parents=True)
    names: dict[str, str] = {}
    before = after = 0
    fonts = sorted(path for path in source.iterdir() if path.suffix.lower() in (".ttf", ".otf"))
    for path in fonts:
        if path.is_symlink():
            raise SystemExit(f"links are not packaged: {path}")
        web = path.with_name(f"{path.stem}-web{path.suffix}")
        if web.exists():
            continue
        out = target / (path.stem.removesuffix("-web") + ".woff2")
        font = TTFont(path)
        glyphs, cmap = font.getGlyphOrder(), font.getBestCmap()
        font.flavor = "woff2"
        font.save(out)
        check = TTFont(out)
        if check.getGlyphOrder() != glyphs or check.getBestCmap() != cmap:
            raise SystemExit(f"WOFF2 changed glyphs or mappings: {path}")
        before += path.stat().st_size
        after += out.stat().st_size
        names[path.name] = out.name
        if path.stem.endswith("-web"):
            names[path.name.replace("-web", "")] = out.name
    (target / "fonts.json").write_text(json.dumps(names, ensure_ascii=False, indent=1), encoding="utf-8")
    return before, after


def copy_tree(source: Path, target: Path, webp: bool) -> tuple[int, int]:
    before = after = 0
    for path in sorted(source.rglob("*")):
        if path.is_symlink():
            raise SystemExit(f"links are not packaged: {path}")
        if not path.is_file():
            continue
        out = target / path.relative_to(source)
        out.parent.mkdir(parents=True, exist_ok=True)
        before += path.stat().st_size
        if webp and path.suffix.lower() == ".png":
            convert(path, out)
            after += out.with_suffix(".webp").stat().st_size
        else:
            shutil.copyfile(path, out)
            after += out.stat().st_size
    return before, after


def mark(path: Path, apply) -> None:
    data = json.loads(path.read_text(encoding="utf-8"))
    apply(data)
    path.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("prepared")
    parser.add_argument("output")
    parser.add_argument("--format", choices=["png", "webp"], default="webp")
    args = parser.parse_args()
    root, out = Path(args.prepared).resolve(), Path(args.output).resolve()
    if out.exists() and any(out.iterdir()):
        raise SystemExit(f"output folder is not empty: {out}")
    webp = args.format == "webp"
    themes = [name for name in THEMES if (root / name / "manifest.json").is_file()]
    if not themes:
        raise SystemExit(f"no prepared theme manifest under {root}")
    total = [0, 0]
    for name in [*themes, *(entry for entry in SHARED if (root / entry).is_dir())]:
        before, after = pack_fonts(root / name, out / name) if name == "fonts" else copy_tree(root / name, out / name, webp and name not in KEEP_PNG)
        total[0] += before; total[1] += after
        if webp and name in themes:
            mark(out / name / "manifest.json", lambda data: data.__setitem__("imageFormat", "webp"))
    if (root / "ui.json").is_file():
        shutil.copyfile(root / "ui.json", out / "ui.json")
        if webp:
            mark(out / "ui.json", lambda data: [appearance.__setitem__("imageFormat", "webp") for appearance in data.values()])
    print(json.dumps({"themes": themes, "format": args.format, "source_bytes": total[0], "package_bytes": total[1]}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
