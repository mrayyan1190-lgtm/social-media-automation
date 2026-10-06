/** Human labels for workflow states. Editorial state (Post) and delivery state (PublicationJob) are always shown separately. */
export const EDITORIAL_LABELS: Record<string, string> = {
  DRAFT: 'Draft',
  IN_REVIEW: 'In Review',
  CHANGES_REQUESTED: 'Changes Requested',
  APPROVED: 'Approved',
  ARCHIVED: 'Archived',
};

export const DELIVERY_LABELS: Record<string, string> = {
  SCHEDULED: 'Scheduled',
  PROCESSING: 'Publishing',
  WAITING_MEDIA: 'Publishing',
  RETRY_PENDING: 'Publishing',
  ACTION_REQUIRED: 'Needs Attention',
  RECONCILIATION_REQUIRED: 'Needs Checking',
  PUBLISHED: 'Published',
  SIMULATED: 'Demo (not sent)',
  FAILED: 'Failed',
  CANCELED: 'Canceled',
};

export const DELIVERY_STATES = Object.keys(DELIVERY_LABELS);

/** Delivery states that still lead to a publication (shown as "Scheduled" to customers). */
export const UPCOMING_DELIVERY = ['SCHEDULED', 'PROCESSING', 'WAITING_MEDIA', 'RETRY_PENDING'];
/** Delivery states that need a person (shown as "Failed" / "Needs Attention"). */
export const PROBLEM_DELIVERY = ['FAILED', 'ACTION_REQUIRED', 'RECONCILIATION_REQUIRED'];

/** Customer wording for a destination's execution mode; the internal enum names are never shown. */
export const MODE_LABELS: Record<string, string> = {
  LIVE: 'Connected',
  TEST_ACCOUNT: 'Test account',
  SIMULATED: 'Demo Mode',
  NOT_CONFIGURED: 'Not connected',
};

/** Shown wherever a demo destination appears, so a demo is never mistaken for a real publication. */
export const DEMO_MODE_NOTE = 'Demo Mode — posts are not sent externally';

/** The seven customer-facing post statuses (Planner, All Posts, Home). */
export type CustomerStatus = 'Draft' | 'In Review' | 'Changes Requested' | 'Approved' | 'Scheduled' | 'Published' | 'Demo (not sent)' | 'Failed';
export const CUSTOMER_STATUSES: CustomerStatus[] = ['Draft', 'In Review', 'Changes Requested', 'Approved', 'Scheduled', 'Published', 'Failed'];

/**
 * One status per post for lists: the delivery outcome when there is a live publication intent, otherwise the editorial
 * state. A demo delivery is never called "Published". Display only; both states stay separate in the data.
 */
export function customerStatus(editorialState: string, deliveryState: string | null | undefined): CustomerStatus {
  if (deliveryState && UPCOMING_DELIVERY.includes(deliveryState)) return 'Scheduled';
  if (deliveryState && PROBLEM_DELIVERY.includes(deliveryState)) return 'Failed';
  if (deliveryState === 'PUBLISHED') return 'Published';
  if (deliveryState === 'SIMULATED') return 'Demo (not sent)';
  return (EDITORIAL_LABELS[editorialState] as CustomerStatus) ?? 'Draft';
}

/** The filter a status belongs to: a demo delivery is listed under Published but always labelled as a demo. */
export function statusGroup(status: CustomerStatus): CustomerStatus {
  return status === 'Demo (not sent)' ? 'Published' : status;
}

export function statusTone(status: string): string {
  switch (status) {
    case 'Published':
    case 'Approved':
      return 'success';
    case 'Demo (not sent)':
      return 'simulated';
    case 'Failed':
    case 'Changes Requested':
      return 'danger';
    case 'Scheduled':
    case 'In Review':
      return 'waiting';
    default:
      return 'neutral';
  }
}

/** CSS tone for a delivery state badge: success, simulated, danger, waiting or neutral. */
export function deliveryTone(state: string): string {
  switch (state) {
    case 'PUBLISHED':
      return 'success';
    case 'SIMULATED':
      return 'simulated';
    case 'FAILED':
    case 'ACTION_REQUIRED':
    case 'RECONCILIATION_REQUIRED':
      return 'danger';
    case 'SCHEDULED':
    case 'PROCESSING':
    case 'WAITING_MEDIA':
    case 'RETRY_PENDING':
      return 'waiting';
    default:
      return 'neutral';
  }
}

/**
 * A customer-language explanation of a publishing problem. Internal causes (leases, provider status codes, worker
 * errors) are translated into what the person can do next.
 */
export function publishingProblem(deliveryState: string, errorCode: string | null, platform: string): string {
  if (deliveryState === 'RECONCILIATION_REQUIRED')
    return `We couldn't confirm whether this post went out. Check your ${platform} page before trying again; it will not be re-sent automatically.`;
  const code = (errorCode ?? '').toUpperCase();
  if (code.includes('REAUTH') || code.includes('TOKEN') || code.includes('AUTH') || code.includes('NOT_READY') || code.includes('PERMISSION'))
    return `Your ${platform} account needs to be reconnected. Reconnect it in Social Accounts, then schedule the post again.`;
  if (code.includes('MEDIA') || code.includes('IMAGE'))
    return `${platform} could not use the attached image. Replace the image, get the new version approved, and schedule again.`;
  if (code.includes('APPROVAL') || code.includes('STALE'))
    return 'The approved content changed before publishing. Get the latest version approved, then schedule again.';
  if (code.includes('LATE'))
    return 'The scheduled time passed before the post could be sent. Schedule it again.';
  if (deliveryState === 'ACTION_REQUIRED')
    return `${platform} needs your attention before this post can be published. Check the account in Social Accounts.`;
  return `${platform} did not accept this post. Open it to review the content, then schedule it again.`;
}
