"""Protect independent scene plans, original shard ownership and package inputs."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import tomllib
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('scene_maker', Path(__file__).parents[1] / 'package-scene-styles.py')
maker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(maker)


class ScenePlanTests(unittest.TestCase):
    def test_replacement_retains_existing_shard_and_spelling(self):
        row = dict(path='Scenes/stage_Main/track.WB', sha256='winter')
        old = dict(path='scenes/STAGE_MAIN/track.wb', archive='Pack/Scenes0000.pkn', sha256='stock')
        result = maker.changes([row], {row['path'].casefold(): old}, {'Scenes/stage_Main/': 'Pack/Scenes0001.pkn'})
        self.assertEqual(result, [dict(path=old['path'], archive=old['archive'], sha256='winter', original_sha256='stock')])

    def test_new_companion_uses_only_its_declared_scene_shard(self):
        row = dict(path='Dds/Scenes/stage_Main/snow.dds', sha256='winter')
        result = maker.changes([row], {}, {'Dds/Scenes/stage_Main/': 'Pack/Dds0005.pkn'})
        self.assertEqual(result[0]['archive'], 'Pack/Dds0005.pkn')
        self.assertEqual(result[0]['original_sha256'], '')
        self.assertFalse(maker.in_scope('Dds/Maps/snow.dds', ['Dds/Scenes/stage_Main/']))

    def test_unchanged_member_contributes_no_edit(self):
        row = dict(path='Scenes/stage_Main/track.WB', sha256='same')
        self.assertEqual(maker.changes([row], {row['path'].casefold(): row}, {}), [])

    def test_missing_and_overlapping_archive_ownership_fail(self):
        row = dict(path='Scenes/stage_Main/track.WB', sha256='winter')
        for owners in ({}, {'Scenes/': 'one', 'Scenes/stage_Main/': 'two'}):
            with self.subTest(owners=owners), self.assertRaisesRegex(ValueError, 'unique target archive'):
                maker.changes([row], {}, owners)

    def test_case_colliding_contributions_fail(self):
        rows = [dict(path='Scenes/stage_Main/track.WB', sha256='one'),
                dict(path='scenes/stage_main/TRACK.wb', sha256='two')]
        with self.assertRaisesRegex(ValueError, 'Duplicate scene contribution'):
            maker.changes(rows, {}, {'Scenes/stage_Main/': 'Pack/Scenes0001.pkn'})

    def test_resource_paths_reject_traversal_and_windows_alias_characters(self):
        for value in ('../outside', '/outside', 'D:/outside', 'Scenes//track.WB', 'Scenes/./track.WB',
                      'Scenes\\track.WB', 'Scenes/track\n.WB', 'Scenes/a?.WB', 'Scenes/a*.WB'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                maker.safe_path(value)
        self.assertEqual(maker.safe_path('Scenes/stage_Main/track.WB'), 'Scenes/stage_Main/track.WB')


class ScenePackageTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        self.blobs = self.root / 'blobs'
        self.blobs.mkdir()
        self.definition = self.root / 'definition'
        self.definition.mkdir()
        (self.definition / 'plugin.toml').write_text('''schema = 1
[plugin]
id = "fixture.scenes"
version = "1.0.0"
[compatibility]
builds = ["cn-279"]
[[options]]
key = "main_style"
type = "enum"
default = "original"
values = ["original", "snow"]
[[options]]
key = "plaza_style"
type = "enum"
default = "original"
values = ["original", "snow"]
''', encoding='utf-8')
        for name in ('plugin-ui.toml', 'settings.html', 'README.md', 'README.zh.md'):
            (self.definition / name).write_text('fixture', encoding='utf-8')
        clients = []
        for build, stamp in (('winter', 100), ('cn-279', 200)):
            client = self.root / build
            (client / 'Pack').mkdir(parents=True)
            pe = bytearray(256)
            pe[:2] = b'MZ'
            struct.pack_into('<I', pe, 60, 128)
            pe[128:132] = b'PE\0\0'
            struct.pack_into('<I', pe, 136, stamp)
            (client / 'BubbleFighter.exe').write_bytes(pe)
            archive = client / 'Pack/Scenes0000.pkn'
            archive.write_bytes(build.encode())
            rows = []
            for scene in ('Main', 'Plaza'):
                payload = (build + scene).encode()
                sha = hashlib.sha256(payload).hexdigest()
                (self.blobs / sha).write_bytes(payload)
                rows.append(dict(path=f'Scenes/stage_{scene}/track.WB', archive='Pack/Scenes0000.pkn',
                                 size=len(payload), sha256=sha))
            clients.append(dict(build=build, root=str(client), files=rows,
                                archives={'Pack/Scenes0000.pkn': maker.digest(archive)}))
        self.inventory = self.root / 'inventory.json'
        self.inventory.write_text(json.dumps(clients), encoding='utf-8')
        self.rules = self.root / 'rules.toml'
        rules = 'schema = 1\nsource_build = "winter"\nsource_timestamp = 100\n'
        for option, scene in (('main_style', 'Main'), ('plaza_style', 'Plaza')):
            rows = [row for row in clients[0]['files'] if f'stage_{scene}/' in row['path']]
            rules += f'''\n[[styles]]
option = "{option}"
value = "snow"
prefixes = ["Scenes/stage_{scene}/"]
count = 1
index_sha256 = "{maker.index_hash(rows)}"
'''
        rules += f'''\n[[clients]]
build = "cn-279"
timestamp = 200
count = 2
index_sha256 = "{maker.index_hash(clients[1]['files'])}"
[clients.additions]
"Scenes/stage_Main/" = "Pack/Scenes0000.pkn"
"Scenes/stage_Plaza/" = "Pack/Scenes0000.pkn"
'''
        self.rules.write_text(rules, encoding='utf-8')

    def build(self, name='package'):
        output = self.root / name
        maker.build(argparse.Namespace(rules=self.rules, inventory=self.inventory,
                                      blobs=self.blobs, definition=self.definition, output=output))
        return output

    def test_both_options_have_independent_snow_plans_and_original_is_noop(self):
        output = self.build()
        package = output / 'fixture.scenes'
        plans = tomllib.loads((package / 'styles.toml').read_text())['plans']
        self.assertEqual({row['when'] for row in plans}, {'main_style', 'plaza_style'})
        self.assertEqual({row['when_value'] for row in plans}, {'snow'})
        for row in plans:
            plan = tomllib.loads((package / row['path']).read_text())
            self.assertEqual(len(plan['archives']), 1)
            member = plan['archives'][0]['members'][0]
            self.assertEqual(maker.digest(package / member['source']), member['sha256'])
            self.assertEqual(plan['archives'][0]['path'], 'Pack/Scenes0000.pkn')
        self.assertEqual(len(list((package / 'payload').iterdir())), 2)

    def test_package_zip_is_deterministic_and_has_direct_import_root(self):
        first, second = self.build('first'), self.build('second')
        name = 'fixture.scenes-1.0.0.zip'
        self.assertEqual(maker.digest(first / name), maker.digest(second / name))
        with zipfile.ZipFile(first / name) as archive:
            self.assertIn('plugin.toml', archive.namelist())
            self.assertIn('styles.toml', archive.namelist())
            self.assertFalse(any(name.startswith('/') or '..' in name.split('/') for name in archive.namelist()))

    def test_changed_original_archive_is_rejected_before_output_creation(self):
        original = self.root / 'cn-279/Pack/Scenes0000.pkn'
        original.write_bytes(b'user modified archive')
        with self.assertRaisesRegex(ValueError, 'Original archive changed'):
            self.build()
        self.assertFalse((self.root / 'package').exists())
        self.assertEqual(original.read_bytes(), b'user modified archive')

    def test_corrupted_payload_and_unknown_style_are_rejected(self):
        blob = next(self.blobs.iterdir())
        blob.write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError, 'payload differs'):
            self.build()
        self.assertFalse((self.root / 'package').exists())
        self.rules.write_text(self.rules.read_text().replace('value = "snow"', 'value = "future"'), encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'declared enum choice'):
            self.build()

    def test_original_choice_cannot_contribute_a_resource_plan(self):
        self.rules.write_text(self.rules.read_text().replace('value = "snow"', 'value = "original"'), encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'declared enum choice'):
            self.build()
        self.assertFalse((self.root / 'package').exists())

    def test_output_cannot_be_written_beneath_an_original_client(self):
        output = self.root / 'cn-279' / 'new-package'
        with self.assertRaisesRegex(ValueError, 'outside every original client'):
            maker.build(argparse.Namespace(rules=self.rules, inventory=self.inventory,
                blobs=self.blobs, definition=self.definition, output=output))
        self.assertFalse(output.exists())


def sound_fixture():
    rows = ''.join('<Row><Cell><Data>description</Data></Cell><Cell><Data>' + key + '</Data></Cell>'
        '<Cell><Data>BF/music/Pregame</Data></Cell><Cell><Data>' + event + '</Data></Cell><Cell><Data>untouched</Data></Cell></Row>'
        for key, event in (('lobby', 'stage-lobby'), ('plaza', 'stage-multi')))
    return ('<Workbook xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"><Worksheet ss:Name="BGM"><Table>'
            + rows + '</Table></Worksheet><Worksheet ss:Name="Effects"><Table/></Worksheet></Workbook>').encode()


MUSIC_ROUTES = [dict(option='main_style', keys=['lobby'], group='BF/music/Pregame/carol', event='stage-lobby-carol'),
                dict(option='plaza_style', keys=['plaza'], group='BF/music/Pregame/carol', event='room-multi-carol')]


class MusicRoutingTests(unittest.TestCase):
    def test_only_selected_scene_cells_change_and_all_other_bytes_survive(self):
        original = sound_fixture()
        for selected in (set(), {'main_style'}, {'plaza_style'}, {'main_style', 'plaza_style'}):
            with self.subTest(selected=selected):
                expected = original
                for key, event in (('main_style', b'stage-lobby'), ('plaza_style', b'stage-multi')):
                    if key in selected:
                        replacement = b'stage-lobby-carol' if key == 'main_style' else b'room-multi-carol'
                        expected = expected.replace(b'<Data>BF/music/Pregame</Data></Cell><Cell><Data>' + event + b'</Data>',
                            b'<Data>BF/music/Pregame/carol</Data></Cell><Cell><Data>' + replacement + b'</Data>')
                self.assertEqual(maker.music_payload(original, MUSIC_ROUTES, selected), expected)

    def test_missing_duplicate_or_sparse_native_routing_keys_are_rejected(self):
        original = sound_fixture()
        for raw in (original.replace(b'lobby', b'missing'), original.replace(b'plaza', b'lobby'),
                    original.replace(b'<Cell><Data>lobby', b'<Cell ss:Index="3"><Data>lobby'),
                    original.replace(b'<Cell><Data>stage-lobby</Data></Cell><Cell><Data>untouched</Data></Cell>', b'')):
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                maker.music_payload(raw, MUSIC_ROUTES, {'main_style'})
        with self.assertRaisesRegex(ValueError, 'Duplicate native BGM'):
            maker.music_payload(original, [MUSIC_ROUTES[0], MUSIC_ROUTES[0]], {'main_style'})


class MusicPackageTests(unittest.TestCase):
    build = ScenePackageTests.build

    def setUp(self):
        ScenePackageTests.setUp(self)
        raw = sound_fixture()
        sha = hashlib.sha256(raw).hexdigest()
        (self.blobs / sha).write_bytes(raw)
        clients = json.loads(self.inventory.read_text())
        clients[1]['files'].append(dict(path='Sounds/SoundList.xml', archive='Pack/Scenes0000.pkn', size=len(raw), sha256=sha))
        self.inventory.write_text(json.dumps(clients), encoding='utf-8')
        definition = self.definition / 'plugin.toml'
        definition.write_text(definition.read_text() + '\n[[options]]\nkey="christmas_bgm"\ntype="bool"\ndefault=true\n', encoding='utf-8')
        text = self.rules.read_text()
        start = text.index('\n[[styles]]')
        music = '\n[music]\noption="christmas_bgm"\nmember="Sounds/SoundList.xml"\n'
        for route in MUSIC_ROUTES:
            music += '\n[[music.routes]]\n' + ''.join(key + '=' + json.dumps(value) + '\n' for key, value in route.items())
        self.rules.write_text(text[:start] + music + text[start:], encoding='utf-8')

    def test_exact_conditions_select_at_most_one_music_member_for_every_combination(self):
        package = self.build() / 'fixture.scenes'
        plans = [p for p in tomllib.loads((package / 'styles.toml').read_text())['plans'] if 'when_options' in p]
        self.assertEqual(len(plans), 3)
        for main in ('original', 'snow'):
            for plaza in ('original', 'snow'):
                for enabled in (True, False):
                    values = dict(main_style=main, plaza_style=plaza, christmas_bgm=enabled)
                    matched = [p for p in plans if all(values[k] == v for k, v in p['when_options'].items())]
                    self.assertEqual(len(matched), int(enabled and 'snow' in (main, plaza)))
                    if matched:
                        member = tomllib.loads((package / matched[0]['path']).read_text())['archives'][0]['members'][0]
                        active = {k for k in ('main_style', 'plaza_style') if values[k] == 'snow'}
                        self.assertEqual((package / member['source']).read_bytes(), maker.music_payload(sound_fixture(), MUSIC_ROUTES, active))
                        self.assertEqual(member['original_sha256'], hashlib.sha256(sound_fixture()).hexdigest())

    def test_corrupt_music_baseline_fails_before_creating_package(self):
        (self.blobs / hashlib.sha256(sound_fixture()).hexdigest()).write_bytes(b'corrupt')
        with self.assertRaisesRegex(ValueError, 'payload differs'):
            self.build()
        self.assertFalse((self.root / 'package').exists())


class OutfitSelectionTests(unittest.TestCase):
    def test_exact_default_directory_and_missing_companions_preserve_other_looks(self):
        mesh = dict(path='Models/Characters/NPC_Pini/Default/Head.msh', size=10, sha256='head')
        unrelated = dict(mesh, path='Models/Characters/NPC_Pini/Default/Santa/Head.msh')
        common = dict(path='Models/Characters/NPC_Pini/common/stand.mtn', size=20, sha256='motion')
        files = {'winter': {r['path'].casefold(): r for r in (mesh, unrelated, common)}}
        rule = dict(source_build='winter', prefix='Models/Characters/NPC_Pini/Default/', count=1,
            index_sha256=maker.index_hash([mesh]), option='pini_outfit', value='christmas', missing_companions=dict(
                source_build='winter', prefix='Models/Characters/NPC_Pini/common/', count=1, index_sha256=maker.index_hash([common])))
        self.assertEqual(maker.outfit_rows(rule, files, {}), [mesh, common])
        self.assertEqual(maker.outfit_rows(rule, files, {common['path'].casefold(): dict(common, sha256='existing motion')}), [mesh])
        with self.assertRaisesRegex(ValueError, 'outfit inventory differs'):
            maker.outfit_rows(dict(rule, count=2), files, {})


if __name__ == '__main__':
    unittest.main()
