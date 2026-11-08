<script setup lang="ts">
withDefaults(defineProps<{ size?: number }>(), { size: 16 })
</script>

<template>
  <svg class="fluent-icon" :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M4 18V5m0 13h16M7 13l4-4 4 3 5-7m-5 0h5v5" /></svg>
</template>

<style scoped>
svg { display: block; flex: none; }
</style>
