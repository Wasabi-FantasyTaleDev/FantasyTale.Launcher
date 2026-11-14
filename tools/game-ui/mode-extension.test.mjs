import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createModeSelection, createScrollDrag, dragScroll, filterModesForBuild, modeSkin, scrollGeometry, validateModeAvailability, validateModes} from './mode-extension.js';

const modes = [{id:1,key:'Pvp',names:{en:'Competitive'}}, {id:2,key:'Item',names:{en:'Item'}}];

test('choosing a button remains pending until Confirm, and successful application is explicit', () => {
  const selection = createModeSelection(modes, 1);
  selection.select(2);
  assert.equal(selection.draftId, 2);
  assert.equal(selection.committedId, 1);
  assert.equal(selection.confirm(), modes[1]);
  assert.equal(selection.committedId, 1, 'a native application can still fail after Confirm');
  selection.reset(2);
  assert.equal(selection.committedId, 2);
  assert.equal(selection.draftId, 2);
});

test('cancel and reopening retain the previously applied mode', () => {
  const selection = createModeSelection(modes, 1);
  selection.select(2);
  selection.cancel();
  assert.equal(selection.draftId, 1);
  selection.reset(1);
  assert.equal(selection.confirm(), modes[0]);
});

test('missing or removed choices cannot be confirmed', () => {
  const selection = createModeSelection(modes, 99);
  assert.equal(selection.confirm(), undefined);
  assert.throws(() => selection.select(99), /not in the catalogue/);
  selection.select(2);
  selection.replaceModes([modes[0]]);
  assert.equal(selection.confirm(), undefined);
  selection.replaceModes([]);
  assert.equal(selection.confirm(), undefined);
});

test('all catalogue rows remain available and duplicate numeric identities fail', () => {
  const all = Array.from({length:47}, (_, id) => ({id,key:`Mode${id}`,names:{en:`Mode ${id}`}}));
  assert.equal(validateModes(all), all);
  assert.equal(createModeSelection(all, 0).modes.length, 47);
  assert.throws(() => validateModes([...modes, modes[0]]), /unique integer IDs/);
});

test('missing named artwork uses the competitive graphic without changing identity', () => {
  const skins = {Noitem:{states:['0','1','2','3'],width:176,height:59}};
  assert.equal(modeSkin('ConquerTeam', skins), skins.Noitem);
  assert.equal(modeSkin('Item', skins), skins.Noitem);
  assert.equal(modes[1].key, 'Item');
});

test('scroll and drag endpoints use the 348-pixel modern track, excluding both arrows', () => {
  const start = scrollGeometry(1034, 384, -8, 348);
  assert.equal(start.maximum, 650);
  assert.equal(start.top, 0);
  const end = scrollGeometry(1034, 384, 99999, 348);
  assert.equal(end.top + end.height, 348);
  assert.equal(dragScroll(0, end.travel, start), 650);
  assert.equal(dragScroll(650, -end.travel, end), 0);
  assert.equal(dragScroll(0, -100, start), 0);
  assert.equal(dragScroll(650, 100, end), 650);
  assert.deepEqual(scrollGeometry(0, 384, 10, 348), {maximum:0,height:348,travel:0,top:0});
});

test('Zombie keeps its identity and text while the two older themes use the ghost artwork', () => {
  const skin = name => ({states:[0,1,2,3].map(state => `${name}/${state}`),width:176,height:59});
  const skins = {Noitem:skin('Noitem'),Zombie:skin('Zombie'),ZombieGhost:skin('ZombieGhost')};
  const zombie = {id:8,key:'Zombie',names:{en:'Zombie'},descriptions:{en:'Escape the zombies!'}};
  const selection = createModeSelection([zombie], 8);
  assert.equal(modeSkin(zombie.key, skins, 'v1'), skins.ZombieGhost);
  assert.equal(modeSkin(zombie.key, skins, 'v2'), skins.ZombieGhost);
  assert.equal(modeSkin(zombie.key, skins, 'v3'), skins.Zombie);
  assert.equal(selection.confirm(), zombie);
  assert.equal(zombie.descriptions.en, 'Escape the zombies!');
});

test('captured scroll dragging tracks outside moves and cleans up on release, cancel and lost capture', () => {
  class Events {
    listeners = new Map();
    addEventListener(name, callback) { this.listeners.set(name, callback); }
    removeEventListener(name, callback) { if (this.listeners.get(name) === callback) this.listeners.delete(name); }
    dispatch(name, event) { this.listeners.get(name)?.(event); }
  }
  const document = new Events(), thumb = new Events();
  thumb.ownerDocument = document;
  let captured, top = 0;
  thumb.setPointerCapture = id => { captured = id; };
  thumb.hasPointerCapture = id => captured === id;
  thumb.releasePointerCapture = () => { captured = undefined; };
  const geometry = scrollGeometry(1034,384,0,348);
  const drag = createScrollDrag({getTop:()=>top,setTop:value=>{top=value;},getGeometry:()=>geometry,getScale:()=>1.25});
  const event = (y, pointerId=7) => ({pointerId,clientY:y,button:0,currentTarget:thumb,preventDefault(){},stopPropagation(){}});
  drag.start(event(10));
  assert.equal(captured, 7);
  document.dispatch('pointermove', event(1000, 8));
  assert.equal(top, 0, 'a different touch cannot move this thumb');
  document.dispatch('pointermove', event(10 + geometry.travel * 1.25));
  assert.equal(top, 650);
  document.dispatch('pointerup', event(1000));
  assert.equal(document.listeners.size, 0);
  assert.equal(captured, undefined);
  drag.start(event(20));
  document.dispatch('pointermove', event(20 - geometry.travel * 1.25));
  assert.equal(top, 0);
  document.dispatch('pointercancel', event(20));
  assert.equal(document.listeners.size, 0);
  drag.start(event(20));
  thumb.dispatch('lostpointercapture', event(20));
  assert.equal(document.listeners.size, 0);
  assert.equal(thumb.listeners.size, 0);
  drag.stop();
});

test('build policy defaults to the complete catalogue and applies only explicit matching restrictions', () => {
  assert.equal(filterModesForBuild(modes,{format:1,rules:[]},undefined), modes);
  const policy = {format:1,rules:[{builds:['kr-*'],allow:[1,2]},{builds:['kr-812'],hidden:[2]}]};
  assert.deepEqual(filterModesForBuild(modes,policy,'kr-812'), [modes[0]]);
  assert.deepEqual(filterModesForBuild(modes,policy,'kr-1256'), modes);
  assert.equal(filterModesForBuild(modes,policy,'tw-454'), modes);
  assert.equal(modes.length, 2, 'filtering does not mutate the complete tag catalogue');
  assert.throws(() => filterModesForBuild(modes,policy,undefined), /current build ID/);
});

test('invalid build policy identities and patterns fail before displaying a partial catalogue', () => {
  assert.throws(() => validateModeAvailability({format:1,rules:[{builds:['kr-812'],hidden:[999]}]},modes), /catalogue IDs/);
  assert.throws(() => validateModeAvailability({format:1,rules:[{builds:['kr-812'],allow:[1,1]}]},modes), /distinct/);
  assert.throws(() => validateModeAvailability({format:1,rules:[{builds:['.*(kr)'],hidden:[]}]},modes), /build IDs/);
});
