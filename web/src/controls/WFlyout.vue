<script setup lang="ts">
// Anchored popup used by ComboBox, menus and the game switcher. Teleports to <body>,
// positions itself under (or over) the anchor, and closes on outside press or Escape.
import { inject, nextTick, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import { overlayLayer } from './overlay'

const props = withDefaults(
  defineProps<{
    open: boolean
    /** Where the popup prefers to sit relative to the anchor. */
    placement?: 'bottom-start' | 'bottom' | 'bottom-end' | 'top-start'
    /** Make the popup exactly as wide as the anchor (ComboBox style). */
    matchAnchorWidth?: boolean
    offset?: number
    /** Extra minimum width for menu-style flyouts. */
    minWidth?: number
  }>(),
  { placement: 'bottom-start', matchAnchorWidth: false, offset: 4, minWidth: 0 },
)
const emit = defineEmits<{ close: [] }>()

const anchorEl = ref<HTMLElement>()
const popEl = ref<HTMLElement>()
const pos = ref({ left: '0px', top: '0px', minWidth: '0px', width: '' as string, maxHeight: '' })
const above = ref(false)
const layer = Math.max(200, inject(overlayLayer, 0) + 10)
provide(overlayLayer, layer)

function updatePosition() {
  const a = anchorEl.value?.getBoundingClientRect()
  const p = popEl.value
  if (!a || !p) return
  const gap = props.offset
  const margin = 8
  const rect = p.getBoundingClientRect()
  const available = Math.max(0, innerWidth - margin * 2)
  const w = Math.min(available, props.matchAnchorWidth ? a.width : Math.max(rect.width, props.minWidth))
  let top = a.bottom + gap
  above.value = false
  const belowSpace = Math.max(0, innerHeight - margin - top)
  const aboveSpace = Math.max(0, a.top - gap - margin)
  const contentHeight = p.scrollHeight + p.offsetHeight - p.clientHeight
  above.value = props.placement === 'top-start'
    ? contentHeight <= aboveSpace || aboveSpace > belowSpace
    : contentHeight > belowSpace && aboveSpace > belowSpace
  const height = Math.min(contentHeight, above.value ? aboveSpace : belowSpace)
  if (above.value) top = a.top - gap - height
  let left = a.left
  if (props.placement === 'bottom') left = a.left + a.width / 2 - w / 2
  if (props.placement === 'bottom-end') left = a.right - w
  left = Math.max(margin, Math.min(left, innerWidth - w - margin))
  pos.value = {
    left: `${left}px`,
    top: `${Math.max(margin, top)}px`,
    minWidth: `${Math.min(available, props.matchAnchorWidth ? a.width : props.minWidth)}px`,
    width: props.matchAnchorWidth ? `${w}px` : '',
    maxHeight: `${above.value ? aboveSpace : belowSpace}px`,
  }
}

function onPointerDown(e: PointerEvent) {
  if (!props.open) return
  const t = e.target as Node
  if (popEl.value?.contains(t) || anchorEl.value?.contains(t)) return
  emit('close')
}
function onKeyDown(e: KeyboardEvent) {
  if (!props.open || e.key !== 'Escape') return
  e.preventDefault()
  e.stopPropagation()
  emit('close')
  anchorEl.value?.querySelector<HTMLElement>('button:not(:disabled),[tabindex="0"]')?.focus()
}
function onScroll(e: Event) {
  if (!props.open) return
  // Close when a scrollable ancestor moves, like a XAML light-dismiss popup.
  if (popEl.value && !popEl.value.contains(e.target as Node)) emit('close')
}

watch(
  () => props.open,
  async (open) => {
    if (!open) return
    await nextTick()
    updatePosition()
  },
)
onMounted(() => {
  document.addEventListener('pointerdown', onPointerDown, true)
  document.addEventListener('keydown', onKeyDown, true)
  window.addEventListener('resize', updatePosition)
  window.addEventListener('scroll', onScroll, true)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onPointerDown, true)
  document.removeEventListener('keydown', onKeyDown, true)
  window.removeEventListener('resize', updatePosition)
  window.removeEventListener('scroll', onScroll, true)
})
</script>

<template>
  <!-- Single root: the Teleport lives inside the anchor so attrs and scope ids inherit. -->
  <span ref="anchorEl" class="w-flyout-anchor">
    <slot name="anchor" />
    <Teleport to="body">
      <Transition name="flyout">
        <div
          v-if="open"
          ref="popEl"
          class="w-flyout"
          :class="{ 'w-flyout--above': above }"
          :style="{ ...pos, zIndex: layer }"
          role="menu"
          @vue:mounted="updatePosition">
          <slot />
        </div>
      </Transition>
    </Teleport>
  </span>
</template>

<style scoped>
.w-flyout-anchor {
  display: block;
}
.w-flyout {
  overflow-y: auto;
  max-width: calc(100vw - 16px);
  position: fixed;
  z-index: 200;
  padding: 4px;
  border-radius: var(--radius-overlay);
  background: var(--flyout-bg);
  -webkit-backdrop-filter: var(--flyout-backdrop);
  backdrop-filter: var(--flyout-backdrop);
  border: 1px solid var(--stroke-surface-flyout);
  box-shadow: var(--shadow-flyout);
  transform-origin: top center;
}
.w-flyout--above {
  transform-origin: bottom center;
}
</style>
