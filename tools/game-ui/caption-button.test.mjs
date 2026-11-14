import assert from 'node:assert/strict';
import {test} from 'node:test';
import {captionButtonGeometry, createCaptionButton, fitCaptionWidth, NATIVE_ACTION_HEIGHT} from './caption-button.js';
import {createGameUi} from './game-ui.js';

const states = ['Up','Down','Hover','Disabled'];
const nativeSkins = (prefix, kind = 'Ok') => states.map(state => `Images/Ui/${prefix}${kind}-${state}.smf`);
function nineSliceFrames(widths, heights) {
  const frames = [];
  let top = 0;
  for (const height of heights) {
    let left = 0;
    for (const width of widths) {
      frames.push([left,top,left + width - 1,top + height - 1]);
      left += width;
    }
    top += height;
  }
  return frames;
}

test('all native O and X state icons retain their original frame dimensions at different requested sizes', async () => {
  const originals = Object.fromEntries(['document', 'getComputedStyle', 'fetch', 'Image'].map(key => [key, globalThis[key]]));
  const surfaces = [];
  class Element extends EventTarget {
    style = {};
    dataset = {};
    classList = {add() {}, remove() {}};
    children = [];
    draws = [];
    append(child) { this.children.push(child); }
    prepend(child) { this.children.unshift(child); }
    getContext() { return {drawImage: (image, ...geometry) => this.draws.push({source:image.src, geometry})}; }
    toDataURL() { surfaces.push(this); return 'data:image/png;base64,c2tpbg=='; }
  }
  let manifest;
  Object.assign(globalThis, {
    document: {createElement: () => new Element(), fonts: {load: async () => []}},
    getComputedStyle: () => ({fontSize:'12px', color:'#fff'}),
    fetch: async () => ({ok:true, json:async () => manifest}),
    Image: class extends Element { async decode() {} },
  });
  try {
    for (const {theme, prefix, widths, heights} of [
      {theme:'v1', prefix:'DialogButton', widths:[26,16,26], heights:[4,17,4]},
      {theme:'v2', prefix:'RestrictionButton', widths:[27,1,6], heights:[5,16,5]},
    ]) {
      const nativeHeight = heights.reduce((sum, value) => sum + value, 0);
      const frames = nineSliceFrames(widths, heights);
      manifest = {sprites:Object.fromEntries(['Ok','Cancel'].flatMap(kind => nativeSkins(prefix, kind).map(path => [path,frames])))};
      const ui = await createGameUi({theme});
      for (const kind of ['Ok','Cancel']) {
        const skins = nativeSkins(prefix, kind);
        for (const width of [80,137]) for (const height of [27,35]) {
          surfaces.length = 0;
          const button = await createCaptionButton(ui, {label:kind, width, height, skins, kind});
          const top = Math.floor((height - nativeHeight) / 2);
          assert.match(button.style.cssText, new RegExp(`width:${width}px;height:${height}px$`), 'the requested click area stays intact');
          assert.equal(surfaces.length, 4, 'every native state is rendered once');
          for (const [index, surface] of surfaces.entries()) {
            assert.equal(surface.width, width, 'caption width remains caller controlled');
            assert.equal(surface.height, nativeHeight);
            assert.equal(surface.draws.length, 9);
            const icon = surface.draws[3];
            assert.match(icon.source, new RegExp(`${prefix}${kind}-${states[index]}/3\\.png$`));
            assert.deepEqual(icon.geometry, [0,heights[0],widths[0],heights[1]], 'the baked icon frame is drawn at its original width and height');
            assert.equal(button.children[index].style.top, `${top}px`);
            assert.equal(button.children[index].style.height, `${nativeHeight}px`);
          }
          assert.match(button.children[4].style.cssText, new RegExp(`top:${top}px;.*height:${nativeHeight}px;`), 'the caption shares the original surface bounds');
        }
      }
    }
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});

test('caption geometry keeps explicit fallback sizes and rejects malformed native manifests', () => {
  const skins = nativeSkins('DialogButton');
  const options = {skins, width:80, height:27};
  assert.deepEqual(captionButtonGeometry({}, options), {width:80,height:27});
  assert.deepEqual(captionButtonGeometry({manifest:{sprites:{}}}, {...options, skins:nativeSkins('GenericButton')}), {width:80,height:27});
  assert.deepEqual(captionButtonGeometry({theme:'v3', manifest:{}}, {...options, skins:states.map(state => `Images/Ui/dialogueNew/dialogue_type01BTN-${state}.smf`)}), {width:80,height:27});
  const frames = nineSliceFrames([26,16,26], [4,17,4]);
  const ui = {manifest:{sprites:Object.fromEntries(skins.map(path => [path,frames]))}};
  assert.deepEqual(captionButtonGeometry(ui, options), {width:80,height:25});
  assert.throws(() => captionButtonGeometry({manifest:{}}, options), /nine-slice frames: Images\/Ui\/DialogButtonOk-Up\.smf/);
  ui.manifest.sprites[skins[3]] = frames.slice(0,8);
  assert.throws(() => captionButtonGeometry(ui, options), /nine-slice frames: Images\/Ui\/DialogButtonOk-Disabled\.smf/);
  ui.manifest.sprites[skins[3]] = frames.map(frame => [...frame]);
  ui.manifest.sprites[skins[3]][3][3] = -1;
  assert.throws(() => captionButtonGeometry(ui, options), /invalid inclusive frame rectangle/);
  ui.manifest.sprites[skins[3]] = nineSliceFrames([26,16,26], [4,18,4]);
  assert.throws(() => captionButtonGeometry(ui, options), /state skins have different original heights/);
});

test('native caption state changes keep the mounted click target and caption intact', async () => {
  const originals = Object.fromEntries(['document', 'getComputedStyle', 'CustomEvent'].map(key => [key, globalThis[key]]));
  class Element extends EventTarget {
    style = {};
    dataset = {};
    classList = {add() {}, remove() {}};
    children = [];
    append(child) { this.children.push(child); child.parentNode = this; }
    dispatchEvent(event) {
      super.dispatchEvent(event);
      if (event.bubbles) this.parentNode?.dispatchEvent(event);
      return true;
    }
  }
  Object.assign(globalThis, {
    document: {createElement: () => new Element(), fonts: {load: async () => []}},
    getComputedStyle: () => ({fontSize:'12px', color:'#fff'}),
    CustomEvent: class extends Event { constructor(type, options) { super(type, options); this.detail = options.detail; } },
  });
  try {
    const calls = [];
    const ui = {panel: async (parent, x, y, width, height, path) => {
      calls.push(path);
      const layer = new Element();
      parent.append(layer);
      return layer;
    }};
    const parent = new Element();
    const actions = [];
    parent.addEventListener('bf-action', event => actions.push(event.detail));
    const button = await createCaptionButton(ui, {label:'Save', width:80, height:27, skins:['up','down','hover','disabled']});
    parent.append(button);
    const children = [...button.children];
    assert.equal(children.length, 5);
    assert.equal(children[4].style.fontWeight, '700');
    for (const [event, selected] of [['pointerenter',2], ['pointerdown',1], ['pointerup',2], ['pointercancel',0]]) {
      button.dispatchEvent(new Event(event));
      assert.deepEqual(button.children, children, 'pointer transitions must not replace child nodes');
      assert.equal(button.children[selected].style.visibility, 'visible');
    }
    button.dispatchEvent(new Event('click'));
    assert.deepEqual(actions, [{name:'Ok', text:'Save'}]);
    button.disabled = true;
    button.dispatchEvent(new Event('pointerenter'));
    assert.equal(children[3].style.visibility, 'visible');
    button.dispatchEvent(new Event('click'));
    assert.equal(actions.length, 1, 'disabled actions cannot reach the host');
    assert.deepEqual(calls, ['up','down','hover','disabled']);
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});

test('fitted caption buttons keep the native 65-pixel minimum and the 37-pixel icon margin', () => {
  const advance = text => text.length * 7.2;
  assert.equal(fitCaptionWidth(['OK'], advance), 65);
  assert.equal(fitCaptionWidth(['Mode Extension'], advance), Math.ceil(14 * 7.2) + 37);
  assert.equal(fitCaptionWidth(['Yes', 'Cancel selection'], advance), Math.ceil(16 * 7.2) + 37);
  assert.equal(NATIVE_ACTION_HEIGHT, 27);
  assert.throws(() => fitCaptionWidth([], advance));
  assert.throws(() => fitCaptionWidth(['x'], () => Number.NaN));
});

test('a plain text skin centres its caption across the face in the theme button colours', async () => {
  const originals = Object.fromEntries(['document', 'getComputedStyle'].map(key => [key, globalThis[key]]));
  class Element extends EventTarget {
    style = {};
    dataset = {};
    classList = {add() {}, remove() {}};
    children = [];
    append(child) { this.children.push(child); }
  }
  Object.assign(globalThis, {
    document: {createElement: () => new Element(), fonts: {load: async () => []}},
    getComputedStyle: () => ({fontSize:'15px', color:'#232323'}),
  });
  try {
    // kr-1256 DlgCreateRoom's Create button: dialogue_type01BTN state skins, text drawn by the client in UiButton colours.
    const skins = ['Up','Down','Hover','Disabled'].map(state => `Images/Ui/dialogueNew/dialogue_type01BTN-${state}.smf`);
    const ui = {theme:'v3', manifest:{sprites:{}, scheme:{UiButton:{TextColor:'0xFF232323', DisabledTextColor:'0xFFe1e9eb'}}},
      panel: async (parent) => { const layer = new Element(); parent.append(layer); return layer; }};
    for (const disabled of [false, true]) {
      const button = await createCaptionButton(ui, {label:'Mode Extension', width:120, height:34, skins, disabled, weight:400});
      const caption = button.children[4];
      assert.match(caption.style.cssText, /^left:0px;top:0px;width:120px;height:34px;/, 'no icon margin on a plain skin');
      assert.equal(caption.style.fontWeight, '400');
      assert.equal(caption.style.color, disabled ? '#e1e9eb' : '#232323');
      assert.equal(button.children[3].style.filter, undefined, 'the plain skin keeps its own disabled art');
    }
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
});

test('fitted plain buttons use their own margin and minimum width', () => {
  const advance = text => text.length * 7.2;
  assert.equal(fitCaptionWidth(['OK'], advance, {margin:24, minimum:83}), 83);
  assert.equal(fitCaptionWidth(['Mode Extension'], advance, {margin:24, minimum:83}), Math.ceil(14 * 7.2) + 24);
  assert.equal(fitCaptionWidth(['Mode Extension'], advance), Math.ceil(14 * 7.2) + 37, 'the O/X defaults are unchanged');
});
