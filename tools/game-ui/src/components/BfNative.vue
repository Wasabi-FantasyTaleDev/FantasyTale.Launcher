<script setup lang="ts">
import {onBeforeUnmount,ref,watch,nextTick} from 'vue';
import {useGameUi,useDrawTracker,type BfRenderer} from '../context';
const props=defineProps<{draw:(ui:BfRenderer)=>Promise<HTMLElement>;revision?:unknown}>();
const emit=defineEmits<{action:[Record<string,unknown>];change:[Record<string,unknown>];error:[Error]}>();
const ui=useGameUi(),host=ref<HTMLElement>(),surface=ref<HTMLElement>();let revision=0,unbindSound:(()=>void)|undefined;
// The previous surface stays until the next one has finished drawing, so a redraw (another item hovered) swaps in one
// step instead of showing an empty panel while text is rasterized.
function clear(){unbindSound?.();unbindSound=undefined;surface.value=undefined;host.value?.replaceChildren()}
// Redraw when the revision's content changes, not whenever a parent render passes an equal new array: a parent that
// re-renders on every pointer move would otherwise rasterize every caption again each time.
const revisionKey=()=>{try{return JSON.stringify(props.revision)}catch{return String(Math.random())}};
const tracker=useDrawTracker();let counted=false;
const settle=()=>{if(counted&&tracker){tracker.pending.value--;counted=false}};
watch([ui,revisionKey,host],async()=>{const current=++revision;if(!ui.value||!host.value){clear();settle();return}const renderer=ui.value;
if(tracker&&!counted){tracker.pending.value++;counted=true}
try{const element=await props.draw(renderer);
// A surface counts as drawn only once its images are decoded, so nothing appears with its pictures still missing.
await Promise.all([...element.querySelectorAll('img')].map(image=>image.decode().catch(()=>undefined)));
if(current!==revision||!host.value)return;unbindSound?.();host.value.replaceChildren(element);surface.value=element;unbindSound=renderer.sounds?.bind(element);await nextTick();if(current===revision)settle();}catch(reason){if(current===revision){clear();settle();emit('error',reason instanceof Error?reason:new Error(String(reason)))}}},{flush:'post'});
onBeforeUnmount(()=>{revision++;settle();unbindSound?.();surface.value=undefined});
defineExpose({surface});
function forward(event:Event,kind:'action'|'change'){const detail=(event as CustomEvent<Record<string,unknown>>).detail;if(kind==='action')emit('action',detail);else emit('change',detail)}
</script>
<template><div ref="host" class="bf-native-host" @bf-action="forward($event,'action')" @bf-change="forward($event,'change')"/><Teleport v-if="surface" :to="surface"><slot/></Teleport></template>
