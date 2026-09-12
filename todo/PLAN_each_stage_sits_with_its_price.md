# PLAN — each stage box sits with the price it belongs to

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/panelView.ts` — the
> reviewer card's markup and the panel stylesheet — and `src_vs_code/src/test/panelView.test.ts`.
>
> Issue [#124](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/124): *"Right
> now `reviews plans` and `reviews code` are in a row and it runs together with the model's own
> checkbox. Move the prices to the middle, and put `reviews plans` after the in price and
> `reviews code` after the out price"* — with a screenshot of the wanted layout.

## The symptom

A reviewer card (`vendorCard`, `src_vs_code/src/panelView.ts:809`) renders in this order:

```
[x] codex  ▶ ⤓ ⟳  remove        <- the head: the vendor's own switch
    [ model select ]
    codex · models the Codex CLI has cached
    ☐ reviews plans   ☐ reviews code      <- the stages row
    [ CLI path ]
    ? $ / 1M in   [ 0.00 ]
    ? $ / 1M out  [ 0.00 ]
```

Three checkboxes are stacked within four lines of each other — the vendor's master switch in the
head and the two stage boxes two lines below it — with nothing between them but a select. They read
as one group of three, which is what the issue means by *сливается*: the master switch is a
different KIND of decision (does this vendor review at all) from the two stage boxes (which stages),
and nothing in the layout says so.

Meanwhile the two price rows are `.field inline` — `justify-content: space-between` — so each is a
label at the left and a 64px number at the right with a gap of empty card between them. There is
room on those rows and a crowd four lines above them.

## What must be true when this is done

1. `reviews plans` sits on the **in-price row**, after the number; `reviews code` sits on the
   **out-price row**, after its number. The standalone stages row is gone for any vendor that has
   price rows.
2. Each of those rows reads label → price → stage box, with the price between the two — the
   *"пододвинуть на средину ценники"* of the issue.
3. **A Team-server row keeps its stages row.** `runtimeFields` returns `''` for a remote vendor
   (`panelView.ts:790`) — its CLI runs on the server, its price is the company's subscription, so it
   has no price rows to carry a stage box. Moving the boxes unconditionally would delete both stage
   controls from every remote reviewer, which is the one way this change could do real damage: a
   Team-server reviewer that cannot be told which stages it serves.
4. The disabled/dimmed behaviour survives in both layouts: when a vendor is switched off, both stage
   boxes stay visible and go `disabled`, and their container carries the `off` class that dims them
   (`.vendor .stages.off`, `panelView.ts:1633`). The rule the gate settled on 2026-09-01 — the boxes
   are readable but inert, because a tickable box that means nothing is the defect — is unchanged.
5. Nothing about what is SAVED changes: both boxes keep `data-setting="plan"` / `data-setting="code"`
   and `data-vendor`, so `settingWrite` and the settings manifest are untouched.

## The change

- **One `stageBox` function** renders a stage control, wrapped in `<span class="stages">` — the same
  class the standalone row carries, so the ONE dimming rule `.vendor .stages.off` reaches it in both
  layouts. Raised on the plan round by two vendors, and they were right: moving the boxes out of the
  `.stages` container without this leaves a switched-off hosted vendor's boxes bright while a remote
  vendor's dim.
- `runtimeFields` (`panelView.ts:789`) takes the two boxes and places each after its own price input.
- `vendorCard` (`panelView.ts:809`) renders the standalone `.field stages` row **when the price rows
  came back empty** — keyed off whether they were actually rendered, not off `runtime === 'remote'`.
  The condition in the code is then the condition that matters ("nothing to hang them on") rather
  than a proxy for it. Also raised on the plan round.
- CSS — the priced row is its own class rather than a third child squeezed into `.inline`, because
  `.inline`'s label is `flex: 1 1 auto` and would absorb the slack, bunching the price and the box
  together at the right edge instead of putting the price between them (raised on the plan round,
  gemini — the "falls out of the existing rule" sentence in the first draft was wrong):

  ```
  .vendor .priced { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .vendor .priced > label  { flex: 1 1 6rem; min-width: 0; margin-bottom: 0; }
  .vendor .priced > input[type="number"] { flex: 0 0 64px; width: 64px; }
  .vendor .priced > .stages { flex: 1 1 8rem; min-width: 0; }
  ```

  Two flexible children of equal weight with the fixed number between them: the price sits between
  the label and the box at any width that fits. **What wraps, and in what order, is decided rather
  than left to chance** — the label and its price share a first line (6rem + 64px), the stage box
  takes the next one, because a price separated from its own label reads as belonging to neither.
- `research/module_extension.md`: the card's anatomy paragraph.
- `src_vs_code/src/helpContent.ts:99` — the *choose-reviewers* article describes the row's parts in
  order ("a checkbox to include it, a model picker, … its price per million tokens, and remove") and
  must name where the stage boxes are now.
- `CHANGELOG.md` under `## Unreleased`; the release commit for this batch names the version.
- This plan promoted to `research/` in the branch's last commit.

## Test plan (RED first)

| # | Test (`src_vs_code/src/test/panelView.test.ts`) | RED symptom expected |
|---|---|---|
| 1 | *each stage box sits on the row of the price it belongs to*: in a hosted vendor's card the `reviews plans` box is inside the same priced row as `id="price-in-<id>"` and AFTER that input, `reviews code` likewise with `price-out-`; there is **exactly one** control of each kind in the card, and **no** standalone `class="field stages"` row | both boxes are in a `.field stages` row of their own, before the prices |
| 2 | *a Team-server row keeps its stage boxes, having no prices to put them on*: a `runtime: 'remote'` vendor renders no `price-in-`/`price-out-` input, and has **exactly one** `class="field stages"` row carrying **both** controls | (green before, and the guard against the damaging way to implement this) |
| 3 | *a vendor that is switched off has its stage boxes dimmed in BOTH layouts*: for a hosted card and for a remote card, every element carrying a `data-setting="plan"`/`"code"` control is inside something with `class="stages off"`, and both inputs are `disabled` | the hosted card's boxes are outside any `.stages`, so the `off` class reaches nothing and they stay bright |
| 4 | the existing *every reviewer row offers the two stages, ticked unless narrowed* (`panelView.test.ts:706`) keeps passing for both card shapes | green before and after |

Run, from a checkout with the extension's dependencies installed (`npm ci` in `src_vs_code`, Node 22
as CI uses): `cd src_vs_code && npm test` — the whole suite; `settingsAreDeclared`, `helpCoverage`,
`pricesInPanel` and the duplicate-selector guard all read this card.

## Definition of Done

- [ ] Tests 1 and 2 written first and watched fail/pass as stated; then green; then red again with
      the fix reverted, and green with it restored.
- [ ] `npm test` green in the worktree; the count reported in the pull request.
- [ ] The diff through the `coai` code round, every finding resolved.
- [ ] `research/module_extension.md`, `helpContent.ts` and `CHANGELOG.md` updated as named above.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
