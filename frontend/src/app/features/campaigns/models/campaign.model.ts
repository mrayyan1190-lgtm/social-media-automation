import { BrandTone } from '../../settings/models/brand-profile.model';

export type SocialPlatform = 'LinkedIn' | 'Facebook' | 'Instagram';
export const SOCIAL_PLATFORMS: SocialPlatform[] = ['LinkedIn', 'Facebook', 'Instagram'];

export type CampaignObjective =
  | 'AUTO' | 'BRAND_AWARENESS' | 'PRODUCT_LAUNCH' | 'LEAD_GENERATION' | 'ENGAGEMENT' | 'WEBSITE_TRAFFIC' | 'ANNOUNCEMENT' | 'EDUCATIONAL';
export const CAMPAIGN_OBJECTIVES: CampaignObjective[] = [
  'AUTO', 'BRAND_AWARENESS', 'PRODUCT_LAUNCH', 'LEAD_GENERATION', 'ENGAGEMENT', 'WEBSITE_TRAFFIC', 'ANNOUNCEMENT', 'EDUCATIONAL',
];

export type CallToActionType =
  | 'AUTO' | 'NONE' | 'LEARN_MORE' | 'VISIT_WEBSITE' | 'BOOK_DEMO' | 'SIGN_UP' | 'CONTACT_US' | 'REGISTER_NOW' | 'DOWNLOAD';
export const CALL_TO_ACTIONS: CallToActionType[] = [
  'AUTO', 'NONE', 'LEARN_MORE', 'VISIT_WEBSITE', 'BOOK_DEMO', 'SIGN_UP', 'CONTACT_US', 'REGISTER_NOW', 'DOWNLOAD',
];
/** UX hint only; the API decides which CTAs need a link. */
export const ctaNeedsUrl = (cta: CallToActionType) => cta !== 'AUTO' && cta !== 'NONE' && cta !== 'CONTACT_US';

/** "Auto Detect" lets the AI infer the objective or CTA from the campaign prompt. */
export const OBJECTIVE_LABELS: Record<CampaignObjective, string> = {
  AUTO: 'Auto Detect', BRAND_AWARENESS: 'Brand Awareness', PRODUCT_LAUNCH: 'Product Launch', LEAD_GENERATION: 'Lead Generation',
  ENGAGEMENT: 'Engagement', WEBSITE_TRAFFIC: 'Website Traffic', ANNOUNCEMENT: 'Announcement', EDUCATIONAL: 'Educational',
};

export const CTA_LABELS: Record<CallToActionType, string> = {
  AUTO: 'Auto Detect', NONE: 'No CTA', LEARN_MORE: 'Learn More', VISIT_WEBSITE: 'Visit Website', BOOK_DEMO: 'Book a Demo',
  SIGN_UP: 'Sign Up', CONTACT_US: 'Contact Us', REGISTER_NOW: 'Register Now', DOWNLOAD: 'Download',
};

/** Default CTA wording for a post line; mirrors the server's defaults (empty for NONE). */
export const CTA_TEXT: Record<CallToActionType, string> = {
  AUTO: '', NONE: '', LEARN_MORE: 'Learn more', VISIT_WEBSITE: 'Visit our website', BOOK_DEMO: 'Book a demo', SIGN_UP: 'Sign up',
  CONTACT_US: 'Contact us', REGISTER_NOW: 'Register now', DOWNLOAD: 'Download',
};

export type ContentLength = 'SHORT' | 'MEDIUM' | 'LONG';
export const CONTENT_LENGTHS: ContentLength[] = ['SHORT', 'MEDIUM', 'LONG'];
export type HashtagMode = 'AUTO' | 'NONE';
export type EmojiMode = 'AUTO' | 'NONE';

export type CampaignStatus = 'Draft' | 'Active' | 'Archived';

/** Basics, details and optional advanced options. Platforms and facts are saved through their own endpoints. */
export interface CampaignInput {
  name: string;
  objective: CampaignObjective;
  description: string;
  /** Null uses the brand profile's audience. */
  targetAudience: string | null;
  callToAction: CallToActionType;
  callToActionUrl: string | null;
  /** Null uses the brand tone. */
  toneOverride: BrandTone | null;
  contentLength: ContentLength;
  hashtagMode: HashtagMode;
  emojiMode: EmojiMode;
}

export interface CampaignFact {
  id: string;
  campaignId: string;
  text: string;
  sourceNote: string | null;
  suppliedByUserId: string;
  suppliedByName: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  rowVersion: string;
}

export interface Campaign extends CampaignInput {
  id: string;
  organizationId: string;
  targetAudience: string;
  platforms: SocialPlatform[];
  status: CampaignStatus;
  createdByUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  rowVersion: string;
  facts: CampaignFact[];
}

export interface CampaignSummary {
  id: string;
  name: string;
  objective: CampaignObjective;
  platforms: SocialPlatform[];
  status: CampaignStatus;
  factCount: number;
  updatedAtUtc: string;
}

export interface CampaignFactInput {
  text: string;
  sourceNote: string | null;
}

export interface ReadinessGap {
  code: string;
  message: string;
}

/** The resolved brief AI generation would receive, and whether it can start. */
export interface CampaignBrief {
  campaign: { id: string; name: string; objective: CampaignObjective; description: string; targetAudience: string; status: CampaignStatus };
  brandContext: { businessName: string; industry: string; businessDescription: string; brandTone: BrandTone; website: string | null } | null;
  callToAction: { type: CallToActionType; url: string | null };
  supportingFacts: string[];
  platforms: SocialPlatform[];
  preferences: { tone: BrandTone | null; length: ContentLength; hashtags: HashtagMode; emojis: EmojiMode };
  readyForGeneration: boolean;
  missingRequirements: ReadinessGap[];
}
