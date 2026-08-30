# Category Dialog Redesign

## Goal

Replace the fragile category edit/delete overlays in the store-products wizard with one native HTML dialog that supports reliable text input, editing, deletion, cancellation, and API error feedback.

## Design

- Use one `<dialog>` element outside the category list and outside Ionic scroll/reorder containers.
- Track one mode: `null`, `edit`, or `delete`.
- Track the selected category id, selected category name, and an edit draft name separately.
- Open actions always replace the previous dialog state before showing the new mode.
- The edit form submits through `(ngSubmit)` and prevents browser navigation.
- Delete requires an explicit confirmation button.
- Clicking the backdrop does not close the dialog; only Cancel, Close, successful save, or successful delete closes it.
- API errors remain in the dialog as a visible message and do not mutate local category state.
- Successful edit replaces the category in the local signal; successful delete removes it locally.

## Scope

- Modify the wizard category template and component state/handlers.
- Update the focused component tests for the native dialog behavior.
- Do not change backend contracts or category business rules.
- Do not change the dashboard seller-products page beyond inherited behavior.

## Acceptance Criteria

1. Clicking Edit opens only the edit dialog.
2. Text input remains focused and typing never closes the dialog.
3. Cancel and Close dismiss without API calls.
4. Saving a valid name sends the existing category update request.
5. Clicking Delete opens only the delete confirmation.
6. Confirming delete sends the existing category delete request.
7. API failures keep the dialog open and show the backend detail.
8. Existing category deletion and editing tests pass.
