# Storefront White Background Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the public storefront canvas white on every storefront route without changing dashboard, authentication, or onboarding themes.

**Architecture:** Keep the global `--app-bg` token unchanged. Override the background only in the shared storefront shell and correct the storefront page's direct `--app-bg` references, so routed storefront pages inherit white while unrelated application areas retain the warm cream theme.

**Tech Stack:** Angular 20 standalone components, Ionic 8, SCSS, Jest, Angular production build.

## Global Constraints

- Change styles only; do not modify backend contracts, state, APIs, or persistence.
- Preserve existing hero, card, modal, footer, banner, error, and action component colors.
- Keep dashboard, login, registration, and `.urbeat-onboarding` visuals unchanged.
- Use existing `--app-surface`/white tokens where possible; do not introduce a new dependency or UI library.
- Maintain strict TypeScript/template settings and mobile/PWA behavior.

---

### Task 1: Scope the storefront canvas background

**Files:**
- Modify: `frontend/src/app/features/store/store-shell.component.ts:128-168`
- Modify: `frontend/src/app/features/store/store-page.component.scss:1-19,205`
- Test: `frontend/src/app/features/store/store-shell.component.spec.ts`
- Test: `frontend/src/app/features/store/store-page.component.spec.ts`

**Interfaces:**
- Consumes: existing `.app-shell`, `.store-stage`, `.store-route`, and `.store-content` layout classes.
- Produces: a white storefront canvas inherited by all routes rendered inside `app-store-shell`, while the global `--app-bg` remains warm cream.

- [ ] **Step 1: Inspect existing storefront specs and identify the appropriate host/query assertions**

  Read the two existing specs before editing. Keep their current setup and add only a focused regression assertion if the component test can inspect rendered styles without introducing brittle browser-only behavior.

- [ ] **Step 2: Add the failing regression assertion for the shell background**

  Add a test that renders `StoreShellComponent` with its existing test dependencies and verifies the `.app-shell` background declaration resolves from the storefront-specific white token/value. If the current spec does not render the shell because it requires route services, use a source-level style assertion in the existing spec convention rather than weakening production types or adding a new test framework.

- [ ] **Step 3: Change only the storefront shell background**

  In the inline styles of `store-shell.component.ts`, change `.app-shell` from:

  ```scss
  background: var(--app-shell-bg);
  ```

  to:

  ```scss
  background: var(--app-surface, #fff);
  ```

  Keep `.store-route.has-footer` as white and do not change the global `--app-bg`, `--app-shell-bg`, or onboarding styles.

- [ ] **Step 4: Remove the direct cream background from the storefront page**

  In `store-page.component.scss`, change both storefront canvas declarations:

  ```scss
  :host { background: #ede9e3; }
  .store-content { background: var(--app-bg, #ede9e3); }
  ```

  to:

  ```scss
  :host { background: var(--app-surface, #fff); }
  .store-content { background: var(--app-surface, #fff); }
  ```

  Preserve the rest of the product list and component-specific backgrounds, including the existing product cards and error banner.

- [ ] **Step 5: Verify the storefront regression tests**

  Run from `frontend/`:

  ```powershell
  npx jest --no-coverage src/app/features/store/store-shell.component.spec.ts src/app/features/store/store-page.component.spec.ts
  ```

  Expected: PASS, with all existing tests passing and the new background regression assertion passing.

- [ ] **Step 6: Verify unrelated theme boundaries**

  Run the existing route and onboarding specs from `frontend/`:

  ```powershell
  npx jest --no-coverage src/app/app.routes.spec.ts src/app/features/store-config/store-config-page.component.spec.ts
  ```

  Expected: PASS, demonstrating that route configuration and store configuration surfaces remain unaffected.

- [ ] **Step 7: Run the production build**

  Run from `frontend/`:

  ```powershell
  npx ng build --configuration production
  ```

  Expected: build completes successfully without TypeScript, template, or SCSS errors.

- [ ] **Step 8: Review the diff and commit the implementation**

  Confirm the diff contains only the storefront styles and focused tests; verify no global token, backend, dependency, or unrelated theme changes were introduced. Then commit:

  ```powershell
  git add frontend/src/app/features/store/store-shell.component.ts frontend/src/app/features/store/store-page.component.scss frontend/src/app/features/store/store-shell.component.spec.ts frontend/src/app/features/store/store-page.component.spec.ts
  git commit -m "fix: deixa fundo do storefront branco"
  ```

## Self-review

- Spec coverage: storefront canvas and list are white (Steps 3-4); component-specific surfaces are preserved (Step 4); unrelated themes remain unchanged (Step 6); production build is required (Step 7).
- No global `--app-bg` change is planned.
- No backend, API, persistence, dependency, or architecture change is planned.
