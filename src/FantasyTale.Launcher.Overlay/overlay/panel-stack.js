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

// A subscription owns its original entry, even when a panel ID is reused later.
export function subscribePanel(panels, panelId, name, handler) {
    const entry = panels.get(panelId);
    if (!entry) return () => {};
    if (typeof handler !== 'function') throw new TypeError('An overlay event handler must be a function.');
    if (!entry.listeners.has(name)) entry.listeners.set(name, []);
    const handlers = entry.listeners.get(name);
    let active = true;
    const subscription = (...args) => { if (active) handler(...args); };
    handlers.push(subscription);
    return () => {
        if (!active) return;
        active = false;
        const index = handlers.indexOf(subscription);
        if (index >= 0) handlers.splice(index, 1);
        if (!handlers.length && entry.listeners.get(name) === handlers) entry.listeners.delete(name);
    };
}
