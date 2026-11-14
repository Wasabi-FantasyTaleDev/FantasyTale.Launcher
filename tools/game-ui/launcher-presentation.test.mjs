import test from 'node:test';
import assert from 'node:assert/strict';
import {createGameUi} from './game-ui.js';
import {themeForClientDate} from './theme.js';
import {extensionSettingsState} from './extension-settings.js';

const painted = [];
class Element {
  style = {};
  dataset = {};
  classList = {add() {}, remove() {}};
  children = [];
  listeners = {};
  append(...children) { this.children.push(...children); for (const child of children) child.parentNode = this; }
  prepend(...children) { this.children.unshift(...children); for (const child of children) child.parentNode = this; }
  replaceChildren(...children) { this.children = children; }
  addEventListener(name, listener) { this.listeners[name] = listener; }
  dispatchEvent(event) { this.lastEvent = event; if (event.bubbles) this.parentNode?.dispatchEvent(event); }
  toDataURL() { return 'data:image/png;base64,c2tpbg=='; }
  getContext() {
    const context = {scale: (...value) => { context.samples = value; }, measureText: text => ({width: text.length * 13, actualBoundingBoxDescent: 3}), fillText() {}, strokeText() {}, drawImage() {}};
    painted.push({canvas: this, context});
    return context;
  }
}
globalThis.document = {createElement: () => new Element(), fonts: {load: async () => []}};
globalThis.getComputedStyle = () => ({fontSize: '13px', color: '#444'});
globalThis.Image = class extends Element { async decode() {} };
globalThis.CustomEvent = class { constructor(type, options) { Object.assign(this, {type}, options); } };
globalThis.fetch = async () => ({ok: true, json: async () => ({sprites: Object.fromEntries(['Ok', 'Cancel'].flatMap(name => ['Up', 'Down', 'Hover', 'Disabled'].map(state => [`Images/Ui/DialogButton${name}-${state}.smf`, [[0, 0, 67, 24]]]))), scheme: {UiLabel: {FontSize: '13'}}})});

for (const theme of ['v1', 'v2', 'v3']) {
  test(`${theme} keeps the selected font, normal weight and logical canvas dimensions`, async () => {
    const ui = await createGameUi({theme});
    const parent = new Element();
    for (const [font, expected] of [[undefined, theme === 'v3' ? 'BF' : 'BFMalgun'], ['Default', 'BF'], ['Malgun', 'BFMalgun']]) {
      await ui.renderNode({type: 'UiLabel', attrs: {Name: 'identity', Text: '扩展设置 Overlay', Width: '220', Height: '20', FontSize: '13', FontName: font}, children: []}, parent);
      const last = painted.at(-1);
      assert.equal(last.context.font, `400 13px ${expected}`);
      assert.deepEqual(last.context.samples, [4, 4]);
      assert.equal(last.canvas.width, 880);
      assert.equal(last.canvas.height, 80);
      assert.match(last.canvas.style.cssText, /width:220px;height:20px/);
    }
    await ui.settingsLabel(parent, '显示百分比血量', 0, 0, 220, 20);
    assert.equal(painted.at(-1).context.font, `400 13px ${theme === 'v3' ? 'BF' : 'BFMalgun'}`);
  });
}

test('v1 canonical button skins retain lowercase and custom action names', async () => {
  const ui = await createGameUi({theme: 'v1'});
  for (const [action, skin] of [['ok', 'Ok'], ['confirm-selection', 'Ok'], ['dismiss', 'Cancel']]) {
    const parent = new Element();
    const button = await ui.button(parent, action, 0, 0, 68, 25, '确定', `Images/Ui/DialogButton${skin}-Up.smf`);
    assert.equal(painted.at(-1).context.font, '400 13px BFMalgun');
    await button.listeners.pointerenter();
    await button.listeners.pointerdown();
    await button.listeners.pointerup();
    await button.setSelected(true);
    button.listeners.click();
    assert.equal(parent.lastEvent.detail.name, action);
  }
});

test('date boundary includes 2017-02-07 and retains every earlier explicit theme', () => {
  assert.equal(themeForClientDate('2017-02-07', 'v1'), 'v3');
  assert.equal(themeForClientDate('2017-02-07', 'v2'), 'v3');
  assert.equal(themeForClientDate('2017-02-06', 'v1'), 'v1');
  assert.equal(themeForClientDate('2017-02-06', 'v2'), 'v2');
  assert.equal(themeForClientDate(undefined, 'v1'), 'v1');
  assert.throws(() => themeForClientDate('2017-02-30'));
});

test('extension settings preserve mutually exclusive values through copied drafts', () => {
  const draft = extensionSettingsState({healthDisplay: 'value', bgm: 'new', oneClickRun: true});
  const copied = extensionSettingsState(draft);
  copied.bgm = 'old';
  assert.equal(draft.bgm, 'new');
  assert.equal(draft.healthDisplay, 'value');
  assert.throws(() => extensionSettingsState({bgm: 'both'}));
});
