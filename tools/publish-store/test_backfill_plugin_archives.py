"""Check that ZIP backfill preserves published releases and refuses unverified payloads."""
import hashlib
from pathlib import Path
import tempfile
import tomllib
import unittest
import zipfile

from backfill_plugin_archives import archive_offer, add_offers
from publish_plugins import package


class PluginArchiveBackfillTests(unittest.TestCase):
    def fixture(self, root, archived=False):
        folder, output = root / 'plugin', root / 'published'
        folder.mkdir()
        (folder / 'plugin.toml').write_text('schema=1\n[plugin]\nid="fixture.plugin"\ngame="fixture"\nversion="1.0.0"\nname={en="Fixture"}\n[compatibility]\nbuilds=["test-1"]\n', encoding='utf-8')
        (folder / 'translations').mkdir()
        (folder / 'translations' / 'text.xml').write_text('中文 한국어 Tiếng Việt', encoding='utf-16')
        row, objects = package(folder, output, archive=archived)
        entry = tomllib.loads(row)['packages'][0]
        entry.pop('plugin_archive')
        return entry, objects[entry['manifest']].read_bytes(), objects

    def test_file_and_zip_backfills_preserve_every_member_and_identity(self):
        for archived in (False, True):
            with self.subTest(archived=archived), tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                entry, raw, objects = self.fixture(root, archived)
                offer = archive_offer(entry, raw, root / 'backfill', lambda row: objects[row['source_path']])
                target = root / 'backfill' / offer['source_path']
                self.assertEqual(offer['sha256'], hashlib.sha256(target.read_bytes()).hexdigest())
                with zipfile.ZipFile(target) as archive:
                    self.assertEqual({'plugin.toml', 'translations/text.xml'}, set(archive.namelist()))
                    self.assertEqual('中文 한국어 Tiếng Việt', archive.read('translations/text.xml').decode('utf-16'))

    def test_catalog_registration_is_additive_and_idempotent_with_nested_tables(self):
        original = b'schema=2\n[catalog]\nid="fixture"\n[[packages]]\nid="fixture.plugin"\nversion="1.0.0"\nkind="plugin"\n[packages.name]\nen="Fixture"\n[[servers]]\nid="server"\n'
        offers = {('fixture.plugin', '1.0.0'): dict(source_path='plugins/fixture.zip', size=42, sha256='a' * 64)}
        updated = add_offers(original, offers)
        self.assertEqual(updated, add_offers(updated, offers))
        before, after = tomllib.loads(original.decode()), tomllib.loads(updated.decode())
        self.assertEqual(offers['fixture.plugin', '1.0.0'], after['packages'][0].pop('plugin_archive'))
        self.assertEqual(before, after)
        offers['fixture.plugin', '1.0.0']['sha256'] = 'b' * 64
        with self.assertRaisesRegex(ValueError, 'identity differs'):
            add_offers(updated, offers)

    def test_corrupt_payload_and_changed_manifest_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            entry, raw, objects = self.fixture(root)
            with self.assertRaisesRegex(ValueError, 'digest mismatch'):
                archive_offer(entry, raw + b' ', root / 'backfill', lambda row: objects[row['source_path']])
            member = next(path for key, path in objects.items() if key.startswith('FantasyTale/files/'))
            member.write_bytes(b'corrupt')
            with self.assertRaisesRegex(ValueError, 'verification'):
                archive_offer(entry, raw, root / 'backfill', lambda row: objects[row['source_path']])


if __name__ == '__main__':
    unittest.main()
