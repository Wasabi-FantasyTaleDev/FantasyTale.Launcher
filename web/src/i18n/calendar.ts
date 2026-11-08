/** Converts valid Gregorian dates in display labels to Buddhist years for Thai, without changing stored metadata. */
export function calendarDateLabel(text: string, locale: string): string {
  if (!/^th(?:-|$)/i.test(locale)) return text
  const formatter = new Intl.DateTimeFormat('th-TH', { calendar: 'buddhist', numberingSystem: 'latn', timeZone: 'UTC', year: 'numeric' })
  return text.replace(/\b(\d{4})-(\d{2})-(\d{2})\b/g, (original, year: string, month: string, day: string) => {
    const date = new Date(0)
    date.setUTCFullYear(Number(year), Number(month) - 1, Number(day))
    if (date.getUTCFullYear() !== Number(year) || date.getUTCMonth() !== Number(month) - 1 || date.getUTCDate() !== Number(day)) return original
    const buddhistYear = formatter.formatToParts(date).find(part => part.type === 'year')!.value
    return `${buddhistYear}-${month}-${day}`
  })
}
