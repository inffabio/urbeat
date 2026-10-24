# Compact Order List And Simple Codes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compact accepted seller orders into one descending accordion list and generate new order codes without the `URB-` prefix while preserving legacy codes.

**Architecture:** Keep the existing `Received` new-order panel and move only accepted statuses into a single computed list in `SellerOrdersPageComponent`. Reuse the existing order-card detail/action content inside an accessible accordion header/body, shared by `/app/pedidos` and `/app/pedidos-separados`. Change only the backend checkout code generator; persisted legacy codes remain unchanged.

**Tech Stack:** Angular 20 standalone components, Signals, modern control flow, SCSS, .NET 9, EF Core, PostgreSQL, Jest, xUnit/FluentAssertions.

## Global Constraints

- New codes are eight uppercase alphanumeric characters with no `URB-` prefix.
- Existing persisted codes are not migrated or rewritten.
- Preserve global uniqueness checks and all checkout, SignalR, notification, printing, and API contracts.
- Keep `Received` orders in the highlighted acceptance area.
- Accepted statuses are `Preparing`, `Ready`, `OnDelivery`, and `Delivered`; exclude `sellerCompletedAtUtc` orders.
- Sort accepted orders by `createdAtUtc` descending using the existing UTC timestamps.
- Preserve status transition actions, details modal, automatic printing, detached-window behavior, accessibility, and 44px touch targets.
- Do not add dependencies or change unrelated backend/frontend features.

---

### Task 1: Change new order code generation without altering legacy orders

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs:376-402`
- Modify: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/CheckoutServiceTests.cs:70-85`
- Inspect only: `backend/src/Urbeat.Infrastructure/Persistence/SystemParameterSeeder.cs` to confirm the legacy prefix parameter is not used by checkout generation

**Interfaces:**
- Consumes: existing `GenerateOrderCodeAsync(CancellationToken)` uniqueness query against `Orders.Code`.
- Produces: checkout confirmation and persisted `Order.Code` matching `^[A-Z0-9]{8}$` for new orders.

- [ ] **Step 1: Update the failing checkout assertion**

  Change the existing regex assertion from `^URB-[A-Z0-9]{8}$` to `^[A-Z0-9]{8}$`. Add an assertion that the generated code does not start with `URB-` and remains exactly eight characters.

- [ ] **Step 2: Run the focused test to verify the current implementation fails**

  From the repository root run:

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CheckoutServiceTests"
  ```

  Expected before implementation: the existing `URB-` generator fails the new regex.

- [ ] **Step 3: Remove the prefix from the generator**

  Keep the random alphabet and length unchanged, but return the generated characters directly:

  ```csharp
  private static string GenerateRandomCode()
  {
      var chars = new char[CodeLength];
      var data = RandomNumberGenerator.GetBytes(CodeLength);
      for (var i = 0; i < CodeLength; i++)
      {
          chars[i] = CodeChars[data[i] % CodeChars.Length];
      }
      return new string(chars);
  }
  ```

  Do not update old database rows, create a migration, or remove unrelated configuration seed data unless compilation/tests prove it is actively used by this generator.

- [ ] **Step 4: Run backend focused tests**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CheckoutServiceTests"
  ```

  Expected: PASS, including uniqueness and checkout response behavior.

### Task 2: Build the compact accepted-order view model

**Files:**
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Test: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`

**Interfaces:**
- Consumes: existing `orders`, `visibleOrders`, `statusGroups`, `OrderSummary`, and `OrderStatus` signals.
- Produces: a computed accepted-order list, expanded-state signal, and a display formatter that preserves legacy `URB-` values while returning new codes unchanged.

- [ ] **Step 1: Add failing tests for accepted filtering and ordering**

  Add a fixture with one `Received`, two accepted orders at different `createdAtUtc` values, and one `sellerCompletedAtUtc` order. Assert that the new computed/list rendering contains only accepted non-completed orders and orders the newer timestamp first. Assert that `Received` remains in the new-orders panel.

- [ ] **Step 2: Add failing tests for code presentation**

  Assert that `displayOrderCode({ code: 'A7K92PQL' })` returns `A7K92PQL` and `displayOrderCode({ code: 'URB-ABC12345' })` returns `URB-ABC12345`. This ensures legacy codes remain recognizable and the UI does not blindly strip arbitrary text.

- [ ] **Step 3: Implement the computed list and expansion state**

  Add signals/computed values equivalent to:

  ```ts
  readonly expandedOrderIds = signal<ReadonlySet<string>>(new Set());

  readonly acceptedOrders = computed(() =>
    this.visibleOrders()
      .filter((order) => order.status !== OrderStatus.Received)
      .sort((left, right) => this.orderTimestamp(right) - this.orderTimestamp(left)),
  );
  ```

  Use a copied array before sorting so no source signal array is mutated. Keep status filtering explicit for the four accepted statuses and ignore malformed/empty timestamps consistently with existing date handling.

- [ ] **Step 4: Implement accordion helpers**

  Add methods with these behaviors:

  ```ts
  isOrderExpanded(orderId: string): boolean;
  toggleOrderExpanded(orderId: string): void;
  displayOrderCode(order: Pick<OrderSummary, 'code'>): string;
  ```

  `toggleOrderExpanded` must immutably clone the `Set`. `displayOrderCode` returns the persisted code unchanged because new codes are generated without a prefix and old codes must retain theirs.

- [ ] **Step 5: Run the component tests**

  ```powershell
  npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts
  ```

  Expected: the new computed/list and formatting tests pass after implementation.

### Task 3: Replace accepted status columns with accessible accordions

**Files:**
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.html`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.scss`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`

**Interfaces:**
- Consumes: `acceptedOrders`, `isOrderExpanded`, `toggleOrderExpanded`, `displayOrderCode`, `orderItems`, `cardAction`, and existing order action/detail methods.
- Produces: one vertical compact list shared by the dashboard and detached route.

- [ ] **Step 1: Add failing template tests for accordion semantics**

  Assert that the accepted area renders one vertical accordion item per accepted order, that each header is a `button` with `aria-expanded="false"` initially and an `aria-controls` id, and that clicking it changes `aria-expanded` to `true` and reveals the details/action content. Assert the header includes the displayed code and customer name and a chevron icon.

- [ ] **Step 2: Replace the status grid markup**

  Keep the `new-orders-panel` unchanged except for any necessary code display call. Replace the `status-flow`/`status-grid` loop with:

  ```html
  <section class="accepted-orders-panel" aria-labelledby="accepted-orders-title">
    <h2 id="accepted-orders-title" class="panel-title">Pedidos aceitos</h2>
    @if (acceptedOrders().length === 0) {
      <p class="muted">Nenhum pedido aceito no momento.</p>
    } @else {
      <div class="accepted-orders-list">
        @for (order of acceptedOrders(); track order.id) {
          <article class="order-accordion" [class.is-expanded]="isOrderExpanded(order.id)">
            <button type="button" class="order-accordion-header"
              [attr.aria-expanded]="isOrderExpanded(order.id)"
              [attr.aria-controls]="'order-panel-' + order.id"
              (click)="toggleOrderExpanded(order.id)">
              <span class="order-accordion-title">
                <strong>#{{ displayOrderCode(order) }}</strong>
                <span>{{ order.customerName || 'Cliente não informado' }}</span>
              </span>
              <i class="bi" [class.bi-chevron-down]="!isOrderExpanded(order.id)" [class.bi-chevron-up]="isOrderExpanded(order.id)" aria-hidden="true"></i>
            </button>
            @if (isOrderExpanded(order.id)) {
              <div class="order-accordion-panel" [id]="'order-panel-' + order.id">
                <ng-container *ngTemplateOutlet="boardCard; context: { $implicit: order, items: orderItems(order.id), action: cardAction(order) }"></ng-container>
              </div>
            }
          </article>
        }
      </div>
    }
  </section>
  ```

  Reuse the existing `boardCard` template for details/actions rather than duplicating business behavior. Ensure nested code/detail buttons do not trigger the accordion header because they are inside the panel, not the header.

- [ ] **Step 3: Add compact vertical styles**

  Replace the accepted grid styles with a full-width vertical list. The header must be compact, readable, keyboard-focusable, and have a minimum 44px interactive height. Use existing tokens/colors and style the expanded panel as a bordered continuation. Preserve mobile responsiveness and avoid fixed heights that clip item details.

- [ ] **Step 4: Update existing tests that assume status columns**

  Adjust selectors/assertions from `.status-grid`, `.status-col`, and column-specific cards to the new `.accepted-orders-list`/`.order-accordion` structure while preserving coverage for status action labels, details opening, printing, delivery completion, and detached host suppression.

- [ ] **Step 5: Run seller-orders tests**

  ```powershell
  npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts src/app/core/services/seller-orders-window.service.spec.ts
  ```

  Expected: PASS, including dashboard and detached-window behavior.

### Task 4: Full validation and review

**Files:**
- No additional files expected.

- [ ] **Step 1: Run backend unit tests**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests
  ```

- [ ] **Step 2: Run affected frontend tests**

  ```powershell
  npx jest --no-coverage src/app/features/seller-orders src/app/app.routes.spec.ts
  ```

- [ ] **Step 3: Build frontend production bundle**

  ```powershell
  npx ng build --configuration production
  ```

  Existing bundle budget warnings may remain; do not weaken or change budgets for this feature.

- [ ] **Step 4: Review compatibility and diff**

  Verify no migration modifies existing codes, no frontend formatter strips `URB-` from legacy records, all API/SignalR/printing payloads still use `Order.Code`, and `git diff --check` passes.

- [ ] **Step 5: Commit the implementation**

  ```powershell
  git add backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs backend/tests/Urbeat.UnitTests/Infrastructure/Services/CheckoutServiceTests.cs frontend/src/app/features/seller-orders
  git commit -m "feat: compacta pedidos aceitos e simplifica codigos"
  ```

## Self-review

- Backend format and legacy compatibility are covered by Task 1.
- Filtering, descending UTC ordering, expansion state, and code display are covered by Task 2.
- Accordion semantics, customer title, chevrons, compact layout, actions, and detached reuse are covered by Task 3.
- Full backend/frontend validation and compatibility review are covered by Task 4.
