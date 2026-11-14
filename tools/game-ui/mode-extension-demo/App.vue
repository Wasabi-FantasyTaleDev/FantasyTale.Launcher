<script setup lang="ts">
import {computed,onMounted,onBeforeUnmount,ref} from 'vue';
import BfUiProvider from '../src/components/BfUiProvider.vue';
import BfModeExtension from '../src/components/BfModeExtension.vue';
import type {BfTheme} from '../src/context';
import type {ModeExtensionMode,ModeExtensionAppearance} from '../src/mode-extension';
import {messages} from './messages';
import {describeModes} from '../../../store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension/mode-descriptions.js';
import availability from '../../../store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension/mode-availability.json';
import {filterModesForBuild,modeWindowGeometry} from '../mode-extension.js';
const query=new URL(location.href).searchParams;
const theme=ref<BfTheme>((['v1','v2','v3'].includes(query.get('theme')??'')?query.get('theme'):'v3') as BfTheme);
const locale=ref(query.get('locale')??'zh-CN');
const labels=computed(()=>messages[locale.value]??messages.en!);
const modes=ref<ModeExtensionMode[]>([]),appearance=ref<ModeExtensionAppearance>();
const actionSkins=ref<Record<string,Record<string,string[]>>>({});
const error=ref(''),open=ref(true),committed=ref(1),draft=ref(1),applied=ref(0);
const size=ref({width:innerWidth,height:innerHeight});
const geometry=computed(()=>modeWindowGeometry(theme.value));
const scale=computed(()=>Math.min(1.25,(size.value.width-36)/geometry.value.width,(size.value.height-116)/geometry.value.height));
const resize=()=>{size.value={width:innerWidth,height:innerHeight};};
function confirm(mode:ModeExtensionMode){committed.value=mode.id;draft.value=mode.id;applied.value++;open.value=false;}
function cancel(){draft.value=committed.value;open.value=false;}
function reopen(){draft.value=committed.value;open.value=true;}
onMounted(async()=>{
  addEventListener('resize',resize);
  try{
    const [catalog,skins,ui]=await Promise.all(['catalog.json','mode-skins.json','ui.json'].map(async name=>{
      const response=await fetch('/'+name);if(!response.ok)throw new Error(`${name}: HTTP ${response.status}`);return response.json();
    }));
    modes.value=describeModes(filterModesForBuild(catalog.modes,availability,query.get('build')??'kr-1256'));appearance.value=skins;
    actionSkins.value=Object.fromEntries(Object.entries(ui).map(([name,value])=>[name,(value as {actionSkins:Record<string,string[]>}).actionSkins]));
  }catch(reason){error.value=String(reason);}
});
onBeforeUnmount(()=>removeEventListener('resize',resize));
</script>
<template>
  <main class="mode-preview">
    <nav class="mode-preview-toolbar">
      <select v-model="theme" aria-label="Window theme"><option>v1</option><option>v2</option><option>v3</option></select>
      <select v-model="locale" aria-label="Language"><option v-for="key in Object.keys(messages)" :key="key" :value="key">{{key}}</option></select>
      <button v-if="!open" type="button" @click="reopen">{{labels.title}}</button>
    </nav>
    <p v-if="error" role="alert">{{error}}</p>
    <div v-if="open&&appearance&&modes.length" class="mode-preview-window" :style="{width:geometry.width*scale+'px',height:geometry.height*scale+'px'}">
      <div :style="{zoom:scale}">
        <BfUiProvider :key="theme" :theme="theme" resource-root="/" :sound="true" @error="error=String($event)">
          <BfModeExtension :modes="modes" :selected-id="committed" :labels="labels" :appearance="appearance" :theme="theme" :locale="locale"
            :text-scale="scale" :action-skins="actionSkins[theme]" resource-root="/" @select="draft=$event" @confirm="confirm" @cancel="cancel" @error="error=String($event)"/>
        </BfUiProvider>
      </div>
    </div>
    <output class="mode-preview-state" :data-committed="committed" :data-draft="draft" :data-applied="applied" aria-live="polite">{{modes.find(mode=>mode.id===committed)?.names[locale]}}</output>
  </main>
</template>
<style>
@font-face{font-family:BFKorean;src:url('/fonts/NexonBTBold.woff2') format('woff2');font-weight:400 700}
*{box-sizing:border-box}html,body,#app{min-height:100%;margin:0}body{background:#24384a;color:#d8ebf4;font:13px 'Segoe UI',sans-serif}.mode-preview{min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;padding:22px 0;gap:18px}.mode-preview-toolbar{display:flex;gap:8px;align-items:center}.mode-preview-toolbar select,.mode-preview-toolbar button{background:#36536a;color:#eef9ff;border:1px solid #64839c;border-radius:4px;padding:5px 9px;font:inherit}.mode-preview-window{position:relative;flex:none}.mode-preview-state{height:20px;color:#9db5c8;font-size:12px}.mode-preview-state:empty{visibility:hidden}
</style>
