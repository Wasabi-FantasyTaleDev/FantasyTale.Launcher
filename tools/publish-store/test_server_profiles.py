"""Exercise portable server publication references before any network writes."""
import hashlib
from pathlib import Path
import tempfile
import unittest

from server_profiles import server_profiles
from upload_alist import graph


class ServerProfilePublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "FantasyTale").mkdir()
        (self.root / "servers").mkdir()
        self.catalog = self.root / "FantasyTale/source.toml"
        self.header = 'schema=2\n[catalog]\nid="fixture"\nname={en="Fixture"}\n'
        self.advertisement = '\n[[servers]]\nid="Community_Server"\ngame="fixture"\nname="Community"\napi_url="https://game.example/"\nhost="game.example"\nport=10101\nbuilds=["kr-1"]\nprofile="servers/Community_Server.toml"\n'
        self.content = 'schema=1\n[server]\nid="Community_Server"\ngame="fixture"\nname="Full name"\napi_url="https://game.example/"\nhost="game.example"\nport=10101\nbuilds=["kr-1"]\n[presentation.summary_translations]\nen="Short"\n[presentation.motd_translations]\nen="Full introduction"\n'
        self.profile = self.root / "servers/Community_Server.toml"
        self.catalog.write_text(self.header + self.advertisement, encoding="utf-8")
        self.profile.write_text(self.content, encoding="utf-8")

    def test_profile_only_catalog_resolves_the_source_root_and_pins_exact_bytes(self):
        (self.root / "servers/unreferenced.toml").write_text('password="private"')
        self.assertEqual([], graph(self.root, "FantasyTale/source.toml"))
        self.assertEqual([("servers/Community_Server.toml", hashlib.sha256(self.profile.read_bytes()).hexdigest())], server_profiles(self.root, "FantasyTale/source.toml"))

    def test_inline_server_and_directory_only_catalogs_need_no_payload_files(self):
        for rows in (self.advertisement.replace('profile="servers/Community_Server.toml"\n', ''), '[[directory_sources]]\nid="live"\nname="Live"\nbase_url="https://directory.example/"\n'):
            with self.subTest(rows=rows):
                self.catalog.write_text(self.header + rows)
                self.assertEqual([], graph(self.root, "FantasyTale/source.toml"))
                self.assertEqual([], server_profiles(self.root, "FantasyTale/source.toml"))
        self.catalog.write_text(self.header)
        with self.assertRaisesRegex(ValueError, "nonempty"):
            graph(self.root, "FantasyTale/source.toml")

    def test_missing_reference_fails_instead_of_silently_omitting_the_profile(self):
        self.profile.unlink()
        with self.assertRaises(FileNotFoundError):
            server_profiles(self.root, "FantasyTale/source.toml")

    def test_identity_game_api_and_game_endpoint_must_match_the_listing(self):
        for original, replacement in (('id="Community_Server"', 'id="Other"'), ('game="fixture"', 'game="other"'), ('https://game.example/', 'https://other.example/'), ('port=10101', 'port=10102'), ('host="game.example"', 'host="other.example"')):
            with self.subTest(replacement=replacement):
                self.profile.write_text(self.content.replace(original, replacement))
                with self.assertRaisesRegex(ValueError, "differs from its advertisement"):
                    server_profiles(self.root, "FantasyTale/source.toml")

    def test_equivalent_api_default_port_and_trailing_slash_remain_compatible(self):
        self.profile.write_text(self.content.replace('https://game.example/', 'https://GAME.example:443'))
        self.assertEqual(1, len(server_profiles(self.root, "FantasyTale/source.toml")))

    def test_secrets_and_bundled_authority_cannot_enter_portable_profiles(self):
        for suffix in ('\n[credentials]\npassword="private"\n', '\n[unrecognized]\nvalue="private"\n'):
            self.profile.write_text(self.content + suffix)
            with self.assertRaisesRegex(ValueError, "portable server profile document"):
                server_profiles(self.root, "FantasyTale/source.toml")
        self.profile.write_text(self.content.replace('[server]', '[server]\nicon_asset="private.png"'))
        with self.assertRaisesRegex(ValueError, "portable server profile fields"):
            server_profiles(self.root, "FantasyTale/source.toml")

    def test_profile_references_cannot_escape_or_alias_the_catalog_or_content(self):
        for path in ("../outside.toml", "FantasyTale/source.toml", "FantasyTale/objects/profile.toml", ".fantasytale-private.toml", ".FANTASYTALE-private.toml", "servers/profile.txt"):
            with self.subTest(path=path):
                self.catalog.write_text(self.header + self.advertisement.replace('servers/Community_Server.toml', path))
                with self.assertRaises(ValueError):
                    server_profiles(self.root, "FantasyTale/source.toml")
        self.catalog.write_text(self.header + self.advertisement)
        with self.assertRaisesRegex(ValueError, "conflicts with publication content"):
            server_profiles(self.root, "FantasyTale/source.toml", [("servers/Community_Server.toml", "0" * 64)])

    def test_rooms_and_case_only_duplicate_servers_cannot_share_profile_references(self):
        self.catalog.write_text(self.header + self.advertisement.replace('[[servers]]', '[[servers]]\nkind="room"'))
        with self.assertRaisesRegex(ValueError, "Only persistent"):
            server_profiles(self.root, "FantasyTale/source.toml")
        self.catalog.write_text(self.header + self.advertisement + self.advertisement.replace('Community_Server', 'community_SERVER'))
        with self.assertRaisesRegex(ValueError, "Duplicate server profile identity"):
            server_profiles(self.root, "FantasyTale/source.toml")

    def test_bom_encoded_profile_retains_its_exact_transport_identity(self):
        self.profile.write_bytes(b"\xef\xbb\xbf" + self.content.encode())
        self.assertEqual(hashlib.sha256(self.profile.read_bytes()).hexdigest(), server_profiles(self.root, "FantasyTale/source.toml")[0][1])

    def test_invalid_schema_and_api_ports_are_rejected_before_upload(self):
        for original, replacement in (("schema=1", "schema=true"), ("https://game.example/", "https://game.example:0/"), ("https://game.example/", "https://game.example:65536/")):
            with self.subTest(replacement=replacement):
                self.profile.write_text(self.content.replace(original, replacement))
                with self.assertRaises(ValueError):
                    server_profiles(self.root, "FantasyTale/source.toml")

    def test_advertisements_must_be_table_rows(self):
        for rows in ('servers="invalid"\n', 'servers=["invalid"]\n'):
            self.catalog.write_text('schema=2\n' + rows + '[catalog]\nid="fixture"\n')
            with self.assertRaisesRegex(ValueError, "table rows"):
                server_profiles(self.root, "FantasyTale/source.toml")


if __name__ == "__main__":
    unittest.main()
