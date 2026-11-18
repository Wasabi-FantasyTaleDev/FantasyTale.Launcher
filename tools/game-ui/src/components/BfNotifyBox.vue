<script setup lang="ts">
/**
 * The client's notification box (NotifyBox): a buttonless notice with the theme's own frame and progress mark.
 * The overlay shows it where the game's own notification would be hidden behind an open overlay surface.
 */
import BfNative from './BfNative.vue';
const props=defineProps<{text:string}>();
const emit=defineEmits<{error:[Error]}>();
</script>
<template><BfNative :revision="props.text" :draw="ui=>ui.message({text:props.text})" @error="emit('error',$event)"/></template>
