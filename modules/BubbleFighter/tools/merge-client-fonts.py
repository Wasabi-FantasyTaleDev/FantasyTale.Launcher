"""Build a private font payload from a Chinese base and client-owned Korean glyphs.

Requires fonttools. Inputs and generated fonts are distribution assets, never
source fixtures. Chinese and other existing non-Korean outlines remain unchanged.
"""
import argparse
import hashlib
import json
from pathlib import Path
from copy import deepcopy

from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.misc.roundTools import otRound
from contextlib import ExitStack


def korean(codepoint):
    return any(start <= codepoint <= end for start, end in (
        (0x1100, 0x11FF), (0x3130, 0x318F), (0xA960, 0xA97F),
        (0xAC00, 0xD7AF), (0xD7B0, 0xD7FF), (0xFFA0, 0xFFDC)))


def outlines(font, codepoints):
    glyphs, cmap = font.getGlyphSet(), font.getBestCmap()
    result = {}
    for codepoint in sorted(codepoints):
        pen = DecomposingRecordingPen(glyphs)
        glyphs[cmap[codepoint]].draw(pen)
        result[codepoint] = (pen.value, font['hmtx'][cmap[codepoint]])
    return result


def build(base_path, korean_path, output, fallback_path=None):
    base_path, korean_path, output = map(lambda path: Path(path).resolve(), (base_path, korean_path, output))
    fallback_path = Path(fallback_path).resolve() if fallback_path else None
    if output in (base_path, korean_path, fallback_path) or output.exists():
        raise ValueError('Use a new output path; original client fonts are read-only')
    with ExitStack() as stack:
        base = stack.enter_context(TTFont(base_path, recalcTimestamp=False))
        donor = stack.enter_context(TTFont(korean_path, recalcTimestamp=False))
        fallback = stack.enter_context(TTFont(fallback_path, recalcTimestamp=False)) if fallback_path else None
        if 'glyf' not in base:
            raise ValueError('The Chinese base must have TrueType outlines')
        sources = [font for font in (donor, fallback) if font is not None]
        if any('glyf' not in font and 'CFF ' not in font for font in sources):
            raise ValueError('Korean sources must have TrueType or CFF outlines')
        retained = {cp for cp in base.getBestCmap() if not korean(cp)}
        assignments = {cp: index for index, font in reversed(list(enumerate(sources)))
                       for cp in font.getBestCmap() if korean(cp)}
        added = set(assignments)
        if not set(range(0xAC00, 0xD7A4)).issubset(added):
            raise ValueError('Korean sources must contain every modern Hangul syllable')
        expected_base = outlines(base, retained)
        # Retain the base font's metrics, hint programs and layout tables. Korean
        # hints refer to a different CVT, so discard those hints on copied glyphs.
        order = list(base.getGlyphOrder())
        output_names = {}
        converted = 0
        for index, font in enumerate(sources):
            required = {font.getBestCmap()[cp] for cp in added if assignments[cp] == index}
            factor = base['head'].unitsPerEm / font['head'].unitsPerEm
            exact = 'glyf' in font and factor == 1
            if exact:
                pending = list(required)
                while pending:
                    glyph = font['glyf'][pending.pop()]
                    if glyph.isComposite():
                        for component in glyph.components:
                            if component.glyphName not in required:
                                required.add(component.glyphName); pending.append(component.glyphName)
            names = {name: f'ftkr.{index}.' + name for name in sorted(required)}
            if set(names.values()).intersection(order):
                raise ValueError('The base already contains generated Korean glyph names')
            glyph_set = font.getGlyphSet()
            for source_name, target_name in names.items():
                if exact:
                    glyph = deepcopy(font['glyf'][source_name]); glyph.removeHinting()
                    if glyph.isComposite():
                        for component in glyph.components: component.glyphName = names[component.glyphName]
                else:
                    # CFF curves use the opposite contour direction. Bound the
                    # cubic approximation before TrueType's integer rounding.
                    recorded = DecomposingRecordingPen(glyph_set)
                    glyph_set[source_name].draw(recorded)
                    pen = TTGlyphPen(None)
                    quadratic = Cu2QuPen(pen, max_err=0.25, reverse_direction='CFF ' in font)
                    recorded.replay(TransformPen(quadratic, (factor, 0, 0, factor, 0, 0)))
                    glyph = pen.glyph()
                    glyph.coordinates.toInt()
                    glyph.recalcBounds(base['glyf'])
                    converted += 1
                base['glyf'][target_name] = glyph
                advance, bearing = font['hmtx'][source_name]
                # CFF renders its outline origin directly; TrueType offsets by
                # lsb - xMin. Align these fields to retain the CFF drawing origin.
                base['hmtx'][target_name] = (otRound(advance * factor),
                                             glyph.xMin if 'CFF ' in font and glyph.numberOfContours else otRound(bearing * factor))
                if 'vmtx' in base:
                    base['vmtx'][target_name] = (tuple(otRound(value * factor) for value in font['vmtx'][source_name])
                                              if 'vmtx' in font else (base['head'].unitsPerEm, 0))
                order.append(target_name)
            output_names.update({cp: names[font.getBestCmap()[cp]] for cp in added if assignments[cp] == index})
        base.setGlyphOrder(order)
        for table in base['cmap'].tables:
            if table.isUnicode() and table.format in (4, 12, 13):
                for cp in added:
                    if table.format != 4 or cp <= 0xFFFF: table.cmap[cp] = output_names[cp]
        if 'DSIG' in base: del base['DSIG']
        base['OS/2'].recalcUnicodeRanges(base)
        if hasattr(base['OS/2'], 'ulCodePageRange1'): base['OS/2'].ulCodePageRange1 |= (1 << 19) | (1 << 21)
        base.recalcTimestamp = False
        base['head'].created = base['head'].modified = 3406620153
        if outlines(base, retained) != expected_base:
            raise ValueError('The merged font changed retained Chinese or Latin geometry')
        expected_korean = outlines(base, added)
        target_set = base.getGlyphSet()
        source_sets = [font.getGlyphSet() for font in sources]
        exact_outlines = {index: outlines(font, [cp for cp in added if assignments[cp] == index])
                          for index, font in enumerate(sources)
                          if 'glyf' in font and base['head'].unitsPerEm == font['head'].unitsPerEm}
        for cp, index in assignments.items():
            font = sources[index]
            factor = base['head'].unitsPerEm / font['head'].unitsPerEm
            source_set = source_sets[index]
            source_pen, target_pen = BoundsPen(source_set), BoundsPen(target_set)
            source_set[font.getBestCmap()[cp]].draw(TransformPen(source_pen, (factor, 0, 0, factor, 0, 0)))
            target_set[output_names[cp]].draw(target_pen)
            expected_bounds = source_pen.bounds
            actual_bounds = target_pen.bounds
            if (expected_bounds is None) != (actual_bounds is None) or expected_bounds is not None and any(
                    abs(a - b) > 1.25 for a, b in zip(expected_bounds, actual_bounds)):
                raise ValueError('Korean outline bounds exceeded the conversion tolerance')
            expected_metrics = tuple(otRound(value * factor) for value in font['hmtx'][font.getBestCmap()[cp]])
            if 'CFF ' in font and actual_bounds is not None:
                expected_metrics = (expected_metrics[0], base['glyf'][output_names[cp]].xMin)
            if base['hmtx'][output_names[cp]] != expected_metrics:
                raise ValueError(f'Korean metrics differ at U+{cp:04X}: '
                                 f'{base["hmtx"][output_names[cp]]} != {expected_metrics}')
            if index in exact_outlines and expected_korean[cp] != exact_outlines[index][cp]:
                raise ValueError('An unchanged-unit Korean glyph differs from its source')
        output.parent.mkdir(parents=True, exist_ok=True)
        base.save(output)
    with TTFont(output, recalcTimestamp=False) as verified:
        if outlines(verified, retained) != expected_base or outlines(verified, added) != expected_korean:
            output.unlink()
            raise ValueError('Serialized font failed outline verification')
    return {'base_codepoints': len(retained), 'korean_codepoints': len(added),
            'primary_korean_codepoints': sum(index == 0 for index in assignments.values()),
            'fallback_korean_codepoints': sum(index != 0 for index in assignments.values()),
            'converted_glyphs': converted, 'size': output.stat().st_size,
            'sha256': hashlib.sha256(output.read_bytes()).hexdigest()}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', type=Path, required=True)
    parser.add_argument('--korean', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--fallback', type=Path, help='Complete Korean font used only for missing Korean codepoints')
    args = parser.parse_args()
    print(json.dumps(build(args.base, args.korean, args.output, args.fallback), sort_keys=True))
