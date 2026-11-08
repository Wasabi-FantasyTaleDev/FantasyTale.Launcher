<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import FluentIcon from '../controls/FluentIcon.vue'
import { state } from '../state'
import { serverImageUrl } from '../serverArtwork'
import { serverIconReference } from '../serverDescription'
const props = defineProps<{ url?: string | null; translations?: Record<string, string>; asset?: string | null; room?: boolean; size?: number; serverId?: string }>()
const { locale } = useI18n()
const failed = ref(false)
const reference = computed(() => serverIconReference({ iconUrl: props.url, iconUrlTranslations: props.translations }, locale.value))
const source = computed(() => reference.value ? serverImageUrl(reference.value, state.games.find(game => game.assetBase)?.assetBase) : props.asset || undefined)
watch(source, () => { failed.value = false })
</script>
<template>
  <span class="server-icon"><img v-if="source && !failed" :key="source" :src="source" alt="" referrerpolicy="no-referrer" @error="failed = true" /><FluentIcon v-else :glyph="room ? 'connect' : 'globe'" :size="size ?? 30" /></span>
</template>
<style scoped>
.server-icon { display: inline-grid; place-items: center; flex: none; width: 100%; height: 100%; overflow: hidden; border-radius: inherit; }.server-icon img { width: 100%; height: 100%; object-fit: contain; }
</style>
