import test from 'node:test';
import assert from 'node:assert/strict';
import { mapHoles, holesWithin, clipPath, DIM_FILTER } from '../../src/FantasyTale.Launcher.Overlay/overlay/native-holes.js';

const viewport = { pixelWidth: 1024, pixelHeight: 768, width: 2048, height: 1536 };

test('holes scale from game pixels to page pixels like anchors', () => {
  assert.deepEqual(mapHoles([{ x: 100, y: 50, width: 200, height: 100 }], viewport), [{ x: 200, y: 100, width: 400, height: 200 }]);
});

test('malformed holes are skipped and the rest still map', () => {
  const mapped = mapHoles([{ x: 'a', y: 0, width: 5, height: 5 }, { x: 0, y: 0, width: 0, height: 5 }, { x: 1, y: 2, width: 3, height: 4 }], viewport);
  assert.deepEqual(mapped, [{ x: 2, y: 4, width: 6, height: 8 }]);
  assert.deepEqual(mapHoles(undefined, viewport), []);
});

test('a hole is clamped to a panel and made relative to it', () => {
  const panel = { x: 100, y: 100, width: 300, height: 200 };
  assert.deepEqual(holesWithin(panel, [{ x: 50, y: 150, width: 100, height: 100 }]), [{ x: 0, y: 50, width: 50, height: 100 }]);
});

test('a hole outside a panel or only touching it cuts nothing', () => {
  const panel = { x: 100, y: 100, width: 300, height: 200 };
  assert.deepEqual(holesWithin(panel, [{ x: 400, y: 100, width: 50, height: 50 }, { x: 0, y: 0, width: 10, height: 10 }]), []);
});

test('a hole inside another is dropped and equal holes keep one', () => {
  const panel = { x: 0, y: 0, width: 500, height: 500 };
  const outer = { x: 10, y: 10, width: 100, height: 100 };
  assert.deepEqual(holesWithin(panel, [outer, { x: 20, y: 20, width: 10, height: 10 }, outer]), [outer]);
});

test('the clip path keeps the box and cuts each hole with the even-odd rule', () => {
  assert.equal(clipPath(300, 200, []), '');
  assert.equal(clipPath(300, 200, [{ x: 10, y: 20, width: 50, height: 40 }]),
    'path(evenodd, "M0 0h300v200h-300ZM10 20h50v40h-50Z")');
});

test('the dim is a brightness filter', () => {
  assert.match(DIM_FILTER, /^brightness\(/);
});
