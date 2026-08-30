# Order Status Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sincronizar transicoes de pedidos entre vendedor e storefront em tempo real, adicionar confirmacao de entrega persistida e padronizar novos codigos como `URB-123456`.

**Architecture:** O backend continua sendo a autoridade para status, autorizacao, snapshots e historico. O `OrderService` persistira as transicoes antes de publicar `OrderStatusUpdated` no Customer Hub; o storefront consumira SignalR com polling de fallback. O painel do vendedor usara uma marca persistida de conclusao apenas para ocultar pedidos da tela do dia, sem alterar o status operacional.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core, PostgreSQL, SignalR, Angular 20 standalone, Ionic 8, Jest.

## Global Constraints

- Backend owns validation, prices, checkout totals, payment/order/store state transitions; client values are inputs, not trusted results.
- Every order status transition must pass `OrderStatusStateMachine`.
- Existing order item, price, address and total snapshots remain immutable after order creation.
- Customer confirmation is a separate `DeliveryConfirmedAtUtc` field, not a new `Confirmed` status.
- Seller completion is a separate `SellerCompletedAtUtc` field and only hides the order from the day board.
- New order codes use uppercase `URB` plus a hyphen, for example `URB-123456`; do not generate new `HAP` or `HAPP` codes.
- Customer ownership and seller store ownership must be checked in the backend.
- Do not expose or modify secrets; deploy only the application step after verification.

---

### Task 1: Model Order Confirmation and Seller Completion

**Files:**
- Modify: `backend/src/Urbeat.Domain/Entities/Order.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/OrderDetailsResponseDto.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/OrderSummaryResponseDto.cs`
- Create: EF migration `AddOrderDeliveryConfirmationAndSellerCompletion` under `backend/src/Urbeat.Infrastructure/Persistence/Migrations/`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderServiceTests.cs`

**Interfaces:**
- Produces persisted nullable timestamps `DeliveryConfirmedAtUtc` and `SellerCompletedAtUtc`.
- Produces response fields with the same names for customer tracking and seller board filtering.

- [ ] **Step 1: Write failing persistence/service tests**

Add tests that create an order with delivery fulfillment and assert:

```csharp
result.Order!.DeliveryConfirmedAtUtc.Should().BeNull();
result.Order!.SellerCompletedAtUtc.Should().BeNull();
```

Add tests for setting each field and reloading the order, proving the values survive persistence.

- [ ] **Step 2: Run tests to verify the missing fields fail**

Run: `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderService"`

Expected: compilation/assertion failure because the response/entity fields and operations do not exist yet.

- [ ] **Step 3: Add entity fields and response fields**

Add nullable UTC timestamps to `Order` and expose them in customer and seller details/summary DTOs. Keep existing item, address and price fields unchanged.

- [ ] **Step 4: Add the EF migration**

Run from repository root:

```powershell
dotnet ef migrations add AddOrderDeliveryConfirmationAndSellerCompletion --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

Run the migration model/build tests without applying it manually to production; WebApi startup applies committed migrations.

- [ ] **Step 5: Run the persistence tests**

Run: `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderService"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Urbeat.Domain/Entities/Order.cs backend/src/Urbeat.Application/DTOs backend/src/Urbeat.Infrastructure/Persistence/Migrations backend/tests/Urbeat.UnitTests
git commit -m "feat: persist order delivery confirmation state"
```

### Task 2: Add Backend Commands for Customer Confirmation and Seller Completion

**Files:**
- Modify: `backend/src/Urbeat.Application/Interfaces/IOrderService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderService.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/OrdersController.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/UpdateOrderStatusResultDto.cs` if command responses need the updated order
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderServiceTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/` order controller/service tests

**Interfaces:**
- `Task<UpdateOrderConfirmationResultDto> ConfirmDeliveryAsync(Guid customerUserId, Guid orderId, string? ipAddress, CancellationToken cancellationToken)`.
- `Task<CompleteSellerOrderResultDto> CompleteForSellerBoardAsync(Guid sellerUserId, Guid orderId, string? ipAddress, CancellationToken cancellationToken)`.
- Customer endpoint: `POST /api/orders/{orderId}/delivery-confirmation`.
- Seller endpoint: `POST /api/orders/{orderId}/complete`.

- [ ] **Step 1: Write failing authorization and state tests**

Cover these cases:

```csharp
// Customer owns a Delivered delivery order: succeeds and is idempotent.
// Customer owns a Delivered pickup order: rejected.
// Customer owns a non-Delivered order: rejected.
// Different customer: forbidden/not found without mutation.
// Seller owns store: completion sets SellerCompletedAtUtc without changing Status.
// Different seller: forbidden without mutation.
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderService"`

Expected: FAIL because the commands and controller routes are absent.

- [ ] **Step 3: Implement customer confirmation**

Validate ownership, `OrderStatus.Delivered`, and `FulfillmentType.Delivery`. If `DeliveryConfirmedAtUtc` is already set, return the current order without duplicating audit/history records. Otherwise set UTC now, mark updated, write an audit entry, save, and return the updated details.

- [ ] **Step 4: Implement seller completion**

Validate seller ownership. If `SellerCompletedAtUtc` is null, set UTC now, mark updated, write an audit entry, save, and return the updated order. Do not change `Order.Status` or snapshots. Repeated calls must be idempotent.

- [ ] **Step 5: Add controller endpoints and response mapping**

Use `CustomerOnly` for confirmation and `SellerOnly` for completion. Return `401`, `403`, `404`, or `409` according to the existing controller conventions. Never trust client-supplied timestamps.

- [ ] **Step 6: Run unit and integration tests**

Run:

```powershell
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderService"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~Order"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Urbeat.Application backend/src/Urbeat.Infrastructure/Services/OrderService.cs backend/src/Urbeat.WebApi/Controllers/OrdersController.cs backend/tests
git commit -m "feat: add order delivery confirmation commands"
```

### Task 3: Standardize New Order Codes as URB

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/SystemParameterSeeder.cs`
- Add migration/data update if `Order.CodePrefix` is persisted and existing deployments already contain `HAP-`
- Test: checkout/order code tests under `backend/tests/Urbeat.UnitTests/Infrastructure/Services/`

**Interfaces:**
- New orders produced by `GenerateOrderCodeAsync` return `URB-` followed by the existing random code length.
- Existing order codes are not rewritten; snapshots and historical references remain stable.

- [ ] **Step 1: Write the failing code-prefix test**

Create a checkout test that confirms the persisted order code matches `^URB-[A-Z0-9]{8}$` and never starts with `HAP-`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~Checkout"`

Expected: FAIL with the current `HAP-` prefix.

- [ ] **Step 3: Change the generator and seeded parameter**

Replace the hardcoded generator prefix with `URB-`. Update `SystemParameterSeeder` from `HAP-` to `URB-`; the seeded parameter documents the canonical prefix and existing order codes remain unchanged.

- [ ] **Step 4: Run code-prefix and checkout tests**

Run: `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~Checkout"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs backend/src/Urbeat.Infrastructure/Persistence/SystemParameterSeeder.cs backend/src/Urbeat.Infrastructure/Persistence/Migrations backend/tests
git commit -m "feat: use urb order code prefix"
```

### Task 4: Emit Explicit Customer Order Status Events

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/NotificationService.SignalR.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/NotificationServiceTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/` SignalR/order status tests

**Interfaces:**
- Event name: `OrderStatusUpdated`.
- Payload: `{ orderId, orderCode, status, changedAtUtc }`.

- [ ] **Step 1: Write the failing notification test**

After a successful seller transition, assert the customer hub receives `OrderStatusUpdated` with the order id, code, numeric status and UTC transition time. Assert invalid transitions emit nothing.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~NotificationService|FullyQualifiedName~OrderService"`

Expected: FAIL because the current path persists a notification but does not emit the explicit event consumed by tracking.

- [ ] **Step 3: Emit after persistence**

Inject/use the existing Customer Hub context through the notification service. Persist the status history and order update first, then send `OrderStatusUpdated` to `Clients.User(order.CustomerUserId.ToString())`. Keep the durable notification for inbox/history if already required.

- [ ] **Step 4: Make event delivery best-effort without changing transaction results**

Use the existing safe SignalR send helper. A hub delivery failure must not roll back a committed order transition; the customer polling fallback handles missed events.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~NotificationService|FullyQualifiedName~OrderService"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~Order"
```

```bash
git add backend/src/Urbeat.Infrastructure/Services/OrderService.cs backend/src/Urbeat.Infrastructure/Services/NotificationService.SignalR.cs backend/src/Urbeat.WebApi/Hubs backend/tests
git commit -m "feat: publish customer order status events"
```

### Task 5: Connect Customer Tracking to SignalR and Fallback Polling

**Files:**
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.ts`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.html`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.scss`
- Modify: `frontend/src/app/core/services/order.service.ts`
- Modify: `frontend/src/app/shared/models/order.model.ts`
- Test: `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts`

**Interfaces:**
- `OrderDetails.deliveryConfirmedAtUtc?: string`.
- `OrderDetails` and `OrderSummary` expose `sellerCompletedAtUtc?: string` only where needed by seller/customer views.
- Customer event listener consumes `OrderStatusUpdated` and reloads only the matching order.

- [ ] **Step 1: Write failing tracking tests**

Add one test per behavior: reload the matching order when `OrderStatusUpdated` arrives; ignore a different order id; poll when SignalR is unavailable; show confirmation only for delivered delivery orders; and keep the customer on the tracking route after successful confirmation.

- [ ] **Step 2: Run focused tests to verify failures**

Run: `npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts --runInBand`

Expected: FAIL for the missing event payload handling, confirmation state and fallback polling.

- [ ] **Step 3: Fix event name and listener payload**

Listen to `OrderStatusUpdated`, match `data.orderId` to `currentOrderId`, and call the existing silent load. Remove the listener on destroy and after terminal state only when no confirmation action remains.

- [ ] **Step 4: Add fallback polling with bounded cleanup**

Start a timer after the initial load and after each silent reload. Poll at most every 30 seconds while the order is not terminal/confirmed. Clear the timer and SignalR listener in `ngOnDestroy`.

- [ ] **Step 5: Add confirmation API call and floating action**

Add `confirmDelivery(orderId)` to `OrderService`. Render the floating button only when `status === Delivered`, fulfillment is `Delivery`, and `deliveryConfirmedAtUtc` is empty. On success reload silently and keep the current route.

- [ ] **Step 6: Run tests and commit**

```powershell
npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts --runInBand
```

```bash
git add frontend/src/app/features/order-tracking frontend/src/app/core/services/order.service.ts frontend/src/app/shared/models/order.model.ts frontend/src/app/core/services/signalr.service.ts
git commit -m "feat: sync customer order tracking in real time"
```

### Task 6: Update Seller Board Columns and Completion Action

**Files:**
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.html`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.scss`
- Modify: `frontend/src/app/core/services/order.service.ts`
- Modify: `frontend/src/app/shared/models/order.model.ts`
- Test: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`
- Test: `frontend/src/app/features/seller-orders/components/seller-order-card/seller-order-card.component.spec.ts`

**Interfaces:**
- `OrderService.completeStoreOrder(orderId): Observable<OrderDetails>` calls `POST /api/orders/{orderId}/complete`.
- Seller board filters out orders with `sellerCompletedAtUtc` from the day view.

- [ ] **Step 1: Write failing seller-board tests**

Add one test per behavior: render the new-order code on line one and first name-phone on line two; show an accepted order in preparing after the status response; move a ready order to on-delivery after transition; hide a completed order from the day board without deleting its snapshot; and preserve the `URB` code in cards and notifications.

- [ ] **Step 2: Run focused tests to verify failures**

Run: `npx jest --no-coverage src/app/features/seller-orders --runInBand`

Expected: FAIL for the two-line new-order layout and completion behavior.

- [ ] **Step 3: Update new-order presentation**

Render `#{{ order.code }}` as the first line and derive the first name from `customerName` for the second line: `{{ firstName }} - {{ customerPhoneNumber }}`. Keep full details available in the expanded/card body.

- [ ] **Step 4: Add completion action and filter**

Show `Concluído` for delivered orders, call the completion endpoint, then remove the order from `orders` after success. Do not remove it from backend history; refresh/reload must continue to filter by `sellerCompletedAtUtc`.

- [ ] **Step 5: Preserve transition behavior and realtime reload**

Keep status transitions server-authoritative. After each success, reload the current status groups and retain the existing print-on-accept behavior. Do not synthesize status locally before the API succeeds.

- [ ] **Step 6: Run tests and commit**

```powershell
npx jest --no-coverage src/app/features/seller-orders --runInBand
```

```bash
git add frontend/src/app/features/seller-orders frontend/src/app/core/services/order.service.ts frontend/src/app/shared/models/order.model.ts
git commit -m "feat: update seller order flow and completion board"
```

### Task 7: End-to-End Verification and Release

**Files:**
- Modify: only files directly implicated by a failing verification test; never modify unrelated worktree changes
- Test: `backend/tests/Urbeat.UnitTests/`
- Test: `backend/tests/Urbeat.IntegrationTests/`
- Test: focused frontend specs from Tasks 5 and 6

- [ ] **Step 1: Run focused frontend regression suite**

Run:

```powershell
npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts src/app/features/seller-orders --runInBand
```

- [ ] **Step 2: Run backend regression suite**

Run:

```powershell
dotnet test backend/tests/Urbeat.IntegrationTests
```

- [ ] **Step 3: Build both applications**

Run:

```powershell
Push-Location frontend
npx ng build --configuration production
Pop-Location
```

Expected: builds succeed; Angular bundle budget warnings may remain non-blocking unless a new error is introduced.

- [ ] **Step 4: Verify the exact user journey**

Using a test customer and seller:

1. Create an order and confirm the seller sees a code such as `URB-123456` in `Novos Pedidos` with first name and phone.
2. Accept it and confirm the customer tracking moves to preparation without refresh.
3. Mark ready and confirm the customer progress updates.
4. Mark on delivery and confirm the customer progress updates.
5. Mark delivered and confirm the floating action appears only for delivery.
6. Confirm delivery as customer and verify the action disappears while the route remains unchanged.
7. Click seller `Concluído` and verify the card leaves the day board but remains in history with snapshots.
8. Disconnect SignalR and verify polling eventually reflects the same state.

- [ ] **Step 5: Run diff validation**

Run: `git diff --check`

- [ ] **Step 6: Publish only application**

After explicit deployment approval, run `validate-pipeline.ps1` and only:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\criarDeployOracleCloud\deploy-all.ps1 -Step application -ServerIP 136.248.115.135 -SSHUser dexter -SSHPort 2208 -SSHKeyPath "$HOME\.ssh\id_ed25519"
```

Do not run `environment`, `nginx`, `ssl`, `verify` or secret cleanup for this release.

- [ ] **Step 7: Commit verification fixes only when needed**

If verification reveals a regression, add the specific failing test and its production fix, run that focused test again, and commit those named files with a message describing the correction. If verification is clean, do not create an empty commit.
