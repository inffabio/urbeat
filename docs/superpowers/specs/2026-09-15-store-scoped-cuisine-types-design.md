# Store-Scoped Cuisine Categories

## Goal

Make store cuisine categories scoped to the store instead of a mutable global catalog. New store setup must show four protected default categories, start with no selection, require a category, and keep categories created during setup private to that store.

## Scope

This change applies to store cuisine categories only. Product, variation, category, additional, plan, landing-page, and system-parameter descriptions remain unchanged.

## Category Rules

The protected default categories are exactly:

- Açaiteria
- Cafeteria
- Churrascaria
- Comida Árabe
- Comida Japonesa
- Comida Mexicana
- Doceria
- Hamburgueria
- Lanches
- Marmitaria
- Padaria
- Pastelaria
- Pizzaria
- Sucos e Vitaminas
- Tapiocaria

They are stored as global records, are always active, are returned in alphabetical order, and cannot be renamed or deleted through store flows.

Custom categories belong to exactly one store. A custom category created while a store is being registered remains local to that registration until the store is created. After creation, it is persisted with that store and cannot be seen or selected by another store.

The category selection starts empty for a new store. The backend and frontend both reject an empty category before creation or advancing the setup flow.

Existing stores keep their current category. Existing non-default global categories are copied once per referencing store and each store is rewired to its private copy. Existing values are not silently mapped to a different default.

## Data Model

Extend `CuisineType` with:

- `IsDefault`: identifies protected defaults.
- `StoreId`: nullable foreign key; null means a protected global default, otherwise the category is private to that store.

Add a uniqueness rule for the category name within a store scope, with global defaults protected from duplicates. The migration must seed or normalize the four defaults, copy legacy categories per store, rewire `Store.CuisineTypeId`, and preserve unrelated data.

## API and Backend Flow

- Keep the initial category endpoint for the four protected defaults only.
- Add a store-scoped category endpoint for authenticated store owners to list and create categories for their own store.
- Remove global category creation from the store controller/service.
- During initial store creation, accept a selected default name or a custom category name and create the custom category in the same transaction as the store.
- Store update accepts only a protected default or a category owned by that store.
- Reject blank, inactive, protected-category mutation, cross-store selection, and duplicate names with validation or a controlled business response.
- Seeders must be idempotent and must not reintroduce old global categories as selectable defaults.

## Frontend Flow

- In the first wizard section, render the four defaults sorted with `localeCompare('pt-BR')`.
- Initialize `cuisineType` to `''` for a new store, even though options are loaded.
- Show a validation error and prevent next/save when the selection is empty.
- The add-category modal adds a local pending option during new-store setup; it must not call the old global endpoint.
- For an existing store, load defaults plus that store's private categories and use the store-scoped endpoint for creation/deletion.
- Protect default options from deletion in the UI.
- Keep current selected category when loading an existing store.

## Tests

Backend tests cover:

- Four default categories and alphabetical ordering.
- Empty category rejection.
- Creation of a private category during store creation.
- Store-owner authorization and cross-store rejection.
- Protected default immutability.
- Legacy category migration and store reference preservation.

Frontend tests cover:

- New wizard starts with an empty category.
- Defaults appear in alphabetical order.
- Empty category blocks next/save and displays an error.
- Pending category is submitted with the new store and is not posted to a global endpoint.
- Existing-store categories are isolated and defaults cannot be deleted.

## Acceptance Criteria

1. A new store displays only the four protected defaults plus categories created locally in that setup session.
2. The category field is blank initially and a store cannot proceed or save while it is blank.
3. A category created for Store A is not available to Store B.
4. Existing stores retain their previous category after migration.
5. No store-specific category operation can mutate the four protected defaults.
6. All backend and frontend tests pass, including the production build.
