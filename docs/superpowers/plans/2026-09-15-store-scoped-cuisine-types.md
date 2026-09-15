# Store-Scoped Cuisine Categories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven development or executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Replace the global mutable cuisine catalog with 15 protected default categories plus store-owned custom categories, while requiring a category during store setup.

**Architecture:** Extend `CuisineType` with `IsDefault` and nullable `StoreId`. Global rows represent only protected defaults; custom rows reference one store. Initial store creation accepts a category name and creates a private category in the same unit of work when it is not a default. Existing non-default rows are copied per referencing store and store foreign keys are rewired by an EF migration.

**Tech Stack:** .NET 9, EF Core/Npgsql, FluentValidation, Angular 20 standalone, Ionic 8, Jest, xUnit.

## Global Constraints

- Preserve existing stores and their current category values; never silently map legacy values to another category.
- The protected defaults are exactly: Açaiteria, Cafeteria, Churrascaria, Comida Árabe, Comida Japonesa, Comida Mexicana, Doceria, Hamburgueria, Lanches, Marmitaria, Padaria, Pastelaria, Pizzaria, Sucos e Vitaminas, Tapiocaria.
- The new-store category selection starts as `''` and empty values are rejected by both frontend and backend.
- Store-specific categories must not be visible to another store and default categories cannot be mutated through store flows.
- Do not remove descriptions from products, variations, product categories, additionals, plans, landing content, or system parameters.
- Keep Angular strictness, existing API contracts outside cuisine categories, and current mobile behavior.
- Do not touch `.opencode`, generated `__pycache__`, audio files, or `*.csproj.user`.

---

## File Map

- Modify `backend/src/Urbeat.Domain/Entities/CuisineType.cs`: add default and owner-store fields.
- Modify `backend/src/Urbeat.Domain/Entities/Store.cs`: keep the existing cuisine FK and navigation; no new duplicate store field.
- Modify `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`: configure nullable owner relation and scoped uniqueness.
- Modify `backend/src/Urbeat.Infrastructure/Persistence/CuisineTypeSeeder.cs`: idempotently seed and normalize the 15 defaults.
- Create `backend/src/Urbeat.Infrastructure/Persistence/Migrations/<timestamp>_ScopeCuisineTypesToStores.cs`: preserve and rewire legacy categories.
- Modify the matching migration designer and `ApplicationDbContextModelSnapshot.cs` through EF tooling.
- Modify `backend/src/Urbeat.Application/DTOs/CuisineTypeResponseDto.cs`, `CreateCuisineTypeRequestDto.cs`, and `CreateStoreRequestDto.cs` only as needed for store scope and pending creation.
- Modify `backend/src/Urbeat.Application/Interfaces/ICuisineTypeService.cs` and `backend/src/Urbeat.Infrastructure/Services/CuisineTypeService.cs`: default listing and owner-scoped CRUD.
- Modify `backend/src/Urbeat.Application/Interfaces/IStoreService.cs` and `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`: resolve default or create a private category in create/update flows.
- Modify `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`: default endpoint plus owner-scoped category routes and controlled errors.
- Modify `backend/src/Urbeat.Application/Validators/CreateStoreRequestDtoValidator.cs` and update validator tests for required category behavior.
- Modify `frontend/src/app/core/services/store.service.ts`: replace global create with store-scoped methods.
- Modify `frontend/src/app/features/store-config/store-config-page.component.ts`: local pending categories during creation, scoped categories for existing stores, protected defaults, and empty validation.
- Modify `frontend/src/app/features/store-config/store-config-page.component.html`: display validation and prevent advancing/saving with no category.
- Modify `frontend/src/app/features/store-config/store-config-page.component.spec.ts`: cover ordering, blank initial state, validation, local pending category, and protected deletion.
- Modify backend and integration fixtures that assume the old seven/eight global categories.

## Task 1: Add the Store Scope to the Domain Model

**Files:**
- Modify `backend/src/Urbeat.Domain/Entities/CuisineType.cs`
- Modify `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Test `backend/tests/Urbeat.UnitTests/Persistence/CuisineTypeConfigurationTests.cs` or the existing persistence test file

- [ ] **Step 1: Write failing model tests**

Assert that a default category has `IsDefault == true` and `StoreId == null`, while a custom category has `IsDefault == false` and a non-null `StoreId`. Assert the EF model exposes a nullable `CuisineType.StoreId` foreign key and a uniqueness index scoped by store.

- [ ] **Step 2: Run the focused test**

Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CuisineTypeConfigurationTests"`.
Expected: fail because the fields and configuration do not exist.

- [ ] **Step 3: Implement the smallest model change**

Add `public bool IsDefault { get; set; }` and `public Guid? StoreId { get; set; }` to `CuisineType`, plus `public Store? Store { get; set; }`. Configure the optional relationship with `DeleteBehavior.Restrict` and a case-insensitive-compatible normalized-name uniqueness strategy matching the project database conventions. Do not change `Store.CuisineTypeId`.

- [ ] **Step 4: Run the focused test**

Run the same command. Expected: PASS.

- [ ] **Step 5: Commit and push**

Run `git add backend/src/Urbeat.Domain/Entities/CuisineType.cs backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs backend/tests/Urbeat.UnitTests/Persistence/CuisineTypeConfigurationTests.cs && git commit -m "feat: scope cuisine categories to stores" && git push origin main`.

## Task 2: Migrate Defaults and Existing Store Categories

**Files:**
- Modify `backend/src/Urbeat.Infrastructure/Persistence/CuisineTypeSeeder.cs`
- Create EF migration from `backend/`
- Modify migration snapshot generated by EF
- Test `backend/tests/Urbeat.UnitTests/Persistence/CuisineTypeMigrationTests.cs` or the existing migration/persistence tests

- [ ] **Step 1: Write migration and seeder tests**

Cover all 15 exact names, alphabetical API ordering, idempotent seeding, and legacy preservation: if two stores reference one old global category, the migration must leave each store pointing to a separate private copy with the same name.

- [ ] **Step 2: Run tests to verify they fail**

Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CuisineTypeMigrationTests"`.
Expected: fail because the new flags, relation, and migration behavior are absent.

- [ ] **Step 3: Implement migration-safe seeding**

Make the seeder upsert the 15 defaults by normalized name, set `IsDefault = true`, `StoreId = null`, and never return early merely because any cuisine row exists. It must not delete custom categories or legacy rows before migration.

- [ ] **Step 4: Generate the migration**

From the repository root run:

```powershell
dotnet ef migrations add ScopeCuisineTypesToStores --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

The migration must add the columns, preserve the existing `Store.CuisineTypeId`, copy every non-default legacy category once per referencing store, rewire each store, mark the four legacy equivalents as defaults only when their names match the approved defaults, and add the scoped uniqueness constraints. It must be safe when there are no stores or no legacy categories.

- [ ] **Step 5: Run migration and persistence tests**

Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CuisineTypeMigrationTests"`. Expected: PASS.

- [ ] **Step 6: Commit and push**

Run `git add backend/src/Urbeat.Infrastructure/Persistence backend/src/Urbeat.Domain/Entities/CuisineType.cs backend/tests/Urbeat.UnitTests && git commit -m "feat: migrate cuisine categories to store scope" && git push origin main`.

## Task 3: Implement Backend Category Ownership and Store Resolution

**Files:**
- Modify `backend/src/Urbeat.Application/Interfaces/ICuisineTypeService.cs`
- Modify `backend/src/Urbeat.Infrastructure/Services/CuisineTypeService.cs`
- Modify `backend/src/Urbeat.Application/Interfaces/IStoreService.cs`
- Modify `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`
- Modify `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`
- Modify `backend/src/Urbeat.Application/DTOs/CreateCuisineTypeRequestDto.cs`
- Modify `backend/src/Urbeat.Application/DTOs/CreateStoreRequestDto.cs` only if an explicit pending-category field is required
- Test backend service and controller tests under `backend/tests/Urbeat.UnitTests` and `backend/tests/Urbeat.IntegrationTests/Api/StoreFlowTests.cs`

- [ ] **Step 1: Write failing service tests**

Cover: `GetActiveAsync` returns exactly the 15 defaults sorted; `GetForStoreAsync(storeId)` returns defaults plus only that store's custom rows; creating a custom category rejects blank, duplicate-in-scope, and default-name mutation; a seller cannot read or create for another store; `CreateForOwnerAsync` accepts a default and creates a private category for a custom name in the same save operation; update rejects another store's custom category.

- [ ] **Step 2: Run focused tests**

Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CuisineTypeServiceTests"` and the focused integration test command. Expected: failures for missing methods and global behavior.

- [ ] **Step 3: Implement service contracts**

Use explicit owner-scoped signatures such as `GetForStoreAsync(Guid ownerUserId, Guid storeId, CancellationToken)` and `CreateForStoreAsync(Guid ownerUserId, Guid storeId, string name, CancellationToken)`. Keep the anonymous/default endpoint separate. Resolve create/update names by first matching an active default, then an active category owned by the target store, otherwise create one private category for the new store only.

- [ ] **Step 4: Implement controller routes and errors**

Keep `GET /api/stores/cuisine-types` as the unauthenticated default list. Replace global `POST /api/stores/cuisine-types` with `GET /api/stores/{storeId}/cuisine-types` and `POST /api/stores/{storeId}/cuisine-types`, enforcing seller ownership. Add deletion only for custom categories if the current store does not reference it, and return `400`/`403`/`404`/`409` consistently rather than silently changing another store.

- [ ] **Step 5: Enforce validation**

Keep `CreateStoreRequestDtoValidator.RuleFor(x => x.CuisineType).NotEmpty().MaximumLength(80)`. Add equivalent non-empty validation to update and category-create DTOs. Ensure service-level validation prevents a blank `.Trim()` from becoming a lookup that can accidentally select a record.

- [ ] **Step 6: Run focused backend tests**

Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CuisineType"` and `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreFlowTests"`. Expected: PASS.

- [ ] **Step 7: Commit and push**

Run `git add backend/src backend/tests && git commit -m "feat: enforce store-owned cuisine categories" && git push origin main`.

## Task 4: Update the Store Configuration Wizard

**Files:**
- Modify `frontend/src/app/core/services/store.service.ts`
- Modify `frontend/src/app/features/store-config/store-config-page.component.ts`
- Modify `frontend/src/app/features/store-config/store-config-page.component.html`
- Modify `frontend/src/app/features/store-config/store-config-page.component.spec.ts`
- Modify `frontend/src/app/shared/models/store.model.ts` only if response fields change

- [ ] **Step 1: Write failing frontend tests**

Add tests asserting that a new component has `cuisineType() === ''`, the 15 defaults are rendered in `localeCompare('pt-BR')` order, `goNext()`/`saveDraft()` rejects blank selection and shows the existing toast/error pattern, adding a category during new setup changes only local state, and default rows cannot be deleted. Add an existing-store test proving store-scoped loading and creation calls use `/api/stores/{storeId}/cuisine-types`.

- [ ] **Step 2: Run focused Jest tests**

Run `npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts`. Expected: failures for current global creation, initial selection, and missing blank validation.

- [ ] **Step 3: Update the service API**

Replace `createCuisineType(name)` with `getStoreCuisineTypes(storeId)`, `createStoreCuisineType(storeId, name)`, and `deleteStoreCuisineType(storeId, categoryId)` methods. Keep `getCuisineTypes()` for protected defaults used before a store exists.

- [ ] **Step 4: Implement new-store local behavior**

Initialize `cuisineType` to `''`. Keep a `pendingCuisineTypes` signal keyed by a client-only id. When no store exists, `addCategory()` validates duplicates against defaults and pending values, adds the pending option, selects it, and closes the modal without an HTTP request. Include only `cuisineType()` in the existing create-store request; backend persists custom ownership atomically.

- [ ] **Step 5: Implement existing-store behavior and validation UI**

When `store?.id` exists, load defaults plus store-owned rows. Existing-store add/delete operations call the scoped API. Disable deletion for `IsDefault` rows. Add an inline category error beside the select and return early from `goNext()`/`saveDraft()` when `!cuisineType().trim()`.

- [ ] **Step 6: Run focused frontend tests and build**

Run `npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts` and `npx ng build --configuration production`. Expected: all focused tests pass and the build completes; existing bundle-budget warnings are acceptable if unchanged.

- [ ] **Step 7: Commit and push**

Run `git add frontend/src/app/core/services/store.service.ts frontend/src/app/features/store-config frontend/src/app/shared/models/store.model.ts && git commit -m "feat: scope wizard cuisine categories to stores" && git push origin main`.

## Task 5: Update Fixtures, Full Validation, and Review

**Files:**
- Modify only backend/frontend tests and fixtures that encode the old default category list or global endpoint.
- Do not modify unrelated product/category descriptions.

- [ ] **Step 1: Search for stale global behavior**

Run `rg -n "POST.*cuisine-types|createCuisineType|CuisineTypeSeeder|Pizza|Brasileira|Japonesa" backend frontend/src/app --glob '!**/node_modules/**'` and classify every match as an approved fixture, a protected default, or stale global behavior.

- [ ] **Step 2: Run complete validation**

Run:

```powershell
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.IntegrationTests
npx jest --no-coverage
npx ng build --configuration production
```

Expected: all tests pass and the Angular production build completes.

- [ ] **Step 3: Request independent review**

Review the complete diff for cross-store authorization, migration data preservation, blank selection handling, and accidental removal of unrelated descriptions. Fix findings with a failing test first.

- [ ] **Step 4: Commit and push any review fixes**

Use a separate commit for each correction and push `origin main` after each commit. Confirm `git rev-parse HEAD` equals `git ls-remote origin refs/heads/main`.
