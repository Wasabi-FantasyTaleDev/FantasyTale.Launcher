"""Prepare one declarative model-switching package from verified client member inventories.

Only definitions, this maker and evidence rules enter Git. Decoded game payloads stay
under the caller's ignored output directory and are distributed through the store.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tomllib

from mesh_texture_aperture import cut_apertures


def prepare_apertures(args, rules, clients, output):
    """Rebuild reviewed heads from hash-verified source meshes and texture pixels."""
    needed = [row for row in rules['characters'] if row.get('classic_apertures')]
    if not needed:
        return {}, {}, []
    if not args.aperture_evidence or not args.materializer:
        raise ValueError('Reviewed aperture rules require --aperture-evidence and --materializer')
    import numpy as np
    from PIL import Image
    evidence = json.loads(args.aperture_evidence.read_text(encoding='utf-8'))
    source = clients[evidence['build']]
    if Path(evidence['root']).resolve() != Path(source['root']).resolve():
        raise ValueError('Aperture evidence source root differs from the member inventory')
    inventory = {row['path'].lower(): row for row in source['files']}
    meshes = {row['path'].lower(): row for row in evidence['meshes']}
    textures = {row['path'].lower(): row for row in evidence['textures']}
    requests, selected = {}, []
    for rule in needed:
        if rule['classic_source'] != evidence['build']:
            raise ValueError('Aperture evidence does not describe the configured classic source')
        for path in rule['classic_apertures']:
            own = inventory[path.lower()]
            row = meshes[path.lower()]
            if own['sha256'].lower() != row['source_sha256']:
                raise ValueError('Reviewed head changed: ' + path)
            selected.append((own, row))
            requests[own['sha256'].lower()] = dict(root=source['root'], archive=own['archive'],
                path=own['path'], sha256=own['sha256'].lower(), size=own['size'])
            for reference in row['textures']:
                texture = textures[reference['path'].lower()]
                if reference['sha256'] != texture['sha256']:
                    raise ValueError('Reviewed texture evidence disagrees: ' + reference['path'])
                requests[texture['sha256']] = dict(root=source['root'], archive=texture['provenance']['source_file'],
                    path=texture['path'], sha256=texture['sha256'], size=texture['size'])
    inputs = output / 'aperture-inputs'
    request_file = output / 'aperture-input-requests.json'
    request_file.write_text(json.dumps(list(requests.values())), encoding='utf-8')
    subprocess.run([str(args.materializer), str(request_file), str(inputs)], check=True)
    prepared, blobs, reports = {}, {}, []
    for own, row in selected:
        raw = (inputs / own['sha256'].lower()).read_bytes()
        masks, dependencies = [], []
        for reference in row['textures']:
            texture = textures[reference['path'].lower()]
            payload = (inputs / texture['sha256']).read_bytes()
            with Image.open(inputs / texture['sha256']) as image:
                pixels = np.asarray(image.convert('RGBA'))
            masks.append(np.all(pixels[:, :, :3] == (255, 0, 255), axis=2))
            # A local companion texture wins over each target's shared texture pool.
            # Its exact pixels must agree with the geometry's color-key footprint.
            name = reference['texture'].replace('\\', '/').rsplit('/', 1)[-1]
            if name in ('', '.', '..'):
                raise ValueError('Unsafe material texture name')
            target = own['path'].rsplit('/', 1)[0] + '/' + name
            dependencies.append(dict(path=target, sha256=texture['sha256'], size=len(payload),
                archive=texture['provenance']['source_file'], prepared=True))
            blobs[texture['sha256']] = payload
        data, report = cut_apertures(raw, masks)
        stamp = hashlib.sha256(data).hexdigest()
        if stamp != row['output_sha256']:
            raise ValueError('Aperture output differs from reviewed conversion: ' + own['path'])
        blobs[stamp] = data
        prepared[own['path'].lower()] = (dict(own, sha256=stamp, size=len(data), prepared=True), dependencies)
        reports.append(dict(path=own['path'], source_sha256=own['sha256'].lower(), sha256=stamp, **report))
    return prepared, blobs, reports

def q(value: str) -> str:
    """Quote a TOML basic string using the common JSON escape subset."""
    return json.dumps(value, ensure_ascii=False)


def resolve_mesh_texture(mesh: str, name: str, files: dict) -> dict | None:
    """Resolve a material without guessing another outfit's texture by basename."""
    name = name.replace('\\', '/').rsplit('/', 1)[-1].lower()
    if name in ('', '.', '..'):
        raise ValueError('Invalid mesh texture name')
    folder = mesh.lower().rsplit('/', 1)[0]
    candidates = []
    # cn-324 sub_8D3110 searches registered directories after the model directory.
    while '/' in folder:
        candidates.append(folder + '/' + name)
        folder = folder.rsplit('/', 1)[0]
    candidates.append('models/characters/texture/' + name)
    # Facial selectors bind the character's Default textures to equipped faces.
    character = '/'.join(mesh.lower().split('/')[:3])
    candidates.append(character + '/default/' + name)
    return next((files[path] for path in candidates if path in files), None)


def complete_new_textures(args, rules, clients, package, report):
    """Pair changed meshes with exact source textures in their own outfit archive."""
    from mesh_texture_aperture import parse_mesh
    if not args.texture_inventory or not args.materializer:
        raise ValueError('New model texture completion requires --texture-inventory and --materializer')
    textures = json.loads(args.texture_inventory.read_text(encoding='utf-8'))
    shared = {}
    for client in textures:
        identity = client['build']
        if Path(client['root']).resolve() != Path(clients[identity]['root']).resolve():
            raise ValueError('Texture inventory root differs from the member inventory')
        entries = {row['path'].lower(): row for row in client['files']}
        if len(entries) != len(client['files']):
            raise ValueError('Duplicate shared texture paths')
        shared[identity] = entries
    source_id = rules['new_source']
    source = clients[source_id]
    source_files = dict(shared[source_id])
    source_files.update((row['path'].lower(), row) for row in source['files'])
    requests, cache, coverage = {}, {}, []
    for summary in report['plans']:
        if summary['generation'] != 'new':
            continue
        identity = summary['build']
        path = package / f"plans/{identity}-{summary['character'].lower()}-new.toml"
        plan = tomllib.loads(path.read_text(encoding='utf-8'))
        originals = {row['path'].lower(): row for row in clients[identity]['files']}
        effective = dict(shared[identity])
        effective.update(originals)
        pending = {}
        for archive in plan['archives']:
            for member in archive['members']:
                key = member['path'].lower()
                pending[key] = member
                effective[key] = member
        meshes = [(archive, member) for archive in plan['archives'] for member in archive['members']
            if member['path'].lower().endswith('.msh')]
        for archive, member in meshes:
            stamp = member['sha256']
            if stamp not in cache:
                cache[stamp] = parse_mesh((package / member['source']).read_bytes()).textures
            for name in cache[stamp]:
                if not name:
                    continue
                texture = resolve_mesh_texture(member['path'], name, source_files)
                if texture is None:
                    raise ValueError('Replacement mesh has no verified source texture: ' + member['path'] + ': ' + name)
                current = resolve_mesh_texture(member['path'], name, effective)
                if current and current['sha256'].lower() == texture['sha256'].lower():
                    continue
                destination = member['path'].rsplit('/', 1)[0] + '/' + name.replace('\\', '/').rsplit('/', 1)[-1]
                key = destination.lower()
                digest = texture['sha256'].lower()
                if key in pending:
                    raise ValueError('Conflicting local texture replacement: ' + destination)
                original = originals.get(key)
                edit = dict(path=original['path'] if original else destination, source='payload/' + digest,
                    sha256=digest, original_sha256=original['sha256'].lower() if original else '')
                archive['members'].append(edit)
                effective[key] = edit
                pending[key] = edit
                if not (package / 'payload' / digest).exists():
                    requests.setdefault(digest, dict(root=source['root'], archive=texture['archive'],
                        path=texture['path'], sha256=digest, size=texture['size']))
                coverage.append(dict(build=identity, character=summary['character'], mesh=member['path'],
                    path=edit['path'], sha256=digest))
        text = 'schema = 1\n'
        for archive in plan['archives']:
            text += f"\n[[archives]]\npath = {q(archive['path'])}\n"
            for member in archive['members']:
                text += '\n[[archives.members]]\n' + ''.join(f'{key} = {q(value)}\n' for key, value in member.items())
        path.write_text(text, encoding='utf-8')
        summary['members'] = sum(len(a['members']) for a in plan['archives'])
    request_path = args.output / 'texture-payload-requests.json'
    request_path.write_text(json.dumps(list(requests.values())), encoding='utf-8')
    if requests:
        subprocess.run([str(args.materializer), str(request_path), str(package / 'payload')], check=True)
    report['texture_companions'] = coverage


def build(args: argparse.Namespace) -> None:
    """Emit content-addressed member payloads and per-target member edits, not whole foreign packs."""
    rules = tomllib.loads(args.rules.read_text(encoding="utf-8"))
    if rules["schema"] != 1:
        raise ValueError("Unsupported character model rules schema")
    characters = {row["key"]: row for row in rules["characters"]}
    generations = {row["build"]: set(row["new"]) for row in rules["native"]}
    inventory = json.loads(args.inventory.read_text(encoding="utf-8"))
    clients = {client["build"]: client for client in inventory}
    boundaries = json.loads(args.boundaries.read_text(encoding="utf-8"))
    allowed: dict[str, set[str]] = {}
    for row in boundaries:
        allowed.setdefault(row['character'], set()).update(path.lower() for path in row['paths'])
    # This item changed after several unrelated remakes and is not a body dependency.
    for paths in allowed.values():
        paths.difference_update(path for path in list(paths) if "/100700/100729/" in path)
    # Effect emitters removed independently of the body remake are not model dependencies.
    allowed["Bazzi"] = {path for path in allowed.get("Bazzi", set()) if not path.endswith(".evn")}
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    package = output / "bubblefighter.fantasy.character-models"
    shutil.copytree(args.definition, package)
    (package / "plans").mkdir()
    prepared, blobs, aperture_reports = prepare_apertures(args, rules, clients, output)
    requests: dict[str, dict] = {}
    catalog = "schema = 1\n"
    report: dict = {"plans": [], "uncovered": [], "payloads": 0, 'apertures': aperture_reports}
    for character, row in characters.items():
        catalog += f"\n[[characters]]\nkey = {q('bubblefighter.' + character.lower())}\n[characters.name]\n"
        catalog += "".join(f"{q(language)} = {q(label)}\n" for language, label in row["name"].items())

    for identity, target in clients.items():
        if identity not in generations:
            raise ValueError("Missing native-generation evidence for " + identity)
        target_files = {f["path"].lower(): f for f in target["files"]}
        for character, rule in characters.items():
            prefix = f"models/characters/{character.lower()}/"
            own = {path: entry for path, entry in target_files.items() if path.startswith(prefix)}
            default = {path: entry for path, entry in own.items() if path.startswith(prefix + "default/")}
            if not default:
                continue
            native = "new" if character in generations[identity] else "classic"
            alternative = "new" if native == "classic" else "classic"
            source_id = rules["new_source"] if alternative == "new" else rule["classic_source"]
            source = clients[source_id]
            source_files = {f["path"].lower(): f for f in source["files"] if f["path"].lower().startswith(prefix)}
            groups: dict[str, list] = {}
            aperture_mode = alternative == 'classic' and identity in rule.get('classic_aperture_builds', [])
            animations = (alternative == 'new' and rules.get('new_animations', False) or
                alternative == 'classic' and rule.get('classic_animations', False))
            # cn-324 sub_4E78C0 resolves each outfit slot in the same item folder.
            # A remade BodyD can split its legs into a newly introduced Legs slot.
            outfit_owners = {path.rsplit('/', 1)[0]: own[path]['archive']
                for path, replacement in source_files.items()
                if path in own and path in allowed.get(character, set()) and path.endswith('.msh')
                and not path.startswith(prefix + 'default/')
                and replacement['sha256'].lower() != own[path]['sha256'].lower()}

            def add(path: str, original: dict | None, replacement: dict, owner_hint: str | None = None) -> None:
                stamp = replacement["sha256"].lower()
                if original is not None and stamp == original["sha256"].lower():
                    return
                if original is None:
                    # Added members stay in their existing Default or outfit archive.
                    owner = owner_hint or next(iter(default.values()))["archive"]
                else:
                    owner = original["archive"]
                groups.setdefault(owner, []).append({"path": original["path"] if original else replacement["path"],
                    "source": "payload/" + stamp, "sha256": stamp,
                    "original_sha256": original["sha256"].lower() if original else ""})
                if not replacement.get('prepared'):
                    requests.setdefault(stamp, {"root": source["root"], "archive": replacement["archive"],
                        "path": replacement["path"], "sha256": stamp, "size": replacement["size"]})

            for path, replacement in source_files.items():
                if aperture_mode and path in prepared and (path in own or path.startswith(prefix + 'default/')):
                    replacement, dependencies = prepared[path]
                    add(path, own.get(path), replacement)
                    owner = own[path]['archive'] if path in own else next(iter(default.values()))['archive']
                    for dependency in dependencies:
                        dest = dependency['path'].lower()
                        # The Default texture is already emitted by the ordinary Default loop.
                        if dest.startswith(prefix + 'default/'):
                            continue
                        add(dest, own.get(dest), dependency, owner)
                    continue
                if path.startswith(prefix + "default/"):
                    add(path, own.get(path), replacement)
                elif path in own and (path in allowed.get(character, set()) or
                        animations and path.endswith(('.mtn', '.face'))):
                    add(path, own[path], replacement)
                elif path not in own and alternative == 'new' and (
                        folder := path.rsplit('/', 1)[0]) in outfit_owners and (
                        path.endswith(('.msh', '.png', '.dds', '.tga', '.bmp'))):
                    add(path, None, replacement, outfit_owners[folder])
            if alternative == 'classic' and rule.get('classic_integrated_legs', False):
                for path, original in own.items():
                    if not path.endswith('/legs.msh') or path in source_files:
                        continue
                    body = path.rsplit('/', 1)[0] + '/bodyd.msh'
                    if body not in source_files or body not in own:
                        continue
                    if not body.startswith(prefix + 'default/') and body not in allowed.get(character, set()):
                        continue
                    if source_files[body]['sha256'].lower() == own[body]['sha256'].lower():
                        continue
                    groups.setdefault(original['archive'], []).append(dict(path=original['path'],
                        original_sha256=original['sha256'].lower(), remove=True))
            # Preserve destination-only resources: later outfits and native selectors can
            # still reference these textures. We never invent a classic outfit substitute.
            uncovered = [path for path in own if path not in source_files and not path.startswith(prefix + "default/")]
            if uncovered:
                report["uncovered"].append({"build": identity, "character": character, "count": len(uncovered), "paths": uncovered})
            plan = f"plans/{identity}-{character.lower()}-{alternative}.toml"
            text = "schema = 1\n"
            for archive, members in sorted(groups.items()):
                text += f"\n[[archives]]\npath = {q(archive)}\n"
                for member in members:
                    text += "\n[[archives.members]]\n" + "".join(f"{key} = {q(value)}\n" for key, value in member.items())
            (package / plan).write_text(text, encoding="utf-8")
            catalog += (f"\n[[native]]\nbuild = {q(identity)}\nkey = {q('bubblefighter.' + character.lower())}\n"
                f"generation = {q(native)}\n{alternative} = {q(plan)}\n")
            report["plans"].append({"build": identity, "character": character, "source": source_id, "generation": alternative,
                "archives": len(groups), "members": sum(len(rows) for rows in groups.values())})
    (package / "models.toml").write_text(catalog, encoding="utf-8")
    (output / "payload-requests.json").write_text(json.dumps(list(requests.values()), ensure_ascii=False), encoding="utf-8")
    required = {member['sha256'] for path in (package / 'plans').glob('*.toml')
        for archive in tomllib.loads(path.read_text(encoding='utf-8')).get('archives', [])
        for member in archive['members'] if not member.get('remove')}
    report["payloads"] = len(required)
    (output / "plan-report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    if args.materializer:
        subprocess.run([str(args.materializer), str(output / "payload-requests.json"), str(package / "payload")], check=True)
        for stamp in required & blobs.keys():
            (package / 'payload' / stamp).write_bytes(blobs[stamp])
        if rules.get('new_model_textures', False):
            complete_new_textures(args, rules, clients, package, report)
            required = {member['sha256'] for path in (package / 'plans').glob('*.toml')
                for archive in tomllib.loads(path.read_text(encoding='utf-8')).get('archives', [])
                for member in archive['members'] if not member.get('remove')}
            report['payloads'] = len(required)
            (output / 'plan-report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
        actual = {path.name for path in (package / 'payload').iterdir()}
        if actual != required:
            raise ValueError('Materialized payload set differs from plan references')
    print(f"Prepared {len(report['plans'])} plans; {len(required)} deduplicated payloads. No client files were modified.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rules", type=Path, default=Path(__file__).with_name("character-models.toml"))
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--boundaries", type=Path, required=True)
    parser.add_argument("--definition", type=Path, default=Path(__file__).resolve().parents[1] / "games/BubbleFighter/plugins/bubblefighter.fantasy.character-models")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--materializer", type=Path)
    parser.add_argument("--aperture-evidence", type=Path)
    parser.add_argument("--texture-inventory", type=Path)
    build(parser.parse_args())
