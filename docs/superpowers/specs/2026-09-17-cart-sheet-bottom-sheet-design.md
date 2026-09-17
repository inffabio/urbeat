# Cart Sheet Bottom Sheet Design

## Goal

Make the storefront cart sheet feel like a compact mobile bottom sheet while preserving a usable desktop presentation.

## Behavior

- The sheet is anchored inside `.app-shell`, above the measured footer clearance.
- The backdrop covers the shell content area only and stops above the footer.
- The item list is the only scrollable region; the sheet footer remains fixed.
- The sheet is shorter than the current version and uses compact product rows.
- A top handle provides a minimum 44px touch target.
- Tapping the handle closes the sheet.
- A downward drag of at least 80px closes the sheet; a shorter drag restores it.
- Escape and backdrop activation retain their existing close behavior.
- Desktop uses the same close controls, while drag is primarily intended for touch devices.

## Layout

- Sheet height: `min(58dvh, calc(100dvh - var(--footer-height)))`.
- Product image: 40px.
- Product row minimum height: 56px.
- Product row gap: 6px.
- Product list: `overflow-y: auto`, `min-height: 0`, and contained overscroll.
- Action footer: non-shrinking, with the existing 44px-plus CTA target.

## Testing

- Verify shell anchoring and footer clearance.
- Verify compact row spacing and item-list-only scrolling.
- Verify handle semantics and close event on click.
- Verify pointer drag closes only after the threshold and does not close on a short drag.
- Run the focused frontend tests, full Jest suite, and production build.
