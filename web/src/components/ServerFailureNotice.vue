<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ServerRequestError } from '../serverFailures'
import WInfoBar from '../controls/WInfoBar.vue'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
const props = defineProps<{ error: Error; dismissible?: boolean }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n(), router = useRouter()
const open = ref(false), copyError = ref('')
const details = computed(() => props.error instanceof ServerRequestError ? props.error.details : undefined)
const expectedFailures = new Set(['wrong_password', 'account_not_found', 'account_banned', 'server_full', 'server_maintenance', 'server_not_ready', 'not_whitelisted', 'invalid_credentials', 'password_too_short', 'launcher_update_required'])
const showDetails = computed(() => !!details.value && !expectedFailures.has(details.value.code ?? ''))
const text = computed(() => details.value ? JSON.stringify(details.value, null, 2) : '')
watch(() => props.error, () => { open.value = false; copyError.value = '' })
async function copy() { try { await navigator.clipboard.writeText(text.value); copyError.value = '' } catch { copyError.value = t('serverFailure.copyFailed') } }
</script>
<template>
  <WInfoBar severity="error" :closable="dismissible" @close="emit('close')"><span class="failure-message">{{ error.message }}</span><template #action><div v-if="showDetails || details?.code === 'launcher_update_required'" class="failure-actions"><WButton v-if="showDetails" variant="subtle" @click="open = true">{{ t('serverFailure.details') }}</WButton><WButton v-if="details?.code === 'launcher_update_required'" @click="router.push({ name: 'settings', query: { tab: 'updates' } })">{{ t('serverFailure.openUpdates') }}</WButton></div></template></WInfoBar>
  <WDialog :open="open && showDetails" :title="t('serverFailure.details')" :close-text="t('titlebar.close')" @close="open = false"><pre class="failure-details">{{ text }}</pre><WButton @click="copy">{{ t('launcherUpdates.copyDetails') }}</WButton><p v-if="copyError">{{ copyError }}</p></WDialog>
</template>
<style scoped>
.failure-message { white-space: pre-wrap; }.failure-actions { display: flex; gap: 8px; margin-top: 8px; }.failure-details { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 320px; overflow-y: auto; user-select: text; font-size: 12px; }
</style>
