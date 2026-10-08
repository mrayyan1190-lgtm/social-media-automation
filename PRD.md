# PRD.md — Social Media Content Automation Engine

## 1. Document Purpose

This Product Requirements Document defines **what the one-week MVP must build, why it exists, who it serves, what business rules must hold, and what counts as done**.

This file is the product source of truth for implementation.

Coding agents and developers must not invent additional product scope when this document is silent. If a requirement conflicts with `ARCHITECTURE.md`, the product intent in this PRD remains authoritative while the implementation approach should follow the architecture document.

---

# 2. Product Name

**Social Media Content Automation Engine**

---

# 3. Product Summary

The Social Media Content Automation Engine is a centralized SaaS workspace that helps a business move from a campaign brief to approved and scheduled social content.

The MVP supports:

- LinkedIn
- Facebook
- Instagram

The product combines:

1. Business and brand context
2. AI-assisted content generation
3. Platform-specific adaptation
4. Human review and approval
5. Immutable content versioning
6. Scheduling and publish-now workflows
7. Background publishing
8. Clear live, test, simulated, and failed outcomes
9. Auditability from creation to publication

The product is **not** simply an AI caption generator.

Its core value is controlling the complete business workflow:

**Brand context → Campaign brief → AI-assisted drafts → Human review → Exact version approval → Durable publication job → Social platform result → History and audit**

---

# 4. Product Problem

Marketing teams often manage social content through fragmented processes.

Typical problems include:

- Writing similar content separately for multiple platforms
- Re-entering the same brand and campaign information repeatedly
- Copying content manually between tools
- Sending drafts for approval through chat or email
- Losing track of which exact version was approved
- Manually remembering when content must be posted
- Repeating publishing attempts without knowing whether the previous attempt succeeded
- Having no reliable audit trail for edits, approval, scheduling, or publication
- Mixing real provider results with mocked or assumed success
- Allowing AI-generated content to move too close to publication without human control

The MVP solves this by putting content creation, approval, scheduling, publishing, and history into one governed workflow.

---

# 5. Product Goal

The one-week MVP must prove one complete and reliable vertical slice:

> A user creates a campaign, AI generates platform-specific drafts, a human reviews an exact version, an approved version is scheduled or published through a durable job, and the system records the real or simulated result without bypassing governance.

The product should demonstrate both:

- useful AI automation
- deterministic business controls

---

# 6. Primary Product Principles

## 6.1 AI assists; business rules decide

AI may:

- generate content
- adapt content for a platform
- evaluate content
- suggest improvements
- rewrite content when requested

AI must not:

- approve content
- change permissions
- select or expose credentials
- create publication jobs by itself
- decide whether a user may publish
- invent unsupported facts to repair a warning

## 6.2 Human review is mandatory

No real or simulated publication job may be created from content that has not passed the required human approval workflow.

## 6.3 Approval applies to an exact version

Approval does not apply to a Post in general.

It applies to a specific immutable version of:

- destination platform
- destination account
- hook
- body/caption
- CTA
- destination URL
- hashtags
- media identity
- media alt text
- canonical approved payload

If any meaningful public-facing content changes, a new version is created and prior approval is no longer valid for that new version.

## 6.4 Provider truth must be visible

The system must never present a simulated action as a real external publication.

Every connected destination must clearly operate in one of these modes:

- `LIVE`
- `TEST_ACCOUNT`
- `SIMULATED`
- `NOT_CONFIGURED`

## 6.5 The database is the source of truth

Browser state must never be treated as authoritative for:

- approval
- scheduling
- publication status
- user permissions
- external publishing results

Workers continue independently after the browser closes.

---

# 7. MVP Users

The MVP has three workspace roles.

## 7.1 Admin

The Admin can:

- manage workspace settings
- manage brand settings
- manage workspace members
- manage social connections
- create campaigns
- create/edit content
- review content
- approve content when policy allows
- schedule approved content
- publish approved content
- inspect history and operational results

The Admin must not bypass:

- immutable version rules
- approval requirements
- tenant boundaries
- audit logging

## 7.2 Creator

The Creator can:

- create campaigns
- enter campaign facts
- generate AI content
- edit drafts
- request AI rewrites
- add or replace draft media
- select destinations
- submit content for review
- schedule an already approved version
- publish an already approved version
- cancel or reschedule jobs when cancellation is still safe

The Creator cannot approve their own submitted content when `RequireDistinctReviewer` is enabled.

## 7.3 Reviewer

The Reviewer can:

- see pending review requests
- inspect the exact submitted version
- inspect campaign intent and supporting facts
- inspect automated warnings
- comment
- request changes
- approve eligible versions

A Reviewer must not silently edit wording and approve the edited wording as though the original submitted version was reviewed.

---

# 8. Workspace and Tenancy Requirements

The workspace is the customer data boundary.

A user may have a global identity, but business data belongs to an organization/workspace.

The system must ensure that:

- campaigns belong to one workspace
- brand data belongs to one workspace
- social accounts belong to one workspace
- posts and versions belong to one workspace
- review requests belong to one workspace
- media belongs to one workspace
- publication jobs belong to one workspace
- notifications belong to one workspace
- audit records belong to one workspace

Knowing or guessing a resource ID must never grant access.

Every business request must validate:

1. authenticated user
2. active workspace membership
3. required permission
4. resource ownership by the same workspace

A cross-workspace request must be denied by the backend.

---

# 9. Onboarding Requirements

The MVP onboarding journey is intentionally small.

A user should be able to:

1. Sign up with only full name, email and password, or sign in
2. Create a workspace (business name, industry, IANA timezone; language defaults to English) or select one;
   the creator becomes its Admin
3. Configure one brand profile
4. Connect at least one social destination through the real provider sign-in (OAuth); onboarding offers no simulated
   shortcut and needs provider app credentials on the server (this never blocks the campaign API)
5. Use seeded Creator and Reviewer demo accounts

The backend reports onboarding progress and the next step (workspace, brand profile, campaigns) so the frontend never
guesses it. Campaigns can be created once the brand profile exists.

Full production invitation email delivery is not required for the one-week MVP.

The product must not block campaign drafting merely because a real social provider connection is unavailable.

---

# 10. Brand Profile Requirements

The brand profile is configured once per workspace for the MVP.

It is deliberately small. Required:

- business description
- target audience (the default for campaigns)
- brand tone (Professional, Friendly, Casual, Confident, Informative, Conversational)

Optional:

- website
- prohibited terms (checked deterministically on every version before review)

The business name, industry and content language belong to the workspace.

The website field is metadata only in the MVP.

The product must not imply that the website has been scraped, verified, or used as a factual source unless such a feature is explicitly added later.

---

# 11. Campaign Requirements

A Creator creates a campaign before generating content.

Each campaign should capture the business intent needed by the AI layer.

## 11.1 Required campaign inputs

At minimum:

- campaign name
- objective (BRAND_AWARENESS, PRODUCT_LAUNCH, LEAD_GENERATION, ENGAGEMENT, WEBSITE_TRAFFIC, ANNOUNCEMENT, EDUCATIONAL)
- description: what the campaign promotes or communicates (topic, main message and product/service context in one field)
- target audience (defaults to the brand profile's audience; stored on the campaign so it can be overridden)
- at least one selected social platform before AI generation

## 11.2 Optional campaign inputs

- supporting information, one fact per line
- CTA (NONE, LEARN_MORE, VISIT_WEBSITE, BOOK_DEMO, SIGN_UP, CONTACT_US, REGISTER_NOW, DOWNLOAD) and an https CTA URL,
  required when the CTA sends the reader to a link
- advanced options with server-side defaults: tone override (default: brand tone), content length (default MEDIUM),
  hashtags AUTO/NONE (default AUTO), emojis AUTO/NONE (default AUTO)

Campaign status is DRAFT until generation is first requested, then ACTIVE; ARCHIVED makes it read-only.

## 11.2.1 Generation readiness

Before generation the backend assembles the resolved campaign brief and reports whether it is ready and what is
missing. A campaign is ready when the brand profile exists, name, objective, description and target audience are set,
at least one platform is selected and a required CTA URL is a valid https URL. Supporting facts and connected social
accounts are not required.

## 11.3 Campaign facts

The campaign can include supporting business facts.

A fact should include:

- stable fact identity
- fact text
- optional source/provenance note
- human supplier/owner

Desired marketing outcomes must not be treated as factual proof.

For example:

> “We want customers to save 50% of reporting time”

is not valid evidence that customers actually save 50%.

---

# 12. Social Platform Selection

The MVP supports:

- LinkedIn
- Facebook
- Instagram

When the user selects multiple platforms, the system creates one independently managed Post per platform.

Example:

- LinkedIn Post
- Facebook Post
- Instagram Post

The system must not store all platform content as three text columns inside one single Post record.

Each platform variant needs its own:

- content
- version history
- review state
- selected destination
- schedule
- publication outcome

---

# 13. AI Generation Requirements

The user should provide business information rather than write model prompts.

The system is responsible for prompt orchestration.

## 13.1 Generation behavior

When generation is requested, the product must:

1. validate user permission
2. validate workspace
3. validate campaign input
4. validate selected platforms
5. create a durable AI generation request
6. capture an input snapshot
7. generate one independently written draft for each selected platform
8. run deterministic validation
9. run one advisory AI evaluation
10. persist generated versions and evaluation results

The user must not need to keep the browser open while the AI worker completes the durable request.

## 13.2 Platform adaptation

The three outputs must not be simple copies of one master caption.

They should share the same factual campaign message but adapt communication style.

LinkedIn should generally favor:

- professional framing
- business value
- readable paragraphs
- restrained hashtags

Facebook should generally favor:

- accessible conversational language
- broad readability
- clear CTA

Instagram should generally favor:

- stronger opening line
- shorter visual-friendly copy
- relevant hashtag use
- compatibility with the selected media requirement

These are editorial tendencies, not permission or workflow rules.

---

# 14. AI Validation Requirements

Before a generated variant is treated as a usable draft, deterministic checks should validate:

- expected platform variants exist
- output follows the required schema
- output language is valid
- output length fits the application rules
- URL format is valid
- prohibited terms are checked
- required platform/media conditions are checked
- source fact references are valid when used

The AI evaluator should provide advisory checks such as:

- brand alignment
- audience relevance
- clarity
- platform fit
- CTA relevance
- unsupported claim risk
- contradictory claim risk

Critical automated issues may block submission to human review.

Stylistic suggestions should not automatically block review.

---

# 15. Unsupported Claims

The system must not silently repair unsupported claims by inventing new evidence.

Examples that should be treated carefully include:

- statistics
- prices
- discounts
- certifications
- customer results
- testimonials
- awards
- partnerships
- guarantees
- rankings
- performance claims

If a generated statement is not supported by supplied facts/context, the system should flag it.

The final authority remains the human reviewer.

---

# 16. AI Rewrite Requirements

The MVP does not automatically run an unlimited repair loop.

A user may explicitly request a rewrite.

Supported rewrite actions may include:

- Shorten
- Expand
- More Professional
- Improve Hook
- Less Promotional
- Custom Rewrite

Every rewrite creates a new candidate content version.

The old version must remain available in history.

The new version must be evaluated again before it is submitted for approval.

---

# 17. Post and Version Requirements

A Post represents one platform-specific content item.

A PostVersion represents one immutable saved public-facing version.

The Post should point to its current version.

Version history must remain readable.

A new version must be created for meaningful public-facing changes such as:

- hook change
- body/caption change
- CTA change
- destination URL change
- hashtag change
- platform destination account change
- media replacement
- alt text change
- AI rewrite
- manual rewrite after review feedback

Previously reviewed versions must never be overwritten.

---

# 18. Editorial States

The Post uses an editorial state.

Required MVP states:

- `DRAFT`
- `IN_REVIEW`
- `CHANGES_REQUESTED`
- `APPROVED`
- `ARCHIVED`

The main workflow is:

`DRAFT → IN_REVIEW → APPROVED`

or:

`DRAFT → IN_REVIEW → CHANGES_REQUESTED → new DRAFT version → IN_REVIEW`

Publishing delivery status does not belong on the Post.

Publishing delivery status belongs to PublicationJobs.

---

# 19. Review Requirements

## 19.1 Submission

A current draft can be submitted for review only when:

- the current version is valid
- required media exists
- the selected destination belongs to the same workspace
- the selected destination mode is eligible
- no critical automated blocker remains

Submitting creates a review request tied to the exact version.

## 19.2 Reviewer view

The reviewer should see:

- campaign name
- campaign objective
- selected platform
- selected destination
- exact content
- media preview
- version number
- supporting facts
- automated warnings
- reviewer comments/history

## 19.3 Review decisions

A Reviewer can:

- Approve
- Request Changes

Request Changes requires an explanation.

Approval records:

- reviewer
- exact version
- decision
- timestamp

## 19.4 Distinct reviewer policy

The workspace has:

`RequireDistinctReviewer`

When enabled, the submitter or last meaningful human editor of the version cannot approve that same version.

**One-week MVP decision (Admin and Creator only):** the person who signs up is the workspace Admin. The Admin creates
Creator logins on the Members page (full name, email, password; no invitation email) and hands the credentials over.
Creators use only Home, AI Studio, Composer and Planner: they generate, write and edit drafts and submit them for
review, but cannot approve, schedule, publish, connect accounts or manage members. The Admin approves or requests
changes and schedules or publishes. The Reviewer role is not offered in the web app. New workspaces start with
`RequireDistinctReviewer` disabled so an Admin may also approve their own posts; the approval-settings switch is
hidden. Human approval of an exact version is still required before any publication job.

---

# 20. Approval Integrity Requirements

The exact approved publication payload includes:

- platform
- destination account
- hook
- body/caption
- CTA
- destination URL
- hashtags
- media asset identity
- media alt text
- canonical payload/renderer version

The product should be able to verify that the payload being scheduled or dispatched still matches the approved payload.

If an approved version is changed, approval must not carry over.

---

# 21. Editing Approved Content

If a user tries to edit an APPROVED Post:

- prior approved version remains immutable
- active approval is revoked for the new version
- a new DRAFT version is created
- any safe, unstarted publication job tied to the old approved state is canceled when appropriate

If external dispatch has already started, editing/cancellation may be rejected as too late.

The in-flight publication history must remain intact.

---

# 22. Media Requirements

A content version carries 0 to 4 images in a fixed order, each with its own alt text (scope change, approved 2026-10-08; previously one image). The order and every image are part of the approved payload; reordering, adding or removing an image creates a new version.

Upload should validate:

- actual content type
- allowed file type
- file size
- dimensions where required

Media metadata should be stored separately from the immutable media object.

Media identity should include:

- media asset ID
- immutable object identity
- content hash

Replacing the image creates a new content version.

A temporary signed delivery URL is not part of immutable approval identity.

## 22.1 AI Image Generation (scope change, approved 2026-10-05)

AI image generation is now part of the product, under the same governance as text:

- A platform operator configures the image provider in `/internal/ai` (provider, model, size, optional separate API key, enabled). Customers never configure it.
- Composer offers three AI modes (approved 2026-10-07): **Text**, **Text + Image** (the user may pick one of the platform's Hugging Face image models: Z-Image-Turbo default, FLUX.1-schnell, SANA 1.5) and **From images** (up to 4 uploaded images are read by a Hugging Face vision model the user picks: Qwen3-VL-4B default, GLM-4.5V, Qwen2.5-VL-3B; the first image is attached to the post, one image per post). Users choose only from these fixed lists; the token stays platform-configured. Every result is a draft that needs human review.
- "Create with AI" (AI Studio, approved 2026-10-07): **Text** writes the posts. **Text + Image** without uploads has the LLM also write the image prompt (the user never writes one), which the chosen image model renders; the image is saved in the same drafts. With uploaded reference images (up to 4) a vision model reads them with the prompt; **Text** attaches nothing, **Text + Image** attaches the first uploaded image and generates none. Model choices live in Advanced Options. If only the image fails, the drafts are still saved and the user sees why.
- Supported image providers: OpenAI, OpenAI-compatible endpoints, and Hugging Face (suggested models: Z-Image-Turbo as default, FLUX.1-schnell, SANA 1.5 1.6B; a Hugging Face token is required).
- A Creator or Admin asks for an image for one post ("Generate Image", with an optional style direction). The request is durable and runs in the AI worker.
- The image is validated from its bytes, stored as an `AI_GENERATED` MediaAsset (with request, provider and model recorded), and attached as a **new PostVersion** with the text unchanged and empty alt text.
- An AI image is never approved automatically: the new version needs human review, and replacing the image on approved content revokes the approval like any other edit.
- The image prompt forbids text, numbers, prices, logos, real people and implied product claims, so the image cannot add unsupported facts.

---

# 23. Social Account Requirements

Each social destination belongs to a workspace.

A SocialAccount must clearly expose its operating mode:

- `LIVE`
- `TEST_ACCOUNT`
- `SIMULATED`
- `NOT_CONFIGURED`

The UI must show this mode clearly.

Drafting should remain possible without a live provider connection.

A post may proceed through review and scheduling using `SIMULATED` mode so the complete business workflow can still be demonstrated.

---

# 24. Provider MVP Targets

## 24.1 LinkedIn

Target:

- text post
- optional image, or a 2-4 image multi-image post
- authorized member or organization destination

## 24.2 Facebook

Target:

- Page text/link post
- or single photo Page post, or a 2-4 photo Page post

## 24.3 Instagram

Target:

- Professional account
- single image or a 2-4 image carousel
- caption

Video, Reels, Stories, advertising, and direct-message automation are not MVP requirements.

---

# 25. Scheduling Requirements

Scheduling is allowed only for an eligible approved current version.

The user selects:

- local date
- local time
- IANA timezone

The server is responsible for resolving the canonical UTC execution time.

The system must safely handle:

- invalid/nonexistent local times
- ambiguous daylight-saving transitions

The product should preserve:

- canonical UTC time
- original local time
- original timezone

---

# 26. Publish Now Requirements

Publish Now must use the same durable publication-job pipeline as scheduled publication.

It must not bypass:

- approval checks
- payload verification
- destination checks
- audit
- publication history
- duplicate protection

“Publish Now” means creating an immediately due publication job.

It does not mean calling the provider directly from the browser request.

---

# 27. Publication Job Requirements

A publication job should represent one lifetime publication intent for:

- one workspace
- one approved PostVersion
- one SocialAccount

It should preserve a frozen snapshot of the approved publication payload.

The job should include product-level information such as:

- approved version identity
- review approval identity
- destination account
- execution mode snapshot
- publish time
- payload hash
- attempt policy
- external/provider result when available

Changing the SocialAccount's current mode later must not rewrite historical job truth.

---

# 28. Delivery States

PublicationJobs use delivery states.

The MVP must support the functional meaning of:

- `SCHEDULED`
- `PROCESSING`
- `WAITING_MEDIA`
- `RETRY_PENDING`
- `ACTION_REQUIRED`
- `RECONCILIATION_REQUIRED`
- `PUBLISHED`
- `SIMULATED`
- `FAILED`
- `CANCELED`

`PUBLISHED` means confirmed external success for a real/test provider destination.

`SIMULATED` means the simulation adapter completed the workflow.

A simulated job must never be reported as externally published.

---

# 29. Retry and Failure Requirements

The system must distinguish safe retry from uncertain external outcome.

Automatic retry may occur only when the failure is known to be safe, such as:

- failure before external send
- confirmed throttling where retry is appropriate
- clearly retryable provider/network condition with no external side effect

The system must not blindly retry when a publication may already have happened.

Examples that should enter `RECONCILIATION_REQUIRED` include:

- timeout after dispatch
- provider error after create may have succeeded
- worker lease lost after dispatch
- provider success followed by local persistence failure

The product must never claim perfect exactly-once delivery across external providers.

Its goal is controlled, effectively-once behavior with reconciliation when outcome is uncertain.

---

# 30. Background Automation Requirements

Scheduled or immediately due publication must execute independently of the browser.

Closing or refreshing the browser must not stop a scheduled job.

Workers must process durable work from authoritative server-side state.

The UI should show operational outcomes rather than pretending that clicking Schedule means publication is complete.

---

# 31. Notifications

The MVP should support simple in-app notifications for useful workflow events such as:

- review requested
- changes requested
- content approved
- content scheduled
- content published
- content simulated
- publication failed
- action required
- social account reauthorization required

Email notification delivery is not required for the one-week MVP.

---

# 32. Audit Requirements

Important business actions should create audit history.

Examples include:

- workspace created
- brand profile changed
- campaign created
- campaign fact added/changed
- AI generation requested
- PostVersion created
- review submitted
- changes requested
- content approved
- approval revoked
- publication scheduled
- publication canceled
- publication attempted
- publication succeeded
- publication simulated
- publication failed
- account connection changed
- permission/role changed

Audit history should answer:

> Who did what, to which resource, and when?

---

# 33. Dashboard Requirements

The MVP dashboard should provide operational visibility.

Useful counts include:

- Draft
- In Review
- Changes Requested
- Approved
- Scheduled
- Published
- Simulated
- Failed / Action Required

It may also show:

- pending reviews
- upcoming publication jobs
- recent activity
- social account connection state

The MVP does not require advanced engagement analytics.

---

# 34. Primary Screens

The MVP needs a small, coherent manager-facing interface.

## 34.1 Dashboard

Shows:

- workflow counts
- pending review
- upcoming publishing
- failures/action required
- recent activity

## 34.2 Campaigns

Allows the user to:

- create/edit campaign
- maintain campaign facts
- select platforms
- generate content
- edit/rewrite drafts
- attach media
- select social destination
- inspect version history
- submit review

## 34.3 Approvals

Allows the Reviewer to:

- see pending requests
- open exact version
- inspect warnings/facts
- comment
- approve
- request changes

## 34.4 Calendar / Publishing

Allows eligible users to:

- schedule approved versions
- see due jobs
- see delivery state
- reschedule/cancel safe jobs
- inspect attempts/results
- retry when explicitly safe
- resolve action-required items

## 34.5 Settings

Includes:

- brand settings
- workspace/team policy
- social connections
- execution mode
- workspace language/timezone

---

# 35. Security Requirements

The MVP must demonstrate serious business protection even though it is a one-week project.

Minimum requirements:

- authenticated access
- backend authorization
- workspace isolation
- provider credentials never exposed to the browser after connection
- social/provider credentials never sent to AI prompts
- secrets never written to normal logs
- HTTPS in deployed environment
- secure server-side input validation
- output encoding
- media upload limits
- rate limiting for sensitive/high-cost operations where practical
- no auth/social access tokens stored in browser localStorage
- stale/concurrent updates must be detectable

The project must include at least one demonstrated cross-workspace access denial.

---

# 36. Concurrency Requirements

The system must protect mutable workflows from stale writes.

Important mutations should detect when another user has already changed the relevant resource.

Examples:

- two users editing the same Post
- reviewer approving an outdated submitted version
- scheduling after approval has been revoked
- two users scheduling the same approved version

The user should receive a conflict response and reload current state rather than silently overwrite it.

---

# 37. Idempotency Requirements

Repeated clicks or retried API requests must not create duplicate business intent.

Particularly important operations include:

- schedule
- publish now
- generation request where appropriate
- provider connection callback handling where appropriate

The product must prevent two publication intents from being created for the same approved version and destination simply because a request was repeated.

---

# 38. Non-Functional Requirements

## 38.1 Reliability

The core product should remain usable when:

- the AI provider is temporarily unavailable
- a social provider is temporarily unavailable
- the browser closes
- a worker restarts normally

Existing approved/scheduled data must remain durable.

## 38.2 Performance

The MVP is not a high-scale benchmark.

However:

- normal application pages should feel responsive
- long-running AI/provider work should not hold browser requests open unnecessarily
- workers should process durable queued work asynchronously
- database operations should avoid long network-held transactions

## 38.3 Observability

The system should provide enough information to debug:

- AI request status
- publication job status
- provider result
- attempt history
- correlation across related operations
- actionable failure reason

## 38.4 Honesty

Known provider limitations or unavailable permissions must be visibly represented.

The product must never claim a real provider success without provider evidence.

---

# 39. One-Week MVP Scope

## 39.1 Build now

The MVP includes:

- workspace access
- one brand per workspace
- seeded Creator and Reviewer
- campaign brief
- campaign facts
- LinkedIn/Facebook/Instagram platform selection
- AI generation
- deterministic AI output validation
- advisory AI evaluation
- user-triggered AI rewrite
- immutable versions
- one-stage human review
- configurable distinct-reviewer policy
- one image per version
- destination selection
- live/test/simulated provider mode
- scheduling
- publish now through the same job pipeline
- durable background publication
- publication history
- retry/action-required/reconciliation handling
- notifications
- audit trail
- simple dashboard
- deployment-ready demo

## 39.2 Keep intentionally small

The MVP deliberately limits:

- one brand per workspace
- one review stage
- one destination per PostVersion
- one image per PostVersion
- one Post per selected platform by default
- operational metrics only

---

# 40. Explicitly Out of Scope

Do not add these to the one-week MVP unless the PRD is formally changed:

- billing
- paid subscriptions
- advanced quota/billing UI
- social listening
- direct messages
- comment/reply automation
- advertising management
- recurring campaign schedules
- video publishing
- Reels
- Stories
- multi-stage legal approval
- advanced engagement analytics
- competitor analysis
- web scraping
- automatic website ingestion
- RAG
- vector database
- document ingestion
- custom model training
- custom recommendation model
- generalized autonomous agents
- full enterprise invitation/email system
- multi-brand workspace support

Scope must be cut before the core governance/publishing workflow is weakened.

---

# 41. Manager Demonstration Flow

The final demonstration should show one coherent product story.

1. Sign in as Creator
2. Select workspace
3. Show configured brand
4. Create campaign
5. Add supporting business facts
6. Generate LinkedIn, Facebook and Instagram variants
7. Show platform differences
8. Show at least one automated warning
9. Edit one variant
10. Attach required Instagram image
11. Select destination
12. Submit exact versions for review
13. Sign in/switch to Reviewer
14. Request a change on one version
15. Create a new version
16. Approve the valid version
17. Demonstrate that an unapproved/modified version cannot be scheduled
18. Schedule an approved version shortly ahead
19. Close or refresh the browser
20. Show that the background worker still executes
21. Show real provider result when available
22. Otherwise show clearly labeled `SIMULATED`
23. Open version history
24. Open audit trail
25. Show one controlled reconciliation/failure case
26. Show one cross-workspace denial

The manager should understand the business controls without needing to inspect source code.

---

# 42. Definition of Done

The MVP is complete only when the following are true.

## 42.1 AI

- A real hosted model generates platform-specific content
- Outputs are stored
- Outputs pass structural validation
- AI evaluation is stored
- User can edit/rewrite content
- AI failure is explicit and recoverable

## 42.2 Governance

- Human review is required before publication intent
- Review is tied to an exact immutable version
- Editing approved content does not retain approval
- Review history is preserved

## 42.3 Automation

- Approved content can be scheduled
- Publish Now uses the same durable job path
- Jobs execute independently of browser state
- Normal worker restart does not lose scheduled work

## 42.4 Integrations

- Every destination exposes current execution mode
- Live/test success stores provider evidence
- Simulation is clearly labeled
- Failed or uncertain outcomes are not falsely reported as success

## 42.5 Security and Tenancy

- another workspace cannot read/change tenant business data
- provider credentials are backend-only
- secrets do not appear in source control or normal logs
- direct unauthorized API calls are rejected

## 42.6 Evidence

The project handover includes:

- working application
- source repository
- database migrations
- setup instructions
- environment template
- test evidence
- provider-access matrix
- deployment notes
- known limitations
- manager demo flow

---

# 43. Minimum Acceptance Tests

The MVP should prove at least these cases:

- cross-workspace read is denied
- Creator cannot approve their own version when distinct-reviewer policy is enabled
- unsupported critical AI claim blocks review submission
- stale edit/review receives conflict response
- editing approved content revokes approval for the new version
- unapproved content cannot be scheduled
- repeated schedule request creates only one publication intent
- two workers cannot both own the same publication job
- worker restart before dispatch can recover the job
- uncertain outcome after dispatch becomes `RECONCILIATION_REQUIRED`
- expired/invalid provider authorization becomes `ACTION_REQUIRED`
- Instagram image requirement is enforced
- simulated publication is labeled `SIMULATED`
- closing the browser does not cancel the scheduled worker process

---

# 44. Product Success Criteria

For the internship project, success is not measured by the number of features.

The MVP succeeds when the manager can see that:

1. The business problem is understood
2. AI is useful but controlled
3. The workflow is centralized
4. Platform content is genuinely adapted
5. Human review cannot be bypassed
6. The exact approved version is protected
7. Scheduling is durable
8. Publication results are truthful
9. Failures are designed, not ignored
10. Tenant/security boundaries exist
11. The system can be extended after the MVP without rewriting the product model

---

# 45. Product Decision Summary

The one-week MVP deliberately protects the difficult business controls and simplifies everything else.

Do not sacrifice:

- tenant isolation
- exact-version approval
- human review
- durable scheduling
- truthful provider status
- reconciliation for uncertain external outcomes

Prefer to remove secondary features instead.

The core product promise is:

> **Create once with business context, adapt with AI, authorize with humans, publish through durable automation, and keep a traceable result.**
