import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createRequire} from 'node:module';
import {runInNewContext} from 'node:vm';
import {labelsFor, languageFor} from '../../store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension/messages.js';
import {describeModes} from '../../store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension/mode-descriptions.js';
import {subscribePanel} from '../../src/FantasyTale.Launcher.Overlay/overlay/panel-stack.js';
import {themeForClientDate} from '../../tools/game-ui/theme.js';
import {modeWindowGeometry} from '../../tools/game-ui/mode-extension.js';

const requireWeb = createRequire(new URL('../package.json', import.meta.url));
const vue = requireWeb('vue');
const source = readFileSync(new URL('../../store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension/mode-extension.js', import.meta.url), 'utf8')
  .replace(/^import .*;\r?\n/gm, '').replace('export default async function setup', 'module.exports = async function setup');
const packageId = 'bubblefighter.fantasy.mode-extension';
const resourceId = 'bubblefighter.fantasy.game-ui-resources';
const modes = [
  {id:1,key:'Pvp',names:{en:'No Item','zh-CN':'竞技模式'}},
  {id:3,key:'Item',names:{en:'Item','zh-CN':'道具模式'}},
  {id:18,key:'Bomb',names:{en:'Bomb Mode','zh-CN':'爆破模式'}},
];
const copy = value => JSON.parse(JSON.stringify(value));
const state = (overrides = {}) => ({revision:1,sessionId:'session-a',open:true,busy:false,selectedId:1,modes:copy(modes),error:null,...overrides});

async function fixture(t, options = {}) {
  const mounted = [], unmounting = [], emissions = [], presentations = [], requests = [], errors = [];
  const listeners = new Map(), panels = new Map([['panel', {listeners}]]), resizeListeners = new Set();
  const skins = {confirm:['normal','hover','pressed'],cancel:['cancel']};
  const appearance = {format:1,skins:{}}, looks = {v1:{actionSkins:skins},v2:{actionSkins:skins},v3:{}};
  const deliver = value => listeners.get('mode-extension.state')?.slice().forEach(callback => callback(value));
  const module = {exports:null};
  const noTimer = () => { throw new Error('The display module cannot poll or schedule native work.'); };
  runInNewContext(source, {
    module, labelsFor, languageFor, describeModes,
    fetch:async url => {
      requests.push(url);
      if (url === `https://packages.fantasytale.local/${packageId}/mode-skins.json`) return {ok:true,json:async()=>appearance};
      if (url === `https://packages.fantasytale.local/${resourceId}/ui.json`) return {ok:true,json:async()=>looks};
      throw new Error(`The display module requested nonvisual data: ${url}`);
    },
    console:{error:(...args)=>errors.push(args)}, innerWidth:options.width ?? 1024, innerHeight:options.height ?? 768,
    addEventListener:(name, callback) => { assert.equal(name, 'resize'); resizeListeners.add(callback); },
    removeEventListener:(name, callback) => { assert.equal(name, 'resize'); resizeListeners.delete(callback); },
    setTimeout:noTimer, setInterval:noTimer,
  }, {filename:'mode-extension.js'});
  const context = {
    vue:{...vue,h:(type, props, children) => ({type,props,children}),
      onMounted:callback=>mounted.push(callback),onBeforeUnmount:callback=>unmounting.push(callback)},
    components:{BfUiProvider:'provider',BfModeExtension:'picker',BfMessageBox:'error',BfCaptionButton:'caption',BfAnchoredPanel:'anchored',themeForClientDate,modeWindowGeometry},
    theme:options.theme ?? 'v2',locale:options.locale ?? 'en',buildContext:{buildDate:options.buildDate ?? '2015-01-01'},
    packages:{url:(path,id=packageId)=>`https://packages.fantasytale.local/${id}/${path}`},
    present:value=>presentations.push(copy(value)),
    on:(name,callback)=>subscribePanel(panels,'panel',name,callback),
    emit:(name,data)=>{
      emissions.push({name,...(data === undefined ? {} : {data:copy(data)})});
      if (name === 'mode-extension.ready') {
        assert.equal(listeners.get('mode-extension.state')?.length, 1, 'state listener must precede ready');
        if (options.initialState) deliver(options.initialState);
      }
    },
    get nativeUi() { throw new Error('The display module cannot consume the native UI bridge.'); },
  };
  const component = await module.exports(context), scope = vue.effectScope();
  const render = scope.run(() => component.setup());
  mounted.forEach(callback => callback());
  await vue.nextTick();
  let disposed = false;
  function dispose() {
    if (disposed) return;
    disposed = true;
    unmounting.forEach(callback => callback());
    scope.stop();
  }
  t.after(dispose);
  function find(type) {
    const visit = node => {
      if (!node) return undefined;
      if (Array.isArray(node)) return node.map(visit).find(Boolean);
      if (typeof type === 'function' ? type(node) : node.type === type) return node;
      return visit(Array.isArray(node.children) ? node.children : node.children?.default?.());
    };
    return visit(render());
  }
  return {emissions,presentations,requests,errors,listeners,resizeListeners,appearance,looks,
    find,dispose,deliver,send:async value=>{deliver(value);await vue.nextTick();}};
}

test('the display subscribes before ready, consumes visual resources only and releases its listeners', async t => {
  const f = await fixture(t, {initialState:state()});
  assert.equal(f.presentations[0].visible, false);
  assert.equal(f.find('picker').props.selectedId, 1);
  assert.deepEqual(f.emissions, [{name:'mode-extension.ready'}]);
  assert.equal(f.requests.length, 2);
  const queued = f.listeners.get('mode-extension.state').slice();
  const stale = f.find('picker').props;
  f.dispose();
  queued.forEach(callback=>callback(state({revision:2,sessionId:'session-b'})));
  stale.onConfirm(modes[1]); stale.onCancel();
  await vue.nextTick();
  assert.equal(f.listeners.size, 0);
  assert.equal(f.resizeListeners.size, 0);
  assert.equal(f.presentations.at(-1).visible, false);
  assert.equal(f.emissions.length, 1);
});

test('only increasing backend revisions change visibility or the active session', async t => {
  const f = await fixture(t);
  assert.equal(f.find('picker'), undefined);
  await f.send(state({revision:4}));
  const original = f.find('picker').props;
  await f.send(state({revision:3,open:false,sessionId:null}));
  await f.send({revision:4});
  assert.equal(f.find('picker').props.key, original.key);
  assert.equal(f.errors.length, 0);
  await f.send(state({revision:5,open:false,sessionId:null}));
  assert.equal(f.find('picker'), undefined);
  assert.equal(f.presentations.at(-1).visible, false);
  await f.send(state({revision:2}));
  assert.equal(f.find('picker'), undefined);
});

test('busy updates retain the draft seed, component identity and mode-card presentation', async t => {
  const f = await fixture(t, {initialState:state()});
  const original = f.find('picker').props;
  await f.send(state({revision:2,busy:true,selectedId:3}));
  const pending = f.find('picker').props;
  assert.equal(pending.busy, true);
  assert.equal(pending.selectedId, original.selectedId);
  assert.equal(pending.key, original.key);
  assert.equal(pending.modes, original.modes);
  assert.equal(f.presentations.length, 2);
  await f.send(state({revision:3,selectedId:18}));
  assert.equal(f.find('picker').props.selectedId, 1);
  await f.send(state({revision:4,sessionId:'session-b',selectedId:3}));
  assert.equal(f.find('picker').props.selectedId, 3);
  assert.notEqual(f.find('picker').props.key, original.key);
  const secondKey = f.find('picker').props.key;
  await f.send(state({revision:5,sessionId:'session-b',open:false}));
  await f.send(state({revision:6,sessionId:'session-b',selectedId:18}));
  assert.equal(f.find('picker').props.selectedId, 18);
  assert.notEqual(f.find('picker').props.key, secondKey);
});

test('confirmation and cancellation emit user intent and await backend closure', async t => {
  const f = await fixture(t, {initialState:state()});
  f.find('picker').props.onConfirm(modes[2]);
  assert.deepEqual(f.emissions.at(-1), {name:'mode-extension.confirm',data:{sessionId:'session-a',modeId:18}});
  assert.equal(f.find('picker').props.selectedId, 1);
  assert.equal(f.presentations.at(-1).visible, true);
  await f.send(state({revision:2,busy:true}));
  const pending = f.find('picker').props, count = f.emissions.length;
  pending.onConfirm(modes[1]); pending.onCancel();
  assert.equal(f.emissions.length, count);
  await f.send(state({revision:3}));
  f.find('picker').props.onCancel();
  assert.deepEqual(f.emissions.at(-1), {name:'mode-extension.cancel',data:{sessionId:'session-a'}});
  assert.ok(f.find('picker'));
  await f.send(state({revision:4,sessionId:null,open:false}));
  assert.equal(f.find('picker'), undefined);
});

test('callbacks from a replaced session cannot confirm, cancel, dismiss or replace the current error', async t => {
  const error = {status:'failed',reason:'nativeRejected',recovery:null};
  const f = await fixture(t, {initialState:state({error})});
  const oldPicker = f.find('picker').props, oldError = f.find('error').props;
  await f.send(state({revision:2,sessionId:'session-b',error}));
  const count = f.emissions.length;
  oldPicker.onConfirm(modes[1]); oldPicker.onCancel(); oldError.onAction(); oldPicker.onError(new Error('Late rendering failure'));
  assert.equal(f.emissions.length, count);
  assert.equal(f.errors.length, 0);
  f.find('error').props.onAction();
  assert.deepEqual(f.emissions.at(-1), {name:'mode-extension.dismiss',data:{sessionId:'session-b'}});
  assert.ok(f.find('error'));
  await f.send(state({revision:3,sessionId:'session-b'}));
  assert.equal(f.find('error'), undefined);
});

test('the backend mode list is authoritative and only descriptions are added for presentation', async t => {
  const f = await fixture(t, {initialState:state({modes:[modes[2]],selectedId:18})});
  const picker = f.find('picker').props;
  assert.deepEqual(copy(picker.modes), describeModes([modes[2]]));
  assert.equal(picker.modes[0].descriptions['zh-CN'], '安装炸弹！');
  picker.onConfirm(modes[0]);
  assert.equal(f.emissions.length, 1);
  await f.send(state({revision:2,modes:[],selectedId:null}));
  assert.equal(f.find('picker').props.modes.length, 0);
});

test('backend errors are localized, accept nullable recovery, and dismiss without a native session', async t => {
  const f = await fixture(t, {locale:'zh-CN'}), text = labelsFor('zh-CN');
  const cases = [
    [{status:'unsupported',reason:'bindingUnavailable',recovery:null}, text.unavailable],
    [{status:'operationUnavailable',reason:'bridgeUnavailable'}, text.unavailable],
    [{status:'outcomeUnknown',reason:'nativeReplyUnavailable',recovery:'restart'}, text.restartRequired],
    [{status:'failed',reason:'nativeRejected',recovery:null}, text.failed],
  ];
  for (const [index, [error, expected]] of cases.entries()) {
    await f.send(state({revision:index+1,open:false,sessionId:null,error}));
    const dialog = f.find('error').props;
    assert.equal(dialog.title, text.title);
    assert.equal(dialog.text, expected);
    dialog.onAction();
    assert.deepEqual(f.emissions.at(-1), {name:'mode-extension.dismiss',data:{sessionId:null}});
    assert.ok(f.find('error'));
  }
  await f.send(state({revision:5,open:false,sessionId:null}));
  assert.equal(f.presentations.at(-1).visible, false);
  assert.equal(f.errors.length, 0);
});

test('malformed new display state fails visibly without changing the last accepted session', async t => {
  const f = await fixture(t, {initialState:state()});
  await f.send(state({revision:2,sessionId:null}));
  assert.equal(f.errors.length, 1);
  assert.equal(f.find('error').props.text, labelsFor('en').failed);
  f.find('error').props.onAction();
  assert.deepEqual(f.emissions.at(-1), {name:'mode-extension.dismiss',data:{sessionId:'session-a'}});
  await f.send(state({revision:2}));
  assert.equal(f.find('error'), undefined);
  assert.equal(f.find('picker').props.selectedId, 1);
});

for (const theme of ['v1','v2','v3']) {
  test(`${theme} retains shared geometry and package-owned appearance`, async t => {
    const f = await fixture(t, {theme,initialState:state(),width:400,height:400});
    const picker = f.find('picker').props, provider = f.find('provider').props;
    assert.equal(picker.theme, theme);
    assert.equal(provider.theme, theme);
    assert.equal(picker.appearance, f.appearance);
    assert.equal(picker.actionSkins, f.looks[theme].actionSkins);
    assert.equal(picker.textScale, Math.min(1,384/modeWindowGeometry(theme).width,384/modeWindowGeometry(theme).height));
    assert.equal(picker.resourceRoot, `https://packages.fantasytale.local/${resourceId}/`);
    assert.equal(picker.modeResourceRoot, `https://packages.fantasytale.local/${packageId}/`);
  });
}

test('the dialog entry is a fitted native caption button zoomed by the anchored panel', async t => {
  const f = await fixture(t, {initialState:state({open:false,entry:{x:300,y:683,width:141,height:27,referenceWidth:0,referenceHeight:0}})});
  const entry = f.find(node => node.type?.props?.sessionId !== undefined);
  assert.ok(entry, 'the entry button is shown while the dialog is open');
  const panel = entry.type.setup({sessionId:'session-a'})();
  assert.equal(panel.type, 'anchored');
  assert.deepEqual([panel.props.width, panel.props.height], [141, 27]);
  const button = panel.children.default({zoom:1.5});
  assert.equal(button.type, 'caption');
  assert.equal(button.props.fit, true);
  assert.equal(button.props.width, undefined, 'a fitted button takes its width from its caption');
  assert.equal(button.props.textScale, 1.5);
});
