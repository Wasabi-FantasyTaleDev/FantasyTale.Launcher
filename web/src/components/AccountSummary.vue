<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AccountProfile, GameSummary } from '../bridge'
import { selectedGame, state } from '../state'
import FluentIcon from '../controls/FluentIcon.vue'
import RankIcon from './RankIcon.vue'
import { accountLevel } from '../levels'
import { characterName } from '../accounts/identity'
const props = defineProps<{ compact?: boolean; profile?: AccountProfile | null; game?: GameSummary }>()
const account = computed(() => props.profile === undefined ? state.account : props.profile)
const game = computed(() => props.game ?? selectedGame.value)
const { t, te, locale } = useI18n()
const name = computed(() => account.value ? characterName(account.value) ?? t('service.noCharacter') : t('service.notSignedIn'))
const failedIcon = ref(false)
watch(() => account.value?.levelIconUrl, () => { failedIcon.value = false })
const fields = computed(() => (props.game ? props.game.profileFields : state.catalog?.profileFields ?? game.value?.profileFields) ?? [])
const hasIcon = computed(() => ((props.game ? props.game.levelIcon : state.catalog?.levelIcon ?? game.value?.levelIcon)) && account.value?.levelIconUrl && !failedIcon.value)
const rank = computed(() => accountLevel(game.value, account.value))
const localIcon = computed(() => ((props.game ? props.game.levelIcon : state.catalog?.levelIcon ?? game.value?.levelIcon)) && rank.value?.icon && game.value?.assetBase)
const rankTitle = computed(() => rank.value ? [t('service.level') + ' ' + rank.value.label, rank.value.badgeName, rank.value.name].filter(Boolean).join(' · ') : '')
const format = (value: number | bigint) => new Intl.NumberFormat(locale.value).format(value)
</script>
<template>
  <div class="account-summary" :class="{ compact }">
    <div class="account-identity"><span class="account-avatar" :class="{ 'has-rank': hasIcon || localIcon }"><img v-if="hasIcon" :src="account!.levelIconUrl!" :alt="t('service.level')" referrerpolicy="no-referrer" @error="failedIcon = true" /><RankIcon v-else-if="localIcon && rank?.icon" :icon="rank.icon" :asset-base="game!.assetBase!" :size="compact ? 24 : 32" :label="rankTitle" /><FluentIcon v-else glyph="contact" :size="22" /></span><div><span class="account-label">{{ t('service.currentAccount') }}</span><strong>{{ name }}</strong></div></div>
    <dl v-if="account" class="account-stats"><div v-for="key in fields" :key="key"><dt>{{ te(`service.${key}`) ? t(`service.${key}`) : key }}</dt><dd :title="key === 'level' ? rankTitle : account.stats[key] == null ? t('service.unknown') : undefined">{{ key === 'level' && rank ? rank.label : account.stats[key] == null ? '—' : format(account.stats[key]!) }}</dd></div></dl>
    <slot />
    <div v-if="!compact && rank?.experience != null" class="rank-experience"><div><span>{{ rank.name || `${t('service.level')} ${rank.label}` }}</span><span>{{ t('service.exp') }} {{ format(rank.experience) }}<template v-if="rank.next != null"> / {{ format(rank.next) }}</template></span></div><progress v-if="rank.progress != null && rank.next != null" :value="rank.progress" max="100" :aria-label="t('service.exp')" /></div>
  </div>
</template>
<style scoped>
.account-summary { display: flex; align-items: center; gap: 24px; flex-wrap: wrap; min-width: 0; }.account-identity { display: flex; align-items: center; gap: 14px; min-width: 120px; flex: 1; }.account-avatar { display: flex; align-items: center; justify-content: center; width: 40px; height: 40px; flex: none; border-radius: 50%; background: var(--subtle-secondary); color: var(--text-secondary); }.account-avatar > img { width: 32px; height: 32px; object-fit: contain; }.account-identity > div { display: flex; flex-direction: column; gap: 4px; }.account-label, dt { color: var(--text-secondary); font-size: 12px; }strong { font-weight: 600; overflow-wrap: anywhere; }.account-stats { display: flex; flex-wrap: wrap; gap: 24px; margin: 0; }dd { margin: 4px 0 0; font-variant-numeric: tabular-nums; }
.compact { gap: 10px; font-size: 12px; }
.compact .account-identity { flex: none; gap: 10px; min-width: 0; }
.compact .account-avatar { width: 18px; height: 24px; background: none; }
.account-avatar.has-rank { background: none; }
.compact .account-avatar.has-rank { width: auto; min-width: 24px; }
.compact .account-avatar > .fluent-icon { width: 18px; height: 18px; }
.compact .account-avatar > img { width: 24px; height: 24px; }
.compact .account-identity > div { flex-direction: row; align-items: center; gap: 12px; min-width: 0; }
.compact strong { font-weight: 400; }
.compact .account-stats { gap: 16px; margin-left: auto; }
.compact .account-stats > div { display: flex; align-items: center; gap: 6px; }
.compact dd { margin: 0; }
.rank-experience { flex-basis: 100%; color: var(--text-secondary); font-size: 12px; }
.rank-experience > div { display: flex; justify-content: space-between; gap: 16px; margin-bottom: 8px; }
.rank-experience progress { width: 100%; height: 4px; accent-color: var(--accent-default); }
</style>
