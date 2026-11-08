"""Exercise repository version guards without publishing or replacing an installation."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


class LauncherVersionTests(unittest.TestCase):
    repository = Path(__file__).resolve().parent.parent

    def run_command(self, *command):
        return subprocess.run(command, cwd=self.repository, capture_output=True,
                              text=True, encoding="utf-8", errors="replace", timeout=60)

    def test_normal_build_uses_repository_version(self):
        result = self.run_command("dotnet", "msbuild", "src/FantasyTale.Launcher.Core",
                                  "-t:ValidateRepositoryVersion", "-nologo")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_build_refuses_a_separate_version_override(self):
        result = self.run_command("dotnet", "msbuild", "src/FantasyTale.Launcher.Core",
                                  "-t:ValidateRepositoryVersion", "-p:Version=999.0.0", "-nologo")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("must match Directory.Build.props", result.stdout + result.stderr)

    def test_packaging_refuses_mismatch_before_creating_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "package"
            result = self.run_command("pwsh", "-NoProfile", "-File", "tools/package-launcher.ps1",
                                      "-Output", str(output), "-Version", "999.0.0")
            self.assertNotEqual(0, result.returncode)
            self.assertIn("Directory.Build.props", result.stdout + result.stderr)
            self.assertFalse(output.exists())

    def test_distribution_checks_actual_product_version(self):
        version = ET.parse(self.repository / "Directory.Build.props").findtext("./PropertyGroup/Version")
        names = ["FantasyTale.Launcher", "FantasyTale.Launcher.Sdk", "FantasyTale.Launcher.Core",
                 "FantasyTale.Launcher.Downloads", "FantasyTale.Launcher.Communication",
                 "FantasyTale.Launcher.Games.BubbleFighter", "updater/FantasyTale.Launcher.Updater",
                 "overlay/FantasyTale.Launcher.Overlay", "overlay/FantasyTale.Launcher.Sdk",
                 "updater/FantasyTale.Launcher.Core", "updater/FantasyTale.Launcher.Downloads",
                 "updater/FantasyTale.Launcher.Sdk"]
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary) / "fixture"
            fixture.mkdir()
            project = fixture / "VersionFixture.csproj"
            project.write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<TargetFramework>net10.0</TargetFramework>'
                f'<Version>{version}</Version>'
                '</PropertyGroup></Project>', encoding="utf-8")
            build = self.run_command("dotnet", "build", str(project), "-c", "Release",
                                     "-o", str(fixture / "output"), "--nologo")
            self.assertEqual(0, build.returncode, build.stdout + build.stderr)
            assembly = fixture / "output/VersionFixture.dll"
            for name in names:
                path = Path(temporary) / (name + ".dll")
                path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(assembly, path)
            command = ("pwsh", "-NoProfile", "-File", "tools/verify-managed-package.ps1",
                       "-Package", temporary, "-ExpectedVersion")
            valid = self.run_command(*command, version)
            self.assertEqual(0, valid.returncode, valid.stdout + valid.stderr)
            invalid = self.run_command(*command, "999.0.0")
            self.assertNotEqual(0, invalid.returncode)
            self.assertIn("Incorrect release label", invalid.stdout + invalid.stderr)
            project.write_text(project.read_text(encoding="utf-8").replace(
                f"<Version>{version}</Version>", "<Version>999.0.0</Version>"), encoding="utf-8")
            stale = self.run_command("dotnet", "build", str(project), "-c", "Release",
                                     "-o", str(fixture / "stale"), "--nologo")
            self.assertEqual(0, stale.returncode, stale.stdout + stale.stderr)
            for dependency in ("overlay/FantasyTale.Launcher.Sdk", "updater/FantasyTale.Launcher.Core"):
                with self.subTest(dependency=dependency):
                    path = Path(temporary) / (dependency + ".dll")
                    shutil.copyfile(fixture / "stale/VersionFixture.dll", path)
                    rejected = self.run_command(*command, version)
                    self.assertNotEqual(0, rejected.returncode)
                    self.assertIn("Mixed release assemblies", rejected.stdout + rejected.stderr)
                    shutil.copyfile(assembly, path)


if __name__ == "__main__":
    unittest.main()
