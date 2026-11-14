"""Export isolated native inventory themes from their own client archives."""
import configparser
from dataclasses import asdict
import hashlib
import io
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
from PIL import Image


def decode_ui(raw):
    """Decode source UI documents without modifying their layout attributes."""
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'cp949', errors='replace')


def prepare_theme(index, theme, target, canonical_categories, read_sheets, popup_index=None):
    """Keep sprites, scheme and form geometry owned by one client build. Roll-over popups the theme's client lacks
    come from popup_index (a newer client): the owner wants the stat popups on every theme."""
    target.mkdir(parents=True, exist_ok=True)
    sprites, provenance = {}, {}
    prefixes = ('Images/Ui/', 'Images/Shop2/inventory_box/', 'Images/Shop2/item_info/',
                'Images/Shop2/main_category/', 'Images/Shop2/sub_category/')
    for prefix in prefixes:
        for name in index.list_prefix(prefix):
            if not name.endswith(('.smf', '.png')):
                continue
            raw = index.read(name)
            destination = target / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(raw)
            provenance[name] = {'sha256': hashlib.sha256(raw).hexdigest(), 'source': asdict(index.provenance(name))}
            if name.endswith('.smf'):
                version, count = struct.unpack_from('<II', raw)
                if version != 2 or len(raw) != 8 + count * 32:
                    raise ValueError(f'Unsupported sprite descriptor: {name}')
                sprites[name] = [list(struct.unpack_from('<iiii', raw, 8 + i * 32)) for i in range(count)]
    for name, rectangles in sprites.items():
        with Image.open((target / name).with_suffix('.png')) as atlas:
            for frame, (left, top, right, bottom) in enumerate(rectangles):
                crop_path = (target / name).with_suffix('') / f'{frame}.png'
                crop_path.parent.mkdir(parents=True, exist_ok=True)
                crop = atlas.crop((left, top, right + 1, bottom + 1)).convert('RGBA')
                crop.save(crop_path)
                if '/main_category/' in name or '/sub_category/' in name:
                    if '/sub_category/' in name:
                        crop = Image.new('RGBA', crop.size)
                    elif crop.width > 18:
                        for y in range(crop.height):
                            colour = crop.getpixel((8, y))
                            for x in range(9, crop.width - 9):
                                crop.putpixel((x, y), colour)
                    crop.save(crop_path.with_name(f'{frame}-blank.png'))
    with Image.open(target / 'Images/Shop2/inventory_box/inventory_use.png') as image:
        use = image.convert('RGBA')
        border = Image.new('RGBA', use.size)
        for box in ((0, 0, use.width, 4), (0, use.height - 4, use.width, use.height),
                    (0, 0, 4, use.height), (use.width - 4, 0, use.width, use.height)):
            border.paste(use.crop(box), box)
        border.save(target / 'Images/Shop2/inventory_box/inventory_use_frame.png')
    actions = {}
    states = ('Up', 'Down', 'Hover', 'Disabled')
    for kind in ('Ok', 'Cancel'):
        actions[kind] = []
        for state_number, state in enumerate(states):
            if theme == 'v1':
                actions[kind].append(f'Images/Ui/DialogButton{kind}-{state}.smf')
                continue
            # DlgCreateRoom.ui uses this four-state v2 button; retain its icon and gloss.
            original = f'Images/Ui/DialogBtn{kind}.smf'
            rectangle = sprites[original][state_number]
            with Image.open(io.BytesIO(index.read(original.replace('.smf', '.png')))) as atlas:
                left, top, right, bottom = rectangle
                crop = atlas.crop((left, top, right + 1, bottom + 1)).convert('RGBA')
            for y in range(5, crop.height - 5):
                colour = crop.getpixel((26, y))
                for x in range(27, crop.width - 6):
                    crop.putpixel((x, y), colour)
            derived = f'Images/Ui/BanPickButton{kind}-{state}.smf'
            xs = ((0, 26), (27, 27), (crop.width - 6, crop.width - 1))
            ys = ((0, 4), (5, crop.height - 6), (crop.height - 5, crop.height - 1))
            rectangles = [[x1, y1, x2, y2] for y1, y2 in ys for x1, x2 in xs]
            sprites[derived] = rectangles
            directory = (target / derived).with_suffix('')
            directory.mkdir(parents=True, exist_ok=True)
            for frame, (x1, y1, x2, y2) in enumerate(rectangles):
                crop.crop((x1, y1, x2 + 1, y2 + 1)).save(directory / f'{frame}.png')
            provenance[derived] = {'derived_from': original, 'frame': state_number,
                                   'source': asdict(index.provenance(original))}
            actions[kind].append(derived)
    scheme = configparser.ConfigParser(interpolation=None)
    scheme.optionxform = str
    scheme.read_string(decode_ui(index.read('Ui/UiScheme.ini')))
    info_raw = index.read('Ui/DlgItemInfo.ui')
    info = ET.fromstring(decode_ui(info_raw))
    def ui_node(element):
        return {'type': element.tag, 'attrs': dict(element.attrib),
                'children': [ui_node(child) for child in element if child.tag != 'UiMarker']}
    info_node = ui_node(info)
    info_node['attrs'].update(PosX='0', PosY='0')
    marker = next(child for child in info if child.get('Name') == 'mrkTitleBar')
    title_bar = {'skin': 'Images/Shop2/item_info/iteminfo_titlebar01.smf',
                 'x': int(marker.get('PosX')), 'y': int(marker.get('PosY')),
                 'width': int(marker.get('Width')), 'height': int(marker.get('Height'))}
    forms = {'DlgItemInfo.ui': info_node}
    # The shop's roll-over popups: weapons (kr-812 DlgWeaponSpecInfo, sub_9760A0 loads it), and the newer clients'
    # clothing and set-armor popups.
    for popup in ('Ui/DlgWeaponInfoPopup.ui', 'Ui/DlgInfoPopupArmor.ui', 'Ui/DlgInfoPopupSetArmor.ui'):
        source = index if index.exists(popup) else popup_index if popup_index and popup_index.exists(popup) else None
        if source is not None:
            raw = source.read(popup)
            forms[popup.removeprefix('Ui/')] = ui_node(ET.fromstring(decode_ui(raw)))
            provenance[popup] = {'sha256': hashlib.sha256(raw).hexdigest(), 'source': asdict(source.provenance(popup))}
    if popup_index is not None and not any('RollOver_Popup' in name for name in sprites):
        for name in popup_index.list_prefix('Images/Ui/RollOver_Popup/'):
            if not name.endswith(('.smf', '.png')):
                continue
            raw = popup_index.read(name)
            (target / name).parent.mkdir(parents=True, exist_ok=True)
            (target / name).write_bytes(raw)
            provenance[name] = {'sha256': hashlib.sha256(raw).hexdigest(), 'source': asdict(popup_index.provenance(name))}
            if name.endswith('.smf'):
                count = struct.unpack_from('<I', raw, 4)[0]
                sprites[name] = [list(struct.unpack_from('<iiii', raw, 8 + i * 32)) for i in range(count)]
                with Image.open(io.BytesIO(popup_index.read(name.replace('.smf', '.png')))) as atlas:
                    for frame, (left, top, right, bottom) in enumerate(sprites[name]):
                        crop = (target / name).with_suffix('') / f'{frame}.png'
                        crop.parent.mkdir(parents=True, exist_ok=True)
                        atlas.crop((left, top, right + 1, bottom + 1)).convert('RGBA').save(crop)
    manifest = {'sprites': sprites, 'scheme': {section: dict(scheme[section]) for section in scheme.sections()},
                'forms': forms, 'provenance': provenance,
                'theme_source': str(index.client_root)}
    (target / 'manifest.json').write_text(json.dumps(manifest), encoding='utf-8')
    category_rows = read_sheets(index.read('Config/ShopItem2.xml'), {'MainCategory', 'SubCategory'})
    canonical_main = {row['id']: row['name'] for row in canonical_categories['MainCategory']}
    canonical_sub = {row['id']: row['name'] for row in canonical_categories['SubCategory']}
    category_keys = {'Weapons': 'weapons', 'Characters': 'characters', 'Clothes': 'clothes', 'Accessories': 'accessories'}
    main = {category_keys[canonical_main[row['id']]]: 'Images/Shop2/main_category/' + row['tabFileName']
            for row in category_rows['MainCategory'] if canonical_main.get(row['id']) in category_keys}
    sub = {canonical_sub[row['id']]: 'Images/Shop2/sub_category/' + row['tabFileName']
           for row in category_rows['SubCategory'] if canonical_sub.get(row['id']) and row.get('tabFileName')}
    sub['all'] = 'Images/Shop2/sub_category/store_tab_00_00'
    order = {row['name']: int(float(row.get('displayOrder') or '0'))
             for row in canonical_categories['SubCategory'] if row.get('name')}
    category_sizes = {key: {'width': sprites[path + '.smf'][0][2] - sprites[path + '.smf'][0][0] + 1,
                            'height': sprites[path + '.smf'][0][3] - sprites[path + '.smf'][0][1] + 1}
                      for key, path in main.items()}
    with Image.open(target / 'Images/Shop2/inventory_box/inventory_slot01/0.png') as card:
        # Inventory markers are 172px; the sprite includes padding for its outer glow.
        inset_x = (card.width - 172) // 2
        inset_y = (card.height - 172) // 2
        rgba = card.crop((inset_x, inset_y, inset_x + 172, inset_y + 172)).convert('RGBA')
        rgba.save(target / 'Images/Shop2/inventory_box/inventory_slot01/0-card.png')
        card = rgba
        white_rows = [y for y in range(min(40, card.height)) if rgba.getpixel((card.width // 2, y)) == (255, 255, 255, 255)]
        if not white_rows:
            raise ValueError(f'{theme} inventory name strip is missing')
        name_top = min(white_rows)
        name_bottom = max(white_rows) + 1
        status_rows = [y for y in range(card.height * 3 // 4, card.height) if rgba.getpixel((card.width // 2, y)) == (207, 207, 207, 255)]
        if not status_rows:
            raise ValueError(f'{theme} inventory status strip is missing')
        status_top = min(status_rows)
        status_bottom = max(status_rows) + 1
    with Image.open(target / 'Images/Ui/DialogSkin-Basic/1.png') as strip:
        rgba = strip.convert('RGBA')
        title_rows = [y for y in range(strip.height) if (lambda c: c[3] == 255 and c[2] - c[0] > 40)(rgba.getpixel((0, y)))]
        if not title_rows:
            raise ValueError(f'{theme} dialog title strip is missing')
    return {'theme': theme, 'categorySkins': main, 'subCategorySkins': sub, 'subCategoryOrder': order,
            'categorySizes': category_sizes,
            'nameBounds': {'top': name_top, 'height': name_bottom - name_top},
            'statusBounds': {'top': status_top, 'height': status_bottom - status_top},
            'titleBounds': {'top': min(title_rows), 'height': max(title_rows) - min(title_rows) + 1},
            'actionSkins': actions, 'actionHeight': 25 if theme == 'v1' else 27,
            'infoSize': {'width': int(info.get('Width')), 'height': int(info.get('Height'))},
            'infoTitleBar': title_bar if title_bar['y'] >= 0 else None,
            'source': {'client': str(index.client_root), 'form_sha256': hashlib.sha256(info_raw).hexdigest()}}
