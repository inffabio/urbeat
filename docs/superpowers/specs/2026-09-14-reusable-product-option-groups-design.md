# Reusable Product Option Groups

## Goal

Allow a seller to reuse the same option-group definition across products without copying it manually, while keeping each product's saved configuration independent.

## Confirmed behavior

- Saved groups belong to the current store and survive page reloads and new sessions.
- When creating a new product, saved groups are displayed by their real names with unchecked checkboxes.
- An unchecked saved group is not associated with the new product.
- Selecting a saved group copies its rules, items, prices, and order into the product.
- Groups manually created in the current product form start selected.
- Editing an existing product checks the groups currently associated with that product.
- Removing or changing a group in one product never changes another product or deletes the reusable group.
- A product can be saved with no option groups.
- The upper and lower `+ Adicionar grupo` controls create the same kind of new selected group.

## Backend design

Add store-owned `ProductOptionGroupTemplate` and `ProductOptionItemTemplate` entities. A template stores the reusable definition and its ordered items. Add an optional `TemplateId` to the product-owned `ProductOptionGroup` snapshot and expose it in the DTO.

Add an authenticated store-scoped endpoint to list reusable templates. Product create/update requests accept the template identifier for selected reusable groups. The product service validates template ownership, copies templates into product-owned groups, and creates templates for newly created groups only after the product operation succeeds. Product snapshots remain independent after persistence. Existing product groups without a template remain valid.

All writes use the existing EF unit of work. Invalid or failed product saves must not leave a reusable template or a partial product group behind. A migration adds the template tables and nullable product-group relationship without changing existing product data.

## Frontend design

The active product wizard loads reusable groups for the current store alongside products and categories. The option-group section has two layers:

- Reusable groups: named rows with a left checkbox, initially unchecked for a new product and checked when associated with the edited product.
- Product groups: editable snapshots. Selecting a reusable group adds its copied data and unselecting removes only that product association. Newly added groups are selected immediately.

The existing group editor, validation, item controls, and ordering remain unchanged. A single lower `+ Adicionar grupo` button is rendered after the last group's `+ Adicionar item` button and remains available when the list is empty; the existing upper button remains.

## Validation

- Backend unit/integration tests cover store isolation, template creation, template selection, snapshot independence, no-group saves, and rollback behavior.
- Frontend tests cover initial checkbox state, selection and deselection, edit state, copy contents, new-group selection, both add-group controls, and request payloads.
- Run the relevant backend tests, focused frontend Jest suites, and the production frontend build.
