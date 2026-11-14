import { computed, onBeforeUnmount, ref, watch, type Ref } from 'vue'

// Scroll areas fade their top and bottom edge while content is hidden past them, so a list is never cut off with a
// hard line. Each edge fades in over the first 24 px of hidden content; the `scroll-edges` class in app.css draws it.
export function useScrollEdges(element: Ref<HTMLElement | undefined>) {
  const edges = ref({ top: 1, bottom: 1 })
  function measure() {
    const target = element.value
    if (!target) { edges.value = { top: 1, bottom: 1 }; return }
    const remaining = Math.max(0, target.scrollHeight - target.clientHeight - target.scrollTop)
    edges.value = { top: 1 - Math.min(1, Math.max(0, target.scrollTop) / 24), bottom: 1 - Math.min(1, remaining / 24) }
  }
  let resize: ResizeObserver | undefined
  // The area and its children are observed: rows that appear or change size alter what is hidden.
  watch(element, target => {
    resize?.disconnect()
    measure()
    if (!target || typeof ResizeObserver === 'undefined') return
    resize = new ResizeObserver(measure)
    resize.observe(target)
    for (const child of target.children) resize.observe(child)
  }, { flush: 'post' })
  onBeforeUnmount(() => resize?.disconnect())
  const style = computed(() => ({ '--scroll-edge-top': edges.value.top, '--scroll-edge-bottom': edges.value.bottom }))
  return { edges, measure, style }
}
