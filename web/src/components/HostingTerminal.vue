<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, hostingPreview, type HostingProfile, type HostingSession } from '../bridge'
import { refreshServers } from '../state'
import WButton from '../controls/WButton.vue'
const props = defineProps<{ profile: HostingProfile; session?: HostingSession }>()
const emit = defineEmits<{ changed: [] }>()
const { t } = useI18n()
const lines = ref<string[]>([]), command = ref(''), replies = ref<string[]>([]), busy = ref(false), view = ref<HTMLElement>()
let timer: ReturnType<typeof setTimeout> | undefined, disposed = false, revision = 0, hiddenLines = 0
function append(line: string) { replies.value = [...replies.value.slice(-127), line] }
async function refresh() {
  const expected = revision, session = props.session
  if (!session) { lines.value = []; return }
  try {
    const result = await call('hosting.log', { id: session.profileId, runId: session.runId })
    if (!disposed && expected === revision && props.session?.runId === session.runId) lines.value = result.slice(hiddenLines)
  } catch (e) { if (!disposed && expected === revision) append((e as Error).message) }
}
async function poll() { await refresh(); if (!disposed) timer = setTimeout(poll, 1200) }
async function send() {
  if (busy.value || !command.value.trim() || !(inHost || demoMode?.enabled || hostingPreview)) return
  const input = command.value.trim(), id = props.profile.id, current = props.session
  command.value = ''; append(`> ${input}`); busy.value = true
  try {
    switch (input.toLowerCase()) {
      case 'help': append(t('localHosting.help')); break
      case 'status': append(`${props.profile.name}: ${t(`hosting.${current?.state ?? 'stopped'}`)}`); break
      case 'clear': hiddenLines += lines.value.length; lines.value = []; replies.value = []; break
      case 'start': await call('hosting.start', { id }); break
      case 'stop': if (current) await call('hosting.stop', { id, runId: current.runId }); break
      case 'restart': if (current && ['starting', 'ready', 'stopping'].includes(current.state)) await call('hosting.stop', { id, runId: current.runId }); await call('hosting.start', { id }); break
      default: append(t('localHosting.unknown'))
    }
    emit('changed'); await refreshServers({ refreshSelectedServices: false }); await refresh()
  } catch (e) { append((e as Error).message) }
  finally { busy.value = false }
}
watch([() => props.profile.id, () => props.session?.runId], () => { revision++; hiddenLines = 0; lines.value = []; replies.value = []; void refresh() })
watch([lines, replies], async () => { await nextTick(); if (view.value) view.value.scrollTop = view.value.scrollHeight })
onMounted(() => { append(t('localHosting.help')); void poll() })
onBeforeUnmount(() => { disposed = true; revision++; clearTimeout(timer) })
</script>
<template>
  <section class="hosting-terminal" :aria-label="t('localHosting.terminal')"><div ref="view" class="terminal-output" role="log" aria-live="off"><pre>{{ [...lines, ...replies].join('\n') || t('hosting.noLogs') }}</pre></div><form class="terminal-input" @submit.prevent="send"><span aria-hidden="true">&gt;</span><input v-model="command" :disabled="busy || !(inHost || demoMode?.enabled || hostingPreview)" :maxlength="256" :aria-label="t('localHosting.input')" :placeholder="t('localHosting.input')" autocomplete="off" spellcheck="false" /><WButton :disabled="busy || !(inHost || demoMode?.enabled || hostingPreview) || !command.trim()" type="submit">{{ t('localHosting.send') }}</WButton></form></section>
</template>
<style scoped>
.hosting-terminal { flex: 1; min-height: 300px; display: flex; flex-direction: column; background: #0c0c0c; color: #d8d8d8; }
.terminal-output, .terminal-input input { font: 13px/1.65 'Cascadia Mono', Consolas, 'Microsoft YaHei UI', 'Microsoft JhengHei UI', monospace; }
.terminal-output { flex: 1; min-height: 0; overflow: auto; padding: 20px 24px; }.terminal-output pre { font: inherit; margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; user-select: text; }
.terminal-input { display: flex; align-items: center; gap: 12px; padding: 12px 20px; border-top: 1px solid #333; }.terminal-input input { flex: 1; min-width: 0; background: transparent; border: 0; outline: 0; color: inherit; font: 13px/1.65 'Cascadia Mono', Consolas, 'Microsoft YaHei UI', 'Microsoft JhengHei UI', monospace; padding: 6px; }.terminal-input input:focus-visible { outline: 1px solid #777; }.terminal-input :deep(.w-btn) { color: #eee; background: #292929; border: 1px solid #555; }
</style>
