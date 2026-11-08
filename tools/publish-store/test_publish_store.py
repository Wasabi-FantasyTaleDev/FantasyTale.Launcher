"""Exercise unattended folder publication without a running storage service."""
import hashlib
import json
from pathlib import Path
import tempfile
import tomllib
import unittest

from publish_store import archive_tree, publish, server_version


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="ft-publish-")
        self.root = Path(self.temporary.name)
        self.input = self.root / "input"
        self.input.mkdir()
        (self.input / "client.exe").write_bytes(b"original game executable")
        (self.input / "data.pack").write_bytes(b"large unchanged game data" * 10000)
        self.config = self.root / "publisher.toml"
        self.config.write_text('''[store]
id = "fixture"
name = "Fixture"
output = "./published"
[[clients]]
game = "fixture"
build = "test-1"
name = "Fixture client"
directory = "./input"
exclude = ["*.log"]
''', encoding="utf-8")
        self.output = self.root / "published"

    def tearDown(self):
        self.temporary.cleanup()

    def catalog(self):
        return tomllib.loads((self.output / "FantasyTale/source.toml").read_text(encoding="utf-8"))

    def snapshot(self, package):
        raw = (self.output / package["manifest"]).read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(), package["manifest_sha256"])
        return tomllib.loads(raw.decode("utf-8"))

    def test_changed_file_only_is_rehashed_and_published_without_any_version(self):
        first = publish(self.config)
        self.assertEqual(first, {"hashed_files": 2, "cached_files": 0, "changed_catalog": 1})
        original = self.catalog()["packages"][0]
        self.assertNotIn("version", original)
        self.assertEqual(original["update_mode"], "files")
        snapshot = self.snapshot(original)
        self.assertNotIn("version", snapshot["release"])
        self.assertEqual(publish(self.config), {"hashed_files": 0, "cached_files": 2, "changed_catalog": 0})
        (self.input / "client.exe").write_bytes(b"changed game executable")
        (self.input / "ignored.log").write_bytes(b"do not distribute")
        (self.input / "session.toml").write_text("do not distribute", encoding="utf-8")
        self.assertEqual(publish(self.config), {"hashed_files": 1, "cached_files": 1, "changed_catalog": 1})
        latest = self.catalog()["packages"][0]
        self.assertNotEqual(latest["manifest_sha256"], original["manifest_sha256"])
        files = self.snapshot(latest)["files"]
        self.assertEqual(len(files), 2)
        old_data = next(f for f in snapshot["files"] if f["path"] == "data.pack")
        self.assertEqual(old_data, next(f for f in files if f["path"] == "data.pack"))
        for file in files:
            self.assertEqual(hashlib.sha256((self.output / file["source_path"]).read_bytes()).hexdigest(), file["sha256"])
        # Old objects remain available for an in-flight download pinned before publication.
        for file in snapshot["files"]:
            self.assertTrue((self.output / file["source_path"]).is_file())

    def test_invalid_update_retains_previous_catalog(self):
        publish(self.config)
        previous = (self.output / "FantasyTale/source.toml").read_bytes()
        (self.input / "CON.bin").write_bytes(b"invalid")
        with self.assertRaises(ValueError):
            publish(self.config)
        self.assertEqual((self.output / "FantasyTale/source.toml").read_bytes(), previous)
        self.assertFalse((self.output / ".publisher.lock").exists())

    def test_literal_asset_names_use_safe_content_addresses(self):
        assets = self.input / "Images" / "#Screen"
        assets.mkdir(parents=True)
        (assets / "100%.bfz").write_bytes(b"literal asset")
        publish(self.config)
        files = self.snapshot(self.catalog()["packages"][0])["files"]
        asset = next(row for row in files if row["path"] == "Images/#Screen/100%.bfz")
        self.assertNotIn("#", asset["source_path"])
        self.assertNotIn("%", asset["source_path"])
        self.assertEqual((self.output / asset["source_path"]).read_bytes(), b"literal asset")

    def test_full_check_and_changed_published_object_bypass_cache(self):
        publish(self.config)
        self.assertEqual(publish(self.config, full_check=True)["hashed_files"], 2)
        package = self.catalog()["packages"][0]
        file = self.snapshot(package)["files"][0]
        target = self.output / file["source_path"]
        target.write_bytes(b"corrupt")
        self.assertEqual(publish(self.config)["hashed_files"], 1)
        self.assertEqual(hashlib.sha256(target.read_bytes()).hexdigest(), file["sha256"])

    def test_server_shares_catalog_and_cache_but_excludes_deployment_data(self):
        server = self.root / "server"
        (server / "profiles/cn-406").mkdir(parents=True)
        (server / "server.exe").write_bytes(b"server executable")
        (server / "profiles/cn-406/profile.yaml").write_text("profile: fixture", encoding="utf-8")
        for relative in [".env", "save.sqlite", "save.sqlite-wal", "private.pem", "profiles/cn-406/local.patch.yaml"]:
            (server / relative).write_text("private fixture", encoding="utf-8")
        with self.config.open("a", encoding="utf-8") as config:
            config.write('\n[[servers]]\nid = "fixture.server"\ngame = "fixture"\nbuilds = ["test-1", "test-2"]\nversion = "2.25.0"\nname = "Fixture server"\ndirectory = "./server"\n')
        self.assertEqual(publish(self.config)["hashed_files"], 4)
        package = self.catalog()["packages"][1]
        self.assertEqual(package["kind"], "server")
        self.assertEqual(package["update_mode"], "release")
        self.assertEqual(package["builds"], ["test-1", "test-2"])
        snapshot = self.snapshot(package)
        self.assertEqual(snapshot["release"]["builds"], package["builds"])
        self.assertEqual({f["path"] for f in snapshot["files"]}, {"server.exe", "profiles/cn-406/profile.yaml"})
        self.assertEqual(publish(self.config)["cached_files"], 4)
        previous = (self.output / "FantasyTale/source.toml").read_bytes()
        self.config.write_text(self.config.read_text(encoding="utf-8").replace('version = "2.25.0"', 'version = "invalid"'), encoding="utf-8")
        with self.assertRaises(ValueError):
            publish(self.config)
        self.assertEqual((self.output / "FantasyTale/source.toml").read_bytes(), previous)

    def test_server_only_publication_does_not_require_client_entries(self):
        self.config.write_text(self.config.read_text(encoding="utf-8").replace("[[clients]]", "[[servers]]")
                               .replace('build = "test-1"', 'id = "fixture.server"\nbuilds = ["test-1"]\nversion = "1.0.0"'), encoding="utf-8")
        self.assertEqual(publish(self.config)["hashed_files"], 2)
        self.assertEqual(self.catalog()["packages"][0]["kind"], "server")

    def test_server_versions_match_bounded_semver_contract(self):
        for version in ["1.0.0", "2.25.0-preview.1+build.4"]:
            self.assertEqual(server_version(version), version)
        for version in ["01.0.0", "1.0.0-01", "1.0.0-a..b", "1.0.0+", "4294967296.0.0"]:
            with self.assertRaises(ValueError):
                server_version(version)

    def test_duplicate_content_objects_do_not_invalidate_other_inputs_cache(self):
        (self.input / "duplicate.exe").write_bytes((self.input / "client.exe").read_bytes())
        self.assertEqual(publish(self.config)["hashed_files"], 3)
        for _ in range(2):
            self.assertEqual(publish(self.config), {"hashed_files": 0, "cached_files": 3, "changed_catalog": 0})

    def test_multiple_clients_share_one_catalog_with_independent_snapshots(self):
        other = self.root / "second"
        other.mkdir()
        (other / "another.exe").write_bytes(b"second build")
        with self.config.open("a", encoding="utf-8") as config:
            config.write('\n[[clients]]\ngame = "fixture"\nbuild = "test-2"\nname = "Another client"\ndirectory = "./second"\nlabel = "Optional label"\n')
        self.assertEqual(publish(self.config)["hashed_files"], 3)
        self.assertEqual(len(self.catalog()["packages"]), 2)
        self.assertEqual(self.catalog()["packages"][1]["version"], "Optional label")

    def test_archive_families_have_one_transport_mode_and_pinned_order(self):
        for names, first, codec in [
            (["game.zip"], "game.zip", "zip"),
            (["game.7z.001", "game.7z.002"], "game.7z.001", "7z"),
            (["game.zip.001", "game.zip.002"], "game.zip.001", "zip"),
            (["game.part01.rar", "game.part02.rar"], "game.part01.rar", "rar"),
            (["game.rar", "game.r00", "game.r01"], "game.rar", "rar"),
            (["game.rar", "game.r00", "game.r01"], "game.r00", "rar"),
            (["game.z01", "game.z02", "game.zip"], "game.zip", "zip"),
        ]:
            with self.subTest(codec=codec, first=first):
                folder = self.root / (first.replace(".", "-") + str(len(list(self.root.iterdir()))))
                folder.mkdir()
                for name in names:
                    (folder / name).write_bytes(name.encode())
                (folder / "unrelated.bin").write_bytes(b"not distributed")
                actual_codec, tree = archive_tree(folder / first)
                self.assertEqual(actual_codec, codec)
                self.assertEqual([row[0] for row in tree], names)

    def test_missing_and_duplicate_volume_numbers_are_rejected(self):
        for names in [["a.7z.002"], ["a.part01.rar", "a.part03.rar"],
                      ["a.zip.001", "a.zip.1"], ["a.z01"]]:
            with self.subTest(names=names):
                folder = self.root / names[0]
                folder.mkdir()
                for name in names:
                    (folder / name).write_bytes(b"part")
                with self.assertRaises(ValueError):
                    archive_tree(folder / names[0])

    def test_archive_publication_keeps_optional_password_in_snapshot_only(self):
        for index in (1, 2):
            (self.input / f"game.7z.{index:03}").write_bytes(f"part-{index}".encode())
        self.config.write_text('''[store]
id = "fixture"
name = "Fixture"
output = "./published"
[[clients]]
game = "fixture"
build = "test-1"
name = "Fixture client"
archive = "./input/game.7z.001"
archive_root = "Game"
password = "test-only"
''', encoding="utf-8")
        self.assertEqual(publish(self.config)["hashed_files"], 2)
        package = self.catalog()["packages"][0]
        self.assertNotIn("password", package)
        snapshot = self.snapshot(package)
        self.assertEqual(snapshot["release"]["format"], "archive")
        self.assertEqual(snapshot["release"]["archive_format"], "7z")
        self.assertEqual(snapshot["release"]["password"], "test-only")
        self.assertEqual(snapshot["release"]["archive_root"], "Game")
        self.assertEqual([row["volume"] for row in snapshot["files"]], [1, 2])
        self.assertEqual(publish(self.config)["cached_files"], 2)


if __name__ == "__main__":
    unittest.main()
