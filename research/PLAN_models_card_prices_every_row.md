# PLAN — a Models card shows the catalog price of every row, not only of a reviewer

> Status: **IMPLEMENTED, 2026-10-09 — to ship in the next extension release.** **Deviations:** the function lives in the
> price service (`priceBook.ts`, `cardPrices` and `billedRoute`), not `modelPrices.ts`; the guard test was renamed
> `theCardsArePricedFromEveryRow.test.ts`; the tabs' by-model map now shares `billedRoute` (the plan round). Scope was:
> the extension's price map for the Settings page (`panelProvider.ts`, `panelView.ts` `cardContextFor`, `priceBook.ts`).
>
> Related docs: [module_extension.md](module_extension.md) (*E5.3 — what the old page left behind*),
> [PLAN_one_model_catalog.md](PLAN_one_model_catalog.md).

## Symptom

Found during catalog E5.3 (2026-10-09). A Models card reads its catalog price as `state.modelPrices[v.model]`
(`panelView.ts`, `cardContextFor`, the `price:` field), and the panel builds that map from the REVIEWERS only:
`modelPrices: await this.modelPrices(shown)` where `shown = vendors.filter(isReviewerRow)` (`panelProvider.ts`, the
render's state). So a row that reviews nothing — a consultant-only or chat-only model, which Models draws like every
other row since E4 — shows no catalog price unless a reviewer happens to use the same model.

The reviewers-only list is deliberate and must stay true for what it protects: the map is keyed by MODEL and an `api`
row's routed price is spread last, so pricing every row let a hidden api consultant put ITS endpoint's rate on a
reviewer's card for the same model (PR #681's review; `theOldPagePricesWhatItShows.test.ts`).

## Goal

Every Models card shows the catalog price of its own row, and no row's price can be overwritten by another row's
endpoint. The spending and consultation tabs, which price every row on purpose, are unchanged.

## Design

The consumers were read (2026-10-09): `PanelState.modelPrices` has ONE reader, `cardContextFor`'s `price:` in
`panelView.ts`, which the Models cards use. The spending tab (`usageTab`) and the consultation tab
(`consultationsTab`) are handed their own map from `this.modelPrices(vendors)` — every row and the chat presets, by
model — and do not read the state's field. So only the cards' map changes.

1. **A pure function in the price service**, `cardPrices(rows, priceOf)` in `priceBook.ts`: for each row, the price of
   ITS model on ITS route — an `api` row's `baseUrl`, `''` for every other runtime, exactly as `modelPrices` routes
   today — keyed by the row's `id`. A row the lists do not know (or with no model) has no entry. The provider calls it
   with `PRICE_BOOK.priceOf` over EVERY configured row (the render's `vendors`, which it also hands the page as
   `catalogRows`), after the same `refreshPriceTables()`.
2. **The state field becomes `cardPrices`** (keyed by row id, documented so), and `cardContextFor` reads
   `state.cardPrices[v.id]`. Keyed by row, no row's route can land on another row's card — the guarantee the
   reviewers-only list was protecting holds by construction, so the list goes from the price call (it stays for
   `vendors: shown`).
3. **The by-model `modelPrices(vendors)`** stays as it is for the two tabs, except that it routes an `api` row through
   the same `billedRoute` helper as the cards (the plan round), so a row's card and its runs share one route.

Rejected: keeping the field by model and pricing every row — two rows on one model and two endpoints would still share
one rate, which is the bug's other half.

## Build order

1. RED: `cardPrices` lands with today's semantics (reviewer rows, by model) and a Models-card test that RUNS the page with a consultant-only row on a model no reviewer uses and asserts its
   price is drawn; and one with an api consultant and a reviewer on the same model, each card showing its own rate.
2. The by-row price map and `cardContextFor` reading it.
3. GREEN, then the break-it check (the plan round): reading by model turns the two-routes test red; pricing only the
   reviewers turns the consultant-only test red; the render pricing `shown` turns the wiring test red.
4. Docs: `research/module_extension.md`; `theOldPagePricesWhatItShows.test.ts` (renamed `theCardsArePricedFromEveryRow.test.ts`) updated to the new guarantee (was: the
   price call takes `shown`; now: it takes every row and the card reads by id); the ~30 test states that set
   `modelPrices: {}` renamed.

## Test plan

- The two RED tests above, through `pageTree` + the page's own script.
- The spending and consultation tabs: `pricesInPanel.test.ts` (`totalsByVendor` with listed prices) and the usage and
  consultation tests keep passing unchanged — their map is `modelPrices(vendors)`, not the cards'.
- `npm test`, eslint, the family checks.

## Definition of Done

- [x] A consultant-only row's Models card shows its catalog price (RED first, teeth shown).
- [x] Two rows on one model and two endpoints each show their own rate.
- [x] The spending and consultation tabs price exactly what they did.
- [x] Docs updated; plan promoted when done.
