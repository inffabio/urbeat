# Pickup Store Address Snapshot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist an immutable store-address snapshot for confirmed pickup orders and expose it only to authorized customer order details, where the Angular tracking page renders the accessible `Retirada na loja` card.

**Architecture:** Add nullable flat snapshot properties to the domain `Order`, configure the EF model, and append one reversible Infrastructure migration without backfill. During authoritative checkout confirmation, read the store address once with the store data, validate the required fields, copy it before `SaveChanges`, and reject invalid pickup confirmation before adding any order entities. Extend `OrderDetailsResponseDto` with a nullable nested DTO, but make `OrderService` populate it only for customer-facing detail paths; seller/driver paths continue using the same shared DTO with the field null. The tracking component derives a complete pickup-address signal from the current order signal and renders a separate section immediately after the timeline.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core/PostgreSQL, xUnit, Angular 20 standalone components, Ionic 8, TypeScript strict mode, Signals, modern control flow, Jest, SCSS tokens.

## Global Constraints

- Backend validation is authoritative; never trust a client-provided store address.
- Add exactly eight nullable `Order` columns: `PickupStoreAddressZipCode varchar(8)`, `PickupStoreAddressStreet varchar(120)`, `PickupStoreAddressNumber varchar(20)`, `PickupStoreAddressNeighborhood varchar(80)`, `PickupStoreAddressCity varchar(80)`, `PickupStoreAddressState varchar(2)`, `PickupStoreAddressComplement varchar(120)`, and `PickupStoreAddressReference varchar(200)`.
- Do not backfill existing orders; legacy null snapshots must remain readable and return `null`.
- Pickup confirmation without a minimum valid store address must fail before a partial order is persisted; delivery orders never populate the snapshot.
- No new public endpoint; preserve customer authorization and exact customer/order filtering.
- Populate the snapshot only in customer-facing `GET /api/orders/{orderId}` and existing customer-facing detail responses; do not expose it in seller, driver, route, print, outbox, logs, or SignalR payloads.
- Use UTC timestamps and existing validation/transaction behavior.
- Keep Angular Signals, standalone components, `@if`/`@for`, existing tokens, mobile/PWA/Capacitor behavior, and 44px interactive targets; add no map or button.
- The UI renders only for `PickUp` with a complete snapshot, below the timeline, with the exact title `Retirada na loja` and accessible label `Endereço da loja`.
- Do not change cart, checkout UI flow, CustomerOtpService, replica infrastructure, deployment, payments, or integrations.

---

## File Map

- Modify `backend/src/Urbeat.Domain/Entities/Order.cs`: eight nullable persisted snapshot properties.
- Modify `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`: max-length configuration matching the existing address fields.
- Create `backend/src/Urbeat.Infrastructure/Persistence/Migrations/20261110231905_AddPickupStoreAddressSnapshot.cs` and `20261110231905_AddPickupStoreAddressSnapshot.Designer.cs`; update `ApplicationDbContextModelSnapshot.cs`.
- Create `backend/src/Urbeat.Application/DTOs/PickupStoreAddressResponseDto.cs`; modify `OrderDetailsResponseDto.cs`.
- Modify `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs`: load/validate/copy the store address for confirmed pickup orders.
- Modify `backend/src/Urbeat.Infrastructure/Services/OrderService.cs`: explicit customer-only projection flag and snapshot-completeness mapping.
- Modify/add backend tests in existing checkout/order integration/unit test files and migration/model validation coverage following repository conventions.
- Modify `frontend/src/app/shared/models/order.model.ts`: optional `PickupStoreAddress` interface and `pickupStoreAddress` property.
- Modify `frontend/src/app/features/order-tracking/tracking-page.component.ts`, `.html`, and `.scss`: derived complete snapshot signal and separate accessible responsive card.
- Modify `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts`: rendering, placement, stale-state, and accessibility assertions.

---

### Task 1: Add red tests for backend snapshot behavior

**Files:**
- Test: `backend/tests/Urbeat.IntegrationTests/Api/CheckoutFlowTests.cs` (or the existing pickup checkout fixture identified during implementation)
- Test: `backend/tests/Urbeat.IntegrationTests/Api/OrdersFlowTests.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/CheckoutServiceTests.cs` if the existing unit fixture can exercise the service

**Interfaces:**
- Consumes: existing `TestWebApplicationFactory`, authenticated customer/seller clients, `UpdateStoreAddressRequestDto`, `CheckoutRequestDto`, `OrderDetailsResponseDto`.
- Produces: executable assertions for snapshot copy, immutability, delivery exclusion, invalid-address rejection, customer authorization, delivered-order detail, and legacy null behavior.

- [ ] **Step 1: Add failing integration tests for checkout persistence.**

Create tests that set a complete store address, confirm `FulfillmentType.PickUp`, read the order as the owning customer, and assert all eight values. Add a second test that edits the store address after confirmation and asserts the order response still contains the original values. Add a delivery test asserting `PickupStoreAddress` is null. Add a missing/blank required-field test asserting the existing checkout error contract and that the customer has no newly created order.

- [ ] **Step 2: Add failing customer-facing and authorization tests.**

Assert the owner receives the nested snapshot after transitioning/completing the order to `Delivered`; assert a different customer receives the existing `404`/unauthorized result and no snapshot; assert seller/driver detail responses serialize no populated pickup snapshot. Add a legacy order fixture with all new properties null and assert customer detail succeeds with `PickupStoreAddress == null`.

- [ ] **Step 3: Run the focused backend tests and verify RED.**

Run:

```powershell
dotnet test backend/tests/Urbeat.IntegrationTests --no-restore --filter "FullyQualifiedName~CheckoutFlowTests|FullyQualifiedName~OrdersFlowTests"
```

Expected: the new assertions fail because the entity, migration, checkout copy, DTO, and customer-only projection do not exist yet.

---

### Task 2: Implement the domain model, DTO contract, and migration

**Files:**
- Modify: `backend/src/Urbeat.Domain/Entities/Order.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/src/Urbeat.Application/DTOs/PickupStoreAddressResponseDto.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/OrderDetailsResponseDto.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/20261110231905_AddPickupStoreAddressSnapshot.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/20261110231905_AddPickupStoreAddressSnapshot.Designer.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: existing relational model/migration validation tests, if present

**Interfaces:**
- Consumes: existing `Order` address naming and EF migration chain.
- Produces: nullable `Order.PickupStoreAddress*` properties; `PickupStoreAddressResponseDto` with `ZipCode`, `Street`, `Number`, `Neighborhood`, `City`, `State`, `Complement`, `Reference`; nullable `OrderDetailsResponseDto.PickupStoreAddress`; a migration whose `Down` drops only these eight columns.

- [ ] **Step 1: Add the nullable properties and EF constraints.**

Place the eight properties near the existing delivery address snapshot in `Order.cs`. Add `HasMaxLength(8/120/20/80/80/2/120/200)` in the `Order` model configuration without `IsRequired`, foreign keys, indexes, or latitude/longitude properties.

- [ ] **Step 2: Add the response DTO and optional contract field.**

Use init-only nullable string properties and add `public PickupStoreAddressResponseDto? PickupStoreAddress { get; init; }` to `OrderDetailsResponseDto`. Keep the existing JSON naming policy; do not add the field to summary DTOs or create an endpoint.

- [ ] **Step 3: Generate the incremental migration from `backend/`.**

Run:

```powershell
dotnet ef migrations add AddPickupStoreAddressSnapshot --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure --context ApplicationDbContext
```

Inspect and retain only eight nullable `Orders` columns with the specified PostgreSQL types/lengths. Verify `Down` drops only those columns, no data update/backfill is present, and the generated model snapshot contains all eight properties.

- [ ] **Step 4: Validate model/migration metadata without changing a deployment database.**

Run `dotnet build backend/Urbeat.sln --no-restore` and the repository’s relational migration/model test (or a clean startup/Testcontainers path when available). Expected: model validation passes and the migration chain is coherent.

---

### Task 3: Implement authoritative pickup snapshot creation and customer-only mapping

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderService.cs`
- Test: files from Task 1

**Interfaces:**
- Consumes: `StoreAddress`, `FulfillmentType`, `Order`, `PickupStoreAddressResponseDto`, and existing `LoadDetailsAsync` callers.
- Produces: confirmation-time snapshot copy; null for delivery/legacy/partial snapshots; customer-only mapping enabled for `GetCustomerOrderAsync` and `ConfirmDeliveryAsync`, disabled for seller/driver/order-status responses.

- [ ] **Step 1: Load one store address read for pickup confirmation and define minimum validity.**

In `BuildCheckoutResultAsync`, after the store is loaded, query the store’s `StoreAddress` once only when `request.FulfillmentType == FulfillmentType.PickUp` and `persistOrder` is true. Require non-empty trimmed `ZipCode`, `Street`, `Number`, `Neighborhood`, `City`, and `State`; preserve optional `Complement`/`Reference`. On failure return the established checkout failure result used for authoritative address validation (or add a narrowly scoped result flag/message if the current contract has no suitable flag) before `Orders.AddAsync`, item creation, outbox, notifications, or audit persistence.

- [ ] **Step 2: Copy the one address read into the new Order properties before SaveChanges.**

Trim only as consistent with existing address validation/copy behavior, assign all eight snapshot fields inside the `Order` initializer or immediately before adding the order, and do not assign them in the delivery branch. Preview remains non-persisting and must not reject merely because pickup address data is absent unless existing preview semantics require it.

- [ ] **Step 3: Add explicit customer-only projection behavior.**

Change `LoadDetailsAsync` to receive an explicit `includePickupStoreAddress` argument defaulting to `false`. Pass `true` from `GetCustomerOrderAsync` and customer confirmation responses; leave seller status/complete, driver, route, and other non-customer callers at `false`. Build the nested DTO only when the flag is true, fulfillment is `PickUp`, and all six required snapshot fields are non-empty; include optional fields as nullable. Never query `StoreAddress` from `LoadDetailsAsync`.

- [ ] **Step 4: Keep response contracts safe for other audiences.**

Audit every `LoadDetailsAsync` call in `OrderService.cs`, especially seller status updates, driver assignment, seller completion, and customer delivery confirmation. Ensure only customer-owned paths can set `PickupStoreAddress`, while the shared DTO remains backward compatible with a null property.

- [ ] **Step 5: Run the focused backend tests and verify GREEN.**

Run:

```powershell
dotnet test backend/tests/Urbeat.IntegrationTests --no-restore --filter "FullyQualifiedName~CheckoutFlowTests|FullyQualifiedName~OrdersFlowTests"
dotnet test backend/tests/Urbeat.UnitTests --no-restore
```

Expected: pickup snapshot, immutability, delivery exclusion, invalid-address atomic rejection, customer authorization, delivered status, and null legacy tests pass.

---

### Task 4: Add frontend model and tracking tests before UI implementation

**Files:**
- Modify: `frontend/src/app/shared/models/order.model.ts`
- Test: `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts`

**Interfaces:**
- Consumes: backend JSON `pickupStoreAddress` with camelCase fields and existing `OrderDetails` signals.
- Produces: optional `PickupStoreAddress` TypeScript type/property and red Jest assertions for complete/partial/null/delivery/loading/error/order-switch states.

- [ ] **Step 1: Add the optional TypeScript model.**

Define `PickupStoreAddress` with `zipCode`, `street`, `number`, `neighborhood`, `city`, `state`, optional `complement` and `reference`; add `pickupStoreAddress?: PickupStoreAddress | null` to `OrderDetails` so old fixtures and responses remain valid.

- [ ] **Step 2: Add failing DOM tests.**

Use the existing tracking test fixture and service signals to assert a complete pickup order renders exactly one separate section with title `Retirada na loja`, accessible text `Endereço da loja`, all required lines, and optional complement/reference. Assert the section occurs after `.tracking-card` and before `.items-card`. Assert no section for delivery, null, missing required fields, loading, unavailable, and switching from a pickup order to a different order without a snapshot. Assert the icon is `aria-hidden` and the address text does not overflow by checking the dedicated class/style contract.

- [ ] **Step 3: Run focused frontend tests and verify RED.**

Run:

```powershell
cd frontend
npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts
```

Expected: model/template assertions fail before the computed signal and markup exist.

---

### Task 5: Render the complete pickup address below the timeline

**Files:**
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.ts`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.html`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.scss`
- Test: `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts`

**Interfaces:**
- Consumes: optional `OrderDetails.pickupStoreAddress`, `FulfillmentType.PickUp`, current `order()` signal, and existing design tokens.
- Produces: a `pickupStoreAddress`/`hasPickupStoreAddress` computed signal that is null/false unless the current order is pickup and all required fields are present; a static accessible responsive section immediately below the timeline.

- [ ] **Step 1: Add a guarded computed signal.**

Derive from `this.order()` on every read, require `fulfillmentType === FulfillmentType.PickUp`, and validate `zipCode`, `street`, `number`, `neighborhood`, `city`, and `state` with `trim()`. Return null for missing, partial, delivery, loading, unavailable, or stale order data; do not cache a previous address independently of the current order id.

- [ ] **Step 2: Add the modern control-flow markup after `.tracking-card`.**

Inside the existing `@if (order(); as o)` block, immediately after the tracking card and before `.items-card`, add an `@if (pickupStoreAddress(); as address)` section with a semantic heading exactly `Retirada na loja`, an accessible address label exactly `Endereço da loja`, street/number, neighborhood, city/state, CEP, and conditional complement/reference. Use a decorative `ion-icon aria-hidden="true"`; do not add a button, map, or reuse the delivery-address card.

- [ ] **Step 3: Add scoped responsive styles using existing tokens.**

Style the new section as its own `.card`/`.pickup-store-address-card`, preserve existing spacing and bordeaux tokens, ensure `overflow-wrap:anywhere`/safe line wrapping on narrow screens, and retain iOS/PWA safe padding. No interactive element is introduced, so existing 44px controls remain unchanged.

- [ ] **Step 4: Run focused Jest tests and fix only feature regressions.**

Run:

```powershell
cd frontend
npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts
```

Expected: all tracking tests, including placement, accessibility, no-stale-data, delivery/null/partial suppression, pass.

---

### Task 6: Full validation and review checks

**Files:**
- Modify: only tests/fixtures required by compilation or existing contract updates.

- [ ] **Step 1: Run required backend validation.**

```powershell
dotnet build backend/Urbeat.sln --no-restore
dotnet test backend/tests/Urbeat.UnitTests --no-restore
dotnet test backend/tests/Urbeat.IntegrationTests --no-restore
```

Expected: build and unit tests pass; integration tests pass, with only repository-defined self-skips when PostgreSQL/Docker is unavailable.

- [ ] **Step 2: Run required frontend validation.**

```powershell
cd frontend
npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts
npx ng build --configuration production
```

Expected: focused tests pass and production build succeeds without weakening strict TypeScript/template checks.

- [ ] **Step 3: Validate migration/model and diff hygiene.**

Inspect the migration and model snapshot for eight nullable columns and no backfill, then run:

```powershell
git diff --check
```

Expected: no whitespace errors and no changes to OCI/deployment/database data.

- [ ] **Step 4: Review exposure boundaries.**

Search all `OrderDetailsResponseDto` construction and `LoadDetailsAsync` call sites. Confirm no seller/driver/route/print/outbox/SignalR payload includes the snapshot and that the customer route still filters by `CustomerUserId`. Leave all changes uncommitted in the working tree for architect review.
