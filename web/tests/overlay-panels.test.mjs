import test from 'node:test';
import assert from 'node:assert/strict';
import { orderedPanels, inputPanels, bringToFront, passesOutsideClicks } from '../../src/FantasyTale.Launcher.Overlay/overlay/panel-stack.js';
import { resolveAnchor } from '../../src/FantasyTale.Launcher.Overlay/overlay/anchors.js';

function panel(layer = 0, interactive = false, modal = false, visible = true) {
  return { layer, interactive, modal, host: { classList: { contains: () => visible } } };
}

test('prepared hidden panels do not render or receive input', () => {
  const panels = new Map([['a', panel(100, true, true, false)]]);
  assert.deepEqual(orderedPanels(panels), []);
  assert.deepEqual(inputPanels(orderedPanels(panels)), []);
});

test('independent HTML and component panels share deterministic layers', () => {
  const lower = panel(5, true), upper = panel(20, true), readOnly = panel(30);
  const shown = orderedPanels(new Map([['upper', upper], ['lower', lower], ['hud', readOnly]]));
  assert.deepEqual(shown, [lower, upper, readOnly]);
  assert.deepEqual(inputPanels(shown), [lower, upper]);
});

test('top modal exclusively receives input even above a higher ordinary layer', () => {
  const ordinary = panel(10000, true), first = panel(0, true, true), second = panel(1, true, true);
  const panels = new Map([['first', first], ['ordinary', ordinary], ['second', second]]);
  assert.deepEqual(orderedPanels(panels), [ordinary, first, second]);
  assert.deepEqual(inputPanels(orderedPanels(panels)), [second]);
  panels.delete('second');
  assert.deepEqual(inputPanels(orderedPanels(panels)), [first]);
  panels.delete('first');
  assert.deepEqual(inputPanels(orderedPanels(panels)), [ordinary]);
});

test('activation raises equal-layer panels without bypassing layer priority', () => {
  const first = panel(0, true), second = panel(0, true), high = panel(10, true);
  const panels = new Map([['a', first], ['b', second], ['high', high]]);
  bringToFront(panels, 'a');
  assert.deepEqual(orderedPanels(panels), [second, first, high]);
  bringToFront(panels, 'missing');
  assert.equal(panels.size, 3);
});

test('outside clicks follow only the top modal policy while lower overlays stay blocked', () => {
  const lower = { ...panel(0, true, true), passThroughOutside: false };
  const upper = { ...panel(1, true, true), passThroughOutside: true };
  const panels = new Map([['lower', lower], ['upper', upper]]);
  assert.equal(passesOutsideClicks(orderedPanels(panels)), true);
  assert.deepEqual(inputPanels(orderedPanels(panels)), [upper]);
  upper.passThroughOutside = false;
  lower.passThroughOutside = true;
  assert.equal(passesOutsideClicks(orderedPanels(panels)), false);
  panels.delete('upper');
  assert.equal(passesOutsideClicks(orderedPanels(panels)), true);
  panels.clear();
  assert.equal(passesOutsideClicks(orderedPanels(panels)), true);
});

test('physical game pixels convert independently from desktop DPI', () => {
  const result = resolveAnchor({x:300,y:150,width:600,height:300},
    {pixelWidth:1920,pixelHeight:1080,width:1280,height:720}, {width:80,height:25});
  assert.deepEqual(result, {x:200,y:100,width:400,height:200});
});

test('reference coordinates survive resolution changes and adjacent panels remain visible', () => {
  const anchor = {x:700,y:500,width:200,height:100,referenceWidth:1024,referenceHeight:768,placement:'right',gap:8};
  const result = resolveAnchor(anchor,{pixelWidth:1920,pixelHeight:1080,width:640,height:480},{width:100,height:30});
  assert.equal(result.x, 540);
  assert.equal(result.y, 312.5);
  assert.equal(result.width,100);
  assert.throws(() => resolveAnchor({...anchor,width:NaN},{width:640,height:480},{width:100,height:30}));
});
