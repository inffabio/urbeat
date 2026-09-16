# Dashboard Product Variations

## Goal

Replicate in the seller dashboard the product variation editor already available in the store setup wizard. Variations are not limited to sizes: the seller may define any labels, such as Small/Medium/Large, weights, portions, formats, or other product alternatives.

## Scope

- Add the variation editor to the dashboard product form when the sale mode is `size`.
- Reuse the existing `StoreProductsState` model and methods.
- Preserve the current API payload and backend persistence based on `ProductVariation`.
- Support adding, editing, removing, reordering, activating/deactivating, setting a default variation, and assigning an individual price and optional description.
- Load and edit existing product variations in the dashboard using the same behavior as the wizard.
- Keep the existing single-price, fixed-weight, variable-weight, option-group, and catalog behaviors unchanged.

## UX Behavior

- Selecting `Por tamanho` in the dashboard reveals the same variation editor used by the wizard.
- The label is generic enough for any variation, while the input remains free-form.
- The first active variation is selected as the default according to the existing shared state behavior.
- The product cannot be saved in variation mode without at least one active variation with a non-empty name and positive price.
- Dashboard and wizard use the same controls, validation, ordering, and API payload.

## Technical Design

- Render the conditional size variation section in `seller-products-page.component.html` after the sale-mode controls.
- Call inherited/shared methods such as `addSizeVariation`, `removeSizeVariation`, `setSizeDefault`, and `reorderSizeVariations`.
- Reuse the existing variation styles from `store-products.shared.scss`.
- Add dashboard-focused tests for rendering, editing, adding, default selection, and payload generation.
- No database schema or backend endpoint changes are required.

## Acceptance Criteria

1. A dashboard seller can select variation mode and add arbitrary variation labels.
2. Each variation accepts its own price and optional description.
3. Existing products show their saved variations when edited.
4. Variations can be reordered, activated/deactivated, defaulted, and removed.
5. Saving sends the same `saleMode` and `variations` contract used by the wizard.
6. Existing dashboard and wizard product flows remain green in focused and production-build validation.
