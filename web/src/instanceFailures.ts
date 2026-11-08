export interface InstanceFailureDetails {
  operation?: string | null
  instanceId?: string | null
  clientPath?: string | null
  lockPath?: string | null
  osError?: number
  kind?: string
}

/** Retains the local lease context when the visible error message is translated. */
export class InstanceRequestError extends Error {
  readonly code = 'instance_in_use'
  readonly details: InstanceFailureDetails
  constructor(message: string, details: InstanceFailureDetails) {
    super(message)
    this.name = 'InstanceRequestError'
    this.details = { ...details }
  }
}

/** Recognizes an actual sharing failure rather than treating the presence of a lock file as a live owner. */
export function instanceFailure(reply: { code?: string; error?: string; instanceFailure?: InstanceFailureDetails }, translate: (key: string) => string): InstanceRequestError | undefined {
  if (reply.code !== 'instance_in_use' && reply.error !== 'instance_in_use') return
  return new InstanceRequestError(translate('instanceBusy.message'), reply.instanceFailure ?? {})
}
