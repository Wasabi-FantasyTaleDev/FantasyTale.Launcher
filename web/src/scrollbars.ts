// WinUI ScrollViewer shows its scrollbar while the content scrolls or the pointer moves over it, then hides it. A
// scroll area is marked active for a short while after either; app.css draws the thumb only on marked areas.

const VISIBLE_MS = 1500
const timers = new WeakMap<Element, ReturnType<typeof setTimeout>>()

function reveal(element: Element) {
  element.setAttribute('data-scrollbar-active', '')
  clearTimeout(timers.get(element))
  timers.set(element, setTimeout(() => element.removeAttribute('data-scrollbar-active'), VISIBLE_MS))
}

function scrollable(element: Element) {
  return element.scrollHeight > element.clientHeight + 1 || element.scrollWidth > element.clientWidth + 1
}

export function installAutoHidingScrollbars(): void {
  document.addEventListener('scroll', event => {
    const target = event.target === document ? document.scrollingElement : event.target
    if (target instanceof Element) reveal(target)
  }, { capture: true, passive: true })
  // Moving the pointer over a scroll area shows its scrollbar, so it can be grabbed without scrolling first.
  // At most one lookup per frame: pointer events arrive far more often than the scrollbar can change.
  let pending: EventTarget | null = null
  document.addEventListener('pointermove', event => {
    if (pending) { pending = event.target; return }
    pending = event.target
    requestAnimationFrame(() => {
      const target = pending
      pending = null
      for (let element = target instanceof Element ? target : null; element; element = element.parentElement) {
        const overflow = getComputedStyle(element).overflowY
        if ((overflow === 'auto' || overflow === 'scroll') && scrollable(element)) { reveal(element); return }
      }
    })
  }, { passive: true })
}
