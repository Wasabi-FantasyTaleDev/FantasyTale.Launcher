"""Normalize legacy font tables for browser loading without selecting fewer glyphs."""
import hashlib
from fontTools import subset
from fontTools.ttLib import TTFont, newTable


def prepare_web_font(source, destination):
    """Retain every glyph and hint while rebuilding legacy cmap and kerning records."""
    with TTFont(source) as font:
        glyphs = font.getGlyphOrder()
        if 'CFF ' in font:
            # CFF uses maxp 0.5; the original file declares the TrueType 1.0 layout.
            maximum = newTable('maxp')
            maximum.tableVersion = 0x5000
            maximum.numGlyphs = len(glyphs)
            font['maxp'] = maximum
        options = subset.Options()
        options.hinting = True
        options.notdef_outline = True
        options.retain_gids = True
        options.name_IDs = ['*']
        options.name_languages = ['*']
        options.name_legacy = True
        normalizer = subset.Subsetter(options=options)
        normalizer.populate(glyphs=glyphs)
        normalizer.subset(font)
        font.save(destination)
    return {'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
            'web_sha256': hashlib.sha256(destination.read_bytes()).hexdigest(),
            'glyph_count': len(glyphs)}
