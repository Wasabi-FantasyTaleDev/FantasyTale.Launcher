import { Fragment as e, Teleport as t, computed as n, createBlock as r, createCommentVNode as i, createElementBlock as a, createElementVNode as o, createVNode as s, defineComponent as c, inject as l, nextTick as u, onBeforeUnmount as d, openBlock as f, provide as p, ref as m, renderSlot as h, shallowRef as g, toDisplayString as _, unref as v, watch as y, withCtx as b, withKeys as x, withModifiers as S } from "vue";
// ../tools/game-ui/ui-sounds.js
var C = Object.freeze([
	"mouseIn",
	"press",
	"toggleButtonOn",
	"toggleButtonOff",
	"popup",
	"cancel",
	"fail",
	"notice",
	"event-slidenotice"
]), w = .75;
function T(e) {
	if (!Number.isInteger(e) || e < 0 || e > 100) throw Error("Effect volume must be an integer from 0 to 100.");
	return e / 100;
}
function E({ resourceRoot: e = "./local/", enabled: t = !0, volume: n = w, maxVoices: r = 16, onError: i = (e) => console.error(e) } = {}) {
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
			for (let t of C) {
				let n = e.sounds[t];
				if (!n || !Number.isFinite(n.gain) || n.gain < 0 || n.gain > 1 || !/^[-\w]+\.wav$/.test(n.file)) throw Error(`Invalid UI sound event: ${t}`);
			}
			if (m) return;
			u = new AudioContext(), d = u.createGain(), d.gain.value = n, d.connect(u.destination);
			let t = u;
			await Promise.all(C.map(async (n) => {
				let r = await (await x(a + e.sounds[n].file)).arrayBuffer(), i = await t.decodeAudioData(r);
				m || s.set(n, i);
			})), m || (p = e);
		})(), f;
	}
	function T(e) {
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
	async function E() {
		if (m || !t) return !1;
		let e = u?.resume();
		if (await S(), m || !t || (e ? await e : await u.resume(), m || !t)) return !1;
		let n = g;
		return g = void 0, n?.root.isConnected && T(n.event), u.state === "running";
	}
	async function D(e) {
		if (!C.includes(e)) throw Error(`Unknown UI sound event: ${e}`);
		if (m || !t) return !1;
		let n = h;
		return await S(), m || !t || n !== h ? !1 : T(e);
	}
	function O(e) {
		let n = !0, r = e.dataset.bfOpenSound, i = /* @__PURE__ */ new WeakSet();
		if (r && !C.includes(r)) throw Error(`Unknown opening sound: ${r}`);
		let a = () => n && !m && e.isConnected, o = (e) => e.target instanceof Element ? e.target.closest("button.sprite-button,button.bf-close") : null, s = (t) => t && e.contains(t) && !t.disabled && t.getAttribute("aria-disabled") !== "true", c = (e) => !l.has(e) && (l.add(e), !0), d = (e, n = !1) => {
			if (t && a()) {
				if (n) {
					let t = h;
					E().then((n) => {
						n && a() && t === h && T(e);
					}).catch(v);
				} else T(e);
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
		return e.addEventListener("pointerover", f), e.addEventListener("pointerout", p), e.addEventListener("pointerdown", _, !0), e.addEventListener("keydown", y), e.addEventListener("change", b), r && t && (u?.state === "running" ? T(r) : g = {
			root: e,
			event: r
		}), () => {
			n = !1, g?.root === e && (g = void 0), e.removeEventListener("pointerover", f), e.removeEventListener("pointerout", p), e.removeEventListener("pointerdown", _, !0), e.removeEventListener("keydown", y), e.removeEventListener("change", b);
		};
	}
	let k = (e) => {
		e.isTrusted && t && u?.state !== "running" && E().catch(v);
	};
	document.addEventListener("pointerdown", k, !0), document.addEventListener("keydown", k, !0);
	function A() {
		m || (m = !0, h++, g = void 0, o.abort(), y(), s.clear(), document.removeEventListener("pointerdown", k, !0), document.removeEventListener("keydown", k, !0), u && u.close().catch(v));
	}
	return {
		preload: S,
		unlock: E,
		play: D,
		bind: O,
		setEnabled: b,
		setVolume: _,
		dispose: A
	};
}
// ../tools/game-ui/theme.js
function D(e, t = "v2") {
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
async function O(e, t, n = {}) {
	let r = getComputedStyle(e), i = n.size || parseFloat(r.fontSize) || 13, a = n.family || "BFMalgun", o = n.weight || 400;
	if (await document.fonts.load(`${o} ${i}px ${a}`), (n.renderer || globalThis.__BF_TEXT_RENDERER || "canvas") === "native") return k(e, t, n, {
		size: i,
		family: a,
		weight: o
	});
	let s = n.width || parseFloat(e.style.width) || e.clientWidth, c = n.height || parseFloat(e.style.height) || e.clientHeight;
	if (!(s > 0 && c > 0)) throw Error("Text requires positive logical dimensions.");
	let l = document.createElement("canvas"), u = n.samples ?? Number(globalThis.__BF_TEXT_SAMPLES), d = Number.isFinite(u) && u >= 1 ? u : 4;
	l.width = Math.ceil(s * d), l.height = Math.ceil(c * d), l.style.cssText = `position:absolute;inset:0;width:${s}px;height:${c}px;pointer-events:none`, l.ariaHidden = "true";
	let f = l.getContext("2d");
	f.scale(d, d), f.imageSmoothingEnabled = !0, f.imageSmoothingQuality = "high", f.font = `${o} ${i}px ${a}`, f.fillStyle = n.color || e.style.color || r.color || "#5b5b5b", f.textBaseline = n.nativeMetrics ? "alphabetic" : "middle", f.textAlign = n.align || "left", f.lineJoin = "round";
	let p = [];
	for (let e of t.split("\n")) {
		let t = "";
		for (let r of e) n.wrap && t && f.measureText(t + r).width > s - 4 && (p.push(t), t = ""), t += r;
		p.push(t);
	}
	let m = n.lineHeight || i + 1, h = n.decoration ?? (n.outline ? 4 : 1), g = h === 4 || h === 2 ? 2 : +(h === 3), _ = f.measureText(t.split("\n")[0] || " "), v = Math.ceil(i + _.actualBoundingBoxDescent) + g * 2, y = (n.vertical === "top" ? 0 : Math.floor(c / 2) - Math.floor(v / 2)) + g + i - 1, b = n.nativeMetrics ? y : n.vertical === "top" ? i / 2 + 2 : (c - (p.length - 1) * m) / 2, x = f.textAlign === "center" ? s / 2 : f.textAlign === "right" ? s - 2 : n.inset ?? 2;
	return p.forEach((e, t) => {
		let r = b + t * m + (n.baselineOffset || 0);
		n.outline && h > 1 && (f.strokeStyle = n.outline, f.lineWidth = 2 * (n.outlineWidth || .75), f.strokeText(e, x, r)), f.fillText(e, x, r);
	}), e.textContent = t, e.style.color = "transparent", e.classList.add("bf-canvas-text"), e.dataset.text = t, e.append(l), e;
}
function k(e, t, n, r) {
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
// ../tools/game-ui/native-layout.js
function A(e, t, n, r, i = 1) {
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
function j(e, t, n, r = 1, i = 15) {
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
var M = 0;
function N(e = {}) {
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
async function P(e, { value: t = {}, onChange: n = () => {} } = {}) {
	let r = N(t), i = ++M, a = await e.dialog({
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
async function F({ theme: e = "v2", resourceRoot: t = "./local/", sounds: n } = {}) {
	if (![
		"v1",
		"v2",
		"v3"
	].includes(e)) throw Error(`Unknown UI theme: ${e}`);
	let r = () => `${t.replace(/\/?$/, "/")}${e}/`, i = await fetch(r() + "manifest.json");
	if (!i.ok) throw Error(`Unable to load ${e} UI resources (${i.status}).`);
	let a = await i.json(), o = (e, t, n = 0) => Number(e[t] || n);
	function s(e, t, n, r, i) {
		return Object.assign(e.style, {
			left: `${t}px`,
			top: `${n}px`,
			width: `${r}px`,
			height: `${i}px`
		}), e;
	}
	async function c(e, t, n, i, o, c, l) {
		let u = a.sprites[t]?.findIndex((e) => e.every((e, t) => e === n[t]));
		if (u === void 0 || u < 0) throw Error(`Missing sprite frame: ${t}`);
		let d = s(document.createElement("span"), i, o, c, l);
		d.className = "tile";
		let f = new Image();
		f.src = r() + t.replace(/\.smf$/i, "") + "/" + u + ".png", Object.assign(f.style, {
			width: "100%",
			height: "100%",
			left: "0",
			top: "0"
		}), d.append(f), e.append(d);
	}
	let l = /* @__PURE__ */ new Map();
	async function u(e, t) {
		let n = e + ":" + t;
		return l.has(n) || l.set(n, (async () => {
			let n = new Image();
			return n.src = r() + e.replace(/\.smf$/i, "") + "/" + t + ".png", await n.decode(), n;
		})()), l.get(n);
	}
	async function d(e, t, n, r) {
		let i = a.sprites[t];
		if (!i) throw Error(`Missing sprite: ${t}`);
		let o = document.createElement("canvas");
		o.className = "skin", o.width = n, o.height = r, o.style.width = n + "px", o.style.height = r + "px", o.ariaHidden = "true";
		let s = o.getContext("2d");
		if (s.imageSmoothingEnabled = !1, i.length !== 9) s.drawImage(await u(t, 0), 0, 0, n, r);
		else {
			let e = [
				i[0][2] - i[0][0] + 1,
				0,
				i[2][2] - i[2][0] + 1
			], a = [
				i[0][3] - i[0][1] + 1,
				0,
				i[6][3] - i[6][1] + 1
			];
			if (e[1] = n - e[0] - e[2], a[1] = r - a[0] - a[2], e[1] < 0 || a[1] < 0) throw Error(`Skin destination too small: ${t}`);
			let o = await Promise.all(i.map((e, n) => u(t, n))), c = 0;
			for (let t = 0; t < 3; t++) {
				let n = 0;
				for (let r = 0; r < 3; r++) e[r] && a[t] && s.drawImage(o[t * 3 + r], n, c, e[r], a[t]), n += e[r];
				c += a[t];
			}
		}
		return e.prepend(o), o;
	}
	async function f(t, n, r, i, o, l, u, f, p = {}) {
		if (e === "v3" && (!f || f.includes("/dialogueNew/"))) {
			let e = s(document.createElement("button"), r, i, o, l);
			e.className = "control sprite-button", e.ariaLabel = u, e.dataset.control = p.Name || n, t.append(e);
			let c = n === "Ok" ? "Images/Ui/dialogueNew/dialogue_type01BTN-Up.smf" : a.scheme.UiButton.SkinImage_Up, m = f || p.SpriteSetUp || c, h = 0, g = !1, _ = [
				m,
				p.SpriteSetDown || m.replace(/(?:-Up|_up)\.smf$/, m.includes("Tab_") ? "_down.smf" : "-Down.smf"),
				p.SpriteSetHover || m.replace(/(?:-Up|_up)\.smf$/, m.includes("Tab_") ? "_hover.smf" : "-Hover.smf"),
				p.SpriteSetDisabled || m.replace(/(?:-Up|_up)\.smf$/, m.includes("Tab_") ? "_disable.smf" : "-Disabled.smf")
			];
			async function v(t) {
				let n = ++h, r = document.createElement("span");
				r.className = "control", s(r, 0, 0, o, l), r.style.pointerEvents = "none", await d(r, _[e.disabled ? 3 : g ? 1 : t], o, l);
				let i = s(document.createElement("span"), 0, 0, o, l);
				i.className = "control", r.append(i), await O(i, u, {
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
			}), e.addEventListener("click", () => t.dispatchEvent(new CustomEvent("bf-action", {
				bubbles: !0,
				detail: {
					name: n,
					text: u
				}
			}))), e;
		}
		let m = s(document.createElement("button"), r, i, o, l);
		m.className = "control sprite-button", m.ariaLabel = u, t.append(m);
		let h = f || `Images/Ui/DialogBtn${n}.smf`, g = 0;
		async function _(t) {
			if (g = t, m.replaceChildren(), e === "v1" && (!f || /DialogButton(?:Ok|Cancel)-Up/.test(f))) {
				let e = [
					"Up",
					"Down",
					"Hover",
					"Disabled"
				][t], r = f?.match(/DialogButton(Ok|Cancel)-Up/)?.[1] ?? n;
				await d(m, `Images/Ui/DialogButton${r}-${e}.smf`, o, l);
				let i = document.createElement("span");
				i.className = "text", i.style.cssText = "left:14px;right:0;top:0;bottom:0;width:" + (o - 14) + "px;height:" + l + "px", m.append(i), await O(i, u, {
					family: "BFMalgun",
					size: 13,
					weight: 400,
					width: o - 14,
					height: l,
					align: "center",
					color: "white",
					outline: "#79a4cf",
					outlineWidth: .5
				});
			} else {
				let e = a.sprites[h];
				if (!e) throw Error(`Missing button: ${h}`);
				await c(m, h, e[Math.min(t, e.length - 1)], 0, 0, o, l);
			}
		}
		return await _(0), m.setSelected = async (e) => {
			m.ariaPressed = String(e), await _(e ? 3 : 0);
		}, m.addEventListener("pointerenter", () => _(m.ariaPressed === "true" ? 3 : 2)), m.addEventListener("pointerleave", () => _(m.ariaPressed === "true" ? 3 : 0)), m.addEventListener("pointerdown", () => _(1)), m.addEventListener("pointerup", () => _(m.ariaPressed === "true" ? 3 : 2)), m.addEventListener("click", () => {
			t.dispatchEvent(new CustomEvent("bf-action", {
				bubbles: !0,
				detail: {
					name: n,
					text: u,
					state: g
				}
			}));
		}), m;
	}
	async function p(t, n, i) {
		let o = s(document.createElement("div"), 0, 0, t, n);
		o.className = "bf-window", o.dataset.theme = e, o.dataset.bfOpenSound = "popup", await d(o, e === "v3" ? a.scheme.Dialog.SkinImage_Title : "Images/Ui/DialogSkin-Basic.smf", t, n);
		let l = document.createElement("span");
		if (l.className = "caption", l.style.width = t - (e === "v1" ? 62 : 50) + "px", l.style.height = (e === "v2" ? 38 : 18) + "px", e === "v1" && (l.style.left = "37px", l.style.top = "11px"), e === "v2" && (l.style.left = "3px", l.style.top = "9px", l.style.width = t + "px"), e === "v3") {
			let e = a.scheme.Dialog;
			s(l, Number(e.Title_TextMargin_Left), Number(e.Title_TextMargin_Top), t - Number(e.Title_TextMargin_Left) - Number(e.Title_TextMargin_Right), Number(e.Title_Margin_Top) - Number(e.Title_TextMargin_Top) - Number(e.Title_TextMargin_Bottom));
		}
		o.append(l), await O(l, i, {
			size: Number(a.scheme.Dialog.Title_FontSize),
			family: "BF",
			align: e === "v1" ? "left" : "center",
			color: "#" + a.scheme.Dialog.Title_TextColor.slice(-6),
			decoration: Number(a.scheme.Dialog.Title_TextDecoration),
			outline: "#" + a.scheme.Dialog.Title_TextDecoColor.slice(-6),
			outlineWidth: 2,
			nativeMetrics: e !== "v1",
			vertical: e === "v3" ? "middle" : "top"
		});
		let u = a.scheme.Dialog.CloseButton_SkinImage, f = a.sprites[u], p = f[0][2] - f[0][0] + 1, m = f[0][3] - f[0][1] + 1, h = Number(a.scheme.Dialog.Title_Margin_Right) + (e === "v1" ? 5 : -3), g = s(document.createElement("button"), e === "v3" ? t - Number(a.scheme.Dialog.CloseButton_MarginRight_X) - p : t - h - p, e === "v3" ? Number(a.scheme.Dialog.CloseButton_MarginRight_Y) : e === "v1" ? 11 : 8, p, m);
		g.className = "bf-close", g.ariaLabel = "关闭", g.dataset.bfPressSound = "cancel", await c(g, u, f[0], 0, 0, p, m);
		let _ = g.querySelector("img"), v = (e) => {
			_.src = r() + u.replace(/\.smf$/i, "") + "/" + e + ".png";
		};
		return g.addEventListener("pointerenter", () => v(g.disabled ? 3 : 2)), g.addEventListener("pointerleave", () => v(g.disabled ? 3 : 0)), g.addEventListener("pointerdown", () => {
			g.disabled || v(1);
		}), g.addEventListener("pointerup", () => v(g.disabled ? 3 : 2)), g.addEventListener("pointercancel", () => v(g.disabled ? 3 : 0)), g.onclick = () => o.dispatchEvent(new CustomEvent("bf-action", {
			bubbles: !0,
			detail: { name: "close" }
		})), o.append(g), o;
	}
	function m(e, t, n) {
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
	function h({ contentWidth: e, contentHeight: t, textWidth: n, textHeight: r, buttonWidth: i, buttonHeight: a, buttonCount: o, buttonSpan: s = i }) {
		let c = Math.max(200, n), l = Math.max(70, r + 16, 58), u = Math.max(e, c + 104, s + 20);
		return {
			width: u,
			height: Math.max(t, l + 9 + a),
			bodyHeight: l,
			buttonY: l + 9,
			buttonX: o === 1 ? Math.floor((u - i) / 2) : void 0
		};
	}
	async function g({ title: t, text: n, iconKind: i = "notice", buttons: o, body: c, contentWidth: l = 350, contentHeight: u, contentX: g = 10, contentY: _ = 38, outerWidth: v, outerHeight: y, buttonY: b }) {
		if (e === "v3") return x(arguments[0]);
		let S = e === "v2", C = l, w = u ?? (S ? 104 : 105), T = typeof c == "function", E = `Images/Ui/MsgBoxIcon${{
			notice: "Notice",
			warning: "Warning",
			question: "Question",
			error: "Error"
		}[i] || "Notice"}.png`, D = S ? 5 : 8, k = o.map((e) => ({
			...e,
			width: e.width ?? (S ? 64 : 68),
			height: e.height ?? (S ? 26 : 25)
		}));
		if (!k.length && !T) throw Error("Message boxes require at least one button.");
		let A = k.reduce((e, t) => e + t.width, 0) + D * (k.length - 1);
		await document.fonts.load("400 13px BFMalgun");
		let j = T ? {
			width: 0,
			height: 0
		} : m(n, 13, "BFMalgun"), M = T ? {
			width: l,
			height: w,
			bodyHeight: w,
			buttonY: b ?? w + 9
		} : h({
			contentWidth: C,
			contentHeight: w,
			textWidth: j.width,
			textHeight: j.height,
			buttonWidth: k[0].width,
			buttonHeight: k[0].height,
			buttonCount: k.length,
			buttonSpan: A
		}), N = M.buttonY + Math.max(0, ...k.map((e) => e.height));
		M.height = Math.max(M.height, N);
		let P = Number(a.scheme.Dialog.Title_Margin_Right), F = Number(a.scheme.Dialog.Title_Margin_Bottom), I = await p(Math.max(v ?? 0, g + M.width + P), Math.max(y ?? 0, _ + M.height + F), t), L = s(document.createElement("div"), g, _, M.width, M.height);
		if (L.className = "bf-form", I.append(L), T) await c(L, M);
		else {
			let e = S ? M.width - 1 : M.width, t = s(document.createElement("div"), +!!S, 2, e, M.bodyHeight);
			t.className = "control", await d(t, "Images/Ui/FrameBox.smf", e, M.bodyHeight), L.append(t);
			let i = new Image();
			i.src = r() + E, i.className = "control", await i.decode().catch(() => {});
			let a = i.naturalWidth || 63, o = i.naturalHeight || 58;
			s(i, 11, Math.round(2 + (M.bodyHeight - o) / 2), a, o), L.append(i);
			let c = Math.max(40, j.height), l = s(document.createElement("div"), 90, Math.round(2 + (M.bodyHeight - c) / 2), M.width - 104, c);
			l.className = "message-copy", L.append(l), await O(l, n, {
				size: 13,
				weight: 400,
				family: "BFMalgun",
				align: "center",
				color: "#444",
				height: c,
				width: M.width - 104
			});
		}
		let R = !S && k.length > 1 ? M.width - A : Math.floor((M.width - A) / 2);
		for (let e of k) {
			let t = M.width - C, n = e.x === void 0 ? R : e.x + (S ? t / 2 : t), r = e.sprite || (S ? `Images/Ui/DialogBtn${e.name}.smf` : `Images/Ui/DialogButton${e.name}-Up.smf`);
			await f(L, e.name, n, M.buttonY, e.width, e.height, e.label, r), R += e.width + D;
		}
		return I;
	}
	async function _(t, n = typeof t == "boolean" && t ? "question" : "notice") {
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
		}], s = await g({
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
			buttonY: a.buttonY
		});
		return a.openSound === !1 ? delete s.dataset.bfOpenSound : s.dataset.bfOpenSound = a.openSound ?? "popup", s;
	}
	let v = {
		gbGraphicOption: "画面设置",
		gbSoundOption: "声音设置",
		lblBgm: "背景音",
		lblEffect: "效果音",
		lblMSG1: "与服务器连接中断。",
		lblText: "与服务器连接中断。",
		lblResolution: "1366 x 768"
	};
	async function y(t, n, i = {}) {
		if (e === "v3") return b(t, n, i);
		let l = {
			...t.attrs,
			...i[t.attrs.Name]
		}, u = o(l, "PosX"), p = o(l, "PosY"), m = o(l, "Width"), h = o(l, "Height"), g = document.createElement("div");
		g.className = "control", s(g, u, p, m, h), n.append(g);
		let _ = l.SkinSpriteSet || l.SpriteSetBackground, x = i[l.Name]?.Text || v[l.Name] || l.Text || "";
		switch (t.type) {
			case "UiPanel":
			case "UiForm":
				if (_) await d(g, _, m, h);
				else if (l.SkinImage) {
					let e = document.createElement("img");
					e.src = r() + l.SkinImage, e.style.cssText = "width:100%;height:100%", g.append(e);
				}
				break;
			case "UiGroupBox":
				_ && await d(g, _, m, h);
				{
					let e = s(document.createElement("span"), 26, 4, m - 26, 22);
					e.textContent = x, e.style.color = "white", e.className = "control bf-label", g.append(e), await O(e, x, {
						color: "white",
						size: Number(l.FontSize) || Number(a.scheme.GroupBox.FontSize),
						family: "BF",
						nativeMetrics: !0,
						vertical: "top",
						inset: 0
					});
				}
				break;
			case "UiLabel":
				g.classList.add("bf-label"), g.textContent = x, g.style.fontSize = `${o(l, "FontSize", 13)}px`, g.style.fontFamily = [
					"Default",
					"DefaultBold",
					"NexonBTBold"
				].includes(l.FontName) ? "BF" : "BFMalgun", l.TextColor && (g.style.color = "#" + l.TextColor.slice(-6)), o(l, "TextFormat") === 5 && (g.style.justifyContent = "center"), await O(g, x, {
					size: Number(l.FontSize) || 13,
					color: l.TextColor ? "#" + l.TextColor.slice(-6) : "#5b5b5b",
					family: g.style.fontFamily || "BFMalgun",
					align: o(l, "TextFormat") === 5 ? "center" : "left",
					outline: i[l.Name]?.OutlineColor,
					nativeMetrics: !0,
					vertical: o(l, "TextFormat") & 4 ? "middle" : "top",
					inset: 0
				});
				break;
			case "UiButton":
				g.remove(), await f(n, /cancel/i.test(l.Name) ? "Cancel" : "Ok", u, p, m, h, x || (/cancel/i.test(l.Name) ? "取消" : "确认"), l.SpriteSet || l.SpriteSetUp);
				return;
			case "UiCheckBox": {
				g.classList.add("bf-check");
				let e = document.createElement("input");
				e.type = "checkbox", e.checked = l.Check === "1", e.ariaLabel = l.Name || x;
				let t = async () => {
					g.querySelectorAll(".tile").forEach((e) => e.remove());
					let t = l.SpriteSet || "Images/Ui/UiCheckBoxSkin.smf";
					await c(g, t, a.sprites[t][+!!e.checked], 0, 0, 21, 18);
				};
				g.setValue = (n) => {
					e.checked = !!n, t();
				}, g.append(e), e.onchange = () => {
					t(), n.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: l.Name,
							value: e.checked
						}
					}));
				}, await t();
				break;
			}
			case "UiTrackBar": {
				g.classList.add("bf-track");
				let e = document.createElement("input");
				e.type = "range", e.min = l.Min ?? 0, e.max = l.Max ?? 100, e.value = l.Position ?? 75, e.ariaLabel = l.Name;
				let t = "Images/Ui/TrackBar.smf", r = a.sprites[t], i = h - 35;
				await c(g, t, r[0], 15, i, 7, 31), await c(g, t, r[1], 22, i, m - 44, 31), await c(g, t, r[2], m - 22, i, 7, 31);
				let o = A(m, h, Number(e.min), Number(e.max), Number(l.Interval) || 1);
				for (let e of o.ticks) await c(g, t, r[7], e, i, 1, 31);
				let u = s(document.createElement("span"), o.knobX(Number(e.value)), i, 12, 31);
				u.className = "control", await c(u, t, r[3], 0, 0, 12, 31), g.append(u, e), l.MinText && F(g, l.MinText === "작게" ? "缩小" : l.MinText, 0, h - 50, 50, 15, "#808080", {
					size: Number(l.FontSize) || Number(a.scheme.TrackBar.FontSize),
					family: "BF",
					align: "center"
				}), l.MaxText && F(g, l.MaxText === "크게" ? "放大" : l.MaxText, m - 50, h - 52, 50, 15, "#808080", {
					size: Number(l.FontSize) || Number(a.scheme.TrackBar.FontSize),
					family: "BF",
					align: "center"
				}), g.setValue = (t) => {
					e.value = String(t), u.style.left = o.knobX(Number(e.value)) + "px";
				}, e.oninput = () => {
					u.style.left = o.knobX(Number(e.value)) + "px", n.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: l.Name,
							value: Number(e.value)
						}
					}));
				};
				break;
			}
			case "UiEdit":
			case "UiEditBox": {
				let e = document.createElement("input");
				e.className = "control bf-edit", s(e, u, p, m, h), e.value = x, e.ariaLabel = l.Name, g.replaceWith(e), g = e;
				break;
			}
			default: throw Error(`Unsupported control: ${t.type} (${l.Name || "unnamed"})`);
		}
		for (let e of t.children) await y(e, g, i);
		return g;
	}
	async function b(e, t, n = {}) {
		let i = {
			...e.attrs,
			...n[e.attrs.Name]
		}, l = o(i, "PosX"), u = o(i, "PosY"), p = o(i, "Width"), m = o(i, "Height");
		if (i.Visible === "False" || i.Visible === "0" || e.type === "UiMarker") return;
		let h = s(document.createElement("div"), l, u, p, m);
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
				if (_) await d(h, _, p, m);
				else if (i.SkinImage) {
					let e = new Image();
					e.src = r() + i.SkinImage, await e.decode(), s(e, 0, 0, e.naturalWidth, e.naturalHeight), h.append(e);
				}
				e.type === "UiGroupBox" && g && F(h, g, 9, 5, p - 18, 20, "#29d4ff", {
					size: 14,
					family: "BF"
				});
				break;
			case "UiLabel": {
				let e = o(i, "TextFormat");
				h.classList.add("bf-label"), await O(h, g, {
					...v,
					align: e & 1 ? "center" : e & 2 ? "right" : "left",
					vertical: e & 4 ? "middle" : e & 8 ? "bottom" : "top",
					wrap: !!(e & 16)
				});
				break;
			}
			case "UiButton": return h.remove(), f(t, /cancel/i.test(i.Name) ? "Cancel" : /okay|^btnok$/i.test(i.Name) ? "Ok" : i.Name, l, u, p, m, g, i.SpriteSet || i.SpriteSetUp, {
				...i,
				FontSize: Number(i.FontSize) ? i.FontSize : a.scheme.UiButton.FontSize
			});
			case "UiCheckBox": {
				h.classList.add("bf-check");
				let e = document.createElement("input");
				e.type = "checkbox", e.checked = i.Check === "1", e.ariaLabel = g || i.Name, h.append(e);
				let n = i.SpriteSet || a.scheme.UiCheckBox.SkinImage, r = a.sprites[n], o = s(document.createElement("span"), 0, 0, 21, 18);
				o.className = "control", h.append(o);
				let l = async () => {
					o.replaceChildren(), await c(o, n, r[+!!e.checked], 0, 0, 21, 18);
				};
				if (h.setValue = (t) => {
					e.checked = !!t, l();
				}, e.onchange = () => {
					l(), t.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: i.Name,
							value: e.checked
						}
					}));
				}, await l(), g) {
					let e = s(document.createElement("span"), 30, 0, Math.max(1, p - 30), m);
					e.className = "control", h.append(e), await O(e, g, {
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
				let n = i.HasText !== "False", r = j(p, Number(e.min), Number(e.max), Number(i.Interval) || 1, n ? 15 : 0), o = a.scheme.TrackBar.SkinImage, l = a.sprites[o], u = r.railY;
				if (await c(h, o, l[0], 15, u, 4, 18), await c(h, o, l[1], 19, u, p - 38, 18), await c(h, o, l[2], p - 19, u, 4, 18), i.HasPoint !== "False") for (let e of r.ticks) await c(h, o, l[7], e, u, 1, 18);
				let d = s(document.createElement("span"), r.knobX(Number(e.value)), u, 9, 18);
				d.className = "control", await c(d, o, l[3], 0, 0, 9, 18), h.append(d);
				let f = () => {
					d.style.left = r.knobX(Number(e.value)) + "px";
				};
				if (h.setValue = (t) => {
					e.value = String(t), f();
				}, e.oninput = () => {
					f(), t.dispatchEvent(new CustomEvent("bf-change", {
						bubbles: !0,
						detail: {
							name: i.Name,
							value: Number(e.value)
						}
					}));
				}, n) {
					let e = (e) => a.translations[e] || {
						작게: "小",
						크게: "大"
					}[e] || e;
					F(h, e(i.MinText || ""), 0, 0, 50, 15, v.color, {
						size: v.size,
						family: v.family,
						align: "center"
					}), F(h, e(i.MaxText || ""), p - 50, 0, 50, 15, v.color, {
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
				return e.className = "control bf-edit", e.ariaLabel = i.Name, e.value = g, s(e, l, u, p, m), h.replaceWith(e), e;
			}
			default: throw Error(`Unsupported cn-774 control: ${e.type} (${i.Name || "unnamed"})`);
		}
		for (let t of e.children) await y(t, h, n);
		return h;
	}
	async function x(e) {
		let { title: t, text: n = "", iconKind: i = "notice", buttons: o = [], body: c } = e, l = typeof c == "function", u = a.forms["DlgMessageBox.ui"], d = (e) => u.children.find((t) => t.attrs.Name === e).attrs, m = d("pnlFrameBox"), h = d("lblText"), g = d("btnOk"), _ = e.contentWidth ?? Number(u.attrs.Width), v = o.map((e) => ({
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
		let S = l ? e.contentHeight ?? 214 : Math.max(Number(m.Height), x.length * 15), C = e.buttonY ?? S + (Number(g.PosY) - Number(m.Height)), w = l ? e.contentHeight ?? 214 : C + Number(g.Height) + 1, E = e.contentX ?? Number(a.scheme.Dialog.Title_Margin_Left), D = e.contentY ?? Number(a.scheme.Dialog.Title_Margin_Top), k = C + Math.max(0, ...v.map((e) => e.height)), A = await p(e.outerWidth ?? _ + 20, Math.max(e.outerHeight ?? 0, w + D + 12, k + D + 12), t), j = s(document.createElement("div"), E, D, _, Math.max(w, k));
		if (j.className = "bf-form", A.append(j), l) await c(j, {
			width: _,
			height: w,
			bodyHeight: S,
			buttonY: C
		});
		else {
			await T(j, Number(m.PosX), Number(m.PosY), _ - 6, S, m.SkinSpriteSet);
			let e = {
				notice: 0,
				warning: 1,
				question: 2,
				error: 3
			}, t = u.children.find((t) => t.attrs.Name === "pnlIcon" && Number(t.attrs.ArrayIndex) === (e[i] ?? 0)).attrs, n = new Image();
			n.src = r() + t.SkinImage, await n.decode(), n.className = "control", s(n, Number(t.PosX), Math.floor((S - n.naturalHeight) / 2), n.naturalWidth, n.naturalHeight), j.append(n);
			let a = s(document.createElement("div"), Number(h.PosX), 0, b, S);
			a.className = "message-copy", j.append(a), await O(a, x.join("\n"), {
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
		let M = v.reduce((e, t) => e + t.width, 0) + Math.max(0, v.length - 1) * 4, N = Math.floor((_ - M) / 2);
		for (let e of v) await f(j, e.name, e.x ?? N, C, e.width, e.height, e.label, e.sprite), N += e.width + 4;
		return A;
	}
	async function S(e, t) {
		let n = a.forms[e];
		if (!n) throw Error(`Missing cn-774 layout: ${e}`);
		let r = a.scheme.Dialog, i = Number(r.Title_Margin_Left), o = Number(r.Title_Margin_Top), s = await p(Number(n.attrs.Width) + i + Number(r.Title_Margin_Right), Number(n.attrs.Height) + o + Number(r.Title_Margin_Bottom), t || n.attrs.Text), c = structuredClone(n);
		return Object.assign(c.attrs, {
			PosX: String(i),
			PosY: String(o)
		}), await y(c, s), s;
	}
	async function C() {
		let e = a.forms["Option/DlgOption4.ui"], t = a.scheme.Dialog, n = Number(t.Title_Margin_Left), r = Number(t.Title_Margin_Top), i = await p(Number(e.attrs.Width) + n + Number(t.Title_Margin_Right), Number(e.attrs.Height) + r + Number(t.Title_Margin_Bottom), e.attrs.Text), o = s(document.createElement("div"), n, r, Number(e.attrs.Width), Number(e.attrs.Height));
		o.className = "bf-form", i.append(o);
		let c = e.children.find((e) => e.attrs.Name === "pnlBackPlane"), l = [], u = 1, f = 0, m = s(document.createElement("div"), Number(c.attrs.PosX), Number(c.attrs.PosY), Number(c.attrs.Width), Number(c.attrs.Height));
		m.className = "control", o.append(m);
		async function h(e) {
			let t = ++f;
			u = e;
			for (let t = 0; t < l.length; t++) l[t].ariaSelected = String(t === e), await l[t].setSelected(t === e);
			let n = document.createElement("div");
			s(n, 0, 0, Number(c.attrs.Width), Number(c.attrs.Height)), n.className = "control", await d(n, c.attrs.SkinSpriteSet, Number(c.attrs.Width), Number(c.attrs.Height));
			let r = structuredClone(a.forms[e ? "Option/DlgOption-VideoSound3.ui" : "Option/DlgOption-Game5.ui"]);
			Object.assign(r.attrs, {
				PosX: "0",
				PosY: "0"
			});
			let i = (e) => {
				Number(e.attrs.PosY) >= 1e3 && (e.attrs.Visible = "False"), e.attrs.Name === "chkWindowMode" && (e.attrs.Check = "1"), ["tbBGMVolume", "tbEffectVolume"].includes(e.attrs.Name) && (e.attrs.Position = "75"), e.children.forEach(i);
			};
			i(r), await y(r, n), t === f && m.replaceChildren(n);
		}
		for (let t of ["btnTabMouseSense", "btnTabDisplaySound"]) {
			let n = await y(e.children.find((e) => e.attrs.Name === t), o);
			n.setAttribute("role", "tab");
			let r = l.length;
			n.onclick = () => {
				h(r);
			}, l.push(n);
		}
		for (let t of ["btnOkay", "btnCancel"]) await y(e.children.find((e) => e.attrs.Name === t), o);
		return await h(u), i;
	}
	async function w() {
		if (e === "v3") return C();
		let t = e === "v2", n = a.forms[t ? "DlgOption2.ui" : "DlgOption.ui"], r = Number(a.scheme.Dialog.Title_Margin_Left), i = Number(a.scheme.Dialog.Title_Margin_Top), o = Number(n.attrs.Width) + r + Number(a.scheme.Dialog.Title_Margin_Right), l = Number(n.attrs.Height) + i + Number(a.scheme.Dialog.Title_Margin_Bottom), u = (e) => n.children.find((t) => t.attrs.Name === e).attrs, m = u("pnlBackPlane"), h = Number(m.Height), g = await p(o, l, "选项"), _ = s(document.createElement("div"), r + Number(m.PosX), i + Number(m.PosY), Number(m.Width), h);
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
		let b = [], x = 1;
		async function S(e) {
			x = e;
			for (let t = 0; t < b.length; t++) {
				b[t].replaceChildren(), b[t].ariaSelected = String(t === e);
				let n = `Images/TaskBar/Option/OptionTab0${t + 1}.smf`;
				await c(b[t], n, a.sprites[n][t === e ? 3 : 0], 0, 0, t ? 106 : 73, 27);
			}
			if (_.replaceChildren(), await d(_, "Images/Ui/DialogInnerSkin03.smf", 295, h), e === 1) await y(v, _);
			else {
				let e = structuredClone(a.forms["DlgOption-Game.ui"]);
				Object.assign(e.attrs, {
					PosX: "0",
					PosY: "0"
				}), e.children = e.children.filter((e) => e.attrs.Name !== "chkErp"), await y(e, _, { gbMouseSense: { Text: "鼠标灵敏度调节" } });
			}
		}
		for (let e = 0; e < 2; e++) {
			let t = u(e ? "btnTabDisplaySound" : "btnTabMouseSense"), n = s(document.createElement("button"), r + Number(t.PosX), i + Number(t.PosY), Number(t.Width), Number(t.Height));
			n.className = "control sprite-button", n.ariaLabel = e ? "画面/声音设置" : "游戏设置", n.onclick = () => S(e), b.push(n), g.append(n);
		}
		await S(x);
		for (let [e, t] of [["btnOkay", "确认"], ["btnCancel", "取消"]]) {
			let n = u(e);
			await f(g, e === "btnOkay" ? "Ok" : "Cancel", r + Number(n.PosX), i + Number(n.PosY), Number(n.Width), Number(n.Height), t);
		}
		return g;
	}
	async function T(t, n, r, i, a, o = "Images/Ui/FrameBox.smf") {
		e === "v3" && (o = {
			"Images/Ui/FrameBox.smf": "Images/Ui/dialogueNew/dialogue_basic02.smf",
			"Images/Ui/DialogInnerSkin03.smf": "Images/Ui/dialogueNew/dialogue_basic02.smf",
			"Images/Ui/DialogInnerSkin06.smf": "Images/Ui/dialogueNew/dialogue_basic03.smf"
		}[o] || o);
		let c = s(document.createElement("div"), n, r, i, a);
		return c.className = "control", await d(c, o, i, a), t.append(c), c;
	}
	let E = {
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
	async function D(e, t, n, r, i, a, o = !1) {
		let c = s(document.createElement("span"), n, r, i, a);
		return c.className = "control", c.textContent = t, e.append(c), await O(c, t, {
			family: E.font,
			size: o ? 12 : E.size,
			weight: 400,
			color: o ? E.muted : E.color,
			nativeMetrics: !0,
			vertical: "middle",
			inset: 0
		}), c;
	}
	async function k(e, t, n, r, i) {
		return T(e, t, n, r, i, E.panel);
	}
	async function M(e, t, n, r, i, a) {
		let o = await T(e, n, r, i, a, E.group);
		o.dataset.bfSettingsGroup = "";
		let c = s(document.createElement("span"), E.groupX, E.groupY, i - E.groupX - 6, E.groupHeight);
		return c.className = "control", o.append(c), await O(c, t, {
			family: "BF",
			size: E.groupSize,
			weight: 400,
			color: E.groupText,
			nativeMetrics: !0,
			vertical: E.groupAlign,
			inset: 0
		}), o;
	}
	async function N(t, n, r, i, a, o) {
		let c = s(document.createElement("button"), r, i, a, o);
		c.className = "control sprite-button bf-text-tab", c.dataset.theme = e, c.ariaLabel = n, c.setAttribute("role", "tab"), t.append(c);
		let l = s(document.createElement("span"), 0, 0, a, o), u = s(document.createElement("span"), 0, 0, a, o);
		return l.className = "bf-tab-active", u.className = "bf-tab-inactive", c.append(l, u), await Promise.all([[l, E.tab], [u, E.inactive]].map(([e, t]) => O(e, n, {
			family: E.font,
			size: 13,
			weight: 400,
			color: t,
			nativeMetrics: !0,
			vertical: "middle",
			align: "center",
			inset: 0
		}))), c.setActive = (e) => {
			c.ariaSelected = String(e), c.tabIndex = e ? 0 : -1;
		}, c.setActive(!1), c;
	}
	function F(t, n, r, i, a, o, c = e === "v3" ? "#bbdde0" : "#5b5b5b", l = {}) {
		let u = s(document.createElement("span"), r, i, a, o);
		return u.className = "control bf-label", u.textContent = n, u.style.color = c, t.append(u), O(u, n, {
			color: c,
			nativeMetrics: !0,
			vertical: "middle",
			wrap: n.length > 40,
			...l
		}), u;
	}
	async function I(t, n, r, i, a = !1) {
		await y({
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
		}, t), F(t, n, r + 30, i + 1, 100, 20, e === "v3" ? "#bbdde0" : "#5b5b5b", {
			size: 11,
			family: "BF"
		});
	}
	async function L() {
		if (e === "v3") return S("Stages/Lobby/DlgCreateRoom.ui", "创建房间");
		let t = e === "v2", n = t ? 694 : 460, r = await p(450, n, "创建房间");
		await T(r, 9, 36, 431, 63), F(r, "房间名称", 24, 43, 60, 23), F(r, t ? "非公开" : "密码", 39, 72, 50, 20), await I(r, "", 15, 75);
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
			let t = s(document.createElement("input"), e, n, i, 23);
			t.className = "control bf-edit", t.value = a, t.ariaLabel = n === 42 ? "房间名称" : "密码", r.append(t);
		}
		F(r, t ? "输入6位以内的字符" : "最多6个字符", 204, 72, 180, 20, "#258cea"), await T(r, 9, 105, 431, n - 149);
		let i = a.forms["DlgCreateRoom.ui"].children.filter((e) => e.type === "UiButton" && e.attrs.Name === "btnGameMode"), o = [], c = t ? [
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
		for (let e = 0; e < c.length; e++) {
			let n = i.find((t) => t.attrs.SpriteSet.includes(c[e]));
			if (!n) continue;
			let a = await f(r, "Mode", 16, 113 + e * 49, 190, 51, c[e], n.attrs.SpriteSet);
			o.push(a), await a.setSelected(e === (t ? 2 : 0)), a.onclick = async () => {
				for (let e of o) await e.setSelected(e === a);
			};
		}
		if (await T(r, t ? 207 : 205, t ? 326 : 145, 228, t ? 321 : 262, "Images/Ui/DialogInnerSkin06.smf"), t) {
			await T(r, 207, 111, 228, 114, "Images/Ui/DialogInnerSkin06.smf"), F(r, "游戏选项", 214, 116, 100, 20, "#2a7ed4");
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
			]) await I(r, e, t, n);
			await T(r, 207, 229, 228, 94, "Images/Ui/DialogInnerSkin06.smf"), F(r, "房间选项", 214, 234, 100, 20, "#2a7ed4"), await I(r, "随机组", 214, 257), await I(r, "闯入", 214, 278, !0), F(r, "模式说明", 214, 331, 100, 20, "#2a7ed4");
		}
		let l = F(r, "只使用泡泡枪的基本规则：“泡封”、“击爆”、“营救”来展现自身实力的游戏模式。要精确击中对方和及时解救我方，才能获取胜利哦。", t ? 211 : 208, t ? 351 : 151, t ? 217 : 218, 100, "#5b5b5b", {
			vertical: "top",
			size: 12
		});
		return l.style.alignItems = "flex-start", l.style.lineHeight = "15px", await f(r, "Ok", t ? 157 : 298, n - 35, t ? 65 : 67, t ? 27 : 25, "确认"), await f(r, "Cancel", t ? 226 : 373, n - 35, t ? 65 : 68, t ? 27 : 25, "取消"), r;
	}
	async function R(e) {
		return P(B, e);
	}
	async function z() {
		let t = e !== "v1", n = t ? 300 : 320, i = t ? 60 : 82, o = s(document.createElement("div"), 0, 0, n, i);
		o.className = "bf-form", await d(o, t ? "Images/Ui/dialogue_Notify_Back00.smf" : "Images/Ui/NotifyBox.smf", n, i);
		let c = s(document.createElement("span"), 18, 8, n - 36, i - 16);
		c.className = "control", o.append(c);
		let l = a.scheme.NotifyBox;
		if (await O(c, t ? "9不在线" : "9不在线。", {
			size: t ? 14 : Number(l.FontSize),
			weight: t ? 400 : Number(l.FontWeight),
			family: "BF",
			align: "center",
			color: t ? "white" : "#111",
			outline: t ? "#168ac2" : "#888",
			outlineWidth: t ? 2 : .75,
			decoration: t ? 4 : Number(l.TextDecoration)
		}), t) {
			let e = "Images/Ui/Notify_Progress.smf", t = a.sprites[e], c = 0, l = async () => {
				o.querySelectorAll(".notify-progress").forEach((e) => e.remove());
				let t = s(document.createElement("span"), n - 28, i - 28, 20, 20);
				t.className = "tile notify-progress";
				let a = new Image();
				a.src = r() + e.replace(/\.smf$/i, "") + "/" + c + ".png", Object.assign(a.style, {
					width: "100%",
					height: "100%",
					left: "0",
					top: "0"
				}), t.append(a), o.append(t);
			};
			await l(), window.setInterval(() => {
				c = (c + 1) % t.length, l();
			}, 180);
		}
		return o;
	}
	let B = {
		theme: e,
		manifest: a,
		sounds: n,
		trackBar: (e, t) => y({
			type: "UiTrackBar",
			attrs: t,
			children: []
		}, e),
		groupBox: (e, t) => y({
			type: "UiGroupBox",
			attrs: t,
			children: []
		}, e),
		windowShell: p,
		renderNode: y,
		button: f,
		panel: T,
		label: F,
		checkbox: I,
		dialog: _,
		settings: w,
		room: L,
		message: z,
		customSettings: R,
		settingsLabel: D,
		settingsPanel: k,
		settingsGroup: M,
		textTab: N
	};
	return {
		...B,
		extensionSettings: (e) => P(B, e)
	};
}
// ../tools/game-ui/src/context.ts
var I = Symbol("bf-ui");
function L(e, t, n = () => !0, r = () => w) {
	let i = g(), a = g(), o = 0, s, c;
	return y(() => [e(), t()], async ([e, t]) => {
		let l = ++o;
		a.value = void 0;
		try {
			c !== t && (s?.dispose(), c = t, s = E({
				resourceRoot: t,
				enabled: n(),
				volume: r(),
				onError: (e) => {
					a.value = e;
				}
			}));
			let u = s;
			if (await Promise.all([z(t), n() ? u.preload() : Promise.resolve()]), l !== o) return;
			let d = await F({
				theme: e,
				resourceRoot: t,
				sounds: u
			});
			l === o && (i.value = d);
		} catch (e) {
			l === o && (a.value = e instanceof Error ? e : Error(String(e)));
		}
	}, { immediate: !0 }), y(n, (e) => s?.setEnabled(e)), y(r, (e) => {
		try {
			s?.setVolume(e);
		} catch (e) {
			a.value = e instanceof Error ? e : Error(String(e));
		}
	}), d(() => {
		o++, s?.dispose();
	}), p(I, i), {
		renderer: i,
		error: a
	};
}
function R() {
	let e = l(I);
	if (!e) throw Error("Game UI controls require BfUiProvider");
	return n(() => e.value);
}
async function z(e) {
	let t = e.replace(/\/?$/, "/");
	await Promise.all([["BF", "GB13000.ttf"], ["BFMalgun", "DF_GB_Y9.TTF"]].map(async ([e, n]) => {
		if (document.fonts.check(`13px ${e}`) && Array.from(document.fonts).some((t) => t.family === e)) return;
		let r = await new FontFace(e, `url("${t}fonts/${n}")`, { weight: "400" }).load();
		document.fonts.add(r);
	}));
}
// ../tools/game-ui/src/components/BfUiProvider.vue?vue&type=script&setup=true&lang.ts
var B = { role: "alert" }, V = /* @__PURE__ */ c({
	__name: "BfUiProvider",
	props: {
		theme: { default: "v2" },
		clientDate: {},
		resourceRoot: { default: "/local/" },
		sound: {
			type: Boolean,
			default: !0
		},
		volume: { default: w }
	},
	emits: ["error"],
	setup(e, { expose: t, emit: n }) {
		let r = e, i = n, { renderer: a, error: s } = L(() => D(r.clientDate, r.theme), () => r.resourceRoot, () => r.sound, () => r.volume);
		return t({
			unlockSound: () => a.value?.sounds?.unlock(),
			playSound: (e) => a.value?.sounds?.play(e)
		}), y(s, (e) => {
			e && i("error", e);
		}), (e, t) => v(a) ? h(e.$slots, "default", {}, void 0, void 0, 0) : v(s) ? h(e.$slots, "error", { error: v(s) }, () => [o("div", B, _(v(s).message), 1)], void 0, 1) : h(e.$slots, "loading", {}, void 0, void 0, 2);
	}
}), H = /* @__PURE__ */ c({
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
		let l = n, p = c, g = R(), _ = m(), v = m(), b = 0, x;
		y([
			g,
			() => l.revision,
			_
		], async () => {
			let e = ++b;
			if (x?.(), x = void 0, v.value = void 0, _.value?.replaceChildren(), !g.value || !_.value) return;
			let t = g.value;
			try {
				let n = await l.draw(t);
				if (e !== b) return;
				_.value.replaceChildren(n), v.value = n, x = t.sounds?.bind(n), await u();
			} catch (t) {
				e === b && p("error", t instanceof Error ? t : Error(String(t)));
			}
		}, { flush: "post" }), d(() => {
			b++, x?.(), v.value = void 0;
		}), s({ surface: v });
		function S(e, t) {
			let n = e.detail;
			p(t === "action" ? "action" : "change", n);
		}
		return (n, s) => (f(), a(e, null, [o("div", {
			ref_key: "host",
			ref: _,
			class: "bf-native-host",
			onBfAction: s[0] ||= (e) => S(e, "action"),
			onBfChange: s[1] ||= (e) => S(e, "change")
		}, null, 544), v.value ? (f(), r(t, {
			key: 0,
			to: v.value
		}, [h(n.$slots, "default")], 8, ["to"])) : i("v-if", !0)], 64));
	}
}), U = /* @__PURE__ */ c({
	__name: "BfWindow",
	props: {
		title: {},
		width: {},
		height: {}
	},
	emits: [
		"close",
		"action",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t;
		function a(e) {
			e.name === "close" && i("close"), i("action", e);
		}
		return (t, o) => (f(), r(H, {
			revision: [
				n.title,
				n.width,
				n.height
			],
			draw: (t) => t.windowShell(e.width, e.height, e.title),
			onAction: a,
			onError: o[0] ||= (e) => i("error", e)
		}, {
			default: b(() => [h(t.$slots, "default")]),
			_: 3
		}, 8, ["revision", "draw"]));
	}
}), W = /* @__PURE__ */ c({
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
		return (i, a) => (f(), r(H, {
			revision: [
				t.x,
				t.y,
				t.width,
				t.height,
				t.skin
			],
			draw: (t) => t.panel(n(), e.x, e.y, e.width, e.height, e.skin)
		}, {
			default: b(() => [h(i.$slots, "default")]),
			_: 3
		}, 8, ["revision", "draw"]));
	}
}), G = /* @__PURE__ */ c({
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
		let o = m(), s = n(() => {
			let { Check: e, Position: t, ...n } = i.attrs;
			return JSON.stringify([i.type, n]);
		});
		y(() => [i.attrs.Check, i.attrs.Position], () => {
			(o.value?.surface)?.setValue?.(i.type === "UiCheckBox" ? i.attrs.Check === "1" : Number(i.attrs.Position));
		}, { flush: "post" });
		let c = t;
		return (t, n) => (f(), r(H, {
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
			default: b(() => [h(t.$slots, "default")]),
			_: 3
		}, 8, ["revision", "draw"]));
	}
}), K = /* @__PURE__ */ c({
	__name: "BfGroupBox",
	props: {
		title: {},
		x: {},
		y: {},
		width: {},
		height: {}
	},
	setup(e) {
		let t = e, i = R(), a = n(() => {
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
		return (e, t) => (f(), r(G, {
			type: "UiGroupBox",
			attrs: a.value
		}, {
			default: b(() => [h(e.$slots, "default")]),
			_: 3
		}, 8, ["attrs"]));
	}
}), q = /* @__PURE__ */ c({
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
		return (e, t) => (f(), r(G, {
			type: "UiLabel",
			attrs: i.value
		}, null, 8, ["attrs"]));
	}
}), J = /* @__PURE__ */ c({
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
		return (n, r) => (f(), a(e, null, [s(G, {
			type: "UiCheckBox",
			attrs: c.value,
			onChange: l
		}, null, 8, ["attrs"]), s(q, {
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
}), Y = /* @__PURE__ */ c({
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
		return (e, t) => (f(), r(G, {
			type: "UiTrackBar",
			attrs: o.value,
			onChange: s
		}, null, 8, ["attrs"]));
	}
}), X = /* @__PURE__ */ c({
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
		return (t, o) => (f(), r(H, {
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
}), Z = /* @__PURE__ */ c({
	__name: "BfGamePage",
	props: { page: {} },
	emits: [
		"action",
		"change",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t;
		return (t, a) => (f(), r(H, {
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
}), Q = /* @__PURE__ */ c({
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
		openSound: { type: Boolean }
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
			openSound: n.openSound
		});
		return (e, t) => (f(), r(H, {
			revision: JSON.stringify({
				title: n.title,
				text: n.text,
				icon: n.icon,
				contentWidth: n.contentWidth,
				contentHeight: n.contentHeight,
				outerWidth: n.outerWidth,
				outerHeight: n.outerHeight,
				buttonY: n.buttonY,
				openSound: n.openSound
			}),
			draw: a,
			onAction: t[0] ||= (e) => i("action", e),
			onChange: t[1] ||= (e) => i("change", e),
			onError: t[2] ||= (e) => i("error", e)
		}, null, 8, ["revision"]));
	}
}), $ = /* @__PURE__ */ c({
	__name: "BfMessageBox",
	props: {
		title: { default: "公告" },
		text: {},
		icon: { default: "notice" },
		buttons: {},
		openSound: { type: Boolean }
	},
	emits: [
		"action",
		"change",
		"error"
	],
	setup(e, { emit: t }) {
		let n = e, i = t;
		return (e, t) => (f(), r(Q, {
			title: n.title,
			text: n.text,
			icon: n.icon,
			buttons: n.buttons,
			"open-sound": n.openSound,
			onAction: t[0] ||= (e) => i("action", e),
			onChange: t[1] ||= (e) => i("change", e),
			onError: t[2] ||= (e) => i("error", e)
		}, null, 8, [
			"title",
			"text",
			"icon",
			"buttons",
			"open-sound"
		]));
	}
}), ee = /* @__PURE__ */ c({
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
		let n = e, i = t, a = N(n.modelValue), o = m(0);
		y(() => n.modelValue, (e) => {
			JSON.stringify(e) !== JSON.stringify(a) && (a = N(e), o.value++);
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
		return (e, t) => (f(), r(H, {
			revision: o.value,
			draw: c,
			onAction: l,
			onError: t[0] ||= (e) => i("error", e)
		}, null, 8, ["revision"]));
	}
});
// ../tools/game-ui/tutorial-model.js
function te(e, t, n = 0) {
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
function ne(e, t) {
	return e.phase !== "typing" || t < e.deadline ? "idle" : e.visible < e.text.length ? (e.visible++, e.deadline = t + e.interval, "character") : (e.phase = "complete", e.finishedAt = t, "complete");
}
function re(e, t) {
	return e.phase === "typing" ? (e.visible = e.text.length, e.phase = "complete", e.finishedAt = t, "reveal") : "advance";
}
function ie(e, t, n = 15) {
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
function ae(e, t, n = !1) {
	if (!t) return;
	let r = e.filter((e) => e.stage === t.stage), i = r.find((e) => String(e.progress) === t.nextProgress);
	if (i) return i;
	if (n && r.length) return r[(r.findIndex((e) => e.progress === t.progress) + 1) % r.length];
}
// ../tools/game-ui/src/components/BfTutorialDialogue.vue?vue&type=script&setup=true&lang.ts
var oe = [
	"data-status",
	"data-progress",
	"data-visible-count"
], se = ["src", "alt"], ce = ["src"], le = ["aria-label"], ue = ["onKeydown"], de = /* @__PURE__ */ c({
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
		let s = e, c = r, l = R(), u = m(), p = m("loading"), h = m(""), g = n(() => s.resourceRoot.replace(/\/?$/, "/")), _ = (e) => `${g.value}Images/TutorialStage/${e}`, b, C = {}, w, T, E, D = 0, O = 0, k = 0, A = !1, j, M = {}, N = /* @__PURE__ */ new Set();
		async function P(e) {
			let t = await fetch(g.value + e);
			if (!t.ok) throw Error(`Tutorial resource ${e}: ${t.status}`);
			return t.json();
		}
		async function F() {
			let e = ++k;
			try {
				let [t, n] = await Promise.all([P("manifest.json"), P("glyphs.json")]);
				if (A || e !== k) return;
				let r = new Image();
				if (r.src = g.value + "glyphs.png", await r.decode(), A || e !== k || (b = t, C = n.glyphs, w = r, s.sound && (j ??= new AudioContext(), j.state === "running" && await z()), A || e !== k)) return;
				p.value = "ready", c("ready", t.dialogues), H();
			} catch (t) {
				!A && e === k && (p.value = "error", c("error", t instanceof Error ? t : Error(String(t))));
			}
		}
		function I() {
			let e = u.value?.getContext("2d");
			if (!e || !T || !w) return;
			e.clearRect(0, 0, 250, 100), e.imageSmoothingEnabled = !1;
			let t = T.text.slice(0, T.visible);
			h.value = t;
			let n = ie(t, C, 15), r = 50 - Math.floor(n.height / 2);
			for (let t of n.lines) {
				let n = 0;
				for (let i of t.text) {
					let a = C[i];
					a ? (a.width && a.height && e.drawImage(w, a.x, a.y, a.width, a.height, n + a.left, r + t.y + 14 - a.top, a.width, a.height), n += a.advance) : (e.font = "400 15px BF", e.fillStyle = "#222222", e.textBaseline = "alphabetic", e.fillText(i, n, r + t.y + 14), n += Math.round(e.measureText(i).width));
				}
			}
		}
		function L(e) {
			if (!s.sound || j?.state !== "running" || !M[e]) return;
			if (N.size >= 8) {
				let e = N.values().next().value;
				e?.stop(), e && N.delete(e);
			}
			let t = j.createBufferSource();
			t.buffer = M[e], t.connect(j.destination), N.add(t), t.onended = () => {
				N.delete(t), t.disconnect();
			}, t.start();
		}
		async function z() {
			if (s.sound && (j ??= new AudioContext(), await j.resume(), !M.type)) {
				let e = j, t = await Promise.all(["type", "textbox"].map(async (t) => {
					let n = await fetch(`${g.value}Sounds/ui-tut-${t}.wav`);
					if (!n.ok) throw Error(`Tutorial sound ${t}: ${n.status}`);
					return [t, await e.decodeAudioData(await n.arrayBuffer())];
				}));
				if (A) return;
				M = Object.fromEntries(t);
			}
		}
		function B(e) {
			if (A || !T) return;
			let t = ne(T, e);
			t === "character" && (I(), L("type")), t === "complete" && c("complete"), s.autoplay && T.phase === "complete" && E && E.holdMs >= 0 && e - (T.finishedAt ?? e) >= E.holdMs ? (p.value = "complete", c("advance", V())) : D = requestAnimationFrame(B);
		}
		function V() {
			return ae(b?.dialogues ?? [], E, s.loop);
		}
		function H() {
			b && u.value && (cancelAnimationFrame(D), O++, E = b.dialogues.find((e) => e.stage === s.stage && e.progress === s.progress), !E && s.text === void 0 ? c("error", Error(`Missing tutorial page ${s.stage}/${s.progress}`)) : (T = te(s.text ?? E.text, b.textPrintSpeed, performance.now()), I(), s.sound && (j?.state !== "running" || !M.type) ? p.value = "awaiting-audio" : (p.value = "playing", L("textbox"), D = requestAnimationFrame(B))));
		}
		async function U() {
			try {
				await z(), A || H();
			} catch (e) {
				c("error", e instanceof Error ? e : Error(String(e)));
			}
		}
		async function W() {
			let e = O;
			try {
				if (await z(), A || e !== O || !T || E?.clickComplete === !1) return;
				if (p.value === "awaiting-audio") {
					H();
					return;
				}
				re(T, performance.now()) === "reveal" ? (I(), L("type"), c("complete")) : c("advance", V());
			} catch (e) {
				c("error", e instanceof Error ? e : Error(String(e)));
			}
		}
		return y([
			u,
			l,
			() => s.stage,
			() => s.progress,
			() => s.text,
			() => s.character
		], () => H(), { flush: "post" }), y(() => s.sound, () => H()), y(g, () => {
			p.value = "loading", cancelAnimationFrame(D), b = void 0, T = void 0, w = void 0, h.value = "", M = {};
			for (let e of N) e.stop();
			N.clear(), F();
		}, { immediate: !0 }), d(() => {
			A = !0, cancelAnimationFrame(D);
			for (let e of N) e.stop();
			N.clear(), j?.close();
		}), t({
			start: U,
			restart: H,
			status: p
		}), (t, n) => (f(), a("div", {
			class: "bf-tutorial-dialogue",
			"data-status": p.value,
			"data-progress": e.progress,
			"data-visible-count": v(T)?.visible ?? 0,
			onClick: W
		}, [
			o("img", {
				class: "bf-tutorial-portrait",
				src: _(e.character + ".png"),
				alt: e.character,
				draggable: "false"
			}, null, 8, se),
			o("img", {
				class: "bf-tutorial-balloon",
				src: _("ChatBalloonNew.png"),
				alt: "",
				draggable: "false"
			}, null, 8, ce),
			o("canvas", {
				ref_key: "canvas",
				ref: u,
				class: "bf-tutorial-copy",
				width: "250",
				height: "100",
				role: "img",
				"aria-label": h.value
			}, null, 8, le),
			p.value === "awaiting-audio" ? (f(), a("button", {
				key: 0,
				class: "bf-tutorial-start",
				onClick: S(U, ["stop"])
			}, "点击开始教学（播放声音）")) : i("v-if", !0),
			o("button", {
				class: "bf-tutorial-skip",
				"aria-label": "跳过对话",
				onKeydown: [x(S(W, ["prevent"]), ["enter"]), x(S(W, ["prevent"]), ["space"])]
			}, null, 40, ue)
		], 8, oe));
	}
});
export { X as BfButton, J as BfCheckbox, G as BfControl, Q as BfDialog, ee as BfExtensionSettings, Z as BfGamePage, K as BfGroupBox, q as BfLabel, $ as BfMessageBox, W as BfPanel, Y as BfSlider, de as BfTutorialDialogue, V as BfUiProvider, U as BfWindow, w as DEFAULT_UI_VOLUME, C as UI_SOUND_EVENTS, E as createUiSoundPlayer, T as effectVolumeGain, D as themeForClientDate };
