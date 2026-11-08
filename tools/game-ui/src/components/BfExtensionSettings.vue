<script setup lang="ts">
import {ref,watch} from 'vue';
import BfNative from './BfNative.vue';
import type {BfRenderer} from '../context';
import {extensionSettingsState} from '../../extension-settings.js';

export type BfExtensionSettingsValue={oneClickRun:boolean;healthDisplay:'percent'|'value';allowEscLeave:boolean;bgm:'old'|'new'};
const props=defineProps<{modelValue?:BfExtensionSettingsValue}>();
const emit=defineEmits<{'update:modelValue':[BfExtensionSettingsValue];change:[BfExtensionSettingsValue];save:[BfExtensionSettingsValue];cancel:[];error:[Error]}>();
let draft=extensionSettingsState(props.modelValue) as BfExtensionSettingsValue;
const revision=ref(0);
watch(()=>props.modelValue,value=>{if(JSON.stringify(value)!==JSON.stringify(draft)){draft=extensionSettingsState(value) as BfExtensionSettingsValue;revision.value++;}},{deep:true});
function change(value:BfExtensionSettingsValue){draft=value;emit('update:modelValue',{...value});emit('change',{...value});}
const draw=(ui:BfRenderer)=>ui.extensionSettings({value:draft,onChange:change});
function action(value:Record<string,unknown>){if(value.name==='Ok')emit('save',{...draft});if(value.name==='Cancel'||value.name==='close')emit('cancel');}
</script>

<template><BfNative :revision="revision" :draw="draw" @action="action" @error="emit('error',$event)"/></template>
