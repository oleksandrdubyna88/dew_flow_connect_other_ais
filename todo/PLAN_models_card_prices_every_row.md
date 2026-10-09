# PLAN — a Models card shows the catalog price of every row, not only of a reviewer

> Status: **plan only, nothing implemented yet (2026-10-09).** Scope: the extension's price map for the Settings page
> (`src_vs_code/src/panelProvider.ts`, `panelView.ts` `cardContextFor`, `modelPrices.ts`).
>
> Related docs: [module_extension.md](../research/module_extension.md) (*E5.3 — what the old page left behind*),
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

## Design (to be reviewed)

Key the map the Models cards read by ROW (`id`), not by model: `modelPrices(rows)` computes each row's price from its own
model and its own route, so two rows on the same model and different endpoints each keep their own rate. The reviewers'
by-model map stays for the old consumers that need it, or they move to the by-row map in the same change — decided by
reading each consumer (`panelProvider.ts` lines that call `this.modelPrices`, `roundsLog.ts`, `vendorsWire.ts`).

## Build order

1. RED: a Models-card test that RUNS the page with a consultant-only row on a model no reviewer uses and asserts its
   price is drawn; and one with an api consultant and a reviewer on the same model, each card showing its own rate.
2. The by-row price map and `cardContextFor` reading it.
3. GREEN, then the break-it check (the old by-model read turns the first test red).
4. Docs: `research/module_extension.md`; `theOldPagePricesWhatItShows.test.ts` updated to the new guarantee.

## Test plan

- The two RED tests above, through `pageTree` + the page's own script.
- `npm test`, eslint, the family checks.

## Definition of Done

- [ ] A consultant-only row's Models card shows its catalog price (RED first, teeth shown).
- [ ] Two rows on one model and two endpoints each show their own rate.
- [ ] The spending and consultation tabs price exactly what they did.
- [ ] Docs updated; plan promoted when done.
