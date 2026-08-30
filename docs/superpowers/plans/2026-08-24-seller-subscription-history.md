# Seller Subscription History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persistir o período das cobranças de mensalidade e remodelar a tela do lojista com histórico descendente e botão de pagamento ainda desabilitado.

**Architecture:** Reutilizar `SellerSubscriptionChargeHistory`, ampliar seu modelo com início/fim do período, preencher os campos na migração e no webhook idempotente, e retornar os campos pelo endpoint existente. A tela Angular continua consumindo os dois endpoints atuais, mas renderiza o período persistido e mantém `Pagar` sem integração.

**Tech Stack:** ASP.NET Core .NET 9, EF Core/PostgreSQL, Angular 20 standalone, signals, Ionic dashboard shell, Jest, xUnit.

## Global Constraints

- Backend owns persisted billing state and ordering; client values are never authoritative.
- Store timestamps in UTC and format with the existing São Paulo helper.
- Payment method is undefined; do not create a payment flow or call a gateway from the disabled button.
- Preserve seller-only authorization and store blocking behavior.
- Preserve mobile/PWA behavior and 44px-plus touch targets.
- Do not change print-agent, OCI deployment, or unrelated storefront behavior.

---

### Task 1: Persist Billing Period

**Files:**
- Modify: `backend/src/Urbeat.Domain/Entities/SellerSubscriptionChargeHistory.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/<timestamp>_AddBillingPeriodToSellerSubscriptionCharges.cs`
- Create: matching migration designer/snapshot updates through EF tooling
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/SellerSubscriptionStatusServiceTests.cs` or the existing subscription test location

**Interfaces:**
- Entity fields: `DateTime? BillingPeriodStartUtc`, `DateTime? BillingPeriodEndUtc`.
- EF mapping: timestamp-with-time-zone nullable columns and an index supporting seller/date history queries.

- [ ] Write a failing mapping/service test asserting a new charge has period start at due date and period end one month later.
- [ ] Run the focused backend test and confirm the period fields are absent or unset.
- [ ] Add nullable period fields and EF precision/mapping configuration.
- [ ] Add the migration from `backend/` with `dotnet ef migrations add AddBillingPeriodToSellerSubscriptionCharges --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure`.
- [ ] Backfill existing rows in `Up`: `BillingPeriodStartUtc = DueDateUtc` and `BillingPeriodEndUtc = DueDateUtc + one calendar month`; keep `Down` reversible.
- [ ] Run the focused backend tests and inspect the generated migration for only the intended table/index changes.

### Task 2: Webhook And DTO Contract

**Files:**
- Modify: `backend/src/Urbeat.Application/DTOs/SellerSubscriptionChargeHistoryItemDto.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/SubscriptionWebhookService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/SellerSubscriptionStatusService.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/SubscriptionWebhookServiceTests.cs` and subscription status tests
- Test: `frontend/src/app/shared/models/subscription.model.ts`

**Interfaces:**
- DTO fields: `BillingPeriodStartUtc` and `BillingPeriodEndUtc` nullable.
- Existing `GET /api/subscriptions/my/charges` returns charges ordered by `DueDateUtc DESC, CreatedAtUtc DESC`.

- [ ] Add failing webhook tests for first charge creation and repeated webhook update, including period values.
- [ ] Run focused tests and confirm they fail before implementation.
- [ ] Set period start/end on charge creation when absent; preserve existing period values on repeated webhook updates.
- [ ] Return period fields from `ListMyChargeHistoryAsync` and order deterministically descending.
- [ ] Update the TypeScript interface without changing endpoint paths.
- [ ] Run backend subscription tests and frontend subscription service tests.

### Task 3: Mensalidade Dashboard UI

**Files:**
- Modify: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.html`
- Modify: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.ts`
- Modify: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.scss`
- Test: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.spec.ts`

**Interfaces:**
- Render period with `formatDate(start) + ' - ' + formatDate(end)` and fallback `Sem período` when legacy data is null.
- Disabled `Pagar` button has no click handler, `disabled`, `aria-disabled="true"`, and accessible explanatory text.

- [ ] Add failing component tests for four columns, period rendering, descending response order, paid status, and disabled unpaid button.
- [ ] Run the focused component tests and verify the intended failures.
- [ ] Remove undefined period controls if present and make the table the primary content after the summary.
- [ ] Render persisted period fields and use explicit status labels/classes.
- [ ] Make `Pagar` disabled and non-actionable while retaining its visual location for future payment integration.
- [ ] Ensure desktop/mobile table behavior and loading/error/empty states remain intact.
- [ ] Run the component test and the seller dashboard regression tests.

### Task 4: Verification

- [ ] Run `dotnet build backend/Urbeat.sln`.
- [ ] Run `dotnet test backend/tests/Urbeat.UnitTests`.
- [ ] Run `npx jest --no-coverage` from `frontend/`.
- [ ] Run `npx ng build --configuration production`.
- [ ] Run the Impeccable detector on the changed dashboard UI files.
- [ ] Review the final diff for unrelated files and confirm no payment gateway was introduced.
