import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createRequire} from 'node:module';
import {test} from 'node:test';
import {runInNewContext} from 'node:vm';
import * as modeApi from './mode-extension.js';

// The shared UI uses the frontend's declared compiler and Vue dependencies.
const requireWeb = createRequire(new URL('../../web/package.json', import.meta.url));
const {parse, compileScript} = requireWeb('@vue/compiler-sfc');
const ts = requireWeb('typescript'), vue = requireWeb('vue');
const compiled = new Map();
const transpile = source => ts.transpileModule(source, {
  compilerOptions:{module:ts.ModuleKind.CommonJS, target:ts.ScriptTarget.ES2022},
}).outputText;
const flush = async () => { for (let index = 0; index < 20; index++) await Promise.resolve(); };

function pending() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return {promise, resolve, reject};
}

function runtime() {
  const scope = vue.effectScope(), timers = new Map(), unmount = [], events = [], images = [];
  const imageWaits = [], textWaits = [], renderer = vue.shallowRef({manifest:{imageFormat:'png'}});
  const tracker = {pending:vue.ref(0)};
  const state = {deferImages:false, deferText:false, draws:0};
  let timerId = 0;
  const api = {...vue, onMounted() {}, onBeforeUnmount:callback => unmount.push(callback),
    watch:(...args) => scope.run(() => vue.watch(...args)),
    h:(type, props, children) => ({type, props:props ?? {}, children}),
  };
  const module = {exports:{}};
  const sandbox = {
    module, exports:module.exports, Error, console, crypto:globalThis.crypto,
    innerWidth:1024, innerHeight:768, addEventListener() {}, removeEventListener() {},
    localStorage:{getItem:() => null},
    document:{fonts:{add() {}}, createElement:() => ({style:{}, classList:{add() {}}, append() {}, querySelectorAll:() => []})},
    FontFace:class { load() { return Promise.resolve(this); } },
    setTimeout(callback, milliseconds) { const id = ++timerId; timers.set(id, {callback, milliseconds}); return id; },
    clearTimeout:id => timers.delete(id),
    Image:class {
      constructor() { images.push(this); }
      decode() {
        if (!state.deferImages) return Promise.resolve();
        const request = pending(); imageWaits.push(request); return request.promise;
      }
    },
  };
  sandbox.require = name => {
    if (name === 'vue') return api;
    if (name === '../context') return {useDrawTracker:() => tracker, useGameUi:() => renderer};
    if (name.endsWith('.vue')) return {__esModule:true, default:{}};
    if (name.endsWith('/text.js')) return {paintText:async () => {
      state.draws++;
      if (state.deferText) { const request = pending(); textWaits.push(request); await request.promise; }
    }};
    if (name.endsWith('/caption-button.js')) return {createCaptionButton:async () => ({})};
    if (name.endsWith('/mode-extension.js')) return modeApi;
    if (name.endsWith('/pagination.js')) return {pageFromDrag:() => 0};
    throw new Error(`Unexpected test import: ${name}`);
  };
  return {
    scope, api, sandbox, timers, events, images, imageWaits, textWaits, renderer, tracker, state,
    component(name, props) {
      if (!compiled.has(name)) {
        const source = readFileSync(new URL(`src/components/${name}.vue`, import.meta.url), 'utf8');
        compiled.set(name, transpile(compileScript(parse(source).descriptor, {id:name}).content));
      }
      runInNewContext(compiled.get(name), sandbox);
      return scope.run(() => sandbox.module.exports.default.setup(props, {expose() {}, emit:(...args) => events.push(args)}));
    },
    takeTimer(milliseconds) {
      const entry = [...timers].find(([, timer]) => timer.milliseconds === milliseconds);
      assert(entry, `Expected a ${milliseconds} ms timer`);
      timers.delete(entry[0]); return entry[1].callback;
    },
    dispose() { unmount.forEach(callback => callback()); scope.stop(); },
  };
}

function banPick(active = false) {
  const fixture = runtime();
  const categories = {
    weapons:['Weapon_Rifle','Weapon_ShotGun','Weapon_Sniper','SubWeapon','MeleeWeapon','SpecialWeapon'],
    characters:['character'], clothes:['SetClothes','Hats','Shirts','Pants','Costumes','Gloves','Shoes'],
    accessories:['HeadAccesory','FaceAccesory','FrontAccesory','BackAccesory'],
  };
  let id = 0;
  const items = Object.entries(categories).flatMap(([category, subcategories]) => subcategories.flatMap(subCategory =>
    Array.from({length:48}, () => ({id:++id, name:`Item ${id}`, category, subCategory, preview:`image-${id}`}))));
  const props = vue.reactive({
    modelValue:[], items, labels:{}, resourceRoot:'/', locale:'en', readonly:false, textScale:1, active,
    defaultSubCategories:{clothes:'SetClothes'}, characters:{}, presets:[],
    appearance:{theme:'v2', categorySizes:{weapons:{height:32}}, subCategoryOrder:{}, nameBounds:{top:1,height:20}},
  });
  return {...fixture, props, bindings:fixture.component('BfBanPick', props)};
}

function modeExtension() {
  const fixture = runtime();
  const bindings = fixture.component('BfModeExtension', vue.reactive({
    modes:[{id:1, key:'Pvp', names:{en:'Arena'}}], labels:{}, appearance:{skins:{}},
    resourceRoot:'/', theme:'v2', locale:'en', textScale:1, busy:false,
  }));
  return {...fixture, bindings};
}

test('strict reveal cannot time out while drawing and clears both timers on disposal', async () => {
  const fixture = runtime();
  fixture.tracker.pending.value = 1;
  const bindings = fixture.component('BfReveal', {strict:true, limit:8000});
  fixture.takeTimer(50)(); fixture.takeTimer(8000)();
  assert.equal(bindings.shown.value, false);
  assert.equal(fixture.events.length, 0);
  fixture.tracker.pending.value = 0; await flush();
  fixture.takeTimer(50)();
  assert.equal(bindings.shown.value, true);
  fixture.dispose(); assert.equal(fixture.timers.size, 0);
});

test('default reveal retains its fallback without changing strict windows', () => {
  const fixture = runtime();
  fixture.tracker.pending.value = 1;
  const bindings = fixture.component('BfReveal', {strict:false, limit:8000});
  fixture.takeTimer(8000)();
  assert.equal(bindings.shown.value, true);
  fixture.dispose(); assert.equal(fixture.timers.size, 0);
});

test('native surfaces discard stale draws and release the latest sound binding on unmount', async () => {
  const fixture = runtime(), draws = [], attachments = [];
  let unbound = 0;
  fixture.renderer.value = {sounds:{bind:() => () => { unbound++; }}};
  const props = vue.reactive({revision:0, draw:() => { const request = pending(); draws.push(request); return request.promise; }});
  const bindings = fixture.component('BfNative', props);
  bindings.host.value = {replaceChildren:element => attachments.push(element)};
  await flush();
  props.revision++; await flush();
  assert.equal(draws.length, 2);
  const latest = {querySelectorAll:() => []};
  draws[1].resolve(latest); await flush();
  draws[0].resolve({querySelectorAll:() => []}); await flush();
  assert.equal(attachments.length, 1);
  assert.equal(fixture.tracker.pending.value, 0);
  fixture.dispose(); assert.equal(unbound, 1);
});

test('leaving during first-page decoding cannot reveal or refill an unmounted editor', async () => {
  const fixture = banPick();
  fixture.state.deferImages = true;
  const revealing = fixture.takeTimer(50)();
  await flush();
  assert.equal(fixture.images.length, 8);
  fixture.dispose();
  fixture.state.deferImages = false;
  fixture.imageWaits.forEach(request => request.resolve());
  await revealing;
  assert.equal(fixture.bindings.pinned.size, 0);
  assert.equal(fixture.bindings.warmed.size, 0);
  assert.equal(fixture.bindings.pinnedText.size, 0);
  assert.equal(fixture.events.length, 0);
  assert.equal(fixture.images.length, 8);
  assert.equal(fixture.timers.size, 0);
});

test('hidden preparation preserves every category and subcategory first page', async () => {
  const fixture = banPick();
  try {
    await fixture.takeTimer(50)();
    assert.equal(fixture.bindings.pinned.size, 144);
    assert.equal(fixture.bindings.pinnedText.size, 144);
    assert.equal(fixture.bindings.warmed.size, 0);
    assert.equal(fixture.bindings.rollingText.size, 0);
    let turns = 0;
    while (fixture.timers.size) {
      assert(++turns < 150);
      fixture.takeTimer(0)(); await flush();
    }
    assert.equal(fixture.state.draws, 144);
    assert.equal(fixture.bindings.finishedText.size, 144);
  } finally { fixture.dispose(); }
});

test('rapid page changes retain all first pages and only the current next three pages of names', async () => {
  const fixture = banPick(true), {bindings, props} = fixture;
  try {
    await fixture.takeTimer(50)();
    for (let index = 0; index < 1000; index++) {
      bindings.page.value = index % 30;
      bindings.warmAhead();
      assert.equal(bindings.pinnedText.size, 144);
      assert(bindings.rollingText.size <= 24);
      assert(bindings.warmed.size <= 3);
    }
    assert.equal(bindings.pinned.size, 144);
    assert.equal(fixture.timers.size, 1);
    bindings.category.value = 'clothes';
    await flush();
    bindings.page.value = 2;
    await flush();
    const wanted = new Set(bindings.matches.value.slice(24, 48).map(item => bindings.textKey(item)));
    assert.deepEqual(new Set(bindings.rollingText.keys()), wanted);
    props.active = false;
    await flush();
    assert.equal(bindings.rollingText.size, 0);
    assert.equal(bindings.pinnedText.size, 144);
    bindings.warmAhead();
    assert.equal(bindings.rollingText.size, 0);
  } finally { fixture.dispose(); }
});

test('a caption completing after unmount cannot restart the worker', async () => {
  const fixture = banPick();
  await fixture.takeTimer(50)();
  fixture.state.deferText = true;
  fixture.takeTimer(0)(); await flush();
  assert.equal(fixture.textWaits.length, 1);
  fixture.dispose();
  fixture.textWaits[0].resolve(); await flush();
  assert.equal(fixture.timers.size, 0);
  assert.equal(fixture.bindings.pinnedText.size, 0);
  assert.equal(fixture.bindings.rollingText.size, 0);
  assert.equal(fixture.bindings.finishedText.size, 0);
});

test('mode scrollbars wait for renderer readiness and use its image format', async () => {
  const fixture = modeExtension(), decoded = pending();
  let calls = 0;
  try {
    fixture.renderer.value = undefined;
    fixture.bindings.revealed.value = true;
    fixture.bindings.scrollbar.value = {querySelectorAll:() => Array.from({length:6}, () => ({decode:() => {
      calls++; assert.match(fixture.bindings.sprite(1), /\.webp$/); return decoded.promise;
    }}))};
    await flush();
    assert.equal(fixture.bindings.sprite(1), undefined);
    assert.equal(calls, 0);
    assert.equal(fixture.events.length, 0);
    assert.equal(fixture.tracker.pending.value, 0);
    fixture.renderer.value = {manifest:{imageFormat:'webp'}};
    await flush();
    assert.equal(calls, 6);
    assert.equal(fixture.tracker.pending.value, 1);
    assert.equal(fixture.bindings.visible.value, false);
    decoded.resolve(); await flush();
    assert.equal(fixture.tracker.pending.value, 0);
    assert.equal(fixture.bindings.visible.value, true);
  } finally { fixture.dispose(); }
});

test('a missing scrollbar closes the partial mode dialog and reports its error', async () => {
  const fixture = modeExtension(), decoded = pending();
  try {
    fixture.bindings.scrollbar.value = {querySelectorAll:() => [{decode:() => decoded.promise}]};
    await flush();
    decoded.reject(new Error('Missing scrollbar')); await flush();
    assert.equal(fixture.tracker.pending.value, 0);
    assert.equal(fixture.bindings.visible.value, false);
    assert.deepEqual(fixture.events.map(event => event[0]), ['cancel', 'error']);
  } finally { fixture.dispose(); }
});

test('stale scrollbar decoding cannot settle the current draw or revive an unmounted mode dialog', async () => {
  const fixture = modeExtension(), old = pending(), current = pending();
  fixture.bindings.scrollbar.value = {querySelectorAll:() => [{decode:() => old.promise}]};
  await flush();
  fixture.bindings.scrollbar.value = {querySelectorAll:() => [{decode:() => current.promise}]};
  await flush();
  old.resolve(); await flush();
  assert.equal(fixture.tracker.pending.value, 1);
  assert.equal(fixture.bindings.scrollbarReady.value, false);
  fixture.dispose(); current.resolve(); await flush();
  assert.equal(fixture.tracker.pending.value, 0);
  assert.equal(fixture.bindings.scrollbarReady.value, false);
  assert.equal(fixture.events.length, 0);
});

async function roomPackage(nativeUi) {
  const fixture = runtime(), requests = [], commands = [], handlers = new Map(), presentations = [];
  const looks = {v2:{actionHeight:27, actionSkins:{Ok:['a','b','c','d']}, categorySizes:{weapons:{height:32}}}};
  fixture.sandbox.fetch = async url => ({ok:true, json:async () => url === 'ui.json' ? looks
    : url === 'pack.json' ? {builds:{'kr-812':'build.json'}, cell:[328,240], atlases:[]}
    : url === 'build.json' ? {items:{1:{sub:'Weapon_Rifle', name:'Arena'}}} : {}});
  const external = fixture.sandbox.require;
  fixture.sandbox.require = name => name === './messages.js' ? {labelsFor:() => ({editor:{}, open:'Ban Pick'})}
    : name === './presets.js' ? {parsePresets:() => [], presetStorageKey:'presets', writePresets:() => []} : external(name);
  const source = readFileSync(new URL('../../store-plugins/BubbleFighter/bubblefighter.fantasy.banpick/ban-pick.js', import.meta.url), 'utf8');
  runInNewContext(transpile(source), fixture.sandbox);
  const component = await fixture.sandbox.module.exports.default({
    vue:fixture.api, components:{BfUiProvider:'provider', BfBanPick:'editor', BfCaptionButton:'caption', BfMessageBox:'message', BfReveal:'reveal'},
    theme:'v2', locale:'en', buildContext:{buildId:'kr-812'}, packages:{url:file => file}, nativeUi,
    present:value => presentations.push(value),
    companion:{
      request(name, version, payload) {
        if (name === 'game.command.execute') { commands.push(payload); return Promise.resolve({status:'completed', response:{status:'success'}}); }
        const request = pending(); requests.push(request); return request.promise;
      },
      on(name, callback) { handlers.set(name, callback); return () => handlers.delete(name); },
      onState:() => () => {},
    },
  });
  const render = fixture.scope.run(() => component.setup());
  const nodes = () => render().children[0].children.default();
  const button = () => fixture.scope.run(() => nodes()[1].type.setup())();
  // The confirmation is wrapped in its native-scale zoom box; the room button in its measured panel box.
  const message = () => nodes().children[0];
  return {...fixture, requests, commands, presentations, nodes, button, message,
    room(payload = {in_room:true, is_host:true}) { handlers.get('game.ban.list.changed')(payload); },
    ask(payload = {command:'bp start fixture', expires_in_ms:10000}) { handlers.get('game.ban.start.confirm')(payload); },
    open() { return button().children[0].children[0].children().props.onClick(); },
    answer() { return message().props.onAction({name:'confirm'}); },
  };
}

const roomReply = {status:'completed', response:{in_room:true, is_host:true}};
async function openRoom(fixture) {
  const opening = fixture.open(); await flush();
  fixture.requests.shift().resolve(roomReply); await opening; await flush();
}

test('a late open and refresh cannot restore a departed room', async () => {
  const fixture = await roomPackage();
  try {
    fixture.room(); await flush();
    const opening = fixture.open(); await flush();
    fixture.room({in_room:false}); await flush();
    fixture.requests.shift().resolve(roomReply); await opening; await flush();
    assert.deepEqual(Array.from(fixture.nodes()), [null, null]);
    assert.equal(fixture.presentations.at(-1).visible, false);
  } finally { fixture.dispose(); }
});

test('old readiness timers cannot reveal a new room editor', async () => {
  const fixture = await roomPackage();
  try {
    fixture.room(); await flush(); await openRoom(fixture);
    const oldReady = fixture.takeTimer(8000);
    fixture.room({in_room:false}); await flush();
    fixture.room(); await flush(); await openRoom(fixture);
    oldReady();
    assert.equal(fixture.nodes()[0].props.style.opacity, 0);
    assert.equal(fixture.timers.size, 1);
  } finally { fixture.dispose(); }
  assert.equal(fixture.timers.size, 0);
});

test('room entry and prepared editor use actual opacity and input barriers', async () => {
  const fixture = await roomPackage();
  try {
    fixture.room(); await flush();
    const outer = fixture.scope.run(() => fixture.nodes()[1].type.setup()), renderButton = () => outer().children[0];
    assert.equal(renderButton().props.style.opacity, 0);
    assert.equal(renderButton().props.inert, true);
    renderButton().children[0].props.onReady(); await flush();
    assert.equal(renderButton().props.style.opacity, 1);
    assert.equal(renderButton().props.inert, false);
    const editor = fixture.nodes()[0];
    assert.equal(editor.props.style.opacity, 0);
    assert.equal(editor.props.inert, true);
    await openRoom(fixture);
    assert.equal(fixture.nodes()[0].props.style.opacity, 0);
    fixture.nodes()[0].children[0].props.onReady();
    assert.equal(fixture.nodes()[0].props.style.opacity, 1);
    assert.equal(fixture.nodes()[0].props.inert, false);
  } finally { fixture.dispose(); }
});

function nativeDialog() {
  const outcomes = [], calls = [];
  return {outcomes, calls, api:{
    async available() { return true; },
    async invoke(request) { calls.push(request); return {status:'completed', result:{status:'queued'}}; },
    result() { const outcome = pending(); outcomes.push(outcome); return outcome.promise; },
  }};
}

test('departing while native availability is pending cannot open a stale dialog', async () => {
  const native = nativeDialog(), available = pending();
  native.api.available = () => available.promise;
  const fixture = await roomPackage(native.api);
  try {
    fixture.room(); await flush(); fixture.ask(); await flush();
    fixture.room({in_room:false}); await flush();
    available.resolve(true); await flush();
    assert.equal(native.calls.length, 0);
    assert.equal(fixture.timers.size, 0);
    assert.equal(fixture.commands.length, 0);
  } finally { fixture.dispose(); }
});

for (const ending of ['leave', 'unmount', 'lose-host']) {
  test(`a native confirmation cannot issue a command after ${ending}`, async () => {
    const native = nativeDialog(), fixture = await roomPackage(native.api);
    try {
      fixture.room(); await flush(); fixture.ask(); await flush();
      fixture.takeTimer(500)(); await flush();
      assert.equal(native.outcomes.length, 1);
      if (ending === 'unmount') fixture.dispose();
      else fixture.room(ending === 'leave' ? {in_room:false} : {in_room:true, is_host:false, count:1});
      await flush();
      native.outcomes[0].resolve({result:{status:'selected', buttonId:'confirm'}}); await flush();
      assert.equal(fixture.commands.length, 0);
      assert.equal(fixture.timers.size, 0);
    } finally { if (ending !== 'unmount') fixture.dispose(); }
  });
}

test('room departure cancels a pending native poll timer', async () => {
  const native = nativeDialog(), fixture = await roomPackage(native.api);
  try {
    fixture.room(); await flush(); fixture.ask(); await flush();
    assert.equal(fixture.timers.size, 1);
    fixture.room({in_room:false}); await flush();
    assert.equal(fixture.timers.size, 0);
    assert.equal(native.outcomes.length, 0);
    assert.equal(fixture.commands.length, 0);
  } finally { fixture.dispose(); }
});

test('a live native confirmation still sends its original command exactly once', async () => {
  const native = nativeDialog(), fixture = await roomPackage(native.api);
  try {
    fixture.room(); await flush(); fixture.ask(); await flush();
    fixture.takeTimer(500)(); await flush();
    native.outcomes[0].resolve({result:{status:'selected', buttonId:'confirm'}}); await flush();
    assert.equal(fixture.commands.length, 1);
    assert.equal(fixture.commands[0].command, 'bp start fixture');
  } finally { fixture.dispose(); }
});

test('fallback confirmation and expiry are cleared on departure without affecting a later question', async () => {
  const fixture = await roomPackage();
  try {
    fixture.room(); await flush(); fixture.ask(); await flush();
    assert.equal(fixture.message().type, 'message');
    const oldExpiry = fixture.takeTimer(10000);
    fixture.room({in_room:false}); await flush();
    assert.deepEqual(Array.from(fixture.nodes()), [null, null]);
    assert.equal(fixture.timers.size, 0);
    fixture.room(); await flush(); fixture.ask({command:'bp start later', expires_in_ms:10000}); await flush();
    oldExpiry();
    assert.equal(fixture.message().type, 'message');
    await fixture.answer();
    assert.equal(fixture.commands.length, 1);
    assert.equal(fixture.commands[0].command, 'bp start later');
  } finally { fixture.dispose(); }
});

test('departure clears an active fallback confirmation and its stale action sends nothing', async () => {
  const fixture = await roomPackage();
  try {
    fixture.room(); await flush(); fixture.ask(); await flush();
    const action = fixture.message().props.onAction;
    assert.equal(fixture.timers.size, 1);
    fixture.room({in_room:false}); await flush();
    await action({name:'confirm'});
    assert.equal(fixture.timers.size, 0);
    assert.equal(fixture.commands.length, 0);
  } finally { fixture.dispose(); }
});
