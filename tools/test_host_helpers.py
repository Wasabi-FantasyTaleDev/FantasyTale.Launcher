"""Build a fresh Host distribution and check its detached helper provenance."""
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


@unittest.skipUnless(os.name == "nt", "The Host helpers target Windows")
class HostHelperTests(unittest.TestCase):
    repository = Path(__file__).resolve().parent.parent

    def run_command(self, *command):
        result = subprocess.run(command, cwd=self.repository, capture_output=True,
                                text=True, encoding="utf-8", errors="replace", timeout=180)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        return result.stdout

    def test_fresh_artifacts_restore_and_copy_matching_helpers(self):
        artifacts = self.repository / "artifacts"
        artifacts.mkdir(exist_ok=True)
        version = ET.parse(self.repository / "Directory.Build.props").findtext("./PropertyGroup/Version")
        with tempfile.TemporaryDirectory(prefix="host-helper-test-", dir=artifacts) as temporary:
            self.run_command("dotnet", "build", "src/FantasyTale.Launcher.Host",
                             "-c", "Release", "--artifacts-path", temporary, "--nologo")
            binaries = Path(temporary) / "bin"
            host = binaries / "FantasyTale.Launcher.Host/release"
            self.run_command("pwsh", "-NoProfile", "-File", "tools/verify-managed-package.ps1",
                             "-Package", str(host), "-ExpectedVersion", version)
            for helper, folder, resources in (
                ("FantasyTale.Launcher.Overlay", "overlay", (
                    "overlay/index.html", "overlay/panel-stack.js", "overlay/anchors.js",
                    "overlay/locales.json", "overlay/surface-ready.js", "overlay/ui-library/game-ui.js",
                    "Microsoft.Web.WebView2.Wpf.dll")),
                ("FantasyTale.Launcher.Updater", "updater", ("FantasyTale.Launcher.Core.dll",)),
            ):
                built = binaries / helper / "release"
                copied = host / folder
                for name in (helper + ".dll", helper + ".exe", helper + ".deps.json",
                             helper + ".runtimeconfig.json", *resources):
                    with self.subTest(helper=helper, file=name):
                        self.assertTrue((copied / name).is_file(), name)
                        with (built / name).open("rb") as source, (copied / name).open("rb") as destination:
                            self.assertEqual(hashlib.file_digest(source, "sha256").digest(),
                                             hashlib.file_digest(destination, "sha256").digest())
                for path in copied.iterdir():
                    self.assertFalse(path.is_dir() and (path.name.startswith(("win-", "linux-", "osx-"))
                                     or path.name in ("publish", "OverlayWebData")), str(path))


if __name__ == "__main__":
    unittest.main()
