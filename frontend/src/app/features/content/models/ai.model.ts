import { SocialPlatform } from '../../campaigns/models/campaign.model';

export type AiRequestStatus = 'PENDING' | 'PROCESSING' | 'COMPLETED' | 'FAILED';
export type RewriteAction = 'Shorten' | 'Expand' | 'MoreProfessional' | 'ImproveHook' | 'LessPromotional' | 'Custom';

export const REWRITE_ACTIONS: { value: RewriteAction; label: string }[] = [
  { value: 'Shorten', label: 'Shorten' },
  { value: 'Expand', label: 'Expand' },
  { value: 'MoreProfessional', label: 'Make Professional' },
  { value: 'ImproveHook', label: 'Improve Hook' },
  { value: 'LessPromotional', label: 'Less Promotional' },
  { value: 'Custom', label: 'Custom Rewrite' },
];

export interface AiGenerationRequest {
  id: string;
  campaignId: string;
  kind: 'Generation' | 'Rewrite' | 'ImageGeneration';
  platforms: SocialPlatform[];
  postId: string | null;
  sourceVersionId: string | null;
  rewriteAction: RewriteAction | null;
  status: AiRequestStatus;
  attemptCount: number;
  maxAttempts: number;
  failureCode: string | null;
  failureMessage: string | null;
  provider: string;
  model: string;
  createdAtUtc: string;
  completedAtUtc: string | null;
  outputs: { platform: SocialPlatform; postId: string; postVersionId: string }[];
}

export type IssueSeverity = 'INFO' | 'WARNING' | 'CRITICAL';

export interface EvaluationIssue {
  severity: IssueSeverity;
  category: string;
  message: string;
  source: 'DETERMINISTIC' | 'AI';
  excerpt: string | null;
}

/** Advisory only: scores and issues inform the human reviewer; they never approve or reject. */
export interface ContentEvaluation {
  postVersionId: string;
  brandAlignment: number | null;
  audienceRelevance: number | null;
  clarity: number | null;
  platformFit: number | null;
  ctaRelevance: number | null;
  summary: string | null;
  issues: EvaluationIssue[];
  hasCriticalIssue: boolean;
  evaluatorModel: string | null;
  createdAtUtc: string;
}

export const isActive = (request: AiGenerationRequest | null): boolean =>
  request?.status === 'PENDING' || request?.status === 'PROCESSING';
