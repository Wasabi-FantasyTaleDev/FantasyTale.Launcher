// The editor's saved selections, kept in this viewer's overlay storage (format 1, validated before use).
export const presetStorageKey = 'bf-ban-pick-presets-v1';

/** Validate saved browser data before exposing it to the rule editor. */
export function parsePresets(raw) {
  if (raw === null) return [];
  const value = JSON.parse(raw);
  if (!value || typeof value !== 'object' || !('format' in value) || value.format !== 1
      || !('presets' in value) || !Array.isArray(value.presets)) throw new Error('Invalid preset format.');
  const ids = new Set();
  const names = new Set();
  return value.presets.map(preset => {
    if (!preset || typeof preset !== 'object' || !('id' in preset) || typeof preset.id !== 'string' || !preset.id
        || !('name' in preset) || typeof preset.name !== 'string' || !preset.name.trim()
        || !('itemIds' in preset) || !Array.isArray(preset.itemIds)
        || !preset.itemIds.every(id => Number.isSafeInteger(id) && Number(id) > 0)
        || ids.has(preset.id) || names.has(preset.name.trim())) throw new Error('Invalid preset entry.');
    ids.add(preset.id);
    names.add(preset.name.trim());
    return { id: preset.id, name: preset.name.trim(), itemIds: [...new Set(preset.itemIds)].sort((a, b) => a - b) };
  });
}

/** Write first so quota or browser errors leave the editor's existing list intact. */
export function writePresets(storage, presets) {
  const raw = JSON.stringify({ format: 1, presets });
  const checked = parsePresets(raw);
  storage.setItem(presetStorageKey, JSON.stringify({ format: 1, presets: checked }));
  return checked;
}
