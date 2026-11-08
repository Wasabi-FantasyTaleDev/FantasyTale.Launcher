"""Create a portable manual-download ZIP from the same verified launcher file index."""
import hashlib
from pathlib import Path
import sys
import tomllib
import uuid
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parent / "publish-store"))
from publish_store import reject_links, safe_path

if len(sys.argv) != 4:
    raise SystemExit("Usage: <package> <index> <new-archive>")
package, index, archive = (Path(value).resolve() for value in sys.argv[1:])
if archive.exists():
    raise SystemExit("Use a new archive path")
definition = tomllib.loads(index.read_text(encoding="utf-8"))
if definition.get("schema") != 1 or definition.get("product") != "fantasytale-launcher":
    raise SystemExit("Use a schema-one launcher index")
rows = definition["files"]
if not isinstance(rows, list) or not rows:
    raise SystemExit("Invalid archive file count")
archive.parent.mkdir(parents=True, exist_ok=True)
temporary = archive.with_name(archive.name + ".publishing-" + uuid.uuid4().hex + ".tmp")
try:
    with zipfile.ZipFile(temporary, "x", zipfile.ZIP_DEFLATED, compresslevel=6) as target:
        for row in rows:
            relative = row["path"]
            safe_path(relative)
            source = package / relative
            reject_links(source)
            with source.open("rb") as stream, target.open(relative, "w", force_zip64=row["size"] >= 2 * 1024**3) as entry:
                digest = hashlib.sha256()
                count = 0
                for chunk in iter(lambda: stream.read(128 * 1024), b""):
                    count += len(chunk)
                    if count > row["size"]:
                        raise ValueError("Archive input exceeds the selected size")
                    digest.update(chunk)
                    entry.write(chunk)
                if count != row["size"] or digest.hexdigest() != row["sha256"]:
                    raise ValueError("Archive input differs from the selected index")
        policy = package / "launcher-update.toml"
        reject_links(policy)
        target.write(policy, "launcher-update.toml")
    temporary.replace(archive)
except BaseException:
    temporary.unlink(missing_ok=True)
    raise
print("Created verified portable archive")
