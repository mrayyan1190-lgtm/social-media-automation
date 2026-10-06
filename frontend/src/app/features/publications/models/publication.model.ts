import { SocialPlatform } from '../../campaigns/models/campaign.model';
import { ExecutionMode } from '../../social-accounts/models/social-account.model';

export type DeliveryState =
  | 'SCHEDULED'
  | 'PROCESSING'
  | 'WAITING_MEDIA'
  | 'RETRY_PENDING'
  | 'ACTION_REQUIRED'
  | 'RECONCILIATION_REQUIRED'
  | 'PUBLISHED'
  | 'SIMULATED'
  | 'FAILED'
  | 'CANCELED';

/** One durable publication intent. canCancel/canReschedule are hints; the API re-checks every command. */
export interface PublicationJob {
  id: string;
  postId: string;
  postVersionId: string;
  versionNumber: number;
  campaignId: string | null;
  campaignName: string | null;
  platform: SocialPlatform;
  socialAccountId: string;
  destinationName: string;
  executionMode: ExecutionMode;
  deliveryState: DeliveryState;
  publishAtUtc: string;
  localPublishAt: string | null;
  timeZoneId: string | null;
  utcOffsetMinutes: number | null;
  payloadHash: string;
  attemptCount: number;
  maxAttempts: number;
  externalPostId: string | null;
  externalPostUrl: string | null;
  lastErrorCode: string | null;
  lastErrorMessage: string | null;
  createdAtUtc: string;
  canceledAtUtc: string | null;
  canCancel: boolean;
  canReschedule: boolean;
  rowVersion: string;
}

/** Wall-clock input; the server resolves the canonical UTC instant. */
export interface LocalSchedule {
  localDate: string; // yyyy-MM-dd
  localTime: string; // HH:mm:ss
  timeZone: string; // IANA
  utcOffsetMinutes: number | null; // only to pick one occurrence of an ambiguous DST time
}

/** One worker pass over a job: outcome, normalized error and provider evidence (never tokens). */
export interface PublicationAttempt {
  id: string;
  attemptNumber: number;
  step: 'DISPATCH' | 'MEDIA_STATUS';
  startedAtUtc: string;
  finishedAtUtc: string | null;
  resultState: DeliveryState | null;
  errorCategory: 'RETRYABLE' | 'ACTION_REQUIRED' | 'UNCERTAIN' | 'PERMANENT' | null;
  errorCode: string | null;
  errorMessage: string | null;
  providerRequestId: string | null;
  externalId: string | null;
  detail: string | null;
}

/** True while the worker may change the job soon (due or in flight), so screens should refresh it. */
export function isActive(job: PublicationJob, now = Date.now()): boolean {
  if (['PROCESSING', 'WAITING_MEDIA', 'RETRY_PENDING'].includes(job.deliveryState)) return true;
  const due = Date.parse(/(Z|[+-]\d{2}:\d{2})$/i.test(job.publishAtUtc) ? job.publishAtUtc : `${job.publishAtUtc}Z`);
  return job.deliveryState === 'SCHEDULED' && due <= now + 60_000;
}
