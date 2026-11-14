"""Append immutable plugin packages to a public AList catalog without replacing its other entries."""
from __future__ import annotations
import argparse
import hashlib
import os
import re
import zipfile
import shutil
from pathlib import Path
import tomllib
import requests
from publish_store import atomic, file_tree, identifier, quote, server_version, stamp as file_stamp, reject_links, safe_path
from upload_alist import AList, retry_network

# Research and retired packages cannot be republished by catalog composition.
# The accepted Auto Dash preview is retired into the generic package.
UNPUBLISHED_PLUGIN_IDS = frozenset({
    "bubblefighter.fantasy.forward-jump",
    "bubblefighter.fantasy.forward-jump-cn2011-preview",
    "bubblefighter.fantasy.auto-dash-cn2011-preview",
    "bubblefighter.fantasy.legacy-security-compatibility",
})
SUPERSEDED_PLUGINS = dict.fromkeys((
    "bubblefighter.fantasy.lxmarid-remaster",
    "bubblefighter.fantasy.dao-classic-model",
    "bubblefighter.fantasy.dizni-classic-model",
), "bubblefighter.fantasy.character-models")

CATALOG_ARRAYS = "packages|profiles|servers|directory_sources|github_releases|deprecated_plugins"


def deprecated_plugin_ids():
    """Read publisher retirement policy as data, independently of research-package safeguards."""
    policy = Path(__file__).with_name("plugin-lifecycle.toml")
    return {identifier(row["id"]) for row in tomllib.loads(policy.read_text(encoding="utf-8")).get("deprecated_plugins", [])}


def import_archive_path(game: str, identity: str, version: str) -> str:
    """Keep a human-downloadable ZIP name stable for one immutable plugin release."""
    return f"FantasyTale/plugins/{identifier(game)}/{identifier(identity)}-{server_version(version)}.zip"


def create_archive(tree, target: Path) -> tuple[int, str]:
    """Stream a deterministic import ZIP with plugin.toml at its root and refuse changing inputs."""
    reject_links(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    pending = target.with_suffix('.pending')
    reject_links(pending)
    try:
        with zipfile.ZipFile(pending, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zipped:
            for relative, source, expected in tree:
                safe_path(relative, literal=True)
                reject_links(source)
                if file_stamp(source) != expected:
                    raise ValueError("Package changed during publication")
                entry = zipfile.ZipInfo(relative, date_time=(1980, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                entry.external_attr = 0o100644 << 16
                with source.open('rb') as input_file, zipped.open(entry, 'w', force_zip64=True) as member:
                    shutil.copyfileobj(input_file, member, length=1024 * 1024)
                    if input_file.tell() != expected[0] or file_stamp(source) != expected:
                        raise ValueError("Package changed during publication")
        with pending.open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        size = pending.stat().st_size
        pending.replace(target)
        return size, digest
    finally:
        pending.unlink(missing_ok=True)


def package(folder: Path, output: Path, *, uploader: str = "", archive: bool = False) -> tuple[str, dict[str, Path]]:
    """Generate a release from a clean package folder; callers supply only distributable files."""
    plugin = tomllib.loads((folder / "plugin.toml").read_text(encoding="utf-8"))
    p = plugin["plugin"]
    authors = p.get("authors", [])
    if not isinstance(authors, list) or len(authors) > 32 or any(not isinstance(author, str) or not author.strip() or len(author) > 128 or any(ord(c) < 32 or ord(c) == 127 for c in author) for author in authors):
        raise ValueError("Invalid bounded plugin authors")
    if not isinstance(uploader, str) or len(uploader) > 128 or uploader and not uploader.strip() or any(ord(c) < 32 or ord(c) == 127 for c in uploader):
        raise ValueError("Invalid optional uploader name")
    identity, game, version = identifier(p["id"]), identifier(p["game"]), server_version(p["version"])
    aliases = p.get("aliases", [])
    if not isinstance(aliases, list) or any(not isinstance(alias, str) for alias in aliases):
        raise ValueError("Plugin aliases must be former identifiers")
    if len(set(aliases)) != len(aliases) or identity in aliases:
        raise ValueError("Plugin aliases must be distinct and exclude the current identifier")
    for alias in aliases:
        identifier(alias)
    requires = p.get("requires", [])
    if not isinstance(requires, list) or any(not isinstance(item, str) for item in requires):
        raise ValueError("Plugin requirements must be package identifiers")
    for item in requires:
        identifier(item.split("@", 1)[0])
    if plugin.get("schema") not in (1, 2) or plugin.get("schema") == 1 and "plugin_policy" in plugin:
        raise ValueError("plugin.toml schema must be 1, or 2 for [[plugin_policy]] rules")
    policy = plugin.get("plugin_policy", [])
    if not isinstance(policy, list) or any(not isinstance(rule, dict) or rule.get("type") not in ("require", "recommend", "conflict")
                                           or not isinstance(rule.get("plugin"), str) for rule in policy):
        raise ValueError("Plugin policy rules need a plugin identifier and a require, recommend or conflict type")
    for rule in policy:
        identifier(rule["plugin"])
    if p.get("kind") == "assets":
        # Generated game art is staged into the package before publication; an empty data folder is a packaging mistake.
        root = plugin.get("assets", {}).get("root")
        data = (folder / root) if isinstance(root, str) and root else None
        if data is None or not data.is_dir() or not any(path.is_file() for path in data.rglob("*")):
            raise ValueError(f"Assets package {identity} has no files under its data folder; stage the generated data first")
    requirements = tomllib.loads(Path(__file__).with_name("package-requirements.toml").read_text(encoding="utf-8"))
    policy = requirements.get("plugins", {}).get(identity, {})
    for relative in policy.get("required", []):
        if not (folder / relative).is_file():
            raise ValueError(f"Plugin {identity} is missing required package asset: {relative}")
    for relative in policy.get("deployed", []):
        declarations = [row for row in plugin.get("files", []) if row["source"] == relative]
        if not declarations:
            raise ValueError(f"Plugin {identity} does not deploy required package asset: {relative}")
        payload = folder / relative
        reject_links(payload)
        with payload.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if any(row["size"] != payload.stat().st_size or row["sha256"] != digest for row in declarations):
            raise ValueError(f"Plugin {identity} has a mismatched deployment payload: {relative}")
    if p.get("kind") == "native":
        library = plugin.get("native", {}).get("library")
        if not library or not (folder / library).is_file():
            raise ValueError(f"Plugin {identity} is missing its native library")
    if identity in UNPUBLISHED_PLUGIN_IDS or identity in deprecated_plugin_ids():
        raise ValueError(f"Plugin {identity} is retired or research-only and is not publishable")
    compatibility = plugin["compatibility"]
    builds = compatibility["builds"]
    if not builds:
        raise ValueError("Plugins require explicit supported client builds")
    if builds == ["*"]:
        if compatibility.get("exclude"):
            raise ValueError("Wildcard publication cannot discard excluded client builds")
        builds = []
    else:
        builds = [identifier(build) for build in builds]
    tree = file_tree(folder, [])
    release = f"schema = 1\n[release]\nid = {quote(identity)}\ngame = {quote(game)}\nversion = {quote(version)}\nbuilds = [{', '.join(quote(b) for b in builds)}]\n"
    objects: dict[str, Path] = {}
    size = 0
    for relative, source, stamp in tree:
        if relative.startswith(".") or source.suffix.lower() in (".pdb", ".cpp", ".h", ".lib", ".exp", ".log", ".dmp"):
            raise ValueError("Package contains development or private files")
        if file_stamp(source) != stamp:
            raise ValueError("Package changed during publication")
    zip_path = import_archive_path(game, identity, version)
    zip_target = output / zip_path
    zip_size, zip_digest = create_archive(tree, zip_target)
    objects[zip_path] = zip_target
    if archive:
        # One immutable archive avoids thousands of tiny storage requests for member-based plugins.
        size, digest = zip_size, zip_digest
        # The public import ZIP is already immutable by plugin identity/version.
        # Reuse it for installation rather than uploading the same archive twice.
        path = zip_path
        release += 'format = "zip"\n'
        release += f'\n[[files]]\npath = {quote(identity + "-" + version + ".zip")}\nsource_path = {quote(path)}\nsize = {size}\nsha256 = {quote(digest)}\n'
    for relative, source, stamp in ([] if archive else tree):
        if file_stamp(source) != stamp:
            raise ValueError("Package changed during publication")
        data = source.read_bytes()
        if len(data) != stamp[0] or file_stamp(source) != stamp:
            raise ValueError("Package changed during publication")
        digest = hashlib.sha256(data).hexdigest()
        path = "FantasyTale/files/" + digest
        target = output / path
        target.parent.mkdir(parents=True, exist_ok=True)
        atomic(target, data)
        objects[path] = target
        size += len(data)
        release += f"\n[[files]]\npath = {quote(relative)}\nsource_path = {quote(path)}\nsize = {len(data)}\nsha256 = {quote(digest)}\n"
    raw = release.encode("utf-8")
    digest = hashlib.sha256(raw).hexdigest()
    path = f"FantasyTale/snapshots/{digest}.toml"
    target = output / path
    target.parent.mkdir(parents=True, exist_ok=True)
    atomic(target, raw)
    objects[path] = target
    localized = lambda texts: "{ " + ", ".join(quote(k) + " = " + quote(v) for k, v in texts.items()) + " }"
    row = f"\n[[packages]]\nid = {quote(identity)}\nkind = \"plugin\"\ngame = {quote(game)}\nbuilds = [{', '.join(quote(b) for b in builds)}]\nversion = {quote(version)}\n"
    row += f"name = {localized(p['name'])}\ndescription = {localized(p.get('description', {'en': ''}))}\npublisher = \"Fantasy Tale Studio\"\nsize = {size}\nmanifest = {quote(path)}\nmanifest_sha256 = {quote(digest)}\n"
    row += f"authors = [{', '.join(quote(author) for author in authors)}]\n"
    if p.get("minimum_launcher_version"):
        row += f"minimum_launcher_version = {quote(server_version(p['minimum_launcher_version']))}\n"
    if aliases or requires or policy:
        # Update discovery needs former identities, and installation needs requirements and policy, before downloading the package itself.
        row += f"plugin_definition = {quote((folder / 'plugin.toml').read_text(encoding='utf-8'))}\n"
    if uploader:
        row += f"uploader = {quote(uploader)}\n"
    row += f'plugin_archive = {{ source_path = {quote(zip_path)}, size = {zip_size}, sha256 = {quote(zip_digest)} }}\n'
    return row, objects


def group_catalog_tables(document: bytes) -> bytes:
    """Keep each root table array contiguous while preserving its exact entries and inline definitions."""
    text = document.decode("utf-8")
    blocks = re.split(r'(?m)^(?=\[\[(?:' + CATALOG_ARRAYS + r')\]\][ \t]*\r?$)', text)
    groups: dict[str, list[str]] = {}
    for block in blocks[1:]:
        section = block.split("]]", 1)[0][2:]
        groups.setdefault(section, []).append(block)
    # The model reader replaces a resumed root table array after another array.
    # Grouping also leaves profile definitions and immutable package metadata intact.
    result = (blocks[0] + "".join("".join(group) for group in groups.values())).encode("utf-8")
    if tomllib.loads(result.decode("utf-8")) != tomllib.loads(text):
        raise ValueError("Catalog grouping would change metadata")
    return result


def merge(previous: bytes, rows: list[str]) -> bytes:
    """Preserve every published entry; refuse to change an existing immutable version."""
    current = tomllib.loads(previous.decode("utf-8"))
    seen = {(p["id"], p.get("version", "")): p for p in current.get("packages", [])}
    output = previous
    for row in rows:
        entry = tomllib.loads(row)["packages"][0]
        key = entry["id"], entry["version"]
        if key in seen:
            if seen[key] != entry:
                raise ValueError("Plugin version is already published with different metadata")
            continue
        seen[key] = entry
        output += row.encode("utf-8")
    return group_catalog_tables(output)


def retire(previous: bytes, identities: list[str]) -> bytes:
    """Withdraw explicitly selected retired entries without changing other catalog data."""
    selected = set(identities)
    if not selected.issubset(UNPUBLISHED_PLUGIN_IDS | SUPERSEDED_PLUGINS.keys()):
        raise ValueError("Only retired or unpublished plugins may be withdrawn")
    if not selected:
        return previous
    original = tomllib.loads(previous.decode("utf-8"))
    published = {entry['id'] for entry in original.get('packages', [])}
    if any(SUPERSEDED_PLUGINS[identity] not in published for identity in selected & SUPERSEDED_PLUGINS.keys()):
        raise ValueError("Publish and independently verify replacement plugins before withdrawing predecessors")
    blocks = re.split(r'(?m)^(?=\[\[(?:' + CATALOG_ARRAYS + r')\]\][ \t]*\r?$)', previous.decode("utf-8"))
    kept = [blocks[0]]
    for block in blocks[1:]:
        if not block.startswith("[[packages]]"):
            kept.append(block)
            continue
        entries = tomllib.loads(block).get("packages", [])
        if len(entries) != 1:
            raise ValueError("Cannot safely isolate catalog entries")
        if entries[0]["id"] not in selected:
            kept.append(block)
    result = "".join(kept).encode("utf-8")
    expected = dict(original)
    expected["packages"] = [p for p in original.get("packages", []) if p["id"] not in selected]
    actual = tomllib.loads(result.decode("utf-8"))
    if "packages" in expected:
        actual.setdefault("packages", [])
    if actual != expected:
        raise ValueError("Withdrawal would change unrelated catalog data")
    return result


def retire_aliases(previous: bytes, identities: list[str]) -> bytes:
    """Withdraw former-ID cards using selected published replacements' own alias metadata.

    Replacement version history and immutable package objects remain available.
    Refuse ambiguous ownership or loss of a former release's supported builds.
    """
    if not identities:
        return previous
    selected = {identifier(value) for value in identities}
    original = tomllib.loads(previous.decode('utf-8'))
    packages = original.get('packages', [])
    replacements, ownership = {}, {}
    for row in packages:
        if row.get('kind') != 'plugin' or not row.get('plugin_definition'):
            continue
        plugin = tomllib.loads(row['plugin_definition'])['plugin']
        if (plugin['id'], plugin['game'], plugin['version']) != (row['id'], row['game'], row['version']):
            raise ValueError('Published alias metadata disagrees with its package identity')
        for alias in plugin.get('aliases', []):
            identifier(alias)
            key = row['game'], alias
            ownership.setdefault(key, set()).add(row['id'])
            if row['id'] in selected:
                replacements.setdefault(key, []).append(row)
    if not selected.issubset({row['id'] for rows in replacements.values() for row in rows}):
        raise ValueError('Every selected replacement must publish explicit former identifiers')
    for key, rows in replacements.items():
        if len(ownership[key]) != 1 or key[1] in selected or (key[0], rows[0]['id']) in ownership:
            raise ValueError('Published former identifier has ambiguous or cyclic ownership')
        for old in packages:
            if old.get('kind') != 'plugin' or (old.get('game'), old['id']) != key:
                continue
            supported = {build for row in rows for build in row.get('builds', [])}
            universal = any(not row.get('builds') for row in rows)
            if not universal and (not old.get('builds') or not set(old['builds']).issubset(supported)):
                raise ValueError('Replacement does not cover every former client build')
    blocks = re.split(r'(?m)^(?=\[\[(?:' + CATALOG_ARRAYS + r')\]\][ \t]*\r?$)', previous.decode('utf-8'))
    kept = [blocks[0]]
    for block in blocks[1:]:
        if block.startswith('[[packages]]'):
            rows = tomllib.loads(block).get('packages', [])
            if len(rows) != 1:
                raise ValueError('Cannot safely isolate catalog entries')
            row = rows[0]
            if row.get('kind') == 'plugin' and (row.get('game'), row['id']) in replacements:
                continue
        kept.append(block)
    result = ''.join(kept).encode('utf-8')
    expected = dict(original)
    expected['packages'] = [row for row in packages if not (row.get('kind') == 'plugin'
        and (row.get('game'), row['id']) in replacements)]
    if tomllib.loads(result.decode('utf-8')) != expected:
        raise ValueError('Former-ID withdrawal would change unrelated catalog data')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", action="append", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--uploader", default="", help="Optional uploader display name; omitted when empty.")
    parser.add_argument("--archive", action="store_true", help="Publish each package as one deterministic ZIP.")
    parser.add_argument("--retire-plugin", action="append", default=[], choices=sorted(UNPUBLISHED_PLUGIN_IDS | SUPERSEDED_PLUGINS.keys()))
    parser.add_argument("--retire-aliases-for", action="append", default=[],
                        help="Withdraw former-ID catalog cards declared by a published replacement; retain its version history.")
    args = parser.parse_args()
    alist = AList(args.base_url, os.environ["FT_ALIST_USERNAME"], os.environ["FT_ALIST_PASSWORD"])
    catalog = "FantasyTale/source.toml"
    metadata = retry_network(lambda: alist.metadata(catalog))
    if metadata is None:
        raise ValueError("Publish the initial public catalog before appending plugins")
    with alist.open_download(catalog, metadata, timeout=(30, 60)) as response:
        previous = response.content
    initial = hashlib.sha256(previous).hexdigest()
    rows, objects = [], {}
    for folder in args.package:
        row, content = package(folder.resolve(), args.output.resolve(), uploader=args.uploader, archive=args.archive)
        rows.append(row); objects.update(content)
    updated = retire_aliases(merge(retire(previous, args.retire_plugin), rows), args.retire_aliases_for)
    for path, local in sorted(objects.items(), key=lambda item: item[0].endswith(".toml")):
        expected = hashlib.sha256(local.read_bytes()).hexdigest()
        def transfer():
            actual = alist.remote_hash(path)
            if actual is not None and actual != expected:
                raise ValueError("Remote immutable plugin object conflict")
            if actual is None:
                alist.put(path, local)
                if alist.remote_hash(path) != expected:
                    raise ValueError("Public plugin verification failed")
        retry_network(transfer)
        print("Verified plugin object", path, flush=True)
    target = args.output / catalog
    atomic(target, updated)
    expected = hashlib.sha256(updated).hexdigest()
    def commit():
        current = alist.remote_hash(catalog)
        if current == expected:
            return
        if current != initial:
            raise ValueError("Catalog changed during upload; retry against the new catalog")
        alist.put(catalog, target)
        if alist.remote_hash(catalog) != expected:
            raise ValueError("Public merged catalog verification failed")
    retry_network(commit)
    print("Published plugins; unrelated catalog entries preserved", flush=True)


if __name__ == "__main__":
    try:
        main()
    except requests.RequestException as error:
        status = error.response.status_code if error.response is not None else "no HTTP response"
        print(f"Plugin publication network failure ({type(error).__name__}, {status})")
        raise SystemExit(1) from None
