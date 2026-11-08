"""Prepare the supplied rank assets; runtime tables remain editable without this importer."""
import argparse
import csv
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path
from PIL import Image


def copy(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)


def manifest(folder: Path, text: str) -> None:
    (folder / 'levels.toml').write_text(text, encoding='utf-8', newline='\n')


def prepare(source: Path, games: Path) -> None:
    bf = games / 'BubbleFighter' / 'levels'
    bf.mkdir(parents=True, exist_ok=True)
    table = next((source / 'Level_BF').glob('*.csv'))
    with table.open(encoding='utf-8-sig', newline='') as reader, (bf / 'levels.csv').open('w', encoding='utf-8', newline='') as writer:
        rows = csv.reader(reader)
        next(rows)
        output = csv.writer(writer)
        output.writerow(['level', 'minimum_experience'])
        output.writerows(rows)
    atlas = Image.open(source / 'Level_BF' / 'LevelMark.png').convert('RGBA')
    # The supplied atlas uses exact cyan for the cell separators, outside the icon art.
    pixels = list(atlas.get_flattened_data())
    atlas.putdata([(r, g, b, 0 if (r, g, b) == (0, 255, 255) else a) for r, g, b, a in pixels])
    atlas.save(bf / 'rank.png')
    manifest(bf, '''schema = 1
names_pending = true
# TODO: Fill original level names when an authoritative name table is available.
[source]
file = "levels.csv"
skip_lines = 1
level_column = 0
exp_column = 1
[[atlases]]
file = "rank.png"
first_level = 1
columns = 5
cell_width = 23
cell_height = 26
''')
    ar = games / 'AirRider' / 'levels'
    ar.mkdir(parents=True, exist_ok=True)
    rows = ET.parse(source / 'Level_AR' / 'leveltable@kr.xml').getroot().findall('Level')
    text = '''schema = 1
[source]
file = "levels.csv"
skip_lines = 1
level_column = 0
exp_column = 1
name_column = 2
'''
    with (ar / 'levels.csv').open('w', encoding='utf-8', newline='') as writer:
        output = csv.writer(writer)
        output.writerow(['level', 'minimum_experience', 'name'])
        for i, row in enumerate(rows):
            level = i + 1
            minimum = 0 if i == 0 else int(rows[i - 1].attrib['nextRp'])
            name = row.attrib['glove']
            output.writerow([level, minimum, name])
            for prefix, variant in [('', 'default'), ('스타', 'star')]:
                path = f'{variant}-{level:03}.png'
                copy(source / 'Level_AR' / (prefix + name + '.png'), ar / path)
                text += f'\n[[icons]]\nlevel = {level}\nvariant = "{variant}"\nfile = "{path}"\n'
    copy(source / 'Level_AR' / '하양.png', ar / 'unranked.png')
    text += '\n[[badges]]\nid = "unranked"\nicon_id = 10001\nname = "Unranked"\nfile = "unranked.png"\n'
    manifest(ar, text)
    tr = games / 'TalesRunner' / 'levels'
    for name in ['essenlevelinfo.txt', 'rankicon.png', 'rankicon2.png']:
        copy(source / 'Level_TR' / name, tr / name)
    manifest(tr, '''schema = 1
# Original role-level identifiers are zero-based. The source omits the initial red chick.
display_offset = 0
[source]
file = "essenlevelinfo.txt"
encoding = "utf-16le"
skip_lines = 1
filter_column = 0
filter_value = "1"
level_column = 1
exp_column = 2
name_column = 3
initial_level = 0
initial_exp = 0
# The first sheet reserves cells 98..100; ordinary levels continue on the second sheet.
[[atlases]]
file = "rankicon.png"
first_level = 0
last_level = 97
columns = 9
cell_width = 26
cell_height = 21
[[atlases]]
file = "rankicon2.png"
first_level = 98
columns = 9
cell_width = 26
cell_height = 21
# Reserved ids stay outside the normal level range when future rows are added.
[[badges]]
id = "unknown"
icon_id = 10001
name = "?"
file = "rankicon.png"
x = 0
y = 231
width = 26
height = 21
[[badges]]
id = "piero"
icon_id = 10000
name = "Piero"
file = "rankicon.png"
x = 26
y = 231
width = 26
height = 21
''')
    for game in ['BubbleFighter', 'AirRider', 'TalesRunner']:
        path = games / game / 'game.toml'
        text = path.read_text(encoding='utf-8')
        if 'levels = ' not in text:
            text = text.replace('[game]', '[game]\nlevels = "levels/levels.toml"', 1)
            path.write_text(text, encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--games', type=Path, required=True)
    args = parser.parse_args()
    prepare(args.source, args.games)
