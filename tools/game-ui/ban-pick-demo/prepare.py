"""Prepare a private Ban Pick demo from client archives and ItemViewer exports."""
import argparse
import csv
from dataclasses import asdict
import hashlib
import json
import io
from pathlib import Path
import shutil
import sys


def prepare(viewer_root, client, text_client, font_data, output, v1_client, assets_only=False, popup_client=None):
    """Extract native UI sprites and render the declared sample item previews."""
    viewer_root = viewer_root.resolve()
    output = output.resolve()
    if assets_only:
        required = [output / 'catalog.json', output / 'preparation.json',
                    *(output / 'fonts' / name for name in ('GB13000.ttf', 'DF_GB_Y9.TTF', 'NexonBTBold.otf'))]
        missing = [str(path) for path in required if not path.is_file()]
        if missing:
            raise ValueError('Prepare the full demo before refreshing themes: ' + ', '.join(missing))
    profile_path = viewer_root / 'bf_item_viewer/data/profiles/kr-812.json'
    if not profile_path.is_file():
        raise ValueError('Use the maintained tools/item-viewer source, not the retired BFItemViewer checkout.')
    sys.path.insert(0, str(viewer_root))
    from bf_item_viewer import __version__
    from bf_item_viewer.service import ClientSession
    from bf_item_viewer.service import default_cache_root
    from bf_item_viewer.archives.index import ResourceIndex
    from bf_item_viewer.catalog.spreadsheet import read_sheets
    from bf_item_viewer.export.batch import ExportOptions, export_items
    from theme_resources import prepare_theme
    from web_fonts import prepare_web_font
    from preview_adapter import PreviewAdapter
    output.mkdir(parents=True, exist_ok=True)
    session = ClientSession(client)
    if not assets_only:
        session.load()
    text_index = ResourceIndex(text_client, default_cache_root())
    if not text_index.open_cached():
        text_index.build()
    workbook_path = 'Config/ShopItem2.xml'
    translations_path = 'StringTable/TranslatedXmlTable.csv'
    text_rows = read_sheets(text_index.read(workbook_path), {'ShopItem'})['ShopItem']
    native_items = {int(float(row['itemId'])): row for row in text_rows
                    if row.get('itemId', '').replace('.', '', 1).isdigit()}
    translations_raw = text_index.read(translations_path)
    translations = {}
    for row in csv.DictReader(io.StringIO(decode_table(translations_raw)), delimiter='\t'):
        category = row.get('SubCategory', '')
        if category.startswith('ShopItem2-ShopItem-itemId-'):
            translations[(row['KeyString'], category.rsplit('-', 1)[-1])] = row
    definitions = json.loads(Path(__file__).with_name('items.json').read_text(encoding='utf-8'))
    popup_index = None
    if popup_client:
        popup_index = ResourceIndex(popup_client, default_cache_root())
        if not popup_index.open_cached():
            popup_index.build()
    v1_index = ResourceIndex(v1_client, default_cache_root())
    if not v1_index.open_cached():
        v1_index.build()
    canonical_categories = read_sheets(text_index.read(workbook_path), {'MainCategory', 'SubCategory'})
    appearances = {theme: prepare_theme(index, theme, output / theme, canonical_categories, read_sheets, popup_index)
                   for theme, index in (('v1', v1_index), ('v2', text_index))}
    (output / 'fonts').mkdir(exist_ok=True)
    if not assets_only:
        for name in ('GB13000.ttf', 'DF_GB_Y9.TTF'):
            shutil.copy2(font_data / name, output / 'fonts' / name)
        shutil.copy2(client / 'Data/NexonBTBold.otf', output / 'fonts/NexonBTBold.otf')
    web_font = prepare_web_font(output / 'fonts/NexonBTBold.otf', output / 'fonts/NexonBTBold-web.otf')
    def publish_appearance():
        temporary_ui = output / 'ui.json.tmp'
        temporary_ui.write_text(json.dumps(appearances), encoding='utf-8')
        temporary_ui.replace(output / 'ui.json')
    if assets_only:
        preparation = json.loads((output / 'preparation.json').read_text(encoding='utf-8'))
        preparation['theme_sources'] = {theme: value['source'] for theme, value in appearances.items()}
        preparation['web_font'] = web_font
        preparation['demo_preview_sources'] = {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                                               for name in ('preview_adapter.py', 'items.json')}
        (output / 'preparation.json').write_text(json.dumps(preparation, ensure_ascii=False, indent=2), encoding='utf-8')
        publish_appearance()
        print('Prepared isolated v1 and v2 native themes; kept the existing item previews.')
        return
    options = ExportOptions(width=328, height=240, supersample=2, aspect_policy='client', overwrite='overwrite')
    manifest = export_items(PreviewAdapter(session, definitions), [row['id'] for row in definitions], output / 'items', options)
    entries = json.loads(Path(manifest).read_text(encoding='utf-8'))['entries']
    exports = {row['item_id']: row for row in entries}
    rows = []
    for definition in definitions:
        item = session.item(definition['id'])
        exported = exports[item.item_id]
        if exported['status'] not in ('written', 'skipped') or not exported['output']:
            raise ValueError(f'Item preview export failed: {item.item_id}')
        native = native_items.get(item.item_id, {})
        def field(name, language):
            translated = translations.get((str(item.item_id), name), {}).get('Translated_' + language, '')
            return translated or (native.get(name, '') if language == 'Cn' else '')
        chinese = field('itemName', 'Cn')
        if not chinese or not any('\u4e00' <= char <= '\u9fff' for char in chinese):
            raise ValueError(f'No native Chinese item name for {item.item_id}; choose a sample present in the Chinese client.')
        names = {'zh-CN': chinese, 'zh-TW': chinese, 'ko': item.name}
        for language in ('en', 'id', 'vi', 'th'):
            names[language] = field('itemName', 'En') or item.name
        details = {}
        for language, source in (('zh-CN', 'Cn'), ('zh-TW', 'Cn'), ('ko', 'Kr'), ('en', 'En'), ('id', 'En'), ('vi', 'En'), ('th', 'En')):
            values = {name: field(name, source) for name in ('slogan', 'itemDesc', 'desc1', 'desc2')}
            if source == 'Cn':
                values = {key: value if not any('\uac00' <= char <= '\ud7a3' for char in value) else '' for key, value in values.items()}
            details[language] = values
        sub_category = 'character' if definition['category'] == 'characters' else item.sub_category
        rows.append({'id': item.item_id, 'category': definition['category'], 'subCategory': sub_category, 'names': names, 'details': details,
                     'preview': 'items/' + Path(exported['output']).name})
    (output / 'catalog.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    source_hashes = {path.relative_to(viewer_root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                     for path in sorted((viewer_root / 'bf_item_viewer').rglob('*'))
                     if path.is_file() and path.suffix in ('.py', '.json')}
    (output / 'preparation.json').write_text(json.dumps({
        'renderer_version': __version__, 'renderer_root': str(viewer_root),
        'web_font': web_font,
        'renderer_sources': source_hashes, 'build': session.build,
        'demo_preview_sources': {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                                 for name in ('preview_adapter.py', 'items.json')},
        'export_manifest': str(Path(manifest).relative_to(output)),
        'sample_count': len(rows), 'options': asdict(options),
        'profile_outline': session.profile.raw.get('shading', {}).get('outline'),
        'text_client': str(text_client.resolve()),
        'text_sources': {path: asdict(text_index.provenance(path)) for path in (workbook_path, translations_path)},
        'theme_sources': {theme: value['source'] for theme, value in appearances.items()},
    }, ensure_ascii=False, indent=2), encoding='utf-8')
    publish_appearance()
    print(f'Prepared {len(rows)} item previews and isolated v1/v2 native themes.')


def decode_table(raw):
    """Read the client's UTF-16 translation table, including quoted multiline cells."""
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--viewer-root', required=True, type=Path)
    parser.add_argument('--client', required=True, type=Path)
    parser.add_argument('--text-client', required=True, type=Path)
    parser.add_argument('--v1-client', required=True, type=Path)
    parser.add_argument('--assets-only', action='store_true', help='Refresh native themes without rerendering the existing catalog')
    parser.add_argument('--font-data', required=True, type=Path)
    parser.add_argument('--popup-client', type=Path, help='A newer client whose roll-over popups fill themes that lack them')
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    prepare(args.viewer_root, args.client, args.text_client, args.font_data, args.output, args.v1_client, args.assets_only, args.popup_client)
