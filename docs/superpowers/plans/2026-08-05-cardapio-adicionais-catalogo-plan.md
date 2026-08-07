# Cardapio Adicionais Catalogo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the product-summary-only Adicionais screen with a store-owned additional catalog, reusable product associations, and a faithful Bootstrap dashboard layout.

**Architecture:** Add store-scoped additional and option-group catalog records plus a product-to-additional join entity. Preserve the existing checkout DTO shape by mapping active associated catalog records into `ProductAdditionalDto`; legacy product-owned additional rows are migrated into the catalog and join table. The Angular page owns CRUD form state and calls dedicated store APIs, while all authorization, validation, association checks, and persistence remain server-side.

**Tech Stack:** .NET 9, EF Core/PostgreSQL, ASP.NET Core controllers, Angular 20 standalone, Ionic 8, Jest.

## Global Constraints

- Backend owns validation, ownership checks, associations, status transitions, and deletion rules.
- Frontend uses relative `/api` URLs and existing `StoreService`/`ToastService` patterns.
- UI follows `DESIGN.md`, Plus Jakarta Sans, mobile-first layout, 44px touch targets, and existing Bootstrap dashboard tokens.
- Titles in Cardapio are left-aligned; non-menu actions use the existing orange action color.
- Price `0.00` is valid and must be accepted by backend and frontend.

---

### Task 1: Add Store Catalog Domain Model and Persistence

**Files:**
- Create: `backend/src/Urbeat.Domain/Entities/StoreAdditionalGroup.cs`
- Create: `backend/src/Urbeat.Domain/Entities/StoreAdditional.cs`
- Create: `backend/src/Urbeat.Domain/Entities/ProductAdditionalAssignment.cs`
- Modify: `backend/src/Urbeat.Domain/Entities/Store.cs`
- Modify: `backend/src/Urbeat.Domain/Entities/Product.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/src/Urbeat.Infrastructure/Persistence/Migrations/<timestamp>_AddStoreAdditionalCatalog.cs`

**Interfaces:**
- `StoreAdditionalGroup`: `StoreId`, `Name`, `IsActive`, `Store`, `Additionals`.
- `StoreAdditional`: `StoreId`, `GroupId`, `Name`, `Description`, `Price`, `IsActive`, `DisplayOrder`, `Store`, `Group`, `ProductAssignments`.
- `ProductAdditionalAssignment`: `ProductId`, `AdditionalId`, `Product`, `Additional`.

- [ ] **Step 1: Write failing persistence/model tests** covering unique group names per store, store ownership, zero price, and product assignment uniqueness.
- [ ] **Step 2: Run `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreAdditional"` and verify the new tests fail because the entities/configuration do not exist.**
- [ ] **Step 3: Add entities, navigation collections, composite unique indexes, decimal precision, cascade rules, and ownership configuration.**
- [ ] **Step 4: Create the EF migration without editing generated migration history manually.**
- [ ] **Step 5: Run the focused tests and `dotnet build backend/Urbeat.sln`; both must pass.**

### Task 2: Implement Store Additional APIs and Legacy Migration Mapping

**Files:**
- Create: `backend/src/Urbeat.Application/DTOs/StoreAdditionalDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/StoreAdditionalRequestDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/StoreAdditionalDeleteResult.cs`
- Create: `backend/src/Urbeat.Application/Interfaces/IStoreAdditionalService.cs`
- Create: `backend/src/Urbeat.Infrastructure/Services/StoreAdditionalService.cs`
- Create: `backend/src/Urbeat.WebApi/Controllers/StoreAdditionalsController.cs`
- Modify: `backend/src/Urbeat.Application/Mappings/EntityToDtoProfile.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/ProductResponseDto.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/CreateProductRequestDto.cs`
- Modify: `backend/src/Urbeat.Application/DTOs/UpdateProductRequestDto.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/ProductService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/DatabaseSeeder.cs` or the active migration/seeder path

**Interfaces:**
- `GET /api/stores/{storeId}/additionals` returns `{ id, storeId, groupId, groupName, name, description, price, isActive, displayOrder, productCount }[]`.
- `GET /api/stores/{storeId}/additionals/groups` returns the store's active group list, including distinct groups already defined in that store's product option groups.
- `POST /api/stores/{storeId}/additionals` accepts `{ name, description?, groupId, price, isActive, displayOrder }` and returns the created DTO.
- `PUT /api/stores/{storeId}/additionals/{additionalId}` accepts the same body and returns the updated DTO.
- `PATCH /api/stores/{storeId}/additionals/{additionalId}/status` accepts `{ isActive }` and returns the updated DTO.
- `DELETE /api/stores/{storeId}/additionals/{additionalId}` returns `204`, `404`, `403`, or `409` when `productCount > 0`.
- Product create/update accepts catalog additional IDs and validates that every ID belongs to the same store.

- [ ] **Step 1: Write failing integration tests for list, group list, create with `0.00`, edit, status toggle, cross-store rejection, and delete conflict.**
- [ ] **Step 2: Run `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreAdditionals"` and confirm the tests fail for missing routes/services.**
- [ ] **Step 3: Implement DTOs, service methods, ownership checks, validation, and controller routes.**
- [ ] **Step 4: Map legacy `ProductAdditional` and product option group names into the store catalog during migration/startup, preserving product associations and checkout output.**
- [ ] **Step 5: Update product write/read paths to resolve catalog additionals and keep the existing public `ProductAdditionalDto` contract.**
- [ ] **Step 6: Run the focused integration tests, product tests, and full backend build.**

### Task 3: Add Frontend Store API Types and Failing Component Tests

**Files:**
- Modify: `frontend/src/app/core/services/store.service.ts`
- Modify: `frontend/src/app/shared/models/product.model.ts`
- Modify: `frontend/src/app/features/seller-additionals/seller-additionals-page.component.spec.ts`
- Modify: `frontend/src/app/core/services/store.service.spec.ts`

**Interfaces:**
- `StoreAdditional`, `StoreAdditionalGroup`, `StoreAdditionalRequest` mirror the backend DTOs.
- `StoreService` exposes `getStoreAdditionals`, `getStoreAdditionalGroups`, `createStoreAdditional`, `updateStoreAdditional`, `toggleStoreAdditional`, and `deleteStoreAdditional`.

- [ ] **Step 1: Add tests for the exact API URLs and for component behaviors: no “Novo adicional” button in the grid, edit loads the form, save resets to new mode, delete confirms/removes, and status toggles.**
- [ ] **Step 2: Run `npx jest --no-coverage src/app/features/seller-additionals/seller-additionals-page.component.spec.ts src/app/core/services/store.service.spec.ts` and verify the new assertions fail.**
- [ ] **Step 3: Add the typed models and service methods with relative API URLs.**
- [ ] **Step 4: Keep the tests failing only for the not-yet-updated component behavior.**

### Task 4: Replace Adicionais Page Behavior and Markup

**Files:**
- Modify: `frontend/src/app/features/seller-additionals/seller-additionals-page.component.ts`
- Modify: `frontend/src/app/features/seller-additionals/seller-additionals-page.component.html`
- Modify: `frontend/src/app/features/seller-additionals/seller-additionals-page.component.scss`

**Interfaces:**
- Signals: `additionals`, `groups`, `editingAdditionalId`, `formName`, `formDescription`, `formGroupId`, `formPrice`, `formIsActive`, `saving`.
- Methods: `load()`, `startEdit(additional)`, `resetForm()`, `saveAdditional()`, `toggleAdditional(additional)`, `deleteAdditional(additional)`, `formatCurrency(value)`.

- [ ] **Step 1: Implement the component state and handlers against the service methods, including `window.confirm` only after the backend-loaded product count is checked.**
- [ ] **Step 2: Replace the grid markup with the reference columns and remove the grid-level `Novo adicional` action.**
- [ ] **Step 3: Add pencil action to load the right-side form as `Editar adicional`; save then reload the list, clear the form, and return to `Novo adicional`.**
- [ ] **Step 4: Add a status toggle in the Status column with active/inactive color states and persist the state through the API.**
- [ ] **Step 5: Add group listbox sourced from product option groups, allow price `0`, and display backend errors for duplicate/invalid groups or linked deletion.**
- [ ] **Step 6: Run the focused Jest tests and verify they pass.**

### Task 5: Match Cardapio Layout and Shared Action Color Rules

**Files:**
- Modify: `frontend/src/app/features/seller-additionals/seller-additionals-page.component.scss`
- Modify: `frontend/src/app/features/seller-categories/seller-categories-page.component.scss`
- Modify: `frontend/src/app/features/seller-products/seller-products-page.component.scss`
- Modify: `frontend/src/app/shared/components/cardapio-menu-tabs/cardapio-menu-tabs.component.scss` if needed
- Modify: `frontend/src/styles/seller-dashboard-bootstrap.scss` only for rules shared by all Cardapio pages

- [ ] **Step 1: Add layout assertions or DOM tests for left-aligned primary headings and absence of the grid “Novo adicional” action.**
- [ ] **Step 2: Set the additions grid container to the wider reference column and the form container to a stable readable width, collapsing at the existing responsive breakpoint.**
- [ ] **Step 3: Apply orange to non-menu action buttons and preserve menu tabs/segmented/group button colors.**
- [ ] **Step 4: Ensure table/mobile states, focus rings, disabled save state, loading, empty, and error states remain usable at 44px touch targets.**
- [ ] **Step 5: Run the Impeccable detector once over all changed UI targets.**

### Task 6: Full Verification and Review

**Files:**
- No production files unless verification finds a defect.

- [ ] **Step 1: Run `npx jest --no-coverage`.**
- [ ] **Step 2: Run `npx ng build --configuration production`.**
- [ ] **Step 3: Run `dotnet test backend/tests/Urbeat.UnitTests`.**
- [ ] **Step 4: Run `dotnet test backend/tests/Urbeat.IntegrationTests`.**
- [ ] **Step 5: Run `dotnet build backend/Urbeat.sln`.**
- [ ] **Step 6: Inspect `git diff` and `git status`, then request code review for the complete feature.**
