"""Bake the native outside stroke into non-Korean glyphs of a private Chinese font.

Requires fonttools and a caller-supplied FreeType shared library. Neither source
fonts nor the published plugin are modified. Runtime font-weight options are
outside this generator's scope.
"""
import argparse
import ctypes as c
import hashlib
import importlib.util
import json
from pathlib import Path

from fontTools.misc.roundTools import otRound
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont

spec = importlib.util.spec_from_file_location('merge_fonts', Path(__file__).with_name('merge-client-fonts.py'))
merge_fonts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(merge_fonts)


class Vector(c.Structure):
    _fields_ = [('x', c.c_long), ('y', c.c_long)]


class Outline(c.Structure):
    _fields_ = [('n_contours', c.c_short), ('n_points', c.c_short),
               ('points', c.POINTER(Vector)), ('tags', c.POINTER(c.c_byte)),
               ('contours', c.POINTER(c.c_short)), ('flags', c.c_int)]


Move = c.CFUNCTYPE(c.c_int, c.POINTER(Vector), c.c_void_p)
Line = Move
Conic = c.CFUNCTYPE(c.c_int, c.POINTER(Vector), c.POINTER(Vector), c.c_void_p)
Cubic = c.CFUNCTYPE(c.c_int, c.POINTER(Vector), c.POINTER(Vector), c.POINTER(Vector), c.c_void_p)


class OutlineFunctions(c.Structure):
    _fields_ = [('move_to', Move), ('line_to', Line), ('conic_to', Conic),
               ('cubic_to', Cubic), ('shift', c.c_int), ('delta', c.c_long)]


def check(error):
    """Keep an unsuccessful native outline operation from producing a candidate."""
    if error:
        raise ValueError(f'FreeType outline operation failed: {error}')


class OutsideStroke:
    """Own one offline stroker and copy its outlines back into TrueType glyphs."""
    def __init__(self, library, radius):
        self.api = c.CDLL(str(Path(library).resolve()))
        self.library, self.stroker = c.c_void_p(), c.c_void_p()
        declarations = {
            'FT_Init_FreeType': ([c.POINTER(c.c_void_p)], c.c_int),
            'FT_Done_FreeType': ([c.c_void_p], c.c_int),
            'FT_Stroker_New': ([c.c_void_p, c.POINTER(c.c_void_p)], c.c_int),
            'FT_Stroker_Set': ([c.c_void_p, c.c_long, c.c_int, c.c_int, c.c_long], None),
            'FT_Stroker_Done': ([c.c_void_p], None),
            'FT_Stroker_ParseOutline': ([c.c_void_p, c.POINTER(Outline), c.c_ubyte], c.c_int),
            'FT_Outline_GetOutsideBorder': ([c.POINTER(Outline)], c.c_int),
            'FT_Stroker_GetBorderCounts': ([c.c_void_p, c.c_int, c.POINTER(c.c_uint), c.POINTER(c.c_uint)], c.c_int),
            'FT_Stroker_ExportBorder': ([c.c_void_p, c.c_int, c.POINTER(Outline)], None),
            'FT_Outline_Decompose': ([c.POINTER(Outline), c.POINTER(OutlineFunctions), c.c_void_p], c.c_int),
        }
        for name, (arguments, result) in declarations.items():
            function = getattr(self.api, name)
            function.argtypes, function.restype = arguments, result
        check(self.api.FT_Init_FreeType(c.byref(self.library)))
        try:
            check(self.api.FT_Stroker_New(self.library, c.byref(self.stroker)))
            # kr-812 sub_AE6820 uses radius 16/64 pixel, butt caps and round joins.
            # kr-812 sub_AE2D30 calls the outside-border path when weight is 700.
            self.api.FT_Stroker_Set(self.stroker, radius, 0, 0, 1)
        except BaseException:
            self.close()
            raise

    def close(self):
        """Release both native owners without touching any font file."""
        if self.stroker.value:
            self.api.FT_Stroker_Done(self.stroker)
            self.stroker.value = None
        if self.library.value:
            check(self.api.FT_Done_FreeType(self.library))
            self.library.value = None

    def glyph(self, coordinates, ends, flags):
        """Return an outside-stroked outline, retaining font-unit precision."""
        if not ends or not coordinates:
            raise ValueError('Only nonempty closed outlines can be stroked')
        if len(coordinates) > 32767 or len(ends) > 32767:
            raise ValueError('The input outline exceeds FreeType signed-short counts')
        points = (Vector * len(coordinates))(*(Vector(otRound(x * 64), otRound(y * 64)) for x, y in coordinates))
        tags = (c.c_byte * len(flags))(*(flag & 1 for flag in flags))
        contours = (c.c_short * len(ends))(*ends)
        outline = Outline(len(ends), len(coordinates), points, tags, contours, 0)
        border = self.api.FT_Outline_GetOutsideBorder(c.byref(outline))
        check(self.api.FT_Stroker_ParseOutline(self.stroker, c.byref(outline), 0))
        point_count, contour_count = c.c_uint(), c.c_uint()
        check(self.api.FT_Stroker_GetBorderCounts(self.stroker, border, c.byref(point_count), c.byref(contour_count)))
        if not 0 < point_count.value <= 32767 or not 0 < contour_count.value <= 32767:
            raise ValueError('The stroked outline cannot be represented by FreeType')
        output_points = (Vector * point_count.value)()
        output_tags = (c.c_byte * point_count.value)()
        output_ends = (c.c_short * contour_count.value)()
        result = Outline(0, 0, output_points, output_tags, output_ends, 0)
        self.api.FT_Stroker_ExportBorder(self.stroker, border, c.byref(result))
        if result.n_points != point_count.value or result.n_contours != contour_count.value:
            raise ValueError('FreeType returned inconsistent outline counts')
        pen = TTGlyphPen(None)
        converted = Cu2QuPen(pen, max_err=0.25)
        active, errors = [False], []

        def point(pointer):
            return (pointer.contents.x / 64, pointer.contents.y / 64)

        def protect(action):
            def invoke(*arguments):
                try:
                    action(*arguments)
                    return 0
                except Exception as error:
                    errors.append(error)
                    return 1
            return invoke

        def move(pointer, _):
            if active[0]:
                converted.closePath()
            converted.moveTo(point(pointer))
            active[0] = True

        callbacks = OutlineFunctions(Move(protect(move)),
            Line(protect(lambda p, _: converted.lineTo(point(p)))),
            Conic(protect(lambda a, b, _: converted.qCurveTo(point(a), point(b)))),
            Cubic(protect(lambda a, b, d, _: converted.curveTo(point(a), point(b), point(d)))), 0, 0)
        error = self.api.FT_Outline_Decompose(c.byref(result), c.byref(callbacks), None)
        if errors:
            raise ValueError('Stroked outline conversion failed') from errors[0]
        check(error)
        if active[0]:
            converted.closePath()
        glyph = pen.glyph()
        glyph.coordinates.toInt()
        return glyph


def build(source, output, library, reference_ppem=11, native_radius=16):
    """Bake the native stroke at a declared reference size, preserving advances."""
    source, output = Path(source).resolve(), Path(output).resolve()
    if output.exists() or source == output:
        raise ValueError('Use a new output path; original and rollback fonts are read-only')
    if reference_ppem <= 0 or native_radius <= 0:
        raise ValueError('The reference size and native radius must be positive')
    with TTFont(source, recalcTimestamp=False) as original_font, TTFont(source, recalcTimestamp=False) as font:
        if 'glyf' not in font:
            raise ValueError('The Chinese base must contain TrueType outlines')
        cmap = font.getBestCmap()
        selected = {cp for cp in cmap if not merge_fonts.korean(cp)}
        untouched = {cp for cp in cmap if cp not in selected}
        if {cmap[cp] for cp in selected} & {cmap[cp] for cp in untouched}:
            raise ValueError('Korean and non-Korean codepoints share an outline; split their glyphs first')
        before_untouched = merge_fonts.outlines(font, untouched)
        advances = {name: value[0] for name, value in font['hmtx'].metrics.items()}
        radius = otRound(font['head'].unitsPerEm * native_radius / reference_ppem)
        stroker = OutsideStroke(library, radius)
        changed = 0
        try:
            for name in sorted({cmap[cp] for cp in selected}):
                # Composite glyphs must resolve against the immutable source,
                # not a component that an earlier iteration already stroked.
                original = original_font['glyf'][name]
                coordinates, ends, flags = original.getCoordinates(original_font['glyf'])
                if not ends:
                    continue
                old_min, old_max = original.xMin, original.yMax
                glyph = stroker.glyph(coordinates, ends, flags)
                glyph.recalcBounds(font['glyf'])
                advance, bearing = font['hmtx'][name]
                font['hmtx'][name] = (advance, bearing + glyph.xMin - old_min)
                if 'vmtx' in font:
                    height, top = font['vmtx'][name]
                    font['vmtx'][name] = (height, top + old_max - glyph.yMax)
                font['glyf'][name] = glyph
                changed += 1
        finally:
            stroker.close()
        if 'DSIG' in font:
            del font['DSIG']
        font['head'].created = font['head'].modified = 3406620153
        expected = merge_fonts.outlines(font, selected)
        output.parent.mkdir(parents=True, exist_ok=True)
        font.save(output)
    with TTFont(output, recalcTimestamp=False) as result:
        if (merge_fonts.outlines(result, selected) != expected
                or merge_fonts.outlines(result, untouched) != before_untouched
                or any(result['hmtx'][name][0] != width for name, width in advances.items())):
            output.unlink()
            raise ValueError('Serialized stroke changed advances, Korean glyphs or the intended outlines')
    return dict(source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                sha256=hashlib.sha256(output.read_bytes()).hexdigest(), size=output.stat().st_size,
                changed_glyphs=changed, codepoints=len(selected), reference_ppem=reference_ppem,
                native_radius_26_6=native_radius, radius_font_units=radius / 64,
                advance_widths_preserved=True, existing_korean_preserved=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--freetype-library', type=Path, required=True)
    parser.add_argument('--reference-ppem', type=int, default=11)
    parser.add_argument('--native-radius', type=int, default=16)
    args = parser.parse_args()
    print(json.dumps(build(args.source, args.output, args.freetype_library,
                           args.reference_ppem, args.native_radius), sort_keys=True))
