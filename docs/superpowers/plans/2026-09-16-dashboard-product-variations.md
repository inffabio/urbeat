# Dashboard Product Variations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the wizard's free-form product variation editor to the seller dashboard product form.

**Architecture:** Keep the existing shared `StoreProductsState`, `ProductVariation` model, validation, payload builder, and API unchanged. The dashboard template will render the same conditional variation controls already used by the wizard, and focused dashboard tests will verify creation/editing behavior and payloads.

**Tech Stack:** Angular 20 standalone components, Ionic 8, TypeScript strict mode, SCSS, Jest.

## Global Constraints

- Variations are free-form and are not limited to sizes.
- Reuse existing shared state methods and `store-products.shared.scss` styles.
- Preserve single-price, fixed-weight, variable-weight, option-group, and catalog behavior.
- Do not change the backend schema, endpoints, or persistence contract.
- Keep touch targets and responsive behavior consistent with the wizard.
- Preserve unrelated worktree changes.

---

### Task 1: Add Dashboard Variation Editor Coverage

**Files:**
- Modify: `frontend/src/app/features/seller-products/seller-products-page.component.spec.ts`
- Reference: `frontend/src/app/features/seller-products/seller-products-page.component.html`
- Reference: `frontend/src/app/shared/state/store-products.state.ts:778-963`

**Interfaces:**
- Consume existing `StoreProductsState` methods: `addSizeVariation`, `removeSizeVariation`, `setSizeDefault`, `reorderSizeVariations`, and `saveProduct`.
- Produce regression tests proving the dashboard exposes and saves free-form variations.

- [ ] **Step 1: Write failing dashboard tests**

Add tests that set `saleMode` to `size`, render the dashboard template, and assert the size variation editor contains name, description, price, default, active, remove, reorder, and add controls. Add a test that adds a variation named `Família`, assigns a price, and verifies the create payload includes `saleMode: 'size'` and the variation data.

- [ ] **Step 2: Run the focused dashboard suite**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/features/seller-products/seller-products-page.component.spec.ts
```

Expected: the new rendering assertion fails because the dashboard template has no variation editor.

### Task 2: Render the Shared Variation Controls

**Files:**
- Modify: `frontend/src/app/features/seller-products/seller-products-page.component.html` immediately after the sale-mode controls and before `Grupos de opções`.
- Reuse: `frontend/src/app/shared/styles/store-products.shared.scss`.

**Interfaces:**
- Consume the inherited signals and methods from `StoreProductsState` already used by the wizard.
- Produce the same variation editor behavior and DOM contract as `store-products-page.component.html`.

- [ ] **Step 1: Add the conditional size editor**

Render the existing dashboard-compatible equivalent of the wizard's `saleMode() === 'size'` section:

```html
@if (saleMode() === 'size') {
  <section class="block-section">
    <div class="section-row">
      <div>
        <h2 class="block-heading">Variações</h2>
        <p class="block-sub">Defina as opções e os preços deste produto.</p>
      </div>
      <button class="add-row-btn" (click)="addSizeVariation()">+ Adicionar variação</button>
    </div>
    <div class="prod-card variation-card">
      <div class="variation-header size-header">
        <span></span><span>Nome</span><span>Descrição</span><span>Preço</span><span>Padrão</span><span>Ativo</span><span></span>
      </div>
      <ion-reorder-group [disabled]="false" (ionItemReorder)="reorderSizeVariations($any($event))">
        @for (variation of sizeVariations(); track variation.uid) {
          <div class="variation-row size-row">
            <ion-reorder class="drag-handle"><ion-icon name="reorder-two-outline"></ion-icon></ion-reorder>
            <div class="v-cell"><span class="v-cell-label">Nome</span><input type="text" class="var-input" [ngModel]="variation.name" (ngModelChange)="variation.name = $event; markDirty()" /></div>
            <div class="v-cell"><span class="v-cell-label">Descrição</span><input type="text" class="var-input" [ngModel]="variation.description" (ngModelChange)="variation.description = $event; markDirty()" /></div>
            <div class="v-cell"><span class="v-cell-label">Preço</span><div class="money-wrap"><span class="money-prefix">R$</span><input type="text" class="money-input" [ngModel]="variation.price" (ngModelChange)="variation.price = maskMoney($event); markDirty()" /></div></div>
            <div class="v-cell"><span class="v-cell-label">Padrão</span><label class="default-radio"><input type="radio" name="dashboardSizeDefault" [checked]="variation.isDefault" (change)="setSizeDefault(variation.uid)" /></label></div>
            <div class="v-cell"><span class="v-cell-label">Ativo</span><label class="switch"><input type="checkbox" [ngModel]="variation.isActive" (ngModelChange)="variation.isActive = $event; markDirty()" /><span class="slider"></span></label></div>
            <div class="v-cell v-cell-action"><span class="v-cell-label">Excluir</span><button class="icon-btn" (click)="removeSizeVariation(variation.uid)">Remover</button></div>
          </div>
        } @empty {
          <div class="empty-state">Nenhuma variação cadastrada.</div>
        }
      </ion-reorder-group>
      <button class="add-row-btn block" (click)="addSizeVariation()">+ Adicionar variação</button>
    </div>
  </section>
}
```

Use the project's existing Portuguese copy and icon/button conventions when aligning the exact markup to the dashboard template.

- [ ] **Step 2: Run focused dashboard tests**

Run:

```bash
npx jest --no-coverage src/app/features/seller-products/seller-products-page.component.spec.ts
```

Expected: all dashboard tests, including the new variation tests, pass.

- [ ] **Step 3: Commit the implementation**

```bash
git add frontend/src/app/features/seller-products/seller-products-page.component.html frontend/src/app/features/seller-products/seller-products-page.component.spec.ts
git commit -m "feat: add product variations to seller dashboard"
```

### Task 3: Full Frontend Verification

**Files:**
- No additional files unless tests expose a regression in the touched dashboard template.

- [ ] **Step 1: Run related suites**

```bash
npx jest --no-coverage src/app/features/seller-products/seller-products-page.component.spec.ts src/app/features/store-config/products/store-products-page.component.spec.ts
```

Expected: all related tests pass.

- [ ] **Step 2: Run the full frontend suite**

```bash
npx jest --no-coverage
```

Expected: all frontend suites pass.

- [ ] **Step 3: Build production frontend**

```bash
npx ng build --configuration production
```

Expected: successful production build with no TypeScript or template errors.

- [ ] **Step 4: Inspect the final diff and push**

```bash
git diff --check
git status --short
git push origin main
```

Confirm only the intended dashboard files were committed; preserve unrelated worktree changes.
