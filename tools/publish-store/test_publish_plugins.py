import tempfile
from pathlib import Path
import tomllib
import unittest
import hashlib
import zipfile
from publish_plugins import package, merge, retire, group_catalog_tables, import_archive_path


class PluginPublicationTests(unittest.TestCase):
    def test_retirement_keeps_tombstones_when_the_last_package_is_withdrawn(self):
        document = (b'schema=2\n[catalog]\nid="fixture"\n[[packages]]\n'
                    b'id="bubblefighter.fantasy.legacy-security-compatibility"\nversion="0.1.0"\n'
                    b'[[deprecated_plugins]]\nid="fixture.retired"\ngame="fixture"\nname={en="Retired"}\n')
        updated = tomllib.loads(retire(document, ['bubblefighter.fantasy.legacy-security-compatibility']).decode())
        self.assertFalse(updated.get('packages'))
        self.assertEqual('fixture.retired', updated['deprecated_plugins'][0]['id'])

    def test_retired_plugin_policy_prevents_accidental_republication(self):
        with tempfile.TemporaryDirectory() as root:
            folder = Path(root) / "package"
            folder.mkdir()
            (folder / "plugin.toml").write_text('schema=1\n[plugin]\nid="bubblefighter.fantasy.dao-classic-model"\n'
                'game="bubblefighter"\nversion="9.0.0"\nname={en="Retired"}\n[compatibility]\nbuilds=["*"]\n', encoding='utf-8')
            with self.assertRaises(ValueError):
                package(folder, Path(root) / "published")

    def test_renamed_plugin_advertises_its_definition_before_installation(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            definition = ('schema = 1\n[plugin]\nid = "fixture.current"\naliases = ["fixture.former"]\n'
                          'game = "fixture"\nversion = "1.0.1"\nname = { en = "Renamed" }\n'
                          '[compatibility]\nbuilds = ["*"]\n')
            (folder / "plugin.toml").write_text(definition, encoding="utf-8")
            row, objects = package(folder, output)
            advertised = tomllib.loads(row)["packages"][0]
            self.assertEqual(definition, advertised["plugin_definition"])
            self.assertEqual(["fixture.former"], tomllib.loads(advertised["plugin_definition"])["plugin"]["aliases"])
            with zipfile.ZipFile(objects[advertised["plugin_archive"]["source_path"]]) as archive:
                self.assertEqual((folder / "plugin.toml").read_bytes(), archive.read("plugin.toml"))
                self.assertEqual(tomllib.loads(definition), tomllib.loads(archive.read("plugin.toml").decode("utf-8")))

    def test_renamed_plugin_rejects_ambiguous_or_invalid_former_identifiers(self):
        with tempfile.TemporaryDirectory() as root:
            folder = Path(root) / "package"
            folder.mkdir()
            for aliases in ('["fixture.current"]', '["fixture.former", "fixture.former"]', '["../former"]', '"former"'):
                with self.subTest(aliases=aliases):
                    (folder / "plugin.toml").write_text('schema = 1\n[plugin]\nid = "fixture.current"\n'
                        f'aliases = {aliases}\ngame = "fixture"\nversion = "1.0.1"\nname = {{ en = "Renamed" }}\n'
                        '[compatibility]\nbuilds = ["*"]\n', encoding="utf-8")
                    with self.assertRaises(ValueError):
                        package(folder, Path(root) / "published")

    def test_import_zip_path_keeps_versions_separate_without_nested_version_directories(self):
        self.assertEqual('FantasyTale/plugins/fixture/fixture.plugin-1.0.0.zip',
                         import_archive_path('fixture', 'fixture.plugin', '1.0.0'))
        self.assertNotEqual(import_archive_path('fixture', 'fixture.plugin', '1.0.0'),
                            import_archive_path('fixture', 'fixture.plugin', '1.0.1'))

    def test_archive_release_is_deterministic_and_contains_every_member(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            definition = b'schema = 1\n[plugin]\nid = "fixture.plugin"\ngame = "fixture"\nversion = "1.0.0"\nname = { en = "Fixture" }\n[compatibility]\nbuilds = ["*"]\n'
            (folder / "plugin.toml").write_bytes(definition)
            (folder / "payload").mkdir()
            (folder / "payload/member").write_bytes(b"verified model")
            row, objects = package(folder, output, archive=True)
            entry = tomllib.loads(row)['packages'][0]
            release = tomllib.loads(objects[entry['manifest']].read_text())
            self.assertEqual('zip', release['release']['format'])
            self.assertEqual(1, len(release['files']))
            member = release['files'][0]
            self.assertEqual(entry['plugin_archive']['source_path'], member['source_path'])
            self.assertEqual(2, len(objects))
            path = objects[member['source_path']]
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), member['sha256'])
            self.assertEqual(path.stat().st_size, member['size'])
            with zipfile.ZipFile(path) as zipped:
                self.assertEqual({'plugin.toml', 'payload/member'}, set(zipped.namelist()))
                self.assertEqual(definition, zipped.read('plugin.toml'))
                self.assertEqual(b'verified model', zipped.read('payload/member'))
                self.assertIsNone(zipped.testzip())
            self.assertEqual(row, package(folder, output, archive=True)[0])

    def test_model_predecessors_require_a_published_replacement_before_retirement(self):
        before = b'schema = 2\n[catalog]\nid = "fixture"\n[[packages]]\nid = "bubblefighter.fantasy.dao-classic-model"\nversion = "1.0.0"\n'
        with self.assertRaisesRegex(ValueError, 'replacement'):
            retire(before, ['bubblefighter.fantasy.dao-classic-model'])
        replacement = b'\n[[packages]]\nid = "bubblefighter.fantasy.character-models"\nversion = "1.0.0"\n'
        updated = retire(before + replacement, ['bubblefighter.fantasy.dao-classic-model'])
        self.assertEqual(['bubblefighter.fantasy.character-models'], [row['id'] for row in tomllib.loads(updated.decode())['packages']])

    def test_chinese_authentication_requires_deploying_the_passport_table(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            manifest = folder / "plugin.toml"
            definition = ('schema = 1\n[plugin]\nid = "bubblefighter.fantasy.login-fix-cn"\ngame = "bubblefighter"\n'
                          'version = "1.0.18"\nkind = "files"\nname = { en = "Authentication" }\n'
                          '[compatibility]\nbuilds = ["cn-406"]\n')
            for source in ("gpk_stub.dll", "login_bridge.dll"):
                content = source.encode()
                (folder / source).write_bytes(content)
                definition += (f'[[files]]\nsource = "{source}"\ntarget = "{source}"\nsize = {len(content)}\n'
                               f'sha256 = "{hashlib.sha256(content).hexdigest()}"\n')
            manifest.write_text(definition, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "missing required package asset: native-passports.toml"):
                package(folder, output)
            (folder / "native-passports.toml").write_bytes(b"schema = 1\n")
            with self.assertRaisesRegex(ValueError, "does not deploy required package asset: native-passports.toml"):
                package(folder, output)

    def test_authentication_package_requires_profile_and_publishes_it(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            (folder / "plugin.toml").write_text(
                'schema = 1\n[plugin]\nid = "bubblefighter.fantasy.login-fix-kr"\ngame = "bubblefighter"\n'
                'version = "0.1.4"\nkind = "native"\nname = { en = "Authentication" }\n'
                '[native]\nlibrary = "kr_auth.dll"\n[compatibility]\nbuilds = ["kr-1256"]\n', encoding="utf-8")
            (folder / "kr_auth.dll").write_bytes(b"fixture")
            with self.assertRaisesRegex(ValueError, "native-login.toml"):
                package(folder, output)
            profile = b'schema = 1\n[client]\nbuild = "kr-1256"\n'
            (folder / "native-login.toml").write_bytes(profile)
            row, objects = package(folder, output)
            metadata = tomllib.loads(row)["packages"][0]
            release = tomllib.loads((output / metadata["manifest"]).read_text(encoding="utf-8"))
            member = next(item for item in release["files"] if item["path"] == "native-login.toml")
            self.assertEqual(profile, objects[member["source_path"]].read_bytes())
            (folder / "kr_auth.dll").unlink()
            with self.assertRaisesRegex(ValueError, "kr_auth.dll"):
                package(folder, output)

    def test_shared_components_publish_all_builds_and_keep_launcher_requirement(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            definition = ('schema = 1\n[plugin]\nid = "fixture.component"\ngame = "fixture"\n'
                          'version = "1.0.0"\nminimum_launcher_version = "0.1.14"\n'
                          'name = { en = "Component" }\n[compatibility]\nbuilds = ["*"]\n')
            (folder / "plugin.toml").write_text(definition, encoding="utf-8")
            row, _ = package(folder, output)
            metadata = tomllib.loads(row)["packages"][0]
            self.assertEqual([], metadata["builds"])
            self.assertEqual("0.1.14", metadata["minimum_launcher_version"])
            release = tomllib.loads((output / metadata["manifest"]).read_text(encoding="utf-8"))
            self.assertEqual([], release["release"]["builds"])
            (folder / "plugin.toml").write_text(definition + 'exclude = ["test-1"]\n', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "excluded"):
                package(folder, output)

    def test_merge_preserves_existing_packages_and_is_idempotent(self):
        previous = b'schema = 1\n[catalog]\nid = "fixture"\n[[packages]]\nid = "existing"\nkind = "client"\n'
        row = '\n[[packages]]\nid = "fixture.plugin"\nversion = "1.0.0"\nmanifest_sha256 = "a"\n'
        updated = merge(previous, [row])
        self.assertTrue(updated.startswith(previous))
        self.assertEqual(2, len(tomllib.loads(updated.decode())["packages"]))
        self.assertEqual(updated, merge(updated, [row]))
        with self.assertRaises(ValueError):
            merge(updated, [row.replace('"a"', '"b"')])

    def test_payload_is_content_addressed_and_rejects_development_files(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            (folder / "plugin.toml").write_text('schema = 1\n[plugin]\nid = "fixture.plugin"\ngame = "fixture"\nversion = "1.0.0"\nname = { en = "Fixture" }\n[compatibility]\nbuilds = ["kr-812"]\n', encoding="utf-8")
            row, objects = package(folder, output)
            self.assertEqual(3, len(objects))
            metadata = tomllib.loads(row)["packages"][0]
            archive = metadata['plugin_archive']
            self.assertTrue(archive['source_path'].endswith('/fixture.plugin-1.0.0.zip'))
            with zipfile.ZipFile(objects[archive['source_path']]) as zipped:
                self.assertEqual(['plugin.toml'], zipped.namelist())
                self.assertEqual((folder / 'plugin.toml').read_bytes(), zipped.read('plugin.toml'))
            self.assertEqual(hashlib.sha256(objects[archive['source_path']].read_bytes()).hexdigest(), archive['sha256'])
            self.assertEqual([], metadata["authors"])
            self.assertNotIn("uploader", metadata)
            definition = folder / "plugin.toml"
            definition.write_text(definition.read_text().replace('[plugin]\n', '[plugin]\nauthors = ["Fantasy Tale Studio"]\n'), encoding="utf-8")
            credited, _ = package(folder, output, uploader="Contributor")
            credits = tomllib.loads(credited)["packages"][0]
            self.assertEqual(["Fantasy Tale Studio"], credits["authors"])
            self.assertEqual("Contributor", credits["uploader"])
            with self.assertRaisesRegex(ValueError, "uploader"):
                package(folder, output, uploader="\ninvalid")
            release = tomllib.loads((output / metadata["manifest"]).read_text(encoding="utf-8"))
            self.assertEqual("plugin.toml", release["files"][0]["path"])
            self.assertTrue((output / release["files"][0]["source_path"]).is_file())
            (folder / "debug.pdb").write_bytes(b"private")
            with self.assertRaises(ValueError):
                package(folder, output)

    def test_append_groups_packages_before_profiles_and_preserves_all_metadata(self):
        previous = (b'schema = 2\n[catalog]\nid = "fixture"\n'
                    b'[[packages]]\nid = "existing.client"\nversion = "1.0.0"\n'
                    b'[[profiles]]\ngame = "fixture"\nbuild = "build"\nrevision = 3\ndefinition = "unchanged"\n'
                    b'[[servers]]\nid = "community"\n')
        row = '\n[[packages]]\nid = "fixture.plugin"\nversion = "1.0.0"\nmanifest_sha256 = "a"\n'
        updated = merge(previous, [row])
        expected = tomllib.loads((previous + row.encode()).decode())
        self.assertEqual(expected, tomllib.loads(updated.decode()))
        self.assertLess(updated.rindex(b'[[packages]]'), updated.index(b'[[profiles]]'))
        self.assertEqual(updated, merge(updated, [row]))
        self.assertEqual(updated, group_catalog_tables(previous + row.encode()))

    def test_retiring_preview_preserves_other_versions_and_metadata(self):
        source = (b'schema = 1\n[catalog]\nid = "fixture"\n'
                  b'[[packages]]\nid = "bubblefighter.fantasy.auto-dash-cn2011-preview"\nversion = "0.1.1"\n'
                  b'[[packages]]\nid = "bubblefighter.fantasy.auto-dash"\nversion = "0.2.0"\n'
                  b'[packages.name]\nen = "Auto Dash"\n')
        updated = retire(source, ["bubblefighter.fantasy.auto-dash-cn2011-preview"])
        parsed = tomllib.loads(updated.decode())
        self.assertEqual("fixture", parsed["catalog"]["id"])
        self.assertEqual([{"id": "bubblefighter.fantasy.auto-dash", "version": "0.2.0",
                           "name": {"en": "Auto Dash"}}], parsed["packages"])
        self.assertEqual(updated, retire(updated, ["bubblefighter.fantasy.auto-dash-cn2011-preview"]))
        self.assertEqual(source, retire(source, []))
        with self.assertRaises(ValueError):
            retire(source, ["bubblefighter.fantasy.auto-dash"])

    def test_research_plugins_cannot_be_published(self):
        with tempfile.TemporaryDirectory() as root:
            folder, output = Path(root) / "package", Path(root) / "published"
            folder.mkdir()
            (folder / "plugin.toml").write_text(
                'schema = 1\n[plugin]\nid = "bubblefighter.fantasy.forward-jump"\n'
                'game = "bubblefighter"\nversion = "0.1.1"\n'
                'name = { en = "Forward Jump" }\n[compatibility]\n'
                'builds = ["cn-279"]\n', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "not publishable"):
                package(folder, output)


if __name__ == "__main__":
    unittest.main()
