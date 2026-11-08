<script setup lang="ts">
import {provideGameUi,type BfTheme} from '../context';
import {themeForClientDate} from '../../theme.js';
import {DEFAULT_UI_VOLUME} from '../../ui-sounds.js';
const props=withDefaults(defineProps<{theme?:BfTheme;clientDate?:string;resourceRoot?:string;sound?:boolean;volume?:number}>(),{theme:'v2',resourceRoot:'/local/',sound:true,volume:DEFAULT_UI_VOLUME});
const emit=defineEmits<{error:[Error]}>();
const {renderer,error}=provideGameUi(()=>themeForClientDate(props.clientDate,props.theme) as BfTheme,()=>props.resourceRoot,()=>props.sound,()=>props.volume);
defineExpose({unlockSound:()=>renderer.value?.sounds?.unlock(),playSound:(event:import('../context').BfUiSoundEvent)=>renderer.value?.sounds?.play(event)});
import {watch} from 'vue';
watch(error,value=>{if(value)emit('error',value)});
</script>
<template><slot v-if="renderer"/><slot v-else-if="error" name="error" :error="error"><div role="alert">{{error.message}}</div></slot><slot v-else name="loading"/></template>
