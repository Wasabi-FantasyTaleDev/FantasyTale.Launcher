import assert from 'node:assert/strict';
import {test} from 'node:test';
import {narratorState,narratorFrame,narratorClick,narratorLines,nextNarratorPage} from './tutorial-model.js';

test('narrator timing and clicks do not burst after delayed frames',()=>{
  const state=narratorState('你\n好',70,0);
  assert.equal(narratorFrame(state,69),'idle');
  assert.equal(state.visible,0);
  assert.equal(narratorFrame(state,70),'character');
  assert.equal(state.visible,1);
  assert.equal(narratorFrame(state,1000),'character');
  assert.equal(state.visible,2,'A delayed frame must not play a burst of missed character sounds');
  assert.equal(narratorFrame(state,1069),'idle');
  assert.equal(narratorFrame(state,1070),'character');
  assert.equal(narratorFrame(state,1140),'complete');
  assert.equal(state.finishedAt,1140,'The hold timeout starts after typing completes');
  assert.equal(narratorClick(state,1200),'advance');
  const reveal=narratorState('接受训练。',70);
  assert.equal(narratorClick(reveal,30),'reveal');
  assert.equal(reveal.visible,reveal.text.length);
  assert.equal(narratorClick(reveal,31),'advance');
});

test('line metrics and page transitions use a synthetic resource catalog',()=>{
  const glyphs={A:{lineHeight:17},B:{lineHeight:20},'\n':{lineHeight:17}};
  assert.equal(narratorLines('A',glyphs).height,17);
  assert.equal(narratorLines('A\nB',glyphs).height,37);
  assert.equal(narratorLines('A\n',glyphs).lines.length,1);
  assert.deepEqual(narratorLines('',glyphs),{height:0,lines:[]});
  const pages=[
    {stage:'first',progress:1,nextProgress:'2'},
    {stage:'first',progress:2,nextProgress:'gameplay'},
    {stage:'first',progress:3,nextProgress:'end'},
    {stage:'other',progress:2,nextProgress:'end'},
  ];
  assert.equal(nextNarratorPage(pages,pages[0]),pages[1]);
  assert.equal(nextNarratorPage(pages,pages[1],false),undefined);
  assert.equal(nextNarratorPage(pages,pages[1],true),pages[2]);
  assert.equal(nextNarratorPage(pages,pages[2],true),pages[0]);
  assert.equal(nextNarratorPage(pages,undefined),undefined);
});
