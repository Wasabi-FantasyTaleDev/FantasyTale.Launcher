<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import { state, perform } from '../state'
import WDialog from '../controls/WDialog.vue'
const { t } = useI18n()
async function stop() {
  const target = state.stopTarget
  if (target && await perform(() => call('instances.stop', { id: target.instanceId, processId: target.processId }))) state.stopTarget = null
}
</script>
<template>
  <WDialog :open="!!state.stopTarget" :title="t('launch.stopTitle')" :primary-text="t('launch.stop')" :close-text="t('dialog.cancel')" :primary-disabled="state.busy" :close-disabled="state.busy" @primary="stop" @close="state.stopTarget = null"><p>{{ t('launch.stopBody') }}</p></WDialog>
</template>
