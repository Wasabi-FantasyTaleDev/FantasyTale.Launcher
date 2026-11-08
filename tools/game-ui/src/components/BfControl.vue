<script setup lang="ts">
import {computed,ref,watch} from 'vue';
import BfNative from './BfNative.vue';
const props=defineProps<{type:string;attrs:Record<string,string>}>();
function createHost(){return document.createElement('div')}
const native=ref<InstanceType<typeof BfNative>>();
const drawingRevision=computed(()=>{const {Check,Position,...layout}=props.attrs;return JSON.stringify([props.type,layout]);});
watch(()=>[props.attrs.Check,props.attrs.Position],()=>{const element=native.value?.surface as (HTMLElement & {setValue?:(value:unknown)=>void})|undefined;element?.setValue?.(props.type==='UiCheckBox'?props.attrs.Check==='1':Number(props.attrs.Position));},{flush:'post'});
const emit=defineEmits<{change:[Record<string,unknown>];action:[Record<string,unknown>];error:[Error]}>();
</script>
<template><BfNative ref="native" :revision="drawingRevision" :draw="async ui=>(await ui.renderNode({type,attrs,children:[]},createHost()))!" @change="emit('change',$event)" @action="emit('action',$event)" @error="emit('error',$event)"><slot/></BfNative></template>
