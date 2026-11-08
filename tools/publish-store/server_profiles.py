"""Validate the explicitly referenced server TOML files in a publication root."""
import hashlib
from pathlib import Path
import re
import tomllib
from urllib.parse import urlsplit

from publish_store import reject_links, safe_path


def server_identity(identity):
    """Match the launcher's file-safe persistent server ID contract."""
    if not isinstance(identity, str) or not re.fullmatch(r"[A-Za-z0-9_-]+", identity):
        raise ValueError("Invalid server identity: use ASCII letters, digits, hyphens and underscores")
    if identity.casefold() in ("con", "prn", "aux", "nul") or re.fullmatch(r"(?:com|lpt)[1-9]", identity, re.IGNORECASE):
        raise ValueError("Reserved server identity")
    return identity


def api_identity(value):
    """Compare HTTP API authorities and base paths without accepting embedded credentials."""
    if not isinstance(value, str) or not value or any(ord(char) < 32 for char in value):
        raise ValueError("Invalid server profile API address")
    parsed = urlsplit(value)
    if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username is not None or parsed.password is not None or parsed.query or parsed.fragment:
        raise ValueError("Invalid server profile API address")
    port = parsed.port
    if port is not None and not 1 <= port <= 65535:
        raise ValueError("Invalid server profile API port")
    return parsed.scheme, parsed.hostname.lower(), port if port is not None else (443 if parsed.scheme == "https" else 80), parsed.path.rstrip("/")


def endpoint(row):
    """Require a complete game endpoint when either host or port is declared."""
    if "host" not in row and "port" not in row:
        return None
    host, port = row.get("host"), row.get("port")
    if not isinstance(host, str) or not host or type(port) is not int or not 1 <= port <= 65535:
        raise ValueError("Invalid server profile game endpoint")
    return host, port


def server_profiles(root: Path, catalog: str, objects=()) -> list[tuple[str, str]]:
    """Pin only declared, portable server profiles that agree with their advertisements."""
    document = tomllib.loads((root / catalog).read_text(encoding="utf-8-sig"))
    forbidden = {path.casefold() for path in [catalog, *(item[0] for item in objects)]}
    selected, identities = {}, set()
    advertisements = document.get("servers", [])
    if not isinstance(advertisements, list) or any(not isinstance(row, dict) for row in advertisements):
        raise ValueError("Server advertisements must be table rows")
    for advertisement in advertisements:
        if advertisement.get("kind", "server") != "server":
            if "profile" in advertisement:
                raise ValueError("Only persistent servers may reference a server profile")
            continue
        identity = server_identity(advertisement["id"])
        if identity.casefold() in identities:
            raise ValueError("Duplicate server profile identity")
        identities.add(identity.casefold())
        if "profile" not in advertisement:
            continue
        path = advertisement["profile"]
        if not isinstance(path, str) or not path.lower().endswith(".toml"):
            raise ValueError("Server profile must reference a relative TOML file")
        safe_path(path)
        if any(part.casefold().startswith(".fantasytale") or part.casefold() == "objects" for part in path.split("/")) or any(path.casefold() == item or path.casefold().startswith(item + "/") or item.startswith(path.casefold() + "/") for item in forbidden):
            raise ValueError("Server profile path conflicts with publication content")
        source = root / path
        reject_links(source)
        content = source.read_bytes()
        profile = tomllib.loads(content.decode("utf-8-sig"))
        if type(profile.get("schema")) is not int or profile["schema"] != 1 or set(profile) - {"schema", "server", "presentation", "plugin_recommendations", "plugin_sources"}:
            raise ValueError("Invalid portable server profile document")
        server = profile.get("server")
        if not isinstance(server, dict) or set(server) - {"id", "game", "name", "api_url", "host", "port", "builds", "bundled_instances", "prefer_ip"}:
            raise ValueError("Invalid portable server profile fields")
        if not isinstance(server.get("name"), str) or not server["name"].strip():
            raise ValueError("Server profile requires a display name")
        if server.get("id") != identity or server.get("game") != advertisement["game"] or api_identity(server.get("api_url")) != api_identity(advertisement.get("api_url")) or endpoint(server) != endpoint(advertisement):
            raise ValueError("Server profile identity or connection differs from its advertisement")
        selected[path] = hashlib.sha256(content).hexdigest()
        forbidden.add(path.casefold())
    return list(selected.items())
