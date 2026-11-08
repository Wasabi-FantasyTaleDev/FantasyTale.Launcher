<script setup lang="ts">
// Window shell: 48px title bar over a WinUI NavigationView. The boot overlay from
// index.html only covers the small static game-definition request; slower local
// inspection and remote synchronization continue after the workspace is visible.
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import AppTitleBar from './components/AppTitleBar.vue'
import AppNav from './components/AppNav.vue'
import AuthDialog from './components/AuthDialog.vue'
import StopGameDialog from './components/StopGameDialog.vue'
import LauncherUpdateFailureDialog from './components/LauncherUpdateFailureDialog.vue'
import ClientPolicyActivationDialog from './components/ClientPolicyActivationDialog.vue'
import PluginLaunchWarningDialog from './components/PluginLaunchWarningDialog.vue'
import PluginResourceConflictDialog from './components/PluginResourceConflictDialog.vue'
import RequiredPluginUpdateDialog from './components/RequiredPluginUpdateDialog.vue'
import GitHubMirrorSuggestionDialog from './components/GitHubMirrorSuggestionDialog.vue'
import { requestRecommendations } from './plugins/recommendations'
import ShellNotification from './components/ShellNotification.vue'
import LaunchFailureNotice from './components/LaunchFailureNotice.vue'
import InstanceFailureNotice from './components/InstanceFailureNotice.vue'
import { InstanceRequestError } from './instanceFailures'
import ServerFailureNotice from './components/ServerFailureNotice.vue'
import { ServerRequestError } from './serverFailures'
import { call, inHost, on, type PluginRecoveryNotice } from './bridge'
import { loadGames, pollRunning, refreshInstances, refreshPluginSelectionsAfterDownload, forgetRecoveredPlugins, cancelStorePluginInstallation, state } from './state'
import { pollClientUpdates, checkClientUpdates, clientUpdates } from './downloads/clientUpdates'
import { downloads, pollDownloads, sessionDownloadIds } from './downloads/live'
import { acknowledgeClientLaunch } from './downloads/completions'
import { previewCurrentClient } from './downloads/context'
import { previewAvailable, stopUpdatePreview, updatePreview } from './downloads/preview'
import PluginRecoveryDialog from './components/PluginRecoveryDialog.vue'
import PluginDeprecationDialog from './components/PluginDeprecationDialog.vue'
import { forgetDeprecatedPlugin } from './state'
import type { PluginDeprecationNotice } from './bridge'
import { startStoreUpdateChecks, liveStoreSession } from './store/background'
import { localized } from './i18n'
import { pluginNotices, showPluginNotice, dismissPluginNotice } from './plugins/notices'
import type { LocalizedText } from './bridge'

const { t, locale } = useI18n()
watch(() => downloads.tasks, tasks => {
  for (const task of tasks) if (task.stage === 'canceled') cancelStorePluginInstallation(task.id)
}, { immediate: true })
const stopAutomaticPluginEvents = on('plugins.autoUpdated', async data => {
  const item = data as { id: string; name?: LocalizedText; fallbackName: string; from: string; to: string }
  try {
    await Promise.all([refreshPluginSelectionsAfterDownload(), liveStoreSession.refreshInventory()])
    showPluginNotice(`auto:${item.id}`, 'success', t('pluginUpdates.autoUpdated', {
      name: localized(item.name) || item.fallbackName, from: item.from, to: item.to,
    }))
  } catch (error) { state.error = (error as Error).message }
})
onBeforeUnmount(stopAutomaticPluginEvents)
watch(locale, language => { if (inHost) void call('window.language', { language }).catch(() => {}) }, { immediate: true })
const route = useRoute()
const shellReady = ref(false)
const recoveryNotices = ref<PluginRecoveryNotice[]>([])
const deprecationNotices = ref<PluginDeprecationNotice[]>([])
function addDeprecationNotices(data: unknown) {
  if (!Array.isArray(data) || data.length === 0) return
  for (const item of data) {
    if (!item || typeof item.game !== 'string' || typeof item.pluginId !== 'string' || !Array.isArray(item.versions)
      || !item.versions.every((version: unknown) => typeof version === 'string') || !item.name || !item.reason) continue
    forgetDeprecatedPlugin(item.game, item.pluginId)
    if (!deprecationNotices.value.some(notice => notice.game === item.game && notice.pluginId === item.pluginId)) deprecationNotices.value.push(item)
  }
  void refreshPluginSelectionsAfterDownload().catch(error => { state.error = (error as Error).message })
  void liveStoreSession.refreshInventory().catch(error => { state.error = (error as Error).message })
}
const stopDeprecationEvents = on('plugins.deprecated', addDeprecationNotices)
onBeforeUnmount(stopDeprecationEvents)
const recoveryNotice = computed(() => recoveryNotices.value[0] ?? null)
const recoveryKey = (notice: PluginRecoveryNotice) => JSON.stringify([notice.game, notice.pluginId, notice.version])
function addRecoveryNotices(data: unknown) {
  if (!Array.isArray(data)) return
  const notices = data.filter((item): item is PluginRecoveryNotice => {
    if (!item || typeof item !== 'object') return false
    const value = item as Record<string, unknown>
    return typeof value.instanceId === 'string' && typeof value.game === 'string' && typeof value.pluginId === 'string' && typeof value.version === 'string'
  })
  const known = new Set(recoveryNotices.value.map(recoveryKey))
  for (const notice of notices) {
    const key = recoveryKey(notice)
    if (known.has(key)) continue
    known.add(key)
    forgetRecoveredPlugins(notice)
    recoveryNotices.value.push(notice)
  }
}
function dismissRecoveryNotice() {
  const notice = recoveryNotice.value
  recoveryNotices.value = recoveryNotices.value.slice(1)
  if (notice) void call('plugins.dismissRecoveryNotice', { game: notice.game, pluginId: notice.pluginId, version: notice.version }).catch(() => {})
}
const stopRecoveryEvents = on('plugins.recovered', addRecoveryNotices)
onBeforeUnmount(stopRecoveryEvents)
watch(() => state.running.filter(client => !client.hook || ['ready', 'not-required'].includes(client.hook.state))
  .map(client => `${client.instanceId}:${client.processId}`), (ready, previous = []) => {
  for (const client of state.running) {
    const identity = `${client.instanceId}:${client.processId}`
    if (!ready.includes(identity) || previous.includes(identity)) continue
    const instance = state.instances.find(item => item.id === client.instanceId)
    if (instance) acknowledgeClientLaunch(downloads.tasks, instance.id, instance.game, instance.build)
  }
}, { immediate: true })
const showBack = computed(() => !!route.meta.back)
watch([() => state.loaded, () => route.query.updatePreview], ([loaded, requested]) => {
  if (loaded && requested === '1' && previewAvailable && !updatePreview.enabled) previewCurrentClient()
}, { immediate: true })
onBeforeUnmount(stopUpdatePreview)
let stopStoreChecks: (() => void) | undefined
onBeforeUnmount(() => stopStoreChecks?.())
let stopPolling: (() => void) | undefined
onBeforeUnmount(() => stopPolling?.())
let stopUpdates: (() => void) | undefined
onBeforeUnmount(() => stopUpdates?.())
let stopRunning: (() => void) | undefined
onBeforeUnmount(() => stopRunning?.())
watch(() => downloads.tasks.filter(task => task.stage === 'completed').map(task => task.id).join(','), () => {
  if (clientUpdates.preferences.autoCheck && inHost) void checkClientUpdates()
  void refreshPluginSelectionsAfterDownload().catch(error => { state.error = (error as Error).message })
})
const recommendedDownloads = new Set<string>()
watch(() => downloads.tasks.map(task => ({ id: task.id, stage: task.stage, target: task.target })), tasks => {
  for (const task of tasks.filter(task => task.stage === 'completed' && task.target.kind === 'client' && sessionDownloadIds.has(task.id))) {
    if (recommendedDownloads.has(task.id)) continue
    if (tasks.some(other => ['queued', 'checking', 'downloading', 'verifying', 'installing'].includes(other.stage) && other.target.instanceId === task.target.instanceId)) continue
    recommendedDownloads.add(task.id)
    if (task.target.instanceId) void refreshInstances().then(() => {
      const instance = state.instances.find(item => item.id === task.target.instanceId)
      if (!instance) return
      const serverId = state.selectedGameId === instance.game ? state.selectedServerId : undefined
      return requestRecommendations(instance.id, instance.name, serverId)
    }).catch(error => { state.error = (error as Error).message })
  }
})

// The Photino host gives the web view a transparent Mica backdrop; keep the
// painted base colour when that is not the case (plain browser dev server).
if (inHost) document.documentElement.classList.add('mica-transparent')

onMounted(async () => {
  stopRunning = pollRunning()
  if (inHost) void call('plugins.recoveryNotices').then(addRecoveryNotices).catch(() => {})
  if (inHost) void call('plugins.deprecationNotices').then(addDeprecationNotices).catch(() => {})
  await loadGames()
  stopStoreChecks = startStoreUpdateChecks()
  stopPolling = pollDownloads()
  stopUpdates = pollClientUpdates()
  document.title = t('app.title')
  // Let the logo enter fully even when catalog initialization finishes in its first frames.
  await Promise.all((document.querySelector('.boot-logo')?.getAnimations() ?? []).map(animation => animation.finished.catch(() => {})))
  // Reveal the workspace after the splash clears so its motion cannot be hidden behind the logo surface.
  await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())))
  const boot = document.getElementById('boot')
  const shell = document.querySelector<HTMLElement>('.shell')
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches
  if (boot) {
    await boot.animate([{ opacity: 1 }, { opacity: 0 }], {
      duration: reduced ? 180 : 280, easing: 'ease-in-out', fill: 'forwards',
    }).finished.catch(() => {})
    boot.remove()
  }
  if (shell) {
    const frames = reduced ? [{ opacity: 0 }, { opacity: 1 }] : [
      { opacity: 0, transform: 'translateY(24px) scale(0.98)' },
      { opacity: 1, transform: 'translateY(0) scale(1)' },
    ]
    const entrance = shell.animate(frames, {
      duration: reduced ? 300 : 650, easing: 'cubic-bezier(0.2, 0.65, 0.25, 1)', fill: 'forwards',
    })
    await entrance.finished.catch(() => {})
    document.documentElement.classList.remove('booting')
    entrance.cancel()
  } else document.documentElement.classList.remove('booting')
  if (inHost) void call('window.ready').catch(error => { state.error = (error as Error).message })
  shellReady.value = true
})
</script>

<template>
  <div class="shell">
    <Teleport to="body"><AppTitleBar :title="t('app.title')" :show-back="showBack" /></Teleport>
    <AppNav>
      <router-view v-slot="{ Component }">
        <Transition name="page" mode="out-in">
          <!-- Home owns pending launch decisions; navigation must not dispose that workflow. -->
          <KeepAlive include="HomePage" :max="1">
            <component :is="Component" :key="route.name === 'home' ? 'home' : route.meta.keepOnQueryChange ? route.path : route.fullPath" />
          </KeepAlive>
        </Transition>
      </router-view>
    </AppNav>
    <AuthDialog />
    <StopGameDialog />
    <LauncherUpdateFailureDialog />
    <ClientPolicyActivationDialog />
    <PluginLaunchWarningDialog />
    <PluginResourceConflictDialog />
    <RequiredPluginUpdateDialog />
    <PluginRecoveryDialog :notice="recoveryNotice" @close="dismissRecoveryNotice" />
    <PluginDeprecationDialog :notices="deprecationNotices" @close="deprecationNotices = []" />
    <GitHubMirrorSuggestionDialog :ready="shellReady" />
    <TransitionGroup name="shell-notice" tag="div" class="plugin-shell-notices">
      <ShellNotification v-for="notice in pluginNotices" :key="notice.id" :severity="notice.severity" :message="notice.message" :persistent="notice.persistent" :duration-ms="notice.durationMs" @close="dismissPluginNotice(notice.id)" />
      <div v-if="state.error" :key="`error:${state.error}`" class="shell-error">
        <ServerFailureNotice v-if="state.requestFailure instanceof ServerRequestError && state.requestFailure.message === state.error" :error="state.requestFailure" dismissible @close="state.error = ''; state.requestFailure = undefined" />
        <LaunchFailureNotice v-else-if="state.launchFailure && state.launchFailureMessage === state.error" :failure="state.launchFailure" :message="state.error" @close="state.error = ''; state.launchFailure = undefined" />
        <InstanceFailureNotice v-else-if="state.requestFailure instanceof InstanceRequestError && state.requestFailure.message === state.error" :error="state.requestFailure" @close="state.error = ''; state.requestFailure = undefined" />
        <ShellNotification v-else severity="error" :message="state.error" @close="state.error = ''" />
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.shell {
  position: fixed;
  inset: 0;
}
.plugin-shell-notices { position: fixed; right: 16px; bottom: 16px; display: grid; gap: 8px; width: min(540px, calc(100vw - 32px)); z-index: 390; }
.shell-error {
  max-width: 100%;
  overflow-wrap: anywhere;
  border-radius: var(--radius-control);
  box-shadow: var(--shadow-flyout);
  color: var(--text-primary);
  z-index: 400;
}
.shell-notice-enter-active,
.shell-notice-leave-active {
  transition: opacity 180ms ease, transform 220ms var(--ease-fast-out-slow-in);
}
.shell-notice-enter-from,
.shell-notice-leave-to {
  opacity: 0;
  transform: translateY(12px);
}
</style>
