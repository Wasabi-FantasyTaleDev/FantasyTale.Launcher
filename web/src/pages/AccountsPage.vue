<script setup lang="ts">
import PageBody from '../components/PageBody.vue'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { call, type AccountSessionList, type Server } from '../bridge'
import { refreshAccounts, selectAccount, selectGame, state, perform } from '../state'
import AccountSummary from '../components/AccountSummary.vue'
import ServerFailureNotice from '../components/ServerFailureNotice.vue'
import { loadAccountGroups } from '../accounts/load'
import { accountExpiryDelay, liveAccountSessions } from '../accounts/expiry'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WDialog from '../controls/WDialog.vue'
const { t } = useI18n()
type AccountGroup = { server: Server; sessions: AccountSessionList; error?: Error }
const filter = ref(''), groups = ref<AccountGroup[]>([])
const remove = ref<{ server: Server; username: string } | null>(null)
const options = computed(() => [{ value: '', label: t('multiAccount.allServers') }, ...state.servers.map(server => ({ value: server.id, label: server.name }))])
const visible = computed(() => groups.value.filter(group => !filter.value || filter.value === group.server.id))
let request = 0, contextRevision = 0, stopped = false
watch([() => state.selectedGameId, () => state.selectedServerId], () => { ++contextRevision }, { flush: 'sync' })
let expiryTimer: ReturnType<typeof setTimeout> | undefined
function expireGroups() { groups.value = groups.value.map(group => ({ ...group, sessions: liveAccountSessions(group.sessions) })) }
watch(groups, value => {
  clearTimeout(expiryTimer)
  const delay = accountExpiryDelay(value.flatMap(group => group.sessions.accounts))
  if (delay !== null) expiryTimer = setTimeout(expireGroups, delay)
})
onMounted(() => { window.addEventListener('focus', expireGroups) })
onBeforeUnmount(() => { stopped = true; ++request; ++contextRevision; clearTimeout(expiryTimer); window.removeEventListener('focus', expireGroups) })
async function refresh() {
  if (stopped) return
  const revision = ++request
  try {
    const results = await loadAccountGroups(state.servers, async (server): Promise<AccountGroup> => {
      try { return { server, sessions: await call('accounts.list', { serverId: server.id }) } }
      catch (error) {
        const previous = groups.value.find(group => group.server.id === server.id && group.server.game === server.game && group.server.apiUrl === server.apiUrl)
        return { server, sessions: previous?.sessions ?? { current: null, accounts: [] }, error: error as Error }
      }
    }, () => request === revision)
    if (request === revision && results) groups.value = results.map(group => ({ ...group, sessions: liveAccountSessions(group.sessions) }))
  } catch (error) { if (request === revision) state.error = (error as Error).message }
}
watch([() => state.servers, () => state.account, () => state.authOpen], () => { void refresh() }, { immediate: true })
async function choose(server: Server, username?: string) {
  if (stopped || state.busy) return
  if (state.selectedGameId !== server.game) selectGame(server.game)
  if (state.selectedGameId !== server.game) return
  state.selectedServerId = server.id
  const revision = ++contextRevision
  const current = () => !stopped && revision === contextRevision && state.selectedServerId === server.id
  if (username) await selectAccount(username)
  else { await nextTick(); if (current()) state.authOpen = true }
  if (current()) await refresh()
}
async function refreshCurrent(server: Server) {
  if (stopped) return
  const revision = contextRevision
  await perform(async () => {
    try { await call('accounts.refresh', { serverId: server.id }) }
    catch (error) { if (!stopped && revision === contextRevision) throw error }
    finally {
      if (!stopped) {
        if (revision === contextRevision && state.selectedServerId === server.id) await refreshAccounts()
        await refresh()
      }
    }
  })
}
async function signOut() {
  const target = remove.value
  if (stopped || !target) return
  const revision = contextRevision
  if (await perform(async () => {
    try { await call('accounts.logout', { serverId: target.server.id, username: target.username }) }
    catch (error) { if (!stopped && revision === contextRevision && remove.value === target) throw error }
    finally {
      if (!stopped) {
        if (state.selectedServerId === target.server.id) await refreshAccounts()
        await refresh()
        if (remove.value === target) remove.value = null
      }
    }
  }) && !stopped && remove.value === target) remove.value = null
}
</script>
<template>
  <section class="page">
    <header class="page-header"><h1 class="page-title">{{ t('accounts.title') }}</h1><WComboBox v-model="filter" :options="options" :disabled="state.busy" :aria-label="t('service.selectServer')" class="server-picker" /></header>
    <PageBody>
      <section v-for="group in visible" :key="group.server.id" class="account-section">
        <header class="group-header"><div><h2>{{ group.server.name }}</h2><span class="muted">{{ localized(state.games.find(game => game.id === group.server.game)?.name) }}</span></div><div class="group-actions"><WButton v-if="group.error" :disabled="state.busy" @click="refresh">{{ t('live.refresh') }}</WButton><WButton :disabled="state.busy" @click="choose(group.server)">{{ t('multiAccount.add') }}</WButton></div></header>
        <ServerFailureNotice v-if="group.error" class="group-failure" :error="group.error" />
        <p v-if="!group.error && !group.sessions.accounts.length" class="surface empty-accounts muted">{{ t('multiAccount.empty') }}</p>
        <article v-for="entry in group.sessions.accounts" :key="entry.account.username" class="surface account-card" :class="{ current: group.sessions.current?.username === entry.account.username }">
          <div class="account-content"><AccountSummary :profile="entry.account" :game="state.games.find(game => game.id === group.server.game)" /></div>
          <div class="account-actions"><template v-if="group.sessions.current?.username === entry.account.username"><span class="current-label">{{ t('multiAccount.current') }}</span><WButton :disabled="state.busy" @click="refreshCurrent(group.server)">{{ t('live.refresh') }}</WButton></template><WButton v-else :disabled="state.busy" @click="choose(group.server, entry.account.username)">{{ t('multiAccount.use') }}</WButton><WButton :disabled="state.busy" @click="remove = { server: group.server, username: entry.account.username }">{{ t('multiAccount.end') }}</WButton></div>
        </article>
      </section>
    </PageBody>
    <WDialog :open="!!remove" :title="t('multiAccount.removeTitle')" :primary-text="t('multiAccount.end')" :close-text="t('dialog.cancel')" :primary-disabled="state.busy" :close-disabled="state.busy" @primary="signOut" @close="remove = null"><p>{{ t('multiAccount.removeBody') }}</p></WDialog>
  </section>
</template>
<style scoped>
.server-picker { width: 280px; }
.account-section + .account-section { margin-top: 28px; }
.group-header { display: flex; align-items: center; justify-content: space-between; gap: 20px; margin-bottom: 12px; }
.group-header h2 { font-size: 16px; font-weight: 600; margin: 0 0 4px; }
.group-header .muted { font-size: 12px; }
.group-actions { display: flex; align-items: center; gap: 8px; }
.group-failure { margin-bottom: 12px; }
.account-card { display: flex; align-items: center; gap: 24px; padding: 20px 24px; margin-bottom: 8px; }
.account-card.current { border-color: color-mix(in srgb, var(--accent) 36%, var(--card-stroke-default)); }
.account-content { min-width: 0; flex: 1; }
.account-actions { display: flex; align-items: center; gap: 12px; flex: none; }
.current-label { color: var(--accent); font-size: 12px; }
.empty-accounts { padding: 24px; }
</style>
