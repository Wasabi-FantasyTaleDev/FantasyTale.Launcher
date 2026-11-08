"""Prepare the cn-279 narrator's original images, translations and PCM sounds."""
import argparse
import csv
import hashlib
import json
import math
import shutil
import struct
import subprocess
import sys
import wave
import xml.etree.ElementTree as ET
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent
NS = {'s': 'urn:schemas-microsoft-com:office:spreadsheet'}
STEPS = (7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767)
INDEX = (-1,-1,-1,-1,2,4,6,8)


def decode_mono_ima(data, count):
    """Decode 36-byte mono blocks, each containing 64 signed PCM samples."""
    output = []
    for offset in range(0, math.ceil(count / 64) * 36, 36):
        predictor, index, reserved = struct.unpack_from('<hBB', data, offset)
        if index > 88 or reserved:
            raise ValueError('Invalid mono IMA block header')
        output.append(predictor)
        for i in range(63):
            nibble = (data[offset + 4 + i // 2] >> (4 * (i % 2))) & 15
            step = STEPS[index]
            delta = step >> 3
            if nibble & 1:
                delta += step >> 2
            if nibble & 2:
                delta += step >> 1
            if nibble & 4:
                delta += step
            predictor = max(-32768, min(32767, predictor + (-delta if nibble & 8 else delta)))
            index = max(0, min(88, index + INDEX[nibble & 7]))
            output.append(predictor)
    return struct.pack('<' + 'h' * count, *output[:count])


def pe_bytes(executable, address, count):
    raw = executable.read_bytes()
    pe = struct.unpack_from('<I', raw, 60)[0]
    optional = pe + 24
    base = struct.unpack_from('<I', raw, optional + 28)[0]
    sections = struct.unpack_from('<H', raw, pe + 6)[0]
    size = struct.unpack_from('<H', raw, pe + 20)[0]
    for i in range(sections):
        offset = optional + size + 40 * i
        virtual_size, virtual, raw_size, raw_offset = struct.unpack_from('<IIII', raw, offset + 8)
        if base + virtual <= address < base + virtual + max(raw_size, virtual_size):
            start = raw_offset + address - base - virtual
            return raw[start:start + count]
    raise ValueError(f'Unmapped client address {address:x}')


def rows(sheet):
    """Preserve sparse SpreadsheetML column indices."""
    for row in sheet.findall('.//s:Row', NS):
        values = []
        for cell in row.findall('s:Cell', NS):
            index = int(cell.attrib.get('{' + NS['s'] + '}Index', len(values) + 1))
            values.extend([''] * (index - len(values) - 1))
            data = cell.find('s:Data', NS)
            values.append(''.join(data.itertext()) if data is not None else '')
        yield values


def build_glyph_atlas(font_path, target, texts, dependency=None):
    """Keep hinted grayscale coverage, bearings and integer advances in data."""
    if dependency is not None:
        sys.path.insert(0, str(dependency))
    import freetype
    face = freetype.Face(str(font_path))
    face.set_char_size(15 << 6, 15 << 6, 0, 0)
    characters = set(''.join(texts)) | {chr(i) for i in range(32, 127)} | {'\n', '\u25a1'}
    atlas = Image.new('RGBA', (512, math.ceil(len(characters) / 16) * 32))
    glyphs = {}
    for i, character in enumerate(sorted(characters)):
        if character!='\n' and not face.get_char_index(character):
            raise ValueError(f'Original font has no glyph for U+{ord(character):04X}: {character!r}')
        # cn-279 sub_53F870 loads outlines with 40968 (0xA008); no emboldening.
        face.load_char(character, 40968)
        advance = face.glyph.advance.x >> 6
        face.glyph.render(freetype.FT_RENDER_MODE_NORMAL)
        bitmap = face.glyph.bitmap
        width, height = bitmap.width, bitmap.rows
        x, y = (i % 16) * 32 + 2, (i // 16) * 32 + 2
        if width > 28 or height > 28:
            raise ValueError('Tutorial glyph exceeds atlas cell')
        if width and height:
            pixels = bytes(bitmap.buffer)
            mask = Image.frombytes('L', (width, height), pixels, 'raw', 'L', bitmap.pitch)
            glyph = Image.new('RGBA', (width, height), (34, 34, 34, 0))
            glyph.putalpha(mask)
            atlas.paste(glyph, (x, y))
        glyphs[character] = {'x': x, 'y': y, 'width': width, 'height': height,
                             'left': face.glyph.bitmap_left, 'top': face.glyph.bitmap_top,
                             'advance': advance, 'lineHeight': height + 15 - face.glyph.bitmap_top}
    atlas.save(target / 'glyphs.png')
    return {'rendererVersion': '.'.join(map(str, freetype.version())), 'loadFlags': 40968,
            'fontSha256': hashlib.sha256(font_path.read_bytes()).hexdigest(),
            'size': 15, 'glyphs': glyphs}


def prepare(client, work, dependencies=None):
    archive = ['dotnet', 'run', '--project', str(REPO / 'tools/BubbleFighter.Fantasy.Archives.Tool'), '-c', 'Release', '--']
    subprocess.run(archive + ['extract', str(client / 'Pack/Images0007.pkn'), str(work), '--filter', 'Images/TutorialStage'], check=True, cwd=REPO)
    members = ['Config/TutorialStage.xml', 'Config/InGameUI.xml', 'Config/Fonts_cn.csv', 'Sounds/SoundList.xml', 'StringTable/TranslatedXmlTable.csv']
    subprocess.run(archive + ['unpack', str(client), str(work), *members], check=True, cwd=REPO)
    target = ROOT / 'local/tutorial/cn-279'
    target.mkdir(parents=True, exist_ok=True)
    images = target / 'Images/TutorialStage'
    images.mkdir(parents=True, exist_ok=True)
    for name in ('Dizni.png', 'Dao.png', 'ChatBalloonNew.png'):
        shutil.copy2(work / 'Images/TutorialStage' / name, images / name)

    translations = {}
    with (work / 'dataraw/StringTable/TranslatedXmlTable.csv').open(encoding='utf-16', newline='') as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            if row['SubCategory'].endswith('-[progress]-dialogueString'):
                translations[(row['SubCategory'], row['KeyString'])] = row['Translated_Cn']
    root = ET.parse(work / 'dataraw/Config/TutorialStage.xml')
    dialogues = []
    for sheet in root.findall('s:Worksheet', NS):
        sheet_name = sheet.attrib['{' + NS['s'] + '}Name']
        values = list(rows(sheet))
        headers = values[1]
        for values_row in values[3:]:
            row = dict(zip(headers, values_row))
            if row.get('dialogueAction') != 'show':
                continue
            key = row['[progress]']
            text = translations.get((f'TutorialStage-{sheet_name}-[progress]-dialogueString', key))
            if not text:
                continue
            dialogues.append({'stage': sheet_name, 'progress': int(key), 'text': text.replace('\r\n', '\n'),
                              'balloon': row['dialogueImageFileName'],
                              'clickComplete': row.get('clickCompleteDialogue') == '1',
                              'holdMs': int(row.get('timeOutDialogue') or '-1'),
                              'nextProgress': row.get('nextProgressDialogue')})
    speed = None
    for values in rows(ET.parse(work / 'dataraw/Config/InGameUI.xml').getroot()):
        if values and values[0].lower() == 'textprintspeed':
            speed = int(values[2])
    if speed is None:
        raise ValueError('Missing textPrintSpeed')

    # cn-279 sub_512040 supplies the bank key at 0x94E020 to the sound loader.
    key = pe_bytes(client / 'BubbleFighterU.exe', 0x94E020, 32).split(b'\0')[0]
    reverse = bytes(int(f'{n:08b}'[::-1], 2) for n in range(256))
    encrypted = (client / 'Sounds/ui.fsb').read_bytes()
    bank = bytes(reverse[b] ^ key[i % len(key)] for i, b in enumerate(encrypted))
    if bank[:4] != b'FSB4':
        raise ValueError('Unsupported sound bank')
    count, header_size = struct.unpack_from('<II', bank, 4)
    offset, data_offset = 48, 48 + header_size
    sounds = {}
    for index in range(count):
        header_length = struct.unpack_from('<H', bank, offset)[0]
        name = bank[offset + 2:offset + 32].split(b'\0')[0].decode('ascii')
        samples, length = struct.unpack_from('<II', bank, offset + 32)
        mode, rate = struct.unpack_from('<II', bank, offset + 48)
        channels = struct.unpack_from('<H', bank, offset + 62)[0]
        if name in ('ui-tut-type.wav', 'ui-tut-textbox.wav'):
            if channels != 1 or mode & 0x400000 == 0:
                raise ValueError('Expected mono IMA tutorial sound')
            pcm = decode_mono_ima(bank[data_offset:data_offset + length], samples)
            destination = target / 'Sounds' / name
            destination.parent.mkdir(exist_ok=True)
            with wave.open(str(destination), 'wb') as output:
                output.setparams((1, 2, rate, samples, 'NONE', 'not compressed'))
                output.writeframes(pcm)
            sounds[name] = {'bank': 'Sounds/ui.fsb', 'subsoundIndex': index,
                            'sampleRate': rate, 'sampleCount': samples, 'channels': channels,
                            'sha256': hashlib.sha256(pcm).hexdigest()}
        offset += header_length
        data_offset += length
    if len(sounds) != 2:
        raise ValueError('Missing tutorial sounds')
    glyphs = build_glyph_atlas(client / 'Data/GB13000.ttf', target, [row['text'] for row in dialogues], dependencies)
    (target / 'glyphs.json').write_text(json.dumps(glyphs, ensure_ascii=False), encoding='utf-8')
    manifest = {'build': 'cn-279', 'referenceWidth': 1024, 'referenceHeight': 768,
                'textPrintSpeed': speed, 'font': {'alias': 'DefaultBold', 'file': 'GB13000.ttf', 'size': 15, 'weight': 400, 'color': '#222222', 'decoration': 1, 'decorationColor': '#666666'},
                'balloonCenter': [847, 474], 'portraitCenter': [884, 646],
                'textRectangle': [727, 400, 250, 100], 'textFormat': 4,
                'dialogues': dialogues, 'sounds': sounds}
    (target / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Prepared {len(dialogues)} translated dialogues, speed={speed}ms and two original sound samples.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--client-root', type=Path, required=True)
    parser.add_argument('--work-dir', type=Path, default=REPO / 'artifacts/game-ui/tutorial-v1')
    parser.add_argument('--python-dependencies', type=Path)
    args = parser.parse_args()
    prepare(args.client_root, args.work_dir, args.python_dependencies)
