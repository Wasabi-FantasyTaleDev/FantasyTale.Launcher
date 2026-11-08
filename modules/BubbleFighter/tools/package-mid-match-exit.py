"""Stage the reviewed native exit routes with the early-client companion adapter."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tomllib
import zipfile


def package(destination: Path, library: Path, activation_test: Path) -> None:
    """Build a new, importable package without modifying an existing output folder."""
    source = Path(__file__).resolve().parents[1] / 'native/mid-match-exit'
    text = (source / 'plugin.preview.toml').read_text(encoding='utf-8')
    text += '\n' + (source / 'native-patches.toml').read_text(encoding='utf-8')
    manifest = tomllib.loads(text)
    clients = tomllib.loads((source / 'exit.toml').read_text(encoding='utf-8'))['clients']
    builds = set(manifest['compatibility']['builds'])
    if builds != set(clients) or any(row['route'] not in ('native', 'companion') for row in clients.values()):
        raise ValueError('Exit routes and published compatibility must agree')
    if manifest.get('options'):
        raise ValueError('Match exit must not require a second opt-in after the plugin is enabled')
    ids = set()
    patched = set()
    for row in manifest['patches']:
        if row['id'] in ids or 'when' in row or row['timing'] != 'unpacked':
            raise ValueError('Invalid or duplicate exit patch')
        ids.add(row['id'])
        before, after = bytes.fromhex(row['expect']), bytes.fromhex(row['write'])
        if not before or len(before) != len(after):
            raise ValueError('Exit patches must retain their verified instruction width')
        for build in row['builds']:
            if build not in clients:
                raise ValueError('A patch targets an undeclared client')
            if clients[build]['route'] == 'companion':
                raise ValueError('Passive companion routes must never patch client memory')
            if clients[build]['route'] == 'native' and ('rva' not in row['locate'] or after != b'\x90' * len(before)):
                raise ValueError('A native route must retain its original leave-session code')
            patched.add(build)
    if patched != {build for build in builds if clients[build]['route'] == 'native'}:
        raise ValueError('Only native routes may declare verified client patches')
    if not library.is_file() or library.is_symlink():
        raise ValueError('Build the native exit library before packaging')
    if not activation_test.is_file() or activation_test.is_symlink():
        raise ValueError('Build the native activation test before packaging')
    destination.mkdir(parents=True, exist_ok=False)
    (destination / 'plugin.toml').write_text(text, encoding='utf-8')
    shutil.copyfile(source / 'exit.toml', destination / 'exit.toml')
    shutil.copyfile(library, destination / 'mid_match_exit.dll')
    checked = subprocess.run([str(activation_test.resolve()), str(destination.resolve())],
                             capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=60)
    activation_log = destination.parent / (destination.name + '-activation.txt')
    activation_log.write_text(checked.stdout + checked.stderr, encoding='utf-8')
    if checked.returncode:
        raise ValueError(f'Packaged DLL activation failed; see {activation_log}')
    archive = destination.parent / (destination.name + '.zip')
    with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for path in sorted(destination.iterdir()):
            info = zipfile.ZipInfo(path.name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            output.writestr(info, path.read_bytes())
    receipt = {'builds': sorted(builds), 'patches': len(ids), 'archive': archive.name,
               'activation_log': activation_log.name,
               'sha256': hashlib.sha256(archive.read_bytes()).hexdigest()}
    print(json.dumps(receipt, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', required=True, type=Path)
    parser.add_argument('--library', required=True, type=Path)
    parser.add_argument('--activation-test', required=True, type=Path)
    options = parser.parse_args()
    package(options.destination, options.library, options.activation_test)
