// Both hook and future image locators publish the same source rectangle.
export function resolveAnchor(anchor, viewport, panel) {
    const width = anchor.referenceWidth || viewport.pixelWidth;
    const height = anchor.referenceHeight || viewport.pixelHeight;
    if (![anchor.x, anchor.y, anchor.width, anchor.height, width, height, viewport.width, viewport.height]
        .every(Number.isFinite) || width <= 0 || height <= 0 || anchor.width <= 0 || anchor.height <= 0)
        throw new Error('Invalid anchor coordinates.');
    const sx = viewport.width / width, sy = viewport.height / height;
    const target = { x: anchor.x * sx, y: anchor.y * sy, width: anchor.width * sx, height: anchor.height * sy };
    const gap = Number.isFinite(anchor.gap) ? Math.max(0, anchor.gap) : 0;
    const result = { ...target };
    switch (anchor.placement ?? 'cover') {
        case 'cover': return result;
        case 'right': result.x += target.width + gap; break;
        case 'left': result.x -= panel.width + gap; break;
        case 'above': result.y -= panel.height + gap; break;
        case 'below': result.y += target.height + gap; break;
        case 'center': result.x += (target.width - panel.width) / 2; result.y += (target.height - panel.height) / 2; break;
        default: throw new Error('Invalid anchor placement.');
    }
    result.width = panel.width; result.height = panel.height;
    result.x = Math.max(0, Math.min(viewport.width - result.width, result.x));
    result.y = Math.max(0, Math.min(viewport.height - result.height, result.y));
    return result;
}
