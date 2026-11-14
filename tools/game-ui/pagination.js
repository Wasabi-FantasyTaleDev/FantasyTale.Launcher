/** Resolve a captured scrollbar drag using its actual available thumb travel. */
export function pageFromDrag(startPage, deltaX, travel, pageCount) {
  if (pageCount <= 1 || travel <= 0) return 0;
  return Math.max(0, Math.min(pageCount - 1, Math.round(startPage + deltaX * (pageCount - 1) / travel)));
}
