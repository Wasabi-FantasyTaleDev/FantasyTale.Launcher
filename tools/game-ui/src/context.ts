import {computed,inject,onBeforeUnmount,provide,shallowRef,watch,type InjectionKey,type Ref} from 'vue';
import {createGameUi} from '../game-ui.js';
import {createUiSoundPlayer,DEFAULT_UI_VOLUME} from '../ui-sounds.js';
export type {UiSoundEvent as BfUiSoundEvent} from '../ui-sounds.js';
export type BfTheme='v1'|'v2'|'v3';
export type BfRenderer=Awaited<ReturnType<typeof createGameUi>>;
const key:InjectionKey<Ref<BfRenderer|undefined>>=Symbol('bf-ui');
/** Load original theme resources once per provider and reject stale theme loads. */
export function provideGameUi(theme:()=>BfTheme,resourceRoot:()=>string,sound:()=>boolean=()=>true,volume:()=>number=()=>DEFAULT_UI_VOLUME){
const renderer=shallowRef<BfRenderer>();const error=shallowRef<Error>();let revision=0;
let sounds:ReturnType<typeof createUiSoundPlayer>|undefined,soundRoot:string|undefined;
watch(()=>[theme(),resourceRoot()] as const,async([selected,root])=>{
  const current=++revision;error.value=undefined;
  try{
    if(soundRoot!==root){sounds?.dispose();soundRoot=root;sounds=createUiSoundPlayer({resourceRoot:root,enabled:sound(),volume:volume(),onError:reason=>{error.value=reason}});}
    const player=sounds!;
    await Promise.all([loadFonts(root),sound()?player.preload():Promise.resolve()]);
    if(current!==revision)return;
    const next=await createGameUi({theme:selected,resourceRoot:root,sounds:player});if(current===revision)renderer.value=next;
  }catch(reason){if(current===revision)error.value=reason instanceof Error?reason:new Error(String(reason));}
},{immediate:true});
watch(sound,value=>sounds?.setEnabled(value));
watch(volume,value=>{try{sounds?.setVolume(value)}catch(reason){error.value=reason instanceof Error?reason:new Error(String(reason))}});
onBeforeUnmount(()=>{revision++;sounds?.dispose()});
provide(key,renderer);return {renderer,error};
}
/** Resolve the enclosing resource provider for an independently reusable control. */
export function useGameUi(){const value=inject(key);if(!value)throw Error('Game UI controls require BfUiProvider');return computed(()=>value.value);}

/** Keep font locations external so a host can provide its own unpacked Chinese resources. */
async function loadFonts(root:string){
const prefix=root.replace(/\/?$/,'/');
await Promise.all([['BF','GB13000.ttf'],['BFMalgun','DF_GB_Y9.TTF']].map(async([family,file])=>{
if(document.fonts.check(`13px ${family}`)&&Array.from(document.fonts).some(font=>font.family===family))return;
const font=await new FontFace(family!,`url("${prefix}fonts/${file}")`,{weight:'400'}).load();document.fonts.add(font);
}));
}
