<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { clientUpdates, installClientUpdate } from '../downloads/clientUpdates'
import { downloads } from '../downloads/live'
import { isRunning, state } from '../state'
import WButton from '../controls/WButton.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const props = defineProps<{ instanceId: string; compact?: boolean; hideUnmanaged?: boolean }>()
const { t } = useI18n()
const update = computed(() => clientUpdates.overview.clients.find(item => item.instanceId === props.instanceId))
const active = computed(() => downloads.tasks.some(task => task.target.instanceId === props.instanceId && !['completed', 'canceled'].includes(task.stage)))
// The client library omits the local-registration label while retaining actionable updates and errors.
const visible = computed(() => !active.value && update.value && update.value.status !== 'busy'
  && (!props.hideUnmanaged || update.value.status !== 'unmanaged')
  && (!props.compact || !['current', 'unmanaged', 'manual'].includes(update.value.status)))
async function install() {
  try { if (update.value) await installClientUpdate(update.value) }
  catch (error) { state.error = (error as Error).message }
}
</script>
<template>
  <Transition name="client-update">
    <div v-if="visible && update" class="client-update-status" :class="{ compact, available: update.status === 'available' }" role="status">
      <FluentIcon :glyph="update.status === 'available' ? 'download' : update.status === 'current' ? 'completed' : 'info'" :size="16" />
      <div class="update-label"><span>{{ t(`clientUpdates.${update.status}`) }}</span><small v-if="update.status === 'available' && update.version"><template v-if="update.installedVersion">{{ update.installedVersion }} → </template>{{ update.version }}</small></div>
      <WButton v-if="update.status === 'available'" :disabled="!!clientUpdates.installing || isRunning(instanceId)" @click="install">{{ t(isRunning(instanceId) ? 'clientUpdates.endGame' : clientUpdates.installing === instanceId ? 'install.starting' : 'clientUpdates.update') }}</WButton>
    </div>
  </Transition>
</template>
<style scoped>
.client-update-status { display: flex; align-items: center; gap: 10px; font-size: 12px; color: var(--text-secondary); }
.client-update-status.compact { min-height: 52px; padding: 10px 24px; border-top: 1px solid var(--card-stroke-default); }
.client-update-status.available > .fluent-icon { color: var(--accent-text-primary); }
.update-label { min-width: 0; display: flex; flex-direction: column; gap: 2px; }
.compact .update-label { flex: 1; flex-direction: row; align-items: baseline; gap: 16px; }
.update-label small { font-size: 11px; color: var(--text-tertiary); }
.client-update-status :deep(.w-btn) { font-size: 12px; }
.client-update-enter-active, .client-update-leave-active { transition: opacity 180ms ease, transform 220ms var(--ease-fast-out-slow-in); }
.client-update-enter-from, .client-update-leave-to { opacity: 0; transform: translateY(5px); }
@media (prefers-reduced-motion: reduce) { .client-update-enter-active, .client-update-leave-active { transition: none; } }
</style>
