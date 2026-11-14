// node_modules/@zkl2333/freetype-wasm/dist/offsets.mjs
var e = {
	pointerBytes: 4,
	FT_FaceRec: {
		sizeof: 132,
		num_faces: 0,
		num_glyphs: 16,
		family_name: 20,
		style_name: 24,
		num_fixed_sizes: 28,
		num_charmaps: 36,
		face_flags: 8,
		style_flags: 12,
		units_per_EM: 68,
		ascender: 70,
		descender: 72,
		height: 74,
		max_advance_width: 76,
		max_advance_height: 78,
		underline_position: 80,
		underline_thickness: 82,
		glyph: 84,
		size: 88,
		charmap: 92,
		_end: 0
	},
	FT_GlyphSlotRec: {
		sizeof: 160,
		metrics: 24,
		linearHoriAdvance: 56,
		linearVertAdvance: 60,
		advance: 64,
		format: 72,
		bitmap: 76,
		bitmap_left: 100,
		bitmap_top: 104,
		glyph_index: 12,
		_end: 0
	},
	FT_Glyph_Metrics: {
		sizeof: 32,
		width: 0,
		height: 4,
		horiBearingX: 8,
		horiBearingY: 12,
		horiAdvance: 16,
		vertBearingX: 20,
		vertBearingY: 24,
		vertAdvance: 28,
		_end: 0
	},
	FT_Bitmap: {
		sizeof: 24,
		rows: 0,
		width: 4,
		pitch: 8,
		buffer: 12,
		num_grays: 16,
		pixel_mode: 18,
		_end: 0
	},
	FT_Size_Metrics: {
		sizeof: 28,
		x_ppem: 0,
		y_ppem: 2,
		x_scale: 4,
		y_scale: 8,
		ascender: 12,
		descender: 16,
		height: 20,
		max_advance: 24,
		_end: 0
	},
	FT_SizeRec: {
		sizeof: 44,
		metrics: 12,
		_end: 0
	},
	FT_Vector: {
		sizeof: 8,
		x: 0,
		y: 4,
		_end: 0
	},
	FT_CharMapRec: {
		sizeof: 12,
		face: 0,
		encoding: 4,
		platform_id: 8,
		encoding_id: 10,
		_end: 0
	}
}, t = {
	LOAD_DEFAULT: 0,
	LOAD_NO_SCALE: 1,
	LOAD_NO_HINTING: 2,
	LOAD_RENDER: 4,
	LOAD_NO_BITMAP: 8,
	LOAD_FORCE_AUTOHINT: 32,
	LOAD_MONOCHROME: 4096,
	LOAD_TARGET_NORMAL: 0,
	LOAD_TARGET_LIGHT: 65536,
	LOAD_TARGET_MONO: 131072,
	RENDER_MODE_NORMAL: 0,
	RENDER_MODE_LIGHT: 1,
	RENDER_MODE_MONO: 2,
	RENDER_MODE_LCD: 3,
	RENDER_MODE_LCD_V: 4,
	PIXEL_MODE_NONE: 0,
	PIXEL_MODE_MONO: 1,
	PIXEL_MODE_GRAY: 2,
	PIXEL_MODE_GRAY2: 3,
	PIXEL_MODE_GRAY4: 4,
	PIXEL_MODE_LCD: 5,
	PIXEL_MODE_LCD_V: 6,
	PIXEL_MODE_BGRA: 7,
	KERNING_DEFAULT: 0,
	KERNING_UNFITTED: 1,
	KERNING_UNSCALED: 2,
	ENCODING_NONE: 0,
	ENCODING_UNICODE: n("unic"),
	ENCODING_MS_SYMBOL: n("symb"),
	ENCODING_SJIS: n("sjis"),
	ENCODING_PRC: n("gb  "),
	ENCODING_BIG5: n("big5"),
	ENCODING_WANSUNG: n("wans"),
	ENCODING_JOHAB: n("joha"),
	ENCODING_ADOBE_LATIN_1: n("lat1"),
	ENCODING_APPLE_ROMAN: n("armn")
};
function n(e) {
	return ((e.charCodeAt(0) & 255) << 24 | (e.charCodeAt(1) & 255) << 16 | (e.charCodeAt(2) & 255) << 8 | e.charCodeAt(3) & 255) >>> 0;
}
var r = "i32", i;
async function a(e = {}) {
	return i ||= (await import("./freetype-B6WmPDeE.js")).default, new o(await i({
		...e.wasmBinary ? { wasmBinary: e.wasmBinary } : {},
		...e.locateFile ? { locateFile: e.locateFile } : {}
	}));
}
var o = class {
	constructor(t) {
		this.module = t, this.offsets = e;
		let n = (e, n, r) => t.cwrap(e, n, r);
		this._fn = {
			InitFreeType: n("FT_Init_FreeType", "number", ["number"]),
			DoneFreeType: n("FT_Done_FreeType", "number", ["number"]),
			LibraryVersion: n("FT_Library_Version", null, [
				"number",
				"number",
				"number",
				"number"
			]),
			NewMemoryFace: n("FT_New_Memory_Face", "number", [
				"number",
				"number",
				"number",
				"number",
				"number"
			]),
			DoneFace: n("FT_Done_Face", "number", ["number"]),
			SetPixelSizes: n("FT_Set_Pixel_Sizes", "number", [
				"number",
				"number",
				"number"
			]),
			SetCharSize: n("FT_Set_Char_Size", "number", [
				"number",
				"number",
				"number",
				"number",
				"number"
			]),
			GetCharIndex: n("FT_Get_Char_Index", "number", ["number", "number"]),
			LoadGlyph: n("FT_Load_Glyph", "number", [
				"number",
				"number",
				"number"
			]),
			LoadChar: n("FT_Load_Char", "number", [
				"number",
				"number",
				"number"
			]),
			RenderGlyph: n("FT_Render_Glyph", "number", ["number", "number"]),
			GetKerning: n("FT_Get_Kerning", "number", [
				"number",
				"number",
				"number",
				"number",
				"number"
			]),
			SelectCharmap: n("FT_Select_Charmap", "number", ["number", "number"])
		};
		let i = t._malloc(4), a = this._fn.InitFreeType(i);
		if (a) throw t._free(i), Error(`FT_Init_FreeType 失败: ${a}`);
		this.library = t.getValue(i, r), t._free(i);
	}
	version() {
		let e = this.module, t = e._malloc(12);
		this._fn.LibraryVersion(this.library, t, t + 4, t + 8);
		let n = [
			e.getValue(t, r),
			e.getValue(t + 4, r),
			e.getValue(t + 8, r)
		];
		return e._free(t), n;
	}
	newFace(e, t = 0) {
		let n = this.module, i = e instanceof Uint8Array ? e : new Uint8Array(e), a = n._malloc(i.length);
		n.HEAPU8.set(i, a);
		let o = n._malloc(4), c = this._fn.NewMemoryFace(this.library, a, i.length, t, o), l = n.getValue(o, r);
		if (n._free(o), c) throw n._free(a), Error(`FT_New_Memory_Face 失败: ${c}`);
		return new s(this, l, a);
	}
	destroy() {
		this._fn.DoneFreeType(this.library);
	}
}, s = class {
	constructor(e, t, n) {
		this.ft = e, this.module = e.module, this.ptr = t, this._dataPtr = n, this._O = e.offsets;
	}
	_faceField(e, t = r) {
		return this.module.getValue(this.ptr + this._O.FT_FaceRec[e], t);
	}
	_str(e) {
		let t = this._faceField(e);
		return t ? this.module.UTF8ToString(t) : "";
	}
	info() {
		return {
			numFaces: this._faceField("num_faces"),
			numGlyphs: this._faceField("num_glyphs"),
			familyName: this._str("family_name"),
			styleName: this._str("style_name"),
			numCharmaps: this._faceField("num_charmaps"),
			unitsPerEM: this.module.getValue(this.ptr + this._O.FT_FaceRec.units_per_EM, "i16") & 65535,
			ascender: this.module.getValue(this.ptr + this._O.FT_FaceRec.ascender, "i16"),
			descender: this.module.getValue(this.ptr + this._O.FT_FaceRec.descender, "i16"),
			height: this.module.getValue(this.ptr + this._O.FT_FaceRec.height, "i16")
		};
	}
	setPixelSize(e, t = 0) {
		let n = this.ft._fn.SetPixelSizes(this.ptr, t, e);
		if (n) throw Error(`FT_Set_Pixel_Sizes 失败: ${n}`);
		return this;
	}
	setCharSize(e, t, n, r) {
		let i = this.ft._fn.SetCharSize(this.ptr, e, t, n, r);
		if (i) throw Error(`FT_Set_Char_Size 失败: ${i}`);
		return this;
	}
	charIndex(e) {
		return this.ft._fn.GetCharIndex(this.ptr, e >>> 0);
	}
	selectCharmap(e) {
		let t = this.ft._fn.SelectCharmap(this.ptr, e >>> 0);
		if (t) throw Error(`FT_Select_Charmap 失败: ${t}`);
		return this;
	}
	loadGlyph(e = {}) {
		let n = this.module, i = this._O, a = e.flags ?? t.LOAD_DEFAULT, o;
		if (o = e.index == null ? this.ft._fn.LoadChar(this.ptr, (e.char ?? 0) >>> 0, a) : this.ft._fn.LoadGlyph(this.ptr, e.index >>> 0, a), o) throw Error(`FT_Load_${e.index == null ? "Char" : "Glyph"} 失败: ${o}`);
		let s = this._faceField("glyph");
		if (e.render !== !1 && !(a & t.LOAD_RENDER) && (o = this.ft._fn.RenderGlyph(s, e.renderMode ?? t.RENDER_MODE_NORMAL), o)) throw Error(`FT_Render_Glyph 失败: ${o}`);
		let c = s + i.FT_GlyphSlotRec.bitmap, l = i.FT_Bitmap, u = n.getValue(c + l.rows, r) >>> 0, d = n.getValue(c + l.width, r) >>> 0, f = n.getValue(c + l.pitch, r), p = n.getValue(c + l.buffer, r), m = n.getValue(c + l.pixel_mode, "i8") & 255, h = n.getValue(c + l.num_grays, "i16") & 65535, g = Math.abs(f) * u, _ = g > 0 ? n.HEAPU8.slice(p, p + g) : /* @__PURE__ */ new Uint8Array(), v = s + i.FT_GlyphSlotRec.metrics, y = i.FT_Glyph_Metrics, b = (e) => n.getValue(v + e, r), x = s + i.FT_GlyphSlotRec.advance;
		return {
			width: d,
			rows: u,
			pitch: f,
			pixelMode: m,
			numGrays: h,
			bitmapLeft: n.getValue(s + i.FT_GlyphSlotRec.bitmap_left, r),
			bitmapTop: n.getValue(s + i.FT_GlyphSlotRec.bitmap_top, r),
			advance: {
				x: n.getValue(x + i.FT_Vector.x, r),
				y: n.getValue(x + i.FT_Vector.y, r)
			},
			metrics: {
				width: b(y.width),
				height: b(y.height),
				horiBearingX: b(y.horiBearingX),
				horiBearingY: b(y.horiBearingY),
				horiAdvance: b(y.horiAdvance),
				vertBearingX: b(y.vertBearingX),
				vertBearingY: b(y.vertBearingY),
				vertAdvance: b(y.vertAdvance)
			},
			buffer: _
		};
	}
	kerning(e, n, i = t.KERNING_DEFAULT) {
		let a = this.module, o = a._malloc(8), s = this.ft._fn.GetKerning(this.ptr, e >>> 0, n >>> 0, i, o), c = this._O.FT_Vector, l = s ? {
			x: 0,
			y: 0
		} : {
			x: a.getValue(o + c.x, r),
			y: a.getValue(o + c.y, r)
		};
		return a._free(o), l;
	}
	destroy() {
		this.ft._fn.DoneFace(this.ptr), this._dataPtr && this.module._free(this._dataPtr), this._dataPtr = 0;
	}
};
export { a as default };
