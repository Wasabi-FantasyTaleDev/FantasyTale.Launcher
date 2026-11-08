"""Check a Windows distribution's detached updater without opening the launcher."""
import argparse
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import shutil
import stat
import struct
import subprocess
import tempfile
from urllib.parse import unquote, urlsplit


def verify_distribution_hygiene(root: Path) -> None:
    """Reject local state and linked files before executing or indexing a distribution."""
    forbidden = {".git", ".hg", ".svn", ".claude", ".codex", ".agent-worktrees", "artifacts",
                 "reference", "private", "local", "logs", "dumps", "backups", "recovery",
                 "webdata", "overlaywebdata", "node_modules", "testresults"}
    for directory, folders, files in os.walk(root, followlinks=False):
        for name in folders + files:
            path = Path(directory) / name
            relative = path.relative_to(root).as_posix()
            lower = name.casefold()
            if path.is_symlink() or getattr(path.lstat(), "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError(f"Distribution contains a linked path: {relative}")
            if lower in forbidden or lower.startswith((".env", "session.toml", ".fantasytale-")) or lower in {
                    "local.patch.yaml", "credentials.json", "accounts.json"} or lower.endswith((
                    ".local.toml", ".local.json", ".local.yaml", ".log", ".dmp", ".pdb", ".bak")):
                raise ValueError(f"Distribution contains private state or debug output: {relative}")


class FrontendEntries(HTMLParser):
    """Collect executable entry scripts and their eagerly loaded styles and modules."""

    def __init__(self):
        super().__init__()
        self.references = []
        self.scripts = 0

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "script" and values.get("src"):
            self.scripts += 1
            self.references.append(values["src"])
        elif tag == "link" and set((values.get("rel") or "").lower().split()) & {"stylesheet", "modulepreload"}:
            self.references.append(values.get("href") or "")


def verify_frontend(root: Path) -> None:
    """Reject HTML entries whose local compiled assets cannot be served from wwwroot."""
    frontend = (root / "wwwroot").resolve()
    parser = FrontendEntries()
    parser.feed((frontend / "index.html").read_text(encoding="utf-8-sig"))
    if not parser.scripts:
        raise ValueError("Launcher frontend has no compiled script entry")
    for reference in parser.references:
        url = urlsplit(reference)
        decoded = unquote(url.path)
        if url.scheme or url.netloc or not decoded or "\\" in decoded:
            raise ValueError("Launcher frontend entry must reference a local asset")
        path = (frontend / decoded.lstrip("/")).resolve()
        if not path.is_relative_to(frontend):
            raise ValueError("Launcher frontend asset escapes wwwroot")
        if not path.is_file() or not path.stat().st_size:
            raise ValueError(f"Launcher frontend is missing a compiled asset: {reference}")


def machine(path: Path) -> int:
    """Read the executable's PE machine without loading its code."""
    with path.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError("Updater distribution contains an invalid executable")
        stream.seek(0x3C)
        offset = struct.unpack("<I", stream.read(4))[0]
        stream.seek(offset)
        if stream.read(4) != b"PE\0\0":
            raise ValueError("Updater distribution contains an invalid PE header")
        return struct.unpack("<H", stream.read(2))[0]


def verify_payload(root: Path, platform: str) -> None:
    """Reject incomplete portable renderer or archive decoder payloads before indexing."""
    verify_distribution_hygiene(root)
    for entry in ("FantasyTale.Launcher", "updater/FantasyTale.Launcher.Updater", "overlay/FantasyTale.Launcher.Overlay"):
        configuration = root / (entry + ".runtimeconfig.json")
        runtime = json.loads(configuration.read_text(encoding="utf-8"))["runtimeOptions"]
        for framework in runtime.get("includedFrameworks", []):
            name, version = framework["name"], framework["version"]
            if name not in ("Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App") or not all(
                    part.isdecimal() for part in version.split(".")) or len(version.split(".")) != 3:
                raise ValueError("Unsupported runtime identity in distribution")
            notices = ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT") if name == "Microsoft.NETCore.App" else ("LICENSE",)
            for notice in notices:
                path = configuration.parent / "ThirdPartyLicenses/runtime" / f"{name}-{version}" / notice
                if not path.is_file() or not path.stat().st_size:
                    raise ValueError(f"Runtime redistribution is missing a required notice: {entry}/{name}/{notice}")
    architecture, expected = {"win-x64": ("x64", 0x8664), "win-arm64": ("arm64", 0xAA64)}[platform]
    overlay = root / "overlay"
    executable = overlay / "FantasyTale.Launcher.Overlay.exe"
    if machine(executable) != expected:
        raise ValueError("Overlay executable must match the distribution architecture")
    config = json.loads((overlay / "FantasyTale.Launcher.Overlay.runtimeconfig.json").read_text(encoding="utf-8"))["runtimeOptions"]
    frameworks = {item["name"] for item in config.get("includedFrameworks", [])}
    if not {"Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"}.issubset(frameworks) or "framework" in config or "frameworks" in config:
        raise ValueError("Portable Overlay must include its own WindowsDesktop runtime")
    for filename in ("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll"):
        if machine(overlay / filename) != expected:
            raise ValueError("Overlay native runtime must match the distribution architecture")
    for filename in ("System.Private.CoreLib.dll", "PresentationFramework.dll", "Microsoft.Web.WebView2.Wpf.dll",
                     "Microsoft.Web.WebView2.Core.dll", "overlay/index.html", "overlay/panel-stack.js", "overlay/anchors.js",
                     "overlay/locales.json", "overlay/surface-ready.js", "overlay/ui-library/game-ui.js", "overlay/ui-library/game-ui.css",
                     "overlay/ui-library/vue.esm-browser.prod.js"):
        if not (overlay / filename).is_file() or not (overlay / filename).stat().st_size:
            raise ValueError(f"Portable Overlay is missing a required file: {filename}")
    # RID publication can flatten NuGet native assets; AnyCPU output keeps the SDK's RID subdirectory.
    loaders = [path for path in (overlay / "WebView2Loader.dll",
               overlay / f"runtimes/{platform}/native/WebView2Loader.dll") if path.is_file()]
    if not loaders or any(machine(path) != expected for path in loaders):
        raise ValueError("Overlay WebView2 loader must exist and match the distribution architecture")
    if machine(root / f"archive/{architecture}/7z.dll") != expected or not (root / "archive/License.txt").is_file():
        raise ValueError("Distribution must include its native archive decoder and license")
    for filename in ("COPYING.LGPL.txt", "PROVENANCE.md", "source/7z2604-src.tar.xz"):
        path = root / "archive" / filename
        if not path.is_file() or not path.stat().st_size:
            raise ValueError(f"Archive decoder redistribution is missing a required file: {filename}")
    if not (root / "wwwroot/index.html").is_file():
        raise ValueError("Distribution is missing the launcher frontend")
    verify_frontend(root)
    if any(path.is_dir() and (path.name.startswith(("win-", "linux-", "osx-")) or path.name in ("publish", "OverlayWebData")) for path in overlay.iterdir()):
        raise ValueError("Overlay contains a nested build or local browser profile")
    print(f"PASS {platform} complete payload: portable Overlay, frontend and native archive decoder")


def verify(root: Path, platform: str, runtime_mode: str = "self-contained") -> None:
    """Execute only the helper's no-argument entry in a separate temporary copy."""
    verify_distribution_hygiene(root)
    expected = {"win-x64": 0x8664, "win-x86": 0x14C, "win-arm64": 0xAA64}[platform]
    updater = root / "updater"
    for path in (root / "FantasyTale.Launcher.exe", updater / "FantasyTale.Launcher.Updater.exe"):
        if machine(path) != expected:
            raise ValueError("Launcher and updater must match the requested architecture")
    portable = runtime_mode == "self-contained"
    for name in ("FantasyTale.Launcher", "updater/FantasyTale.Launcher.Updater"):
        config = json.loads((root / (name + ".runtimeconfig.json")).read_text(encoding="utf-8"))["runtimeOptions"]
        if portable != ("includedFrameworks" in config) or portable == ("framework" in config or "frameworks" in config):
            raise ValueError("Launcher and updater runtime modes do not match the distribution")
    if any(path.is_dir() and (path.name.startswith(("win-", "linux-", "osx-")) or path.name == "publish") for path in updater.iterdir()):
        raise ValueError("Updater contains a nested build distribution")
    if portable:
        for name in ("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll"):
            if not (updater / name).is_file():
                raise ValueError("Portable updater is missing a runtime dependency")
    with tempfile.TemporaryDirectory(prefix="ft-detached-updater-") as temporary:
        detached = Path(temporary) / "updater"
        shutil.copytree(updater, detached)
        environment = dict(os.environ)
        if portable:
            unavailable = str(Path(temporary) / "unavailable-runtime")
            environment.update({"DOTNET_ROOT": unavailable, "DOTNET_ROOT_X64": unavailable,
                                "DOTNET_ROOT_X86": unavailable, "DOTNET_ROOT_ARM64": unavailable,
                                "DOTNET_ROOT(x86)": unavailable, "DOTNET_MULTILEVEL_LOOKUP": "0"})
        result = subprocess.run([str(detached / "FantasyTale.Launcher.Updater.exe")], cwd=detached,
                                env=environment, capture_output=True, timeout=20,
                                creationflags=subprocess.CREATE_NO_WINDOW)
        if result.returncode != 2:
            raise ValueError("Detached updater did not reach its expected no-argument entry point")
    print(f"PASS {platform} {runtime_mode} updater: matching PE architecture, runtime mode and detached execution")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--platform", required=True, choices=("win-x64", "win-x86", "win-arm64"))
    parser.add_argument("--runtime-mode", default="self-contained", choices=("self-contained", "framework-dependent"))
    parser.add_argument("--complete", action="store_true", help="Also require the current portable Overlay and archive payload")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("Detached updater execution requires Windows")
    verify(args.directory.resolve(), args.platform, args.runtime_mode)
    if args.complete:
        if args.runtime_mode != "self-contained" or args.platform == "win-x86":
            parser.error("Complete publication requires a self-contained x64 or ARM64 package")
        verify_payload(args.directory.resolve(), args.platform)
