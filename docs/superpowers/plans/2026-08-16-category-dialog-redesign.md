# Category Dialog Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broken category edit/delete overlays with one reliable native HTML dialog in the store-products wizard.

**Architecture:** Keep the existing category API and local signals, but replace the two independent modal states with one dialog mode and one selected category. Render one `<dialog>` outside the category list; use a form submit for editing and an explicit confirmation button for deletion.

**Tech Stack:** Angular 20 standalone components, strict TypeScript, native HTML `<dialog>`, Jest.

## Global Constraints

- Preserve existing API contracts and backend ownership/product rules.
- Keep Angular and TypeScript strictness enabled.
- Preserve existing wizard and onboarding visual tokens.
- Maintain 44px-plus touch targets.
- Do not modify the separate dashboard category implementation beyond inherited methods.

### Task 1: Add Native Dialog Behavior Tests

**Files:**
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

- [ ] Add tests asserting the template has exactly one native `dialog` and uses `ngSubmit` for editing.
- [ ] Add a test that opening edit sets only edit mode and preserves the draft name.
- [ ] Add a test that opening delete sets only delete mode.
- [ ] Add tests that cancel clears the dialog state without calling the service.
- [ ] Run `npx jest --no-coverage src/app/features/store-config/products/store-products-page.component.spec.ts --runInBand` and confirm the new tests fail because the native dialog state does not exist.

### Task 2: Replace Component Dialog State

**Files:**
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.ts`

- [ ] Replace `editingCategoryId`, `editingCategoryName`, `deletingCategoryId`, and `deletingCategoryName` with a `categoryDialogMode` signal, selected id/name signals, edit draft signal, and error signal.
- [ ] Implement `openEditCategory`, `openDeleteCategory`, and `closeCategoryDialog` around that single state.
- [ ] Keep existing API calls, local updates, loading signals, and toast behavior.
- [ ] Ensure API errors keep the dialog open and expose the backend detail in the dialog.
- [ ] Run the focused Jest suite and confirm all tests pass.

### Task 3: Replace Modal Markup and Styles

**Files:**
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.html`
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.scss`

- [ ] Remove both custom modal blocks.
- [ ] Add one native `<dialog>` outside the category list with conditional edit/delete content.
- [ ] Use `(ngSubmit)` for the edit form and `type="submit"` for Save.
- [ ] Use explicit `type="button"` for Cancel, Close, and Delete actions.
- [ ] Do not bind backdrop clicks to close the dialog.
- [ ] Style the dialog and `::backdrop` using existing tokens and touch target sizes.
- [ ] Run the focused Jest suite and production build.

### Task 4: Validate and Publish

**Files:**
- No additional source files.

- [ ] Run focused Jest tests.
- [ ] Run `npx ng build --configuration production`.
- [ ] Run `git diff --check`.
- [ ] Deploy only `application` through `deploy-all.ps1`.
- [ ] Confirm backend health and recreated frontend container.
