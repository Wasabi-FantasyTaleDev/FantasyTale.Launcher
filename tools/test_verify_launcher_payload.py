"""Exercise release gates without executing a renderer or requiring a desktop."""
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("verify_updater", Path(__file__).with_name("verify-updater-package.py"))
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


class LauncherPayloadTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def put(self, name, data=b"fixture"):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def pe(self, name, machine):
        data = bytearray(90)
        data[:2] = b"MZ"
        struct.pack_into("<I", data, 0x3C, 80)
        data[80:84] = b"PE\0\0"
        struct.pack_into("<H", data, 84, machine)
        return self.put(name, data)

    def fixture(self, platform="win-x64"):
        architecture, machine = {"win-x64": ("x64", 0x8664), "win-arm64": ("arm64", 0xAA64)}[platform]
        for name in ("FantasyTale.Launcher.Overlay.exe", "hostfxr.dll", "hostpolicy.dll", "coreclr.dll",
                     "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", f"runtimes/{platform}/native/WebView2Loader.dll"):
            self.pe("overlay/" + name, machine)
        self.pe(f"archive/{architecture}/7z.dll", machine)
        for name in ("System.Private.CoreLib.dll", "PresentationFramework.dll", "Microsoft.Web.WebView2.Wpf.dll",
                     "Microsoft.Web.WebView2.Core.dll", "overlay/index.html", "overlay/panel-stack.js", "overlay/anchors.js",
                     "overlay/locales.json", "overlay/surface-ready.js", "overlay/ui-library/game-ui.js", "overlay/ui-library/game-ui.css",
                     "overlay/ui-library/vue.esm-browser.prod.js"):
            self.put("overlay/" + name)
        self.put("wwwroot/index.html", b'<script type="module" src="./assets/index-fixture.js"></script><link rel="stylesheet" href="./assets/index-fixture.css">')
        self.put("wwwroot/assets/index-fixture.js")
        self.put("wwwroot/assets/index-fixture.css")
        self.put("archive/License.txt")
        for name in ("COPYING.LGPL.txt", "PROVENANCE.md", "source/7z2604-src.tar.xz"):
            self.put("archive/" + name)
        for entry in ("FantasyTale.Launcher", "updater/FantasyTale.Launcher.Updater", "overlay/FantasyTale.Launcher.Overlay"):
            frameworks = ["Microsoft.NETCore.App"]
            if entry.startswith("overlay/"):
                frameworks.append("Microsoft.WindowsDesktop.App")
            self.put(entry + ".runtimeconfig.json", json.dumps({"runtimeOptions": {
                "includedFrameworks": [{"name": name, "version": "10.0.12"} for name in frameworks]
            }}).encode())
            for name in frameworks:
                notices = ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT") if name == "Microsoft.NETCore.App" else ("LICENSE",)
                for notice in notices:
                    self.put(str(Path(entry).parent / "ThirdPartyLicenses/runtime" / f"{name}-10.0.12" / notice))

    def test_both_supported_payloads(self):
        self.fixture()
        VERIFIER.verify_payload(self.root, "win-x64")
        with tempfile.TemporaryDirectory() as directory:
            self.root = Path(directory)
            self.fixture("win-arm64")
            VERIFIER.verify_payload(self.root, "win-arm64")

    def test_missing_overlay_is_rejected(self):
        with self.assertRaises(FileNotFoundError):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_flattened_rid_loader_is_accepted_for_both_architectures(self):
        for platform, machine in (("win-x64", 0x8664), ("win-arm64", 0xAA64)):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as directory:
                self.root = Path(directory)
                self.fixture(platform)
                (self.root / f"overlay/runtimes/{platform}/native/WebView2Loader.dll").unlink()
                self.pe("overlay/WebView2Loader.dll", machine)
                VERIFIER.verify_payload(self.root, platform)

    def test_conflicting_or_absent_loader_is_rejected(self):
        self.fixture()
        self.pe("overlay/WebView2Loader.dll", 0xAA64)
        with self.assertRaisesRegex(ValueError, "WebView2"):
            VERIFIER.verify_payload(self.root, "win-x64")
        (self.root / "overlay/WebView2Loader.dll").unlink()
        (self.root / "overlay/runtimes/win-x64/native/WebView2Loader.dll").unlink()
        with self.assertRaisesRegex(ValueError, "WebView2"):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_compiled_frontend_assets_must_exist_and_be_nonempty(self):
        self.fixture()
        for name in ("index-fixture.js", "index-fixture.css"):
            with self.subTest(asset=name):
                path = self.root / "wwwroot/assets" / name
                path.unlink()
                with self.assertRaisesRegex(ValueError, "missing a compiled asset"):
                    VERIFIER.verify_payload(self.root, "win-x64")
                path.write_bytes(b"")
                with self.assertRaisesRegex(ValueError, "missing a compiled asset"):
                    VERIFIER.verify_payload(self.root, "win-x64")
                path.write_bytes(b"fixture")

    def test_frontend_entry_is_required_and_cannot_escape_package(self):
        self.fixture()
        for reference in ("https://example.invalid/app.js", "../outside.js", "%2e%2e/outside.js", "assets\\app.js"):
            with self.subTest(reference=reference):
                self.put("wwwroot/index.html", f'<script src="{reference}"></script>'.encode())
                with self.assertRaisesRegex(ValueError, "local asset|escapes wwwroot"):
                    VERIFIER.verify_payload(self.root, "win-x64")
        self.put("wwwroot/index.html", b'<div id="app"></div>')
        with self.assertRaisesRegex(ValueError, "no compiled script entry"):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_frontend_root_paths_and_query_strings_are_supported(self):
        self.fixture()
        self.put("wwwroot/index.html", b'<script src="/assets/index-fixture.js?v=1"></script><link rel="modulepreload" href="./assets/index-fixture.js#module">')
        VERIFIER.verify_payload(self.root, "win-x64")

    def test_cross_architecture_renderer_is_rejected(self):
        self.fixture()
        self.pe("overlay/FantasyTale.Launcher.Overlay.exe", 0xAA64)
        with self.assertRaisesRegex(ValueError, "architecture"):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_developer_runtime_dependency_is_rejected(self):
        self.fixture()
        self.put("overlay/FantasyTale.Launcher.Overlay.runtimeconfig.json", json.dumps({"runtimeOptions": {
            "frameworks": [{"name": "Microsoft.NETCore.App"}, {"name": "Microsoft.WindowsDesktop.App"}]
        }}).encode())
        with self.assertRaisesRegex(ValueError, "WindowsDesktop"):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_missing_ui_or_native_dependency_is_rejected(self):
        self.fixture()
        (self.root / "overlay/overlay/ui-library/game-ui.js").unlink()
        with self.assertRaisesRegex(ValueError, "required file"):
            VERIFIER.verify_payload(self.root, "win-x64")
        self.put("overlay/overlay/ui-library/game-ui.js")
        self.pe("overlay/runtimes/win-x64/native/WebView2Loader.dll", 0xAA64)
        with self.assertRaisesRegex(ValueError, "WebView2"):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_browser_profile_is_rejected(self):
        self.fixture()
        self.put("overlay/OverlayWebData/profile")
        with self.assertRaisesRegex(ValueError, "private state"):
            VERIFIER.verify_payload(self.root, "win-x64")

    def test_private_state_is_rejected_at_any_depth_and_in_any_case(self):
        for name in (".git/config", "games/BubbleFighter/servers/test.LOCAL.toml",
                     "games/BubbleFighter/session.toml", "games/BubbleFighter/session.toml.backup",
                     "updater/.env", "overlay/Logs/fault.txt", "games/BubbleFighter/plugins/test/crash.DMP",
                     "FantasyTale.Launcher.pdb", "games/BubbleFighter/plugins/test/reference/input.txt",
                     "games/BubbleFighter/plugins/test/.fantasytale-native/journal.toml"):
            with self.subTest(path=name), tempfile.TemporaryDirectory() as directory:
                previous = self.root
                self.root = Path(directory)
                self.put(name)
                with self.assertRaisesRegex(ValueError, "private state"):
                    VERIFIER.verify_distribution_hygiene(self.root)
                self.root = previous

    def test_licenses_source_archives_and_public_config_are_allowed(self):
        for name in ("archive/source/7z2604-src.tar.xz", "ThirdPartyLicenses/runtime/LICENSE.TXT",
                     "launcher-update.toml", "games/BubbleFighter/builds/kr-812.toml"):
            self.put(name)
        VERIFIER.verify_distribution_hygiene(self.root)

    def test_decoder_source_and_notices_are_required(self):
        self.fixture()
        for name in ("COPYING.LGPL.txt", "PROVENANCE.md", "source/7z2604-src.tar.xz"):
            with self.subTest(filename=name):
                path = self.root / "archive" / name
                path.unlink()
                with self.assertRaisesRegex(ValueError, "redistribution.*required file"):
                    VERIFIER.verify_payload(self.root, "win-x64")
                path.write_bytes(b"")
                with self.assertRaisesRegex(ValueError, "redistribution.*required file"):
                    VERIFIER.verify_payload(self.root, "win-x64")
                path.write_bytes(b"fixture")

    def test_runtime_notices_are_required_for_each_executable(self):
        self.fixture()
        paths = list(self.root.glob("**/ThirdPartyLicenses/runtime/*/*"))
        self.assertEqual(7, len(paths))
        for path in paths:
            with self.subTest(path=path.relative_to(self.root)):
                path.unlink()
                with self.assertRaisesRegex(ValueError, "Runtime redistribution.*required notice"):
                    VERIFIER.verify_payload(self.root, "win-x64")
                path.write_bytes(b"")
                with self.assertRaisesRegex(ValueError, "Runtime redistribution.*required notice"):
                    VERIFIER.verify_payload(self.root, "win-x64")
                path.write_bytes(b"fixture")


if __name__ == "__main__":
    unittest.main()
