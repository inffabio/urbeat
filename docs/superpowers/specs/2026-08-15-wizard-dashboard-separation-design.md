# Wizard and Dashboard Separation Design

## Goal

Make the seller onboarding wizard and the post-creation seller dashboard visibly and structurally independent, while ensuring wizard screens scroll correctly on desktop and mobile.

## Current Problem

The route tree already has separate route families:

- `/configurar-loja/*` loads the onboarding components under `frontend/src/app/features/store-config/`.
- `/app/*` loads dedicated dashboard components under `frontend/src/app/features/seller-*`.

However, wizard components still import or render `ConfigSubnavComponent`, whose links are dashboard-only: Horários, Informações, Impressão, Bio, and Bairros. The root app shell also clips overflow on larger screens, so the long first wizard screen can extend below the viewport without a usable page scrollbar.

## Design

### 1. Explicit Surface Ownership

The wizard owns only the first-store setup flow:

- Loja
- Horários
- Entrega
- Cardápio
- Publicar

Wizard screens render `WizardHeader` and `WizardFooter`. They do not render `ConfigSubnavComponent`, dashboard sidebar controls, or dashboard save/navigation affordances.

The dashboard owns all post-creation management:

- Horários
- Informações
- Impressão
- Bio
- Bairros

Dashboard screens remain under `/app/*`, use their dedicated `seller-*` components, and may render `ConfigSubnavComponent`. The route tests must continue to assert that dashboard configuration routes load dedicated seller components rather than wizard components.

### 2. Implementation Boundary

Use the existing route split instead of duplicating business logic or creating a second feature hierarchy. Remove dashboard navigation from wizard templates and delete only dead dashboard branching/imports in wizard components where the route split makes it unnecessary. Preserve the existing dashboard components and route URLs.

The publish step must preserve the store slug as the public URL path. After publication, the storefront URL is `https://www.urbeat.com.br/<slug>`, for example `https://www.urbeat.com.br/nome-da-loja`. The wizard and dashboard remain operational routes; they must not become the public storefront path.

### 3. Wizard Scrolling

Scope scrolling to the wizard surface. When a wizard header is present, the app shell must allow vertical scrolling within the viewport while retaining horizontal clipping. The wizard content needs bottom breathing room so the fixed/flow footer and mobile safe-area do not cover the final fields or action.

The solution must preserve:

- desktop app-shell framing;
- mobile/PWA safe-area behavior;
- existing Ionic `ion-content` scrolling on wizard subpages that already use it;
- dashboard scrolling behavior.

### 4. Verification

Add or update focused frontend tests to verify:

- wizard templates do not render dashboard configuration navigation;
- dashboard routes still resolve to dedicated seller components;
- wizard route steps remain unchanged;
- the scroll CSS is scoped to the wizard and does not change dashboard overflow rules.

Run:

```bash
npx jest --no-coverage src/app/app.routes.spec.ts src/app/features/store-config/store-config-page.component.spec.ts
npx ng build --configuration production
node .opencode/skills/impeccable/scripts/detect.mjs --json frontend/src/app/features/store-config frontend/src/theme/global.scss
```
