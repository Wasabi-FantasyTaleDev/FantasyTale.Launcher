<script setup lang="ts">
import BfNative from './BfNative.vue';
const props=defineProps<{page:'settings'|'room'|'message'|'notice'|'warning'|'error'|'confirm'}>();
const emit=defineEmits<{action:[Record<string,unknown>];change:[Record<string,unknown>];error:[Error]}>();
</script>
<template><BfNative :revision="props.page" :draw="ui=>page==='notice'||page==='warning'||page==='error'||page==='confirm'?ui.dialog({confirm:page==='confirm',icon:page==='confirm'?'question':page,openSound:page==='error'?'fail':'popup'}):ui[page]()" @action="emit('action',$event)" @change="emit('change',$event)" @error="emit('error',$event)"/></template>
