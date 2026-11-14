<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch} from 'vue';
import BfNative from './BfNative.vue';
import BfWindow from './BfWindow.vue';
import BfReveal from './BfReveal.vue';
import {useGameUi, useDrawTracker, type BfRenderer, type BfTheme} from '../context';
import {paintText} from '../../text.js';
import {captionButtonGeometry,createCaptionButton,measureCaptionWidth,NATIVE_ACTION_HEIGHT} from '../../caption-button.js';
import {createModeSelection, createScrollDrag, modeSkin, scrollGeometry, modeWindowGeometry} from '../../mode-extension.js';
import type {ModeExtensionAppearance, ModeExtensionLabels, ModeExtensionMode} from '../mode-extension';

const props = withDefaults(defineProps<{
  modes: readonly ModeExtensionMode[];
  selectedId?: number;
  labels: ModeExtensionLabels;
  appearance: ModeExtensionAppearance;
  resourceRoot: string;
  modeResourceRoot?: string;
  theme: BfTheme;
  locale?: string;
  textScale?: number;
  busy?: boolean;
  actionSkins?: Readonly<Record<string, readonly string[]>>;
}>(), {locale:'zh-CN', textScale:1, busy:false});
const emit = defineEmits<{confirm:[ModeExtensionMode];cancel:[];select:[number];error:[Error]}>();
const selection = reactive(createModeSelection(props.modes, props.selectedId));
const revealed = ref(false);
const scrollbarReady = ref(false);
const visible = computed(() => revealed.value && scrollbarReady.value);
const viewport = ref<HTMLElement>();
const track = ref<HTMLElement>();
const scrollbar = ref<HTMLElement>();
const scrollTop = ref(0);
const legacyActionWidth = ref(65);
const legacyActionHeight = ref(27);
const ui = useGameUi();
const catalogue = computed(() => selection.modes);
const width = 602, viewHeight = 384, rowPitch = 65;
const windowGeometry = computed(() => modeWindowGeometry(props.theme));
const height = computed(() => windowGeometry.value.height);
const contentHeight = computed(() => Math.max(0, Math.ceil(catalogue.value.length / 3) * rowPitch - 6));
// The native v1/v2 scroll arrows are 21x20; dialogueNew uses 18x18.
const arrowHeight = computed(() => props.theme === 'v3' ? 18 : 20);
const trackHeight = computed(() => viewHeight - 2 * arrowHeight.value);
const geometry = computed(() => scrollGeometry(contentHeight.value, viewHeight, scrollTop.value, trackHeight.value));
const hasSelection = computed(() => Boolean(selection.confirm()));
const modeRevision = computed(() => [props.locale,props.textScale,props.appearance,props.theme,props.modes,props.modeResourceRoot]);
const confirmRevision = computed(() => [props.locale,props.theme,hasSelection.value,props.busy,props.textScale]);
const cancelRevision = computed(() => [props.locale,props.theme,props.busy,props.textScale]);
const family = computed(() => props.locale === 'ko' ? 'BFKorean' : 'BF');
const titleBounds = computed(() => props.theme === 'v1' ? {top:8,height:23} : props.theme === 'v2' ? {top:6,height:24} : {top:8,height:27});
const prefix = computed(() => props.resourceRoot.replace(/\/?$/, '/'));
const modePrefix = computed(() => (props.modeResourceRoot ?? props.resourceRoot).replace(/\/?$/, '/'));
const scrollPath = computed(() => 'Images/Ui/' + (props.theme === 'v3' ? 'dialogueNew/' : '') + 'ScrollbarVertical');
function sprite(index:number) {
  if (!ui.value) return undefined;
  const extension = ui.value.manifest.imageFormat === 'webp' ? 'webp' : 'png';
  return `${prefix.value}${props.theme}/${scrollPath.value}/${index}.${extension}`;
}
function drawFailed(error:Error) { emit('cancel'); emit('error',error); }
const drawTracker = useDrawTracker();
let scrollbarGeneration = 0, scrollbarPending = false;
function settleScrollbar() {
  if (scrollbarPending && drawTracker) { drawTracker.pending.value--; scrollbarPending = false; }
}
// These images are Vue markup outside BfNative, so they join the same readiness barrier explicitly.
watch(() => [scrollbar.value, props.theme, prefix.value, ui.value, ui.value?.manifest.imageFormat], async () => {
  const generation = ++scrollbarGeneration, element = scrollbar.value;
  scrollbarReady.value = false;
  if (!element || !ui.value) { settleScrollbar(); return; }
  if (drawTracker && !scrollbarPending) { drawTracker.pending.value++; scrollbarPending = true; }
  try {
    await Promise.all([...element.querySelectorAll('img')].map(image => image.decode()));
    if (generation !== scrollbarGeneration) return;
    scrollbarReady.value = true; settleScrollbar();
  } catch (reason) {
    if (generation !== scrollbarGeneration) return;
    settleScrollbar(); drawFailed(reason instanceof Error ? reason : new Error(String(reason)));
  }
}, {flush:'post'});
onBeforeUnmount(() => { scrollbarGeneration++; settleScrollbar(); });
function name(mode:ModeExtensionMode) { return mode.names[props.locale] ?? mode.names.en ?? mode.key; }
function description(mode:ModeExtensionMode) { return mode.descriptions?.[props.locale] ?? props.labels.hidden; }
function select(mode:ModeExtensionMode) { if (!props.busy) { selection.select(mode.id); emit('select',mode.id); } }
function confirm() { const mode = selection.confirm(); if (mode && !props.busy) emit('confirm',mode); }
function cancel() { if (!props.busy) { selection.cancel(); emit('cancel'); } }
function setScroll(value:number) { if (viewport.value) { viewport.value.scrollTop = value; syncScroll(); } }
function scroll(delta:number) { setScroll((viewport.value?.scrollTop ?? 0) + delta); }
function syncScroll() { scrollTop.value = viewport.value?.scrollTop ?? 0; }
function trackStart(event:PointerEvent) { scroll((event.offsetY < geometry.value.top ? -1 : 1) * viewHeight); }
watch(() => props.selectedId, value => { selection.reset(value); });
watch(() => props.modes, value => { selection.replaceModes(value); void nextTick(syncScroll); });
// Keep the button node and keyboard focus stable when only the pending choice changes.
watch(() => [selection.draftId, props.busy], () => {
  for (const button of viewport.value?.querySelectorAll<HTMLButtonElement>('[data-mode-id]') ?? []) {
    button.setAttribute('aria-checked', String(Number(button.dataset.modeId) === selection.draftId));
    button.disabled = props.busy;
  }
});
const drag = createScrollDrag({getTop:()=>viewport.value?.scrollTop ?? 0,setTop:setScroll,getGeometry:()=>geometry.value,
  getScale:()=>(track.value?.getBoundingClientRect().height ?? 0) / trackHeight.value});
onBeforeUnmount(() => drag.stop());
watch(() => props.theme, () => drag.stop());
function key(event:KeyboardEvent) {
  if (event.key === 'Escape') { event.preventDefault(); cancel(); return; }
  if (props.busy || !(event.target as HTMLElement).closest('[data-mode-id]')) return;
  const index = catalogue.value.findIndex((mode:ModeExtensionMode) => mode.id === selection.draftId);
  const moves:Record<string,number> = {ArrowLeft:-1,ArrowRight:1,ArrowUp:-3,ArrowDown:3,PageUp:-18,PageDown:18};
  if (!(event.key in moves)) return;
  event.preventDefault();
  const mode = catalogue.value[Math.max(0,Math.min(catalogue.value.length-1,(index < 0 ? 0 : index)+moves[event.key]!))];
  if (!mode) return;
  select(mode);
  void nextTick(() => {
    const button = viewport.value?.querySelector<HTMLElement>(`[data-mode-id="${mode.id}"]`);
    button?.focus({preventScroll:true}); button?.scrollIntoView({block:'nearest'});
  });
}
async function drawPanel(renderer:BfRenderer) {
  const element = document.createElement('div');
  await renderer.panel(element,0,0,580,402,props.theme === 'v3' ? 'Images/Ui/dialogueNew/dialogue_basic02.smf' : 'Images/Ui/FrameBox.smf');
  return element;
}
async function drawMode(renderer:BfRenderer, mode:ModeExtensionMode) {
  const skin = modeSkin(mode.key, props.appearance.skins, props.theme), button = document.createElement('button');
  button.type = 'button'; button.className = 'bf-mode-button'; button.dataset.modeId = String(mode.id);
  button.setAttribute('role','radio'); button.dataset.state='0';
  button.ariaLabel = name(mode); button.dataset.bfPressSound = 'toggleButtonOn';
  const layers = await Promise.all(skin.states.map(async path => {
    const image = new Image(); image.crossOrigin='anonymous'; image.src=modePrefix.value+path; image.alt='';
    image.draggable=false; await image.decode(); return image;
  }));
  button.append(...layers);
  const caption = document.createElement('span'); caption.className='bf-mode-name'; button.append(caption);
  // kr-1256 DlgCreateRoom_add_survival.ui keeps independent title and introduction labels inside each 176x59 button.
  await paintText(caption,name(mode),{family:family.value,size:15,weight:700,align:'left',vertical:'middle',
    color:'#232323',decoration:0,renderer:'freetype',resourceRoot:props.resourceRoot,pixelRatio:props.textScale,
    centerInk:true,inset:0,wrap:false,width:130,height:20});
  const introduction = document.createElement('span'); introduction.className='bf-mode-description'; button.append(introduction);
  await paintText(introduction,description(mode),{family:family.value,size:11,weight:400,align:'left',vertical:'middle',
    color:'#ffffff',decoration:0,renderer:'freetype',resourceRoot:props.resourceRoot,pixelRatio:props.textScale,
    centerInk:true,inset:0,wrap:false,width:140,height:16});
  button.setAttribute('aria-checked',String(mode.id===selection.draftId)); button.disabled=props.busy;
  for (const [event,state] of [['pointerenter',2],['pointerleave',0],['pointerdown',1],['pointerup',2],['pointercancel',0]] as const)
    button.addEventListener(event,()=>{button.dataset.state=String(state);});
  button.addEventListener('click',()=>select(mode));
  return button;
}
async function drawAction(renderer:BfRenderer, kind:'Ok'|'Cancel') {
  const label = kind==='Ok'?props.labels.confirm:props.labels.cancel;
  if (props.theme !== 'v3') {
    const skins = props.actionSkins?.[kind];
    if (!skins) throw new Error('Native confirmation and cancellation skins are required.');
    // Retain the native icon margin and fit both localized captions with the same compact width.
    const width = await measureCaptionWidth([props.labels.confirm,props.labels.cancel],family.value);
    const {height} = captionButtonGeometry(renderer,{skins:[...skins],width,height:NATIVE_ACTION_HEIGHT});
    legacyActionWidth.value = width;
    legacyActionHeight.value = height;
    const result = await createCaptionButton(renderer,{label,width,height,skins:[...skins],kind,
      disabled:props.busy||(kind==='Ok'&&!hasSelection.value),family:family.value,size:13,renderer:'freetype',resourceRoot:props.resourceRoot,pixelRatio:props.textScale});
    result.dataset.action=kind; result.dataset.bfPressSound=kind==='Cancel'?'cancel':'press'; return result;
  }
  const button=document.createElement('button'); button.type='button';button.className='control sprite-button';
  button.style.cssText='left:0;top:0;width:112px;height:33px';button.ariaLabel=label;button.dataset.action=kind;
  button.disabled=props.busy||(kind==='Ok'&&!hasSelection.value);button.dataset.bfPressSound=kind==='Cancel'?'cancel':'press';
  const type=kind==='Ok'?'dialogue_type01BTN':'dialogue_type02BTN';
  const layers:HTMLElement[]=[];
  for (const state of ['Up','Down','Hover','Disabled']) layers.push(await renderer.panel(button,0,0,112,33,`Images/Ui/dialogueNew/${type}-${state}.smf`));
  const caption=document.createElement('span');caption.style.cssText='position:absolute;inset:0;width:112px;height:33px;pointer-events:none';button.append(caption);
  await paintText(caption,label,{family:family.value,size:15,weight:400,color:'#232323',decoration:0,align:'center',vertical:'middle',centerInk:true,renderer:'freetype',resourceRoot:props.resourceRoot,pixelRatio:props.textScale});
  const paint=(state:number)=>layers.forEach((layer,index)=>{layer.style.visibility=index===(button.disabled?3:state)?'visible':'hidden';layer.style.pointerEvents='none';});
  paint(0);for(const [event,state] of [['pointerenter',2],['pointerleave',0],['pointerdown',1],['pointerup',2]] as const)button.addEventListener(event,()=>paint(state));
  button.addEventListener('click',()=>{if(!button.disabled)button.dispatchEvent(new CustomEvent('bf-action',{bubbles:true,detail:{name:kind}}));});
  return button;
}
onMounted(() => { void nextTick(syncScroll); });
</script>

<template>
  <BfReveal strict @ready="revealed=true">
  <div class="bf-mode-reveal" :inert="!visible" :aria-hidden="!visible"
    :style="{width:width+'px',height:height+'px',opacity:visible?1:0}">
  <BfWindow :title="labels.title" :width="width" :height="height" :close-label="labels.close" :title-align="theme==='v1'?'left':'center'"
    :font-family="family" :font-weight="600" text-renderer="freetype" :text-scale="textScale" :resource-root="resourceRoot" :title-bounds="titleBounds"
    @close="cancel" @error="drawFailed">
    <section class="bf-mode-content" :aria-busy="busy" @keydown="key">
      <div class="bf-mode-panel"><BfNative :draw="drawPanel" :revision="theme" @error="drawFailed"/></div>
      <div ref="viewport" class="bf-mode-viewport" role="radiogroup" :aria-label="labels.title" @scroll="syncScroll">
        <div class="bf-mode-grid">
          <BfNative v-for="mode in catalogue" :key="mode.id" :draw="renderer=>drawMode(renderer,mode)"
            :revision="modeRevision" @error="drawFailed"/>
        </div>
      </div>
      <div ref="scrollbar" class="bf-mode-scrollbar" :style="{'--scroll-width':(theme==='v3'?18:21)+'px','--arrow-height':arrowHeight+'px'}">
        <button class="bf-mode-scroll-arrow" type="button" :aria-label="labels.previous" @click="scroll(-rowPitch)"><img :src="sprite(12)" alt="" draggable="false"/></button>
        <div ref="track" class="bf-mode-scroll-track" @pointerdown.self="trackStart">
          <img class="bf-mode-track-image" :src="sprite(1)" alt="" draggable="false"/>
          <div class="bf-mode-scroll-thumb" role="scrollbar" tabindex="0" aria-orientation="vertical" :aria-label="labels.scroll" :aria-valuemin="0" :aria-valuemax="geometry.maximum" :aria-valuenow="scrollTop"
            :style="{height:geometry.height+'px',top:geometry.top+'px'}" @pointerdown="drag.start"
            @keydown.up.prevent="scroll(-rowPitch)" @keydown.down.prevent="scroll(rowPitch)" @keydown.home.prevent="scroll(-geometry.maximum)" @keydown.end.prevent="scroll(geometry.maximum)">
            <img :src="sprite(3)" alt="" draggable="false"/><img :src="sprite(4)" alt="" draggable="false"/><img :src="sprite(5)" alt="" draggable="false"/>
          </div>
        </div>
        <button class="bf-mode-scroll-arrow" type="button" :aria-label="labels.next" @click="scroll(rowPitch)"><img :src="sprite(15)" alt="" draggable="false"/></button>
      </div>
      <footer class="bf-mode-actions" :style="{top:windowGeometry.actionsTop+'px',height:windowGeometry.actionsHeight+'px','--action-width':(theme==='v3'?112:legacyActionWidth)+'px','--action-height':(theme==='v3'?33:legacyActionHeight)+'px'}">
        <div><BfNative :draw="renderer=>drawAction(renderer,'Ok')" :revision="confirmRevision" @action="confirm" @error="drawFailed"/></div>
        <div><BfNative :draw="renderer=>drawAction(renderer,'Cancel')" :revision="cancelRevision" @action="cancel" @error="drawFailed"/></div>
      </footer>
    </section>
  </BfWindow>
  </div>
  </BfReveal>
</template>

<style>
.bf-mode-reveal{position:relative}
.bf-mode-content{position:absolute;inset:0;pointer-events:none}
.bf-mode-content>div,.bf-mode-actions{pointer-events:auto}
.bf-mode-panel{position:absolute;left:11px;top:46px;width:580px;height:402px;pointer-events:none!important}
.bf-mode-viewport{position:absolute;left:21px;top:55px;width:544px;height:384px;overflow-y:auto;overflow-x:hidden;scrollbar-width:none;overscroll-behavior:contain}
.bf-mode-viewport::-webkit-scrollbar{display:none}
.bf-mode-grid{display:grid;grid-template-columns:repeat(3,176px);grid-auto-rows:59px;column-gap:8px;row-gap:6px}
.bf-mode-button{position:relative;border:0;background:transparent;padding:0;width:176px;height:59px;overflow:hidden;touch-action:pan-y}
.bf-mode-button>img{position:absolute;inset:0;width:176px;height:59px;pointer-events:none;visibility:hidden}
.bf-mode-button[aria-checked=true]>img:nth-child(4),
.bf-mode-button:not(:disabled)[aria-checked=false][data-state='0']>img:nth-child(1),
.bf-mode-button:not(:disabled)[aria-checked=false][data-state='1']>img:nth-child(2),
.bf-mode-button:not(:disabled)[aria-checked=false][data-state='2']>img:nth-child(3),
.bf-mode-button:disabled[aria-checked=false]>img:nth-child(2){visibility:visible}
.bf-mode-name{position:absolute;left:7px;top:12px;width:130px;height:20px;pointer-events:none;overflow:hidden}
.bf-mode-description{position:absolute;left:7px;top:31px;width:140px;height:16px;pointer-events:none;overflow:hidden}
.bf-mode-button:focus-visible{outline:2px solid #fff;outline-offset:-5px}
.bf-mode-scrollbar{position:absolute;right:15px;top:55px;width:var(--scroll-width);height:384px;display:flex;flex-direction:column}
.bf-mode-scroll-arrow{border:0;background:transparent;padding:0;width:var(--scroll-width);height:var(--arrow-height);flex:none;overflow:hidden}
.bf-mode-scroll-arrow>img{width:var(--scroll-width);height:var(--arrow-height);pointer-events:none}
.bf-mode-scroll-track{position:relative;flex:1;min-height:0}
.bf-mode-track-image{position:absolute;width:var(--scroll-width);height:100%;pointer-events:none}
.bf-mode-scroll-thumb{position:absolute;left:0;width:var(--scroll-width);display:flex;flex-direction:column;touch-action:none;min-height:24px}
.bf-mode-scroll-thumb>img{width:var(--scroll-width);pointer-events:none}
.bf-mode-scroll-thumb>img:nth-child(2){flex:1;min-height:1px}
.bf-mode-scroll-thumb>img:first-child,.bf-mode-scroll-thumb>img:last-child{height:11px}
.bf-mode-actions{position:absolute;left:0;width:602px;display:flex;gap:8px;justify-content:center;align-items:center}
.bf-mode-actions>div{position:relative;width:var(--action-width);height:var(--action-height)}
</style>
