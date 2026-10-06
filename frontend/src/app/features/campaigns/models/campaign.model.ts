export type SocialPlatform = 'LinkedIn' | 'Facebook' | 'Instagram';
export const SOCIAL_PLATFORMS: SocialPlatform[] = ['LinkedIn', 'Facebook', 'Instagram'];

export type ContentLength = 'SHORT' | 'MEDIUM' | 'LONG';
export type HashtagMode = 'AUTO' | 'NONE';
export type EmojiMode = 'AUTO' | 'NONE';

export type CampaignStatus = 'Draft' | 'Active' | 'Archived';

/** Preset swatches for the colour picker; any #RRGGBB is accepted. */
export const CAMPAIGN_COLORS = ['#2563EB', '#7C3AED', '#DB2777', '#DC2626', '#EA580C', '#CA8A04', '#16A34A', '#0D9488', '#475569'];

/** A campaign is only a folder for related posts. */
export interface CampaignInput {
  name: string;
  color: string;
  description: string | null;
}

export interface Campaign extends CampaignInput {
  id: string;
  organizationId: string;
  status: CampaignStatus;
  createdByUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  rowVersion: string;
}

export interface CampaignSummary {
  id: string;
  name: string;
  color: string;
  description: string | null;
  status: CampaignStatus;
  postCount: number;
  updatedAtUtc: string;
  createdByUserId: string;
}
