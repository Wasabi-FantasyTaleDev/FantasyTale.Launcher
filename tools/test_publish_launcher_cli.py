"""Exercise catalog validation through the actual managed publication command."""
from pathlib import Path
import subprocess
import tempfile
import unittest


class PublisherCatalogCommandTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.repository = Path(__file__).resolve().parent.parent
        project = cls.repository / "tools/publish-launcher/FantasyTale.Launcher.Publish.csproj"
        subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--no-restore"],
                       cwd=cls.repository, check=True, capture_output=True, text=True, timeout=120)
        cls.command = project.parent / "bin/Release/net10.0/FantasyTale.Launcher.Publish.dll"

    def validate(self, schema):
        with tempfile.TemporaryDirectory() as temporary:
            catalog = Path(temporary) / "source.toml"
            catalog.write_text(f'schema={schema}\n[catalog]\nid="fixture"\nname={{en="Fixture"}}\n# ' + "x" * (4 * 1024 * 1024), encoding="utf-8")
            self.assertGreater(catalog.stat().st_size, 4 * 1024 * 1024)
            return subprocess.run(["dotnet", str(self.command), "--validate-catalog", str(catalog)],
                                  cwd=self.repository, capture_output=True, text=True, timeout=30)

    def test_large_valid_catalog_is_validated_by_the_managed_reader(self):
        result = self.validate(2)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Validated catalog: 0 packages, 0 client profiles.", result.stdout)

    def test_large_catalog_retains_schema_validation(self):
        result = self.validate(3)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("must be 1 or 2", result.stderr)


if __name__ == "__main__":
    unittest.main()
