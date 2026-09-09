# Storefront Mobile Footer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the storefront mobile footer fixed and usable while dynamically reserving enough scrollable clearance for every storefront route and the last catalog item.

**Architecture:** `FooterNavComponent` remains the viewport-anchored visual component and reports its rendered safe-zone height. `StoreShellComponent` stores that measurement as an inherited CSS custom property. Routed storefront scrollports consume the property as bottom padding, preserving the existing internal catalog scroll model and preventing content from being stranded behind the footer.

**Tech Stack:** Angular 20 standalone components, Ionic 8, TypeScript strict mode, SCSS, Jest.

## Global Constraints

- The footer remains fixed, visible, and actionable on mobile storefront screens.
- The footer clearance must be inside the scrolling content, not added as shell height.
- Preserve `env(safe-area-inset-bottom, 0px)`, existing Urbeat bordeaux styling, and 44px-plus touch targets.
- Do not introduce a UI library or weaken TypeScript/template strictness.
- Do not change `print-agent/` or `oci-mcp-server/`.
- Do not revert unrelated worktree changes.

## File Map

- Modify `frontend/src/app/shared/components/footer-nav/footer-nav.component.ts`: report the measured safe-zone height and disconnect the observer on destroy.
- Modify `frontend/src/app/shared/components/footer-nav/footer-nav.component.spec.ts`: test measurement emission, fixed positioning, safe area, and observer cleanup.
- Modify `frontend/src/app/features/store/store-shell.component.ts`: receive footer height and expose it as a CSS custom property on the shell.
- Modify `frontend/src/app/features/store/store-shell.component.spec.ts`: test the shell-to-footer clearance contract.
- Modify `frontend/src/app/features/store/store-page.component.scss`: consume the dynamic clearance for the catalog scrollport/products surface.
- Modify `frontend/src/app/features/store/store-page.component.spec.ts`: test that the catalog uses the dynamic variable rather than a fixed-only clearance.
- Modify any additional storefront route stylesheet discovered during implementation only if it owns a separate scroll container; add its focused test alongside the change.

### Task 1: Add Footer Measurement Contract

**Files:**
- Modify: `frontend/src/app/shared/components/footer-nav/footer-nav.component.ts`
- Test: `frontend/src/app/shared/components/footer-nav/footer-nav.component.spec.ts`

**Interfaces:**
- Produce `@Output() heightChange = new EventEmitter<number>()`.
- Measure `.footer-nav-safe-zone` with `ResizeObserver` after view initialization.
- Disconnect the observer in `ngOnDestroy`.

- [ ] **Step 1: Add failing tests**

Add tests that install a fake `ResizeObserver`, trigger its callback with a `contentRect.height`, and assert `heightChange` emits that value. Add a destroy test asserting `disconnect()` is called. Keep the existing source-structure assertions for fixed positioning and safe area.

- [ ] **Step 2: Run the focused footer suite**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/shared/components/footer-nav/footer-nav.component.spec.ts
```

Expected: the new measurement and cleanup tests fail before implementation.

- [ ] **Step 3: Implement the minimal measurement**

Use `ElementRef<HTMLElement>` and `AfterViewInit`/`OnDestroy`. Observe the host or safe-zone element, emit only finite non-negative heights, and disconnect during destruction. Do not move the footer into document flow or change its visual dimensions.

- [ ] **Step 4: Re-run the focused footer suite**

Run the same Jest command. Expected: all footer tests pass.

- [ ] **Step 5: Commit the isolated component change**

```bash
git add frontend/src/app/shared/components/footer-nav/footer-nav.component.ts frontend/src/app/shared/components/footer-nav/footer-nav.component.spec.ts
git commit -m "fix: measure storefront footer height"
```

### Task 2: Propagate Dynamic Clearance Through Store Shell

**Files:**
- Modify: `frontend/src/app/features/store/store-shell.component.ts`
- Test: `frontend/src/app/features/store/store-shell.component.spec.ts`

**Interfaces:**
- Consume `FooterNavComponent.heightChange`.
- Produce a shell CSS custom property such as `--store-footer-clearance` in pixels, with a safe initial fallback.

- [ ] **Step 1: Add failing shell tests**

Add a host test that verifies the footer output is bound to a shell handler and a source assertion that the shell exposes a pixel-valued footer-clearance custom property without adding fixed footer padding to `.store-route`.

- [ ] **Step 2: Run the focused shell suite**

Run:

```bash
npx jest --no-coverage src/app/features/store/store-shell.component.spec.ts
```

Expected: the new wiring assertion fails before implementation.

- [ ] **Step 3: Implement shell propagation**

Add a signal for the measured height, bind it on `.app-shell` with Angular style syntax, and handle the child output. Keep the footer conditional rendering and inert behavior unchanged. Use a non-zero fallback matching the current minimum bar plus safe-area allowance so content remains reachable before the first observer callback.

- [ ] **Step 4: Re-run the focused shell suite**

Run the same Jest command. Expected: all shell tests pass.

- [ ] **Step 5: Commit the shell change**

```bash
git add frontend/src/app/features/store/store-shell.component.ts frontend/src/app/features/store/store-shell.component.spec.ts
git commit -m "fix: propagate storefront footer clearance"
```

### Task 3: Make Catalog and Other Storefront Scrollports Consume the Clearance

**Files:**
- Modify: `frontend/src/app/features/store/store-page.component.scss`
- Test: `frontend/src/app/features/store/store-page.component.spec.ts`
- Modify/Test: any other storefront route stylesheet/spec that contains an independent `overflow-y: auto` scrollport.

**Interfaces:**
- Consume `var(--store-footer-clearance, 72px)` from the shell.
- Preserve `.store-content` as the catalog scrollport and keep clearance on the products/content surface where it does not create a second shell-level offset.

- [ ] **Step 1: Add failing style assertions**

Change the catalog test to require the dynamic CSS variable in the products clearance and to reject a clearance expression that relies only on literal `64px`. Add equivalent assertions for any additional independent storefront scrollport found in the route styles.

- [ ] **Step 2: Run the focused store-page suite**

Run:

```bash
npx jest --no-coverage src/app/features/store/store-page.component.spec.ts
```

Expected: the new dynamic-clearance assertion fails before the stylesheet change.

- [ ] **Step 3: Implement the CSS contract**

Use `padding-bottom: calc(var(--store-footer-clearance, 72px) + 8px)` on the catalog products surface, retaining `box-sizing` behavior and safe-area support through the measured footer. For other storefront scrollports, apply the same variable inside the scroll container without adding padding to `.store-route` or `.app-shell`. Keep desktop media-query behavior unchanged unless the shared variable is required for correctness.

- [ ] **Step 4: Re-run the focused store-page suite**

Run the same Jest command. Expected: all store-page tests pass.

- [ ] **Step 5: Commit the scrollport change**

```bash
git add frontend/src/app/features/store
git commit -m "fix: keep storefront content clear of footer"
```

### Task 4: Full Frontend Verification

**Files:**
- No additional files unless verification exposes a regression in the touched storefront files.

- [ ] **Step 1: Run all focused suites together**

```bash
npx jest --no-coverage src/app/shared/components/footer-nav/footer-nav.component.spec.ts src/app/features/store/store-shell.component.spec.ts src/app/features/store/store-page.component.spec.ts
```

Expected: PASS.

- [ ] **Step 2: Build production frontend**

```bash
npx ng build --configuration production
```

Expected: successful production build with no TypeScript or template errors.

- [ ] **Step 3: Inspect the final diff**

```bash
git status --short
```

Confirm only the intended storefront files changed in the implementation commits and unrelated worktree changes remain untouched.

- [ ] **Step 4: Perform device-oriented checks when available**

Check a short viewport and a long catalog. Scroll to the last product, expand/collapse the mobile browser chrome, and verify Android Chrome plus iOS Safari/WebView behavior. Confirm the footer remains in the same bottom position and its buttons remain clickable.
