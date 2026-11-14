import {labelsFor, languageFor} from './messages.js';
import {describeModes} from './mode-descriptions.js';

const UI = 'bubblefighter.fantasy.game-ui-resources';
const V3_ENTRY_SKINS = ['Up', 'Down', 'Hover', 'Disabled'].map(state => `Images/Ui/dialogueNew/dialogue_type01BTN-${state}.smf`);
const ANCHOR = {x:0,y:0,width:800,height:600,referenceWidth:800,referenceHeight:600,placement:'cover'};

async function json(url) {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url}: HTTP ${response.status}`);
  return response.json();
}

/** Displays backend-owned room state and sends the user's confirmation or cancellation. */
export default async function setup({vue, components, theme, locale, buildContext, packages, present, on, emit}) {
  const {h, reactive, ref, watch, onMounted, onBeforeUnmount} = vue;
  const {BfUiProvider, BfModeExtension, BfMessageBox, BfCaptionButton, BfAnchoredPanel, themeForClientDate, modeWindowGeometry} = components;
  if (!BfAnchoredPanel) throw new Error('This launcher does not include the anchored panel component.');
  if (!BfModeExtension) throw new Error('This launcher does not include the mode-selection component.');
  const language = languageFor(locale), text = labelsFor(language);
  const style = themeForClientDate(buildContext?.buildDate?.slice(0, 10), theme ?? 'v2');
  const geometry = modeWindowGeometry(style);
  const uiRoot = packages.url('ui.json', UI).replace(/ui\.json$/, '');
  const modeRoot = packages.url('mode-skins.json').replace(/mode-skins\.json$/, '');
  const [appearance, looks] = await Promise.all([
    json(packages.url('mode-skins.json')), json(packages.url('ui.json', UI)),
  ]);
  if (style !== 'v3' && !looks[style]?.actionSkins) throw new Error(`The game UI resources have no ${style} button skins.`);
  if (language === 'ko') {
    const stored = await json(packages.url('fonts/fonts.json', UI));
    const file = stored['NexonBTBold-web.otf'] ?? 'NexonBTBold-web.otf';
    const face = await new FontFace('BFKorean', `url("${packages.url('fonts/' + file, UI)}")`, {weight:'400 700'}).load();
    document.fonts.add(face);
  }

  return {
    name:'ModeExtension',
    setup() {
      const view = reactive({revision:-1,sessionId:null,open:false,busy:false,error:null,renderError:'',modes:[],generation:0,
        entry:null,selectedName:null});
      const selectedId = ref(), scale = ref(1);
      let disposed = false, unsubscribe, modesSnapshot = '';
      function report(error) {
        if (disposed) return;
        console.error('Mode Extension:', error);
        view.renderError = text.failed;
      }
      function receive(state) {
        if (disposed) return;
        try {
          if (!state || !Number.isSafeInteger(state.revision) || state.revision < 0)
            throw new Error('Mode state requires a nonnegative integer revision.');
          if (state.revision <= view.revision) return;
          if ((state.sessionId !== null && (typeof state.sessionId !== 'string' || !state.sessionId))
              || typeof state.open !== 'boolean' || typeof state.busy !== 'boolean' || (state.open && state.sessionId === null)
              || (state.selectedId !== null && !Number.isSafeInteger(state.selectedId)) || !Array.isArray(state.modes)
              || (state.error !== null && (!state.error || typeof state.error.status !== 'string' || typeof state.error.reason !== 'string'
                || (state.error.recovery != null && typeof state.error.recovery !== 'string'))))
            throw new Error('Mode state has an invalid display payload.');
          const nextSnapshot = JSON.stringify(state.modes);
          const modes = nextSnapshot === modesSnapshot ? view.modes : describeModes(state.modes);
          if (state.open && (!view.open || state.sessionId !== view.sessionId)) {
            selectedId.value = state.selectedId ?? undefined;
            view.generation++;
          }
          const entry = state.entry ?? null;
          if (entry !== null && (![entry.x,entry.y,entry.width,entry.height,entry.referenceWidth,entry.referenceHeight].every(Number.isSafeInteger)
              || entry.width <= 0 || entry.height <= 0 || (entry.referenceWidth === 0) !== (entry.referenceHeight === 0)))
            throw new Error('Mode state has an invalid entry rectangle.');
          if (state.selectedName != null && typeof state.selectedName !== 'string') throw new Error('Mode state has an invalid selected name.');
          Object.assign(view, {revision:state.revision,sessionId:state.sessionId,open:state.open,busy:state.busy,
            modes,error:state.error,renderError:'',entry,selectedName:state.selectedName ?? null});
          modesSnapshot = nextSnapshot;
        } catch (error) { report(error); }
      }
      function confirm(mode, sessionId) {
        if (disposed || !view.open || view.busy || sessionId !== view.sessionId || !sessionId
            || !view.modes.some(candidate => candidate.id === mode?.id)) return;
        // The native description label shows the extension mode's own introduction where one exists.
        const description = mode.descriptions?.[language];
        emit('mode-extension.confirm', {sessionId,modeId:mode.id,...(typeof description === 'string' ? {description} : {})});
      }
      function cancel(sessionId) {
        if (disposed || !view.open || view.busy || sessionId !== view.sessionId || !sessionId) return;
        emit('mode-extension.cancel', {sessionId});
      }
      function openPicker(sessionId) {
        if (disposed || view.open || !view.entry || sessionId !== view.sessionId || !sessionId) return;
        emit('mode-extension.open', {sessionId});
      }
      function dismiss(sessionId) {
        if (disposed || sessionId !== view.sessionId || (!view.error && !view.renderError)) return;
        emit('mode-extension.dismiss', {sessionId});
      }
      function errorText() {
        return view.renderError || (view.error
          ? ['unsupported','operationUnavailable'].includes(view.error.status) ? text.unavailable
            : view.error.recovery === 'restart' ? text.restartRequired : text.failed
          : '');
      }
      // The overlay's physical pixels are the client's game pixels, so 1 / devicePixelRatio draws the window at the native
      // dialogs' scale; display scaling would otherwise enlarge it. It still shrinks to fit a small client.
      const resize = () => {
        const native = 1 / (globalThis.devicePixelRatio || 1);
        scale.value = Math.max(0.1, Math.min(native, (innerWidth - 16) / geometry.width, (innerHeight - 16) / geometry.height));
      };
      // The entry button is pinned over the client's Create Room dialog, at the rectangle the controller reads from it.
      function entryAnchor(entry) {
        const anchor = {x:entry.x,y:entry.y,width:entry.width,height:entry.height,placement:'cover'};
        return entry.referenceWidth > 0 ? {...anchor,referenceWidth:entry.referenceWidth,referenceHeight:entry.referenceHeight} : anchor;
      }
      watch(() => [view.open, view.error, view.renderError, JSON.stringify(view.entry)], () => present(view.open || view.error || view.renderError
        ? {visible:true,interactive:true,modal:true,backdrop:true,anchor:ANCHOR}
        : view.entry ? {visible:true,interactive:true,modal:false,backdrop:false,anchor:entryAnchor(view.entry)}
        : {visible:false}), {immediate:true});
      onMounted(() => {
        addEventListener('resize', resize); resize();
        unsubscribe = on('mode-extension.state', receive);
        emit('mode-extension.ready');
      });
      onBeforeUnmount(() => {
        disposed = true; removeEventListener('resize', resize);
        unsubscribe?.(); present({visible:false});
      });
      const centered = 'position:absolute;inset:0;display:flex;align-items:center;justify-content:center';
      // Sized like the native OK and Cancel buttons (BfCaptionButton fit), laid out in game pixels and zoomed to the
      // anchor's box, so display scaling cannot make it larger than the native buttons beside it.
      const family = language === 'ko' ? 'BFKorean' : 'BF';
      const entryLabel = () => view.selectedName || text.title;
      const EntryButton = {
        props:{sessionId:String},
        setup(props) {
          // v3 clients (kr-1256) draw DlgCreateRoom's own Create button, a plain 83x34 text button
          // (Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui btnCreate), captioned like the picker's v3 actions;
          // older themes use the O/X button.
          const native = style === 'v3'
            ? {skins:V3_ENTRY_SKINS, height:34, fitMinimum:83, fitMargin:24, fontSize:15, fontWeight:400}
            : {skins:looks[style]?.actionSkins?.Ok, fontSize:13};
          return () => h(BfAnchoredPanel, {width:view.entry?.width ?? 0, height:view.entry?.height ?? 0}, {
            default:({zoom}) => h(BfCaptionButton, {label:entryLabel(), ...native, fit:true, fontFamily:family,
              renderer:'freetype', resourceRoot:uiRoot, textScale:zoom,
              onClick:() => openPicker(props.sessionId), onError:report}),
          });
        },
      };
      return () => {
        const sessionId = view.sessionId, error = errorText();
        return h('div', {style:centered}, [
          h(BfUiProvider, {theme:style,resourceRoot:uiRoot,sound:true,onError:report}, {
            default:() => [
              view.open ? h('div', {style:{zoom:scale.value,visibility:error?'hidden':'visible'}}, [
                h(BfModeExtension, {key:view.generation,modes:view.modes,selectedId:selectedId.value,labels:text,
                  appearance,resourceRoot:uiRoot,modeResourceRoot:modeRoot,theme:style,locale:language,
                  actionSkins:looks[style]?.actionSkins,textScale:scale.value,busy:view.busy,
                  onConfirm:mode => confirm(mode,sessionId),onCancel:() => cancel(sessionId),
                  onError:error => { if (view.open && sessionId === view.sessionId) report(error); }}),
              ]) : null,
              !view.open && !error && view.entry && sessionId ? h(EntryButton, {key:sessionId,sessionId}) : null,
              error ? h('div', {style:centered}, [h('div', {style:{zoom:scale.value}}, [h(BfMessageBox, {title:text.title,text:error,icon:'error',
                buttons:[{name:'Ok',label:text.confirm}],textScale:scale.value,onAction:() => dismiss(sessionId),
                onError:error => console.error('Mode Extension error dialog:', error)})])]) : null,
            ],
          }),
        ]);
      };
    },
  };
}
