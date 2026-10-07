# Social Media Content Automation Engine

A centralized SaaS workspace that converts a business campaign brief into platform-specific AI-assisted drafts, requires human approval of the exact content version, schedules approved content through durable background jobs, publishes to LinkedIn, Facebook, and Instagram where provider access is available, and records the final delivery result and audit trail.

This repository is being built as a one-week MVP for a manager-facing demonstration. The goal is not to create a large social-media suite. The goal is to prove one complete, reliable business flow from campaign input to traceable publication.

---

## 1. What this product solves

Marketing teams often create similar content repeatedly for different channels, exchange approvals through chat or email, manually remember posting times, and struggle to prove which version was actually approved or what happened when an external API failed.

The Social Media Content Automation Engine brings that workflow into one controlled system:

**Brand Context -> Campaign Brief -> AI Drafts -> Validation -> Human Review -> Approval -> Scheduling -> Background Publishing -> Delivery Result -> Audit History**

AI assists with content generation and evaluation. Deterministic business rules decide what is allowed to happen.

---

## 2. One-week MVP scope

### Build now

- Workspace-based access and tenant isolation
- Brand profile
- Campaign brief and supporting facts
- LinkedIn, Facebook, and Instagram content variants
- AI generation and advisory evaluation
- Immutable post versions
- Human review and approval
- Review comments and change requests
- Schedule and Publish Now through the same publication-job path
- Single-image media upload
- Durable background publishing
- Live, test-account, and clearly labelled simulation modes
- Publication history and failure states
- Notifications and audit trail
- Manager-facing dashboard and operational views

### Keep intentionally small

- One brand per workspace
- One review stage
- One destination account per post version
- One image per version
- One post per selected platform by default
- Operational metrics only
- One reliable end-to-end vertical slice before secondary features

### Out of scope for this MVP

- Billing and subscriptions
- Social listening
- Direct messages
- Ad management
- Recurring campaigns
- Video, Reels, and Stories
- Multi-stage legal approval
- Engagement analytics
- Web scraping or RAG
- Custom model training
- General multi-agent architecture
- Python AI microservice in the week-one runtime

---

## 3. Core business workflow

1. A user signs in and selects a workspace.
2. The workspace brand profile provides reusable brand context.
3. A Creator creates a campaign brief and supplies factual supporting information.
4. A durable AI request is created.
5. The AI worker generates one independently written draft for each selected platform.
6. Deterministic validation checks schema, platform rules, language, URLs, prohibited terms, media requirements, and fact references.
7. An advisory AI evaluation checks brand alignment, audience relevance, clarity, platform fit, CTA relevance, and unsupported or contradictory claims.
8. Candidate post versions are stored.
9. The Creator edits or rewrites content if needed. Every meaningful change creates a new immutable version.
10. The Creator submits the exact current version for review.
11. A Reviewer approves it or requests changes.
12. Approval is bound to the exact version, destination account, caption, CTA, URL, hashtags, media identity, alt text, and canonical payload representation.
13. Only the current approved version may create a publication job.
14. The user chooses Schedule or Publish Now. Both use the same publication-job path.
15. The publishing worker claims the durable job, revalidates approval and account state, and dispatches through the provider adapter.
16. The result is recorded as PUBLISHED, SIMULATED, ACTION_REQUIRED, RECONCILIATION_REQUIRED, FAILED, or another defined delivery state.
17. Notifications, publication attempts, version history, and audit history remain available for review.

---

## 4. User roles

### Admin

Can manage the workspace, brand profile, members, social connections, and normal content workflows. Admin rights do not bypass version checks, approval integrity, or audit requirements.

### Creator

Can create campaigns, generate and edit drafts, submit content for review, and schedule or publish an already approved version. A Creator cannot approve a review when the workspace policy `RequireDistinctReviewer` is enabled.

### Reviewer

Can inspect the exact submitted version, supporting facts, automated warnings, comments, destination, and media; then approve or request changes. A Reviewer does not silently edit wording and approve the same unreviewed change.

For the manager demo, `RequireDistinctReviewer` should be enabled so separation of duties is visible.

---

## 5. Content and approval model

A `Post` represents one independently managed platform variant.

A `PostVersion` is an immutable public-facing version of that post.

Meaningful edits never overwrite a reviewed version. They create a new version and move the Post's current-version pointer.

Approval belongs to the exact submitted version, not only to the Post record.

### Editorial states

- `DRAFT`
- `IN_REVIEW`
- `CHANGES_REQUESTED`
- `APPROVED`
- `ARCHIVED`

Delivery state does not belong on the Post. It belongs to the publication job.

Editing an approved version invalidates the active approval. If an unstarted publication job exists and can be safely cancelled, it is cancelled and the edited content returns to a new draft version.

---

## 6. AI behavior

Users provide business facts and editorial intent. They do not need to write prompts.

The AI workflow is intentionally bounded:

**Generate -> Deterministic Validation -> Advisory Evaluation -> Store Candidate Versions**

Automatic repair loops are not part of the MVP. If a user wants a change, actions such as Shorten, Expand, More Professional, Improve Hook, or Custom Rewrite create a new candidate version that is evaluated again.

The AI may:

- Generate platform-specific drafts
- Adapt tone and structure by platform
- Suggest hooks and CTAs
- Evaluate brand alignment and clarity
- Flag unsupported or contradictory claims
- Rewrite content when explicitly requested

The AI may not:

- Approve content
- Change permissions
- Connect social accounts
- Access social credentials
- Create publication jobs
- Decide authorization
- Invent unsupported statistics, prices, discounts, certifications, testimonials, partnerships, or guarantees

The MVP AI runtime is handled by a durable `.NET AI worker` that reads queued AI requests, calls the hosted LLM, validates output, records model/prompt/schema metadata, and persists results through the application infrastructure.

A separate Python AI service is a future option only if later features such as RAG, document ingestion, or advanced ML justify an additional operational boundary.

---

## 7. Scheduling and publishing

Schedule and Publish Now are the same business operation with different due times.

The browser submits:

- Local date and time
- IANA timezone
- UTC offset only when required to resolve an ambiguous daylight-saving transition

The server resolves and stores the canonical UTC instant together with the original local scheduling context.

A publication job stores the exact approved version and a frozen approved payload snapshot.

The browser may close after scheduling. Background workers continue independently from durable database state.

### Delivery states

The implementation will use states including:

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

The system never assumes cross-provider exactly-once delivery. If an external request may already have created a post but the local outcome is uncertain, the job moves to `RECONCILIATION_REQUIRED` instead of being blindly sent again.

---

## 8. Social provider modes

Every connected destination has an explicit execution mode:

- `LIVE`
- `TEST_ACCOUNT`
- `SIMULATED`
- `NOT_CONFIGURED`

Simulation is a transparent fallback for the manager demo when external provider approval is unavailable.

A simulated job may pass through the real campaign, review, approval, scheduling, worker, and audit workflow, but its final result must be labelled `SIMULATED` and must never be presented or counted as an external publication.

Provider access should be tested on Day 1. The MVP is considered valid even if only one provider is live, as long as other unavailable providers are honestly represented through their configured mode.

---

## 9. Data ownership and tenancy

The workspace is the customer data boundary.

A user is a global identity. `OrganizationMembers` links a user to a workspace and gives that user a workspace-specific role.

Tenant-owned data includes:

- Brand profiles
- Campaigns and campaign facts
- Posts and post versions
- Review requests and comments
- AI requests and evaluations
- Media assets
- Social accounts
- Publication jobs and attempts
- Notifications
- Audit records

Every business read and mutation must validate:

1. Authenticated user
2. Active workspace membership
3. Required permission
4. Resource ownership
5. Relevant rowversion or concurrency precondition where applicable

Knowing an entity identifier is never permission.

---

## 10. High-level application architecture

The MVP is a modular monolith with durable workers, not a large microservice system.

### Web client

Responsible for campaign forms, editing, approvals, calendar/publishing views, social connection settings, notifications, and dashboard presentation.

### Core application/API

Responsible for identity context, tenant authorization, business rules, immutable versions, review decisions, scheduling, provider connection workflows, idempotency, and transaction boundaries.

### AI worker

Reads durable AI requests, calls the hosted LLM, performs output validation/evaluation, and persists AI results.

### Publishing worker

Claims due jobs, revalidates approved state, prepares provider-specific payloads, handles media checkpoints, dispatches posts, classifies failures, and records final or recoverable states.

### SQL Server

Acts as the authoritative source of transactional business state, durable work queues, version history, review history, publication jobs, notifications, and audit information.

### Blob/Object Storage

Stores immutable media objects. SQL stores media metadata, ownership, hashes, and object identity.

### External providers

- Hosted LLM
- LinkedIn
- Facebook
- Instagram

External provider approval and permissions are dependencies, not architecture guarantees.

---

## 11. Repository layout

```text
/
|-- README.md, PRD.md, ARCHITECTURE.md, AGENTS.md, TASKS.md
|-- .gitignore, .editorconfig
|-- docker-compose.yml                local infrastructure (SQL Server)
|-- .env.example                      environment template (no secrets)
|
|-- frontend/                         Angular web client (see frontend/README.md)
|   `-- src/app/{core,shared,features}/
|
`-- backend/
    |-- SocialMediaAutomation.sln
    |-- Directory.Build.props         shared build settings (net10.0, nullable, warnings as errors)
    |-- Directory.Packages.props      central NuGet package versions
    |-- .config/dotnet-tools.json     local tools (dotnet-ef)
    |-- src/
    |   |-- SocialMediaAutomation.Domain/
    |   |-- SocialMediaAutomation.Application/
    |   |-- SocialMediaAutomation.Infrastructure/
    |   |-- SocialMediaAutomation.Api/
    |   `-- SocialMediaAutomation.Worker/       Ai | Publishing | Combined
    |-- tests/
    |   |-- SocialMediaAutomation.Domain.Tests/
    |   |-- SocialMediaAutomation.Application.Tests/
    |   |-- SocialMediaAutomation.IntegrationTests/
    |   `-- SocialMediaAutomation.ArchitectureTests/
    |-- deploy/
    `-- scripts/
```

Dependency direction: `Api`/`Worker` -> `Application` + `Infrastructure`; `Infrastructure` -> `Application` -> `Domain`. `SocialMediaAutomation.ArchitectureTests` enforces it. `ARCHITECTURE.md` §51 has the full folder conventions.

---

## 12. Local development prerequisites

- .NET SDK 10 (built with 10.0.401)
- Node.js 24 (`>=24.0`) and npm. The frontend uses Angular 21, because Angular 22 requires Node `>=24.15`.
- Docker Desktop (runs local SQL Server 2022)
- Git
- Optional: an API key for a hosted LLM (OpenAI, Groq or any OpenAI-compatible endpoint, entered in the app) and LinkedIn/Meta developer app credentials. Without them the app still runs end to end: AI generation needs a provider, and publishing uses SIMULATED destinations.

Do not hardcode secrets in source files, browser configuration, screenshots, AI prompts, or logs.

---

## 13. Local startup flow

All commands run from the repository root unless noted.

**Quick start** (after the one-time setup in step 1):

```bash
docker compose up -d                                                                  # SQL Server
cd backend && dotnet tool restore && dotnet ef database update --project src/SocialMediaAutomation.Infrastructure --startup-project src/SocialMediaAutomation.Api && cd ..
dotnet run --project backend/src/SocialMediaAutomation.Api --launch-profile http       # terminal 1: API on :5242
dotnet run --project backend/src/SocialMediaAutomation.Worker                          # terminal 2: AI + publishing workers
cd frontend && npm install && npm start                                               # terminal 3: web app on :4200
```

Open http://localhost:4200 and either create an account at `/signup` (email, password and workspace name; you become the workspace Admin) or sign in as `admin@demo-a.example`, `creator@demo-a.example` or `reviewer@demo-a.example` with your `Seed:DemoPassword`. New accounts are guided through `/onboarding/profile` (full name, time zone), `/onboarding/brand` and `/onboarding/social-connections` (at least one account) before Home. AI generation needs the system AI provider configured (see "AI provider" below). The API, worker and web app must all be running: without the worker, AI requests stay pending and scheduled posts never run.

### 1. Configure environment

```bash
cp .env.example .env          # set MSSQL_SA_PASSWORD (.env is gitignored)
```

The API and worker share one user-secrets store (`social-media-automation-dev`), so this sets the connection string for both:

```bash
dotnet user-secrets set "ConnectionStrings:Core" \
  "Server=localhost,1433;Database=SocialMediaAutomation;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True" \
  --project backend/src/SocialMediaAutomation.Api

# shared password for all seeded demo users (choose your own; never commit it)
dotnet user-secrets set "Seed:DemoPassword" "<choose-a-demo-password>" --project backend/src/SocialMediaAutomation.Api
```

Configuration sections: `ConnectionStrings`, `Authentication`, `Ai`, `Storage`, `SocialProviders` (`LinkedIn`, `Facebook`, `Instagram`), and `Workers` (`Mode`, `Ai`, `Publishing`). Non-secret keys are in each project's `appsettings.json`. Secrets go in user-secrets locally and in environment variables when deployed (`ConnectionStrings__Core`, `Workers__Mode`, ...). `.env.example` lists them all. Both processes refuse to start without a connection string.

### 2. Start infrastructure

```bash
docker compose up -d
```

Media bytes are stored on local disk in development (`Storage:Provider = Local`, `Storage:LocalRoot = ../../.local-storage/media`, resolved from each project folder to `backend/.local-storage/media`, gitignored). Azure Blob storage for staging is not implemented yet.

### 3. Restore, build and test the backend

```bash
cd backend
dotnet tool restore
dotnet restore
dotnet build
dotnet test
```

Integration tests need the local SQL Server from step 2. Each run creates its own `SocialMediaAutomation_Test_<guid>` database on the server in `ConnectionStrings:Core` (user-secrets or `ConnectionStrings__Core`), applies migrations, seeds demo data, and drops the database afterwards. They fail rather than skip when no SQL Server is configured.

### 4. Apply database migrations

```bash
cd backend
dotnet ef database update --project src/SocialMediaAutomation.Infrastructure --startup-project src/SocialMediaAutomation.Api
# adding a migration (from TASKS.md Phase 2 onward):
dotnet ef migrations add <Name> --project src/SocialMediaAutomation.Infrastructure --startup-project src/SocialMediaAutomation.Api --output-dir Persistence/Migrations
```

`database update` also creates the database if it does not exist. The `CustomerExperience` migration only widens CHECK constraints (campaign objective and CTA `AUTO` = "Auto Detect", and the `TEAM_INVITATION` notification type). The `SimplifiedInputModel` migration converts existing brand profiles and campaigns to the simplified input model and cannot be reverted (its `Down` throws): back up a database with real data before applying it. Migrations live in `backend/src/SocialMediaAutomation.Infrastructure/Persistence/Migrations` and are committed to source control.

### 5. Seed demo data

In Development (`Seed:DemoData = true` in `appsettings.Development.json`), the API seeds demo data at startup once the database is migrated. Seeding is idempotent, and it is skipped with a warning if `Seed:DemoPassword` is not set. Every demo user signs in with that password. It also seeds **Centaiva Demo Workspace** (`admin@centaiva.test` Admin, `creator@centaiva.test` Creator, `reviewer@centaiva.test` Reviewer; brand profile and demo destinations, so they land on the dashboard) with `Seed:DemoWorkspacePassword`, and the internal system administrator described under "Internal system administration". None of the customer demo users is a system admin.

| Workspace | Email | Role |
|---|---|---|
| Demo Organization A (`a0000000-0000-0000-0000-00000000000a`, Europe/London, `RequireDistinctReviewer` on) | `admin@demo-a.example` | Admin |
| | `creator@demo-a.example` | Creator |
| | `reviewer@demo-a.example` | Reviewer |
| Demo Organization B (`b0000000-0000-0000-0000-00000000000b`, America/New_York), used for tenant-isolation checks | `admin@demo-b.example` | Admin |

Later phases extend the seed (brand profile, sample destinations in LIVE/TEST_ACCOUNT/SIMULATED/NOT_CONFIGURED modes). Secrets and real provider tokens must never be included in seed files.

### 6. Start the API

```bash
dotnet run --project backend/src/SocialMediaAutomation.Api --launch-profile http   # http://localhost:5242
curl http://localhost:5242/health/live     # process is up
curl http://localhost:5242/health/ready    # Healthy / Degraded (a worker is not reporting) / Unhealthy (503, SQL Server unreachable)
```

Authentication is a server-issued HttpOnly session cookie (`sma_session`). Browser code never sees a token. Every endpoint except login and `/health` requires a signed-in user, and every organization endpoint re-checks membership and role on each request.

| Method | Path | Who |
|---|---|---|
| POST | `/api/v1/auth/login`, `/api/v1/auth/logout` | anyone / signed in |
| GET | `/api/v1/me` | signed in |
| GET, POST | `/api/v1/organizations` | signed in (list own memberships / create, creator becomes Admin) |
| GET | `/api/v1/organizations/{orgId}` | any active member |
| PUT | `/api/v1/organizations/{orgId}/settings` | Admin, `If-Match` required |
| GET | `/api/v1/organizations/{orgId}/members` | any active member |
| PUT | `/api/v1/organizations/{orgId}/members/{memberId}/role` | Admin, `If-Match` required |
| POST | `/api/v1/organizations/{orgId}/members` | Admin; adds an existing account by email with a role (no invitation email in the MVP) |
| POST | `/api/v1/organizations/{orgId}/members/{memberId}/deactivate` | Admin, `If-Match`; the last active Admin cannot be deactivated |
| GET | `/api/v1/organizations/{orgId}/brand-profile` | any active member |
| PUT | `/api/v1/organizations/{orgId}/brand-profile` | Admin; creates when none exists, otherwise `If-Match` required |
| GET, POST | `/api/v1/organizations/{orgId}/campaigns` (`?includeArchived=true`) | any member reads; Admin/Creator create |
| GET, PUT | `/api/v1/organizations/{orgId}/campaigns/{campaignId}` | any member reads; Admin/Creator update with `If-Match` (response includes facts) |
| POST | `/api/v1/organizations/{orgId}/campaigns/{campaignId}/archive` | Admin/Creator, `If-Match`; archived campaigns are read-only |
| POST | `/api/v1/organizations/{orgId}/campaigns/{campaignId}/facts` | Admin/Creator |
| PUT, DELETE | `/api/v1/organizations/{orgId}/campaigns/{campaignId}/facts/{factId}` | Admin/Creator, `If-Match`; DELETE is a soft remove |
| GET, POST | `/api/v1/organizations/{orgId}/campaigns/{campaignId}/posts` | any member lists; Admin/Creator create a platform post with Version 1 |
| GET | `/api/v1/organizations/{orgId}/posts` | any member; every non-archived post with its latest publishing state (Publisher, Home) |
| GET | `/api/v1/organizations/{orgId}/posts/{postId}` and `.../versions` | any member (post with current version; full version history) |
| POST | `/api/v1/organizations/{orgId}/posts/{postId}/versions` | Admin/Creator, `If-Match`; saves an edit as a new immutable version (no-op if nothing changed) |
| POST | `/api/v1/organizations/{orgId}/media` | Admin/Creator; multipart `file`, JPEG/PNG detected from bytes, max 8 MB |
| GET | `/api/v1/organizations/{orgId}/media` | any member; Media Library (assets with usage count) |
| GET | `/api/v1/organizations/{orgId}/media/{id}` and `.../content` | any member (metadata; image bytes for preview) |

Errors are RFC 7807 problem responses with a stable `code` (`UNAUTHORIZED`, `FORBIDDEN`, `NOT_FOUND`, `VALIDATION_FAILED`, `CONCURRENCY_CONFLICT`, `INVALID_STATE_TRANSITION`). A workspace the caller does not belong to returns `NOT_FOUND`, the same as an unknown id.

### 7. Start workers

The worker reads `Workers:Mode` = `Ai`, `Publishing`, or `Combined`. Development defaults to `Combined`. Other environments must set it explicitly; an unknown or missing mode stops startup.

```bash
dotnet run --project backend/src/SocialMediaAutomation.Worker
dotnet run --project backend/src/SocialMediaAutomation.Worker -- --Workers:Mode=Publishing   # run one mode
```

The AI worker polls SQL every `Workers:Ai:PollIntervalSeconds` and processes queued generation and rewrite requests. The publishing worker polls every `Workers:Publishing:PollIntervalSeconds` and runs due publication jobs. Without a running worker nothing is lost: AI requests stay `PENDING` and jobs stay `SCHEDULED` until it starts. Both record a heartbeat that `/health/ready` and the Admin dashboard show.

#### Internal system administration (`/internal`)

Workspace Admin is a customer role and never grants platform access. System Admin is a platform-level flag on the user (`Users.IsSystemAdmin`), not an `OrganizationMember` role, and no product screen or endpoint can assign it.

- Sign in at `/internal/login` (`POST /api/internal/auth/login`). Valid credentials without the flag get `403 FORBIDDEN` and no session. Customers keep using `/login`.
- Every `/api/internal/*` endpoint (`/me`, `/overview`) requires the `RequireSystemAdmin` policy: a session from the internal sign-in and the flag still set in the database. The legacy org-scoped `ai-settings` and `operations` endpoints also require the flag.
- In Development the API seeds `systemadmin@local.test` from `Seed:SystemAdmin:Email/Password` in `appsettings.Development.json` (created only if no user has that email; never seeded outside Development). Change these values locally; never commit a real password.
- Production: there is no seed. Grant the flag to an existing user directly in the database (`UPDATE Users SET IsSystemAdmin = 1 WHERE NormalizedEmail = '<EMAIL IN UPPER CASE>'`).
- Pages (grouped sidebar): Overview; Workspaces (suspend/reactivate, audit history) and Users (disable/enable, sign-in and audit activity); AI / LLM and Social Providers configuration; Workers, Queues, Publications and Reconciliation (Check Provider Status, Confirm Published, Confirm Failed; never a blind resend); System Health; Logs (Audit, Sign-ins, AI, Publishing, System). All read/write endpoints live under `/api/internal/*` and are listed in `InternalControllers.cs`.
- **AI image generation with Hugging Face:** in `/internal/ai` → Image Generation choose *Hugging Face*, pick a model (`Tongyi-MAI/Z-Image-Turbo` default, `black-forest-labs/FLUX.1-schnell`, or `Efficient-Large-Model/SANA1.5_1.6B_1024px_diffusers`), paste a Hugging Face token (`hf_…`, fine-grained with *Make calls to Inference Providers*) as the image key, Test Connection, tick *Enabled* and save. Usage is billed to that Hugging Face account. Apply the `HuggingFaceProvider` migration first. Users then get *Text + Image* in AI Studio and a *Generate image* button in Composer.
- **Secrets entered in `/internal/ai` and `/internal/social-providers`** (LLM API key, LinkedIn client secret, Meta app secret) are write-only: stored through `ISecretStore`, never returned by any API, never logged. The MVP store keeps them in `PlatformSecrets`, encrypted with the Data Protection key ring (`DataProtection:KeysPath`, shared by API and worker); a managed store such as Azure Key Vault plugs in behind the same interface. Configuration rows hold only a secret reference.
- Precedence: platform settings saved in `/internal` override the `Ai__*` and `SocialProviders__*` environment configuration below. Disabling AI or a provider there turns it off even if environment values exist.
- Suspending a workspace blocks its members (`403 WORKSPACE_SUSPENDED`) and stops due posts from being sent; disabling a user ends their open sessions immediately. Every operator action is recorded in the platform audit log with actor, target, result and correlation id; every sign-in, failed sign-in and sign-out is recorded (never the password or cookie).
- Correlation ids: each API request has one (`X-Correlation-Id`); AI requests and publication jobs store the id of the request that created them, so Logs can follow one operation from API to worker to provider.
- Migration `PlatformAdministration` adds the platform configuration, secret, audit, sign-in and system-event tables plus correlation-id and worker-version columns.

#### AI provider (system configuration, not a user setting)

The hosted LLM is configured by developers/operators, never by users. Set these for **both** the API and the worker, through environment variables or user-secrets (never commit the key):

```bash
Ai__Provider=OpenAI          # OpenAI | Groq | OpenAICompatible
Ai__Model=gpt-4o-mini
Ai__ApiKey=<secret>
Ai__BaseUrl=                 # only for OpenAICompatible, e.g. http://localhost:11434/v1
```

When `Ai:ApiKey` and `Ai:Model` are set they are used for every workspace. The older per-workspace `ai-settings` endpoints below remain only as an internal fallback (used by the integration tests); the web app no longer shows them. Keys stored that way are encrypted with ASP.NET Data Protection (key ring at `DataProtection:KeysPath`) and never returned by the API.

| Method | Path | Who |
|---|---|---|
| GET, PUT | `/api/v1/organizations/{orgId}/ai-settings` | members read (no key, only a hint); Admin saves |
| POST | `/api/v1/organizations/{orgId}/ai-settings/models` | Admin; lists provider models |
| POST | `/api/v1/organizations/{orgId}/campaigns/{campaignId}/ai-generations` | Admin/Creator; 202 Accepted, durable request |
| GET | `/api/v1/organizations/{orgId}/campaigns/{campaignId}/ai-generations`, `/ai-generations/{id}` | any member; status, failure, outputs |
| POST | `/api/v1/organizations/{orgId}/posts/{postId}/rewrites` | Admin/Creator, `If-Match`; 202 Accepted |
| GET | `/api/v1/organizations/{orgId}/posts/{postId}/evaluations` | any member; advisory scores and issues per version |

#### Destinations, review and approval

The demo seed gives Organization A one SIMULATED destination per platform plus a NOT_CONFIGURED Facebook page, and gives Organization B one SIMULATED LinkedIn destination. A post version needs an eligible destination (same workspace and platform, not NOT_CONFIGURED) before it can be submitted for review.

Live/test-account destinations come from provider sign-in under Settings > Connections. It is available only when the provider's app credentials are set, for example `dotnet user-secrets set "SocialProviders:LinkedIn:ClientSecret" "<secret>" --project backend/src/SocialMediaAutomation.Api` plus `ClientId` (LinkedIn) or `AppId`/`AppSecret` (Facebook, Instagram). Register `http://localhost:4200/api/v1/oauth/{linkedin|facebook|instagram}/callback` as the redirect URL in the provider app; it is preset in `appsettings.Development.json` (`SocialProviders:{Platform}:RedirectUri`), and `App:WebBaseUrl` is where the callback sends the browser back. Without credentials the UI shows the provider as blocked and SIMULATED destinations keep the workflow usable.

| Method | Path | Who |
|---|---|---|
| GET, POST | `/api/v1/organizations/{orgId}/social-accounts` | members list; Admin creates SIMULATED/NOT_CONFIGURED destinations |
| GET | `/api/v1/organizations/{orgId}/social-connections/providers` | any member; whether live connection is available per provider, or why it is blocked |
| GET | `/api/v1/organizations/{orgId}/social-connections` | any member; grant metadata only (never tokens) |
| POST | `/api/v1/organizations/{orgId}/social-connections/{platform}/start` | Admin; `{ "mode": "LIVE" \| "TEST_ACCOUNT" }` → `{ authorizationUrl }`; 409 `PROVIDER_NOT_CONFIGURED` without app credentials |
| POST | `/api/v1/organizations/{orgId}/social-connections/{id}/disconnect` | Admin; wipes tokens, destinations become DISCONNECTED (never SIMULATED) |
| GET | `/api/v1/oauth/{platform}/callback` | anonymous provider redirect; validates the one-time state and redirects to `/settings/connections` |
| POST | `/api/v1/organizations/{orgId}/posts/{postId}/submit-review` | Admin/Creator, `If-Match` (post ETag); 409 `SUBMISSION_BLOCKED` lists the reasons |
| GET | `/api/v1/organizations/{orgId}/review-requests` (`?status=PENDING`, `?postId=`) and `/review-requests/{id}` | any member; the detail is the full reviewer view of the exact version |
| POST | `/api/v1/organizations/{orgId}/review-requests/{id}/approve` | Reviewer/Admin, `If-Match` (review ETag); 403 `DISTINCT_REVIEWER_REQUIRED` for the submitter or last editor when the policy is on |
| POST | `/api/v1/organizations/{orgId}/review-requests/{id}/request-changes` | Reviewer/Admin, `If-Match`; explanation required |
| POST | `/api/v1/organizations/{orgId}/review-requests/{id}/comments` | any member |
| GET, POST | `/api/v1/organizations/{orgId}/notifications` (`/unread-count`, `/{id}/read`, `/read-all`) | the signed-in user's own notifications |
| GET | `/api/v1/organizations/{orgId}/audit` (`?resourceId=`) | Admin |

#### Scheduling and Publish Now

Both create a durable `PublicationJob` only; the publishing worker executes it. Only the approved current version can be scheduled, once per destination.

| Method | Path | Who |
|---|---|---|
| POST | `/api/v1/organizations/{orgId}/posts/{postId}/schedule` | Admin/Creator, `If-Match` (post ETag); `{ postVersionId, localDate: "2026-11-01", localTime: "09:30:00", timeZone: "Europe/London", utcOffsetMinutes? }`; 201 new, 200 identical repeat, 409 `APPROVAL_REQUIRED`/`APPROVAL_STALE`/`SOCIAL_ACCOUNT_NOT_READY`/`IDEMPOTENCY_CONFLICT`, 400 `LOCAL_TIME_NONEXISTENT`/`LOCAL_TIME_AMBIGUOUS` |
| POST | `/api/v1/organizations/{orgId}/posts/{postId}/publish-now` | Admin/Creator, `If-Match`; `{ postVersionId }`; immediately due job |
| GET | `/api/v1/organizations/{orgId}/publication-jobs` (`?postId=`, `?openOnly=true`) and `/publication-jobs/{id}` | any member |
| POST | `/api/v1/organizations/{orgId}/publication-jobs/{id}/cancel` | Admin/Creator, `If-Match` (job ETag); 409 `PUBLICATION_TOO_LATE_TO_CANCEL` once dispatch started |
| POST | `/api/v1/organizations/{orgId}/publication-jobs/{id}/reschedule` | Admin/Creator, `If-Match`; same time fields as schedule |
| GET | `/api/v1/organizations/{orgId}/publication-jobs/{id}/attempts` | any member; every worker pass with result, normalized error and provider evidence |
| GET | `/api/v1/organizations/{orgId}/publication-jobs?from=&to=&platform=&state=` | any member; calendar filters (UTC range on the publish time, repeat `state`) |
| GET | `/api/v1/organizations/{orgId}/dashboard` | any member; editorial and delivery counts, pending reviews, upcoming and failed jobs, destinations |
| GET | `/api/v1/organizations/{orgId}/operations` | Admin; worker heartbeats, queue age, expired leases, throttling, action-required and reconciliation counts |

#### Health, security and diagnostics

- `GET /health/live` checks that the process answers. `GET /health/ready` (also `/health`) checks the database and reports `Degraded` when the AI or publishing worker has not sent a heartbeat for 2 minutes.
- Every response has an `X-Correlation-Id` header (a well-formed incoming value is reused). Problem responses include it as `correlationId`, and API and worker log lines carry it, or the `PublicationJobId` / `AiGenerationRequestId`, as a scope. Production logs are JSON (`appsettings.Production.json`).
- Sign-in, AI calls, OAuth starts and uploads are rate limited per minute (`RateLimiting:LoginPerMinute` 10, `AiPerMinute` 20, `OAuthPerMinute` 10, `UploadPerMinute` 30) and return 429 `RATE_LIMITED`.
- Unsafe requests from a browser Origin other than the API or `App:WebBaseUrl` get 403. Production enables HSTS and HTTPS redirection (`Security:RequireHttps`); behind a TLS-terminating proxy set `Security:TrustForwardedHeaders=true`.
- Acceptance suite: `dotnet test backend/tests/SocialMediaAutomation.IntegrationTests --filter Category=Acceptance`.

#### Publishing worker and providers

The publishing worker (`Workers:Mode` = `Publishing` or `Combined`) polls every `Workers:Publishing:PollIntervalSeconds`. It claims due jobs in one short SQL statement with a lease, revalidates everything before sending, and saves a dispatch checkpoint before the provider call. A job whose worker dies before that checkpoint is picked up again after the 5-minute lease expires; after the checkpoint it becomes `RECONCILIATION_REQUIRED` and is never resent automatically. Only failures that are known to be safe are retried (at most 3 attempts). Jobs more than 2 hours late, and anything a person must fix (auth, permission, media), end `ACTION_REQUIRED`.

| Destination mode | Publisher | Status |
|---|---|---|
| SIMULATED | `SimulatedPublisher` | works end to end; ends `SIMULATED`, never `PUBLISHED`, no external id |
| LIVE / TEST_ACCOUNT LinkedIn | `LinkedInPublisher` (Posts API, `SocialProviders:LinkedIn:ApiVersion`, default `202509`) | **unverified**: no LinkedIn app credentials; tested against stub HTTP only |
| LIVE / TEST_ACCOUNT Facebook | `FacebookPublisher` (Graph `/feed`, `/photos`) | **unverified**: no Meta app/Page access; stub HTTP only |
| LIVE / TEST_ACCOUNT Instagram | `InstagramPublisher` (container → status → `media_publish`) | **blocked**: no Meta access, and Instagram needs a public image URL that local storage cannot provide (jobs end `ACTION_REQUIRED` `MEDIA_URL_UNAVAILABLE`) |

"Continue with Google" sign-in is optional. Create an OAuth client (type "Web application") in Google Cloud Console, add `http://localhost:4200/api/v1/auth/google/callback` as an authorized redirect URI, then set `dotnet user-secrets set "Authentication:Google:ClientId" "<id>" --project backend/src/SocialMediaAutomation.Api` and `Authentication:Google:ClientSecret` the same way (deployed: `Authentication__Google__ClientId` / `__ClientSecret`). A verified Google email signs in the account with that email, or creates a new account with its own workspace. Without a client ID the button shows "Google sign-in is not available yet."

### 8. Start the web client

```bash
cd frontend
npm install
npm start          # http://localhost:4200, proxies /api and /health to the API
npm run build
npm test -- --watch=false
```

Sign in at http://localhost:4200 with a seeded demo account (§13.5) and the `Seed:DemoPassword` you set. The app provides:

- A left sidebar: **Home** (`/home`; quick create actions, workspace summary, content publishing ring, to-do, recent campaigns, upcoming publishing, social accounts, activity, and a small dismissible Get Started panel), **AI Studio** (`/ai-studio`, prompt-first; results are saved as a draft campaign), **Campaigns** (`/campaigns`, name + prompt + platforms; detail tabs Overview, Content, Activity), **Publisher** (`/publisher/planner|posts|approvals|drafts|scheduled|published|failed`), **Media Library** (`/library`), **Brand** (`/brand`), **Social Accounts** (`/social-accounts`, Admin), **Team** (`/team`, Admin), plus Notifications, Settings (`/settings/workspace`, Admin) and Profile.
- The post composer (`/posts/{id}`): edit, AI rewrites, versions, submit for review, schedule / publish now. Older links (`/dashboard`, `/approvals`, `/calendar`, `/settings/brand|team|social-connections`) redirect to the new pages. Role-based hiding in the UI is a convenience only; the API enforces every rule. Browser storage holds only UX preferences (the selected workspace id and whether Home's Get Started panel was hidden), never a token.

---

## 14. Manager demo path

The intended demonstration is:

1. Sign in as Creator.
2. Select the demo workspace.
3. Show brand settings.
4. Create a campaign with supporting facts.
5. Generate LinkedIn, Facebook, and Instagram variants.
6. Show platform adaptation and at least one automated warning.
7. Edit one variant and attach required media.
8. Select destination accounts.
9. Submit exact versions for review.
10. Switch to Reviewer.
11. Request a change on one version.
12. Create the corrected new version.
13. Approve the current version.
14. Demonstrate the backend rejecting an unapproved or stale scheduling attempt.
15. Schedule an approved post shortly in the future.
16. Close or refresh the browser.
17. Show that the worker continues and records the result.
18. Open the real external post when live/test access exists, otherwise show the clearly labelled `SIMULATED` result.
19. Show version history, audit history, one controlled reconciliation case, and one cross-workspace denial.

---

## 15. Definition of done for the MVP

### AI

Real model-generated platform variants are stored, structurally validated, evaluated, editable, and recoverable when a request fails.

### Governance

No publication job can be created from a non-current or unapproved version. Approval history remains immutable.

### Automation

Approved scheduled work executes independently of the browser through durable worker state and survives a normal application restart.

### Integrations

Each destination exposes its actual mode and capabilities. Live/test success records the external provider identifier or URL. Simulation is never represented as real external publication.

### Security and tenancy

A different workspace cannot read or modify business data. Provider credentials remain backend-only. Secrets are absent from source, logs, screenshots, and AI prompts.

### Evidence

The final handover includes test results, manager-demo evidence, provider access status, deployment/setup instructions, and known limitations.

---

## 16. Project documentation order

Before implementation, the coding agent or developer should use the repository documentation in this order:

1. `AGENTS.md` - rules the coding agent must follow
2. `PRD.md` - what product is being built and why
3. `ARCHITECTURE.md` - how the system fits together
4. `TASKS.md` - what to build next and in what order
5. `README.md` - how to understand, configure, run, and demonstrate the repository

If implementation behavior conflicts with the approved product or architecture documents, do not invent a new behavior. Stop and update the specification first.

---

## 17. Core project principle

> AI creates content; deterministic business rules decide what is allowed to happen.

The MVP should remain understandable, auditable, and demoable. Do not add complexity only to make the architecture look more advanced.
