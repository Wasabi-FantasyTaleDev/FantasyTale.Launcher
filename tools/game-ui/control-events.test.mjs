import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createGameUi} from './game-ui.js';

class Element extends EventTarget {
  children = [];
  parentNode = null;
  style = {};
  dataset = {};
  className = '';
  classList = {add: value => { this.className += ' ' + value; }};
  append(child) {
    child.remove?.();
    this.children.push(child);
    child.parentNode = this;
  }
  remove() {
    if (this.parentNode) this.parentNode.children = this.parentNode.children.filter(child => child !== this);
    this.parentNode = null;
  }
  replaceChildren(...children) {
    for (const child of [...this.children]) child.remove?.();
    for (const child of children) this.append(child);
  }
  querySelectorAll(selector) {
    return this.children.flatMap(child => [
      ...(child.className?.split(' ').includes(selector.slice(1)) ? [child] : []),
      ...(child.querySelectorAll?.(selector) ?? []),
    ]);
  }
  dispatchEvent(event) {
    const accepted = super.dispatchEvent(event);
    if (event.bubbles) this.parentNode?.dispatchEvent(event);
    return accepted;
  }
}

test('returned native controls still deliver events after a Vue host adopts them', async () => {
  const originals = Object.fromEntries(['document', 'fetch', 'Image', 'CustomEvent'].map(key => [key, globalThis[key]]));
  const frames = [[0, 0, 20, 17], [21, 0, 41, 17], [42, 0, 62, 17], [63, 0, 83, 17]];
  const manifest = {
    sprites: {'Images/Ui/UiCheckBoxSkin.smf': frames, 'Images/TestButton.smf': frames},
    scheme: {UiCheckBox: {SkinImage: 'Images/Ui/UiCheckBoxSkin.smf'}, UiLabel: {FontSize: '13'}},
  };
  Object.assign(globalThis, {
    document: {createElement: () => new Element()},
    fetch: async () => ({ok: true, json: async () => manifest}),
    Image: class extends Element {},
    CustomEvent: class extends Event {constructor(type, options) {super(type, options); this.detail = options.detail;}},
  });
  try {
    for (const theme of ['v1', 'v2', 'v3']) {
      const ui = await createGameUi({theme});
      const temporary = new Element(), mounted = new Element();
      const changes = [], actions = [], detached = [];
      mounted.addEventListener('bf-change', event => changes.push(event.detail));
      mounted.addEventListener('bf-action', event => actions.push(event.detail));
      temporary.addEventListener('bf-change', event => detached.push(event.detail));
      temporary.addEventListener('bf-action', event => detached.push(event.detail));
      const checkbox = await ui.renderNode({type: 'UiCheckBox', attrs: {
        Name: 'restrictedOnly', Width: '21', Height: '18', Check: '0',
      }, children: []}, temporary);
      mounted.append(checkbox);
      const input = checkbox.children[0];
      input.checked = true;
      input.onchange();
      assert.deepEqual(changes, [{name: 'restrictedOnly', value: true}], theme);
      checkbox.setValue(false);
      assert.equal(input.checked, false, theme);
      assert.equal(changes.length, 1, 'host updates must not become user changes');
      const button = await ui.button(temporary, 'Save', 0, 0, 80, 27, 'Save', 'Images/TestButton.smf');
      mounted.append(button);
      button.dispatchEvent(new Event('click'));
      assert.equal(actions.length, 1, theme);
      assert.equal(actions[0].name, 'Save', theme);
      assert.equal(detached.length, 0, 'events must follow the mounted control rather than its former parent');
    }
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});
