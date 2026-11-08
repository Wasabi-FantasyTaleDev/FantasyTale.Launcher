<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

const props = withDefaults(defineProps<{ axis?: 'height' | 'width' }>(), { axis: 'height' })
const content = ref<HTMLElement>()
const size = ref<number>()
let observer: ResizeObserver | undefined
const dimensions = computed(() => size.value === undefined ? undefined : { [props.axis]: `${size.value}px` })
watch(content, element => {
  observer?.disconnect()
  size.value = undefined
  if (!element) return
  // Layout dimensions stay stable while an ancestor is entering with a scale transform.
  const measure = () => { size.value = props.axis === 'height' ? element.offsetHeight : element.offsetWidth }
  measure()
  observer = new ResizeObserver(measure)
  observer.observe(element)
}, { flush: 'post' })
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div class="w-animated-size" :class="axis" :style="dimensions"><div ref="content" class="resize-content"><slot /></div></div>
</template>

<style scoped>
.w-animated-size { min-width: 0; transition: height 280ms cubic-bezier(0.2,0.65,0.25,1), width 280ms cubic-bezier(0.2,0.65,0.25,1); }
.resize-content { position: relative; display: flow-root; }
.width { display: inline-flex; flex: none; overflow: clip; vertical-align: middle; }
.width > .resize-content { display: inline-flex; width: max-content; flex: none; }
@media (prefers-reduced-motion: reduce) { .w-animated-size { transition: none; } }
</style>
