<script setup lang="ts">
import {computed} from 'vue';import BfControl from './BfControl.vue';
const props=withDefaults(defineProps<{text:string;x:number;y:number;width:number;height:number;size?:number;color?:string;align?:'left'|'center'}>(),{size:13,color:'0xFF5B5B5B',align:'left'});
const attrs=computed(()=>({Text:props.text,PosX:String(props.x),PosY:String(props.y),Width:String(props.width),Height:String(props.height),FontSize:String(props.size),TextColor:props.color,TextFormat:props.align==='center'?'5':'4'}));
</script>
<template><BfControl type="UiLabel" :attrs="attrs"/></template>
