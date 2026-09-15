# Mock Pix Payment Design

## Goal

Provide a deterministic, backend-driven Pix simulation for the current test environment so a pending Pix payment has a one-minute payment window and randomly either succeeds between 10 and 50 seconds or expires at 60 seconds.

## Scope

The mock applies only when `Payments:Provider` is explicitly set to `Mock`. It must never call Mercado Pago. The existing real payment strategy remains available for a future environment switch.

The simulation covers payment creation, persisted pending state, countdown metadata, automatic terminal transition, payment history, order state transition, Outbox notifications, and the existing frontend polling/redirect behavior.

## Backend Design

Add a `MockPixPaymentStrategy` implementing the existing `IOrderPaymentStrategy`. It creates or reuses the existing payment row, assigns a mock transaction ID and checkout URL, and stores persisted simulation metadata: expiration time, optional scheduled approval time, and the selected outcome. The outcome is chosen once per payment attempt using an injectable random source; retries create a fresh attempt only when the existing payment is terminal.

Add a hosted `MockPixPaymentWorker` that periodically finds pending mock Pix payments whose scheduled approval or expiration time has arrived. It uses the same payment state transition rules as the real webhook path, writes payment/order histories and durable notifications in one transaction, and emits the existing Outbox order events. It must use optimistic concurrency so two worker iterations cannot apply the same transition twice.

The public payment response exposes the existing fields plus `ExpiresAtUtc` when available. No mock-only secret or fake QR credential is treated as a real payment credential. The mock checkout URL is an internal route or safe placeholder understood by the test UI.

## Frontend Design

Update `OnlinePaymentPageComponent` to show a one-minute countdown based on the server-provided `ExpiresAtUtc`, not a client-created deadline. While pending it displays the mock Pix instructions and status. When the payment becomes paid, the existing navigation to order tracking remains. When it expires, the page stops polling, displays “Tempo para pagamento encerrado”, and offers the existing retry flow.

The UI must handle refresh, delayed responses, browser background throttling, and navigation away without leaking timers or subscriptions. Server status remains authoritative.

## State and Retry Rules

- `Pending` remains active until approval or expiration.
- Approval transitions `Payment.Pending -> Payment.Paid` and advances the order using the existing payment rules.
- Expiration transitions `Payment.Pending -> Payment.Failed` with a payment history entry sourced as `Mock`.
- A failed mock payment can be retried using the existing payment-attempt path without creating another order.
- A paid payment is terminal and is never reset by the mock worker.
- Reprocessing the same payment is idempotent.

## Configuration

Use explicit options with defaults suitable for tests: provider `MercadoPago` unless overridden, mock window 60 seconds, approval lower bound 10 seconds, approval upper bound 50 seconds, and worker polling interval configurable. The current test environment enables `Mock`; real production configuration must remain opt-in to change.

## Testing

- Unit tests cover random outcome selection, timing boundaries, strategy reuse, response metadata, and transition idempotency using fake clock/random sources.
- Integration tests cover mock payment creation, approval, expiration, histories, order status, Outbox records, and retry without duplicate order creation.
- Frontend tests cover countdown rendering, paid redirect, expiration state, retry, refresh, and cleanup.
- Existing real Mercado Pago strategy and webhook tests must remain unchanged and passing.

## Non-goals

- No Mercado Pago API calls or webhook changes for the mock.
- No RabbitMQ.
- No real QR-code generation or banking integration.
- No changes to payment behavior when the provider is not `Mock`.

## Implementation Notes

- `MockPixOptions` binds to the `Payments` configuration section (`Provider`, `WindowSeconds`, `ApprovalMinimumSeconds`, `ApprovalMaximumSeconds`, `WorkerPollingIntervalSeconds`, `WorkerEnabled`). `Provider` defaults to `MercadoPago`, so existing behavior is unchanged unless explicitly set to `Mock`. `appsettings.json` documents the defaults; `appsettings.Production.json` pins `Provider=MercadoPago`; the integration test factory opts into `Mock` per test via `WithWebHostBuilder`.
- `MockPixPaymentStrategy` implements `IOrderPaymentStrategy` and only handles `PixOnline` when `Provider=Mock`; it is registered ahead of the real strategy, so the factory resolves it first without changing `MercadoPagoOrderPaymentStrategy`.
- Persisted metadata lives on `Payment` (`MockExpiresAtUtc`, `MockApprovalAtUtc`, `MockOutcome`) and is exposed on the DTO as nullable `ExpiresAtUtc`.
- `MockPixPaymentProcessor` (scoped) performs the terminal transition in one unit of work and is driven by the hosted `MockPixPaymentWorker`, which is registered only when the mock is enabled. Optimistic concurrency is enforced by the existing `ConcurrencyStamp` token; a losing iteration detaches and records a conflict audit without duplicating history.
- The frontend countdown is derived from the server-provided `expiresAtUtc` (via `Date.now()`), never from a client-created deadline, and clamps to zero. Expiry stops order polling and shows "Tempo para pagamento encerrado"; the retry button reuses the existing payment-attempt path without creating another order.

## Limitations

- The mock handles only `PixOnline`; `CardOnline` continues to route to the real Mercado Pago strategy even when `Provider=Mock` (there is currently no card flow in the UI). Extending the mock to card would require widening `CanHandle` and the worker/processor transition logic.
- `MockPixOptions.Validate()` is unit-tested but not enforced at startup (`ValidateOnStart`), so a misconfigured environment would fall back to defaults rather than aborting.
- Integration tests drive `MockPixPaymentProcessor` directly with a fake clock/random; the hosted worker's polling loop is exercised indirectly by the same processor code and is disabled in the test factory (`Payments:WorkerEnabled=false`).
