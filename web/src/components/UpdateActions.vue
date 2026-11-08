<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import type { DownloadTask } from '../downloads/model'
import { canCancel, canPause } from '../downloads/model'
import { downloadAction } from '../downloads/live'
import { launcherUpdate } from '../downloads/launcherUpdate'
import { state } from '../state'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import FluentIcon from '../controls/FluentIcon.vue'

defineProps<{ task: DownloadTask; hideRetry?: boolean }>()
const { t } = useI18n()
const confirmation = ref<'cancel' | 'restart' | null>(null)
const dialogKind = ref<'cancel' | 'restart'>('cancel')
const deferred = ref<{ task: DownloadTask; action: 'cancel' | 'restart' } | null>(null)
const busy = ref(false)
const archivePassword = ref('')
const passwordTask = ref<DownloadTask>()
async function act(task: DownloadTask, action: Parameters<typeof downloadAction>[1], password?: string) {
  if (busy.value) return
  busy.value = true
  try { await downloadAction(task, action, password) }
  catch (error) { state.error = (error as Error).message }
  finally { busy.value = false }
}
function requestArchivePassword(task: DownloadTask) { archivePassword.value = ''; passwordTask.value = task }
async function submitArchivePassword() {
  const task = passwordTask.value
  if (!task || !archivePassword.value) return
  const password = archivePassword.value
  closeArchivePassword()
  await act(task, 'retry', password)
}
function closeArchivePassword() { passwordTask.value = undefined; archivePassword.value = '' }
onBeforeUnmount(closeArchivePassword)
function confirm(task: DownloadTask) {
  if (!confirmation.value) return
  deferred.value = { task, action: confirmation.value }; confirmation.value = null
}
function showConfirmation(kind: 'cancel' | 'restart', task?: DownloadTask) {
  if (kind === 'restart' && task?.mode === 'live' && task.target.kind === 'launcher') {
    void launcherUpdate.refresh().then(() => launcherUpdate.show()); return
  }
  dialogKind.value = kind; confirmation.value = kind
}
function afterLeave() {
  const pending = deferred.value; deferred.value = null
  if (pending) void act(pending.task, pending.action)
}
</script>
<template>
  <div class="update-actions">
    <TransitionGroup name="update-action" tag="div" class="action-buttons">
    <WButton v-if="canPause(task) && !(task.target.kind === 'launcher' && launcherUpdate.mandatory.value)" key="pause" :disabled="busy" @click="act(task, 'pause')"><FluentIcon glyph="pause" :size="14" />{{ t('updates.pause') }}</WButton>
    <WButton v-if="task.stage === 'queued' && task.mode === 'preview'" key="start" :disabled="busy" @click="act(task, 'start')"><FluentIcon glyph="download" :size="14" />{{ t('updates.start') }}</WButton>
    <WButton v-if="task.stage === 'waiting' && task.mode === 'preview'" key="install" @click="act(task, 'install')">{{ t('updates.install') }}</WButton>
    <WButton v-if="canCancel(task) && !(task.target.kind === 'launcher' && launcherUpdate.mandatory.value)" key="cancel" variant="subtle" :disabled="busy" @click="showConfirmation('cancel')">{{ t('updates.cancel') }}</WButton>
    <WButton v-if="task.stage === 'paused'" key="resume" variant="accent" :disabled="busy" @click="act(task, 'resume')"><FluentIcon glyph="play" :size="14" />{{ t('updates.resume') }}</WButton>
    <WButton v-if="task.stage === 'failed' && task.failure === 'archive-password'" key="archive-password" variant="accent" :disabled="busy" @click="requestArchivePassword(task)">{{ t('updates.enterArchivePassword') }}</WButton>
    <WButton v-if="task.stage === 'failed' && task.failure !== 'archive-password' && !$props.hideRetry" key="retry" variant="accent" :disabled="busy" @click="act(task, 'retry')"><FluentIcon glyph="refresh" :size="14" />{{ t(task.failure === 'startup' ? 'clientUpdates.check' : 'updates.retry') }}</WButton>
    <div v-if="$slots['before-actions']" key="before-actions" class="extra-action"><slot name="before-actions" /></div>
    <WButton v-if="task.stage === 'ready'" key="restart" variant="accent" :disabled="busy" @click="showConfirmation('restart', task)"><FluentIcon glyph="refresh" :size="14" />{{ t('updates.restart') }}</WButton>
    </TransitionGroup>
    <slot />
    <WDialog :open="confirmation !== null" :title="t(dialogKind === 'restart' ? task.mode === 'live' ? 'launcherUpdates.restartTitle' : 'updates.previewRestartTitle' : 'updates.cancelTitle')" :primary-text="t(dialogKind === 'restart' ? task.mode === 'live' ? 'launcherUpdates.restart' : 'updates.previewComplete' : 'updates.cancelUpdate')" :close-text="t(dialogKind === 'restart' ? 'dialog.cancel' : 'updates.continueDownload')" @close="confirmation = null" @primary="confirm(task)" @after-leave="afterLeave">
      <p>{{ t(dialogKind === 'restart' ? task.mode === 'live' ? 'launcherUpdates.restartBody' : 'updates.previewRestartBody' : 'updates.cancelBody') }}</p>
    </WDialog>
    <WDialog :open="!!passwordTask" :title="t('updates.archivePasswordTitle')" :primary-text="t('updates.archivePasswordConfirm')" :close-text="t('dialog.cancel')" :primary-disabled="!archivePassword" @close="closeArchivePassword" @primary="submitArchivePassword">
      <p>{{ t('updates.archivePasswordBody') }}</p>
      <WTextBox v-model="archivePassword" type="password" autocomplete="off" maxlength="256" :aria-label="t('updates.archivePasswordPlaceholder')" :placeholder="t('updates.archivePasswordPlaceholder')" @keyup.enter="submitArchivePassword" />
    </WDialog>
  </div>
</template>
<style scoped>
.update-actions, .action-buttons { display: flex; align-items: center; gap: 8px; }
.action-buttons { position: relative; flex-wrap: nowrap; flex: none; }
.action-buttons > :deep(.w-btn), .extra-action { flex: none; }
.extra-action { display: flex; align-items: center; }
.update-action-move { transition: transform 320ms cubic-bezier(.2,.8,.2,1); }
.update-action-enter-active, .update-action-leave-active { transition: opacity 220ms ease, transform 320ms cubic-bezier(.2,.8,.2,1); }
.update-action-enter-from, .update-action-leave-to { opacity: 0; transform: translateX(10px); }
.update-action-leave-active { position: absolute; pointer-events: none; }
@media (prefers-reduced-motion: reduce) { .update-action-move, .update-action-enter-active, .update-action-leave-active { transition: none; } .update-action-enter-from, .update-action-leave-to { transform: none; } }
</style>
