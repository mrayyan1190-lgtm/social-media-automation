import { BrandTone } from '../../settings/models/brand-profile.model';
import { ContentLength, EmojiMode, HashtagMode, SocialPlatform } from '../../campaigns/models/campaign.model';

export type AiRequestStatus = 'PENDING' | 'PROCESSING' | 'COMPLETED' | 'FAILED';
export type RewriteAction = 'Rewrite' | 'Regenerate' | 'Shorten' | 'Expand' | 'MoreProfessional' | 'ImproveHook' | 'LessPromotional' | 'Custom';

/** Composer AI edit actions. Each one saves a new version; nothing is overwritten. */
export const REWRITE_ACTIONS: { value: RewriteAction; label: string }[] = [
  { value: 'Rewrite', label: 'Rewrite' },
  { value: 'Shorten', label: 'Shorten' },
  { value: 'Expand', label: 'Expand' },
  { value: 'ImproveHook', label: 'Improve Hook' },
  { value: 'MoreProfessional', label: 'More Professional' },
  { value: 'LessPromotional', label: 'Less Promotional' },
];

/** AI Studio input. Brand description, audience and tone are added by the server from the brand profile. */
export interface GenerationInput {
  prompt: string;
  platforms: SocialPlatform[];
  campaignId: string | null;
  /** Null uses the brand tone. */
  tone: BrandTone | null;
  length: ContentLength;
  hashtags: HashtagMode;
  emojis: EmojiMode;
  /** Uploaded images for a vision model to read (the first is attached to the new post). */
  mediaAssetIds?: string[];
  visionModel?: string | null;
  /** Text + Image: attach the first uploaded image, or (without uploads) generate one with `imageModel`. */
  withImage?: boolean;
  imageModel?: string | null;
  /** "All platforms": one shared post, identical on every platform (still one draft per platform to review). */
  unified?: boolean;
}

/** What a PROCESSING request is doing right now. */
export type AiRequestStage = 'ANALYZING_IMAGES' | 'WRITING' | 'GENERATING_IMAGE';

/** Which AI actions are available; the model lists are the Hugging Face models a user may pick (empty = no choice). */
export interface AiCapabilities {
  textGeneration: boolean;
  imageGeneration: boolean;
  imageAnalysis: boolean;
  imageModels: string[];
  visionModels: string[];
}

export interface AiGenerationRequest {
  id: string;
  campaignId: string | null;
  kind: 'Generation' | 'Rewrite' | 'ImageGeneration';
  platforms: SocialPlatform[];
  postId: string | null;
  sourceVersionId: string | null;
  rewriteAction: RewriteAction | null;
  status: AiRequestStatus;
  stage: AiRequestStage | null;
  /** Completed, but an optional part (the image) did not work. */
  warning: string | null;
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
