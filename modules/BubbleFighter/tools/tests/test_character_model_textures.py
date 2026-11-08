"""Protect outfit texture lookup from shared-pool and cross-outfit substitutions."""
import importlib.util
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
spec = importlib.util.spec_from_file_location("texture_maker", Path(__file__).parents[1] / "package-character-models.py")
maker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(maker)


class TextureResolutionTests(unittest.TestCase):
    def test_local_companion_wins_without_changing_other_outfits(self):
        shared = {'sha256': 'shared'}
        repaired = {'sha256': 'matching-new-mesh'}
        files = {'models/characters/texture/hat.png': shared,
                 'models/characters/leo/100000/100001/hat.png': repaired}
        self.assertIs(maker.resolve_mesh_texture('Models/Characters/Leo/100000/100001/Head.msh', 'HAT.PNG', files), repaired)
        self.assertIs(maker.resolve_mesh_texture('Models/Characters/Leo/100000/100002/Head.msh', 'hat.png', files), shared)
        self.assertIs(files['models/characters/texture/hat.png'], shared)

    def test_registered_parent_precedes_shared_pool(self):
        parent = {'sha256': 'parent'}
        files = {'models/characters/leo/100000/hat.png': parent,
                 'models/characters/texture/hat.png': {'sha256': 'shared'}}
        self.assertIs(maker.resolve_mesh_texture('Models/Characters/Leo/100000/100001/Head.msh', 'hat.png', files), parent)

    def test_missing_source_does_not_borrow_another_characters_image(self):
        files = {'models/characters/lavi/100000/100001/hat.png': {'sha256': 'wrong-character'}}
        self.assertIsNone(maker.resolve_mesh_texture('Models/Characters/Leo/100000/100001/Head.msh', 'hat.png', files))

    def test_equipped_face_can_use_its_own_default_texture(self):
        face = {'sha256': 'face'}
        files = {'models/characters/lavi/default/f00.png': face}
        self.assertIs(maker.resolve_mesh_texture('Models/Characters/Lavi/100800/100899/Face.msh', 'f00.png', files), face)


if __name__ == '__main__':
    unittest.main()
