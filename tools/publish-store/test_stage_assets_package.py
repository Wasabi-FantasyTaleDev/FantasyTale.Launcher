import json
import tempfile
import unittest
from pathlib import Path

from publish_plugins import package
from stage_assets_package import stage, validate_pack, validate_ui_resources

MANIFEST = ('schema = 1\n[plugin]\nid = "fixture.previews"\ngame = "fixture"\nversion = "1.0.0"\nkind = "assets"\n'
            'name = { en = "Previews" }\n[compatibility]\nbuilds = ["*"]\n[assets]\nroot = "data"\n')
HASH = "0123456789abcdef0123"


def write_pack(root: Path, *, reference: str = f"atlas/{HASH}.webp") -> Path:
    pack = root / "pack"
    (pack / "atlas").mkdir(parents=True)
    (pack / "builds").mkdir()
    (pack / "atlas" / f"{HASH}.webp").write_bytes(b"RIFF")
    (pack / "pack.json").write_text(json.dumps({"atlases": [reference]}), encoding="utf-8")
    (pack / "builds" / "kr-812.json").write_text(json.dumps({"items": {"1": {"atlas": f"{HASH}.webp"}}}), encoding="utf-8")
    return pack


class AssetsPackageTests(unittest.TestCase):
    def test_staging_copies_the_manifest_and_the_pack_without_touching_the_repository_folder(self):
        with tempfile.TemporaryDirectory() as root_text:
            root = Path(root_text)
            source = root / "source"
            source.mkdir()
            (source / "plugin.toml").write_text(MANIFEST, encoding="utf-8")
            pack = write_pack(root)
            self.assertEqual([], stage(source, pack, root / "staged"))
            self.assertTrue((root / "staged" / "plugin.toml").is_file())
            self.assertEqual(b"RIFF", (root / "staged" / "data" / "atlas" / f"{HASH}.webp").read_bytes())
            self.assertFalse((source / "data").exists())

    def test_a_staged_package_is_publishable_and_an_unstaged_one_is_not(self):
        with tempfile.TemporaryDirectory() as root_text:
            root = Path(root_text)
            source = root / "source"
            source.mkdir()
            (source / "plugin.toml").write_text(MANIFEST, encoding="utf-8")
            with self.assertRaises(ValueError):
                package(source, root / "published-empty")
            stage(source, write_pack(root), root / "staged")
            row, _ = package(root / "staged", root / "published")
            self.assertIn('id = "fixture.previews"', row)

    def test_a_reference_to_a_missing_file_is_rejected(self):
        with tempfile.TemporaryDirectory() as root_text:
            pack = write_pack(Path(root_text), reference="atlas/ffffffffffffffffffff.webp")
            with self.assertRaises(ValueError):
                validate_pack(pack)

    def test_unexpected_entries_and_bad_names_are_rejected(self):
        with tempfile.TemporaryDirectory() as root_text:
            pack = write_pack(Path(root_text))
            (pack / "atlas" / "Not-Hashed.webp").write_bytes(b"x")
            with self.assertRaises(ValueError):
                validate_pack(pack)
            (pack / "atlas" / "Not-Hashed.webp").unlink()
            (pack / "extra.bin").write_bytes(b"x")
            with self.assertRaises(ValueError):
                validate_pack(pack)

    def test_unreferenced_atlas_files_only_warn(self):
        with tempfile.TemporaryDirectory() as root_text:
            pack = write_pack(Path(root_text))
            (pack / "atlas" / "aaaaaaaaaaaaaaaaaaaa.webp").write_bytes(b"x")
            self.assertEqual(["atlas/aaaaaaaaaaaaaaaaaaaa.webp is not referenced by any JSON file"], validate_pack(pack))

    def test_requirements_make_the_catalog_carry_the_definition(self):
        with tempfile.TemporaryDirectory() as root_text:
            root = Path(root_text)
            folder = root / "dependent"
            folder.mkdir()
            definition = ('schema = 1\n[plugin]\nid = "fixture.dependent"\nrequires = ["fixture.previews@>=1.0.0"]\n'
                          'game = "fixture"\nversion = "1.0.0"\nname = { en = "Dependent" }\n[compatibility]\nbuilds = ["*"]\n')
            (folder / "plugin.toml").write_text(definition, encoding="utf-8")
            row, _ = package(folder, root / "published")
            self.assertIn("plugin_definition", row)
            (folder / "plugin.toml").write_text(definition.replace('"fixture.previews@>=1.0.0"', '"../escape"'), encoding="utf-8")
            with self.assertRaises(ValueError):
                package(folder, root / "published-bad")



class UiResourceTests(unittest.TestCase):
    def write(self, root: Path) -> Path:
        ui = root / "ui"
        (ui / "v2").mkdir(parents=True)
        (ui / "fonts").mkdir()
        (ui / "ui.json").write_text("{}", encoding="utf-8")
        (ui / "v2" / "manifest.json").write_text('{"imageFormat": "webp"}', encoding="utf-8")
        (ui / "fonts" / "GB13000.woff2").write_bytes(b"wOF2")
        (ui / "fonts" / "fonts.json").write_text('{"GB13000.ttf": "GB13000.woff2"}', encoding="utf-8")
        return ui

    def test_a_ui_resource_set_is_staged_into_the_data_folder(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "package"
            source.mkdir()
            (source / "plugin.toml").write_text(MANIFEST, encoding="utf-8")
            stage(source, self.write(root), root / "out")
            self.assertTrue((root / "out" / "data" / "v2" / "manifest.json").is_file())
            self.assertTrue((root / "out" / "data" / "fonts" / "GB13000.woff2").is_file())

    def test_a_font_map_naming_a_missing_file_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            ui = self.write(Path(temp))
            (ui / "fonts" / "GB13000.woff2").unlink()
            with self.assertRaises(ValueError):
                validate_ui_resources(ui)

    def test_a_set_without_theme_or_with_strangers_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            ui = self.write(Path(temp))
            (ui / "notes.txt").write_text("x", encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_ui_resources(ui)


if __name__ == "__main__":
    unittest.main()
