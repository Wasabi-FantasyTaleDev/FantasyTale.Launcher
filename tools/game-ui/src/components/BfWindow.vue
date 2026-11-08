<script setup lang="ts">
import BfNative from './BfNative.vue';
const props=defineProps<{title:string;width:number;height:number}>();
const emit=defineEmits<{close:[];action:[Record<string,unknown>];error:[Error]}>();
function action(value:Record<string,unknown>){if(value.name==='close')emit('close');emit('action',value)}
</script>
<template><BfNative :revision="[props.title,props.width,props.height]" :draw="ui=>ui.windowShell(width,height,title)" @action="action" @error="emit('error',$event)"><slot/></BfNative></template>
