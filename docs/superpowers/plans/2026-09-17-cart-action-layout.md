# Cart Action Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the cart checkout action visible above the mobile footer regardless of item count and give the cart more vertical viewport space.

**Architecture:** The cart will use the shared action bar in its existing fixed mode, positioned above the measured storefront footer clearance. The cart scrollport will reserve the action bar height plus footer clearance, preventing overlap. The cart shell will use the available viewport height rather than changing its desktop width.

**Tech Stack:** Angular 20 standalone components, Ionic 8, SCSS, Jest.

## Global Constraints

- Preserve strict TypeScript and Angular template checking.
- Preserve the shared fixed action-bar behavior used by checkout pages.
- Keep mobile/PWA/Capacitor safe-area behavior and 44px-plus touch targets.
- Use existing `var(--app-*)` tokens and `--store-footer-clearance`.
- Do not modify unrelated worktree changes or deployment credentials.

---

### Task 1: Lock the cart layout contract with tests

**Files:**
- Modify: `frontend/src/app/features/cart/cart-page.component.spec.ts`

**Interfaces:**
- The cart template must render `app-sticky-action-bar` without `placement="inline"`.
- The cart styles must reserve `--store-footer-clearance` plus action-bar height in `.cart-content`.
- The cart host must preserve its current desktop width and use the available vertical viewport.

- [ ] **Step 1: Add failing assertions**

Add expectations that the cart action bar has no inline placement attribute, `.cart-content` reserves fixed-action space, and the host width is expanded.

- [ ] **Step 2: Run the focused cart suite**

Run: `npx jest --no-coverage src/app/features/cart/cart-page.component.spec.ts`

Expected: FAIL because the current template still uses inline placement and the current styles do not reserve the fixed action-bar space.

### Task 2: Restore fixed cart action and spacing

**Files:**
- Modify: `frontend/src/app/features/cart/cart-page.component.html`
- Modify: `frontend/src/app/features/cart/cart-page.component.scss`

**Interfaces:**
- `app-sticky-action-bar` uses its default fixed placement.
- `.cart-content` receives bottom padding calculated from footer clearance, safe area, and the 48px action bar.
- `:host` uses a wider desktop max width without changing mobile width.

- [ ] **Step 1: Remove the cart-only inline placement**

Remove `placement="inline"` from the cart action-bar element and leave its disabled state and action binding unchanged.

- [ ] **Step 2: Reserve fixed action-bar space**

Set the cart scrollport bottom padding to:

```scss
padding-bottom: calc(var(--store-footer-clearance, calc(64px + max(8px, env(safe-area-inset-bottom, 0px))) + 48px + 16px));
```

Keep the existing `screen-padding` spacing for the content itself.

- [ ] **Step 3: Expand vertical cart space**

Keep the desktop `:host` max width at `620px`. Ensure the cart shell and `ion-content` can use the available viewport height, without a fixed height that would grow with the item list or reduce the scrollable area.

- [ ] **Step 4: Run the focused suite**

Run: `npx jest --no-coverage src/app/features/cart/cart-page.component.spec.ts`

Expected: PASS.

### Task 3: Verify the frontend surface

**Files:**
- No additional files.

- [ ] **Step 1: Run all frontend tests**

Run: `npx jest --no-coverage`

Expected: PASS with no failed suites.

- [ ] **Step 2: Build production frontend**

Run: `npx ng build --configuration production`

Expected: successful production build; report any pre-existing budget warnings without weakening budgets.

- [ ] **Step 3: Review the final diff**

Run: `git diff --check` and inspect only the intended cart files.

- [ ] **Step 4: Commit the implementation**

```bash
git add frontend/src/app/features/cart/cart-page.component.html frontend/src/app/features/cart/cart-page.component.scss frontend/src/app/features/cart/cart-page.component.spec.ts
git commit -m "fix: keep cart checkout action visible"
```
