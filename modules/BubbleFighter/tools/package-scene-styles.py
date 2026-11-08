"""Build independent scene-style member plans from verified original client inventories.

Inventories contain archive SHA-256 identities and decoded member hashes. The archive
reader must retain the original member names and case-sensitive client-relative URIs.
Only the generated package contains game assets; this tool never changes a client.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import struct
import re
import xml.etree.ElementTree as ET
import tomllib
import zipfile


def safe_path(value: str) -> str:
    """Keep package, archive and member references beneath their declared roots."""
    path = PurePosixPath(value)
    if (not value or path.is_absolute() or any(part in ('', '.', '..') for part in value.split('/'))
            or any(ord(c) < 32 or c in '\\:<>"|?*' for c in value)):
        raise ValueError('Unsafe resource path: ' + value)
    return value


def digest(path: Path) -> str:
    """Hash an input without loading an entire client archive into memory."""
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def fingerprint(root: Path) -> int:
    """Bind each inventory to the executable identity, independently of folder names."""
    with (root / 'BubbleFighter.exe').open('rb') as stream:
        if stream.read(2) != b'MZ':
            raise ValueError('Invalid client executable')
        stream.seek(60)
        offset = struct.unpack('<I', stream.read(4))[0]
        stream.seek(offset)
        if stream.read(4) != b'PE\0\0':
            raise ValueError('Invalid client PE header')
        stream.seek(offset + 8)
        return struct.unpack('<I', stream.read(4))[0]


def index_hash(rows: list[dict]) -> str:
    """Pin names, decoded sizes and hashes rather than accepting a partial extraction."""
    text = ''.join(f"{row['path']}\t{row['size']}\t{row['sha256']}\n" for row in sorted(rows, key=lambda row: row['path']))
    return hashlib.sha256(text.encode()).hexdigest()


def in_scope(path: str, prefixes: list[str]) -> bool:
    """Use complete directory prefixes so one scene cannot claim another scene's assets."""
    return any(path.casefold().startswith(prefix.casefold()) for prefix in prefixes)


def changes(source: list[dict], target: dict[str, dict], additions: dict[str, str]) -> list[dict]:
    """Retain existing shard ownership and bind replacements to their decoded originals."""
    result = []
    seen = set()
    for row in source:
        path = safe_path(row['path'])
        key = path.casefold()
        if key in seen:
            raise ValueError('Duplicate scene contribution: ' + path)
        seen.add(key)
        old = target.get(key)
        if old and old['sha256'] == row['sha256']:
            continue
        owners = [archive for prefix, archive in additions.items() if in_scope(path, [prefix])]
        if len(owners) != 1:
            raise ValueError('Scene member has no unique target archive: ' + path)
        result.append(dict(path=old['path'] if old else path,
            archive=old['archive'] if old else owners[0], sha256=row['sha256'],
            original_sha256=old['sha256'] if old else ''))
    return result


def music_payload(raw: bytes, routes: list[dict], active: set[str]) -> bytes:
    """Change only the native BGM table's group and event cells, retaining all other bytes."""
    ET.fromstring(raw)
    worksheets = list(re.finditer(rb'<Worksheet\b[^>]*\bss:Name="BGM"[^>]*>.*?</Worksheet>', raw, re.S))
    if len(worksheets) != 1:
        raise ValueError('Expected one native BGM sound worksheet')
    worksheet = worksheets[0]
    edits = []
    seen = set()
    for route in routes:
        if route['option'] not in active:
            continue
        for key in route['keys']:
            if key in seen:
                raise ValueError('Duplicate native BGM routing key: ' + key)
            seen.add(key)
            candidates = []
            for row in re.finditer(rb'<Row\b[^>]*>.*?</Row>', worksheet[0], re.S):
                cells = list(re.finditer(rb'<Cell\b[^>]*>.*?</Cell>', row[0], re.S))
                values = [re.search(rb'<Data\b[^>]*>(.*?)</Data>', cell[0], re.S) for cell in cells]
                if len(values) > 1 and values[1] and values[1][1] == key.encode('ascii'):
                    candidates.append((row, cells, values))
            if len(candidates) != 1:
                raise ValueError('Expected one native BGM routing key: ' + key)
            row, cells, values = candidates[0]
            if len(values) < 4:
                raise ValueError('Native BGM routing columns differ: ' + key)
            for index, value in ((2, route['group']), (3, route['event'])):
                if not values[index] or b'ss:Index' in b''.join(cell[0].split(b'>')[0] for cell in cells[:index + 1]):
                    raise ValueError('Native BGM routing columns differ: ' + key)
                begin = worksheet.start() + row.start() + cells[index].start() + values[index].start(1)
                end = begin + len(values[index][1])
                edits.append((begin, end, value.encode('ascii')))
    for begin, end, value in sorted(edits, reverse=True):
        raw = raw[:begin] + value + raw[end:]
    ET.fromstring(raw)
    return raw


def verified_blob(blobs: Path, row: dict) -> bytes:
    """Bind every derived member and companion to its decoded inventory identity."""
    blob = blobs / row['sha256']
    if blob.is_symlink() or blob.stat().st_size != row['size'] or digest(blob) != row['sha256']:
        raise ValueError('Decoded source payload differs: ' + row['path'])
    return blob.read_bytes()


def outfit_rows(rule: dict, files: dict[str, dict[str, dict]], target: dict[str, dict]) -> list[dict]:
    """Select one reviewed outfit and only add missing native animation companions."""
    source = files[rule['source_build']]
    prefix = safe_path(rule['prefix'].rstrip('/')) + '/'
    rows = [row for row in source.values() if row['path'].casefold().startswith(prefix.casefold())
            and '/' not in row['path'][len(prefix):] and PurePosixPath(row['path']).suffix.casefold() in ('.msh', '.png')]
    if len(rows) != rule['count'] or index_hash(rows) != rule['index_sha256']:
        raise ValueError('Source outfit inventory differs: ' + rule['option'] + '/' + rule['value'])
    for dependency in rule.get('dependencies', []):
        row = source[safe_path(dependency['source']).casefold()]
        destination = safe_path(dependency['target'])
        if row['sha256'] != dependency['sha256'] or not destination.casefold().startswith(prefix.casefold()):
            raise ValueError('Outfit texture companion differs from reviewed evidence')
        rows.append(dict(row, path=destination))
    if missing := rule.get('missing_companions'):
        companions = [row for row in files[missing['source_build']].values() if in_scope(row['path'], [missing['prefix']])]
        if len(companions) != missing['count'] or index_hash(companions) != missing['index_sha256']:
            raise ValueError('Native outfit companion inventory differs')
        rows.extend(row for row in companions if row['path'].casefold() not in target)
    return rows


def build(args: argparse.Namespace) -> None:
    """Emit scoped scene, sound and NPC member edits from the reviewed resource evidence."""
    rules = tomllib.loads(args.rules.read_text(encoding='utf-8'))
    if rules['schema'] != 1:
        raise ValueError('Unsupported scene evidence schema')
    inventory = json.loads(args.inventory.read_text(encoding='utf-8-sig'))
    clients = {row['build']: row for row in inventory}
    targets = {row['build']: row for row in rules['clients']}
    if len(targets) != len(rules['clients']):
        raise ValueError('Duplicate target client evidence')
    choices = [(row['option'], row['value']) for row in rules['styles']]
    if len(set(choices)) != len(choices):
        raise ValueError('Duplicate scene style evidence')
    sources = {row['build']: row['timestamp'] for row in rules.get('sources', [])}
    if len(sources) != len(rules.get('sources', [])) or sources.keys() & (targets.keys() | {rules['source_build']}):
        raise ValueError('Duplicate source client evidence')
    if len(clients) != len(inventory) or set(clients) != {rules['source_build'], *targets, *sources}:
        raise ValueError('Supply exactly the reviewed source and target inventories')
    definitions = tomllib.loads((args.definition / 'plugin.toml').read_text(encoding='utf-8'))
    music = rules.get('music')
    if music:
        option = next((row for row in definitions['options'] if row['key'] == music['option']), None)
        if not option or option['type'] != 'bool' or option['default'] is not True:
            raise ValueError('Christmas music must be a declared default-enabled boolean option')
    if set(definitions['compatibility']['builds']) != set(targets):
        raise ValueError('Published compatibility differs from reviewed client evidence')
    prefixes = [prefix for style in rules['styles'] for prefix in style['prefixes']]
    files = {}
    for identity, client in clients.items():
        expected = rules['source_timestamp'] if identity == rules['source_build'] else sources[identity] if identity in sources else targets[identity]['timestamp']
        if fingerprint(Path(client['root'])) != expected:
            raise ValueError('Client image identity differs: ' + identity)
        rows = {safe_path(row['path']).casefold(): row for row in client['files']}
        if len(rows) != len(client['files']):
            raise ValueError('Duplicate client member names: ' + identity)
        for row in rows.values():
            safe_path(row['archive'])
            if len(row['sha256']) != 64 or any(c not in '0123456789abcdef' for c in row['sha256']):
                raise ValueError('Invalid member digest')
        for archive, sha in client['archives'].items():
            if digest(Path(client['root']) / safe_path(archive)) != sha:
                raise ValueError('Original archive changed since inventory: ' + identity + '/' + archive)
        if any(row['archive'] not in client['archives'] for row in rows.values()):
            raise ValueError('Inventory omits an original archive identity')
        if identity in targets:
            selected = [row for row in rows.values() if in_scope(row['path'], prefixes)]
            rule = targets[identity]
            if len(selected) != rule['count'] or index_hash(selected) != rule['index_sha256']:
                raise ValueError('Target scene inventory differs: ' + identity)
        files[identity] = rows
    source = files[rules['source_build']]
    styles = []
    for style in rules['styles']:
        option = next((row for row in definitions['options'] if row['key'] == style['option']), None)
        if (not option or option['type'] != 'enum' or style['value'] not in option['values']
                or style['value'] == 'original' or option['default'] != 'original'):
            raise ValueError('Scene style is not an explicitly declared enum choice')
        rows = [row for row in source.values() if in_scope(row['path'], style['prefixes'])]
        if len(rows) != style['count'] or index_hash(rows) != style['index_sha256']:
            raise ValueError('Source scene inventory differs: ' + style['option'])
        for dependency in style.get('dependencies', []):
            row = source[safe_path(dependency['source']).casefold()]
            if row['sha256'] != dependency['sha256'] or not in_scope(dependency['target'], style['prefixes']):
                raise ValueError('Scene texture companion differs from reviewed evidence')
            rows.append(dict(row, path=safe_path(dependency['target'])))
        for row in rows:
            blob = args.blobs / row['sha256']
            if blob.is_symlink() or blob.stat().st_size != row['size'] or digest(blob) != row['sha256']:
                raise ValueError('Decoded source payload differs: ' + row['path'])
        styles.append((style, rows))
    outfits = []
    for identity in targets:
        for rule in rules.get('outfits', []):
            builds = rule.get('builds', list(targets))
            if not isinstance(builds, list) or not builds or len(set(builds)) != len(builds) or any(build not in targets for build in builds):
                raise ValueError('Outfit target builds differ from reviewed client evidence')
            if identity not in builds:
                continue
            option = next((row for row in definitions['options'] if row['key'] == rule['option']), None)
            if not option or option['type'] != 'enum' or rule['value'] not in option['values']:
                raise ValueError('Outfit must match a declared enum choice')
            rows = outfit_rows(rule, files, files[identity])
            for row in rows:
                verified_blob(args.blobs, row)
            edits = changes(rows, files[identity], targets[identity]['additions'])
            if edits:
                outfits.append((identity, rule, edits))
    music_jobs = []
    if music:
        for identity in targets:
            original = files[identity][music['member'].casefold()]
            raw = verified_blob(args.blobs, original)
            for active in ({'main_style'}, {'plaza_style'}, {'main_style', 'plaza_style'}):
                body = music_payload(raw, music['routes'], active)
                if body != raw:
                    music_jobs.append((identity, original, active, body))
    output = args.output.resolve()
    if any(output.is_relative_to(Path(client['root']).resolve()) for client in clients.values()):
        raise ValueError('Package output must stay outside every original client directory')
    output.mkdir(parents=True, exist_ok=False)
    package = output / definitions['plugin']['id']
    (package / 'plans').mkdir(parents=True)
    (package / 'payload').mkdir()
    for name in ('plugin.toml', 'plugin-ui.toml', 'settings.html', 'README.md', 'README.zh.md'):
        shutil.copyfile(args.definition / name, package / name)
    manifest = 'schema = 1\n'
    report = []
    payloads = set()
    generated = {}
    for identity, target in targets.items():
        for style, rows in styles:
            edits = changes(rows, files[identity], target['additions'])
            if not edits:
                raise ValueError('Reviewed snow style unexpectedly makes no changes')
            plan_name = 'plans/' + identity + '-' + style['option'] + '-' + style['value'] + '.toml'
            plan = 'schema = 1\n'
            for archive in sorted({row['archive'] for row in edits}):
                plan += '\n[[archives]]\npath = ' + json.dumps(archive) + '\n'
                for row in edits:
                    if row['archive'] != archive:
                        continue
                    plan += '\n[[archives.members]]\n'
                    for key, value in dict(path=row['path'], source='payload/' + row['sha256'],
                        sha256=row['sha256'], original_sha256=row['original_sha256']).items():
                        plan += key + ' = ' + json.dumps(value) + '\n'
                    payloads.add(row['sha256'])
            (package / plan_name).write_text(plan, encoding='utf-8', newline='\n')
            manifest += '\n[[plans]]\n' + ''.join(key + ' = ' + json.dumps(value) + '\n' for key, value in
                dict(build=identity, when=style['option'], when_value=style['value'], path=plan_name).items())
            report.append(dict(build=identity, option=style['option'], value=style['value'], edits=len(edits),
                additions=sum(not row['original_sha256'] for row in edits), archives=len({row['archive'] for row in edits})))
        if music:
            for build_id, original, active, body in music_jobs:
                if build_id != identity:
                    continue
                sha = hashlib.sha256(body).hexdigest()
                generated[sha] = body
                payloads.add(sha)
                suffix = 'both' if len(active) == 2 else next(iter(active))
                name = 'plans/' + identity + '-music-' + suffix + '.toml'
                plan = 'schema = 1\n\n[[archives]]\npath = ' + json.dumps(original['archive']) + '\n\n[[archives.members]]\n'
                for key, value in dict(path=original['path'], source='payload/' + sha, sha256=sha, original_sha256=original['sha256']).items():
                    plan += key + ' = ' + json.dumps(value) + '\n'
                (package / name).write_text(plan, encoding='utf-8', newline='\n')
                condition = dict(main_style='snow' if 'main_style' in active else 'original',
                    plaza_style='snow' if 'plaza_style' in active else 'original', **{music['option']: True})
                manifest += '\n[[plans]]\nbuild = ' + json.dumps(identity) + '\npath = ' + json.dumps(name) + '\nwhen_options = { ' + ', '.join(
                    key + ' = ' + json.dumps(value) for key, value in condition.items()) + ' }\n'
                report.append(dict(build=identity, option=music['option'], value=suffix, edits=1, additions=0, archives=1))
    for identity, rule, edits in outfits:
        name = 'plans/' + identity + '-' + rule['option'] + '-' + rule['value'] + '.toml'
        plan = 'schema = 1\n'
        for archive in sorted({row['archive'] for row in edits}):
            plan += '\n[[archives]]\npath = ' + json.dumps(archive) + '\n'
            for row in edits:
                if row['archive'] != archive:
                    continue
                plan += '\n[[archives.members]]\n'
                for key, value in dict(path=row['path'], source='payload/' + row['sha256'], sha256=row['sha256'], original_sha256=row['original_sha256']).items():
                    plan += key + ' = ' + json.dumps(value) + '\n'
                payloads.add(row['sha256'])
        (package / name).write_text(plan, encoding='utf-8', newline='\n')
        manifest += '\n[[plans]]\n' + ''.join(key + ' = ' + json.dumps(value) + '\n' for key, value in
            dict(build=identity, when=rule['option'], when_value=rule['value'], path=name).items())
        report.append(dict(build=identity, option=rule['option'], value=rule['value'], edits=len(edits), additions=sum(not row['original_sha256'] for row in edits), archives=len({row['archive'] for row in edits})))
    for sha in sorted(payloads):
        if sha in generated:
            (package / 'payload' / sha).write_bytes(generated[sha])
        else:
            shutil.copyfile(args.blobs / sha, package / 'payload' / sha)
    (package / 'styles.toml').write_text(manifest, encoding='utf-8', newline='\n')
    archive_path = output / (package.name + '-' + definitions['plugin']['version'] + '.zip')
    with zipfile.ZipFile(archive_path, 'x', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(package.rglob('*')):
            if path.is_file():
                info = zipfile.ZipInfo(path.relative_to(package).as_posix(), (2000, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, path.read_bytes())
    receipt = dict(plans=report, payloads=len(payloads), zip=archive_path.name, zip_sha256=digest(archive_path),
        live_rendering_verified=False)
    (output / 'package-report.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(f'PASS {len(report)} independent scene plans; {len(payloads)} verified payloads; {archive_path}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--inventory', type=Path, required=True)
    parser.add_argument('--blobs', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--rules', type=Path, default=Path(__file__).with_name('scene-style-evidence.toml'))
    parser.add_argument('--definition', type=Path, default=Path(__file__).resolve().parents[1] /
        'games/BubbleFighter/plugins/bubblefighter.fantasy.scene-styles')
    build(parser.parse_args())
