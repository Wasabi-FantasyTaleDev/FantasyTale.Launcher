"""Verify font fusion geometry using generated, redistributable test glyphs."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib import TTFont


spec = importlib.util.spec_from_file_location('merge_fonts', Path(__file__).parents[1] / 'merge-client-fonts.py')
merge_fonts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(merge_fonts)


def font(path, upm=1024, korean=False, cff=False, complete=True, cff_bearing=None, shape_offset=0):
    """Create distinct Latin, Chinese and Korean outlines without game font assets."""
    builder = FontBuilder(upm, isTTF=not cff)
    names = ['.notdef', 'latin', 'chinese', 'hangul']
    builder.setupGlyphOrder(names)
    mapping = {0x41: 'latin', 0x4E2D: 'chinese'}
    if korean:
        mapping.update({cp: 'hangul' for cp in (range(0xAC00, 0xD7A4) if complete else [0xAC00])})
    builder.setupCharacterMap(mapping)
    glyphs = {}
    for index, name in enumerate(names):
        pen = T2CharStringPen(None, None) if cff else TTGlyphPen(None)
        pen.moveTo((10 + index, 20 + shape_offset))
        pen.lineTo((200 + index * 10, 20 + shape_offset))
        if cff:
            pen.curveTo((300, 150), (100, 400), (30, 320))
        else:
            pen.qCurveTo((300, 150), (30, 320))
        pen.closePath()
        glyphs[name] = pen.getCharString() if cff else pen.glyph()
    if cff:
        builder.setupCFF('Fixture', {'FullName': 'Fixture', 'FamilyName': 'Fixture', 'Weight': 'Regular'}, glyphs, {})
    else:
        builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({name: (500 + index * 50, cff_bearing if cff_bearing is not None else 10 + index)
                                    for index, name in enumerate(names)})
    builder.setupHorizontalHeader(ascent=800, descent=-200)
    builder.setupNameTable({'familyName': 'Fixture', 'styleName': 'Regular', 'uniqueFontIdentifier': 'Fixture', 'fullName': 'Fixture', 'psName': 'Fixture'})
    builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    builder.setupPost()
    builder.save(path)


class MergeFontTests(unittest.TestCase):
    def test_matching_units_preserve_every_chinese_latin_and_korean_outline(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            font(root / 'korean.ttf', korean=True, shape_offset=20)
            report = merge_fonts.build(root / 'base.ttf', root / 'korean.ttf', root / 'merged.ttf')
            with TTFont(root / 'base.ttf') as base, TTFont(root / 'korean.ttf') as source, TTFont(root / 'merged.ttf') as result:
                self.assertEqual(merge_fonts.outlines(base, [0x41, 0x4E2D]), merge_fonts.outlines(result, [0x41, 0x4E2D]))
                self.assertEqual(merge_fonts.outlines(source, [0xAC00]), merge_fonts.outlines(result, [0xAC00]))
            self.assertEqual(11172, report['primary_korean_codepoints'])
            self.assertEqual(0, report['converted_glyphs'])

    def test_cff_scaling_uses_primary_glyphs_and_fills_only_missing_korean(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            font(root / 'primary.otf', upm=1000, korean=True, cff=True, complete=False)
            font(root / 'fallback.ttf', korean=True)
            report = merge_fonts.build(root / 'base.ttf', root / 'primary.otf', root / 'merged.ttf', root / 'fallback.ttf')
            with TTFont(root / 'base.ttf') as base, TTFont(root / 'fallback.ttf') as fallback, TTFont(root / 'merged.ttf') as result:
                self.assertEqual(merge_fonts.outlines(base, [0x41, 0x4E2D]), merge_fonts.outlines(result, [0x41, 0x4E2D]))
                self.assertEqual(merge_fonts.outlines(fallback, [0xAC01]), merge_fonts.outlines(result, [0xAC01]))
                self.assertEqual(666, result['hmtx'][result.getBestCmap()[0xAC00]][0])
            self.assertEqual(1, report['primary_korean_codepoints'])
            self.assertEqual(11171, report['fallback_korean_codepoints'])
            self.assertGreater(report['converted_glyphs'], 0)

    def test_incomplete_korean_without_fallback_never_writes_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            font(root / 'primary.ttf', korean=True, complete=False)
            with self.assertRaisesRegex(ValueError, 'every modern Hangul'):
                merge_fonts.build(root / 'base.ttf', root / 'primary.ttf', root / 'merged.ttf')
            self.assertFalse((root / 'merged.ttf').exists())

    def test_cff_outline_origin_survives_inconsistent_horizontal_bearing(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            font(root / 'primary.otf', korean=True, cff=True, cff_bearing=0)
            merge_fonts.build(root / 'base.ttf', root / 'primary.otf', root / 'merged.ttf')
            with TTFont(root / 'primary.otf') as source, TTFont(root / 'merged.ttf') as result:
                from fontTools.pens.boundsPen import BoundsPen
                source_set, result_set = source.getGlyphSet(), result.getGlyphSet()
                before, after = BoundsPen(source_set), BoundsPen(result_set)
                source_set[source.getBestCmap()[0xAC00]].draw(before)
                result_set[result.getBestCmap()[0xAC00]].draw(after)
                self.assertEqual(before.bounds[0], after.bounds[0])
                self.assertEqual(source['hmtx'][source.getBestCmap()[0xAC00]][0], result['hmtx'][result.getBestCmap()[0xAC00]][0])

    def test_scaled_truetype_does_not_change_the_chinese_units_or_widths(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            font(root / 'primary.ttf', upm=1000, korean=True)
            report = merge_fonts.build(root / 'base.ttf', root / 'primary.ttf', root / 'merged.ttf')
            with TTFont(root / 'base.ttf') as base, TTFont(root / 'merged.ttf') as result:
                self.assertEqual(1024, result['head'].unitsPerEm)
                self.assertEqual(merge_fonts.outlines(base, [0x41, 0x4E2D]), merge_fonts.outlines(result, [0x41, 0x4E2D]))
            self.assertGreater(report['converted_glyphs'], 0)

    def test_serialized_output_is_reproducible(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            font(root / 'primary.otf', korean=True, cff=True, upm=1000, cff_bearing=0)
            first = merge_fonts.build(root / 'base.ttf', root / 'primary.otf', root / 'first.ttf')
            second = merge_fonts.build(root / 'base.ttf', root / 'primary.otf', root / 'second.ttf')
            self.assertEqual(first['sha256'], second['sha256'])

    def test_substituted_client_font_is_rejected_before_variant_generation(self):
        spec = importlib.util.spec_from_file_location('font_variants', Path(__file__).parents[1] / 'build-client-font-variants.py')
        variants = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(variants)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            font(root / 'base.ttf')
            definition = root / 'variants.toml'
            definition.write_text('schema = 1\n[base]\nfile = "base.ttf"\nsha256 = "' + '0' * 64 + '"\n')
            with self.assertRaisesRegex(ValueError, 'Original font hash differs'):
                variants.build(definition, root, root, root / 'output')
            self.assertFalse((root / 'output').exists())


if __name__ == '__main__':
    unittest.main()
