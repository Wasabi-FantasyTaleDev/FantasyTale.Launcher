<script setup lang="ts">
import {computed} from 'vue';
import {useGameUi} from '../context';
import BfControl from './BfControl.vue';
const props=defineProps<{title:string;x:number;y:number;width:number;height:number}>();const ui=useGameUi();
const attrs=computed(()=>{const renderer=ui.value;if(!renderer)return {};const form=renderer.manifest.forms[renderer.theme==='v3'?'Option/DlgOption-VideoSound3.ui':renderer.theme==='v2'?'DlgOption-VideoSound2.ui':'DlgOption-VideoSound.ui'];const original=form.children.find((n:{type:string})=>n.type==='UiGroupBox'||n.type==='UiPanel');return {...original.attrs,Name:'',Text:props.title,PosX:String(props.x),PosY:String(props.y),Width:String(props.width),Height:String(props.height)} as Record<string,string>});
</script>
<template><BfControl type="UiGroupBox" :attrs="attrs"><slot/></BfControl></template>
