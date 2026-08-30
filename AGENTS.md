# Urbeat Agent Instructions

Trust executable config and scripts over stale prose, especially `DefinicoesTecnicas.md`.

## Boundaries

- `backend/Urbeat.sln` is the .NET 9 application: `Domain -> Application -> Infrastructure -> WebApi`, with unit and integration tests under `backend/tests/`.
- `backend/src/UrbeatLogs` is a separate logs-database project and is not in the solution.
- `frontend/` is Angular 20 standalone + Ionic 8, bootstrapped by `frontend/src/main.ts`; routes are in `frontend/src/app/app.routes.ts`.
- `print-agent/` and `oci-mcp-server/` are separate projects; do not change them for normal application work.
- `frontend/` also contains Capacitor Android/iOS projects; app labels live in `capacitor.config.ts` and native resource files.

## Commands

Run backend commands from the repository root:

```bash
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~MyTestClass"
dotnet test backend/tests/Urbeat.IntegrationTests
```

Run frontend commands from `frontend/` (there is no committed npm lockfile, so use `npm install`, not `npm ci`):

```bash
npm install
npx ng build --configuration production
npx ng serve
npx jest --no-coverage
npx jest --no-coverage src/app/path/to/file.spec.ts
```

- `ng serve` proxies `/api`, `/hubs` (including WebSockets), and `/health` to `http://localhost:5000`; the API must be running separately.
- Jest collects coverage by default; use `--no-coverage` for focused runs. Its path aliases are in `frontend/jest.config.js`, not `tsconfig.json`.
- There is no repository ESLint, Prettier, pre-commit hook, or CI configuration.
- Integration tests replace EF Core with an InMemory database, so Docker is not required for them.

## Backend Constraints

- Backend owns validation, prices, checkout totals, and payment/order/store state transitions; client values are inputs, not trusted results.
- EF Core handles writes; Infrastructure read-side repositories may use Dapper.
- WebApi applies EF migrations at startup with `Database.MigrateAsync()`. Do not deploy with `dotnet ef database update`.
- Add migrations from `backend/`: `dotnet ef migrations add <Name> --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure`.
- Startup also runs cuisine, admin, system-parameter, and landing-page seeders; changes to seed data must account for existing database rows.
- A store may open or serve checkout only when its publish summary has `CanPublish`.
- Neighborhood snapshots accept empty coordinate pairs for pending neighborhoods, reject partial/invalid pairs, and restoration must preserve empty fields rather than invent coordinates.

## Frontend Constraints

- Keep TypeScript and Angular template strictness enabled; fix types instead of weakening `tsconfig`.
- Production API and SignalR URLs are intentionally relative to the current origin.
- Use `frontend/src/theme/variables.scss` (`var(--app-*)`) and the existing font/tokens. Product, storefront, and dashboard surfaces use bordeaux; landing, seller auth, and `/configurar-loja` use the scoped `.urbeat-onboarding` green theme. Read `DESIGN.md` and use the `impeccable` skill for UI changes.
- Put reusable Angular UI in `frontend/src/app/shared/components/`; preserve mobile/PWA/Capacitor behavior and 44px-plus touch targets. Do not introduce PrimeNG or new Bootstrap components.
- Keep timestamps in UTC and use the existing Sao Paulo helpers for display and calendar boundaries.
- If a dependency is CommonJS, add it to `allowedCommonJsDependencies` in `frontend/angular.json`.

## Operations

- Local full stack: `docker compose up --build` from the repository root. The root stack uses PostgreSQL 16 on port 5432, Redis 7, WebApi on 5000, and frontend on 4200.
- Deployment entrypoint: `scripts/criarDeployOracleCloud/deploy-all.ps1`. Its `all` pipeline is `prerequisites -> vault -> docker -> environment -> application -> nginx -> ssl -> verify`; run `scripts/criarDeployOracleCloud/validate-pipeline.ps1` before deployment.
- OCI deploy requires configured OCI CLI access, a local ignored `scripts/criarDeployOracleCloud/configs/oci.local.json` (copied from `.example`) or `OCI_COMPARTMENT_OCID`, the existing Vault and enabled encryption key, and SSH access; Vault display names `urbeat`, `Urbeat`, and `UrBeat` are accepted case-insensitively. A failed `prerequisites` step must be fixed rather than bypassed.
- Use `deploy-all.ps1 -Step all -ServerIP "136.248.115.135" -SSHUser dexter -SSHPort 2208 -SSHKeyPath "$HOME/.ssh/id_ed25519"`; do not reorder steps or use `01-cleanup-secrets.ps1` for normal deploys.
- `scripts/deploy-internal.ps1` targets `192.168.1.15`, increments `frontend/package.json`, and commits/pushes; it is not the OCI deployment path.
- OCI SSH defaults are user `dexter`, port `2208`; do not weaken or reorder the pipeline or HTTP-before-SSL sequence.
- Never commit `.env` files, credentials, or secret values. Do not change, delete, replace, rotate, or print existing OCI Vault secrets without explicit authorization; use `configs/secrets-map.json` for mappings.
- OCI database rebuilds require backup first, optional explicit reset confirmation, then WebApi startup to apply committed migrations. Never use `dotnet ef database update` for this.
- The OpenCode PostgreSQL MCP is read-only at `localhost:15432/urbeat_main`; it is separate from the Compose database on port 5432.
- Build print-agent packages only when needed with `scripts/print-agent/build-print-agent-packages.ps1`; this publishes separate Windows/Linux artifacts under `downloads/urbeat-print-agent/`.

## Agent Collaboration

- The architect agent is responsible for clarifying requirements, analyzing architecture, identifying risks, and producing an implementation plan; it must not implement application code directly.
- The developer agent is responsible for implementing code changes, tests, scripts, and technical fixes based on the approved plan, then running the relevant validation commands.
- The coordinating agent must delegate implementation work to the developer agent, review the result, resolve review findings, and report verification status; it should not substitute direct implementation when the developer agent is available.
- Use the reviewer agent for an independent review after substantial implementation and before merge or deployment.
