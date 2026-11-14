import assert from 'node:assert/strict';
import {test} from 'node:test';
import {pageFromDrag} from './pagination.js';

test('captured native scrollbar drags reach both ends and preserve intermediate pages', () => {
  assert.equal(pageFromDrag(0, 120, 120, 2), 1);
  assert.equal(pageFromDrag(1, -120, 120, 2), 0);
  assert.equal(pageFromDrag(1, 60, 240, 5), 2);
  assert.equal(pageFromDrag(3, -120, 240, 5), 1);
  assert.equal(pageFromDrag(2, 1000, 240, 5), 4);
  assert.equal(pageFromDrag(2, -1000, 240, 5), 0);
  assert.equal(pageFromDrag(0, 1000, 0, 1), 0);
});
