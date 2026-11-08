import type { LauncherUpdateAttempt, LauncherUpdateState } from '../bridge'

const reasons = new Set(['timeout', 'serverAccess', 'feedMissing', 'serverBusy', 'serverError', 'serverReply', 'serverNotFound',
  'secureConnection', 'connection', 'networkUnknown', 'invalidInformation', 'localStorage', 'unknown', 'feedOutdated', 'feedChanged'])
/** Never render exception messages or server-controlled text as discovery guidance. */
export function updateFailureReason(update: LauncherUpdateState): string {
  return update.failureReason && reasons.has(update.failureReason) ? update.failureReason :
    update.failure === 'network' ? 'networkUnknown' : update.failure === 'disk' ? 'localStorage' : 'unknown'
}
export function updateFailureKey(update: LauncherUpdateState): string { return `launcherUpdates.reasons.${updateFailureReason(update)}` }

const attemptStatuses = new Set(['failed', 'rolled-back', 'recovery-required'])
const attemptPhases = new Set(['handoff', 'staged-verification', 'parent-exit', 'installation-lock', 'recovery', 'staging',
  'replacement', 'frontend-startup', 'installed-verification', 'commit'])
/** Only fixed operation names and a signed 32-bit HRESULT may enter copied installation diagnostics. */
export function updateAttemptDetails(attempt: LauncherUpdateAttempt | null, t: (key: string) => string): string[] {
  if (!attempt || !attemptStatuses.has(attempt.status)) return []
  const lines = [`${t('launcherUpdateDiagnostics.lastAttempt')}: ${t(`launcherUpdateDiagnostics.${attempt.status}`)}`]
  if (attempt.failure && ['disk', 'network', 'integrity', 'startup'].includes(attempt.failure))
    lines.push(t(attempt.failure === 'startup' ? 'launcherUpdates.startup' : `clientUpdates.${attempt.failure}`))
  if (attempt.phase && attemptPhases.has(attempt.phase))
    lines.push(`${t('launcherUpdateDiagnostics.phase')}: ${t(`launcherUpdateDiagnostics.phases.${attempt.phase}`)}`)
  if (typeof attempt.errorCode === 'number' && Number.isInteger(attempt.errorCode) && attempt.errorCode >= -2147483648 && attempt.errorCode <= 2147483647)
    lines.push(`HRESULT: 0x${(attempt.errorCode >>> 0).toString(16).padStart(8, '0').toUpperCase()}`)
  return lines
}
