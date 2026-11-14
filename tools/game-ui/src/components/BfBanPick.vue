<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import BfWindow from './BfWindow.vue';
import BfControl from './BfControl.vue';
import BfNative from './BfNative.vue';
import { useDrawTracker, type BfRenderer } from '../context';
import {paintText} from '../../text.js';
import {createCaptionButton} from '../../caption-button.js';
import {pageFromDrag} from '../../pagination.js';
import type { BanPickCategory, BanPickItem, BanPickLabels, BanPickPreset, BanPickAppearance, BanPickSprite } from '../ban-pick';

const props = withDefaults(defineProps<{
  modelValue: readonly number[];
  items: readonly BanPickItem[];
  labels: BanPickLabels;
  resourceRoot: string;
  appearance: BanPickAppearance;
  locale?: string;
  presets?: readonly BanPickPreset[];
  textScale?: number;
  /** A member who does not host the room sees the list but cannot change it. */
  readonly?: boolean;
  /** FighterConfig name -> the character's shown name, for the armor popups. */
  characters?: Readonly<Record<string, string>>;
  /** False while the editor is kept built but closed: no animation and no picture warming then. */
  active?: boolean;
  /** The sub tab a category opens on (it is also listed right after "all"). */
  defaultSubCategories?: Readonly<Partial<Record<BanPickCategory, string>>>;
}>(), { locale: 'zh-CN', presets: () => [], textScale:1, readonly:false, characters: () => ({}), defaultSubCategories: () => ({}), active: true });
const emit = defineEmits<{
  'update:modelValue': [number[]];
  apply: [number[]];
  cancel: [];
  'save-preset': [string, number[]];
  'delete-preset': [string];
  error: [Error];
  /** Every surface has drawn and the first page's pictures are decoded; the host shows the window now. */
  ready: [];
}>();
const categories: BanPickCategory[] = ['weapons', 'characters', 'clothes', 'accessories'];
const categoryHeight = computed(() => Math.max(...Object.values(props.appearance.categorySizes).map(size => size.height)));
const fontSize = (size: number) => Math.max(size >= 12 ? 10 : 1, Math.round(size * props.textScale)) / props.textScale;
const category = ref<BanPickCategory>('weapons');
const subCategory = ref(props.defaultSubCategories.weapons ?? 'all');
const query = ref('');
const page = ref(0);
const restrictedOnly = ref(false);
const describedId = ref<number>();
const presetName = ref('');
const presetId = ref('');
const presetsOpen = ref(false);
const presetDialog = ref<HTMLDialogElement>();
watch(presetsOpen, value => { if (value) presetDialog.value?.showModal(); else presetDialog.value?.close(); }, {flush:'post'});
const panel = ref<HTMLElement>();
const pageTrack = ref<HTMLElement>();
// The window stays invisible until every surface has drawn and the first page's pictures are decoded, then shows
// complete; later redraws keep their previous picture until replaced, so it never shows half-drawn again.
const drawTracker = useDrawTracker();
const shown = ref(false);
let disposed = false, revealGeneration = 0;
let revealTimer: ReturnType<typeof setTimeout> | undefined;
async function pictures() {
  const urls = new Set<string>();
  for (const item of visible.value) {
    if (typeof item.preview === 'string') urls.add(item.preview);
    else if (item.preview) urls.add(item.preview.url);
  }
  await Promise.all([...urls].map(url => { const image = Object.assign(new Image(), {crossOrigin: 'anonymous'}); image.src = url; return image.decode().catch(() => undefined); }));
  // The editor's own markup images too (category and sub tabs, page bar, frames).
  await Promise.all([...(panel.value?.querySelectorAll('img') ?? [])].map(image => image.decode().catch(() => undefined)));
}
function scheduleReveal() {
  if (shown.value || disposed) return;
  const generation = ++revealGeneration;
  clearTimeout(revealTimer);
  // Wait for a quiet moment: surfaces mount in waves, so "nothing pending" must hold for a frame or two.
  revealTimer = setTimeout(async () => {
    revealTimer = undefined;
    if (disposed || generation !== revealGeneration) return;
    if ((drawTracker?.pending.value ?? 0) > 0) return;
    await pictures();
    if (disposed || generation !== revealGeneration) return;
    if ((drawTracker?.pending.value ?? 0) === 0) {
      shown.value = true;
      emit('ready');
      // Built: warm every tab's first page and the next pages here, after the window itself is complete.
      // Every tab's and every sub tab's first page, pictures and names; then the next pages of the shown list.
      pin(categories.flatMap(firstPage));
      pin(categories.flatMap(subFirstPages));
      warmAhead();
    } else scheduleReveal();
  }, 50);
}
watch(() => drawTracker?.pending.value ?? 0, scheduleReveal, {immediate: true});
onBeforeUnmount(() => { disposed = true; revealGeneration++; clearTimeout(revealTimer); });
// The weapon popup follows the pointer over a card that has gauge values (kr-812 DlgWeaponSpecInfo).
const specHover = ref<{id:number; x:number; y:number}>();
const specItem = computed(() => props.items.find(item => item.id === specHover.value?.id && item.category !== 'characters'));
// The popup the client shows for the item: weapon gauges, set armor, or the clothing popup (newer clients' forms).
const specForm = computed(() => !specItem.value ? '' : specItem.value.spec?.length ? 'DlgWeaponInfoPopup.ui'
  : specItem.value.members ? 'DlgInfoPopupSetArmor.ui' : 'DlgInfoPopupArmor.ui');
const specSize = ref({width: 274, height: 168});
function trackSpec(event: MouseEvent, item: BanPickItem) {
  if (item.category === 'characters' || !panel.value) { specHover.value = undefined; return; }
  const box = panel.value.getBoundingClientRect();
  const zoom = box.width / panel.value.offsetWidth || 1;
  specHover.value = {id:item.id, x:(event.clientX - box.left) / zoom, y:(event.clientY - box.top) / zoom};
}
const specStyle = computed(() => {
  if (!specHover.value || !panel.value) return {};
  const {width, height} = specSize.value, room = {w: panel.value.offsetWidth, h: panel.value.offsetHeight};
  const x = specHover.value.x + 18 + width > room.w ? specHover.value.x - 8 - width : specHover.value.x + 18;
  const y = Math.min(Math.max(0, specHover.value.y + 20), Math.max(0, room.h - height));
  return {left: Math.max(0, x) + 'px', top: y + 'px'};
});
type UiNode = {type:string; attrs:Record<string,string>; children:UiNode[]};
const GAUGES = ['Damage', 'HitRatio', 'MoveSpeed', 'FireCoolTick', 'Eiz'];
const ARMOR_GAUGES = ['pnlArmorPoint', 'pnlReloadSpeed', 'pnlMoveSpeed'];
const ARMOR_VALUES = ['lblArmorPoint', 'lblReloadSpeed', 'lblMoveSpeed'];
function wearers(allow: string | undefined) {
  const names = props.characters;
  const tokens = !allow || allow.trim().toLowerCase() === 'all' ? Object.keys(names) : allow.trim().split(/\s+/);
  return tokens.map(token => names[token] ?? token).join(', ');
}
async function drawSpec(ui: BfRenderer) {
  const item = specItem.value, form = ui.manifest.forms[specForm.value] as UiNode | undefined;
  const root = document.createElement('div');
  if (!item || !form) return root;
  specSize.value = {width: Number(form.attrs.Width), height: Number(form.attrs.Height)};
  const gaugeValue = (name: string) => {
    const weapon = GAUGES.indexOf(name), armor = ARMOR_GAUGES.indexOf(name);
    return weapon >= 0 ? item.spec?.[weapon] : armor >= 0 ? item.setStats?.[armor] ?? 0 : undefined;
  };
  const headings = specForm.value === 'DlgWeaponInfoPopup.ui' ? props.labels.stats ?? []
    : [props.labels.wearable ?? '', ...(specForm.value === 'DlgInfoPopupSetArmor.ui' ? props.labels.armorStats ?? [] : [])];
  const named = (name: string): string | undefined => {
    if (['WeaponName', 'itemName', 'lblItemName'].includes(name)) return item.name;
    if (['useChars', 'lblAvailableCharacters'].includes(name)) return wearers(item.allow);
    if (['notice', 'lblNotice'].includes(name)) return props.labels.wearNotice ?? '';
    const value = ARMOR_VALUES.indexOf(name);
    return value >= 0 ? String(item.setStats?.[value] ?? 0) : undefined;
  };
  // Gauge widths follow the value; the ownership badge has no meaning here. Labels are drawn below through FreeType.
  const labels: {attrs:Record<string,string>; x:number; y:number}[] = [];
  const shape = (node: UiNode, x: number, y: number): UiNode => {
    const at = {x: x + Number(node.attrs.PosX || 0), y: y + Number(node.attrs.PosY || 0)};
    const children = node.children.filter(child => !['Owned', 'lblOwned', 'pnEnchantLv', 'pnlEnchantLv'].includes(child.attrs.Name ?? ''))
      .filter(child => { if (child.type !== 'UiLabel') return true; labels.push({attrs: child.attrs, ...at}); return false; })
      .map(child => shape(child, at.x, at.y));
    const value = gaugeValue(node.attrs.Name ?? '');
    const attrs = value === undefined ? node.attrs : {...node.attrs, Width: String(Math.max(0, Math.round(Number(node.attrs.Width) * Math.min(100, value) / 100)))};
    return {...node, attrs, children};
  };
  const shaped = shape(form, 0, 0);
  const element = (await ui.renderNode({...shaped, attrs: {...shaped.attrs, PosX: '0', PosY: '0'}}, root))!;
  const names = labels.filter(label => !label.attrs.Name && !/^\d+$/.test(label.attrs.Text ?? '')).sort((a, b) => Number(a.attrs.PosY) - Number(b.attrs.PosY));
  for (const label of labels) {
    const a = label.attrs, stat = names.indexOf(label);
    const text = named(a.Name ?? '') ?? (stat >= 0 ? headings[stat] ?? a.Text ?? '' : a.Text ?? '');
    const span = document.createElement('span');
    span.className = 'control bf-label';
    span.style.cssText = `position:absolute;left:${label.x + Number(a.PosX)}px;top:${label.y + Number(a.PosY)}px;width:${a.Width}px;height:${a.Height}px;pointer-events:none`;
    element.append(span);
    const size = Number(a.FontSize) || 12, format = Number(a.TextFormat);
    await paintText(span, text, {color: '#' + (a.TextColor ?? '0xFFE4E4E4').slice(-6), size: fontSize(size), family: familyFor(text),
      weight: 400, renderer: 'freetype', align: format & 1 ? 'center' : format & 2 ? 'right' : 'left', vertical: format & 4 ? 'middle' : 'top',
      centerInk: Boolean(format & 4) && !(format & 16), wrap: Boolean(format & 16), decoration: 0, inset: 0,
      resourceRoot: props.resourceRoot, pixelRatio: props.textScale});
  }
  return element;
}
let pageDrag: {pointer: number; x: number; page: number; travel: number} | undefined;
function startPageDrag(event: PointerEvent) {
  if (pageCount.value <= 1 || !pageTrack.value) return;
  const thumb = event.currentTarget as HTMLElement;
  pageDrag = {pointer:event.pointerId, x:event.clientX, page:page.value,
    travel:pageTrack.value.getBoundingClientRect().width - thumb.getBoundingClientRect().width};
  thumb.setPointerCapture(event.pointerId);
  event.preventDefault();
}
function movePageDrag(event: PointerEvent) {
  if (pageDrag?.pointer === event.pointerId) page.value = pageFromDrag(pageDrag.page, event.clientX - pageDrag.x, pageDrag.travel, pageCount.value);
}
function selectTrackPage(event: PointerEvent) {
  if (!pageTrack.value) return;
  const rect = pageTrack.value.getBoundingClientRect();
  page.value = Math.max(0, Math.min(pageCount.value - 1, Math.floor((event.clientX - rect.left) * pageCount.value / rect.width)));
}
function pageKey(event: KeyboardEvent) {
  const keys: Record<string, number> = {ArrowLeft:page.value - 1, ArrowDown:page.value - 1, ArrowRight:page.value + 1, ArrowUp:page.value + 1,
    Home:0, End:pageCount.value - 1, PageUp:page.value - 1, PageDown:page.value + 1};
  const next = keys[event.key];
  if (next === undefined) return;
  event.preventDefault();
  page.value = Math.max(0, Math.min(pageCount.value - 1, next));
}
// A set counts as banned when every piece is; the model holds item ids only.
const selected = computed(() => {
  const ids = new Set(props.modelValue);
  for (const item of props.items) if (item.members?.length && item.members.every(id => ids.has(id))) ids.add(item.id);
  return ids;
});
const subCategories = computed(() => {
  const first = props.defaultSubCategories[category.value];
  return [...new Set(props.items.filter(item => item.category === category.value).map(item => item.subCategory))]
    .sort((a, b) => Number(b === first) - Number(a === first)
      || (props.appearance.subCategoryOrder[a] ?? 0) - (props.appearance.subCategoryOrder[b] ?? 0));
});
const matches = computed(() => props.items.filter(item => item.category === category.value
  // A member who cannot edit sees only what the host banned.
  && ((!restrictedOnly.value && !props.readonly) || selected.value.has(item.id))
  && (subCategory.value === 'all' || item.subCategory === subCategory.value)
  && (!query.value.trim() || `${item.name} ${item.id}`.toLocaleLowerCase(props.locale).includes(query.value.trim().toLocaleLowerCase(props.locale)))));
// Four columns by two rows: the same 180x178 cards, but a window short enough for a 768-pixel game at full scale.
const pageSize = 8;
const pageCount = computed(() => Math.max(1, Math.ceil(matches.value.length / pageSize)));
const visible = computed(() => matches.value.slice(page.value * pageSize, page.value * pageSize + pageSize));

// Picture warming (owner's rule): the first page of every tab and every sub tab is decoded once the editor is built
// and stays decoded while the editor lives (it is released when the player leaves the room). The pages after the
// shown one are warmed on a small rolling set of NEXT_LIMIT sheets that never displaces a first page.
const NEXT_LIMIT = 3;
const pinned = new Map<string, HTMLImageElement>();
const warmed = new Map<string, HTMLImageElement>();
function decoded(url: string) {
  const image = Object.assign(new Image(), {crossOrigin: 'anonymous'});
  image.src = url;
  void image.decode().catch(() => undefined);
  return image;
}
function pin(list: readonly BanPickItem[]) {
  if (disposed) return;
  warmText(list, true);
  for (const url of new Set(list.map(pictureUrl).filter((url): url is string => Boolean(url))))
    if (!pinned.has(url)) pinned.set(url, warmed.get(url) ?? decoded(url));
}
function pictureUrl(item: BanPickItem) {
  return typeof item.preview === 'string' ? item.preview : item.preview?.url;
}
function warm(list: readonly BanPickItem[]) {
  for (const url of new Set(list.map(pictureUrl).filter((url): url is string => Boolean(url)))) {
    if (pinned.has(url)) continue;
    const kept = warmed.get(url);
    if (kept) { warmed.delete(url); warmed.set(url, kept); continue; }
    warmed.set(url, decoded(url));
    while (warmed.size > NEXT_LIMIT) warmed.delete(warmed.keys().next().value!);
  }
}
// Caption warming: draw upcoming item names once, one at a time between frames; the text renderer keeps the result,
// so the real card copies it. Card states ("allowed"/"banned") are shared by every card and cached by the first.
const pinnedText = new Map<string, BanPickItem>();
const rollingText = new Map<string, BanPickItem>();
const finishedText = new Set<string>();
let textRunning = false;
let textTimer: ReturnType<typeof setTimeout> | undefined;
function textKey(item: BanPickItem) {
  return JSON.stringify([item.name, familyFor(item.name), fontSize(12), props.textScale,
    props.appearance.nameBounds.height, props.resourceRoot]);
}
function scheduleText() {
  if (disposed || textRunning || textTimer !== undefined) return;
  textTimer = setTimeout(() => {
    textTimer = undefined;
    if (disposed) return;
    const task = [...pinnedText, ...rollingText].find(([key]) => !finishedText.has(key));
    if (!task) return;
    const [key, item] = task;
    textRunning = true;
    void drawItemName(item).catch(() => undefined).finally(() => {
      textRunning = false;
      if (disposed) return;
      if (pinnedText.has(key) || rollingText.has(key)) finishedText.add(key);
      scheduleText();
    });
  }, 0);
}
function warmText(list: readonly BanPickItem[], firstPage = false) {
  if (disposed) return;
  const target = firstPage ? pinnedText : rollingText;
  if (!firstPage) target.clear();
  for (const item of list) target.set(textKey(item), item);
  for (const key of finishedText) if (!pinnedText.has(key) && !rollingText.has(key)) finishedText.delete(key);
  scheduleText();
}
// The first page every tab opens on, as the editor would list it.
function firstPage(value: BanPickCategory) {
  const sub = props.defaultSubCategories[value];
  return props.items.filter(item => item.category === value && (!sub || item.subCategory === sub)
    && (!props.readonly || selected.value.has(item.id))).slice(0, pageSize);
}
// The first page of every sub tab of a tab, as the editor would list it.
function subFirstPages(value: BanPickCategory) {
  const listed = props.items.filter(item => item.category === value && (!props.readonly || selected.value.has(item.id)));
  return [...new Set(listed.map(item => item.subCategory))].flatMap(sub => listed.filter(item => item.subCategory === sub).slice(0, pageSize));
}
// Owner's rule: the page after the shown one fully (pictures and names), the next three pages' names.
function warmAhead() {
  if (disposed || !props.active) return;
  const at = (offset: number, count: number) => matches.value.slice((page.value + offset) * pageSize, (page.value + offset + count) * pageSize);
  warm(at(1, 1));
  warmText(at(1, 3));
}
// The next pages of what is listed now.
watch([matches, page], () => warmAhead());
onBeforeUnmount(() => {
  clearTimeout(textTimer); pinned.clear(); warmed.clear(); pinnedText.clear(); rollingText.clear(); finishedText.clear();
});
// Closing drops only the next-page sheets; the first pages stay pinned until the editor is released.
watch(() => props.active, value => {
  if (!value) { warmed.clear(); warmText([]); return; }
  warmAhead();
});
const prefix = computed(() => props.resourceRoot.replace(/\/?$/, '/') + props.appearance.theme + '/');
const asset = (path: string) => prefix.value + (props.appearance.imageFormat === 'webp' ? path.replace(/\.png$/i, '.webp') : path);
const inspected = computed(() => props.items.find(item => item.id === describedId.value) ?? visible.value[0]);
const cardFrame = () => asset('Images/Shop2/inventory_box/inventory_slot01/0-card.png');
const checkAttrs = computed(() => ({ Name: props.labels.restrictedOnly, PosX: '0', PosY: '0', Width: '21', Height: '18', Check: restrictedOnly.value ? '1' : '0' }));
const tabImage = (path: string, active: boolean) => asset(path + `/${active ? 1 : 0}-blank.png`);
const count = (value: BanPickCategory) => props.items.filter(item => item.category === value && !item.members && selected.value.has(item.id)).length;
// A sheet cell is scaled to fit the 164x120 model view, centred, as a single image with object-fit: contain would be.
function spriteStyle(sprite: BanPickSprite) {
  const scale = Math.min(164 / sprite.width, 120 / sprite.height);
  return {backgroundImage:`url("${sprite.url}")`, backgroundRepeat:'no-repeat',
    backgroundSize:`${sprite.sheetWidth * scale}px ${sprite.sheetHeight * scale}px`,
    backgroundPosition:`${(164 - sprite.width * scale) / 2 - sprite.x * scale}px ${(120 - sprite.height * scale) / 2 - sprite.y * scale}px`};
}
function toggle(id: number) {
  describedId.value = id;
  if (props.readonly) return;
  const next = new Set(props.modelValue);
  const members = props.items.find(item => item.id === id)?.members;
  if (members?.length) {
    const banned = members.every(member => next.has(member));
    for (const member of members) { if (banned) next.delete(member); else next.add(member); }
  } else if (!next.delete(id)) next.add(id);
  emit('update:modelValue', [...next].sort((a, b) => a - b));
}
// Select all bans every item the current tab, sub tab and search show (all pages); pressed again when they are
// all banned, it allows them again.
function selectAll() {
  if (props.readonly) return;
  const next = new Set(props.modelValue);
  const shown = [...new Set(matches.value.flatMap(item => item.members?.length ? [...item.members] : [item.id]))];
  const every = shown.length > 0 && shown.every(id => next.has(id));
  for (const id of shown) { if (every) next.delete(id); else next.add(id); }
  emit('update:modelValue', [...next].sort((x, y) => x - y));
}
function loadPreset() {
  const preset = props.presets.find(value => value.id === presetId.value);
  if (preset) emit('update:modelValue', [...preset.itemIds]);
}
watch(presetId, value => { presetName.value = props.presets.find(preset => preset.id === value)?.name ?? ''; });
watch(() => props.presets, values => { if (!values.some(preset => preset.id === presetId.value)) presetId.value = ''; });
watch(category, value => { subCategory.value = props.defaultSubCategories[value] ?? 'all'; page.value = 0; describedId.value = undefined; });
watch([subCategory, query, restrictedOnly], () => { page.value = 0; describedId.value = undefined; });
watch(pageCount, value => { page.value = Math.min(page.value, value - 1); });

async function drawInfo(ui: BfRenderer) {
  if (ui.theme !== props.appearance.theme) throw new Error('Inventory theme and UI provider must match.');
  // kr-812 sub_95BB60 fills DlgItemInfo's labels from the selected ShopItem row.
  const original = ui.manifest.forms['DlgItemInfo.ui'];
  if (!original) throw new Error('DlgItemInfo.ui is missing from the prepared UI resources.');
  const ratio = 742 / props.appearance.infoSize.width;
  const source = {...original, attrs:{...original.attrs, Width:'742'}, children:original.children.map((node: {attrs:Record<string,string>}) => ({
    ...node, attrs:{...node.attrs, PosX:String(Math.round(Number(node.attrs.PosX) * ratio)),
      Width:String(Math.max(1, Math.round(Number(node.attrs.Width) * ratio)))},
  }))};
  const item = inspected.value;
  const text: Record<string, string> = {
    lblSlogan: item?.slogan ?? '', lblItemName: item?.name ?? '',
    lblItemDesc: item?.description || props.labels.noDescription,
    lblDesc1: item?.detail1 ?? '', lblDesc2: item?.detail2 ?? '', lblStock: '',
  };
  const root = (await ui.renderNode({ ...source, children: source.children.filter((node: {type: string}) => node.type !== 'UiLabel') }, document.createElement('div')))!;
  const originalBar = props.appearance.infoTitleBar;
  const bar = originalBar ? {...originalBar, x:Math.round(originalBar.x * ratio), width:Math.round(originalBar.width * ratio)} : null;
  if (bar) {
    const title = document.createElement('canvas');
    title.width = bar.width; title.height = bar.height;
    title.style.cssText = `position:absolute;left:${bar.x}px;top:${bar.y}px;width:${bar.width}px;height:${bar.height}px`;
    title.ariaHidden = 'true';
    const context = title.getContext('2d')!;
    context.imageSmoothingEnabled = false;
    const frames = ui.manifest.sprites[bar.skin];
    const left = frames[0][2] - frames[0][0] + 1, right = frames[2][2] - frames[2][0] + 1;
    const widths = [left, bar.width - left - right, right];
    let x = 0;
    for (let frame = 0; frame < 3; frame++) {
      const image = Object.assign(new Image(), { crossOrigin: 'anonymous' }); image.src = asset(bar.skin.replace('.smf', '') + `/${frame}.png`);
      await image.decode(); context.drawImage(image, x, 0, widths[frame]!, bar.height); x += widths[frame]!;
    }
    const image = Object.assign(new Image(), { crossOrigin: 'anonymous' }); image.className = 'control'; image.alt = ''; image.ariaHidden = 'true';
    image.style.cssText = title.style.cssText; image.src = title.toDataURL('image/png');
    await image.decode(); root.append(image);
  }
  for (const node of source.children.filter((value: {type: string}) => value.type === 'UiLabel')) {
    const a = node.attrs;
    const name = a.Name;
    const label = document.createElement('span');
    label.className = 'control bf-label';
    label.style.cssText = `left:${a.PosX}px;top:${a.PosY}px;width:${a.Width}px;height:${a.Height}px`;
    root.append(label);
    const format = Number(a.TextFormat);
    await paintText(label, text[name] ?? '', {color:'#' + a.TextColor.slice(-6),
      size: fontSize(Number(a.FontSize)), family: familyFor(text[name] ?? '', a.FontName === 'Malgun' ? 'BFMalgun' : 'BF'),weight:700,renderer:'freetype',
      align: format & 1 ? 'center' : format & 2 ? 'right' : 'left',
      outline: a.TextDecoColor ? '#' + a.TextDecoColor.slice(-6) : undefined,
      decoration: Number(a.TextDecoration),wrap:Boolean(format & 16),nativeMetrics:true,
      vertical: format & 4 ? 'middle' : 'top',centerInk:Boolean(format & 4) && !(format & 16),inset:0,resourceRoot:props.resourceRoot,pixelRatio:props.textScale});
  }
  return root;
}

// Item names and descriptions come from the client's own data, so they keep the client's script whatever the
// launcher language is; the Chinese face has no Hangul, and a canvas font has no per-glyph fallback to the Korean face.
const HANGUL = /[ᄀ-ᇿ㄰-㆏가-힯]/;
function familyFor(text: string, fallback?: string) {
  return props.locale === 'ko' || HANGUL.test(text) ? 'BFKorean' : fallback ?? 'BF';
}

function actionWidth(label: string) {
  const context = document.createElement('canvas').getContext('2d')!;
  context.font = `700 ${fontSize(12)}px ${familyFor(label)}`;
  return Math.max(67, Math.ceil(context.measureText(label).width) + 39);
}

async function drawCaption(text: string, rect: {left:number;top:number;width:number;height:number}, color: string, align:'left'|'center' = 'left', outline?:string, size = 12) {
  const label = document.createElement('span'); label.className = 'control';
  label.style.cssText = `position:absolute;left:${rect.left}px;top:${rect.top}px;width:${rect.width}px;height:${rect.height}px;pointer-events:none;text-shadow:none`;
  await paintText(label, text, {family:familyFor(text),size:fontSize(size),weight:700,resourceRoot:props.resourceRoot,pixelRatio:props.textScale,
    color,renderer:'freetype',centerInk:true,inset:0,width:rect.width,height:rect.height,align,
    outline,outlineWidth:0.5,decoration:outline?4:1});
  return label;
}
async function drawItemName(item: BanPickItem) {
  const label = await drawCaption(item.name, {left:26,top:props.appearance.nameBounds.top,width:140,height:props.appearance.nameBounds.height}, '#454545');
  label.classList.add('ban-pick-item-name'); return label;
}
async function drawItemState(item: BanPickItem) {
  const banned = selected.value.has(item.id);
  const label = await drawCaption(banned?props.labels.banned:props.labels.allowed,
    {left:4,top:props.appearance.statusBounds.top,width:164,height:props.appearance.statusBounds.height}, '#4b4b4b','center');
  label.classList.add('ban-pick-item-state'); return label;
}
async function drawCategory(value: BanPickCategory) {
  const size = props.appearance.categorySizes[value];
  const label = await drawCaption(props.labels.categories[value], {left:0,top:0,width:size.width,height:size.height}, '#fff','center','#3d4d7b', props.locale.startsWith('zh') || props.locale === 'ko' ? 15 : 11);
  label.classList.add('ban-pick-category-caption'); return label;
}
// Inline captions (hint, counters, sub tabs, badge, page) go through the same FreeType path as the rest of the
// editor; the box is the text's measured width so it can sit in flex rows and buttons.
function textWidth(text: string, size: number) {
  const context = document.createElement('canvas').getContext('2d')!;
  context.font = `700 ${fontSize(size)}px ${familyFor(text)}`;
  return Math.ceil(context.measureText(text).width) + 6;
}
async function drawInline(text: string, color: string, size = 12, height = 18, outline?: string) {
  const box = document.createElement('span');
  const width = textWidth(text, size);
  box.style.cssText = `position:relative;display:inline-block;width:${width}px;height:${height}px;vertical-align:middle;pointer-events:none`;
  box.append(await drawCaption(text, {left:0, top:0, width, height}, color, 'center', outline, size));
  return box;
}
async function drawBanPickLabel() {
  const label = await drawCaption(props.labels.restrictedOnly, {left:30,top:0,width:Math.max(112,actionWidth(props.labels.restrictedOnly)-8)-30,height:18}, '#456779');
  label.classList.add('ban-pick-checkbox-caption'); return label;
}

async function drawAction(ui: BfRenderer, label: string, width: number, disabled = false, kind: 'Ok' | 'Cancel' = 'Ok') {
  if (ui.theme !== props.appearance.theme) throw new Error('Button theme and UI provider must match.');
  return createCaptionButton(ui, {label, width, height:props.appearance.actionHeight,
    skins:props.appearance.actionSkins[kind], disabled, kind,
    family:familyFor(label),size:fontSize(12),renderer:'freetype',resourceRoot:props.resourceRoot,pixelRatio:props.textScale});
}


</script>

<template>
  <BfWindow @error="emit('error',$event)" :title="labels.title" :title-align="appearance.theme === 'v1' ? 'left' : 'center'" :font-family="locale === 'ko' ? 'BFKorean' : 'BF'" :font-weight="600" :title-bounds="appearance.titleBounds" :resource-root="resourceRoot" :close-label="labels.close" :width="772" :height="739 + categoryHeight" text-renderer="freetype" :text-scale="textScale" @close="emit('cancel')">
    <section ref="panel" class="bf-ban-pick" :class="{ 'ban-pick-active': active }" :data-theme="appearance.theme" :style="{'--action-height': appearance.actionHeight+'px', '--category-height': categoryHeight+'px', '--body-font-size':fontSize(13)+'px','--text-size':fontSize(12)+'px'}" :lang="locale" :aria-label="labels.title">
      <div class="ban-pick-heading">
        <span class="ban-pick-hint" :aria-label="labels.hint"><BfNative @error="emit('error',$event)" :revision="[readonly,labels.hint,labels.bannedHint,locale,textScale]" :draw="() => drawInline(readonly ? labels.bannedHint ?? labels.hint : labels.hint,'#556f80')" /></span>
        <span class="ban-pick-total" :aria-label="`${labels.selected} ${modelValue.length}`"><BfNative @error="emit('error',$event)" :revision="[labels.selected,locale,textScale]" :draw="() => drawInline(labels.selected,'#456779')" /><BfNative @error="emit('error',$event)" :revision="[modelValue.length,textScale]" :draw="() => drawInline(String(modelValue.length),'#dc7a08',17,22)" /></span>
      </div>
      <div class="ban-pick-category-line">
      <div class="ban-pick-tabs" role="tablist" :aria-label="labels.title">
        <button v-for="value in categories" :id="`ban-pick-tab-${value}`" :key="value" type="button" role="tab"
          :style="{width: appearance.categorySizes[value].width+'px', height: appearance.categorySizes[value].height+'px'}" :aria-selected="category === value" aria-controls="ban-pick-items" :class="{ active: category === value }"
          @click="category = value">
          <img :src="tabImage(appearance.categorySkins[value], category === value)" alt="" draggable="false"/><BfNative @error="emit('error',$event)" :revision="[labels.categories[value], locale,textScale]" :draw="() => drawCategory(value)" /><span v-if="count(value)" class="category-count"><BfNative @error="emit('error',$event)" :revision="[count(value),textScale]" :draw="() => drawInline(String(count(value)),'#5f4600',11,13)" /></span>
        </button>
      </div>
      <div class="ban-pick-tools"><label v-if="!readonly" class="ban-pick-only" :style="{width:Math.max(112,actionWidth(labels.restrictedOnly)-8)+'px'}"><BfControl @error="emit('error',$event)" type="UiCheckBox" :attrs="checkAttrs" @change="restrictedOnly = Boolean($event.value)"/><BfNative @error="emit('error',$event)" :revision="[labels.restrictedOnly,locale,textScale]" :draw="drawBanPickLabel" /></label><span class="ban-pick-field"><input v-model="query" type="search" :aria-label="labels.search" /><span v-if="!query" class="ban-pick-placeholder"><BfNative @error="emit('error',$event)" :revision="[labels.search,locale,textScale]" :draw="() => drawInline(labels.search,'#9aa6ae')" /></span></span></div>
      </div>
      <div class="ban-pick-filter">
        <BfControl @error="emit('error',$event)" type="UiPanel" :attrs="{Name:'pnlSubTabBack',PosX:'0',PosY:'0',Width:'746',Height:'30',SkinSpriteSet:'Images/Shop2/sub_category/store_tab_base.smf'}"/>
        <div class="ban-pick-subtabs">
          <button type="button" :class="{ active: subCategory === 'all' }" :aria-pressed="subCategory === 'all'" @click="subCategory = 'all'"><img :src="tabImage(appearance.subCategorySkins.all!, subCategory === 'all')" alt=""/><BfNative @error="emit('error',$event)" :revision="[labels.all,subCategory === 'all',locale,textScale]" :draw="() => drawInline(labels.all, subCategory === 'all' ? '#ffffff' : '#a8bde0')" /></button>
            <button v-for="value in subCategories" :key="value" type="button" :class="{ active: subCategory === value }"
              :aria-pressed="subCategory === value" @click="subCategory = value"><img v-if="appearance.subCategorySkins[value]" :src="tabImage(appearance.subCategorySkins[value]!, subCategory === value)" alt=""/><BfNative @error="emit('error',$event)" :revision="[labels.subCategories[value] ?? value,subCategory === value,locale,textScale]" :draw="() => drawInline(labels.subCategories[value] ?? value, subCategory === value ? '#ffffff' : '#a8bde0')" /></button>
        </div>
      </div>
      <div id="ban-pick-items" class="ban-pick-content" role="tabpanel" :aria-labelledby="`ban-pick-tab-${category}`">
        <BfControl @error="emit('error',$event)" type="UiPanel" :attrs="{Name:'inventoryBack',PosX:'0',PosY:'0',Width:'746',Height:'386',SkinSpriteSet:'Images/Ui/DialogInnerSkin03.smf'}"/>
        <div class="ban-pick-grid">
          <button v-for="item in visible" :key="item.id" type="button" class="ban-pick-card"
            :class="{ banned: selected.has(item.id) }" :aria-pressed="selected.has(item.id)" :aria-disabled="readonly"
            :aria-label="`${item.name}: ${selected.has(item.id) ? labels.banned : labels.allowed}`"
            @mouseenter="describedId = item.id" @focus="describedId = item.id" @click="toggle(item.id)"
            @mouseenter.passive="trackSpec($event, item)" @mousemove.passive="trackSpec($event, item)" @mouseleave="specHover = undefined">
            <img class="ban-pick-card-frame" :src="cardFrame()" alt="" draggable="false" />
            <BfNative @error="emit('error',$event)" :revision="[item.name, locale,textScale]" :draw="() => drawItemName(item)" />
            <span class="ban-pick-item-preview"><img v-if="typeof item.preview === 'string'" :src="item.preview" alt="" draggable="false" /><span v-else-if="item.preview" class="ban-pick-sprite" :style="spriteStyle(item.preview)"></span><span v-else class="ban-pick-item-missing"><BfNative v-if="labels.noPreview" class="ban-pick-inline" @error="emit('error',$event)" :revision="[labels.noPreview,locale,textScale]" :draw="() => drawInline(labels.noPreview ?? '','#e8edf0')" /></span></span>
            <img v-if="selected.has(item.id)" class="ban-pick-use-frame" :src="asset('Images/Shop2/inventory_box/inventory_use_frame.png')" alt="" draggable="false" />
            <span v-if="selected.has(item.id)" class="ban-pick-badge"><BfNative @error="emit('error',$event)" :revision="[labels.banned,locale,textScale]" :draw="() => drawInline(labels.banned,'#ffe428',12,18,'#705d00')" /></span>
            <BfNative @error="emit('error',$event)" :revision="[selected.has(item.id), labels.banned, labels.allowed, locale,textScale]" :draw="() => drawItemState(item)" />
          </button>
        </div>
        <p v-if="!visible.length" class="ban-pick-empty"><BfNative @error="emit('error',$event)" :revision="[labels.empty,locale,textScale]" :draw="() => drawInline(labels.empty,'#ffffff',13,20)" /></p>
        <div class="ban-pick-pagination">
          <button type="button" class="page-previous" :disabled="page === 0" :aria-label="labels.previous" @click="page--"><img :src="asset('Images/Ui/ScrollbarHorizon/12.png')" alt=""/><img class="page-hover" :src="asset('Images/Ui/ScrollbarHorizon/14.png')" alt=""/></button>
          <span ref="pageTrack" class="page-track" @pointerdown="selectTrackPage"><span class="page-thumb" role="slider" tabindex="0" :aria-label="labels.page" :aria-valuemin="1" :aria-valuemax="pageCount" :aria-valuenow="page + 1" @keydown="pageKey" @pointerdown.stop="startPageDrag" @pointermove="movePageDrag" @pointerup="pageDrag = undefined" @pointercancel="pageDrag = undefined" @lostpointercapture="pageDrag = undefined" :style="{width: `${100 / pageCount}%`, left: pageCount > 1 ? `calc((100% - max(58px, ${100 / pageCount}%)) * ${page / (pageCount - 1)})` : '0'}"><img :src="asset('Images/Ui/ScrollbarHorizon/3.png')" alt="" draggable="false"/><img :src="asset('Images/Ui/ScrollbarHorizon/4.png')" alt="" draggable="false"/><img :src="asset('Images/Ui/ScrollbarHorizon/5.png')" alt="" draggable="false"/><span><BfNative @error="emit('error',$event)" :revision="[page,pageCount,textScale]" :draw="() => drawInline(`${page + 1} / ${pageCount}`,'#404c53')" /></span></span></span>
          <button type="button" class="page-next" :disabled="page + 1 >= pageCount" :aria-label="labels.next" @click="page++"><img :src="asset('Images/Ui/ScrollbarHorizon/15.png')" alt=""/><img class="page-hover" :src="asset('Images/Ui/ScrollbarHorizon/17.png')" alt=""/></button>
        </div>
      </div>
      <div class="ban-pick-bottom">
        <section class="ban-pick-info" :style="{height: appearance.infoSize.height+'px'}" :aria-label="inspected?.name"><BfNative @error="emit('error',$event)" :revision="[inspected, labels.noDescription, locale,textScale]" :draw="drawInfo" /></section>

      </div>
      <div class="ban-pick-actions">
        <template v-if="!readonly">
        <div class="native-action" :style="{width:actionWidth(labels.reset)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,labels.reset]" :draw="ui => drawAction(ui, labels.reset, actionWidth(labels.reset), false, 'Ok')" @action="selectAll"/></div>
        <div class="native-action" :style="{width:actionWidth(labels.presets)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,labels.presets]" :draw="ui => drawAction(ui, labels.presets, actionWidth(labels.presets))" @action="presetsOpen = true"/></div>
        </template>
        <div>
          <div v-if="!readonly" class="native-action" :style="{width:actionWidth(labels.apply)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,labels.apply]" :draw="ui => drawAction(ui, labels.apply, actionWidth(labels.apply))" @action="emit('apply', [...modelValue])"/></div>
          <div class="native-action" :style="{width:actionWidth(readonly ? labels.close : labels.cancel)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,readonly,labels.cancel,labels.close]" :draw="ui => drawAction(ui, readonly ? labels.close : labels.cancel, actionWidth(readonly ? labels.close : labels.cancel), false, 'Cancel')" @action="emit('cancel')"/></div>
        </div>
      </div>
      <div v-if="specItem" class="ban-pick-spec" :style="{...specStyle, width: specSize.width + 'px', height: specSize.height + 'px'}"><BfNative @error="emit('error',$event)" :revision="[specItem.id, specForm, labels.stats, locale, textScale]" :draw="drawSpec" /></div>
    </section>
  </BfWindow>
  <!-- showModal() lifts the dialog into the top layer; staying in place keeps the overlay panel's shadow-root styles. -->
    <dialog v-if="!readonly" ref="presetDialog" class="ban-pick-preset-dialog" :data-theme="appearance.theme" :aria-label="labels.presets" @cancel.prevent="presetsOpen = false" @close="presetsOpen = false" :style="{'--action-height':appearance.actionHeight+'px'}">
      <BfWindow @error="emit('error',$event)" :title="labels.presets" :title-align="appearance.theme === 'v1' ? 'left' : 'center'" :font-family="locale === 'ko' ? 'BFKorean' : 'BF'" :font-weight="600" :title-bounds="appearance.titleBounds" :resource-root="resourceRoot" text-renderer="freetype" :text-scale="textScale" :width="320" :height="205" :close-label="labels.close" @close="presetsOpen = false">
        <section class="ban-pick-presets" :lang="locale">


          <select v-model="presetId" :aria-label="labels.choosePreset"><option value="">{{ labels.choosePreset }}</option><option v-for="preset in presets" :key="preset.id" :value="preset.id">{{ preset.name }}</option></select>
          <span class="ban-pick-field"><input v-model="presetName" :aria-label="labels.presetName" /><span v-if="!presetName" class="ban-pick-placeholder"><BfNative @error="emit('error',$event)" :revision="[labels.presetName,locale,textScale]" :draw="() => drawInline(labels.presetName,'#9aa6ae')" /></span></span>
          <div class="preset-buttons">
            <div class="native-action" :style="{width:actionWidth(labels.savePreset)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,labels.savePreset, !presetName.trim()]" :draw="ui => drawAction(ui, labels.savePreset, actionWidth(labels.savePreset), !presetName.trim())" @action="emit('save-preset', presetName.trim(), [...modelValue])"/></div>
            <div class="native-action" :style="{width:actionWidth(labels.loadPreset)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,labels.loadPreset, !presetId]" :draw="ui => drawAction(ui, labels.loadPreset, actionWidth(labels.loadPreset), !presetId)" @action="loadPreset"/></div>
            <div class="native-action" :style="{width:actionWidth(labels.deletePreset)+'px'}"><BfNative @error="emit('error',$event)" :revision="[textScale,labels.deletePreset, !presetId]" :draw="ui => drawAction(ui, labels.deletePreset, actionWidth(labels.deletePreset), !presetId, 'Cancel')" @action="emit('delete-preset', presetId)"/></div>
          </div>
        </section>
      </BfWindow>
    </dialog>
</template>

<style scoped>
.bf-ban-pick{position:absolute;inset:40px 13px 16px;--item-float:0px;font:700 var(--body-font-size) BF,Arial,sans-serif;font-synthesis:none;font-style:normal;color:#404c53}
.bf-ban-pick:lang(ko){font-family:BFKorean,Arial,sans-serif}
.ban-pick-heading{height:20px;display:flex;align-items:center;justify-content:space-between;padding:0 8px;gap:12px}
.ban-pick-category-line{height:calc(var(--category-height) + 4px);display:flex;align-items:end;justify-content:space-between}.ban-pick-tools{height:27px;display:flex;flex:1;min-width:0;align-items:center;justify-content:flex-end;gap:8px;padding:0 4px}.ban-pick-tools input{width:126px;max-width:126px;min-width:70px;flex:1;height:22px;border:1px solid #a7b0b4;border-radius:4px;background:#fff;color:#45586c;font:700 var(--text-size) BF,Arial;padding:2px 6px}
.ban-pick-hint{font-size:var(--text-size);color:#556f80}.ban-pick-total{white-space:nowrap;color:#456779;font-size:12px}.ban-pick-total strong{color:#dc7a08;font:700 17px Arial;padding-left:5px}
.ban-pick-tabs{display:flex;padding:0 4px;gap:3px;height:var(--category-height);align-items:end;flex:none}
.ban-pick-tabs button{position:relative;width:67px;height:27px;border:0;background:transparent;font:inherit;font-size:13px;color:#fff;text-shadow:none;cursor:pointer;padding:0;isolation:isolate}.ban-pick-tabs button>img{position:absolute;inset:0;width:100%;height:100%;z-index:-1}.ban-pick-tabs button>span:not(.category-count){position:absolute;inset:0;display:flex;align-items:center;justify-content:center}.bf-ban-pick:not(:lang(zh-CN)):not(:lang(zh-TW)):not(:lang(ko)) .ban-pick-tabs button{font-size:11px}
.category-count{position:absolute;right:1px;top:1px;font:9px Arial;background:#f8d64c;color:#5f4600;border-radius:5px;padding:0 2px;text-shadow:none}
.ban-pick-filter{position:relative;height:30px;padding:0 8px;display:flex;align-items:center;justify-content:space-between;gap:8px}
.ban-pick-subtabs{position:relative;display:flex;align-items:center;min-width:0;gap:0}.ban-pick-subtabs button{position:relative;isolation:isolate;min-width:63px;height:22px;border:0;background:none;color:#a8bde0;font:inherit;font-size:var(--text-size);padding:0 8px;white-space:nowrap;cursor:pointer}.ban-pick-subtabs button>img{position:absolute;inset:0;width:100%;height:22px;z-index:-1}.ban-pick-subtabs button.active{color:white}
.ban-pick-only{position:relative;flex:none;width:112px;height:18px;cursor:pointer;color:#456779;font-size:12px;white-space:nowrap}.ban-pick-only>span{position:absolute;left:30px;top:0;height:18px;display:flex;align-items:center;pointer-events:none}
.ban-pick-content{position:relative;height:386px;padding:8px 2px 0;isolation:isolate;box-sizing:border-box}.ban-pick-content :deep(.skin){z-index:-1}
.ban-pick-grid{display:grid;grid-template-columns:repeat(4,180px);grid-auto-rows:178px;gap:0;justify-content:center}
.ban-pick-card{position:relative;width:172px;height:172px;border:0;padding:0;background:transparent;text-align:left;color:#454545;font:inherit;cursor:pointer;isolation:isolate}
.ban-pick-card-frame{position:absolute;inset:0;width:172px;height:172px;z-index:-1}
.ban-pick-card:focus-visible{outline:2px solid #fff;outline-offset:-5px;border-radius:7px}
.ban-pick-item-name{position:absolute;left:26px;top:1px;width:140px;height:20px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font:700 12px BF,Arial;line-height:20px}
.ban-pick-item-preview{position:absolute;left:4px;top:21px;width:164px;height:120px;overflow:hidden;border-radius:5px}
.ban-pick-sprite{position:absolute;inset:0}
.ban-pick-item-missing{display:flex;align-items:center;justify-content:center;width:100%;height:100%;border:1px dashed rgba(255,255,255,.35);border-radius:5px;box-sizing:border-box;color:rgba(255,255,255,.7);font-size:12px}
.ban-pick-item-preview img{width:164px;height:120px;object-fit:contain}
/* kr-812 sub_9B23D0 floats the item view by sin(time * 2*pi / 2000) * 2.5. A compositor animation, only while the
   editor is open, so a closed or idle editor costs the game no frames. */
@keyframes ban-pick-float{0%,100%{transform:translateY(0)}25%{transform:translateY(2.5px)}75%{transform:translateY(-2.5px)}}
.ban-pick-active .ban-pick-sprite,.ban-pick-active .ban-pick-item-preview img{animation:ban-pick-float 2s ease-in-out infinite}
@media (prefers-reduced-motion: reduce){.ban-pick-active .ban-pick-sprite,.ban-pick-active .ban-pick-item-preview img{animation:none}}
.ban-pick-use-frame{position:absolute;left:3px;top:20px;width:166px;height:122px;pointer-events:none}.ban-pick-badge{position:absolute;left:10px;top:27px;font-size:var(--text-size);color:#ffe428;text-shadow:none;-webkit-text-stroke:.5px #705d00;paint-order:stroke fill}
.ban-pick-item-state{position:absolute;left:4px;top:140px;width:164px;height:24px;display:flex;align-items:center;justify-content:center;font-size:12px;white-space:nowrap;color:#4b4b4b}

.ban-pick-bottom{display:flex;gap:8px;height:200px;padding:10px 2px 0}.ban-pick-info{position:relative;width:100%;height:186px;flex:none}.ban-pick-info :deep(.bf-label){overflow:hidden}
/* DlgInventory6.ui supplies the horizontal scrollbar atlas and native panel skins. */
.ban-pick-preset-dialog{border:0;padding:0;background:transparent;width:320px;height:205px;max-width:calc(100vw - 24px);max-height:calc(100vh - 24px);overflow:visible;color:#404c53}
.ban-pick-preset-dialog::backdrop{background:rgb(13 30 46 / 55%)}
.ban-pick-presets{position:absolute;inset:43px 14px 14px;display:flex;flex-direction:column;gap:14px;isolation:isolate;font:700 13px BF,Arial; font-synthesis:none}
.ban-pick-presets:lang(ko){font-family:BFKorean,Arial}
.ban-pick-presets input,.ban-pick-presets select{position:relative;width:100%;height:25px;border:1px solid #a7b0b4;border-radius:4px;font:inherit;background:#fff;color:#45586c;padding:2px 5px}
.preset-buttons{display:flex;justify-content:center;gap:6px;margin-top:8px}
.ban-pick-pagination{position:absolute;bottom:6px;left:8px;right:8px;height:20px;display:flex;align-items:center;background:#58666b;font:700 12px BF,Arial;color:#4b4b4b;border-radius:4px}
.ban-pick-pagination button{position:relative;flex:none;width:23px;height:20px;border:0;padding:0;background:none;cursor:pointer}.ban-pick-pagination button>img{position:absolute;inset:0;width:23px;height:20px}.ban-pick-pagination .page-hover{display:none}.ban-pick-pagination button:hover:not(:disabled) .page-hover{display:block}.ban-pick-pagination button:disabled{opacity:.6;cursor:default}
.page-track{position:relative;flex:1;height:20px}.page-thumb{position:absolute;top:0;display:grid;grid-template-columns:11px 1fr 11px;height:20px;min-width:58px;touch-action:none;user-select:none;cursor:grab}.page-thumb:active{cursor:grabbing}.page-thumb>img{width:100%;height:20px}.page-thumb>span{position:absolute;inset:0;display:flex;align-items:center;justify-content:center}
.ban-pick-empty{position:absolute;inset:70px 20px auto;text-align:center;color:white}
.native-action{position:relative;height:var(--action-height)}.native-action :deep(button){cursor:pointer}.native-action :deep(button:disabled){cursor:default}
.ban-pick-actions{height:40px;display:flex;align-items:end;justify-content:flex-end;gap:6px;padding:0 4px}.ban-pick-actions>div{display:flex;gap:6px}.bf-ban-pick[data-theme=v2] .ban-pick-actions{justify-content:center}.ban-pick-preset-dialog[data-theme=v1] .preset-buttons{justify-content:flex-end}
.ban-pick-tabs button{font-family:BF,BFKorean,Arial;font-weight:700;font-size:15px}.bf-ban-pick:lang(ko) .ban-pick-tabs button,.bf-ban-pick:lang(ko) .ban-pick-item-name{font-family:BFKorean,Arial}
.bf-ban-pick:lang(ko) .ban-pick-tools input{font-family:BFKorean,Arial}
.ban-pick-field{position:relative;display:inline-flex;align-items:center;min-width:0;flex:0 1 auto}.ban-pick-field>input{flex:1}.ban-pick-presets .ban-pick-field{width:100%}.ban-pick-placeholder{position:absolute;left:5px;top:50%;transform:translateY(-50%);pointer-events:none}.ban-pick-spec,.ban-pick-spec *{pointer-events:none!important}.ban-pick-spec{position:absolute;z-index:20}.ban-pick-inline{display:inline-block;vertical-align:middle;line-height:0}.ban-pick-total{display:inline-flex;align-items:center;gap:5px}.ban-pick-subtabs button{display:inline-flex;align-items:center;justify-content:center}.ban-pick-subtabs button+button{border-left:1px solid #6382b9}
button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid #f5c933;outline-offset:1px}
</style>
