"""Build a standalone narrator resource pack for fixed UTF-8 host dialogue."""
import argparse
import json
import shutil
import re
import tempfile
from pathlib import Path
from importlib.util import module_from_spec, spec_from_file_location

ROOT = Path(__file__).resolve().parent
spec = spec_from_file_location('tutorial_prepare', ROOT / 'prepare-tutorial.py')
prepare = module_from_spec(spec)
spec.loader.exec_module(prepare)


def format_pages(text, glyphs):
    """Wrap custom copy at measured advances and page it inside the native box."""
    paragraphs = []
    for paragraph in text.split('\n'):
        lines = []
        line, width = '', 0
        for token in re.findall(r'(?:[A-Za-z0-9]+(?: +[A-Za-z0-9]+)*|.)[，。！？；：、）》」』】”’）!?.,:;)]*', paragraph):
            token_width = sum(glyphs[c]['advance'] for c in token)
            parts = list(token) if token_width > 250 else [token]
            for part in parts:
                advance = sum(glyphs[c]['advance'] for c in part)
                if line and width + advance > 250:
                    lines.append(line)
                    line, width = '', 0
                line += part
                width += advance
        lines.append(line)
        paragraphs.append(lines)
    result, page, height = [], [], 0
    for lines in paragraphs:
        paragraph_height = sum(max([15] + [glyphs[c]['lineHeight'] for c in line + '\n']) for line in lines)
        # Keep a supplied sentence/paragraph together when it fits one page.
        if page and paragraph_height <= 100 and height + paragraph_height > 100:
            result.append('\n'.join(page))
            page, height = [], 0
        for line in lines:
            line_height = max([15] + [glyphs[c]['lineHeight'] for c in line + '\n'])
            if page and height + line_height > 100:
                result.append('\n'.join(page))
                page, height = [], 0
            page.append(line)
            height += line_height
    if page:
        result.append('\n'.join(page))
    return result


def generate(input_path, output, base, font, stage, hold_ms, dependencies=None, paginate=False):
    if output.resolve() == base.resolve():
        raise ValueError('Use a separate output pack to preserve the original client resources')
    raw = input_path.read_text(encoding='utf-8-sig')
    if input_path.suffix.lower() == '.json':
        source = json.loads(raw)
        if isinstance(source, dict):
            source = source['dialogues']
    else:
        source = [text for text in raw.replace('\r\n', '\n').split('\n\n') if text.strip()]
    if not isinstance(source, list) or not source or len(source) > 1000:
        raise ValueError('Supply between 1 and 1000 dialogue entries')
    pages = []
    for index, item in enumerate(source):
        row = {'text': item} if isinstance(item, str) else item
        if not isinstance(row, dict) or not isinstance(row.get('text'), str) or not row['text']:
            raise ValueError(f'Entry {index + 1} requires nonempty text')
        text = row['text'].replace('\r\n', '\n').replace('\r', '\n')
        if any(ord(c) < 32 and c != '\n' for c in text):
            raise ValueError(f'Entry {index + 1} contains an unsupported control character')
        timeout = int(row.get('holdMs', hold_ms))
        if timeout < -1:
            raise ValueError('holdMs must be -1 or nonnegative')
        pages.append({'stage': stage, 'progress': int(row.get('progress', index + 1)), 'text': text,
                      'balloon': 'ChatBalloonNew.png', 'clickComplete': bool(row.get('clickComplete', True)),
                      'holdMs': timeout})
    if len({p['progress'] for p in pages}) != len(pages):
        raise ValueError('Dialogue progress identifiers must be unique')
    if sum(len(page['text']) for page in pages) > 200000:
        raise ValueError('A dialogue pack is limited to 200000 input characters')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='tutorial-glyphs-', dir=output.parent) as temporary:
        glyphs = prepare.build_glyph_atlas(font, Path(temporary), [page['text'] for page in pages], dependencies)
        atlas_bytes = (Path(temporary) / 'glyphs.png').read_bytes()
    if paginate:
        expanded = []
        for page in pages:
            parts = format_pages(page['text'], glyphs['glyphs'])
            for index, text in enumerate(parts):
                expanded.append({**page, 'progress': len(expanded) + 1, 'text': text,
                                 'sourceProgress': page['progress'], 'sourceText': page['text'],
                                 'part': index + 1, 'parts': len(parts)})
        pages = expanded
    for index, page in enumerate(pages):
        page['nextProgress'] = str(pages[index + 1]['progress']) if index + 1 < len(pages) else '-1'
    report = []
    for page in pages:
        heights = []
        lines = page['text'].split('\n')
        if page['text'].endswith('\n'):
            lines.pop()
        for index, line in enumerate(lines):
            chars = line + ('\n' if index < len(lines) - 1 or page['text'].endswith('\n') else '')
            width = sum(glyphs['glyphs'][c]['advance'] for c in line)
            height = max([15] + [glyphs['glyphs'][c]['lineHeight'] for c in chars])
            if width > 250:
                raise ValueError(f"Page {page['progress']}, line {index + 1}: {width} units exceeds 250; supply an explicit line break")
            heights.append(height)
        if sum(heights) > 100:
            raise ValueError(f"Page {page['progress']}: {sum(heights)} units exceeds the 100-unit text rectangle")
        report.append({'progress': page['progress'], 'textHeight': sum(heights), 'utf16Units': len(page['text'].encode('utf-16le')) // 2})
    output.mkdir(parents=True, exist_ok=True)
    for directory in ('Images', 'Sounds'):
        shutil.copytree(base / directory, output / directory, dirs_exist_ok=True)
    manifest = json.loads((base / 'manifest.json').read_text(encoding='utf-8'))
    manifest['dialogues'] = pages
    manifest['contentSource'] = 'custom'
    (output / 'glyphs.png').write_bytes(atlas_bytes)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    (output / 'glyphs.json').write_text(json.dumps(glyphs, ensure_ascii=False), encoding='utf-8')
    (output / 'text-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Generated {len(pages)} pages and {len(glyphs["glyphs"])} glyphs in {output}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--base-resources', type=Path, default=ROOT / 'local/tutorial/cn-279')
    parser.add_argument('--font', type=Path, default=ROOT / 'local/fonts/GB13000.ttf')
    parser.add_argument('--stage', default='Custom')
    parser.add_argument('--hold-ms', type=int, default=4500)
    parser.add_argument('--python-dependencies', type=Path)
    parser.add_argument('--paginate', action='store_true', help='Wrap and paginate custom text without resizing the original balloon')
    args = parser.parse_args()
    generate(args.input, args.output, args.base_resources, args.font, args.stage, args.hold_ms, args.python_dependencies, args.paginate)
