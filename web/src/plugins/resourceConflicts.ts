import { reactive } from 'vue'
import type { LocalizedText } from '../bridge'

export interface ResourceConflictParty { pluginId: string; version: string; name: { values: LocalizedText }; action: string; value: string | null }
export interface ResourceConflict { id: string; code: string; archive: string; member: string; target: string; key: string; field: string | null; original: string | null; parties: ResourceConflictParty[] }
export interface ResourceConflictReport { snapshot: string; build: string; language: string | null; conflicts: ResourceConflict[] }
export interface ResourceConflictChoice { snapshot: string; conflictId: string; keepPluginId: string }

export class ResourceConflictError extends Error {
  constructor(public readonly report: ResourceConflictReport) { super('resource_conflict') }
}

/** Keep a structured backend failure distinct from ordinary launch errors. */
export function resourceConflictError(message: { code?: string; conflictReport?: ResourceConflictReport }): ResourceConflictError | undefined {
  const report = message.conflictReport
  if (message.code !== 'resource_conflict' || !report || typeof report.snapshot !== 'string' || !Array.isArray(report.conflicts)
    || !report.conflicts.length || report.conflicts.some(conflict => typeof conflict.id !== 'string' || !Array.isArray(conflict.parties)
      || conflict.parties.length < 2 || conflict.parties.some(party => typeof party.pluginId !== 'string' || typeof party.name?.values?.en !== 'string'))) return
  return new ResourceConflictError(report)
}

export const resourceConflictPrompt = reactive({ open: false, report: null as ResourceConflictReport | null, selected: {} as Record<string, string> })
let finish: ((choices: ResourceConflictChoice[] | null) => void) | undefined

export function finishResourceConflicts(accepted: boolean): void {
  const report = resourceConflictPrompt.report
  if (accepted && (!report || report.conflicts.some(conflict => !conflict.parties.some(party => party.pluginId === resourceConflictPrompt.selected[conflict.id])))) return
  const choices = accepted && report ? report.conflicts.map(conflict => ({ snapshot: report.snapshot, conflictId: conflict.id, keepPluginId: resourceConflictPrompt.selected[conflict.id]! })) : null
  resourceConflictPrompt.open = false
  const resolve = finish; finish = undefined
  resolve?.(choices)
}

/** Carry staged conflict decisions through retries without applying them to changed inputs. */
export async function launchWithResourceChoices<T>(launch: (choices: ResourceConflictChoice[]) => Promise<T>, valid: () => boolean): Promise<T | { cancelled: true }> {
  let snapshot = ''
  const choices = new Map<string, ResourceConflictChoice>()
  while (valid()) {
    try { return await launch([...choices.values()]) }
    catch (error) {
      if (!(error instanceof ResourceConflictError)) throw error
      if (!valid() || finish) return { cancelled: true }
      if (snapshot !== error.report.snapshot) { choices.clear(); snapshot = error.report.snapshot }
      Object.assign(resourceConflictPrompt, { open: true, report: error.report, selected: {} })
      const answer = await new Promise<ResourceConflictChoice[] | null>(resolve => { finish = resolve })
      if (!answer || !valid()) return { cancelled: true }
      for (const choice of answer) choices.set(choice.conflictId, choice)
    }
  }
  return { cancelled: true }
}
