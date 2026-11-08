<script setup lang="ts">
import { onMounted, ref } from 'vue'

defineProps<{ contained?: boolean }>()
const scroller = ref<HTMLElement>()
const edge = ref(0)
function updateEdge() {
  edge.value = Math.min(1, Math.max(0, scroller.value?.scrollTop ?? 0) / 24)
}
onMounted(updateEdge)
</script>

<template>
  <div class="page-body" :class="{ contained }" :style="{ '--page-edge-opacity': 1 - edge, '--page-edge-strength': edge }">
    <div ref="scroller" class="page-scroll" @scroll.passive="updateEdge"><slot /></div>
  </div>
</template>

<style scoped>
.contained > .page-scroll { display: flex; flex-direction: column; overflow: hidden; }
/* Fade moving content into the existing backdrop without painting a second header. */
.page-scroll {
  -webkit-mask-image: linear-gradient(to bottom, rgb(0 0 0 / var(--page-edge-opacity)) 0, #000 24px);
  mask-image: linear-gradient(to bottom, rgb(0 0 0 / var(--page-edge-opacity)) 0, #000 24px);
}
.page-body::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 10px;
  height: 24px;
  z-index: 1;
  pointer-events: none;
  opacity: var(--page-edge-strength);
  -webkit-backdrop-filter: blur(5px);
  backdrop-filter: blur(5px);
  -webkit-mask-image: linear-gradient(to bottom, #000, transparent);
  mask-image: linear-gradient(to bottom, #000, transparent);
}
@media (prefers-reduced-transparency: reduce), (forced-colors: active) {
  .page-scroll { -webkit-mask-image: none; mask-image: none; }
  .page-body::before { display: none; }
}
</style>
