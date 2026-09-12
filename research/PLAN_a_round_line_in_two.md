# PLAN — a reviewer's line breaks where the model made it long

> Status: **IMPLEMENTED, 2026-09-12.** `ReviewerRow` is `{ provider, rest, said }`; the sidebar puts
> the identity on one line and the status, indented, on the next, inside one `.reviewer` element;
> `reviewerLines` joins all three and the rounds log page is untouched.
>
> **The code round found a defect this plan introduced.** Normalising the status meant reading
> `state.status.length`, and a session file that omits `status` or carries a number reached it and
> threw — one malformed reviewer blanking the whole Active rounds view. `rounds.ts` had learned this
> once already, for `model`, and the same type check now guards the status; whitespace is normalised
> with it. Two vendors raised it independently.
>
> Three more deviations from the plan, all from that round. **The detail survives a missing status**:
> the first fix suppressed `(30 s)` along with the blank status, hiding a duration the file states as
> a fact — though the finding COUNT stays gated on `done`, as it was long before this change, because
> a count from an unfinished reviewer is not a result. **`.reviewer` gained `overflow-wrap: anywhere`**:
> splitting the status onto its own line fixed where the status goes and not what a 30-character model
> id does to the line above it. And the plan was still sitting in `todo/` marked *plan only* when the
> round read the diff — correctly reported as a convention failure rather than as a nit.
>
> Of the plan round's eleven findings seven were accepted and four rejected; of the code round's
> eighteen, seven accepted and eleven rejected with reasons. The largest rejection is architectural:
> a reviewer proposed keeping the row structured and formatting per renderer, which is precisely what
> `rounds.ts`' own docstring forbids — two renderers building the same sentence independently drift,
> and this change adds a second seam to the one builder rather than a second builder.
>
> Related docs: [module_extension.md](module_extension.md).
>
> Issue [#132](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/132): *"when we
> show the model the line gets very long, so break it into two lines —
> `local/Architecture · Qwen3.5-35B-A3B-Q5_vk128:latest` on one, `done (3 findings, 30 s)` on the
> second, with about five spaces of indent."*

## The symptom

A running round's card lists one line per reviewer (`roundCard`, `src_vs_code/src/panelView.ts:1520`),
built by `reviewerRows` in `src_vs_code/src/rounds.ts`:

```
local/Architecture · Qwen3.5-35B-A3B-Q5_vk128:latest — done (3 findings, 30 s)
```

The model name was added to that line on 2026-09-08 and it doubled its length. The sidebar is narrow
— the round card's own head was split into three lines for exactly this reason, after a branch name
was cut off with an ellipsis where it got interesting — and `.reviewer` wraps
(`white-space` is not set, so a long line reflows), which puts the status under the middle of a model
id rather than anywhere meaningful. A model id is a single unbreakable token of 30-odd characters, so
the wrap point is wherever it happens to land.

## What must be true when this is done

1. In the sidebar a reviewer is **two lines**: what it IS
   (`local/Architecture · Qwen3.5-35B-A3B-Q5_vk128:latest`) and, under it, indented, what it is DOING
   (`done (3 findings, 30 s)`).
2. **The rounds log page is unchanged.** It renders the reviewer as ONE line through `reviewerLines`
   (`roundsLog.ts:423`), searches it as one string and exports it as one — the seam the panel needs
   must not reach it. `reviewerLines`' output stays byte-identical, which the existing
   `rounds.test.ts` assertions pin verbatim.
3. A reviewer with no model reads exactly as it does today, split the same way — the second line is
   the status either way, so nothing depends on the model being present.
4. The vendor's word keeps its colour on the first line, and only the vendor's word: `.who` is
   inline-styled from `vendorPalette` and there is deliberately no `.who` CSS rule
   (`vendorClassIsNotACard.test.ts` enforces that a vendor's class is not a card).

## The change

- `rounds.ts` — `ReviewerRow` gains a third field. Today it is `{ provider, rest }`, split at the
  vendor's name so the panel can colour that word and the export cannot. The same reasoning applies
  one seam along: split again at the em dash, into what the reviewer IS and what it SAID.

  ```ts
  export interface ReviewerRow {
    readonly provider: string;
    /** The role and the model — what this reviewer IS. Starts at the slash. */
    readonly rest: string;
    /** The status and its detail — what it is DOING. No leading dash; the joiner owns that. */
    readonly said: string;
  }
  ```

  `restOf` returns the two halves it already builds; `reviewerLines` becomes
  `` `${provider}${rest} — ${said}` `` and so returns exactly what it returns today. **One builder,
  two seams** — the file's existing rule, and the reason its docstring gives: two renderers building
  the same sentence independently would drift.

  **Nothing splits or parses a rendered string, and this is the point the plan round pressed on
  hardest** (four findings, three of them assuming the opposite). `restOf` already HOLDS
  `state.role`, the normalised model and the detail list as separate values, and composes them into
  one sentence; the change is that it returns two of its own pieces instead of concatenating them.
  So a model whose name contains an em dash, or a reviewer with no model at all, cannot confuse a
  separator that is never looked for — and a regression case with a dash inside the model id proves
  it rather than asserting it.

  **An empty status yields no second line and no dangling dash.** `said` is built from
  `state.status`, which a hand-edited or foreign session file can leave blank; today that renders
  `…/Architecture — ` with a trailing dash, and under the change it would add an indented empty row.
  Both the joiner and the panel omit their part when `said` is empty. This is the one case where
  `reviewerLines`' output CHANGES, and it changes from a dangling dash to no dash — recorded here
  because the plan's own promise is that the log page does not move.
- `panelView.ts:1520-1524` — **one `<div class="reviewer">` per reviewer, as now**, holding the
  identity line and, under it, a `<div class="said">`. Not two sibling divs: one reviewer is one
  element, and splitting it into two would be the first place in this card where a row is not a row
  (raised on the plan round). The vendor's colour stays on the `.who` span alone, inside the first
  line.
- One CSS rule: `.reviewer .said { margin-left: 16px; }` beside the existing `.reviewer`
  (`panelView.ts:1754`), whose own `margin-left: 8px` the inner line inherits as its origin — 24px
  from the card's edge, about the five spaces the issue asks for. Not five literal spaces: the panel
  is not a monospace surface and a run of `&nbsp;` would be markup pretending to be layout.
- `research/module_extension.md` — the round card's paragraph.
- `CHANGELOG.md` under `## Unreleased`; the release commit for this batch names the version.
- This plan promoted to `research/` in the branch's last commit.

## Test plan (RED first)

| # | Test | RED symptom expected |
|---|---|---|
| 1 | `rounds.test.ts`: *a reviewer row is split into what it is and what it said* — for a reviewer with a model, `rest` is `/Architecture · <model>` with no status in it, and `said` is `done (3 findings)` with no leading dash | `said` does not exist; `rest` carries the whole sentence |
| 2 | `rounds.test.ts`: the existing verbatim `reviewerLines` assertions — with a model, without one, with a blank one, with a non-string one, and from a round predating the field — stay **exactly** as they are and pass untouched | green before and after: this is the guard that the log page did not move |
| 3 | `activeRounds.test.ts`: *the sidebar breaks a reviewer over two lines* — one `<div class="reviewer">` per reviewer, carrying the provider and the model, with a `<div class="said">` inside it carrying the status; for a reviewer with a model AND for one without | one div carries the whole sentence |
| 4 | `panelView.test.ts`: the stylesheet has a `.reviewer .said` rule whose `margin-left` is **exactly 16px** — a value, not merely the property, or a rule setting `0` would satisfy it (raised on the plan round) — and (the existing guard) no selector is defined twice | no such rule |
| 5 | `rounds.test.ts`: *a model with an em dash in its name is not mistaken for a separator* — a reviewer whose model is `Qwen — custom` keeps the whole id on the identity half and `done (…)` on the other. The regression case for the split-by-parsing the plan does NOT do | green by construction; the case exists so a later refactor to parsing goes red |
| 6 | `rounds.test.ts` + `activeRounds.test.ts`: *a reviewer with no status says nothing rather than a dangling dash* — `reviewerLines` ends at the model with no trailing ` — `, and the panel renders no `.said` div at all | today the line ends `— ` and the panel would render an indented empty row |
| 7 | `activeRounds.test.ts`: the vendor's inline colour is on the `.who` span only — not on the row, not on the identity line, and not on the `.said` div | (guards the colouring while the markup moves) |

Run, from a checkout with the extension's dependencies installed (`npm ci` in `src_vs_code`, Node 22
as CI uses): `cd src_vs_code && npm test` — the whole suite, because `roundsLog.test.ts` and
`bundledPage.test.ts` both read the reviewer text on the page that must not change.

## Definition of Done

- [x] Tests 1 and 3 written first and watched fail — `what it IS — no status, no dash` and `the
      status is a line of its own inside it` — then green; with the whole fix reverted **seven** fail
      across three suites (rounds 3, activeRounds 2, panelView 2), all green when restored.
- [x] `npm test` green in the worktree: **1728 tests, 1727 pass, 0 fail** (1 skipped).
- [x] The diff through the `coai` code round — `proceed`, 10 gating against a threshold of 5, all 12
      reviewers answered; 7 findings accepted, 11 rejected with reasons, all recorded via `resolve`.
- [x] `research/module_extension.md` carries the decision and its three deliberate parts;
      `CHANGELOG.md` under `## Unreleased`.
- [x] This plan promoted to `research/` with `IMPLEMENTED` and the date; indexes updated both sides.

**Deviation from this list:** the version is deliberately NOT bumped here. This is one of seven
issues landing before a single extension release, and the release commit at the end of the batch
renames the `## Unreleased` heading to the version it cuts.
