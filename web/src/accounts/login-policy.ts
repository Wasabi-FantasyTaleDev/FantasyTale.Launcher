/** Required client sign-in is preserved except for explicitly name-only room destinations. */
export function resolveLoginPolicy(build: string | null | undefined, game: string | null | undefined, server?: string | null, room = false): string {
  return room && server === 'none' ? 'none' : build === 'required' ? 'required' : server ?? build ?? game ?? 'optional'
}
