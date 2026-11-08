"""Prepare local client-owned resources and declarative layouts for the Web UI lab."""
import json
import argparse
import csv
import configparser
import hashlib
import shutil
import struct
import xml.etree.ElementTree as ET
from PIL import Image
from pathlib import Path

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent
OUT = ROOT / "local"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--v1-resources', type=Path, default=REPO / 'artifacts/game-ui/v1-resources/dataraw')
parser.add_argument('--v1-layouts', type=Path, default=REPO / 'artifacts/game-ui/v1')
parser.add_argument('--v2-resources', type=Path, default=REPO / 'artifacts/game-ui/v2')
parser.add_argument('--v3-resources', type=Path, help='Extracted cn-774 dataraw directory')
parser.add_argument('--only-v3', action='store_true', help='Leave the existing v1/v2 packs untouched')
parser.add_argument('--font-data', type=Path, required=True, help='Directory containing the client-owned font files for the local preview')
args = parser.parse_args()


def convert(theme, source, layouts):
    target = OUT / theme
    target.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source / "Images", target / "Images", dirs_exist_ok=True)
    sprites = {}
    provenance = {}
    for log in (source / 'sources.tsv', source.parent / 'sources.tsv', layouts / 'sources.tsv'):
        if log.exists():
            with log.open(encoding='utf-8-sig') as stream:
                for row in csv.DictReader(stream, delimiter='\t'):
                    provenance[row['member']] = row
    for path in (source / "Images").rglob("*.smf"):
        raw = path.read_bytes()
        version, count = struct.unpack_from("<II", raw)
        if version != 2 or len(raw) != 8 + count * 32:
            raise ValueError(f"Unsupported sprite descriptor: {path}")
        sprites[path.relative_to(source).as_posix()] = [
            list(struct.unpack_from("<iiii", raw, 8 + i * 32)) for i in range(count)
        ]
        atlas = Image.open(path.with_suffix('.png'))
        for index, (left, top, right, bottom) in enumerate(sprites[path.relative_to(source).as_posix()]):
            crop = target / path.relative_to(source).with_suffix('') / f'{index}.png'
            crop.parent.mkdir(parents=True, exist_ok=True)
            atlas.crop((left, top, right + 1, bottom + 1)).save(crop)
    translations = {}
    translation_file = source / 'StringTable/TranslatedStringTable.csv'
    if translation_file.exists():
        with translation_file.open(encoding='utf-16', newline='') as stream:
            for row in csv.reader(stream, delimiter='\t'):
                if len(row) >= 2 and row[0] and row[1]:
                    translations[row[0]] = row[1].replace('\\n', '\n')
    ui_translations = {}
    ui_translation_file = source / 'StringTable/ui.yaml'
    if ui_translation_file.exists():
        import yaml
        ui_translations = yaml.safe_load(ui_translation_file.read_text(encoding='utf-8-sig'))
    forms = {}
    for path in (layouts / "Ui").rglob("*.ui"):
        root = ET.parse(path).getroot()
        form_translations = ui_translations.get('ui/' + path.relative_to(layouts / 'Ui').as_posix(), {})
        def node(element):
            attrs = dict(element.attrib)
            if attrs.get('Text') in translations:
                attrs['Text'] = translations[attrs['Text']]
            localized = form_translations.get(attrs.get('Name'), {}).get('cn')
            if localized is not None:
                attrs['Text'] = '' if localized == 'reserved' else localized
            if attrs.get('TextKey') in ('uicustom/Button/Confirm', 'uicustom/Button/Cancel'):
                attrs['Text'] = '确认' if attrs['TextKey'].endswith('Confirm') else '取消'
            return {"type": element.tag, "attrs": attrs,
                    "children": [node(child) for child in element]}
        forms[path.relative_to(layouts / 'Ui').as_posix()] = node(root)
    scheme = configparser.ConfigParser(interpolation=None)
    scheme.optionxform = str
    scheme.read_string((layouts / 'Ui/UiScheme.ini').read_text(encoding='cp949', errors='replace'))
    scheme_data = {section: dict(scheme[section]) for section in scheme.sections()}
    (target / "manifest.json").write_text(json.dumps({"sprites": sprites, "forms": forms, "provenance": provenance, "scheme": scheme_data, "translations": translations}, ensure_ascii=False), encoding="utf-8")
    research = ROOT / 'research'
    research.mkdir(exist_ok=True)
    with (research / f'assets-{theme}.tsv').open('w', encoding='utf-8', newline='') as stream:
        writer = csv.writer(stream, delimiter='\t')
        writer.writerow(('member', 'source_archive', 'sha256', 'frame', 'inclusive_rectangle'))
        for member, frames in sorted(sprites.items()):
            origin = provenance.get(member, {})
            digest = hashlib.sha256((source / member).read_bytes()).hexdigest()
            for frame, rectangle in enumerate(frames):
                writer.writerow((member, origin.get('source', ''), digest, frame, ','.join(map(str, rectangle))))


if not args.only_v3:
    convert("v1", args.v1_resources, args.v1_layouts)
    convert("v2", args.v2_resources, args.v2_resources)
if args.v3_resources:
    convert('v3', args.v3_resources, args.v3_resources)
elif args.only_v3:
    parser.error('--only-v3 requires --v3-resources')
(OUT / "fonts").mkdir(parents=True, exist_ok=True)
for name in ("GB13000.ttf", "DF_GB_Y9.TTF"):
    shutil.copy2(args.font_data / name, OUT / "fonts" / name)
print("Prepared requested themes, sprite rectangles, original layouts and fonts.")
