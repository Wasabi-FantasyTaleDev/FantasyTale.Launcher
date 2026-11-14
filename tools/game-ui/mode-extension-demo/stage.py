"""Stage the mode overlay with private native button art, without copying the shared UI package."""
from __future__ import annotations

import argparse
import json
from pathlib import Path, PurePosixPath
import shutil


SOURCE_FILES = (
    'plugin.toml', 'mode-extension.html', 'mode-extension.js', 'controller.json',
    'messages.js', 'mode-descriptions.js', 'mode-catalog.json', 'mode-availability.json',
    'room-bindings.json', 'mode-availability.schema.json', 'README.md', 'README.zh.md',
)


def reject_link(path: Path) -> None:
    """Require ordinary local inputs so staging cannot escape through a linked directory."""
    if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
        raise ValueError(f'Linked staging input is not supported: {path}')


def stage(package: Path, resources: Path, output: Path) -> dict:
    """Validate every input before creating the output, then copy only the required small mode assets."""
    for root in (package, resources, output):
        reject_link(root)
    package, resources, output = package.resolve(), resources.resolve(), output.resolve()
    if output.is_relative_to(package) or output.is_relative_to(resources):
        raise ValueError('The staging output must be outside its source directories.')
    if output.exists() and any(output.iterdir()):
        raise ValueError(f'The staging output must be empty: {output}')
    for name in SOURCE_FILES:
        source = package / name
        reject_link(source)
        if not source.is_file() or not source.stat().st_size:
            raise ValueError(f'Missing mode package source: {source}')
    reject_link(resources / 'mode-skins.json')
    manifest = json.loads((resources / 'mode-skins.json').read_text(encoding='utf-8'))
    if manifest.get('version') != 1 or not isinstance(manifest.get('skins'), dict):
        raise ValueError('The private mode art has no supported mode-skins.json.')
    files = set()
    skins = {}
    for name, skin in manifest['skins'].items():
        states = skin.get('states')
        if not isinstance(states, list) or len(states) != 4 or (skin.get('width'), skin.get('height')) != (176, 59):
            raise ValueError(f'Native mode art requires four 176x59 states: {name}')
        for reference in states:
            if not isinstance(reference, str) or '\\' in reference:
                raise ValueError(f'Invalid mode asset reference: {reference}')
            relative = PurePosixPath(reference)
            if relative.is_absolute() or '..' in relative.parts or not relative.parts or relative.parts[0] != 'modes':
                raise ValueError(f'Mode asset is outside modes/: {reference}')
            source = resources
            for part in relative.parts:
                source /= part
                reject_link(source)
            if not source.is_file() or not source.resolve().is_relative_to(resources):
                raise ValueError(f'Missing mode asset: {reference}')
            files.add(reference)
        # Source archive paths and other private provenance are not distribution inputs.
        skins[name] = {'states': states, 'width': 176, 'height': 59}
    if 'Noitem' not in skins:
        raise ValueError('The native competitive-mode fallback is missing.')
    output.mkdir(parents=True, exist_ok=True)
    for name in SOURCE_FILES:
        shutil.copyfile(package / name, output / name)
    for reference in sorted(files):
        destination = output / reference
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(resources / reference, destination)
    (output / 'mode-skins.json').write_text(json.dumps({'version': 1, 'skins': skins}, ensure_ascii=False, indent=2), encoding='utf-8')
    return {'skins': len(skins), 'images': len(files), 'output': str(output)}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True, type=Path)
    parser.add_argument('--resources', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(stage(args.package, args.resources, args.output)))
