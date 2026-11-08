<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = defineProps<{ source: string; audioSource?: string | null; muted: boolean; loop: boolean }>()
const emit = defineEmits<{ ready: []; failed: []; ended: []; muteRequired: []; audioFailed: [] }>()
const video = ref<HTMLVideoElement>(), audio = ref<HTMLAudioElement>()
const ready = ref(false)
let frame: number | undefined, deadline: ReturnType<typeof setTimeout> | undefined
let stopped = false, failed = false, audioFailed = false, audioStarting = false
function pause() { video.value?.pause(); audio.value?.pause() }
function fail() {
  if (stopped || failed) return
  failed = true; clearTimeout(deadline); pause(); emit('failed')
}
function failAudio() {
  if (stopped || audioFailed) return
  audioFailed = true; audio.value?.pause(); emit('audioFailed')
}
function reveal() {
  const element = video.value
  if (!element || stopped || failed || ready.value || frame !== undefined) return
  const show = () => {
    frame = undefined
    if (stopped || failed) return
    clearTimeout(deadline); ready.value = true; emit('ready')
  }
  if (typeof element.requestVideoFrameCallback === 'function') frame = element.requestVideoFrameCallback(show)
  else show()
}
function syncAudio(force = false) {
  const v = video.value, a = audio.value
  if (!v || !a || stopped || failed || audioFailed || a.readyState < 1) return
  a.muted = props.muted; a.playbackRate = v.playbackRate
  const pastEnd = Number.isFinite(a.duration) && v.currentTime >= a.duration
  if (pastEnd || v.paused || v.ended || v.seeking || v.readyState < 3 || document.hidden) { a.pause(); return }
  if (force || Math.abs(a.currentTime - v.currentTime) > 0.18) a.currentTime = v.currentTime
  if (a.paused && !audioStarting) {
    audioStarting = true
    void a.play().catch((error: DOMException) => {
      if (stopped || error.name === 'AbortError') return
      if (error.name === 'NotAllowedError') { a.muted = true; emit('muteRequired') }
      else failAudio()
    }).finally(() => { audioStarting = false })
  }
}
async function play() {
  const element = video.value
  if (!element || stopped || failed || document.hidden) return
  element.muted = !!props.audioSource || props.muted
  syncAudio(true)
  try { await element.play(); syncAudio(true) }
  catch (error) {
    if (stopped || (error as DOMException).name === 'AbortError') return
    if ((error as DOMException).name === 'NotAllowedError' && !element.muted) {
      element.muted = true; emit('muteRequired')
      try { await element.play() } catch { fail() }
    } else fail()
  }
}
function visibilityChanged() {
  clearTimeout(deadline)
  if (document.hidden) pause()
  else { if (!ready.value) deadline = setTimeout(fail, 15000); void play() }
}
watch(() => props.muted, () => {
  if (video.value) video.value.muted = !!props.audioSource || props.muted
  if (audio.value) audio.value.muted = props.muted
  syncAudio(true); void play()
}, { flush: 'sync' })
onMounted(() => { document.addEventListener('visibilitychange', visibilityChanged); visibilityChanged() })
onBeforeUnmount(() => {
  stopped = true; clearTimeout(deadline); document.removeEventListener('visibilitychange', visibilityChanged)
  if (frame !== undefined) video.value?.cancelVideoFrameCallback(frame)
  if (video.value) video.value.muted = true
  if (audio.value) audio.value.muted = true
  pause()
})
</script>

<template>
  <div class="clip" :class="{ ready }">
    <video ref="video" :src="source" autoplay :muted="!!audioSource || muted" :loop="loop" playsinline preload="auto"
      disablepictureinpicture aria-hidden="true" @loadeddata="reveal" @playing="reveal(); syncAudio(true)" @error="fail"
      @ended="audio?.pause(); emit('ended')" @pause="audio?.pause()" @waiting="audio?.pause()" @seeking="audio?.pause()"
      @seeked="syncAudio(true)" @timeupdate="syncAudio()" @ratechange="syncAudio(true)" />
    <audio v-if="audioSource" ref="audio" :src="audioSource" :muted="muted" preload="auto" aria-hidden="true"
      @canplay="syncAudio(true)" @error="failAudio" />
  </div>
</template>

<style scoped>
.clip { position: absolute; inset: 0; opacity: 0; pointer-events: none; transition: opacity 420ms cubic-bezier(0.2, 0, 0, 1); }
.clip.ready { opacity: 1; }
video { width: 100%; height: 100%; object-fit: cover; }
</style>
