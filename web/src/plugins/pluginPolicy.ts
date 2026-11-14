import { reactive } from 'vue'
import { call, demoMode, inHost, type LocalizedText } from '../bridge'
import { i18n } from '../i18n'
import { refreshInstances, state } from '../state'
import { matchesPluginIdentity } from '../store/updates'

/** One unmet plugin policy rule, as the host evaluates it for a client and the launcher language. */
export interface PolicyFinding {
  plugin: string; pluginVersion: string; pluginName: LocalizedText; pluginDescription?: LocalizedText | null; pluginKind?: string
  pluginRequired: boolean; pluginActive?: boolean
  type: 'require' | 'recommend' | 'conflict'; other: string; range: string
  status: 'missing' | 'disabled' | 'version' | 'unavailable' | 'conflict'; blocks: boolean; defaultSelected: boolean
  target: { id: string; version: string; name: LocalizedText; description?: LocalizedText | null; kind: string } | null
}
export interface PolicyTarget { id: string; version: string }

/** The open plugin policy prompt: requirements and conflicts that block, and recommendations to accept. */
export const policyPrompt = reactive({
  open: false, busy: false, error: '', instanceId: '', launching: false,
  enabling: null as PolicyTarget | null, keepEnabling: true,
  disabling: null as { id: string; version?: string } | null, keepDisabling: true,
  findings: [] as PolicyFinding[], cards: [] as PolicyFinding[], selected: [] as string[],
})
let finish: ((proceed: boolean) => void) | undefined
let tail: Promise<unknown> = Promise.resolve()

export const findingKey = (finding: PolicyFinding) => `${finding.plugin}@${finding.type}@${finding.other}`

function language() { return String(i18n.global.locale.value) }

async function evaluate(): Promise<PolicyFinding[]> {
  return await call('pluginPolicy.check', { instanceId: policyPrompt.instanceId, language: language(),
    enabling: policyPrompt.enabling && policyPrompt.keepEnabling ? policyPrompt.enabling : undefined,
    disabling: policyPrompt.disabling && policyPrompt.keepDisabling ? policyPrompt.disabling : undefined,
    launching: policyPrompt.launching || undefined }) as PolicyFinding[]
}

/**
 * Checks plugin policy before enabling/disabling a package or launching a client and, when a rule is unmet, opens the prompt.
 * Resolves true when the caller may continue: every blocking rule is met (or the dependent package was switched off)
 * and the chosen recommendations were applied.
 */
export function requestPluginPolicy(instanceId: string, options: { enabling?: PolicyTarget; disabling?: { id: string; version?: string }; launching?: boolean },
  valid: () => boolean = () => true): Promise<boolean> {
  if (!(inHost || !!demoMode?.enabled)) return Promise.resolve(true)
  const request = tail.then(async () => {
    if (!valid()) return false
    Object.assign(policyPrompt, { instanceId, launching: !!options.launching,
      enabling: options.enabling ?? null, keepEnabling: true,
      disabling: options.disabling ?? null, keepDisabling: true, error: '', busy: false })
    const findings = await evaluate()
    if (!valid()) return false
    if (!findings.length) return true
    Object.assign(policyPrompt, { findings, cards: findings.map(item => ({ ...item })), open: true,
      selected: findings.filter(item => item.type === 'recommend' && item.defaultSelected).map(findingKey) })
    return new Promise<boolean>(resolve => { finish = resolve })
  })
  tail = request.catch(() => {})
  return request
}

/** Re-evaluates after an action in the prompt, keeping settled cards visible while adding any new rules. */
export async function refreshPolicy(): Promise<void> {
  const next = await evaluate()
  policyPrompt.findings = next
  const nextByKey = new Map(next.map(item => [findingKey(item), item]))
  const updated = policyPrompt.cards.map(card => nextByKey.get(findingKey(card)) ?? card)
  for (const item of next) if (!updated.some(card => findingKey(card) === findingKey(item))) updated.push(item)
  policyPrompt.cards = updated
}

async function configure(id: string, version: string, enabled: boolean) {
  await call('plugins.configure', { instanceId: policyPrompt.instanceId, id, version, enabled })
}

/** Runs one prompt action and re-evaluates; errors stay in the prompt. */
export async function policyAction(action: () => Promise<void>): Promise<void> {
  if (policyPrompt.busy) return
  policyPrompt.busy = true; policyPrompt.error = ''
  try { await action(); await refreshInstances(); await refreshPolicy() }
  catch (error) { policyPrompt.error = (error as Error).message }
  finally { policyPrompt.busy = false }
}

/** Enables an installed, disabled requirement or recommendation, or cancels a pending disable for that plugin. */
export function enableTarget(finding: PolicyFinding): Promise<void> {
  const disablingId = policyPrompt.disabling?.id
  if (disablingId && (finding.other === disablingId || finding.target?.id === disablingId
    || state.plugins.some(item => matchesPluginIdentity(item, finding.other) && matchesPluginIdentity(item, disablingId)))) {
    policyPrompt.keepDisabling = false
    return policyAction(async () => {})
  }
  return policyAction(() => configure(finding.target!.id, finding.target!.version, true))
}
/** Disables the package a rule conflicts with. */
export const disableTarget = (finding: PolicyFinding) => policyAction(() => configure(finding.target!.id, finding.target!.version, false))

/**
 * Switches the package that declares a rule. While enabling, switching it off withdraws the request; otherwise the
 * package is switched now, which is how a launch or dependency disable proceeds cleanly.
 */
export function switchDependent(finding: PolicyFinding, enabled: boolean): Promise<void> {
  if (policyPrompt.enabling?.id === finding.plugin) {
    policyPrompt.keepEnabling = enabled
    return policyAction(async () => {})
  }
  return policyAction(() => configure(finding.plugin, finding.pluginVersion, enabled))
}

/** Whether the prompt can continue: no blocking rule remains. */
export function policyResolved(): boolean {
  return !policyPrompt.findings.some(finding => finding.blocks)
}

/** Applies the chosen installed recommendations, then releases the caller if nothing blocks any more. */
export async function continuePolicy(): Promise<void> {
  const chosen = policyPrompt.findings.filter(item => item.type === 'recommend' && item.status === 'disabled' && item.target
    && policyPrompt.selected.includes(findingKey(item)))
  if (chosen.length) await policyAction(async () => { for (const item of chosen) await configure(item.target!.id, item.target!.version, true) })
  if (policyPrompt.error || !policyResolved()) return
  close(policyPrompt.enabling ? policyPrompt.keepEnabling : policyPrompt.disabling ? policyPrompt.keepDisabling : true)
}

/** Closes the prompt without continuing. */
export function cancelPolicy(): void { close(false) }

function close(proceed: boolean) {
  policyPrompt.open = false
  policyPrompt.cards = []
  const resolve = finish; finish = undefined; resolve?.(proceed)
}
