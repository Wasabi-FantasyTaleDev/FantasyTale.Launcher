<script setup lang="ts">
import {onBeforeUnmount,ref,watch} from 'vue';
import {useDrawTracker} from '../context';
// Keeps its content invisible until the provider has no surface left drawing, so a control never appears half-drawn.
// Surfaces mount in waves, so "nothing pending" must hold briefly; strict windows also wait beyond the fallback limit.
const props=withDefaults(defineProps<{limit?:number;strict?:boolean}>(),{limit:8000,strict:false});
const emit=defineEmits<{ready:[]}>();
const tracker=useDrawTracker(),shown=ref(false);let quiet:ReturnType<typeof setTimeout>|undefined;
function reveal(){if(shown.value)return;shown.value=true;emit('ready')}
function check(){if(shown.value)return;clearTimeout(quiet);quiet=setTimeout(()=>{if((tracker?.pending.value??0)===0)reveal()},50)}
watch(()=>tracker?.pending.value??0,check,{immediate:true});
const fallback=setTimeout(()=>{if(!props.strict)reveal()},props.limit);
onBeforeUnmount(()=>{clearTimeout(quiet);clearTimeout(fallback)});
</script>
<template><div class="bf-reveal" :style="{display:'contents',visibility:shown?'visible':'hidden'}"><slot/></div></template>
