/** Stable API error codes (backend: Domain/Constants/ErrorCodes.cs). Compare codes, never English messages. */
export const ErrorCodes = {
  Unauthorized: 'UNAUTHORIZED',
  Forbidden: 'FORBIDDEN',
  NotFound: 'NOT_FOUND',
  ValidationFailed: 'VALIDATION_FAILED',
  ConcurrencyConflict: 'CONCURRENCY_CONFLICT',
  InvalidStateTransition: 'INVALID_STATE_TRANSITION',
} as const;
