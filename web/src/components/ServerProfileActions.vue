<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, type Server, type ServerProfileImport } from '../bridge'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const props = defineProps<{ server?: Server; game: string; disabled: boolean }>()
const emit = defineEmits<{ saved: [server: Server] }>()
const { t } = useI18n()
const busy = ref(false), error = ref('')
const pending = ref<{ text: string; inspection: ServerProfileImport }>()
let revision = 0
watch(() => [props.game, props.server?.id], () => { ++revision; pending.value = undefined; error.value = '' })
onBeforeUnmount(() => { ++revision })
function message(cause: unknown) { const text = (cause as Error).message; return text === 'server_profile_conflict' ? t('serverInfo.profileChanged') : text }
async function commit(text: string, expected: number, fingerprint?: string | null) {
  const server = await call('servers.importProfile', { text, game: props.game, expectedFingerprint: fingerprint ?? null })
  if (expected === revision) { pending.value = undefined; emit('saved', server) }
}
async function importProfile() {
  if (busy.value || props.disabled || !(inHost || demoMode?.enabled)) return
  const expected = ++revision, game = props.game
  busy.value = true; error.value = ''
  try {
    const text = await call('shell.pickServerProfile', { title: t('serverInfo.importProfile') })
    if (text === null || expected !== revision) return
    const inspection = await call('servers.inspectProfile', { text, game })
    if (expected !== revision) return
    if (inspection.exists && !inspection.identical) pending.value = { text, inspection }
    else await commit(text, expected)
  } catch (cause) { if (expected === revision) error.value = message(cause) }
  finally { busy.value = false }
}
async function overwrite() {
  const candidate = pending.value
  if (!candidate || busy.value) return
  busy.value = true; error.value = ''
  try { await commit(candidate.text, revision, candidate.inspection.fingerprint) }
  catch (cause) { pending.value = undefined; error.value = message(cause) }
  finally { busy.value = false }
}
async function exportProfile() {
  if (!props.server || busy.value || props.disabled || !(inHost || demoMode?.enabled)) return
  busy.value = true; error.value = ''
  try {
    const profile = await call('servers.exportProfile', { id: props.server.id })
    await call('shell.saveServerProfile', { ...profile, title: t('serverInfo.exportProfile') })
  } catch (cause) { error.value = message(cause) }
  finally { busy.value = false }
}
async function refreshProfile() {
  if (!props.server?.apiUrl || busy.value || props.disabled || !(inHost || demoMode?.enabled)) return
  busy.value = true; error.value = ''
  try { emit('saved', await call('servers.refreshProfile', { id: props.server.id })) }
  catch (cause) { error.value = message(cause) }
  finally { busy.value = false }
}
</script>
<template>
  <div class="profile-actions">
    <WButton v-if="server?.apiUrl" :disabled="disabled || busy || !(inHost || demoMode?.enabled)" @click="refreshProfile"><FluentIcon glyph="refresh" />{{ t('serverInfo.refreshProfile') }}</WButton>
    <WButton :disabled="disabled || busy || !server || !(inHost || demoMode?.enabled)" @click="exportProfile"><FluentIcon glyph="upload" />{{ t('serverInfo.exportProfile') }}</WButton>
    <WButton :disabled="disabled || busy || !(inHost || demoMode?.enabled)" @click="importProfile"><FluentIcon glyph="download" />{{ t('serverInfo.importProfile') }}</WButton>
  </div>
  <WInfoBar v-if="error" severity="error" @close="error = ''">{{ error }}</WInfoBar>
  <WDialog :open="!!pending" :title="t('serverInfo.overwriteProfileTitle')" :primary-text="t('serverInfo.overwriteProfile')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :close-disabled="busy" dismiss-on-backdrop @primary="overwrite" @close="pending = undefined">
    <p>{{ t('serverInfo.overwriteProfileBody', { name: pending?.inspection.server.name }) }}</p>
  </WDialog>
</template>
<style scoped>
.profile-actions { display: flex; flex-wrap: wrap; gap: 8px; padding-top: 16px; border-top: 1px solid var(--stroke-divider); }
.profile-actions > * { flex: 1; }
</style>
