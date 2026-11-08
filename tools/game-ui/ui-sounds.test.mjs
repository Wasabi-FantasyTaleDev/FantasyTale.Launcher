import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createUiSoundPlayer,UI_SOUND_EVENTS,DEFAULT_UI_VOLUME,effectVolumeGain} from './ui-sounds.js';

// Lifecycle tests use synthetic metadata; original PCM checks run separately.
class FakeAudioContext {
  static instances=[];
  state='suspended';destination={};gains=[];sources=[];
  constructor(){FakeAudioContext.instances.push(this)}
  createGain(){const gain={gain:{value:1},connect(){},disconnect(){}};this.gains.push(gain);return gain}
  createBufferSource(){const source={connect(){},disconnect(){},start(){this.started=true},stop(){this.stopped=true}};this.sources.push(source);return source}
  async decodeAudioData(){return {duration:1}}
  async resume(){this.state='running'}
  async close(){this.state='closed'}
}
test('UI audio gain, unlock, mute, overlap and disposal need no private bank',async()=>{
const original={document:globalThis.document,fetch:globalThis.fetch,AudioContext:globalThis.AudioContext};
globalThis.document=new EventTarget();globalThis.AudioContext=FakeAudioContext;
const manifest={version:1,sounds:Object.fromEntries(UI_SOUND_EVENTS.map(event=>[event,{file:`${event}.wav`,gain:0.5}]))};
globalThis.fetch=async url=>({ok:true,json:async()=>manifest,arrayBuffer:async()=>new ArrayBuffer(8)});
try{
  assert.equal(effectVolumeGain(75),DEFAULT_UI_VOLUME);
  for(const value of [-1,101,NaN,Infinity,1.5])assert.throws(()=>effectVolumeGain(value));
  const player=createUiSoundPlayer({maxVoices:2});
  await player.preload();const context=FakeAudioContext.instances.at(-1);
  assert.equal(context.gains[0].gain.value,0.75);
  assert.equal(await player.play('press'),false,'No playback before an audio gesture');
  await player.unlock();assert.equal(await player.play('press'),true);
  assert.equal(context.gains.at(-1).gain.value,manifest.sounds.press.gain);
  assert.equal(context.gains[0].gain.value*context.gains.at(-1).gain.value,0.375);
  player.setVolume(0.2);assert.equal(context.gains[0].gain.value,0.2);
  await player.play('mouseIn');await player.play('popup');
  assert.equal(context.sources[0].stopped,true,'Bound overlap stops oldest voice');
  player.setEnabled(false);assert.equal(context.sources.at(-1).stopped,true);
  assert.equal(await player.play('press'),false);
  player.setEnabled(true);assert.equal(await player.play('cancel'),true);
  await assert.rejects(player.play('unknown'));
  assert.throws(()=>player.setVolume(1.1));
  assert.throws(()=>player.bind({dataset:{bfOpenSound:'unknown'}}));
  player.dispose();assert.equal(context.state,'closed');assert.equal(await player.play('press'),false);
  const muted=createUiSoundPlayer({enabled:false});muted.setEnabled(false);muted.dispose();
}finally{Object.assign(globalThis,original)}
});
