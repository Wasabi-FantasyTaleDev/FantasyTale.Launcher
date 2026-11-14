import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createGameUi} from './game-ui.js';

test('skin images retain one integer-composed surface and its logical size', async () => {
  const originals = Object.fromEntries(['document', 'fetch', 'Image'].map(key => [key, globalThis[key]]));
  const draws = [];
  let encoded = false;
  class Element {
    style = {};
    children = [];
    append(child) { this.children.push(child); }
    prepend(child) { this.children.unshift(child); }
    getContext() { return {drawImage: (...args) => draws.push(args.slice(1))}; }
    toDataURL(type) {
      assert.equal(type, 'image/png');
      assert.equal(this.width, 30);
      assert.equal(this.height, 24);
      assert.equal(draws.length, 9, 'all edges must be composed before the image is encoded');
      encoded = true;
      return 'data:image/png;base64,c2tpbg==';
    }
  }
  const skin = 'Images/Ui/Test.smf';
  const frames = [
    [0, 0, 2, 3], [3, 0, 3, 3], [4, 0, 5, 3],
    [0, 4, 2, 4], [3, 4, 3, 4], [4, 4, 5, 4],
    [0, 5, 2, 7], [3, 5, 3, 7], [4, 5, 5, 7],
  ];
  Object.assign(globalThis, {
    document: {createElement: () => new Element()},
    fetch: async () => ({ok: true, json: async () => ({sprites: {[skin]: frames}})}),
    Image: class extends Element { async decode() { if (this.src.startsWith('data:')) assert.equal(encoded, true); } },
  });
  try {
    const ui = await createGameUi({theme: 'v2'});
    const panel = await ui.panel(new Element(), 0, 0, 30, 24, skin);
    assert.deepEqual(draws, [
      [0, 0, 3, 4], [3, 0, 25, 4], [28, 0, 2, 4],
      [0, 4, 3, 17], [3, 4, 25, 17], [28, 4, 2, 17],
      [0, 21, 3, 3], [3, 21, 25, 3], [28, 21, 2, 3],
    ]);
    const image = panel.children[0];
    assert.equal(image.className, 'skin');
    assert.equal(image.width, 30);
    assert.equal(image.height, 24);
    assert.equal(image.style.width, '30px');
    assert.equal(image.style.height, '24px');
    assert.equal(image.alt, '');
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});
