# Dashboard Delivery Radius Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add editable delivery-radius controls to the dashboard neighborhoods page, recalculate available neighborhoods immediately, remove selected neighborhoods outside a reduced radius, and persist everything through the existing save button.

**Architecture:** Reuse `StoreDeliveryPageComponent` for the radius signal, neighborhood loading, distance filtering, dirty state, and delivery-config save flow. Extend the authenticated store-neighborhood query with an optional radius override for preview, and extend the delivery-config update payload so radius and areas are saved atomically. `SellerNeighborhoodsPageComponent` supplies the lateral dashboard container and invokes shared radius-change behavior.

**Tech Stack:** Angular 20 standalone + Ionic 8, TypeScript reactive forms/signals, ASP.NET Core/.NET 9, EF Core, Dapper, Jest, xUnit.

## Global Constraints

- The radius field starts from the store's `maxDeliveryRadiusKm`.
- Radius edits update the available-neighborhood list immediately but are persisted only by the existing save button.
- Distance filtering remains owned by the backend; the frontend may mirror the returned result for display.
- Reducing the radius removes selected areas outside the new radius, while invalid radius values do not query or remove areas.
- Preserve 44px-plus touch targets, existing responsive layout, strict typing, and unrelated worktree changes.

---

## File Map

- Modify `frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts`: hold the edited radius, query with preview radius, and remove out-of-range selected areas safely.
- Modify `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.html`: add the lateral radius card and count/status UI.
- Modify `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.scss`: style the card and responsive states using existing tokens/layout.
- Modify `frontend/src/app/core/services/store.service.ts` and `frontend/src/app/shared/models/store.model.ts`: support an optional preview radius and delivery-config radius payload.
- Modify `backend/src/Urbeat.Application/DTOs/UpdateStoreDeliveryConfigRequestDto.cs`: accept `maxDeliveryRadiusKm`.
- Modify `backend/src/Urbeat.Application/Validators/UpdateStoreDeliveryConfigRequestDtoValidator.cs`: validate a supplied radius as positive.
- Modify `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`, `backend/src/Urbeat.Application/Interfaces/IStoreService.cs`, and `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`: pass and persist the radius and accept the preview query parameter.
- Modify `backend/tests/Urbeat.UnitTests/Infrastructure/Services/StoreServiceDeliveryNeighborhoodsTests.cs`: verify radius overrides and filtering.
- Modify `backend/tests/Urbeat.IntegrationTests/Api/NeighborhoodsDeliveryFlowTests.cs` and relevant store-management tests: verify query and persistence contracts.
- Modify `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.spec.ts` and `frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`: verify UI behavior, removal, invalid states, and payloads.

### Task 1: Extend the backend preview and save contracts

**Files:**
- Modify: `backend/src/Urbeat.Application/DTOs/UpdateStoreDeliveryConfigRequestDto.cs`
- Modify: `backend/src/Urbeat.Application/Validators/UpdateStoreDeliveryConfigRequestDtoValidator.cs`
- Modify: `backend/src/Urbeat.Application/Interfaces/IStoreService.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/StoreServiceDeliveryNeighborhoodsTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/Api/NeighborhoodsDeliveryFlowTests.cs`

**Interfaces:**
- `GetDeliveryNeighborhoodsByStoreAsync(Guid storeId, double? radiusKm, CancellationToken cancellationToken)` uses `radiusKm` when supplied and the persisted store radius otherwise.
- `UpdateDeliveryConfigAsync(..., double? maxDeliveryRadiusKm, ...)` updates the store radius when supplied and keeps the current radius when omitted.

- [ ] **Step 1: Add failing service tests**

Add tests proving a supplied preview radius is used without changing the persisted store, and that the delivery-config request can persist a positive radius while rejecting zero/negative values.

- [ ] **Step 2: Run the focused backend tests and verify failure**

Run from the repository root:

```bash
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreServiceDeliveryNeighborhoodsTests"
```

Expected: compile/test failure because the new parameter is not present.

- [ ] **Step 3: Implement the optional radius query parameter**

Update the controller query binding and service/interface signature. In `StoreService`, calculate with `radiusKm ?? store.MaxDeliveryRadiusKm`, retaining existing city, coordinate, Haversine, and coordinate-less manual-neighborhood rules.

- [ ] **Step 4: Add the optional radius to the save request**

Add `double? MaxDeliveryRadiusKm` to the request DTO. Validate only when supplied, requiring a value greater than zero. In `UpdateDeliveryConfigAsync`, assign it to `store.MaxDeliveryRadiusKm` before saving; when omitted, preserve the existing value for callers that do not edit radius.

- [ ] **Step 5: Update integration coverage**

Add an authenticated request test that queries with a temporary radius, verifies the returned neighborhood set, then saves a different radius through `PATCH /api/stores/{id}/delivery-config` and verifies `GET /api/stores/me` returns it.

- [ ] **Step 6: Run backend tests**

Run:

```bash
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreServiceDeliveryNeighborhoodsTests"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~NeighborhoodsDeliveryFlowTests"
```

Expected: focused tests pass.

### Task 2: Implement dashboard radius editing and recalculation

**Files:**
- Modify: `frontend/src/app/core/services/store.service.ts`
- Modify: `frontend/src/app/shared/models/store.model.ts`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts`
- Modify: `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.html`
- Modify: `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.scss`
- Test: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`
- Test: `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.spec.ts`

**Interfaces:**
- `StoreService.getDeliveryNeighborhoodsByStore(storeId: string, radiusKm?: number)` sends the optional radius as a query parameter.
- `UpdateDeliveryConfigRequest.maxDeliveryRadiusKm?: number` is included in `persistConfig()`.
- `StoreDeliveryPageComponent.onDeliveryRadiusChange(value: number): void` updates the signal, reloads neighborhoods with the temporary radius, removes out-of-range selected areas only after a successful response, and marks the form dirty.

- [ ] **Step 1: Add failing frontend tests for radius state and payload**

Cover initial hydration from `maxDeliveryRadiusKm`, service call with the edited radius, `maxDeliveryRadiusKm` in the save payload, and no request/removal for values below `1`.

- [ ] **Step 2: Add a failing dashboard test for selected-area removal**

Create selected areas with one neighborhood inside and one outside the response returned for the reduced radius. Trigger `onDeliveryRadiusChange`, flush the observable, and assert only the inside area remains and `formDirty()` is true. Assert failed reloads retain both areas.

- [ ] **Step 3: Run focused frontend tests and verify failure**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/features/store-config/delivery/store-delivery-page.component.spec.ts src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.spec.ts
```

Expected: failures for the missing radius API binding, handler, payload, and dashboard container.

- [ ] **Step 4: Add the radius state and service binding**

Hydrate `maxDeliveryRadiusKm` from the loaded store, add the optional query parameter in `StoreService`, and include the current valid radius in `persistConfig()`.

- [ ] **Step 5: Implement the recalculation handler**

Normalize the input as a number. For invalid values, expose validation without querying or removing areas. For valid values, set the radius and call `getDeliveryNeighborhoodsByStore(storeId, value)`. On success, replace the available list and remove only selected `FormArray` groups whose neighborhood is absent from the successful eligible response; mark dirty. On error, retain the previous list and selected groups and show the existing error toast.

- [ ] **Step 6: Add the lateral dashboard container**

Place an `Alcance de entrega` card in the existing aside column with a numeric input (`min="1"`, `step="1"`, 44px minimum interaction height), `km` suffix, available-neighborhood count, and pending-save message. Bind input changes to the shared handler. Keep the current save button as the only persistence action.

- [ ] **Step 7: Add responsive styles**

Use the existing card, color, spacing, and breakpoint conventions. Keep the lateral card beside the main list on wide screens and allow the existing column layout to stack it on smaller screens without horizontal overflow.

- [ ] **Step 8: Run focused frontend tests and production build**

Run:

```bash
npx jest --no-coverage src/app/features/store-config/delivery/store-delivery-page.component.spec.ts src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.spec.ts
npx ng build --configuration production
```

Expected: focused tests pass and production build succeeds.

### Task 3: Full verification and review

**Files:**
- No new files; review all implementation and test files from Tasks 1-2.

- [ ] **Step 1: Run backend build and unit tests**

```bash
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
```

Expected: build succeeds and all unit tests pass.

- [ ] **Step 2: Run relevant integration tests**

```bash
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~NeighborhoodsDeliveryFlowTests"
```

Expected: relevant integration tests pass. Record unrelated pre-existing failures separately rather than changing their fixtures.

- [ ] **Step 3: Inspect the final diff**

```bash
git diff --check
git diff --stat
git status --short
```

Confirm no unrelated files are staged or modified by the implementation.

- [ ] **Step 4: Commit only the feature changes**

```bash
git add -- backend/src/Urbeat.Application/DTOs/UpdateStoreDeliveryConfigRequestDto.cs backend/src/Urbeat.Application/Validators/UpdateStoreDeliveryConfigRequestDtoValidator.cs backend/src/Urbeat.Application/Interfaces/IStoreService.cs backend/src/Urbeat.WebApi/Controllers/StoresController.cs backend/src/Urbeat.Infrastructure/Services/StoreService.cs backend/tests/Urbeat.UnitTests/Infrastructure/Services/StoreServiceDeliveryNeighborhoodsTests.cs backend/tests/Urbeat.IntegrationTests/Api/NeighborhoodsDeliveryFlowTests.cs frontend/src/app/core/services/store.service.ts frontend/src/app/shared/models/store.model.ts frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts frontend/src/app/features/store-config/delivery/store-delivery-page.component.html frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.html frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.scss frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.spec.ts
git commit -m "feat: configure dashboard delivery radius"
```
