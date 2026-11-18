import { resolveAnchor } from './anchors.js';

// Holes arrive in game client pixels and are mapped to page pixels exactly like a cover anchor, so they follow the
// same scaling as panels anchored to native forms.
export function mapHoles(holes, viewport) {
    if (!Array.isArray(holes)) return [];
    const result = [];
    for (const hole of holes) {
        try {
            const rect = resolveAnchor({ x: hole.x, y: hole.y, width: hole.width, height: hole.height }, viewport, { width: 0, height: 0 });
            result.push({ x: rect.x, y: rect.y, width: rect.width, height: rect.height });
        } catch { /* A malformed rectangle is skipped; the others still apply. */ }
    }
    return result;
}

// The parts of each hole that lie inside the rectangle, relative to its top left; holes wholly outside are dropped.
// A hole inside another one adds nothing, and under the even-odd rule it would fill the cut again.
export function holesWithin(rect, holes) {
    const local = [];
    for (const hole of holes) {
        const left = Math.max(hole.x, rect.x), top = Math.max(hole.y, rect.y);
        const right = Math.min(hole.x + hole.width, rect.x + rect.width), bottom = Math.min(hole.y + hole.height, rect.y + rect.height);
        if (right > left && bottom > top) local.push({ x: left - rect.x, y: top - rect.y, width: right - left, height: bottom - top });
    }
    const same = (a, b) => a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;
    const inside = (a, b) => b.x <= a.x && b.y <= a.y && b.x + b.width >= a.x + a.width && b.y + b.height >= a.y + a.height;
    // Keep a hole unless another contains it; of equal holes only the first stays.
    return local.filter((a, i) => !local.some((b, j) => j !== i && inside(a, b) && (!same(a, b) || j < i)));
}

// A clip-path that shows the whole box except the local holes, or '' when there is nothing to cut.
export function clipPath(width, height, localHoles) {
    if (!localHoles.length) return '';
    const num = value => Math.round(value * 100) / 100;
    const cuts = localHoles.map(h => `M${num(h.x)} ${num(h.y)}h${num(h.width)}v${num(h.height)}h${num(-h.width)}Z`).join('');
    return `path(evenodd, "M0 0h${num(width)}v${num(height)}h${num(-width)}Z${cuts}")`;
}

// Dim shown on a panel that a native dialog is open above, like a modal scrim confined to that panel.
export const DIM_FILTER = 'brightness(0.55)';
