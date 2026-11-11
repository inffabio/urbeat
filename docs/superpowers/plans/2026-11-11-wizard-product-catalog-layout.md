# Wizard Product Catalog Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar o catálogo lateral de produtos do wizard legível e operável em todas as larguras, com edição identificável, rolagem automática para o formulário e nenhuma sobreposição.

**Architecture:** Manter a seleção e o carregamento de produto em `StoreProductsState`, adicionando no componente do wizard apenas a rolagem para uma âncora do formulário após a seleção. Reorganizar o template e o stylesheet compartilhado com uma grade responsiva: conteúdo do produto separado das ações em desktop e duas faixas em mobile; o dashboard permanece fora do escopo visual do wizard.

**Tech Stack:** Angular 20 standalone, Ionic, Signals, TypeScript strict, SCSS, Jest.

## Global Constraints

- O botão de edição deve usar ícone de lápis/caneta e manter `aria-label`/`title` contextualizados.
- Editar deve carregar o produto e rolar suavemente para a seção de cadastro sem ficar sob o cabeçalho fixo.
- O nome deve ser sempre visível após cadastro e edição, sem ficar sob preço ou ações.
- Busca e filtro devem permanecer harmônicos, sem overflow ou sobreposição em telas pequenas.
- Ações continuam semânticas: editar carrega o formulário, copiar cria rascunho, chevron expande detalhes e lixeira exclui.
- Preview de grupos/variações pode ser compacto, mas deve permanecer legível e acessível.
- Preservar dashboard, cópia de produto, confirmação de alterações não salvas e demais fluxos.
- Não incluir alterações não relacionadas já existentes no working tree.

---

### Task 1: Rolagem automática e âncora do editor

**Files:**
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.html`
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.ts`
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

**Interfaces:**
- Produces a stable template reference/class for the product editor section.
- `StoreProductsPageComponent.selectProduct(product: Product): void` continues honoring `allowDraftSwitch()` and then scrolls to the editor only after the view reflects the selected product.

- [ ] **Step 1: Add failing component tests.** Assert the product form section has a stable editor anchor and that selecting a product schedules a scroll call to that anchor after the existing confirmation/selection flow.
- [ ] **Step 2: Run the focused spec and confirm the new assertions fail.**
  From `frontend/`: `npx jest --no-coverage src/app/features/store-config/products/store-products-page.component.spec.ts`.
- [ ] **Step 3: Add the editor anchor in the template.** Put a template reference or stable class on the “Cadastrar produtos” section, not on the catalog list; add `scroll-margin-top` in styles so the sticky wizard header and a small gap remain clear.
- [ ] **Step 4: Implement safe scrolling in the wizard component.** Keep `super.selectProduct(product)` first, then use `requestAnimationFrame` (or the existing Angular-safe equivalent) to call `scrollIntoView({ behavior: 'smooth', block: 'start' })` on the editor anchor. Guard missing DOM references and preserve dashboard behavior.
- [ ] **Step 5: Run the focused spec and confirm PASS.**

### Task 2: Catalog action semantics and product name rendering

**Files:**
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.html`
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

**Interfaces:**
- The catalog edit control remains `(click)="$event.stopPropagation(); selectProduct(product)"` and uses a recognized edit icon.
- The product identity block renders `product.name` as its primary text with a safe fallback only when the API data is genuinely empty.

- [ ] **Step 1: Add failing template assertions.** Verify the edit button has a pencil/edit icon, contextual `aria-label` and `title`; verify the name binding is present in the catalog product identity block; verify the copy, toggle and delete controls retain labels/semantics.
- [ ] **Step 2: Run the focused spec and confirm the markup assertions fail if the current icon/contract does not satisfy them.**
- [ ] **Step 3: Update the edit control.** Use the existing registered Ionic edit icon that reads clearly as a pencil, keep the button icon-only if the responsive design requires it, and add `aria-hidden` only to the icon—not to the button label.
- [ ] **Step 4: Harden the identity markup.** Keep the product name as the first strong identity element, avoid hiding it behind a fixed single-line box on narrow screens, and use a visible fallback such as `Produto sem nome` only for an actually absent name.
- [ ] **Step 5: Run the focused spec and confirm PASS.**

### Task 3: Responsive catalog and compact preview layout

**Files:**
- Modify: `frontend/src/app/shared/styles/store-products.shared.scss`
- Modify: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

**Interfaces:**
- The shared stylesheet must distinguish wizard-only adjustments from dashboard styles using `.wizard-content:not(.is-dashboard)` where necessary.
- Desktop and mobile selectors must preserve the product row as a bounded layout without horizontal overflow.

- [ ] **Step 1: Add failing style-contract tests.** Assert the catalog tools have a responsive single-column fallback, product rows give actions their own bounded area on narrow screens, product info can shrink/wrap, and the preview uses reduced padding.
- [ ] **Step 2: Run the focused spec and confirm the assertions fail.**
- [ ] **Step 3: Reorganize the catalog row.** Keep `.catalog-product` as a two-area grid on wide screens; give `.catalog-product-main` a `minmax(0, 1fr)` area and `.catalog-product-actions` a non-overlapping action area. At narrow breakpoints, make the row two explicit rows with actions spanning the full width, preserving at least 44px touch targets.
- [ ] **Step 4: Fix the identity and price constraints.** Set `min-width: 0`, controlled wrapping/line clamping, and a stable price treatment so the name cannot be covered by actions or price. Do not use overflow rules that hide the entire name.
- [ ] **Step 5: Reflow search and category filter.** Keep the current side-by-side grid when available; switch to one column below the breakpoint where the current 200–230px select no longer fits. Ensure both controls stay within the catalog card.
- [ ] **Step 6: Compact the detail preview.** Reduce `.catalog-preview` padding and gaps, tighten preview row typography within readable contrast, and preserve the expanded content inside the parent width.
- [ ] **Step 7: Run the focused spec and production build.**
  `npx jest --no-coverage src/app/features/store-config/products/store-products-page.component.spec.ts`
  `npx ng build --configuration production`

### Task 4: Final validation and review

**Files:**
- Review only: files changed in Tasks 1–3

- [ ] **Step 1: Run targeted product tests.**
  `npx jest --no-coverage src/app/features/store-config/products/store-products-page.component.spec.ts src/app/features/seller-products/seller-products-page.component.spec.ts`
- [ ] **Step 2: Run the complete frontend suite.**
  `npx jest --no-coverage`
- [ ] **Step 3: Run the production build.**
  `npx ng build --configuration production`
- [ ] **Step 4: Run the Impeccable detector once on changed UI targets.**
  `C:\Projetos\urbeat\.opencode\skills\impeccable\scripts\impeccable.cmd detect --json frontend/src/app/shared/styles/store-products.shared.scss frontend/src/app/features/store-config/products/store-products-page.component.html frontend/src/app/features/store-config/products/store-products-page.component.ts`
- [ ] **Step 5: Run `git diff --check` and review responsive scope.** Confirm no dashboard regression, no horizontal overflow contract, no unrelated files, and no staged/committed changes from the existing working tree.
