# Product Detail Bottom Sheet Design

## Goal

Rebuild the product customization screen as a polished storefront bottom sheet for mobile and a centered panel for desktop.

## Behavior

- Remove the separate "Voltar ao cardapio" link.
- Add a top handle with a minimum 44px touch target.
- Tapping the handle or dragging downward past the threshold calls the existing `onBack()` flow.
- A short drag restores the panel and does not navigate away.
- The backdrop and Escape retain their close behavior where the route shell provides them.
- The product choices area is the only scrollable region.
- The add bar remains visible at the bottom of the panel and uses a solid surface, not a fading gradient.

## Layout

- Keep product identity visible near the top with a compact hero image and title/price block.
- Use a single rounded product sheet with no extra background strip after its content.
- Keep quantity controls and the "Adicionar" CTA in a non-floating sticky footer.
- Preserve existing variation, weight, additional, option-group, notes, validation, and cart behavior.
- On desktop, center the panel with a bounded width; on mobile, respect footer clearance and safe areas.

## Testing

- Verify the back link is absent and the handle is present.
- Verify handle click, downward drag threshold, short drag, and Escape/back behavior.
- Verify the add bar remains sticky and the content region scrolls independently.
- Verify no extra bottom link/background strip is rendered.
- Run focused tests, the full Jest suite, and the production build.
