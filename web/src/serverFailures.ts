export interface ServerFailureDetails {
  code?: string; httpStatus?: number | null; transportCode?: string | null; reason?: string | null
  maintenanceStart?: string | null; maintenanceEnd?: string | null; maintenanceDescription?: string | null
}
export class ServerRequestError extends Error {
  constructor(message: string, readonly details: ServerFailureDetails) { super(message); this.name = 'ServerRequestError' }
}
const codes = new Set(['wrong_password', 'account_not_found', 'account_banned', 'server_full', 'server_maintenance', 'server_not_ready', 'not_whitelisted', 'invalid_credentials', 'password_too_short', 'network_error', 'network_timeout', 'http_error', 'launcher_update_required'])
/**
 * Translates machine-readable server errors into user-facing localized Error instances.
 *
 * Invariant:
 * Only explicit error codes belonging to the defined contract (`codes`) are converted
 * into `ServerRequestError` containing structured diagnostics (HTTP status, transport code,
 * maintenance windows). Unrecognized errors return undefined so caller fallback logic
 * handles them cleanly.
 */
export function serverFailure(message: ServerFailureDetails & { error: string }, translate: (key: string) => string): Error | undefined {
  if (!message.code || !codes.has(message.code)) return undefined
  const details: ServerFailureDetails = { code: message.code, httpStatus: message.httpStatus, transportCode: message.transportCode,
    reason: message.reason, maintenanceStart: message.maintenanceStart, maintenanceEnd: message.maintenanceEnd, maintenanceDescription: message.maintenanceDescription }
  let text = translate(`serverFailure.${message.code}`)
  if (message.reason) text += `\n${message.reason}`
  if (message.maintenanceDescription) text += `\n${message.maintenanceDescription}`
  if (message.maintenanceStart) text += `\n${translate('serverFailure.maintenanceStart')}: ${message.maintenanceStart}`
  if (message.maintenanceEnd) text += `\n${translate('serverFailure.maintenanceEnd')}: ${message.maintenanceEnd}`
  return new ServerRequestError(text, details)
}
