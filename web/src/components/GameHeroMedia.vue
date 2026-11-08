<script setup lang="ts">
import { computed, onActivated, onDeactivated, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import HeroVideo from './HeroVideo.vue'
import FluentIcon from '../controls/FluentIcon.vue'

const props = defineProps<{ sources: string[]; audioTracks?: (string | null)[]; accent: string; image?: string | null; disabled?: boolean }>()
const { t } = useI18n()
const index = ref(0), muted = ref(true), changing = ref(true)
const audioError = ref(false)
const active = ref(true)
onActivated(() => { active.value = true })
onDeactivated(() => { active.value = false })
const failed = ref(new Set<number>())
const source = computed(() => failed.value.size < props.sources.length ? props.sources[index.value] : undefined)
function nextVideo() {
  for (let offset = 1; offset <= props.sources.length; offset++) {
    const candidate = (index.value + offset) % props.sources.length
    if (!failed.value.has(candidate)) {
      if (candidate !== index.value) { changing.value = true; audioError.value = false; index.value = candidate }
      return
    }
  }
}
function fail() {
  failed.value.add(index.value)
  changing.value = false
  nextVideo()
}
</script>

<template>
  <div class="hero-media" :style="{ '--hero-accent': accent }">
    <!-- Unmount the entire media subtree so suppression also stops companion audio and pending transitions. -->
    <template v-if="active && !disabled">
    <div class="media-placeholder" aria-hidden="true"><span /><span /></div>
    <img v-if="image && !source" class="media-cover" :src="image" alt="" />
    <Transition name="playlist-video" mode="out-in"><HeroVideo v-if="source" :key="index" :source="source" :audio-source="audioTracks?.[index]" :muted="muted" :loop="sources.length - failed.size === 1"
      @audio-failed="audioError = true" @ready="changing = false" @failed="fail" @ended="nextVideo" @mute-required="muted = true" /></Transition>
    <div v-if="sources.length" class="media-controls">
      <span v-if="audioError && source" class="media-error" role="status">{{ t('service.audioUnavailable') }}</span>
      <span v-if="!source" class="media-error" role="status">{{ t('live.videoUnavailable') }}</span>
      <span v-if="sources.length > 1" class="video-position" :aria-label="t('live.videoPosition', { current: index + 1, total: sources.length })">{{ index + 1 }} / {{ sources.length }}</span>
      <button v-if="sources.length > 1" type="button" :aria-label="t('live.nextVideo')" :title="t('live.nextVideo')" :disabled="changing || !source" @click="nextVideo"><FluentIcon glyph="nextVideo" :size="16" /></button>
      <button type="button" :aria-label="muted ? t('live.unmuteVideo') : t('live.muteVideo')" :title="muted ? t('live.unmuteVideo') : t('live.muteVideo')" :disabled="!source" @click="muted = !muted"><FluentIcon :glyph="muted ? 'mute' : 'volume'" :size="16" /></button>
    </div>
    </template>
  </div>
</template>

<style scoped>
.hero-media { position: absolute; inset: 0; overflow: hidden; pointer-events: none; }
.media-cover { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; object-position: center 28%; }
.media-placeholder { position: absolute; inset: 0; overflow: hidden; }
.media-placeholder span { position: absolute; width: 480px; height: 480px; border: 1px solid #ffffff26; border-radius: 50%; right: -40px; top: -110px; box-shadow: 0 0 0 56px #ffffff08, 0 0 0 112px #ffffff05; }
.media-placeholder span + span { width: 260px; height: 260px; top: 100px; right: 230px; }
.playlist-video-leave-active { transition: opacity 140ms ease; }
.playlist-video-leave-to { opacity: 0; }
.media-controls { position: absolute; right: 16px; bottom: 16px; z-index: 4; display: flex; align-items: center; gap: 6px; color: white; pointer-events: auto; }
.media-controls button { display: grid; place-items: center; width: 32px; height: 32px; border: 1px solid #ffffff40; border-radius: 6px; background: #15233180; backdrop-filter: blur(12px); color: white; }
.media-controls button:hover:not(:disabled) { background: #152331bb; }
.media-controls button:active:not(:disabled) { background: #152331dd; }
.media-controls button:disabled { opacity: 0.45; }
.media-controls button:focus-visible { outline: 2px solid white; outline-offset: 2px; }
.video-position, .media-error { font-size: 12px; padding: 4px 8px; border-radius: 4px; background: #15233180; }
</style>
