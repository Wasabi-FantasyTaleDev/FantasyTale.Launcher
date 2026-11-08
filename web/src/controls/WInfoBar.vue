<script setup lang="ts">
// WinUI 3 InfoBar: severity banner with icon, title, message and an optional close.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FluentIcon from './FluentIcon.vue'
import type { GlyphName } from '../icons'

export type InfoBarSeverity = 'info' | 'success' | 'warning' | 'error'

const props = withDefaults(
  defineProps<{ severity?: InfoBarSeverity; title?: string; closable?: boolean; open?: boolean }>(),
  { severity: 'info', title: '', closable: true, open: true },
)
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()

const glyph = computed<GlyphName>(() => {
  switch (props.severity) {
    case 'success':
      return 'completed'
    case 'warning':
      return 'warning'
    case 'error':
      return 'errorBadge'
    default:
      return 'info'
  }
})
</script>

<template>
  <div v-if="open" class="w-infobar" :class="`w-infobar--${severity}`" :role="severity === 'error' || severity === 'warning' ? 'alert' : 'status'">
    <FluentIcon :glyph="glyph" :size="16" class="w-infobar-icon" />
    <div class="w-infobar-content">
      <span v-if="title" class="w-infobar-title">{{ title }}</span>
      <p class="w-infobar-message"><slot /></p>
      <slot name="action" />
    </div>
    <button v-if="closable" type="button" class="w-infobar-close" :aria-label="t('titlebar.close')" @click="emit('close')">
      <FluentIcon glyph="cancel" :size="16" />
    </button>
  </div>
</template>

<style scoped>
.w-infobar {
  display: flex;
  align-items: flex-start;
  box-sizing: border-box;
  min-width: 0;
  min-height: 48px;
  padding: 0 0 0 16px;
  border-radius: var(--radius-control);
  border: 1px solid var(--card-stroke-default);
  color: var(--text-primary);
  font-size: 14px;
  line-height: 20px;
}
.w-infobar--info {
  --ib-color: var(--fill-attention);
  background: var(--fill-attention-bg);
}
.w-infobar--success {
  --ib-color: var(--fill-success);
  background: var(--fill-success-bg);
}
.w-infobar--warning {
  --ib-color: var(--fill-caution);
  background: var(--fill-caution-bg);
}
.w-infobar--error {
  --ib-color: var(--fill-critical);
  background: var(--fill-critical-bg);
}
.w-infobar-icon {
  color: var(--ib-color);
  width: 16px;
  height: 20px;
  line-height: 20px;
  margin: 14px 14px 0 0;
  transform: translateY(2px);
}
.w-infobar-content {
  flex: 1;
  min-width: 0;
  padding: 14px 0;
  margin-right: 16px;
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 20px;
}
.w-infobar .w-infobar-title {
  display: block;
  font-size: inherit;
  font-weight: 600;
  line-height: 20px;
}
.w-infobar .w-infobar-message {
  margin: 0;
  font-size: inherit;
  font-weight: 400;
  line-height: 20px;
}
.w-infobar-close {
  flex: none;
  width: 38px;
  height: 38px;
  margin: 5px;
  border-radius: var(--radius-control);
  color: var(--text-secondary);
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.w-infobar-close:hover {
  background: var(--subtle-secondary);
}
.w-infobar-close:active {
  background: var(--subtle-tertiary);
}
</style>
