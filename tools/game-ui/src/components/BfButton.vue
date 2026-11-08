<script setup lang="ts">
import BfNative from './BfNative.vue';
const props=withDefaults(defineProps<{kind?:'Ok'|'Cancel';label:string;x:number;y:number;width?:number;height?:number}>(),{kind:'Ok',width:65,height:27});
const emit=defineEmits<{click:[]}>();
function createHost(){return document.createElement('div')}
</script>
<template><BfNative :revision="[props.kind,props.label,props.x,props.y,props.width,props.height]" :draw="ui=>ui.button(createHost(),kind,x,y,width,height,label)" @action="emit('click')"/></template>
