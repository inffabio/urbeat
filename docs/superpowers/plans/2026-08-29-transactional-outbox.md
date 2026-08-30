# Transactional Outbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a durable PostgreSQL-backed outbox for order events and outbound SignalR, notification, e-mail, SMS/WhatsApp, and print work.

**Architecture:** EF Core writes business state and outbox records in one transaction. A hosted dispatcher claims records with PostgreSQL row locks and routes them to typed handlers using at-least-once delivery, idempotency, retry, and terminal failure states. RabbitMQ is deferred behind a publisher boundary.

**Tech Stack:** .NET 9, EF Core 9, PostgreSQL, ASP.NET Core hosted services, Hangfire-compatible existing infrastructure, SignalR, MailKit, existing SMS and print adapters.

## Global Constraints

- PostgreSQL remains the source of truth for order state.
- Do not introduce RabbitMQ in this phase.
- Do not log payloads, tokens, credentials, or payment secrets.
- Keep timestamps in UTC.
- Preserve existing polling fallback and existing payment webhook idempotency.
- WebApi applies EF migrations at startup; do not use `dotnet ef database update`.
- Integration tests use the existing InMemory database harness; relational-only locking behavior must have unit coverage or a PostgreSQL-conditional test.

---

### Task 1: Add Outbox Persistence Model

**Files:**
- Create: `backend/src/Urbeat.Domain/Entities/OutboxMessage.cs`
- Create: `backend/src/Urbeat.Domain/Entities/OutboxMessageStatus.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/20260829000000_AddTransactionalOutbox.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/20260829000000_AddTransactionalOutbox.Designer.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/OutboxMessageTests.cs`

**Interfaces:**
- Produces an EF entity with `Id`, `Type`, `AggregateId`, `AggregateType`, `Payload`, `OccurredAtUtc`, `AvailableAtUtc`, `ProcessedAtUtc`, `AttemptCount`, `LockedUntilUtc`, `LastError`, and `Status`.

- [ ] **Step 1: Write tests for valid pending, processed, and failed message state.**
- [ ] **Step 2: Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OutboxMessageTests"` and verify the new tests fail.**
- [ ] **Step 3: Add the entity, enum, EF configuration, indexes, and migration.** Use a PostgreSQL-friendly index for `(Status, AvailableAtUtc, LockedUntilUtc)` and a length limit for `Type` and `LastError`.
- [ ] **Step 4: Run the focused unit test and `dotnet build backend/Urbeat.sln`; both must pass.**
- [ ] **Step 5: Run `git diff --cached --check` after staging only this task's files.**

### Task 2: Add Outbox Writer and Event Contracts

**Files:**
- Create: `backend/src/Urbeat.Application/Interfaces/IOutboxWriter.cs`
- Create: `backend/src/Urbeat.Application/Outbox/OutboxEventTypes.cs`
- Create: `backend/src/Urbeat.Application/Outbox/OrderCreatedEvent.cs`
- Create: `backend/src/Urbeat.Application/Outbox/OrderStatusChangedEvent.cs`
- Create: `backend/src/Urbeat.Application/Outbox/OutboundMessageRequestedEvent.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/OutboxWriter.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/OutboxWriterTests.cs`

**Interfaces:**
- `IOutboxWriter.EnqueueAsync<T>(string type, Guid aggregateId, T payload, DateTime occurredAtUtc, CancellationToken cancellationToken = default)` adds but does not save an `OutboxMessage` to the current `ApplicationDbContext` unit of work.

- [ ] **Step 1: Write tests proving event payloads serialize deterministically and do not include EF entities or secrets.**
- [ ] **Step 2: Run the focused test and verify failure.**
- [ ] **Step 3: Implement the writer with `System.Text.Json`, invariant options, UTC validation, and stable event type constants.**
- [ ] **Step 4: Register `IOutboxWriter` as scoped and run focused tests plus the solution build.**
- [ ] **Step 5: Commit the task as `feat: add transactional outbox persistence`.**

### Task 3: Integrate Order Transactions

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/PaymentWebhookService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/NotificationService.SignalR.cs`
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/OrderOutboxIntegrationTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/**/OrderOutboxTests.cs`

**Interfaces:**
- Existing order services continue returning their current DTOs.
- Every order mutation that changes externally visible state enqueues exactly one typed event before the existing unit-of-work save.

- [ ] **Step 1: Add failing tests for commit atomicity: order state plus outbox commits together, and a forced save failure leaves neither durable.**
- [ ] **Step 2: Add failing tests for `OrderCreated` and `OrderStatusChanged` payload fields and duplicate suppression for repeated webhook delivery.**
- [ ] **Step 3: Inject `IOutboxWriter` into the affected services and enqueue events using the same DbContext before `SaveChangesAsync`.**
- [ ] **Step 4: Remove direct SignalR delivery from the mutation path while retaining persisted `Notification` creation and polling behavior.**
- [ ] **Step 5: Run `dotnet test backend/tests/Urbeat.UnitTests` and `dotnet test backend/tests/Urbeat.IntegrationTests`.**
- [ ] **Step 6: Run `dotnet build backend/Urbeat.sln` and verify no direct order-to-hub call remains outside an outbox handler.**

### Task 4: Add Dispatcher, Claims, Retry, and Idempotency

**Files:**
- Create: `backend/src/Urbeat.Application/Interfaces/IOutboxDispatcher.cs`
- Create: `backend/src/Urbeat.Application/Interfaces/IOutboxEventHandler.cs`
- Create: `backend/src/Urbeat.Application/Outbox/OutboxProcessingResult.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/OutboxDispatcher.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/OutboxWorker.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/OutboxOptions.cs`
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Modify: `backend/src/Urbeat.WebApi/appsettings.json`
- Modify: `backend/src/Urbeat.WebApi/appsettings.Production.json`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/OutboxDispatcherTests.cs`

**Interfaces:**
- `IOutboxEventHandler.HandleAsync(OutboxMessage message, CancellationToken cancellationToken)` returns `OutboxProcessingResult`.
- `IOutboxDispatcher.DispatchBatchAsync(CancellationToken cancellationToken)` claims and processes one bounded batch.

- [ ] **Step 1: Write tests for routing, successful processing, transient retry, permanent failure, claim expiry, and cancellation.**
- [ ] **Step 2: Run the focused tests and verify failure.**
- [ ] **Step 3: Implement PostgreSQL-safe claim acquisition with bounded batches, `LockedUntilUtc`, and stale processing recovery.**
- [ ] **Step 4: Implement retry with jittered exponential backoff and a configurable maximum attempt count.**
- [ ] **Step 5: Add the hosted worker with scoped dependency resolution and graceful shutdown.**
- [ ] **Step 6: Register options and run all backend unit tests.**

### Task 5: Implement Integration Handlers

**Files:**
- Create: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/OrderSignalRHandler.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/NotificationHandler.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/EmailHandler.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/SmsHandler.cs`
- Create: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/PrintHandler.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/Email/EmailConfirmationService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/CustomerOtpService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/OutboxHandlerTests.cs`

**Interfaces:**
- Each handler implements `IOutboxEventHandler`, declares supported event types, and uses a deterministic delivery key.
- Existing providers remain behind `IEmailService`, `ICustomerVerificationMessageSender`, and the existing print abstraction.

- [ ] **Step 1: Write handler tests asserting provider calls happen only from the handler, retryable exceptions are classified correctly, and duplicate delivery keys are ignored.**
- [ ] **Step 2: Run focused tests and verify failure.**
- [ ] **Step 3: Implement the SignalR handler using the existing hub contexts and preserve customer/seller method names.**
- [ ] **Step 4: Implement e-mail and SMS handlers and change request services to enqueue outbound requests instead of calling providers directly.**
- [ ] **Step 5: Implement print handler with a deterministic order/job key and explicit duplicate behavior.**
- [ ] **Step 6: Run backend unit and integration tests, confirming existing e-mail, OTP, payment, and print tests remain green.**

### Task 6: Observability, Operations, and Verification

**Files:**
- Create: `backend/src/Urbeat.WebApi/Health/OutboxHealthCheck.cs`
- Create: `backend/src/Urbeat.WebApi/Controllers/OutboxOperationsController.cs`
- Modify: `backend/src/Urbeat.WebApi/Program.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Outbox/OutboxDispatcher.cs`
- Modify: `backend/src/Urbeat.WebApi/appsettings.Production.json`
- Test: `backend/tests/Urbeat.UnitTests/WebApi/OutboxHealthCheckTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/**/OutboxOperationsTests.cs`
- Modify: `docs/superpowers/specs/2026-08-29-transactional-outbox-design.md`

**Interfaces:**
- Health check reports worker liveness and pending/failed thresholds without exposing payloads.
- Operations endpoint is authenticated and exposes counts plus safe metadata; it does not expose secrets or permit arbitrary payload editing.

- [ ] **Step 1: Write tests for backlog health thresholds and authorization of operations endpoints.**
- [ ] **Step 2: Implement structured logging with message ID, type, aggregate ID, attempt, result, and latency.**
- [ ] **Step 3: Add metrics/log-friendly counters for pending, retries, failures, and processing latency.**
- [ ] **Step 4: Add authenticated listing and safe retry action for terminal messages.**
- [ ] **Step 5: Run `dotnet test backend/tests/Urbeat.UnitTests`, `dotnet test backend/tests/Urbeat.IntegrationTests`, and `dotnet build backend/Urbeat.sln`.**
- [ ] **Step 6: Verify migrations are applied through WebApi startup and perform a staging smoke test for order creation, status change, e-mail, OTP, SignalR, and print dispatch.**
