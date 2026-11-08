<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type MirrorOverview, type MirrorResult, type NetworkPreferences } from '../bridge'
import WExpander from '../controls/WExpander.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WComboBox from '../controls/WComboBox.vue'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'
import WProgressRing from '../controls/WProgressRing.vue'
const { t } = useI18n()
const preferences = ref<NetworkPreferences>({ ipPreference: 'china', gitHubMirror: false, gitHubMirrorSource: 'auto', defaultPending: false, suggestionPending: false })
const overview = ref<MirrorOverview>({ providers: [], results: [] })
const loading = ref(true), saving = ref(false), testing = ref(false), dialog = ref(false), error = ref('')
let disposed = false, poll: ReturnType<typeof setTimeout> | undefined, defaultsPoll: ReturnType<typeof setTimeout> | undefined
let preferenceRevision = 0, testRevision = 0
const options = computed(() => [{ value: 'auto', label: t('githubMirrors.auto') }, ...overview.value.providers.map(provider => ({ value: provider.id, label: provider.name }))])
const rows = computed(() => overview.value.providers.map(provider => ({ ...provider, result: overview.value.results.find(result => result.id === provider.id) }))
  .sort((a, b) => testing.value ? 0 : (a.result?.state === 'available' ? 0 : 1) - (b.result?.state === 'available' ? 0 : 1) || (b.result?.bytesPerSecond ?? 0) - (a.result?.bytesPerSecond ?? 0)))
async function loadPreferences() {
  const expected = ++preferenceRevision
  try {
    const result = await call('settings.network')
    if (disposed || expected !== preferenceRevision) return
    preferences.value = result
    if (result.defaultPending) defaultsPoll = setTimeout(loadPreferences, 500)
  } catch (e) { if (!disposed && expected === preferenceRevision) error.value = (e as Error).message }
}
async function save(change: { githubMirror?: boolean; githubMirrorSource?: string }) {
  const expected = ++preferenceRevision
  saving.value = true; error.value = ''; clearTimeout(defaultsPoll)
  try { const result = await call('settings.setNetwork', change); if (!disposed && expected === preferenceRevision) preferences.value = result }
  catch (e) { if (!disposed && expected === preferenceRevision) error.value = (e as Error).message }
  finally { if (!disposed && expected === preferenceRevision) saving.value = false }
}
async function refreshResults() {
  const expected = testRevision
  try { const result = await call('settings.githubMirrors'); if (!disposed && expected === testRevision) overview.value = result }
  catch (e) { if (!disposed && expected === testRevision) error.value = (e as Error).message }
  if (!disposed && testing.value && expected === testRevision) poll = setTimeout(refreshResults, 400)
}
async function testMirrors() {
  dialog.value = true
  if (testing.value) return
  ++testRevision
  testing.value = true; error.value = ''
  overview.value.results = overview.value.providers.map(provider => ({ id: provider.id, state: 'pending', milliseconds: null, bytesPerSecond: null }))
  void refreshResults()
  try { const results = await call('settings.testGitHubMirrors'); if (!disposed) overview.value.results = results }
  catch (e) { if (!disposed) error.value = (e as Error).message }
  finally { if (!disposed) { ++testRevision; testing.value = false; clearTimeout(poll); await refreshResults() } }
}
function speed(result: MirrorResult) { return `${((result.bytesPerSecond ?? 0) / 1048576).toFixed(2)} MB/s` }
onMounted(async () => {
  await Promise.all([loadPreferences(), refreshResults()]); if (!disposed) loading.value = false
})
onBeforeUnmount(() => { disposed = true; clearTimeout(poll); clearTimeout(defaultsPoll) })
</script>
<template>
  <WExpander settings icon="github" :header="t('launcherUpdates.githubMirror')">
    <template #action><WProgressRing v-if="loading || preferences.defaultPending" :size="18" /><WToggleSwitch :model-value="preferences.gitHubMirror" :disabled="loading || saving" :aria-label="t('launcherUpdates.githubMirror')" @update:model-value="save({ githubMirror: $event })" /></template>
    <div class="mirror-choice"><span>{{ t('githubMirrors.provider') }}</span><div class="mirror-actions"><WComboBox :model-value="preferences.gitHubMirrorSource" :options="options" :disabled="loading || saving" :aria-label="t('githubMirrors.provider')" @update:model-value="save({ githubMirrorSource: $event })" /><WButton :disabled="loading" @click="testMirrors">{{ t('githubMirrors.test') }}</WButton></div></div>
    <p v-if="error && !dialog" class="mirror-error" role="alert">{{ error }}</p>
  </WExpander>
  <WDialog :open="dialog" dismiss-on-backdrop :title="t('githubMirrors.testTitle')" :close-text="t('titlebar.close')" @close="dialog = false">
    <div class="mirror-results">
      <article v-for="row in rows" :key="row.id" class="mirror-result">
        <span>{{ row.name }}</span>
        <div class="mirror-status"><WProgressRing v-if="testing && (!row.result || ['pending','testing'].includes(row.result.state))" :size="18" />
          <template v-else-if="row.result?.state === 'available'"><span>{{ speed(row.result) }}</span><small>{{ row.result.milliseconds }} ms</small></template>
          <span v-else class="unavailable">{{ t('githubMirrors.' + (row.result?.state ?? 'pending')) }}</span>
          <WButton :variant="preferences.gitHubMirror && preferences.gitHubMirrorSource === row.id ? 'accent' : 'standard'" :disabled="saving || preferences.gitHubMirror && preferences.gitHubMirrorSource === row.id" @click="save({ githubMirror: true, githubMirrorSource: row.id })">{{ t(preferences.gitHubMirror && preferences.gitHubMirrorSource === row.id ? 'githubMirrors.using' : 'githubMirrors.use') }}</WButton>
        </div>
      </article>
    </div>
    <p v-if="error" class="mirror-error" role="alert">{{ error }}</p>
    <div class="mirror-test-footer"><WButton :disabled="testing" @click="testMirrors">{{ t(testing ? 'githubMirrors.testing' : 'githubMirrors.retest') }}</WButton></div>
  </WDialog>
</template>
<style scoped>
.mirror-choice { display: flex; align-items: center; justify-content: space-between; gap: 16px; min-height: 68px; padding: 12px 0; }
.mirror-actions { display: flex; align-items: center; gap: 8px; min-width: 0; }
.mirror-actions > :first-child { width: 240px; max-width: 34vw; }
.mirror-results { display: flex; flex-direction: column; gap: 6px; max-height: min(420px, 55vh); overflow-y: auto; }
.mirror-result { display: flex; align-items: center; justify-content: space-between; gap: 16px; min-height: 54px; padding: 10px 14px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--card-bg-secondary); }
.mirror-result > span { overflow-wrap: anywhere; }
.mirror-status { display: flex; align-items: center; gap: 10px; flex: none; font-variant-numeric: tabular-nums; }
.mirror-status small, .unavailable { color: var(--text-secondary); }
.mirror-test-footer { display: flex; justify-content: flex-end; padding-top: 16px; }
.mirror-error { color: var(--text-secondary); overflow-wrap: anywhere; }
</style>
