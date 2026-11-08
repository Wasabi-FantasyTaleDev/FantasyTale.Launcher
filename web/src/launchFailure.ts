import type { LaunchCleanupFailure } from './bridge'

/**
 * Extracts and formats a Windows NTSTATUS error code from client crash exit codes.
 *
 * Windows Status Encoding:
 * In Windows NTSTATUS codes, bits 31:30 represent the severity level:
 * - 0b00: Success
 * - 0b01: Informational
 * - 0b10: Warning
 * - 0b11: Error (0xC0000000 - 0xFFFFFFFF, such as STATUS_ACCESS_VIOLATION 0xC0000005)
 *
 * Only error-severity values are displayed as NTSTATUS. The code is an observed
 * exit status, not proof of the failure's cause (e.g. '0xC0000005').
 */
export function clientFailureCode(failure: LaunchCleanupFailure): string | undefined {
  const code = failure.exitCode
  if (!failure.exitConfirmed || code == null || !Number.isInteger(code) || code < -0x80000000 || code > 0xffffffff) return
  const status = code >>> 0
  if (status >>> 30 !== 3) return
  return `0x${status.toString(16).toUpperCase().padStart(8, '0')}`
}
