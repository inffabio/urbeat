# Wizard Products Header Spacing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the product wizard content consistently below the sticky progress header with a small gap on every viewport.

**Architecture:** Adjust only the shared product-page stylesheet used by the wizard and dashboard, scoping the extra spacing to `.wizard-content:not(.is-dashboard)`. Preserve the existing dashboard layout and align the wizard with the `container` spacing used by hours and delivery.

**Tech Stack:** Angular 20, Ionic, SCSS, Jest.

## Global Constraints

- The wizard content must never render beneath the progress header.
- Keep a small, consistent gap below the header on desktop and mobile.
- Do not change `WizardHeaderComponent` or other wizard pages.
- Do not alter dashboard product spacing.
- Preserve existing uncommitted changes outside this task.

---

### Task 1: Fix wizard product spacing

**Files:**
- Modify: `frontend/src/app/shared/styles/store-products.shared.scss`
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

- [ ] **Step 1: Add a regression assertion** that the product layout has wizard-only top spacing and that the mobile rule does not reset it to zero.
- [ ] **Step 2: Run the focused spec and confirm the assertion fails.**
- [ ] **Step 3: Update the stylesheet** so `.wizard-content:not(.is-dashboard) .products-layout` retains a top offset consistent with other wizard `.container` layouts, including the mobile breakpoint; leave dashboard rules unchanged.
- [ ] **Step 4: Run the focused spec and production build.**
- [ ] **Step 5: Run the Impeccable detector on the changed stylesheet/template and `git diff --check`.
