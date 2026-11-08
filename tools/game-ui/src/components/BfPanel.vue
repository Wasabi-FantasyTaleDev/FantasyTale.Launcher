<script setup lang="ts">
import BfNative from './BfNative.vue';
const props=withDefaults(defineProps<{x:number;y:number;width:number;height:number;skin?:string}>(),{skin:'Images/Ui/FrameBox.smf'});
function createHost(){return document.createElement('div')}
</script>
<template><BfNative :revision="[props.x,props.y,props.width,props.height,props.skin]" :draw="ui=>ui.panel(createHost(),x,y,width,height,skin)"><slot/></BfNative></template>
