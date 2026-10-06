import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

/** The API's RFC 7807 problem response, reduced to what the UI needs. */
export interface ApiProblem {
  status: number;
  code: string;
  detail: string;
}

const GENERIC = 'Something went wrong. Please try again in a moment.';
const SERVER = 'Something went wrong on our side. Please try again in a moment.';

/** Friendly text for responses that carry no usable message of their own. */
const STATUS_TEXT: Record<number, string> = {
  0: 'We could not reach the server. Check your connection and try again.',
  401: 'Your session has ended. Please sign in again.',
  403: 'You do not have permission to do this.',
  404: 'The requested item could not be found.',
  408: 'The request took too long. Please try again.',
  413: 'That file is too large.',
  429: 'Too many requests. Wait a moment and try again.',
};

/**
 * Turns any error into a stable code plus a sentence that is safe to show. Business errors keep the API's message
 * (written for people); server failures, network errors and technical texts (HTTP status lines, JSON paths, stack
 * traces) are replaced by plain wording, so raw error text never reaches the screen.
 */
export function toApiProblem(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse) {
    const body = typeof error.error === 'object' && error.error ? error.error : null;
    const status = error.status;
    const code: string = body?.code ?? (status === 0 ? 'NETWORK_ERROR' : status >= 500 ? 'INTERNAL_ERROR' : 'UNEXPECTED_ERROR');
    if (status >= 500 || code === 'INTERNAL_ERROR') return { status, code, detail: SERVER };

    const fieldErrors = body?.errors ? Object.values(body.errors as Record<string, string[]>).flat() : [];
    if (fieldErrors.length) {
      const readable = fieldErrors.filter((m) => !isTechnical(m));
      return { status, code, detail: readable.length ? readable.join(' ') : 'Some of the information entered is not valid. Check it and try again.' };
    }
    const detail = typeof body?.detail === 'string' && !isTechnical(body.detail) ? body.detail : (STATUS_TEXT[status] ?? GENERIC);
    return { status, code, detail };
  }
  // Messages the pages raise themselves are plain sentences; programming errors (TypeError, …) are not shown as-is.
  if (error instanceof Error && error.constructor === Error && error.message && !isTechnical(error.message))
    return { status: 0, code: 'UNEXPECTED_ERROR', detail: error.message };
  return { status: 0, code: 'UNEXPECTED_ERROR', detail: GENERIC };
}

/** Texts that are meant for developers, not for people using the app. */
function isTechnical(message: string): boolean {
  return /Http failure|https?:\/\/|\$\.|JSON|Exception|stack|at [\w.$]+ \(|SqlException|Npgsql|EntityFramework|System\.|undefined|null reference/i
    .test(message);
}

/** Row versions are sent back as a strong ETag so the API can reject stale writes. */
export function ifMatch(rowVersion: string): HttpHeaders {
  return new HttpHeaders({ 'If-Match': `"${rowVersion}"` });
}
