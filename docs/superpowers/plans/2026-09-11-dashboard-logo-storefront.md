# Dashboard Logo Storefront Link Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every dashboard logo variant open the current store's public storefront in a new tab with the requested tooltip.

**Architecture:** Keep the existing `SellerAppShellComponent` template as the single rendering location. Wrap the desktop/sidebar logo and mobile logo in semantic anchors using the store slug already exposed by `SellerShellFacade`; preserve existing classes, inline background-image bindings, and fallback content. Extend the component spec with DOM assertions for all rendered variants.

**Tech Stack:** Angular 20 standalone components, Angular templates, SCSS, Jest.

## Global Constraints

- Use the existing storefront route `/${facade.store()?.slug}`.
- Use exactly `Clique para ir para loja` for `title` and the accessible label.
- Open in a new tab with `target="_blank"` and `rel="noopener noreferrer"`.
- Preserve desktop, collapsed-sidebar, and mobile behavior and existing logo fallback.
- Do not add a tooltip dependency or weaken strict TypeScript/template settings.

---

## File Map

- Modify `frontend/src/app/features/seller-shell/seller-app-shell.component.html`: add the storefront anchors around the desktop/sidebar and mobile logo elements.
- Modify `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`: provide a slug in the facade fixture and assert the three logo variants expose the correct link attributes.
- Modify `frontend/src/app/features/seller-shell/seller-app-shell.component.scss` only if the anchor wrapper changes default link styling or layout; otherwise leave styles untouched.

### Task 1: Add storefront links to logo variants

**Files:**
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.html:2-17`
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.html:148-158`
- Test: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`

**Interfaces:**
- Consumes: `facade.store()?.slug`, `facade.store()?.logoUrl`, and the existing logo classes.
- Produces: anchors around each logo with `href="/${facade.store()?.slug}"`, `target="_blank"`, `rel="noopener noreferrer"`, `title="Clique para ir para loja"`, and `aria-label="Clique para ir para loja"`.

- [ ] **Step 1: Extend the test fixture with a store slug**

In `facadeMock.store`, retain the existing `isOpen: true` value and add `slug: 'loja-teste'`:

```ts
store: jest.fn(() => ({ isOpen: true, slug: 'loja-teste' })),
```

- [ ] **Step 2: Add the failing DOM test**

Add this test to `seller-app-shell.component.spec.ts`:

```ts
it('links every dashboard logo variant to the storefront in a new tab', () => {
  fixture.detectChanges();

  const links = Array.from(fixture.nativeElement.querySelectorAll('a.storefront-logo-link')) as HTMLAnchorElement[];

  expect(links).toHaveLength(3);
  for (const link of links) {
    expect(link.getAttribute('href')).toBe('/loja-teste');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link.getAttribute('title')).toBe('Clique para ir para loja');
    expect(link.getAttribute('aria-label')).toBe('Clique para ir para loja');
  }
});
```

- [ ] **Step 3: Run the focused test and verify it fails**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts
```

Expected: FAIL because no elements currently have the `storefront-logo-link` class and the fixture does not yet render three links.

- [ ] **Step 4: Wrap the desktop/sidebar logo without changing its visual element**

In the `brand-row`, insert an anchor immediately around the existing `.brand-mark` div. Use the existing signal binding and keep the div's `aria-hidden="true"`:

```html
<a
  class="storefront-logo-link"
  [href]="'/' + facade.store()?.slug"
  target="_blank"
  rel="noopener noreferrer"
  title="Clique para ir para loja"
  aria-label="Clique para ir para loja"
>
  <div
    class="brand-mark"
    [class.has-logo]="!!facade.store()?.logoUrl"
    [style.background-image]="facade.store()?.logoUrl ? 'url(' + facade.store()?.logoUrl + ')' : null"
    aria-hidden="true"
  >
    @if (facade.store()?.logoUrl) {
      <span></span>
    } @else {
      H
    }
  </div>
</a>
```

The anchor must remain the first grid child so the existing `brand-row` and collapsed-sidebar grid rules continue to position the logo.

- [ ] **Step 5: Wrap the mobile logo with the same link contract**

In `.mobile-brand`, insert the same anchor attributes around `.mobile-brand-mark`, preserving the existing background binding and fallback:

```html
<a
  class="storefront-logo-link"
  [href]="'/' + facade.store()?.slug"
  target="_blank"
  rel="noopener noreferrer"
  title="Clique para ir para loja"
  aria-label="Clique para ir para loja"
>
  <div
    class="mobile-brand-mark"
    [class.has-logo]="!!facade.store()?.logoUrl"
    [style.background-image]="facade.store()?.logoUrl ? 'url(' + facade.store()?.logoUrl + ')' : null"
    aria-hidden="true"
  >
    @if (!facade.store()?.logoUrl) { H }
  </div>
</a>
```

- [ ] **Step 6: Add only the minimal anchor styling if required**

After the template change, inspect the focused test DOM and browser/build behavior. If the browser's default anchor styling affects layout or color, add this to the component stylesheet:

```scss
.storefront-logo-link {
  color: inherit;
  text-decoration: none;
}
```

Do not alter logo dimensions, grid placement, or responsive rules.

- [ ] **Step 7: Add a fallback regression assertion**

Add this test to ensure the existing no-logo content remains available:

```ts
it('keeps the logo fallback when the store has no logo', () => {
  fixture.detectChanges();

  const marks = fixture.nativeElement.querySelectorAll('.brand-mark, .mobile-brand-mark');

  expect(marks).toHaveLength(3);
  expect(Array.from(marks).every((mark: Element) => mark.textContent?.trim() === 'H')).toBe(true);
});
```

- [ ] **Step 8: Run focused tests and production build**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts
npx ng build --configuration production
```

Expected: the focused suite passes and the production build completes successfully.

- [ ] **Step 9: Review the final diff and commit the implementation**

Run:

```bash
git diff --check
git diff -- frontend/src/app/features/seller-shell/seller-app-shell.component.html frontend/src/app/features/seller-shell/seller-app-shell.component.scss frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts
```

Stage only the implementation files and commit:

```bash
git add -- frontend/src/app/features/seller-shell/seller-app-shell.component.html frontend/src/app/features/seller-shell/seller-app-shell.component.scss frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts
git commit -m "feat: link dashboard logo to storefront"
```

## Verification Checklist

- `brand-row` desktop logo remains visually unchanged.
- Collapsed sidebar still exposes the same clickable logo and tooltip.
- Mobile topbar logo opens the same storefront URL.
- The store name remains outside the anchor and is not accidentally clickable.
- No unrelated worktree changes are staged.
