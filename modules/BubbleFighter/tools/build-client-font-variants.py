"""Generate verified private font variants from pinned client-owned sources.

Requires fonttools. Output assets remain outside version control; report.json
contains the file declarations to review against the published plugin manifest.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import tomllib


spec = importlib.util.spec_from_file_location('merge_fonts', Path(__file__).with_name('merge-client-fonts.py'))
merge_fonts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(merge_fonts)


def verified_source(folder, row):
    """Reject substituted source fonts before generating any variant."""
    relative = Path(row['file'])
    if relative.name != row['file']:
        raise ValueError('Font sources must be filenames under the supplied Data directory')
    source = folder / relative
    with source.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    if digest != row['sha256']:
        raise ValueError(f'Original font hash differs: {source.name}')
    return source


def build(definition, base_folder, korean_folder, output):
    """Build all declared variants, preserving primary Korean glyph priority."""
    config = tomllib.loads(definition.read_text(encoding='utf-8'))
    if config['schema'] != 1:
        raise ValueError('Unsupported font variant schema')
    if output.exists():
        raise ValueError('Use a new output directory for verified font variants')
    base = verified_source(base_folder, config['base'])
    rows = config['variants']
    names = [row['file'] for row in rows]
    if len(names) != len(set(names)):
        raise ValueError('Font variant sources must be distinct')
    sources = {row['file']: verified_source(korean_folder, row) for row in rows}
    for row in rows:
        if row.get('fallback') and row['fallback'] not in sources:
            raise ValueError('Fallback must reference a verified font variant source')
    reports, files = [], []
    for row in rows:
        name = row['file']
        relative = 'fonts/fused-' + Path(name).stem + '.ttf'
        report = merge_fonts.build(base, sources[name], output / relative, sources.get(row.get('fallback')))
        reports.append(dict(source=name, source_sha256=row['sha256'], fallback=row.get('fallback'), **report))
        file = dict(source=relative, target='Data/' + name, size=report['size'], sha256=report['sha256'])
        if 'builds' in row:
            file['builds'] = row['builds']
        files.append(file)
        print(f'Verified {name}: {report["primary_korean_codepoints"]} primary, '
              f'{report["fallback_korean_codepoints"]} fallback Korean codepoints', flush=True)
    if len({row['sha256'] for row in files}) != len(files):
        raise ValueError('Distinct original fonts generated identical variant files')
    result = dict(base=config['base'], variants=reports, files=files)
    (output / 'report.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--definition', type=Path, default=Path(__file__).with_name('font-variants.toml'))
    parser.add_argument('--base-data', type=Path, required=True)
    parser.add_argument('--korean-data', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    build(args.definition, args.base_data, args.korean_data, args.output)
