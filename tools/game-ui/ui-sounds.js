/** Original SoundList event identifiers, independent of the selected UI theme. */
export const UI_SOUND_EVENTS = Object.freeze(['mouseIn','press','toggleButtonOn','toggleButtonOff','popup','cancel','fail','notice','event-slidenotice']);
/** cn-717 sub_984DF0 reads effectVolume with a default of 75. */
export const DEFAULT_UI_VOLUME = 0.75;
/** cn-279 sub_511EA0 and cn-717 sub_B86460 divide the 0..100 setting by 100. */
export function effectVolumeGain(percent){if(!Number.isInteger(percent)||percent<0||percent>100)throw Error('Effect volume must be an integer from 0 to 100.');return percent/100;}
/** @typedef {'mouseIn'|'press'|'toggleButtonOn'|'toggleButtonOff'|'popup'|'cancel'|'fail'|'notice'|'event-slidenotice'} UiSoundEvent */

/**
 * Own one decoded bank and audio context per resource provider, across theme changes.
 * @param {{resourceRoot?:string,enabled?:boolean,volume?:number,maxVoices?:number,onError?:(error:Error)=>void}} options
 */
export function createUiSoundPlayer({resourceRoot='./local/',enabled=true,volume=DEFAULT_UI_VOLUME,maxVoices=16,onError=error=>console.error(error)}={}) {
  if(!Number.isInteger(maxVoices)||maxVoices<1||maxVoices>64)throw Error('UI maxVoices must be an integer from 1 to 64.');
  const base=resourceRoot.replace(/\/?$/,'/')+'sounds/';
  const abort=new AbortController(),buffers=new Map(),voices=new Set(),handled=new WeakSet();
  let context,master,loading,manifest,disposed=false,generation=0,pendingOpening;
  function setVolume(value){if(!Number.isFinite(value)||value<0||value>1)throw Error('UI volume must be between 0 and 1.');volume=value;if(master)master.gain.value=value;}
  setVolume(volume);
  function report(reason){if(!disposed)onError(reason instanceof Error?reason:new Error(String(reason)));}
  function stop(){for(const voice of voices){voice.source.stop();voice.source.disconnect();voice.gain.disconnect();}voices.clear();}
  function setEnabled(value){enabled=Boolean(value);generation++;if(!enabled){pendingOpening=undefined;stop();}else void preload().catch(report);}
  async function fetchResource(url){const response=await fetch(url,{signal:abort.signal});if(!response.ok)throw Error(`UI sound resource ${url}: ${response.status}`);return response;}
  async function preload(){
    if(disposed)throw Error('UI sound player is disposed.');
    if(!loading)loading=(async()=>{
      const loaded=await (await fetchResource(base+'manifest.json')).json();
      if(loaded.version!==1||!loaded.sounds)throw Error('Unsupported UI sound manifest.');
      for(const event of UI_SOUND_EVENTS){const spec=loaded.sounds[event];if(!spec||!Number.isFinite(spec.gain)||spec.gain<0||spec.gain>1||!/^[-\w]+\.wav$/.test(spec.file))throw Error(`Invalid UI sound event: ${event}`);}
      if(disposed)return;
      context=new AudioContext();master=context.createGain();master.gain.value=volume;master.connect(context.destination);
      const audio=context;
      await Promise.all(UI_SOUND_EVENTS.map(async event=>{
        const data=await (await fetchResource(base+loaded.sounds[event].file)).arrayBuffer();
        const buffer=await audio.decodeAudioData(data);if(!disposed)buffers.set(event,buffer);
      }));
      if(!disposed)manifest=loaded;
    })();
    return loading;
  }
  function start(event){
    if(disposed||!enabled||context?.state!=='running'||!manifest||!buffers.has(event))return false;
    // Bound overlap without altering sample amplitude, pitch, or duration.
    if(voices.size>=maxVoices){const oldest=voices.values().next().value;oldest.source.stop();oldest.source.disconnect();oldest.gain.disconnect();voices.delete(oldest);}
    const source=context.createBufferSource(),gain=context.createGain(),voice={source,gain};
    source.buffer=buffers.get(event);gain.gain.value=manifest.sounds[event].gain;
    source.connect(gain);gain.connect(master);voices.add(voice);
    source.onended=()=>{voices.delete(voice);source.disconnect();gain.disconnect();};source.start();return true;
  }
  async function unlock(){
    if(disposed||!enabled)return false;
    // resume() is invoked synchronously when called from the trusted gesture.
    const resumed=context?.resume();await preload();
    if(disposed||!enabled)return false;
    if(resumed)await resumed;else await context.resume();
    if(disposed||!enabled)return false;
    const opening=pendingOpening;pendingOpening=undefined;
    if(opening?.root.isConnected)start(opening.event);
    return context.state==='running';
  }
  /** Explicit host events use the same original bank and per-event gain. */
  /** @param {UiSoundEvent} event */
  async function play(event){
    if(!UI_SOUND_EVENTS.includes(event))throw Error(`Unknown UI sound event: ${event}`);
    if(disposed||!enabled)return false;
    const current=generation;await preload();
    if(disposed||!enabled||current!==generation)return false;
    return start(event);
  }
  /** Bind actual interactions and opening once, after the rendered surface is attached. */
  /** @param {HTMLElement} root */
  function bind(root){
    let active=true;
    const opening=root.dataset.bfOpenSound,hovered=new WeakSet();
    if(opening&&!UI_SOUND_EVENTS.includes(opening))throw Error(`Unknown opening sound: ${opening}`);
    const current=()=>active&&!disposed&&root.isConnected;
    const control=event=>event.target instanceof Element?event.target.closest('button.sprite-button,button.bf-close'):null;
    const eligible=button=>button&&root.contains(button)&&!button.disabled&&button.getAttribute('aria-disabled')!=='true';
    const claim=event=>{if(handled.has(event))return false;handled.add(event);return true;};
    const request=(event,gesture=false)=>{
      if(!enabled||!current())return;
      if(gesture){const epoch=generation;void unlock().then(ok=>{if(ok&&current()&&epoch===generation)start(event);}).catch(report);}
      else start(event);
    };
    const enter=event=>{if(event.pointerType==='touch')return;const button=control(event);if(eligible(button)&&!hovered.has(button)&&!(event.relatedTarget instanceof Node&&button.contains(event.relatedTarget))&&claim(event)){hovered.add(button);request('mouseIn');}};
    const leave=event=>{const button=control(event);if(button&&!(event.relatedTarget instanceof Node&&button.contains(event.relatedTarget)))hovered.delete(button);};
    // cn-279 sub_4B4840 calls the default press event on the button down path.
    const down=event=>{if(event.button!==0)return;const button=control(event);if(eligible(button)&&claim(event))request(button.dataset.bfPressSound||'press',true);};
    const keydown=event=>{if(event.repeat||!['Enter',' '].includes(event.key))return;const button=control(event);if(eligible(button)&&claim(event))request(button.dataset.bfPressSound||'press',true);};
    const change=event=>{const input=event.target;if(input instanceof HTMLInputElement&&input.type==='checkbox'&&input.closest('.bf-check')&&!input.disabled&&claim(event))request(input.checked?'toggleButtonOn':'toggleButtonOff',true);};
    // Capture before the sprite's down handler replaces the event target bitmap.
    root.addEventListener('pointerover',enter);root.addEventListener('pointerout',leave);root.addEventListener('pointerdown',down,true);
    root.addEventListener('keydown',keydown);root.addEventListener('change',change);
    if(opening){
      if(enabled){if(context?.state==='running')start(opening);else pendingOpening={root,event:opening};}
    }
    return ()=>{active=false;if(pendingOpening?.root===root)pendingOpening=undefined;root.removeEventListener('pointerover',enter);root.removeEventListener('pointerout',leave);root.removeEventListener('pointerdown',down,true);root.removeEventListener('keydown',keydown);root.removeEventListener('change',change);};
  }
  const gesture=event=>{if(event.isTrusted&&enabled&&context?.state!=='running')void unlock().catch(report);};
  document.addEventListener('pointerdown',gesture,true);document.addEventListener('keydown',gesture,true);
  function dispose(){if(disposed)return;disposed=true;generation++;pendingOpening=undefined;abort.abort();stop();buffers.clear();document.removeEventListener('pointerdown',gesture,true);document.removeEventListener('keydown',gesture,true);if(context)void context.close().catch(report);}
  return {preload,unlock,play,bind,setEnabled,setVolume,dispose};
}
