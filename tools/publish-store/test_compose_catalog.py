"""Verify unified composition preserves releases and refuses profile rollback."""
from pathlib import Path
import tempfile
import tomllib
import unittest

from compose_catalog import compose


class CatalogCompositionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.catalog = self.root / "source.toml"
        self.catalog.write_text('schema=1\n[catalog]\nid="fixture"\nname={en="Fixture"}\n[[packages]]\nid="fixture.auth"\nkind="plugin"\nversion="1.0.0"\nmanifest="release.toml"\n')
        self.definition = self.root / "build.toml"
        self.definition.write_text('schema=1\n[build]\nid="kr-1"\ngame="fixture"\nexecutable="Game.exe"\n[plugins]\nrequired=["fixture.auth"]\n')
        self.config = self.root / "profiles.toml"
        self.config.write_text('[[profiles]]\ndefinition="build.toml"\nrevision=1\n')
        self.output = self.root / "unified.toml"

    def tearDown(self):
        self.temp.cleanup()

    def test_retirement_records_survive_future_composition_and_are_absent_from_legacy(self):
        self.config.write_text('[[deprecated_plugins]]\nid="fixture.retired"\ngame="fixture"\nname={en="Retired"}\nreason={en="No longer needed"}\n')
        legacy = self.root / "legacy.toml"
        compose(self.catalog, self.config, self.output, legacy)
        first = tomllib.loads(self.output.read_text())
        self.assertEqual("fixture.retired", first["deprecated_plugins"][0]["id"])
        self.assertNotIn("deprecated_plugins", tomllib.loads(legacy.read_text()))
        self.config.write_text('[[profiles]]\ndefinition="build.toml"\nrevision=1\n')
        next_path = self.root / "next.toml"
        compose(self.output, self.config, next_path)
        self.assertEqual(first["deprecated_plugins"], tomllib.loads(next_path.read_text())["deprecated_plugins"])

    def test_github_release_subscription_survives_later_profile_composition(self):
        subscription = '\n[[github_releases]]\nid="fixture.server"\ngame="fixture"\nbuilds=["kr-1"]\nrepository="fixture/server"\nasset="server-win-x64.zip"\nname={en="Server"}\npublisher="Fixture"\n'
        self.config.write_text(subscription)
        compose(self.catalog, self.config, self.output)
        first = tomllib.loads(self.output.read_text())
        self.assertEqual("fixture/server", first["github_releases"][0]["repository"])
        self.config.write_text('[[profiles]]\ndefinition="build.toml"\nrevision=1\n')
        next_path = self.root / "next.toml"
        compose(self.output, self.config, next_path)
        self.assertEqual(first["github_releases"], tomllib.loads(next_path.read_text())["github_releases"])

    def test_profiles_and_packages_share_one_document_with_legacy_projection(self):
        legacy = self.root / "legacy.toml"
        result = compose(self.catalog, self.config, self.output, legacy)
        unified = tomllib.loads(self.output.read_text())
        self.assertEqual(1, result["profiles"])
        self.assertEqual(2, unified["schema"])
        self.assertEqual(tomllib.loads(self.catalog.read_text())["packages"], unified["packages"])
        self.assertIn('required=["fixture.auth"]', unified["profiles"][0]["definition"])
        old = tomllib.loads(legacy.read_text())
        self.assertEqual(1, old["schema"])
        self.assertNotIn("profiles", old)
        self.assertEqual(unified["packages"], old["packages"])

    def test_changed_profile_requires_advancing_revision_and_preserves_previous_output(self):
        compose(self.catalog, self.config, self.output)
        previous = self.output.read_bytes()
        self.definition.write_text(self.definition.read_text().replace("Game.exe", "Next.exe"))
        with self.assertRaisesRegex(ValueError, "advancing"):
            compose(self.output, self.config, self.root / "next.toml")
        self.assertFalse((self.root / "next.toml").exists())
        self.assertEqual(previous, self.output.read_bytes())
        self.config.write_text(self.config.read_text().replace("revision=1", "revision=2"))
        compose(self.output, self.config, self.root / "next.toml")
        self.assertEqual(2, tomllib.loads((self.root / "next.toml").read_text())["profiles"][0]["revision"])

    def test_server_and_room_discovery_is_preserved_with_profiles_and_excluded_from_legacy(self):
        self.config.write_text(self.config.read_text() + '\n[[servers]]\nid="official"\nkind="server"\ngame="fixture"\nname="Official"\napi_url="https://game.example/"\nbuilds=["kr-1"]\n'
            + '\n[[servers]]\nid="room"\nkind="room"\nexpires_at="2026-10-22T12:00:00Z"\ngame="fixture"\nname="Room"\napi_url="https://room.example/"\nbuilds=["kr-1"]\n'
            + '\n[[directory_sources]]\nid="live"\nname="Live"\nbase_url="https://directory.example/"\n')
        legacy = self.root / "legacy.toml"
        result = compose(self.catalog, self.config, self.output, legacy)
        self.assertEqual(2, result["servers"])
        first = tomllib.loads(self.output.read_text())
        self.assertNotIn("servers", tomllib.loads(legacy.read_text()))
        self.config.write_text('[[directory_sources]]\nid="live"\nname="Changed"\nbase_url="https://next.example/"\n')
        next_path = self.root / "next.toml"
        compose(self.output, self.config, next_path)
        updated = tomllib.loads(next_path.read_text())
        self.assertEqual(first["servers"], updated["servers"])
        self.assertEqual(first["profiles"], updated["profiles"])
        self.assertEqual("Changed", updated["directory_sources"][0]["name"])
        self.config.write_text('[[servers]]\nid="duplicate"\n[[servers]]\nid="duplicate"\n')
        with self.assertRaisesRegex(ValueError, "Duplicate discovery"):
            compose(self.output, self.config, self.root / "bad.toml")

    def test_server_profile_reference_and_localized_artwork_survive_composition(self):
        self.config.write_text(self.config.read_text() + '\n[[servers]]\nid="community"\nkind="server"\ngame="fixture"\nname="Community"\napi_url="https://game.example/"\nbuilds=["kr-1"]\nprofile="servers/community.toml"\n'
            + '[servers.presentation.summary_translations]\nzh="Short"\nen="Summary"\n'
            + '[servers.presentation.motd_translations]\nzh="Full"\n'
            + '[servers.presentation.icon_url_translations]\nen="https://images.example/icon.png"\n'
            + '[servers.presentation.images_translations]\nko=["https://images.example/one.png"]\n')
        compose(self.catalog, self.config, self.output)
        server = tomllib.loads(self.output.read_text())["servers"][0]
        self.assertEqual("servers/community.toml", server["profile"])
        self.assertEqual("Full", server["presentation"]["motd_translations"]["zh"])
        self.assertEqual(["https://images.example/one.png"], server["presentation"]["images_translations"]["ko"])

    def test_duplicate_profile_and_existing_destination_are_rejected_before_writes(self):
        self.config.write_text(self.config.read_text() * 2)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            compose(self.catalog, self.config, self.output)
        self.assertFalse(self.output.exists())
        with self.assertRaisesRegex(ValueError, "new catalog"):
            compose(self.catalog, self.config, self.catalog)

    def test_file_safe_server_id_keeps_its_spelling_and_has_no_client_id_length_ceiling(self):
        for identity in ("FantasyTale", "Community_Server-01", "_private", "server_" + "a" * 128):
            with self.subTest(identity=identity):
                self.config.write_text('[[servers]]\nid="' + identity + '"\nkind="server"\n')
                output = self.root / ("catalog-" + str(len(identity)) + ".toml")
                compose(self.catalog, self.config, output)
                self.assertEqual(identity, tomllib.loads(output.read_text())["servers"][0]["id"])
                output.unlink()

    def test_invalid_or_reserved_server_ids_fail_before_catalog_creation(self):
        for identity in ("has space", "\u670d\u52a1\u5668", "caf\u00e9", "server.example", "CON", "lPt9", "../outside"):
            with self.subTest(identity=identity):
                self.config.write_text('[[servers]]\nid="' + identity + '"\n', encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "server identity"):
                    compose(self.catalog, self.config, self.output)
                self.assertFalse(self.output.exists())

    def test_case_insensitive_server_collision_is_rejected_and_override_retains_new_spelling(self):
        self.config.write_text('[[servers]]\nid="Community_Server"\nname="Original"\n')
        compose(self.catalog, self.config, self.output)
        self.config.write_text('[[servers]]\nid="community_SERVER"\nname="Changed"\n')
        updated = self.root / "updated.toml"
        compose(self.output, self.config, updated)
        rows = tomllib.loads(updated.read_text())["servers"]
        self.assertEqual(1, len(rows))
        self.assertEqual("community_SERVER", rows[0]["id"])
        self.assertEqual("Changed", rows[0]["name"])
        self.config.write_text('[[servers]]\nid="Community_Server"\n[[servers]]\nid="community_SERVER"\n')
        with self.assertRaisesRegex(ValueError, "Duplicate discovery"):
            compose(self.catalog, self.config, self.root / "invalid.toml")
        self.assertFalse((self.root / "invalid.toml").exists())


if __name__ == "__main__":
    unittest.main()
