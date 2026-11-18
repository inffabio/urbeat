# Subscription Plan Catalog and Public Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven development or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralize subscription plan configuration in the Urbeat admin dashboard, require a server-validated plan selection during seller registration, and connect the landing page to trial/subscription billing stubs.

**Architecture:** Extend the existing `PlanService`, `SellerSubscriptionStatusService`, registration flow, and EF Core persistence rather than introducing parallel subscription systems. Admin APIs own catalog writes, public APIs expose only safe active-plan data, and registration sends only `planId` plus billing period; the server snapshots all commercial terms and creates an `AwaitingGateway` charge stub.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core/PostgreSQL, FluentValidation, Angular 20 standalone components, Signals, Ionic, Jest.

## Global Constraints

- Backend remains authoritative for plan availability, amounts, trials, discounts, subscription state, and billing dates.
- Store timestamps in UTC.
- Preserve Domain → Application → Infrastructure → WebApi layering.
- Preserve Angular standalone components, Signals, `inject()`, strict typing, and modern control flow.

- The seller dashboard is read-only for plans/subscriptions in the initial product; there is no legacy seller contract flow.
- Do not alter `print-agent` or `oci-mcp-server`.
- Do not include existing unrelated untracked files in commits.
- Use TDD: write a failing test, run it, implement the smallest fix, then run the focused and regression suites.

---

## Task 1: Extend plan domain and persistence

**Files:**
- Modify: `backend/src/Urbeat.Domain/Entities/Plan.cs`
- Modify: `backend/src/Urbeat.Domain/Entities/SellerSubscription.cs`
- Create: `backend/src/Urbeat.Domain/Entities/SubscriptionBillingPeriod.cs`
- Create: `backend/src/Urbeat.Domain/Entities/SubscriptionGatewayConfiguration.cs`
- Create: `backend/src/Urbeat.Domain/Entities/SellerSubscriptionChargeStatus.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/<timestamp>_ConfigurableSubscriptionBilling.cs`
- Test: `backend/tests/Urbeat.UnitTests/Domain/SubscriptionPlanRulesTests.cs`

**Interfaces:**
- `SubscriptionBillingPeriod`: `Monthly`, `Annual`.
- `Plan`: `MonthlyAmount`, `AnnualAmount`, `AnnualDiscountPercent`, `MonthlyEnabled`, `AnnualEnabled`, and existing name/description/trial/benefits/active fields.
- `SellerSubscription`: `BillingPeriod`, `PlanDescriptionSnapshot`, `TrialDaysSnapshot`, `AnnualDiscountPercentSnapshot`, and existing snapshot fields.
- Gateway configuration must be stored separately from public plan fields and represented without plaintext logging.

- [ ] **Step 1: Write failing domain tests** for valid/invalid billing-period combinations, discount range `0..100`, non-negative amounts, snapshot fields, and upgrade-only ordering.
- [ ] **Step 2: Run the focused tests**:
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~SubscriptionPlanRulesTests"`
  Expected: FAIL because the new types/rules do not exist.
- [ ] **Step 3: Implement the domain fields, enums, and EF mappings** with PostgreSQL decimal precision, defaults compatible with existing monthly plans, UTC-compatible date columns, and no destructive migration.
- [ ] **Step 4: Generate and inspect the migration** from `backend/`:
  `dotnet ef migrations add ConfigurableSubscriptionBilling --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure`
- [ ] **Step 5: Run focused tests and build**:
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~SubscriptionPlanRulesTests"`
  `dotnet build backend/Urbeat.sln`
- [ ] **Step 6: Commit**:
  `git add backend/src/Urbeat.Domain backend/src/Urbeat.Infrastructure/Persistence backend/tests/Urbeat.UnitTests/Domain && git commit -m "feat: model configurable subscription billing"`

## Task 2: Add authoritative plan validation and admin catalog contracts

**Files:**
- Modify: `backend/src/Urbeat.Application/DTOs/CreatePlanRequestDto.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/UpdatePlanRequestDto.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/PlanResponseDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/UpdateGatewayConfigurationRequestDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/GatewayConfigurationResponseDto.cs`
- Modify: `backend/src/Urbeat.Application/Validators/CreatePlanRequestDtoValidator.cs`
- Modify: `backend/src/Urbeat.Application/Validators/UpdatePlanRequestDtoValidator.cs`
- Create: `backend/src/Urbeat.Application/Validators/BillingPeriodConfigurationValidator.cs`
- Modify: `backend/src/Urbeat.Application/Interfaces/IPlanService.cs`
- Test: `backend/tests/Urbeat.UnitTests/Application/Validators/ConfigurablePlanValidatorTests.cs`

**Interfaces:**
- Create/update requests accept monthly and annual pricing, enabled periods, annual discount, trial days, benefits, and gateway metadata.
- `IPlanService` adds admin-only gateway configuration methods without exposing secrets in public DTOs.
- Validation rejects no enabled period, negative values, discount outside `0..100`, invalid trial days, empty names, and malformed benefits.

- [ ] **Step 1: Write failing validator tests** covering the exact invalid combinations and a valid monthly-only and monthly+annual plan.
- [ ] **Step 2: Run the focused validator tests** and confirm failure.
- [ ] **Step 3: Implement DTOs, validators, and service interface additions**. Keep wire-compatible legacy monthly fields only where required to read existing data; do not let them override the new authoritative values.
- [ ] **Step 4: Run focused tests** and confirm all pass.
- [ ] **Step 5: Commit**:
  `git add backend/src/Urbeat.Application backend/tests/Urbeat.UnitTests/Application/Validators && git commit -m "feat: validate configurable subscription plans"`

## Task 3: Implement admin catalog APIs and service behavior

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/PlanService.cs`
- Create or modify: `backend/src/Urbeat.WebApi/Controllers/AdminPlansController.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/PublicPlansController.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/BillingPlanSeeder.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/ConfigurablePlanServiceTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/Api/AdminPlansTests.cs`

**Interfaces:**
- Admin endpoints require the existing Admin policy and support list, create, update, activation, ordering, and gateway configuration.
- Public `GET /api/public/plans` returns only safe active fields, billing-period options, computed annual display amount, trial, description, and benefits.
- Existing subscriptions remain readable after plan edits because their snapshot fields are not recomputed.

- [ ] **Step 1: Write failing service/controller tests** for admin authorization, CRUD, ordering, active-only public output, and hidden gateway parameters.
- [ ] **Step 2: Run the focused unit/integration tests** and confirm failure.
- [ ] **Step 3: Implement service mapping, admin controller, public DTO mapping, and compatible seed behavior**. Ensure deactivating a plan does not mutate existing subscriptions.
- [ ] **Step 4: Run focused tests**:
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~ConfigurablePlanServiceTests"`
  `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~AdminPlansTests"`
- [ ] **Step 5: Commit**:
  `git add backend/src/Urbeat.Infrastructure/Services backend/src/Urbeat.WebApi/Controllers backend/src/Urbeat.Infrastructure/Persistence/BillingPlanSeeder.cs backend/tests && git commit -m "feat: add admin subscription plan catalog"`

## Task 4: Add pending seller plan selection and consume it at store creation

**Files:**
- Modify: `backend/src/Urbeat.Application/DTOs/RegisterUserRequestDto.cs`
- Modify: `backend/src/Urbeat.Application/Validators/RegisterUserRequestDtoValidator.cs`
- Modify: registration service/controller files identified by existing `registerSeller` implementation
- Modify: `backend/src/Urbeat.Infrastructure/Services/SellerSubscriptionStatusService.cs`
- Create: `backend/src/Urbeat.Application/DTOs/SubscriptionSelectionDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/SubscriptionChargeStubDto.cs`
- Create or modify: charge persistence entity/configuration files
- Test: `backend/tests/Urbeat.UnitTests/Application/Validators/RegisterPlanSelectionTests.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/SellerSubscriptionContractTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/Api/SellerRegistrationSubscriptionTests.cs`

**Interfaces:**
- Registration request accepts required `PlanId` and `BillingPeriod` for new seller registration and stores a pending selection.
- Registration rejects missing/inactive plan or disabled billing period with no seller/store side effects.
- `POST /api/stores` consumes the pending selection atomically, creates one subscription with snapshot name, description, amount, trial days, discount, period, UTC start/trial/end/next billing dates, and starts trial at store creation.
- Charge stub status is `AwaitingGateway`; it has no gateway identifier, boleto code, PDF, or payment URL.

- [ ] **Step 1: Write failing unit and integration tests** for missing plan, invalid period, successful monthly/annual selection, UTC trial dates, snapshot immutability, one-time charge-stub creation, allowed upgrades, rejected downgrades, and preserved subscription history.
- [ ] **Step 2: Run focused tests** and confirm failure.
- [ ] **Step 3: Implement server-side selection validation and transactional subscription creation** after seller registration succeeds. Use an idempotency key based on seller/store and registration operation where the existing flow supports it.
- [ ] **Step 4: Implement the stub charge lifecycle** without calling any gateway.
- [ ] **Step 5: Run focused tests**:
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~RegisterPlanSelection|FullyQualifiedName~SellerSubscriptionContract"`
  `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~SellerRegistrationSubscriptionTests"`
- [ ] **Step 6: Commit**:
  `git add backend/src backend/tests && git commit -m "feat: link seller registration to subscription plans"`

## Task 5: Implement admin plan dashboard

**Files:**
- Modify: `frontend/src/app/features/admin-plans/admin-plans-page.component.ts`
- Modify: `frontend/src/app/features/admin-plans/admin-plans-page.component.html`
- Modify: `frontend/src/app/features/admin-plans/admin-plans-page.component.scss`
- Create or modify: `frontend/src/app/core/services/admin-plans.service.ts`
- Create or modify: `frontend/src/app/shared/models/subscription.model.ts`
- Modify: admin route/menu files if needed
- Test: `frontend/src/app/features/admin-plans/admin-plans-page.component.spec.ts`
- Test: `frontend/src/app/core/services/admin-plans.service.spec.ts`

- [ ] **Step 1: Write failing component/service tests** for list, create, edit, activate/deactivate, benefits, monthly/annual controls, discount, trial, validation errors, and gateway-configuration masking.
- [ ] **Step 2: Run the focused Jest tests** and confirm failure:
  `npx jest --no-coverage src/app/features/admin-plans src/app/core/services/admin-plans.service.spec.ts`
- [ ] **Step 3: Implement the standalone admin UI** using existing admin-shell tokens and patterns. Keep gateway secrets masked and never render server-secret values into public pages.
- [ ] **Step 4: Run focused tests** and confirm pass.
- [ ] **Step 5: Run the admin route/branding specs**:
  `npx jest --no-coverage src/app/features/admin-plans src/app/app.routes.admin.spec.ts src/app/branding-consistency.spec.ts`
- [ ] **Step 6: Commit**:
  `git add frontend/src/app/features/admin-plans frontend/src/app/core/services/admin-plans.service.ts frontend/src/app/shared/models/subscription.model.ts frontend/src/app/app.routes.ts && git commit -m "feat: add admin subscription plan dashboard"`

## Task 6: Make seller subscription surface read-only

**Files:**
- Modify: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.ts`
- Modify: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.html`
- Modify: `frontend/src/app/features/seller-subscription/seller-subscription-page.component.spec.ts`
- Modify: `frontend/src/app/core/services/subscription.service.ts`
- Modify: `frontend/src/app/shared/models/subscription.model.ts`

- [ ] **Step 1: Write failing tests** proving the seller dashboard does not offer plan administration, plan selection, upgrade/downgrade actions, or unsafe gateway settings, while displaying the selected period, trial, snapshot amount, and `Boleto aguardando integração` state.
- [ ] **Step 2: Run the focused seller subscription spec and confirm failure.**
- [ ] **Step 3: Implement read-only seller subscription presentation. Remove plan administration and contract actions, preserve existing overdue/blocked behavior, and show the current plan, period, trial, and stub state.**
- [ ] **Step 4: Run**:
  `npx jest --no-coverage src/app/features/seller-subscription src/app/core/services/subscription.service.spec.ts`
- [ ] **Step 5: Commit**:
  `git add frontend/src/app/features/seller-subscription frontend/src/app/core/services/subscription.service.ts frontend/src/app/shared/models/subscription.model.ts && git commit -m "fix: make seller subscription view read only"`

## Task 7: Connect landing plans and CTA behavior

**Files:**
- Modify: `frontend/src/app/features/landing-page/landing-page.component.ts`
- Modify: `frontend/src/app/features/landing-page/landing-page.component.html`
- Modify: `frontend/src/app/features/landing-page/landing-page.component.scss`
- Modify: `frontend/src/app/shared/models/plan-offer.model.ts`
- Modify: `frontend/src/app/core/services/plan-offer.service.ts`
- Test: `frontend/src/app/features/landing-page/landing-page.component.spec.ts`

- [ ] **Step 1: Write failing tests** for zero/one/multiple plans, conditional title, monthly/annual selection, CTA query params, and `Criar meu cadastro` scrolling to `#plano`.
- [ ] **Step 2: Run the focused landing specs and confirm failure.**
- [ ] **Step 3: Implement computed title and period selection with Signals. Use `routerLink`/router navigation to `/cadastro?planId=...&billingPeriod=...`; do not pass amount/trial/features.**
- [ ] **Step 4: Implement reduced-motion-aware scroll behavior and make the single-plan CTA slightly larger using existing scoped tokens.**
- [ ] **Step 5: Run**:
  `npx jest --no-coverage src/app/features/landing-page src/app/core/services/plan-offer.service.spec.ts`
- [ ] **Step 6: Run the impeccable detector on changed UI files.**
- [ ] **Step 7: Commit**:
  `git add frontend/src/app/features/landing-page frontend/src/app/shared/models/plan-offer.model.ts frontend/src/app/core/services/plan-offer.service.ts && git commit -m "feat: connect landing page to subscription plans"`

## Task 8: Carry plan selection through seller registration

**Files:**
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.ts`
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.html`
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.scss`
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.spec.ts`
- Modify: `frontend/src/app/core/services/auth.service.ts`
- Modify: `frontend/src/app/shared/models/auth.model.ts`

- [ ] **Step 1: Write failing tests** for query-param parsing, plan summary, required selection, request payload containing only `planId`/`billingPeriod`, and invalid/deactivated-plan handling.
- [ ] **Step 2: Run the focused seller registration spec and confirm failure.**
- [ ] **Step 3: Implement strict query-param parsing and request construction. Keep the server as authority and show a clear retry/back-to-plans path if the selection is invalid.**
- [ ] **Step 4: Run**:
  `npx jest --no-coverage src/app/features/seller-register src/app/core/services/auth.service.spec.ts`
- [ ] **Step 5: Commit**:
  `git add frontend/src/app/features/seller-register frontend/src/app/core/services/auth.service.ts frontend/src/app/shared/models/auth.model.ts && git commit -m "feat: preserve subscription selection during seller signup"`

## Task 9: Full validation and review

- [ ] Run backend unit tests:
  `dotnet test backend/tests/Urbeat.UnitTests`
- [ ] Run relevant integration tests:
  `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~Plan|FullyQualifiedName~Subscription|FullyQualifiedName~SellerRegistration"`
- [ ] Build backend:
  `dotnet build backend/Urbeat.sln`
- [ ] Run frontend tests:
  `npx jest --no-coverage`
- [ ] Build frontend:
  `npx ng build --configuration production`
- [ ] Run:
  `git diff --check`
- [ ] Confirm no unrelated untracked files are staged.
- [ ] Review public DTOs and logs for gateway secrets.
- [ ] Ask the architect/reviewer subagent for a final diff review.

