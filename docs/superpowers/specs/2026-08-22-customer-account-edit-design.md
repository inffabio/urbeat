# Customer Account Editing Design

## Goal

Enable authenticated customers to access Conta from the storefront footer, open a two-item account menu, edit the existing customer registration data, and save changes without duplicating the checkout form.

## Approved Flow

- The storefront footer order is `Cardápio`, `Carrinho`, `Pedidos`, `Conta`.
- `Pedidos` remains disabled.
- `Conta` is enabled only when `AuthService.customerProfile()` contains an authenticated customer profile.
- Selecting `Conta` opens a menu above the footer with exactly:
  - `Cadastro`
  - `Sair`
- `Sair` clears the customer session through `AuthService.logout()` and closes the menu.
- `Cadastro` navigates to `/:storePath/conta/cadastro`.
- The account form reuses the customer registration fields and validations already used by checkout:
  - Full name
  - Phone
  - Email
  - CEP
  - City
  - State
  - Neighborhood
  - Street
  - Number
  - Complement
- The form loads the authenticated profile and primary address.
- `Salvar` starts disabled, becomes enabled after a valid change, and becomes disabled again after a successful save.
- Email changes are saved immediately, without a confirmation step, per user decision.
- Password editing is not part of this screen because the current customer profile contract does not expose a password field. Existing password recovery remains the password-management flow.

## Architecture

### Shared Form

Extract the reusable customer fields, masks, validation, CEP lookup, and address state into `CustomerProfileFormComponent`. The component supports two modes:

- `checkout`: emits validated customer and address data for checkout session creation.
- `edit`: loads existing values, tracks dirty state, and emits a save request.

The existing checkout behavior must remain unchanged.

### Store Account Menu

Keep account-menu state in `StoreShellComponent`, alongside footer and cart-sheet state. Render the menu as a fixed surface above the footer so it is not clipped by route content. The menu closes after selecting either action and closes when the user taps outside.

While the menu or account sheet is open, the footer remains visible but is inert, consistent with the existing cart-sheet behavior.

### Customer API

Add an authenticated endpoint:

```http
PUT /api/customer/me
```

Request fields:

```json
{
  "fullName": "Maria Oliveira",
  "email": "maria@email.com",
  "phoneNumber": "22999999999"
}
```

The endpoint resolves the user from the authenticated claim, validates the request server-side, updates Identity user phone/email, updates the `FullName` claim, and returns the refreshed `CustomerProfileResponse`.

The primary address uses the existing authenticated endpoint:

```http
PUT /api/customer/addresses/{addressId}
```

Both updates must complete before the UI reports success. If the address does not exist, the edit flow must create it through the existing address service rather than inventing an address ID.

## UX States

- Loading: show the existing form loading treatment while profile/address data is fetched.
- Clean form: `Salvar` disabled.
- Valid dirty form: `Salvar` enabled.
- Invalid dirty form: `Salvar` disabled and field errors shown after interaction.
- Saving: disable fields and show a saving state to prevent duplicate submissions.
- Success: update the auth profile signal, show a success toast, and keep the customer on the account form.
- Error: keep edits in place, re-enable the form, and show an actionable error toast.
- Unauthenticated direct navigation: redirect back to the storefront and do not expose the account form.

## Validation And Testing

- Add frontend tests for customer-profile loading, dirty/save state, successful profile and address updates, error recovery, account-menu actions, and logout.
- Preserve and rerun all existing checkout customer-page tests.
- Add backend unit/integration coverage for authenticated profile update, validation, email conflict, and claim/profile response consistency.
- Run frontend focused tests, backend tests covering the customer controller, production frontend build, and the Impeccable detector on changed UI files.

## Scope Exclusions

- No password-change form.
- No multi-address management UI beyond editing the current primary address.
- No order-history implementation; `Pedidos` remains disabled.
