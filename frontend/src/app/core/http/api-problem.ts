import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

/** The API's RFC 7807 problem response, reduced to what the UI needs. */
export interface ApiProblem {
  status: number;
  code: string;
  detail: string;
}

export function toApiProblem(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    const fieldErrors = body?.errors ? Object.values(body.errors as Record<string, string[]>).flat().join(' ') : '';
    return {
      status: error.status,
      code: body?.code ?? (error.status === 0 ? 'NETWORK_ERROR' : 'UNEXPECTED_ERROR'),
      detail: fieldErrors || body?.detail || (error.status === 0 ? 'The server could not be reached.' : error.message),
    };
  }
  return { status: 0, code: 'UNEXPECTED_ERROR', detail: String(error) };
}

/** Row versions are sent back as a strong ETag so the API can reject stale writes. */
export function ifMatch(rowVersion: string): HttpHeaders {
  return new HttpHeaders({ 'If-Match': `"${rowVersion}"` });
}
