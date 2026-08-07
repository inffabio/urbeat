---
name: Urbeat
description: A local-first delivery marketplace that makes small merchants look big.
colors:
  primary: "#D54A51"
  primary-dark: "#B63A41"
  primary-soft: "#FDECEE"
  success-green: "#119441"
  bg: "#ede9e3"
  bg-warm: "#fbf7f2"
  surface: "#ffffff"
  surface-soft: "#fff8f0"
  hairline-warm: "#f3efe9"
  text-primary: "#171717"
  ink: "#161616"
  ink-soft: "#3a3632"
  slate-warm: "#565049"
  text-secondary: "#5a5a63"
  muted-strong: "#6f6f6f"
  text-muted: "#767680"
  muted-warm: "#b5a89e"
  border-light: "#eadfd6"
  border-medium: "#d6d0cc"
  brown: "#6c4634"
typography:
  body:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  headline:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, sans-serif"
    fontSize: "22px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "normal"
  title:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, sans-serif"
    fontSize: "17px"
    fontWeight: 700
    lineHeight: 1.3
    letterSpacing: "normal"
  label:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, sans-serif"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "0.3px"
rounded:
  sm: "10px"
  md: "14px"
  lg: "18px"
  xl: "26px"
  "2xl": "34px"
  full: "999px"
spacing:
  "1": "4px"
  "2": "8px"
  "3": "12px"
  "4": "16px"
  "5": "20px"
  "6": "24px"
  "7": "32px"
  "8": "40px"
shadows:
  sm: "0 8px 22px rgba(33, 20, 8, .07)"
  md: "0 14px 34px rgba(33, 20, 8, .09)"
  lg: "0 30px 90px rgba(0, 0, 0, .18)"
  none: "0 0 0 transparent"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.surface}"
    typography: "{typography.title}"
    rounded: "{rounded.full}"
    padding: "16px 20px"
  button-primary-active:
    backgroundColor: "{colors.primary-dark}"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.primary}"
    typography: "{typography.title}"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: "16px"
  input-field:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: "14px 16px"
  chip-category:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.full}"
    padding: "8px 16px"
  chip-category-active:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.surface}"
---

# Design System: Urbeat

## 1. Overview

**Creative North Star: "The Well-Kept Storefront"**

Urbeat dresses small local merchants in a storefront they could never afford to build themselves — meticulously maintained, every detail intentional. The system's job is to disappear into *the merchant's* brand while quietly guaranteeing that every store feels credible, warm, and safe to hand your card to. The mood is a well-lit neighborhood shop at dusk: warm cream walls (`#ede9e3`), a single confident bordeaux sign (`#D54A51`), everything in its place. Warmth carries through the cream surface and bordeaux accent; the premium feeling comes from restraint, generous spacing, and rounded-but-not-cartoonish geometry — never from more color or more decoration.

This is a **mobile-first product** first and a marketing surface second. Most surfaces (storefront, cart, checkout, order tracking, seller config) are transactional and must feel fast, obvious, and trustworthy under a thumb. The one marketing surface — the seller-recruitment landing page — is allowed a louder voice (Poppins display, JetBrains Mono accents, dark-ink palette) but pulls from the same brand accent. The landing page uses its own editorial tokens (`assets/css/styles.css`); those are a deliberate marketing variant, not the product system documented here.

The system explicitly rejects three things: the **loud, ad-saturated big-delivery-app look** (iFood/Uber Eats density and banner-stacking), the **cheap templated-marketplace feel** (default-widget storefronts), and **sterile corporate-SaaS blandness** (cold gray dashboards) — even on the seller-facing config screens.

**Key Characteristics:**
- Warm cream canvas, single bordeaux accent used sparingly (≤10% of any screen).
- Mobile-first: thumb-reachable actions, ≥44px touch targets, pill CTAs, `env(safe-area-inset-bottom)` everywhere.
- Rounded geometry: containers capped at 26px (radius-xl); actions and chips go full pill (`999px`).
- Flat-by-default surfaces; shadows are soft, diffuse, warm-toned, and never paired with a hard border.
- Premium through space, hierarchy, and typographic care — not through more color or more decoration.
- Skeleton loaders for content, pastel toasts for feedback, error banners with clear retry affordances.

## 2. Colors

A warm cream-and-bordeaux palette: one saturated brand hue against a warm neutral canvas, with functional green reserved for status only.

### Primary
- **Bordeaux Signal** (`#D54A51`): The brand's one voice. Primary CTA fills, active chip states, floating cart bar, selected radio indicators, focus accents, the theme color. On a warm cream field it reads as confident and refined, not alarming.
- **Bordeaux Deep** (`#B63A41`): Pressed/active state for bordeaux elements. Appears in response to touch, never at rest.
- **Bordeaux Wash** (`#FDECEE`): Tinted fill behind selected cards, warning banners, delivery icons, and hover states on product cards, chips, and quantity controls.

### Secondary
- **Success Green** (`#119441`): Discounts, savings labels, free-shipping indicators, confirmed-order checkmarks, and completed timeline steps. Green is a status language, never a decoration.

### Neutral

The neutral ramp is **warm** end to end — every grey carries a faint red/brown cast so nothing on a Urbeat screen reads as cold "enterprise SaaS" grey.

- **Warm Cream** (`#ede9e3`): The primary page background across the entire product (`--app-bg`). Every surface floats on this.
- **Warm Background** (`#fbf7f2`): Secondary warm background for nested panels and the app-shell gradient.
- **Pure Surface** (`#ffffff`): Cards, sheets, inputs, modals, product cards, and all elevated containers.
- **Soft Surface** (`#fff8f0`): Barely-warm surface for selected or accent-tinted panels (active receive cards, active payment options).
- **Warm Hairline** (`#f3efe9`): Subtle fill for inactive chips, ghost backgrounds, and skeleton shimmer gradients.
- **Ink** (`#161616`): Primary text. Near-black, not pure black. Page titles, section headers, store names.
- **Primary Text** (`#171717`): Product names, strong labels, headings, emphasis.
- **Ink Soft** (`#3a3632`): Warm dark grey for secondary headings and structural type.
- **Slate Warm** (`#565049`): Warm mid grey for tertiary structure and metadata labels.
- **Secondary Text** (`#6f6f76`): Supporting labels, descriptions, secondary copy.
- **Muted Strong** (`#6f6f6f`): AA-safe tertiary text — the floor for visible copy. Never go below this for body text.
- **Muted Text** (`#8c8c91`): Input placeholders and purely decorative hints. Never used for readable content.
- **Muted Warm** (`#b5a89e`): Warm light grey for disabled states, inactive icon fills.
- **Border Light** (`#eadfd6`): Dividers, input strokes, card borders, subtle separators.
- **Border Medium** (`#d6d0cc`): Stronger borders for emphasis, disabled button fills.
- **Brown** (`#6c4634`): Inactive icons, tertiary icon accents on unselected receive/payment cards.

### Named Rules
**The One Bordeaux Rule.** Bordeaux Signal (`#D54A51`) appears on ≤10% of any screen — the CTA, the active state, the selected chip, the floating cart. Its scarcity is what makes it read as premium. Two bordeaux things competing for attention on one screen is a bug.

**The Status-Only Rule.** Green is a status language, not a palette color. Green means "saved/confirmed/free." Never use it to decorate or as a secondary accent.

**The Solid-Fill Rule.** Primary buttons use a solid Bordeaux Signal fill. The global `.btn-primary` class currently ships a linear gradient; treat that as a legacy exception to be phased out in favor of the solid-fill canonical style used by floating-cart bars, continue buttons, and category chips.

## 3. Typography

**Product Font:** Inter (with `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Arial, sans-serif`) — the entire product UI. Loaded via Google Fonts at weights 400, 500, 600, 700, 800.

**Landing Font:** Poppins — seller-recruitment landing page only. Never enters the product UI.

**Mono Font:** JetBrains Mono (with `ui-monospace, monospace`) — landing accents and order codes. In the product, order numbers use Inter 600.

**Character:** Inter runs from 400 (body) through 800 (headings) with tight letter-spacing on large type. The pairing is single-sans: one family in multiple weights is enough. Poppins is reserved for the marketing voice.

### Hierarchy
- **Headline** (Inter 700, 22px, lh 1.2): Page titles, section headers, store name on the store page. Uses `font-weight: 800` at the largest sizes (29px+ page titles).
- **Title** (Inter 700, 17px, lh 1.3): Card titles, product names, list-item headers, payment-option labels.
- **Body** (Inter 400, 15px, lh 1.5): Descriptions, running copy. Cap prose at 65–75ch. `text-wrap: pretty` on long blocks.
- **Label** (Inter 600, 12px, tracking 0.3px): Metadata, cart-item text, prices' supporting text, small captions, timeline step labels.
- **Mono** (JetBrains Mono 500, 13px): Landing technical accents and order codes. Product UI uses Inter for numeric data.

**Font-weight ceiling:** 800 is the absolute maximum across the entire system. Weight 850 appears in legacy code (`.page-title h1` in `global.scss:188`) — this is a bug; cap at 800.

**Letter-spacing floor:** −0.04em. Never go tighter. Headings use −0.035em to −0.04em tracking.

### Named Rules
**The Two-Voices Rule.** Poppins is for selling Urbeat to merchants on the landing page; Inter is for everything a customer or merchant actually *uses*. Never bring Poppins into the product UI or Inter into the landing hero.

**The Placeholder-Is-Not-Body Rule.** The codebase overrides browser-default placeholder opacity: `input::placeholder { color: var(--app-text-secondary) }` — secondary text, not muted. This is correct. Muted (`#8c8c91`) is reserved for tertiary metadata only. Real body text uses Primary Text or Secondary Text to hold AA contrast.

## 4. Elevation

Flat-by-default with soft, diffuse shadows for genuine elevation only. Depth comes primarily from the cream-vs-white tonal contrast (white surfaces float on `#ede9e3`), not from heavy shadows. Borders and shadows are never paired on the same element — choose one.

### Shadow Vocabulary
- **Small** (`--shadow-sm: 0 8px 22px rgba(33, 20, 8, .07)`): Cards, product cards, receive/address cards, category metric bars at rest.
- **Medium** (`--shadow-md: 0 14px 34px rgba(33, 20, 8, .09)`): Interactive surfaces, sticky headers, hovered cards (not actively used in current code; available).
- **Large** (`--shadow-lg: 0 30px 90px rgba(0, 0, 0, .18)`): Bottom sheets, modals, the app-shell on desktop breakpoints.
- **None** (`--shadow-none: 0 0 0 transparent`): The explicit "no shadow" reset, used to clear inherited shadows.

**Bordeaux glow** is used for interactive elevation on the floating cart bar (`0 10px 26px var(--app-brand-shadow)`) and active timeline dots (`box-shadow: 0 0 0 4px var(--app-brand-shadow)`). This is a purposeful identity signal, not generic depth.

### Named Rules
**The Soft-And-Wide Rule.** Shadow blur is always ≥ the vertical offset and opacity stays ≤0.18. If a shadow looks like a hard line under a card, it's wrong. Never pair a 1px border with a drop shadow on the same element — pick tonal separation OR one shadow.

**The Tonal-First Rule.** The primary depth mechanism is the cream background under white surfaces. Shadows are secondary. A card without a shadow on the cream canvas still reads as elevated.

## 5. Components

Every interactive component ships all seven states: default, hover, focus-visible, active, disabled, loading (skeleton), and error. No half-shipped components.

### 5.1 Buttons

All actions are pill-shaped (`999px`). This is a brand signature — rectangular buttons do not exist in the product UI.

- **Primary (`btn-primary`):** Bordeaux Signal solid fill (`#D54A51`), white text, Inter 700 16px, padding `16px 20px`, full-width on mobile, `letter-spacing: 0.3px`. Canonical style is solid fill. The existing class uses a linear gradient (brand → brand-dark); that is a legacy exception to be phased out.
- **Pressed:** Fills to Bordeaux Deep (`#B63A41`), scales to 0.98. No hover-lift on touch.
- **Disabled:** Replaces fill with border-medium (`#d6d0cc`), `cursor: not-allowed`, no shadow.
- **Secondary (`btn-secondary`):** Transparent, bordeaux text, no border, Inter 700 16px, padding `12px`. Presses to bordeaux-deep.
- **Ghost (`ghost-btn`):** Borderless, transparent background, inherits text color. For icon-only actions: remove, close, back.
- **Circle (`circle-btn`):** 54px × 54px, `border-radius: 50%`, near-white (`rgba(255,255,255,.96)`), shadow-sm. Used for back chevrons in hero overlays. Presses to scale 0.95.
- **Cart Button (`cart-btn` - product detail):** Full pill, 58px height, brand solid fill, white text, icon + two-line label (`13px` body / `15px` strong total). Flex layout, gap 10px. Disabled: `opacity: 0.5`, `cursor: not-allowed`.
- **Retry (`btn-retry` - error banners):** Pilled, white surface, border-light stroke, bordeaux text, 13px weight 600, padding `8px 16px`. Hover shifts border to brand.
- **Focus-visible:** All buttons get a ring: primary uses `box-shadow: 0 0 0 3px var(--app-brand-shadow)`.

All button transitions: `opacity 0.15s` / `transform 0.05s` (press), `border-color 0.15s` (hover). No spring or bounce curves.

### 5.2 Quantity Control (product detail + cart)

Used on the product-detail sticky bar and inside cart product cards.

- **Product-detail variant (`qty`):** 58px height, `border-radius: 18px`, `1.5px solid border-light`, white fill. Three equal zones: minus button (40px circle, brand text, hover → bordeaux-wash bg) / value (weight 800, ink, centered) / plus button (same as minus).
- **Cart variant (`cart-qty-pill`):** 46px height, `border-radius: 16px`, three-zone grid (30px / 36px / 30px), border-light stroke, white fill, `inset 0 0 0 1px rgba(255,255,255,.55)` inner highlight. Hover → bordeaux-wash bg; active → brand fill with white icon.
- **Minus/Plus buttons:** 30px circle, borderless, transparent, brand text. Font-size 15–17px.

### 5.3 Chips (category filters)

- **Style:** Pill (`999px`), white surface, `1px solid border-light`, padding `0 18px`, height 46px. Inter 15px weight 700.
- **Active:** Fills Bordeaux Signal, white text, weight 800.
- **Hover:** Fills Bordeaux Wash, border shifts to brand-shadow, text turns bordeaux. Active chips stay brand-filled on hover.
- **Layout:** Horizontal scroll row (`flex`, `nowrap`), no scrollbar, `overscroll-behavior-x: contain`, `touch-action: pan-x`. Right-edge fade via a 22px `::after` pseudo-element. Rendered inside a `<nav role="tablist">`.

### 5.4 Cards / Containers

- **Default card (`card`):** `border-radius: 18px` (`--radius-lg`), white surface, `1px solid border-light`, shadow-sm, padding `16px`. Never pair border and shadow on the same element.
- **Store panel sheet:** `border-radius: 34px 34px 0 0` (`--radius-2xl`), white surface, `margin-top: -58px`, `padding: 86px 22px 0`, z-index 8, `box-shadow: 0 -10px 26px rgba(0,0,0,.04)`. Pulled up over the hero; the overhang creates depth.
- **Product detail sheet:** `border-radius: 32px 32px 0 0`, white surface, `margin-top: -38px`, `padding: 28px 24px calc(22px + env(safe-area-inset-bottom))`, z-index 8.
- **Configuration card (option groups):** `border-radius: 16px`, white surface, `1px solid border-light`, shadow-sm, padding `15px 16px 14px`, `margin-bottom: 14px`. Houses variation/choice grids, flavor grids, check lists, and compact options.

### 5.5 Store-specific patterns

These patterns form the merchant storefront — the primary customer surface.

#### Store Hero
- **Dimensions:** 286px tall, full width. Background is ink (`#161616`) with an `<img>` filling `object-fit: cover`.
- **Overlay:** `linear-gradient(180deg, rgba(0,0,0,.2), rgba(0,0,0,.05) 42%, rgba(0,0,0,.44))`. Darker at bottom to transition into the pulled-up panel.
- **Hero actions:** Absolutely positioned, `left/right: 22px`, `top: calc(52px + env(safe-area-inset-top))`. Contains the circle back button.

#### Store Logo
- **Dimensions:** 144px × 144px, `border-radius: 50%`, white surface, `box-shadow: 0 12px 26px rgba(0,0,0,.14)`.
- **Position:** Absolute, `top: -74px` from the panel, `left: 50%`, `transform: translateX(-50%)`. Overhangs the hero-panel junction.
- **Inner image:** 132px × 132px, `border-radius: 50%`, `object-fit: contain`.

#### Store Title & Subtitle
- **Title:** Inter 800, 34px, `letter-spacing: -0.04em`, ink color, `margin: 0`. No clamp — fixed size for consistent identity.
- **Subtitle:** Inter, 20px, secondary text, `margin: 2px 0 22px`. Typically cuisine type.

#### Store Metrics Bar
- **Container:** `display: grid`, 3 equal columns, `1px solid border-light`, `border-radius: 18px` (radius-lg), white surface, shadow-sm, `padding: 14px 8px`, `margin-bottom: 22px`.
- **Metric item:** Flex row, centered, `gap: 8px`, `padding: 0 8px`, `border-right: 1px solid border-light` (last child has none).
  - **Icon:** Ionic icon, 17px, secondary text. Green (`#119441`) when store is open.
  - **Strong:** 15px, weight 800, ink. Green when status is "Aberta".
  - **Span:** 11px, muted text (`#8c8c91`), the metric label.

#### Search Box
- **Container:** `display: flex`, `gap: 14px`, centers the search pill and optional filter button.
- **Pill:** 62px height, `border-radius: 999px`, white fill, `1px solid border-light`, `padding: 0 18px`, shadow-sm (`0 7px 20px rgba(0,0,0,.03)`). Search icon (22px, muted) + borderless `<input type="search">` (16px, primary text).
- **Filter button (hidden by default):** 58px circle, border-light stroke, white fill, brand icon (24px), shadow-sm. Displayed when filters are active.

#### Product Card
- **Layout:** 3-column grid (`112px 1fr 42px`), `gap: 14px`, `align-items: center`, `padding: 12px`, white surface, `1px solid border-light`, `border-radius: 18px` (radius-lg), no shadow at rest.
- **Image:** 112×96px, `border-radius: 14px`, `object-fit: cover`.
- **Info column:** `min-width: 0` for text truncation.
  - **Name (h3):** Inter 800, 18px, `line-height: 1.12`, `letter-spacing: -0.035em`, primary text.
  - **Description (p):** Inter 400, 14px, secondary text, `line-height: 1.28`, clamped to 2 lines (`-webkit-line-clamp: 2`).
  - **Price (.price):** Inter 800, 17px, bordeaux, `letter-spacing: -0.02em`, `margin-top: 6px`.
- **Add button (.add-btn):** 36px circle, `border: 1.5px solid brand`, brand icon color, white fill, `justify-self: end`. Acts as a visual ">" affordance; actual navigation is on the whole card.
- **Hover:** Fills Bordeaux Wash (`#FDECEE`), border shifts to brand-shadow.
- **Active:** Scales to 0.985.
- **Focus-visible:** Same as hover; `outline: none`.

#### Floating Cart Bar
- **Position:** `position: sticky`, `bottom: calc(84px + env(safe-area-inset-bottom))`, `z-index: 25`.
- **Style:** 62px height, Bordeaux Signal fill, `border-radius: 999px`, white text, `box-shadow: 0 10px 26px var(--app-brand-shadow)`.
- **Grid:** Four columns: bag icon zone (with count badge) / item count / vertical rule (1px × 28px, `rgba(255,255,255,.35)`) / total price.
- **Badge:** White circle, 18px, brand text, positioned at top-right of bag icon.

#### Category Section Header
- **Style:** Flex, `padding: 18px 4px 2px`, `scroll-margin-top: 18px` for anchor scrolling.
- **h2:** Inter 800, 20px, `letter-spacing: -0.035em`, ink.

### 5.6 Product detail patterns

These patterns form the product detail page — variations, choices, additionals, and the add-to-cart flow.

#### Product Hero
- **Dimensions:** 340px tall (280px on ≤400px), ink background, product image `object-fit: cover`.
- **Overlay:** `linear-gradient(180deg, rgba(0,0,0,.12), transparent 52%, rgba(0,0,0,.38))`. Lighter than the store hero for product clarity.
- **Hero actions:** Same pattern as store hero: circle back button at `top: 52px`.

#### Title + Price Row
- **Layout:** Flex between, `gap: 14px`, `align-items: flex-start`.
- **Title (h1):** Inter 800, 33px, `letter-spacing: -0.06em`, `line-height: 1.1`, ink.
- **Price:** Inter 800, 30px, brand, `letter-spacing: -0.04em`, `white-space: nowrap`.

#### Product Description
- Inter, 17px, `line-height: 1.45`, muted-strong (`#6f6f6f`), `margin: 16px 0 28px`.

#### Section Title (variations, choices, additionals)
- **Layout:** Flex between, `margin-bottom: 16px`.
- **Heading (h2):** Inter 800, 27px, `letter-spacing: -0.04em`, ink.
- **Subtitle (span):** 16px, brand, right-aligned. Typically "Escolha 1 opção".

#### Required Label Badge
- Pill (`999px`), Bordeaux Wash bg, bordeaux text, 11px weight 800, `padding: 4px 9px`, `min-height: 22px`. Appears next to option group names to indicate mandatory selection.

#### Flavor Note
- 12px, `line-height: 1.35`, with weight 800 strong in `#1b1b1f`. Explains pricing mode ("O valor será o item mais caro").

#### Variation / Choice Buttons (`og-buttons`)
- **Grid:** 3 columns (`repeat(3, minmax(0, 1fr))`), `gap: 12px`. Falls to 2 or 1 on narrow screens.
- **Button (`og-btn`):** `min-height: 62px`, `border-radius: 10px`, `1px solid border-light`, white fill, `box-shadow: 0 6px 15px rgba(33,20,8,.035)`. Flex-column, centered text.
  - **Name (strong):** 12px weight 700, ink.
  - **Price (small):** 10px, `#7a7a80`, weight 500.
- **Active:** Border shifts to brand, bg → Bordeaux Wash, shadow → `0 8px 18px var(--app-brand-shadow)`.
- **Check badge:** 22px circle, brand fill, white checkmark, `position: absolute`, `top: -8px`, `right: -8px`, `border: 2px solid white`, `box-shadow: 0 6px 14px var(--app-brand-shadow)`. Only visible on `.active`.

#### Flavor Grid (`displayStyle: buttons`)
- **Grid:** 2 columns (`repeat(2, minmax(0, 1fr))`), `gap: 10px`.
- **Card (`flavor-card`):** `min-height: 58px`, `border-radius: 13px`, `1px solid border-light`, white fill, padding `13px 14px`. Hidden `<input type="checkbox">` inside.
  - **Name (strong):** 13px weight 800, ink block.
  - **Price (span):** 12px weight 800, ink block.
- **Active:** Same as variation button: brand border, Bordeaux Wash bg, check badge appears.
- **Hover:** Brand border + Bordeaux Wash.

#### Chip Grid (`displayStyle: chips`)
- **Layout:** `flex-wrap`, `gap: 10px`.
- **Chip (`acai-chip`):** Inline flex, `min-height: 42px`, `border-radius: 12px`, `1px solid border-light`, white fill, `padding: 8px 12px`, Inter 14px.
  - **Checkbox (`chip-box`):** 22px, `border-radius: 6px`, `1.6px solid #cfcfd3`. Checked → brand fill + brand border.
  - **Name:** Text, color `#222`.
  - **Price (small):** 12px weight 600, brand.
- **Checked state:** Brand border, Bordeaux Wash bg.

#### Check List (`displayStyle: list` + additionals)
- **Layout:** Vertical grid, no gap.
- **Row (`check-row`):** Flex between, `padding: 14px 0`, `border-bottom: 1px solid border-light`, Inter 18px. Hidden `<input>` inside for accessibility.
  - **Checkbox (`box`):** 24px, `border-radius: 7px`, `2px solid border-medium`. Checked → brand fill + brand border, white checkmark icon (14px).
  - **Label (`check-name`):** Flex, `gap: 14px`, ink.
  - **Price:** Brand, weight 700, 15px.
- **Hover:** `rgba(253,236,238,.3)` background.
- **Last row:** No bottom border.

#### Compact Options (`displayStyle: checkbox`)
- **Grid:** 2 columns (`repeat(2, minmax(0, 1fr))`), `gap: 8px`.
- **Option (`compact-option`):** `min-height: 48px`, `border-radius: 9px`, `1px solid border-light`, white fill, `padding: 8px 10px`, Inter 11px. Hidden `<input type="checkbox">`.
  - **Checkbox (`compact-box`):** 18px, `border-radius: 5px`, `1.5px solid #d2d2d6`.
  - **Name (strong):** 11px weight 800, block.
  - **Price (small):** 10px, `#77777e`.
- **Checked:** Brand border + Bordeaux Wash + brand-filled checkbox.
- **Hover:** Brand border + Bordeaux Wash.

#### Notes Input
- **Style:** 54px height, `border-radius: 11px`, `1px solid border-medium`, white fill. Three-column grid: pencil icon (20px, `#777`) / `<input>` (16px, `#444`) / character counter (11px, right-aligned).
- **Placeholder:** "Ex.: sem cebola, molho à parte..." — muted text color.

#### Sticky Bottom Bar (product detail)
- **Position:** `position: sticky`, `bottom: 84px`, `z-index: 26`. Two-column grid: quantity control (128px) / cart button (1fr), `gap: 12px`.
- **Background:** `linear-gradient(180deg, transparent, white 32%)` to fade the sheet content behind it.

#### Back to Menu Link
- Centered, bordeaux text, 18px weight 800, `margin: 18px 22px 20px`, `cursor: pointer`. Hover underlines.

### 5.7 Inputs / Fields
- **Search pill:** Pill shape (`999px`), 62px height, white fill, border-light stroke, 18px horizontal padding. Icon + borderless input inside. Focus: no visible ring shift; the pill container carries the visual weight.
- **Form fields:** `18px` radius (`--radius-lg`), white fill, border-light stroke, padding `14px 16px`. Focus shifts border to bordeaux.
- **Placeholder:** Uses secondary text (`#6f6f76`), not muted — the global stylesheet enforces this explicitly via `input::placeholder, textarea::placeholder`.

### 5.8 Navigation
- **Page head:** Three-column grid (54px back button / 1fr title / 54px empty), `padding: 14px 22px 22px`. Title centered, Inter 800, 22–29px, `-0.03em` to `-0.04em` tracking.
- **E-commerce bottom actions:** Full-width pill buttons with `env(safe-area-inset-bottom)` padding. Continue/checkout buttons use a three-column grid: icon / label+subtitle / chevron.

### 5.9 Selectable Cards (receive, payment, address)
- **Style:** White surface, border-light stroke, radius-lg, shadow-sm, padding 18–20px. Grid layout with icon, text, and status indicator.
- **Active:** Border shifts to brand (`#D54A51`), background tints to surface-soft (`#fff8f0`), radio dot fills with brand concentric circles (24px dot, 2px border, checked → brand fill with white inset).
- **Disabled:** Opacity 0.45, grayscale 0.6, `not-allowed` cursor, `pointer-events: none`.
- **Hover:** Border shifts to border-medium on inactive cards.

### 5.10 Toasts (Ionic)
- **Shape:** 12px radius, soft shadow (`0 4px 16px rgba(0,0,0,0.10)`), compact (36–48px max-height).
- **Variants:** Pastel semantic tones — error (`#fee2e2` bg / `#991b1b` text), success (`#dcfce7` bg / `#166534` text), warning (`#fef3c7` bg / `#92400e` text), info (`#dbeafe` bg / `#1e40af` text). Inter 500, 13px.
- **Grouped variant:** Multi-line, taller, left-aligned for error lists. `line-height: 1.55`.
- **Client-side toast (product detail):** White pill surface, shadow-lg, primary text, fixed at bottom-center. Used for inline "Item adicionado" feedback.

### 5.11 Skeleton Loaders
- **Pattern:** Shimmer gradient (`border-light → hairline-warm → border-light`) with `background-size: 200% 100%` animated across. 1.5s ease-in-out infinite.
- **Shapes:** Image placeholders (same dimensions as real content, `border-radius: 18px`), title lines (20px height, 70% width), description lines (14px height, 90% width), price lines (18px height, 40% width), pill shapes (110px × 46px, `border-radius: 16px` for quantity controls), summary rows (60% width, 18px height).
- **Reduced motion:** Fall back to a static `border-light` fill; no animation.

### 5.12 Modal / Bottom Sheet
- **Overlay:** `rgba(0,0,0,.45)` (cart/payment modals) or `rgba(0,0,0,.4)` (checkout bottom sheets), fixed, flex-centered.
- **Sheet:** White surface, `radius-xl` top corners (`26px 26px 0 0`), max-height 80vh, overflow-y auto. Close button: 36px circle, `rgba(0,0,0,.05)` bg, positioned top-right.
- **Actions:** Stacked buttons at the bottom, no footer bar on sheets.

### 5.13 Error Banners
- **Style:** Red-tinted background (`#fef2f2`), red border (`#fecaca`), `border-radius: 14px` (radius-md), padding `14px 16px`.
- **Layout:** Icon (brand, 22px) + text block (strong title + secondary description) + optional retry button (pilled, white, border-light, brand text, 13px weight 600).
- **Context:** Appears in-store (page-level errors), in cart, and during checkout. Not a toast; stays inline until dismissed or resolved.

### 5.14 Empty States
- **Store:** Centered `<p class="empty">` with secondary text, `padding: 32px`. Content: "Nenhum item encontrado." Appears when a search or filter returns no results.
- **Cart:** Centered layout with large bag icon (64px, muted), heading (20px weight 800), description (secondary text), and a full-width primary button to return to the store.

## 6. Do's and Don'ts

### Do:
- **Do** keep Bordeaux Signal (`#D54A51`) to ≤10% of any screen — one CTA, one active state, one floating bar.
- **Do** let the cream canvas (`#ede9e3`) and white surfaces carry depth; keep shadows soft and wide.
- **Do** design for the thumb first: full-width pill CTAs, ≥44px touch targets, `env(safe-area-inset-bottom)`.
- **Do** use Inter exclusively in the product UI; Poppins is for the landing page only.
- **Do** reserve green strictly for status (savings, confirmation, free-shipping indicators).
- **Do** cap font-weight at 800; use design tokens (`var(--app-*)`) for all colors.
- **Do** use solid Bordeaux Signal fill for canonical primary buttons — the gradient is legacy.
- **Do** include skeleton loaders for every async content area; never ship a blank screen with a spinner.
- **Do** respect `prefers-reduced-motion` with instant transitions, static skeletons, and no animation.

### Don't:
- **Don't** build the **loud, ad-saturated big-delivery-app look** — no banner stacks, no dense promo grids, no aggressive upsell.
- **Don't** ship a **cheap templated-marketplace** feel — every store should read as *that merchant's* place.
- **Don't** let seller config screens turn into **sterile corporate SaaS** — keep the warmth through cream bg and brand accent.
- **Don't** pair a `1px` border with a drop shadow on the same element.
- **Don't** use font-weight 850 or 900 — 800 is the ceiling. Fix the `.page-title h1` 850-weight bug.
- **Don't** introduce a second brand accent to compete with bordeaux.
- **Don't** use gradient fills on buttons — solid color only. Phase out the `.btn-primary` gradient.
- **Don't** use green (#119441) for decoration or as a non-status color. It means saved, confirmed, free — nothing else.
- **Don't** use muted text (`#8c8c91`) for body copy or labels — it fails AA contrast on white. Use secondary text (`#6f6f76`) or muted-strong (`#6f6f6f`) at minimum.
- **Don't** use Poppins in any product UI surface — product is Inter-only.
- **Don't** default to a modal when inline expansion, a bottom sheet, or an inline state change would work. Exhaust alternatives first.
- **Don't** use `box-shadow` values greater than `0.18` opacity or blur tighter than the vertical offset.
