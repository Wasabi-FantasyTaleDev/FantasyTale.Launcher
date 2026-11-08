<script setup lang="ts">
import {onBeforeUnmount,ref,watch,nextTick} from 'vue';
import {useGameUi,type BfRenderer} from '../context';
const props=defineProps<{draw:(ui:BfRenderer)=>Promise<HTMLElement>;revision?:unknown}>();
const emit=defineEmits<{action:[Record<string,unknown>];change:[Record<string,unknown>];error:[Error]}>();
const ui=useGameUi(),host=ref<HTMLElement>(),surface=ref<HTMLElement>();let revision=0,unbindSound:(()=>void)|undefined;
watch([ui,()=>props.revision,host],async()=>{const current=++revision;unbindSound?.();unbindSound=undefined;surface.value=undefined;host.value?.replaceChildren();if(!ui.value||!host.value)return;const renderer=ui.value;try{const element=await props.draw(renderer);if(current!==revision)return;host.value.replaceChildren(element);surface.value=element;unbindSound=renderer.sounds?.bind(element);await nextTick();}catch(reason){if(current===revision)emit('error',reason instanceof Error?reason:new Error(String(reason)));}},{flush:'post'});
onBeforeUnmount(()=>{revision++;unbindSound?.();surface.value=undefined});
defineExpose({surface});
function forward(event:Event,kind:'action'|'change'){const detail=(event as CustomEvent<Record<string,unknown>>).detail;if(kind==='action')emit('action',detail);else emit('change',detail)}
</script>
<template><div ref="host" class="bf-native-host" @bf-action="forward($event,'action')" @bf-change="forward($event,'change')"/><Teleport v-if="surface" :to="surface"><slot/></Teleport></template>
