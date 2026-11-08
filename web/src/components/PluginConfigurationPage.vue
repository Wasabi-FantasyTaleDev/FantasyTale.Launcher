<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { OptionValue, PluginOption } from '../bridge'
import { isDark } from '../theme'

const props = defineProps<{ html: string; options: PluginOption[]; values: Record<string, OptionValue>; context?: Record<string, unknown> }>()
const emit = defineEmits<{ change: [values: Record<string, OptionValue>] }>()
const { locale } = useI18n()
const frame = ref<HTMLIFrameElement>()
// Start at the CSS minimum so first measurement cannot undo an oversized placeholder.
const frameHeight = ref(160)
const contentSized = ref(false)
// A unique opaque origin keeps package scripts away from the launcher bridge.
const policy = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; form-action 'none'; base-uri 'none'; frame-src 'none'"
function document() { return `<meta http-equiv="Content-Security-Policy" content="${policy}">${props.html}` }
function initialize() {
  // Reactive option definitions are Vue proxies, which postMessage cannot clone.
  const message = { type: 'fantasytale:configuration', schema: 1, language: locale.value, theme: isDark() ? 'dark' : 'light', options: props.options, values: { ...props.values }, context: props.context ?? {} }
  frame.value?.contentWindow?.postMessage(JSON.parse(JSON.stringify(message)), '*')
}
function receive(event: MessageEvent) {
  if (event.source !== frame.value?.contentWindow || event.origin !== 'null') return
  const message = event.data
  if (!message || typeof message !== 'object' || message.schema !== 1) return
  if (message.type === 'fantasytale:ready') { initialize(); return }
  if (message.type === 'fantasytale:resize') {
    if (Number.isSafeInteger(message.height) && message.height > 0) {
      frameHeight.value = message.height
      contentSized.value = true
    }
    return
  }
  if (message.type !== 'fantasytale:change' || !message.values || typeof message.values !== 'object' || Array.isArray(message.values)) return
  const next = { ...props.values }
  const entries = Object.entries(message.values)
  if (entries.length > props.options.length) return
  for (const [key, value] of entries) {
    const option = props.options.find(o => o.key === key)
    if (!option) return
    const valid = option.type === 'bool' ? typeof value === 'boolean'
      : option.type === 'int' ? typeof value === 'number' && Number.isSafeInteger(value) && value >= option.minimum && value <= option.maximum
      : typeof value === 'string' && new TextEncoder().encode(value).length <= option.maxLength && (option.type !== 'enum' || option.values.includes(value))
    if (!valid) return
    next[key] = value as OptionValue
  }
  emit('change', next)
}
onMounted(() => window.addEventListener('message', receive))
onBeforeUnmount(() => window.removeEventListener('message', receive))
watch([() => props.values, () => props.context, locale, isDark], initialize, { deep: true })
watch(() => props.html, () => { contentSized.value = false; frameHeight.value = 160 })
</script>

<template>
  <iframe ref="frame" class="plugin-configuration-page" :class="{ 'content-sized': contentSized }" :scrolling="contentSized ? 'no' : 'auto'" :style="{ height: `${frameHeight}px`, colorScheme: isDark() ? 'dark' : 'light' }" :srcdoc="document()" sandbox="allow-scripts" referrerpolicy="no-referrer" :title="$t('pluginConfiguration.custom')" @load="initialize" />
</template>

<style scoped>
.plugin-configuration-page { display: block; width: 100%; min-height: 160px; max-height: min(420px, 55vh); border: 0; border-radius: 8px; background: transparent; }
.plugin-configuration-page.content-sized { max-height: none; }
</style>
