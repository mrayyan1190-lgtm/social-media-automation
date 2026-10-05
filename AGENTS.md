# AGENTS.md — Coding Agent Rules for Social Media Content Automation Engine

## 1. Purpose

This file defines the rules that every coding agent must follow while implementing the Social Media Content Automation Engine.

This is not product documentation and not architecture documentation.

It is the **behavior contract for the coding agent**.

The agent must use these files as the source of truth:

1. `AGENTS.md` — how the agent must behave
2. `PRD.md` — what the product must do
3. `ARCHITECTURE.md` — how the system must be structured
4. `TASKS.md` — what to build next and in what order
5. `README.md` — how to run and understand the repository

The agent must not silently redefine the product or architecture.

---

# 2. Mandatory Reading Order

Before implementing any task, read:

```text
AGENTS.md
    ↓
PRD.md
    ↓
ARCHITECTURE.md
    ↓
TASKS.md
    ↓
relevant existing source files
```

Do not start coding from `TASKS.md` alone.

A task is only valid in the context of the PRD and architecture.

---

# 3. Source-of-Truth Priority

If documents appear to conflict, use this order:

```text
1. PRD.md
   Product intent and required business behavior

2. ARCHITECTURE.md
   Technical boundaries and implementation structure

3. AGENTS.md
   Agent behavior and change rules

4. TASKS.md
   Implementation order

5. README.md
   Setup and operational guidance
```

Do not resolve a conflict by inventing a third interpretation.

If a real contradiction blocks implementation:

- stop
- identify the conflict
- explain exactly which documents disagree
- ask for a decision before changing the architecture

---

# 4. Core Agent Principle

The agent must implement the designed system.

The agent must not redesign the system while coding.

Do not:

- replace architecture because another pattern is more familiar
- add a new framework without need
- add a microservice for presentation value
- simplify away version-bound approval
- bypass durable workers
- move business rules into frontend code
- create a second source of truth
- invent product features that are not in scope

---

# 5. Work One Task at a Time

The agent must work from `TASKS.md`.

For each task:

1. identify the exact task
2. read the relevant PRD section
3. read the relevant architecture section
4. inspect existing code
5. identify files that must change
6. implement only the required scope
7. run appropriate tests
8. verify no architecture rule was violated
9. update the task state only after the work is complete
10. summarize what changed

Do not combine multiple unrelated tasks into one large generation.

---

# 6. Small Change Rule

Prefer small, reviewable changes.

A coding task should usually produce:

- one feature
- one state transition
- one API operation
- one database migration
- one UI flow
- one worker behavior

Do not generate thousands of lines across unrelated areas unless the task explicitly requires a foundation scaffold.

---

# 7. No Unrequested Refactoring

Do not refactor unrelated code.

Do not:

- rename unrelated classes
- reorganize folders without need
- replace established patterns during a feature task
- reformat the whole repository
- replace libraries merely because another library is preferred
- rewrite working tests that are unrelated

If a refactor is necessary for the current task, keep it minimal and explain why.

---

# 8. No Unapproved Dependencies

Do not add a dependency without a clear project need.

Before adding a package, check whether:

- the platform/framework already provides the capability
- the repository already contains an equivalent package
- the dependency is justified by the current task

Never introduce a package simply because it makes generated code shorter.

Prefer the company-aligned stack and existing framework capabilities.

---

# 9. MVP Technology Boundary

For the one-week MVP, preserve this runtime design:

```text
Web Application
      ↓
Core API
      ↓
SQL Server

SQL Server
   ↓
AI Worker
   ↓
Hosted LLM

SQL Server
   ↓
Publishing Worker
   ↓
Social Provider Adapters
```

The current MVP uses:

- Angular web client
- ASP.NET Core backend
- SQL Server
- .NET AI worker
- .NET publishing worker
- Blob/Object storage
- Hosted LLM
- LinkedIn/Facebook/Instagram adapters

Do not introduce a Python runtime into the MVP unless the architecture is explicitly changed.

Python remains a future extraction option for advanced AI workloads.

---

# 10. Modular Monolith Rule

The core backend is a modular monolith.

Do not create independent network services for:

- users
- campaigns
- reviews
- notifications
- media
- publishing
- social connections

The AI and publishing workers are separate runtime processes/modes because they execute durable background work.

That does not mean every domain module becomes a microservice.

---

# 11. Layering Rule

Preserve these backend boundaries:

```text
API
  ↓
Application
  ↓
Domain

Infrastructure implements external dependencies
```

## Domain

May contain:

- entities
- value objects
- enums
- business invariants
- state rules

Must not depend on:

- HTTP
- controllers
- provider SDKs
- Angular
- direct secret access

## Application

May contain:

- commands
- queries
- use cases
- authorization orchestration
- transaction orchestration
- interfaces/ports

## Infrastructure

Contains:

- EF Core
- SQL Server mappings
- Blob storage
- LLM client
- social provider clients
- OAuth implementation
- external integrations

## API

Contains:

- HTTP endpoints
- transport validation
- authentication context
- request/response mapping

## Worker

Contains:

- background loops
- durable claiming
- scheduling/lease handling
- application-service invocation

---

# 12. Frontend Rule

The frontend is not the business authority.

The frontend may:

- hide invalid buttons
- disable unavailable actions
- display status
- provide optimistic UX where safe

The backend must still enforce:

- permissions
- workspace ownership
- state transitions
- approval
- scheduling
- idempotency
- concurrency
- publication eligibility

Never implement a rule only in Angular.

---

# 13. Tenant Isolation Is Non-Negotiable

Every tenant-owned resource must stay inside one `OrganizationId` boundary.

The agent must never implement a query that assumes a resource ID is enough.

Every business operation must verify:

```text
Authenticated user
      ↓
Active organization membership
      ↓
Permission
      ↓
Resource OrganizationId
```

Cross-workspace access must fail.

Important tenant-owned records include:

- BrandProfiles
- Campaigns
- CampaignFacts
- Posts
- PostVersions
- ReviewRequests
- ReviewComments
- ContentEvaluations
- MediaAssets
- SocialAccounts
- PublicationJobs
- Notifications
- AuditLogs

Do not add convenience methods that bypass tenant filtering.

---

# 14. User Identity vs Workspace Ownership

A User is a global identity.

A User does not directly own business data.

The Organization owns the business data.

Use OrganizationMembers to connect users to workspaces and roles.

Do not place a single permanent `OrganizationId` on the User model as a shortcut for tenancy.

---

# 15. Role Rules

MVP roles:

- Admin
- Creator
- Reviewer

Do not invent new permanent roles without PRD change.

Respect `RequireDistinctReviewer`.

When it is enabled:

- submitter cannot approve their submitted version
- last meaningful human editor cannot approve the same version

Do not create an admin bypass that ignores immutable-version or audit rules.

---

# 16. Post Version Rule

A reviewed PostVersion is immutable.

Never update reviewed public content in place.

A meaningful edit must create a new PostVersion.

Meaningful public-facing changes include:

- hook
- body
- caption
- CTA
- destination URL
- hashtags
- destination account
- media replacement
- alt text
- AI rewrite
- reviewer-feedback rewrite

Always preserve previous versions.

---

# 17. Current Version Rule

A Post must point to its current version.

The current version must:

- belong to the same Post
- belong to the same Organization
- remain consistent with concurrency rules

Do not create a new version without updating `CurrentVersionId` in the same valid transaction when required.

---

# 18. Approval Rule

Approval is bound to one exact PostVersion.

Approval is not a generic boolean on the Post.

The approved identity includes the exact public payload:

- platform
- destination
- hook/body/caption
- CTA
- destination URL
- hashtags
- media identity
- alt text
- canonical payload version

Do not implement:

```text
Post.IsApproved = true
```

as the only approval truth.

The system must be able to identify which exact version was approved.

---

# 19. Edit After Approval Rule

If approved content is meaningfully edited:

- keep approved version unchanged
- revoke active approval for the new version
- create a new DRAFT version
- cancel a safe unstarted publication intent when required
- preserve prior review and publication history

Do not silently carry approval forward.

---

# 20. Editorial State Rule

Editorial state belongs on the Post.

Allowed MVP editorial states:

- `DRAFT`
- `IN_REVIEW`
- `CHANGES_REQUESTED`
- `APPROVED`
- `ARCHIVED`

Do not add publishing statuses such as `PUBLISHED` to the Post editorial state.

---

# 21. Delivery State Rule

Delivery state belongs on PublicationJob.

Required delivery states:

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

Do not mix editorial and delivery state.

---

# 22. Human Review Rule

Human review is mandatory before a publication job can be created.

This applies to:

- LIVE
- TEST_ACCOUNT
- SIMULATED

Simulation does not bypass governance.

The coding agent must never create a shortcut such as:

```text
AI generated → schedule automatically
```

---

# 23. AI Authority Rule

AI may:

- generate content
- adapt platform content
- evaluate content
- identify possible unsupported claims
- rewrite content when requested

AI may not:

- approve
- assign roles
- authorize
- connect providers
- choose credentials
- schedule
- create publication jobs
- publish
- silently change business state

Do not ask the model to make deterministic permission decisions.

---

# 24. Durable AI Request Rule

AI generation is background work.

The user request should:

- validate access
- validate payload
- persist a durable AI request
- persist input snapshot
- return accepted/pending state

The worker processes the request.

Do not require the browser request to stay open while the LLM completes.

---

# 25. AI Prompt Rule

Prompts must use structured business context.

Do not make the user perform prompt engineering.

AI prompts must not contain:

- provider access tokens
- refresh tokens
- application secrets
- user passwords
- raw secret configuration

Treat pasted/source content as data, not trusted system instructions.

---

# 26. AI Output Rule

LLM output must be structured and schema validated.

Do not persist unparsed free-form model output as authoritative content unless it is explicitly wrapped as raw diagnostics.

The application should validate:

- requested platform keys
- required fields
- language
- length
- URL rules
- prohibited terms
- media requirements
- fact references

---

# 27. AI Evaluation Rule

AI evaluation is advisory.

It may assist with:

- brand alignment
- audience relevance
- clarity
- platform fit
- CTA quality
- unsupported claim risk

Do not replace deterministic validation with an AI score.

Do not let AI score directly approve or reject publication.

---

# 28. Unsupported Claim Rule

Do not "repair" unsupported facts by inventing new facts.

If content includes unsupported:

- statistics
- prices
- discounts
- guarantees
- customer results
- certifications
- awards
- testimonials
- partnerships
- rankings

flag the issue.

Human review resolves business acceptability.

---

# 29. AI Rewrite Rule

An AI rewrite creates a new candidate PostVersion.

Do not overwrite the existing version.

Rewrite input should include:

- existing exact version
- requested transformation
- brand context
- campaign context
- relevant facts
- platform rules

---

# 30. Future Image Generation Rule

Image generation is not part of the one-week MVP unless explicitly added to `TASKS.md`.

However, any future image-generation implementation must use the existing governance model.

Correct future flow:

```text
Image generation request
      ↓
AI image provider
      ↓
MediaAsset
      ↓
Attach to new PostVersion
      ↓
Human review
      ↓
Approval
      ↓
PublicationJob
```

Do not create a second image-only approval or publishing path.

AI-generated images must not bypass:

- MediaAsset
- immutable storage
- versioning
- human review
- approval
- publication jobs

---

# 31. Future Image Provider Abstraction

If image generation is added later:

use a provider abstraction.

Do not hard-code the domain to one vendor.

Conceptual contract:

```text
IImageGenerationProvider
  GenerateAsync()
  GetResultAsync()
  NormalizeError()
```

Generated image metadata should preserve:

- OrganizationId
- generation request id
- source = AI_GENERATED
- provider
- model/version
- dimensions
- MIME type
- file hash
- immutable object key
- moderation/safety result if available

---

# 32. Media Rule

Media bytes belong in Blob/Object storage.

SQL stores metadata.

Do not store ordinary media files as large business-row blobs unless architecture is explicitly changed.

An immutable MediaAsset identity should be based on stable object identity and content hash.

A temporary signed URL is not the media identity.

---

# 33. Media Replacement Rule

Replacing media creates a new PostVersion.

Never silently overwrite media already referenced by an approved or published version.

---

# 34. Social Account Rule

A SocialAccount is the business destination.

Do not treat raw OAuth credentials as the SocialAccount identity.

Keep credentials/grant concerns isolated from business destination metadata.

---

# 35. Provider Modes Rule

Allowed SocialAccount modes:

- `LIVE`
- `TEST_ACCOUNT`
- `SIMULATED`
- `NOT_CONFIGURED`

The UI must expose the mode.

The publication job must snapshot the execution mode.

Do not read the current account mode later and rewrite historical job truth.

---

# 36. Simulation Rule

Simulation is a transparent fallback.

It is not fake provider success.

A simulation job ends as:

`SIMULATED`

Never:

`PUBLISHED`

Do not create a fake external provider URL or ID.

---

# 37. Publish Now Rule

Publish Now must use the publication-job pipeline.

Correct:

```text
Approve
  ↓
Publish Now
  ↓
Create immediately due PublicationJob
  ↓
Publishing Worker
  ↓
Provider Adapter
```

Never implement direct browser/API synchronous provider publishing for Publish Now.

---

# 38. Scheduling Rule

Only an eligible approved current version may be scheduled.

Scheduling input:

- local date
- local time
- IANA timezone

Server resolves canonical UTC.

Do not trust client-only UTC conversion as the authoritative schedule.

---

# 39. Publication Job Rule

One PublicationJob represents one lifetime publication intent for:

- one Organization
- one exact PostVersion
- one exact SocialAccount

Do not create duplicate intents because of repeated clicks.

Use:

- idempotency
- database uniqueness
- validation

---

# 40. Worker Transaction Rule

Never hold a database transaction open while calling:

- LLM provider
- LinkedIn
- Facebook
- Instagram
- Blob external network operation where unnecessary

Claim work quickly.

Commit.

Release locks.

Then perform network work.

---

# 41. Worker Lease Rule

Durable workers must use a lease/fencing pattern.

A job claim should include:

- worker identity
- lease token
- lease timestamps
- expiry

Later state updates must verify the lease token.

A stale worker must not overwrite newer state.

---

# 42. Dispatch Revalidation Rule

Immediately before provider mutation, revalidate:

- workspace state
- active approval
- exact current eligible version
- destination
- execution mode
- payload hash
- credentials/capability
- media
- late-job policy

Do not assume data valid at schedule time remains valid forever.

---

# 43. Retry Rule

Never blindly retry uncertain external sends.

Safe automatic retry only when it is established that the external side effect did not occur or provider semantics explicitly support safe retry.

Examples of uncertain outcome:

- timeout after send
- provider server error after create request
- lease lost after dispatch
- provider success followed by database persistence failure

These become:

`RECONCILIATION_REQUIRED`

---

# 44. Exactly-Once Claim Rule

Never claim perfect exactly-once delivery across external providers.

Use language such as:

- effectively-once behavior
- duplicate protection
- reconciliation for uncertain outcome

Do not write documentation or UI text that promises impossible guarantees.

---

# 45. External Provider Adapter Rule

Provider-specific behavior belongs inside adapters.

Do not scatter:

```text
if LinkedIn ...
if Facebook ...
if Instagram ...
```

through unrelated services.

Use a common social publisher abstraction.

Provider adapters handle:

- provider payload mapping
- multi-step provider flow
- provider status normalization
- provider errors
- provider-specific capability checks

---

# 46. Instagram Multi-Step Rule

If Instagram requires media container creation and later publishing:

persist checkpoints.

Do not:

- sleep for long periods inside an HTTP request
- hold DB transaction while waiting
- lose container id on restart

Use:

- WAITING_MEDIA
- NextActionAtUtc
- provider container id

---

# 47. Idempotency Rule

Use idempotency where repeated user/network requests could duplicate intent.

Particularly:

- schedule
- publish now
- selected generation requests if required
- OAuth callback handling where appropriate

Never rely only on disabling the frontend button.

---

# 48. Concurrency Rule

Use rowversion/ETag/expected version for mutable aggregates.

Do not silently overwrite stale state.

Examples:

- two edits
- stale approval
- schedule after approval revoked
- two schedule attempts

Return a conflict and require reload.

---

# 49. Transaction Rule

Operations that must commit atomically should do so.

Examples:

## Post creation

- Post
- Version 1
- CurrentVersion pointer

## Approval

- review decision
- active approval
- editorial state
- notification
- audit

## Schedule

- publication intent
- idempotency record
- audit

## Safe cancel-and-edit

- job cancellation
- approval revocation
- new PostVersion
- CurrentVersion pointer
- DRAFT state
- audit

---

# 50. API State Transition Rule

Use explicit commands.

Good:

```text
POST /posts/{id}/submit-review
POST /review-requests/{id}/approve
POST /review-requests/{id}/request-changes
POST /posts/{id}/schedule
POST /publication-jobs/{id}/cancel
```

Bad:

```text
PATCH /posts/{id}
{
  "status": "APPROVED"
}
```

The frontend must not choose arbitrary workflow states.

---

# 51. Stable Error Code Rule

Use stable machine-readable error codes.

Do not force frontend logic to parse English error messages.

Examples:

- `TENANT_MISMATCH`
- `FORBIDDEN`
- `VALIDATION_FAILED`
- `CONCURRENCY_CONFLICT`
- `INVALID_STATE_TRANSITION`
- `APPROVAL_REQUIRED`
- `APPROVAL_STALE`
- `AI_GENERATION_FAILED`
- `SOCIAL_ACCOUNT_NOT_READY`
- `IDEMPOTENCY_CONFLICT`
- `PUBLICATION_TOO_LATE_TO_CANCEL`

---

# 52. Audit Rule

Important business actions require audit.

Audit must record:

- actor
- organization
- action
- resource
- timestamp
- safe metadata

Audit must not record:

- access tokens
- refresh tokens
- passwords
- secret keys
- authorization headers

---

# 53. Notification Rule

Notifications are user workflow feedback.

They are not the audit log.

Keep these concerns separate.

---

# 54. Secrets Rule

Never commit secrets.

Never write secrets into:

- source
- markdown
- screenshots
- tests
- logs
- AI prompts
- browser local storage

Use environment/managed secret configuration.

---

# 55. OAuth Rule

OAuth flow must:

- begin server-side
- use short-lived state
- bind state to user/workspace/provider
- validate callback state
- exchange authorization code server-side
- store token material securely

Do not expose social tokens to browser code.

---

# 56. Authentication Rule

Use the repository's chosen authentication mechanism.

Do not replace it casually.

Do not store auth tokens in localStorage when architecture specifies secure cookie/server-managed session behavior.

---

# 57. Logging Rule

Use correlation ids.

Logs should allow tracing across:

- API request
- AI request
- campaign
- post
- version
- review
- publication
- attempt

Redact secrets.

---

# 58. Test Rule

Every important business rule should have an automated test where practical.

At minimum preserve tests for:

- cross-tenant denial
- distinct reviewer
- unsupported claim blocker
- stale concurrency
- approval revocation after edit
- schedule requires approval
- duplicate schedule protection
- worker ownership
- restart recovery
- reconciliation after uncertain dispatch
- action required for invalid authorization
- Instagram media requirement
- simulation status
- browser-independent scheduling

Do not delete failing tests to make a task "pass".

---

# 59. No Test Deletion Rule

Never remove or weaken a test simply because new code fails it.

If a test is genuinely obsolete because the product specification changed:

- identify the specification change
- update the test intentionally
- explain why

Do not silently delete coverage.

---

# 60. Build Verification Rule

After a coding task, run the relevant verification.

Depending on scope:

- compile/build
- unit tests
- integration tests
- database migration validation
- frontend tests
- lint/format checks
- worker tests

Do not claim completion if the relevant build/tests were not run.

If a test cannot be run, say so explicitly.

---

# 61. Database Migration Rule

Schema changes require migrations.

Do not manually change production database assumptions without a migration.

A migration should:

- be narrowly scoped
- preserve existing data where required
- add constraints intentionally
- add indexes needed by the feature
- avoid unrelated schema rewrites

---

# 62. Index Rule

When implementing worker/business queries, preserve indexes for important access patterns.

Examples:

- PublicationJobs by status + NextActionAtUtc
- AI requests by status + next attempt
- reviews by organization + status
- campaigns by organization + updated time
- notifications by user + unread/time
- audit by organization + time

Do not add random indexes without query need.

---

# 63. DTO and JSON Rule

JSON columns do not replace typed application contracts.

Use:

- valid JSON constraint where supported
- typed DTO/model validation
- versioned schema where long-lived payload snapshots are stored

Do not let arbitrary JSON silently become domain state.

---

# 64. API Compatibility Rule

Avoid breaking existing endpoint contracts during unrelated tasks.

If an API contract must change:

- update contract definition
- update frontend client
- update tests
- document the change

Do not create hidden mismatches between frontend and backend.

---

# 65. Generated Code Quality Rule

Generated code must be maintainable.

Do not produce:

- giant controllers
- giant services
- duplicate business logic
- magic strings for important states
- unbounded retry loops
- empty catch blocks
- hard-coded secrets
- hard-coded tenant ids
- provider logic copied in multiple places

Prefer clear names and narrow responsibilities.

---

# 66. No Placeholder Success Rule

Do not implement fake success paths such as:

```text
return Ok("Published");
```

when the provider was not called.

When provider access is unavailable, use the simulation adapter and label the result `SIMULATED`.

---

# 67. No Hidden Mock Rule

Mocks/stubs must be clearly confined to:

- tests
- development fixtures
- explicit simulation adapter

Do not let production code silently fall back from LIVE to SIMULATED without the user/system knowing.

---

# 68. Error Handling Rule

Failures must become explicit state.

Do not swallow errors.

AI failure:

- mark request failed/retryable appropriately
- preserve error category
- return user-visible status

Publishing failure:

- classify safe retry
- action required
- reconciliation
- permanent failed

---

# 69. Time Rule

Persist authoritative operational timestamps in UTC.

Preserve original local scheduling representation and timezone where required.

Do not store ambiguous local time as the only scheduling value.

---

# 70. Media URL Rule

Do not store short-lived signed URLs as permanent business identity.

Generate or refresh delivery URLs near dispatch.

Approval identity uses stable media identity.

---

# 71. Scope Rule

The one-week MVP must remain focused.

Do not add:

- billing
- social listening
- DMs
- comment automation
- ads
- recurring campaign scheduling
- video/Reels/Stories
- engagement analytics
- RAG
- web scraping
- custom ML
- vector database
- multi-agent orchestration
- multi-stage legal approval
- multi-brand support

unless the PRD is explicitly changed.

---

# 72. Future Feature Rule

Future-ready does not mean build now.

It means:

- preserve clean boundaries
- avoid choices that block extension
- document extension point
- do not implement unused complexity

This applies especially to:

- Python AI extraction
- image generation
- RAG
- analytics
- billing
- recurring schedules

---

# 73. TASKS.md Rule

Only implement tasks that are:

- present in `TASKS.md`
- or explicitly requested by the user

When a task is completed:

- mark only that task complete
- do not mark future tasks done
- do not invent progress

If implementation reveals a missing prerequisite:

- add/request a prerequisite task
- do not silently widen scope

---

# 74. README Rule

If a change affects:

- setup
- startup
- environment variables
- migrations
- demo accounts
- deployment
- worker commands

update `README.md`.

Do not let setup instructions become stale.

---

# 75. ARCHITECTURE.md Rule

If the implementation truly requires an architectural change:

do not simply change code.

First:

1. identify why current architecture is insufficient
2. propose the change
3. explain impact
4. wait for approval
5. update `ARCHITECTURE.md`
6. then implement

Architecture must not drift silently.

---

# 76. PRD.md Rule

If product behavior changes:

do not silently redefine it in code.

Update the PRD only after the product decision is approved.

Examples:

- removing mandatory review
- changing role permissions
- changing MVP scope
- changing provider semantics
- making image generation part of MVP

---

# 77. Commenting Rule

Comment code when the business reason is non-obvious.

Good comment:

```text
Do not retry after dispatch timeout because the provider may
have created the post; move to reconciliation instead.
```

Bad comment:

```text
Increment attempt count.
```

Prefer code that explains itself for ordinary mechanics.

---

# 78. Naming Rule

Use domain language consistently.

Preferred terms:

- Organization
- OrganizationMember
- Campaign
- CampaignFact
- Post
- PostVersion
- ReviewRequest
- ReviewComment
- MediaAsset
- SocialAccount
- PublicationJob
- PublicationAttempt
- AiGenerationRequest
- ContentEvaluation
- AuditLog

Do not create multiple names for the same concept such as:

- WorkspacePost
- SocialContentItem
- PublicationContent

unless there is a real separate concept.

---

# 79. Enum Rule

Use centralized enums/value definitions for important states.

Do not scatter state strings such as:

```text
"Approved"
"APPROVED"
"approved"
```

through the codebase.

Use one canonical representation.

---

# 80. Security Review Checklist

Before completing a task that touches security-sensitive functionality, verify:

- tenant ownership checked
- permissions checked
- no secret leaks
- request validated
- concurrency protected if needed
- idempotency handled if needed
- audit event written if important
- logs are safe
- external side effect is controlled

---

# 81. Publishing Review Checklist

Before completing publishing functionality, verify:

- exact approved PostVersion used
- correct SocialAccount used
- execution mode snapshotted
- payload hash verified
- media identity stable
- job uniqueness enforced
- lease used
- dispatch checkpoint persisted
- uncertain outcome reconciled
- external id stored on confirmed success
- simulation never reported as publish

---

# 82. AI Review Checklist

Before completing AI functionality, verify:

- durable request persisted
- input snapshot stored
- prompts versioned
- schema validated
- no secrets included
- deterministic validation exists
- evaluation separated from permission decisions
- failures explicit
- token/call budget bounded
- outputs create new versions rather than overwrite reviewed content

---

# 83. Review Workflow Checklist

Before completing review functionality, verify:

- review tied to exact version
- version immutable
- reviewer permission checked
- distinct reviewer policy enforced
- request-changes requires explanation
- approval transaction atomic
- stale approval rejected
- audit created
- notification created

---

# 84. Definition of Agent Completion

A task is not complete merely because code was generated.

A task is complete when:

- implementation matches PRD
- implementation matches architecture
- relevant tests pass
- build passes
- migrations are valid when applicable
- no unrelated refactors were introduced
- documentation is updated where required
- task status is updated truthfully
- limitations are stated

---

# 85. Agent Response Format After a Task

After completing a coding task, summarize:

## Implemented

What was added or changed.

## Files Changed

List only relevant files.

## Business Rules Preserved

Mention important invariants affected by this task.

## Verification

State exactly what was run:

- build
- tests
- migrations
- lint
- manual checks

## Remaining / Blocked

State any known limitation or external dependency.

Do not claim a provider integration works if it was not actually verified.

---

# 86. Stop Conditions

The agent must stop and ask before proceeding if:

- PRD and architecture materially conflict
- a task requires removing human approval
- a task requires mutating approved versions
- a task requires bypassing tenant isolation
- a task requires unsafe provider retry
- a task requires exposing secrets
- a task requires replacing the core stack
- a task introduces a new microservice boundary
- a task changes MVP scope substantially
- a task makes image generation part of MVP without PRD change

---

# 87. Final Non-Negotiable Summary

The coding agent must always preserve these rules:

```text
Tenant data stays inside its Organization.

AI creates content; it does not authorize actions.

Reviewed versions are immutable.

Approval belongs to an exact version.

Changing approved content creates a new version.

Human review is required before publication intent.

Publish Now and Schedule use PublicationJobs.

Publishing happens in a durable worker.

Simulation is SIMULATED, never PUBLISHED.

Unknown external outcome is reconciled, not blindly retried.

Provider secrets remain backend-only.

Database state is authoritative.

Frontend controls UX, not authorization.

Future image generation becomes MediaAsset + PostVersion + Human Review.

Do not redesign the architecture while coding.

Build one TASKS.md item at a time.

Test before claiming completion.
```

The coding agent's job is not to make the project larger.

The coding agent's job is to make the agreed product correct, reviewable, and deployable.
