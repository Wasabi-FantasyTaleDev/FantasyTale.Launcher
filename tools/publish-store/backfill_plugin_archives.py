"""Add importable ZIPs to published plugins without replacing their manifests or payloads."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tomllib
import zipfile

import requests

from publish_plugins import create_archive, import_archive_path
from publish_store import atomic, safe_path, stamp, reject_links, quote
from upload_alist import AList, retry_network


def verify(path: Path, size: int, digest: str) -> None:
    """Verify complete downloaded bytes against their published immutable identity."""
    reject_links(path)
    if type(size) is not int or size < 0 or not isinstance(digest, str) or not re.fullmatch('[0-9a-f]{64}', digest):
        raise ValueError("Invalid published file identity")
    with path.open('rb') as stream:
        if path.stat().st_size != size or hashlib.file_digest(stream, 'sha256').hexdigest() != digest:
            raise ValueError("Published plugin bytes failed verification")


def archive_offer(package: dict, raw: bytes, output: Path, fetch) -> dict:
    """Reconstruct exactly one published version using only hash-pinned source objects."""
    if hashlib.sha256(raw).hexdigest() != package['manifest_sha256']:
        raise ValueError("Published plugin manifest digest mismatch")
    document = tomllib.loads(raw.decode('utf-8-sig'))
    release = document['release']
    if (document.get('schema') != 1 or package['kind'] != 'plugin'
            or any(release.get(key) != package.get(key) for key in ('id', 'game', 'version', 'builds'))):
        raise ValueError("Published plugin manifest identity mismatch")
    files = document['files']
    if not files or sum(row['size'] for row in files) != package['size']:
        raise ValueError("Published plugin size mismatch")
    target_path = import_archive_path(package['game'], package['id'], package['version'])
    target = output / target_path
    reject_links(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    mode = release.get('format', 'files')
    if mode == 'zip':
        if len(files) != 1:
            raise ValueError("A ZIP plugin must have one transport file")
        row = files[0]
        source = fetch(row)
        verify(source, row['size'], row['sha256'])
        shutil.copyfile(source, target)
        size, digest = row['size'], row['sha256']
    elif mode == 'files':
        tree, names = [], set()
        for row in files:
            relative = row['path']
            safe_path(relative, literal=True)
            if relative.casefold() in names:
                raise ValueError("Plugin manifest has conflicting member names")
            names.add(relative.casefold())
            source = fetch(row)
            verify(source, row['size'], row['sha256'])
            tree.append((relative, source, stamp(source)))
        if 'plugin.toml' not in names:
            raise ValueError("Plugin manifest has no root plugin.toml")
        size, digest = create_archive(sorted(tree), target)
    else:
        raise ValueError("Unsupported plugin transport format")
    with zipfile.ZipFile(target) as archive:
        names = set()
        for member in archive.infolist():
            if member.is_dir():
                safe_path(member.filename.rstrip('/'), literal=True)
                continue
            safe_path(member.filename, literal=True)
            if member.filename.casefold() in names or (member.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("Plugin ZIP has conflicting or linked members")
            names.add(member.filename.casefold())
        if archive.testzip() is not None:
            raise ValueError("Plugin ZIP failed CRC verification")
        plugin = tomllib.loads(archive.read('plugin.toml').decode('utf-8-sig'))['plugin']
        if any(plugin[key] != package[key] for key in ('id', 'game', 'version')):
            raise ValueError("Plugin ZIP root identity mismatch")
    return dict(source_path=target_path, size=size, sha256=digest)


def add_offers(previous: bytes, offers: dict[tuple[str, str], dict]) -> bytes:
    """Add ZIP transport metadata while retaining every existing catalog field and release identity."""
    before = tomllib.loads(previous.decode('utf-8-sig'))
    blocks = re.split(r'(?m)^(?=\[\[(?:packages|profiles|servers|directory_sources|github_releases)\]\][ \t]*\r?$)', previous.decode('utf-8-sig'))
    result = [blocks[0]]
    applied = set()
    for block in blocks[1:]:
        if block.startswith('[[packages]]'):
            row = tomllib.loads(block)['packages'][0]
            key = row['id'], row.get('version', '')
            if key in offers:
                offer = offers[key]
                if row.get('kind') != 'plugin':
                    raise ValueError("Only plugin packages may offer import ZIPs")
                if 'plugin_archive' in row and row['plugin_archive'] != offer:
                    raise ValueError("Published plugin ZIP identity differs")
                if 'plugin_archive' not in row:
                    inline = ', '.join(f'{name} = {value if type(value) is int else quote(value)}' for name, value in offer.items())
                    block = block.replace('[[packages]]', '[[packages]]\nplugin_archive = { ' + inline + ' }', 1)
                applied.add(key)
        result.append(block)
    if applied != set(offers):
        raise ValueError("ZIP offers must match existing published packages")
    content = ''.join(result).encode('utf-8')
    after = tomllib.loads(content.decode())
    for row in after.get('packages', []):
        key = row['id'], row.get('version', '')
        original = next(p for p in before['packages'] if (p['id'], p.get('version', '')) == key)
        if key in offers and 'plugin_archive' not in original:
            row.pop('plugin_archive')
    if after != before:
        raise ValueError("ZIP registration would change existing catalog metadata")
    return content


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--publish', action='store_true', help='Upload verified ZIPs and commit additive catalog metadata last.')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    alist = AList(args.base_url, os.environ['FT_ALIST_USERNAME'], os.environ['FT_ALIST_PASSWORD'], 1800)
    catalog = 'FantasyTale/source.toml'

    def read(path):
        safe_path(path)
        metadata = retry_network(lambda: alist.metadata(path))
        if metadata is None:
            raise ValueError("A published plugin object is missing")
        with alist.open_download(path, metadata, timeout=(30, 120)) as response:
            return response.content

    previous = retry_network(lambda: read(catalog))
    baseline = hashlib.sha256(previous).hexdigest()
    atomic(args.output / 'baseline.toml', previous)
    cache = args.output / 'objects'
    cache.mkdir(exist_ok=True)

    def fetch(row):
        safe_path(row['source_path'])
        digest = row['sha256']
        if not re.fullmatch('[0-9a-f]{64}', digest):
            raise ValueError("Invalid published object digest")
        path = cache / digest
        if path.exists():
            verify(path, row['size'], digest)
            return path
        def transfer():
            metadata = alist.metadata(row['source_path'])
            if metadata is None:
                raise ValueError("A published plugin object is missing")
            pending = path.with_suffix('.pending')
            try:
                with alist.open_download(row['source_path'], metadata, stream=True, timeout=(30, 120)) as response, pending.open('wb') as stream:
                    for chunk in response.iter_content(1024 * 1024):
                        stream.write(chunk)
                        if stream.tell() > row['size']:
                            raise ValueError("Published plugin object exceeds its pinned size")
                verify(pending, row['size'], digest)
                pending.replace(path)
            finally:
                pending.unlink(missing_ok=True)
        retry_network(transfer)
        return path

    offers = {}
    packages = [p for p in tomllib.loads(previous.decode('utf-8-sig')).get('packages', []) if p['kind'] == 'plugin']
    for index, package in enumerate(packages, 1):
        raw = retry_network(lambda: read(package['manifest']))
        offer = archive_offer(package, raw, args.output, fetch)
        offers[package['id'], package['version']] = offer
        print(f"Prepared plugin ZIP {index}/{len(packages)}: {package['id']} {package['version']}", flush=True)
        if args.publish:
            path = offer['source_path']
            def upload():
                actual = alist.remote_hash(path)
                if actual is not None and actual != offer['sha256']:
                    raise ValueError("Remote immutable plugin ZIP conflict")
                if actual is None:
                    alist.put(path, args.output / path)
                if alist.remote_hash(path, expected_size=offer['size']) != offer['sha256']:
                    raise ValueError("Public plugin ZIP verification failed")
            retry_network(upload)
    updated = add_offers(previous, offers)
    target = args.output / 'source.toml'
    atomic(target, updated)
    expected = hashlib.sha256(updated).hexdigest()
    if args.publish:
        if retry_network(lambda: alist.remote_hash(catalog)) != baseline:
            raise ValueError("Catalog changed; prepare ZIP registration again")
        retry_network(lambda: alist.put(catalog, target))
        if retry_network(lambda: alist.remote_hash(catalog)) != expected:
            raise ValueError("Published ZIP catalog verification failed")
    atomic(args.output / 'receipt.json', json.dumps(dict(published=args.publish, catalog_sha256=expected,
        plugin_versions=len(offers), archives=list(offers.values())), indent=2).encode())
    print(f"Plugin ZIPs verified: {len(offers)}; catalog published: {args.publish}", flush=True)


if __name__ == '__main__':
    try:
        main()
    except requests.RequestException as error:
        print(f"Plugin ZIP network failure ({type(error).__name__})", flush=True)
        raise SystemExit(1) from None
