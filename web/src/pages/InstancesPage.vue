<script setup lang="ts">
import PageBody from '../components/PageBody.vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost, type Instance } from '../bridge'
import { localized } from '../i18n'
import { gameInstances, gamePreparing, isRunning, perform, selectedGame, state } from '../state'
import ClientUpdateStatus from '../components/ClientUpdateStatus.vue'
import { clientUpdates, checkClientUpdates } from '../downloads/clientUpdates'
import InstanceDialog from '../components/InstanceDialog.vue'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import PackageSummaryCard from '../components/PackageSummaryCard.vue'
import { requestStop, runningStatusKey } from '../state'

const { t } = useI18n()
const editorOpen = ref(false), mode = ref<'create' | 'edit' | 'clone'>('create')
const editing = ref<Instance>(), deleting = ref<Instance>()
const removalNotice = ref(''), removalError = ref('')
const recovering = ref<Instance>()
async function recover() { if (recovering.value && await perform(() => call('instances.recover', { id: recovering.value!.id, confirmedStopped: true }))) recovering.value = undefined }
function edit(value: 'create' | 'edit' | 'clone', instance?: Instance) {
  if (value !== 'create' && !state.instancesLoaded) return
  mode.value = value; editing.value = instance; editorOpen.value = true
}
function confirmRemoval(instance: Instance) { removalError.value = ''; removalNotice.value = ''; deleting.value = instance }
async function remove(deleteFiles: boolean) {
  const instance = deleting.value
  if (!instance || state.busy) return
  removalError.value = ''
  const success = await perform(async () => {
    const result = await call('instances.delete', { id: instance.id, deleteFiles })
    removalNotice.value = result.preservedFiles ? t('clientRemoval.preserved', { count: result.preservedFiles }) : ''
  })
  if (success) deleting.value = undefined
  else removalError.value = state.error
}
const buildName = (id: string) => localized(selectedGame.value?.builds.find(b => b.id === id)?.name, { dates: true }) || id
</script>

<template>
  <section class="page">
    <header class="page-header"><div><h1 class="page-title">{{ t('instances.title') }}</h1><p class="page-desc">{{ t('instances.desc') }}</p></div><div class="header-actions"><WButton v-if="!gamePreparing && gameInstances.length" :disabled="!inHost || clientUpdates.requesting || clientUpdates.overview.checking" @click="checkClientUpdates"><FluentIcon glyph="refresh" />{{ t(clientUpdates.requesting || clientUpdates.overview.checking ? 'clientUpdates.checking' : 'clientUpdates.check') }}</WButton><WButton v-if="!gamePreparing" variant="accent" :disabled="state.busy" @click="edit('create')"><FluentIcon glyph="add" />{{ t('live.add') }}</WButton></div></header>
    <PageBody>
    <p v-if="removalNotice" class="removal-notice" role="status">{{ removalNotice }}</p>
    <div v-if="gamePreparing" class="empty-state"><FluentIcon glyph="clock" :size="40" /><h2>{{ t('live.gameComingSoon') }}</h2><p>{{ localized(selectedGame?.serviceNotice ?? undefined) || t('live.gamePending') }}</p></div>
    <div v-else-if="!gameInstances.length" class="empty-state"><FluentIcon glyph="gamepad" :size="40" /><h2>{{ t('live.emptyTitle') }}</h2><p>{{ t('live.emptyBody') }}</p><WButton @click="edit('create')">{{ t('live.add') }}</WButton></div>
    <div v-else class="instance-list">
      <p v-if="clientUpdates.error || clientUpdates.overview.error" role="alert">{{ clientUpdates.error || t(`clientUpdates.${clientUpdates.overview.error}`) }}</p>
      <article v-for="instance in gameInstances" :key="instance.id" class="instance-row surface" :class="{ chosen: instance.id === state.selectedInstanceId }">
        <button class="instance-select" :aria-pressed="instance.id === state.selectedInstanceId" @click="state.selectedInstanceId = instance.id"><span class="instance-icon"><FluentIcon glyph="gamepad" :size="24" /></span><span class="instance-text"><strong>{{ instance.name }}</strong><span>{{ buildName(instance.build) }}<span class="meta-dot">·</span>{{ selectedGame?.builds.find(b => b.id === instance.build)?.releaseVersion || instance.build }}</span></span></button>
        <WButton v-if="isRunning(instance.id)" variant="subtle" :disabled="state.busy" @click="requestStop(instance.id)">{{ t(runningStatusKey(instance.id)) }}</WButton>
        <!-- <span v-else class="instance-status">{{ instance.id === state.selectedInstanceId ? t('live.selected') : '' }}</span> -->
        <ClientUpdateStatus :instance-id="instance.id" hide-unmanaged />
        <div class="row-actions">
          <WButton v-if="instance.recoveryRequired" :disabled="!inHost || isRunning(instance.id) || state.busy" @click="recovering = instance">{{ t('recovery.action') }}</WButton>
          <WButton v-if="instance.diagnosticsEnabled" variant="subtle" :title="t('launch.diagnostics')" :aria-label="t('launch.diagnostics')" :disabled="!inHost || state.busy" @click="perform(() => call('instances.openDiagnostics', { id: instance.id }))"><FluentIcon glyph="info" /></WButton>
          <WButton variant="subtle" :title="t('instances.menu.openFolder')" :aria-label="t('instances.menu.openFolder')" :disabled="!inHost || state.busy" @click="perform(() => call('instances.openFolder', { id: instance.id }))"><FluentIcon glyph="folderOpen" /></WButton>
          <WButton variant="subtle" :title="t('live.editTitle')" :aria-label="t('live.editTitle')" :disabled="!state.instancesLoaded || isRunning(instance.id) || state.busy" @click="edit('edit', instance)"><FluentIcon glyph="edit" /></WButton>
          <WButton variant="subtle" :title="t('live.cloneTitle')" :aria-label="t('live.cloneTitle')" :disabled="!state.instancesLoaded || isRunning(instance.id) || state.busy" @click="edit('clone', instance)"><FluentIcon glyph="copy" /></WButton>
          <WButton variant="subtle" :title="t('instances.menu.remove')" :aria-label="t('instances.menu.remove')" :disabled="isRunning(instance.id) || state.busy" @click="confirmRemoval(instance)"><FluentIcon glyph="delete" /></WButton>
        </div>
      </article>
    </div>
    <InstanceDialog :open="editorOpen" :mode="mode" :instance="editing" @close="editorOpen = false" />
    <WDialog :open="!!deleting" :title="t('live.deleteTitle')" :primary-text="t('clientRemoval.now')" :secondary-text="t('clientRemoval.record')" :close-text="t('dialog.cancel')" primary-first focus-panel dismiss-on-backdrop :primary-disabled="state.busy" :secondary-disabled="state.busy" :close-disabled="state.busy" @primary="remove(true)" @secondary="remove(false)" @close="deleting = undefined">
      <PackageSummaryCard v-if="deleting" :name="deleting.name" glyph="gamepad"><template #meta>{{ buildName(deleting.build) }}</template></PackageSummaryCard>
      <p class="removal-body">{{ t('clientRemoval.confirm', { name: deleting?.name }) }}</p>
      <p v-if="removalError" class="removal-error" role="alert">{{ removalError }}</p>
    </WDialog>
    <WDialog :open="!!recovering" :title="t('recovery.title')" :primary-text="t('recovery.action')" :close-text="t('dialog.cancel')" :primary-disabled="state.busy" :close-disabled="state.busy" @primary="recover" @close="recovering = undefined"><p>{{ t('recovery.body', { name: recovering?.name }) }}</p></WDialog>
  </PageBody>
  </section>
</template>

<style scoped>
.header-actions { display: flex; align-items: center; gap: 8px; }
.removal-body { margin: 16px 0 0; line-height: 1.65; overflow-wrap: anywhere; }
.removal-error, .removal-notice { padding: 10px 12px; border-radius: var(--radius-control); background: var(--card-bg-default); font-size: 13px; line-height: 1.6; }
.removal-error { color: var(--fill-critical); }
.instance-list { display: flex; flex-direction: column; gap: 8px; }
.instance-row { display: flex; align-items: center; padding: 12px; gap: 8px; position: relative; }
.instance-row.chosen::before { content: ''; position: absolute; left: 0; width: 3px; height: 24px; border-radius: 2px; background: var(--accent-default); }
.instance-select { display: flex; align-items: center; gap: 18px; flex: 1; text-align: left; min-width: 0; padding: 8px; }
.instance-icon { color: var(--accent-text-primary); width: 40px; text-align: center; }
.instance-text { display: flex; flex-direction: column; gap: 6px; min-width: 0; }
.instance-text strong { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.instance-text > span { color: var(--text-secondary); font-size: 12px; }
.meta-dot { padding: 0 8px; }
.instance-status { color: var(--text-tertiary); font-size: 12px; margin: 0 12px; }
.instance-status.running { color: var(--fill-success); }
.row-actions { display: flex; gap: 2px; }
</style>
