<script setup lang="ts">
import PageBody from '../components/PageBody.vue'
import WContentTransition from '../controls/WContentTransition.vue'
import { computed, ref, useId, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { previewAvailable, setPreviewStage, stopUpdatePreview, updatePreview } from '../downloads/preview'
import { downloads, downloadTasks, clearFinishedDownloads } from '../downloads/live'
import { state } from '../state'
import { previewCurrentClient } from '../downloads/context'
import { downloadStageKey, fileBytes, formatBytes, isTerminal, percent, taskStages, transferMetrics, type TaskStage } from '../downloads/model'
import UpdateActions from '../components/UpdateActions.vue'
import UpdateProgress from '../components/UpdateProgress.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WProgressBar from '../controls/WProgressBar.vue'

const { t, locale } = useI18n(), route = useRoute(), router = useRouter()
const filter = ref('all'), selectedPath = ref('')
const detailsExpanded = ref(false), detailTab = ref<'files' | 'source'>('files')
const workspace = ref<HTMLElement>(), progress = ref<HTMLElement>(), detailsToggle = ref<HTMLButtonElement>()
const detailHeight = ref(0), detailsId = useId()
const tabs = ['all', 'active', 'attention', 'done'] as const
const tasks = computed(() => downloadTasks.value)
const active = computed(() => tasks.value.filter(task => !isTerminal(task) && task.stage !== 'failed'))
const attention = computed(() => tasks.value.filter(task => ['failed', 'paused', 'waiting', 'ready'].includes(task.stage)))
const done = computed(() => tasks.value.filter(isTerminal))
const visible = computed(() => filter.value === 'active' ? active.value : filter.value === 'attention' ? attention.value : filter.value === 'done' ? done.value : tasks.value)
const selected = computed(() => visible.value.find(task => task.id === downloads.selectedId) ?? visible.value[0])
const speed = computed(() => tasks.value.reduce((total, task) => total + transferMetrics(task).speed, 0))
const count = (tab: string) => tab === 'active' ? active.value.length : tab === 'attention' ? attention.value.length : tab === 'done' ? done.value.length : tasks.value.length
const bytes = (value: number | null) => formatBytes(value, locale.value)
const stages = computed(() => taskStages.filter(stage => selected.value?.target.kind === 'launcher' ? stage !== 'waiting' : stage !== 'ready').map(value => ({ value, label: t(`updates.${value}`) })))
watch(() => selected.value?.id, () => { selectedPath.value = ''; detailsExpanded.value = false; detailTab.value = 'files' })
watch(detailsExpanded, open => { if (!open) selectedPath.value = '' })
watch([workspace, progress, detailsToggle], ([area, summary, toggle], _, onCleanup) => {
  if (!area || !summary || !toggle) return
  // Animate to the remaining workspace height so details never move task controls off-screen.
  const resize = () => { detailHeight.value = Math.max(0, area.clientHeight - summary.getBoundingClientRect().height - toggle.getBoundingClientRect().height - 2) }
  const observer = new ResizeObserver(resize)
  for (const element of [area, summary, toggle]) observer.observe(element)
  resize()
  onCleanup(() => observer.disconnect())
}, { flush: 'post' })
watch(() => route.query.task, id => { if (typeof id === 'string') { downloads.selectedId = id; filter.value = 'all' } }, { immediate: true })
function exitPreview() { stopUpdatePreview(); if (route.query.updatePreview) void router.replace({ query: {} }) }
</script>
<template>
  <section class="page downloads-page">
    <header class="page-header"><div><h1 class="page-title">{{ t('updates.title') }}</h1><p class="page-desc">{{ t('updates.description') }}</p></div><WButton v-if="previewAvailable && !updatePreview.enabled" @click="previewCurrentClient"><FluentIcon glyph="play" :size="14" />{{ t('updates.previewStart') }}</WButton><WButton v-if="done.length" variant="subtle" @click="clearFinishedDownloads().catch(error => { state.error = error.message })">{{ t('updates.clearFinished') }}</WButton></header>
    <PageBody contained>
    <p v-if="downloads.error" class="muted" role="alert">{{ downloads.error }}</p>
    <div v-if="updatePreview.enabled" class="preview-toolbar"><div><span class="preview-label">{{ t('updates.preview') }}</span><span>{{ t('updates.previewBody') }}</span></div><div class="preview-controls"><WComboBox v-if="selected?.mode === 'preview'" :model-value="selected.stage" :options="stages" :aria-label="t('updates.scenario')" @update:model-value="setPreviewStage(selected!, $event as TaskStage)" /><WButton variant="subtle" @click="router.push('/home')">{{ t('updates.home') }}</WButton><WButton variant="subtle" @click="exitPreview">{{ t('updates.previewStop') }}</WButton></div></div>
    <div v-if="tasks.length" class="download-toolbar">
      <div class="download-tabs" role="group" :aria-label="t('updates.title')"><button v-for="tab in tabs" :key="tab" :aria-pressed="filter === tab" :class="{ selected: filter === tab }" @click="filter = tab">{{ t(`updates.${tab}`) }}<span>{{ count(tab) }}</span></button></div>
      <div class="download-speed"><FluentIcon glyph="download" :size="14" /><span>{{ t('updates.totalSpeed') }}</span><strong>{{ speed ? `${bytes(speed)}/s` : '—' }}</strong></div>
    </div>
    <div v-if="!tasks.length" class="empty-state"><FluentIcon glyph="download" :size="40" /><h2>{{ t('updates.empty') }}</h2><p>{{ t('updates.emptyBody') }}</p><WButton @click="router.push('/instances')">{{ t('live.manage') }}</WButton></div>
    <div v-else-if="!visible.length" class="filtered-empty">{{ t('updates.noResults') }}</div>
    <div v-else ref="workspace" class="download-workspace" :class="{ single: visible.length === 1 }">
      <div class="task-list" role="group" :aria-label="t('updates.all')"><button v-for="task in visible" :key="task.id" class="task-card" :class="{ selected: selected?.id === task.id }" :aria-pressed="selected?.id === task.id" @click="downloads.selectedId = task.id"><span class="task-icon"><FluentIcon :glyph="task.target.kind === 'launcher' ? 'refresh' : task.target.kind === 'client' ? 'gamepad' : 'library'" :size="22" /></span><span class="task-copy"><span class="task-kind">{{ t(`updates.${task.target.kind}`) }}</span><strong>{{ localized(task.target.nameLocalized ?? undefined) || task.target.name }}</strong><span v-if="task.target.toVersion" class="task-version"><template v-if="task.target.fromVersion">{{ task.target.fromVersion }} → </template>{{ task.target.toVersion }}</span></span><span v-if="visible.length > 1" class="task-stage" :class="task.stage">{{ t(downloadStageKey(task)) }}<span>{{ transferMetrics(task).percent == null ? '—' : `${Math.floor(transferMetrics(task).percent!)}%` }}</span></span><WProgressBar v-if="visible.length > 1" :value="transferMetrics(task).percent ?? 0" :aria-label="`${localized(task.target.nameLocalized ?? undefined) || task.target.name}: ${t('updates.total')}`" /></button></div>
      <section v-if="selected" class="task-detail" :aria-label="localized(selected.target.nameLocalized ?? undefined) || selected.target.name">
        <div ref="progress" class="detail-progress"><div class="detail-status" role="status"><span class="status-dot" :class="selected.stage" />{{ t(downloadStageKey(selected)) }}</div><UpdateProgress :task="selected" :selected-path="selectedPath || undefined" /><UpdateActions :task="selected" class="detail-actions" /></div>
        <button ref="detailsToggle" type="button" class="details-toggle" :aria-expanded="detailsExpanded" :aria-controls="detailsId" @click="detailsExpanded = !detailsExpanded">
          <span>{{ t(detailsExpanded ? 'updates.collapseDetails' : 'updates.expandDetails') }}</span>
          <FluentIcon glyph="chevronDown" :size="12" />
        </button>
        <div :id="detailsId" class="detail-collapse" :class="{ expanded: detailsExpanded }" :style="{ '--detail-height': `${detailHeight}px` }" :inert="!detailsExpanded" :aria-hidden="!detailsExpanded">
          <div class="detail-content">
            <div class="file-heading">
              <div class="detail-tabs" role="group" :aria-label="t('updates.details')">
                <button type="button" :aria-pressed="detailTab === 'files'" @click="detailTab = 'files'">{{ t('updates.files') }}<span>{{ selected.files.length }}</span></button>
                <button type="button" :aria-pressed="detailTab === 'source'" @click="detailTab = 'source'">{{ t('updates.source') }}</button>
              </div>
              <WButton v-if="selectedPath" variant="subtle" @click="selectedPath = ''">{{ t('updates.currentFile') }}</WButton>
            </div>
            <WContentTransition><PageBody :key="`${selected.id}:${detailTab}`" class="detail-body">
              <dl v-if="detailTab === 'source'" class="task-metadata"><div><dt>{{ t('updates.source') }}</dt><dd>{{ selected.target.sourceName }}<span>{{ selected.target.sourceKind === 'alist' ? 'AList' : 'HTTP' }}</span></dd></div><div><dt>{{ t('updates.directory') }}</dt><dd class="directory">{{ selected.target.directory }}</dd></div></dl>
              <div v-else class="file-list" role="group" :aria-label="t('updates.files')"><button v-for="file in selected.files" :key="file.path" class="file-row" :class="{ inspected: selectedPath === file.path }" :aria-pressed="selectedPath === file.path" :aria-label="`${t('updates.selectFile')}: ${file.path}`" @click="selectedPath = file.path"><FluentIcon :glyph="file.stage === 'verified' ? 'completed' : file.stage === 'failed' ? 'warning' : 'openFile'" :size="16" /><span class="file-name" :title="file.path">{{ file.path }}<small>{{ bytes(fileBytes(file)) }} / {{ bytes(file.size) }}</small></span><span class="file-state">{{ t(`updates.file_${file.stage}`) }}<small>{{ file.size == null ? '—' : `${Math.floor(percent(fileBytes(file), file.size) ?? 0)}%` }}</small></span></button></div>
            </PageBody></WContentTransition>
          </div>
        </div>
      </section>
    </div>
  </PageBody>
  </section>
</template>
<style scoped>
.downloads-page { max-width: 1520px; margin: 0 auto; }
.preview-toolbar, .download-toolbar { flex: none; }
.page-header { gap: 16px; flex-wrap: wrap; }
.preview-toolbar { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding: 12px 16px; border: 1px solid var(--card-stroke-default); border-radius: 8px; margin-bottom: 20px; background: var(--card-bg-default); font-size: 12px; color: var(--text-secondary); }
.preview-toolbar > div { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.preview-label { display: inline-block; flex: none; font-size: 11px; line-height: 20px; border: 1px solid var(--card-stroke-default); border-radius: 4px; padding: 0 7px; color: var(--text-secondary); }
.preview-controls :deep(.w-combobox) { min-width: 152px; font-size: 12px; }
.preview-controls :deep(.w-btn) { font-size: 12px; }
.download-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 20px; padding-bottom: 16px; }
.download-tabs { display: flex; flex-wrap: wrap; gap: 6px; }
.download-speed { display: flex; align-items: center; flex: none; gap: 8px; padding-right: 4px; color: var(--text-secondary); font-size: 12px; white-space: nowrap; }
.download-speed strong { min-width: 72px; color: var(--text-primary); font-weight: 500; text-align: right; font-variant-numeric: tabular-nums; }
.download-tabs > button { display: flex; align-items: center; gap: 8px; position: relative; border-radius: 5px; padding: 8px 13px; color: var(--text-secondary); font-size: 13px; }
.download-tabs > button:hover, .download-tabs > button.selected { background: var(--subtle-secondary); }
.download-tabs > button.selected { color: var(--text-primary); }
.download-tabs > button.selected::after { content: ''; position: absolute; height: 3px; width: 16px; bottom: 0; left: calc(50% - 8px); border-radius: 2px; background: var(--accent-default); }
.download-tabs span { font-size: 11px; color: var(--text-secondary); }
.download-workspace { flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(230px, 0.7fr) minmax(0, 1.5fr); gap: 20px; overflow: hidden; }
.task-list { display: flex; flex-direction: column; gap: 8px; overflow-y: auto; min-height: 0; padding: 2px; }
.task-card { flex: none; }
.task-card { display: grid; grid-template-columns: 40px minmax(0, 1fr); gap: 12px; padding: 18px; text-align: left; border: 1px solid var(--card-stroke-default); border-radius: 8px; background: var(--card-bg-default); }
.task-card:hover { background: var(--ctrl-fill-secondary); }
.task-card.selected { background: color-mix(in srgb, var(--accent-default) 5%, var(--card-bg-default)); border-color: color-mix(in srgb, var(--accent-default) 45%, var(--card-stroke-default)); }
.task-icon { width: 40px; height: 40px; display: grid; place-items: center; border-radius: 8px; color: var(--accent-text-primary); background: var(--subtle-secondary); }
.task-copy { min-width: 0; display: flex; flex-direction: column; gap: 3px; }
.task-kind, .task-version { color: var(--text-secondary); font-size: 11px; }
.task-copy strong { font-size: 13px; line-height: 20px; font-weight: 600; overflow-wrap: anywhere; }
.task-stage { display: flex; align-items: center; gap: 6px; grid-column: 1 / -1; color: var(--text-secondary); font-size: 12px; }
.task-stage > span { margin-left: auto; font-variant-numeric: tabular-nums; }
.task-stage.failed { color: var(--system-critical, #c42b1c); }
.task-card > :deep(.w-progress) { grid-column: 1 / -1; }
.task-detail { min-width: 0; min-height: 0; max-height: 100%; align-self: start; display: flex; flex-direction: column; border: 1px solid var(--card-stroke-default); border-radius: 8px; background: var(--card-bg-default); overflow: hidden; }
.detail-body { --page-inset-inline: 0px; --page-inset-bottom: 12px; }
.details-toggle { flex: none; display: flex; align-items: center; justify-content: space-between; gap: 16px; min-height: 44px; padding: 12px 24px; border-top: 1px solid var(--card-stroke-default); color: var(--text-secondary); font-size: 12px; text-align: left; transition: background-color 120ms linear; }
.details-toggle:hover { background: var(--subtle-secondary); color: var(--text-primary); }
.details-toggle:active { background: var(--subtle-tertiary); }
.details-toggle:focus-visible { outline-offset: -3px; }
.details-toggle .fluent-icon { transition: transform 240ms var(--ease-fast-out-slow-in); }
.details-toggle[aria-expanded='true'] .fluent-icon { transform: rotate(180deg); }
.detail-collapse { flex: none; height: 0; opacity: 0; overflow: hidden; transition: height 240ms var(--ease-fast-out-slow-in), opacity 180ms ease; }
.detail-collapse.expanded { height: var(--detail-height); opacity: 1; }
.detail-content { height: var(--detail-height); min-height: 0; display: flex; flex-direction: column; }
.file-heading { flex: none; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 4px 16px 8px; }
.detail-tabs { display: flex; align-items: center; gap: 6px; }
.detail-tabs button { position: relative; padding: 7px 10px; border-radius: 4px; color: var(--text-secondary); font-size: 12px; }
.detail-tabs button:hover { background: var(--subtle-secondary); }
.detail-tabs button[aria-pressed='true'] { background: var(--subtle-secondary); color: var(--text-primary); }
.detail-tabs button[aria-pressed='true']::after { content: ''; position: absolute; bottom: 0; left: calc(50% - 8px); width: 16px; height: 2px; border-radius: 2px; background: var(--accent-default); }
.detail-tabs span { margin-left: 8px; color: var(--text-secondary); }
.file-heading :deep(.w-btn) { min-height: 28px; font-size: 12px; }
.detail-progress { flex: none; padding: 18px 24px; }
.detail-status { display: flex; align-items: center; gap: 8px; margin-bottom: 20px; font-size: 13px; font-weight: 600; }
.status-dot { width: 6px; height: 6px; border-radius: 50%; background: var(--accent-default); }
.status-dot.failed { background: var(--system-critical, #c42b1c); }
.status-dot.paused, .status-dot.canceled { background: var(--text-tertiary); }
.detail-actions { margin-top: 18px; }
.task-metadata { margin: 0; padding: 16px 24px; display: flex; flex-direction: column; gap: 10px; }
.task-metadata > div { display: grid; grid-template-columns: 90px minmax(0, 1fr); gap: 12px; font-size: 12px; }
.task-metadata dt { color: var(--text-secondary); }
.task-metadata dd { margin: 0; overflow-wrap: anywhere; }
.task-metadata dd > span { margin-left: 10px; font-size: 10px; color: var(--text-secondary); border: 1px solid var(--card-stroke-default); border-radius: 3px; padding: 1px 4px; }
.directory { user-select: text; }
.file-list { padding: 8px 12px 0; }
.file-row { display: flex; align-items: center; gap: 12px; padding: 10px 12px; border-radius: 4px; width: 100%; text-align: left; }
.file-row:hover, .file-row.inspected { background: var(--subtle-secondary); }
.file-row > .fluent-icon { color: var(--text-secondary); flex: none; }
.file-name { flex: 1; min-width: 0; font-size: 12px; overflow-wrap: anywhere; }
.file-row small { display: block; font-size: 11px; margin-top: 2px; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.file-state { text-align: right; flex: none; color: var(--text-secondary); font-size: 11px; }
.filtered-empty { text-align: center; color: var(--text-secondary); padding: 80px 20px; }
@media (max-width: 1040px) { .download-workspace { grid-template-columns: minmax(200px, 0.65fr) minmax(0, 1.35fr); gap: 12px; } .task-card { padding: 14px; grid-template-columns: minmax(0, 1fr); gap: 8px; } .task-icon { display: none; } }
@media (max-width: 760px) { .download-workspace { grid-template-columns: minmax(0, 1fr); } .task-list { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); } .detail-progress { padding: 18px; } .task-metadata { padding: 16px 18px; } }
@media (max-width: 760px) {
  .download-workspace { grid-template-rows: auto minmax(0, 1fr); }
  .task-list { max-height: 100px; }
  .task-card { padding: 10px 12px; gap: 7px; }
  .task-copy .task-kind, .task-copy .task-version { display: none; }
  .task-copy strong { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .preview-toolbar { margin-bottom: 12px; }
}
@media (prefers-reduced-motion: reduce) { .detail-collapse, .details-toggle .fluent-icon, :deep(.w-progress-fill) { transition: none; } }
.detail-status { margin-bottom: 14px; }
.detail-actions { margin-top: 12px; }
.download-workspace.single { grid-template-columns: minmax(0, 1fr); grid-template-rows: auto minmax(0, 1fr); }
.download-workspace.single .task-list { grid-template-columns: minmax(0, 1fr); overflow: visible; max-height: none; }
.download-workspace.single .task-detail { border: 0; border-radius: 0; background: transparent; }
.download-workspace.single .detail-progress { padding-inline: 0; }
</style>
