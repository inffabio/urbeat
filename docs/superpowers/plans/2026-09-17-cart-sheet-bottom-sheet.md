# Cart Sheet Bottom Sheet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the storefront cart into a compact bottom sheet with a scroll-only item list, fixed actions, and a touch-friendly drag handle.

**Architecture:** Keep the existing standalone `CartSheetComponent` and its parent-provided footer clearance. Anchor the sheet and backdrop inside `.app-shell`, keep the item list as the only flexible scroll region, and implement drag state locally in the component without adding dependencies.

**Tech Stack:** Angular 20 standalone components, Ionic icons, TypeScript strict mode, Jest.

## Global Constraints

- Preserve `.app-shell` footer clearance and never cover the footer menu.
- Preserve 44px-plus touch targets for the collapse handle and primary CTA.
- Keep the item list as the only scrollable region.
- Use `prefers-reduced-motion` to disable sheet animation.
- Do not add a new dependency.

---

### Task 1: Add Bottom-Sheet Interaction Tests

**Files:**
- Modify: `frontend/src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts`
- Test: `frontend/src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts`

**Interfaces:**
- Consumes: existing `CartSheetComponent` inputs and `close` output.
- Produces: regression coverage for handle semantics, drag threshold, compact layout, and list-only scrolling.

- [ ] **Step 1: Write failing tests**

Add tests that assert:

```typescript
it('renders a 44px-plus handle that closes on click', () => {
  const handle = fixture.nativeElement.querySelector('[data-action="collapse-sheet"]') as HTMLButtonElement;
  expect(handle).not.toBeNull();
  expect(handle.getAttribute('aria-label')).toBe('Recolher sacola');
  handle.click();
  expect(close).toHaveBeenCalled();
});

it('closes after a downward pointer drag past the threshold', () => {
  const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;
  sheet.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, clientY: 100 }));
  sheet.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, clientY: 190 }));
  expect(close).toHaveBeenCalled();
});

it('does not close after a short downward drag', () => {
  const sheet = fixture.nativeElement.querySelector('.cart-sheet') as HTMLElement;
  sheet.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, clientY: 100 }));
  sheet.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, clientY: 140 }));
  expect(close).not.toHaveBeenCalled();
});
```

Also assert the sheet uses `height: min(58dvh, ...)`, the list has `overflow-y: auto`, and the footer remains `flex-shrink: 0`.

- [ ] **Step 2: Run tests to verify failure**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts
```

Expected: FAIL because the handle data attribute and pointer handlers do not exist and the current sheet height is not `58dvh`.

### Task 2: Implement Compact Bottom Sheet

**Files:**
- Modify: `frontend/src/app/shared/components/cart-sheet/cart-sheet.component.ts`

**Interfaces:**
- Consumes: `footerHeight`, `sheetHeight`, `close`, and `cart` state.
- Produces: a compact sheet whose `close` output fires from handle clicks, backdrop clicks, Escape, and downward drags beyond 80px.

- [ ] **Step 1: Add the handle and pointer state**

Render the top control with `data-action="collapse-sheet"`, retain its accessible label, and add `pointerdown`/`pointerup` handlers to the dialog. Track the starting Y coordinate locally; emit `close` only when `clientY - startY >= 80`.

- [ ] **Step 2: Apply the compact layout**

Use:

```css
.cart-sheet {
  position: absolute;
  height: min(58dvh, calc(100dvh - var(--footer-height)));
  max-height: min(58dvh, calc(100dvh - var(--footer-height)));
}
.cart-sheet-list {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}
```

Keep the footer non-shrinking and retain the existing backdrop bottom clearance.

- [ ] **Step 3: Run focused tests to verify green**

```bash
npx jest --no-coverage src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts
```

Expected: PASS.

### Task 3: Verify Store Integration

**Files:**
- Modify: none unless a regression is found.
- Test: `frontend/src/app/features/store/store-shell.component.spec.ts`

- [ ] **Step 1: Run related tests**

```bash
npx jest --no-coverage src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts src/app/features/store/store-shell.component.spec.ts src/app/shared/components/footer-nav/footer-nav.component.spec.ts
```

Expected: all related suites pass and the measured footer height is still passed to the cart sheet.

- [ ] **Step 2: Run the full frontend validation**

```bash
npx jest --no-coverage
npx ng build --configuration production
```

Expected: all Jest suites pass and the production build completes; the existing initial bundle budget warning is acceptable and must be reported if present.

- [ ] **Step 3: Review the final diff**

```bash
git diff --check
git diff -- frontend/src/app/shared/components/cart-sheet/cart-sheet.component.ts frontend/src/app/shared/components/cart-sheet/cart-sheet.component.spec.ts
```

Expected: no whitespace errors and no changes outside the cart sheet and its tests.
