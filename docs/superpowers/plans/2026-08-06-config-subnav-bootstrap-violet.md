# Config Subnav Bootstrap Violet Implementation Plan

**Goal:** Rework the seller Configurações submenu to use Bootstrap navigation classes and the dashboard's violet visual language.

**Architecture:** Keep `ConfigSubnavComponent` standalone and preserve all existing routes and active-link behavior. Replace its local pill CSS with Bootstrap `nav`/`nav-pills` markup plus a small scoped wrapper for overflow and the existing dashboard tokens.

**Tech Stack:** Angular 20 standalone components, Ionic icons, Bootstrap 5.3.3, Jest.

## Global Constraints

- Preserve the five existing configuration routes and labels.
- Use existing dashboard tokens `--dash-primary`, `--dash-primary-strong`, `--dash-primary-soft`, and `--dash-line`.
- Keep touch targets at least 44px and support horizontal scrolling on narrow screens.
- Do not change unrelated dashboard buttons or routes.

---

### Task 1: Update ConfigSubnav Bootstrap Markup and Styling

**Files:**
- Modify: `frontend/src/app/features/seller-shell/config-subnav.component.ts`

- [x] **Step 1: Add a focused component expectation for Bootstrap navigation classes and violet active state.**
- [x] **Step 2: Run the focused Jest test and confirm the new expectation fails before implementation.**
- [x] **Step 3: Change the template to use `nav nav-pills`, `nav-item`, and `nav-link`, keeping current `routerLink` and active-link options.**
- [x] **Step 4: Replace the local pill styling with scoped wrapper styles for Bootstrap-compatible spacing, active violet tokens, focus state, and mobile overflow.**
- [x] **Step 5: Run the focused Jest test and confirm it passes.**

### Task 2: Validate Frontend

**Files:**
- Test: `frontend/src/app/features/seller-shell/config-subnav.component.spec.ts`

- [x] **Step 1: Run the focused ConfigSubnav test suite.**
- [x] **Step 2: Run the production Angular build and confirm completion, recording any pre-existing budget warnings.**
