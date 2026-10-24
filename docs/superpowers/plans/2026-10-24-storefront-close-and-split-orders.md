# Storefront Close And Split Orders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve customer order completion and provide a dedicated second-monitor orders window for sellers.

**Architecture:** Keep the customer changes local to the tracking/storefront feature: the delivered modal clears `CartService`, `CustomerOrderTrackingService`, and temporary checkout state without touching `AuthService` or backend history. Add a root Angular split-window state service using `BroadcastChannel` plus `storage` fallback; reuse the existing seller orders component on a dedicated `/app/pedidos-separados` route and make only the dashboard instance suppress its order board while detached mode is active.

**Tech Stack:** Angular 20 standalone components, Ionic 8, Signals, Angular Router, browser `BroadcastChannel`/`window.open`, Jest.

## Global Constraints

- Implement this scope in the frontend only; do not delete or alter backend order history or customer accounts.
- Preserve SignalR real-time delivery and existing duplicate/reconnect behavior.
- The detached window must be a browser window that can be moved using the native title bar before fullscreen; an HTML bar cannot move a browser window.
- Use existing Angular services, `inject()`, Signals, standalone components, modern control flow, and current design tokens.
- Keep seller controls accessible while the original order board is visually disabled/gray.
- Do not introduce a new UI library, dependency, or persistent credential/state format.

---

### Task 1: Add a storefront reset operation after delivery

**Files:**
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.ts`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.html`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.scss`
- Modify: `frontend/src/app/core/services/cart-checkout-action.service.ts` only if reset orchestration needs a public idempotent reset
- Test: `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts`
- Test: `frontend/src/app/core/services/customer-order-tracking.service.spec.ts` if reset behavior is extended

**Interfaces:**
- Consumes: `CartService.clearAll()`, `CustomerOrderTrackingService.reset()`, `CartCheckoutActionService.reset()`, existing `goToMenu()` and `delivered()` signals.
- Produces: a delivered-order celebration modal and a single `finishDeliveredOrder()` action that clears local storefront state, preserves authentication, and navigates to the store menu.

- [ ] **Step 1: Add failing tests for delivered modal visibility and local reset**

  Extend the tracking component test setup with mocks/spies for `CartService.clearAll`, `CustomerOrderTrackingService.reset`, and `CartCheckoutActionService.reset`. Assert that a delivered order renders a dialog containing `Bom apetite!`, that the dialog action invokes all three local resets and navigates to the store path, and that the auth service is not called. Assert that a non-delivered order does not render the dialog.

- [ ] **Step 2: Add the modal state and transition guard**

  Add signals equivalent to:

  ```ts
  readonly deliveredCelebrationOpen = signal(false);
  private celebratedOrderId: string | null = null;
  ```

  Use an effect or the existing tracking update lifecycle to open the modal once when `order()?.status === OrderStatus.Delivered` and the current order ID differs from `celebratedOrderId`. Do not reopen it for repeated SignalR/detail refreshes of the same order. Reset the guard when the route changes to another order.

- [ ] **Step 3: Implement the finish action without logout**

  Inject `CartService` and `CartCheckoutActionService`. Implement:

  ```ts
  finishDeliveredOrder(): void {
    this.deliveredCelebrationOpen.set(false);
    this.tracking.reset();
    this.cart.clearAll();
    this.cartCheckoutAction.reset();
    this.goToMenu();
  }
  ```

  Keep `AuthService` untouched. If the existing store shell owns a cart sheet/SignalR lifecycle, use its public reset path rather than stopping the customer session globally.

- [ ] **Step 4: Replace duplicate navigation controls**

  In `tracking-page.component.html`, change the primary button at the bottom from `ACOMPANHAR PEDIDO`/`refresh()` to `VOLTAR AO CARDÁPIO`/`goToMenu()`. Remove the `<app-back-to-menu-link>` below it. Keep the footer navigation unchanged.

- [ ] **Step 5: Add the accessible modal markup and styles**

  Render a modal only while `deliveredCelebrationOpen()` is true:

  ```html
  @if (deliveredCelebrationOpen()) {
    <div class="delivered-celebration-overlay" role="presentation">
      <section class="delivered-celebration-modal" role="dialog" aria-modal="true" aria-labelledby="delivered-celebration-title">
        <h2 id="delivered-celebration-title">Bom apetite!</h2>
        <p>Obrigado pelo pedido. Quando quiser, volte ao cardápio.</p>
        <button type="button" (click)="finishDeliveredOrder()">Voltar ao cardápio</button>
      </section>
    </div>
  }
  ```

  Use existing `--app-*` tokens, ensure the action is at least 44px high, and do not make the overlay dismissible by clicking outside; the explicit action is the state-clearing boundary.

- [ ] **Step 6: Run focused storefront tests**

  Run from `frontend/`:

  ```powershell
  npx jest --no-coverage src/app/features/order-tracking/tracking-page.component.spec.ts src/app/core/services/customer-order-tracking.service.spec.ts
  ```

  Expected: PASS, including existing delivered timeline/confirmation tests and the new modal/reset assertions.

### Task 2: Add detached orders window state and route

**Files:**
- Create: `frontend/src/app/core/services/seller-orders-window.service.ts`
- Modify: `frontend/src/app/app.routes.ts`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.html`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.scss`
- Test: `frontend/src/app/core/services/seller-orders-window.service.spec.ts`
- Test: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`
- Test: `frontend/src/app/app.routes.spec.ts`

**Interfaces:**
- Consumes: `window.open`, the current seller orders route/component, `SignalRService`, and `SellerShellFacade.newOrderPulse()`.
- Produces: `SellerOrdersWindowService.isDetached`, `isDetachedRoute`, `openDetachedWindow()`, `closeDetachedWindow()`, and `restoreFromClosedSecondary()` state/events.

- [ ] **Step 1: Add failing service tests for cross-window protocol**

  Mock `BroadcastChannel`, `window.open`, `window.addEventListener('storage', ...)`, and timers. Cover:

  - `openDetachedWindow()` returns false and leaves state normal when popup is blocked;
  - successful open publishes `{ type: 'detached-open' }` and marks the main instance detached;
  - a `detached-ready` message receives the current mode;
  - `fixar ao Dashboard` publishes `{ type: 'detached-close' }` and clears state;
  - a closed `Window` is detected by the monitor and restores normal mode;
  - `storage` fallback works when `BroadcastChannel` is unavailable.

- [ ] **Step 2: Implement the service protocol**

  Use a namespaced channel/key such as `urbeat-seller-orders-window`. The service must expose Signals:

  ```ts
  readonly isDetached = signal(false);
  readonly isDetachedRoute = signal(false);
  readonly secondaryWindowOpen = signal(false);
  ```

  `openDetachedWindow()` must call `window.open('/app/pedidos-separados', 'urbeat-orders-window', 'popup=yes,width=1400,height=900')` directly from the button click. If it returns `null`, do not publish detached mode. If it succeeds, publish the open message and monitor `windowRef.closed` without preventing the user from moving the window using its native title bar.

  `closeDetachedWindow()` must publish the close message, close the secondary window when accessible, and restore the main state. BroadcastChannel/listener/timer cleanup belongs in `ngOnDestroy` or an explicit `dispose()`.

- [ ] **Step 3: Add the dedicated route**

  Add a child route under `/app` before/alongside `pedidos`:

  ```ts
  {
    path: 'pedidos-separados',
    loadComponent: () => import('./features/seller-orders/seller-orders-page.component')
      .then((m) => m.SellerOrdersPageComponent),
    data: { detachedOrdersWindow: true },
  }
  ```

  The component must detect route data through `ActivatedRoute` and register itself as the secondary instance. Do not create a second orders implementation; the same component/template must render both modes.

- [ ] **Step 4: Add the top controls and detached-mode visual state**

  Next to the existing `Tela cheia` button, render:

  ```html
  <button type="button" class="btn-soft" (click)="toggleOrdersWindow()">
    {{ isDetachedRoute() ? 'Fixar ao Dashboard' : 'Separar tela pedidos' }}
  </button>
  ```

  In the main `/app/pedidos` instance, apply a class such as `.orders-page.is-detached-host` to the order board only. Keep the header, top buttons, period controls, refresh, navigation and accessibility controls active. The board and new-order/status columns must be visually gray and must not render new order cards while `isDetached` is true. The dedicated route keeps the live board active.

- [ ] **Step 5: Gate SignalR-driven refreshes in the main instance**

  In the existing `newOrderPulse` effect and `OrderStatusUpdated` listener, skip `load({ silent: true })` when the component is the detached host. Continue loading normally when `isDetachedRoute()` is true. On transition back to normal, invoke one authoritative `load({ silent: true })` so orders received while detached are restored.

- [ ] **Step 6: Handle fullscreen without claiming drag support**

  Keep the existing `Tela cheia` button and `document.fullscreenElement` behavior. The detached route may enter fullscreen after the user has moved the browser window to the desired monitor; do not attempt to implement fake draggable HTML window chrome. If fullscreen fails, retain the separate route/window and expose the existing accessible controls.

- [ ] **Step 7: Run focused split-window tests**

  Run from `frontend/`:

  ```powershell
  npx jest --no-coverage src/app/core/services/seller-orders-window.service.spec.ts src/app/features/seller-orders/seller-orders-page.component.spec.ts src/app/app.routes.spec.ts
  ```

  Expected: PASS, including existing fullscreen, SignalR pulse, order action, and routing tests.

### Task 3: Full frontend validation and review

**Files:**
- No additional files expected beyond Tasks 1-2.

- [ ] **Step 1: Run all affected feature tests**

  ```powershell
  npx jest --no-coverage src/app/features/order-tracking src/app/core/services/customer-order-tracking.service.spec.ts src/app/core/services/cart.service.spec.ts src/app/core/services/cart-checkout-action.service.spec.ts src/app/features/seller-orders src/app/app.routes.spec.ts
  ```

  Expected: all affected suites pass with no regressions in delivered timeline, cart persistence, fullscreen, SignalR, or seller order actions.

- [ ] **Step 2: Run the production build**

  ```powershell
  npx ng build --configuration production
  ```

  Expected: successful build. Existing bundle-budget warnings may remain but must not be hidden or changed as part of this feature.

- [ ] **Step 3: Review behavior boundaries**

  Confirm the diff does not touch backend data, auth/logout, order history deletion, or unrelated seller screens. Confirm the split-window service cleans all listeners/timers and that popup failure/secondary close returns to normal mode.

- [ ] **Step 4: Commit the implementation**

  ```powershell
  git add frontend/src/app/app.routes.ts frontend/src/app/core/services/seller-orders-window.service.ts frontend/src/app/core/services/seller-orders-window.service.spec.ts frontend/src/app/features/order-tracking frontend/src/app/features/seller-orders frontend/src/app/app.routes.spec.ts
  git commit -m "feat: encerra pedidos e separa tela de pedidos"
  ```

## Self-review

- Storefront button/link change and delivered reset modal are covered by Task 1.
- Dedicated `/app/pedidos-separados` route, same controls, second-monitor behavior, gray host board, and restoration are covered by Task 2.
- SignalR remains active only where the live board is active, with authoritative reload on restore.
- No backend or user/account deletion is planned.
- Popup/fullscreen browser limitations are explicit and tested.
