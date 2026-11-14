<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { BfBanPick, BfUiProvider, type BanPickItem, type BanPickLabels, type BanPickPreset, type BanPickAppearance } from '../src';
import { parsePresets, presetStorageKey, writePresets } from './preset-store';

interface PreparedItem { id: number; category: BanPickItem['category']; subCategory: string; preview: string; names: Record<string, string>; details: Record<string, {slogan: string; itemDesc: string; desc1: string; desc2: string}>; }
const { t, tm, locale } = useI18n();
const languages = [{ id: 'zh-CN', name: '简体中文' }, { id: 'zh-TW', name: '繁體中文' }, { id: 'ko', name: '한국어' }, { id: 'en', name: 'English' }, { id: 'id', name: 'Bahasa Indonesia' }, { id: 'vi', name: 'Tiếng Việt' }, { id: 'th', name: 'ไทย' }];
const prepared = ref<PreparedItem[]>([]);
const requestedTheme = new URL(window.location.href).searchParams.get('theme');
const theme = ref<'v1' | 'v2'>(requestedTheme === 'v1' ? 'v1' : 'v2');
const appearances = ref<Partial<Record<'v1' | 'v2', BanPickAppearance>>>({});
const assetRevision = ref(0);
let appearanceLoad = 0;
const appearance = computed(() => appearances.value[theme.value]);
const windowHeight = computed(() => appearance.value
  ? 739 + Math.max(...Object.values(appearance.value.categorySizes).map(size => size.height)) : 771);
const loadingError = ref('');
const open = ref(true);
const committed = ref([202025, 300003, 100004, 100050]);
const draft = ref([...committed.value]);
const saved = ref(false);
const presets = ref<BanPickPreset[]>([]);
const presetNotice = ref('');
const viewport = ref({width:window.innerWidth, height:window.innerHeight});
const sidebar = computed(() => viewport.value.width >= 960 && viewport.value.height < windowHeight.value + 79);
const displayScale = computed(() => Math.min(1,(viewport.value.width - 24)/772,
  (viewport.value.height - (sidebar.value?24:79) - ((saved.value?1:0)+(presetNotice.value?1:0))*30) / windowHeight.value));
const resize = () => { viewport.value = {width:window.innerWidth,height:window.innerHeight}; };
const storageReady = ref(true);
const items = computed<BanPickItem[]>(() => prepared.value.map(item => {
  const details = item.details[locale.value] ?? item.details.en!;
  return { ...item, name: item.names[locale.value] ?? item.names.en!, preview: './' + item.preview,
    slogan: details.slogan, description: details.itemDesc, detail1: details.desc1, detail2: details.desc2 };
}));
const labels = computed(() => tm('editor') as unknown as BanPickLabels);
watch(draft, value => { if (value.length !== committed.value.length || value.some(id => !committed.value.includes(id))) saved.value = false; });
watch(theme, value => {
  const url = new URL(window.location.href);
  url.searchParams.set('theme', value);
  window.history.replaceState(null, '', url);
});
function apply(value: number[]) { committed.value = [...value]; draft.value = [...value]; saved.value = true; }
function cancel() { draft.value = [...committed.value]; saved.value = false; open.value = false; }
function reopen() { draft.value = [...committed.value]; open.value = true; saved.value = false; }
function savePreset(name: string, itemIds: number[]) {
  if (!storageReady.value) return;
  const existing = presets.value.find(preset => preset.name === name);
  const next = { id: existing?.id ?? crypto.randomUUID(), name, itemIds: [...itemIds] };
  persist([...presets.value.filter(preset => preset.id !== next.id), next], 'presetSaved');
}
function deletePreset(id: string) { if (storageReady.value) persist(presets.value.filter(preset => preset.id !== id), 'presetDeleted'); }
function persist(next: BanPickPreset[], notice: string) {
  try { presets.value = writePresets(localStorage, next); presetNotice.value = notice; }
  catch { presetNotice.value = 'presetStorageError'; }
}
async function loadAppearance() {
  const revision = ++appearanceLoad;
  const response = await fetch('./ui.json', {cache:'no-store'});
  if (!response.ok) throw new Error(`ui.json: HTTP ${response.status}`);
  const next = await response.json() as Partial<Record<'v1' | 'v2', BanPickAppearance>>;
  for (const value of ['v1','v2'] as const) {
    if (!next[value]?.nameBounds || !next[value]?.titleBounds || !next[value]?.statusBounds) throw new Error(`${value}: regenerate the native UI resources with prepare.py`);
  }
  if (revision !== appearanceLoad) return;
  appearances.value = next;
  assetRevision.value++;
  loadingError.value = '';
}
async function loadCatalog() {
  const response = await fetch('./catalog.json',{cache:'no-store'});
  if (!response.ok) throw new Error(`catalog.json: HTTP ${response.status}`);
  prepared.value = await response.json();
}
const hot = (import.meta as ImportMeta & {hot?:{on(event:string, listener:()=>void):void;off(event:string, listener:()=>void):void}}).hot;
const refreshResources = () => { void loadCatalog().then(loadAppearance).catch(error => { loadingError.value = String(error); }); };
hot?.on('vite:afterUpdate', refreshResources);
hot?.on('ban-pick:resources-ready', refreshResources);
onBeforeUnmount(() => { appearanceLoad++; window.removeEventListener('resize',resize); hot?.off('vite:afterUpdate', refreshResources); hot?.off('ban-pick:resources-ready', refreshResources); });
onMounted(async () => {
  window.addEventListener('resize',resize);
  try { presets.value = parsePresets(localStorage.getItem(presetStorageKey)); }
  catch { storageReady.value = false; presetNotice.value = 'presetStorageError'; }
  try {
    await loadCatalog();
    await loadAppearance();
  } catch (error) { loadingError.value = String(error); }
});
</script>

<template>
  <main class="preview" :class="{'preview-sidebar':sidebar}" :style="{'--preview-scale':displayScale}">
    <div class="preview-toolbar">
      <span>{{ t('demo') }}</span>
      <label>{{ t('theme') }} <select v-model="theme"><option value="v1">{{ t('themes.v1') }}</option><option value="v2">{{ t('themes.v2') }}</option></select></label>
      <label>{{ t('language') }} <select v-model="locale"><option v-for="language in languages" :key="language.id" :value="language.id">{{ language.name }}</option></select></label>
      <button v-if="!open" type="button" @click="reopen">{{ t('open') }}</button>
    </div>
    <p v-if="loadingError" role="alert">{{ loadingError }}</p>
    <BfUiProvider v-else-if="open && prepared.length && appearance" :key="theme+'-'+assetRevision" :theme="theme" resource-root="./" :sound="false" @error="loadingError = String($event)">
      <BfBanPick v-model="draft" :items="items" :labels="labels" :presets="presets" :appearance="appearance" :text-scale="displayScale" resource-root="./" :locale="locale" @apply="apply" @cancel="cancel" @save-preset="savePreset" @delete-preset="deletePreset" @error="loadingError = String($event)" />
    </BfUiProvider>
    <p v-if="saved" class="preview-saved" role="status">{{ t('saved') }}</p>
    <p v-if="presetNotice" class="preview-saved" :role="presetNotice === 'presetStorageError' ? 'alert' : 'status'">{{ t(presetNotice) }}</p>
  </main>
</template>

<style>
@font-face{font-family:BF;src:url('/fonts/GB13000.ttf') format('truetype');font-weight:400;font-display:swap}
@font-face{font-family:BF;src:url('/fonts/DF_GB_Y9.TTF') format('truetype');font-weight:700;font-style:normal;font-display:swap}
@font-face{font-family:BFMalgun;src:url('/fonts/DF_GB_Y9.TTF') format('truetype');font-weight:400;font-display:swap}
@font-face{font-family:BFKorean;src:url('/fonts/NexonBTBold-web.otf') format('opentype');font-weight:700;font-style:normal;font-display:swap}
*{box-sizing:border-box}body{display:flow-root;margin:0;background:#233545;background-image:radial-gradient(ellipse at 50% 30%,#426783,transparent 65%);min-height:100vh;color:#e8f0f5;font:13px 'Segoe UI',sans-serif}
.preview{--preview-scale:min(1,calc((100vw - 24px)/772px));width:calc(772px * var(--preview-scale));margin:15px auto}.preview .bf-window{zoom:var(--preview-scale)}.preview-toolbar{height:39px;display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:10px;color:#c6d7e2}.preview-toolbar label{display:flex;align-items:center;gap:8px}.preview-toolbar select,.preview-toolbar button{font:inherit;color:#e2eef5;border:1px solid #608099;border-radius:4px;background:#304b60;padding:4px 7px}.preview-saved{text-align:center;color:#ffe36c;margin:10px 0}
.preview-sidebar{margin:12px auto}.preview-sidebar .preview-toolbar{position:fixed;left:12px;top:15px;width:168px;height:auto;flex-direction:column;align-items:flex-start;gap:12px;margin:0}
@media(max-width:630px){.preview-toolbar{gap:8px;font-size:11px}}
</style>
