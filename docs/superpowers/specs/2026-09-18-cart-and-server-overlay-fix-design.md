# Cart And Server Overlay Fix

## Goal

Keep the application inaccessible while an HTTP request is active, remove the
white card behind the vector loader, and make cart removal and checkout actions
reliable on mobile and desktop.

## Design

The server-access overlay keeps its fixed, full-viewport backdrop and touch
blocking behavior. The visual panel becomes transparent so only the responsive
hamburger SVG and its loading message remain visible. The overlay continues to
own the interaction block; transparency must not change pointer interception.

Cart item identity is normalized when persisted state is loaded. Cart item
removal accepts the item object at the UI boundary and uses its stable frontend
identity, with a defensive generated identity for older persisted entries that
have none. The cart page and cart sheet use the same removal behavior.

The checkout action is rendered by `StoreShellComponent`, alongside the footer
it belongs to, only on the cart route. It is placed in normal shell flow above
the footer rather than relying on a fixed element nested in the Ionic routed
page. The cart page remains responsible for its business validation and
publishes the current checkout-action state to the shell through a small shared
state service. This preserves the existing disabled behavior while removing the
stacking-context and viewport-coordinate dependency.

## States And Accessibility

- Overlay: visible while one or more tracked requests are active; backdrop
  receives pointer and touch events; reduced motion disables animation.
- Cart removal: item disappears immediately and the persisted cart updates;
  no item with a missing legacy ID can become undeletable.
- Checkout action: visible above the footer whenever the cart has items,
  disabled for closed stores or orders below the minimum, and keyboard/touch
  accessible with a 44px-plus target.
- Empty cart: action is removed and the existing empty state remains.

## Verification

Add regression tests for transparent overlay styling and interaction blocking,
legacy cart items without IDs, removal from both cart surfaces, shell placement
of the checkout action, disabled business states, and navigation to the next
checkout step. Run focused tests, the full frontend suite, and the production
build.
