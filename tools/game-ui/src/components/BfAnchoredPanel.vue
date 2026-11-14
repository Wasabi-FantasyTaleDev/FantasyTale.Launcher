<script setup lang="ts">
import {onBeforeUnmount,onMounted,ref,watch} from 'vue';
/**
 * Lays its content out in game client pixels and zooms it to the panel box an anchor gave it. The box is measured, not
 * taken from the shared overlay page's width, so display scaling never draws the content larger than the native
 * controls beside it. `width`/`height` are the anchor's size in game pixels; the slot receives the zoom as `zoom`, which
 * FreeType text needs as its pixel ratio.
 */
const props=withDefaults(defineProps<{width:number;height:number;align?:'start'|'center'|'end'}>(),{align:'start'});
const emit=defineEmits<{zoom:[number]}>();
const box=ref<HTMLElement>(),zoom=ref(1);let observer:ResizeObserver|undefined;
const measure=()=>{const width=box.value?.clientWidth??0;if(width>0&&props.width>0&&width/props.width!==zoom.value){zoom.value=width/props.width;emit('zoom',zoom.value)}};
onMounted(()=>{observer=new ResizeObserver(measure);if(box.value)observer.observe(box.value);measure()});
onBeforeUnmount(()=>observer?.disconnect());
watch(()=>props.width,measure);
const justify={start:'flex-start',center:'center',end:'flex-end'} as const;
</script>
<template><div ref="box" class="bf-anchored-panel" style="position:absolute;inset:0;overflow:hidden"><div :style="{zoom,width:width+'px',height:height+'px',display:'flex',alignItems:'center',justifyContent:justify[align]}"><slot :zoom="zoom"/></div></div></template>
