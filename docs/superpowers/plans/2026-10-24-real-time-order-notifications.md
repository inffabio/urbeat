# Real-Time Order Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver new-order notifications to the seller and order-status updates to the customer immediately through SignalR.

**Architecture:** Keep the durable `OrderCreated`/`OrderStatusChanged` Outbox flow as the source of truth. Replace the Infrastructure-to-WebApi dynamic hub invocation with a typed application-level live notification publisher implemented in WebApi, so seller and customer hub contexts are resolved by the composition root and failures remain observable/retryable. Preserve the existing Angular listeners and their authoritative refresh/reconciliation behavior.

**Tech Stack:** .NET 9, ASP.NET Core SignalR, PostgreSQL/EF Core Outbox, Angular 20 standalone, Signals, Jest.

## Global Constraints

- Preserve the architecture `Domain -> Application -> Infrastructure -> WebApi`.
- Keep the Outbox as the durable source of truth and retain at-least-once delivery semantics.
- Do not change order status transitions, checkout totals, HTTP contracts, or persistence schemas.
- Do not log access tokens, passwords, full notification payloads, or customer personal data.
- Seller event: `ReceiveSellerNotification`; customer event: `OrderStatusUpdated` and existing customer notification events.
- Frontend remains authoritative through order list/detail refresh after live events and must tolerate duplicate events.

---

### Task 1: Introduce a typed live notification publisher

**Files:**
- Create: `backend/src/Urbeat.Application/Interfaces/ILiveNotificationPublisher.cs`
- Create: `backend/src/Urbeat.WebApi/Services/SignalRLiveNotificationPublisher.cs`
- Modify: `backend/src/Urbeat.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/NotificationService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/NotificationService.SignalR.cs`
- Test: `backend/tests/Urbeat.UnitTests/` existing notification service test location, or create a focused test beside the existing notification tests

**Interfaces:**
- Consumes: seller/customer recipient IDs and event names/payloads from `NotificationService`.
- Produces: `ILiveNotificationPublisher.PublishSellerAsync(...)` and `PublishCustomerAsync(...)`, returning `Task<bool>` and reporting failed hub sends without leaking payload data.

- [ ] **Step 1: Add a failing unit test for successful and failed live publication**

  Define a test double for `ILiveNotificationPublisher` and verify that `NotificationService` delegates seller and customer events to it with the correct recipient ID and event name. Add a failure case asserting `false` is returned to the Outbox handler path rather than being swallowed as success.

- [ ] **Step 2: Define the Application abstraction**

  Add an interface with explicit methods equivalent to:

  ```csharp
  public interface ILiveNotificationPublisher
  {
      Task<bool> PublishSellerAsync(Guid userId, string method, object payload, CancellationToken cancellationToken = default);
      Task<bool> PublishCustomerAsync(Guid userId, string method, object payload, CancellationToken cancellationToken = default);
  }
  ```

  Keep the interface free of ASP.NET/SignalR types so Infrastructure does not depend on WebApi.

- [ ] **Step 3: Implement the typed WebApi publisher**

  Inject `IHubContext<SellerNotificationHub>` and `IHubContext<CustomerNotificationHub>` into `SignalRLiveNotificationPublisher`. Send through `Clients.User(userId.ToString()).SendCoreAsync(method, new[] { payload }, cancellationToken)`. Catch non-cancellation exceptions, log only method, recipient identifier, exception type/message sanitized as appropriate, and return `false`; rethrow cancellation requested by the caller.

- [ ] **Step 4: Register the publisher from WebApi and remove dynamic hub resolution**

  Register the WebApi implementation in `AddWebApi`. Change the Infrastructure registration of `NotificationService` to resolve `ILiveNotificationPublisher` from DI and pass it to the service. Remove `Type.GetType`, `dynamic` hub fields, and reflection-based `IHubContext` resolution. Do not alter Redis backplane registration.

- [ ] **Step 5: Route existing seller/customer event methods through the abstraction**

  Preserve the existing event names and payload shapes in `NotificationService.SignalR.cs`:

  ```csharp
  ReceiveSellerNotification
  ReceiveCustomerNotification
  OrderStatusUpdated
  OrderAcceptedForPrinting
  ```

  Replace direct dynamic hub calls with the typed publisher. Keep `OrderAcceptedForPrinting` and customer status events working exactly as before.

- [ ] **Step 6: Run the focused backend tests**

  Run from repository root:

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~Notification"
  ```

  Expected: PASS, including success and failure propagation tests.

### Task 2: Verify Outbox delivery for seller and customer flows

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/NotificationHandler.cs` only if required by the typed publisher result contract
- Modify: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/OrderSignalRHandler.cs` only if required by the typed publisher result contract
- Test: existing backend Outbox/notification handler tests

**Interfaces:**
- Consumes: `ILiveNotificationPublisher` through `INotificationService`.
- Produces: retry on failed `OrderCreated` live delivery and successful customer/seller pushes when the hub is available.

- [ ] **Step 1: Add/extend handler tests for `OrderCreated`**

  Arrange a notification row of type `NewOrder`, a seller user ID, and a fake live publisher. Process an `OrderCreated` event with `OrderStatus.Received`; assert the seller publisher receives `ReceiveSellerNotification` and the seller ID. Assert a publisher failure produces `OutboxProcessingResult` retry/failure according to the current handler contract.

- [ ] **Step 2: Add/extend handler tests for customer status updates**

  Process an `OrderStatusChanged` event with a customer notification row and assert `OrderStatusUpdated` reaches the customer ID with order ID, code, status, and UTC change time. Verify duplicate processing does not create duplicate notification rows or mutate status incorrectly.

- [ ] **Step 3: Run backend unit and integration tests**

  Run:

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests
  dotnet test backend/tests/Urbeat.IntegrationTests
  ```

  Expected: PASS; no migration or database schema change should be produced.

### Task 3: Protect the existing Angular real-time consumers

**Files:**
- Modify: `frontend/src/app/features/seller-shell/seller-shell.facade.spec.ts` only if a missing regression is found
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts` only if a missing regression is found
- Modify: `frontend/src/app/core/services/customer-order-tracking.service.spec.ts` only if a missing regression is found
- Modify: `frontend/src/app/core/services/signalr.service.spec.ts` only if a missing regression is found

**Interfaces:**
- Consumes: `ReceiveSellerNotification` and `OrderStatusUpdated` events from the existing `SignalRService` listener API.
- Produces: seller order list refresh through `SellerShellFacade.newOrderPulse()` and immediate customer state update through `CustomerOrderTrackingService.trackedOrders()`.

- [ ] **Step 1: Confirm seller event behavior with the existing tests**

  Ensure a `NotificationType.NewOrder` event updates `newOrderPulse`, and ensure `SellerOrdersPageComponent` calls `load({ silent: true })` once for a new pulse. Preserve duplicate pulse protection by notification ID.

- [ ] **Step 2: Confirm customer event behavior with the existing tests**

  Ensure `OrderStatusUpdated` updates the matching tracked order before the background detail refresh, and ensure the tracking page derives the visible timeline from the updated signal. Preserve store filtering and stale-event/version protections.

- [ ] **Step 3: Run focused frontend tests**

  Run from `frontend/`:

  ```powershell
  npx jest --no-coverage src/app/core/services/signalr.service.spec.ts src/app/core/services/customer-order-tracking.service.spec.ts src/app/features/seller-shell/seller-shell.facade.spec.ts src/app/features/seller-orders/seller-orders-page.component.spec.ts src/app/features/order-tracking/tracking-page.component.spec.ts
  ```

  Expected: PASS with existing reconnection, duplicate, and status-immediate assertions intact.

### Task 4: Full validation and release review

**Files:**
- No source files expected beyond Tasks 1-3.

- [ ] **Step 1: Build the backend solution**

  Run:

  ```powershell
  dotnet build backend/Urbeat.sln
  ```

  Expected: successful build with no new errors.

- [ ] **Step 2: Build the frontend for production**

  Run from `frontend/`:

  ```powershell
  npx ng build --configuration production
  ```

  Expected: successful production build; existing bundle-budget warnings may remain and must be reported, not silently changed.

- [ ] **Step 3: Review the diff and production observability**

  Confirm no schema, API, checkout, credentials, or unrelated UI changes. Confirm log statements contain no tokens, passwords, or full order payloads. Verify the deployment health check remains HTTP 200 and monitor for disappearance of `SignalR live push not delivered` and `NotificationHandler: SignalR notification push failed` after a controlled test order.

- [ ] **Step 4: Commit the implementation**

  ```powershell
  git add backend frontend
  git commit -m "fix: entrega notificacoes de pedidos em tempo real"
  ```

## Self-review

- Seller new-order flow is covered by Tasks 1-3.
- Customer status flow is covered by Tasks 1-3.
- Outbox retry and at-least-once behavior remain covered by Task 2.
- Reconnection and duplicate tolerance remain covered by Task 3.
- No database migration or HTTP contract change is planned.
- No credentials or sensitive payloads are logged.
