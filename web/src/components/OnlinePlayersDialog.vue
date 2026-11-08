<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import OnlinePlayersPanel from './OnlinePlayersPanel.vue'
defineProps<{ open: boolean; game?: string; serverId?: string; sourceId?: string; entryId?: string }>()
const emit = defineEmits<{ close: []; availability: [enabled: boolean] }>()
const { t } = useI18n()
</script>
<template><WDialog :open="open" :title="t('serverInfo.roster')" wide :close-text="t('titlebar.close')" @close="emit('close')"><OnlinePlayersPanel :active="open" :game="game" :server-id="serverId" :source-id="sourceId" :entry-id="entryId" @availability="emit('availability', $event)" /></WDialog></template>
