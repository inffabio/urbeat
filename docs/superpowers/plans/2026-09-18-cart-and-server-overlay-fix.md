# Cart And Server Overlay Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven development to implement this plan task-by-task.

**Goal:** Remove the spinner panel background and make cart removal and checkout placement reliable across Ionic mobile and desktop layouts.

**Architecture:** Keep the full-screen server overlay as the interaction barrier while making its visual panel transparent. Normalize cart item identity at the service boundary and use item objects for UI removal. Move the cart checkout action into `StoreShellComponent`, with a small root-scoped state/event service owned by the cart page so the action is structurally adjacent to the footer but business validation remains in the cart page.

**Tech Stack:** Angular 20 standalone components, Ionic 8, TypeScript signals, RxJS, Jest.

## Global Constraints

- Preserve strict TypeScript and Angular template checking.
- Preserve mobile/PWA/Capacitor behavior and safe-area handling.
- Keep product UI tokens from `frontend/src/theme/variables.scss`.
- Keep all interactive targets at least 44px.
- Keep the overlay inaccessible state active until all tracked HTTP requests finish, including errors.
- Exclude only the local print-agent origin from the server overlay.

### Task 1: Transparent Server Overlay

**Files:**
- Modify: `frontend/src/app/shared/components/server-access-overlay/server-access-overlay.component.ts`
- Test: `frontend/src/app/shared/components/server-access-overlay/server-access-overlay.component.spec.ts`

- [ ] Write a failing test asserting the panel has no opaque surface background while the overlay retains a full-viewport pointer-blocking backdrop.
- [ ] Run `npx jest --no-coverage src/app/shared/components/server-access-overlay/server-access-overlay.component.spec.ts` and confirm the new assertion fails against `background: var(--app-surface, #fff)`.
- [ ] Remove the panel background and shadow only; leave `.server-access-overlay` fixed, `inset: 0`, `touch-action: none`, and its backdrop color intact.
- [ ] Run the focused overlay test and confirm it passes.

### Task 2: Stable Cart Item Removal

**Files:**
- Modify: `frontend/src/app/core/services/cart.service.ts`
- Modify: `frontend/src/app/features/cart/cart-page.component.html`
- Modify: `frontend/src/app/features/cart/cart-page.component.ts`
- Modify: `frontend/src/app/shared/components/cart-sheet/cart-sheet.component.ts`
- Test: `frontend/src/app/core/services/cart.service.spec.ts`
- Test: `frontend/src/app/features/cart/cart-page.component.spec.ts`
- Test: `frontend/src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts`

- [ ] Add a failing service test that loads a persisted item without `id`, then verifies the loaded item receives a stable generated ID and can be removed.
- [ ] Run the three focused cart test files and confirm the legacy-item test fails because `load()` currently preserves an undefined ID.
- [ ] Normalize persisted items in `CartService.load()` with a deterministic per-entry frontend ID when absent, without changing existing IDs.
- [ ] Change cart-page item removal to pass the `CartItem` object instead of `item.id!`, and keep the service's object-removal path as the authoritative fallback.
- [ ] Add a DOM click regression test for the cart-page remove control and retain the cart-sheet remove test against the same service behavior.
- [ ] Run the focused cart tests and confirm all pass.

### Task 3: Shell-Owned Cart Checkout Action

**Files:**
- Create: `frontend/src/app/core/services/cart-checkout-action.service.ts`
- Modify: `frontend/src/app/features/cart/cart-page.component.ts`
- Modify: `frontend/src/app/features/cart/cart-page.component.html`
- Modify: `frontend/src/app/features/store/store-shell.component.ts`
- Modify: `frontend/src/app/features/store/store-shell.component.spec.ts`
- Test: `frontend/src/app/core/services/cart-checkout-action.service.spec.ts`

- [ ] Add service tests first for publishing visibility/disabled state and forwarding a continue request.
- [ ] Run the service test and confirm it fails because the service does not exist.
- [ ] Implement a root-scoped signal state with `visible`, `disabled`, `detail`, `setState()`, `reset()`, and a `requestContinue()` event stream.
- [ ] In `CartPageComponent`, publish `visible = !cart.isEmpty()` and `disabled = belowMinimum() || !storeOpen()` from a signal effect, subscribe to continue requests, and route the request through the existing `continueCheckout()` method. Reset state and unsubscribe on destroy.
- [ ] Remove the route-local `app-sticky-action-bar` from `cart-page.component.html`.
- [ ] Render `app-sticky-action-bar` in `StoreShellComponent` directly between the routed content and `app-footer-nav`, only for the cart route and visible state; bind its disabled state and emit `requestContinue()`.
- [ ] Add tests proving the shell places the action before the footer, hides it for non-cart routes/empty carts, and preserves the disabled condition.
- [ ] Run the focused shell/cart/action tests and confirm all pass.

### Task 4: Full Verification

**Files:**
- No additional production files.

- [ ] Run `npx jest --no-coverage` from `frontend/`.
- [ ] Run `npx ng build --configuration production` from `frontend/`.
- [ ] Run `git diff --check` and review only intended files.
- [ ] Run an independent code review focused on overlay pointer blocking, cart identity migration, Ionic shell placement, and stale subscriptions.
- [ ] Commit the implementation with `fix(frontend): stabilize cart actions and transparent loader`.
