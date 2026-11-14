// Ban Pick in the game overlay. The server decides everything: it says whether the player is in a room and hosts it,
// and every change is the same "bp" command a host could type, sent through Nirinsou's command channel. This module
// only shows the room button and the editor; the launcher needs no change for it.
import { labelsFor } from './messages.js';
import { parsePresets, presetStorageKey, writePresets } from './presets.js';

const PREVIEWS = 'bubblefighter.fantasy.item-previews';
const UI = 'bubblefighter.fantasy.game-ui-resources';
const LIST_CHANGED = 'game.ban.list.changed';
const LIST_GET = 'game.ban.list.get';
const START_CONFIRM = 'game.ban.start.confirm';
const COMMAND = 'game.command.execute';

// The editor's tabs by the client's own sub category keys (ShopItem2 SubCategory names, original spelling kept).
const TABS = {
    weapons: ['Weapon_Rifle', 'Weapon_ShotGun', 'Weapon_Sniper', 'SubWeapon', 'MeleeWeapon', 'SpecialWeapon'],
    clothes: ['SetClothes', 'Hats', 'Shirts', 'Pants', 'Costumes', 'Gloves', 'Shoes'],
    accessories: ['HeadAccesory', 'FaceAccesory', 'FrontAccesory', 'BackAccesory'],
};
const TAB_OF = new Map(Object.entries(TABS).flatMap(([tab, keys]) => keys.map(key => [key, tab])));

// Game-pixel rectangles against an 800x600 reference, scaled to the client: the button sits at the room screen's top
// right; the open editor covers the client.
const REFERENCE = { referenceWidth: 800, referenceHeight: 600, placement: 'cover' };
const BUTTON_ANCHOR = { x: 690, y: 4, width: 104, height: 30, ...REFERENCE };
const EDITOR_ANCHOR = { x: 0, y: 0, width: 800, height: 600, ...REFERENCE };

async function json(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
    return response.json();
}

function tabOf(item) {
    if (TAB_OF.has(item.sub)) return { category: TAB_OF.get(item.sub), subCategory: item.sub };
    if (/character/i.test(item.main ?? '') || /character/i.test(item.sub ?? '')) return { category: 'characters', subCategory: 'character' };
    return null;
}

let fontsAdded = false;
async function addFonts(fontUrl) {
    // Font faces are document-wide: a shadow root cannot declare them. Weight 700 is the original W9 face, never synthesized.
    if (fontsAdded) return;
    fontsAdded = true;
    // The resources package stores compressed copies and names them in fonts.json.
    const stored = await fetch(fontUrl('fonts.json')).then(response => response.ok ? response.json() : {}).catch(() => ({}));
    for (const [family, file, weight] of [['BF', 'GB13000.ttf', '400'], ['BF', 'DF_GB_Y9.TTF', '700'],
        ['BFMalgun', 'DF_GB_Y9.TTF', '400'], ['BFKorean', 'NexonBTBold-web.otf', '700']]) {
        const face = new FontFace(family, `url("${fontUrl(stored[file] ?? file)}")`, { weight, display: 'swap' });
        document.fonts.add(face);
        face.load().catch(error => console.error(`Ban Pick font ${file}:`, error));
    }
}

export default async function setup({ vue, components, theme, locale, buildContext, companion, nativeUi, packages, present }) {
    const { h, ref, reactive, computed, watch, onMounted, onBeforeUnmount } = vue;
    const { BfUiProvider, BfBanPick, BfCaptionButton, BfMessageBox, BfReveal } = components;
    // The editor has v1 and v2 appearances only; a v3 client uses v2 rather than mixing generations.
    const style = theme === 'v1' ? 'v1' : 'v2';
    const uiRoot = packages.url('ui.json', UI).replace(/ui\.json$/, '');
    const text = labelsFor(locale);
    await addFonts(file => packages.url('fonts/' + file, UI));
    // The appearance table also holds the caption-free button skins: the theme's dialog button sprite has a baked-in label.
    const looks = await json(packages.url('ui.json', UI));
    if (!looks[style]) throw new Error(`The game UI resources have no ${style} appearance.`);

    const dialogButton = kind => style === 'v1' ? `Images/Ui/DialogButton${kind}-Up.smf` : `Images/Ui/DialogBtn${kind}.smf`;
    const room = reactive({ inRoom: false, isHost: false, playing: false, count: 0, version: 0, button: '', entries: [] });
    // prepared: the editor is built (invisible) behind the room button, so opening only shows it.
    const view = reactive({ open: false, prepared: false, ready: false, busy: false, notice: '', error: '' });
    const draft = ref([]);
    const confirm = ref(null);
    let confirmTimer = 0;
    let confirmWaitTimer = 0, finishConfirmWait, confirmationGeneration = 0;
    let readyTimer = 0, editorGeneration = 0, disposed = false;
    const items = ref([]);
    const appearance = ref(null);
    const presets = ref([]);
    const scale = ref(1);
    let storageReady = true;
    try { presets.value = parsePresets(localStorage.getItem(presetStorageKey)); } catch { storageReady = false; }

    function clearConfirmation() {
        confirmationGeneration++;
        clearTimeout(confirmTimer); confirmTimer = 0; confirm.value = null;
        clearTimeout(confirmWaitTimer); confirmWaitTimer = 0;
        finishConfirmWait?.();
    }

    function releaseEditor() {
        editorGeneration++;
        clearConfirmation();
        clearTimeout(readyTimer); readyTimer = 0;
        view.open = false; view.prepared = false; view.ready = false;
    }

    function editorReady() {
        clearTimeout(readyTimer); readyTimer = 0;
        view.ready = true;
    }

    function adopt(payload) {
        if (disposed) return;
        if (!payload || typeof payload !== 'object') return;
        room.inRoom = payload.in_room === true;
        room.isHost = room.inRoom && payload.is_host === true;
        // A started round closes the editor and the button; they come back when the room waits again.
        room.playing = room.inRoom && payload.playing === true;
        if (room.playing) releaseEditor();
        if (typeof payload.count === 'number') room.count = payload.count;
        if (typeof payload.list_version === 'number') room.version = payload.list_version;
        if (typeof payload.button_label === 'string' && payload.button_label) room.button = payload.button_label;
        if (Array.isArray(payload.room)) { room.entries = payload.room; room.count = payload.room.length; }
        if (!room.inRoom) { room.entries = []; room.count = 0; releaseEditor(); }
    }

    async function refresh() {
        if (disposed) return;
        const generation = editorGeneration;
        const reply = await companion.request(LIST_GET, 1, {}, { timeoutMs: 10000 });
        if (!disposed && generation === editorGeneration && reply.status === 'completed') adopt(reply.response);
    }

    // Game Control Support first: the overlay draws a dialog or notice only when the native operation is unavailable or fails.
    const SETTLED = ['selected', 'dismissed', 'closed', 'failed', 'outcomeUnknown', 'operationUnavailable', 'unsupported', 'notReady', 'busy'];
    async function nativeOperation(kind, request, current = () => !disposed) {
        if (!nativeUi || !current()) return null;
        try {
            if (!await nativeUi.available(kind)) return null;
            if (!current()) return null;
            const id = kind + '-' + crypto.randomUUID().replaceAll('-', '');
            const reply = await nativeUi.invoke({ operationId: id, kind, ...request });
            return reply.status === 'completed' && !['unsupported', 'notReady', 'busy', 'failed'].includes(reply.result?.status) ? id : null;
        } catch (error) { console.error(error); return null; }
    }

    async function notify(text) {
        if (!text) return;
        if (await nativeOperation('notification', { text: String(text).slice(0, 2048) })) return;
        view.error = String(text);
    }

    function confirmationCurrent(generation, question) {
        return !disposed && generation === editorGeneration && question === confirmationGeneration && shown.value && room.isHost;
    }

    async function runCommand(command, current) {
        if (!current()) return;
        // A late or refused yes changes nothing on the server; its explanation is shown.
        const reply = await companion.request(COMMAND, 1, { operation_id: crypto.randomUUID().replaceAll('-', ''), command }, { timeoutMs: 15000 });
        if (!current()) return;
        if (reply.status !== 'completed' || reply.response?.status !== 'success' || reply.response?.message)
            await notify(reply.response?.message || reply.status);
    }

    // The server asks the host whether to start although members wear items they may allow; yes is its own command.
    async function ask(payload) {
        if (disposed || !shown.value || !room.isHost || typeof payload?.command !== 'string' || !payload.command.startsWith('bp ')) return;
        clearConfirmation();
        const generation = editorGeneration, question = confirmationGeneration;
        const current = () => confirmationCurrent(generation, question);
        const lines = Array.isArray(payload.violators) ? payload.violators.slice(0, 16).map(seat =>
            `${String(seat?.nickname ?? '').slice(0, 64)}: ${(Array.isArray(seat?.items) ? seat.items.slice(0, 16) : []).join(', ')}`) : [];
        const asked = { title: String(payload.title ?? ''), text: [String(payload.text ?? ''), ...lines].join('\n').slice(0, 8192),
            yes: String(payload.confirm_label || 'OK'), no: String(payload.cancel_label || 'Cancel'), command: payload.command, generation, question };
        const expires = Number.isInteger(payload.expires_in_ms) && payload.expires_in_ms > 0 && payload.expires_in_ms <= 600000 ? payload.expires_in_ms : 10000;
        const operation = await nativeOperation('messageBox', { title: asked.title, text: asked.text, icon: 'question',
            buttons: [{ id: 'confirm', label: asked.yes }, { id: 'cancel', label: asked.no }] }, current);
        if (!current()) return;
        if (operation) {
            // The native modal stays until the host answers; a yes after the server's window is refused there.
            for (let waited = 0; waited < 600000; waited += 500) {
                await new Promise(resolve => {
                    const finish = () => {
                        if (finishConfirmWait === finish) { finishConfirmWait = undefined; confirmWaitTimer = 0; }
                        resolve();
                    };
                    finishConfirmWait = finish;
                    confirmWaitTimer = setTimeout(finish, 500);
                });
                if (!current()) return;
                const outcome = (await nativeUi.result(operation)).result;
                if (!current()) return;
                if (!outcome || !SETTLED.includes(outcome.status)) continue;
                if (outcome.status === 'selected' && outcome.buttonId === 'confirm') await runCommand(asked.command, current);
                return;
            }
            return;
        }
        clearTimeout(confirmTimer);
        confirm.value = asked;
        confirmTimer = setTimeout(() => { if (confirm.value?.question === question) { confirm.value = null; confirmTimer = 0; } }, expires);
    }

    async function answer(name) {
        const asked = confirm.value;
        confirm.value = null;
        clearTimeout(confirmTimer);
        if (name !== 'confirm' || !asked) return;
        await runCommand(asked.command, () => confirmationCurrent(asked.generation, asked.question));
    }

    const characters = ref({});

    async function loadCatalog() {
        if (items.value.length && appearance.value) return;
        const pack = await json(packages.url('pack.json', PREVIEWS));
        const buildPath = pack.builds?.[buildContext?.buildId];
        if (!buildPath) throw new Error(`The item previews have no data for ${buildContext?.buildId ?? 'this client'}.`);
        const build = await json(packages.url(buildPath, PREVIEWS));
        const [cellWidth, cellHeight] = pack.cell;
        const sheets = pack.atlases.map(atlas => ({ url: packages.url(atlas.file, PREVIEWS), columns: atlas.columns, width: atlas.width, height: atlas.height }));
        const list = [];
        for (const [id, item] of Object.entries(build.items)) {
            const tab = tabOf(item);
            if (!tab) continue;
            const sheet = Number.isInteger(item.atlas) ? sheets[item.atlas] : null;
            list.push({
                id: Number(id), ...tab, name: item.name || id, allow: item.allow ?? '',
                // The client's own pre-rendered icon stands in for an item it has no model for.
                preview: !(sheet && Number.isInteger(item.cell)) && item.icon ? packages.url(item.icon, PREVIEWS) : sheet && Number.isInteger(item.cell) ? {
                    url: sheet.url, sheetWidth: sheet.width, sheetHeight: sheet.height, width: cellWidth, height: cellHeight,
                    x: (item.cell % sheet.columns) * cellWidth, y: Math.floor(item.cell / sheet.columns) * cellHeight,
                } : null,
                slogan: item.slogan ?? '', description: item.description ?? '', detail1: item.detail1 ?? '', detail2: item.detail2 ?? '',
                ...(Array.isArray(item.spec) ? { spec: item.spec } : {}),
            });
        }
        // Clothing sets: one card bans every piece; the shop's own set icon is its picture.
        for (const [id, set] of Object.entries(build.sets ?? {})) {
            list.push({
                id: Number(id), category: 'clothes', subCategory: 'SetClothes', name: set.name || id,
                preview: set.icon ? packages.url(set.icon, PREVIEWS) : null, members: set.items, setStats: set.stats ?? null,
                allow: set.allow ?? '', slogan: set.slogan ?? '', description: set.description ?? '',
                detail1: set.detail1 ?? '', detail2: set.detail2 ?? '',
            });
        }
        characters.value = build.characters ?? {};
        list.sort((a, b) => a.id - b.id);
        appearance.value = looks[style];
        items.value = list;
    }

    const known = computed(() => new Set(items.value.map(item => item.id)));
    const roomItems = () => room.entries.filter(entry => entry.kind === 'item').map(entry => Number(entry.item));

    async function open() {
        if (disposed || !shown.value) return;
        const generation = editorGeneration;
        view.error = ''; view.notice = '';
        try {
            await Promise.all([loadCatalog(), refresh()]);
            if (disposed || generation !== editorGeneration || !shown.value) return;
            draft.value = roomItems().filter(id => known.value.has(id));
            if (!view.prepared) { view.ready = false; view.prepared = true; }
            // Never leave an invisible window up: show it anyway if a part has not reported within 8 seconds.
            if (!view.ready) {
                clearTimeout(readyTimer);
                readyTimer = setTimeout(() => {
                    if (!disposed && generation === editorGeneration && view.prepared) editorReady();
                }, 8000);
            }
            view.open = true;
        } catch (error) {
            if (!disposed && generation === editorGeneration && shown.value) { console.error(error); await notify(String(error?.message ?? error)); }
        }
    }

    // Once the room button is on screen, build the editor invisibly and retain every tab's and sub tab's first-page
    // preview sheets until the player leaves the room; decoded memory depends on the build's atlas coverage.
    async function prepare() {
        if (disposed || view.prepared || view.open || !shown.value) return;
        const generation = editorGeneration;
        try {
            await loadCatalog();
            if (disposed || generation !== editorGeneration || view.prepared || view.open || !shown.value) return;
            draft.value = roomItems().filter(id => known.value.has(id));
            view.ready = false;
            view.prepared = true;
        } catch (error) { console.error(error); }
    }

    // The whole list in one "bp set": the editor's items plus every entry it cannot show (groups, items of no tab),
    // so applying never drops what another way of editing added.
    async function apply(selection) {
        if (view.busy) return;
        const kept = room.entries.filter(entry => entry.kind === 'group').map(entry => String(entry.group))
            .concat(roomItems().filter(id => !known.value.has(id)).map(String));
        const words = [...new Set([...selection.map(String), ...kept])];
        const command = words.length ? 'bp set ' + words.join(' ') : 'bp clear';
        view.busy = true; view.error = '';
        try {
            const reply = await companion.request(COMMAND, 1, { operation_id: crypto.randomUUID().replaceAll('-', ''), command }, { timeoutMs: 15000 });
            const result = reply.response ?? {};
            if (reply.status !== 'completed' || result.status !== 'success' || result.message) {
                await notify(result.message || reply.status);
                return;
            }
            view.open = false;
            await refresh();
        } finally { view.busy = false; }
    }

    function persist(next) {
        if (!storageReady) return;
        try { presets.value = writePresets(localStorage, next); view.notice = ''; } catch { view.notice = text.presetStorageError; }
    }

    const shown = computed(() => room.inRoom && !room.playing && (room.isHost || room.count > 0));
    // Leaving the room (or the button going away) releases the prepared editor and its pictures.
    watch(shown, value => { if (!value && !view.open) releaseEditor(); });
    watch(() => [shown.value, view.open, confirm.value], () => {
        if (view.open || confirm.value) present({ visible: true, interactive: true, modal: true, backdrop: true, anchor: EDITOR_ANCHOR });
        else if (shown.value) present({ visible: true, interactive: true, modal: false, backdrop: false, anchor: BUTTON_ANCHOR });
        else present({ visible: false });
    }, { immediate: true });
    // A non-host's open editor follows list changes; a host editing keeps their draft.
    watch(() => room.version, () => { if (view.open && !room.isHost) draft.value = roomItems().filter(id => known.value.has(id)); });
    watch(() => room.isHost, host => {
        if (!host) clearConfirmation();
        if (!host && view.open) draft.value = roomItems().filter(id => known.value.has(id));
    });

    return {
        name: 'BanPickRoom',
        setup() {
            const unsubscribe = [
                companion.on(LIST_CHANGED, payload => { adopt(payload); if (view.open && room.inRoom) void refresh(); }),
                companion.on(START_CONFIRM, payload => { void ask(payload); }),
                companion.onState(({ state }) => { if (state === 'connected') void refresh(); }),
            ];
            const resize = () => {
                // Layout and assets scale together; the editor picks whole-pixel font sizes for the final scale. The
                // overlay's physical pixels are the client's game pixels, so 1 / devicePixelRatio is the native dialogs'
                // scale; display scaling would otherwise enlarge the window. It still shrinks to fit a small client.
                const height = appearance.value ? 739 + Math.max(...Object.values(appearance.value.categorySizes).map(size => size.height)) : 771;
                const native = 1 / (globalThis.devicePixelRatio || 1);
                scale.value = Math.min(native, (innerWidth - 16) / 772, (innerHeight - 16) / height);
            };
            onMounted(() => { addEventListener('resize', resize); resize(); });
            watch(appearance, resize);
            onBeforeUnmount(() => { disposed = true; releaseEditor(); clearTimeout(confirmTimer); removeEventListener('resize', resize); unsubscribe.forEach(stop => stop()); });
            const RoomButton = {
                setup() {
                    const ready = ref(false), box = ref(), zoom = ref(1);
                    // The panel box spans BUTTON_ANCHOR's 104 reference pixels; the button is laid out in those pixels and
                    // zoomed to the box (not to the shared overlay page), so display scaling cannot enlarge it.
                    const measure = () => { const width = box.value?.clientWidth ?? 0; if (width > 0) zoom.value = width / BUTTON_ANCHOR.width; };
                    let observer;
                    onMounted(() => { observer = new ResizeObserver(measure); observer.observe(box.value); measure(); });
                    onBeforeUnmount(() => observer?.disconnect());
                    // State skins explicitly set visibility, so a real box owns the initial opacity and input barrier.
                    return () => h('div', { ref: box, style: { position: 'absolute', inset: 0, overflow: 'hidden' } }, [
                        h('div', { inert: !ready.value, 'aria-hidden': !ready.value,
                            style: { zoom: zoom.value, width: '104px', height: looks[style].actionHeight + 'px', opacity: ready.value ? 1 : 0 } }, [
                            h(BfReveal, { onReady: () => { ready.value = true; void prepare(); } }, () => h(BfCaptionButton, {
                                label: room.button || text.open, skins: looks[style].actionSkins.Ok,
                                width: 104, height: looks[style].actionHeight, fontFamily: locale === 'ko' ? 'BFKorean' : 'BF', onClick: open,
                                renderer: 'freetype', resourceRoot: uiRoot, textScale: zoom.value,
                                onError: error => { view.error = String(error?.message ?? error); view.ready = true; },
                            })),
                        ]),
                    ]);
                },
            };
            return () => h('div', { class: 'ban-pick-room', style: 'position:absolute;inset:0;display:flex;align-items:center;justify-content:center' }, [
                h(BfUiProvider, { theme: style, resourceRoot: uiRoot, sound: true, onError: error => { view.error = String(error?.message ?? error); view.ready = true; } }, {
                    default: () => confirm.value
                        ? h('div', { style: { zoom: scale.value } }, [h(BfMessageBox, { title: confirm.value.title, text: confirm.value.text,
                            // The theme's own dialog buttons, as the overlay gives every message box.
                            buttons: [{ name: 'confirm', label: confirm.value.yes, sprite: dialogButton('Ok') },
                                { name: 'cancel', label: confirm.value.no, sprite: dialogButton('Cancel') }],
                            textScale: scale.value,
                            onAction: data => answer(data?.name), onError: error => { view.error = String(error?.message ?? error); view.ready = true; } })])
                        : [view.prepared && appearance.value
                        // Built ahead and kept invisible until open and every part has drawn, so it never appears
                        // half-built; while closed it takes no room and no input.
                        ? h('div', { key: 'editor', inert: !view.open || !view.ready, 'aria-hidden': !view.open || !view.ready, style: view.open
                            ? { zoom: scale.value, opacity: view.ready ? 1 : 0 }
                            : { zoom: scale.value, opacity: 0, position: 'absolute', left: '0', top: '0', pointerEvents: 'none' } }, [h(BfBanPick, {
                            onReady: editorReady,
                            modelValue: draft.value, 'onUpdate:modelValue': value => { draft.value = value; },
                            items: items.value, labels: text.editor, appearance: appearance.value, resourceRoot: uiRoot,
                            locale, presets: presets.value, textScale: scale.value, readonly: !room.isHost,
                            characters: characters.value, defaultSubCategories: { clothes: 'SetClothes' }, active: view.open,
                            onApply: apply, onCancel: () => { view.open = false; },
                            onSavePreset: (name, itemIds) => {
                                const existing = presets.value.find(preset => preset.name === name);
                                persist([...presets.value.filter(preset => preset.name !== name), { id: existing?.id ?? crypto.randomUUID(), name, itemIds: [...itemIds] }]);
                            },
                            onDeletePreset: id => persist(presets.value.filter(preset => preset.id !== id)),
                            onError: error => { view.error = String(error?.message ?? error); view.ready = true; },
                        })]) : null,
                        // The room button appears only once drawn, like the editor; then the editor is prepared.
                        !view.open && shown.value ? h(RoomButton, { key: 'room-button' }) : null],
                }),
                view.error ? h('p', { role: 'alert', class: 'ban-pick-room-error', onClick: () => { view.error = ''; },
                    style: 'position:absolute;left:50%;bottom:8px;transform:translateX(-50%);margin:0;padding:4px 10px;border-radius:4px;background:#7a1f1f;color:#fff;font:12px sans-serif;cursor:pointer' }, view.error) : null,
            ]);
        },
    };
}
