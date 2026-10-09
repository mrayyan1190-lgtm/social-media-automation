import { SocialPlatform } from '../../../shared/models/platform.model';
import { ContentEvaluation } from '../../content/models/ai.model';
import { EditorialState, PostVersion } from '../../content/models/post.model';
import { SocialAccount } from '../../social-accounts/models/social-account.model';

export type ReviewStatus = 'PENDING' | 'APPROVED' | 'CHANGES_REQUESTED';

export interface ReviewSummary {
  id: string;
  postId: string;
  postVersionId: string;
  versionNumber: number;
  platform: SocialPlatform;
  /** Hook or start of the body of the reviewed version. */
  excerpt: string;
  status: ReviewStatus;
  submittedByUserId: string;
  submittedByName: string;
  assignedReviewerUserId: string | null;
  assignedReviewerName: string | null;
  submissionNote: string | null;
  submittedAtUtc: string;
  decidedByUserId: string | null;
  decidedByName: string | null;
  decidedAtUtc: string | null;
  decisionComment: string | null;
  approvedPayloadHash: string | null;
  /** The post's group: the posts of one multi-platform post are listed, approved and scheduled together. */
  groupId: string;
  rowVersion: string;
}

export interface ReviewComment {
  id: string;
  reviewRequestId: string;
  authorUserId: string;
  authorName: string;
  body: string;
  createdAtUtc: string;
}

/** Everything a reviewer needs about one exact submitted version. `canDecide` is a UX hint; the API re-checks. */
export interface ReviewDetail {
  review: ReviewSummary;
  postState: EditorialState;
  postCurrentVersionId: string;
  isCurrentVersion: boolean;
  version: PostVersion;
  destination: SocialAccount | null;
  evaluation: ContentEvaluation | null;
  comments: ReviewComment[];
  history: ReviewSummary[];
  canDecide: boolean;
  cannotDecideReason: string | null;
}

/** Shown with a SUBMISSION_BLOCKED error: how to clear unsupported claims without weakening the check. */
export const BLOCKED_HINT = 'Open the post in Composer and use Improve → Remove unsupported claims, or add the missing fact to your prompt or brand details.';
