import { Fragment as e, Teleport as t, computed as n, createBlock as r, createCommentVNode as i, createElementBlock as a, createElementVNode as o, createVNode as s, defineComponent as c, inject as l, isRef as u, nextTick as d, normalizeClass as f, normalizeStyle as p, onBeforeUnmount as m, onMounted as h, openBlock as g, provide as _, reactive as v, ref as y, renderList as b, renderSlot as x, shallowRef as S, toDisplayString as C, unref as w, vModelSelect as T, vModelText as E, watch as D, withCtx as O, withDirectives as k, withKeys as A, withModifiers as j } from "vue";
// ../tools/game-ui/ui-sounds.js
var M = Object.freeze([
	"mouseIn",
	"press",
	"toggleButtonOn",
	"toggleButtonOff",
	"popup",
	"cancel",
	"fail",
	"notice",
	"event-slidenotice"
]), N = .75;
function P(e) {
	if (!Number.isInteger(e) || e < 0 || e > 100) throw Error("Effect volume must be an integer from 0 to 100.");
	return e / 100;
}
function F({ resourceRoot: e = "./local/", enabled: t = !0, volume: n = N, maxVoices: r = 16, onError: i = (e) => console.error(e) } = {}) {
	if (!Number.isInteger(r) || r < 1 || r > 64) throw Error("UI maxVoices must be an integer from 1 to 64.");
	let a = e.replace(/\/?$/, "/") + "sounds/", o = new AbortController(), s = /* @__PURE__ */ new Map(), c = /* @__PURE__ */ new Set(), l = /* @__PURE__ */ new WeakSet(), u, d, f, p, m = !1, h = 0, g;
	function _(e) {
		if (!Number.isFinite(e) || e < 0 || e > 1) throw Error("UI volume must be between 0 and 1.");
		n = e, d && (d.gain.value = e);
	}
	_(n);
	function v(e) {
		m || i(e instanceof Error ? e : Error(String(e)));
	}
	function y() {
		for (let e of c) e.source.stop(), e.source.disconnect(), e.gain.disconnect();
		c.clear();
	}
	function b(e) {
		t = !!e, h++, t ? S().catch(v) : (g = void 0, y());
	}
	async function x(e) {
		let t = await fetch(e, { signal: o.signal });
		if (!t.ok) throw Error(`UI sound resource ${e}: ${t.status}`);
		return t;
	}
	async function S() {
		if (m) throw Error("UI sound player is disposed.");
		return f ||= (async () => {
			let e = await (await x(a + "manifest.json")).json();
			if (e.version !== 1 || !e.sounds) throw Error("Unsupported UI sound manifest.");
			for (let t of M) {
				let n = e.sounds[t];
				if (!n || !Number.isFinite(n.gain) || n.gain < 0 || n.gain > 1 || !/^[-\w]+\.wav$/.test(n.file)) throw Error(`Invalid UI sound event: ${t}`);
			}
			if (m) return;
			u = new AudioContext(), d = u.createGain(), d.gain.value = n, d.connect(u.destination);
			let t = u;
			await Promise.all(M.map(async (n) => {
				let r = await (await x(a + e.sounds[n].file)).arrayBuffer(), i = await t.decodeAudioData(r);
				m || s.set(n, i);
			})), m || (p = e);
		})(), f;
	}
	function C(e) {
		if (m || !t || u?.state !== "running" || !p || !s.has(e)) return !1;
		if (c.size >= r) {
			let e = c.values().next().value;
			e.source.stop(), e.source.disconnect(), e.gain.disconnect(), c.delete(e);
		}
		let n = u.createBufferSource(), i = u.createGain(), a = {
			source: n,
			gain: i
		};
		return n.buffer = s.get(e), i.gain.value = p.sounds[e].gain, n.connect(i), i.connect(d), c.add(a), n.onended = () => {
			c.delete(a), n.disconnect(), i.disconnect();
		}, n.start(), !0;
	}
	async function w() {
		if (m || !t) return !1;
		let e = u?.resume();
		if (await S(), m || !t || (e ? await e : await u.resume(), m || !t)) return !1;
		let n = g;
		return g = void 0, n?.root.isConnected && C(n.event), u.state === "running";
	}
	async function T(e) {
		if (!M.includes(e)) throw Error(`Unknown UI sound event: ${e}`);
		if (m || !t) return !1;
		let n = h;
		return await S(), m || !t || n !== h ? !1 : C(e);
	}
	function E(e) {
		let n = !0, r = e.dataset.bfOpenSound, i = /* @__PURE__ */ new WeakSet();
		if (r && !M.includes(r)) throw Error(`Unknown opening sound: ${r}`);
		let a = () => n && !m && e.isConnected, o = (e) => e.target instanceof Element ? e.target.closest("button") : null, s = (t) => t && e.contains(t) && !t.disabled && t.getAttribute("aria-disabled") !== "true", c = (e) => !l.has(e) && (l.add(e), !0), d = (e, n = !1) => {
			if (t && a()) {
				if (n) {
					let t = h;
					w().then((n) => {
						n && a() && t === h && C(e);
					}).catch(v);
				} else C(e);
			}
		}, f = (e) => {
			if (e.pointerType === "touch") return;
			let t = o(e);
			s(t) && !i.has(t) && !(e.relatedTarget instanceof Node && t.contains(e.relatedTarget)) && c(e) && (i.add(t), d("mouseIn"));
		}, p = (e) => {
			let t = o(e);
			t && !(e.relatedTarget instanceof Node && t.contains(e.relatedTarget)) && i.delete(t);
		}, _ = (e) => {
			if (e.button !== 0) return;
			let t = o(e);
			s(t) && c(e) && d(t.dataset.bfPressSound || "press", !0);
		}, y = (e) => {
			if (e.repeat || !["Enter", " "].includes(e.key)) return;
			let t = o(e);
			s(t) && c(e) && d(t.dataset.bfPressSound || "press", !0);
		}, b = (e) => {
			let t = e.target;
			t instanceof HTMLInputElement && t.type === "checkbox" && t.closest(".bf-check") && !t.disabled && c(e) && d(t.checked ? "toggleButtonOn" : "toggleButtonOff", !0);
		};
		return e.addEventListener("pointerover", f), e.addEventListener("pointerout", p), e.addEventListener("pointerdown", _, !0), e.addEventListener("keydown", y), e.addEventListener("change", b), r && t && (u?.state === "running" ? C(r) : g = {
			root: e,
			event: r
		}), () => {
			n = !1, g?.root === e && (g = void 0), e.removeEventListener("pointerover", f), e.removeEventListener("pointerout", p), e.removeEventListener("pointerdown", _, !0), e.removeEventListener("keydown", y), e.removeEventListener("change", b);
		};
	}
	let D = (e) => {
		e.isTrusted && t && u?.state !== "running" && w().catch(v);
	};
	document.addEventListener("pointerdown", D, !0), document.addEventListener("keydown", D, !0);
	function O() {
		m || (m = !0, h++, g = void 0, o.abort(), y(), s.clear(), document.removeEventListener("pointerdown", D, !0), document.removeEventListener("keydown", D, !0), u && u.close().catch(v));
	}
	return {
		preload: S,
		unlock: w,
		play: T,
		bind: E,
		setEnabled: b,
		setVolume: _,
		dispose: O
	};
}
// ../tools/game-ui/theme.js
function I(e, t = "v2") {
	if (![
		"v1",
		"v2",
		"v3"
	].includes(t)) throw Error(`Unknown UI theme: ${t}`);
	if (e === void 0 || e === "") return t;
	if (typeof e != "string" || !/^\d{4}-\d{2}-\d{2}$/.test(e)) throw Error("Client date must use YYYY-MM-DD.");
	let n = /* @__PURE__ */ new Date(e + "T00:00:00Z");
	if (!Number.isFinite(n.getTime()) || n.toISOString().slice(0, 10) !== e) throw Error(`Invalid client date: ${e}`);
	return e >= "2017-02-07" ? "v3" : t;
}
// ../tools/game-ui/text.js
async function L(e, t, n = {}) {
	let r = getComputedStyle(e), i = n.size || parseFloat(r.fontSize) || 13, a = n.family || "BFMalgun", o = n.weight || 400;
	try {
		await document.fonts.load(`${o} ${i}px ${a}`);
	} catch (e) {
		throw Error(`Unable to load UI font ${a} at weight ${o}.`, { cause: e });
	}
	let s = n.renderer || globalThis.__BF_TEXT_RENDERER || "canvas";
	if (s === "svg") return R(e, t, n, {
		size: i,
		family: a,
		weight: o
	});
	if (s === "native") return z(e, t, n, {
		size: i,
		family: a,
		weight: o
	});
	if (s === "freetype") return re(e, t, n, {
		size: i,
		family: a,
		weight: o
	});
	let c = n.width || parseFloat(e.style.width) || e.clientWidth, l = n.height || parseFloat(e.style.height) || e.clientHeight;
	if (!(c > 0 && l > 0)) throw Error("Text requires positive logical dimensions.");
	let u = document.createElement("canvas"), d = n.samples ?? Number(globalThis.__BF_TEXT_SAMPLES), f = Number.isFinite(d) && d >= 1 ? d : 4;
	u.width = Math.ceil(c * f), u.height = Math.ceil(l * f), u.style.cssText = `position:absolute;inset:0;width:${c}px;height:${l}px;pointer-events:none`, u.ariaHidden = "true";
	let p = u.getContext("2d");
	p.scale(f, f), p.imageSmoothingEnabled = !0, p.imageSmoothingQuality = "high", p.font = `${o} ${i}px ${a}`, p.fillStyle = n.color || e.style.color || r.color || "#5b5b5b", p.textBaseline = n.nativeMetrics || n.centerInk ? "alphabetic" : "middle", p.textAlign = n.align || "left", p.lineJoin = "round";
	let m = [];
	for (let e of t.split("\n")) {
		let t = "";
		for (let r of e) n.wrap && t && p.measureText(t + r).width > c - 4 && (m.push(t), t = ""), t += r;
		m.push(t);
	}
	let h = n.lineHeight || i + 1, g = n.decoration ?? (n.outline ? 4 : 1), _ = g === 4 || g === 2 ? 2 : +(g === 3), v = p.measureText(t.split("\n")[0] || " "), y = Math.ceil(i + v.actualBoundingBoxDescent) + _ * 2, b = (n.vertical === "top" ? 0 : Math.floor(l / 2) - Math.floor(y / 2)) + _ + i - 1, x = n.centerInk ? (l - v.actualBoundingBoxAscent - v.actualBoundingBoxDescent) / 2 + v.actualBoundingBoxAscent : n.nativeMetrics ? b : n.vertical === "top" ? i / 2 + 2 : (l - (m.length - 1) * h) / 2, S = p.textAlign === "center" ? c / 2 : p.textAlign === "right" ? c - 2 : n.inset ?? 2;
	return m.forEach((e, t) => {
		let r = x + t * h + (n.baselineOffset || 0);
		n.outline && g > 1 && (p.strokeStyle = n.outline, p.lineWidth = 2 * (n.outlineWidth || .75), p.strokeText(e, S, r)), p.fillText(e, S, r);
	}), e.textContent = t, e.style.color = "transparent", e.classList.add("bf-canvas-text"), e.dataset.text = t, e.append(u), e;
}
function R(e, t, n, { size: r, family: i, weight: a }) {
	let o = n.width || parseFloat(e.style.width) || e.clientWidth, s = n.height || parseFloat(e.style.height) || e.clientHeight;
	if (!(o > 0 && s > 0)) throw Error("Text requires positive logical dimensions.");
	let c = document.createElement("canvas").getContext("2d");
	c.font = `${a} ${r}px ${i}`, c.textBaseline = "alphabetic";
	let l = c.measureText(t || " "), u = "http://www.w3.org/2000/svg", d = document.createElementNS(u, "svg");
	d.setAttribute("width", String(o)), d.setAttribute("height", String(s)), d.setAttribute("aria-hidden", "true"), d.style.cssText = `position:absolute;inset:0;width:${o}px;height:${s}px;pointer-events:none;overflow:hidden`;
	let f = document.createElementNS(u, "text"), p = n.align || "left";
	return f.setAttribute("x", String(p === "center" ? o / 2 : p === "right" ? o - 2 : n.inset ?? 2)), f.setAttribute("y", String((s - l.actualBoundingBoxAscent - l.actualBoundingBoxDescent) / 2 + l.actualBoundingBoxAscent)), f.setAttribute("text-anchor", p === "center" ? "middle" : p === "right" ? "end" : "start"), f.style.font = `${a} ${r}px ${i}`, f.style.fontSynthesis = "none", f.setAttribute("fill", n.color || "#5b5b5b"), n.outline && (f.setAttribute("stroke", n.outline), f.setAttribute("stroke-width", String((n.outlineWidth ?? .5) * 2)), f.setAttribute("stroke-linejoin", "round"), f.setAttribute("paint-order", "stroke fill")), f.textContent = t, d.append(f), e.textContent = t, e.style.color = "transparent", e.dataset.text = t, e.append(d), e;
}
function z(e, t, n, r) {
	let { size: i, family: a, weight: o } = r, s = getComputedStyle(e), c = n.decoration ?? (n.outline ? 4 : 1), l = n.align || "left", u = n.vertical || "middle";
	return e.textContent = t, e.classList.remove("bf-canvas-text"), Object.assign(e.style, {
		color: n.color || e.style.color || s.color || "#5b5b5b",
		fontFamily: a,
		fontSize: `${i}px`,
		fontWeight: String(o),
		fontSynthesis: "none",
		textRendering: "geometricPrecision",
		whiteSpace: "pre-wrap",
		textAlign: l,
		lineHeight: `${n.lineHeight || i + 1}px`,
		display: "flex",
		alignItems: u === "top" ? "flex-start" : u === "bottom" ? "flex-end" : "center",
		justifyContent: l === "center" ? "center" : l === "right" ? "flex-end" : "flex-start",
		padding: `${n.inset ?? 0}px`,
		boxSizing: "border-box",
		WebkitFontSmoothing: "antialiased",
		MozOsxFontSmoothing: "grayscale",
		WebkitTextStroke: n.outline && c > 1 ? `${n.outlineWidth || 1}px ${n.outline}` : "0 transparent",
		paintOrder: "stroke fill"
	}), e.dataset.text = t, e;
}
var B = /* @__PURE__ */ new Map(), V = null, H = null, U = 2e3, ee = 200, W = /* @__PURE__ */ new Map(), G = /* @__PURE__ */ new Map();
async function te() {
	return H || (V ||= (async () => {
		let { default: e } = await import("./dist--VOHifh3.js"), t = await e(), n = t.module._malloc(4);
		t.module._FT_Stroker_New(t.library, n);
		let r = t.module.getValue(n, "i32");
		t.module._FT_Stroker_Set(r, 16, 0, 0, 1), t.module._FT_Stroker_New(t.library, n);
		let i = t.module.getValue(n, "i32");
		t.module._FT_Stroker_Set(i, 9, 0, 0, 1), t.module._FT_Stroker_New(t.library, n);
		let a = t.module.getValue(n, "i32");
		t.module._FT_Stroker_Set(a, 32, 1, 0, 1), t.module._FT_Stroker_New(t.library, n);
		let o = t.module.getValue(n, "i32");
		t.module._FT_Stroker_Set(o, 128, 1, 0, 1), t.module._free(n);
		let s = /* @__PURE__ */ new Map();
		function c(e, n) {
			if (s.has(e)) return s.get(e);
			let r = t.newFace(n);
			return s.set(e, r), r;
		}
		return {
			ft: t,
			getFace: c,
			strokerBold: r,
			strokerMedium: i,
			strokerDeco32: a,
			strokerDeco128: o,
			dispose() {
				t.module._FT_Stroker_Done(r), t.module._FT_Stroker_Done(i), t.module._FT_Stroker_Done(a), t.module._FT_Stroker_Done(o);
				for (let e of s.values()) e.destroy();
				s.clear(), t.destroy(), H = null, V = null;
			}
		};
	})(), H = await V, H);
}
function K(e, t = {
	r: 91,
	g: 91,
	b: 91,
	a: 255
}) {
	if (!e) return t;
	let n = e.trim();
	return n.startsWith("#") && (n = n.slice(1)), n.length === 3 && (n = n.split("").map((e) => e + e).join("")), n.length === 6 ? {
		r: parseInt(n.slice(0, 2), 16),
		g: parseInt(n.slice(2, 4), 16),
		b: parseInt(n.slice(4, 6), 16),
		a: 255
	} : t;
}
function ne(e, t, { size: n, weight: r = 400, glyphWidths: i = null }) {
	let a = t.exactPixelSize ? n : Math.max(11, n), o = r === 700 || t.weight === 700 || t.bold === !0, s = t.decoration ?? (t.outline ? 4 : 1), c = s === 4 || s === 2 ? 2 : +(s === 3), l = t.lineHeight || a + 1, u = t.inset ?? 2, d = t.width, f = t.height;
	if (!(d > 0 && f > 0)) throw Error("Text requires positive logical dimensions.");
	let p = (e) => i && i.has(e) ? i.get(e) : e.charCodeAt(0) > 127 ? a : Math.ceil(a * .55), m = e.split("\n"), h = [], g = d - c * 2 - u * 2;
	for (let e of m) {
		if (!t.wrap) {
			let t = 0;
			for (let n of e) t += p(n);
			h.push({
				text: e,
				width: t
			});
			continue;
		}
		let n = "", r = 0;
		for (let t of e) {
			let e = p(t);
			n && r + e > g && (h.push({
				text: n,
				width: r
			}), n = "", r = 0), n += t, r += e;
		}
		h.push({
			text: n,
			width: r
		});
	}
	let _ = (h.length - 1) * l + a + c * 2, v = c + a - 1;
	return v = t.vertical === "top" ? c + a - 1 : t.vertical === "bottom" ? f - _ + c + a - 1 : Math.floor(f / 2) - Math.floor(_ / 2) + c + a - 1, v += t.baselineOffset || 0, {
		clampedSize: a,
		isBold: o,
		decoration: s,
		border: c,
		spacing: l,
		lines: h.map((e, n) => {
			let r = u + c;
			return t.align === "center" ? r = Math.floor((d - (e.width + c * 2)) / 2) + c : t.align === "right" && (r = d - e.width - c - u), {
				text: e.text,
				x: r,
				y: v + n * l,
				width: e.width
			};
		}),
		contentHeight: _,
		firstLineY: v
	};
}
async function re(e, t, n, r) {
	let i = n.width || parseFloat(e.style.width) || e.clientWidth, a = n.height || parseFloat(e.style.height) || e.clientHeight;
	if (!(i > 0 && a > 0)) throw Error("Text requires positive logical dimensions.");
	let o = Math.max(.25, (n.pixelRatio ?? 1) * (globalThis.devicePixelRatio || 1)), s = Math.ceil(i * o), c = Math.ceil(a * o), { size: l, family: u, weight: d = 400 } = r, f = Math.max(1, Math.round(Math.max(11, l) * o)), p = d === 700 || n.weight === 700 || n.bold === !0, m = !p && (d === 600 || n.weight === 600), h = n.decoration ?? (n.outline ? 4 : 1), g = JSON.stringify([
		t,
		u,
		l,
		d,
		i,
		a,
		o,
		n.color || e.style.color || "",
		n.outline || "",
		h,
		n.align,
		n.vertical,
		!!n.centerInk,
		n.inset,
		!!n.wrap,
		n.lineHeight,
		n.baselineOffset,
		!!n.bold,
		!!n.nativeMetrics
	]), _ = W.get(g);
	if (_) {
		W.delete(g), W.set(g, _);
		let n = document.createElement("canvas");
		return n.width = s, n.height = c, n.style.cssText = `position:absolute;inset:0;width:${i}px;height:${a}px;pointer-events:none`, n.ariaHidden = "true", n.getContext("2d").putImageData(new ImageData(new Uint8ClampedArray(_), s, c), 0, 0), ie(e, t, n);
	}
	let v = n.fontData || n.fontBuffer || B.get(u);
	if (!v && typeof fetch == "function") {
		let e = (n.resourceRoot || globalThis.__BF_RESOURCE_ROOT || "").replace(/\/?$/, "/"), t = {
			BF: "GB13000.woff2",
			BFMalgun: "DF_GB_Y9.woff2",
			BFKorean: "NexonBTBold.woff2"
		}[u];
		try {
			let n = t ? await fetch(`${e}fonts/${t}`) : null;
			if (n?.ok) {
				let e = await n.arrayBuffer();
				v = new Uint8Array(e), B.set(u, v);
			}
		} catch {}
	}
	if (!v) throw Error(`FreeType font data for family '${u}' is not loaded or registered.`);
	let y = await te(), b = y.getFace(u, v);
	b.setPixelSize(f, f);
	let x = /* @__PURE__ */ new Map();
	for (let e of t) {
		if (e === "\n" || x.has(e)) continue;
		let t = b.charIndex(e.charCodeAt(0));
		t ||= b.charIndex(9633) || b.charIndex(32), y.ft.module._FT_Load_Glyph(b.ptr, t, 40968);
		let n = y.ft.module.getValue(b.ptr + y.ft.offsets.FT_FaceRec.glyph, "i32"), r = y.ft.module.getValue(n + y.ft.offsets.FT_GlyphSlotRec.advance + y.ft.offsets.FT_Vector.x, "i32") >> 6;
		x.set(e, r);
	}
	let S = ne(t, {
		...n,
		width: s,
		height: c,
		exactPixelSize: !0,
		inset: Math.round((n.inset ?? 2) * o),
		lineHeight: n.lineHeight ? Math.round(n.lineHeight * o) : void 0
	}, {
		size: f,
		weight: d,
		glyphWidths: x
	}), C = document.createElement("canvas");
	C.width = Math.ceil(s), C.height = Math.ceil(c), C.style.cssText = `position:absolute;inset:0;width:${i}px;height:${a}px;pointer-events:none`, C.ariaHidden = "true";
	let w = C.getContext("2d");
	if (!w) throw Error("Unable to obtain 2D rendering context for canvas.");
	let T = w.createImageData ? w.createImageData(C.width, C.height) : {
		data: new Uint8Array(C.width * C.height * 4),
		width: C.width,
		height: C.height
	}, E = T.data, D = getComputedStyle(e), O = K(n.color || e.style.color || D?.color || "#5b5b5b"), k = K(n.outline || "#ffffff");
	function A(e, t, n, r, i, a) {
		if (e < 0 || e >= C.width || t < 0 || t >= C.height) return;
		let o = (t * C.width + e) * 4, s = E[o + 3], c = Math.min(255, Math.round(s + a * (255 - s) / 255));
		if (c === 0) return;
		let l = Math.round(255 * s / c);
		E[o] = Math.round((l * E[o] + (255 - l) * n) / 255), E[o + 1] = Math.round((l * E[o + 1] + (255 - l) * r) / 255), E[o + 2] = Math.round((l * E[o + 2] + (255 - l) * i) / 255), E[o + 3] = c;
	}
	function j(e, t, n, r, i = null) {
		t = Math.round(t), n = Math.round(n);
		let a = b.charIndex(e);
		a ||= b.charIndex(9633) || b.charIndex(32);
		let o = `${u}:${f}:${p ? 1 : m ? 2 : 0}:${+!!i}:${e}`, s = G.get(o);
		if (!s) {
			y.ft.module._FT_Load_Glyph(b.ptr, a, 40968);
			let e = y.ft.module.getValue(b.ptr + y.ft.offsets.FT_FaceRec.glyph, "i32"), t = y.ft.module._malloc(4);
			y.ft.module._FT_Get_Glyph(e, t), (p || m) && y.ft.module._FT_Glyph_StrokeBorder(t, p ? y.strokerBold : y.strokerMedium, 0, 1), i && y.ft.module._FT_Glyph_StrokeBorder(t, i, 0, 1), y.ft.module._FT_Glyph_To_Bitmap(t, 0, 0, 1);
			let n = y.ft.module.getValue(t, "i32"), r = y.ft.module.getValue(n + 20, "i32"), c = y.ft.module.getValue(n + 24, "i32"), l = y.ft.module.getValue(n + 28 + y.ft.offsets.FT_Bitmap.rows, "i32"), u = y.ft.module.getValue(n + 28 + y.ft.offsets.FT_Bitmap.width, "i32"), d = y.ft.module.getValue(n + 28 + y.ft.offsets.FT_Bitmap.pitch, "i32"), f = y.ft.module.getValue(n + 28 + y.ft.offsets.FT_Bitmap.buffer, "i32"), h = new Uint8Array(l * u);
			for (let e = 0; e < l; e++) for (let t = 0; t < u; t++) h[e * u + t] = y.ft.module.HEAPU8[f + e * d + t];
			y.ft.module._FT_Done_Glyph(n), y.ft.module._free(t), s = {
				left: r,
				top: c,
				rows: l,
				cols: u,
				coverageBuffer: h
			}, G.size >= U && G.delete(G.keys().next().value), G.set(o, s);
		}
		let { left: c, top: l, rows: d, cols: h, coverageBuffer: g } = s;
		for (let e = 0; e < d; e++) for (let i = 0; i < h; i++) {
			let a = g[e * h + i];
			if (a > 0) {
				let o = Math.round(r.a * a / 255);
				A(t + c + i, n - l + e, r.r, r.g, r.b, o);
			}
		}
	}
	for (let e of S.lines) {
		let t = e.x, n = Math.round(2 * o);
		h === 2 && (t -= n);
		let r = h === 2 ? e.y - n : e.y;
		for (let n = 0; n < e.text.length; n++) {
			let i = e.text[n];
			j(i.charCodeAt(0), t, r, O, null), t += x.get(i) || 0;
		}
	}
	if (h > 1) {
		let e = h === 4 ? y.strokerDeco128 : y.strokerDeco32;
		for (let t of S.lines) {
			let n = t.x;
			for (let r = 0; r < t.text.length; r++) {
				let i = t.text[r];
				j(i.charCodeAt(0), n, t.y, k, e), n += x.get(i) || 0;
			}
		}
	}
	if (n.centerInk) {
		let e = C.width * 4, t = -1, n = -1;
		for (let r = 0; r < C.height; r++) for (let i = r * e + 3; i < (r + 1) * e; i += 4) if (E[i]) {
			t < 0 && (t = r), n = r;
			break;
		}
		let r = t < 0 ? 0 : Math.floor((C.height - (n - t + 1)) / 2) - t;
		if (r) {
			let i = new Uint8ClampedArray(E.length);
			for (let a = t; a <= n; a++) {
				let t = a + r;
				t >= 0 && t < C.height && i.set(E.subarray(a * e, (a + 1) * e), t * e);
			}
			E.set(i);
		}
	}
	for (w.putImageData(T, 0, 0), W.set(g, E.slice()); W.size > ee;) W.delete(W.keys().next().value);
	return ie(e, t, C);
}
function ie(e, t, n) {
	return e.textContent = t, e.style.color = "transparent", e.classList.remove("bf-canvas-text"), e.classList.add("bf-freetype-text"), e.dataset.text = t, e.append(n), e;
}
// ../tools/game-ui/native-layout.js
function ae(e, t, n, r, i = 1) {
	if (![
		e,
		t,
		n,
		r,
		i
	].every(Number.isFinite) || e <= 41 || t < 55 || r <= n || i < 1) throw Error("Invalid native track geometry");
	let a = r - n + 1, o = a % i === 1 ? i : 1, s = (e - 41) / (r - n);
	return {
		railY: t - 35,
		ticks: Array.from({ length: Math.floor(a / o) + 1 }, (e, t) => Math.trunc(20 + o * s * t)),
		knobX: (e) => Math.trunc(15 + s * (Math.max(n, Math.min(r, e)) - n))
	};
}
function oe(e, t, n, r = 1, i = 15) {
	if (![
		e,
		t,
		n,
		r,
		i
	].every(Number.isFinite) || e <= 39 || n <= t || r < 1 || i < 0) throw Error("Invalid compact track geometry");
	let a = n - t + 1, o = a % r === 1 ? r : 1, s = (e - 39) / (n - t);
	return {
		railY: i + 2,
		ticks: Array.from({ length: Math.floor(a / o) + 1 }, (e, t) => Math.trunc(20 + o * s * t)),
		knobX: (e) => Math.trunc(15 + s * (Math.max(t, Math.min(n, e)) - t))
	};
}
// ../tools/game-ui/extension-settings.js
var se = 0;
function ce(e = {}) {
	let t = {
		oneClickRun: !1,
		healthDisplay: "percent",
		allowEscLeave: !1,
		bgm: "old",
		...e
	};
	if (typeof t.oneClickRun != "boolean" || typeof t.allowEscLeave != "boolean" || !["percent", "value"].includes(t.healthDisplay) || !["old", "new"].includes(t.bgm)) throw Error("Invalid extension settings.");
	return t;
}
async function le(e, { value: t = {}, onChange: n = () => {} } = {}) {
	let r = ce(t), i = ++se, a = await e.dialog({
		title: "扩展设置",
		text: "",
		contentWidth: 350,
		contentHeight: 250,
		buttonY: 260,
		buttons: [{
			name: "Ok",
			label: "确认"
		}, {
			name: "Cancel",
			label: "取消"
		}],
		body: async (t) => {
			let o = await e.settingsPanel(t, 3, 27, 344, 224);
			o.dataset.bfSettingsPanel = "";
			let s = /* @__PURE__ */ new Map();
			async function c(t, i, o, c, l, u) {
				let d = i === "healthPercent" || i === "healthValue" || i === "oldBgm" || i === "newBgm", f = () => i === "healthPercent" ? r.healthDisplay === "percent" : i === "healthValue" ? r.healthDisplay === "value" : i === "oldBgm" ? r.bgm === "old" : i === "newBgm" ? r.bgm === "new" : r[i], p = document.createElement("label");
				Object.assign(p.style, {
					position: "absolute",
					left: c + "px",
					top: l + "px",
					width: u + "px",
					height: "20px",
					cursor: "pointer"
				}), t.append(p);
				let m = await e.renderNode({
					type: "UiCheckBox",
					attrs: {
						Name: i,
						PosX: "0",
						PosY: "0",
						Width: "21",
						Height: "18",
						Check: f() ? "1" : "0"
					},
					children: []
				}, p), h = m.querySelector("input");
				h.ariaLabel = o;
				let g = await e.settingsLabel(p, o, 28, 0, u - 28, 20);
				g.style.pointerEvents = "none", s.set(i, {
					element: m,
					checked: f
				}), h.addEventListener("change", () => {
					d ? i === "healthPercent" || i === "healthValue" ? r.healthDisplay = i === "healthPercent" ? "percent" : "value" : r.bgm = i === "oldBgm" ? "old" : "new" : r[i] = h.checked;
					for (let { element: e, checked: t } of s.values()) e.setValue(t());
					n({ ...r }), a.dispatchEvent(new CustomEvent("bf-settings-change", {
						bubbles: !0,
						detail: { ...r }
					}));
				});
			}
			let l = document.createElement("div"), u = document.createElement("div");
			o.append(l, u);
			for (let e of [l, u]) Object.assign(e.style, {
				position: "absolute",
				inset: "0"
			});
			let d = await e.settingsGroup(l, "操作设置", 5, 5, 334, 110);
			await c(d, "oneClickRun", "一键跑步", 17, 35, 300), await c(d, "allowEscLeave", "允许ESC中途离场", 17, 70, 300);
			let f = await e.settingsGroup(l, "血量显示", 5, 120, 334, 98);
			await c(f, "healthPercent", "显示百分比血量", 17, 41, 145), await c(f, "healthValue", "显示数值血量", 184, 41, 133);
			let p = await e.settingsGroup(u, "背景音乐", 5, 5, 334, 213);
			await c(p, "oldBgm", "使用旧版BGM", 17, 39, 300), await c(p, "newBgm", "使用新版BGM", 17, 78, 300), await e.settingsLabel(p, "选择游戏中播放的背景音乐版本。", 17, 126, 300, 22, !0);
			let m = [];
			function h(e) {
				l.hidden = e !== 0, u.hidden = e !== 1;
				for (let t = 0; t < m.length; t++) m[t].setActive(t === e);
			}
			for (let [n, r] of ["游戏设置", "声音设置"].entries()) {
				let a = await e.textTab(t, r, 12 + n * 107, 0, 104, 28);
				a.id = `bf-extension-tab-${i}-${n}`, a.setAttribute("aria-controls", `bf-extension-page-${i}-${n}`);
				let o = n ? u : l;
				o.id = `bf-extension-page-${i}-${n}`, o.setAttribute("role", "tabpanel"), o.setAttribute("aria-labelledby", a.id), a.addEventListener("click", () => h(n)), a.addEventListener("keydown", (e) => {
					if ([
						"ArrowLeft",
						"ArrowRight",
						"Home",
						"End"
					].includes(e.key)) {
						e.preventDefault();
						let t = e.key === "Home" ? 0 : e.key === "End" ? 1 : 1 - n;
						h(t), m[t].focus();
					}
				}), m.push(a);
			}
			h(0);
		}
	});
	return a.addEventListener("bf-action", (e) => {
		e.detail.settings = { ...r };
	}), a.getSettings = () => ({ ...r }), a;
}
// ../tools/game-ui/game-ui.js
async function ue({ theme: e = "v2", resourceRoot: t = "./local/", sounds: n } = {}) {
	if (![
		"v1",
		"v2",
		"v3"
	].includes(e)) throw Error(`Unknown UI theme: ${e}`);
	let r = () => `${t.replace(/\/?$/, "/")}${e}/`, i = await fetch(r() + "manifest.json");
	if (!i.ok) throw Error(`Unable to load ${e} UI resources (${i.status}).`);
	let a = await i.json(), o = (e) => a.imageFormat === "webp" ? e.replace(/\.png$/i, ".webp") : e, s = (e, t, n = 0) => Number(e[t] || n);
	function c(e, t, n, r, i) {
		return Object.assign(e.style, {
			left: `${t}px`,
			top: `${n}px`,
			width: `${r}px`,
			height: `${i}px`
		}), e;
	}
	async function l(e, t, n, i, s, l, u) {
		let d = a.sprites[t]?.findIndex((e) => e.every((e, t) => e === n[t]));
		if (d === void 0 || d < 0) throw Error(`Missing sprite frame: ${t}`);
		let f = c(document.createElement("span"), i, s, l, u);
		f.className = "tile";
		let p = Object.assign(new Image(), { crossOrigin: "anonymous" });
		p.src = r() + t.replace(/\.smf$/i, "") + "/" + o(d + ".png"), Object.assign(p.style, {
			width: "100%",
			height: "100%",
			left: "0",
			top: "0"
		}), f.append(p), e.append(f);
	}
	let u = /* @__PURE__ */ new Map();
	async function d(e, t) {
		let n = e + ":" + t;
		return u.has(n) || u.set(n, (async () => {
			let n = Object.assign(new Image(), { crossOrigin: "anonymous" });
			return n.src = r() + e.replace(/\.smf$/i, "") + "/" + o(t + ".png"), await n.decode(), n;
		})()), u.get(n);
	}
	async function f(e, t, n, r) {
		let i = a.sprites[t];
		if (!i) throw Error(`Missing sprite: ${t}`);
		let o = document.createElement("canvas");
		o.className = "skin", o.width = n, o.height = r, o.style.width = n + "px", o.style.height = r + "px", o.ariaHidden = "true";
		let s = o.getContext("2d");
		if (s.imageSmoothingEnabled = !1, i.length !== 9) s.drawImage(await d(t, 0), 0, 0, n, r);
		else {
			let e = [
				i[0][2] - i[0][0] + 1,
				0,
				i[2][2] - i[2][0] + 1
			], a = [
				i[0][3] - i[0][1] + 1,
				0,
				i[6][3] - i[6][1] + 1
			], o = (e, t) => {
				if (e[0] + e[2] > t) {
					let n = t / (e[0] + e[2]);
					e[0] = Math.floor(e[0] * n), e[2] = t - e[0];
				}
				e[1] = t - e[0] - e[2];
			};
			o(e, n), o(a, r);
			let c = await Promise.all(i.map((e, n) => d(t, n))), l = 0;
			for (let t = 0; t < 3; t++) {
				let n = 0;
				for (let r = 0; r < 3; r++) e[r] && a[t] && s.drawImage(c[t * 3 + r], n, l, e[r], a[t]), n += e[r];
				l += a[t];
			}
		}
		let c = Object.assign(new Image(), { crossOrigin: "anonymous" });
		return c.className = "skin", c.width = n, c.height = r, c.style.width = n + "px", c.style.height = r + "px", c.alt = "", c.ariaHidden = "true", c.src = o.toDataURL("image/png"), await c.decode(), e.prepend(c), c;
	}
	async function p(t, n, r, i, o, s, u, d, p = {}) {
		if (e === "v3" && (!d || d.includes("/dialogueNew/"))) {
			let e = c(document.createElement("button"), r, i, o, s);
			e.className = "control sprite-button", e.ariaLabel = u, e.dataset.control = p.Name || n, t.append(e);
			let l = n === "Ok" ? "Images/Ui/dialogueNew/dialogue_type01BTN-Up.smf" : a.scheme.UiButton.SkinImage_Up, m = d || p.SpriteSetUp || l, h = 0, g = !1, _ = [
				m,
				p.SpriteSetDown || m.replace(/(?:-Up|_up)\.smf$/, m.includes("Tab_") ? "_down.smf" : "-Down.smf"),
				p.SpriteSetHover || m.replace(/(?:-Up|_up)\.smf$/, m.includes("Tab_") ? "_hover.smf" : "-Hover.smf"),
				p.SpriteSetDisabled || m.replace(/(?:-Up|_up)\.smf$/, m.includes("Tab_") ? "_disable.smf" : "-Disabled.smf")
			];
			async function v(t) {
				let n = ++h, r = document.createElement("span");
				r.className = "control", c(r, 0, 0, o, s), r.style.pointerEvents = "none", await f(r, _[e.disabled ? 3 : g ? 1 : t], o, s);
				let i = c(document.createElement("span"), 0, 0, o, s);
				i.className = "control", r.append(i), await L(i, u, {
					family: "BF",
					size: Number(p.FontSize) || 16,
					weight: 400,
					align: "center",
					color: e.disabled ? "#e1e9eb" : p.TextColor ? "#" + p.TextColor.slice(-6) : "#232323",
					decoration: 1,
					nativeMetrics: !0,
					vertical: "middle",
					inset: 0
				}), n === h && e.replaceChildren(r);
			}
			return await v(0), e.setSelected = async (t) => {
				g = !!t, e.ariaPressed = String(g), await v(0);
			}, e.addEventListener("pointerenter", () => {
				v(2);
			}), e.addEventListener("pointerleave", () => {
				v(0);
			}), e.addEventListener("pointerdown", () => {
				v(1);
			}), e.addEventListener("pointerup", () => {
				v(2);
			}), e.addEventListener("pointercancel", () => {
				v(0);
			}), e.addEventListener("click", () => e.dispatchEvent(new CustomEvent("bf-action", {
				bubbles: !0,
				detail: {
					name: n,
					text: u
				}
			}))), e;
		}
		let m = c(document.createElement("button"), r, i, o, s);
		m.className = "control sprite-button", m.ariaLabel = u, t.append(m);
		let h = d || `Images/Ui/DialogBtn${n}.smf`, g = 0;
		async function _(t) {
			if (g = t, m.replaceChildren(), e === "v1" && (!d || /DialogButton(?:Ok|Cancel)-Up/.test(d))) {
				let e = [
					"Up",
					"Down",
					"Hover",
					"Disabled"
				][t], r = d?.match(/DialogButton(Ok|Cancel)-Up/)?.[1] ?? n;
				await f(m, `Images/Ui/DialogButton${r}-${e}.smf`, o, s);
				let i = document.createElement("span");
				i.className = "text", i.style.cssText = "left:14px;right:0;top:0;bottom:0;width:" + (o - 14) + "px;height:" + s + "px", m.append(i), await L(i, u, {
					family: "BFMalgun",
					size: 13,
					weight: 400,
					width: o - 14,
					height: s,
					align: "center",
					color: "white",
					outline: "#79a4cf",
					outlineWidth: .5
				});
			} else {
				let e = a.sprites[h];
				if (!e) throw Error(`Missing button: ${h}`);
				await l(m, h, e[Math.min(t, e.length - 1)], 0, 0, o, s);
			}
		}
		return await _(0), m.setSelected = async (e) => {
			m.ariaPressed = String(e), await _(e ? 3 : 0);
		}, m.addEventListener("pointerenter", () => _(m.ariaPressed === "true" ? 3 : 2)), m.addEventListener("pointerleave", () => _(m.ariaPressed === "true" ? 3 : 0)), m.addEventListener("pointerdown", () => _(1)), m.addEventListener("pointerup", () => _(m.ariaPressed === "true" ? 3 : 2)), m.addEventListener("click", () => {
			m.dispatchEvent(new CustomEvent("bf-action", {
				bubbles: !0,
				detail: {
					name: n,
					text: u,
					state: g
				}
			}));
		}), m;
	}
	async function m(t, n, i, s = {}) {
		let u = c(document.createElement("div"), 0, 0, t, n);
		u.className = "bf-window", u.dataset.theme = e, u.dataset.bfOpenSound = "popup", await f(u, e === "v3" ? a.scheme.Dialog.SkinImage_Title : "Images/Ui/DialogSkin-Basic.smf", t, n);
		let d = document.createElement("span");
		if (d.className = "caption", d.style.width = t - (e === "v1" ? 62 : 50) + "px", d.style.height = (e === "v2" ? 38 : 18) + "px", e === "v1" && (d.style.left = "37px", d.style.top = "11px"), e === "v2" && (d.style.left = "3px", d.style.top = "9px", d.style.width = t + "px"), e === "v3") {
			let e = a.scheme.Dialog;
			c(d, Number(e.Title_TextMargin_Left), Number(e.Title_TextMargin_Top), t - Number(e.Title_TextMargin_Left) - Number(e.Title_TextMargin_Right), Number(e.Title_Margin_Top) - Number(e.Title_TextMargin_Top) - Number(e.Title_TextMargin_Bottom));
		}
		u.append(d), await L(d, i, {
			size: Number(a.scheme.Dialog.Title_FontSize),
			family: "BF",
			align: e === "v1" ? "left" : "center",
			color: "#" + a.scheme.Dialog.Title_TextColor.slice(-6),
			decoration: Number(a.scheme.Dialog.Title_TextDecoration),
			outline: "#" + a.scheme.Dialog.Title_TextDecoColor.slice(-6),
			outlineWidth: s.renderer === "freetype" ? .5 : 2,
			nativeMetrics: e !== "v1",
			vertical: e === "v3" ? "middle" : "top",
			...s
		});
		let p = a.scheme.Dialog.CloseButton_SkinImage, m = a.sprites[p], h = m[0][2] - m[0][0] + 1, g = m[0][3] - m[0][1] + 1, _ = Number(a.scheme.Dialog.Title_Margin_Right) + (e === "v1" ? 5 : -3), v = c(document.createElement("button"), e === "v3" ? t - Number(a.scheme.Dialog.CloseButton_MarginRight_X) - h : t - _ - h, e === "v3" ? Number(a.scheme.Dialog.CloseButton_MarginRight_Y) : e === "v1" ? 11 : 8, h, g);
		v.className = "bf-close", v.ariaLabel = "关闭", v.dataset.bfPressSound = "cancel", await l(v, p, m[0], 0, 0, h, g);
		let y = v.querySelector("img"), b = (e) => {
			y.src = r() + p.replace(/\.smf$/i, "") + "/" + o(e + ".png");
		};
		return v.addEventListener("pointerenter", () => b(v.disabled ? 3 : 2)), v.addEventListener("pointerleave", () => b(v.disabled ? 3 : 0)), v.addEventListener("pointerdown", () => {
			v.disabled || b(1);
		}), v.addEventListener("pointerup", () => b(v.disabled ? 3 : 2)), v.addEventListener("pointercancel", () => b(v.disabled ? 3 : 0)), v.onclick = () => u.dispatchEvent(new CustomEvent("bf-action", {
			bubbles: !0,
			detail: { name: "close" }
		})), u.append(v), u;
	}
	function h(e, t, n) {
		let r = document.createElement("canvas").getContext("2d");
		if (!r) throw Error("Unable to measure message text.");
		r.font = `400 ${t}px ${n}`;
		let i = [];
		for (let t of e.split("\n")) {
			let e = "";
			for (let n of t) e += n;
			i.push(e);
		}
		return {
			width: Math.ceil(Math.max(...i.map((e) => r.measureText(e || " ").width), 0)),
			height: Math.ceil(i.length * (t + 1))
		};
	}
	function g({ contentWidth: e, contentHeight: t, textWidth: n, textHeight: r, buttonWidth: i, buttonHeight: a, buttonCount: o, buttonSpan: s = i }) {
		let c = Math.max(200, n), l = Math.max(70, r + 16, 58), u = Math.max(e, c + 104, s + 20);
		return {
			width: u,
			height: Math.max(t, l + 9 + a),
			bodyHeight: l,
			buttonY: l + 9,
			buttonX: o === 1 ? Math.floor((u - i) / 2) : void 0
		};
	}
	async function _({ title: n, text: i, iconKind: s = "notice", buttons: l, body: u, contentWidth: d = 350, contentHeight: _, contentX: v = 10, contentY: y = 38, outerWidth: b, outerHeight: x, buttonY: C, textScale: w = 1 }) {
		let T = {
			renderer: "freetype",
			resourceRoot: t,
			pixelRatio: w
		};
		if (e === "v3") return S(arguments[0]);
		let E = e === "v2", D = d, O = _ ?? (E ? 104 : 105), k = typeof u == "function", A = o(`Images/Ui/MsgBoxIcon${{
			notice: "Notice",
			warning: "Warning",
			question: "Question",
			error: "Error"
		}[s] || "Notice"}.png`), j = E ? 5 : 8, M = l.map((e) => ({
			...e,
			width: e.width ?? (E ? 64 : 68),
			height: e.height ?? (E ? 26 : 25)
		}));
		if (!M.length && !k) throw Error("Message boxes require at least one button.");
		let N = M.reduce((e, t) => e + t.width, 0) + j * (M.length - 1);
		await document.fonts.load("400 13px BF");
		let P = k ? {
			width: 0,
			height: 0
		} : h(i, 13, "BF"), F = k ? {
			width: d,
			height: O,
			bodyHeight: O,
			buttonY: C ?? O + 9
		} : g({
			contentWidth: D,
			contentHeight: O,
			textWidth: P.width,
			textHeight: P.height,
			buttonWidth: M[0].width,
			buttonHeight: M[0].height,
			buttonCount: M.length,
			buttonSpan: N
		}), I = F.buttonY + Math.max(0, ...M.map((e) => e.height));
		F.height = Math.max(F.height, I);
		let R = Number(a.scheme.Dialog.Title_Margin_Right), z = Number(a.scheme.Dialog.Title_Margin_Bottom), B = await m(Math.max(b ?? 0, v + F.width + R), Math.max(x ?? 0, y + F.height + z), n, T), V = c(document.createElement("div"), v, y, F.width, F.height);
		if (V.className = "bf-form", B.append(V), k) await u(V, F);
		else {
			let e = E ? F.width - 1 : F.width, t = c(document.createElement("div"), +!!E, 2, e, F.bodyHeight);
			t.className = "control", await f(t, "Images/Ui/FrameBox.smf", e, F.bodyHeight), V.append(t);
			let n = Object.assign(new Image(), { crossOrigin: "anonymous" });
			n.src = r() + A, n.className = "control", await n.decode().catch(() => {});
			let a = n.naturalWidth || 63, o = n.naturalHeight || 58;
			c(n, 11, Math.round(2 + (F.bodyHeight - o) / 2), a, o), V.append(n);
			let s = Math.max(40, P.height), l = c(document.createElement("div"), 90, Math.round(2 + (F.bodyHeight - s) / 2), F.width - 104, s);
			l.className = "message-copy", V.append(l), await L(l, i, {
				size: 13,
				weight: 400,
				family: "BF",
				align: "center",
				color: "#444",
				height: s,
				width: F.width - 104,
				...T
			});
		}
		let H = !E && M.length > 1 ? F.width - N : Math.floor((F.width - N) / 2);
		for (let e of M) {
			let t = F.width - D, n = e.x === void 0 ? H : e.x + (E ? t / 2 : t), r = e.sprite || (E ? `Images/Ui/DialogBtn${e.name}.smf` : `Images/Ui/DialogButton${e.name}-Up.smf`);
			await p(V, e.name, n, F.buttonY, e.width, e.height, e.label, r), H += e.width + j;
		}
		return B;
	}
	async function v(t, n = typeof t == "boolean" && t ? "question" : "notice") {
		let r = e !== "v1", i = typeof t == "boolean" ? t : !!t?.confirm, a = typeof t == "object" && t ? t : {}, o = e === "v3" ? i ? [{
			name: "Ok",
			label: "确认"
		}, {
			name: "Cancel",
			label: "取消"
		}] : [{
			name: "Ok",
			label: "确认"
		}] : i ? [{
			name: "Ok",
			label: "确认",
			x: r ? 113 : void 0,
			sprite: r ? "Images/Ui/DialogBtnOk.smf" : "Images/Ui/DialogButtonOk-Up.smf"
		}, {
			name: "Cancel",
			label: "取消",
			x: r ? 182 : void 0,
			sprite: r ? "Images/Ui/DialogBtnCancel.smf" : "Images/Ui/DialogButtonCancel-Up.smf"
		}] : [{
			name: "Ok",
			label: "确认"
		}], s = await _({
			title: a.title || (i ? "确认" : "公告"),
			text: a.text || (i ? r ? "是否中断新手教程?" : "是否停止观看教学视频?" : r ? "自动开始新手教程。\n完成新手教程可以获得丰富奖励，按ESC键可以退出。\n根据电脑配置不同可能会延长加载时间。" : "与服务器连接中断。"),
			iconKind: a.icon || n,
			buttons: a.buttons ?? o,
			body: a.body,
			contentWidth: a.contentWidth,
			contentHeight: a.contentHeight,
			contentX: a.contentX,
			contentY: a.contentY,
			outerWidth: a.outerWidth,
			outerHeight: a.outerHeight,
			buttonY: a.buttonY,
			textScale: a.textScale
		});
		return a.openSound === !1 ? delete s.dataset.bfOpenSound : s.dataset.bfOpenSound = a.openSound ?? "popup", s;
	}
	let y = {
		gbGraphicOption: "画面设置",
		gbSoundOption: "声音设置",
		lblBgm: "背景音",
		lblEffect: "效果音",
		lblMSG1: "与服务器连接中断。",
		lblText: "与服务器连接中断。",
		lblResolution: "1366 x 768"
	};
	async function b(t, n, i = {}) {
		if (e === "v3") return x(t, n, i);
		let o = {
			...t.attrs,
			...i[t.attrs.Name]
		}, u = s(o, "PosX"), d = s(o, "PosY"), m = s(o, "Width"), h = s(o, "Height"), g = document.createElement("div");
		g.className = "control", c(g, u, d, m, h), n.append(g);
		let _ = o.SkinSpriteSet || o.SpriteSetBackground, v = i[o.Name]?.Text || y[o.Name] || o.Text || "";
		switch (t.type) {
			case "UiPanel":
			case "UiForm":
				if (_) await f(g, _, m, h);
				else if (o.SkinImage) {
					let e = document.createElement("img");
					e.src = r() + o.SkinImage, e.style.cssText = "width:100%;height:100%", g.append(e);
				}
				break;
			case "UiGroupBox":
				_ && await f(g, _, m, h);
				{
					let e = c(document.createElement("span"), 26, 4, m - 26, 22);
					e.textContent = v, e.style.color = "white", e.className = "control bf-label", g.append(e), await L(e, v, {
						color: "white",
						size: Number(o.FontSize) || Number(a.scheme.GroupBox.FontSize),
						family: "BF",
						nativeMetrics: !0,
						vertical: "top",
						inset: 0
					});
				}
				break;
			case "UiLabel":
				g.classList.add("bf-label"), g.textContent = v, g.style.fontSize = `${s(o, "FontSize", 13)}px`, g.style.fontFamily = [
					"Default",
					"DefaultBold",
					"NexonBTBold"
				].includes(o.FontName) ? "BF" : "BFMalgun", o.TextColor && (g.style.color = "#" + o.TextColor.slice(-6)), s(o, "TextFormat") === 5 && (g.style.justifyContent = "center"), await L(g, v, {
					size: Number(o.FontSize) || 13,
					color: o.TextColor ? "#" + o.TextColor.slice(-6) : "#5b5b5b",
					family: g.style.fontFamily || "BFMalgun",
					align: s(o, "TextFormat") === 5 ? "center" : "left",
					outline: i[o.Name]?.OutlineColor,
					nativeMetrics: !0,
					vertical: s(o, "TextFormat") & 4 ? "middle" : "top",
					inset: 0
				});
				break;
			case "UiButton":
				g.remove(), await p(n, /cancel/i.test(o.Name) ? "Cancel" : "Ok", u, d, m, h, v || (/cancel/i.test(o.Name) ? "取消" : "确认"), o.SpriteSet || o.SpriteSetUp);
				return;
			case "UiCheckBox": {
				g.classList.add("bf-check");
				let e = document.createElement("input");
				e.type = "checkbox", e.checked = o.Check === "1", e.ariaLabel = o.Name || v;
				let t = async () => {
					g.querySelectorAll(".tile").forEach((e) => e.remove());
					let t = o.SpriteSet || "Images/Ui/UiCheckBoxSkin.smf";
					await l(g, t, a.sprites[t][+!!e.checked], 0, 0, 21, 18);
				};
				g.setValue = (n) => {
					e.checked = !!n, t();
				}, g.append(e), e.onchange = () => {
					t(), g.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: o.Name,
							value: e.checked
						}
					}));
				}, await t();
				break;
			}
			case "UiTrackBar": {
				g.classList.add("bf-track");
				let e = document.createElement("input");
				e.type = "range", e.min = o.Min ?? 0, e.max = o.Max ?? 100, e.value = o.Position ?? 75, e.ariaLabel = o.Name;
				let t = "Images/Ui/TrackBar.smf", n = a.sprites[t], r = h - 35;
				await l(g, t, n[0], 15, r, 7, 31), await l(g, t, n[1], 22, r, m - 44, 31), await l(g, t, n[2], m - 22, r, 7, 31);
				let i = ae(m, h, Number(e.min), Number(e.max), Number(o.Interval) || 1);
				for (let e of i.ticks) await l(g, t, n[7], e, r, 1, 31);
				let s = c(document.createElement("span"), i.knobX(Number(e.value)), r, 12, 31);
				s.className = "control", await l(s, t, n[3], 0, 0, 12, 31), g.append(s, e), o.MinText && M(g, o.MinText === "작게" ? "缩小" : o.MinText, 0, h - 50, 50, 15, "#808080", {
					size: Number(o.FontSize) || Number(a.scheme.TrackBar.FontSize),
					family: "BF",
					align: "center"
				}), o.MaxText && M(g, o.MaxText === "크게" ? "放大" : o.MaxText, m - 50, h - 52, 50, 15, "#808080", {
					size: Number(o.FontSize) || Number(a.scheme.TrackBar.FontSize),
					family: "BF",
					align: "center"
				}), g.setValue = (t) => {
					e.value = String(t), s.style.left = i.knobX(Number(e.value)) + "px";
				}, e.oninput = () => {
					s.style.left = i.knobX(Number(e.value)) + "px", g.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: o.Name,
							value: Number(e.value)
						}
					}));
				};
				break;
			}
			case "UiEdit":
			case "UiEditBox": {
				let e = document.createElement("input");
				e.className = "control bf-edit", c(e, u, d, m, h), e.value = v, e.ariaLabel = o.Name, g.replaceWith(e), g = e;
				break;
			}
			default: throw Error(`Unsupported control: ${t.type} (${o.Name || "unnamed"})`);
		}
		for (let e of t.children) await b(e, g, i);
		return g;
	}
	async function x(e, t, n = {}) {
		let i = {
			...e.attrs,
			...n[e.attrs.Name]
		}, o = s(i, "PosX"), u = s(i, "PosY"), d = s(i, "Width"), m = s(i, "Height");
		if (i.Visible === "False" || i.Visible === "0" || e.type === "UiMarker") return;
		let h = c(document.createElement("div"), o, u, d, m);
		h.className = "control", h.dataset.control = i.Name || "", t.append(h);
		let g = i.Text || "", _ = i.SkinSpriteSet || i.SpriteSetBackground, v = {
			family: i.FontName === "Malgun" ? "BFMalgun" : "BF",
			size: Number(i.FontSize) || Number(a.scheme.UiLabel.FontSize),
			weight: 400,
			color: i.TextColor ? "#" + i.TextColor.slice(-6) : "#bbdde0",
			decoration: Number(i.TextDecoration) || 1,
			nativeMetrics: !0,
			inset: 0
		};
		switch (e.type) {
			case "UiPanel":
			case "UiForm":
			case "UiGroupBox":
				if (_) await f(h, _, d, m);
				else if (i.SkinImage) {
					let e = Object.assign(new Image(), { crossOrigin: "anonymous" });
					e.src = r() + i.SkinImage, await e.decode(), c(e, 0, 0, e.naturalWidth, e.naturalHeight), h.append(e);
				}
				e.type === "UiGroupBox" && g && M(h, g, 9, 5, d - 18, 20, "#29d4ff", {
					size: 14,
					family: "BF"
				});
				break;
			case "UiLabel": {
				let e = s(i, "TextFormat");
				h.classList.add("bf-label"), await L(h, g, {
					...v,
					align: e & 1 ? "center" : e & 2 ? "right" : "left",
					vertical: e & 4 ? "middle" : e & 8 ? "bottom" : "top",
					wrap: !!(e & 16)
				});
				break;
			}
			case "UiButton": return h.remove(), p(t, /cancel/i.test(i.Name) ? "Cancel" : /okay|^btnok$/i.test(i.Name) ? "Ok" : i.Name, o, u, d, m, g, i.SpriteSet || i.SpriteSetUp, {
				...i,
				FontSize: Number(i.FontSize) ? i.FontSize : a.scheme.UiButton.FontSize
			});
			case "UiCheckBox": {
				h.classList.add("bf-check");
				let e = document.createElement("input");
				e.type = "checkbox", e.checked = i.Check === "1", e.ariaLabel = g || i.Name, h.append(e);
				let t = i.SpriteSet || a.scheme.UiCheckBox.SkinImage, n = a.sprites[t], r = c(document.createElement("span"), 0, 0, 21, 18);
				r.className = "control", h.append(r);
				let o = async () => {
					r.replaceChildren(), await l(r, t, n[+!!e.checked], 0, 0, 21, 18);
				};
				if (h.setValue = (t) => {
					e.checked = !!t, o();
				}, e.onchange = () => {
					o(), h.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: i.Name,
							value: e.checked
						}
					}));
				}, await o(), g) {
					let e = c(document.createElement("span"), 30, 0, Math.max(1, d - 30), m);
					e.className = "control", h.append(e), await L(e, g, {
						...v,
						vertical: "middle"
					});
				}
				break;
			}
			case "UiTrackBar": {
				h.classList.add("bf-track");
				let e = document.createElement("input");
				e.type = "range", e.min = i.Min ?? 0, e.max = i.Max ?? 100, e.value = i.Position ?? 50, e.ariaLabel = i.Name, h.append(e);
				let t = i.HasText !== "False", n = oe(d, Number(e.min), Number(e.max), Number(i.Interval) || 1, t ? 15 : 0), r = a.scheme.TrackBar.SkinImage, o = a.sprites[r], s = n.railY;
				if (await l(h, r, o[0], 15, s, 4, 18), await l(h, r, o[1], 19, s, d - 38, 18), await l(h, r, o[2], d - 19, s, 4, 18), i.HasPoint !== "False") for (let e of n.ticks) await l(h, r, o[7], e, s, 1, 18);
				let u = c(document.createElement("span"), n.knobX(Number(e.value)), s, 9, 18);
				u.className = "control", await l(u, r, o[3], 0, 0, 9, 18), h.append(u);
				let f = () => {
					u.style.left = n.knobX(Number(e.value)) + "px";
				};
				if (h.setValue = (t) => {
					e.value = String(t), f();
				}, e.oninput = () => {
					f(), h.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: i.Name,
							value: Number(e.value)
						}
					}));
				}, t) {
					let e = (e) => a.translations[e] || {
						작게: "小",
						크게: "大"
					}[e] || e;
					M(h, e(i.MinText || ""), 0, 0, 50, 15, v.color, {
						size: v.size,
						family: v.family,
						align: "center"
					}), M(h, e(i.MaxText || ""), d - 50, 0, 50, 15, v.color, {
						size: v.size,
						family: v.family,
						align: "center"
					});
				}
				break;
			}
			case "UiEdit":
			case "UiEditBox": {
				let e = document.createElement("input");
				return e.className = "control bf-edit", e.ariaLabel = i.Name, e.value = g, c(e, o, u, d, m), h.replaceWith(e), e;
			}
			default: throw Error(`Unsupported cn-774 control: ${e.type} (${i.Name || "unnamed"})`);
		}
		for (let t of e.children) await b(t, h, n);
		return h;
	}
	async function S(e) {
		let { title: t, text: n = "", iconKind: i = "notice", buttons: o = [], body: s } = e, l = typeof s == "function", u = a.forms["DlgMessageBox.ui"], d = (e) => u.children.find((t) => t.attrs.Name === e).attrs, f = d("pnlFrameBox"), h = d("lblText"), g = d("btnOk"), _ = e.contentWidth ?? Number(u.attrs.Width), v = o.map((e) => ({
			...e,
			width: e.width ?? Number(g.Width),
			height: e.height ?? Number(g.Height)
		}));
		if (!v.length && !l) throw Error("Message boxes require at least one button.");
		await document.fonts.load("400 14px BF");
		let y = document.createElement("canvas").getContext("2d");
		y.font = "400 14px BF";
		let b = _ - Number(h.PosX) - (Number(u.attrs.Width) - Number(h.PosX) - Number(h.Width)), x = [];
		for (let e of n.split("\n")) {
			let t = "";
			for (let n of e) t && y.measureText(t + n).width > b && (x.push(t), t = ""), t += n;
			x.push(t);
		}
		let S = l ? e.contentHeight ?? 214 : Math.max(Number(f.Height), x.length * 15), C = e.buttonY ?? S + (Number(g.PosY) - Number(f.Height)), w = l ? e.contentHeight ?? 214 : C + Number(g.Height) + 1, T = e.contentX ?? Number(a.scheme.Dialog.Title_Margin_Left), D = e.contentY ?? Number(a.scheme.Dialog.Title_Margin_Top), O = C + Math.max(0, ...v.map((e) => e.height)), k = await m(e.outerWidth ?? _ + 20, Math.max(e.outerHeight ?? 0, w + D + 12, O + D + 12), t), A = c(document.createElement("div"), T, D, _, Math.max(w, O));
		if (A.className = "bf-form", k.append(A), l) await s(A, {
			width: _,
			height: w,
			bodyHeight: S,
			buttonY: C
		});
		else {
			await E(A, Number(f.PosX), Number(f.PosY), _ - 6, S, f.SkinSpriteSet);
			let e = {
				notice: 0,
				warning: 1,
				question: 2,
				error: 3
			}, t = u.children.find((t) => t.attrs.Name === "pnlIcon" && Number(t.attrs.ArrayIndex) === (e[i] ?? 0)).attrs, n = Object.assign(new Image(), { crossOrigin: "anonymous" });
			n.src = r() + t.SkinImage, await n.decode(), n.className = "control", c(n, Number(t.PosX), Math.floor((S - n.naturalHeight) / 2), n.naturalWidth, n.naturalHeight), A.append(n);
			let a = c(document.createElement("div"), Number(h.PosX), 0, b, S);
			a.className = "message-copy", A.append(a), await L(a, x.join("\n"), {
				family: "BF",
				size: 14,
				weight: 400,
				color: "#e4f3f4",
				align: "center",
				height: S,
				width: b,
				decoration: 1,
				lineHeight: 15
			});
		}
		let j = v.reduce((e, t) => e + t.width, 0) + Math.max(0, v.length - 1) * 4, M = Math.floor((_ - j) / 2);
		for (let e of v) await p(A, e.name, e.x ?? M, C, e.width, e.height, e.label, e.sprite), M += e.width + 4;
		return k;
	}
	async function C(e, t) {
		let n = a.forms[e];
		if (!n) throw Error(`Missing cn-774 layout: ${e}`);
		let r = a.scheme.Dialog, i = Number(r.Title_Margin_Left), o = Number(r.Title_Margin_Top), s = await m(Number(n.attrs.Width) + i + Number(r.Title_Margin_Right), Number(n.attrs.Height) + o + Number(r.Title_Margin_Bottom), t || n.attrs.Text), c = structuredClone(n);
		return Object.assign(c.attrs, {
			PosX: String(i),
			PosY: String(o)
		}), await b(c, s), s;
	}
	async function w() {
		let e = a.forms["Option/DlgOption4.ui"], t = a.scheme.Dialog, n = Number(t.Title_Margin_Left), r = Number(t.Title_Margin_Top), i = await m(Number(e.attrs.Width) + n + Number(t.Title_Margin_Right), Number(e.attrs.Height) + r + Number(t.Title_Margin_Bottom), e.attrs.Text), o = c(document.createElement("div"), n, r, Number(e.attrs.Width), Number(e.attrs.Height));
		o.className = "bf-form", i.append(o);
		let s = e.children.find((e) => e.attrs.Name === "pnlBackPlane"), l = [], u = 1, d = 0, p = c(document.createElement("div"), Number(s.attrs.PosX), Number(s.attrs.PosY), Number(s.attrs.Width), Number(s.attrs.Height));
		p.className = "control", o.append(p);
		async function h(e) {
			let t = ++d;
			u = e;
			for (let t = 0; t < l.length; t++) l[t].ariaSelected = String(t === e), await l[t].setSelected(t === e);
			let n = document.createElement("div");
			c(n, 0, 0, Number(s.attrs.Width), Number(s.attrs.Height)), n.className = "control", await f(n, s.attrs.SkinSpriteSet, Number(s.attrs.Width), Number(s.attrs.Height));
			let r = structuredClone(a.forms[e ? "Option/DlgOption-VideoSound3.ui" : "Option/DlgOption-Game5.ui"]);
			Object.assign(r.attrs, {
				PosX: "0",
				PosY: "0"
			});
			let i = (e) => {
				Number(e.attrs.PosY) >= 1e3 && (e.attrs.Visible = "False"), e.attrs.Name === "chkWindowMode" && (e.attrs.Check = "1"), ["tbBGMVolume", "tbEffectVolume"].includes(e.attrs.Name) && (e.attrs.Position = "75"), e.children.forEach(i);
			};
			i(r), await b(r, n), t === d && p.replaceChildren(n);
		}
		for (let t of ["btnTabMouseSense", "btnTabDisplaySound"]) {
			let n = await b(e.children.find((e) => e.attrs.Name === t), o);
			n.setAttribute("role", "tab");
			let r = l.length;
			n.onclick = () => {
				h(r);
			}, l.push(n);
		}
		for (let t of ["btnOkay", "btnCancel"]) await b(e.children.find((e) => e.attrs.Name === t), o);
		return await h(u), i;
	}
	async function T() {
		if (e === "v3") return w();
		let t = e === "v2", n = a.forms[t ? "DlgOption2.ui" : "DlgOption.ui"], r = Number(a.scheme.Dialog.Title_Margin_Left), i = Number(a.scheme.Dialog.Title_Margin_Top), o = Number(n.attrs.Width) + r + Number(a.scheme.Dialog.Title_Margin_Right), s = Number(n.attrs.Height) + i + Number(a.scheme.Dialog.Title_Margin_Bottom), u = (e) => n.children.find((t) => t.attrs.Name === e).attrs, d = u("pnlBackPlane"), h = Number(d.Height), g = await m(o, s, "选项"), _ = c(document.createElement("div"), r + Number(d.PosX), i + Number(d.PosY), Number(d.Width), h);
		_.className = "control", g.append(_);
		let v = structuredClone(a.forms[t ? "DlgOption-VideoSound2.ui" : "DlgOption-VideoSound.ui"]);
		v.attrs = {
			...v.attrs,
			PosX: "0",
			PosY: "0"
		}, v.children = v.children.filter((e) => !["chkResolutionRatio", "lblResolutionRatio"].includes(e.attrs.Name));
		for (let e of v.children) ["chkWindowMode", "chkLowQuality"].includes(e.attrs.Name) && (e.attrs.Check = "1"), e.attrs.Name === "chkBgm" && (e.attrs.Check = "0"), e.type === "UiTrackBar" && (e.attrs.Position = "75"), e.type === "UiLabel" && e.attrs.Text.includes("低清晰度模式") && Object.assign(e.attrs, {
			Text: "( 低清晰度模式重启游戏后生效 )",
			Width: "250",
			FontSize: "12"
		}), e.type === "UiLabel" && e.attrs.Text === "창모드로 실행" && (e.attrs.Text = t ? "窗口模式" : "窗口模式运行"), e.type === "UiLabel" && e.attrs.Text === "음소거" && (e.attrs.Text = "静音");
		let y = [], x = 1;
		async function S(e) {
			x = e;
			for (let t = 0; t < y.length; t++) {
				y[t].replaceChildren(), y[t].ariaSelected = String(t === e);
				let n = `Images/TaskBar/Option/OptionTab0${t + 1}.smf`;
				await l(y[t], n, a.sprites[n][t === e ? 3 : 0], 0, 0, t ? 106 : 73, 27);
			}
			if (_.replaceChildren(), await f(_, "Images/Ui/DialogInnerSkin03.smf", 295, h), e === 1) await b(v, _);
			else {
				let e = structuredClone(a.forms["DlgOption-Game.ui"]);
				Object.assign(e.attrs, {
					PosX: "0",
					PosY: "0"
				}), e.children = e.children.filter((e) => e.attrs.Name !== "chkErp"), await b(e, _, { gbMouseSense: { Text: "鼠标灵敏度调节" } });
			}
		}
		for (let e = 0; e < 2; e++) {
			let t = u(e ? "btnTabDisplaySound" : "btnTabMouseSense"), n = c(document.createElement("button"), r + Number(t.PosX), i + Number(t.PosY), Number(t.Width), Number(t.Height));
			n.className = "control sprite-button", n.ariaLabel = e ? "画面/声音设置" : "游戏设置", n.onclick = () => S(e), y.push(n), g.append(n);
		}
		await S(x);
		for (let [e, t] of [["btnOkay", "确认"], ["btnCancel", "取消"]]) {
			let n = u(e);
			await p(g, e === "btnOkay" ? "Ok" : "Cancel", r + Number(n.PosX), i + Number(n.PosY), Number(n.Width), Number(n.Height), t);
		}
		return g;
	}
	async function E(t, n, r, i, a, o = "Images/Ui/FrameBox.smf") {
		e === "v3" && (o = {
			"Images/Ui/FrameBox.smf": "Images/Ui/dialogueNew/dialogue_basic02.smf",
			"Images/Ui/DialogInnerSkin03.smf": "Images/Ui/dialogueNew/dialogue_basic02.smf",
			"Images/Ui/DialogInnerSkin06.smf": "Images/Ui/dialogueNew/dialogue_basic03.smf"
		}[o] || o);
		let s = c(document.createElement("div"), n, r, i, a);
		return s.className = "control", await f(s, o, i, a), t.append(s), s;
	}
	let D = {
		v1: {
			color: "#5b5b5b",
			muted: "#808080",
			font: "BFMalgun",
			size: 13,
			tab: "#ffffff",
			inactive: "#687075",
			panel: "Images/Ui/DialogInnerSkin03.smf",
			group: "Images/Ui/DialogSkin-Basic04.smf",
			groupText: "#ffffff",
			groupX: 26,
			groupY: 4,
			groupSize: 13,
			groupHeight: 18,
			groupAlign: "top"
		},
		v2: {
			color: "#5b5b5b",
			muted: "#808080",
			font: "BFMalgun",
			size: 13,
			tab: "#ffffff",
			inactive: "#849ba6",
			panel: "Images/Ui/DialogInnerSkin03.smf",
			group: "Images/Ui/DialogSkin-Basic04.smf",
			groupText: "#ffffff",
			groupX: 26,
			groupY: 4,
			groupSize: 13,
			groupHeight: 18,
			groupAlign: "top"
		},
		v3: {
			color: "#bbdde0",
			muted: "#89b4ca",
			font: "BF",
			size: 13,
			tab: "#ffffff",
			inactive: "#e0eff4",
			panel: "Images/Ui/dialogueNew/dialogue_basic02.smf",
			group: "Images/Ui/dialogueNew/dialogue_basic03.smf",
			groupText: "#29d4ff",
			groupX: 9,
			groupY: 5,
			groupSize: 14,
			groupHeight: 20,
			groupAlign: "middle"
		}
	}[e];
	async function O(e, t, n, r, i, a, o = !1) {
		let s = c(document.createElement("span"), n, r, i, a);
		return s.className = "control", s.textContent = t, e.append(s), await L(s, t, {
			family: D.font,
			size: o ? 12 : D.size,
			weight: 400,
			color: o ? D.muted : D.color,
			nativeMetrics: !0,
			vertical: "middle",
			inset: 0
		}), s;
	}
	async function k(e, t, n, r, i) {
		return E(e, t, n, r, i, D.panel);
	}
	async function A(e, t, n, r, i, a) {
		let o = await E(e, n, r, i, a, D.group);
		o.dataset.bfSettingsGroup = "";
		let s = c(document.createElement("span"), D.groupX, D.groupY, i - D.groupX - 6, D.groupHeight);
		return s.className = "control", o.append(s), await L(s, t, {
			family: "BF",
			size: D.groupSize,
			weight: 400,
			color: D.groupText,
			nativeMetrics: !0,
			vertical: D.groupAlign,
			inset: 0
		}), o;
	}
	async function j(t, n, r, i, a, o) {
		let s = c(document.createElement("button"), r, i, a, o);
		s.className = "control sprite-button bf-text-tab", s.dataset.theme = e, s.ariaLabel = n, s.setAttribute("role", "tab"), t.append(s);
		let l = c(document.createElement("span"), 0, 0, a, o), u = c(document.createElement("span"), 0, 0, a, o);
		return l.className = "bf-tab-active", u.className = "bf-tab-inactive", s.append(l, u), await Promise.all([[l, D.tab], [u, D.inactive]].map(([e, t]) => L(e, n, {
			family: D.font,
			size: 13,
			weight: 400,
			color: t,
			nativeMetrics: !0,
			vertical: "middle",
			align: "center",
			inset: 0
		}))), s.setActive = (e) => {
			s.ariaSelected = String(e), s.tabIndex = e ? 0 : -1;
		}, s.setActive(!1), s;
	}
	function M(t, n, r, i, a, o, s = e === "v3" ? "#bbdde0" : "#5b5b5b", l = {}) {
		let u = c(document.createElement("span"), r, i, a, o);
		return u.className = "control bf-label", u.textContent = n, u.style.color = s, t.append(u), L(u, n, {
			color: s,
			nativeMetrics: !0,
			vertical: "middle",
			wrap: n.length > 40,
			...l
		}), u;
	}
	async function N(t, n, r, i, a = !1) {
		await b({
			type: "UiCheckBox",
			attrs: {
				Name: n,
				PosX: String(r),
				PosY: String(i),
				Width: "21",
				Height: "18",
				Check: a ? "1" : "0"
			},
			children: []
		}, t), M(t, n, r + 30, i + 1, 100, 20, e === "v3" ? "#bbdde0" : "#5b5b5b", {
			size: 11,
			family: "BF"
		});
	}
	async function P() {
		if (e === "v3") return C("Stages/Lobby/DlgCreateRoom.ui", "创建房间");
		let t = e === "v2", n = t ? 694 : 460, r = await m(450, n, "创建房间");
		await E(r, 9, 36, 431, 63), M(r, "房间名称", 24, 43, 60, 23), M(r, t ? "非公开" : "密码", 39, 72, 50, 20), await N(r, "", 15, 75);
		for (let [e, n, i, a] of [[
			89,
			42,
			343,
			t ? "今天看我的！" : "逃跑达人宝宝"
		], [
			89,
			70,
			110,
			""
		]]) {
			let t = c(document.createElement("input"), e, n, i, 23);
			t.className = "control bf-edit", t.value = a, t.ariaLabel = n === 42 ? "房间名称" : "密码", r.append(t);
		}
		M(r, t ? "输入6位以内的字符" : "最多6个字符", 204, 72, 180, 20, "#258cea"), await E(r, 9, 105, 431, n - 149);
		let i = a.forms["DlgCreateRoom.ui"].children.filter((e) => e.type === "UiButton" && e.attrs.Name === "btnGameMode"), o = [], s = t ? [
			"13_Beast",
			"12_Demolition",
			"01_Noitem",
			"02_Item",
			"07_Sniper",
			"06_Npc",
			"05_Zombie",
			"09_Zombie_Pve",
			"08_Surviaval",
			"08_Survival_Team",
			"11_Survival_Sniper"
		] : [
			"01_Noitem",
			"02_Item",
			"03_Starsingle",
			"04_Starteam",
			"05_Zombie"
		];
		for (let e = 0; e < s.length; e++) {
			let n = i.find((t) => t.attrs.SpriteSet.includes(s[e]));
			if (!n) continue;
			let a = await p(r, "Mode", 16, 113 + e * 49, 190, 51, s[e], n.attrs.SpriteSet);
			o.push(a), await a.setSelected(e === (t ? 2 : 0)), a.onclick = async () => {
				for (let e of o) await e.setSelected(e === a);
			};
		}
		if (await E(r, t ? 207 : 205, t ? 326 : 145, 228, t ? 321 : 262, "Images/Ui/DialogInnerSkin06.smf"), t) {
			await E(r, 207, 111, 228, 114, "Images/Ui/DialogInnerSkin06.smf"), M(r, "游戏选项", 214, 116, 100, 20, "#2a7ed4");
			for (let [e, t, n] of [
				[
					"锤子战",
					214,
					139
				],
				[
					"回合战",
					214,
					160
				],
				[
					"极速击爆",
					214,
					181
				],
				[
					"地图选项",
					214,
					202
				],
				[
					"组织比赛",
					327,
					139
				]
			]) await N(r, e, t, n);
			await E(r, 207, 229, 228, 94, "Images/Ui/DialogInnerSkin06.smf"), M(r, "房间选项", 214, 234, 100, 20, "#2a7ed4"), await N(r, "随机组", 214, 257), await N(r, "闯入", 214, 278, !0), M(r, "模式说明", 214, 331, 100, 20, "#2a7ed4");
		}
		let l = M(r, "只使用泡泡枪的基本规则：“泡封”、“击爆”、“营救”来展现自身实力的游戏模式。要精确击中对方和及时解救我方，才能获取胜利哦。", t ? 211 : 208, t ? 351 : 151, t ? 217 : 218, 100, "#5b5b5b", {
			vertical: "top",
			size: 12
		});
		return l.style.alignItems = "flex-start", l.style.lineHeight = "15px", await p(r, "Ok", t ? 157 : 298, n - 35, t ? 65 : 67, t ? 27 : 25, "确认"), await p(r, "Cancel", t ? 226 : 373, n - 35, t ? 65 : 68, t ? 27 : 25, "取消"), r;
	}
	async function F(e) {
		return le(R, e);
	}
	async function I({ text: t } = {}) {
		let n = e !== "v1", i = typeof t == "string" && t ? t : n ? "9不在线" : "9不在线。", s = a.scheme.NotifyBox, l = n ? 14 : Number(s.FontSize), u = n ? 300 : 320, d = n ? 60 : 82, p = Math.min(640, Math.max(u, Math.ceil(h(i, l, "BF").width) + 48)), m = c(document.createElement("div"), 0, 0, p, d);
		m.className = "bf-form bf-notify-box", await f(m, n ? "Images/Ui/dialogue_Notify_Back00.smf" : "Images/Ui/NotifyBox.smf", p, d);
		let g = c(document.createElement("span"), 18, 8, p - 36, d - 16);
		if (g.className = "control", m.append(g), await L(g, i, {
			size: l,
			weight: n ? 400 : Number(s.FontWeight),
			family: "BF",
			align: "center",
			wrap: !0,
			color: n ? "white" : "#111",
			outline: n ? "#168ac2" : "#888",
			outlineWidth: n ? 2 : .75,
			decoration: n ? 4 : Number(s.TextDecoration)
		}), n) {
			let e = "Images/Ui/Notify_Progress.smf", t = a.sprites[e], n = 0, i = async () => {
				m.querySelectorAll(".notify-progress").forEach((e) => e.remove());
				let t = c(document.createElement("span"), p - 28, d - 28, 20, 20);
				t.className = "tile notify-progress";
				let i = Object.assign(new Image(), { crossOrigin: "anonymous" });
				i.src = r() + e.replace(/\.smf$/i, "") + "/" + o(n + ".png"), Object.assign(i.style, {
					width: "100%",
					height: "100%",
					left: "0",
					top: "0"
				}), t.append(i), m.append(t);
			};
			await i();
			let s = window.setInterval(() => {
				!m.isConnected && m.dataset.mounted ? window.clearInterval(s) : (m.isConnected && (m.dataset.mounted = "1"), n = (n + 1) % t.length, i());
			}, 180);
		}
		return m;
	}
	let R = {
		theme: e,
		manifest: a,
		sounds: n,
		trackBar: (e, t) => b({
			type: "UiTrackBar",
			attrs: t,
			children: []
		}, e),
		groupBox: (e, t) => b({
			type: "UiGroupBox",
			attrs: t,
			children: []
		}, e),
		windowShell: m,
		renderNode: b,
		button: p,
		panel: E,
		label: M,
		checkbox: N,
		dialog: v,
		settings: T,
		room: P,
		message: I,
		customSettings: F,
		settingsLabel: O,
		settingsPanel: k,
		settingsGroup: A,
		textTab: j
	};
	return {
		...R,
		image: o,
		extensionSettings: (e) => le(R, e)
	};
}
// ../tools/game-ui/src/context.ts
var de = Symbol("bf-ui"), q = Symbol("bf-ui-draws");
function fe() {
	return l(q, void 0);
}
function pe(e, t, n = () => !0, r = () => N) {
	let i = S(), a = S(), o = 0, s, c;
	D(() => [e(), t()], async ([e, t]) => {
		let l = ++o;
		a.value = void 0;
		try {
			c !== t && (s?.dispose(), c = t, s = F({
				resourceRoot: t,
				enabled: n(),
				volume: r(),
				onError: (e) => {
					a.value = e;
				}
			}));
			let u = s;
			if (await Promise.all([he(t), n() ? u.preload() : Promise.resolve()]), l !== o) return;
			let d = await ue({
				theme: e,
				resourceRoot: t,
				sounds: u
			});
			l === o && (i.value = d);
		} catch (e) {
			l === o && (a.value = e instanceof Error ? e : Error(String(e)));
		}
	}, { immediate: !0 }), D(n, (e) => s?.setEnabled(e)), D(r, (e) => {
		try {
			s?.setVolume(e);
		} catch (e) {
			a.value = e instanceof Error ? e : Error(String(e));
		}
	}), m(() => {
		o++, s?.dispose();
	}), _(de, i);
	let l = S(0);
	return _(q, { pending: l }), {
		renderer: i,
		error: a,
		pending: l
	};
}
function me() {
	let e = l(de);
	if (!e) throw Error("Game UI controls require BfUiProvider");
	return n(() => e.value);
}
async function he(e) {
	let t = e.replace(/\/?$/, "/"), n = await fetch(t + "fonts/fonts.json").catch(() => null), r = n?.ok ? await n.json() : {};
	await Promise.all([["BF", "GB13000.ttf"], ["BFMalgun", "DF_GB_Y9.TTF"]].map(async ([e, n]) => {
		if (document.fonts.check(`13px ${e}`) && Array.from(document.fonts).some((t) => t.family === e)) return;
		let i = await new FontFace(e, `url("${t}fonts/${r[n] ?? n}")`, { weight: "400" }).load();
		document.fonts.add(i);
	}));
}
// ../tools/game-ui/src/components/BfUiProvider.vue?vue&type=script&setup=true&lang.ts
var ge = { role: "alert" }, J = /* @__PURE__ */ c({
	__name: "BfUiProvider",
	props: {
		theme: { default: "v2" },
		clientDate: {},
		resourceRoot: { default: "/local/" },
		sound: {
			type: Boolean,
			default: !0
		},
		volume: { default: N }
	},
	emits: ["error"],
	setup(e, { expose: t, emit: r }) {
		let i = e, s = r, { renderer: c, error: l } = pe(() => I(i.clientDate, i.theme), () => i.resourceRoot, () => i.sound, () => i.volume);
		t({
			unlockSound: () => c.value?.sounds?.unlock(),
			playSound: (e) => c.value?.sounds?.play(e)
		}), D(l, (e) => {
			e && s("error", e);
		});
		let u = n(() => {
			let e = i.resourceRoot.replace(/\/?$/, "/") + "cursors/";
			return {
				"--bf-cursor": `url("${e}normal.png") 0 0, default`,
				"--bf-cursor-clicked": `url("${e}clicked.png") 0 0, default`
			};
		});
		return (e, t) => (g(), a("div", {
			class: "bf-ui-root",
			style: p(u.value)
		}, [w(c) ? x(e.$slots, "default", {}, void 0, void 0, 0) : w(l) ? x(e.$slots, "error", { error: w(l) }, () => [o("div", ge, C(w(l).message), 1)], void 0, 1) : x(e.$slots, "loading", {}, void 0, void 0, 2)], 4));
	}
}), Y = /* @__PURE__ */ c({
	__name: "BfNative",
	props: {
		draw: { type: Function },
		revision: {}
	},
	emits: [
		"action",
		"change",
		"error"
	],
	setup(n, { expose: s, emit: c }) {
		let l = n, u = c, f = me(), p = y(), h = y(), _ = 0, v;
		function b() {
			v?.(), v = void 0, h.value = void 0, p.value?.replaceChildren();
		}
		let S = () => {
			try {
				return JSON.stringify(l.revision);
			} catch {
				return String(Math.random());
			}
		}, C = fe(), w = !1, T = () => {
			w && C && (C.pending.value--, w = !1);
		};
		D([
			f,
			S,
			p
		], async () => {
			let e = ++_;
			if (!f.value || !p.value) {
				b(), T();
				return;
			}
			let t = f.value;
			C && !w && (C.pending.value++, w = !0);
			try {
				let n = await l.draw(t);
				if (await Promise.all([...n.querySelectorAll("img")].map((e) => e.decode().catch(() => void 0))), e !== _ || !p.value) return;
				v?.(), p.value.replaceChildren(n), h.value = n, v = t.sounds?.bind(n), await d(), e === _ && T();
			} catch (t) {
				e === _ && (b(), T(), u("error", t instanceof Error ? t : Error(String(t))));
			}
		}, { flush: "post" }), m(() => {
			_++, T(), v?.(), h.value = void 0;
		}), s({ surface: h });
		function E(e, t) {
			let n = e.detail;
			u(t === "action" ? "action" : "change", n);
		}
		return (n, s) => (g(), a(e, null, [o("div", {
			ref_key: "host",
			ref: p,
			class: "bf-native-host",
			onBfAction: s[0] ||= (e) => E(e, "action"),
			onBfChange: s[1] ||= (e) => E(e, "change")
		}, null, 544), h.value ? (g(), r(t, {
			key: 0,
			to: h.value
		}, [x(n.$slots, "default")], 8, ["to"])) : i("v-if", !0)], 64));
	}
}), _e = /* @__PURE__ */ c({
	__name: "BfWindow",
	props: {
		title: {},
		width: {},
		height: {},
		closeLabel: {},
		titleAlign: {},
		fontFamily: {},
		fontWeight: {},
		textRenderer: {},
		textScale: {},
		resourceRoot: {},
		titleBounds: {}
	},
	emits: [
		"close",
		"action",
		"error"
	],
	setup(e, { emit: t }) {
		let i = e, a = t, o = n(() => [
			i.title,
			i.width,
			i.height,
			i.closeLabel,
			i.titleAlign,
			i.fontFamily,
			i.fontWeight,
			i.textRenderer,
			i.textScale,
			i.titleBounds,
			i.resourceRoot
		]);
		function s(e) {
			e.name === "close" && a("close"), a("action", e);
		}
		async function c(e) {
			let t = await e.windowShell(i.width, i.height, i.title);
			i.closeLabel && t.querySelector(".bf-close")?.setAttribute("aria-label", i.closeLabel);
			let n = t.querySelector(".caption");
			if (i.titleAlign && n) {
				let t = e.manifest.scheme.Dialog;
				i.titleBounds && (n.style.top = i.titleBounds.top + "px", n.style.height = i.titleBounds.height + "px"), i.titleAlign === "center" && (n.style.left = (i.width - parseFloat(n.style.width)) / 2 + "px");
				let r = Number(t.Title_FontSize), a = i.textScale ?? 1, o = i.textScale ? Math.max(10, Math.round(r * a)) / a : r;
				if (await L(n, i.title, {
					size: o,
					family: i.fontFamily ?? "BF",
					align: i.titleAlign,
					weight: i.fontWeight ?? 400,
					color: "#" + t.Title_TextColor.slice(-6),
					decoration: Number(t.Title_TextDecoration),
					outline: "#" + t.Title_TextDecoColor.slice(-6),
					outlineWidth: i.textRenderer === "svg" || i.textRenderer === "freetype" ? .5 : i.textRenderer === "native" ? 1 : 2,
					nativeMetrics: e.theme !== "v1",
					renderer: i.textRenderer === "freetype" ? "freetype" : i.titleBounds && i.textRenderer !== "svg" ? "canvas" : i.textRenderer,
					resourceRoot: i.resourceRoot,
					pixelRatio: i.textScale,
					centerInk: !!i.titleBounds,
					vertical: e.theme === "v3" ? "middle" : "top"
				}), i.titleBounds && i.textRenderer !== "svg") {
					let e = n.querySelector("canvas");
					if (e) {
						let t = Object.assign(new Image(), { crossOrigin: "anonymous" });
						t.alt = "", t.ariaHidden = "true", t.style.cssText = e.style.cssText, t.src = e.toDataURL("image/png"), await t.decode(), e.replaceWith(t);
					}
				}
			}
			return t;
		}
		return (e, t) => (g(), r(Y, {
			revision: o.value,
			draw: c,
			onAction: s,
			onError: t[0] ||= (e) => a("error", e)
		}, {
			default: O(() => [x(e.$slots, "default")]),
			_: 3
		}, 8, ["revision"]));
	}
}), ve = /* @__PURE__ */ c({
	__name: "BfPanel",
	props: {
		x: {},
		y: {},
		width: {},
		height: {},
		skin: { default: "Images/Ui/FrameBox.smf" }
	},
	setup(e) {
		let t = e;
		function n() {
			return document.createElement("div");
		}
		return (i, a) => (g(), r(Y, {
			revision: [
				t.x,
				t.y,
				t.width,
				t.height,
				t.skin
			],
			draw: (t) => t.panel(n(), e.x, e.y, e.width, e.height, e.skin)
		}, {
			default: O(() => [x(i.$slots, "default")]),
			_: 3
		}, 8, ["revision", "draw"]));
	}
}), ye = /* @__PURE__ */ c({
	__name: "BfControl",
	props: {
		type: {},
		attrs: {}
	},
	emits: [
		"change",
		"action",
		"error"
	],
	setup(e, { emit: t }) {
		let i = e;
		function a() {
			return document.createElement("div");
		}
		let o = y(), s = n(() => {
			let { Check: e, Position: t, ...n } = i.attrs;
			return JSON.stringify([i.type, n]);
		});
		D(() => [i.attrs.Check, i.attrs.Position], () => {
			(o.value?.surface)?.setValue?.(i.type === "UiCheckBox" ? i.attrs.Check === "1" : Number(i.attrs.Position));
		}, { flush: "post" });
		let c = t;
		return (t, n) => (g(), r(Y, {
			ref_key: "native",
			ref: o,
			revision: s.value,
			draw: async (t) => await t.renderNode({
				type: e.type,
				attrs: e.attrs,
				children: []
			}, a()),
			onChange: n[0] ||= (e) => c("change", e),
			onAction: n[1] ||= (e) => c("action", e),
			onError: n[2] ||= (e) => c("error", e)
		}, {
			default: O(() => [x(t.$slots, "default")]),
			_: 3
		}, 8, ["revision", "draw"]));
	}
}), be = /* @__PURE__ */ c({
	__name: "BfGroupBox",
	props: {
		title: {},
		x: {},
		y: {},
		width: {},
		height: {}
	},
	setup(e) {
		let t = e, i = me(), a = n(() => {
			let e = i.value;
			return e ? {
				...e.manifest.forms[e.theme === "v3" ? "Option/DlgOption-VideoSound3.ui" : e.theme === "v2" ? "DlgOption-VideoSound2.ui" : "DlgOption-VideoSound.ui"].children.find((e) => e.type === "UiGroupBox" || e.type === "UiPanel").attrs,
				Name: "",
				Text: t.title,
				PosX: String(t.x),
				PosY: String(t.y),
				Width: String(t.width),
				Height: String(t.height)
			} : {};
		});
		return (e, t) => (g(), r(ye, {
			type: "UiGroupBox",
			attrs: a.value
		}, {
			default: O(() => [x(e.$slots, "default")]),
			_: 3
		}, 8, ["attrs"]));
	}
}), X = /* @__PURE__ */ c({
	__name: "BfLabel",
	props: {
		text: {},
		x: {},
		y: {},
		width: {},
		height: {},
		size: { default: 13 },
		color: { default: "0xFF5B5B5B" },
		align: { default: "left" }
	},
	setup(e) {
		let t = e, i = n(() => ({
			Text: t.text,
			PosX: String(t.x),
			PosY: String(t.y),
			Width: String(t.width),
			Height: String(t.height),
			FontSize: String(t.size),
			TextColor: t.color,
			TextFormat: t.align === "center" ? "5" : "4"
		}));
		return (e, t) => (g(), r(ye, {
			type: "UiLabel",
			attrs: i.value
		}, null, 8, ["attrs"]));
	}
}), xe = /* @__PURE__ */ c({
	__name: "BfCheckbox",
	props: {
		modelValue: { type: Boolean },
		label: {},
		name: {},
		x: {},
		y: {},
		width: { default: 220 }
	},
	emits: ["update:modelValue", "change"],
	setup(t, { emit: r }) {
		let i = t, o = r, c = n(() => ({
			Name: i.name || i.label,
			PosX: String(i.x),
			PosY: String(i.y),
			Width: "21",
			Height: "18",
			Check: i.modelValue ? "1" : "0"
		}));
		function l(e) {
			let t = !!e.value;
			o("update:modelValue", t), o("change", t);
		}
		return (n, r) => (g(), a(e, null, [s(ye, {
			type: "UiCheckBox",
			attrs: c.value,
			onChange: l
		}, null, 8, ["attrs"]), s(X, {
			text: t.label,
			x: t.x + 23,
			y: t.y,
			width: t.width,
			height: 20
		}, null, 8, [
			"text",
			"x",
			"y",
			"width"
		])], 64));
	}
}), Se = /* @__PURE__ */ c({
	__name: "BfSlider",
	props: {
		modelValue: {},
		name: {},
		x: {},
		y: {},
		width: {},
		height: { default: 55 },
		min: { default: 0 },
		max: { default: 100 },
		interval: { default: 10 },
		minText: { default: "" },
		maxText: { default: "" }
	},
	emits: ["update:modelValue", "change"],
	setup(e, { emit: t }) {
		let i = e, a = t, o = n(() => ({
			Name: i.name,
			PosX: String(i.x),
			PosY: String(i.y),
			Width: String(i.width),
			Height: String(i.height),
			Min: String(i.min),
			Max: String(i.max),
			Position: String(i.modelValue),
			Interval: String(i.interval),
			MinText: i.minText,
			MaxText: i.maxText
		}));
		function s(e) {
			a("update:modelValue", Number(e.value)), a("change", Number(e.value));
		}
		return (e, t) => (g(), r(ye, {
			type: "UiTrackBar",
			attrs: o.value,
			onChange: s
		}, null, 8, ["attrs"]));
	}
}), Z = /* @__PURE__ */ c({
	__name: "BfButton",
	props: {
		kind: { default: "Ok" },
		label: {},
		x: {},
		y: {},
		width: { default: 65 },
		height: { default: 27 }
	},
	emits: ["click"],
	setup(e, { emit: t }) {
		let n = e, i = t;
		function a() {
			return document.createElement("div");
		}
		return (t, o) => (g(), r(Y, {
			revision: [
				n.kind,
				n.label,
				n.x,
				n.y,
				n.width,
				n.height
			],
			draw: (t) => t.button(a(), e.kind, e.x, e.y, e.width, e.height, e.label),
			onAction: o[0] ||= (e) => i("click")
		}, null, 8, ["revision", "draw"]));
	}
}), Ce = /* @__PURE__ */ c({
	__name: "BfGamePage",
	props: { page: {} },
	emits: [
		"action",
		"change",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t;
		return (t, a) => (g(), r(Y, {
			revision: n.page,
			draw: (t) => e.page === "notice" || e.page === "warning" || e.page === "error" || e.page === "confirm" ? t.dialog({
				confirm: e.page === "confirm",
				icon: e.page === "confirm" ? "question" : e.page,
				openSound: e.page === "error" ? "fail" : "popup"
			}) : t[e.page](),
			onAction: a[0] ||= (e) => i("action", e),
			onChange: a[1] ||= (e) => i("change", e),
			onError: a[2] ||= (e) => i("error", e)
		}, null, 8, ["revision", "draw"]));
	}
}), we = /* @__PURE__ */ c({
	__name: "BfDialog",
	props: {
		title: { default: "公告" },
		text: { default: "" },
		icon: { default: "notice" },
		buttons: {
			type: [Array, Function],
			default: () => [{
				name: "Ok",
				label: "确认"
			}]
		},
		contentWidth: {},
		contentHeight: {},
		contentX: {},
		contentY: {},
		outerWidth: {},
		outerHeight: {},
		buttonY: {},
		body: {},
		openSound: { type: Boolean },
		textScale: {}
	},
	emits: [
		"action",
		"change",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t, a = (e) => e.dialog({
			title: n.title,
			text: n.text,
			icon: n.icon,
			buttons: typeof n.buttons == "function" ? n.buttons(e) : n.buttons,
			contentWidth: n.contentWidth,
			contentHeight: n.contentHeight,
			contentX: n.contentX,
			contentY: n.contentY,
			outerWidth: n.outerWidth,
			outerHeight: n.outerHeight,
			buttonY: n.buttonY,
			body: n.body ? ((t, r) => n.body(e, t, r)) : void 0,
			openSound: n.openSound,
			textScale: n.textScale
		});
		return (e, t) => (g(), r(Y, {
			revision: JSON.stringify({
				title: n.title,
				text: n.text,
				icon: n.icon,
				contentWidth: n.contentWidth,
				contentHeight: n.contentHeight,
				outerWidth: n.outerWidth,
				outerHeight: n.outerHeight,
				buttonY: n.buttonY,
				openSound: n.openSound,
				textScale: n.textScale
			}),
			draw: a,
			onAction: t[0] ||= (e) => i("action", e),
			onChange: t[1] ||= (e) => i("change", e),
			onError: t[2] ||= (e) => i("error", e)
		}, null, 8, ["revision"]));
	}
}), Te = /* @__PURE__ */ c({
	__name: "BfMessageBox",
	props: {
		title: { default: "公告" },
		text: {},
		icon: { default: "notice" },
		buttons: {},
		openSound: { type: Boolean },
		textScale: {}
	},
	emits: [
		"action",
		"change",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t;
		return (e, t) => (g(), r(we, {
			title: n.title,
			text: n.text,
			icon: n.icon,
			buttons: n.buttons,
			"open-sound": n.openSound,
			"text-scale": n.textScale,
			onAction: t[0] ||= (e) => i("action", e),
			onChange: t[1] ||= (e) => i("change", e),
			onError: t[2] ||= (e) => i("error", e)
		}, null, 8, [
			"title",
			"text",
			"icon",
			"buttons",
			"open-sound",
			"text-scale"
		]));
	}
}), Ee = /* @__PURE__ */ c({
	__name: "BfNotifyBox",
	props: { text: {} },
	emits: ["error"],
	setup(e, { emit: t }) {
		let n = e, i = t;
		return (e, t) => (g(), r(Y, {
			revision: n.text,
			draw: (e) => e.message({ text: n.text }),
			onError: t[0] ||= (e) => i("error", e)
		}, null, 8, ["revision", "draw"]));
	}
}), De = /* @__PURE__ */ c({
	__name: "BfExtensionSettings",
	props: { modelValue: {} },
	emits: [
		"update:modelValue",
		"change",
		"save",
		"cancel",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t, a = ce(n.modelValue), o = y(0);
		D(() => n.modelValue, (e) => {
			JSON.stringify(e) !== JSON.stringify(a) && (a = ce(e), o.value++);
		}, { deep: !0 });
		function s(e) {
			a = e, i("update:modelValue", { ...e }), i("change", { ...e });
		}
		let c = (e) => e.extensionSettings({
			value: a,
			onChange: s
		});
		function l(e) {
			e.name === "Ok" && i("save", { ...a }), (e.name === "Cancel" || e.name === "close") && i("cancel");
		}
		return (e, t) => (g(), r(Y, {
			revision: o.value,
			draw: c,
			onAction: l,
			onError: t[0] ||= (e) => i("error", e)
		}, null, 8, ["revision"]));
	}
}), Oe = (e) => /(?:^|\/)(?:DialogButton|RestrictionButton)(?:Ok|Cancel)-(?:Up|Down|Hover|Disabled)\.smf$/i.test(e);
function ke(e) {
	return e.some(Oe);
}
function Ae(e, { skins: t, width: n, height: r }) {
	if (t.length !== 4) throw Error("Caption buttons require four native state skins.");
	let i = Oe;
	if (!t.some(i) || e.manifest === void 0) return {
		width: n,
		height: r
	};
	if (!t.every(i)) throw Error("Native caption buttons require matching icon-button state skins.");
	let a = t.map((t) => {
		let n = e.manifest?.sprites?.[t];
		if (!Array.isArray(n) || n.length !== 9) throw Error(`Native caption button requires nine-slice frames: ${t}`);
		for (let e of n) if (!Array.isArray(e) || e.length !== 4 || !e.every(Number.isInteger) || e[0] < 0 || e[1] < 0 || e[2] < e[0] || e[3] < e[1]) throw Error(`Native caption button has an invalid inclusive frame rectangle: ${t}`);
		let r = 0;
		for (let e of [
			0,
			3,
			6
		]) {
			let i = n[e][3] - n[e][1] + 1;
			if (n.slice(e, e + 3).some((e) => e[3] - e[1] + 1 !== i)) throw Error(`Native caption button has inconsistent nine-slice row heights: ${t}`);
			r += i;
		}
		return r;
	});
	if (a.some((e) => e !== a[0])) throw Error("Native caption button state skins have different original heights.");
	return {
		width: n,
		height: a[0]
	};
}
var je = 27;
function Me(e, t, { margin: n = 37, minimum: r = 65 } = {}) {
	if (!Array.isArray(e) || e.length === 0) throw Error("Fitting a caption button needs at least one caption.");
	let i = 0;
	for (let n of e) {
		let e = t(n);
		if (!Number.isFinite(e) || e < 0) throw Error("A caption measurement must be a finite nonnegative width.");
		i = Math.max(i, e);
	}
	return Math.max(r, Math.ceil(i) + n);
}
async function Ne(e, t = "BF", { size: n = 13, margin: r, minimum: i } = {}) {
	await document.fonts.load(`700 ${n}px ${t}`);
	let a = document.createElement("canvas").getContext("2d");
	if (!a) throw Error("Native button captions require a text measurement context.");
	return a.font = `700 ${n}px ${t}`, Me(e, (e) => a.measureText(e).width, {
		margin: r,
		minimum: i
	});
}
function Pe(e, t) {
	let n = /^0x([0-9a-f]{2})([0-9a-f]{6})$/i.exec(String(e ?? ""));
	return n ? `#${n[2]}` : t;
}
async function Fe(e, { label: t, width: n, height: r, skins: i, disabled: a = !1, kind: o = "Ok", family: s = "BF", size: c = 12, weight: l = 700, renderer: u = "native", resourceRoot: d = "", pixelRatio: f = 1 }) {
	let p = Ae(e, {
		skins: i,
		width: n,
		height: r
	}), m = Math.floor((r - p.height) / 2), h = document.createElement("button");
	h.className = "control sprite-button", h.ariaLabel = t, h.style.cssText = `left:0;top:0;width:${n}px;height:${r}px`, h.disabled = a;
	let g = [];
	for (let t of i) {
		let n = await e.panel(h, 0, m, p.width, p.height, t);
		n.style.pointerEvents = "none", g.push(n);
	}
	let _ = ke(i);
	_ && (g[3].style.filter = "grayscale(1)");
	let v = document.createElement("span");
	v.className = "control";
	let [y, b] = _ ? [27, p.width - 33] : [0, p.width];
	v.style.cssText = `left:${y}px;top:${m}px;width:${b}px;height:${p.height}px;pointer-events:none`, h.append(v);
	let x = e.manifest?.scheme?.UiButton ?? {}, S = _ && e.theme !== "v1";
	await L(v, t, {
		family: s,
		size: c,
		weight: l,
		align: "center",
		vertical: "middle",
		centerInk: !0,
		renderer: u,
		resourceRoot: d,
		pixelRatio: f,
		color: _ ? "#fff" : a ? Pe(x.DisabledTextColor, "#e1e9eb") : Pe(x.TextColor, "#232323"),
		decoration: S ? 4 : 0,
		outline: S ? a ? "#949494" : "#4190d2" : void 0,
		outlineWidth: .5,
		inset: 0
	}), a && _ && (v.style.opacity = "0.6");
	function C(e) {
		g.forEach((t, n) => {
			t.style.visibility = n === (h.disabled ? 3 : e) ? "visible" : "hidden";
		});
	}
	C(0);
	for (let [e, t] of [
		["pointerenter", 2],
		["pointerleave", 0],
		["pointerdown", 1],
		["pointerup", 2],
		["pointercancel", 0]
	]) h.addEventListener(e, () => C(t));
	return h.addEventListener("click", () => {
		h.disabled || h.dispatchEvent(new CustomEvent("bf-action", {
			bubbles: !0,
			detail: {
				name: o,
				text: t
			}
		}));
	}), h;
}
// ../tools/game-ui/pagination.js
function Ie(e, t, n, r) {
	return r <= 1 || n <= 0 ? 0 : Math.max(0, Math.min(r - 1, Math.round(e + t * (r - 1) / n)));
}
// ../tools/game-ui/src/components/BfBanPick.vue?vue&type=script&setup=true&lang.ts
var Le = [
	"data-theme",
	"lang",
	"aria-label"
], Re = { class: "ban-pick-heading" }, ze = ["aria-label"], Be = ["aria-label"], Ve = { class: "ban-pick-category-line" }, He = ["aria-label"], Ue = [
	"id",
	"aria-selected",
	"onClick"
], We = ["src"], Ge = {
	key: 0,
	class: "category-count"
}, Ke = { class: "ban-pick-tools" }, qe = { class: "ban-pick-field" }, Je = ["aria-label"], Ye = {
	key: 0,
	class: "ban-pick-placeholder"
}, Xe = { class: "ban-pick-filter" }, Ze = { class: "ban-pick-subtabs" }, Qe = ["aria-pressed"], $e = ["src"], et = ["aria-pressed", "onClick"], tt = ["src"], nt = ["aria-labelledby"], rt = { class: "ban-pick-grid" }, it = [
	"aria-pressed",
	"aria-disabled",
	"aria-label",
	"onMouseenter",
	"onFocus",
	"onClick",
	"onMouseenterPassive",
	"onMousemovePassive"
], at = ["src"], ot = { class: "ban-pick-item-preview" }, st = ["src"], ct = {
	key: 2,
	class: "ban-pick-item-missing"
}, lt = ["src"], ut = {
	key: 1,
	class: "ban-pick-badge"
}, dt = {
	key: 0,
	class: "ban-pick-empty"
}, ft = { class: "ban-pick-pagination" }, pt = ["disabled", "aria-label"], mt = ["src"], ht = ["src"], gt = [
	"aria-label",
	"aria-valuemax",
	"aria-valuenow"
], _t = ["src"], vt = ["src"], yt = ["src"], bt = ["disabled", "aria-label"], xt = ["src"], St = ["src"], Ct = { class: "ban-pick-bottom" }, wt = ["aria-label"], Tt = { class: "ban-pick-actions" }, Et = ["data-theme", "aria-label"], Dt = ["lang"], Ot = ["aria-label"], kt = { value: "" }, At = ["value"], jt = { class: "ban-pick-field" }, Mt = ["aria-label"], Nt = {
	key: 0,
	class: "ban-pick-placeholder"
}, Pt = { class: "preset-buttons" }, Ft = 8, It = 3, Lt = /*#__PURE__*/ ((e, t) => {
	let n = e.__vccOpts || e;
	for (let [e, r] of t) n[e] = r;
	return n;
})(/* @__PURE__ */ c({
	__name: "BfBanPick",
	props: {
		modelValue: {},
		items: {},
		labels: {},
		resourceRoot: {},
		appearance: {},
		locale: { default: "zh-CN" },
		presets: { default: () => [] },
		textScale: { default: 1 },
		readonly: {
			type: Boolean,
			default: !1
		},
		characters: { default: () => ({}) },
		active: {
			type: Boolean,
			default: !0
		},
		defaultSubCategories: { default: () => ({}) }
	},
	emits: [
		"update:modelValue",
		"apply",
		"cancel",
		"save-preset",
		"delete-preset",
		"error",
		"ready"
	],
	setup(t, { emit: c }) {
		let l = t, d = c, h = [
			"weapons",
			"characters",
			"clothes",
			"accessories"
		], _ = n(() => Math.max(...Object.values(l.appearance.categorySizes).map((e) => e.height))), v = (e) => Math.max(e >= 12 ? 10 : 1, Math.round(e * l.textScale)) / l.textScale, x = y("weapons"), S = y(l.defaultSubCategories.weapons ?? "all"), w = y(""), A = y(0), M = y(!1), N = y(), P = y(""), F = y(""), I = y(!1), R = y();
		D(I, (e) => {
			e ? R.value?.showModal() : R.value?.close();
		}, { flush: "post" });
		let z = y(), B = y(), V = fe(), H = y(!1), U = !1, ee = 0, W;
		async function G() {
			let e = /* @__PURE__ */ new Set();
			for (let t of xe.value) typeof t.preview == "string" ? e.add(t.preview) : t.preview && e.add(t.preview.url);
			await Promise.all([...e].map((e) => {
				let t = Object.assign(new Image(), { crossOrigin: "anonymous" });
				return t.src = e, t.decode().catch(() => void 0);
			})), await Promise.all([...z.value?.querySelectorAll("img") ?? []].map((e) => e.decode().catch(() => void 0)));
		}
		function te() {
			if (H.value || U) return;
			let e = ++ee;
			clearTimeout(W), W = setTimeout(async () => {
				W = void 0, U || e !== ee || (V?.pending.value ?? 0) > 0 || (await G(), !(U || e !== ee) && ((V?.pending.value ?? 0) === 0 ? (H.value = !0, d("ready"), we(h.flatMap(Lt)), we(h.flatMap(Rt)), zt()) : te()));
			}, 50);
		}
		D(() => V?.pending.value ?? 0, te, { immediate: !0 }), m(() => {
			U = !0, ee++, clearTimeout(W);
		});
		let K = y(), ne = n(() => l.items.find((e) => e.id === K.value?.id && e.category !== "characters")), re = n(() => ne.value ? ne.value.spec?.length ? "DlgWeaponInfoPopup.ui" : ne.value.members ? "DlgInfoPopupSetArmor.ui" : "DlgInfoPopupArmor.ui" : ""), ie = y({
			width: 274,
			height: 168
		});
		function ae(e, t) {
			if (t.category === "characters" || !z.value) {
				K.value = void 0;
				return;
			}
			let n = z.value.getBoundingClientRect(), r = n.width / z.value.offsetWidth || 1;
			K.value = {
				id: t.id,
				x: (e.clientX - n.left) / r,
				y: (e.clientY - n.top) / r
			};
		}
		let oe = n(() => {
			if (!K.value || !z.value) return {};
			let { width: e, height: t } = ie.value, n = {
				w: z.value.offsetWidth,
				h: z.value.offsetHeight
			}, r = K.value.x + 18 + e > n.w ? K.value.x - 8 - e : K.value.x + 18, i = Math.min(Math.max(0, K.value.y + 20), Math.max(0, n.h - t));
			return {
				left: Math.max(0, r) + "px",
				top: i + "px"
			};
		}), se = [
			"Damage",
			"HitRatio",
			"MoveSpeed",
			"FireCoolTick",
			"Eiz"
		], ce = [
			"pnlArmorPoint",
			"pnlReloadSpeed",
			"pnlMoveSpeed"
		], le = [
			"lblArmorPoint",
			"lblReloadSpeed",
			"lblMoveSpeed"
		];
		function ue(e) {
			let t = l.characters;
			return (!e || e.trim().toLowerCase() === "all" ? Object.keys(t) : e.trim().split(/\s+/)).map((e) => t[e] ?? e).join(", ");
		}
		async function de(e) {
			let t = ne.value, n = e.manifest.forms[re.value], r = document.createElement("div");
			if (!t || !n) return r;
			ie.value = {
				width: Number(n.attrs.Width),
				height: Number(n.attrs.Height)
			};
			let i = (e) => {
				let n = se.indexOf(e), r = ce.indexOf(e);
				return n >= 0 ? t.spec?.[n] : r >= 0 ? t.setStats?.[r] ?? 0 : void 0;
			}, a = re.value === "DlgWeaponInfoPopup.ui" ? l.labels.stats ?? [] : [l.labels.wearable ?? "", ...re.value === "DlgInfoPopupSetArmor.ui" ? l.labels.armorStats ?? [] : []], o = (e) => {
				if ([
					"WeaponName",
					"itemName",
					"lblItemName"
				].includes(e)) return t.name;
				if (["useChars", "lblAvailableCharacters"].includes(e)) return ue(t.allow);
				if (["notice", "lblNotice"].includes(e)) return l.labels.wearNotice ?? "";
				let n = le.indexOf(e);
				return n >= 0 ? String(t.setStats?.[n] ?? 0) : void 0;
			}, s = [], c = (e, t, n) => {
				let r = {
					x: t + Number(e.attrs.PosX || 0),
					y: n + Number(e.attrs.PosY || 0)
				}, a = e.children.filter((e) => ![
					"Owned",
					"lblOwned",
					"pnEnchantLv",
					"pnlEnchantLv"
				].includes(e.attrs.Name ?? "")).filter((e) => e.type !== "UiLabel" || (s.push({
					attrs: e.attrs,
					...r
				}), !1)).map((e) => c(e, r.x, r.y)), o = i(e.attrs.Name ?? ""), l = o === void 0 ? e.attrs : {
					...e.attrs,
					Width: String(Math.max(0, Math.round(Number(e.attrs.Width) * Math.min(100, o) / 100)))
				};
				return {
					...e,
					attrs: l,
					children: a
				};
			}, u = c(n, 0, 0), d = await e.renderNode({
				...u,
				attrs: {
					...u.attrs,
					PosX: "0",
					PosY: "0"
				}
			}, r), f = s.filter((e) => !e.attrs.Name && !/^\d+$/.test(e.attrs.Text ?? "")).sort((e, t) => Number(e.attrs.PosY) - Number(t.attrs.PosY));
			for (let e of s) {
				let t = e.attrs, n = f.indexOf(e), r = o(t.Name ?? "") ?? (n >= 0 ? a[n] ?? t.Text ?? "" : t.Text ?? ""), i = document.createElement("span");
				i.className = "control bf-label", i.style.cssText = `position:absolute;left:${e.x + Number(t.PosX)}px;top:${e.y + Number(t.PosY)}px;width:${t.Width}px;height:${t.Height}px;pointer-events:none`, d.append(i);
				let s = Number(t.FontSize) || 12, c = Number(t.TextFormat);
				await L(i, r, {
					color: "#" + (t.TextColor ?? "0xFFE4E4E4").slice(-6),
					size: v(s),
					family: $t(r),
					weight: 400,
					renderer: "freetype",
					align: c & 1 ? "center" : c & 2 ? "right" : "left",
					vertical: c & 4 ? "middle" : "top",
					centerInk: !!(c & 4) && !(c & 16),
					wrap: !!(c & 16),
					decoration: 0,
					inset: 0,
					resourceRoot: l.resourceRoot,
					pixelRatio: l.textScale
				});
			}
			return d;
		}
		let q;
		function pe(e) {
			if (X.value <= 1 || !B.value) return;
			let t = e.currentTarget;
			q = {
				pointer: e.pointerId,
				x: e.clientX,
				page: A.value,
				travel: B.value.getBoundingClientRect().width - t.getBoundingClientRect().width
			}, t.setPointerCapture(e.pointerId), e.preventDefault();
		}
		function me(e) {
			q?.pointer === e.pointerId && (A.value = Ie(q.page, e.clientX - q.x, q.travel, X.value));
		}
		function he(e) {
			if (!B.value) return;
			let t = B.value.getBoundingClientRect();
			A.value = Math.max(0, Math.min(X.value - 1, Math.floor((e.clientX - t.left) * X.value / t.width)));
		}
		function ge(e) {
			let t = {
				ArrowLeft: A.value - 1,
				ArrowDown: A.value - 1,
				ArrowRight: A.value + 1,
				ArrowUp: A.value + 1,
				Home: 0,
				End: X.value - 1,
				PageUp: A.value - 1,
				PageDown: A.value + 1
			}[e.key];
			t !== void 0 && (e.preventDefault(), A.value = Math.max(0, Math.min(X.value - 1, t)));
		}
		let J = n(() => {
			let e = new Set(l.modelValue);
			for (let t of l.items) t.members?.length && t.members.every((t) => e.has(t)) && e.add(t.id);
			return e;
		}), ve = n(() => {
			let e = l.defaultSubCategories[x.value];
			return [...new Set(l.items.filter((e) => e.category === x.value).map((e) => e.subCategory))].sort((t, n) => Number(n === e) - Number(t === e) || (l.appearance.subCategoryOrder[t] ?? 0) - (l.appearance.subCategoryOrder[n] ?? 0));
		}), be = n(() => l.items.filter((e) => e.category === x.value && (!M.value && !l.readonly || J.value.has(e.id)) && (S.value === "all" || e.subCategory === S.value) && (!w.value.trim() || `${e.name} ${e.id}`.toLocaleLowerCase(l.locale).includes(w.value.trim().toLocaleLowerCase(l.locale))))), X = n(() => Math.max(1, Math.ceil(be.value.length / Ft))), xe = n(() => be.value.slice(A.value * Ft, A.value * Ft + Ft)), Se = /* @__PURE__ */ new Map(), Z = /* @__PURE__ */ new Map();
		function Ce(e) {
			let t = Object.assign(new Image(), { crossOrigin: "anonymous" });
			return t.src = e, t.decode().catch(() => void 0), t;
		}
		function we(e) {
			if (!U) {
				Pe(e, !0);
				for (let t of new Set(e.map(Te).filter((e) => !!e))) Se.has(t) || Se.set(t, Z.get(t) ?? Ce(t));
			}
		}
		function Te(e) {
			return typeof e.preview == "string" ? e.preview : e.preview?.url;
		}
		function Ee(e) {
			for (let t of new Set(e.map(Te).filter((e) => !!e))) {
				if (Se.has(t)) continue;
				let e = Z.get(t);
				if (e) Z.delete(t), Z.set(t, e);
				else for (Z.set(t, Ce(t)); Z.size > It;) Z.delete(Z.keys().next().value);
			}
		}
		let De = /* @__PURE__ */ new Map(), Oe = /* @__PURE__ */ new Map(), ke = /* @__PURE__ */ new Set(), Ae = !1, je;
		function Me(e) {
			return JSON.stringify([
				e.name,
				$t(e.name),
				v(12),
				l.textScale,
				l.appearance.nameBounds.height,
				l.resourceRoot
			]);
		}
		function Ne() {
			U || Ae || je !== void 0 || (je = setTimeout(() => {
				if (je = void 0, U) return;
				let e = [...De, ...Oe].find(([e]) => !ke.has(e));
				if (!e) return;
				let [t, n] = e;
				Ae = !0, tn(n).catch(() => void 0).finally(() => {
					Ae = !1, !U && ((De.has(t) || Oe.has(t)) && ke.add(t), Ne());
				});
			}, 0));
		}
		function Pe(e, t = !1) {
			if (U) return;
			let n = t ? De : Oe;
			t || n.clear();
			for (let t of e) n.set(Me(t), t);
			for (let e of ke) !De.has(e) && !Oe.has(e) && ke.delete(e);
			Ne();
		}
		function Lt(e) {
			let t = l.defaultSubCategories[e];
			return l.items.filter((n) => n.category === e && (!t || n.subCategory === t) && (!l.readonly || J.value.has(n.id))).slice(0, Ft);
		}
		function Rt(e) {
			let t = l.items.filter((t) => t.category === e && (!l.readonly || J.value.has(t.id)));
			return [...new Set(t.map((e) => e.subCategory))].flatMap((e) => t.filter((t) => t.subCategory === e).slice(0, Ft));
		}
		function zt() {
			if (U || !l.active) return;
			let e = (e, t) => be.value.slice((A.value + e) * Ft, (A.value + e + t) * Ft);
			Ee(e(1, 1)), Pe(e(1, 3));
		}
		D([be, A], () => zt()), m(() => {
			clearTimeout(je), Se.clear(), Z.clear(), De.clear(), Oe.clear(), ke.clear();
		}), D(() => l.active, (e) => {
			e ? zt() : (Z.clear(), Pe([]));
		});
		let Bt = n(() => l.resourceRoot.replace(/\/?$/, "/") + l.appearance.theme + "/"), Vt = (e) => Bt.value + (l.appearance.imageFormat === "webp" ? e.replace(/\.png$/i, ".webp") : e), Ht = n(() => l.items.find((e) => e.id === N.value) ?? xe.value[0]), Ut = () => Vt("Images/Shop2/inventory_box/inventory_slot01/0-card.png"), Wt = n(() => ({
			Name: l.labels.restrictedOnly,
			PosX: "0",
			PosY: "0",
			Width: "21",
			Height: "18",
			Check: M.value ? "1" : "0"
		})), Gt = (e, t) => Vt(e + `/${+!!t}-blank.png`), Kt = (e) => l.items.filter((t) => t.category === e && !t.members && J.value.has(t.id)).length;
		function qt(e) {
			let t = Math.min(164 / e.width, 120 / e.height);
			return {
				backgroundImage: `url("${e.url}")`,
				backgroundRepeat: "no-repeat",
				backgroundSize: `${e.sheetWidth * t}px ${e.sheetHeight * t}px`,
				backgroundPosition: `${(164 - e.width * t) / 2 - e.x * t}px ${(120 - e.height * t) / 2 - e.y * t}px`
			};
		}
		function Jt(e) {
			if (N.value = e, l.readonly) return;
			let t = new Set(l.modelValue), n = l.items.find((t) => t.id === e)?.members;
			if (n?.length) {
				let e = n.every((e) => t.has(e));
				for (let r of n) e ? t.delete(r) : t.add(r);
			} else t.delete(e) || t.add(e);
			d("update:modelValue", [...t].sort((e, t) => e - t));
		}
		function Yt() {
			if (l.readonly) return;
			let e = new Set(l.modelValue), t = [...new Set(be.value.flatMap((e) => e.members?.length ? [...e.members] : [e.id]))], n = t.length > 0 && t.every((t) => e.has(t));
			for (let r of t) n ? e.delete(r) : e.add(r);
			d("update:modelValue", [...e].sort((e, t) => e - t));
		}
		function Xt() {
			let e = l.presets.find((e) => e.id === F.value);
			e && d("update:modelValue", [...e.itemIds]);
		}
		D(F, (e) => {
			P.value = l.presets.find((t) => t.id === e)?.name ?? "";
		}), D(() => l.presets, (e) => {
			e.some((e) => e.id === F.value) || (F.value = "");
		}), D(x, (e) => {
			S.value = l.defaultSubCategories[e] ?? "all", A.value = 0, N.value = void 0;
		}), D([
			S,
			w,
			M
		], () => {
			A.value = 0, N.value = void 0;
		}), D(X, (e) => {
			A.value = Math.min(A.value, e - 1);
		});
		async function Zt(e) {
			if (e.theme !== l.appearance.theme) throw Error("Inventory theme and UI provider must match.");
			let t = e.manifest.forms["DlgItemInfo.ui"];
			if (!t) throw Error("DlgItemInfo.ui is missing from the prepared UI resources.");
			let n = 742 / l.appearance.infoSize.width, r = {
				...t,
				attrs: {
					...t.attrs,
					Width: "742"
				},
				children: t.children.map((e) => ({
					...e,
					attrs: {
						...e.attrs,
						PosX: String(Math.round(Number(e.attrs.PosX) * n)),
						Width: String(Math.max(1, Math.round(Number(e.attrs.Width) * n)))
					}
				}))
			}, i = Ht.value, a = {
				lblSlogan: i?.slogan ?? "",
				lblItemName: i?.name ?? "",
				lblItemDesc: i?.description || l.labels.noDescription,
				lblDesc1: i?.detail1 ?? "",
				lblDesc2: i?.detail2 ?? "",
				lblStock: ""
			}, o = await e.renderNode({
				...r,
				children: r.children.filter((e) => e.type !== "UiLabel")
			}, document.createElement("div")), s = l.appearance.infoTitleBar, c = s ? {
				...s,
				x: Math.round(s.x * n),
				width: Math.round(s.width * n)
			} : null;
			if (c) {
				let t = document.createElement("canvas");
				t.width = c.width, t.height = c.height, t.style.cssText = `position:absolute;left:${c.x}px;top:${c.y}px;width:${c.width}px;height:${c.height}px`, t.ariaHidden = "true";
				let n = t.getContext("2d");
				n.imageSmoothingEnabled = !1;
				let r = e.manifest.sprites[c.skin], i = r[0][2] - r[0][0] + 1, a = r[2][2] - r[2][0] + 1, s = [
					i,
					c.width - i - a,
					a
				], l = 0;
				for (let e = 0; e < 3; e++) {
					let t = Object.assign(new Image(), { crossOrigin: "anonymous" });
					t.src = Vt(c.skin.replace(".smf", "") + `/${e}.png`), await t.decode(), n.drawImage(t, l, 0, s[e], c.height), l += s[e];
				}
				let u = Object.assign(new Image(), { crossOrigin: "anonymous" });
				u.className = "control", u.alt = "", u.ariaHidden = "true", u.style.cssText = t.style.cssText, u.src = t.toDataURL("image/png"), await u.decode(), o.append(u);
			}
			for (let e of r.children.filter((e) => e.type === "UiLabel")) {
				let t = e.attrs, n = t.Name, r = document.createElement("span");
				r.className = "control bf-label", r.style.cssText = `left:${t.PosX}px;top:${t.PosY}px;width:${t.Width}px;height:${t.Height}px`, o.append(r);
				let i = Number(t.TextFormat);
				await L(r, a[n] ?? "", {
					color: "#" + t.TextColor.slice(-6),
					size: v(Number(t.FontSize)),
					family: $t(a[n] ?? "", t.FontName === "Malgun" ? "BFMalgun" : "BF"),
					weight: 700,
					renderer: "freetype",
					align: i & 1 ? "center" : i & 2 ? "right" : "left",
					outline: t.TextDecoColor ? "#" + t.TextDecoColor.slice(-6) : void 0,
					decoration: Number(t.TextDecoration),
					wrap: !!(i & 16),
					nativeMetrics: !0,
					vertical: i & 4 ? "middle" : "top",
					centerInk: !!(i & 4) && !(i & 16),
					inset: 0,
					resourceRoot: l.resourceRoot,
					pixelRatio: l.textScale
				});
			}
			return o;
		}
		let Qt = /[ᄀ-ᇿ㄰-㆏가-힯]/;
		function $t(e, t) {
			return l.locale === "ko" || Qt.test(e) ? "BFKorean" : t ?? "BF";
		}
		function Q(e) {
			let t = document.createElement("canvas").getContext("2d");
			return t.font = `700 ${v(12)}px ${$t(e)}`, Math.max(67, Math.ceil(t.measureText(e).width) + 39);
		}
		async function en(e, t, n, r = "left", i, a = 12) {
			let o = document.createElement("span");
			return o.className = "control", o.style.cssText = `position:absolute;left:${t.left}px;top:${t.top}px;width:${t.width}px;height:${t.height}px;pointer-events:none;text-shadow:none`, await L(o, e, {
				family: $t(e),
				size: v(a),
				weight: 700,
				resourceRoot: l.resourceRoot,
				pixelRatio: l.textScale,
				color: n,
				renderer: "freetype",
				centerInk: !0,
				inset: 0,
				width: t.width,
				height: t.height,
				align: r,
				outline: i,
				outlineWidth: .5,
				decoration: i ? 4 : 1
			}), o;
		}
		async function tn(e) {
			let t = await en(e.name, {
				left: 26,
				top: l.appearance.nameBounds.top,
				width: 140,
				height: l.appearance.nameBounds.height
			}, "#454545");
			return t.classList.add("ban-pick-item-name"), t;
		}
		async function nn(e) {
			let t = await en(J.value.has(e.id) ? l.labels.banned : l.labels.allowed, {
				left: 4,
				top: l.appearance.statusBounds.top,
				width: 164,
				height: l.appearance.statusBounds.height
			}, "#4b4b4b", "center");
			return t.classList.add("ban-pick-item-state"), t;
		}
		async function rn(e) {
			let t = l.appearance.categorySizes[e], n = await en(l.labels.categories[e], {
				left: 0,
				top: 0,
				width: t.width,
				height: t.height
			}, "#fff", "center", "#3d4d7b", l.locale.startsWith("zh") || l.locale === "ko" ? 15 : 11);
			return n.classList.add("ban-pick-category-caption"), n;
		}
		function an(e, t) {
			let n = document.createElement("canvas").getContext("2d");
			return n.font = `700 ${v(t)}px ${$t(e)}`, Math.ceil(n.measureText(e).width) + 6;
		}
		async function $(e, t, n = 12, r = 18, i) {
			let a = document.createElement("span"), o = an(e, n);
			return a.style.cssText = `position:relative;display:inline-block;width:${o}px;height:${r}px;vertical-align:middle;pointer-events:none`, a.append(await en(e, {
				left: 0,
				top: 0,
				width: o,
				height: r
			}, t, "center", i, n)), a;
		}
		async function on() {
			let e = await en(l.labels.restrictedOnly, {
				left: 30,
				top: 0,
				width: Math.max(112, Q(l.labels.restrictedOnly) - 8) - 30,
				height: 18
			}, "#456779");
			return e.classList.add("ban-pick-checkbox-caption"), e;
		}
		async function sn(e, t, n, r = !1, i = "Ok") {
			if (e.theme !== l.appearance.theme) throw Error("Button theme and UI provider must match.");
			return Fe(e, {
				label: t,
				width: n,
				height: l.appearance.actionHeight,
				skins: l.appearance.actionSkins[i],
				disabled: r,
				kind: i,
				family: $t(t),
				size: v(12),
				renderer: "freetype",
				resourceRoot: l.resourceRoot,
				pixelRatio: l.textScale
			});
		}
		return (n, c) => (g(), a(e, null, [
			s(_e, {
				onError: c[36] ||= (e) => d("error", e),
				title: t.labels.title,
				"title-align": t.appearance.theme === "v1" ? "left" : "center",
				"font-family": t.locale === "ko" ? "BFKorean" : "BF",
				"font-weight": 600,
				"title-bounds": t.appearance.titleBounds,
				"resource-root": t.resourceRoot,
				"close-label": t.labels.close,
				width: 772,
				height: 739 + _.value,
				"text-renderer": "freetype",
				"text-scale": t.textScale,
				onClose: c[37] ||= (e) => d("cancel")
			}, {
				default: O(() => [o("section", {
					ref_key: "panel",
					ref: z,
					class: f(["bf-ban-pick", { "ban-pick-active": t.active }]),
					"data-theme": t.appearance.theme,
					style: p({
						"--action-height": t.appearance.actionHeight + "px",
						"--category-height": _.value + "px",
						"--body-font-size": v(13) + "px",
						"--text-size": v(12) + "px"
					}),
					lang: t.locale,
					"aria-label": t.labels.title
				}, [
					o("div", Re, [o("span", {
						class: "ban-pick-hint",
						"aria-label": t.labels.hint
					}, [s(Y, {
						onError: c[0] ||= (e) => d("error", e),
						revision: [
							t.readonly,
							t.labels.hint,
							t.labels.bannedHint,
							t.locale,
							t.textScale
						],
						draw: () => $(t.readonly ? t.labels.bannedHint ?? t.labels.hint : t.labels.hint, "#556f80")
					}, null, 8, ["revision", "draw"])], 8, ze), o("span", {
						class: "ban-pick-total",
						"aria-label": `${t.labels.selected} ${t.modelValue.length}`
					}, [s(Y, {
						onError: c[1] ||= (e) => d("error", e),
						revision: [
							t.labels.selected,
							t.locale,
							t.textScale
						],
						draw: () => $(t.labels.selected, "#456779")
					}, null, 8, ["revision", "draw"]), s(Y, {
						onError: c[2] ||= (e) => d("error", e),
						revision: [t.modelValue.length, t.textScale],
						draw: () => $(String(t.modelValue.length), "#dc7a08", 17, 22)
					}, null, 8, ["revision", "draw"])], 8, Be)]),
					o("div", Ve, [o("div", {
						class: "ban-pick-tabs",
						role: "tablist",
						"aria-label": t.labels.title
					}, [(g(), a(e, null, b(h, (e) => o("button", {
						id: `ban-pick-tab-${e}`,
						key: e,
						type: "button",
						role: "tab",
						style: p({
							width: t.appearance.categorySizes[e].width + "px",
							height: t.appearance.categorySizes[e].height + "px"
						}),
						"aria-selected": x.value === e,
						"aria-controls": "ban-pick-items",
						class: f({ active: x.value === e }),
						onClick: (t) => x.value = e
					}, [
						o("img", {
							src: Gt(t.appearance.categorySkins[e], x.value === e),
							alt: "",
							draggable: "false"
						}, null, 8, We),
						s(Y, {
							onError: c[3] ||= (e) => d("error", e),
							revision: [
								t.labels.categories[e],
								t.locale,
								t.textScale
							],
							draw: () => rn(e)
						}, null, 8, ["revision", "draw"]),
						Kt(e) ? (g(), a("span", Ge, [s(Y, {
							onError: c[4] ||= (e) => d("error", e),
							revision: [Kt(e), t.textScale],
							draw: () => $(String(Kt(e)), "#5f4600", 11, 13)
						}, null, 8, ["revision", "draw"])])) : i("v-if", !0)
					], 14, Ue)), 64))], 8, He), o("div", Ke, [t.readonly ? i("v-if", !0) : (g(), a("label", {
						key: 0,
						class: "ban-pick-only",
						style: p({ width: Math.max(112, Q(t.labels.restrictedOnly) - 8) + "px" })
					}, [s(ye, {
						onError: c[5] ||= (e) => d("error", e),
						type: "UiCheckBox",
						attrs: Wt.value,
						onChange: c[6] ||= (e) => M.value = !!e.value
					}, null, 8, ["attrs"]), s(Y, {
						onError: c[7] ||= (e) => d("error", e),
						revision: [
							t.labels.restrictedOnly,
							t.locale,
							t.textScale
						],
						draw: on
					}, null, 8, ["revision"])], 4)), o("span", qe, [k(o("input", {
						"onUpdate:modelValue": c[8] ||= (e) => w.value = e,
						type: "search",
						"aria-label": t.labels.search
					}, null, 8, Je), [[E, w.value]]), w.value ? i("v-if", !0) : (g(), a("span", Ye, [s(Y, {
						onError: c[9] ||= (e) => d("error", e),
						revision: [
							t.labels.search,
							t.locale,
							t.textScale
						],
						draw: () => $(t.labels.search, "#9aa6ae")
					}, null, 8, ["revision", "draw"])]))])])]),
					o("div", Xe, [s(ye, {
						onError: c[10] ||= (e) => d("error", e),
						type: "UiPanel",
						attrs: {
							Name: "pnlSubTabBack",
							PosX: "0",
							PosY: "0",
							Width: "746",
							Height: "30",
							SkinSpriteSet: "Images/Shop2/sub_category/store_tab_base.smf"
						}
					}), o("div", Ze, [o("button", {
						type: "button",
						class: f({ active: S.value === "all" }),
						"aria-pressed": S.value === "all",
						onClick: c[12] ||= (e) => S.value = "all"
					}, [o("img", {
						src: Gt(t.appearance.subCategorySkins.all, S.value === "all"),
						alt: ""
					}, null, 8, $e), s(Y, {
						onError: c[11] ||= (e) => d("error", e),
						revision: [
							t.labels.all,
							S.value === "all",
							t.locale,
							t.textScale
						],
						draw: () => $(t.labels.all, S.value === "all" ? "#ffffff" : "#a8bde0")
					}, null, 8, ["revision", "draw"])], 10, Qe), (g(!0), a(e, null, b(ve.value, (e) => (g(), a("button", {
						key: e,
						type: "button",
						class: f({ active: S.value === e }),
						"aria-pressed": S.value === e,
						onClick: (t) => S.value = e
					}, [t.appearance.subCategorySkins[e] ? (g(), a("img", {
						key: 0,
						src: Gt(t.appearance.subCategorySkins[e], S.value === e),
						alt: ""
					}, null, 8, tt)) : i("v-if", !0), s(Y, {
						onError: c[13] ||= (e) => d("error", e),
						revision: [
							t.labels.subCategories[e] ?? e,
							S.value === e,
							t.locale,
							t.textScale
						],
						draw: () => $(t.labels.subCategories[e] ?? e, S.value === e ? "#ffffff" : "#a8bde0")
					}, null, 8, ["revision", "draw"])], 10, et))), 128))])]),
					o("div", {
						id: "ban-pick-items",
						class: "ban-pick-content",
						role: "tabpanel",
						"aria-labelledby": `ban-pick-tab-${x.value}`
					}, [
						s(ye, {
							onError: c[14] ||= (e) => d("error", e),
							type: "UiPanel",
							attrs: {
								Name: "inventoryBack",
								PosX: "0",
								PosY: "0",
								Width: "746",
								Height: "386",
								SkinSpriteSet: "Images/Ui/DialogInnerSkin03.smf"
							}
						}),
						o("div", rt, [(g(!0), a(e, null, b(xe.value, (e) => (g(), a("button", {
							key: e.id,
							type: "button",
							class: f(["ban-pick-card", { banned: J.value.has(e.id) }]),
							"aria-pressed": J.value.has(e.id),
							"aria-disabled": t.readonly,
							"aria-label": `${e.name}: ${J.value.has(e.id) ? t.labels.banned : t.labels.allowed}`,
							onMouseenter: (t) => N.value = e.id,
							onFocus: (t) => N.value = e.id,
							onClick: (t) => Jt(e.id),
							onMouseenterPassive: (t) => ae(t, e),
							onMousemovePassive: (t) => ae(t, e),
							onMouseleave: c[19] ||= (e) => K.value = void 0
						}, [
							o("img", {
								class: "ban-pick-card-frame",
								src: Ut(),
								alt: "",
								draggable: "false"
							}, null, 8, at),
							s(Y, {
								onError: c[15] ||= (e) => d("error", e),
								revision: [
									e.name,
									t.locale,
									t.textScale
								],
								draw: () => tn(e)
							}, null, 8, ["revision", "draw"]),
							o("span", ot, [typeof e.preview == "string" ? (g(), a("img", {
								key: 0,
								src: e.preview,
								alt: "",
								draggable: "false"
							}, null, 8, st)) : e.preview ? (g(), a("span", {
								key: 1,
								class: "ban-pick-sprite",
								style: p(qt(e.preview))
							}, null, 4)) : (g(), a("span", ct, [t.labels.noPreview ? (g(), r(Y, {
								key: 0,
								class: "ban-pick-inline",
								onError: c[16] ||= (e) => d("error", e),
								revision: [
									t.labels.noPreview,
									t.locale,
									t.textScale
								],
								draw: () => $(t.labels.noPreview ?? "", "#e8edf0")
							}, null, 8, ["revision", "draw"])) : i("v-if", !0)]))]),
							J.value.has(e.id) ? (g(), a("img", {
								key: 0,
								class: "ban-pick-use-frame",
								src: Vt("Images/Shop2/inventory_box/inventory_use_frame.png"),
								alt: "",
								draggable: "false"
							}, null, 8, lt)) : i("v-if", !0),
							J.value.has(e.id) ? (g(), a("span", ut, [s(Y, {
								onError: c[17] ||= (e) => d("error", e),
								revision: [
									t.labels.banned,
									t.locale,
									t.textScale
								],
								draw: () => $(t.labels.banned, "#ffe428", 12, 18, "#705d00")
							}, null, 8, ["revision", "draw"])])) : i("v-if", !0),
							s(Y, {
								onError: c[18] ||= (e) => d("error", e),
								revision: [
									J.value.has(e.id),
									t.labels.banned,
									t.labels.allowed,
									t.locale,
									t.textScale
								],
								draw: () => nn(e)
							}, null, 8, ["revision", "draw"])
						], 42, it))), 128))]),
						xe.value.length ? i("v-if", !0) : (g(), a("p", dt, [s(Y, {
							onError: c[20] ||= (e) => d("error", e),
							revision: [
								t.labels.empty,
								t.locale,
								t.textScale
							],
							draw: () => $(t.labels.empty, "#ffffff", 13, 20)
						}, null, 8, ["revision", "draw"])])),
						o("div", ft, [
							o("button", {
								type: "button",
								class: "page-previous",
								disabled: A.value === 0,
								"aria-label": t.labels.previous,
								onClick: c[21] ||= (e) => A.value--
							}, [o("img", {
								src: Vt("Images/Ui/ScrollbarHorizon/12.png"),
								alt: ""
							}, null, 8, mt), o("img", {
								class: "page-hover",
								src: Vt("Images/Ui/ScrollbarHorizon/14.png"),
								alt: ""
							}, null, 8, ht)], 8, pt),
							o("span", {
								ref_key: "pageTrack",
								ref: B,
								class: "page-track",
								onPointerdown: he
							}, [o("span", {
								class: "page-thumb",
								role: "slider",
								tabindex: "0",
								"aria-label": t.labels.page,
								"aria-valuemin": 1,
								"aria-valuemax": X.value,
								"aria-valuenow": A.value + 1,
								onKeydown: ge,
								onPointerdown: j(pe, ["stop"]),
								onPointermove: me,
								onPointerup: c[23] ||= (e) => u(q) ? q.value = void 0 : q = void 0,
								onPointercancel: c[24] ||= (e) => u(q) ? q.value = void 0 : q = void 0,
								onLostpointercapture: c[25] ||= (e) => u(q) ? q.value = void 0 : q = void 0,
								style: p({
									width: `${100 / X.value}%`,
									left: X.value > 1 ? `calc((100% - max(58px, ${100 / X.value}%)) * ${A.value / (X.value - 1)})` : "0"
								})
							}, [
								o("img", {
									src: Vt("Images/Ui/ScrollbarHorizon/3.png"),
									alt: "",
									draggable: "false"
								}, null, 8, _t),
								o("img", {
									src: Vt("Images/Ui/ScrollbarHorizon/4.png"),
									alt: "",
									draggable: "false"
								}, null, 8, vt),
								o("img", {
									src: Vt("Images/Ui/ScrollbarHorizon/5.png"),
									alt: "",
									draggable: "false"
								}, null, 8, yt),
								o("span", null, [s(Y, {
									onError: c[22] ||= (e) => d("error", e),
									revision: [
										A.value,
										X.value,
										t.textScale
									],
									draw: () => $(`${A.value + 1} / ${X.value}`, "#404c53")
								}, null, 8, ["revision", "draw"])])
							], 44, gt)], 544),
							o("button", {
								type: "button",
								class: "page-next",
								disabled: A.value + 1 >= X.value,
								"aria-label": t.labels.next,
								onClick: c[26] ||= (e) => A.value++
							}, [o("img", {
								src: Vt("Images/Ui/ScrollbarHorizon/15.png"),
								alt: ""
							}, null, 8, xt), o("img", {
								class: "page-hover",
								src: Vt("Images/Ui/ScrollbarHorizon/17.png"),
								alt: ""
							}, null, 8, St)], 8, bt)
						])
					], 8, nt),
					o("div", Ct, [o("section", {
						class: "ban-pick-info",
						style: p({ height: t.appearance.infoSize.height + "px" }),
						"aria-label": Ht.value?.name
					}, [s(Y, {
						onError: c[27] ||= (e) => d("error", e),
						revision: [
							Ht.value,
							t.labels.noDescription,
							t.locale,
							t.textScale
						],
						draw: Zt
					}, null, 8, ["revision"])], 12, wt)]),
					o("div", Tt, [t.readonly ? i("v-if", !0) : (g(), a(e, { key: 0 }, [o("div", {
						class: "native-action",
						style: p({ width: Q(t.labels.reset) + "px" })
					}, [s(Y, {
						onError: c[28] ||= (e) => d("error", e),
						revision: [t.textScale, t.labels.reset],
						draw: (e) => sn(e, t.labels.reset, Q(t.labels.reset), !1, "Ok"),
						onAction: Yt
					}, null, 8, ["revision", "draw"])], 4), o("div", {
						class: "native-action",
						style: p({ width: Q(t.labels.presets) + "px" })
					}, [s(Y, {
						onError: c[29] ||= (e) => d("error", e),
						revision: [t.textScale, t.labels.presets],
						draw: (e) => sn(e, t.labels.presets, Q(t.labels.presets)),
						onAction: c[30] ||= (e) => I.value = !0
					}, null, 8, ["revision", "draw"])], 4)], 64)), o("div", null, [t.readonly ? i("v-if", !0) : (g(), a("div", {
						key: 0,
						class: "native-action",
						style: p({ width: Q(t.labels.apply) + "px" })
					}, [s(Y, {
						onError: c[31] ||= (e) => d("error", e),
						revision: [t.textScale, t.labels.apply],
						draw: (e) => sn(e, t.labels.apply, Q(t.labels.apply)),
						onAction: c[32] ||= (e) => d("apply", [...t.modelValue])
					}, null, 8, ["revision", "draw"])], 4)), o("div", {
						class: "native-action",
						style: p({ width: Q(t.readonly ? t.labels.close : t.labels.cancel) + "px" })
					}, [s(Y, {
						onError: c[33] ||= (e) => d("error", e),
						revision: [
							t.textScale,
							t.readonly,
							t.labels.cancel,
							t.labels.close
						],
						draw: (e) => sn(e, t.readonly ? t.labels.close : t.labels.cancel, Q(t.readonly ? t.labels.close : t.labels.cancel), !1, "Cancel"),
						onAction: c[34] ||= (e) => d("cancel")
					}, null, 8, ["revision", "draw"])], 4)])]),
					ne.value ? (g(), a("div", {
						key: 0,
						class: "ban-pick-spec",
						style: p({
							...oe.value,
							width: ie.value.width + "px",
							height: ie.value.height + "px"
						})
					}, [s(Y, {
						onError: c[35] ||= (e) => d("error", e),
						revision: [
							ne.value.id,
							re.value,
							t.labels.stats,
							t.locale,
							t.textScale
						],
						draw: de
					}, null, 8, ["revision"])], 4)) : i("v-if", !0)
				], 14, Le)]),
				_: 1
			}, 8, [
				"title",
				"title-align",
				"font-family",
				"title-bounds",
				"resource-root",
				"close-label",
				"height",
				"text-scale"
			]),
			i(" showModal() lifts the dialog into the top layer; staying in place keeps the overlay panel's shadow-root styles. "),
			t.readonly ? i("v-if", !0) : (g(), a("dialog", {
				key: 0,
				ref_key: "presetDialog",
				ref: R,
				class: "ban-pick-preset-dialog",
				"data-theme": t.appearance.theme,
				"aria-label": t.labels.presets,
				onCancel: c[48] ||= j((e) => I.value = !1, ["prevent"]),
				onClose: c[49] ||= (e) => I.value = !1,
				style: p({ "--action-height": t.appearance.actionHeight + "px" })
			}, [s(_e, {
				onError: c[46] ||= (e) => d("error", e),
				title: t.labels.presets,
				"title-align": t.appearance.theme === "v1" ? "left" : "center",
				"font-family": t.locale === "ko" ? "BFKorean" : "BF",
				"font-weight": 600,
				"title-bounds": t.appearance.titleBounds,
				"resource-root": t.resourceRoot,
				"text-renderer": "freetype",
				"text-scale": t.textScale,
				width: 320,
				height: 205,
				"close-label": t.labels.close,
				onClose: c[47] ||= (e) => I.value = !1
			}, {
				default: O(() => [o("section", {
					class: "ban-pick-presets",
					lang: t.locale
				}, [
					k(o("select", {
						"onUpdate:modelValue": c[38] ||= (e) => F.value = e,
						"aria-label": t.labels.choosePreset
					}, [o("option", kt, C(t.labels.choosePreset), 1), (g(!0), a(e, null, b(t.presets, (e) => (g(), a("option", {
						key: e.id,
						value: e.id
					}, C(e.name), 9, At))), 128))], 8, Ot), [[T, F.value]]),
					o("span", jt, [k(o("input", {
						"onUpdate:modelValue": c[39] ||= (e) => P.value = e,
						"aria-label": t.labels.presetName
					}, null, 8, Mt), [[E, P.value]]), P.value ? i("v-if", !0) : (g(), a("span", Nt, [s(Y, {
						onError: c[40] ||= (e) => d("error", e),
						revision: [
							t.labels.presetName,
							t.locale,
							t.textScale
						],
						draw: () => $(t.labels.presetName, "#9aa6ae")
					}, null, 8, ["revision", "draw"])]))]),
					o("div", Pt, [
						o("div", {
							class: "native-action",
							style: p({ width: Q(t.labels.savePreset) + "px" })
						}, [s(Y, {
							onError: c[41] ||= (e) => d("error", e),
							revision: [
								t.textScale,
								t.labels.savePreset,
								!P.value.trim()
							],
							draw: (e) => sn(e, t.labels.savePreset, Q(t.labels.savePreset), !P.value.trim()),
							onAction: c[42] ||= (e) => d("save-preset", P.value.trim(), [...t.modelValue])
						}, null, 8, ["revision", "draw"])], 4),
						o("div", {
							class: "native-action",
							style: p({ width: Q(t.labels.loadPreset) + "px" })
						}, [s(Y, {
							onError: c[43] ||= (e) => d("error", e),
							revision: [
								t.textScale,
								t.labels.loadPreset,
								!F.value
							],
							draw: (e) => sn(e, t.labels.loadPreset, Q(t.labels.loadPreset), !F.value),
							onAction: Xt
						}, null, 8, ["revision", "draw"])], 4),
						o("div", {
							class: "native-action",
							style: p({ width: Q(t.labels.deletePreset) + "px" })
						}, [s(Y, {
							onError: c[44] ||= (e) => d("error", e),
							revision: [
								t.textScale,
								t.labels.deletePreset,
								!F.value
							],
							draw: (e) => sn(e, t.labels.deletePreset, Q(t.labels.deletePreset), !F.value, "Cancel"),
							onAction: c[45] ||= (e) => d("delete-preset", F.value)
						}, null, 8, ["revision", "draw"])], 4)
					])
				], 8, Dt)]),
				_: 1
			}, 8, [
				"title",
				"title-align",
				"font-family",
				"title-bounds",
				"resource-root",
				"text-scale",
				"close-label"
			])], 44, Et))
		], 64));
	}
}), [["__scopeId", "data-v-f5244219"]]), Rt = /* @__PURE__ */ c({
	__name: "BfReveal",
	props: {
		limit: { default: 8e3 },
		strict: {
			type: Boolean,
			default: !1
		}
	},
	emits: ["ready"],
	setup(e, { emit: t }) {
		let n = e, r = t, i = fe(), o = y(!1), s;
		function c() {
			o.value || (o.value = !0, r("ready"));
		}
		function l() {
			o.value || (clearTimeout(s), s = setTimeout(() => {
				(i?.pending.value ?? 0) === 0 && c();
			}, 50));
		}
		D(() => i?.pending.value ?? 0, l, { immediate: !0 });
		let u = setTimeout(() => {
			n.strict || c();
		}, n.limit);
		return m(() => {
			clearTimeout(s), clearTimeout(u);
		}), (e, t) => (g(), a("div", {
			class: "bf-reveal",
			style: p({
				display: "contents",
				visibility: o.value ? "visible" : "hidden"
			})
		}, [x(e.$slots, "default")], 4));
	}
}), zt = Object.freeze({
	Pvp: "Noitem",
	Item: "Item",
	PvpStarTeam: "StarTeam",
	Zombie: "Zombie",
	EventSoccer: "Soccer",
	Sniper: "Sniper",
	SinglePvp: "Survival",
	TeamSurvival: "Survival",
	ZombiePve: "ZombieBoss",
	SniperTeamSurvival: "SurvivalSna",
	Bomb: "Demolition",
	Captain: "Captain",
	BfRunner: "CrazyRacing",
	Area32: "BoomhillWorld",
	ExtremeRacing: "LetsRun",
	SuddenSurvival: "Area32",
	WeaponMaster: "WeaponMaster",
	Snowfight: "WaterBomb",
	ZombieGhost: "ZombieGhost",
	ControlPoint: "ControlPoint",
	FinalZone: "FinalZone",
	Shotgun: "Shot",
	ZombieEscape: "Escape",
	AllPang: "AllPang",
	JumpJump: "Jump",
	Psionic: "Psionic",
	NoItemCustomAiWar: "CustomAI",
	TeamSurvivalCustomAiWar: "TeamSurvivalCustomAiWar",
	BnB: "BnB",
	Horde: "Pirates",
	PirateAssaultSeason3: "PiratesAttack",
	NoItemPractice: "Noitem",
	NoItemTutorial: "CustomAI",
	ZombieOrigin: "ZombieGhost",
	SimpleSuddenSurvival: "Area32",
	ZombieSurvival: "Escape"
}), Bt = Object.freeze({
	v1: { Zombie: "ZombieGhost" },
	v2: { Zombie: "ZombieGhost" },
	v3: {}
});
function Vt(e, t, n = "v3") {
	let r = t[Bt[n]?.[e] ?? zt[e] ?? "Noitem"] ?? t.Noitem;
	if (!r || r.states.length !== 4) throw Error("Four competitive-mode button states are required.");
	return r;
}
function Ht(e) {
	let t = /* @__PURE__ */ new Set();
	for (let n of e) {
		if (!Number.isSafeInteger(n.id) || n.id < 0 || t.has(n.id) || typeof n.key != "string" || !n.key || !n.names || typeof n.names.en != "string") throw Error("Mode catalogue entries require unique integer IDs and localized names.");
		t.add(n.id);
	}
	return e;
}
function Ut(e, t) {
	return e.find((e) => e.id === t);
}
function Wt(e, t) {
	let n = Ut(Ht(e), t)?.id;
	return {
		modes: e,
		committedId: n,
		draftId: n,
		select(e) {
			if (!Ut(this.modes, e)) throw Error("The selected mode is not in the catalogue.");
			this.draftId = e;
		},
		reset(e) {
			this.committedId = Ut(this.modes, e)?.id, this.cancel();
		},
		replaceModes(e) {
			this.modes = Ht(e), this.committedId = Ut(e, this.committedId)?.id, this.draftId = Ut(e, this.draftId)?.id;
		},
		confirm() {
			return Ut(this.modes, this.draftId);
		},
		cancel() {
			this.draftId = this.committedId;
		}
	};
}
function Gt(e, t, n, r, i = 24) {
	let a = Math.max(0, e - t), o = a ? Math.min(r, Math.max(i, r * t / e)) : r, s = r - o;
	return {
		maximum: a,
		height: o,
		travel: s,
		top: a ? Math.max(0, Math.min(a, n)) / a * s : 0
	};
}
function Kt(e, t, n) {
	return n.travel > 0 ? Math.max(0, Math.min(n.maximum, e + t * n.maximum / n.travel)) : 0;
}
function qt({ getTop: e, setTop: t, getGeometry: n, getScale: r }) {
	let i;
	function a(e) {
		i && e.pointerId === i.pointer && (e.preventDefault(), t(Kt(i.top, (e.clientY - i.y) / i.scale, n())));
	}
	function o(e = void 0) {
		if (!i || e && e.pointerId !== i.pointer) return;
		let t = i;
		i = void 0, t.document.removeEventListener("pointermove", a, !0), t.document.removeEventListener("pointerup", o, !0), t.document.removeEventListener("pointercancel", o, !0), t.element.removeEventListener("lostpointercapture", o), t.element.hasPointerCapture(t.pointer) && t.element.releasePointerCapture(t.pointer);
	}
	function s(t) {
		if (t.button !== 0 || !n().maximum) return;
		o();
		let s = t.currentTarget, c = r();
		if (!(c > 0) || !Number.isFinite(c)) throw Error("A scroll drag requires a visible native track.");
		i = {
			pointer: t.pointerId,
			y: t.clientY,
			top: e(),
			scale: c,
			element: s,
			document: s.ownerDocument
		}, i.document.addEventListener("pointermove", a, !0), i.document.addEventListener("pointerup", o, !0), i.document.addEventListener("pointercancel", o, !0), s.addEventListener("lostpointercapture", o), t.preventDefault(), t.stopPropagation();
		try {
			s.setPointerCapture(t.pointerId);
		} catch (e) {
			throw o(), e;
		}
	}
	return {
		start: s,
		stop: o
	};
}
function Jt(e, t) {
	if (Ht(t), e?.format !== 1 || !Array.isArray(e.rules)) throw Error("Mode availability requires format 1 and a rules array.");
	let n = new Set(t.map((e) => e.id));
	for (let t of e.rules) {
		if (!t || !Array.isArray(t.builds) || !t.builds.length || t.builds.some((e) => typeof e != "string" || !/^[A-Za-z0-9*_.-]+$/.test(e))) throw Error("Each mode-availability rule requires build IDs or wildcard patterns.");
		for (let e of ["allow", "hidden"]) if (t[e] !== void 0 && (!Array.isArray(t[e]) || new Set(t[e]).size !== t[e].length || t[e].some((e) => !Number.isSafeInteger(e) || !n.has(e)))) throw Error(`Mode availability ${e} must contain distinct catalogue IDs.`);
		if (t.allow === void 0 && t.hidden === void 0) throw Error("A mode-availability rule requires allow or hidden IDs.");
	}
	return e;
}
function Yt(e, t, n) {
	if (Jt(t, e), !t.rules.length) return e;
	if (typeof n != "string" || !n) throw Error("Build-specific mode availability requires the current build ID.");
	let r = (e) => RegExp("^" + e.split("*").map((e) => e.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join(".*") + "$", "i").test(n), i = e;
	for (let e of t.rules) {
		if (!e.builds.some(r)) continue;
		let t = e.allow === void 0 ? void 0 : new Set(e.allow), n = new Set(e.hidden ?? []);
		i = i.filter((e) => (!t || t.has(e.id)) && !n.has(e.id));
	}
	return i;
}
function Xt(e) {
	if (![
		"v1",
		"v2",
		"v3"
	].includes(e)) throw Error("Unknown mode window theme.");
	return {
		width: 602,
		height: e === "v3" ? 516 : 496,
		actionsTop: e === "v3" ? 464 : 448,
		actionsHeight: e === "v3" ? 33 : 42
	};
}
// ../tools/game-ui/src/components/BfModeExtension.vue?vue&type=script&setup=true&lang.ts
var Zt = ["inert", "aria-hidden"], Qt = ["aria-busy"], $t = { class: "bf-mode-panel" }, Q = ["aria-label"], en = { class: "bf-mode-grid" }, tn = ["aria-label"], nn = ["src"], rn = ["src"], an = [
	"aria-label",
	"aria-valuemax",
	"aria-valuenow"
], $ = ["src"], on = ["src"], sn = ["src"], cn = ["aria-label"], ln = ["src"], un = 602, dn = 384, fn = 65, pn = /* @__PURE__ */ c({
	__name: "BfModeExtension",
	props: {
		modes: {},
		selectedId: {},
		labels: {},
		appearance: {},
		resourceRoot: {},
		modeResourceRoot: {},
		theme: {},
		locale: { default: "zh-CN" },
		textScale: { default: 1 },
		busy: {
			type: Boolean,
			default: !1
		},
		actionSkins: {}
	},
	emits: [
		"confirm",
		"cancel",
		"select",
		"error"
	],
	setup(t, { emit: i }) {
		let c = t, l = i, u = v(Wt(c.modes, c.selectedId)), f = y(!1), _ = y(!1), x = n(() => f.value && _.value), S = y(), C = y(), T = y(), E = y(0), k = y(65), M = y(27), N = me(), P = n(() => u.modes), F = n(() => Xt(c.theme)), I = n(() => F.value.height), R = n(() => Math.max(0, Math.ceil(P.value.length / 3) * fn - 6)), z = n(() => c.theme === "v3" ? 18 : 20), B = n(() => dn - 2 * z.value), V = n(() => Gt(R.value, dn, E.value, B.value)), H = n(() => !!u.confirm()), U = n(() => [
			c.locale,
			c.textScale,
			c.appearance,
			c.theme,
			c.modes,
			c.modeResourceRoot
		]), ee = n(() => [
			c.locale,
			c.theme,
			H.value,
			c.busy,
			c.textScale
		]), W = n(() => [
			c.locale,
			c.theme,
			c.busy,
			c.textScale
		]), G = n(() => c.locale === "ko" ? "BFKorean" : "BF"), te = n(() => c.theme === "v1" ? {
			top: 8,
			height: 23
		} : c.theme === "v2" ? {
			top: 6,
			height: 24
		} : {
			top: 8,
			height: 27
		}), K = n(() => c.resourceRoot.replace(/\/?$/, "/")), ne = n(() => (c.modeResourceRoot ?? c.resourceRoot).replace(/\/?$/, "/")), re = n(() => "Images/Ui/" + (c.theme === "v3" ? "dialogueNew/" : "") + "ScrollbarVertical");
		function ie(e) {
			if (!N.value) return;
			let t = N.value.manifest.imageFormat === "webp" ? "webp" : "png";
			return `${K.value}${c.theme}/${re.value}/${e}.${t}`;
		}
		function ae(e) {
			l("cancel"), l("error", e);
		}
		let oe = fe(), se = 0, ce = !1;
		function le() {
			ce && oe && (oe.pending.value--, ce = !1);
		}
		D(() => [
			T.value,
			c.theme,
			K.value,
			N.value,
			N.value?.manifest.imageFormat
		], async () => {
			let e = ++se, t = T.value;
			if (_.value = !1, !t || !N.value) le();
			else {
				oe && !ce && (oe.pending.value++, ce = !0);
				try {
					if (await Promise.all([...t.querySelectorAll("img")].map((e) => e.decode())), e !== se) return;
					_.value = !0, le();
				} catch (t) {
					if (e !== se) return;
					le(), ae(t instanceof Error ? t : Error(String(t)));
				}
			}
		}, { flush: "post" }), m(() => {
			se++, le();
		});
		function ue(e) {
			return e.names[c.locale] ?? e.names.en ?? e.key;
		}
		function de(e) {
			return e.descriptions?.[c.locale] ?? c.labels.hidden;
		}
		function q(e) {
			c.busy || (u.select(e.id), l("select", e.id));
		}
		function pe() {
			let e = u.confirm();
			e && !c.busy && l("confirm", e);
		}
		function he() {
			c.busy || (u.cancel(), l("cancel"));
		}
		function ge(e) {
			S.value && (S.value.scrollTop = e, ve());
		}
		function J(e) {
			ge((S.value?.scrollTop ?? 0) + e);
		}
		function ve() {
			E.value = S.value?.scrollTop ?? 0;
		}
		function ye(e) {
			J((e.offsetY < V.value.top ? -1 : 1) * dn);
		}
		D(() => c.selectedId, (e) => {
			u.reset(e);
		}), D(() => c.modes, (e) => {
			u.replaceModes(e), d(ve);
		}), D(() => [u.draftId, c.busy], () => {
			for (let e of S.value?.querySelectorAll("[data-mode-id]") ?? []) e.setAttribute("aria-checked", String(Number(e.dataset.modeId) === u.draftId)), e.disabled = c.busy;
		});
		let be = qt({
			getTop: () => S.value?.scrollTop ?? 0,
			setTop: ge,
			getGeometry: () => V.value,
			getScale: () => (C.value?.getBoundingClientRect().height ?? 0) / B.value
		});
		m(() => be.stop()), D(() => c.theme, () => be.stop());
		function X(e) {
			if (e.key === "Escape") {
				e.preventDefault(), he();
				return;
			}
			if (c.busy || !e.target.closest("[data-mode-id]")) return;
			let t = P.value.findIndex((e) => e.id === u.draftId), n = {
				ArrowLeft: -1,
				ArrowRight: 1,
				ArrowUp: -3,
				ArrowDown: 3,
				PageUp: -18,
				PageDown: 18
			};
			if (!(e.key in n)) return;
			e.preventDefault();
			let r = P.value[Math.max(0, Math.min(P.value.length - 1, (t < 0 ? 0 : t) + n[e.key]))];
			r && (q(r), d(() => {
				let e = S.value?.querySelector(`[data-mode-id="${r.id}"]`);
				e?.focus({ preventScroll: !0 }), e?.scrollIntoView({ block: "nearest" });
			}));
		}
		async function xe(e) {
			let t = document.createElement("div");
			return await e.panel(t, 0, 0, 580, 402, c.theme === "v3" ? "Images/Ui/dialogueNew/dialogue_basic02.smf" : "Images/Ui/FrameBox.smf"), t;
		}
		async function Se(e, t) {
			let n = Vt(t.key, c.appearance.skins, c.theme), r = document.createElement("button");
			r.type = "button", r.className = "bf-mode-button", r.dataset.modeId = String(t.id), r.setAttribute("role", "radio"), r.dataset.state = "0", r.ariaLabel = ue(t), r.dataset.bfPressSound = "toggleButtonOn";
			let i = await Promise.all(n.states.map(async (e) => {
				let t = new Image();
				return t.crossOrigin = "anonymous", t.src = ne.value + e, t.alt = "", t.draggable = !1, await t.decode(), t;
			}));
			r.append(...i);
			let a = document.createElement("span");
			a.className = "bf-mode-name", r.append(a), await L(a, ue(t), {
				family: G.value,
				size: 15,
				weight: 700,
				align: "left",
				vertical: "middle",
				color: "#232323",
				decoration: 0,
				renderer: "freetype",
				resourceRoot: c.resourceRoot,
				pixelRatio: c.textScale,
				centerInk: !0,
				inset: 0,
				wrap: !1,
				width: 130,
				height: 20
			});
			let o = document.createElement("span");
			o.className = "bf-mode-description", r.append(o), await L(o, de(t), {
				family: G.value,
				size: 11,
				weight: 400,
				align: "left",
				vertical: "middle",
				color: "#ffffff",
				decoration: 0,
				renderer: "freetype",
				resourceRoot: c.resourceRoot,
				pixelRatio: c.textScale,
				centerInk: !0,
				inset: 0,
				wrap: !1,
				width: 140,
				height: 16
			}), r.setAttribute("aria-checked", String(t.id === u.draftId)), r.disabled = c.busy;
			for (let [e, t] of [
				["pointerenter", 2],
				["pointerleave", 0],
				["pointerdown", 1],
				["pointerup", 2],
				["pointercancel", 0]
			]) r.addEventListener(e, () => {
				r.dataset.state = String(t);
			});
			return r.addEventListener("click", () => q(t)), r;
		}
		async function Z(e, t) {
			let n = t === "Ok" ? c.labels.confirm : c.labels.cancel;
			if (c.theme !== "v3") {
				let r = c.actionSkins?.[t];
				if (!r) throw Error("Native confirmation and cancellation skins are required.");
				let i = await Ne([c.labels.confirm, c.labels.cancel], G.value), { height: a } = Ae(e, {
					skins: [...r],
					width: i,
					height: 27
				});
				k.value = i, M.value = a;
				let o = await Fe(e, {
					label: n,
					width: i,
					height: a,
					skins: [...r],
					kind: t,
					disabled: c.busy || t === "Ok" && !H.value,
					family: G.value,
					size: 13,
					renderer: "freetype",
					resourceRoot: c.resourceRoot,
					pixelRatio: c.textScale
				});
				return o.dataset.action = t, o.dataset.bfPressSound = t === "Cancel" ? "cancel" : "press", o;
			}
			let r = document.createElement("button");
			r.type = "button", r.className = "control sprite-button", r.style.cssText = "left:0;top:0;width:112px;height:33px", r.ariaLabel = n, r.dataset.action = t, r.disabled = c.busy || t === "Ok" && !H.value, r.dataset.bfPressSound = t === "Cancel" ? "cancel" : "press";
			let i = t === "Ok" ? "dialogue_type01BTN" : "dialogue_type02BTN", a = [];
			for (let t of [
				"Up",
				"Down",
				"Hover",
				"Disabled"
			]) a.push(await e.panel(r, 0, 0, 112, 33, `Images/Ui/dialogueNew/${i}-${t}.smf`));
			let o = document.createElement("span");
			o.style.cssText = "position:absolute;inset:0;width:112px;height:33px;pointer-events:none", r.append(o), await L(o, n, {
				family: G.value,
				size: 15,
				weight: 400,
				color: "#232323",
				decoration: 0,
				align: "center",
				vertical: "middle",
				centerInk: !0,
				renderer: "freetype",
				resourceRoot: c.resourceRoot,
				pixelRatio: c.textScale
			});
			let s = (e) => a.forEach((t, n) => {
				t.style.visibility = n === (r.disabled ? 3 : e) ? "visible" : "hidden", t.style.pointerEvents = "none";
			});
			s(0);
			for (let [e, t] of [
				["pointerenter", 2],
				["pointerleave", 0],
				["pointerdown", 1],
				["pointerup", 2]
			]) r.addEventListener(e, () => s(t));
			return r.addEventListener("click", () => {
				r.disabled || r.dispatchEvent(new CustomEvent("bf-action", {
					bubbles: !0,
					detail: { name: t }
				}));
			}), r;
		}
		return h(() => {
			d(ve);
		}), (n, i) => (g(), r(Rt, {
			strict: "",
			onReady: i[7] ||= (e) => f.value = !0
		}, {
			default: O(() => [o("div", {
				class: "bf-mode-reveal",
				inert: !x.value,
				"aria-hidden": !x.value,
				style: p({
					width: "602px",
					height: I.value + "px",
					opacity: +!!x.value
				})
			}, [s(_e, {
				title: t.labels.title,
				width: un,
				height: I.value,
				"close-label": t.labels.close,
				"title-align": t.theme === "v1" ? "left" : "center",
				"font-family": G.value,
				"font-weight": 600,
				"text-renderer": "freetype",
				"text-scale": t.textScale,
				"resource-root": t.resourceRoot,
				"title-bounds": te.value,
				onClose: he,
				onError: ae
			}, {
				default: O(() => [o("section", {
					class: "bf-mode-content",
					"aria-busy": t.busy,
					onKeydown: X
				}, [
					o("div", $t, [s(Y, {
						draw: xe,
						revision: t.theme,
						onError: ae
					}, null, 8, ["revision"])]),
					o("div", {
						ref_key: "viewport",
						ref: S,
						class: "bf-mode-viewport",
						role: "radiogroup",
						"aria-label": t.labels.title,
						onScroll: ve
					}, [o("div", en, [(g(!0), a(e, null, b(P.value, (e) => (g(), r(Y, {
						key: e.id,
						draw: (t) => Se(t, e),
						revision: U.value,
						onError: ae
					}, null, 8, ["draw", "revision"]))), 128))])], 40, Q),
					o("div", {
						ref_key: "scrollbar",
						ref: T,
						class: "bf-mode-scrollbar",
						style: p({
							"--scroll-width": (t.theme === "v3" ? 18 : 21) + "px",
							"--arrow-height": z.value + "px"
						})
					}, [
						o("button", {
							class: "bf-mode-scroll-arrow",
							type: "button",
							"aria-label": t.labels.previous,
							onClick: i[0] ||= (e) => J(-65)
						}, [o("img", {
							src: ie(12),
							alt: "",
							draggable: "false"
						}, null, 8, nn)], 8, tn),
						o("div", {
							ref_key: "track",
							ref: C,
							class: "bf-mode-scroll-track",
							onPointerdown: j(ye, ["self"])
						}, [o("img", {
							class: "bf-mode-track-image",
							src: ie(1),
							alt: "",
							draggable: "false"
						}, null, 8, rn), o("div", {
							class: "bf-mode-scroll-thumb",
							role: "scrollbar",
							tabindex: "0",
							"aria-orientation": "vertical",
							"aria-label": t.labels.scroll,
							"aria-valuemin": 0,
							"aria-valuemax": V.value.maximum,
							"aria-valuenow": E.value,
							style: p({
								height: V.value.height + "px",
								top: V.value.top + "px"
							}),
							onPointerdown: i[1] ||= (...e) => w(be).start && w(be).start(...e),
							onKeydown: [
								i[2] ||= A(j((e) => J(-65), ["prevent"]), ["up"]),
								i[3] ||= A(j((e) => J(fn), ["prevent"]), ["down"]),
								i[4] ||= A(j((e) => J(-V.value.maximum), ["prevent"]), ["home"]),
								i[5] ||= A(j((e) => J(V.value.maximum), ["prevent"]), ["end"])
							]
						}, [
							o("img", {
								src: ie(3),
								alt: "",
								draggable: "false"
							}, null, 8, $),
							o("img", {
								src: ie(4),
								alt: "",
								draggable: "false"
							}, null, 8, on),
							o("img", {
								src: ie(5),
								alt: "",
								draggable: "false"
							}, null, 8, sn)
						], 44, an)], 544),
						o("button", {
							class: "bf-mode-scroll-arrow",
							type: "button",
							"aria-label": t.labels.next,
							onClick: i[6] ||= (e) => J(fn)
						}, [o("img", {
							src: ie(15),
							alt: "",
							draggable: "false"
						}, null, 8, ln)], 8, cn)
					], 4),
					o("footer", {
						class: "bf-mode-actions",
						style: p({
							top: F.value.actionsTop + "px",
							height: F.value.actionsHeight + "px",
							"--action-width": (t.theme === "v3" ? 112 : k.value) + "px",
							"--action-height": (t.theme === "v3" ? 33 : M.value) + "px"
						})
					}, [o("div", null, [s(Y, {
						draw: (e) => Z(e, "Ok"),
						revision: ee.value,
						onAction: pe,
						onError: ae
					}, null, 8, ["draw", "revision"])]), o("div", null, [s(Y, {
						draw: (e) => Z(e, "Cancel"),
						revision: W.value,
						onAction: he,
						onError: ae
					}, null, 8, ["draw", "revision"])])], 4)
				], 40, Qt)]),
				_: 1
			}, 8, [
				"title",
				"height",
				"close-label",
				"title-align",
				"font-family",
				"text-scale",
				"resource-root",
				"title-bounds"
			])], 12, Zt)]),
			_: 1
		}));
	}
}), mn = /* @__PURE__ */ c({
	__name: "BfCaptionButton",
	props: {
		label: {},
		skins: {},
		width: { default: 65 },
		height: { default: 27 },
		fit: {
			type: Boolean,
			default: !1
		},
		fitCaptions: {},
		fitMargin: { default: 37 },
		fitMinimum: { default: 65 },
		kind: { default: "Ok" },
		disabled: {
			type: Boolean,
			default: !1
		},
		fontFamily: { default: "BF" },
		fontSize: { default: 12 },
		fontWeight: { default: 700 },
		renderer: { default: "svg" },
		resourceRoot: { default: "" },
		textScale: { default: 1 }
	},
	emits: ["click", "error"],
	setup(e, { emit: t }) {
		let n = e, r = t, i = y(), o = 0;
		D(() => n.fit ? [
			n.fontFamily,
			String(n.fontSize),
			String(n.fitMargin),
			String(n.fitMinimum),
			...n.fitCaptions?.length ? n.fitCaptions : [n.label]
		] : null, async (e) => {
			let t = ++o;
			if (!e) i.value = void 0;
			else try {
				let [n, r, a, s, ...c] = e, l = await Ne(c, n, {
					size: Math.max(13, Number(r)),
					margin: Number(a),
					minimum: Number(s)
				});
				t === o && (i.value = l);
			} catch (e) {
				t === o && r("error", e instanceof Error ? e : Error(String(e)));
			}
		}, { immediate: !0 });
		let c = () => n.fit ? i.value ?? n.fitMinimum : n.width, l = () => n.height;
		return (t, n) => (g(), a("div", {
			class: "bf-caption-button",
			style: p({
				position: "relative",
				width: c() + "px",
				height: l() + "px"
			})
		}, [s(Y, {
			onError: n[0] ||= (e) => r("error", e),
			revision: [
				e.label,
				e.skins,
				c(),
				l(),
				e.kind,
				e.disabled,
				e.fontFamily,
				e.fontSize,
				e.fontWeight,
				e.renderer,
				e.resourceRoot,
				e.textScale
			],
			draw: (t) => w(Fe)(t, {
				label: e.label,
				width: c(),
				height: l(),
				skins: e.skins,
				disabled: e.disabled,
				kind: e.kind,
				family: e.fontFamily,
				size: e.fontSize,
				weight: e.fontWeight,
				renderer: e.renderer,
				resourceRoot: e.resourceRoot,
				pixelRatio: e.textScale
			}),
			onAction: n[1] ||= (e) => r("click")
		}, null, 8, ["revision", "draw"])], 4));
	}
}), hn = /* @__PURE__ */ c({
	__name: "BfAnchoredPanel",
	props: {
		width: {},
		height: {},
		align: { default: "start" }
	},
	emits: ["zoom"],
	setup(e, { emit: t }) {
		let n = e, r = t, i = y(), s = y(1), c, l = () => {
			let e = i.value?.clientWidth ?? 0;
			e > 0 && n.width > 0 && e / n.width !== s.value && (s.value = e / n.width, r("zoom", s.value));
		};
		h(() => {
			c = new ResizeObserver(l), i.value && c.observe(i.value), l();
		}), m(() => c?.disconnect()), D(() => n.width, l);
		let u = {
			start: "flex-start",
			center: "center",
			end: "flex-end"
		};
		return (t, n) => (g(), a("div", {
			ref_key: "box",
			ref: i,
			class: "bf-anchored-panel",
			style: {
				position: "absolute",
				inset: "0",
				overflow: "hidden"
			}
		}, [o("div", { style: p({
			zoom: s.value,
			width: e.width + "px",
			height: e.height + "px",
			display: "flex",
			alignItems: "center",
			justifyContent: u[e.align]
		}) }, [x(t.$slots, "default", { zoom: s.value })], 4)], 512));
	}
});
// ../tools/game-ui/tutorial-model.js
function gn(e, t, n = 0) {
	if (typeof e != "string" || !Number.isFinite(t) || t <= 0) throw Error("Invalid narrator timing");
	return {
		text: e,
		interval: t,
		visible: 0,
		deadline: n + t,
		phase: "typing",
		finishedAt: void 0
	};
}
function _n(e, t) {
	return e.phase !== "typing" || t < e.deadline ? "idle" : e.visible < e.text.length ? (e.visible++, e.deadline = t + e.interval, "character") : (e.phase = "complete", e.finishedAt = t, "complete");
}
function vn(e, t) {
	return e.phase === "typing" ? (e.visible = e.text.length, e.phase = "complete", e.finishedAt = t, "reveal") : "advance";
}
function yn(e, t, n = 15) {
	if (!e) return {
		height: 0,
		lines: []
	};
	let r = e.split("\n");
	e.endsWith("\n") && r.pop();
	let i = 0, a = r.map((a, o) => {
		let s = a + (o < r.length - 1 || e.endsWith("\n") ? "\n" : ""), c = Math.max(...Array.from(s, (e) => t[e]?.lineHeight ?? n), n), l = {
			text: a,
			y: i,
			height: c
		};
		return i += c, l;
	});
	return {
		height: i,
		lines: a
	};
}
function bn(e, t, n = !1) {
	if (!t) return;
	let r = e.filter((e) => e.stage === t.stage), i = r.find((e) => String(e.progress) === t.nextProgress);
	if (i) return i;
	if (n && r.length) return r[(r.findIndex((e) => e.progress === t.progress) + 1) % r.length];
}
// ../tools/game-ui/src/components/BfTutorialDialogue.vue?vue&type=script&setup=true&lang.ts
var xn = [
	"data-status",
	"data-progress",
	"data-visible-count"
], Sn = ["src", "alt"], Cn = ["src"], wn = ["aria-label"], Tn = ["onKeydown"], En = /* @__PURE__ */ c({
	__name: "BfTutorialDialogue",
	props: {
		stage: { default: "Stage1" },
		progress: { default: 21 },
		text: {},
		resourceRoot: { default: "/local/tutorial/cn-279/" },
		character: { default: "Dizni" },
		sound: {
			type: Boolean,
			default: !0
		},
		autoplay: {
			type: Boolean,
			default: !1
		},
		loop: {
			type: Boolean,
			default: !1
		}
	},
	emits: [
		"ready",
		"complete",
		"advance",
		"error"
	],
	setup(e, { expose: t, emit: r }) {
		let s = e, c = r, l = me(), u = y(), d = y("loading"), f = y(""), p = n(() => s.resourceRoot.replace(/\/?$/, "/")), h = (e) => `${p.value}Images/TutorialStage/${e}`, _, v = {}, b, x, S, C = 0, T = 0, E = 0, O = !1, k, M = {}, N = /* @__PURE__ */ new Set();
		async function P(e) {
			let t = await fetch(p.value + e);
			if (!t.ok) throw Error(`Tutorial resource ${e}: ${t.status}`);
			return t.json();
		}
		async function F() {
			let e = ++E;
			try {
				let [t, n] = await Promise.all([P("manifest.json"), P("glyphs.json")]);
				if (O || e !== E) return;
				let r = Object.assign(new Image(), { crossOrigin: "anonymous" });
				if (r.src = p.value + "glyphs.png", await r.decode(), O || e !== E || (_ = t, v = n.glyphs, b = r, s.sound && (k ??= new AudioContext(), k.state === "running" && await R()), O || e !== E)) return;
				d.value = "ready", c("ready", t.dialogues), V();
			} catch (t) {
				!O && e === E && (d.value = "error", c("error", t instanceof Error ? t : Error(String(t))));
			}
		}
		function I() {
			let e = u.value?.getContext("2d");
			if (!e || !x || !b) return;
			e.clearRect(0, 0, 250, 100), e.imageSmoothingEnabled = !1;
			let t = x.text.slice(0, x.visible);
			f.value = t;
			let n = yn(t, v, 15), r = 50 - Math.floor(n.height / 2);
			for (let t of n.lines) {
				let n = 0;
				for (let i of t.text) {
					let a = v[i];
					a ? (a.width && a.height && e.drawImage(b, a.x, a.y, a.width, a.height, n + a.left, r + t.y + 14 - a.top, a.width, a.height), n += a.advance) : (e.font = "400 15px BF", e.fillStyle = "#222222", e.textBaseline = "alphabetic", e.fillText(i, n, r + t.y + 14), n += Math.round(e.measureText(i).width));
				}
			}
		}
		function L(e) {
			if (!s.sound || k?.state !== "running" || !M[e]) return;
			if (N.size >= 8) {
				let e = N.values().next().value;
				e?.stop(), e && N.delete(e);
			}
			let t = k.createBufferSource();
			t.buffer = M[e], t.connect(k.destination), N.add(t), t.onended = () => {
				N.delete(t), t.disconnect();
			}, t.start();
		}
		async function R() {
			if (s.sound && (k ??= new AudioContext(), await k.resume(), !M.type)) {
				let e = k, t = await Promise.all(["type", "textbox"].map(async (t) => {
					let n = await fetch(`${p.value}Sounds/ui-tut-${t}.wav`);
					if (!n.ok) throw Error(`Tutorial sound ${t}: ${n.status}`);
					return [t, await e.decodeAudioData(await n.arrayBuffer())];
				}));
				if (O) return;
				M = Object.fromEntries(t);
			}
		}
		function z(e) {
			if (O || !x) return;
			let t = _n(x, e);
			t === "character" && (I(), L("type")), t === "complete" && c("complete"), s.autoplay && x.phase === "complete" && S && S.holdMs >= 0 && e - (x.finishedAt ?? e) >= S.holdMs ? (d.value = "complete", c("advance", B())) : C = requestAnimationFrame(z);
		}
		function B() {
			return bn(_?.dialogues ?? [], S, s.loop);
		}
		function V() {
			_ && u.value && (cancelAnimationFrame(C), T++, S = _.dialogues.find((e) => e.stage === s.stage && e.progress === s.progress), !S && s.text === void 0 ? c("error", Error(`Missing tutorial page ${s.stage}/${s.progress}`)) : (x = gn(s.text ?? S.text, _.textPrintSpeed, performance.now()), I(), s.sound && (k?.state !== "running" || !M.type) ? d.value = "awaiting-audio" : (d.value = "playing", L("textbox"), C = requestAnimationFrame(z))));
		}
		async function H() {
			try {
				await R(), O || V();
			} catch (e) {
				c("error", e instanceof Error ? e : Error(String(e)));
			}
		}
		async function U() {
			let e = T;
			try {
				if (await R(), O || e !== T || !x || S?.clickComplete === !1) return;
				if (d.value === "awaiting-audio") {
					V();
					return;
				}
				vn(x, performance.now()) === "reveal" ? (I(), L("type"), c("complete")) : c("advance", B());
			} catch (e) {
				c("error", e instanceof Error ? e : Error(String(e)));
			}
		}
		return D([
			u,
			l,
			() => s.stage,
			() => s.progress,
			() => s.text,
			() => s.character
		], () => V(), { flush: "post" }), D(() => s.sound, () => V()), D(p, () => {
			d.value = "loading", cancelAnimationFrame(C), _ = void 0, x = void 0, b = void 0, f.value = "", M = {};
			for (let e of N) e.stop();
			N.clear(), F();
		}, { immediate: !0 }), m(() => {
			O = !0, cancelAnimationFrame(C);
			for (let e of N) e.stop();
			N.clear(), k?.close();
		}), t({
			start: H,
			restart: V,
			status: d
		}), (t, n) => (g(), a("div", {
			class: "bf-tutorial-dialogue",
			"data-status": d.value,
			"data-progress": e.progress,
			"data-visible-count": w(x)?.visible ?? 0,
			onClick: U
		}, [
			o("img", {
				class: "bf-tutorial-portrait",
				src: h(e.character + ".png"),
				alt: e.character,
				draggable: "false"
			}, null, 8, Sn),
			o("img", {
				class: "bf-tutorial-balloon",
				src: h("ChatBalloonNew.png"),
				alt: "",
				draggable: "false"
			}, null, 8, Cn),
			o("canvas", {
				ref_key: "canvas",
				ref: u,
				class: "bf-tutorial-copy",
				width: "250",
				height: "100",
				role: "img",
				"aria-label": f.value
			}, null, 8, wn),
			d.value === "awaiting-audio" ? (g(), a("button", {
				key: 0,
				class: "bf-tutorial-start",
				onClick: j(H, ["stop"])
			}, "点击开始教学（播放声音）")) : i("v-if", !0),
			o("button", {
				class: "bf-tutorial-skip",
				"aria-label": "跳过对话",
				onKeydown: [A(j(U, ["prevent"]), ["enter"]), A(j(U, ["prevent"]), ["space"])]
			}, null, 40, Tn)
		], 8, xn));
	}
});
export { hn as BfAnchoredPanel, Lt as BfBanPick, Z as BfButton, mn as BfCaptionButton, xe as BfCheckbox, ye as BfControl, we as BfDialog, De as BfExtensionSettings, Ce as BfGamePage, be as BfGroupBox, X as BfLabel, Te as BfMessageBox, pn as BfModeExtension, Ee as BfNotifyBox, ve as BfPanel, Rt as BfReveal, Se as BfSlider, En as BfTutorialDialogue, J as BfUiProvider, _e as BfWindow, N as DEFAULT_UI_VOLUME, je as NATIVE_ACTION_HEIGHT, M as UI_SOUND_EVENTS, F as createUiSoundPlayer, P as effectVolumeGain, Yt as filterModesForBuild, Me as fitCaptionWidth, Ne as measureCaptionWidth, Xt as modeWindowGeometry, I as themeForClientDate, Jt as validateModeAvailability };
