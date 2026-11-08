<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { prefs } from '../theme'
import { call } from '../bridge'
import { state } from '../state'
import PageBody from '../components/PageBody.vue'
import WSelectorBar from '../controls/WSelectorBar.vue'
import WContentTransition from '../controls/WContentTransition.vue'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WExpander from '../controls/WExpander.vue'
import NativeGameUiTests from '../components/NativeGameUiTests.vue'
import ResourceOverrideTests from '../components/ResourceOverrideTests.vue'

const { t } = useI18n()
const tab = ref('options')
const overlayTest = ref(false)
const passThroughOutside = ref(true)
const overlayBusy = ref(true)
onMounted(async () => {
  try {
    const settings = await call('settings.overlayTest')
    overlayTest.value = settings.enabled
    passThroughOutside.value = settings.passThroughOutside
  }
  catch (error) { state.error = (error as Error).message }
  finally { overlayBusy.value = false }
})
async function setOverlayTest(enabled: boolean, outside = passThroughOutside.value) {
  overlayBusy.value = true
  try {
    const settings = await call('settings.setOverlayTest', { enabled, passThroughOutside: outside })
    overlayTest.value = settings.enabled
    passThroughOutside.value = settings.passThroughOutside
  }
  catch (error) { state.error = (error as Error).message }
  finally { overlayBusy.value = false }
}
const tabs = computed(() => [
  { key: 'options', label: t('developerMode.tabs.options') },
  { key: 'sources', label: t('developerMode.tabs.sources') },
  { key: 'nativeUi', label: t('nativeUi.title') },
  { key: 'resources', label: t('resourceOverrides.title') },
])
</script>

<template>
  <section v-if="prefs.developerMode" class="page developer-page">
    <header class="page-header"><h1 class="page-title">{{ t('developerMode.navigation') }}</h1></header>
    <WSelectorBar v-model="tab" :items="tabs" compact-indicator class="developer-tabs" :aria-label="t('developerMode.navigation')" />
    <WContentTransition>
      <PageBody :key="tab" role="tabpanel" :aria-label="tabs.find(item => item.key === tab)?.label">
        <div v-if="tab === 'options'" class="settings-group">
        <WSettingsCard icon="briefcase" :header="t('developerMode.nsfw.title')" :description="t('developerMode.nsfw.description')">
          <WToggleSwitch v-model="prefs.nsfw" :aria-label="t('developerMode.nsfw.title')" />
        </WSettingsCard>
        <WExpander settings icon="openInNewWindow" :header="t('developerMode.overlayTest.title')" :description="t('developerMode.overlayTest.description')">
          <template #action>
          <WToggleSwitch :model-value="overlayTest" :disabled="overlayBusy" :aria-label="t('developerMode.overlayTest.title')" @update:model-value="setOverlayTest" />
          </template>
        <WSettingsCard :header="t('developerMode.overlayTest.passThrough')" :description="t('developerMode.overlayTest.passThroughDescription')">
          <WToggleSwitch :model-value="passThroughOutside" :disabled="overlayBusy" :aria-label="t('developerMode.overlayTest.passThrough')" @update:model-value="setOverlayTest(overlayTest, $event)" />
        </WSettingsCard>
        </WExpander>
        </div>
        <NativeGameUiTests v-else-if="tab === 'nativeUi'" />
        <ResourceOverrideTests v-else-if="tab === 'resources'" />
        <!-- TODO: Implement store-source management without storing credentials in web-view preferences. -->
        <p v-else class="developer-placeholder">{{ t('developerMode.pending') }}</p>
      </PageBody>
    </WContentTransition>
  </section>
</template>

<style scoped>
.settings-group { display: flex; flex-direction: column; gap: 4px; width: 100%; }
.developer-tabs { display: flex; flex: none; gap: 8px; margin-bottom: 20px; padding-bottom: 0; max-width: 100%; overflow-x: auto; }
.developer-tabs :deep(.w-selbar-item) { padding: 8px 16px; font-weight: 400; }
.developer-tabs :deep(.w-selbar-item.selected) { background: var(--subtle-secondary); }
.developer-placeholder { margin: 16px 0; color: var(--text-secondary); font-size: 14px; }
</style>
