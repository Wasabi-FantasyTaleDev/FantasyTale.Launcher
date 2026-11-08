// Map insertion order breaks ties within a layer and is updated on activation.
export function orderedPanels(panels) {
    return [...panels.values()].filter(p => p.host.classList.contains('visible'))
        .sort((a, b) => Number(a.modal) - Number(b.modal) || a.layer - b.layer);
}

export function inputPanels(shown) {
    const modal = shown.findLast(p => p.modal);
    return modal ? [modal] : shown.filter(p => p.interactive);
}

// The top modal owns outside-click policy; lower panels cannot override it.
export function passesOutsideClicks(shown) {
    const modal = shown.findLast(p => p.modal);
    return !modal || modal.passThroughOutside === true;
}

export function bringToFront(panels, panelId) {
    const entry = panels.get(panelId);
    if (!entry) return;
    panels.delete(panelId);
    panels.set(panelId, entry);
}
