# Storefront Mobile Footer Layout

## Goal

Keep the storefront footer navigation fixed, visible, and actionable at the bottom of every mobile storefront screen while ensuring scrollable content can reach its final item without being permanently hidden behind the footer.

## Design

- Keep `FooterNavComponent` outside the route content and anchored with `position: fixed; bottom: 0`.
- Preserve the existing mobile shell width, safe-area padding, visual styling, and touch targets.
- Measure the rendered footer safe-zone height with `ResizeObserver` so the clearance reflects the actual device, font, safe-area, and responsive dimensions.
- Propagate the measured height from `FooterNavComponent` through `StoreShellComponent` as a CSS custom property inherited by routed storefront content.
- Apply bottom clearance to each storefront scrollport, with the menu height plus a small breathing room. The clearance must be inside the scrolling content, not added as shell height and not used to move the fixed footer.
- Keep the catalog's clearance on the products surface so long product lists can scroll beyond the footer. Apply the same contract to other storefront-specific scroll containers where they exist.
- Retain safe-area handling and avoid using a fixed `64px` assumption as the only clearance calculation.

## Behavior

- The footer remains in the same viewport position while the user scrolls.
- The footer remains interactive unless the cart sheet or account menu intentionally makes it inert.
- Product lists of any length can be scrolled until the last product is fully visible above the footer clearance.
- Browser address-bar changes, short mobile viewports, iOS safe areas, and Android devices must not place the footer below the visible viewport.
- Desktop storefront behavior and the established Urbeat bordeaux visual language remain unchanged.

## Implementation Boundaries

- Change only the frontend storefront footer/layout and its focused tests.
- Do not replace the storefront scroll model with document scrolling unless validation proves the current scrollport cannot be made reliable.
- Do not introduce a new UI library or weaken TypeScript/template strictness.

## Verification

- Add focused unit/structural tests for footer measurement/cleanup and dynamic clearance wiring.
- Verify that the footer remains fixed and safe-area aware.
- Run the focused Jest suites and production Angular build.
- Manually validate short mobile viewports, long catalogs, Android Chrome, iOS Safari/WebView, and browser chrome transitions when device access is available.
