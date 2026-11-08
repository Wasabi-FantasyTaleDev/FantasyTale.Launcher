<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { computed, ref, watch } from 'vue'
import { localized } from '../i18n'
import { languageNames } from '../state'
import type { ClientComponent } from '../bridge'
import WCheckBox from '../controls/WCheckBox.vue'
import WButton from '../controls/WButton.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WSettingsExpander from '../controls/WSettingsExpander.vue'

const props = defineProps<{ items: ClientComponent[]; selected: string[]; languages: string[]; nativeLanguage: string; additionalLanguages: string[]; loading: boolean; error: string; disabled?: boolean }>()
const emit = defineEmits<{ plugin: [id: string, checked: boolean]; language: [code: string, checked: boolean]; retry: [] }>()
const { t } = useI18n()
const orderedItems = computed(() => [...props.items].sort((a, b) => Number(b.required) - Number(a.required)))
const selectedCount = computed(() => props.items.filter(item => item.required || props.selected.includes(item.id)).length)
const pluginsOpen = ref(false), languagesOpen = ref(false)
watch([() => props.items, () => props.additionalLanguages], () => { pluginsOpen.value = false; languagesOpen.value = false })
</script>

<template>
  <div v-if="loading || error || items.length || additionalLanguages.length" class="client-components">
    <div v-if="loading" class="component-loading" role="status"><WProgressRing :size="18" /><span>{{ t('recommendations.loading') }}</span></div>
    <div v-else-if="error" class="component-error" role="alert"><span>{{ error }}</span><WButton :disabled="disabled" @click="emit('retry')">{{ t('store.retry') }}</WButton></div>
    <template v-else>
      <WSettingsExpander v-if="items.length" v-model:open="pluginsOpen" class="component-expander" :header="`${t('recommendations.title')} · ${t('recommendations.selectedCount', { selected: selectedCount, total: items.length })}`">
        <div class="component-list">
          <div v-for="item in orderedItems" :key="item.id" class="component-card">
            <FluentIcon glyph="puzzle" :size="20" />
            <div class="component-copy"><div class="component-name"><span>{{ localized(item.name) }}</span><span v-if="item.required" class="component-badge">{{ t('serverInfo.required') }}</span></div><small v-if="item.description">{{ localized(item.description) }}</small></div>
            <WCheckBox :model-value="item.required || selected.includes(item.id)" :disabled="disabled || item.required" @update:model-value="emit('plugin', item.id, $event)"><span class="component-checkbox-name">{{ localized(item.name) }}</span></WCheckBox>
          </div>
        </div>
      </WSettingsExpander>
      <WSettingsExpander v-if="additionalLanguages.length" v-model:open="languagesOpen" class="component-expander" :header="t('recommendations.displayLanguages')">
        <div class="language-options">
          <WCheckBox v-if="nativeLanguage" :model-value="true" disabled>{{ languageNames[nativeLanguage] ?? nativeLanguage }}</WCheckBox>
          <WCheckBox v-for="code in additionalLanguages" :key="code" :model-value="languages.includes(code)" :disabled="disabled" @update:model-value="emit('language', code, $event)">{{ languageNames[code] ?? code }}</WCheckBox>
        </div>
      </WSettingsExpander>
    </template>
  </div>
</template>

<style scoped>
.client-components { display: grid; gap: 8px; }
.component-expander :deep(.w-expander-header) { min-height: 44px; padding: 10px 14px; }
.component-expander :deep(.w-card-header) { font-weight: 400; }
.component-expander :deep(.w-expander-body) { padding: 12px; }
.component-list { display: grid; gap: 8px; max-height: min(230px, 30vh); overflow-y: auto; overscroll-behavior: contain; }
.component-card { display: flex; align-items: center; gap: 12px; padding: 12px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--card-bg-default); }
.component-card > .fluent-icon { color: var(--accent-text-primary); }
.component-checkbox-name { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.component-copy { flex: 1; min-width: 0; display: grid; gap: 4px; }
.component-name { display: flex; align-items: center; flex-wrap: wrap; gap: 4px 8px; line-height: 20px; font-size: 14px; }
.component-copy small { color: var(--text-secondary); font-size: 12px; line-height: 17px; overflow-wrap: anywhere; }
.component-badge { flex: none; padding: 0 6px; border-radius: 4px; background: color-mix(in srgb, var(--accent-default) 12%, var(--card-bg-default)); color: var(--accent-text-primary); font-size: 11px; line-height: 19px; }
.language-options { display: flex; flex-wrap: wrap; gap: 12px 20px; padding: 12px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--card-bg-default); }
.component-loading, .component-error { display: flex; align-items: center; gap: 10px; color: var(--text-secondary); font-size: 12px; }
.component-error { justify-content: space-between; overflow-wrap: anywhere; }
</style>
