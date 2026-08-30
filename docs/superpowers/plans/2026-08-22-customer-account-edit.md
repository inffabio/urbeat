# Customer Account Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable authenticated storefront customers to open Conta, edit the existing customer registration data and primary address, and save changes immediately.

**Architecture:** Add an authenticated customer profile update endpoint, reuse the existing checkout customer form through a dedicated account edit component, and keep account-menu state in the storefront shell. The footer enables Conta only when the customer profile is loaded and remains visible but inert while account UI is open.

**Tech Stack:** Angular 20 standalone components, Ionic 8, Angular signals, `ApiService`, ASP.NET Core .NET 9, EF Core Identity, FluentValidation, Jest, xUnit.

## Global Constraints

- Backend owns validation and persisted customer state; client values are inputs, not trusted results.
- Customer-only endpoints must resolve identity from the authenticated claim.
- Reuse `AddressService` and `PUT /api/customer/addresses/{addressId}` for the primary address.
- Product UI uses existing bordeaux tokens and 44px-plus touch targets.
- Preserve checkout behavior and all existing tests.
- Do not change seller, print-agent, or OCI deployment projects.

---

### Task 1: Customer Profile Update Contract

**Files:**
- Create: `backend/src/Urbeat.Application/DTOs/UpdateCustomerProfileRequestDto.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/CustomerController.cs`
- Modify: `frontend/src/app/shared/models/auth.model.ts`
- Modify: `frontend/src/app/core/services/auth.service.ts`
- Test: `backend/tests/Urbeat.UnitTests/WebApi/CustomerControllerTests.cs`
- Test: `frontend/src/app/core/services/auth.service.spec.ts`

**Interfaces:**
- `PUT /api/customer/me` accepts `fullName`, `email`, and `phoneNumber`.
- Returns `CustomerProfileResponse`.
- `AuthService.updateCustomerProfile(request)` updates `customerProfile` with the response.

- [ ] **Step 1: Write backend tests for authenticated profile update and invalid input.**
- [ ] **Step 2: Run the focused backend tests and verify they fail because the endpoint is absent.**
- [ ] **Step 3: Add the request DTO, validation, Identity update, FullName claim update, and refreshed response.**
- [ ] **Step 4: Add the frontend request model and `AuthService.updateCustomerProfile`.**
- [ ] **Step 5: Run backend and frontend service tests and verify they pass.**

### Task 2: Reusable Customer Account Form

**Files:**
- Create: `frontend/src/app/shared/components/customer-profile-form/customer-profile-form.component.ts`
- Create: `frontend/src/app/shared/components/customer-profile-form/customer-profile-form.component.html`
- Create: `frontend/src/app/shared/components/customer-profile-form/customer-profile-form.component.scss`
- Create: `frontend/src/app/shared/components/customer-profile-form/customer-profile-form.component.spec.ts`
- Modify: `frontend/src/app/features/checkout/customer-page.component.ts`
- Modify: `frontend/src/app/features/checkout/customer-page.component.html`
- Modify: `frontend/src/app/features/checkout/customer-page.component.scss`
- Modify: `frontend/src/app/features/checkout/customer-page.component.spec.ts`

**Interfaces:**
- `@Input() mode: 'checkout' | 'edit'`.
- `@Input() initialProfile` and `@Input() initialAddress` for edit mode.
- `@Output() validSubmit` emits `{ profile, address }`.
- `@Output() dirtyChange` emits whether a valid change exists.

- [ ] **Step 1: Add tests for initial values, validation, dirty state, disabled save, and valid submit.**
- [ ] **Step 2: Run the component tests and verify the new component behavior fails.**
- [ ] **Step 3: Move the common fields, masks, CEP lookup, validation, and address state into the shared component.**
- [ ] **Step 4: Keep checkout-specific session creation in `CustomerPageComponent` and adapt its template to consume the shared form output.**
- [ ] **Step 5: Run all existing checkout tests and the new shared-form tests.**

### Task 3: Account Edit Page And Address Persistence

**Files:**
- Create: `frontend/src/app/features/customer-account/customer-account-page.component.ts`
- Create: `frontend/src/app/features/customer-account/customer-account-page.component.html`
- Create: `frontend/src/app/features/customer-account/customer-account-page.component.scss`
- Create: `frontend/src/app/features/customer-account/customer-account-page.component.spec.ts`
- Modify: `frontend/src/app/core/services/address.service.ts`
- Modify: `frontend/src/app/shared/models/address.model.ts`
- Modify: `frontend/src/app/app.routes.ts`

**Interfaces:**
- Route: `:storePath/conta/cadastro`.
- `AddressService.update(addressId, payload)` calls `PUT /api/customer/addresses/{addressId}`.
- Account page loads `AuthService.restoreCustomerSession()` and `AddressService.list()`, selects the primary address, and saves profile then address.

- [ ] **Step 1: Add tests for authenticated loading, save disabled while clean, enabled after valid edits, successful profile/address saves, and error recovery.**
- [ ] **Step 2: Run the account-page tests and verify they fail because the route/component does not exist.**
- [ ] **Step 3: Implement loading, dirty state, save state, success/error handling, and the shared edit form.**
- [ ] **Step 4: Add the route and address update method.**
- [ ] **Step 5: Run account-page, checkout, auth, and address service tests.**

### Task 4: Store Account Menu And Footer Integration

**Files:**
- Create: `frontend/src/app/shared/components/account-menu/account-menu.component.ts`
- Create: `frontend/src/app/shared/components/account-menu/account-menu.component.spec.ts`
- Modify: `frontend/src/app/features/store/store-shell.component.ts`
- Modify: `frontend/src/app/features/store/store-shell.component.spec.ts`
- Modify: `frontend/src/app/shared/components/footer-nav/footer-nav.component.ts`
- Modify: `frontend/src/app/shared/components/footer-nav/footer-nav.component.spec.ts`

**Interfaces:**
- `AccountMenuComponent` exposes `isOpen`, `onProfile`, and `onLogout` outputs.
- Store shell enables Conta from `AuthService.customerProfile()`, opens the menu, routes to `conta/cadastro`, and calls `AuthService.logout()`.

- [ ] **Step 1: Add tests for enabled/disabled Conta, menu order, Cadastro navigation, and Sair logout.**
- [ ] **Step 2: Run focused menu and shell tests and verify they fail.**
- [ ] **Step 3: Implement the menu above the footer using existing surface, shadow, typography, and bordeaux tokens.**
- [ ] **Step 4: Integrate menu state and inert behavior into the shell and footer.**
- [ ] **Step 5: Run the complete storefront focused suite.**

### Task 5: Full Verification

- [ ] **Step 1: Run all frontend tests with `npx jest --no-coverage`.**
- [ ] **Step 2: Run backend unit tests covering customer/auth controllers.**
- [ ] **Step 3: Run `npx ng build --configuration production`.**
- [ ] **Step 4: Run the Impeccable detector on all changed UI files.**
- [ ] **Step 5: Review the final diff and report any remaining warnings without deploying unless explicitly requested.**
