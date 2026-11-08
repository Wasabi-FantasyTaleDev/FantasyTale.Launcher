"""Verify that generation plans carry existing source motions without inventing client actions."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import tomllib
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
spec = importlib.util.spec_from_file_location("character_maker", Path(__file__).parents[1] / "package-character-models.py")
maker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(maker)


class GenerationMotionTests(unittest.TestCase):
    def test_new_motions_follow_existing_actions_and_archive_ownership(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            definition = root / "definition"
            definition.mkdir()
            (root / "rules.toml").write_text('''schema = 1
new_source = "new-client"
new_animations = true
[[characters]]
key = "Dizni"
classic_source = "old-client"
classic_animations = true
classic_integrated_legs = true
[characters.name]
en = "Dizni"
[[native]]
build = "old-client"
new = []
[[native]]
build = "new-client"
new = ["Dizni"]
''', encoding="utf-8")
            prefix = "Models/Characters/Dizni/"

            def member(name, digest, archive):
                return dict(path=prefix + name, sha256=digest * 64, archive=archive, size=1)

            old = [member("Default/Head.msh", "1", "Default.bfz"),
                   member("common/idle.mtn", "2", "common.bfz"),
                   member("common/idle.face", "a", "common.bfz"),
                   member("rifle/fire.mtn", "3", "rifle.bfz"),
                   member("common/old-only.mtn", "4", "common.bfz"),
                   member("100000/Head.msh", "5", "equipment.bfz"),
                   member("100100/100118/BodyD.msh", "a", "pants.bfz"),
                   member("100100/100119/BodyD.msh", "a", "unchanged.bfz")]
            new = [member("Default/Head.msh", "6", "Packs/model.pkn"),
                   member("common/idle.mtn", "7", "Packs/motion.pkn"),
                   member("common/idle.face", "b", "Packs/expressions.pkn"),
                   member("rifle/fire.mtn", "3", "Packs/motion.pkn"),
                   member("common/new-only.mtn", "8", "Packs/motion.pkn"),
                   member("100000/Head.msh", "9", "Packs/equipment.pkn"),
                   member("100100/100118/BodyD.msh", "b", "Packs/equipment.pkn"),
                   member("100100/100118/Legs.msh", "c", "Packs/legs.pkn"),
                   member("100100/100118/legs.png", "d", "Packs/textures.pkn"),
                   member("100100/100119/BodyD.msh", "a", "Packs/equipment.pkn"),
                   member("100100/100119/Legs.msh", "c", "Packs/legs.pkn"),
                   member("100100/100120/BodyD.msh", "b", "Packs/equipment.pkn"),
                   member("100100/100120/Legs.msh", "c", "Packs/legs.pkn"),
                   member("common/new-only.face", "c", "Packs/motion.pkn")]
            (root / "inventory.json").write_text(json.dumps([
                dict(build="old-client", root="old", files=old),
                dict(build="new-client", root="new", files=new)]), encoding="utf-8")
            (root / "boundaries.json").write_text(json.dumps([dict(character="Dizni",
                paths=[prefix + "100100/100118/BodyD.msh", prefix + "100100/100119/BodyD.msh"])]), encoding="utf-8")
            args = argparse.Namespace(rules=root / "rules.toml", inventory=root / "inventory.json",
                boundaries=root / "boundaries.json", definition=definition, output=root / "output",
                aperture_evidence=None, materializer=None)
            maker.build(args)
            package = root / "output/bubblefighter.fantasy.character-models"
            for build, generation, expected_archive, expected_hash in [
                ("old-client", "new", "common.bfz", "7"),
                ("new-client", "classic", "Packs/motion.pkn", "2")]:
                with self.subTest(build=build):
                    plan = tomllib.loads((package / f"plans/{build}-dizni-{generation}.toml").read_text())
                    edits = {m["path"]: (a["path"], m) for a in plan["archives"] for m in a["members"]}
                    expected = {prefix + "Default/Head.msh", prefix + "common/idle.mtn",
                        prefix + "common/idle.face", prefix + "100100/100118/BodyD.msh"}
                    if generation == 'new':
                        expected.update(prefix + "100100/100118/" + name for name in ['Legs.msh', 'legs.png'])
                        for name in ['Legs.msh', 'legs.png']:
                            owner, member = edits[prefix + "100100/100118/" + name]
                            self.assertEqual(owner, 'pants.bfz')
                            self.assertEqual(member['original_sha256'], '')
                    else:
                        expected.add(prefix + '100100/100118/Legs.msh')
                        owner, removal = edits[prefix + '100100/100118/Legs.msh']
                        self.assertEqual(owner, 'Packs/legs.pkn')
                        self.assertTrue(removal['remove'])
                        self.assertEqual(removal['original_sha256'], 'c' * 64)
                        self.assertNotIn('source', removal)
                    self.assertEqual(set(edits), expected)
                    archive, motion = edits[prefix + "common/idle.mtn"]
                    self.assertEqual(archive, expected_archive)
                    self.assertEqual(motion["sha256"], expected_hash * 64)
            catalog = tomllib.loads((package / "models.toml").read_text())
            for row in catalog["native"]:
                self.assertNotIn(row["generation"], row)


if __name__ == "__main__":
    unittest.main()
