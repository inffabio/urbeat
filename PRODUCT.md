# Product

## Register

product

## Users

Urbeat is a two-sided delivery marketplace, and both sides matter equally:

- **End customers** browsing a merchant's store on their phone, adding items to a cart, checking out, paying, and tracking a live delivery order. Context: on mobile, often hungry/impatient, deciding quickly, frequently on the go or at home. Job to be done: find what they want, order it with minimal friction, and know when it arrives.
- **Sellers / merchants** (small local business owners) setting up and running their store: configuring hours, delivery zones, products, and publishing. Context: often non-technical, doing this between other tasks, wanting to look professional without effort. Job to be done: stand up a credible storefront fast and manage orders confidently.

## Product Purpose

Urbeat lets local merchants create a delivery storefront and sell directly to customers, and lets customers order and track deliveries from those stores. It exists to give small businesses a professional, trustworthy online ordering presence without the density, noise, or commodity feel of the big delivery apps. Success looks like: sellers publish a store they're proud of, and customers complete orders confidently on their phones with minimal drop-off.

## Brand Personality

Local, trustworthy, premium. Warm but sharper than expected — the product is composed and confident, not soft. The voice is clear, reassuring, and decisive — a well-run neighborhood shop with a point of view, not a faceless platform. Bordeaux and cream carry the warmth; typography and spacing carry the premium restraint. It should feel curated and credible: the kind of place you'd trust with your card and your dinner, run by someone who knows what they're doing.

## Anti-references

- **Generic iFood / Uber Eats clone.** No loud, dense, ad-saturated, banner-stacked big-delivery-app aesthetic. Avoid visual overload and aggressive upsell.
- **Cheap templated marketplace.** Nothing that reads as a low-effort Shopify/template store — no stock-generic layouts, no default-widget look.
- **Sterile corporate SaaS.** No cold gray enterprise-dashboard blandness, even on the seller-facing config screens.
- **Design-token drift.** No competing color systems, no hard-coded hex values, no diverging font stacks. Every surface pulls from the same token source. If a value appears twice, it's either the same token or a deliberate new one.

## Design Principles

- **Local over platform.** Every store should feel like *that merchant's* place, not a Urbeat template. The chrome recedes; the merchant's identity leads.
- **Trust is earned in the details.** Clear pricing, honest delivery estimates, unambiguous states, and no dark patterns. Confidence comes from precision, not persuasion.
- **Mobile is the real product.** The phone experience is primary, not a shrunk-down desktop. Thumb-reachable actions, generous touch targets, fast perceived performance.
- **Premium through restraint.** Warmth and quality come from space, hierarchy, and typographic care — not from more color, more cards, or more decoration.
- **Frictionless to the finish.** The path from browse to paid-and-tracked is the product. Remove steps, defaults over prompts, no dead ends.
- **Consistency is clarity.** Every screen uses the same brand accent, the same type scale, the same interaction patterns. When the user learns it once, they know it everywhere. Divergence is friction; unity is speed.
- **Error before elegance.** A beautiful interface that silently breaks is broken. Every async call has an error handler, every image has a fallback, every interactive element covers all states. Production-readiness ships before visual refinement.

## Accessibility & Inclusion

- Target **WCAG 2.1 AA**: body text ≥4.5:1 contrast, large text ≥3:1, including placeholder text.
- **Mobile-first priority** — most customers order on phones; optimize touch targets (≥44px), thumb reach, and readable type at small sizes.
- Honor `prefers-reduced-motion` with crossfade/instant fallbacks for every animation.
- Full keyboard and screen-reader support on forms and checkout; visible focus states throughout.
