# Acompanhamento de Pedidos do Cliente Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir acompanhar múltiplos pedidos do cliente em tempo real, com acesso pelo footer, lista de pedidos e detalhes com barra horizontal de status.

**Architecture:** Criar um serviço global `CustomerOrderTrackingService` como fonte única dos pedidos acompanhados, IDs persistidos no `localStorage`, detalhes carregados pela API e eventos filtrados pelo Customer Hub. O `StoreShellComponent` consumirá sinais do serviço para renderizar o footer; uma nova página `/pedidos` listará pedidos ativos e reutilizará `/pedido/:orderId` para os detalhes.

**Tech Stack:** Angular 20 standalone, Ionic 8, TypeScript strict, RxJS, `@microsoft/signalr`, Jest.

## Global Constraints

- Não confiar no cliente para status, preços ou totais; usar dados da API.
- Usar `America/Sao_Paulo` e os helpers existentes para datas.
- Manter alvos de toque de pelo menos 44px e comportamento mobile/PWA.
- Não adicionar bibliotecas novas.
- O status deve seguir `Recebido`, `Preparando`, `Pronto`, `Saiu para entregar`, `Entregue`.
- Pedidos de retirada não devem mostrar etapa de entrega.

---

### Task 1: Serviço compartilhado de acompanhamento

**Files:**
- Create: `frontend/src/app/core/services/customer-order-tracking.service.ts`
- Test: `frontend/src/app/core/services/customer-order-tracking.service.spec.ts`
- Modify: `frontend/src/app/core/services/signalr.service.ts`
- Test: `frontend/src/app/core/services/signalr.service.spec.ts`

**Interfaces:**
- Consumes: `OrderService.getOrder`, `SignalRService.startCustomerHub`, `SignalRService.onCustomerEvent` e `OrderStatusUpdated`.
- Produces: `activeOrders`, `trackedOrders`, `trackOrder(orderId: string)`, `untrackOrder(orderId: string)`, `refresh(orderId: string)`, `start()` e `stop()`.

- [ ] **Step 1: Write the failing tests**

Adicionar testes que verifiquem:

```typescript
it('restores tracked order ids and loads their details', () => {});
it('adds a paid order only once', () => {});
it('updates the matching order when OrderStatusUpdated arrives', () => {});
it('ignores events for orders that are not tracked', () => {});
it('removes delivered and cancelled orders from activeOrders but keeps them tracked', () => {});
it('falls back to refresh polling when the customer hub cannot start', () => {});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx jest --no-coverage src/app/core/services/customer-order-tracking.service.spec.ts`

Expected: FAIL because the service and its public signals do not exist.

- [ ] **Step 3: Implement the minimal service and listener lifecycle**

Implement a root service with:

```typescript
readonly trackedOrders = signal<OrderDetails[]>([]);
readonly activeOrders = computed(() => this.trackedOrders().filter((order) =>
  order.status !== OrderStatus.Delivered && order.status !== OrderStatus.Cancelled));

trackOrder(orderId: string): void;
untrackOrder(orderId: string): void;
refresh(orderId: string): void;
start(): Promise<void>;
stop(): void;
```

Persist only order IDs under a namespaced storage key. Register `OrderStatusUpdated` before starting the hub, filter by tracked IDs, use per-order request sequence numbers, and poll active orders when hub startup fails. Treat storage/API failures as recoverable and never expose secrets.

- [ ] **Step 4: Extend SignalR listener registration safely**

Add a customer listener registration path that can be installed before `startCustomerHub()` without warning or losing the callback. Ensure callbacks are attached to the eventual connection and removed by `removeCustomerListener`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `npx jest --no-coverage src/app/core/services/customer-order-tracking.service.spec.ts src/app/core/services/signalr.service.spec.ts`

Expected: all tests PASS.

---

### Task 2: Integrar pagamento e tela de acompanhamento

**Files:**
- Modify: `frontend/src/app/features/payment/online/online-payment-page.component.ts`
- Test: `frontend/src/app/features/payment/online/online-payment-page.component.spec.ts`
- Modify: `frontend/src/app/features/payment/delivery/delivery-payment-page.component.ts`
- Test: `frontend/src/app/features/payment/delivery/delivery-payment-page.component.spec.ts`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.ts`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.html`
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.scss`
- Test: `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts`

**Interfaces:**
- Consumes: `CustomerOrderTrackingService.trackOrder` and its `trackedOrders`/refresh state.
- Produces: tracking route that renders a horizontal status bar followed by item details, options, quantities, subtotal, applicable freight and total.

- [ ] **Step 1: Write failing tests**

Add tests that assert the payment completion path calls `trackOrder(orderId)` before navigation, SignalR status updates replace the displayed order, and the template contains the horizontal status sequence and item/option/total data.

- [ ] **Step 2: Run focused tests and confirm RED**

Run: `npx jest --no-coverage src/app/features/payment/online/online-payment-page.component.spec.ts src/app/features/payment/delivery/delivery-payment-page.component.spec.ts src/app/features/order-tracking/tracking-page.component.spec.ts`

Expected: new assertions FAIL before implementation.

- [ ] **Step 3: Integrate tracking registration**

Inject the tracking service in both payment flows and register the order as soon as the API confirms payment/order receipt. Keep existing polling only for payment settlement; do not create duplicate tracking registrations.

- [ ] **Step 4: Use shared order state in tracking**

Load the route order immediately, subscribe to the shared service signal for that ID, and retain a guarded API refresh fallback. Do not stop the global Customer Hub when the details page is destroyed; only remove page-local effects. Add a sequence guard so stale API responses cannot overwrite SignalR data.

- [ ] **Step 5: Render the approved layout**

Replace the top tracking visualization with a responsive horizontal stepper. Render `Recebido → Preparando → Pronto → Saiu para entregar → Entregue`, omitting delivery-specific steps for pickup. Under it render each item separately with quantity, selected options and prices, then subtotal, freight only for delivery, and total. Keep accessible labels and 44px controls.

- [ ] **Step 6: Run focused tests**

Run the same focused Jest command and expect all tests PASS.

---

### Task 3: Footer e lista de pedidos ativos

**Files:**
- Create: `frontend/src/app/features/customer-orders/customer-orders-page.component.ts`
- Create: `frontend/src/app/features/customer-orders/customer-orders-page.component.html`
- Create: `frontend/src/app/features/customer-orders/customer-orders-page.component.scss`
- Create: `frontend/src/app/features/customer-orders/customer-orders-page.component.spec.ts`
- Modify: `frontend/src/app/features/store/store-shell.component.ts`
- Modify: `frontend/src/app/shared/components/footer-nav/footer-nav.component.ts`
- Modify: `frontend/src/app/app.routes.ts`
- Modify: `frontend/src/app/features/store/store-shell.component.spec.ts`
- Modify: `frontend/src/app/shared/components/footer-nav/footer-nav.component.spec.ts`

**Interfaces:**
- Consumes: `CustomerOrderTrackingService.activeOrders`, `trackedOrders` and `trackOrder`.
- Produces: enabled red footer item with count badge, `/pedidos` list and navigation to `/pedido/:orderId`.

- [ ] **Step 1: Write failing tests**

Cover:

```typescript
it('enables and marks Pedidos red when there is an active order', () => {});
it('shows the number of active orders in the badge', () => {});
it('keeps Pedidos disabled when there are no active orders', () => {});
it('lists multiple active orders and navigates to the selected order', () => {});
it('renders an empty state with a menu action', () => {});
```

- [ ] **Step 2: Run tests to confirm RED**

Run: `npx jest --no-coverage src/app/features/store/store-shell.component.spec.ts src/app/shared/components/footer-nav/footer-nav.component.spec.ts src/app/features/customer-orders/customer-orders-page.component.spec.ts`

Expected: new assertions FAIL because the footer is permanently disabled and the list route does not exist.

- [ ] **Step 3: Implement footer state and navigation**

Compute the `pedidos` item from `activeOrders().length`, use the existing brand red token, set `badge`, and route `onFooterSelect('pedidos')` to `/:storePath/pedidos`. Preserve current cart/account behavior and inert handling.

- [ ] **Step 4: Implement the customer order list page**

Render active orders ordered by most recent, with code, status, item count, total and accessible selection buttons. Include an empty state and a menu return action. Use existing currency/date helpers and no client-side recalculation of totals.

- [ ] **Step 5: Register the route and start tracking at store-shell scope**

Add `pedidos` before the dynamic store child route and start the tracking service after the store context resolves. Keep it scoped to the current store and avoid displaying orders from another store.

- [ ] **Step 6: Run focused tests**

Run the focused Jest command and expect all tests PASS.

---

### Task 4: Validação integrada

**Files:**
- Modify: `frontend/src/app/features/order-tracking/tracking-page.component.spec.ts` if integration assertions require fixture updates.

- [ ] **Step 1: Run the complete frontend suite**

Run: `npx jest --no-coverage`

Expected: all tests PASS.

- [ ] **Step 2: Run the production build**

Run: `npx ng build --configuration production`

Expected: build succeeds; record only the known initial bundle budget warning if it remains.

- [ ] **Step 3: Run diff validation**

Run: `git diff --check`

Expected: no whitespace errors.

- [ ] **Step 4: Manually verify the critical flow**

With the API running, create/pay an order, confirm the route reaches tracking, change its status from the seller side, verify the horizontal bar advances without refresh, confirm footer `Pedidos` turns red with the active count, open the list, and select each active order.
