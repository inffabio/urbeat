# Remove Store Description Implementation Plan

**Goal:** Remove the store description field from wizard, dashboard, public contracts, persistence, and existing database data.

**Architecture:** Remove the property from the Store aggregate and every store-specific DTO/projection, then apply a new EF migration that drops `Stores.Description`. Remove the corresponding frontend controls and payload fields while preserving unrelated product, category, additional, plan, and system descriptions.

**Tech Stack:** .NET 9, EF Core/Npgsql, Angular 20, Ionic 8, Jest, xUnit.

## Global Constraints

- Existing worktree changes are unrelated and must not be reverted.
- The removal is destructive: existing `Stores.Description` values are deleted by migration.
- Do not remove other properties named `Description`.
- Keep Angular strictness and existing Urbeat visual tokens.

## Tasks

### Task 1: Remove the backend store contract and persistence field

- Remove `Description` from `Store`, store create/update/response/public DTOs, and publish summary.
- Remove store validators, service assignments, AutoMapper/read-repository projections, publish checks, and demo-store description seed data.
- Add a migration after the current latest migration with `DropColumn("Description", "Stores")` and update the model snapshot.
- Update backend fixtures and assertions that construct or inspect store descriptions.
- Run backend unit and integration tests.

### Task 2: Remove wizard and dashboard UI/API usage

- Remove the description signal, load mapping, request property, and description section from `StoreConfigPageComponent`.
- Remove the description control from `SellerStoreInfoPageComponent`.
- Remove the bio editor/preview description UI and related save/load/dirty logic from `SellerBioPageComponent`, retaining logo and banner behavior.
- Remove store description properties from frontend store models and the publish-summary display.
- Update frontend fixtures and component tests.
- Run focused Jest tests and the production Angular build.

### Task 3: Verify the removal boundary

- Search for store-specific `description`/`Description` references and inspect remaining matches to ensure they belong to other domains.
- Run `dotnet build backend/Urbeat.sln`, relevant backend tests, focused frontend tests, and `npx ng build --configuration production`.
- Run the Impeccable detector against changed frontend targets.
