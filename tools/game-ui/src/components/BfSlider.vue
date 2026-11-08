<script setup lang="ts">
import {computed} from 'vue';import BfControl from './BfControl.vue';
const props=withDefaults(defineProps<{modelValue:number;name:string;x:number;y:number;width:number;height?:number;min?:number;max?:number;interval?:number;minText?:string;maxText?:string}>(),{height:55,min:0,max:100,interval:10,minText:'',maxText:''});
const emit=defineEmits<{'update:modelValue':[number];change:[number]}>();
const attrs=computed(()=>({Name:props.name,PosX:String(props.x),PosY:String(props.y),Width:String(props.width),Height:String(props.height),Min:String(props.min),Max:String(props.max),Position:String(props.modelValue),Interval:String(props.interval),MinText:props.minText,MaxText:props.maxText}));
function change(value:Record<string,unknown>){emit('update:modelValue',Number(value.value));emit('change',Number(value.value))}
</script>
<template><BfControl type="UiTrackBar" :attrs="attrs" @change="change"/></template>
