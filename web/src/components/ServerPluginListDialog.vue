<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { PolicyPluginEntry, ServerStoreSource } from '../bridge'
import WDialog from '../controls/WDialog.vue'
import ServerPluginListPanel from './ServerPluginListPanel.vue'
defineProps<{ open: boolean; game: string; builds: string[]; recommendations: PolicyPluginEntry[]; sources: ServerStoreSource[]; serverId?: string }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
</script>
<template><WDialog :open="open" :title="t('serverInfo.plugins')" :close-text="t('titlebar.close')" @close="emit('close')"><ServerPluginListPanel :active="open" :game="game" :builds="builds" :recommendations="recommendations" :sources="sources" :server-id="serverId" /></WDialog></template>
