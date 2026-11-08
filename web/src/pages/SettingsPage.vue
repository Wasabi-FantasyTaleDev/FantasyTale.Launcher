<script setup lang="ts">
import PageBody from '../components/PageBody.vue'
import LauncherUpdateSettings from '../components/LauncherUpdateSettings.vue'
import LauncherSourceSettings from '../components/LauncherSourceSettings.vue'
import GitHubMirrorSettings from '../components/GitHubMirrorSettings.vue'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { languages, type Language } from '../i18n'
import { prefs } from '../theme'
import { clientUpdates, checkClientUpdates, saveUpdatePreferences, type ClientUpdatePreferences } from '../downloads/clientUpdates'
import { call } from '../bridge'
import { state, perform, refreshServices } from '../state'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WComboBox from '../controls/WComboBox.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WButton from '../controls/WButton.vue'
import WSelectorBar from '../controls/WSelectorBar.vue'
import WContentTransition from '../controls/WContentTransition.vue'

const { t, locale } = useI18n()
const route = useRoute()
const category = ref(['appearance', 'network', 'updates', 'about'].includes(String(route.query.tab)) ? String(route.query.tab) : 'appearance')
watch(() => route.query.tab, tab => { if (['appearance', 'network', 'updates', 'about'].includes(String(tab))) category.value = String(tab) })
const categories = computed(() => [
  { id: 'appearance', label: 'settings.group.appearance' },
  { id: 'network', label: 'service.network' },
  { id: 'updates', label: 'launcherUpdates.updates' },
  // { id: 'clients', label: 'settings.gameDirs' },
  { id: 'about', label: 'settings.group.about' },
].map(item => ({ key: item.id, label: t(item.label) })))
const themeOptions = computed(() => ['system', 'light', 'dark'].map(value => ({ value, label: t(`settings.themeName.${value}`) })))
const theme = computed({ get: () => prefs.theme, set: (value: string) => { prefs.theme = value as typeof prefs.theme } })
const systemAccent = computed({ get: () => prefs.systemAccent, set: (value: boolean) => { prefs.systemAccent = value; localStorage.setItem('systemAccent', value ? 'on' : 'off') } })
const developerMode = computed({ get: () => prefs.developerMode, set: (value: boolean) => { prefs.developerMode = value; localStorage.setItem('developerMode', value ? 'on' : 'off') } })
const names: Record<Language, string> = { 'zh-CN': '简体中文', 'zh-TW': '繁體中文', ko: '한국어', id: 'Bahasa Indonesia', vi: 'Tiếng Việt', th: 'ไทย', en: 'English' }
const languageOptions = languages.map(value => ({ value, label: names[value] }))
const language = computed({ get: () => locale.value, set: (value: string) => { locale.value = value; localStorage.setItem('language', value) } })
const ipOptions = computed(() => ['china', 'all', 'disabled'].map(value => ({ value, label: t(`service.${value}`) })))
async function setIpPreference(value: string) {
  try { state.ipPreference = (await call('settings.setNetwork', { ipPreference: value })).ipPreference; await refreshServices() }
  catch (error) { state.error = (error as Error).message }
}
async function preference(key: keyof ClientUpdatePreferences, value: boolean) {
  try { await saveUpdatePreferences({ ...clientUpdates.preferences, [key]: value }) }
  catch (error) { state.error = (error as Error).message }
}
const version = ref('')
onMounted(async () => { try { version.value = (await call('app.info')).version } catch (error) { state.error = (error as Error).message } })
</script>
<template>
  <section class="page settings-page"><header class="page-header"><h1 class="page-title">{{ t('settings.title') }}</h1></header>
    <WSelectorBar v-model="category" :items="categories" compact-indicator class="settings-categories" :aria-label="t('settings.title')" />
    <WContentTransition>
    <PageBody :key="category">
    <template v-if="category === 'appearance'">
    <h2 class="section-title">{{ t('settings.group.appearance') }}</h2><div class="settings-group">
      <WSettingsCard icon="personalize" :header="t('settings.theme')" :description="t('settings.themeDesc')"><WComboBox v-model="theme" :options="themeOptions" class="card-control" :aria-label="t('settings.theme')" /></WSettingsCard>
      <WSettingsCard icon="color" :header="t('settings.accent')" :description="t('settings.accentDesc')"><WToggleSwitch v-model="systemAccent" :aria-label="t('settings.accent')" /></WSettingsCard>
      <WSettingsCard icon="font" :header="t('settings.language')" :description="t('settings.languageDesc')"><WComboBox v-model="language" :options="languageOptions" class="card-control" :aria-label="t('settings.language')" /></WSettingsCard>
    </div>
    </template>
    <template v-else-if="category === 'network'">
    <h2 class="section-title">{{ t('service.network') }}</h2><div class="settings-group">
      <!-- Restore description with :description="t('service.ipDescription')" when needed. -->
      <WSettingsCard icon="globe" :header="t('service.ipPreference')" ><WComboBox :model-value="state.ipPreference" :options="ipOptions" class="card-control" :aria-label="t('service.ipPreference')" @update:model-value="setIpPreference" /></WSettingsCard>
      <GitHubMirrorSettings />
    </div><!-- <p class="settings-footnote">{{ t('service.serverOverride') }}</p> -->
    </template>
    <template v-else-if="category === 'updates'">
    <LauncherUpdateSettings />
    <LauncherSourceSettings />
    <h2 class="section-title client-updates-title">{{ t('clientUpdates.title') }}</h2><div class="settings-group">
      <WSettingsCard icon="refresh" :header="t('clientUpdates.autoCheck')" :description="t('clientUpdates.autoCheckDesc')"><WToggleSwitch :model-value="clientUpdates.preferences.autoCheck" :disabled="clientUpdates.saving" :aria-label="t('clientUpdates.autoCheck')" @update:model-value="preference('autoCheck', $event)" /></WSettingsCard>
      <WSettingsCard icon="puzzle" :header="t('clientUpdates.autoCheckPlugins')" :description="t('clientUpdates.autoCheckPluginsDesc')"><WToggleSwitch :model-value="clientUpdates.preferences.autoCheckPlugins" :disabled="clientUpdates.saving" :aria-label="t('clientUpdates.autoCheckPlugins')" @update:model-value="preference('autoCheckPlugins', $event)" /></WSettingsCard>
      <WSettingsCard icon="download" :header="t('clientUpdates.autoUpdatePlugins')" :description="t('clientUpdates.autoUpdatePluginsDesc')"><WToggleSwitch :model-value="clientUpdates.preferences.autoUpdatePlugins" :disabled="clientUpdates.saving" :aria-label="t('clientUpdates.autoUpdatePlugins')" @update:model-value="preference('autoUpdatePlugins', $event)" /></WSettingsCard>
      <WSettingsCard icon="download" :header="t('clientUpdates.removeOldPluginVersions')" :description="t('clientUpdates.removeOldPluginVersionsDesc')"><WToggleSwitch :model-value="clientUpdates.preferences.removeOldPluginVersions" :disabled="clientUpdates.saving" :aria-label="t('clientUpdates.removeOldPluginVersions')" @update:model-value="preference('removeOldPluginVersions', $event)" /></WSettingsCard>
      <WSettingsCard icon="download" :header="t('clientUpdates.autoDownload')" :description="t('clientUpdates.autoDownloadDesc')"><WToggleSwitch :model-value="clientUpdates.preferences.autoDownload" :disabled="clientUpdates.saving" :aria-label="t('clientUpdates.autoDownload')" @update:model-value="preference('autoDownload', $event)" /></WSettingsCard>
      <WSettingsCard icon="completed" :header="t('clientUpdates.check')" :description="clientUpdates.overview.clients.length ? t('clientUpdates.checked', { n: clientUpdates.overview.clients.length }) : ''"><WButton :disabled="clientUpdates.requesting || clientUpdates.overview.checking" @click="checkClientUpdates">{{ t(clientUpdates.requesting || clientUpdates.overview.checking ? 'clientUpdates.checking' : 'clientUpdates.check') }}</WButton></WSettingsCard>
    </div><p v-if="clientUpdates.error || clientUpdates.overview.error" role="alert" class="settings-footnote">{{ clientUpdates.error || t(`clientUpdates.${clientUpdates.overview.error}`) }}</p>
    </template>
    <!-- <template v-else-if="category === 'clients'">
    <h2 class="section-title">{{ t('settings.gameDirs') }}</h2><div class="settings-group">
      <WSettingsCard v-for="instance in state.instances" :key="instance.id" icon="hardDrive" :header="instance.name" :description="instance.build"><WButton @click="perform(() => call('instances.openFolder', { id: instance.id }))">{{ t('instances.menu.openFolder') }}</WButton></WSettingsCard>
      <WSettingsCard v-if="!state.instances.length" icon="hardDrive" :header="t('live.noInstance')"><WButton @click="$router.push('/instances')">{{ t('live.add') }}</WButton></WSettingsCard>
    </div>
    </template> -->
    <template v-else>
    <h2 class="section-title">{{ t('settings.group.about') }}</h2><div class="settings-group"><WSettingsCard icon="info" :header="t('app.title')" :description="t('settings.version', { v: version || '0.1.0' })" />
    <WSettingsCard icon="settings" :header="t('developerMode.title')" :description="t('developerMode.description')">
      <WToggleSwitch v-model="developerMode" :aria-label="t('developerMode.title')" />
    </WSettingsCard>
    </div>
    <!-- <p class="settings-footnote">{{ t('live.settingsNote') }}</p> -->
    </template>
  </PageBody>
    </WContentTransition>
  </section>
</template>
<style scoped>
.settings-categories { display: flex; flex: none; gap: 8px; margin-bottom: 20px; padding-bottom: 0; }
.settings-categories :deep(.w-selbar-item) { padding: 8px 16px; border-radius: 5px; font-weight: 400; }
.settings-categories :deep(.w-selbar-item.selected) { background: var(--subtle-secondary); }
.section-title { margin-top: 4px; }
.client-updates-title { margin-top: 24px; }
.settings-group { display: flex; flex-direction: column; gap: 4px; width: 100%; }
.card-control { width: 240px; flex: none; }
.settings-footnote { color: var(--text-tertiary); font-size: 12px; margin-top: 24px; }
</style>
