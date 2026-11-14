<script setup lang="ts">
import { computed } from 'vue'
import MarkdownIt from 'markdown-it'
import { call } from '../bridge'

// Operator text (notices, server and plugin descriptions) in Markdown. Raw HTML in the source is escaped, not
// rendered, and markdown-it refuses javascript:, vbscript: and data: links, so the output is safe to insert. Links
// open in the system browser, never inside the Launcher.
const props = defineProps<{ source: string }>()
const html = computed(() => markdown.render(props.source ?? ''))

function open(event: MouseEvent) {
  const link = (event.target as HTMLElement | null)?.closest('a')
  if (!link) return
  event.preventDefault()
  const href = link.getAttribute('href') ?? ''
  if (/^https?:\/\//i.test(href)) void call('shell.openExternal', { url: href }).catch(() => {})
}
</script>

<script lang="ts">
const markdown = new MarkdownIt({ html: false, linkify: true, breaks: true, typographer: false })
</script>

<template>
  <!-- eslint-disable-next-line vue/no-v-html -- markdown-it output with html disabled -->
  <div class="markdown-text" @click="open" v-html="html" />
</template>

<style scoped>
.markdown-text { overflow-wrap: anywhere; line-height: 1.7; }
.markdown-text > :deep(:first-child) { margin-top: 0; }
.markdown-text > :deep(:last-child) { margin-bottom: 0; }
.markdown-text :deep(p), .markdown-text :deep(ul), .markdown-text :deep(ol), .markdown-text :deep(blockquote), .markdown-text :deep(pre), .markdown-text :deep(table) { margin: 0 0 10px; }
.markdown-text :deep(h1), .markdown-text :deep(h2), .markdown-text :deep(h3), .markdown-text :deep(h4), .markdown-text :deep(h5), .markdown-text :deep(h6) { margin: 14px 0 6px; font-weight: 600; line-height: 1.4; }
.markdown-text :deep(h1) { font-size: 18px; }
.markdown-text :deep(h2) { font-size: 16px; }
.markdown-text :deep(h3) { font-size: 14px; }
.markdown-text :deep(h4), .markdown-text :deep(h5), .markdown-text :deep(h6) { font-size: 13px; }
.markdown-text :deep(ul), .markdown-text :deep(ol) { padding-left: 22px; }
.markdown-text :deep(li + li) { margin-top: 2px; }
.markdown-text :deep(blockquote) { padding: 2px 0 2px 12px; border-left: 3px solid var(--stroke-divider); color: var(--text-secondary); }
.markdown-text :deep(blockquote p) { margin: 0; }
.markdown-text :deep(code) { padding: 1px 5px; border-radius: 4px; background: var(--subtle-secondary); font-family: 'Cascadia Mono', Consolas, monospace; font-size: .92em; }
.markdown-text :deep(pre) { padding: 10px 12px; border-radius: var(--radius-control, 4px); background: var(--subtle-secondary); overflow-x: auto; }
.markdown-text :deep(pre code) { padding: 0; background: none; }
.markdown-text :deep(hr) { margin: 12px 0; border: 0; border-top: 1px solid var(--stroke-divider); }
.markdown-text :deep(table) { border-collapse: collapse; }
.markdown-text :deep(th), .markdown-text :deep(td) { padding: 4px 10px; border: 1px solid var(--stroke-divider); text-align: left; }
.markdown-text :deep(a) { color: var(--accent-text, var(--accent-default)); text-decoration: none; cursor: pointer; }
.markdown-text :deep(a:hover) { text-decoration: underline; }
.markdown-text :deep(img) { max-width: 100%; }
</style>
