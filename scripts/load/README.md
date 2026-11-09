# urbeat API smoke load test

Non-destructive k6 smoke test for the urbeat API. It only issues `GET` requests and is meant to
show that an environment is reachable and healthy, not to validate business flows.

> For the separate **mutable business-flow** scenario (synthetic sellers/stores/products and
> concurrent pay-on-delivery orders), see [BUSINESS-LOAD.md](./BUSINESS-LOAD.md). It is strictly for
> an explicitly authorized non-production environment and requires `-Mode Run`,
> `-BusinessTestAuthorized`, `-Cleanup`, and `-AllowRemote` for a remote target.

## Safety boundaries

- Only `GET`.
- Defaults to `http://localhost:5000` with a single virtual user and 30 seconds.
- No tokens, cookies, credentials, or private payloads.
- Never calls login, checkout, store creation, WhatsApp, or any mutating endpoint.
- Do not run stress tests against production. A remote target must be explicitly authorized.

## Requirements

Install k6 from the official instructions: <https://k6.io/docs/get-started/installation/>. The
PowerShell wrapper does not install tools automatically.

## Run locally

From the repository root, with the API already running on `http://localhost:5000`:

```powershell
k6 run scripts/load/urbeat-api-smoke.js
```

Or through the wrapper, which validates the URL and delegates to k6:

```powershell
pwsh ./scripts/load/run-urbeat-api-smoke.ps1
```

## Optional public GET path

`PUBLIC_PATH` adds a second safe `GET` to the same origin. Good candidates are read-only public
endpoints such as `/api/public/plans` or `/api/public/stores`. Leave it unset to run health-only.

```powershell
$env:PUBLIC_PATH = "/api/public/plans"
k6 run scripts/load/urbeat-api-smoke.js
```

```bash
PUBLIC_PATH=/api/public/plans k6 run scripts/load/urbeat-api-smoke.js
```

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `BASE_URL` | `http://localhost:5000` | API origin; must not include credentials, query, or fragment. |
| `PUBLIC_PATH` | empty | Optional public read-only path on `BASE_URL`. |
| `SCENARIO` | `smoke` | `smoke` (constant VUs) or `ramp` (ramping VUs, then hold). |
| `VUS` | `1` | Virtual users for the constant scenario, or the target for the ramp scenario. |
| `DURATION` | `30s` | Duration of the run, or the hold stage at the target VU count for `ramp`. |
| `RAMP_UP_DURATION` | `30s` | Ramp-up stage used only by `SCENARIO=ramp`. |
| `SLEEP_SECONDS` | `1` | Pause between iterations to keep the test gentle. |

## Ramp-up scenario

`SCENARIO=ramp` starts at zero, reaches `VUS` over `RAMP_UP_DURATION`, and then holds that load for
`DURATION`. It is still read-only and uses the same health/public requests, but it separates
warm-up latency from steady-state behavior:

```bash
BASE_URL=http://localhost:5000 \
PUBLIC_PATH=/api/public/plans \
SCENARIO=ramp \
VUS=50 \
RAMP_UP_DURATION=1m \
DURATION=2m \
k6 run scripts/load/urbeat-api-smoke.js
```

```powershell
pwsh ./scripts/load/run-urbeat-api-smoke.ps1 `
  -Scenario ramp `
  -Vus 50 `
  -RampUpDuration 1m `
  -Duration 2m `
  -PublicPath /api/public/plans
```

## Low-load run against an authorized URL

Use a staging or explicitly authorized environment, never production for stress:

```bash
BASE_URL=https://staging.example.com \
PUBLIC_PATH=/api/public/plans \
VUS=2 \
DURATION=30s \
k6 run scripts/load/urbeat-api-smoke.js
```

```powershell
pwsh ./scripts/load/run-urbeat-api-smoke.ps1 `
  -BaseUrl https://staging.example.com `
  -AllowRemote `
  -Vus 2 `
  -Duration 30s `
  -PublicPath /api/public/plans
```

## Thresholds

| Metric | Threshold | Meaning |
| --- | --- | --- |
| `checks` | `rate>0.99` | At least 99% of status assertions must pass. |
| `http_req_failed` | `rate<0.01` | Fewer than 1% of requests may fail. |
| `http_req_duration{endpoint:health}` | `p(95)<500ms`, `p(99)<1000ms` | Health latency budget. |
| `http_req_duration{endpoint:public}` | `p(95)<800ms`, `p(99)<1500ms` | Optional public path latency budget. |

A `503` from `/health` means a dependency is unhealthy (for example the outbox health check or the
database), not necessarily that the API is under load pressure. Treat it as an environment signal.

## Limits

Health-only runs do not exercise the order flow. They do not cover authentication, catalog search,
cart, checkout, payments, SignalR/WhatsApp delivery, file upload, or database write paths. Those
flows need a dedicated authenticated/staging scenario built with synthetic data and seeded test
accounts, isolated from production. This script is intentionally not that scenario.
