"""Keep renamed store cards from presenting a former identifier as its own latest plugin."""
import json
import tomllib
import unittest
from publish_plugins import retire_aliases


def package(identity, version, aliases=None, builds=None, game='fixture', kind='plugin'):
    row = f'\n[[packages]]\nid={json.dumps(identity)}\ngame={json.dumps(game)}\nkind={json.dumps(kind)}\nversion={json.dumps(version)}\nbuilds={json.dumps(builds or [])}\n'
    if aliases is not None:
        definition = f'[plugin]\nid={json.dumps(identity)}\ngame={json.dumps(game)}\nversion={json.dumps(version)}\naliases={json.dumps(aliases)}\n'
        row += 'plugin_definition=' + json.dumps(definition) + '\n'
    return row


class AliasWithdrawalTests(unittest.TestCase):
    def test_removes_only_former_id_and_retains_current_version_history_and_other_tables(self):
        text = 'schema=2\n[catalog]\nid="fixture"\n'
        text += package('fixture.old', '1.0.4', builds=['a'])
        text += package('fixture.current', '1.0.8', ['fixture.old'], ['a'])
        text += package('fixture.current', '1.0.9', ['fixture.old'], ['a'])
        text += package('fixture.other', '1.0.0', builds=['b'])
        text += '\n[[deprecated_plugins]]\nid="fixture.retired"\ngame="fixture"\n'
        before = text.encode()
        result = retire_aliases(before, ['fixture.current'])
        parsed = tomllib.loads(result.decode())
        self.assertEqual([('fixture.current', '1.0.8'), ('fixture.current', '1.0.9'), ('fixture.other', '1.0.0')],
                         [(row['id'], row['version']) for row in parsed['packages']])
        self.assertEqual('fixture.retired', parsed['deprecated_plugins'][0]['id'])
        self.assertEqual(result, retire_aliases(result, ['fixture.current']))
        self.assertEqual(before, retire_aliases(before, []))

    def test_aliases_are_scoped_to_their_game_and_plugins(self):
        text = 'schema=2\n' + package('fixture.old', '1.0.0', game='other')
        text += package('fixture.old', '1.0.0', kind='server')
        text += package('fixture.current', '1.0.8', ['fixture.old'])
        self.assertEqual(3, len(tomllib.loads(retire_aliases(text.encode(), ['fixture.current']).decode())['packages']))

    def test_multiple_previous_identifiers_are_removed(self):
        text = 'schema=2\n' + package('fixture.old1', '1.0.0') + package('fixture.old2', '1.0.1')
        text += package('fixture.current', '1.0.8', ['fixture.old1', 'fixture.old2'])
        rows = tomllib.loads(retire_aliases(text.encode(), ['fixture.current']).decode())['packages']
        self.assertEqual(['fixture.current'], [row['id'] for row in rows])

    def test_ambiguous_alias_ownership_and_missing_metadata_are_rejected(self):
        text = 'schema=2\n' + package('fixture.one', '1.0.0', ['fixture.old'])
        text += package('fixture.two', '1.0.0', ['fixture.old'])
        with self.assertRaisesRegex(ValueError, 'ambiguous'):
            retire_aliases(text.encode(), ['fixture.one'])
        with self.assertRaisesRegex(ValueError, 'explicit former'):
            retire_aliases(('schema=2\n' + package('fixture.current', '1.0.0')).encode(), ['fixture.current'])

    def test_replacement_cannot_remove_builds_supported_by_former_versions(self):
        for builds in ([], ['a', 'b']):
            text = 'schema=2\n' + package('fixture.old', '1.0.0', builds=builds)
            text += package('fixture.current', '1.0.8', ['fixture.old'], ['a'])
            with self.assertRaisesRegex(ValueError, 'every former client build'):
                retire_aliases(text.encode(), ['fixture.current'])

    def test_cyclic_alias_and_mismatched_metadata_are_rejected(self):
        text = 'schema=2\n' + package('fixture.old', '1.0.0', ['fixture.current'])
        text += package('fixture.current', '1.0.8', ['fixture.old'])
        with self.assertRaisesRegex(ValueError, 'cyclic'):
            retire_aliases(text.encode(), ['fixture.current'])
        text = ('schema=2\n' + package('fixture.current', '1.0.8', ['fixture.old'])).replace('id=\\"fixture.current\\"', 'id=\\"fixture.wrong\\"')
        with self.assertRaisesRegex(ValueError, 'disagrees'):
            retire_aliases(text.encode(), ['fixture.current'])


if __name__ == '__main__':
    unittest.main()
