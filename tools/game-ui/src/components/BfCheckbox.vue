<script setup lang="ts">
import {computed} from 'vue';import BfControl from './BfControl.vue';import BfLabel from './BfLabel.vue';
const props=withDefaults(defineProps<{modelValue:boolean;label:string;name?:string;x:number;y:number;width?:number}>(),{width:220});
const emit=defineEmits<{'update:modelValue':[boolean];change:[boolean]}>();
const attrs=computed(()=>({Name:props.name||props.label,PosX:String(props.x),PosY:String(props.y),Width:'21',Height:'18',Check:props.modelValue?'1':'0'}));
function change(value:Record<string,unknown>){const checked=Boolean(value.value);emit('update:modelValue',checked);emit('change',checked)}
</script>
<template><BfControl type="UiCheckBox" :attrs="attrs" @change="change"/><BfLabel :text="label" :x="x+23" :y="y" :width="width" :height="20"/></template>
