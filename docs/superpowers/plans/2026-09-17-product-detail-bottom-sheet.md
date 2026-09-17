# Product Detail Bottom Sheet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild product customization as a compact bottom sheet with a fixed add footer and handle-based dismissal.

**Architecture:** Keep `ProductDetailPageComponent` and its existing selection/cart logic. Restructure only its template and stylesheet: a shell with handle, scrollable product body, and solid sticky action footer. Add local pointer gesture state to the page and reuse `onBack()` for dismissal.

**Tech Stack:** Angular 20 standalone, Ionic `IonContent`/`IonIcon`, strict TypeScript, Jest.

## Global Constraints

- Preserve all existing product selection and price behavior.
- Remove the separate back-to-menu component from this screen.
- Keep the CTA visible and solid at the bottom of the product panel.
- Keep the product body as the only scrollable region.
- Preserve safe-area and store-footer clearance on mobile.
- Do not add dependencies.

---

### Task 1: Add Regression Tests

**Files:**
- Modify: `frontend/src/app/features/product-detail/product-detail-page.component.spec.ts`

- [ ] **Step 1: Write failing tests**

Assert the rendered template/source contains the handle and product body/footer structure, does not contain `app-back-to-menu-link`, and defines the expected sticky footer/body scroll rules. Add behavior tests for handle click, drag threshold, and short drag.

- [ ] **Step 2: Run focused tests**

```bash
npx jest --no-coverage src/app/features/product-detail/product-detail-page.component.spec.ts
```

Expected: FAIL because the old back link and old sticky layout remain.

### Task 2: Implement Product Bottom Sheet

**Files:**
- Modify: `frontend/src/app/features/product-detail/product-detail-page.component.html`
- Modify: `frontend/src/app/features/product-detail/product-detail-page.component.scss`
- Modify: `frontend/src/app/features/product-detail/product-detail-page.component.ts`

- [ ] **Step 1: Restructure the template**

Use a handle button, compact product identity, `.product-detail-body` scroll region, and `.product-fixed` action footer. Remove `BackToMenuLinkComponent` from imports and remove its template node.

- [ ] **Step 2: Add gesture handling**

Store the active pointer start Y only from the handle. Close at an 80px downward delta, ignore short drags, and keep keyboard activation/click behavior intact.

- [ ] **Step 3: Replace layout styles**

Use a solid sticky footer, remove the gradient and trailing back-link spacing, set the body to `min-height: 0` and `overflow-y: auto`, and preserve safe-area/footer clearance.

- [ ] **Step 4: Run focused tests**

```bash
npx jest --no-coverage src/app/features/product-detail/product-detail-page.component.spec.ts
```

Expected: PASS.

### Task 3: Validate Integration

**Files:**
- Test: `frontend/src/app/features/product-detail/product-detail-page.component.spec.ts`
- Test: related storefront/cart suites.

- [ ] **Step 1: Run related tests**

```bash
npx jest --no-coverage src/app/features/product-detail/product-detail-page.component.spec.ts src/app/features/store/store-shell.component.spec.ts src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts
```

- [ ] **Step 2: Run full validation**

```bash
npx jest --no-coverage
npx ng build --configuration production
```

- [ ] **Step 3: Review the diff**

```bash
git diff --check
git diff -- frontend/src/app/features/product-detail/product-detail-page.component.ts frontend/src/app/features/product-detail/product-detail-page.component.html frontend/src/app/features/product-detail/product-detail-page.component.scss frontend/src/app/features/product-detail/product-detail-page.component.spec.ts
```
