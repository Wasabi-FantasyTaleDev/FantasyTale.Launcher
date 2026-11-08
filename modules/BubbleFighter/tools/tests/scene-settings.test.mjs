import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

const html = readFileSync(new URL('../../games/BubbleFighter/plugins/bubblefighter.fantasy.scene-styles/settings.html', import.meta.url), 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];

function fixture() {
  class Element {
    constructor() { this.children = []; this.attributes = {}; }
    append(...children) { this.children.push(...children); }
    replaceChildren() { this.children = []; }
    setAttribute(name, value) { this.attributes[name] = value; }
    querySelectorAll(selector) {
      return this.children.flatMap(child => [child, ...child.querySelectorAll(selector)])
        .filter(child => selector === 'input[type=radio]' ? child.type === 'radio' : child.type === 'checkbox');
    }
  }
  const elements = { intro: new Element(), list: new Element() }, listeners = {}, messages = [];
  const parent = { postMessage: message => messages.push(JSON.parse(JSON.stringify(message))) };
  const document = { getElementById: id => elements[id], createElement: () => new Element(), documentElement: { dataset: {}, scrollHeight: 500 } };
  runInNewContext(script, { parent, document, window: { addEventListener: (type, listener) => { listeners[type] = listener; } }, ResizeObserver: class { observe() {} } });
  const configure = (values = {}, language = 'zh-CN', source = parent) => listeners.message({ source, data: { schema: 1, type: 'fantasytale:configuration', values, language, theme: 'dark' } });
  const input = (key, value) => [...elements.list.querySelectorAll('input[type=radio]'), ...elements.list.querySelectorAll('input[type=checkbox]')].find(row => row.name === key && (value === undefined || row.value === value));
  const select = (key, value) => { const row = input(key, value); row.checked = true; row.onchange(); };
  const toggle = (key, checked) => { const row = input(key); row.checked = checked; row.onchange(); };
  return { configure, input, select, toggle, elements, messages };
}

test('keeps both switches enabled by default and places them before independent outfits', () => {
  const f = fixture(); f.configure();
  assert.equal(f.input('christmas_bgm').checked, true);
  assert.equal(f.input('christmas_npcs').checked, true);
  assert.deepEqual(f.elements.list.children.map(row => row.children[0].textContent), ['大厅', '广场', '下雪时使用圣诞BGM', '为糯米和贝尼穿上圣诞服装', '糯米的服装', '贝尼的服装']);
  assert.equal(f.input('lomi_outfit', 'default').checked, true);
  assert.equal(f.input('pini_outfit', 'default').checked, true);
  assert.deepEqual(f.elements.list.children[4].children[1].children.map(choice => choice.children[2].textContent), ['默认', '原版', '圣诞']);
  assert.deepEqual(f.elements.list.children[5].children[1].children.map(choice => choice.children[2].textContent), ['默认', '原版', '圣诞', '小僵尸']);
});

test('manual Default restores the client outfit and survives an acknowledgement while the plaza stays Snow', () => {
  const f = fixture(); f.configure(); f.select('plaza_style', 'snow');
  f.select('lomi_outfit', 'default'); f.select('pini_outfit', 'default');
  const saved = f.messages.at(-1).values;
  f.configure(saved);
  assert.equal(f.input('lomi_outfit', 'default').checked, true);
  assert.equal(f.input('pini_outfit', 'default').checked, true);
  assert.equal(saved.plaza_style, 'snow');
});

for (const scene of ['main_style', 'plaza_style']) test(`selects Christmas on ${scene} snow and preserves a later manual zombie selection through echoes`, () => {
  const f = fixture(); f.configure(); f.select(scene, 'snow');
  assert.equal(f.messages.at(-1).values.lomi_outfit, 'christmas');
  assert.equal(f.messages.at(-1).values.pini_outfit, 'christmas');
  const focused = f.input('pini_outfit', 'zombie');
  f.select('pini_outfit', 'zombie'); f.configure(f.messages.at(-1).values);
  assert.equal(f.input('pini_outfit', 'zombie'), focused);
  assert.equal(focused.checked, true);
  assert.equal(f.input('lomi_outfit', 'christmas').checked, true);
  f.select(scene === 'main_style' ? 'plaza_style' : 'main_style', 'snow');
  assert.equal(f.messages.at(-1).values.pini_outfit, 'christmas');
  assert.equal(f.messages.at(-1).values.lomi_outfit, 'christmas');
});

for (const scene of ['main_style', 'plaza_style']) test(`disabled automatic outfits preserve explicit outfits on ${scene} snow`, () => {
  const f = fixture(); f.configure({ christmas_npcs: false, christmas_bgm: false, pini_outfit: 'zombie' });
  f.select(scene, 'snow');
  assert.equal(f.messages.at(-1).values.lomi_outfit, 'default');
  assert.equal(f.messages.at(-1).values.pini_outfit, 'zombie');
  assert.equal(f.messages.at(-1).values.christmas_bgm, false);
  f.toggle('christmas_npcs', true);
  assert.equal(f.messages.at(-1).values.pini_outfit, 'christmas');
  f.select('lomi_outfit', 'original');
  assert.equal(f.messages.at(-1).values.lomi_outfit, 'original');
});

test('restores manual selections without retriggering the automatic convenience switch', () => {
  const f = fixture(); f.configure({ plaza_style: 'snow', christmas_npcs: true, lomi_outfit: 'original', pini_outfit: 'zombie' });
  assert.equal(f.input('pini_outfit', 'zombie').checked, true);
  assert.equal(f.input('lomi_outfit', 'original').checked, true);
  assert.equal(f.messages.length, 1);
  f.configure({}, 'zh-CN', {});
  assert.equal(f.input('pini_outfit', 'zombie').checked, true);
});

test('localizes switch labels and outfit choices in every declared language', () => {
  const f = fixture();
  for (const language of ['en', 'zh-CN', 'zh-TW', 'ko', 'id', 'vi', 'th']) {
    f.configure({}, language);
    assert.ok(f.input('christmas_bgm').attributes['aria-label']);
    assert.ok(f.input('christmas_npcs').attributes['aria-label']);
    assert.equal(f.input('christmas_npcs').attributes.role, 'switch');
    for (const row of f.elements.list.children) assert.ok(row.children[0].textContent);
  }
});
