<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost } from '../bridge'
import { localized } from '../i18n'
import { selectedServer, state, refreshAccounts } from '../state'
import WDialog from '../controls/WDialog.vue'
import ServerFailureNotice from './ServerFailureNotice.vue'
import WTextBox from '../controls/WTextBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import { accountSection, accountSectionsShown, defaultSection, loadServerStatus, pendingAuthSection, signInSections, serverStatus } from '../servers/sections'
const { t } = useI18n()
const mode = ref<'login' | 'register'>('login'), username = ref(''), password = ref(''), confirmation = ref(''), error = ref(''), notice = ref(''), busy = ref(false)
const failure = ref<Error>()
const fields = reactive<Record<string, string>>({})
const rememberPassword = ref(false)
// The section (Main, Test Server, Oversea Server...) whose account this dialog signs in to; hidden when there is no choice.
const section = ref<string | null>(null)
// Shared sections sign in through the default section, so only it and the separate sections are offered.
const sections = computed(() => signInSections(serverStatus.value))
const sectionOptions = computed(() => sections.value.map(entry => ({ value: entry.id, label: entry.name })))
async function loadSections() {
  const requested = pendingAuthSection.value
  pendingAuthSection.value = null
  try {
    const status = await loadServerStatus()
    // With every section sharing one sign-in, accounts carry no section at all.
    if (!accountSectionsShown(status)) section.value = null
    else if (requested && status.sections.some(entry => entry.id === requested)) section.value = accountSection(requested, status)
    else if (!section.value || !status.sections.some(entry => entry.id === section.value)) section.value = defaultSection(status)
  }
  catch { section.value = null }
}
const authContent = ref<HTMLElement>()
const contentHeight = ref<number>()
let contentObserver: ResizeObserver | undefined
watch(authContent, element => {
  contentObserver?.disconnect()
  contentHeight.value = undefined
  if (!element) return
  const measure = () => { contentHeight.value = element.getBoundingClientRect().height }
  measure()
  contentObserver = new ResizeObserver(measure)
  contentObserver.observe(element)
}, { flush: 'post' })
onBeforeUnmount(() => contentObserver?.disconnect())
const schema = computed(() => state.catalog?.registrationFields ?? [])
const canSubmit = computed(() => (inHost || !!demoMode?.enabled) && !!selectedServer.value?.apiUrl && !!state.catalog && !state.serverLoading && !!username.value.trim() && !!password.value && !busy.value &&
  (mode.value === 'login' || !!state.catalog.registrationEnabled && password.value === confirmation.value && schema.value.every(f => !f.required || (f.type === 'checkbox' ? fields[f.key] === 'true' : !!fields[f.key]?.trim()))))
let filledUsername = '', filledPassword = ''
function clearSecrets() { filledUsername = ''; filledPassword = ''; password.value = ''; confirmation.value = ''; for (const key of Object.keys(fields)) delete fields[key] }
let dialogRevision = 0
onBeforeUnmount(() => { ++dialogRevision; clearSecrets() })
let inputRevision = 0
watch([username, password, rememberPassword], () => { ++inputRevision }, { flush: 'sync' })
watch(username, value => {
  if (filledUsername && value !== filledUsername) {
    if (password.value === filledPassword) password.value = ''
    filledUsername = ''; filledPassword = ''; rememberPassword.value = false
  }
}, { flush: 'sync' })
async function loadRemembered() {
  if (!(inHost || !!demoMode?.enabled) || !state.authOpen || mode.value !== 'login') return
  const serverId = state.selectedServerId, revision = dialogRevision, draftRevision = inputRevision
  const current = () => state.authOpen && mode.value === 'login' && revision === dialogRevision && serverId === state.selectedServerId && draftRevision === inputRevision && !busy.value
  try {
    const saved = await call('accounts.rememberedLogin', { serverId, section: section.value ?? undefined })
    if (!current() || !saved) return
    username.value = saved.username; password.value = saved.password; rememberPassword.value = true
    filledUsername = saved.username; filledPassword = saved.password
  } catch { if (current()) error.value = t('service.rememberUnavailable') }
}
watch(() => state.authOpen, open => { ++dialogRevision; clearSecrets(); error.value = ''; notice.value = ''; username.value = ''; rememberPassword.value = false; mode.value = 'login'; if (open) { void loadRemembered(); void loadSections() } }, { flush: 'sync' })
watch(section, () => { ++dialogRevision; clearSecrets(); username.value = ''; rememberPassword.value = false; error.value = ''; notice.value = ''; void loadRemembered() })
watch(() => state.selectedServerId, () => { ++dialogRevision; state.authOpen = false; clearSecrets(); error.value = ''; notice.value = ''; username.value = '' }, { flush: 'sync' })
watch(mode, value => { ++dialogRevision; clearSecrets(); rememberPassword.value = false; error.value = ''; notice.value = ''; if (value === 'login') void loadRemembered() }, { flush: 'sync' })
async function submit() {
  if (!canSubmit.value) return
  const serverId = state.selectedServerId
  const revision = dialogRevision
  const current = () => revision === dialogRevision && serverId === state.selectedServerId && state.authOpen
  busy.value = true; error.value = ''; failure.value = undefined; notice.value = ''
  try {
    if (mode.value === 'register') {
      await call('accounts.register', { serverId, section: section.value ?? undefined, username: username.value.trim(), password: password.value, fields: { ...fields } })
      if (!current()) return
      mode.value = 'login'; clearSecrets(); notice.value = t('service.registered')
    } else {
      await call('accounts.login', { serverId, section: section.value ?? undefined, username: username.value.trim(), password: password.value, rememberPassword: rememberPassword.value })
      if (!current()) return
      await refreshAccounts()
      if (!current()) return
      state.authOpen = false
      clearSecrets()
    }
  } catch (e) {
    if (!current()) return
    failure.value = e as Error
    const message = (e as Error).message
    error.value = message === 'Sign-in expired or the account credentials were rejected.' ? t('service.authRejected')
      : message === 'The password is shorter than the server allows.' ? t('service.passwordTooShort') : message
    clearSecrets()
  }
  finally { busy.value = false }
}
</script>
<template>
  <WDialog :open="state.authOpen" :title="selectedServer?.name || t('service.login')" :primary-text="t(busy && mode === 'login' ? 'service.connecting' : `service.${mode}`)" :primary-busy="busy" :close-text="t('dialog.cancel')" :primary-disabled="!canSubmit" :close-disabled="busy" @primary="submit" @close="state.authOpen = false">
    <template v-if="accountSectionsShown()" #title-extra><WComboBox :model-value="section ?? undefined" class="auth-section" @update:model-value="section = $event ?? null" :options="sectionOptions" :disabled="busy" :aria-label="t('service.serverSection')" /></template>
    <div class="auth-tabs" role="tablist"><button v-for="tab in (['login', 'register'] as const)" :key="tab" type="button" role="tab" :aria-selected="mode === tab" :class="{ selected: mode === tab }" :disabled="busy" @click="mode = tab">{{ t(`service.${tab}`) }}</button></div>
    <div class="auth-body" :style="contentHeight === undefined ? undefined : { height: `${contentHeight}px` }"><div ref="authContent"><form :key="mode" class="form-stack" @submit.prevent="submit">
      <!-- <p v-if="!selectedServer?.apiUrl" class="muted">{{ t('service.noApi') }}</p> -->
      <label class="field"><span>{{ t('accounts.username') }}</span><WTextBox v-model="username" autocomplete="username" :maxlength="64" :disabled="busy" required /></label>
      <label class="field"><span>{{ t('service.password') }}</span><WTextBox v-model="password" type="password" :autocomplete="mode === 'login' ? 'current-password' : 'new-password'" :disabled="busy" required /></label>
      <WCheckBox v-if="mode === 'login'" v-model="rememberPassword" class="remember-password" :disabled="busy || !(inHost || !!demoMode?.enabled)">{{ t('service.rememberPassword') }}</WCheckBox>
      <template v-if="mode === 'register'">
        <label class="field"><span>{{ t('service.confirmPassword') }}</span><WTextBox v-model="confirmation" type="password" autocomplete="new-password" :disabled="busy" required /></label>
        <p v-if="confirmation && confirmation !== password" class="form-error">{{ t('service.passwordMismatch') }}</p>
        <label v-for="field in schema" :key="field.key" class="field"><span>{{ localized(field.label) }}{{ field.required ? ' *' : '' }}</span>
          <WComboBox v-if="field.type === 'select'" :model-value="fields[field.key] ?? ''" :options="field.choices.map(v => ({ value: v, label: v }))" :disabled="busy" :aria-label="localized(field.label)" @update:model-value="fields[field.key] = $event" />
          <WToggleSwitch v-else-if="field.type === 'checkbox'" :model-value="fields[field.key] === 'true'" :disabled="busy" :aria-label="localized(field.label)" @update:model-value="fields[field.key] = String($event)" />
          <WTextBox v-else :model-value="fields[field.key] ?? ''" :type="field.type" :maxlength="field.maxLength" :required="field.required" :disabled="busy" @update:model-value="fields[field.key] = $event" />
        </label>
      </template>
      <p v-if="mode === 'register' && state.catalog?.registrationEnabled === false" class="muted" role="status">{{ t('service.registrationDisabled') }}</p>
      <ServerFailureNotice v-if="error && failure" :error="failure" /><p v-else-if="error" class="form-error" role="alert">{{ error }}</p><p v-if="notice" class="muted" role="status">{{ notice }}</p>
      <!-- <p v-if="!inHost" class="muted">{{ t('live.browserOnly') }}</p> -->
      <button type="submit" hidden :disabled="!canSubmit" />
    </form></div></div>
  </WDialog>
</template>
<style scoped>
.auth-tabs { display: flex; gap: 24px; margin-bottom: 24px; border-bottom: 1px solid var(--stroke-divider); }.auth-tabs button { padding: 8px 2px 12px; position: relative; }.auth-tabs .selected::after { content: ''; position: absolute; height: 3px; left: 0; right: 0; bottom: -1px; border-radius: 2px; background: var(--accent-default); }.form-error { color: var(--fill-critical); font-size: 12px; overflow-wrap: anywhere; margin: 0; }
.remember-password { align-self: flex-start; }
.auth-section { min-width: 168px; }
.auth-body { overflow: hidden; transition: height 280ms cubic-bezier(0.2, 0.65, 0.25, 1); }
.auth-body .form-stack { padding: 8px 2px 4px; }
.auth-body form { animation: auth-content-in 220ms ease-out; }
@keyframes auth-content-in { from { opacity: 0; } to { opacity: 1; } }
@media (prefers-reduced-motion: reduce) {
  .auth-body { transition: none; }
  .auth-body form { animation: none; }
}
</style>
