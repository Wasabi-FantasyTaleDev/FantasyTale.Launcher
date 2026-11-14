/** Compares semantic precedence; returns undefined when either version is not semantic. */
export function compareVersions(left: string, right: string): number | undefined {
  const parse = (text: string) => /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/.exec(text)
  const a = parse(left), b = parse(right)
  if (!a || !b) return undefined
  for (let i = 1; i <= 3; i++) {
    const x = BigInt(a[i]!), y = BigInt(b[i]!)
    if (x !== y) return x > y ? 1 : -1
  }
  if (!a[4] || !b[4]) return a[4] ? -1 : b[4] ? 1 : 0
  const x = a[4].split('.'), y = b[4].split('.')
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    if (x[i] === undefined || y[i] === undefined) return x[i] === undefined ? -1 : 1
    if (x[i] === y[i]) continue
    const an = /^\d+$/.test(x[i]!), bn = /^\d+$/.test(y[i]!)
    if (an && bn) return BigInt(x[i]!) > BigInt(y[i]!) ? 1 : -1
    if (an !== bn) return an ? -1 : 1
    return x[i]! > y[i]! ? 1 : -1
  }
  return 0
}

/** Orders semantic releases newest first; unversioned catalogs retain their order. */
export function newestFirst<T extends { version: string }>(releases: readonly T[]): T[] {
  return [...releases].sort((left, right) => -(compareVersions(left.version, right.version) ?? 0))
}

/** Tests whether a semantic version satisfies a space-separated constraint range (`>=`, `<=`, `>`, `<`, `=`). */
export function satisfiesRange(version: string, range: string): boolean {
  const tokens = range.trim().split(/\s+/).filter(Boolean)
  if (!tokens.length) return true
  if (compareVersions(version, version) === undefined) return false
  for (const token of tokens) {
    const match = /^(>=|<=|>|<|=)?(.+)$/.exec(token)
    if (!match) return false
    const comparison = compareVersions(version, match[2]!)
    if (comparison === undefined) return false
    const operator = match[1] ?? '='
    if (operator === '>=' ? comparison < 0 : operator === '<=' ? comparison > 0
      : operator === '>' ? comparison <= 0 : operator === '<' ? comparison >= 0 : comparison !== 0) return false
  }
  return true
}
