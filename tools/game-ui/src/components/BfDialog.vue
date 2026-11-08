<script setup lang="ts">
import BfNative from './BfNative.vue';
import type {BfRenderer,BfUiSoundEvent} from '../context';

export type BfDialogIcon='notice'|'warning'|'question'|'error';
export type BfDialogButton={name:'Ok'|'Cancel'|string;label:string;width?:number;height?:number;x?:number;sprite?:string};
export type BfDialogButtons=BfDialogButton[]|((ui:BfRenderer)=>BfDialogButton[]);
export type BfDialogGeometry={width:number;height:number;bodyHeight:number;buttonY:number};
export type BfDialogBody=(ui:BfRenderer,root:HTMLElement,geometry:BfDialogGeometry)=>Promise<void>|void;

const props=withDefaults(defineProps<{
  title?:string;text?:string;icon?:BfDialogIcon;buttons?:BfDialogButtons;
  contentWidth?:number;contentHeight?:number;contentX?:number;contentY?:number;
  outerWidth?:number;outerHeight?:number;buttonY?:number;body?:BfDialogBody;openSound?:BfUiSoundEvent|false;
}>(),{title:'公告',text:'',icon:'notice',buttons:()=>[{name:'Ok',label:'确认'}]});
const emit=defineEmits<{action:[Record<string,unknown>];change:[Record<string,unknown>];error:[Error]}>();
const draw=(ui:BfRenderer)=>ui.dialog({
  title:props.title,text:props.text,icon:props.icon,buttons:typeof props.buttons==='function'?props.buttons(ui):props.buttons,
  contentWidth:props.contentWidth,contentHeight:props.contentHeight,contentX:props.contentX,contentY:props.contentY,
  outerWidth:props.outerWidth,outerHeight:props.outerHeight,buttonY:props.buttonY,
  body:props.body?((root:HTMLElement,geometry:BfDialogGeometry)=>props.body!(ui,root,geometry)):undefined,openSound:props.openSound
});
</script>

<template><BfNative :revision="JSON.stringify({title:props.title,text:props.text,icon:props.icon,contentWidth:props.contentWidth,contentHeight:props.contentHeight,outerWidth:props.outerWidth,outerHeight:props.outerHeight,buttonY:props.buttonY,openSound:props.openSound})" :draw="draw" @action="emit('action',$event)" @change="emit('change',$event)" @error="emit('error',$event)"/></template>
