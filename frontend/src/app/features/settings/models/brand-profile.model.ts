export type BrandTone = 'Professional' | 'Friendly' | 'Casual' | 'Confident' | 'Informative' | 'Conversational';
export const BRAND_TONES: BrandTone[] = ['Professional', 'Friendly', 'Casual', 'Confident', 'Informative', 'Conversational'];

export interface BrandProfileInput {
  businessDescription: string;
  targetAudience: string;
  brandTone: BrandTone;
  website: string | null;
  prohibitedTerms: string[];
}

export interface BrandProfile extends BrandProfileInput {
  id: string;
  organizationId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  rowVersion: string;
}
