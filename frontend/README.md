# Frontend: Angular web client

Presentation layer for the Social Media Content Automation Engine. The backend enforces every business rule (see `../AGENTS.md` §12). This app only hides or disables actions for UX.

## Commands

```bash
npm install
npm start                    # http://localhost:4200, proxies /api and /health to http://localhost:5242
npm run build
npm test -- --watch=false
```

## Structure

| Folder | Holds | Must not hold |
|---|---|---|
| `src/app/core/` | App-wide singletons: auth, guards, interceptors, HTTP setup, layout shell, config | Feature components |
| `src/app/shared/` | Reusable presentation pieces: components, directives, pipes, shared models, validators, utilities | Feature-specific business logic |
| `src/app/features/<feature>/` | One product area: `pages/`, `components/`, `services/` (e.g. `CampaignApiService`), `models/`, `<feature>.routes.ts` | Cross-feature code |
| `src/environments/` | `environment.ts` (production) and `environment.development.ts` (swapped in by the development build) | Secrets of any kind |
| `src/styles/` | Global styles | |
| `src/assets/` | Images and icons bundled under `/assets` | |

Create feature subfolders when the first file needs them. Use one focused API service per backend resource, not a shared catch-all service.
