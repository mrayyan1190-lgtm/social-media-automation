import { PostListItem } from '../content/models/post.model';
import { AuditEntry } from '../dashboard/dashboard-api.service';

/** Helpers shared by the Admin home and the Creator home. */

export interface Activity {
  id: string;
  actor: string;
  text: string;
  at: string;
}

/** Business events in plain language; technical events are left out. Shared with the Admin home. */
export function fromAudit(e: AuditEntry, byId: Map<string, PostListItem>): Activity | null {
  const post = e.resourceType === 'Post' ? byId.get(e.resourceId) : null;
  const postName = post ? `a ${post.platform} post` : 'a post';
  const text = ((): string | null => {
    switch (e.action) {
      case 'WORKSPACE_CREATED': return 'created the workspace';
      case 'BRAND_PROFILE_SAVED': return 'updated the brand profile';
      case 'CAMPAIGN_CREATED': return 'created a campaign';
      case 'CAMPAIGN_UPDATED': return 'updated a campaign';
      case 'POST_VERSION_CREATED': return e.metadata['source']?.startsWith('Ai') ? `generated ${postName}` : `edited ${postName}`;
      case 'REVIEW_SUBMITTED': return 'submitted content for review';
      case 'CONTENT_APPROVED': return `approved ${postName}`;
      case 'CHANGES_REQUESTED': return `requested changes on ${postName}`;
      case 'PUBLICATION_SCHEDULED': return `scheduled ${postName}`;
      case 'PUBLICATION_PUBLISHED': return `published ${postName}`;
      case 'PUBLICATION_FAILED': return `could not publish ${postName}`;
      case 'SOCIAL_CONNECTION_CONNECTED': return 'connected a social account';
      case 'MEMBER_ADDED': return 'added a team member';
      default: return null;
    }
  })();
  return text ? { id: e.id, actor: e.actorName ?? 'Someone', text, at: e.createdAtUtc } : null;
}

/** API timestamps are UTC, sometimes without a zone suffix. */
export function utcMs(utc: string): number {
  return Date.parse(/(Z|[+-]\d{2}:\d{2})$/i.test(utc) ? utc : `${utc}Z`);
}

/** One of the six design illustrations, picked from the id so a post always gets the same one. */
export function art(id: string): number {
  let h = 0;
  for (const c of id) h = (h * 31 + c.charCodeAt(0)) | 0;
  return Math.abs(h) % 6;
}

/**
 * The overview ring (also the Creator home's content-flow ring): one continuous gradient through the legend colours. Each colour peaks at the middle of its share
 * (sized by its count) and fades straight into the next, with no solid bands, also across the top where the last meets the first.
 */
export function ringFill(counts: [number, string][]): string {
  const parts = counts.filter(([n]) => n > 0);
  const sum = parts.reduce((t, [n]) => t + n, 0);
  if (!sum) return '#EAEAF4';
  if (parts.length === 1) return parts[0][1];
  const stops: string[] = [];
  let start = 0;
  for (const [n, color] of parts) {
    const width = (n / sum) * 100;
    stops.push(`${color} ${(start + width / 2).toFixed(2)}%`);
    start += width;
  }
  const seam = `color-mix(in srgb, ${parts[parts.length - 1][1]}, ${parts[0][1]})`;
  return `conic-gradient(${seam} 0%, ${stops.join(', ')}, ${seam} 100%)`;
}
