export function hexColor(value: string): string | null {
  return /^#[0-9a-f]{6}$/i.test(value.trim()) ? value.trim().toUpperCase() : null
}

export function rgbColor(channels: readonly string[]): string | null {
  if (channels.length !== 3 || channels.some(channel => !/^\d{1,3}$/.test(channel) || Number(channel) > 255)) return null
  return `#${channels.map(channel => Number(channel).toString(16).padStart(2, '0')).join('').toUpperCase()}`
}

export function colorChannels(value: string): string[] {
  const hex = hexColor(value)
  return hex ? [1, 3, 5].map(index => String(parseInt(hex.slice(index, index + 2), 16))) : ['', '', '']
}

export function savedColors(value: string | null): string[] {
  try {
    const parsed: unknown = JSON.parse(value ?? '[]')
    return Array.isArray(parsed) ? [...new Set(parsed.flatMap(item => typeof item === 'string' && hexColor(item) ? [hexColor(item)!] : []))] : []
  } catch { return [] }
}
