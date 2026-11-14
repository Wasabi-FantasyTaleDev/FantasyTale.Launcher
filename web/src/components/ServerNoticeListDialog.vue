<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import MarkdownText from './MarkdownText.vue'
import type { ServerNotice } from '../bridge'
import { useScrollEdges } from '../scrollEdges'

// Every published notice of the server, newest first, as cards; opened with Show All. Notices shown here count as seen.
defineProps<{ open: boolean; notices: ServerNotice[]; loading?: boolean; error?: string }>()
const emit = defineEmits<{ close: [] }>()
const { t, locale } = useI18n()
const list = ref<HTMLElement>()
const { style: listEdges, measure: measureListEdges } = useScrollEdges(list)
function published(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(locale.value, { year: 'numeric', month: 'short', day: 'numeric' })
}
</script>

<template>
  <WDialog :open="open" wide :title="t('notices.allTitle')" :close-text="t('dialog.close')" @close="emit('close')">
    <div v-if="loading" class="notice-list-state"><WProgressRing :size="24" /></div>
    <p v-else-if="error" class="notice-list-state form-error" role="alert">{{ error }}</p>
    <p v-else-if="!notices.length" class="notice-list-state muted">{{ t('notices.none') }}</p>
    <div v-else ref="list" class="notice-list scroll-edges" :style="listEdges" @scroll.passive="measureListEdges">
      <article v-for="notice in notices" :key="notice.id" class="notice-card" :class="`is-${notice.level}`">
        <header>
          <span class="notice-icon" aria-hidden="true"><FluentIcon :glyph="notice.level === 'info' ? 'info' : 'warning'" :size="16" /></span>
          <div><h3>{{ notice.title }}</h3><span v-if="notice.publishedAt" class="notice-date">{{ published(notice.publishedAt) }}</span></div>
        </header>
        <MarkdownText :source="notice.body" class="notice-card-body" />
      </article>
    </div>
  </WDialog>
</template>

<style scoped>
.notice-list { display: grid; gap: 8px; max-height: calc(100dvh - 260px); overflow: hidden auto; }
.notice-list-state { display: grid; place-items: center; min-height: 120px; margin: 0; }
.notice-card { display: grid; gap: 10px; padding: 16px 20px; border-radius: var(--radius-overlay, 4px); background: var(--card-default, var(--solid-bg-tertiary)); border: 1px solid var(--stroke-card, var(--stroke-divider)); }
.notice-card header { display: flex; align-items: flex-start; gap: 12px; }
.notice-card header > div { flex: 1; min-width: 0; }
.notice-card h3 { margin: 0; font-size: 14px; font-weight: 600; line-height: 1.4; overflow-wrap: anywhere; }
.notice-date { color: var(--text-secondary); font-size: 12px; }
.notice-icon { display: grid; place-items: center; flex: none; width: 28px; height: 28px; border-radius: 50%; background: rgb(0 103 192 / .12); color: var(--accent-default); }
.is-warning .notice-icon { background: rgb(247 99 12 / .14); color: #c25106; }
.is-important .notice-icon { background: rgb(196 43 28 / .12); color: #c42b1c; }
.notice-card-body { padding-left: 40px; color: var(--text-secondary); font-size: 13px; }
.form-error { color: var(--fill-critical); }
</style>
