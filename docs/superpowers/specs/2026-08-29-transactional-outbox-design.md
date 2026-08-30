# Transactional Outbox Design

## Goal

Provide durable, retryable delivery of Urbeat business events to SignalR, notifications, e-mail, SMS/WhatsApp, and printing without losing side effects after a database commit.

## Scope

The first implementation covers order events and the complete outbound integration infrastructure. PostgreSQL remains the source of truth. RabbitMQ is not introduced in this phase, but the publisher boundary must allow it to be added later.

## Architecture

Application services persist business state and an outbox message in the same EF Core transaction. A hosted worker claims pending messages from PostgreSQL and dispatches them to typed handlers. Handlers perform external delivery with at-least-once semantics and idempotency keys. Failed messages use bounded exponential retry and then enter a terminal failed state for operational review.

The outbox is an internal durable transport, not a replacement for order state. SignalR remains a live-delivery optimization; clients continue to recover through persisted notifications and polling.

## Message Model

Each message contains:

- `Id` as the immutable message identifier.
- `Type` as a stable event name, such as `OrderStatusChanged`.
- `AggregateId` and optional `AggregateType` for tracing.
- JSON `Payload` containing the event contract, not an EF entity.
- `OccurredAtUtc`, `AvailableAtUtc`, `ProcessedAtUtc`.
- `AttemptCount`, `LockedUntilUtc`, `LastError`.
- `Status` with `Pending`, `Processing`, `Processed`, and `Failed`.

The database enforces indexes for pending acquisition and a uniqueness rule for consumer delivery keys where needed. Payloads never contain secrets, access tokens, or raw payment credentials.

## Event Flows

### Orders

Order creation, payment-driven order confirmation, and order status changes write the order, histories, durable notification rows, audit records, and corresponding outbox messages in one transaction. The event payload includes the order ID, store ID, customer ID, order code, previous status, new status, and UTC transition time.

### SignalR and notifications

The dispatcher sends customer and seller live events after commit. A failed live send does not roll back the order or notification. Persisted notifications remain available for the existing list/polling fallback.

### E-mail and SMS/WhatsApp

Message requests are represented by outbox events with a deterministic delivery key. Providers are called by handlers, with provider/message identifiers stored for deduplication and diagnostics. Retryable provider errors remain pending; invalid recipient or permanent provider errors become failed.

### Printing

Print jobs are emitted as durable events containing the order snapshot required by the print agent. The handler must use a deterministic job key so retries cannot create duplicate physical prints without an explicit retry policy.

## Dispatch and Concurrency

The worker claims a bounded batch using row locks that do not block other workers. Claims expire through `LockedUntilUtc`, allowing recovery after process termination. Handlers are invoked outside the database transaction. A message is marked processed only after successful handling. Multiple workers may process different messages concurrently, but order events are acquired in sequence per aggregate: the claim refuses a later `Sequence` while an earlier sequence for the same aggregate is still pending or processing.

## Failure Handling

Transient failures use exponential delays with jitter and a maximum attempt count. Terminal failures retain the payload and last error, emit structured logs and metrics, and are exposed to an operational query/endpoint. Processing is at-least-once; every handler must be idempotent. Cancellation during shutdown releases work through claim expiry rather than marking messages processed.

## Compatibility and Rollout

Existing direct SignalR/e-mail/SMS/print calls are replaced incrementally by outbox writes. The first migration creates the table and indexes without changing existing order data. The worker is disabled or drains safely during rollout until the migration is present. No existing secrets or payment gateway contracts are changed.

## Testing

- Unit tests cover event serialization, retry classification, idempotency keys, backoff, and handler routing.
- Integration tests verify that business state and outbox rows commit together and roll back together.
- Integration tests verify claim expiry, concurrent claims, retries, and terminal failure.
- Order tests verify no direct external delivery occurs before commit.
- Existing SignalR, payment webhook, e-mail, SMS, and print tests remain green.

## Observability

Every message logs its ID, type, aggregate ID, attempt, result, and latency without payload secrets. Metrics cover pending count, processing latency, retry count, failed count, and handler-specific failures. Health checks report real worker liveness (a thread-safe heartbeat the worker refreshes every poll iteration, flagged unhealthy when stale beyond `WorkerStaleAfter`) and an alertable backlog threshold.

## Implementation Status (2026-08-29)

- Persistence, writer, order event integration, dispatcher (claims, retry, idempotency), SignalR/notification/e-mail handlers, health check and operations endpoint are implemented.
- **At-least-once (honest) delivery.** There is no exactly-once guarantee. Each handler documents its real semantics:
  - **E-mail/SMS/print:** an `OutboxDelivery` row is persisted after a successful send, keyed by a deterministic `DeliveryKey` (unique index) with the provider/message identifier in `ProviderMessageId` and the attempt count. The delivery key is forwarded to the provider as the idempotency key when the adapter supports server-side dedup (e.g. Infobip); SMTP has no such guarantee, so a crash between the send and the record can still duplicate a message. The unique index is a best-effort guard, not an exactly-once boundary.
  - **SignalR:** live pushes are at-least-once with no delivery marker. A crash after the push but before the message is marked processed (or a claim re-acquisition after lock expiry) can deliver the same live event more than once. Clients must tolerate duplicates; the durable notification rows and the list/polling endpoints remain the authoritative source of truth. The delivery marker is deliberately omitted so it cannot be falsely marked delivered before (or instead of) the send.
  - **Printing:** `PrintHandler` reports an explicit terminal failure (`OutboxProcessingResult.Fail`) rather than a no-op success. The print agent is a local loopback agent reached from the seller browser/Capacitor app; there is no backend-to-agent delivery channel, so the job is retained in the failed queue for operational review instead of being silently dropped.
- **Per-order ordering.** Order events carry a monotonic `Sequence` (creation is 1; each transition increments `Order.StatusVersion`). The dispatcher claim refuses to acquire a later sequence while an earlier sequence for the same aggregate is still pending or processing (relational `NOT EXISTS` guard and an equivalent in-memory guard). A terminal earlier event (processed or failed) does not block later events.
- **Webhook concurrency.** `Payment.ConcurrencyStamp` is an optimistic concurrency token: concurrent webhooks that target the same payment with different statuses race on it, and the losing write raises `DbUpdateConcurrencyException`. The loser records only its idempotency event key and is reported as ignored, preserving the winning request's state machine and history.
- **Order mutation concurrency.** `Order.StatusVersion` is an EF Core optimistic concurrency token, so two concurrent mutations of the same order cannot both advance the sequence and emit the same `Sequence`. The losing write raises `DbUpdateConcurrencyException`, which the order service converts into a `ConcurrentUpdate` result (HTTP 409) after discarding its tracked entities, so the winning transition, its history, and the persisted sequence stay authoritative. The outbox `Sequence` is always the persisted, post-increment `Order.StatusVersion`.
- **SMS/WhatsApp OTP:** OTP delivery stays synchronous in `CustomerOtpService` (never in the outbox) because the code is an ephemeral secret (1-minute lifetime). The durable "send intent" is the `CustomerPhoneVerification` row, which stores only `CodeHash` and lifetime metadata — no OTP code is written to any durable outbox payload. There is no generic non-OTP SMS/WhatsApp outbox handler until a real non-secret message flow exists.
- **E-mail confirmation.** The confirmation e-mail is emitted as an `OutboundMessageRequested` event whose payload carries only `Channel`, a unique-per-request `DeliveryKey`, the `Template`, and a `CorrelationId`/`UserId` reference. The short code and raw Identity token never leave the Redis token cache and are never persisted in the outbox payload; `EmailHandler` resolves the token, renders the content, and sends at dispatch time. The delivery key is unique per request (`email-confirm:{correlationId}`), so a resend produces a fresh key instead of being deduplicated against a previous confirmation.
- **No silent success.** A message whose type has no registered handler is terminalized as `Failed` with a clear diagnostic, never marked processed. Likewise, a handler that receives a channel it does not deliver (e.g. `EmailHandler` given an unknown channel) returns an explicit terminal `Fail` rather than `Ok`, so the dispatcher never marks an undelivered channel as processed.
- **Error sanitization.** `LastError` and the operations endpoint redact e-mail addresses, phone numbers, URLs, and credential-like fragments by type before persisting or returning them: `key=value`/`key: value` credential pairs, `Bearer` tokens, JSON secret values (`"token":"…"`), and common password/secret key names, while preserving safe diagnostic fragments.
- **Worker liveness health check.** `OutboxHealthCheck` reports real liveness, not just backlog: the worker refreshes a thread-safe heartbeat on every poll iteration, and a heartbeat that is missing or older than `WorkerStaleAfter` is reported `Unhealthy` regardless of queue depth. When the worker is intentionally disabled (`WorkerEnabled = false`) the liveness signal is skipped. Backlog and failed counts remain secondary, `Degraded` signals.
- **Migration safety:** applying migrations to a relational store is mandatory — a migration failure aborts startup instead of continuing with a warning.
