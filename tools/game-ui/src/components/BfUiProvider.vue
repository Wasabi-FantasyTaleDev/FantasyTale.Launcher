<script setup lang="ts">
import {provideGameUi,type BfTheme} from '../context';
import {themeForClientDate} from '../../theme.js';
import {DEFAULT_UI_VOLUME} from '../../ui-sounds.js';
const props=withDefaults(defineProps<{theme?:BfTheme;clientDate?:string;resourceRoot?:string;sound?:boolean;volume?:number}>(),{theme:'v2',resourceRoot:'/local/',sound:true,volume:DEFAULT_UI_VOLUME});
const emit=defineEmits<{error:[Error]}>();
const {renderer,error}=provideGameUi(()=>themeForClientDate(props.clientDate,props.theme) as BfTheme,()=>props.resourceRoot,()=>props.sound,()=>props.volume);
defineExpose({unlockSound:()=>renderer.value?.sounds?.unlock(),playSound:(event:import('../context').BfUiSoundEvent)=>renderer.value?.sounds?.play(event)});
import {computed,watch} from 'vue';
watch(error,value=>{if(value)emit('error',value)});
// The game draws its own pointer (kr-812 Images/Ui/MouseCursorNormal.png and MouseCursorClicked.png, tip at the top-left
// pixel) and never lets text be selected; a missing cursor file falls back to the system arrow.
const cursors=computed(()=>{const root=props.resourceRoot.replace(/\/?$/,'/')+'cursors/';
  return {'--bf-cursor':`url("${root}normal.png") 0 0, default`,'--bf-cursor-clicked':`url("${root}clicked.png") 0 0, default`}});
</script>
<template><div class="bf-ui-root" :style="cursors"><slot v-if="renderer"/><slot v-else-if="error" name="error" :error="error"><div role="alert">{{error.message}}</div></slot><slot v-else name="loading"/></div></template>
<style>.bf-ui-root{display:contents;user-select:none;-webkit-user-select:none}.bf-ui-root *{cursor:var(--bf-cursor)!important}.bf-ui-root :active{--bf-cursor:var(--bf-cursor-clicked)}.bf-ui-root :is(input[type=text],input[type=search],input:not([type]),textarea){user-select:text;-webkit-user-select:text}</style>
