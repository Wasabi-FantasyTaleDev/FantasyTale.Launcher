<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import MarkdownText from './MarkdownText.vue'
import type { ServerNotice } from '../bridge'
import { useScrollEdges } from '../scrollEdges'

// Operator notices shown when the player starts the game. The caller passes only notices not seen before and marks
// them seen when this dialog closes, so each notice appears once.
const props = defineProps<{ notices: ServerNotice[] }>()
const emit = defineEmits<{ done: []; showAll: [] }>()
const { t, locale } = useI18n()
const index = ref(0)
watch(() => props.notices, () => { index.value = 0 })
const notice = computed(() => props.notices[index.value])
const last = computed(() => index.value >= props.notices.length - 1)
const body = ref<HTMLElement>()
const { style: bodyEdges, measure: measureBodyEdges } = useScrollEdges(body)
function next() { if (last.value) emit('done'); else index.value++ }
function published(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(locale.value, { year: 'numeric', month: 'short', day: 'numeric' })
}
</script>

<template>
  <WDialog :open="notices.length > 0" primary-first :title="t('notices.title')" :primary-text="t(last ? 'notices.start' : 'notices.next')" :secondary-text="t('notices.showAll')" :close-text="t('dialog.close')" @primary="next" @secondary="emit('showAll')" @close="emit('done')">
    <Transition name="notice-page" mode="out-in">
      <article v-if="notice" :key="notice.id" class="notice" :class="`is-${notice.level}`">
        <header>
          <span class="notice-icon" aria-hidden="true"><FluentIcon :glyph="notice.level === 'info' ? 'info' : 'warning'" :size="16" /></span>
          <div><h3>{{ notice.title }}</h3><span v-if="notice.publishedAt" class="notice-date">{{ published(notice.publishedAt) }}</span></div>
          <span v-if="notices.length > 1" class="notice-count">{{ index + 1 }} / {{ notices.length }}</span>
        </header>
        <div ref="body" class="notice-body scroll-edges" :style="bodyEdges" @scroll.passive="measureBodyEdges"><MarkdownText :source="notice.body" /></div>
      </article>
    </Transition>
  </WDialog>
</template>

<style scoped>
.notice { display: grid; gap: 14px; }
.notice header { display: flex; align-items: flex-start; gap: 12px; }
.notice header > div { flex: 1; min-width: 0; }
.notice h3 { margin: 0; font-size: 16px; font-weight: 600; line-height: 1.4; overflow-wrap: anywhere; }
.notice-date { color: var(--text-secondary); font-size: 12px; }
.notice-icon { display: grid; place-items: center; flex: none; width: 32px; height: 32px; border-radius: 50%; background: rgb(0 103 192 / .12); color: var(--accent-default); }
.is-warning .notice-icon { background: rgb(247 99 12 / .14); color: #c25106; }
.is-important .notice-icon { background: rgb(196 43 28 / .12); color: #c42b1c; }
.notice-count { flex: none; padding: 2px 10px; border-radius: 999px; background: var(--subtle-secondary); color: var(--text-secondary); font-size: 12px; }
.notice-body { margin: 0; max-height: calc(100dvh - 300px); overflow: hidden auto; }
.notice-page-enter-active, .notice-page-leave-active { transition: opacity 160ms ease, transform 160ms ease; }
.notice-page-enter-from { opacity: 0; transform: translateX(10px); }
.notice-page-leave-to { opacity: 0; transform: translateX(-10px); }
@media (prefers-reduced-motion: reduce) { .notice-page-enter-active, .notice-page-leave-active { transition: none; } }
</style>
