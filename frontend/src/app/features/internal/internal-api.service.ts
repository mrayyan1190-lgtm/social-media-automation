import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

// Read models of /api/internal (system administrators only). No secrets, tokens, password hashes or post content.

export type HealthStatus = 'HEALTHY' | 'DEGRADED' | 'UNAVAILABLE' | 'NOT_CONFIGURED' | 'NOT_MONITORED';

export interface Overview {
  environment: string;
  generatedAtUtc: string;
  summary: { workspaces: number; activeUsers: number; aiRequestsToday: number; publishedToday: number; failedPublicationsToday: number; reconciliationRequired: number };
  attention: { severity: 'CRITICAL' | 'WARNING' | 'INFO'; title: string; detail: string; actionLabel: string; actionRoute: string }[];
  status: { name: string; status: HealthStatus; detail: string | null }[];
  operations: {
    aiPending: number; aiProcessing: number; aiFailedToday: number;
    publishingScheduled: number; publishingProcessing: number; publishingFailedToday: number; publishingNeedsAttention: number;
  };
  recentAdminActivity: AuditLogItem[];
}

export interface WorkspaceListItem {
  id: string; name: string; primaryAdminName: string | null; primaryAdminEmail: string | null; members: number;
  socialAccounts: number; status: 'Active' | 'Suspended'; createdAtUtc: string; lastActivityAtUtc: string | null;
}

export interface WorkspaceDetail {
  id: string; name: string; timeZoneId: string; createdAtUtc: string; status: 'Active' | 'Suspended';
  usage: { members: number; posts: number; connectedSocialAccounts: number; aiRequests: number; publishedPosts: number };
  members: { userId: string; name: string; email: string; role: string; status: string }[];
  connections: { platform: string; displayName: string; status: string }[];
}

export interface UserListItem {
  id: string; name: string; email: string; isActive: boolean; isSystemAdmin: boolean; createdAtUtc: string; lastLoginAtUtc: string | null;
  workspaces: { organizationId: string; organizationName: string; role: string; status: string }[];
}

export interface UserDetail { user: UserListItem; recentSignIns: SignInLogItem[]; recentAudit: AuditLogItem[] }

export interface AiConfiguration {
  source: 'PLATFORM' | 'ENVIRONMENT' | 'NONE'; provider: string; baseUrl: string | null; model: string | null; fallbackModel: string | null;
  maxOutputTokens: number | null; requestTimeoutSeconds: number | null; enabled: boolean; apiKeyConfigured: boolean;
  apiKeyUpdatedAtUtc: string | null; updatedAtUtc: string | null; rowVersion: string | null;
  status: {
    configured: 'CONFIGURED' | 'NOT_CONFIGURED'; health: HealthStatus; defaultModel: string | null; lastSuccessAtUtc: string | null;
    lastFailureAtUtc: string | null; lastFailure: string | null; requestsToday: number; failedToday: number;
  };
  image: AiImageConfiguration;
  vision: AiVisionConfiguration;
}

/** Image understanding: its own token or a saved Hugging Face token (tokenSource says which); never carries the token. */
export interface AiVisionConfiguration {
  enabled: boolean; model: string; models: string[]; tokenSource: 'VISION_KEY' | 'LLM_KEY' | 'NONE'; usable: boolean;
  hasOwnApiKey: boolean; apiKeyUpdatedAtUtc: string | null;
}

export interface AiImageConfiguration {
  enabled: boolean; provider: string | null; baseUrl: string | null; model: string | null; size: string | null; sizes: string[];
  ownApiKey: boolean; apiKeyUpdatedAtUtc: string | null; usable: boolean; status: AiConfiguration['status'];
}

export interface AiConfigurationInput {
  provider: string; baseUrl: string | null; model: string; fallbackModel: string | null; maxOutputTokens: number | null;
  requestTimeoutSeconds: number | null; enabled: boolean; rowVersion: string | null;
}

export interface SocialProvider {
  provider: 'LINKEDIN' | 'META'; source: 'PLATFORM' | 'ENVIRONMENT' | 'NONE'; clientId: string; redirectUri: string; derivedRedirectUri: string | null; configurationId: string | null;
  apiVersion: string | null; enabled: boolean; secretConfigured: boolean; secretUpdatedAtUtc: string | null; updatedAtUtc: string | null;
  rowVersion: string | null; configurationStatus: 'CONFIGURED' | 'INCOMPLETE' | 'DISABLED' | 'NOT_CONFIGURED';
  lastSuccessAtUtc: string | null; lastFailureAtUtc: string | null; lastFailure: string | null; failuresLast24Hours: number;
}

export interface Worker {
  workerId: string; kind: 'AI' | 'PUBLISHING'; instance: string; status: 'HEALTHY' | 'STALE' | 'OFFLINE'; lastSeenAtUtc: string; currentJobs: number;
  startedAtUtc: string; uptimeSeconds: number; version: string | null;
}

export interface AiQueue {
  summary: { pending: number; processing: number; retryPending: number; failedLast24Hours: number; oldestPendingAtUtc: string | null };
  items: {
    id: string; organizationId: string; workspaceName: string; operation: string; status: string; attempts: number; maxAttempts: number;
    createdAtUtc: string; startedAtUtc: string | null; durationMs: number | null; error: string | null; correlationId: string | null;
  }[];
}

export interface Publication {
  id: string; organizationId: string; workspaceName: string; postId: string; versionNumber: number;
  platform: string; destination: string; executionMode: string; publishAtUtc: string; state: string; attempts: number; maxAttempts: number;
  worker: string | null; lastErrorCode: string | null; lastErrorMessage: string | null; correlationId: string | null; createdAtUtc: string;
}

export interface PublishingQueue {
  summary: { scheduled: number; processing: number; waitingMedia: number; retryPending: number; actionRequired: number; reconciliationRequired: number; failedLast24Hours: number };
  items: Publication[];
}

export interface PublicationDetail {
  publication: Publication; payloadHash: string; externalPostId: string | null; externalPostUrl: string | null; dispatchStartedAtUtc: string | null;
  completedAtUtc: string | null; canceledAtUtc: string | null;
  attempts: {
    id: string; attemptNumber: number; step: string; workerId: string; startedAtUtc: string; finishedAtUtc: string | null; resultState: string | null;
    errorCategory: string | null; errorCode: string | null; errorMessage: string | null; providerRequestId: string | null; externalId: string | null; detail: string | null;
  }[];
  timeline: { atUtc: string; title: string; detail: string | null; tone: string }[];
}

export interface ReconciliationCheck { verdict: string; summary: string; externalPostId: string | null; externalPostUrl: string | null; providerRequestId: string | null }

export interface HealthComponent {
  component: string; status: HealthStatus; latencyMs: number | null; lastCheckAtUtc: string | null; lastSuccessAtUtc: string | null;
  lastError: string | null; monitoring: string;
}

export interface AuditLogItem {
  id: string; atUtc: string; actor: string | null; action: string; targetType: string; target: string; workspace: string | null; result: string;
  correlationId: string | null; scope: 'PLATFORM' | 'WORKSPACE';
}

export interface SignInLogItem {
  id: string; atUtc: string; userId: string | null; userName: string | null; email: string; area: string; eventType: string; succeeded: boolean;
  ipAddress: string | null; userAgent: string | null; correlationId: string | null;
}

export interface AiLogItem {
  id: string; requestId: string; workspace: string; operation: string; model: string; succeeded: boolean; startedAtUtc: string; durationMs: number;
  inputTokens: number | null; outputTokens: number | null; attempts: number; error: string | null; correlationId: string | null;
}

export interface PublishingLogItem {
  id: string; atUtc: string; publicationId: string; workspace: string; platform: string; operation: string; status: string;
  providerRequestId: string | null; durationMs: number | null; attempt: number | null; errorCategory: string | null; error: string | null; correlationId: string | null;
}

export interface SystemLogItem { id: string; atUtc: string; level: string; service: string; event: string; message: string; correlationId: string | null }

/** Logs → All: one timeline of every log. Outcome is SUCCESS, FAILED, WARNING or INFO. */
export interface ActivityLogItem {
  id: string; atUtc: string; source: string; outcome: string; actor: string | null; workspace: string | null; event: string; message: string;
  correlationId: string | null;
}

/** Query parameters with empty values removed. */
function params(values: Record<string, string | string[] | null | undefined>): Record<string, string | string[]> {
  return Object.fromEntries(Object.entries(values).filter(([, v]) => v !== null && v !== undefined && v !== '' && !(Array.isArray(v) && !v.length))) as Record<string, string | string[]>;
}

@Injectable({ providedIn: 'root' })
export class InternalApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.internalApiBaseUrl;

  overview(): Observable<Overview> { return this.http.get<Overview>(`${this.base}/overview`); }

  workspaces(q: { search?: string; status?: string; createdFrom?: string }): Observable<WorkspaceListItem[]> {
    return this.http.get<WorkspaceListItem[]>(`${this.base}/workspaces`, { params: params(q) });
  }
  workspace(id: string): Observable<WorkspaceDetail> { return this.http.get<WorkspaceDetail>(`${this.base}/workspaces/${id}`); }
  workspaceAudit(id: string): Observable<AuditLogItem[]> { return this.http.get<AuditLogItem[]>(`${this.base}/workspaces/${id}/audit`); }
  suspendWorkspace(id: string): Observable<WorkspaceDetail> { return this.http.post<WorkspaceDetail>(`${this.base}/workspaces/${id}/suspend`, {}); }
  reactivateWorkspace(id: string): Observable<WorkspaceDetail> { return this.http.post<WorkspaceDetail>(`${this.base}/workspaces/${id}/reactivate`, {}); }

  users(q: { search?: string; status?: string; workspaceId?: string; createdFrom?: string }): Observable<UserListItem[]> {
    return this.http.get<UserListItem[]>(`${this.base}/users`, { params: params(q) });
  }
  user(id: string): Observable<UserDetail> { return this.http.get<UserDetail>(`${this.base}/users/${id}`); }
  disableUser(id: string): Observable<UserDetail> { return this.http.post<UserDetail>(`${this.base}/users/${id}/disable`, {}); }
  enableUser(id: string): Observable<UserDetail> { return this.http.post<UserDetail>(`${this.base}/users/${id}/enable`, {}); }

  aiConfiguration(): Observable<AiConfiguration> { return this.http.get<AiConfiguration>(`${this.base}/ai/configuration`); }
  saveAiConfiguration(input: AiConfigurationInput): Observable<AiConfiguration> { return this.http.put<AiConfiguration>(`${this.base}/ai/configuration`, input); }
  replaceAiKey(value: string): Observable<AiConfiguration> { return this.http.put<AiConfiguration>(`${this.base}/ai/api-key`, { value }); }
  saveAiImage(input: { provider: string; baseUrl: string | null; model: string; size: string; enabled: boolean; rowVersion: string | null }): Observable<AiConfiguration> {
    return this.http.put<AiConfiguration>(`${this.base}/ai/image-configuration`, input);
  }
  replaceAiImageKey(value: string): Observable<AiConfiguration> { return this.http.put<AiConfiguration>(`${this.base}/ai/image-api-key`, { value }); }
  replaceAiVisionKey(value: string): Observable<AiConfiguration> { return this.http.put<AiConfiguration>(`${this.base}/ai/vision-api-key`, { value }); }
  testAiImage(input: { provider: string; baseUrl: string | null; model: string | null; apiKey: string | null }): Observable<{ succeeded: boolean; message: string; modelAvailable: boolean; modelCount: number }> {
    return this.http.post<{ succeeded: boolean; message: string; modelAvailable: boolean; modelCount: number }>(`${this.base}/ai/image-test`, input);
  }
  saveAiVision(input: { model: string; enabled: boolean; rowVersion: string | null }): Observable<AiConfiguration> {
    return this.http.put<AiConfiguration>(`${this.base}/ai/vision-configuration`, input);
  }
  testAiVision(model: string): Observable<{ succeeded: boolean; message: string; modelAvailable: boolean; modelCount: number }> {
    return this.http.post<{ succeeded: boolean; message: string; modelAvailable: boolean; modelCount: number }>(`${this.base}/ai/vision-test`, { model });
  }
  testAi(input: { provider: string; baseUrl: string | null; model: string | null; apiKey: string | null }): Observable<{ succeeded: boolean; message: string; modelAvailable: boolean; modelCount: number }> {
    return this.http.post<{ succeeded: boolean; message: string; modelAvailable: boolean; modelCount: number }>(`${this.base}/ai/test`, input);
  }

  socialProviders(): Observable<SocialProvider[]> { return this.http.get<SocialProvider[]>(`${this.base}/social-providers`); }
  saveSocialProvider(provider: 'linkedin' | 'meta', input: { clientId: string; redirectUri: string | null; configurationId: string | null; enabled: boolean; rowVersion: string | null }): Observable<SocialProvider> {
    return this.http.put<SocialProvider>(`${this.base}/social-providers/${provider}`, input);
  }
  replaceProviderSecret(provider: 'linkedin' | 'meta', value: string): Observable<SocialProvider> {
    return this.http.put<SocialProvider>(`${this.base}/social-providers/${provider}/secret`, { value });
  }
  testSocialProvider(provider: 'linkedin' | 'meta'): Observable<{ valid: boolean; message: string }> {
    return this.http.post<{ valid: boolean; message: string }>(`${this.base}/social-providers/${provider}/test`, {});
  }

  workers(): Observable<Worker[]> { return this.http.get<Worker[]>(`${this.base}/workers`); }
  aiQueue(): Observable<AiQueue> { return this.http.get<AiQueue>(`${this.base}/queues/ai`); }
  publishingQueue(): Observable<PublishingQueue> { return this.http.get<PublishingQueue>(`${this.base}/queues/publishing`); }

  publications(q: { workspaceId?: string; platform?: string; from?: string; to?: string; status?: string[] }): Observable<Publication[]> {
    return this.http.get<Publication[]>(`${this.base}/publications`, { params: params(q) });
  }
  publication(id: string): Observable<PublicationDetail> { return this.http.get<PublicationDetail>(`${this.base}/publications/${id}`); }

  reconciliation(): Observable<Publication[]> { return this.http.get<Publication[]>(`${this.base}/reconciliation`); }
  checkReconciliation(id: string): Observable<ReconciliationCheck> { return this.http.post<ReconciliationCheck>(`${this.base}/reconciliation/${id}/check`, {}); }
  confirmPublished(id: string, externalPostId: string | null, externalPostUrl: string | null): Observable<PublicationDetail> {
    return this.http.post<PublicationDetail>(`${this.base}/reconciliation/${id}/confirm-published`, { externalPostId, externalPostUrl });
  }
  confirmFailed(id: string, reason: string): Observable<PublicationDetail> {
    return this.http.post<PublicationDetail>(`${this.base}/reconciliation/${id}/confirm-failed`, { reason });
  }

  systemHealth(): Observable<HealthComponent[]> { return this.http.get<HealthComponent[]>(`${this.base}/system-health`); }

  allLog(q: Record<string, string>): Observable<ActivityLogItem[]> { return this.http.get<ActivityLogItem[]>(`${this.base}/logs/all`, { params: params(q) }); }
  auditLog(q: Record<string, string>): Observable<AuditLogItem[]> { return this.http.get<AuditLogItem[]>(`${this.base}/logs/audit`, { params: params(q) }); }
  signInLog(q: Record<string, string>): Observable<SignInLogItem[]> { return this.http.get<SignInLogItem[]>(`${this.base}/logs/signins`, { params: params(q) }); }
  aiLog(q: Record<string, string>): Observable<AiLogItem[]> { return this.http.get<AiLogItem[]>(`${this.base}/logs/ai`, { params: params(q) }); }
  publishingLog(q: Record<string, string>): Observable<PublishingLogItem[]> { return this.http.get<PublishingLogItem[]>(`${this.base}/logs/publishing`, { params: params(q) }); }
  systemLog(q: Record<string, string>): Observable<SystemLogItem[]> { return this.http.get<SystemLogItem[]>(`${this.base}/logs/system`, { params: params(q) }); }
}

// ---- Shared presentation helpers ----

export const HEALTH_LABELS: Record<string, string> = {
  HEALTHY: 'Healthy', DEGRADED: 'Degraded', UNAVAILABLE: 'Unavailable', NOT_CONFIGURED: 'Not Configured', NOT_MONITORED: 'Not Monitored',
  STALE: 'Stale', OFFLINE: 'Offline', CONFIGURED: 'Configured', INCOMPLETE: 'Incomplete', DISABLED: 'Disabled',
};

/** Badge tone for any operational status. */
export function tone(status: string | null | undefined): string {
  switch (status) {
    case 'HEALTHY': case 'CONFIGURED': case 'PUBLISHED': case 'COMPLETED': case 'SUCCESS': case 'Active': case 'LOGIN_SUCCEEDED': return 'success';
    case 'DEGRADED': case 'STALE': case 'INCOMPLETE': case 'WARNING': case 'RETRY_PENDING': case 'ACTION_REQUIRED': case 'WAITING_MEDIA': return 'waiting';
    case 'UNAVAILABLE': case 'OFFLINE': case 'FAILED': case 'FAILURE': case 'RECONCILIATION_REQUIRED': case 'ERROR': case 'CRITICAL': case 'Suspended': return 'danger';
    case 'SIMULATED': return 'simulated';
    default: return 'neutral';
  }
}

/** Short human time ("3 min ago", "2 h ago", or a date). */
export function ago(utc: string | null | undefined): string {
  if (!utc) return '—';
  const ms = Date.now() - Date.parse(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`);
  const minutes = Math.round(ms / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  return when(utc);
}

export function when(utc: string | null | undefined): string {
  if (!utc) return '—';
  return new Date(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

export function words(code: string | null | undefined): string {
  return (code ?? '').toLowerCase().replace(/_/g, ' ').replace(/^\w/, (c) => c.toUpperCase());
}
