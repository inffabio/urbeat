# Wizard and Dashboard Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep onboarding wizard screens exclusive to `/configurar-loja/*`, dashboard configuration exclusive to `/app/*`, and make the wizard vertically scrollable on all supported viewport sizes.

**Architecture:** Preserve the existing route split and dedicated `seller-*` dashboard components. Remove dashboard-only navigation from wizard templates, scope app-shell scrolling to surfaces containing the wizard header, and retain the existing public store slug behavior at `https://www.urbeat.com.br/<slug>`.

**Tech Stack:** Angular 20 standalone components, Ionic 8, Angular templates, SCSS, Jest, Angular production build.

## Global Constraints

- `/configurar-loja/*` is only the first-store onboarding flow: Loja, Horários, Entrega, Cardápio, Publicar.
- `/app/*` is only the post-creation seller dashboard and its management screens.
- Dashboard-only links are Horários, Informações, Impressão, Bio, and Bairros.
- Preserve existing route URLs and dedicated dashboard components.
- Public store URLs use `https://www.urbeat.com.br/<slug>`; the wizard and dashboard are not public storefront paths.
- Preserve mobile/PWA/Capacitor behavior, safe-area spacing, and 44px-plus touch targets.
- Do not weaken Angular or TypeScript strictness.

---

### Task 1: Remove Dashboard Navigation From Wizard

**Files:**
- Modify: `frontend/src/app/features/store-config/store-config-page.component.html`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.ts`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.html`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts`
- Test: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`
- Test: `frontend/src/app/app.routes.spec.ts`

**Interfaces:**
- Wizard templates keep `WizardHeaderComponent` and `WizardFooterComponent`.
- Dashboard navigation remains provided by `ConfigSubnavComponent` only from dashboard components.

- [ ] **Step 1: Add a failing template ownership test**

Assert that the wizard component template does not contain `app-config-subnav`, while the route tests continue asserting that `/app/configuracoes/informacoes`, `/app/configuracoes/horarios`, `/app/configuracoes/bairros`, and `/app/configuracoes/bio` resolve to dedicated seller components.

- [ ] **Step 2: Run the focused tests and confirm the ownership test fails**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts src/app/app.routes.spec.ts
```

Expected: the new wizard ownership assertion fails because `ConfigSubnavComponent` is currently rendered by the wizard template.

- [ ] **Step 3: Remove dashboard-only imports and markup from wizard components**

Remove `ConfigSubnavComponent` from wizard component imports and remove its template usage. Remove only dead dashboard-specific branches from the wizard components where the route split makes them unreachable; keep wizard copy, step navigation, and form behavior unchanged.

- [ ] **Step 4: Run focused tests and confirm they pass**

```bash
npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts src/app/app.routes.spec.ts
```

Expected: PASS, with dashboard route ownership still pointing to `seller-*` components.

### Task 2: Scope Wizard Scrolling

**Files:**
- Modify: `frontend/src/theme/global.scss`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.scss`
- Test: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`

**Interfaces:**
- The app shell remains fixed for dashboard use.
- Wizard surfaces containing `app-wizard-header` gain vertical scrolling without changing dashboard overflow.

- [ ] **Step 1: Add a failing style contract test**

Add a focused assertion or fixture-level check that the wizard host/shell includes the scoped scroll behavior and bottom safe-area spacing expected for a long form.

- [ ] **Step 2: Run the focused test and confirm it fails**

```bash
npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts
```

Expected: FAIL until the wizard scroll contract is present.

- [ ] **Step 3: Implement scoped vertical scrolling**

Allow the app shell to scroll vertically only when it contains a wizard header, retain `overflow-x: hidden`, and add wizard bottom spacing for the footer and safe area. Do not change `.seller-main` or dashboard shell overflow rules. Keep existing `ion-content` scrolling on wizard subpages.

- [ ] **Step 4: Run the focused test and inspect responsive styles**

```bash
npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts
```

Expected: PASS. Confirm the CSS remains scoped to wizard surfaces and does not introduce a second scrollbar in dashboard routes.

### Task 3: Validate Public Slug and Full Frontend Surface

**Files:**
- Inspect/modify only if needed: `frontend/src/app/features/store-config/publish/store-publish-page.component.html`
- Inspect/modify only if needed: `frontend/src/app/features/store-config/publish/store-publish-page.component.ts`
- Inspect/modify only if needed: `frontend/src/app/app.routes.ts`
- Test: relevant existing publish and route specs

**Interfaces:**
- Store slug remains the value used by the public storefront route `/:storePath`.
- Publish review/edit links remain inside `/configurar-loja/*` until publication completes.

- [ ] **Step 1: Verify the publish URL contract in existing code/tests**

Confirm that the store slug is normalized from the store name, persisted through `CreateStoreRequest`, and that the public route resolves `/:storePath` rather than `/configurar-loja` or `/app`.

- [ ] **Step 2: Add a regression assertion if the contract is not covered**

Assert the final public URL shape as `https://www.urbeat.com.br/<slug>` using the existing environment/base URL conventions, without hardcoding a new API base path.

- [ ] **Step 3: Run the full focused verification**

```bash
npx jest --no-coverage src/app/app.routes.spec.ts src/app/features/store-config/store-config-page.component.spec.ts
npx ng build --configuration production
node .opencode/skills/impeccable/scripts/detect.mjs --json frontend/src/app/features/store-config frontend/src/theme/global.scss
```

Expected: all focused tests pass, production build succeeds, and the detector reports no new blocking findings.
