import assert from 'node:assert/strict';
import {test} from 'node:test';
import {paintText} from './text.js';

test('single-line ink centering balances visible glyph bounds inside its rectangle', async () => {
  const originals = Object.fromEntries(['document', 'getComputedStyle'].map(key => [key, globalThis[key]]));
  const fills = [];
  const context = {scale() {}, measureText: () => {
    assert.equal(context.textBaseline, 'alphabetic', 'measure and paint must use the same baseline');
    return {width:48, actualBoundingBoxAscent:9, actualBoundingBoxDescent:3};
  },
    fillText: (...args) => fills.push(args)};
  const canvas = {style:{}, getContext: () => context};
  Object.assign(globalThis, {
    document: {createElement: () => canvas, fonts: {load: async () => []}},
    getComputedStyle: () => ({fontSize:'12px', color:'#454545'}),
  });
  try {
    const label = {style:{}, dataset:{}, classList:{add() {}}, append() {}};
    await paintText(label, 'Item', {size:12, weight:700, family:'BF', width:140, height:20, centerInk:true, inset:0});
    assert.equal(context.font, '700 12px BF');
    assert.equal(context.textBaseline, 'alphabetic');
    const y = fills[0][2];
    assert.equal(y - 9, 4, 'top ink margin');
    assert.equal(20 - y - 3, 4, 'bottom ink margin');
    assert.equal(canvas.width, 560);
    assert.equal(canvas.height, 80);
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});

test('vector captions preserve the measured baseline without rasterizing glyphs', async () => {
  const originals = Object.fromEntries(['document', 'getComputedStyle'].map(key => [key, globalThis[key]]));
  const context = {measureText: () => ({actualBoundingBoxAscent:9,actualBoundingBoxDescent:3})};
  const node = () => ({style:{},dataset:{},attributes:{},children:[],setAttribute(key,value){this.attributes[key]=value;},append(child){this.children.push(child);}});
  Object.assign(globalThis, {
    document:{fonts:{load:async()=>[]},createElement:()=>({getContext:()=>context}),createElementNS:()=>node()},
    getComputedStyle:()=>({fontSize:'12px'}),
  });
  try {
    const label = node(); label.style.width='140px';label.style.height='20px';
    await paintText(label,'Item',{renderer:'svg',size:12,family:'BF',weight:700,align:'center'});
    const svg = label.children[0],glyphs=svg.children[0];
    assert.equal(glyphs.attributes.x,'70');
    assert.equal(glyphs.attributes.y,'13');
    assert.equal(glyphs.attributes['text-anchor'],'middle');
    assert.equal(glyphs.textContent,'Item');
    assert.equal(glyphs.style.font,'700 12px BF');
    assert.equal(svg.attributes.width,'140');
    assert.equal(svg.attributes.height,'20');
  } finally {
    for (const [key,value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key]=value;
    }
  }
});
