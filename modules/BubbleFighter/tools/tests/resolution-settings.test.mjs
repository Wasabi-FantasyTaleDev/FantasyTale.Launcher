import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const html = readFileSync(new URL('../../games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/settings.html', import.meta.url), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];

function fixture(reducedMotion = true) {
  const timers = new Map(); let timerSequence = 0;
  class Element {
    constructor() {
      this.children = []; this.listeners = {}; this.value = ''; this.textContent = ''; this.disabled = false; this.hidden = false; this.style = {};
      const classes = new Set();
      this.classList = { add: value => classes.add(value), remove: value => classes.delete(value), contains: value => classes.has(value), toggle: (value, force) => { if (force) classes.add(value); else classes.delete(value); } };
    }
    setAttribute() {}
    addEventListener(type, callback) { this.listeners[type] = callback; }
    replaceChildren(...children) { this.children = children; }
    append(...children) { for (const child of children) { child.parent = this; this.children.push(child); } }
    remove() { this.parent.children = this.parent.children.filter(child => child !== this); }
    getBoundingClientRect() { return { height: 38 }; }
    focus() {}
    click() { if (!this.disabled) this.listeners.click?.(); }
  }
  const elements = Object.fromEntries(['rows', 'reset', 'empty-state', 'width', 'height', 'add', 'error', 'empty', 'caption', 'hint', 'width-label', 'height-label'].map(id => [id, new Element()]));
  for (const id of ['width', 'height', 'add']) elements[id].disabled = true;
  const messages = [], listeners = {}, parent = { postMessage: message => messages.push(JSON.parse(JSON.stringify(message))) };
  const document = { getElementById: id => elements[id], createElement: () => new Element(), documentElement: {} };
  runInNewContext(script, { document, parent, TextEncoder, window: { addEventListener: (type, callback) => { listeners[type] = callback; }, matchMedia: () => ({ matches: reducedMotion }), setTimeout: callback => { const id = ++timerSequence; timers.set(id, callback); return id; }, clearTimeout: id => timers.delete(id) } });
  const configuration = (text = '', language = 'en', maxLength = 4096) => ({ type: 'fantasytale:configuration', schema: 1, language, options: [{ key: 'custom_resolutions', type: 'string', maxLength, label: { en: 'Additional resolutions' }, description: { en: 'Original choices are preserved.' } }], values: { custom_resolutions: text } });
  const receive = (data, source = parent) => listeners.message({ source, data });
  const initialize = (...args) => receive(configuration(...args));
  const add = (width, height) => { elements.width.value = String(width); elements.height.value = String(height); elements.add.click(); };
  return { elements, messages, configuration, receive, initialize, add, document, flush: () => { for (const [id, callback] of [...timers]) { timers.delete(id); callback(); } } };
}

test('waits for the declared host option and rejects unrelated messages', () => {
  const f = fixture();
  assert.deepEqual(f.messages, [{ type: 'fantasytale:ready', schema: 1 }]);
  f.receive(f.configuration(), {});
  f.receive({ ...f.configuration(), schema: 2 });
  f.receive({ ...f.configuration(), options: [] });
  f.receive({ ...f.configuration(), values: [] });
  assert.equal(f.elements.add.disabled, true);
  f.initialize();
  assert.equal(f.elements.add.disabled, false);
  assert.equal(f.elements['width-label'].textContent, 'Width (px)');
});

test('adds and removes draft entries without bypassing launcher Save', () => {
  const f = fixture(); f.initialize('800x600'); f.add(1920, 1080);
  assert.equal(f.elements.rows.children.length, 2);
  assert.deepEqual(f.messages.at(-1), { type: 'fantasytale:change', schema: 1, values: { custom_resolutions: '800x600;1920x1080' } });
  f.elements.rows.children[0].children[0].children[0].children[1].click();
  assert.equal(f.messages.at(-1).values.custom_resolutions, '1920x1080');
  f.elements.rows.children[0].children[0].children[0].children[1].click();
  assert.equal(f.messages.at(-1).values.custom_resolutions, '');
  assert.equal(f.elements.empty.hidden, false);
});

test('host echoes preserve the resolution currently being typed', () => {
  const f = fixture(); f.initialize(); f.add(1920, 1080);
  f.elements.width.value = '2560'; f.elements.height.value = '1440';
  f.initialize(f.messages.at(-1).values.custom_resolutions);
  assert.equal(f.elements.width.value, '2560'); assert.equal(f.elements.height.value, '1440');
  let prevented = false;
  f.elements.height.listeners.keydown({ key: 'Enter', preventDefault: () => { prevented = true; } });
  assert.equal(prevented, true);
  assert.equal(f.messages.at(-1).values.custom_resolutions, '1920x1080;2560x1440');
});

test('rejects duplicate, incomplete and out-of-range dimensions', () => {
  const f = fixture(); f.initialize('1920x1080');
  for (const pair of [[1920,1080], [0,600], [-1,600], [1.5,600], ['1e3',600], ['',600], [2147483648,600]]) {
    f.add(...pair); assert.notEqual(f.elements.error.textContent, '');
    assert.equal(f.elements.rows.children.length, 1); assert.equal(f.messages.length, 1);
  }
  f.add(2147483647,2147483647);
  assert.equal(f.elements.rows.children.length, 2);
});

test('supports saved separators and all six display languages', () => {
  const f = fixture();
  const captions = { en: 'Add', 'zh-CN': '添加', 'zh-TW': '新增', ko: '추가', id: 'Tambah', vi: 'Thêm' };
  for (const [language, caption] of Object.entries(captions)) {
    f.initialize(' 1920 x 1080;2560X1440,800x600\r\n', language);
    assert.equal(f.elements.add.textContent, caption);
    assert.equal(f.elements.rows.children.length, 3);
    assert.equal(f.document.documentElement.lang, language);
  }
  f.initialize('', 'unknown'); assert.equal(f.elements.add.textContent, 'Add');
});

test('invalid saved data cannot be silently replaced through stale remove buttons', () => {
  const f = fixture(); f.initialize('1920x1080');
  for (const invalid of ['bad', '0x600', '1920x1080junk', '1x1;'.repeat(257), ' '.repeat(4097)]) {
    f.initialize(invalid);
    assert.equal(f.elements.add.disabled, true); assert.notEqual(f.elements.error.textContent, '');
    f.elements.rows.children[0].children[0].children[0].children[1].click(); assert.equal(f.messages.length, 1);
  }
  f.initialize('1280x720'); assert.equal(f.elements.add.disabled, false);
  f.elements.rows.children[0].children[0].children[0].children[1].click(); assert.equal(f.messages.at(-1).values.custom_resolutions, '');
});

test('enforces entry and byte limits while allowing removal at the boundary', () => {
  const f = fixture(); f.initialize('1x1;'.repeat(256)); f.add(1920,1080);
  assert.equal(f.messages.length, 1); assert.notEqual(f.elements.error.textContent, '');
  f.elements.rows.children[0].children[0].children[0].children[1].click();
  assert.equal(f.elements.rows.children.length, 255); assert.equal(f.messages.length, 2);
  const g = fixture(); g.initialize('', 'en', 8); g.add(1920,1080); assert.equal(g.messages.length, 1);
  g.add(800,600); assert.equal(g.messages.at(-1).values.custom_resolutions, '800x600');
  g.add(1,1); assert.equal(g.elements.rows.children.length, 1);
});


test('retains existing rows through host echoes and completes animated removal without losing draft updates', () => {
  const f = fixture(false); f.initialize('800x600');
  const first = f.elements.rows.children[0];
  f.add(1920,1080);
  assert.equal(f.elements.rows.children[0], first);
  const second = f.elements.rows.children[1];
  f.initialize('800x600;1920x1080');
  assert.equal(f.elements.rows.children[1], second);
  second.children[0].children[0].children[1].click();
  assert.equal(f.messages.at(-1).values.custom_resolutions, '800x600');
  assert.equal(f.elements.rows.children.length, 2);
  assert.equal(second.classList.contains('collapsed'), true);
  assert.equal(second.inert, true);
  f.initialize('800x600');
  assert.equal(f.elements.rows.children.length, 2);
  f.flush();
  assert.equal(f.elements.rows.children.length, 1);
  assert.equal(f.elements.rows.children[0], first);
});

test('readding a leaving resolution reverses its exit instead of dropping the restored entry', () => {
  const f = fixture(false); f.initialize('800x600');
  const row = f.elements.rows.children[0];
  row.children[0].children[0].children[1].click();
  assert.equal(f.elements['empty-state'].classList.contains('collapsed'), false);
  f.add(800,600); f.flush();
  assert.equal(f.elements.rows.children.length, 1);
  assert.equal(f.elements.rows.children[0], row);
  assert.equal(row.classList.contains('collapsed'), false);
  assert.equal(row.inert, false);
  assert.equal(f.messages.at(-1).values.custom_resolutions, '800x600');
});


test('a page-only editor can explicitly clear an invalid draft without bypassing Save', () => {
  const f = fixture(); f.initialize('invalid');
  assert.equal(f.elements.reset.hidden, false);
  f.elements.reset.click();
  assert.equal(f.messages.at(-1).type, 'fantasytale:change');
  assert.equal(f.messages.at(-1).values.custom_resolutions, '');
  assert.equal(f.elements.reset.hidden, true);
  assert.equal(f.elements.add.disabled, false);
  f.add(1920,1080);
  assert.equal(f.messages.at(-1).values.custom_resolutions, '1920x1080');
});
