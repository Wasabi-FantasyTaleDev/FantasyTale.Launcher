<script setup lang="ts">
// WinUI NavigationView, Left mode: pane on the Mica layer with a hamburger toggle,
// navigation items and a footer; the content area is a raised layer with a
// rounded top-left corner and a one-pixel stroke, exactly like the Settings app.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { navEntries } from '../router'
import FluentIcon from '../controls/FluentIcon.vue'
import LauncherUpdateEntry from './LauncherUpdateEntry.vue'
import { state, navigationEntries, selectedGame } from '../state'
import { prefs } from '../theme'
import type { GlyphName } from '../icons'
import WInfoBadge from '../controls/WInfoBadge.vue'
import { availableStoreUpdates } from '../store/background'
import { previewStoreUpdateCount } from '../store/preview'
import { demoMode, inHost } from '../bridge'

const { t } = useI18n()
const storeUpdateCount = computed(() => import.meta.env.DEV && !inHost && previewStoreUpdateCount.value !== undefined
  ? previewStoreUpdateCount.value : availableStoreUpdates.value.length)
const route = useRoute()
const router = useRouter()
function navigate(name: string) {
  if (disabled(name)) return
  if (narrow.value) expanded.value = false
  void router.push({ name })
}
const showDeveloper = computed(() => prefs.developerMode || !!demoMode?.enabled)
function disabled(name: string) { return name === 'developer' && !showDeveloper.value || name === 'hosting' && selectedGame.value?.localHostingEnabled === false }

// Each launch starts compact; expansion applies only to the current session.
const expanded = ref(false)
const narrow = ref(window.innerWidth < 1000)
const compact = computed(() => !expanded.value)

const availableItems = computed(() => navEntries.filter(entry => entry.footer || navigationEntries.value.includes(entry.tag)))
const mainItems = computed(() => {
  const entries = availableItems.value.filter(entry => !entry.footer)
  return showDeveloper.value ? [...entries, { tag: 'developer', label: 'developerMode.navigation', icon: 'developerTools' }] : entries
})
const footerItems = computed(() => availableItems.value.filter(entry => entry.footer))
const selected = computed(() => String(route.name ?? 'home'))

// Apply the same policy to bookmarks and history as to visible navigation.
watch([mainItems, footerItems, selected, () => state.loaded], () => {
  if (state.loaded && route.name && ![...mainItems.value, ...footerItems.value].some(entry => entry.tag === route.name))
    void router.replace({ name: 'home' })
}, { immediate: true, flush: 'sync' })

const listEl = ref<HTMLElement>()
const footEl = ref<HTMLElement>()
const pill = ref({ top: 0, footer: false, ready: false })

// The accent pill slides between items instead of fading in place.
function measure() {
  const el =
    listEl.value?.querySelector<HTMLElement>(`.nav-item[data-tag="${CSS.escape(selected.value)}"]`) ??
    footEl.value?.querySelector<HTMLElement>(`.nav-item[data-tag="${CSS.escape(selected.value)}"]`)
  if (!el) {
    pill.value = { top: 0, footer: pill.value.footer, ready: false }
    return
  }
  const footer = footEl.value?.contains(el) ?? false
  const container = footer ? footEl.value : listEl.value
  if (container) pill.value = { top: el.getBoundingClientRect().top - container.getBoundingClientRect().top + 10, footer, ready: true }
}

watch(selected, () => nextTick(measure))
watch(mainItems, () => nextTick(measure))
watch(compact, () => nextTick(measure))
watch(() => t('nav.home'), () => nextTick(measure))

function onResize() {
  narrow.value = window.innerWidth < 1000
  nextTick(measure)
}
onMounted(() => {
  measure()
  window.addEventListener('resize', onResize)
})
onBeforeUnmount(() => window.removeEventListener('resize', onResize))

</script>

<template>
  <div class="nav-root">
    <nav class="pane" :class="{ compact }">
      <div class="pane-top">
        <button
          type="button"
          class="pane-btn"
          :aria-label="compact ? $t('nav.expand') : $t('nav.collapse')"
          :aria-expanded="!compact"
          @click="expanded = !expanded">
          <FluentIcon glyph="globalNavButton" />
        </button>
      </div>
      <div class="pane-scroll">
        <div ref="listEl" class="nav-list">
          <TransitionGroup name="nav-entry" @after-enter="measure" @after-leave="measure">
          <button
            v-for="entry in mainItems"
            :key="entry.tag"
            type="button"
            class="nav-item"
            :class="{ selected: entry.tag === selected }"
            :data-tag="entry.tag"
            :disabled="disabled(entry.tag)"
            :aria-label="entry.tag === 'store' && storeUpdateCount ? `${t(entry.label)}, ${t('store.updateCount', { count: storeUpdateCount })}` : t(entry.label)"
            :aria-current="entry.tag === selected ? 'page' : undefined"
            :title="disabled(entry.tag) ? t('featureAvailability.development') : compact ? t(entry.label) : undefined"
            @click="navigate(entry.tag)">
            <FluentIcon :glyph="entry.icon as GlyphName" />
            <span class="nav-label" :class="{ hidden: compact }">{{ t(entry.label) }}</span>
            <WInfoBadge v-if="entry.tag === 'store' && storeUpdateCount" class="store-update-count" :value="storeUpdateCount" :label="t('store.updateCount', { count: storeUpdateCount })" />
          </button>
          </TransitionGroup>
          <span class="nav-pill" :class="{ ready: pill.ready && !pill.footer }" :style="{ top: `${pill.top}px` }" />
        </div>
      </div>
      <div ref="footEl" class="pane-footer">
        <LauncherUpdateEntry :compact="compact" @layout="measure" />
        <TransitionGroup name="nav-entry" @after-enter="measure" @after-leave="measure">
        <button
          v-for="entry in footerItems"
          :key="entry.tag"
          type="button"
          class="nav-item"
          :class="{ selected: entry.tag === selected }"
          :data-tag="entry.tag"
          :aria-label="entry.tag === 'store' && storeUpdateCount ? `${t(entry.label)}, ${t('store.updateCount', { count: storeUpdateCount })}` : t(entry.label)"
          :aria-current="entry.tag === selected ? 'page' : undefined"
          :title="compact ? t(entry.label) : undefined"
          @click="navigate(entry.tag)">
          <FluentIcon :glyph="entry.icon as GlyphName" />
          <span class="nav-label" :class="{ hidden: compact }">{{ t(entry.label) }}</span>
            <WInfoBadge v-if="entry.tag === 'store' && storeUpdateCount" class="store-update-count" :value="storeUpdateCount" :label="t('store.updateCount', { count: storeUpdateCount })" />
        </button>
        </TransitionGroup>
        <span class="nav-pill" :class="{ ready: pill.ready && pill.footer }" :style="{ top: `${pill.top}px` }" />
      </div>
    </nav>
    <div class="content-layer">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.nav-root {
  position: absolute;
  inset: 48px 0 0 0;
  display: flex;
}
.pane {
  flex: none;
  /* NavigationView OpenPaneLength. */
  width: 256px;
  display: flex;
  flex-direction: column;
  min-height: 0;
  transition: width var(--duration-fast) var(--ease-fast-out-slow-in);
}
.pane.compact {
  width: 56px;
}
.pane-top {
  flex: none;
  padding: 4px 8px 8px;
}
.pane-btn {
  width: 40px;
  height: 36px;
  border-radius: var(--radius-control);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--text-primary);
  transition: background-color var(--duration-faster) linear;
}
.pane-btn:hover {
  background: var(--subtle-secondary);
}
.pane-btn:active {
  background: var(--subtle-tertiary);
}
.pane-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 0 8px;
  scrollbar-width: thin;
}
.pane-footer {
  flex: none;
  position: relative;
  min-height: 54px;
  padding: 8px;
}
.nav-list {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-top: 8px;
}
.nav-item {
  position: relative;
  display: flex;
  align-items: center;
  width: 100%;
  min-height: 38px;
  height: 38px;
  padding: 0 10px;
  border-radius: var(--radius-control);
  color: var(--text-primary);
  font-size: 14px;
  text-align: left;
  text-decoration: none;
  transition: background-color var(--duration-faster) linear;
}
.nav-item .fluent-icon {
  flex: none;
  width: 20px;
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
}
.nav-item:disabled { color: var(--text-tertiary); cursor: default; background: transparent; }
.nav-entry-enter-active,
.nav-entry-leave-active {
  transition: opacity 180ms linear;
}
.nav-entry-enter-from,
.nav-entry-leave-to {
  opacity: 0;
}
.nav-entry-leave-active {
  /* Keep the departing entry in its row so fading cannot change its position. */
  pointer-events: none;
}
.compact .nav-item {
  padding-inline: 10px;
}
.nav-item:hover {
  background: var(--subtle-secondary);
}
.nav-item:active {
  background: var(--subtle-tertiary);
}
.nav-item.selected {
  background: var(--subtle-secondary);
}
.nav-item.selected .nav-label {
  font-weight: 400;
}
.nav-label {
  display: block;
  width: calc(100% - 32px);
  margin-left: 32px;
  opacity: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  transition: opacity 120ms linear;
}
.store-update-count { margin-left: auto; }
.compact .store-update-count { position: absolute; right: -3px; top: 0; }
.nav-label.hidden {
  opacity: 0;
  pointer-events: none;
}
.nav-pill {
  pointer-events: none;
  position: absolute;
  left: 0;
  width: 3px;
  height: 16px;
  border-radius: 2px;
  background: var(--accent-default);
  opacity: 0;
  transition:
    top var(--duration-fast) var(--ease-fast-out-slow-in),
    opacity var(--duration-faster) linear;
}
.nav-pill.ready {
  opacity: 1;
}
/* Raised content layer: top-left rounded corner and a 1px stroke, like Settings. */
.content-layer {
  flex: 1;
  min-width: 0;
  position: relative;
  background: var(--layer-default);
  border-top-left-radius: var(--radius-overlay);
  border-left: 1px solid var(--stroke-surface-flyout);
  border-top: 1px solid var(--stroke-surface-flyout);
  overflow: hidden;
}
@media (prefers-reduced-motion: reduce) {
  .pane, .nav-label, .nav-pill, .nav-entry-enter-active, .nav-entry-leave-active { transition: none; }
}
</style>
