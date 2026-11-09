# urbeat business-flow load test

Mutable k6 scenario for **explicitly authorized non-production urbeat environments**. It creates
synthetic sellers, stores, products, customers, and pay-on-delivery orders to exercise the real
business flow under concurrency.

> **Do not run this against production.** The wrapper rejects `urbeat.com.br`,
> `api.urbeat.com.br`, `www.urbeat.com.br`, and any `*.urbeat.com.br` host. A remote target requires
> `-AllowRemote`, and an actual run requires `-BusinessTestAuthorized` plus `-Cleanup`. The default
> mode is `Plan`, which performs no network calls and no k6 run.

## Safety contract

- Default mode is `Plan`: local validation only; no API calls, no k6, no mutations.
- `Preflight` performs only `GET /health`, `GET /health/ready`, `GET /api/public/stores`,
  `GET /api/stores/cuisine-types`, and, when a target store is supplied, public store/catalog reads.
- `Run` requires all of:
  - `-AllowRemote` when the target is not loopback;
  - `-BusinessTestAuthorized`;
  - `-Cleanup` (acknowledges a manual cleanup plan);
  - a seller/customer pool **or** `-AllowSyntheticAccountCreation`;
  - `-AllowNeighborhoodImport` when the run creates seller or anchor stores (see below).
- Every synthetic e-mail, store slug, category, and product carries `RUN_ID`.
- Synthetic e-mails use `loadtest.invalid` by default (`.invalid`/`.test` only). They can never
  reach a real inbox.
- The wrapper never prints access tokens, refresh cookies, passwords, pool contents, or admin
  tokens. Pool files are read locally; they are not copied to `artifacts/`.
- Do not enable k6 `--http-debug` or equivalent request logging: it can print request bodies that
  contain synthetic passwords.
- HTTP redirects are disabled in both the wrapper preflight and k6 requests. A test URL that
  redirects to production therefore fails before any mutation follows the redirect.
- The script never deletes data. There is no automatic cleanup.

## What the scenarios do

### `sellers` (per-VU iterations)

Each seller VU:

1. Obtains a confirmed Seller token:
   - from a pre-provisioned pool, or
   - by creating a confirmed Customer through `POST /api/checkout/customer-session` and promoting
     that identity with `POST /api/auth/register/seller` using the customer credential as
     `currentPassword`.
2. Creates one store with a `load-<RUN_ID>-s<VU>` slug.
3. Configures address, seven open business-hour days, a delivery area, one category, and one
   product with a harmless placeholder image URL.
4. Reads the publish summary, publishes the store, and verifies `isOpen`/`isPublished`.
5. Optionally (`-SellerStatusSmoke`) creates one pickup order in its own store, moves it from
   `Received` to `Preparing` with the seller token, and checks the seller order list.

### `customers` (ramping VUs)

Each customer VU gets its own session (never a shared account) and repeatedly:

1. Loads the public catalog.
2. Calls `POST /api/checkout/preview` for a pickup order by default.
3. Calls `POST /api/checkout/confirm` with `CashOnDelivery` and `cashWithoutChange: true`.
4. Reads back `GET /api/orders/{id}` and `GET /api/orders/my`.
5. Pauses for `SLEEP_SECONDS` (default `1`).

Pickup is the default because it avoids delivery-area dependencies. `-FulfillmentType delivery`
uses the customer address and requires a compatible target-area/neighborhood.

## Account bootstrap and e-mail safety

Registration normally creates an **unconfirmed** identity and queues a confirmation e-mail. This
scenario deliberately avoids that path for new identities:

1. `POST /api/checkout/customer-session` creates a confirmed Customer identity and returns an
   access token. It does not call the SMS/WhatsApp OTP sender and does not queue an e-mail
   confirmation.
2. `POST /api/auth/register/seller` is then used as a **role promotion** for that already-confirmed
   identity, authenticating with the customer access token as ownership proof. It adds the Seller
   role and credential; it does not queue a confirmation e-mail because the identity is confirmed.
3. The script asserts the promotion response has `emailConfirmationPending === false`. If that
   contract ever changes, it fails closed before creating more accounts.

The bootstrap relies on the anonymous `/api/checkout/customer-session` endpoint, which creates
confirmed customer identities by design for the guest checkout flow. Confirm that endpoint is
acceptable and enabled in the test environment. Its existence here is a test convenience, not a
recommendation for production exposure.

Defense in depth: configure the test environment with `Email:LogOnly=true` (or no SMTP) and
`WhatsApp:WebhookEnabled=false`. The script also never calls `/api/auth/email/*`,
`forgot-password`, `resend-confirmation`, WhatsApp, webhook, upload, or payment endpoints.

## Mock payment

This scenario intentionally does **not** call `POST /api/payments/order`. New checkouts no longer
accept online Pix/card, and the mock Pix worker is a legacy online-payment path that writes payment
rows and outbox events.

Instead, "mocked payment" means an order confirmed with `CashOnDelivery` (pay at handover). It
creates an order, order items, status history, audit rows, notifications, and an `OrderCreated`
outbox message. The outbox handler pushes SignalR and logs print delivery as best-effort; it does
not send WhatsApp.

## Neighborhood import / geocoding side effects

`PUT /api/stores/{storeId}/address` has two backend side effects:

1. If latitude/longitude are missing, it starts a background Nominatim geocoding task. The script
   always sends São Paulo coordinates, so this path is skipped.
2. It enqueues `ImportStoreNeighborhoodsJob`, which calls the OpenStreetMap importer for the store
   city/UF. This is independent of the coordinates and cannot be suppressed by the HTTP payload.

Because of item 2, runs that create seller or anchor stores require `-AllowNeighborhoodImport`.
That switch is an explicit operator attestation that the test environment disables or mocks the
`ImportStoreNeighborhoodsJob`/OSM importer (for example by not running the Hangfire business worker
or by configuring a no-op importer). The script cannot verify a server-side Hangfire job; without
the attestation it refuses to run the store-creation scenarios.

Customer-only runs against an existing `-TargetStoreId` do not call the store-address endpoint and
do not require this switch.

## Preconditions

- k6 installed and on `PATH` (or pass `-K6Path`).
- An authorized non-production environment.
- At least one existing store for bootstrap, unless you pass `-BootstrapStoreId`/`-TargetStoreId`.
  The bootstrap store is used only by `/api/checkout/customer-session`.
- `RateLimiting:AuthPermitLimit` high enough for the seller VU count. Seller onboarding performs
  registration and login calls per VU; the default 30/min is too low for large runs. Development
  defaults to 1000/min.
- Optional outbox observability: `URBEAT_LOAD_OUTBOX_ADMIN_TOKEN` in the process environment plus
  `-AllowOutboxAdminRead`. Only `GET /api/outbox/summary` is called; the token is never printed.

## Commands

### Plan (default, no network)

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1
```

### Preflight (GET-only)

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 -Mode Preflight
```

### Low-load local run

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 `
  -Mode Run `
  -AllowSyntheticAccountCreation `
  -AllowNeighborhoodImport `
  -BusinessTestAuthorized `
  -Cleanup `
  -Vus 5 `
  -Duration 30s
```

This creates one anchor store in `setup()` and uses the remaining VUs for customers/orders. The
anchor store is isolated by `RUN_ID` and is published/open so checkout can succeed.

### Low-load authorized remote run

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 `
  -Mode Run `
  -BaseUrl https://staging.example.com `
  -AllowRemote `
  -AllowSyntheticAccountCreation `
  -AllowNeighborhoodImport `
  -BusinessTestAuthorized `
  -Cleanup `
  -Vus 5 `
  -Duration 30s
```

### 300 VUs (ramped)

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 `
  -Mode Run `
  -BaseUrl https://staging.example.com `
  -AllowRemote `
  -AllowSyntheticAccountCreation `
  -AllowNeighborhoodImport `
  -BusinessTestAuthorized `
  -Cleanup `
  -Vus 300 `
  -SellerVus 30 `
  -CustomerVus 270 `
  -RampUpDuration 2m `
  -Duration 3m
```

The default split is 10% sellers (minimum 1) for `VUS >= 5`; explicit `-SellerVus`/`-CustomerVus`
must sum to `-Vus`. Maximum total is 300.

### Reuse an existing open store

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 `
  -Mode Run `
  -BaseUrl https://staging.example.com `
  -AllowRemote `
  -AllowSyntheticAccountCreation `
  -AllowNeighborhoodImport `
  -BusinessTestAuthorized `
  -Cleanup `
  -TargetStoreId "<store-guid>" `
  -BootstrapStoreId "<store-guid>" `
  -Vus 10
```

The target store must be open and have at least one available catalog product. Without
`-TargetStoreId`, setup creates one dedicated anchor store. For a customer-only run against an
existing target store, pass `-SellerVus 0 -CustomerVus <Vus>`; then no store-address call is made
and `-AllowNeighborhoodImport` is not required.

### Delivery mode

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 `
  -Mode Run `
  -AllowSyntheticAccountCreation `
  -AllowNeighborhoodImport `
  -BusinessTestAuthorized `
  -Cleanup `
  -FulfillmentType delivery `
  -TargetDeliveryNeighborhood "Centro" `
  -Vus 5
```

For an anchor store, setup knows the generated area name. For an external target store, the
supplied neighborhood must match an active delivery area.

### Pre-provisioned pools

Pool files contain confirmed accounts only:

```json
[
  { "email": "load-seller-01@example.test", "password": "..." },
  { "email": "load-seller-02@example.test", "password": "..." }
]
```

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 `
  -Mode Run `
  -SellerPoolPath .\seller-pool.json `
  -CustomerPoolPath .\customer-pool.json `
  -BusinessTestAuthorized `
  -Cleanup `
  -Vus 10
```

Pool requirements:

- At least `VUS` entries. When the first seller pool account is reserved for the anchor store,
  at least `VUS + 1` entries are required.
- Accounts must be confirmed and, for sellers, must not already own a store.
- In `delivery` mode, pool customers should have an address in the target neighborhood or fewer
  than 3 existing addresses so the script can create a compatible one.
- Do not put real production credentials in a pool file. Production `@urbeat.com.br` accounts are
  rejected.
- On Windows, pool JSON is passed through an environment variable. The wrapper rejects pools larger
  than 30,000 characters; use synthetic account creation for large runs.

### Optional seller status smoke

```powershell
pwsh ./scripts/load/run-urbeat-business-load.ps1 ... -SellerStatusSmoke
```

Creates one pickup order per seller VU and moves it `Received -> Preparing`. This is intentionally
off by default because it doubles seller-side write load.

## Configuration reference

| Wrapper flag | k6 env | Default | Purpose |
| --- | --- | --- | --- |
| `-BaseUrl` | `BASE_URL` | `http://localhost:5000` | API origin; no path/credentials/query. Redirects are disabled, so use the canonical http/https origin. |
| `-Mode` | n/a | `Plan` | `Plan`, `Preflight`, or `Run`. |
| `-Vus` | `VUS` | `5` | Total VUs; max 300. |
| `-SellerVus` | `SELLER_VUS` | 10% (min 1 for VUS >= 5) | Seller onboarding VUs. |
| `-CustomerVus` | `CUSTOMER_VUS` | remainder | Customer order VUs. |
| `-Duration` | `DURATION` | `30s` | Customer hold duration. |
| `-RampUpDuration` | `RAMP_UP_DURATION` | `30s` | Customer ramp-up. |
| `-SellerMaxDuration` | `SELLER_MAX_DURATION` | `10m` | Seller per-VU max duration. |
| `-RequestTimeout` | `REQUEST_TIMEOUT` | `15s` | Per-request timeout. |
| `-SleepSeconds` | `SLEEP_SECONDS` | `1` | Pause between customer iterations. |
| `-FulfillmentType` | `FULFILLMENT_TYPE` | `pickup` | `pickup` or `delivery`. |
| `-TargetStoreId` | `TARGET_STORE_ID` | empty | Existing open store with products. |
| `-BootstrapStoreId` | `BOOTSTRAP_STORE_ID` | auto-discover | Store used only for customer-session bootstrap. |
| `-TargetDeliveryNeighborhood` | `TARGET_DELIVERY_NEIGHBORHOOD` | empty | Required for delivery with an external target. |
| `-TargetDeliveryCity` | `TARGET_DELIVERY_CITY` | `São Paulo` | Synthetic address city. |
| `-RunId` | `RUN_ID` | generated | 3-32 chars of lowercase letters, digits, and single hyphens. |
| `-SellerPoolPath` / `-CustomerPoolPath` | `SELLER_POOL_JSON` / `CUSTOMER_POOL_JSON` | none | Confirmed account pools. |
| `-AllowSyntheticAccountCreation` | `ALLOW_SYNTHETIC_ACCOUNT_CREATION` | false | Confirmed Customer → Seller bootstrap. |
| `-AllowNeighborhoodImport` | `ALLOW_NEIGHBORHOOD_IMPORT` | false | Attests that the backend OSM neighborhood import job is disabled/mocked when stores are created. |
| `-SellerStatusSmoke` | `SELLER_STATUS_SMOKE` | false | Seller order status transition smoke. |
| `-SkipCustomerReadback` | `CUSTOMER_READBACK` | false | Skip `GET /api/orders/{id}` and `/api/orders/my`. |
| `-CustomerSessionPerIteration` | `CUSTOMER_SESSION_PER_ITERATION` | false | Create a new customer per iteration instead of per VU. |
| `-AllowOutboxAdminRead` | `OUTBOX_ADMIN_TOKEN` | false | Read-only outbox summary if `URBEAT_LOAD_OUTBOX_ADMIN_TOKEN` is set. |
| `-Cleanup` | n/a | required for Run | Acknowledges manual cleanup; writes the read-only inventory. |
| `-CleanupPlanPath` | n/a | `artifacts/cleanup-<runid>.sql` | Cleanup inventory output. |
| `-SummaryExportPath` | n/a | `artifacts/summary-<runid>.json` | k6 summary export. |
| `-K6Path` | n/a | `k6` on PATH | Explicit k6 executable path. |

Optional secure process environment variables (never command-line arguments):

- `URBEAT_LOAD_SYNTHETIC_PASSWORD` — generated if absent; must be at least 12 characters.
- `URBEAT_LOAD_ANCHOR_SELLER_PASSWORD` — required when `-AnchorSellerEmail` is supplied.
- `URBEAT_LOAD_OUTBOX_ADMIN_TOKEN` — read-only outbox summary, only with `-AllowOutboxAdminRead`.

## Endpoints covered

| Endpoint | Scenario | Notes |
| --- | --- | --- |
| `GET /health`, `/health/ready` | wrapper preflight | Must be 200. |
| `GET /api/public/stores` | setup | Bootstrap store discovery. |
| `GET /api/stores/cuisine-types` | setup | Anonymous active cuisine lookup. |
| `POST /api/checkout/customer-session` | setup/sellers/customers | Confirmed synthetic Customer + address + token. |
| `POST /api/auth/register/seller` | sellers | Promotion of an already-confirmed identity. |
| `POST /api/auth/login/seller` | sellers | Seller token. |
| `POST /api/auth/login/customer` | customers (pool mode) | Customer token. |
| `POST /api/stores` | sellers | Store creation. |
| `PUT /api/stores/{id}/address` | sellers | Store address; coordinates skip geocoding, but enqueues the OSM neighborhood job. |
| `PUT /api/stores/{id}/business-hours` | sellers | Seven open days. |
| `PATCH /api/stores/{id}/delivery-config` | sellers | Delivery area/fee. |
| `POST /api/stores/{id}/categories` | sellers | One category. |
| `POST /api/stores/{id}/products` | sellers | One single-sale product. |
| `GET /api/stores/{id}/publish/summary` | sellers | Publish readiness. |
| `POST /api/stores/{id}/publish` | sellers | Publish + open. |
| `GET /api/stores/my-store` | sellers | Verify open/published. |
| `GET /api/public/stores/{id}/catalog/products` | setup/customers | Catalog read. |
| `POST /api/checkout/preview` | customers | Server-authoritative total. |
| `POST /api/checkout/confirm` | customers | Cash-on-delivery order. |
| `GET /api/orders/{id}` | customers | Status readback. |
| `GET /api/orders/my` | customers | Order list readback. |
| `PATCH /api/orders/{id}/status` | sellers (optional) | `Received -> Preparing` smoke. |
| `GET /api/orders/store` | sellers (optional) | Seller order list smoke. |
| `GET /api/outbox/summary` | optional | Admin token; read-only. |

## Metrics and thresholds

Custom metrics:

- `urbeat_stores_created`, `urbeat_products_created`, `urbeat_stores_published`
- `urbeat_orders_created`, `urbeat_mock_payments`
- `urbeat_step_failures{stage}`, `urbeat_blocked{reason}`
- `urbeat_stage_duration{stage}`
- `urbeat_onboarding_success`, `urbeat_order_success`, `urbeat_status_smoke_success`

Thresholds include:

- `http_req_failed{scenario:sellers} < 5%`
- `http_req_failed{scenario:customers} < 2%`
- `checks{stage:seller_store} > 95%`
- `checks{stage:checkout_confirm} > 95%`
- `urbeat_onboarding_success > 90%`
- `urbeat_order_success > 90%`
- `checkout_confirm` p95 < 2s, p99 < 4s
- `checkout_preview`/`catalog`/`customer_session` p95 budgets

## Cleanup and rollback

There is **no public API to delete stores or identities**. The scenario never deletes anything and
never invents a deletion endpoint.

The wrapper writes a read-only inventory to `artifacts/cleanup-<RUN_ID>.sql`:

- identities: `load+<RUN_ID>+%@loadtest.invalid`
- stores: slug `load-<RUN_ID>-%`
- products/orders: joined through those stores
- customer addresses: joined through those identities

Review the inventory with the environment owner. If deletion is approved, build a reviewed
transaction where every statement is scoped to the `RUN_ID` patterns. Never delete by a broad name
or date range. Outbox messages, notifications, audit logs, and SignalR deliveries may not carry
`RUN_ID` and must be reviewed separately.

## Direct k6 invocation (advanced)

The wrapper is the supported path. Direct invocation requires all safety variables and is easy to
misuse:

```powershell
$env:BUSINESS_TEST_AUTHORIZED = "true"
$env:ALLOW_SYNTHETIC_MUTATIONS = "true"
$env:RUN_ID = "load-20260101-120000-abcd"
$env:VUS = "5"
$env:ALLOW_SYNTHETIC_ACCOUNT_CREATION = "true"
$env:ALLOW_NEIGHBORHOOD_IMPORT = "true"
$env:SYNTHETIC_PASSWORD = "<strong test-only password>"
k6 run scripts/load/urbeat-business-load.js
```

The script itself rejects production hostnames and refuses to start without the authorization
flags.

## Validation

The wrapper safety tests do not need k6 or a running API:

```powershell
powershell -NoProfile -Command "Import-Module Pester; Invoke-Pester -Script scripts/load/tests/run-urbeat-business-load.Tests.ps1"
```

With k6 installed, `k6 inspect scripts/load/urbeat-business-load.js` validates the script/options
without executing it. The wrapper's default `Plan` mode is also a safe local validation step.

## Limits

- Maximum documented load is 300 VUs. Start with `Plan`, then `Preflight`, then a low-load `Run`.
- Auth endpoints are rate limited. Large seller runs can return `429`; increase the test
  environment's `RateLimiting:AuthPermitLimit` after authorization.
- The anchor/target store must be open. Closed stores make checkout fail by design.
- Delivery mode depends on delivery-area configuration; pickup is the robust default.
- Outbox/worker observability requires an admin token; without it, the scenario reports the
  limitation instead of guessing.
- Order status transitions are optional (`-SellerStatusSmoke`) so the default run focuses on
  concurrent order creation.
- `-CustomerSessionPerIteration` creates one confirmed customer per iteration. It is useful for
  session-isolation tests but grows identities/addresses quickly; keep runs short and clean up by
  `RUN_ID`.
- The load test writes real rows in the target database. Treat database capacity, backups, and
  cleanup as part of the test plan.
