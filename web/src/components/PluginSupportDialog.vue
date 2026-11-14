<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { answerSupportOffer, supportPrompt as prompt } from '../plugins/supportOffers'
import WDialog from '../controls/WDialog.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import WInfoBar from '../controls/WInfoBar.vue'
const { t } = useI18n()
function choose(id: string, checked: boolean) {
  prompt.selected = checked ? [...new Set([...prompt.selected, id])] : prompt.selected.filter(value => value !== id)
}
</script>

<template>
  <WDialog :open="!!prompt.offer" :title="t('pluginPolicy.supportTitle', { name: localized(prompt.offer?.name) })"
    :primary-text="t('pluginPolicy.supportEnable')" :close-text="t('pluginPolicy.supportLater')"
    :primary-disabled="prompt.busy || !prompt.selected.length" :primary-busy="prompt.busy"
    @primary="answerSupportOffer(true)" @close="answerSupportOffer(false)">
    <div v-if="prompt.offer" class="support-content">
      <p>{{ t('pluginPolicy.supportBody', { name: localized(prompt.offer.name), version: prompt.offer.version }) }}</p>
      <WCheckBox v-for="client in prompt.offer.clients" :key="client.id" class="support-card"
        :model-value="prompt.selected.includes(client.id)" :disabled="prompt.busy" @update:model-value="choose(client.id, $event)">
        <span><strong>{{ client.name }}</strong><small>{{ client.build }}</small></span>
      </WCheckBox>
      <WInfoBar v-if="prompt.error" severity="error" :closable="false">{{ prompt.error }}</WInfoBar>
    </div>
  </WDialog>
</template>

<style scoped>
.support-content { display: grid; gap: 12px; }
.support-content p { margin: 0; }
.support-card { display: flex; align-items: center; gap: 14px; padding: 16px; border: 1px solid var(--card-stroke-default);
  border-radius: var(--radius-control); background: var(--card-bg-default); cursor: pointer; }
.support-card span { display: grid; gap: 4px; }
.support-card strong { font-weight: 600; }
.support-card small { color: var(--text-secondary); }
</style>
