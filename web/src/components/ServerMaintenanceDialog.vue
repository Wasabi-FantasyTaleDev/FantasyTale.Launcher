<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WButton from '../controls/WButton.vue'
import MarkdownText from './MarkdownText.vue'
import type { ServerStatus, ServerStatusEntry } from '../bridge'
import { sectionName, sectionsShown } from '../servers/sections'
import { useScrollEdges } from '../scrollEdges'

// Shown when the player starts the game while any server of the selected profile is under maintenance. Servers are
// settings-card rows grouped by section; the player continues on their own section, or plays in another section that
// is still open.
const props = defineProps<{ status: ServerStatus | null; section: string | null; switching?: boolean }>()
const emit = defineEmits<{ continue: []; close: []; switch: [section: string] }>()
const { t, locale } = useI18n()
const showSections = computed(() => sectionsShown(props.status))
const groups = computed(() => {
  const status = props.status
  if (!status) return []
  const sections = showSections.value ? status.sections : [{ id: '', name: '' }]
  return sections.map(section => ({
    id: section.id, name: section.name,
    // An automatic selector only routes to one of the listed servers, so it would repeat a row.
    servers: status.servers.filter(server => !server.automatic && (!showSections.value || server.section === section.id)).sort((a, b) => a.id - b.id),
  })).filter(group => group.servers.length).map(group => {
    // One maintenance explanation shared by a section's servers is shown once, under the section heading.
    const texts = new Set(group.servers.filter(server => server.state === 'maintenance').map(server => server.maintenance?.description ?? ''))
    const shared = texts.size === 1 && group.servers.filter(server => server.state === 'maintenance').length > 1 ? [...texts][0] : ''
    return { ...group, shared }
  })
})
const allDown = computed(() => !!props.status?.servers.length && props.status.servers.every(server => server.state !== 'online'))
const own = (server: ServerStatusEntry) => !showSections.value || !props.section || server.section === props.section
const canContinue = computed(() => !!props.status?.servers.some(server => server.state === 'online' && own(server)))
function when(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString(locale.value, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })
}
// The row's caption: players, the reason it is closed, or the maintenance period. The operator's explanation is
// Markdown and shown below it (once under the section heading when every closed server shares it).
function description(server: ServerStatusEntry) {
  if (server.state === 'online') return server.players != null && server.capacity ? t('maintenance.players', { players: server.players, capacity: server.capacity }) : t('maintenance.canEnter')
  if (server.state === 'offline') return t('maintenance.offlineHint')
  const start = when(server.maintenance?.start), end = when(server.maintenance?.end)
  return start && end ? t('maintenance.window', { start, end }) : end ? t('maintenance.until', { end }) : start ? t('maintenance.from', { start }) : ''
}
const explanation = (server: ServerStatusEntry, shared = '') =>
  server.state === 'maintenance' && server.maintenance?.description && server.maintenance.description !== shared ? server.maintenance.description : ''
const list = ref<HTMLElement>()
const { style: listEdges, measure: measureListEdges } = useScrollEdges(list)
watch([groups, () => props.section], measureListEdges, { flush: 'post' })
const icon = (server: ServerStatusEntry) => server.state === 'maintenance' ? 'repair' : server.state === 'online' ? 'globe' : 'blocked'
</script>

<template>
  <WDialog :open="!!status" wide :title="t(allDown ? 'maintenance.titleAll' : 'maintenance.title')" :primary-text="canContinue ? t('maintenance.continue') : ''" :close-text="canContinue ? '' : t('dialog.close')" :primary-disabled="switching" @primary="emit('continue')" @close="emit('close')">
    <WInfoBar :severity="allDown || !canContinue ? 'warning' : 'info'" :closable="false">{{ t(allDown ? 'maintenance.introAll' : canContinue ? 'maintenance.intro' : 'maintenance.introSection', { section: sectionName(section, status) }) }}</WInfoBar>
    <div ref="list" class="maintenance-groups scroll-edges" :style="listEdges" @scroll.passive="measureListEdges">
      <section v-for="group in groups" :key="group.id" class="maintenance-group">
        <header v-if="showSections" class="maintenance-section-head">
          <h3 class="type-body-strong">{{ group.name }}</h3>
          <Transition name="section-choice" mode="out-in">
            <span v-if="group.id === section" key="selected" class="maintenance-selected"><FluentIcon glyph="checkMark" :size="12" />{{ t('maintenance.selected') }}</span>
            <WButton v-else-if="group.servers.some(server => server.state === 'online')" key="play" :disabled="switching" @click="emit('switch', group.id)">{{ t('maintenance.playHere') }}</WButton>
          </Transition>
        </header>
        <MarkdownText v-if="group.shared" class="maintenance-shared type-caption" :source="group.shared" />
        <div v-for="server in group.servers" :key="server.id" class="maintenance-row" :class="`is-${server.state}`">
          <span class="maintenance-icon" aria-hidden="true"><FluentIcon :glyph="icon(server)" :size="20" /></span>
          <span class="maintenance-text">
            <span class="maintenance-name"><span class="type-body">{{ server.name }}</span><span class="maintenance-badge">{{ t('maintenance.state.' + server.state) }}</span></span>
            <span v-if="description(server)" class="type-caption maintenance-description">{{ description(server) }}</span>
            <MarkdownText v-if="explanation(server, group.shared)" class="type-caption maintenance-description" :source="explanation(server, group.shared)" />
          </span>
        </div>
      </section>
    </div>
  </WDialog>
</template>

<style scoped>
.maintenance-groups { display: grid; gap: 20px; margin-top: 12px; padding-top: 6px; max-height: calc(100dvh - 330px); overflow: hidden auto; }
/* The list grows with the window and scrolls only when the dialog would no longer fit. */
.maintenance-shared { margin: -2px 0 6px; color: var(--text-secondary); }
.maintenance-group { display: grid; gap: 4px; }
.maintenance-section-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; min-height: 32px; margin-bottom: 4px; }
.maintenance-section-head h3 { margin: 0; }
/* The signed-in section is marked selected; another open section offers Play here on its heading. */
.maintenance-selected { display: inline-flex; align-items: center; gap: 6px; padding: 4px 12px; border-radius: var(--radius-control, 4px); background: var(--accent-default); color: var(--text-on-accent, #fff); font-size: 12px; line-height: 20px; }
/* WinUI content transition: the old element fades out quickly, the new one fades in while sliding a few pixels into
   place on the Fluent decelerate curve (ControlFastAnimation 83/167 ms, ControlNormal 333 ms, spline 0,0,0,1). */
.section-choice-leave-active { transition: opacity 83ms linear; }
.section-choice-leave-to { opacity: 0; }
.section-choice-enter-active { transition: opacity 167ms linear, transform 333ms cubic-bezier(0, 0, 0, 1); }
.section-choice-enter-from { opacity: 0; transform: translateY(4px); }
@media (prefers-reduced-motion: reduce) { .section-choice-enter-active, .section-choice-leave-active { transition: none; } }
.maintenance-row { display: flex; align-items: center; gap: 16px; min-height: 64px; padding: 12px 16px 12px 20px; border-radius: var(--radius-overlay, 4px); background: var(--card-default, var(--solid-bg-tertiary)); border: 1px solid var(--stroke-card, var(--stroke-divider)); box-sizing: border-box; }
.maintenance-icon { display: grid; place-items: center; flex: none; width: 20px; color: var(--text-primary); }
.maintenance-text { display: grid; gap: 2px; flex: 1; min-width: 0; }
.maintenance-name { display: flex; align-items: center; gap: 8px; min-width: 0; flex-wrap: wrap; }
.maintenance-description { color: var(--text-secondary); overflow-wrap: anywhere; }
.markdown-text.maintenance-description, .markdown-text.maintenance-shared { line-height: inherit; }
/* The status is a small badge after the name: caution for maintenance, success when the server can be entered. */
.maintenance-badge { padding: 0 8px; border-radius: 999px; font-size: 12px; line-height: 20px; white-space: nowrap; background: var(--subtle-secondary); color: var(--text-secondary); }
.is-maintenance .maintenance-badge { background: var(--fill-caution-bg); color: var(--fill-caution); }
.is-online .maintenance-badge { background: var(--fill-success-bg); color: var(--fill-success); }
</style>
