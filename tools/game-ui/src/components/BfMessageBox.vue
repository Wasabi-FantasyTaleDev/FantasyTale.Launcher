<script setup lang="ts">
import BfDialog,{type BfDialogButton,type BfDialogIcon} from './BfDialog.vue';
import type {BfUiSoundEvent} from '../context';

export type BfMessageIcon=BfDialogIcon;
export type BfMessageButton=BfDialogButton;
const props=withDefaults(defineProps<{title?:string;text:string;icon?:BfMessageIcon;buttons:BfMessageButton[];openSound?:BfUiSoundEvent|false;textScale?:number}>(),{title:'公告',icon:'notice'});
const emit=defineEmits<{action:[Record<string,unknown>];change:[Record<string,unknown>];error:[Error]}>();
</script>

<template><BfDialog :title="props.title" :text="props.text" :icon="props.icon" :buttons="props.buttons" :open-sound="props.openSound" :text-scale="props.textScale" @action="emit('action',$event)" @change="emit('change',$event)" @error="emit('error',$event)"/></template>
