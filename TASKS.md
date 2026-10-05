# TASKS.md — Social Media Content Automation Engine

## 1. Purpose

This file defines the **implementation order** for the Social Media Content Automation Engine.

It is the execution checklist for developers and coding agents.

Use it together with:

- `AGENTS.md` — coding rules
- `PRD.md` — product requirements
- `ARCHITECTURE.md` — technical structure
- `README.md` — project setup and runtime guidance

Do not implement tasks out of order when a dependency is incomplete.

Do not mark a task complete unless its exit criteria are satisfied.

---

# 2. Task Status Convention

Use:

```text
[ ] Not started
[~] In progress
[x] Complete
[!] Blocked
```

A task is complete only when:

- implementation exists
- relevant build passes
- relevant tests pass
- required migration exists
- architecture rules are preserved
- acceptance criteria are met

---

# 3. MVP Execution Strategy

The MVP must be built as one vertical slice:

```text
Workspace
   ↓
Brand
   ↓
Campaign
   ↓
AI Generation
   ↓
Post Versions
   ↓
Human Review
   ↓
Approval
   ↓
Schedule / Publish Now
   ↓
Publication Job
   ↓
Worker
   ↓
Provider / Simulation
   ↓
History + Audit
```

Do not spend the week building isolated screens that do not connect end to end.

---

# 4. Phase 0 — Project Baseline and Guardrails

## Goal

Establish repository structure and make the architecture visible to every coding agent before feature work begins.

### Tasks

- [x] Add root documentation files
  - [x] `README.md`
  - [x] `PRD.md`
  - [x] `ARCHITECTURE.md`
  - [x] `AGENTS.md`
  - [x] `TASKS.md`

- [x] Create repository structure (`frontend/` + `backend/`, see ARCHITECTURE.md §51)
  - [x] `frontend` (Angular: `core`, `shared`, `features`, `environments`, `styles`, `assets`)
  - [x] `backend/src/SocialMediaAutomation.Domain`
  - [x] `backend/src/SocialMediaAutomation.Application`
  - [x] `backend/src/SocialMediaAutomation.Infrastructure`
  - [x] `backend/src/SocialMediaAutomation.Api`
  - [x] `backend/src/SocialMediaAutomation.Worker`
  - [x] `backend/tests` (Domain, Application, Integration, Architecture test projects)
  - [x] `backend/deploy`
  - [x] `backend/scripts`
  - [x] root `docker-compose.yml`, `.env.example`, `.editorconfig`

- [x] Create base solution/project files
- [x] Add environment configuration pattern
- [x] Add local development configuration
- [x] Add `.gitignore`
- [x] Add environment template without secrets
- [x] Add base logging
- [x] Add health endpoint
- [x] Confirm application builds

## Exit Criteria

- repository structure exists
- API starts
- web app starts
- worker starts
- database configuration is reachable
- no secrets committed
- documentation files are present at repository root

> Status: all exit criteria verified. Database reachability was confirmed on 2026-10-04: `docker compose up -d`, `dotnet ef database update`, then `/health` returned `Healthy` (200).

---

# 5. Phase 1 — Day 1: Architecture Freeze and External Risk Check

## Goal

Freeze MVP assumptions before feature coding.

### Product Decisions

- [ ] Confirm one brand per workspace
- [ ] Confirm three roles
  - [ ] Admin
  - [ ] Creator
  - [ ] Reviewer

- [ ] Confirm `RequireDistinctReviewer = true` for demo
- [ ] Confirm supported social platforms
  - [ ] LinkedIn
  - [ ] Facebook
  - [ ] Instagram

- [ ] Confirm provider execution modes
  - [ ] LIVE
  - [ ] TEST_ACCOUNT
  - [ ] SIMULATED
  - [ ] NOT_CONFIGURED

- [ ] Confirm one image per PostVersion for MVP
- [ ] Confirm no video/Reels/Stories
- [ ] Confirm Publish Now uses PublicationJob
- [ ] Confirm `.NET AI worker` for MVP
- [ ] Confirm AI image generation is future scope only

### External Provider Checks

- [ ] Create/verify LinkedIn developer app
- [ ] Create/verify Meta developer app
- [ ] Record which account permissions are available
- [ ] Record which providers can be tested live
- [ ] Record which providers require simulation
- [ ] Create provider access matrix

### Provider Access Matrix Template

```text
LinkedIn:
Mode:
Account:
Permissions:
Live publish verified:
Known blocker:

Facebook:
Mode:
Page:
Permissions:
Live publish verified:
Known blocker:

Instagram:
Mode:
Professional account:
Permissions:
Live publish verified:
Known blocker:
```

## Exit Criteria

- MVP scope is frozen
- provider access risk is known
- at least one provider path is identified for live/test publishing if possible
- simulation fallback is available for blocked providers

---

# 6. Phase 2 — Identity, Workspace and Tenancy

## Goal

Create the customer data boundary before business content is added.

### Domain Tasks

- [x] Create `Organization`
- [x] Create `User` identity reference
- [x] Create `OrganizationMember`
- [x] Create role enum
- [x] Add membership status
- [x] Add workspace default timezone
- [x] Add workspace default language
- [x] Add workspace policy settings
- [x] Add `RequireDistinctReviewer`

### Database Tasks

- [x] Create initial identity/tenancy migration
- [x] Add unique Organization + User membership constraint
- [x] Add tenant-scoped indexes
- [x] Add rowversion where required

### Application Tasks

- [x] Resolve authenticated user
- [x] Resolve active workspace
- [x] Check membership
- [x] Check role/permission
- [x] Add reusable tenant authorization helper
- [x] Prevent cross-workspace resource access

### API Tasks

- [x] `GET /api/v1/me`
- [x] `GET /api/v1/organizations`
- [x] `POST /api/v1/organizations`
- [x] `GET /api/v1/organizations/{orgId}`
- [x] member listing endpoint
- [x] basic member role update endpoint for seeded/demo users

### Seed Tasks

- [x] Seed demo organization
- [x] Seed Admin
- [x] Seed Creator
- [x] Seed Reviewer
- [x] Seed second organization for tenant-isolation test

### Tests

- [x] Member can access own organization
- [x] Non-member cannot access organization
- [x] Cross-workspace resource read is denied
- [x] Cross-workspace mutation is denied
- [x] Role restrictions are enforced by backend

## Exit Criteria

- tenant isolation works
- demo users exist
- backend rejects unauthorized direct API calls
- organization context is available to all later modules

> Status: complete (2026-10-04). Sign-in uses a server-issued HttpOnly session cookie (`POST /api/v1/auth/login`). Settings update and role change require `If-Match`. Audit records for workspace creation and role changes are deferred to Phase 22 (the AuditLog table does not exist yet). Login rate limiting and anti-forgery are deferred to Phase 26.

---

# 7. Phase 3 — Brand Profile

## Goal

Create persistent brand context used by AI generation.

### Domain/Data Tasks

- [x] Create `BrandProfile`
- [x] Add OrganizationId
- [x] Add brand name
- [x] Add industry
- [x] Add business description
- [x] Add products/services
- [x] Add value proposition
- [x] Add default audience
- [x] Add brand voice traits
- [x] Add default language
- [x] Add preferred CTA style
- [x] Add preferred terms
- [x] Add prohibited terms
- [x] Add brand hashtags
- [x] Add emoji preference
- [x] Add optional website metadata
- [x] Add optional market/context notes
- [x] Add rowversion

### API Tasks

- [x] Get brand profile
- [x] Create/update brand profile
- [x] Enforce workspace ownership
- [x] Enforce stale-update conflict

### Frontend Tasks

- [x] Brand settings screen
- [x] Form validation
- [x] Save success/error state
- [x] Display current brand values

### Tests

- [x] Brand profile can be saved
- [x] Brand profile reloads
- [x] Other workspace cannot read profile
- [x] Other workspace cannot modify profile
- [x] Stale update returns conflict

## Exit Criteria

- brand profile persists
- brand data can be loaded by AI generation later
- tenant and concurrency rules work

> Status: complete (2026-10-04). Admin-only edits; all members can read. Create-or-update via `PUT /brand-profile`, with `If-Match` required once the profile exists. List fields are stored as JSON arrays with an ISJSON check. The website is stored as metadata only and is never fetched. The demo seed includes a completed profile for Organization A.

---

# 8. Phase 4 — Campaigns and Supporting Facts

## Goal

Capture the business input required for generation.

### Domain/Data Tasks

- [x] Create `Campaign`
- [x] Create `CampaignFact`
- [x] Add campaign objective
- [x] Add topic/product/service
- [x] Add key message
- [x] Add target audience
- [x] Add pain point
- [x] Add primary benefit
- [x] Add CTA
- [x] Add destination URL
- [x] Add content category
- [x] Add tone override
- [x] Add length preference
- [x] Add hashtag preference
- [x] Add emoji preference
- [x] Add angle/hook direction
- [x] Add optional start/end/embargo metadata
- [x] Add selected platform set
- [x] Add rowversion

### CampaignFact Fields

- [x] stable FactId
- [x] fact text
- [x] provenance/source note
- [x] supplied by user
- [x] created/updated timestamps

### API Tasks

- [x] list campaigns
- [x] create campaign
- [x] get campaign
- [x] update campaign
- [x] archive campaign
- [x] add/update/remove campaign fact

### Frontend Tasks

- [x] campaign list
- [x] create campaign form
- [x] campaign edit screen
- [x] supporting facts editor
- [x] platform selection
- [x] CTA/URL validation

### Tests

- [x] campaign belongs to workspace
- [x] facts belong to same workspace/campaign
- [x] invalid URL rejected
- [x] unsupported platform value rejected
- [x] stale campaign update rejected

## Exit Criteria

- Creator can create campaign
- business context persists
- facts are structured and traceable
- campaign is ready for AI generation

> Status: complete (2026-10-04). Admin and Creator manage campaigns; Reviewer is read-only. Facts and the acting user are linked through composite same-tenant foreign keys: (OrganizationId, CampaignId) → Campaigns and (OrganizationId, UserId) → OrganizationMembers. Fact removal is a soft delete so fact ids stay resolvable for later AI snapshots. Archived campaigns and their facts are read-only. The target audience falls back to the brand default when one exists. Frontend: login, workspace switcher, campaign list/create/edit, facts editor and brand settings, verified in a headless Chrome run against the real API. Audit entries for campaign and fact changes are deferred to Phase 22.

> Revision (2026-10-05, requested by the product owner; PRD §9-11 updated): simplified input model. Sign-up takes only full name, email and password; the workspace (name, industry, IANA time zone) is a separate step and carries the industry. The brand profile keeps business description, target audience, brand tone, optional website and optional prohibited terms (brand name, products/services, value proposition, voice traits, preferred terms, CTA style, hashtags, emoji preference and market notes were removed from Phase 3). Campaigns use an objective enum, one description field (replacing topic and key message), a stored target audience defaulted from the brand, a CTA enum with an https CTA URL, and optional tone/length/hashtag/emoji options with server defaults; pain point, benefit, category, angle, hook and timing fields were removed. Platforms moved to a normalized `CampaignPlatforms` table set through `PUT .../platforms`; `PUT .../facts` replaces facts one per line (unchanged lines keep their id; source note now optional). New: `GET /api/v1/onboarding/status`, onboarding state on `/me` and the login response, `GET .../campaigns/{id}/review` (resolved brief + readiness, also enforced before AI generation with `CAMPAIGN_NOT_READY_FOR_GENERATION`), `POST .../social-accounts/simulated`, campaign status DRAFT until generation is first requested, and audit entries for workspace creation, brand saves and campaign changes. Campaign creation requires the brand profile. Migration `SimplifiedInputModel` converts existing rows and is not reversible.

---

# 9. Phase 5 — Post and Immutable Version Model

## Goal

Create the content model before AI/review logic depends on it.

### Domain Tasks

- [x] Create `Post`
- [x] Create `PostVersion`
- [x] Create platform enum
- [x] Create editorial state enum

Editorial states:

- [x] DRAFT
- [x] IN_REVIEW
- [x] CHANGES_REQUESTED
- [x] APPROVED
- [x] ARCHIVED

### Post Requirements

- [x] OrganizationId
- [x] CampaignId
- [x] Platform
- [x] EditorialState
- [x] CurrentVersionId
- [x] ActiveApproval reference
- [x] rowversion

### PostVersion Requirements

- [x] OrganizationId
- [x] PostId
- [x] VersionNumber
- [x] Hook
- [x] Body/Caption
- [x] CTA
- [x] DestinationUrl
- [x] Hashtags
- [x] SocialAccountId if destination selected
- [x] MediaAssetId if attached
- [x] AltText
- [x] CreatedBy
- [x] CreationSource
- [x] CreatedAt
- [x] immutable after review

### Constraints

- [x] unique Post + VersionNumber
- [x] CurrentVersion belongs to same Post
- [x] same-tenant relationships

### Tests

- [x] Version 1 created with Post
- [x] CurrentVersion points to Version 1
- [x] new edit creates Version 2
- [x] previous version remains unchanged
- [x] cross-post version reference rejected
- [x] cross-tenant version reference rejected

## Exit Criteria

- content versions are immutable by design
- one Post exists per platform variant
- history can be displayed

> Status: complete (2026-10-04). Posts and versions are append-only. SQL triggers reject UPDATE/DELETE on PostVersions and MediaAssets. A composite FK (OrganizationId, PostId, CurrentVersionId) keeps the current version inside the same post and tenant, and a filtered unique index allows one active post per platform per campaign. Posts can also be created manually (Version 1, source Manual) so the editor works before AI generation. SocialAccountId exists on PostVersion, but its same-tenant FK and selection UI arrive in Phase 9. Editing an APPROVED post already returns it to DRAFT and clears ActiveApprovalId; the publication-job cancellation part belongs to Phase 12.

---

# 10. Phase 6 — Durable AI Generation

## Goal

Generate real platform-specific drafts without tying the user request to the LLM request lifetime.

### Data Tasks

- [x] Create `AiGenerationRequest`
- [x] Create `AiRequestOutput`
- [x] Create `AiCallLog`
- [x] Create `ContentEvaluation`

### AiGenerationRequest

Include:

- [x] OrganizationId
- [x] CampaignId
- [x] requester
- [x] input snapshot
- [x] prompt version
- [x] schema version
- [x] status
- [x] attempt count
- [x] next attempt
- [x] created/completed timestamps

### API Tasks

- [x] request generation endpoint
- [x] get generation status endpoint
- [x] get generation outputs endpoint
- [ ] enforce permission/quota placeholder
- [x] persist durable request
- [x] return accepted/pending response

### AI Worker Tasks

- [x] claim pending request
- [x] acquire lease/claim safely
- [x] load input snapshot
- [x] build generation prompt
- [x] call hosted LLM
- [x] require structured response
- [x] validate schema
- [x] validate selected platform keys
- [x] run deterministic validations
- [x] call advisory evaluator
- [x] persist evaluation
- [x] create Post per selected platform
- [x] create Version 1 per Post
- [x] persist usage/latency/model metadata
- [x] mark request complete
- [x] handle bounded failure/retry

### Deterministic Validation

- [x] required platform variants present
- [x] output schema valid
- [x] language valid
- [x] content length valid
- [x] URL rules valid
- [x] prohibited terms checked
- [x] platform media constraints checked
- [x] fact references valid where used

### Evaluation

Store:

- [x] brand alignment
- [x] audience relevance
- [x] clarity
- [x] platform fit
- [x] CTA relevance
- [x] unsupported claim warning
- [x] contradictory claim warning

### Frontend Tasks

- [x] Generate Content action
- [x] loading/pending state
- [x] generation error state
- [x] poll/refresh job status
- [x] render LinkedIn variant
- [x] render Facebook variant
- [x] render Instagram variant
- [x] show evaluation/warnings

### Tests

- [x] generation request is durable
- [x] three selected platforms create three Posts
- [x] LLM failure does not corrupt campaign
- [x] malformed model output is rejected
- [x] unsupported claim warning persists
- [x] browser/API request does not need to wait for model completion

## Exit Criteria

- real LLM generates stored variants
- outputs are structurally validated
- evaluation is visible
- AI failures are explicit

> Status: complete except the quota placeholder (2026-10-04); permission is enforced, but there is no per-workspace AI quota yet. The provider is configurable per workspace on an Admin "AI settings" page: OpenAI, Groq, or any OpenAI-compatible endpoint (https; plain http only for localhost), with a model dropdown from the provider's /models. The API key is encrypted with ASP.NET Data Protection and never returned. One OpenAI-compatible adapter uses JSON-object mode, and the application validates its own schema. The worker claims via a single UPDATE…OUTPUT with READPAST/UPDLOCK, a 5-minute lease and lease-token fencing, and retries up to 3 attempts with backoff for transient/invalid output; auth/config failures are final. Verified end to end with the real worker process against a local OpenAI-compatible mock, not with a real hosted model (no key available).

---

# 11. Phase 7 — AI Rewrite

## Goal

Allow controlled user-requested revisions without overwriting history.

### Rewrite Types

- [x] Shorten
- [x] Expand
- [x] More Professional
- [x] Improve Hook
- [x] Less Promotional
- [x] Custom Rewrite

### Backend Tasks

- [x] rewrite command
- [x] create durable AI rewrite request
- [x] include exact source version
- [x] include campaign/brand context
- [x] include rewrite instruction
- [x] run validation/evaluation
- [x] create new PostVersion
- [x] update CurrentVersionId
- [x] leave old version unchanged

### Frontend Tasks

- [x] rewrite action menu
- [x] custom instruction field
- [x] show new version
- [x] version history

### Tests

- [x] rewrite creates new version
- [x] rewrite does not mutate old version
- [x] rewritten version must be reevaluated
- [x] reviewed version remains immutable

## Exit Criteria

- Creator can use AI to modify a draft safely
- version history remains intact

> Status: complete (2026-10-04). Rewrites are durable AI requests bound to the exact source version (If-Match). If a newer version was saved in the meantime, the rewrite fails with AI_SOURCE_CHANGED instead of overwriting newer work. Each rewrite produces a new AiRewrite version that is re-evaluated; image, alt text and destination carry over.

---

# 12. Phase 8 — Media Upload

## Goal

Support one immutable image per PostVersion.

### Data Tasks

- [x] Create `MediaAsset`
- [x] OrganizationId
- [x] original filename
- [x] MIME type
- [x] size
- [x] width
- [x] height
- [x] SHA-256 hash
- [x] immutable object key
- [x] source type
- [x] created by
- [x] created at

### Storage Tasks

- [x] configure local/dev object storage
- [ ] configure staging blob storage
- [x] immutable upload path
- [x] prevent silent overwrite
- [ ] generate temporary delivery URL

### API Tasks

- [x] upload media
- [x] validate actual file type
- [x] enforce size limits
- [x] enforce platform requirements
- [x] attach MediaAsset to new PostVersion

### Frontend Tasks

- [x] upload image
- [x] image preview
- [x] alt text
- [x] replace image
- [x] clear validation error

### Tests

- [x] invalid MIME rejected
- [x] oversized file rejected
- [x] replacing image creates new version
- [x] media from another workspace rejected
- [ ] signed URL is not used as media identity

## Exit Criteria

- image upload works
- Instagram single-image requirement can be satisfied
- media identity is immutable

> Status: partially complete (2026-10-04). Done: MediaAsset, local/dev object storage (create-new only, immutable keys `organizations/{org}/media/{id}.{ext}`), byte-sniffed JPEG/PNG with dimensions and SHA-256, 8 MB limit, per-platform rules (Instagram: JPEG, 4:5 to 1.91:1), attach/replace/remove creating new versions, cross-tenant rejection (application and composite FK), editor upload/preview/alt text. Open: Azure Blob storage for staging, provider-readable temporary delivery URLs (needed at dispatch, Phase 14+), and the signed-URL identity test that depends on them. Today, media identity carries no URL at all; previews use an authenticated API endpoint.

---

# 13. Phase 9 — Social Account and Simulation Model

## Goal

Create destinations without blocking the whole product on provider approval.

### Data Tasks

- [x] Create `SocialAccount`
- [x] Create `OAuthConnection`
- [x] Create `OAuthState`

### SocialAccount Fields

- [x] OrganizationId
- [x] Provider
- [x] ExternalAccountId
- [x] AccountName
- [x] execution mode
- [x] capability metadata
- [x] connection status
- [x] last verified time

### Execution Modes

- [x] LIVE
- [x] TEST_ACCOUNT
- [x] SIMULATED
- [x] NOT_CONFIGURED

### OAuth Tasks

- [x] start provider connection
- [x] generate short-lived state
- [x] bind state to user/org/provider
- [x] validate callback
- [x] exchange code server-side
- [x] store protected token material
- [x] never return raw token to browser
- [x] disconnect/revoke flow

### Simulation Tasks

- [x] create explicit simulated destination
- [x] display SIMULATED badge in UI
- [x] do not generate fake provider IDs
- [x] do not silently fallback from LIVE to SIMULATED

### Tests

- [x] account belongs to workspace
- [x] tokens never returned by normal account API
- [x] invalid OAuth state rejected
- [x] simulation visible
- [x] not-configured destination cannot publish

## Exit Criteria

- user can select an eligible destination
- simulation mode is explicit
- provider credentials remain backend-only

> Status: complete in code, live providers unverified (2026-10-04). OAuthConnection/OAuthState with a one-time, 10-minute, SHA-256-hashed state bound to user, workspace, provider and requested mode (LIVE/TEST_ACCOUNT); anonymous callback burns the state before any provider call, re-checks membership, exchanges the code server-side (client secret in the form body), stores tokens only Data-Protection-encrypted, discovers destinations (LinkedIn member, Facebook Pages, Instagram professional accounts) with capability metadata and last-verified time, and never changes the mode of an existing destination. Disconnect wipes tokens and makes destinations ineligible without switching them to SIMULATED. Providers without app credentials are shown as blocked with a reason. **Blocked:** no LinkedIn/Meta app credentials are available, so the real authorize/token/discovery calls were verified only against stub HTTP responses and a fake gateway, not against the providers. Token refresh and long-lived Meta tokens belong to the publishing adapter phases.

---

# 14. Phase 10 — Human Review Workflow

## Goal

Make human authorization mandatory and version-specific.

### Data Tasks

- [x] Create `ReviewRequest`
- [x] Create `ReviewComment`
- [x] add review decision fields
- [x] link exact PostVersionId

### Submit Review Command

- [x] verify permission
- [x] verify current version
- [x] verify destination
- [x] verify media requirement
- [x] verify no critical blocker
- [x] verify rowversion
- [x] create ReviewRequest
- [x] set Post to IN_REVIEW
- [x] create notification
- [x] create audit

### Approval Command

- [x] verify Reviewer permission
- [x] verify exact submitted version
- [x] enforce distinct reviewer policy
- [x] verify concurrency
- [x] write decision
- [x] set active approval
- [x] set Post APPROVED
- [x] create notification
- [x] create audit
- [x] commit atomically

### Request Changes Command

- [x] require explanation
- [x] write review decision
- [x] set CHANGES_REQUESTED
- [x] create notification
- [x] create audit

### Creator Revision

- [x] create new DRAFT version
- [x] do not overwrite reviewed version
- [x] resubmit new version

### Frontend Tasks

- [x] approval queue
- [x] review detail screen
- [x] exact version display
- [x] facts display
- [x] warnings display
- [x] media preview
- [x] comments
- [x] approve button
- [x] request changes modal
- [x] show reviewer policy errors

### Tests

- [x] unreviewed content cannot be approved
- [x] wrong reviewer denied
- [x] Creator self-approval denied when policy ON
- [x] request changes requires comment
- [x] stale review decision rejected
- [x] approval references exact PostVersion
- [x] approval transaction is atomic

## Exit Criteria

- second user can review exact version
- version-bound approval is visible
- human review is enforceable server-side

> Status: complete (2026-10-04). Submission checks permission, tenant, the post concurrency token, the current version, an eligible destination of the same platform and workspace, required media and suitability, and critical blockers (latest AI evaluation of that version plus rule checks for prohibited terms and embedded URLs on every version). Approval and request-changes each commit the review decision, post state, notification and audit in one transaction; the atomicity test forces a failure inside SQL Server and verifies nothing persisted. The database enforces one pending review per post, that an approval carries its payload and hash, that changes-requested has an explanation, and (by composite FK) that the active approval belongs to the post’s current version.

---

# 15. Phase 11 — Approval Payload and Hash

## Goal

Ensure scheduled content is exactly what was approved.

### Tasks

- [x] define canonical publication payload schema
- [x] add canonical payload version
- [x] normalize field ordering
- [x] include platform
- [x] include destination account
- [x] include hook/body
- [x] include CTA
- [x] include destination URL
- [x] include hashtags
- [x] include MediaAsset identity
- [x] include media content hash
- [x] include alt text
- [x] compute payload hash
- [x] store/associate approved hash
- [x] add verification helper

### Tests

- [x] same payload produces same hash
- [x] changed caption changes hash
- [x] changed hashtag changes hash
- [x] changed account changes hash
- [x] changed media changes hash
- [x] temporary signed URL does not change hash

## Exit Criteria

- application can prove scheduled payload matches approved payload

> Status: complete (2026-10-04). `ApprovalPayload` (approval-payload-v1) is canonical JSON in a fixed field order, hashed with SHA-256, and stored with the approval. Media identity is asset id + object key + content hash; there is no URL field. `Matches(hash)` is the verification helper scheduling will use.

---

# 16. Phase 12 — Edit After Approval

## Goal

Protect approval integrity.

### Tasks

- [x] edit approved Post command
- [x] create new PostVersion
- [x] clear/revoke active approval for new version
- [x] set DRAFT
- [x] cancel safe unstarted publication job where applicable
- [x] reject edit when dispatch already started and operation is too late
- [x] audit the approval revocation

### Tests

- [x] old approved version remains unchanged
- [x] new version is DRAFT
- [x] approval does not carry over
- [x] safe scheduled job is canceled
- [x] in-flight job is preserved

## Exit Criteria

- no modified content can inherit stale approval

> Status: complete (2026-10-04). Editing approved content (manually or by AI rewrite) creates a new DRAFT version, clears the active approval and audits APPROVAL_REVOKED; the database refuses an approval that does not belong to the current version. Since Phase 13, the same unit of work cancels the old version's safe publication jobs (audited as PUBLICATION_CANCELED, reason edited), and the edit is rejected with PUBLICATION_TOO_LATE_TO_CANCEL while a job is PROCESSING, WAITING_MEDIA or RECONCILIATION_REQUIRED.

---

# 17. Phase 13 — Scheduling

## Goal

Create durable publication intent from an approved version.

### Data Tasks

- [x] Create `PublicationJob`
- [x] add unique lifetime intent constraint
- [x] add rowversion if needed
- [x] add idempotency relation

### PublicationJob Fields

- [x] OrganizationId
- [x] PostId
- [x] PostVersionId
- [x] ReviewRequest/approval identity
- [x] SocialAccountId
- [x] execution mode snapshot
- [x] frozen payload snapshot
- [x] payload hash
- [x] PublishAtUtc
- [x] original local date/time
- [x] original timezone
- [x] selected DST offset when applicable
- [x] delivery state
- [x] attempt count
- [x] max attempts
- [x] NextActionAtUtc
- [x] lease fields
- [x] external id/url
- [x] error information

### API Tasks

- [x] schedule endpoint
- [x] publish-now endpoint
- [x] cancel endpoint
- [x] reschedule endpoint where safe
- [x] publication detail endpoint
- [x] publication list endpoint

### Schedule Validation

- [x] user permission
- [x] tenant ownership
- [x] Post APPROVED
- [x] approval references exact current version
- [x] payload hash matches
- [x] eligible SocialAccount
- [x] required media
- [x] local date/time valid
- [x] timezone valid
- [x] resolve DST ambiguity
- [x] canonical UTC in future unless Publish Now
- [x] create publication intent atomically

### Tests

- [x] unapproved schedule rejected
- [x] stale approval rejected
- [x] cross-tenant account rejected
- [x] double-click creates one job
- [x] invalid local time rejected
- [x] ambiguous DST handled explicitly
- [x] Publish Now creates due PublicationJob instead of direct provider call

## Exit Criteria

- approved exact version can be scheduled durably
- repeated request does not duplicate intent

> Status: complete (2026-10-04). `PublicationJob` freezes the approved payload JSON and hash, the approval (ReviewRequest) id and the destination's execution mode, and stores PublishAtUtc with the original local time, IANA zone and resolved UTC offset. Lease/checkpoint/external-result/error fields exist for the worker (Phase 14+). Duplicate protection is the unique lifetime intent `UX_PublicationJobs_LifetimeIntent` (Organization, PostVersion, SocialAccount): a repeated identical request returns the existing job (200), a concurrent race resolves to the winner, and a different time returns `IDEMPOTENCY_CONFLICT`. There is no separate `ApiIdempotencyKeys` table; the natural key covers Schedule and Publish Now. Nonexistent DST times return `LOCAL_TIME_NONEXISTENT`; ambiguous ones return `LOCAL_TIME_AMBIGUOUS` unless the request picks one of the two offsets. Publish Now creates an immediately due SCHEDULED job (or moves an existing scheduled one forward); no provider is called. Cancel is allowed in SCHEDULED/RETRY_PENDING/ACTION_REQUIRED, reschedule in SCHEDULED/CANCELED (reactivating the same intent); in-flight states return `PUBLICATION_TOO_LATE_TO_CANCEL`. A new `SchedulePublication` permission is granted to Admin and Creator. Nothing executes the jobs yet: that is Phase 14.

---

# 18. Phase 14 — Publishing Worker Foundation

## Goal

Execute due jobs independently of the browser.

### Worker Tasks

- [x] query due jobs
- [x] use small batch
- [x] acquire lease
- [x] generate lease token
- [x] commit claim before network call
- [x] use fencing token on updates
- [x] recover expired pre-dispatch leases
- [ ] record worker heartbeat

### Dispatch Revalidation

- [x] workspace active
- [x] approval still active
- [x] exact PostVersion still eligible
- [x] destination correct
- [x] execution mode valid
- [x] payload hash correct
- [x] media exists
- [x] credentials/capability valid
- [x] late-job policy valid

### Tests

- [x] two workers cannot own same job
- [x] stale worker cannot overwrite new state
- [x] worker restart before dispatch recovers job
- [x] browser closure does not affect job

## Exit Criteria

- due jobs execute from durable SQL state
- browser is not part of execution correctness

> Status: complete except the worker heartbeat (2026-10-04). Claims are one auto-committed `UPDATE ... OUTPUT` with ROWLOCK/UPDLOCK/READPAST, batch of 5, a 5-minute lease and a per-row NEWID() lease token; every later save is fenced by the token and the row version. A dispatch checkpoint (`DispatchStartedAtUtc`) is saved before the publishing call: expired leases before it are reclaimed, after it the sweep moves the job to RECONCILIATION_REQUIRED. Revalidation checks workspace status, post/approval/current version, approval hash, destination and mode snapshot, connection status, token expiry, media row and stored file, capability, the frozen payload hash, and lateness (more than 2 hours late is ACTION_REQUIRED). Worker heartbeat is not recorded yet; the worker only logs.

---

# 19. Phase 15 — Social Publisher Adapter Contract

## Goal

Keep provider behavior isolated.

### Create Common Contract

- [x] validate capability
- [x] prepare payload
- [x] publish
- [x] get/check status when required
- [x] normalize provider error
- [x] return provider evidence

### Implement Adapters

- [x] `SimulatedPublisher`
- [x] `LinkedInPublisher`
- [x] `FacebookPublisher`
- [x] `InstagramPublisher`

### Rules

- [x] provider-specific logic remains inside adapter/integration module
- [x] no scattered provider if/else across business services
- [x] normalized error categories
- [x] normalized success result

## Exit Criteria

- worker can select publisher by provider/mode
- simulation and live adapters use same business pipeline

> Status: complete (2026-10-04). `ISocialPublisher` (CanPublish, ValidateCapabilityAsync, PrepareAsync, GetStatusAsync, PublishAsync, NormalizeError) with exactly one publisher per platform and mode; SIMULATED is served only by `SimulatedPublisher`, so a LIVE job can never fall back to simulation. Categories: RETRYABLE, ACTION_REQUIRED, UNCERTAIN, PERMANENT.

---

# 20. Phase 16 — Simulated Publishing

## Goal

Guarantee a reliable demo even when external approvals are blocked.

### Tasks

- [x] implement SimulatedPublisher
- [x] generate deterministic simulated result
- [x] end job as SIMULATED
- [x] create attempt history
- [x] create notification
- [x] create audit
- [x] show clear SIMULATED badge
- [ ] exclude from real published metric

### Tests

- [x] simulation never becomes PUBLISHED
- [x] no fake external id required
- [x] simulation still requires approved version
- [x] simulation still uses publication worker

## Exit Criteria

- full end-to-end workflow can be demonstrated without fake provider success

> Status: complete except the metric item (2026-10-04). Simulated jobs run through the same worker, revalidation and attempt history, end SIMULATED with no external id/URL (also enforced by a check constraint), and record a deterministic description of what would have been sent. Verified end to end with the real worker process. There is no published-count metric yet (Phase 23), so "exclude from real published metric" stays open.

---

# 21. Phase 17 — LinkedIn MVP Adapter

## Goal

Publish a basic authorized LinkedIn post where provider access permits.

### Tasks

- [ ] verify required product/permission
- [x] map text payload
- [x] support optional single image if feasible
- [x] use provider API version configuration
- [x] record external provider id
- [x] record provider URL when available
- [x] normalize auth failure
- [x] normalize permission failure
- [x] normalize rate limit
- [x] normalize uncertain timeout

### Tests

- [ ] test-account/live publish if credentials available
- [x] invalid auth -> ACTION_REQUIRED
- [x] safe rate limit -> RETRY_PENDING
- [x] uncertain post-send timeout -> RECONCILIATION_REQUIRED

## Exit Criteria

- real/test publish is demonstrated if provider access exists
- otherwise adapter remains wired and provider matrix documents blocker

> Status: implemented, live publishing unverified (2026-10-04). `LinkedInPublisher` posts to `/rest/posts` (LinkedIn-Version from `SocialProviders:LinkedIn:ApiVersion`, default 202509) as the connected member, with an optional image via initializeUpload + PUT, escapes the commentary text format and records the `x-restli-id` URN and feed URL. **Blocked:** no LinkedIn app credentials or approved "Share on LinkedIn" product are available, so the request shape and error mapping are verified only against stub HTTP; nothing was posted to LinkedIn.

---

# 22. Phase 18 — Facebook MVP Adapter

## Goal

Support Facebook Page text/link or single-image publishing where available.

### Tasks

- [x] verify Page destination
- [x] verify Page access capability
- [x] text/link flow
- [x] single-image flow if enabled
- [x] store external id
- [x] normalize provider error

### Tests

- [ ] live/test publish if provider access exists
- [x] permission failure handled
- [ ] invalid media handled
- [x] uncertain outcome reconciled

## Exit Criteria

- provider path works or is honestly marked blocked/simulated

> Status: implemented, live publishing unverified (2026-10-04). `FacebookPublisher` fetches the Page token with the user token (creates nothing), then posts to `/{page}/feed` (message + link) or `/{page}/photos` (multipart). Graph error codes 190/102 map to AUTH_EXPIRED, 4/17/32/613 to THROTTLED, and 10/200-range to PERMISSION_MISSING. **Blocked:** no Meta app or Page access; verified only against stub HTTP. No dedicated invalid-media test against Facebook exists yet.

---

# 23. Phase 19 — Instagram MVP Adapter

## Goal

Support one-image + caption Professional account publishing.

### Tasks

- [x] enforce image requirement
- [x] create media container
- [x] persist container id
- [x] move to WAITING_MEDIA when required
- [x] persist NextActionAtUtc
- [x] check readiness
- [x] publish container
- [x] store external media id
- [x] normalize provider errors

### Tests

- [x] no-image Instagram publish rejected
- [x] media container checkpoint survives restart
- [x] waiting does not hold DB transaction
- [x] provider success stores external id
- [x] uncertain result reconciled

## Exit Criteria

- Instagram multi-step flow is durable where provider access permits

> Status: implemented, live publishing blocked (2026-10-04). Container creation (an invisible step, so safe to repeat), a saved checkpoint (`ProviderContainerId`, `MediaContainerCreatedAtUtc`), WAITING_MEDIA with a 30-second re-check through NextActionAtUtc (never sleeping), a 30-minute processing limit, then `media_publish` and a best-effort permalink. **Blocked twice:** there is no Meta access, and Instagram fetches images from a public URL while local media storage provides none, so a LIVE Instagram job currently ends ACTION_REQUIRED (`MEDIA_URL_UNAVAILABLE`) until public object storage exists (Phase 29). The durable flow is verified with a scripted provider and the adapter against stub HTTP.

---

# 24. Phase 20 — Retry, Action Required and Reconciliation

## Goal

Make failures safe and operator-visible.

### Retry Categories

- [x] pre-send temporary failure
- [x] provider throttling
- [x] documented safe transient failure

### Action Required Categories

- [x] expired token
- [x] revoked permission
- [x] invalid account
- [x] invalid media
- [x] excessive lateness
- [x] provider capability missing

### Reconciliation Categories

- [x] timeout after dispatch
- [x] provider error after request may have succeeded
- [x] lease loss after dispatch
- [x] provider success but local persistence failed

### Tasks

- [x] create normalized error categories
- [x] create bounded retry backoff
- [x] add NextActionAtUtc
- [x] implement ACTION_REQUIRED
- [x] implement RECONCILIATION_REQUIRED
- [x] add operator-visible reason
- [ ] add manual retry only when allowed
- [x] prevent blind retry for reconciliation

### Tests

- [x] safe transient retries
- [x] retry count bounded
- [x] expired auth does not retry forever
- [x] timeout-after-send does not republish automatically
- [x] reconciliation item remains visible

## Exit Criteria

- failure path is as deliberate as success path

> Status: complete except manual retry (2026-10-04). Retries happen only for RETRYABLE failures (backoff 1, 5, then 15 minutes, at most 3 attempts, then FAILED). Before the publishing call an "uncertain" error is downgraded to retryable, because nothing can exist yet. After it, timeouts, 5xx, unexpected errors, a lost lease and a failed save after a provider success all become RECONCILIATION_REQUIRED, keeping any provider id as evidence; such jobs are never claimed again. Every pass is a `PublicationAttempt` row (step, worker, timings, result, category, code, provider request id, external id). There is no operator action yet to resolve a reconciliation or to retry an ACTION_REQUIRED job manually; editing the post (new version) or canceling an ACTION_REQUIRED job are the current ways out.

---

# 25. Phase 21 — Notifications

## Goal

Provide in-app workflow feedback.

### Notification Types

- [x] REVIEW_REQUESTED
- [x] CHANGES_REQUESTED
- [x] APPROVED
- [ ] SCHEDULED
- [x] PUBLISHED
- [x] SIMULATED
- [x] FAILED
- [x] ACTION_REQUIRED
- [ ] REAUTHORIZATION_REQUIRED

### Tasks

- [x] notification table
- [x] create notification service
- [x] unread count
- [x] notification list
- [x] mark read
- [x] link to related resource

### Tests

- [x] review creates notification
- [x] approval creates notification
- [x] publication result creates notification
- [x] notification isolated by recipient/workspace

## Exit Criteria

- user can see important workflow changes without reading logs

> Status: partially complete (2026-10-04), pulled forward because review decisions must notify atomically. Since Phases 14-20 the worker notifies the job creator of PUBLISHED, SIMULATED, FAILED, ACTION_REQUIRED and RECONCILIATION_REQUIRED outcomes (a RECONCILIATION_REQUIRED type was added). SCHEDULED and REAUTHORIZATION_REQUIRED notifications are not emitted yet.

---

# 26. Phase 22 — Audit Trail

## Goal

Create traceability of business decisions.

### Audit Events

- [ ] workspace created
- [ ] brand updated
- [ ] campaign created
- [ ] campaign fact changed
- [ ] AI generation requested
- [x] PostVersion created
- [x] review submitted
- [x] changes requested
- [x] approved
- [x] approval revoked
- [ ] scheduled
- [ ] canceled
- [ ] publication attempted
- [ ] published
- [ ] simulated
- [ ] failed
- [ ] account connected/disconnected
- [ ] role changed

### Tasks

- [x] AuditLog entity
- [x] audit service
- [x] safe metadata serialization
- [x] audit query endpoint
- [ ] manager-facing audit view

### Tests

- [x] important commands produce audit
- [x] secret/token data absent
- [ ] cross-workspace audit access denied

## Exit Criteria

- system can answer who did what and when

> Status: partially complete (2026-10-04), pulled forward because approval must audit atomically. AuditLogs are append-only (SQL trigger) and metadata keys that look like secrets are refused. Open: audit for workspace/brand/campaign/fact/AI-request/role events, the publication events, the manager-facing view, and an explicit cross-workspace audit test.

---

# 27. Phase 23 — Dashboard

## Goal

Give the manager one operational view.

### Metrics

- [x] Draft
- [x] In Review
- [x] Changes Requested
- [x] Approved
- [x] Scheduled
- [x] Published
- [x] Simulated
- [x] Failed/Action Required

### Widgets

- [x] pending reviews
- [x] upcoming publications
- [x] connection status
- [x] recent activity
- [x] unresolved failures

### Tasks

- [x] dashboard summary endpoint
- [x] dashboard frontend
- [x] separate simulated/published counts
- [x] tenant-scoped queries

## Exit Criteria

- manager can understand current workflow health from one page

> Status: complete (2026-10-04). `GET /dashboard` returns editorial counts (posts) and delivery counts (jobs) as separate maps, pending reviews, upcoming and needs-attention jobs, and destinations, all scoped to the caller's workspace. SIMULATED and PUBLISHED are separate numbers. Recent activity is the audit trail for Admins and the user's own notifications for everyone else, because the audit log stays Admin-only.

---

# 28. Phase 24 — Calendar / Publishing View

## Goal

Display scheduled and completed publication work.

### Tasks

- [x] calendar query endpoint
- [x] filter by date range
- [x] filter by platform
- [x] filter by delivery status
- [x] display local workspace timezone
- [x] show scheduled jobs
- [x] show published jobs
- [x] show simulated jobs
- [x] show failed/action-required jobs
- [x] open publication detail
- [x] safe cancel
- [x] safe reschedule
- [ ] retry/reconcile action where eligible

## Exit Criteria

- users can understand and operate publication work from one screen

> Status: complete except the retry/reconcile action (2026-10-04). `GET /publication-jobs` accepts `from`, `to`, `platform` and repeated `state`. The Calendar & publishing page groups jobs by day in the workspace time zone, shows all ten delivery states with counts, expands attempt history, and offers cancel/reschedule only where the API allows. It refreshes itself while jobs are due or in flight. There is no manual retry or reconciliation action yet (no backend command exists; see Phase 20).

---

# 29. Phase 25 — Frontend Integration Pass

## Goal

Connect the entire business journey.

### Navigation

- [x] Dashboard
- [x] Campaigns
- [x] Approvals
- [x] Calendar / Publishing
- [x] Settings

### Cross-Flow Integration

- [x] campaign -> generate
- [x] generate -> edit
- [x] edit -> attach media
- [x] destination selection
- [x] submit review
- [x] reviewer decision
- [x] version revision
- [x] approval
- [x] schedule
- [x] publication result
- [x] history
- [x] audit

### UX Requirements

- [x] loading states
- [x] empty states
- [x] error states
- [x] concurrency conflict handling
- [x] visible execution mode
- [x] visible version number
- [x] visible editorial state
- [x] visible delivery state

## Exit Criteria

- one complete vertical flow works through the UI

> Status: complete (2026-10-04). New pages: Dashboard, Calendar & publishing, and Settings with Workspace (name, language, time zone, distinct-reviewer policy), Team & roles, Brand profile, Social connections (with an execution-mode legend) and AI provider. The schedule dialog and job view are shared by the post page and the calendar. The whole journey was run in headless Chrome against the real API, SQL Server, AI worker (local mock LLM, since no real key exists) and publishing worker: campaign with fact, AI variants with warnings, edit removing an unsupported claim, Instagram image + alt text, request changes, revision, approval with comment, schedule, publish now → SIMULATED, calendar filter/reschedule/cancel, dashboard on desktop and phone width, with no page errors.

---

# 30. Phase 26 — Security Hardening

## Goal

Demonstrate that controls are backend enforced.

### Tasks

- [x] verify authentication protection
- [x] verify tenant-scoped queries
- [x] verify role/permission checks
- [x] verify anti-forgery if cookie auth used
- [x] validate request payloads
- [x] output encoding
- [x] media upload limits
- [x] rate-limit high-risk/high-cost operations where practical
- [x] redact secrets in logging
- [x] secure OAuth callback paths
- [x] provider tokens backend-only
- [x] no auth/social token in localStorage
- [x] workspace suspension blocks new dispatch

### Security Tests

- [x] cross-workspace GET denied
- [x] cross-workspace mutation denied
- [x] direct schedule of unapproved content denied
- [x] unauthorized approval denied
- [x] invalid OAuth state denied
- [x] token not present in browser response
- [x] secret absent from logs

## Exit Criteria

- backend proves business controls independently of UI

> Status: complete (2026-10-05). Fallback policy requires authentication; the session is an HttpOnly, SameSite=Strict cookie. Anti-forgery also rejects state-changing requests whose Origin is neither the API nor `App:WebBaseUrl` (no CORS is enabled). Every response carries nosniff, X-Frame-Options DENY, Referrer-Policy and a restrictive CSP; these are set when the response starts, because the exception handler cleared earlier headers on error responses (defect found and fixed). Built-in rate limiting returns 429 `RATE_LIMITED`: sign-in per client address, and AI calls, OAuth starts and uploads per user (`RateLimiting:*PerMinute`). Production enables HSTS and HTTPS redirection (`Security:RequireHttps`, `Security:TrustForwardedHeaders` behind a proxy). Request logs record the path only, never query strings (which carry OAuth codes and states). A test captures every log line during login, LLM-key save, model listing and an OAuth round trip, and proves the password, LLM key, OAuth code, state, provider tokens and "Bearer " never appear. Uploads are limited to 8 MB plus multipart overhead. Angular never binds raw HTML. localStorage holds only the selected workspace id.

---

# 31. Phase 27 — Concurrency and Idempotency Hardening

## Goal

Protect the system from duplicate clicks and stale users.

### Tasks

- [x] rowversion on mutable aggregates
- [x] If-Match/expected version support where used
- [x] stable concurrency error response
- [ ] API idempotency key store
- [x] schedule idempotency
- [x] publish-now idempotency
- [x] relevant OAuth callback idempotency
- [x] duplicate publication constraint

### Tests

- [x] two users edit stale Post
- [x] stale reviewer approval fails
- [x] duplicate schedule produces one intent
- [x] duplicate publish-now produces one intent
- [x] stale worker cannot overwrite current job

## Exit Criteria

- repeated/stale operations cannot silently corrupt workflow

> Status: complete except a generic idempotency-key store (2026-10-05). Campaign, brand, workspace, member, post, review, AI-settings and publication-job writes require If-Match and return `CONCURRENCY_CONFLICT` (409) when stale. Schedule and Publish Now are idempotent through the unique lifetime intent (Organization, PostVersion, SocialAccount): repeats and concurrent first requests return the same job. OAuth callbacks consume a one-time state, so a replayed callback is refused. There is no `ApiIdempotencyKeys` table: no endpoint currently needs one beyond the natural keys, so it was not built.

---

# 32. Phase 28 — Observability and Operations

## Goal

Make asynchronous problems diagnosable.

### Tasks

- [x] correlation ID middleware
- [x] structured logging
- [x] AI request correlation
- [x] publication job correlation
- [x] worker heartbeat
- [x] queue age metrics/logs
- [x] expired lease detection
- [x] reconciliation count
- [x] action-required count
- [x] provider throttle visibility
- [x] readiness health check
- [x] liveness health check

## Exit Criteria

- failed demo path can be diagnosed without database guessing

> Status: complete (2026-10-05). `X-Correlation-Id` is accepted when well-formed, otherwise minted. It is returned on every response (including errors), included in problem bodies, and logged as a scope on every request line. Worker log lines carry `PublicationJobId` or `AiGenerationRequestId` scopes; the first implementation used dictionary scopes that the console formatter printed as a type name, which was found in a live run and fixed. Production uses the JSON console formatter with scopes. Workers upsert a `WorkerHeartbeats` row at most every 30 s. `/health/live` checks the process only; `/health/ready` (and `/health`) checks the database and reports Degraded when the AI or publishing worker is silent for more than 2 minutes. `GET /operations` (Admin) shows worker liveness, due-queue size and oldest due time, processing, expired leases, waiting media, retry pending and throttled, action required, reconciliation required, and AI queue/failure counts; the dashboard shows it to Admins.

---

# 33. Phase 29 — Deployment

## Goal

Run the complete product in a stable demo environment.

### Environment Tasks

- [ ] development config
- [ ] staging/demo config
- [ ] secure secret config
- [ ] persistent SQL database
- [ ] blob storage
- [ ] web deployment
- [ ] API deployment
- [ ] AI worker deployment
- [ ] publishing worker deployment
- [ ] persistent data-protection/encryption keys
- [ ] correct OAuth callbacks
- [ ] HTTPS

### Database Tasks

- [ ] run migrations
- [ ] seed demo users
- [ ] seed demo organization
- [ ] seed simulation accounts
- [ ] seed optional sample campaign

### Deployment Tests

- [ ] restart API
- [ ] restart worker
- [ ] scheduled job remains
- [ ] protected token data remains readable after restart
- [ ] web/API connectivity
- [ ] provider callback connectivity

## Exit Criteria

- demo environment is stable after restart
- no local-only dependency is required for manager demo

---

# 34. Phase 30 — Acceptance Test Suite

## Goal

Prove the critical business rules.

### Required Acceptance Tests

- [x] Cross-tenant read denied
- [x] Cross-tenant mutation denied
- [x] Creator cannot self-approve when policy enabled
- [x] Critical unsupported AI claim blocks review
- [x] Stale edit returns conflict
- [x] Stale review returns conflict
- [x] Editing approved content revokes approval
- [x] Unapproved version cannot schedule
- [x] Duplicate schedule produces one intent
- [x] Two workers cannot both own a job
- [x] Worker restart before dispatch recovers job
- [x] Timeout after dispatch becomes RECONCILIATION_REQUIRED
- [x] Expired token becomes ACTION_REQUIRED
- [x] Instagram requires image
- [x] SIMULATED is never PUBLISHED
- [x] Browser closure does not stop scheduled execution

## Exit Criteria

- all critical tests pass
- any blocked external-provider test is explicitly documented

> Status: complete (2026-10-05). Run with `dotnet test backend/tests/SocialMediaAutomation.IntegrationTests --filter Category=Acceptance` (22 tests; each carries a trait naming its scenario number). Scenario 4 got a new isolated test: an AI rewrite whose only problem is an unsupported statistic blocks submission, and the same content without the claim can be submitted. The existing test only covered a prohibited term. Provider-side behaviour (timeouts, expired tokens, Instagram containers) is proven with scripted providers and the real adapters against stub HTTP; no test calls the real LinkedIn or Meta APIs (blocked: no credentials).

---

# 35. Phase 31 — Manager Demo Preparation

## Goal

Prepare one deterministic story.

### Demo Data

- [ ] Creator account
- [ ] Reviewer account
- [ ] second workspace account for denial demo
- [ ] completed BrandProfile
- [ ] sample campaign
- [ ] supporting facts
- [ ] at least one image
- [ ] social account modes clearly visible

### Rehearsal Flow

- [ ] Creator signs in
- [ ] show workspace
- [ ] show brand
- [ ] create/open campaign
- [ ] generate three platform variants
- [ ] show automated warning
- [ ] edit one variant
- [ ] attach Instagram image
- [ ] select destinations
- [ ] submit review
- [ ] switch to Reviewer
- [ ] request change
- [ ] create new version
- [ ] approve
- [ ] try invalid unapproved schedule
- [ ] show backend rejection
- [ ] schedule approved version shortly ahead
- [ ] close/refresh browser
- [ ] show worker result
- [ ] open external post if verified
- [ ] otherwise show SIMULATED result
- [ ] show version history
- [ ] show audit
- [ ] show reconciliation case
- [ ] show cross-workspace denial

## Exit Criteria

- demo works repeatedly
- presentation does not depend on improvisation
- external limitations are clearly stated

---

# 36. Seven-Day Delivery Map

## Day 1

- [x] Phase 0
- [ ] Phase 1
- [ ] provider access check
- [ ] deploy skeleton

### Day 1 Exit

Architecture frozen and external risks visible.

---

## Day 2

- [x] Phase 2
- [x] Phase 3
- [x] Phase 4
- [x] Phase 5 foundation

### Day 2 Exit

Workspace, brand and campaign data persist with tenant isolation.

---

## Day 3

- [x] Phase 6
- [x] Phase 7
- [ ] Phase 8

### Day 3 Exit

Real AI creates stored platform variants and media upload works.

---

## Day 4

- [x] Phase 10
- [x] Phase 11
- [ ] Phase 12

### Day 4 Exit

A second user can approve an exact immutable version, and editing invalidates approval.

---

## Day 5

- [ ] Phase 9 if not completed
- [ ] Phase 13
- [ ] Phase 14
- [ ] Phase 15
- [ ] Phase 16
- [ ] at least one live/test provider adapter attempt

### Day 5 Exit

Approved content flows through durable publication worker.

---

## Day 6

- [ ] Phase 17–20 where provider access allows
- [ ] Phase 21–28
- [ ] security/integration testing
- [ ] deployment hardening

### Day 6 Exit

Critical tests pass and failure paths are visible.

---

## Day 7

- [ ] feature freeze
- [ ] Phase 29
- [ ] Phase 30
- [ ] Phase 31
- [ ] bug fixes only
- [ ] manager pitch
- [ ] handover

### Day 7 Exit

Deployed, tested, rehearsed MVP.

---

# 37. Scope-Cut Order if Time Is Running Out

Cut in this order before weakening the core architecture:

1. [ ] full invitation email
2. [ ] advanced dashboard visual polish
3. [ ] rich version-diff UI
4. [ ] secondary notification polish
5. [ ] optional Facebook live integration
6. [ ] optional Instagram live integration
7. [ ] optional LinkedIn image publishing
8. [ ] non-critical audit filters
9. [ ] non-essential advanced rewrite actions

Do **not** cut:

- tenant isolation
- immutable versions
- human review
- exact approval binding
- durable publication jobs
- simulation labeling
- reconciliation for uncertain outcomes
- server-side authorization

---

# 38. AI Image Generation (moved into scope 2026-10-05; see PRD §22.1)

Originally deferred. The product owner approved it on 2026-10-05; implemented as:

- [x] Platform image configuration in `/internal/ai` (write-only key in the secret store, test without generating)
- [x] `IImageGenerationProvider` abstraction with an OpenAI-compatible implementation
- [x] Durable `ImageGeneration` AI request processed by the AI worker
- [x] `AI_GENERATED` MediaAsset with generation request, provider and model
- [x] Attach the image as a new PostVersion (`AiImage` source) that requires human review
- [ ] Moderation / safety result capture (not returned by the current provider API)

The phases below were the original plan and are kept for reference.

Future phase:

## Future Phase A — AI Image Request Model

- [ ] `ImageGenerationRequest`
- [ ] platform/aspect ratio
- [ ] visual brief
- [ ] brand visual guidance
- [ ] prohibited elements
- [ ] optional reference MediaAssets
- [ ] provider/model metadata
- [ ] usage/cost metadata

## Future Phase B — Image Provider Contract

- [ ] `IImageGenerationProvider`
- [ ] GenerateAsync
- [ ] GetResultAsync
- [ ] NormalizeError

## Future Phase C — Generated Media Persistence

- [ ] store generated bytes in Blob Storage
- [ ] create MediaAsset
- [ ] source type `AI_GENERATED`
- [ ] content hash
- [ ] dimensions
- [ ] MIME type
- [ ] provider/model provenance
- [ ] moderation/safety metadata

## Future Phase D — Governance

- [ ] attach generated image to new PostVersion
- [ ] require human review
- [ ] include MediaAsset identity in approval hash
- [ ] publish through existing PublicationJob pipeline

### Future Image Rule

```text
AI Generated Image
      ↓
MediaAsset
      ↓
New PostVersion
      ↓
Human Review
      ↓
Approval
      ↓
PublicationJob
```

Never create an image-generation shortcut directly to social publishing.

---

# 39. Explicitly Deferred — Future Python AI Extraction

Do not implement during MVP.

Consider later only if requirements justify it.

Possible triggers:

- RAG
- embeddings
- vector search
- document ingestion
- advanced NLP
- custom ML
- computer vision
- complex image generation orchestration

If extracted later, the Python service must not own:

- authorization
- tenant business state
- review
- approval
- scheduling
- social provider credentials
- publication intent

---

# 40. Final MVP Completion Checklist

The project is ready for manager delivery when all of these are true:

## Product

- [ ] centralized workspace exists
- [ ] brand profile exists
- [ ] campaign can be created
- [ ] facts can be supplied
- [ ] three platform variants can be generated

## AI

- [ ] real hosted model used
- [ ] structured output
- [ ] deterministic validation
- [ ] advisory evaluation
- [ ] rewrite creates new version
- [ ] failure is durable and visible

## Governance

- [ ] immutable PostVersions
- [ ] exact version review
- [ ] reviewer separation works
- [ ] approval cannot survive a meaningful edit

## Scheduling

- [ ] approved version only
- [ ] timezone-safe scheduling
- [ ] Publish Now uses same job pipeline
- [ ] duplicate schedule prevented

## Automation

- [ ] durable publishing worker
- [ ] lease/fencing
- [ ] browser-independent execution
- [ ] safe retry only
- [ ] reconciliation for uncertain result

## Providers

- [ ] execution mode visible
- [ ] simulation works
- [ ] at least one live/test provider path demonstrated if access permits
- [ ] provider blockers documented honestly

## Security

- [ ] tenant isolation demonstrated
- [ ] backend authorization demonstrated
- [ ] provider secrets backend-only
- [ ] no token leakage

## Operations

- [ ] notifications
- [ ] audit trail
- [ ] dashboard
- [ ] publishing/calendar view
- [ ] failure/action-required visibility

## Deployment

- [ ] migrations run cleanly
- [ ] staging/demo deployed
- [ ] workers running
- [ ] restart smoke test passed
- [ ] README setup is accurate

## Demo

- [ ] deterministic demo data
- [ ] demo rehearsed
- [ ] failure path rehearsed
- [ ] cross-tenant denial rehearsed
- [ ] manager pitch prepared

---

# 41. Final Task Rule

The coding agent should always ask:

```text
What is the next unchecked task whose prerequisites are complete?
```

Then implement only that scope.

The objective is not to finish the largest amount of code.

The objective is to finish the **correct end-to-end product**.
