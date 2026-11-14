import assert from 'node:assert/strict';
import test from 'node:test';
import {parsePresets, writePresets, presetStorageKey} from './preset-store.ts';

test('saved presets survive serialization, including an allow-all preset', () => {
  const saved = new Map();
  const storage = {setItem: (key, value) => saved.set(key, value)};
  writePresets(storage, [{id:'first',name:'Custom rules',itemIds:[202025,300003,202025]}, {id:'empty',name:'Allow all',itemIds:[]}]);
  assert.deepEqual(parsePresets(saved.get(presetStorageKey)), [
    {id:'first',name:'Custom rules',itemIds:[202025,300003]}, {id:'empty',name:'Allow all',itemIds:[]},
  ]);
});

test('corrupt browser data and duplicate identities are rejected before writing', () => {
  assert.throws(() => parsePresets('{'));
  for (const itemIds of [[0], [-1], ['202025'], [Number.MAX_SAFE_INTEGER+1]]) {
    assert.throws(() => parsePresets(JSON.stringify({format:1,presets:[{id:'id',name:'Name',itemIds}]})));
  }
  const first = {id:'id',name:'Name',itemIds:[]};
  assert.throws(() => parsePresets(JSON.stringify({format:1,presets:[first, {...first,name:'Other'}]})));
  assert.throws(() => parsePresets(JSON.stringify({format:1,presets:[first, {...first,id:'other'}]})));
});

test('storage failure cannot mutate the existing caller-owned preset list', () => {
  const presets = [{id:'id',name:'Existing',itemIds:[202025]}];
  const original = structuredClone(presets);
  const storage = {setItem: () => {throw new Error('quota exceeded');}};
  assert.throws(() => writePresets(storage,presets), /quota exceeded/);
  assert.deepEqual(presets,original);
});
