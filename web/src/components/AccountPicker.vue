<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { refreshAccounts, selectAccount, state } from '../state'
import AccountSummary from './AccountSummary.vue'
import WFlyout from '../controls/WFlyout.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import { characterName } from '../accounts/identity'
import { call, type AccountProfile } from '../bridge'
import WDialog from '../controls/WDialog.vue'
import WProgressRing from '../controls/WProgressRing.vue'
const { t } = useI18n()
const open = ref(false), menu = ref<HTMLElement>()
const connecting = computed(() => !state.account && (state.accountRestoring || state.serverLoading))
const removal = ref<{ serverId: string; username: string; name: string } | null>(null)
const removing = ref(false), removalError = ref('')
let request = 0
function close() { ++request; open.value = false }
function clearRemoval() { removal.value = null; removalError.value = '' }
watch(() => state.selectedServerId, () => { close(); clearRemoval() }, { flush: 'sync' })
onBeforeUnmount(() => { close(); clearRemoval() })
async function toggle() {
  if (connecting.value) return
  if (open.value) { close(); return }
  const revision = ++request, serverId = state.selectedServerId
  try {
    await refreshAccounts()
    if (revision !== request || serverId !== state.selectedServerId) return
    open.value = true
    await nextTick()
    if (revision === request && open.value) menu.value?.querySelector<HTMLButtonElement>('[aria-checked="true"],button')?.focus()
  } catch (error) { if (revision === request && serverId === state.selectedServerId) state.error = (error as Error).message }
}
async function choose(username: string) { close(); await selectAccount(username) }
function requestRemoval(account: AccountProfile) {
  close()
  removalError.value = ''
  removal.value = { serverId: state.selectedServerId, username: account.username, name: characterName(account) ?? t('service.noCharacter') }
}
async function confirmRemoval() {
  const target = removal.value
  if (!target || removing.value || state.busy || target.serverId !== state.selectedServerId) return
  removing.value = true
  removalError.value = ''
  try {
    await call('accounts.logout', { serverId: target.serverId, username: target.username })
    if (removal.value === target) clearRemoval()
  } catch (error) {
    if (removal.value === target) removalError.value = (error as Error).message
  } finally {
    if (target.serverId === state.selectedServerId) {
      try { await refreshAccounts() }
      catch (error) { if (removal.value === target) removalError.value = (error as Error).message }
      if (removal.value === target && !state.accounts.some(entry => entry.account.username === target.username)) clearRemoval()
    }
    removing.value = false
  }
}
function move(event: KeyboardEvent) {
  if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const buttons = [...menu.value?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []]
  const current = buttons.indexOf(document.activeElement as HTMLButtonElement)
  const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (current + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length
  buttons[next]?.focus()
}
</script>
<template>
  <WFlyout :open="open" :min-width="300" @close="close">
    <template #anchor><button class="account-trigger" type="button" :disabled="state.busy || connecting || state.serverLoading || !state.selectedServerId" :aria-busy="connecting" :aria-label="t('multiAccount.choose')" aria-haspopup="menu" :aria-expanded="open" @click="toggle"><Transition name="account-connection" mode="out-in"><span v-if="connecting" key="connecting" class="account-connecting" role="status"><WProgressRing :size="16" /><span>{{ t('service.currentAccount') }}</span><span>{{ t('service.connecting') }}</span></span><AccountSummary v-else key="account" compact><FluentIcon glyph="chevronDown" :size="10" /></AccountSummary></Transition></button></template>
    <div ref="menu" class="account-menu" role="menu" :aria-label="t('multiAccount.choose')" @keydown="move">
      <div v-for="entry in state.accounts" :key="entry.account.username" class="account-row" :class="{ selected: entry.account.username === state.account?.username }" role="none">
        <button class="account-choice" type="button" role="menuitemradio" :aria-checked="entry.account.username === state.account?.username" :disabled="state.busy" @click="choose(entry.account.username)"><span>{{ characterName(entry.account) ?? t('service.noCharacter') }}</span></button>
        <button class="account-remove" type="button" role="menuitem" :disabled="state.busy || removing" :aria-label="t('multiAccount.deleteNamed', { name: characterName(entry.account) ?? t('service.noCharacter') })" :title="t('multiAccount.deleteRecord')" @click.stop="requestRemoval(entry.account)"><FluentIcon glyph="cancel" :size="12" /></button>
      </div>
      <button type="button" role="menuitem" class="add-account" @click="close(); state.authOpen = true"><FluentIcon glyph="add" :size="14" />{{ t('multiAccount.add') }}</button>
    </div>
  </WFlyout>
  <WDialog :open="!!removal" :title="t('multiAccount.deleteTitle')" :primary-text="t('multiAccount.deleteRecord')" :close-text="t('dialog.cancel')" :primary-disabled="removing || state.busy" :close-disabled="removing" focus-panel @primary="confirmRemoval" @close="clearRemoval">
    <p>{{ t('multiAccount.deleteBody', { name: removal?.name ?? '' }) }}</p>
    <p v-if="removalError" class="removal-error" role="alert">{{ removalError }}</p>
  </WDialog>
</template>
<style scoped>
.account-trigger { display: block; width: 100%; padding: 4px 6px; min-height: 32px; border-radius: var(--radius-control); color: var(--text-primary); text-align: left; }
.account-trigger:hover:not(:disabled), .account-menu button:hover { background: var(--subtle-secondary); }
.account-trigger:disabled { opacity: 0.5; }
.account-trigger[aria-busy="true"] { opacity: 1; }
.account-connecting { display: flex; align-items: center; gap: 12px; min-height: 24px; font-size: 12px; color: var(--text-secondary); }
.account-connecting > .w-ring { flex: none; margin-right: 2px; }
.account-connection-enter-active, .account-connection-leave-active { transition: opacity 140ms ease; }
.account-connection-enter-from, .account-connection-leave-to { opacity: 0; }
@media (prefers-reduced-motion: reduce) { .account-connection-enter-active, .account-connection-leave-active { transition-duration: 60ms; } }
.account-menu { min-width: 280px; max-height: 380px; overflow-y: auto; }
.account-menu button { position: relative; display: flex; align-items: center; gap: 4px; border-radius: var(--radius-control); color: var(--text-primary); text-align: left; transition: background-color var(--duration-fast) linear; }
.account-row { position: relative; display: flex; align-items: center; min-height: 40px; border-radius: var(--radius-control); }
.account-choice { flex: 1; min-width: 0; align-self: stretch; padding: 10px 10px 10px 16px; overflow-wrap: anywhere; }
.account-menu .account-remove { flex: none; width: 30px; height: 30px; margin: 0 5px; padding: 0; justify-content: center; color: var(--text-secondary); }
.account-menu .account-remove:hover:not(:disabled) { color: var(--text-primary); }
.account-menu button:disabled { opacity: 0.5; }
.account-menu .selected { background: var(--solid-bg-tertiary); }
.account-menu .selected::before { content: ''; position: absolute; left: 6px; top: 50%; width: 3px; height: 16px; border-radius: 1.5px; background: var(--accent); transform: translateY(-50%); pointer-events: none; }
.account-menu .add-account { width: 100%; padding: 10px 14px 10px 16px; gap: 10px; border-top: 1px solid var(--stroke-divider); margin-top: 6px; }
.removal-error { color: var(--system-critical); }
</style>
