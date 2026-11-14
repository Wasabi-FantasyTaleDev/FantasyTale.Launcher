"""Prepare a private mode-picker preview from native UI resources and client archives."""
from __future__ import annotations

import argparse
from dataclasses import asdict
import hashlib
import io
import json
from pathlib import Path
import shutil
import struct
import sys
import xml.etree.ElementTree as ET

from PIL import Image


def prepare(viewer: Path, client: Path, shared: Path, third: Path, output: Path) -> None:
    """Extract the modern mode-button states while retaining each window theme independently."""
    sys.path.insert(0, str(viewer))
    from bf_item_viewer.archives.index import ResourceIndex
    from bf_item_viewer.service import default_cache_root
    output.mkdir(parents=True, exist_ok=True)
    for name in ('fonts', 'sounds', 'cursors', 'v1', 'v2'):
        source = shared / name
        if not source.is_dir():
            raise ValueError(f'Missing prepared resources: {source}')
        shutil.copytree(source, output / name, dirs_exist_ok=True)
    shutil.copy2(shared / 'ui.json', output / 'ui.json')
    shutil.copytree(third, output / 'v3', dirs_exist_ok=True)
    index = ResourceIndex(client, default_cache_root())
    try:
        if not index.open_cached():
            raise ValueError('Build the maintained ItemViewer index for this client before preparing mode resources.')
        document = 'Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui'
        raw = index.read(document)
        form = ET.fromstring(raw)
        references = {}
        for button in form.iter('UiButton'):
            sprite = button.get('SpriteSet', '')
            if not sprite.startswith('Images/DlgCreateRoom/BTN_'):
                continue
            name = Path(sprite).stem.removeprefix('BTN_')
            references[name] = {'width': int(button.get('Width')), 'height': int(button.get('Height')),
                                'attrs': dict(button.attrib)}
        skins = {}
        for member in index.list_prefix('Images/DlgCreateRoom/', '.smf'):
            name = Path(member).stem.removeprefix('BTN_')
            descriptor = index.read(member)
            version, count = struct.unpack_from('<II', descriptor)
            if version != 2 or count != 4 or len(descriptor) != 8 + count * 32:
                raise ValueError(f'Expected four native mode-button states: {member}')
            png = index.read(member[:-4] + '.png')
            with Image.open(io.BytesIO(png)) as image:
                paths = []
                for number in range(count):
                    left, top, right, bottom = struct.unpack_from('<iiii', descriptor, 8 + number * 32)
                    crop = image.crop((left, top, right + 1, bottom + 1)).convert('RGBA')
                    relative = Path('modes') / name / f'{number}.webp'
                    destination = output / relative
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    crop.save(destination, 'WEBP', lossless=True, exact=True)
                    with Image.open(destination) as check:
                        if crop.tobytes() != check.convert('RGBA').tobytes():
                            raise ValueError(f'Converted native sprite differs: {relative}')
                    paths.append(relative.as_posix())
            skins[name] = {'states': paths, 'width': 176, 'height': 59, 'form': references.get(name),
                           'source': asdict(index.provenance(member)), 'sha256': hashlib.sha256(png).hexdigest()}
        if 'Noitem' not in skins:
            raise ValueError('The competitive-mode fallback sprite is missing.')
        result = {'version': 1, 'nativeForm': document, 'formSha256': hashlib.sha256(raw).hexdigest(),
                  'skins': skins}
        (output / 'mode-skins.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({'mode_skins': len(skins), 'themes': ['v1', 'v2', 'v3'], 'output': str(output)}))
    finally:
        index.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--viewer', required=True, type=Path)
    parser.add_argument('--client', required=True, type=Path)
    parser.add_argument('--shared', required=True, type=Path)
    parser.add_argument('--v3', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    prepare(args.viewer, args.client, args.shared, args.v3, args.output)
