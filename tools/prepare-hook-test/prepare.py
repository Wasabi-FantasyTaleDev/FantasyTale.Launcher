"""Prepare H8 host/session/plugin files without changing a client installation."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tomllib


def quoted(value: str | Path) -> str:
    """Encode a TOML basic string using the compatible JSON string escapes."""
    return json.dumps(str(value).replace("\\", "/"), ensure_ascii=False)


def prepare(build_id: str, output: Path, configuration: str) -> None:
    """Create one build's reviewable test kit from tracked definitions and binaries."""
    root = Path(__file__).resolve().parents[2]
    game = root / "games/BubbleFighter"
    definition_path = game / "builds" / f"{build_id}.toml"
    definition = tomllib.loads(definition_path.read_text(encoding="utf-8"))
    build = definition["build"]
    if build["id"] != build_id:
        raise ValueError("build definition id differs from its filename")
    binary = root / "native/out" / configuration
    for name in ("nmcogame.dll", "crash_recorder.dll"):
        if not (binary / name).is_file():
            raise FileNotFoundError(f"Build Release|x86 first: missing {binary / name}")
    if output.exists():
        raise FileExistsError(f"Choose a new output directory: {output}")
    local_data = os.environ.get("LOCALAPPDATA")
    if not local_data:
        raise ValueError("LOCALAPPDATA is required to prepare a local test log path")
    log = Path(local_data) / f"FantasyTale/BubbleFighter/logs/v3-{build_id}/fthook.log"
    output.mkdir(parents=True)
    shutil.copy2(binary / "nmcogame.dll", output / "nmcogame.dll")
    base = (
        'schema = 1\n\n[session]\ngame = "bubblefighter"\n'
        f"build = {quoted(build_id)}\nbranch = {quoted(build['branch'])}\n"
        f"instance = {quoted('v3-' + build_id + '-test')}\n"
        f"messenger = {quoted(build['messenger'])}\nlog = {quoted(log)}\nlog_max_kib = 1024\n"
        f"\n[session.fingerprint]\npe_timestamp = {definition['fingerprint']['pe_timestamp']}\n"
    )
    (output / "host-only.toml").write_text(base, encoding="utf-8")
    recorder_id = "bubblefighter.fantasy.diagnostics"
    recorder_path = output / "plugins" / recorder_id
    recorder_path.mkdir(parents=True)
    recorder_manifest = game / "plugins" / recorder_id / "plugin.toml"
    manifest = tomllib.loads(recorder_manifest.read_text(encoding="utf-8"))
    if build_id not in manifest["compatibility"]["builds"]:
        raise ValueError(f"recorder does not declare {build_id}")
    shutil.copy2(recorder_manifest, recorder_path / "plugin.toml")
    shutil.copy2(binary / manifest["native"]["library"], recorder_path / manifest["native"]["library"])
    recorder = (
        f"\n[[plugins]]\nid = {quoted(recorder_id)}\nversion = {quoted(manifest['plugin']['version'])}\n"
        f"path = {quoted(recorder_path)}\nrequired = false\n[plugins.options]\n"
        "enabled = true\nmax_records = 32\nmax_kib = 1024\nexternal_privileged = false\n"
    )
    (output / "recorder.toml").write_text(base + recorder, encoding="utf-8")
    font_id = "bubblefighter.fantasy.font-support-cn"
    font_manifest = game / "plugins" / font_id / "plugin.toml"
    font = tomllib.loads(font_manifest.read_text(encoding="utf-8"))
    if build_id in font["compatibility"]["builds"]:
        font_path = output / "plugins" / font_id
        font_path.mkdir(parents=True)
        shutil.copy2(font_manifest, font_path / "plugin.toml")
        entry = (
            f"\n[[plugins]]\nid = {quoted(font_id)}\nversion = {quoted(font['plugin']['version'])}\n"
            f"path = {quoted(font_path)}\nrequired = false\n"
        )
        (output / "recorder-font.toml").write_text(base + recorder + entry, encoding="utf-8")
    for language in ("", ".zh"):
        shutil.copy2(root / f"docs/hook/real-client-test{language}.md", output / f"README{language}.md")
    instructions = (
        f"Build: {build_id}; executable: {build['executable']}; messenger: {build['messenger']}.\n"
        "Use a test client copy and preserve the existing DLL/session files first.\n"
        "Copy nmcogame.dll and one chosen template renamed to session.toml beside the executable.\n"
        "Keep this kit at its current path: plugin paths in the templates are absolute.\n"
        "Begin with host-only.toml; then recorder.toml; finally recorder-font.toml if present.\n"
        "Forward mode requires the original Nexon library named nmcogame.nexon.dll.\n"
        "Use the existing per-build launch procedure. Preserve logs before restarting.\n"
        f"Runtime log: {log}\nCrash log: {log.parent / 'crash.log'}\n"
        "No client files have been changed. No account or ticket is embedded.\n"
    )
    (output / "START-HERE.txt").write_text(instructions, encoding="utf-8")
    hashes = {
        str(path.relative_to(output)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(output.rglob("*")) if path.is_file()
    }
    (output / "sha256.json").write_text(json.dumps(hashes, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {build_id}: {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", required=True, choices=("kr-812", "id-400", "id-1809", "cn-324"))
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--configuration", default="Release", choices=("Release", "Debug"))
    args = parser.parse_args()
    prepare(args.build, args.output.resolve(), args.configuration)
