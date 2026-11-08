import assert from 'node:assert/strict';
import {test} from 'node:test';
import {existsSync,readFileSync} from 'node:fs';
import {pathToFileURL} from 'node:url';
import {resolve,sep} from 'node:path';
import {createHash} from 'node:crypto';
import {narratorLines,nextNarratorPage} from './tutorial-model.js';
import {UI_SOUND_EVENTS} from './ui-sounds.js';

const explicit=Object.hasOwn(process.env,'FT_GAME_UI_RESOURCES');
const resources=explicit?pathToFileURL(resolve(process.env.FT_GAME_UI_RESOURCES)+sep):new URL('./local/',import.meta.url);
// Explicit roots and partially installed groups must fail, not silently skip.
const optional=directory=>({skip:!explicit&&!existsSync(new URL(directory,resources))?'Private resource group is not installed':false});
const json=path=>JSON.parse(readFileSync(new URL(path,resources)));

test('original tutorial atlas, transitions and PCM match the local client extraction',optional('tutorial/cn-279/'),()=>{
  const manifest=json('tutorial/cn-279/manifest.json'),font=json('tutorial/cn-279/glyphs.json');
  assert.equal(manifest.textPrintSpeed,70);
  assert.equal(manifest.font.weight,400);
  assert.equal(font.loadFlags,40968);
  const screenshotPage=manifest.dialogues.find(page=>page.stage==='Stage1'&&page.progress===21);
  assert.equal(screenshotPage.text,'在正式进入游戏之前，先要\n接受训练。');
  for(const page of manifest.dialogues)for(const character of page.text)assert.ok(font.glyphs[character],`Missing tutorial glyph: ${character}`);
  assert.equal(narratorLines('在',font.glyphs).height,17);
  assert.equal(narratorLines('在\n之',font.glyphs).height,34);
  const beforeGameplay=manifest.dialogues.find(page=>page.stage==='Stage1'&&page.progress===61);
  assert.equal(nextNarratorPage(manifest.dialogues,beforeGameplay,false),undefined);
  assert.equal(nextNarratorPage(manifest.dialogues,beforeGameplay,true).progress,80);
  const pages=manifest.dialogues.filter(page=>page.stage==='Stage1');
  assert.equal(nextNarratorPage(manifest.dialogues,pages.at(-1),true).progress,pages[0].progress);
  // Reference hashes describe decoded client PCM, not redistributable fixtures.
  const expected={type:'b34a156ea652ff0f9e0a65e28e3bcdca5ecc1bac63cf30807ca4be0da4e5a1b3',textbox:'37045ee6748bbce742d78aa9df45cefeaa42911d35eeb728919205f948accf81'};
  for(const [name,hash] of Object.entries(expected)){
    const wav=readFileSync(new URL(`tutorial/cn-279/Sounds/ui-tut-${name}.wav`,resources));
    assert.equal(wav.toString('ascii',0,4),'RIFF');
    assert.equal(wav.readUInt32LE(24),44100);
    assert.equal(wav.readUInt16LE(22),1);
    assert.equal(createHash('sha256').update(wav.subarray(44)).digest('hex'),hash);
  }
});

test('local setup copy fits its atlas and loops only in presentation mode',optional('tutorial/fantasy-setup/'),()=>{
  const setup=json('tutorial/fantasy-setup/manifest.json'),font=json('tutorial/fantasy-setup/glyphs.json');
  assert.equal(setup.dialogues.length,5,'One page per setup step');
  assert.ok(!setup.dialogues[0].text.includes('Bubble Fighter Fantasy'));
  for(const page of setup.dialogues){
    assert.ok(narratorLines(page.text,font.glyphs).height<=100);
    for(const line of page.text.split('\n')){
      for(const c of line)assert.ok(font.glyphs[c],`Missing setup glyph: ${c}`);
      assert.ok(Array.from(line).reduce((sum,c)=>sum+font.glyphs[c].advance,0)<=250);
    }
  }
  assert.equal(nextNarratorPage(setup.dialogues,setup.dialogues.at(-1),true).progress,1);
  assert.equal(nextNarratorPage(setup.dialogues,setup.dialogues.at(-1),false),undefined);
});

test('local UI sound WAV headers match the extracted bank manifest',optional('sounds/'),()=>{
  const manifest=json('sounds/manifest.json');
  for(const event of UI_SOUND_EVENTS){
    const spec=manifest.sounds[event];
    const wav=readFileSync(new URL('sounds/'+spec.file,resources));
    assert.equal(wav.toString('ascii',0,4),'RIFF');
    assert.equal(wav.readUInt16LE(22),1);
    assert.equal(wav.readUInt32LE(24),spec.sampleRate);
    assert.equal(wav.readUInt32LE(40),spec.sampleCount*2);
  }
});
