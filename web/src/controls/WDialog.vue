<script setup lang="ts">
// WinUI 3 ContentDialog: smoked layer, rounded surface, title, content and up to
// three bottom buttons (primary / secondary / close). Escape and close fire `close`.
import WButton from './WButton.vue'
import FluentIcon from './FluentIcon.vue'
import WProgressRing from './WProgressRing.vue'
import { computed, inject, nextTick, onBeforeUnmount, provide, ref, watch } from 'vue'
import { overlayLayer } from './overlay'

const props = withDefaults(
  defineProps<{
    open: boolean
    title?: string
    primaryText?: string
    secondaryText?: string
    closeText?: string
    secondaryDisabled?: boolean
    primaryDisabled?: boolean
    primaryBusy?: boolean
    closeDisabled?: boolean
    primaryFirst?: boolean
    dismissOnBackdrop?: boolean
    showDismissButton?: boolean
    dismissLabel?: string
    focusPanel?: boolean
    wide?: boolean
    navigation?: boolean
    imageViewer?: boolean
  }>(),
  { title: '', primaryText: '', secondaryText: '', closeText: '', primaryDisabled: false, primaryBusy: false, secondaryDisabled: false, closeDisabled: false, focusPanel: false },
)
const emit = defineEmits<{ close: []; primary: []; secondary: []; afterLeave: [] }>()
const actions = computed(() => props.primaryFirst ? ['primary', 'secondary'] as const : ['secondary', 'primary'] as const)
const panel = ref<HTMLElement>()
const layer = Math.max(300, inject(overlayLayer, 0) + 100)
provide(overlayLayer, layer)
let previous: HTMLElement | null = null
let resizeObserver: ResizeObserver | undefined
let contentObserver: MutationObserver | undefined
let measuredHeight = 0
let resizeFrame = 0
let resizeTimer: ReturnType<typeof setTimeout> | undefined
function resetPanelSize() {
  resizeObserver?.disconnect(); resizeObserver = undefined
  contentObserver?.disconnect(); contentObserver = undefined
  cancelAnimationFrame(resizeFrame)
  if (resizeTimer) clearTimeout(resizeTimer)
  resizeTimer = undefined
  measuredHeight = 0
  if (panel.value) panel.value.style.height = ''
}
function observePanelSize() {
  const element = panel.value
  if (!element) return
  resetPanelSize()
  measuredHeight = element.offsetHeight
  const resize = () => {
    const current = resizeTimer ? element.offsetHeight : measuredHeight
    cancelAnimationFrame(resizeFrame)
    if (resizeTimer) clearTimeout(resizeTimer)
    resizeTimer = undefined
    const transition = element.style.transition
    element.style.transition = 'none'
    element.style.height = ''
    const next = element.offsetHeight
    if (!next || next === current) {
      element.style.transition = transition
      measuredHeight = next
      return
    }
    element.style.height = `${current}px`
    void element.offsetHeight
    element.style.transition = transition
    resizeFrame = requestAnimationFrame(() => {
      element.style.height = `${next}px`
      measuredHeight = next
      resizeTimer = setTimeout(() => {
        element.style.height = ''
        measuredHeight = element.offsetHeight
        resizeTimer = undefined
      }, 220)
    })
  }
  // Observe natural content changes without feeding the panel's own height animation back into itself.
  contentObserver = new MutationObserver(records => {
    if (records.some(record => record.type !== 'attributes' || record.target !== element)) resize()
  })
  contentObserver.observe(element, { childList: true, subtree: true, characterData: true, attributes: true })
  let width = element.offsetWidth
  resizeObserver = new ResizeObserver(() => {
    if (element.offsetWidth === width) return
    width = element.offsetWidth
    resize()
  })
  resizeObserver.observe(element)
}
watch(() => props.open, async open => {
  if (!open) { resetPanelSize(); previous?.focus(); return }
  previous = document.activeElement as HTMLElement
  await nextTick()
  observePanelSize()
  const initial = props.focusPanel ? panel.value : panel.value?.querySelector<HTMLElement>('input:not(:disabled),button:not(:disabled)') ?? panel.value
  initial?.focus()
})
onBeforeUnmount(() => { resetPanelSize(); previous?.focus() })

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    e.preventDefault()
    e.stopPropagation()
    if (!props.closeDisabled) emit('close')
  }
  if (e.key === 'Tab') {
    const nodes = Array.from(panel.value?.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),[tabindex="0"]') ?? [])
      .filter(node => node.getAttribute('tabindex') !== '-1' && !node.closest('[inert]') && node.getClientRects().length > 0)
    const first = nodes[0], last = nodes.at(-1)
    if (!first) { e.preventDefault(); panel.value?.focus() }
    else if (document.activeElement === panel.value) { e.preventDefault(); (e.shiftKey ? last : first)?.focus() }
    else if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus() }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus() }
  }
}
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog" :duration="{ enter: 340, leave: 200 }" @after-leave="emit('afterLeave')">
      <div v-if="open" class="w-dialog-smoke" :style="{ zIndex: layer }" @keydown="onKeydown" @click.self="dismissOnBackdrop && !closeDisabled && emit('close')">
        <div ref="panel" class="w-dialog" :class="{ 'has-dismiss': showDismissButton, wide, navigation, 'image-viewer': imageViewer }" role="dialog" aria-modal="true" :aria-label="title" tabindex="-1">
          <button v-if="showDismissButton && !closeDisabled" type="button" class="w-dialog-dismiss" :aria-label="dismissLabel" @click="emit('close')"><FluentIcon glyph="cancel" :size="12" /></button>
          <h2 v-if="title" class="w-dialog-title type-subtitle">{{ title }}</h2>
          <div class="w-dialog-content"><slot /></div>
          <div class="w-dialog-actions">
            <template v-for="action in actions" :key="action">
              <WButton v-if="props[`${action}Text`]" :variant="action === 'primary' ? 'accent' : 'standard'" class="w-dialog-btn" :class="{ 'is-busy': action === 'primary' && primaryBusy }" :disabled="props[`${action}Disabled`] || (action === 'primary' && primaryBusy)" :aria-busy="action === 'primary' && primaryBusy" @click="action === 'primary' ? emit('primary') : emit('secondary')"><WProgressRing v-if="action === 'primary' && primaryBusy" :size="16" />{{ props[`${action}Text`] }}</WButton>
            </template>
            <WButton v-if="closeText" class="w-dialog-btn" :disabled="closeDisabled" @click="emit('close')">{{ closeText }}</WButton>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.w-dialog-btn.is-busy:disabled { --w-btn-border: var(--ctrl-elevation-border-accent); background-color: var(--accent-default); color: var(--text-on-accent-primary); }
.w-dialog-btn.is-busy :deep(.w-ring-arc) { stroke: currentColor; }
.w-dialog-smoke {
  position: fixed;
  inset: 0;
  z-index: 300;
  display: grid;
  place-items: center;
  background: var(--smoke);
  padding: 24px;
}
.w-dialog {
  position: relative;
  width: 100%;
  max-width: 548px;
  min-width: 320px;
  max-height: calc(100vh - 48px);
  display: flex;
  flex-direction: column;
  border-radius: var(--radius-overlay);
  background: var(--solid-bg-tertiary);
  border: 1px solid var(--stroke-surface-default);
  /* WinUI paints a LayerFillColorAlt band across the top of a ContentDialog. */
  box-shadow:
    inset 0 40px 0 -39px var(--layer-alt),
    var(--shadow-dialog);
  color: var(--text-primary);
  overflow: hidden;
  transition: height 220ms cubic-bezier(0.2, 0.65, 0.25, 1);
}
.w-dialog:focus { outline: none; }
.w-dialog.wide { max-width: 660px; }
.w-dialog.navigation { max-width: 1080px; }
.navigation > .w-dialog-title { display: none; }
.navigation > .w-dialog-content { padding: 0; overflow: hidden; }
.w-dialog.image-viewer { max-width: 1280px; }
.image-viewer > .w-dialog-title { display: none; }
.image-viewer > .w-dialog-content { padding: 0; overflow: hidden; }
.w-dialog-title {
  margin: 0;
  padding: 20px 24px 12px;
}
.has-dismiss .w-dialog-title { padding-right: 64px; }
.w-dialog-dismiss { position: absolute; top: 12px; right: 12px; display: grid; place-items: center; width: 32px; height: 32px; border-radius: var(--radius-control); color: var(--text-secondary); background: transparent; transition: background 167ms linear, color 167ms linear; }
.w-dialog-dismiss:hover { background: var(--subtle-secondary); color: var(--text-primary); }
.w-dialog-dismiss:active { background: var(--subtle-tertiary); }
.w-dialog-content {
  flex: 1;
  overflow-y: auto;
  padding: 0 24px 24px;
}
.w-dialog-actions {
  display: flex;
  gap: 8px;
  padding: 24px;
  border-top: 1px solid var(--stroke-divider);
  background: var(--solid-bg-base);
}
.w-dialog-actions:empty {
  display: none;
}
.w-dialog-btn {
  flex: 1;
}
.dialog-enter-active {
  transition: opacity 340ms cubic-bezier(0.2, 0.65, 0.25, 1);
}
.dialog-leave-active {
  transition: opacity 200ms ease-in;
}
.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;
}
.dialog-enter-active .w-dialog {
  transition:
    transform 340ms cubic-bezier(0.2, 0.65, 0.25, 1),
    opacity 280ms ease-out;
}
.dialog-leave-active .w-dialog {
  transition:
    transform 200ms ease-in,
    opacity 200ms ease-in;
}
.dialog-enter-from .w-dialog {
  opacity: 0;
  transform: translateY(16px) scale(0.96);
}
.dialog-leave-to .w-dialog {
  opacity: 0;
  transform: translateY(8px) scale(0.98);
}
@media (prefers-reduced-motion: reduce) {
  .dialog-enter-active, .dialog-leave-active,
  .dialog-enter-active .w-dialog, .dialog-leave-active .w-dialog { transition-duration: 180ms !important; }
  .dialog-enter-from .w-dialog, .dialog-leave-to .w-dialog { transform: none; }
}
</style>
