<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import WDialog from '../controls/WDialog.vue'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'

const props = defineProps<{ ready: boolean }>()
const { t } = useI18n()
const pending = ref(false), enabled = ref(true), saving = ref(false), error = ref('')
const open = computed(() => props.ready && pending.value)
let disposed = false, revision = 0, timer: ReturnType<typeof setTimeout> | undefined

async function refresh() {
  clearTimeout(timer)
  const expected = ++revision
  try {
    const result = await call('settings.network')
    if (disposed || expected !== revision) return
    if (result.suggestionPending && !pending.value) enabled.value = true
    pending.value = result.suggestionPending
    if (result.defaultPending) timer = setTimeout(refresh, 500)
  } catch {
    // A bridge failure must not interrupt startup or discard the persisted detection result.
  }
}
async function save(value = enabled.value) {
  if (saving.value) return
  const expected = ++revision
  clearTimeout(timer)
  saving.value = true; error.value = ''
  try {
    await call('settings.setNetwork', { githubMirror: value })
    if (!disposed && expected === revision) pending.value = false
  } catch (e) { if (!disposed && expected === revision) error.value = (e as Error).message }
  finally { if (!disposed && expected === revision) saving.value = false }
}
watch(open, visible => {
  if (!visible) return
  clearTimeout(timer)
  ++revision
  void call('settings.showGitHubMirrorSuggestion').catch(e => {
    if (!disposed) error.value = (e as Error).message
  })
}, { flush: 'post' })
onMounted(refresh)
onBeforeUnmount(() => { disposed = true; ++revision; clearTimeout(timer) })
</script>

<template>
  <WDialog :open="open" focus-panel close-disabled :title="t('launcherUpdates.githubMirror')" :primary-text="t('githubMirrors.confirm')" :primary-busy="saving" @primary="save()">
    <p class="mirror-suggestion-copy">{{ t('githubMirrors.suggestion') }}</p>
    <WSettingsCard icon="github" :header="t('launcherUpdates.githubMirror')">
      <WToggleSwitch v-model="enabled" :disabled="saving" :aria-label="t('launcherUpdates.githubMirror')" />
    </WSettingsCard>
    <p v-if="error" class="mirror-suggestion-error" role="alert">{{ error }}</p>
  </WDialog>
</template>

<style scoped>
.mirror-suggestion-copy { margin: 0 0 16px; line-height: 1.6; }
.mirror-suggestion-error { margin: 12px 0 0; color: var(--text-secondary); overflow-wrap: anywhere; }
</style>
