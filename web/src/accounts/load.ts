/**
 * Loads server account groups with a fixed concurrency budget while strictly
 * preserving original server order in the returned array.
 *
 * Concurrency & Invariants:
 * 1. Budget: Limits parallel bridge RPCs to at most 4 workers to prevent overloading
 *    the host bridge or triggering connection queue exhaustion.
 * 2. Order Preservation: Results are stored by explicit array index rather than
 *    completion order, guaranteeing stable presentation.
 * 3. Stale-Result Predicate: The `current` callback is checked before every item
 *    and before final return. If the user navigates away or switches servers/games,
 *    no more work is scheduled. Existing requests finish, but their results are not applied.
 */
export async function loadAccountGroups<T, R>(servers: readonly T[], load: (server: T) => Promise<R>, current = () => true): Promise<R[] | undefined> {
  const results: R[] = new Array(servers.length)
  let next = 0, failed = false
  await Promise.all(Array.from({ length: Math.min(4, servers.length) }, async () => {
    while (!failed && current()) {
      const index = next++
      if (index >= servers.length) return
      try { results[index] = await load(servers[index]!) }
      catch (error) { failed = true; throw error }
    }
  }))
  return current() ? results : undefined
}
