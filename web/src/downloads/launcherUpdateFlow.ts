import { computed, reactive } from 'vue'
import type { LauncherUpdateState } from '../bridge'
import type { DownloadTask } from './model'

export interface LauncherUpdateTransport {
  read: () => Promise<LauncherUpdateState>
  download: (hash: string) => Promise<DownloadTask>
  task: () => Promise<DownloadTask | null>
  restart: (hash: string) => Promise<unknown>
}

/** Download permission and installation confirmation are separate user actions. */
export function createLauncherUpdateFlow(transport: LauncherUpdateTransport) {
  const state = reactive({
    update: { status: 'unconfigured', version: null, revision: null, manifestSha256: null, failure: null } as LauncherUpdateState,
    task: null as DownloadTask | null,
    dialog: null as 'download' | 'confirm' | 'progress' | 'complete' | null,
    consent: '', busy: false, restarting: false, error: '',
  })
  let stopped = false, reading = false, taskId = '', promptedHash = '', generation = 0
  const mandatory = computed(() => state.update.mandatory === true)
  const active = computed(() => !!state.consent && !state.error)
  const visible = computed(() => active.value || ['available', 'downloading', 'ready'].includes(state.update.status) ||
    (state.update.status === 'failed' && !!state.update.version && !!state.update.manifestSha256))
  const canInstall = computed(() => state.update.status === 'ready' && state.task?.stage === 'ready' &&
    (!taskId || state.task.id === taskId) && !state.busy && !state.restarting)

  function dismiss() { if (!state.restarting && !mandatory.value) state.dialog = null }
  function show() {
    state.dialog = canInstall.value ? 'confirm' : active.value || state.update.status === 'downloading' || state.update.status === 'ready' ? 'progress' : 'download'
  }
  async function restart(hash: string) {
    if (stopped || state.restarting || state.consent !== hash) return
    state.restarting = true
    try { await transport.restart(hash) }
    catch (error) {
      if (!stopped) {
        state.error = (error as Error).message
        state.consent = ''; state.restarting = false; state.dialog = 'progress'
      }
    }
  }
  async function refresh() {
    if (stopped || reading || state.busy || state.restarting) return
    reading = true
    const expected = generation
    try {
      const update = await transport.read()
      if (stopped || expected !== generation) return
      state.update = update
      if (state.consent && (update.manifestSha256 !== state.consent || update.status === 'available' || update.status === 'current')) {
        state.consent = ''; state.dialog = null; taskId = ''
      }
      if (['downloading', 'ready', 'failed'].includes(update.status)) {
        const task = await transport.task()
        if (stopped || expected !== generation) return
        state.task = task
        if (state.consent && (update.status === 'failed' || task?.stage === 'canceled')) {
          state.consent = ''; state.dialog = null
        }
      } else state.task = null
      if (mandatory.value && !state.restarting) {
        state.dialog = canInstall.value && !state.error ? 'confirm' :
          ['downloading', 'ready'].includes(update.status) || state.error ? 'progress' : 'download'
      }
      const hash = update.manifestSha256
      // Verified staging asks once per update; dismissing keeps it ready without repeated polling prompts.
      if (hash && canInstall.value && !state.error && promptedHash !== hash) {
        promptedHash = hash
        state.dialog = 'confirm'
      }
    } catch { /* Transient polling errors leave the last selection intact; failure guidance owns network notices. */ }
    finally { reading = false }
  }
  async function accept() {
    const hash = state.update.manifestSha256
    if (stopped || state.busy || state.restarting || !hash) return
    generation++; state.busy = true; state.error = ''; state.consent = hash
    state.dialog = 'progress'
    try {
      const task = await transport.download(hash)
      if (stopped) return
      state.task = task; taskId = task.id
    } catch (error) {
      if (!stopped) { state.consent = ''; state.error = (error as Error).message; state.dialog = 'progress' }
    } finally { state.busy = false }
  }
  async function install() {
    const hash = state.update.manifestSha256
    if (stopped || !hash || !canInstall.value) return
    generation++; state.error = ''; state.consent = hash; state.dialog = 'progress'
    await restart(hash)
  }
  function stop() { stopped = true; generation++ }
  return { state, active, visible, canInstall, mandatory, show, dismiss, accept, install, refresh, stop }
}
