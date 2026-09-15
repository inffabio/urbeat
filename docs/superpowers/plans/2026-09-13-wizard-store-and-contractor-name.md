# Wizard Store And Contractor Name Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven development to implement this plan task-by-task.

**Goal:** Let the first wizard page edit the store name and contractor name independently, persist both in their existing domains, and show the store name beside the dashboard logo.

**Architecture:** Keep the store name in the existing `Store.Name` column and retain its database limit of 120 characters; enforce the requested 100-character product limit in the create/update DTO validators and wizard input. Add a seller-profile update endpoint for the existing `FullName` claim instead of duplicating contractor data in `Store`.

**Tech Stack:** .NET 9, ASP.NET Core Identity, FluentValidation, EF Core/PostgreSQL, Angular 20 standalone, Ionic 8, Jest.

## Global Constraints

- Do not add a second store-name column or migration for `Store.Name`.
- Store name accepts at most 100 characters in the API and wizard; the existing database column remains 120 characters.
- Contractor name is persisted as the seller profile `FullName` claim.
- Preserve strict TypeScript and existing Urbeat onboarding visual tokens.
- Seller dashboard must use `StoreResponse.name`, not the authenticated user's name.

### Task 1: Seller profile contract

**Files:**
- Create: `backend/src/Urbeat.Application/DTOs/UpdateSellerProfileRequestDto.cs`
- Create: `backend/src/Urbeat.Application/Validators/UpdateSellerProfileRequestDtoValidator.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/SellerController.cs`
- Modify: `frontend/src/app/shared/models/auth.model.ts`
- Modify: `frontend/src/app/core/services/auth.service.ts`
- Test: `backend/tests/Urbeat.UnitTests/WebApi/SellerControllerTests.cs` or the existing seller API test location

- [ ] Add `UpdateSellerProfileRequestDto.FullName` and require a trimmed name between 3 and 120 characters.
- [ ] Add `PUT /api/seller/profile` under the existing seller-only controller, update or create the `FullName` claim, and return `{ fullName, document, phoneNumber, email }`.
- [ ] Add `updateSellerProfile({ fullName: string })` to `AuthService` and the corresponding `SellerProfileResponse` typing.
- [ ] Add tests proving an authenticated seller can update the claim and invalid empty/oversized names are rejected.

### Task 2: Store-name validation and wizard fields

**Files:**
- Modify: `backend/src/Urbeat.Application/Validators/CreateStoreRequestDtoValidator.cs`
- Modify: `backend/src/Urbeat.Application/Validators/UpdateStoreRequestDtoValidator.cs`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.ts`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.html`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.scss`
- Test: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`

- [ ] Change `Store.Name` validation messages/rules to maximum 100 characters without changing EF column size.
- [ ] Add a separate `contractorName` signal initialized from seller profile and existing profile data, while `storeName` remains strictly `Store.Name`.
- [ ] Render `Nome da loja` as the first field, required with `maxlength="100"`, followed by `Nome do contratante/lojista`, required and editable.
- [ ] Preserve the existing two-column form grid, with both identity fields occupying the first row and existing category/phone fields following them.
- [ ] Remove the fallback that copies `profile.fullName` into `storeName`; use it only for `contractorName`.
- [ ] Add tests for field labels, independent values, max length, profile fallback, existing-store population, and request payloads.

### Task 3: Save both values and verify dashboard display

**Files:**
- Modify: `frontend/src/app/features/store-config/store-config-page.component.ts`
- Modify: `frontend/src/app/features/seller-shell/seller-shell.facade.ts`
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.html`
- Test: `frontend/src/app/features/seller-shell/seller-shell.facade.spec.ts`
- Test: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`

- [ ] Save the store request using only `storeName` for `name`.
- [ ] After the store save succeeds, update the seller profile with `contractorName`; do not advance the wizard if either save fails.
- [ ] Ensure the dashboard label beside both desktop and mobile logos is `facade.store()?.name`/`storeName`, with no owner-name fallback except the existing generic `Minha loja` fallback when no store is loaded.
- [ ] Add tests proving the dashboard renders the store response name and wizard sends separate store/profile values.

### Task 4: Validation

- [ ] Run `dotnet test backend/tests/Urbeat.UnitTests`.
- [ ] Run `dotnet test backend/tests/Urbeat.IntegrationTests --filter FullyQualifiedName~StoreFlow` and the seller profile flow tests.
- [ ] Run focused Jest tests for the wizard, auth service, and seller shell.
- [ ] Run `npx ng build --configuration production`.
- [ ] Review the diff and confirm no migration changes `Store.Name` from 120.
