"""Compose one store catalog with packages, client profiles and server discovery metadata."""
import argparse
import hashlib
from pathlib import Path
import tomllib

from publish_store import atomic, identifier, quote, reject_links
from server_profiles import server_identity


def value(item):
    """Render only the scalar, list and inline-table types used by catalog metadata."""
    if isinstance(item, str):
        return quote(item)
    if type(item) is bool:
        return str(item).lower()
    if type(item) is int:
        return str(item)
    if isinstance(item, list):
        return "[" + ", ".join(value(entry) for entry in item) + "]"
    if isinstance(item, dict):
        return "{ " + ", ".join(quote(key) + " = " + value(entry) for key, entry in item.items()) + " }"
    raise ValueError("Unsupported catalog value")


def compose(catalog, configuration, output, legacy_output=None):
    """Retain package identities and merge advancing client definitions into a unified catalog."""
    catalog, configuration, output = map(lambda path: Path(path).absolute(), (catalog, configuration, output))
    for path in (catalog, configuration, output):
        reject_links(path)
    if output.exists() or output in (catalog, configuration):
        raise ValueError("Use a new catalog output path")
    if legacy_output is not None:
        legacy_output = Path(legacy_output).absolute()
        reject_links(legacy_output)
        if legacy_output.exists() or legacy_output in (catalog, configuration, output):
            raise ValueError("Use a distinct new legacy output path")
    source = tomllib.loads(catalog.read_text(encoding="utf-8"))
    config = tomllib.loads(configuration.read_text(encoding="utf-8"))
    network_keys = {"servers", "directory_sources", "github_releases", "deprecated_plugins"}
    if source.get("schema") not in (1, 2) or set(source) - {"schema", "catalog", "packages", "profiles", *network_keys}:
        raise ValueError("Unsupported input catalog")
    if not config or set(config) - {"profiles", *network_keys} or not any(config.values()):
        raise ValueError("Configure profiles or server discovery metadata")
    packages = source.get("packages", [])
    profiles = {}
    for row in source.get("profiles", []):
        key = (row["game"], row["build"])
        if key in profiles:
            raise ValueError("Duplicate existing client profile")
        profiles[key] = row
    seen = set()
    for row in config.get("profiles", []):
        if set(row) != {"definition", "revision"} or type(row["revision"]) is not int or not 1 <= row["revision"] <= 9007199254740991:
            raise ValueError("Each profile needs a definition file and a positive revision")
        path = configuration.parent / row["definition"]
        reject_links(path)
        definition = path.read_text(encoding="utf-8-sig")
        document = tomllib.loads(definition)
        if document.get("schema") != 1:
            raise ValueError("Unsupported client definition schema")
        build = document["build"]
        key = (identifier(build["game"]), identifier(build["id"]))
        if key in seen:
            raise ValueError("Duplicate configured client profile")
        seen.add(key)
        previous = profiles.get(key)
        if previous is not None and (row["revision"] < previous["revision"] or
                row["revision"] == previous["revision"] and definition != previous["definition"]):
            raise ValueError("Profile revision regressed or changed without advancing")
        profiles[key] = {"game": key[0], "build": key[1], "revision": row["revision"], "definition": definition}

    network = {}
    for section in sorted(network_keys):
        entries = {}
        for rows in (source.get(section, []), config.get(section, [])):
            seen = set()
            for row in rows:
                if section == "deprecated_plugins":
                    if set(row) - {"id", "game", "name", "reason"} or not {"id", "game", "name"}.issubset(row):
                        raise ValueError("Retired plugins require id, game and localized name")
                    identifier(row["game"])
                    for field in ("name", "reason"):
                        localized = row.get(field, {})
                        if not isinstance(localized, dict) or field == "name" and not localized or any(
                                not isinstance(key, str) or not key or not isinstance(text, str) or not text.strip()
                                for key, text in localized.items()):
                            raise ValueError("Retired plugin text must be a localized string table")
                persistent = section == "servers" and row.get("kind", "server") == "server"
                identity = server_identity(row["id"]) if persistent else identifier(row["id"])
                identity = identity.casefold() if section == "servers" else identity
                if identity in seen:
                    raise ValueError("Duplicate discovery identity")
                seen.add(identity)
                entries[identity] = row
        network[section] = entries

    def render(schema, rows, include_profiles):
        text = f"schema = {schema}\n[catalog]\n" + "".join(f"{quote(key)} = {value(entry)}\n" for key, entry in source["catalog"].items())
        for row in rows:
            text += "\n[[packages]]\n" + "".join(f"{quote(key)} = {value(entry)}\n" for key, entry in row.items())
        if include_profiles:
            for _, row in sorted(profiles.items()):
                text += "\n[[profiles]]\n" + "".join(f"{quote(key)} = {value(entry)}\n" for key, entry in row.items())
            for section, entries in network.items():
                for _, row in sorted(entries.items()):
                    text += f"\n[[{section}]]\n" + "".join(f"{quote(key)} = {value(entry)}\n" for key, entry in row.items())
        return text.encode("utf-8")

    unified = render(2, packages, True)
    legacy = render(1, [{key: entry for key, entry in row.items() if key not in ("plugin_definition", "minimum_launcher_version")} for row in packages], False)
    output.parent.mkdir(parents=True, exist_ok=True)
    atomic(output, unified)
    if legacy_output is not None:
        legacy_output.parent.mkdir(parents=True, exist_ok=True)
        atomic(legacy_output, legacy)
    return {"profiles": len(profiles), "packages": len(packages), **{key: len(rows) for key, rows in network.items()}, "sha256": hashlib.sha256(unified).hexdigest()}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--legacy-output", type=Path)
    args = parser.parse_args()
    print(compose(args.catalog, args.config, args.output, args.legacy_output))
