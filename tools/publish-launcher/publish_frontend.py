"""Publish a frontend build against an exact, verified launcher backend baseline."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tomllib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "publish-store"))
from publish_store import reject_links, safe_path


def publish(baseline, frontend, output, feed, backend_version, revision):
    """Retain backend identities, replace the complete frontend set and advance the feed."""
    baseline, frontend, output = map(lambda path: Path(path).absolute(), (baseline, frontend, output))
    for path in (baseline, frontend, output):
        reject_links(path)
    safe_path(feed)
    if output.exists():
        raise ValueError("Use a new publication directory")
    for parent in (baseline, frontend):
        if output == parent or parent in output.parents or output in parent.parents:
            raise ValueError("Publication inputs and output must be separate")
    index = baseline / feed
    reject_links(index)
    base = tomllib.loads(index.read_text(encoding="utf-8"))
    if base.get("schema") != 1 or base.get("product") != "fantasytale-launcher" or base.get("platform") not in ("win-x64", "win-arm64", "win-x86"):
        raise ValueError("Use a complete launcher baseline")
    if not backend_version or base.get("version") != backend_version:
        raise ValueError("The frontend must explicitly target the baseline backend version")
    if type(revision) is not int or revision <= base["revision"]:
        raise ValueError("Frontend revision must advance the baseline")
    rows = base.get("files", [])
    if not rows:
        raise ValueError("Invalid baseline file count")
    verified = []
    destinations = set()
    for row in rows:
        safe_path(row["path"]); safe_path(row["source_path"])
        if row["path"].lower() in destinations:
            raise ValueError("Duplicate baseline destination")
        destinations.add(row["path"].lower())
        source = baseline / row["source_path"]
        reject_links(source)
        if source.stat().st_size != row["size"] or digest(source) != row["sha256"]:
            raise ValueError("Baseline object differs from its declared identity")
        if not row["path"].lower().startswith("wwwroot/"):
            verified.append((dict(row), source))
    if not {"fantasytale.launcher.exe", "updater/fantasytale.launcher.updater.exe", "wwwroot/index.html"} <= destinations:
        raise ValueError("Baseline is incomplete")
    prefix = str(Path(feed).parent).replace("\\", "/") + "/objects/"
    pending = [frontend]
    frontend_paths = set()
    while pending:
        folder = pending.pop()
        for source in sorted(folder.iterdir()):
            reject_links(source)
            if source.is_dir():
                pending.append(source); continue
            relative = source.relative_to(frontend).as_posix()
            safe_path(relative)
            if source.suffix.lower() in (".pem", ".key") or source.name.lower() == ".env" or relative.lower() in frontend_paths:
                raise ValueError("Invalid frontend input")
            frontend_paths.add(relative.lower())
            sha = digest(source)
            verified.append(({"path": "wwwroot/" + relative, "source_path": prefix + sha, "size": source.stat().st_size, "sha256": sha}, source))
    if "index.html" not in frontend_paths:
        raise ValueError("Frontend release is incomplete")
    text = "schema = 1\nproduct = \"fantasytale-launcher\"\n" + f"platform = {json.dumps(base['platform'])}\nrevision = {revision}\nversion = {json.dumps(backend_version)}\n"
    for row, _ in sorted(verified, key=lambda pair: pair[0]["path"].lower()):
        text += "\n[[files]]\n" + "".join(f"{key} = {json.dumps(row[key])}\n" for key in ("path", "source_path", "size", "sha256"))
    aliases = []
    metadata = baseline / ".fantasytale-feed-aliases.json"
    if metadata.exists():
        reject_links(metadata)
        record = json.loads(metadata.read_text(encoding="utf-8"))
        if record["feed_path"] != feed:
            raise ValueError("Baseline aliases target another feed")
        aliases = record.get("aliases", [])
        if not isinstance(aliases, list):
            raise ValueError("Invalid feed aliases")
        for alias in aliases:
            safe_path(alias)
            if alias.lower() == feed.lower() or "objects" in alias.lower().split("/"):
                raise ValueError("Invalid feed alias")
        if len({alias.lower() for alias in aliases}) != len(aliases):
            raise ValueError("Duplicate feed aliases")
    output.mkdir(parents=True)
    for row, source in verified:
        target = output / row["source_path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists():
            shutil.copyfile(source, target)
        if target.stat().st_size != row["size"] or digest(target) != row["sha256"]:
            raise ValueError("Publication input changed while assembling the release")
    target = output / feed
    target.parent.mkdir(parents=True, exist_ok=True)
    content = text.encode("utf-8")
    target.write_bytes(content)
    for alias in aliases:
        path = output / alias
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
    (output / ".fantasytale-feed-aliases.json").write_text(json.dumps({"feed_path": feed, "aliases": aliases, "sha256": hashlib.sha256(content).hexdigest()}), encoding="utf-8")
    print(f"Frontend release ready: {target}; backend {backend_version} retained; revision {revision}")
    return target


def digest(path):
    """Hash a file without buffering the complete runtime object."""
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--frontend", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--feed", default="FantasyTale/launcher/win-x64/current.toml")
    parser.add_argument("--backend-version", required=True)
    parser.add_argument("--revision", required=True, type=int)
    args = parser.parse_args()
    publish(args.baseline, args.frontend, args.output, args.feed, args.backend_version, args.revision)
