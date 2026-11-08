"""Check offline Chinese outline stroking using generated font fixtures."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

from test_merge_client_fonts import font

spec = importlib.util.spec_from_file_location('embolden_fonts', Path(__file__).parents[1] / 'embolden-client-font.py')
embolden = importlib.util.module_from_spec(spec)
spec.loader.exec_module(embolden)


def bounds(value, cp):
    """Measure drawn bounds, including the horizontal bearing origin."""
    glyphs = value.getGlyphSet()
    pen = BoundsPen(glyphs)
    glyphs[value.getBestCmap()[cp]].draw(pen)
    return pen.bounds


class StrokeValidationTests(unittest.TestCase):
    def test_existing_output_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'existing.ttf'
            path.write_bytes(b'preserved rollback font')
            with self.assertRaisesRegex(ValueError, 'new output path'):
                embolden.build(path, path, 'unused-library')
            self.assertEqual(b'preserved rollback font', path.read_bytes())

    def test_nonpositive_reference_and_radius_fail_before_native_loading(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for reference, radius in ((0, 16), (11, 0), (-11, 16), (11, -16)):
                with self.assertRaisesRegex(ValueError, 'must be positive'):
                    embolden.build(root / 'source.ttf', root / 'output.ttf', 'unused', reference, radius)
            self.assertFalse((root / 'output.ttf').exists())

    def test_shared_korean_outline_is_rejected_before_any_write(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf', korean=True)
            with TTFont(root / 'base.ttf') as value:
                for table in value['cmap'].tables:
                    if table.isUnicode():
                        table.cmap[0x41] = table.cmap[0xAC00]
                value.save(root / 'shared.ttf')
            with self.assertRaisesRegex(ValueError, 'share an outline'):
                embolden.build(root / 'shared.ttf', root / 'output.ttf', 'unused')
            self.assertFalse((root / 'output.ttf').exists())


@unittest.skipUnless(os.environ.get('FT_TEST_LIBRARY'), 'Set FT_TEST_LIBRARY to the offline FreeType library')
class NativeStrokeTests(unittest.TestCase):
    def test_outside_stroke_retains_origin_advances_and_all_korean_glyphs(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf', korean=True)
            result = embolden.build(root / 'base.ttf', root / 'output.ttf', os.environ['FT_TEST_LIBRARY'])
            with TTFont(root / 'base.ttf') as before, TTFont(root / 'output.ttf') as after:
                self.assertEqual(embolden.merge_fonts.outlines(before, range(0xAC00, 0xD7A4)),
                                 embolden.merge_fonts.outlines(after, range(0xAC00, 0xD7A4)))
                for cp in (0x41, 0x4E2D):
                    original, actual = bounds(before, cp), bounds(after, cp)
                    radius = result['radius_font_units']
                    for expected, coordinate in zip((original[0] - radius, original[1] - radius,
                                                     original[2] + radius, original[3] + radius), actual):
                        self.assertAlmostEqual(expected, coordinate, delta=1.25)
                    self.assertEqual(before['hmtx'][before.getBestCmap()[cp]][0], after['hmtx'][after.getBestCmap()[cp]][0])
            self.assertEqual(2, result['changed_glyphs'])

    def test_empty_space_retains_its_advance_and_does_not_acquire_a_shape(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            with TTFont(root / 'base.ttf') as value:
                order = list(value.getGlyphOrder())
                value['glyf']['space'] = TTGlyphPen(None).glyph()
                value['hmtx']['space'] = (321, 0)
                value.setGlyphOrder(order + ['space'])
                for table in value['cmap'].tables:
                    if table.isUnicode():
                        table.cmap[32] = 'space'
                value.save(root / 'with-space.ttf')
            embolden.build(root / 'with-space.ttf', root / 'output.ttf', os.environ['FT_TEST_LIBRARY'])
            with TTFont(root / 'output.ttf') as result:
                self.assertIsNone(bounds(result, 32))
                self.assertEqual((321, 0), result['hmtx']['space'])

    def test_composite_uses_the_unmodified_component_once(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            with TTFont(root / 'base.ttf') as value:
                order = list(value.getGlyphOrder())
                pen = TTGlyphPen(value.getGlyphSet())
                pen.addComponent('latin', (1, 0, 0, 1, 120, 0))
                value['glyf']['zlatin'] = pen.glyph()
                value['hmtx']['zlatin'] = (550, 131)
                value.setGlyphOrder(order + ['zlatin'])
                for table in value['cmap'].tables:
                    if table.isUnicode():
                        table.cmap[0x42] = 'zlatin'
                value.save(root / 'composite.ttf')
            embolden.build(root / 'composite.ttf', root / 'output.ttf', os.environ['FT_TEST_LIBRARY'])
            with TTFont(root / 'output.ttf') as result:
                a, b = bounds(result, 0x41), bounds(result, 0x42)
                self.assertEqual((a[0] + 120, a[1], a[2] + 120, a[3]), b)

    def test_same_input_and_reference_generate_identical_fonts(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            first = embolden.build(root / 'base.ttf', root / 'first.ttf', os.environ['FT_TEST_LIBRARY'])
            second = embolden.build(root / 'base.ttf', root / 'second.ttf', os.environ['FT_TEST_LIBRARY'])
            self.assertEqual(first['sha256'], second['sha256'])


if __name__ == '__main__':
    unittest.main()
