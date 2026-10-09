import { SocialPlatform } from '../../../shared/models/platform.model';

/** Editorial states, spelled exactly as the PRD and API spell them. Delivery states never appear on a post. */
export type EditorialState = 'DRAFT' | 'IN_REVIEW' | 'CHANGES_REQUESTED' | 'APPROVED' | 'ARCHIVED';
export type PostVersionSource = 'Manual' | 'AiGeneration' | 'AiRewrite' | 'AiImage';

/** Image metadata. There is deliberately no URL: identity is the id plus content hash. */
export interface MediaAsset {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  width: number;
  height: number;
  sha256: string;
  sourceType: string;
  createdAtUtc: string;
}

/** Most images one post can carry (single image, or a LinkedIn multi-image / Facebook multi-photo / Instagram carousel post). */
export const MAX_POST_IMAGES = 4;

/** One image of a post, in publishing order; the alt text describes this image. */
export interface PostImage {
  mediaAssetId: string;
  altText: string | null;
}

export interface PostVersionImage extends PostImage {
  media: MediaAsset;
}

export interface PostContent {
  hook: string | null;
  body: string;
  callToAction: string | null;
  destinationUrl: string | null;
  hashtags: string[];
  /** 0 to MAX_POST_IMAGES images in publishing order. */
  images: PostImage[];
  /** Destination account; required before review. */
  socialAccountId: string | null;
}

export interface PostVersion extends PostContent {
  id: string;
  postId: string;
  versionNumber: number;
  socialAccountId: string | null;
  creationSource: PostVersionSource;
  createdByUserId: string;
  createdByName: string;
  createdAtUtc: string;
  images: PostVersionImage[];
}

export interface Post {
  id: string;
  organizationId: string;
  platform: SocialPlatform;
  editorialState: EditorialState;
  currentVersionId: string;
  /** The approval bound to the current version; null unless APPROVED. */
  activeApprovalId: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  rowVersion: string;
  currentVersion: PostVersion;
  /** Shared by the posts written together for several platforms (its own id otherwise); reviewed and scheduled as one item. */
  groupId: string;
}

/** Client-side mirror of the API's platform rules, for early feedback only. */
export const PLATFORM_TEXT_LIMITS: Record<SocialPlatform, number> = { LinkedIn: 3000, Facebook: 5000, Instagram: 2200 };

/** GET /posts: one row per non-archived post in the workspace, with its latest publication intent (if any). */
export interface PostListItem {
  id: string;
  platform: SocialPlatform;
  editorialState: EditorialState;
  currentVersionNumber: number;
  hook: string | null;
  bodyExcerpt: string;
  socialAccountId: string | null;
  createdByUserId: string;
  createdByName: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  latestJobId: string | null;
  latestDeliveryState: string | null;
  latestPublishAtUtc: string | null;
  /** The current version's first image (thumbnail), if any. */
  mediaAssetId?: string | null;
  imageCount?: number;
}

/** Media Library row. */
export interface MediaLibraryItem {
  asset: MediaAsset;
  usedByPostCount: number;
}

/** The post text as it will be published (hook, body, call to action, hashtags), without the separate link. */
export function postText(v: Pick<PostVersion, 'hook' | 'body' | 'callToAction' | 'hashtags'>): string {
  return [v.hook, v.body, v.callToAction, v.hashtags.length ? v.hashtags.join(' ') : null]
    .filter((part): part is string => !!part?.trim())
    .join('\n\n');
}
