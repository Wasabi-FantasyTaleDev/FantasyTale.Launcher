<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { LevelIcon } from '../bridge'
import FluentIcon from '../controls/FluentIcon.vue'
const props = withDefaults(defineProps<{ icon: LevelIcon; assetBase: string; size?: number; label?: string }>(), { size: 28, label: '' })
const failed = ref(false)
const source = computed(() => props.assetBase + props.icon.path.split('/').map(encodeURIComponent).join('/'))
watch(source, () => { failed.value = false })
const scale = computed(() => Math.min(1, props.size / Math.max(props.icon.width, props.icon.height)))
const style = computed(() => ({ width: `${props.icon.width * scale.value}px`, height: `${props.icon.height * scale.value}px`,
  backgroundImage: `url(${JSON.stringify(source.value)})`, backgroundSize: `${props.icon.sheetWidth * scale.value}px ${props.icon.sheetHeight * scale.value}px`,
  backgroundPosition: `${-props.icon.x * scale.value}px ${-props.icon.y * scale.value}px` }))
</script>
<template>
  <span class="rank-icon" :title="label" :aria-label="label || undefined" :role="label ? 'img' : undefined">
    <FluentIcon v-if="failed" glyph="contact" :size="size" />
    <span v-else class="rank-sprite" :style="style" aria-hidden="true" />
    <img :src="source" alt="" class="rank-probe" @error="failed = true" />
  </span>
</template>
<style scoped>
.rank-icon { display: inline-flex; align-items: center; justify-content: center; flex: none; }
.rank-sprite { display: inline-block; background-repeat: no-repeat; }
.rank-probe { display: none; }
</style>
