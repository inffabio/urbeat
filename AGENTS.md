# Configuração de Agentes Multi-Model no OpenCode

## Agente 1: Arquiteto de Software (Líder / Planejador)

- **Modelo:** `openai/chatgpt-5.5`
- **Fluxo:** `plan`
- **Instruções do Sistema:**
  Você é o Arquiteto de Software Principal. Sua função é receber os requisitos do usuário, analisar o código atual da aplicação e criar um plano técnico detalhado passo a passo de como implementar a solução. Divida a tarefa em subtarefas atômicas e claras. Escreva a arquitetura detalhada e as assinaturas de funções, mas **não escreva o código final**. Delegue a escrita do código para o Desenvolvedor Pleno.

## Agente 2: Desenvolvedor Pleno (Executor)

- **Modelo:** `deepseek/deepseek-v4-pro`
- **Fluxo:** `build`
- **Instruções do Sistema:**
  Você é um Desenvolvedor Pleno focado em execução rápida, eficiente e limpa. Você deve receber estritamente o plano técnico de arquitetura criado pelo Arquiteto (ChatGPT-5.5) e segui-lo à risca para gerar os códigos, criar arquivos, resolver testes e debugar erros encontrados no terminal.

---

## Do Not Miss

- **Backend owns business rules.** Prices, validation, state transitions, checkout totals, payment/order status are always recomputed server-side; Angular sends inputs only.
- **Frontend is Angular 20 standalone + Ionic 8.** No PrimeNG. Trust `frontend/package.json` over stale docs — especially `DefinicoesTecnicas.md`, which is an early aspirational document referencing PrimeNG (not used) and other outdated stack choices.
- **Mobile target**: PWA (service worker + install prompt) + Capacitor 8 for native Android/iOS. Bluetooth printing via `cordova-plugin-bluetooth-serial`. The frontend is one codebase for web, PWA, and native.
- **UI work must load the `/impeccable` skill before code**, follow `DESIGN.md`, use `--app-*` CSS vars from `frontend/src/theme/variables.scss`, and be mobile-first (44px+ touch targets, safe-area).
- **Componentize Angular UI aggressively.** Reusable cards, buttons, inputs, modals, empty states, skeletons belong in `frontend/src/app/shared/components/`; pages compose from them.
- **Keep production and source in sync.** Any manual production SQL, config, nginx, or `.env` change must be reflected back in migrations, entities, or repo config.

## Naming

- **Backend**: `Urbeat.*` — project names, directories, `.csproj` files, C# namespaces, and `.sln` all use Urbeat.
- **Frontend**: Package `urbeat-frontend`, Capacitor `com.urbeat.app`, container names `urbeat_*`, CSS classes `urbeat-toast-*`, localStorage keys `urbeat_*`.
- **Logs DB**: Separate project `backend/src/UrbeatLogs` (NOT in the `.sln`), loaded by WebApi via `appsettings.UrbeatLogs.json`.
- **Brand files**: Logo at `frontend/src/assets/images/logo_v2.png` (also `.svg`), favicon at `frontend/src/favicon.ico`.

## Commands

```bash
# Backend — build from repo root
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~MyTestClass"
dotnet test backend/tests/Urbeat.IntegrationTests   # uses InMemory database (no Docker needed)

# Frontend (from frontend/)
npm ci
npx ng build --configuration production
npx ng serve   # uses proxy.conf.json → localhost:5000
npx jest --no-coverage                   # fast (collectCoverage is on by default)
npx jest --no-coverage src/app/path/to/file.spec.ts

# Production deploy (Windows/PowerShell from repo root)
./scripts/criarDeployOracleCloud/deploy-all.ps1 -Step application -ServerIP "..." -SSHUser "ubuntu"
./scripts/criarDeployOracleCloud/deploy-all.ps1 -Step verify -ServerIP "..." -SSHUser "ubuntu"
```

## Architecture

- **Backend**: Clean Architecture .NET 9 — `Urbeat.Domain` → `Urbeat.Application` → `Urbeat.Infrastructure` → `Urbeat.WebApi`. Tests at `backend/tests/` (xUnit + FluentAssertions + Moq + coverlet). EF Core with PostgreSQL.
- **Frontend**: Angular 20 standalone, bootstrapped in `src/main.ts` via `appConfig`. Routes in `src/app/app.routes.ts`. Ionic via `provideIonicAngular({ mode: 'md' })`.
- **Route layout**: Three groups — public pages (landing, login, register, forgot-password), `/app/*` (auth-guarded seller dashboard shell), `/:storePath/*` (customer storefront). Guards enforce seller role for `/app/*`. There is no standalone customer registration page; customers register inline during checkout. Note also `/painel/*` for admin login/landing (separate adminGuard) and `configurar-loja` as the public store-setup wizard.
- **33 feature directories** under `src/app/features/`, each lazy-loaded. Two distinct email-confirmation features: `email-confirm/` (routes `c/:code`, `confirmar-email`) and `email-confirmation/` (route `confirmacao-email`). A standalone `/produtos` public route also exists. Core services/utils/guards at `src/app/core/`. Design tokens at `src/theme/variables.scss`.
- **Runtime stores**: PostgreSQL 16 main DB, separate `UrbeatLogs` DB, Redis 7, Hangfire (InMemory in dev, PostgreSQL in prod), SignalR hubs, Cloudinary image uploads.
- **SignalR hubs**: `/hubs/seller-notifications` and `/hubs/customer-notifications`. Service at `frontend/src/app/core/services/signalr.service.ts`.
- **PWA**: Service worker (`@angular/service-worker`), manifest, install prompt. Config at `frontend/ngsw-config.json` (caches all JS/CSS, not `/api`).

## Frontend Gotchas

- `environment*.ts` uses empty `apiUrl` and `signalRBase`; all calls are relative. `ng serve` relies on `proxy.conf.json` for `/api`, `/hubs` (with `ws: true`), and `/health` to `localhost:5000`. Also has `useMock: false`.
- `DESIGN.md` at repo root defines the design system (colors, typography, spacing) used by the `/impeccable` skill. Font is `'Plus Jakarta Sans'` (loaded via `global.scss` `@import`). **Note**: `DESIGN.md` internally references "Inter" as the font — this is stale; the codebase uses Plus Jakarta Sans exclusively.
- Angular style entry is `styles.scss` which `@use`s `theme/variables.scss` and `theme/global.scss`.
- Jest path aliases (`@core`, `@features`, `@shared`, `@environments`) are in **`jest.config.js` only**, not `tsconfig.json`. Angular resolves imports differently. Jest setup file is `frontend/setup-jest.ts` (zone.js test env + CSS/DOMParser mocks).
- Ionic, ionicons, and leaflet are mocked in `frontend/src/__mocks__/`.
- `tsconfig.json` is strict (`strictTemplates`, `noImplicitReturns`, `noPropertyAccessFromIndexSignature`). Fix types rather than weakening config.
- **No ESLint, Prettier, EditorConfig, Husky, or CI.** Run focused build/tests locally.
- `auth.guard.ts` checks for `Seller` JWT role via `isSeller()` helper in `core/utils/jwt.helper.ts` — decodes role from `role` or `http://schemas.microsoft.com/.../role` claim.
- `pending-changes.guard.ts` protects dirty forms on product/hours/delivery/bio edit routes.
- `core/utils/sao-paulo-date.helper.ts` provides `formatSaoPauloTime`, `formatSaoPauloDate`, `saoPauloPeriodRange` — all storage/filters are UTC, display uses `America/Sao_Paulo`.
- Toast service at `core/services/toast.service.ts` — wraps Ionic `ToastController`.

## Backend Gotchas

- **EF migrations auto-run at startup** in `Program.cs` via `ApplicationDbContext.Database.MigrateAsync()`. Do not use `dotnet ef database update` as a deploy step.
- Generate migrations from `backend/`: `dotnet ef migrations add Name --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure`. ~50+ migrations exist.
- **Dapper is used for read-side queries** in `Urbeat.Infrastructure` (`StoreReadRepository`, `DapperUnitOfWork`). EF Core handles write side.
- **Logs DB is separate**: project `backend/src/UrbeatLogs`, config `appsettings.UrbeatLogs.json` loaded explicitly; its `DbContextFactory` has a hardcoded connection string.
- **Hangfire dashboard** at `/hangfire` with Basic Auth (`Hangfire:DashboardUser`, `Hangfire:DashboardPassword`). Recurring jobs: operational heartbeat (hourly) and subscription notifications (daily).
- **Prometheus metrics** at `/metrics`. Custom counters in `Program.cs` (orders, payments, users, stores, products — prefix `urbeat_`).
- Refresh token cookie: `urbeat.refresh_token`. JWT: 15 min access, 7 day refresh (configurable).
- Integration tests use `WebApplicationFactory` + InMemory database; Docker is not required.
- Middleware order: `ProblemDetailsMiddleware` and `GlobalExceptionMiddleware` are registered **before** `UseAuthentication`/`UseAuthorization` — they only catch exceptions from downstream endpoints, not 401/403 from auth itself.
- Seeder order in `Program.cs`: CuisineType → AdminUser → SystemParameter → LandingPage. `DemoDataSeeder` is commented out.
- nginx reverse proxy forwarding — `UseForwardedHeaders` with empty `KnownNetworks`/`KnownProxies` (trusts all).
- Production `appsettings.json` secrets are injected via Docker env vars, not committed.

## Docker & Deploy

- **Root `docker-compose.yml`**: minimal local dev (Postgres 16, Redis 7, WebApi, frontend asset build). Container names prefixed `urbeat_`.
- **`docker/docker-compose.dev.yml`**: full local stack with nginx, Prometheus, Grafana, env vars from `docker/.env` (copy from `.env.example`).
- **Production**: ARM64 (aarch64) on Oracle Cloud. Frontend container is a run-once Angular asset copier; nginx serves the shared volume. WebApi container listens on port 8080 internally, frontend build uses `node:22-alpine`.
- **Deploy pipeline** at `scripts/criarDeployOracleCloud/`: 10 scripts (prerequisites → docker → environment → application → nginx → ssl → verify + cleanup). Secrets mapped via `configs/secrets-map.json`.
- The `04-deploy-application.ps1` script generates a full `docker-compose.yml` on the server at `/opt/urbeat/`.
- Deploy package excludes `backend/**/bin`, `backend/**/obj`, `backend/**/TestResults`, `frontend/node_modules`, `frontend/.angular`, `frontend/dist`, `frontend/coverage`.
- `.gitignore` excludes `docker/.env.production`, `scripts/Zohokeys.txt`, `*Zoho*.txt`, `.impeccable/critique/`.
- Docker env pattern: `docker/.env.example` (committed template) → copy to `docker/.env` for local full-stack dev. `docker/.env.production` on the server is the only authoritative copy of secrets — never committed.
- **Project-local skills** at `.opencode/skills/` (cloudinary-docs, cloudinary-transformations, deploy-oci, impeccable). `skills-lock.json` at **repo root** pins cloudinary-docs, cloudinary-transformations, and impeccable versions (deploy-oci is not pinned).
