<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, type Server } from '../bridge'
import { perform, refreshServers, selectedGame, state } from '../state'
import { languages } from '../i18n'
import { descriptionLanguages } from '../serverDescription'
import ServerProfileActions from './ServerProfileActions.vue'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WButton from '../controls/WButton.vue'
import WSelectorBar from '../controls/WSelectorBar.vue'
import WAnimatedSize from '../controls/WAnimatedSize.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import WComboBox from '../controls/WComboBox.vue'
const props = defineProps<{ open: boolean; server?: Server }>()
const emit = defineEmits<{ close: []; saved: [server: Server] }>()
const { t, locale } = useI18n()
const serverId = ref('')
const name = ref(''), apiUrl = ref(''), host = ref(''), port = ref(''), preferIp = ref<boolean | null>(null), useHttps = ref(false)
const tab = ref('connection')
const descriptionLanguage = ref(locale.value), descriptions = ref<Record<string, string>>({}), fallbackMotd = ref('')
const summaries = ref<Record<string, string>>({}), fallbackSummary = ref('')
const icons = ref<Record<string, string>>({}), galleries = ref<Record<string, string[]>>({}), fallbackIcon = ref(''), fallbackGallery = ref<string[]>([])
const languageOptions = computed(() => [...new Set([...languages, ...Object.keys(descriptions.value), ...Object.keys(summaries.value), ...Object.keys(icons.value), ...Object.keys(galleries.value)])].map(value => ({ value, label: descriptionLanguages[value] ?? value })))
const motd = computed({ get: () => descriptions.value[descriptionLanguage.value] ?? descriptions.value[descriptionLanguage.value.split('-')[0]!] ?? '', set: (value: string) => { descriptions.value[descriptionLanguage.value] = value } })
const summary = computed({ get: () => summaries.value[descriptionLanguage.value] ?? summaries.value[descriptionLanguage.value.split('-')[0]!] ?? '', set: (value: string) => { summaries.value[descriptionLanguage.value] = value } })
const iconUrl = computed({ get: () => icons.value[descriptionLanguage.value] ?? icons.value[descriptionLanguage.value.split('-')[0]!] ?? '', set: (value: string) => { icons.value[descriptionLanguage.value] = value } })
const images = computed({ get: () => (galleries.value[descriptionLanguage.value] ?? galleries.value[descriptionLanguage.value.split('-')[0]!] ?? []).join('\n'), set: (value: string) => { galleries.value[descriptionLanguage.value] = value.split(/\r?\n/).map(url => url.trim()).filter(Boolean) } })
const picking = ref(false), artworkError = ref('')
const locked = computed(() => state.busy || picking.value)
const iconFieldId = useId(), galleryFieldId = useId(), descriptionFieldId = useId(), summaryFieldId = useId()
const browserPicker = ref<HTMLInputElement>(), pickerTarget = ref<'icon' | 'gallery'>('icon')
let pickerLanguage = '', pickerGame = ''
const browserUrls: string[] = []
let artworkRevision = 0
const tabs = computed(() => [{ key: 'connection', label: t('serverInfo.connectionConfig') }, { key: 'information', label: t('serverInfo.information') }])
const ipOn = computed(() => preferIp.value ?? state.ipPreference !== 'disabled')
const idError = computed(() => {
  const id = serverId.value
  if (id && !/^[A-Za-z0-9_-]+$/.test(id)) return t('serverInfo.idInvalid')
  if (/^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i.test(id)) return t('serverInfo.idReserved')
  const effectiveId = id || generatedId.value
  if (!props.server && effectiveId && state.servers.some(server => server.id.toLowerCase() === effectiveId.toLowerCase())) return t('serverInfo.idExists')
  return ''
})
const generatedId = computed(() => {
  let address = host.value.trim(), selectedPort = port.value
  if (!address && apiUrl.value) {
    try { const api = new URL(apiUrl.value); address = api.hostname.replace(/^\[|\]$/g, ''); selectedPort = api.port || (api.protocol === 'https:' ? '443' : '80') } catch { return '' }
  }
  return address && selectedPort ? `${address.replace(/[^A-Za-z0-9_-]/g, '_')}_${Number(selectedPort)}` : ''
})
const valid = computed(() => !idError.value && (!!serverId.value || !!generatedId.value) && !!name.value.trim() && (!host.value.trim() || /^\d+$/.test(port.value) && +port.value > 0 && +port.value <= 65535))
watch(() => props.open, open => {
  ++artworkRevision; picking.value = false; artworkError.value = ''
  browserUrls.splice(0).forEach(url => URL.revokeObjectURL(url))
  if (!open) return
  serverId.value = props.server?.id ?? ''
  name.value = props.server?.name ?? ''; apiUrl.value = props.server?.apiUrl ?? ''
  host.value = props.server?.endpoint?.host ?? ''; port.value = String(props.server?.endpoint?.port ?? '')
  preferIp.value = props.server?.preferIp ?? null
  useHttps.value = props.server?.useHttps ?? false
  tab.value = 'connection'; descriptionLanguage.value = locale.value
  descriptions.value = { ...props.server?.presentation?.motdTranslations }
  summaries.value = { ...props.server?.presentation?.summaryTranslations }
  fallbackMotd.value = props.server?.presentation?.motd ?? ''
  fallbackSummary.value = props.server?.presentation?.summary ?? ''
  if (!Object.keys(descriptions.value).length && fallbackMotd.value) {
    descriptions.value[locale.value] = fallbackMotd.value; fallbackMotd.value = ''
  }
  if (!Object.keys(summaries.value).length && fallbackSummary.value) {
    summaries.value[locale.value] = fallbackSummary.value; fallbackSummary.value = ''
  }
  icons.value = { ...props.server?.presentation?.iconUrlTranslations }
  galleries.value = Object.fromEntries(Object.entries(props.server?.presentation?.imagesTranslations ?? {}).map(([language, gallery]) => [language, [...gallery]]))
  fallbackIcon.value = props.server?.presentation?.iconUrl ?? ''; fallbackGallery.value = [...props.server?.presentation?.images ?? []]
  if (!Object.keys(icons.value).length && fallbackIcon.value) { icons.value[locale.value] = fallbackIcon.value; fallbackIcon.value = '' }
  if (!Object.keys(galleries.value).length && fallbackGallery.value.length) { galleries.value[locale.value] = fallbackGallery.value; fallbackGallery.value = [] }
})
watch([() => state.selectedGameId, descriptionLanguage], () => { ++artworkRevision; picking.value = false })
onBeforeUnmount(() => { ++artworkRevision; browserUrls.forEach(url => URL.revokeObjectURL(url)) })
function applyArtwork(target: 'icon' | 'gallery', references: string[], language: string) {
  if (!references.length) return
  if (target === 'icon') icons.value[language] = references[0]!
  else galleries.value[language] = [...new Set([...(galleries.value[language] ?? galleries.value[language.split('-')[0]!] ?? []), ...references])]
}
async function chooseArtwork(target: 'icon' | 'gallery') {
  if (locked.value) return
  artworkError.value = ''
  if (!(inHost || !!demoMode?.enabled)) {
    pickerTarget.value = target
    pickerLanguage = descriptionLanguage.value; pickerGame = state.selectedGameId
    await nextTick()
    browserPicker.value?.click()
    return
  }
  const expected = ++artworkRevision, game = state.selectedGameId, language = descriptionLanguage.value
  picking.value = true
  try {
    const references = await call('shell.importServerImages', { multiple: target === 'gallery', title: t(target === 'icon' ? 'serverInfo.chooseIcon' : 'serverInfo.chooseImages') })
    if (expected === artworkRevision && props.open && state.selectedGameId === game) applyArtwork(target, references, language)
  } catch (error) {
    if (expected === artworkRevision && props.open) artworkError.value = (error as Error).message === 'server_image_format_unsupported' ? t('serverInfo.unsupportedImage') : (error as Error).message
  } finally { if (expected === artworkRevision) picking.value = false }
}
function browserFilesSelected(event: Event) {
  const input = event.target as HTMLInputElement
  if (props.open && state.selectedGameId === pickerGame) {
    const urls = Array.from(input.files ?? [], file => URL.createObjectURL(file))
    browserUrls.push(...urls)
    applyArtwork(pickerTarget.value, urls, pickerLanguage)
  }
  input.value = ''
}
async function imported(server: Server) {
  state.selectedServerId = server.id; await refreshServers(); emit('saved', server); emit('close')
}
async function save() {
  if (!valid.value || locked.value || !(inHost || !!demoMode?.enabled)) return
  const motdTranslations = Object.fromEntries(Object.entries(descriptions.value).map(([language, text]) => [language, text.trim()]).filter(([, text]) => text))
  const summaryTranslations = Object.fromEntries(Object.entries(summaries.value).map(([language, text]) => [language, text.trim()]).filter(([, text]) => text))
  const iconUrlTranslations = Object.fromEntries(Object.entries(icons.value).map(([language, url]) => [language, url.trim()]).filter(([, url]) => url))
  const imagesTranslations = Object.fromEntries(Object.entries(galleries.value).map(([language, gallery]) => [language, gallery.map(url => url.trim()).filter(Boolean)] as const).filter(([, gallery]) => gallery.length))
  const hasTranslations = [motdTranslations, summaryTranslations, iconUrlTranslations, imagesTranslations].some(map => Object.keys(map).length)
  const presentation = fallbackMotd.value || fallbackSummary.value || hasTranslations || fallbackIcon.value || fallbackGallery.value.length ? {
    motd: fallbackMotd.value, motdTranslations, summary: fallbackSummary.value, summaryTranslations,
    iconUrl: fallbackIcon.value || null, iconUrlTranslations, images: fallbackGallery.value, imagesTranslations,
  } : null
  const saved = await perform(async () => {
    const server = await call('servers.save', { id: props.server?.id ?? serverId.value, createOnly: !props.server, game: selectedGame.value?.id, name: name.value.trim(), apiUrl: apiUrl.value.trim() || null,
      endpoint: host.value.trim() ? { host: host.value.trim(), port: +port.value } : null, bundledInstances: props.server?.bundledInstances ?? false, preferIp: preferIp.value, useHttps: useHttps.value, presentation })
    state.selectedServerId = server.id; await refreshServers()
    emit('saved', server)
  })
  if (saved) emit('close')
}
</script>
<template>
  <WDialog dismiss-on-backdrop :open="open" :title="t(server ? 'service.editServer' : 'service.addServer')" :primary-text="t('live.save')" :close-text="t('dialog.cancel')" :primary-disabled="!valid || locked || !(inHost || !!demoMode?.enabled)" :close-disabled="locked" @primary="save" @close="emit('close')">
    <WSelectorBar v-model="tab" class="server-tabs" :items="tabs" compact-indicator :aria-label="t(server ? 'service.editServer' : 'service.addServer')" />
    <WAnimatedSize><Transition name="server-form">
    <div v-if="tab === 'connection'" key="connection" class="form-stack server-form-panel" role="tabpanel" :aria-label="t('serverInfo.connectionConfig')">
      <label class="field"><span>{{ t('service.serverName') }}</span><WTextBox v-model="name" :maxlength="128" :disabled="locked" /></label>
      <label class="field"><span>{{ t('serverInfo.serverId') }}</span><WTextBox v-model="serverId" :placeholder="generatedId || t('serverInfo.idAutomatic')" :disabled="locked || !!server" :aria-invalid="!!idError" /><small v-if="idError" class="id-error" role="alert">{{ idError }}</small></label>
      <label class="field"><span>{{ t('service.apiUrl') }}</span><WTextBox v-model="apiUrl" type="url" placeholder="https://server.example" :maxlength="2048" :disabled="locked" /></label>
      <div class="endpoint-fields"><label class="field"><span>{{ t('live.host') }}</span><WTextBox v-model="host" :disabled="locked" /></label><label class="field port"><span>{{ t('live.port') }}</span><WTextBox v-model="port" type="number" :min="1" :max="65535" :disabled="locked" /></label></div>
      <div class="ip-row"><div><strong>{{ t('service.ipPreference') }}</strong><p class="muted">{{ preferIp === null ? t('service.followGlobal') : t(ipOn ? 'service.on' : 'service.off') }}</p></div><WToggleSwitch :model-value="ipOn" :disabled="locked" :aria-label="t('service.ipPreference')" @update:model-value="preferIp = $event" /></div>
      <div class="ip-row"><div><strong>{{ t('service.useHttps') }}</strong><p class="muted">{{ t('service.useHttpsDescription') }}</p></div><WToggleSwitch :model-value="useHttps" :disabled="locked" :aria-label="t('service.useHttps')" @update:model-value="useHttps = $event" /></div>
      <!-- <p class="muted">{{ t('service.ipDescription') }}</p> --><!-- <p class="muted">{{ t('service.serverOverride') }}</p> -->
      <WButton v-if="preferIp !== null" variant="subtle" :disabled="locked" @click="preferIp = null">{{ t('service.followGlobal') }}</WButton>
      <!-- <p v-if="!inHost" class="muted">{{ t('live.browserOnly') }}</p> -->
    </div>
    <div v-else key="information" class="form-stack server-form-panel" role="tabpanel" :aria-label="t('serverInfo.information')">
      <!-- <p class="optional-hint">{{ t('serverInfo.optionalInformation') }}</p> -->
      <div class="artwork-heading content-language"><span>{{ t('serverInfo.descriptionLanguage') }}</span><WComboBox v-model="descriptionLanguage" :options="languageOptions" :disabled="locked" :aria-label="t('serverInfo.descriptionLanguage')" /></div>
      <div class="field description-field"><label :for="summaryFieldId">{{ t('serverInfo.shortIntroduction') }}</label><Transition name="description-language" mode="out-in"><textarea :id="summaryFieldId" :key="descriptionLanguage" v-model="summary" class="server-textarea" rows="2" :placeholder="t('serverInfo.summaryPlaceholder')" :disabled="locked" /></Transition></div>
      <div class="field"><label :for="descriptionFieldId">{{ t('serverInfo.fullIntroduction') }}</label><Transition name="description-language" mode="out-in"><textarea :id="descriptionFieldId" :key="descriptionLanguage" v-model="motd" class="server-textarea" rows="4" :placeholder="t('serverInfo.descriptionPlaceholder')" :disabled="locked" /></Transition></div>
      <div class="field"><label :for="iconFieldId">{{ t('serverInfo.iconUrl') }}</label><div class="artwork-field"><WTextBox :id="iconFieldId" v-model="iconUrl" placeholder="https://" :disabled="locked" /><WButton :disabled="locked" :aria-label="t('serverInfo.chooseIcon')" @click="chooseArtwork('icon')">{{ t('serverInfo.selectFile') }}</WButton></div></div>
      <div class="field"><div class="artwork-heading"><label :for="galleryFieldId">{{ t('serverInfo.galleryUrls') }}</label><WButton :disabled="locked" :aria-label="t('serverInfo.chooseImages')" @click="chooseArtwork('gallery')">{{ t('serverInfo.selectFile') }}</WButton></div><textarea :id="galleryFieldId" v-model="images" class="server-textarea" rows="3" placeholder="https://" :disabled="locked" /><small class="optional-hint">{{ t('serverInfo.galleryHint') }}</small></div>
      <ServerProfileActions :server="server" :game="state.selectedGameId" :disabled="locked" @saved="imported" />
    </div>
    </Transition></WAnimatedSize>
    <input v-if="!(inHost || !!demoMode?.enabled)" ref="browserPicker" type="file" hidden accept=".png,.jpg,.jpeg,.webp,.gif,.bmp,.ico" :multiple="pickerTarget === 'gallery'" @change="browserFilesSelected" />
    <WInfoBar v-if="artworkError" severity="error" @close="artworkError = ''">{{ artworkError }}</WInfoBar>
  </WDialog>
</template>
<style scoped>
.server-tabs { width: 100%; padding-bottom: 8px; margin-bottom: 18px; border-bottom: 1px solid var(--stroke-divider); }.server-tabs :deep(.w-selbar-item.selected) { font-weight: 400; }
.server-form-panel { padding: 2px; }
.artwork-field { display: flex; align-items: center; gap: 8px; }.artwork-field > .w-textbox { flex: 1; min-width: 0; }.artwork-field > .w-btn { flex: none; }
.artwork-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.content-language .w-flyout-anchor { width: 180px; max-width: 55%; }
.description-language-enter-active, .description-language-leave-active { transition: opacity 100ms linear; }.description-language-enter-from, .description-language-leave-to { opacity: 0; }
.id-error { color: var(--fill-critical); font-size: 12px; }
.optional-hint { margin: 0; font-size: 12px; color: var(--text-secondary); line-height: 1.6; }
.server-textarea { width: 100%; box-sizing: border-box; resize: vertical; min-height: 76px; padding: 8px 11px; border: 1px solid var(--ctrl-stroke-default); border-bottom-color: var(--ctrl-stroke-secondary); border-radius: var(--radius-control); background: var(--ctrl-fill-default); color: var(--text-primary); font: inherit; font-size: 14px; line-height: 1.6; }
.server-textarea:focus { outline: none; border-bottom: 2px solid var(--accent-default); background: var(--ctrl-fill-input-active); }
.server-form-enter-active { transition: opacity 180ms linear, transform 220ms cubic-bezier(0.1,0.9,0.2,1); }.server-form-leave-active { position: absolute; inset: 0 0 auto; width: 100%; pointer-events: none; transition: opacity 120ms linear; }.server-form-enter-from { opacity: 0; transform: translateY(6px); }.server-form-leave-to { opacity: 0; }
@media (prefers-reduced-motion: reduce) { .server-form-enter-active,.server-form-leave-active,.description-language-enter-active,.description-language-leave-active { transition: none; }.server-form-enter-from { transform: none; } }

.endpoint-fields { display: flex; gap: 12px; }.endpoint-fields > .field { flex: 1; }.endpoint-fields .port { max-width: 100px; }
.ip-row { display: flex; align-items: center; justify-content: space-between; gap: 24px; padding-top: 8px; }.ip-row strong { font-weight: 400; }.ip-row p { margin: 4px 0 0; }
</style>
