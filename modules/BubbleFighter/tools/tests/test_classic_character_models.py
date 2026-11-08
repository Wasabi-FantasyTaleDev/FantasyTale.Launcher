"""Protect archive surgery against shard injection and unrelated resource replacement."""
import importlib.util
from pathlib import Path
import unittest
import tempfile
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("maker", Path(__file__).parents[1] / "package-classic-character-models.py")
maker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(maker)


class ReplacementTests(unittest.TestCase):
    def test_only_existing_default_members_change(self):
        prefix = "Models/Characters/Dao/Default/"
        before = {prefix + "Head.msh": "new", prefix + "unchanged.png": "same",
                  prefix + "new-only.png": "preserve", "Models/Characters/Dao/100000/Head.msh": "costume"}
        classic = {prefix + "Head.msh": "old", prefix + "unchanged.png": "same",
                   prefix + "other-shard.png": "do-not-inject", "Models/Characters/Dao/100000/Head.msh": "wrong"}
        self.assertEqual(maker.replacements(before, classic, prefix), {prefix + "Head.msh": prefix + "Head.msh"})

    def test_case_insensitive_source_retains_target_spelling(self):
        self.assertEqual(maker.replacements({"Models/Dao/Default/Head.msh": "new"},
            {"models/dao/default/head.msh": "old"}, "Models/Dao/Default/"),
            {"Models/Dao/Default/Head.msh": "models/dao/default/head.msh"})

    def test_unchanged_shard_has_no_payload(self):
        self.assertEqual(maker.replacements({"Default/face.png": "same"}, {"Default/face.png": "same"}, "Default/"), {})

    def test_dizni_faces_and_motions_restore_together(self):
        root = "Models/Characters/Dizni/"
        names = [root + "Default/Face.msh", root + "common/pis-basic-stand.face",
                 root + "common/pis-basic-stand.mtn"]
        before = {name: "new" for name in names}
        classic = {name.lower(): "old" for name in names}
        self.assertEqual(maker.replacements(before, classic, root + "Default/", companions=True),
                         {name: name.lower() for name in names})

    def test_companions_preserve_new_actions_clothing_and_other_characters(self):
        root = "Models/Characters/Dizni/"
        before = {root + "common/new-action.mtn": "new", root + "100000/Head.msh": "new",
                  root + "common/effect.evn": "new", "Models/Characters/Dao/common/idle.mtn": "new"}
        classic = {name: "old" for name in before if not name.endswith("new-action.mtn")}
        classic[root + "common/other-shard.face"] = "old"
        self.assertEqual(maker.replacements(before, classic, root + "Default/", companions=True), {})

    def test_face_dependency_case_and_separators_are_normalized(self):
        with tempfile.TemporaryDirectory() as folder:
            tree = Path(folder)
            name = "Models/Characters/Dizni/idle.face"
            (tree / name).parent.mkdir(parents=True)
            (tree / name).write_text("StartTick,ResourcePath\n1,Models\\Characters\\Dizni/Default/F00.PNG\n"
                                     "30,Models/Characters/Leo/Default/f00.png\n", encoding="utf-8")
            self.assertEqual(maker.face_references(tree, {name: "hash"}, "Models/Characters/Dizni/"),
                             {"models/characters/dizni/default/f00.png"})

    def test_malformed_face_selector_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            tree = Path(folder)
            (tree / "idle.face").write_text("StartTick,ResourcePath\n1\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Invalid face selector entry"):
                maker.face_references(tree, {"idle.face": "hash"}, "")

    @patch.object(maker.subprocess, "check_output", return_value="  12  Default/Head.msh\n  12  default/head.msh\n2 member(s)\n")
    def test_duplicate_archive_paths_are_rejected(self, output):
        with self.assertRaisesRegex(ValueError, "duplicate"):
            maker.paths(Path("tool"), Path("archive.pkn"))

    @patch.object(maker.subprocess, "check_output", return_value="unexpected output")
    def test_failed_member_inventory_is_rejected(self, output):
        with self.assertRaisesRegex(ValueError, "empty"):
            maker.paths(Path("tool"), Path("archive.pkn"))


if __name__ == "__main__":
    unittest.main()
