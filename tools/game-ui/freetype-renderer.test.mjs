import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  computeFreetypeLayout,
  registerFreetypeFont,
  clearFreetypeCaches,
  paintText
} from './text.js';

test('freetype layout clamps font size to at least 11 and computes decoration borders', () => {
  // cn-717 sub_BAE810 clamps size < 11 to 11
  const layoutSmall = computeFreetypeLayout('Test', { width: 100, height: 30, decoration: 1 }, { size: 8, weight: 400 });
  assert.equal(layoutSmall.clampedSize, 11, 'size < 11 must be clamped to 11');
  assert.equal(layoutSmall.border, 0, 'decoration 1 has 0 border');

  // cn-717 sub_BAD8F0 decoration border dimensions:
  // deco 2: 2px border, deco 3: 1px border, deco 4: 2px border
  const layoutDeco2 = computeFreetypeLayout('Test', { width: 100, height: 30, decoration: 2 }, { size: 13, weight: 400 });
  assert.equal(layoutDeco2.border, 2, 'decoration 2 border is 2');

  const layoutDeco3 = computeFreetypeLayout('Test', { width: 100, height: 30, decoration: 3 }, { size: 13, weight: 400 });
  assert.equal(layoutDeco3.border, 1, 'decoration 3 border is 1');

  const layoutDeco4 = computeFreetypeLayout('Test', { width: 100, height: 30, decoration: 4 }, { size: 14, weight: 700 });
  assert.equal(layoutDeco4.border, 2, 'decoration 4 border is 2');
});

test('freetype layout computes integer centring without fractional offsets', () => {
  // cn-717 sub_BB0440: integer halves floor(height/2) - floor(contentHeight/2) + border + size - 1
  const width = 200;
  const height = 41;
  const size = 13;
  // contentHeight for single line deco 1: 0*spacing + 13 + 0 = 13
  // floor(41 / 2) - floor(13 / 2) + 0 + 13 - 1 = 20 - 6 + 12 = 26
  const layout = computeFreetypeLayout('Notice', { width, height, align: 'center', vertical: 'middle', decoration: 1 }, { size, weight: 700 });
  assert.equal(layout.lines.length, 1);
  assert.equal(Number.isInteger(layout.lines[0].x), true, 'x must be integer');
  assert.equal(Number.isInteger(layout.lines[0].y), true, 'y baseline must be integer');
  assert.equal(layout.lines[0].y, 26, 'baseline follows cn-717 sub_BB0440 integer formula');
});

test('freetype layout wraps lines when text exceeds content bounds', () => {
  const glyphWidths = new Map([['A', 10], [' ', 5]]);
  // available width: 60 - 2*2 inset - 2*0 border = 56.
  // 'AAAAA ' is 55 <= 56. Next 'A' makes 65 > 56, wrapping to line 2.
  const layout = computeFreetypeLayout('AAAAA AAAAA', { width: 60, height: 60, wrap: true }, { size: 13, weight: 400, glyphWidths });
  assert.equal(layout.lines.length, 2, 'must wrap into two lines');
  assert.equal(layout.lines[0].text, 'AAAAA ');
  assert.equal(layout.lines[1].text, 'AAAAA');
});

test('freetype input validation fails loudly on invalid dimensions', () => {
  assert.throws(() => {
    computeFreetypeLayout('Hello', { width: 0, height: 20 }, { size: 13, weight: 400 });
  }, /positive logical dimensions/);

  assert.throws(() => {
    computeFreetypeLayout('Hello', { width: 100, height: -5 }, { size: 13, weight: 400 });
  }, /positive logical dimensions/);
});

test('freetype font registry rejects invalid arguments and caches can be cleared', () => {
  assert.throws(() => {
    registerFreetypeFont('', null);
  }, /requires family name and buffer/);

  const dummyData = new Uint8Array([1, 2, 3]);
  registerFreetypeFont('TestFont', dummyData);
  assert.doesNotThrow(() => clearFreetypeCaches());
});

test('paintText with renderer freetype throws loudly if font data is missing', async () => {
  const originals = Object.fromEntries(['document', 'getComputedStyle'].map(k => [k, globalThis[k]]));
  Object.assign(globalThis, {
    document: { fonts: { load: async () => [] }, createElement: () => ({ style: {}, classList: { add() {} } }) },
    getComputedStyle: () => ({ fontSize: '13px', color: '#5b5b5b' })
  });

  try {
    const el = { clientWidth: 100, clientHeight: 30, style: {}, classList: { add() {} } };
    await assert.rejects(
      async () => {
        await paintText(el, 'Test', { renderer: 'freetype', family: 'NonExistentFont' });
      },
      /FreeType font data for family 'NonExistentFont' is not loaded or registered/
    );
  } finally {
    for (const [k, v] of Object.entries(originals)) {
      if (v === undefined) delete globalThis[k]; else globalThis[k] = v;
    }
  }
});
