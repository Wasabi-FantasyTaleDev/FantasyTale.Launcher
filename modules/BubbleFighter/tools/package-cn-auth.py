"""Prepare a local authentication package from verified, independently built payloads."""
import argparse
import hashlib
from pathlib import Path
import tomllib
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gpk", type=Path, required=True, help="Existing published gpk_stub.dll payload")
    parser.add_argument("--output", type=Path, required=True, help="New local archive path")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    plugin = root / "games/BubbleFighter/plugins/bubblefighter.fantasy.login-fix-cn"
    manifest = (plugin / "plugin.toml").read_bytes()
    definition = tomllib.loads(manifest.decode("utf-8"))
    payloads = {
        "gpk_stub.dll": args.gpk.read_bytes(),
        "login_bridge.dll": (root / "artifacts/cn-auth/login_bridge.dll").read_bytes(),
        "native-passports.toml": (plugin / "native-passports.toml").read_bytes(),
        "TOML-LICENSE": (root / "native/third_party/tomlplusplus/LICENSE").read_bytes(),
    }
    for row in definition["files"]:
        payload = payloads[row["source"]]
        if len(payload) != row["size"] or hashlib.sha256(payload).hexdigest() != row["sha256"]:
            raise ValueError(f"Payload identity mismatch: {row['source']}")
    payloads["plugin.toml"] = manifest
    payloads["file-install.toml"] = (plugin / "file-install.toml").read_bytes()
    payloads["LICENSE"] = (root / "LICENSE").read_bytes()
    payloads["NATIVE-LOGIN.md"] = (root / "games/BubbleFighter/native/login-bridge/README.md").read_bytes()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, content in sorted(payloads.items()):
            entry = zipfile.ZipInfo(name, date_time=(2000, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, content)
    print(f"Prepared {definition['plugin']['id']} {definition['plugin']['version']}: {args.output}")
    print(f"SHA256: {hashlib.sha256(args.output.read_bytes()).hexdigest()}")
    print("Local package only; no game installation, store publication or deployment.")


if __name__ == "__main__":
    main()
