# Urbeat agent instructions

Repository-wide instructions. Trust executable configuration and scripts over stale prose, especially `DefinicoesTecnicas.md`.

OpenCode config lives in `.opencode/opencode.json` (`default_agent: arquiteto`); the `arquiteto` primary agent delegates code changes to the `programador` subagent (`.opencode/agents/`). There are no nested `AGENTS.md` scopes.

## Boundaries and entrypoints

- `backend/Urbeat.sln` is the .NET 9 application (`Domain -> Application -> Infrastructure -> WebApi`) and includes unit/integration test projects.
- `backend/src/UrbeatLogs/` is a separate logs database project; it is not in `backend/Urbeat.sln`.
- `frontend/` is Angular 20 standalone + Ionic 8 + Capacitor 8; bootstrap is `frontend/src/main.ts`, routes are `frontend/src/app/app.routes.ts`, CLI config is `frontend/angular.json`, and Capacitor `webDir` is `dist/frontend/browser`.
- `print-agent/` and `oci-mcp-server/` are separate projects; do not change them for normal Urbeat application work.
- Capacitor Android/iOS resources under `frontend/` are separate from Angular routes and web assets.
- `docker/` holds the deployment Compose/nginx stack; root `docker-compose.yml` is for local development.

## Commands

- Backend from the repository root: `dotnet build backend/Urbeat.sln`.
- Backend tests: `dotnet test backend/tests/Urbeat.UnitTests`; focus with `--filter "FullyQualifiedName~TestClassOrMethod"`; integration suite: `dotnet test backend/tests/Urbeat.IntegrationTests`.
- Frontend from `frontend/`: `npm ci` for a clean install, `npx ng build --configuration production`, `npx ng serve`, and `npx jest --no-coverage`.
- Run one frontend spec with `npx jest --no-coverage src/app/path/to/file.spec.ts`; Jest aliases are in `frontend/jest.config.js`, not `tsconfig.json`, and coverage is enabled by default.
- `ng serve` proxies `/api`, `/hubs` (including WebSockets), and `/health` to `http://localhost:5000`; run the API separately.
- Most integration tests use EF Core InMemory through `TestWebApplicationFactory`; `Relational/*` tests use Testcontainers PostgreSQL and self-skip when Docker is unavailable, so Docker is required only to execute those paths.
- No repository lint, formatter, pre-commit hook, e2e suite, or CI workflow exists. Validate with the production build and targeted tests instead.

## Backend rules

- The backend is authoritative for validation, prices, checkout totals, and payment/order/store transitions; client values are inputs only.
- EF Core handles writes; Infrastructure read-side repositories may use Dapper.
- Relational WebApi startup runs `Database.MigrateAsync()` and seeders; the InMemory test harness skips both and seeds its own data. Add migrations from `backend/` with `dotnet ef migrations add <Name> --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure`.
- Never use `dotnet ef database update` against a deployment database; startup applies committed migrations. Seeder changes must work with existing rows.
- `PublishStoreAsync` requires `CanPublish`; publishing does not gate opening or checkout. Opening is blocked for subscription-blocked stores, and checkout requires `IsOpen` and `!IsSubscriptionBlocked`. Recompute prices, discounts, fees, and totals server-side.
- Preserve the neighborhood snapshot contract in `backend/scripts/import/neighborhood_snapshot.py`: empty Latitude/Longitude pairs mean pending neighborhoods; one-sided or invalid pairs are rejected; restore uses `COALESCE`, so existing coordinates are kept and empty fields stay empty.
- Store timestamps in UTC; use existing São Paulo helpers only for display and calendar boundaries.

## Frontend rules

- Keep TypeScript and Angular template strictness enabled; fix types instead of weakening `tsconfig`.
- Production API and SignalR URLs are intentionally relative to the current origin.
- Use `frontend/src/theme/variables.scss` and existing `var(--app-*)` tokens. Product/storefront/dashboard surfaces use bordeaux; landing, seller auth, and `/configurar-loja` use the scoped `.urbeat-onboarding` green theme. Read `DESIGN.md` and load the `impeccable` skill for UI work.
- Put reusable Angular UI in `frontend/src/app/shared/components/`; preserve mobile/PWA/Capacitor behavior and 44px-plus touch targets. Do not introduce PrimeNG or new Bootstrap components.
- If a dependency is CommonJS, add it to `allowedCommonJsDependencies` in `frontend/angular.json`.
- User-facing brand copy is lowercase `urbeat`; `frontend/src/app/branding-consistency.spec.ts` enforces this and covers Capacitor/PWA files too.

## Local stack and deployment

- `docker compose up --build` from the repository root starts PostgreSQL 16 on `5432`, Redis 7 on `6379`, and WebApi on host `5000`. Its `frontend` service only builds/copies assets and runs no HTTP server; use `npx ng serve` for `4200`. The deployed `docker/` stack runs nginx on `80/443` and maps WebApi `5000:8080`.
- The WebApi Docker image defaults to container port `8080`; root Compose sets `ASPNETCORE_URLS=http://+:5000`. Standalone `dotnet run` uses `http://localhost:5281` and `https://localhost:7274`.
- Do not run an OCI deployment unless the user explicitly requests it. Validate first with the local, non-destructive `scripts/criarDeployOracleCloud/validate-pipeline.ps1`; the `deploy-all.ps1 -Step all` order is `prerequisites -> vault -> docker -> environment -> application -> nginx -> ssl -> verify`. Do not reorder it or use `01-cleanup-secrets.ps1` for normal deploys (it schedules Vault secrets for deletion).
- Before OCI SSH/SCP, run `scripts/criarDeployOracleCloud/port-knock.ps1` in order: `1230/tcp`, `5688/udp`, `9112/tcp`. Defaults are SSH user `dexter`, port `2208`; use `-ExpectedCommit` for production. Dirty sources are blocked unless `-AllowDirty` is explicit; `deploy-all.ps1` forwards both only to the `application` step.
- `scripts/deploy-internal.ps1` is a separate path targeting `192.168.1.15`; it validates the source before any remote command and does not commit or push.
- Never commit `.env`, credentials, or secret values. Do not change, rotate, delete, or print existing OCI Vault secrets; use `scripts/criarDeployOracleCloud/configs/secrets-map.json` for mappings.
