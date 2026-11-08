<script setup lang="ts">
// Custom title bar for the chromeless window: back button and caption text,
// a drag region, then the Windows 11 caption buttons. In a plain browser the
// caption buttons stay inert (the window chrome is the browser's own).
import { onBeforeUnmount, onMounted, provide, ref } from 'vue'
import { useRouter } from 'vue-router'
import { call, inHost, on } from '../bridge'
import FluentIcon from '../controls/FluentIcon.vue'
import WDialog from '../controls/WDialog.vue'
import { overlayLayer } from '../controls/overlay'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
provide(overlayLayer, 500)
const exitOpen = ref(false)
const exitBusy = ref(false)
const exitError = ref('')
let pendingClose: 'window.exit' | 'window.close' | null = null
function requestClose() {
  if (!inHost) return
  if (document.documentElement.classList.contains('booting')) { void call('window.close'); return }
  if (exitBusy.value) return
  exitError.value = ''
  exitOpen.value = true
}
function finishClose(method: 'window.exit' | 'window.close') {
  if (exitBusy.value) return
  pendingClose = method
  exitBusy.value = true
  exitOpen.value = false
}
async function completeClose() {
  const method = pendingClose
  pendingClose = null
  if (!method) return
  try { await call(method) }
  catch (error) { exitError.value = (error as Error).message; exitOpen.value = true }
  finally { exitBusy.value = false }
}

defineProps<{ title: string; showBack?: boolean }>()

const router = useRouter()
const maximized = ref(false)
const unsubscribe = on('window.state', data => { maximized.value = (data as { maximized: boolean }).maximized })
onBeforeUnmount(unsubscribe)
onMounted(async () => { if (inHost) maximized.value = (await call('window.state')).maximized })

function startDrag(event: MouseEvent) {
  if (!inHost || event.button !== 0 || (event.target as HTMLElement).closest('button')) return
  event.preventDefault()
  void call('window.startDrag')
}

async function toggleMaximize() {
  maximized.value = await call('window.toggleMaximize')
}
</script>

<template>
  <header class="titlebar" @mousedown="startDrag" @dblclick="!($event.target as HTMLElement).closest('button') && inHost && toggleMaximize()">
    <button v-if="showBack" type="button" class="nav-btn" :aria-label="$t('titlebar.back')" @click="router.back()">
      <FluentIcon glyph="back" :size="14" />
    </button>
    <div class="titlebar-content">
      <span class="titlebar-title">{{ title }}</span>
    </div>
    <div class="caption" :class="{ inert: !inHost }">
      <button type="button" :aria-label="$t('titlebar.minimize')" :tabindex="inHost ? 0 : -1" @click="inHost && call('window.minimize')">
        <FluentIcon glyph="chromeMinimize" :size="10" />
      </button>
      <button
        type="button"
        :aria-label="maximized ? $t('titlebar.restore') : $t('titlebar.maximize')"
        :tabindex="inHost ? 0 : -1"
        @click="inHost && toggleMaximize()">
        <FluentIcon :glyph="maximized ? 'chromeRestore' : 'chromeMaximize'" :size="10" />
      </button>
      <button type="button" class="close" :aria-label="$t('titlebar.close')" :tabindex="inHost ? 0 : -1" @click="requestClose">
        <FluentIcon glyph="chromeClose" :size="10" />
      </button>
    </div>
  </header>
  <WDialog primary-first :open="exitOpen" :title="t('launcherExit.title')" :primary-text="t('launcherExit.background')" :secondary-text="t('launcherExit.end')" :close-text="t('dialog.cancel')" :secondary-disabled="exitBusy" :primary-disabled="exitBusy" :close-disabled="exitBusy" @after-leave="completeClose" @primary="finishClose('window.close')" @secondary="finishClose('window.exit')" @close="!exitBusy && (exitOpen = false)">
    <p>{{ t('launcherExit.body') }}</p>
    <p v-if="exitError" class="form-error" role="alert">{{ exitError }}</p>
  </WDialog>
</template>

<style scoped>
.titlebar {
  position: fixed;
  inset: 0 0 auto 0;
  height: 48px;
  display: flex;
  align-items: stretch;
  z-index: 50;
  user-select: none;
}
.nav-btn {
  flex: none;
  align-self: flex-start;
  width: 32px;
  height: 32px;
  margin-left: 8px;
  border-radius: var(--radius-control);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--text-primary);
  transition: background-color var(--duration-faster) linear;
}
.nav-btn:hover {
  background: var(--subtle-secondary);
}
.nav-btn:active {
  background: var(--subtle-tertiary);
}
.titlebar-content {
  flex: 1;
  display: flex;
  align-items: center;
  height: 32px;
  padding-left: 16px;
  min-width: 0;
}
.titlebar-title {
  color: var(--text-primary);
  font-size: 12px;
  line-height: 16px;
  font-weight: 400;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.caption {
  display: flex;
  align-items: stretch;
  height: 32px;
}
.caption.inert {
  /* In a browser the fake buttons look right but do nothing. */
  opacity: 0.38;
}
.caption button {
  width: 46px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--text-primary);
  transition:
    background-color var(--duration-faster) linear,
    color var(--duration-faster) linear;
}
.caption button:hover {
  background: var(--subtle-secondary);
}
.caption button:active {
  background: var(--subtle-tertiary);
}
.caption button.close:hover {
  background: #c42b1c;
  color: #ffffff;
}
.caption button.close:active {
  background: #c83c31;
  color: rgba(255, 255, 255, 0.7);
}
</style>
