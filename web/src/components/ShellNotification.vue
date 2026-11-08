<script setup lang="ts">
import { onBeforeUnmount, watch } from 'vue'
import WInfoBar from '../controls/WInfoBar.vue'
import type { InfoBarSeverity } from '../controls/WInfoBar.vue'

const props = withDefaults(defineProps<{
  message: string
  title?: string
  severity?: InfoBarSeverity
  persistent?: boolean
  durationMs?: number
}>(), { title: '', severity: 'info', persistent: undefined, durationMs: 6000 })
const emit = defineEmits<{ close: [] }>()
let timer: ReturnType<typeof setTimeout> | undefined
let remaining = 6000
let started = 0
let hovered = false
let focused = false
let dismissed = false
function stop() {
  if (timer !== undefined) {
    clearTimeout(timer)
    timer = undefined
    remaining = Math.max(0, remaining - (performance.now() - started))
  }
}
function resume() {
  if (!props.message || dismissed || hovered || focused || timer !== undefined
    || (props.persistent ?? props.severity === 'error') || props.durationMs <= 0) return
  started = performance.now()
  timer = setTimeout(() => { timer = undefined; dismissed = true; emit('close') }, remaining)
}
function hover(value: boolean) { hovered = value; if (value) stop(); else resume() }
function focus(value: boolean) { focused = value; if (value) stop(); else resume() }
function close() { if (dismissed) return; stop(); dismissed = true; emit('close') }
watch(() => [props.message, props.title, props.severity, props.persistent, props.durationMs], () => {
  stop(); remaining = props.durationMs; dismissed = false; resume()
}, { immediate: true, flush: 'sync' })
onBeforeUnmount(stop)
</script>

<template>
  <WInfoBar :severity="severity" :title="title" @close="close" @mouseenter="hover(true)" @mouseleave="hover(false)"
    @focusin="focus(true)" @focusout="focus(false)">{{ message }}</WInfoBar>
</template>
