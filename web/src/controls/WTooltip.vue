<script setup lang="ts">
// WinUI ToolTip: appears above the wrapped element after a short hover delay.
import { inject, onBeforeUnmount, ref } from 'vue'
import { overlayLayer } from './overlay'

const props = withDefaults(defineProps<{ text: string; placement?: 'top' | 'right' }>(), { placement: 'top' })

const visible = ref(false)
const pos = ref({ left: 0, top: 0 })
const host = ref<HTMLElement>()
const layer = Math.max(400, inject(overlayLayer, 0) + 20)
let timer = 0

function show() {
  timer = window.setTimeout(() => {
    const r = host.value?.getBoundingClientRect()
    if (!r) return
    pos.value = props.placement === 'right' ? { left: r.right + 10, top: r.top + r.height / 2 } : { left: r.left + r.width / 2, top: r.top }
    visible.value = true
  }, 500)
}
function hide() {
  clearTimeout(timer)
  visible.value = false
}
onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <span ref="host" class="w-tooltip-host" @mouseenter="show" @mouseleave="hide" @focusin="show" @focusout="hide">
    <slot />
    <Teleport to="body">
      <Transition name="tip">
        <span v-if="visible" class="w-tooltip type-caption" :class="{ right: placement === 'right' }" role="tooltip" :style="{ left: `${pos.left}px`, top: `${pos.top}px`, zIndex: layer }">
          {{ text }}
        </span>
      </Transition>
    </Teleport>
  </span>
</template>

<style scoped>
.w-tooltip-host {
  display: inline-flex;
}
.w-tooltip {
  position: fixed;
  transform: translate(-50%, calc(-100% - 8px));
  z-index: 400;
  max-width: 320px;
  padding: 6px 10px;
  border-radius: var(--radius-control);
  background: var(--flyout-bg);
  -webkit-backdrop-filter: var(--flyout-backdrop);
  backdrop-filter: var(--flyout-backdrop);
  border: 1px solid var(--stroke-surface-flyout);
  box-shadow: var(--shadow-flyout);
  color: var(--text-primary);
  pointer-events: none;
  white-space: normal;
}
.tip-enter-active {
  transition:
    opacity var(--duration-fast) linear,
    transform var(--duration-fast) var(--ease-fast-out-slow-in);
}
.w-tooltip.right { transform: translateY(-50%); }
.tip-enter-from.right { opacity: 0; transform: translate(-4px, -50%); }
.tip-leave-active {
  transition: opacity var(--duration-faster) linear;
}
.tip-enter-from {
  opacity: 0;
  transform: translate(-50%, calc(-100% - 4px));
}
.tip-leave-to {
  opacity: 0;
}
</style>
