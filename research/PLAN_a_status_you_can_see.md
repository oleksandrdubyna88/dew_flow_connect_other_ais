# PLAN — done, running and queued carry a mark you can see without reading

> Status: **IMPLEMENTED, 2026-09-15.** Kind: **feature** (a small one). Scope:
> `src_vs_code/src/rounds.ts` (one field on `ReviewerRow`), `src_vs_code/src/panelView.ts` (the
> reviewer line and five CSS rules), and their tests. Origin:
> [issue #286](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/286) —
> *"done / running / queued … нужно как то выделять, что б понимать, статус. читать тяжело. давай
> рядом делать иконки для каждого статуса. done - зеленая галочка, остальные придумай"*.
>
> Related docs: [module_extension.md](module_extension.md), [module_tests.md](module_tests.md).
>
> ## Deviations — what shipped differently, and why
>
> 1. **Five CSS rules, not four.** The plan budgeted only colours. `statusMark(...)` is concatenated
>    straight onto the escaped sentence, so with no rule of its own the glyph rendered flush against
>    the word — `✓done`. `.reviewer .said .mark` carries `display: inline-block`, a fixed `width` and
>    a `margin-right`. (gemini, the plan round.)
> 2. **An unrecognised status gets an EMPTY mark, not nothing.** The plan said "no mark". That would
>    have started such a row a glyph-width left of every other row — a ragged column, which is the
>    same "hard to read" the issue is about. The span is rendered and held to the same width; only
>    the glyph and the modifier class are withheld. Requirement 4 is unchanged: nothing is invented.
> 3. **`statusMark` is exported.** The plan had it private and tested through the page. Three
>    reviewers made the same point — it is a BRANCH, and a substring assertion over generated markup
>    cannot tell a branch that returned the wrong glyph from one that returned the right one. Testing
>    it by calling it is better evidence than running the page would be, and needs no harness.
> 4. **The mapping is asserted per status**, glyph and colour variable both, rather than "four
>    distinct classes" and "some charts variable" — each of which stays green when the mapping is
>    inverted.
> 5. **Two existing assertions elsewhere had to change**, which the plan did not anticipate:
>    `panelView.test.ts` pins the reviewer line as one literal string, and
>    `panelServerPromptAgreement.test.ts` broke on something subtler — a CSS comment is inlined into
>    the page, so writing *"a status the panel does not know…"* put the phrase **does not know** into
>    the HTML, which that test asserts is absent when the server agrees about roles. The comment was
>    reworded. A comment in this stylesheet is page content, not a private note.
>
> **Checked, and not done:** that the four glyphs render on a machine this has not run on. There is
> no font check in this suite and no way to write one. The argument standing in for it is that every
> glyph was already shipping elsewhere in this product before this change — NOT that any of them was
> verified on another machine.

## The symptom

In the sidebar's **Active rounds**, every reviewer's second line is grey 11px text, and the status is
a word inside it. A round with six reviewers is six near-identical grey lines that differ in one
word somewhere in the middle. The operator has to *read* the column to answer "what is happening",
and the answer is the one thing that should be visible without reading.

## Where the words come from

`ReviewerState.status` is a free string the SERVER writes (`rounds.ts:9-12`) — it is not a union, and
the panel does not get to decide the vocabulary. The ones that reach the sidebar in practice are
`done`, `running`, `queued` and `failed`.

- `restOf` composes the second line — `rounds.ts:337`:
  `return { rest: \`/${state.role}${named}\`, said: \`${status}${brackets}\`.trim() };`
  so `said` is `"done (3 findings, 30 s)"` — **the status and its detail welded into one string**.
- `reviewerRows` (`rounds.ts:358`) builds the detail: a findings count for `done`, the server's note
  for `failed` and `queued` (which is why a queued reviewer can say *"2 ahead on this engine, about
  4 min"*), the duration and the tokens.
- The panel renders it at `panelView.ts:2096`:
  `<div class="said">${escapeHtml(row.said)}</div>`, styled by one rule — `panelView.ts:2372`:
  `.reviewer .said { margin-left: 16px; }`.

**So the status is not separable at the point of rendering.** That is the actual obstacle, and it is
why this is not a pure CSS change.

## What must be true when it is done

1. Each of `done`, `running`, `queued` and `failed` shows a mark beside its word in the sidebar's
   reviewer lines.
2. `done` is a **green tick** — the operator named that one.
3. The word is still written. The mark is an addition, never a replacement: this panel's own rule,
   written at `panelView.ts:1402`, is *"The colour is never the only signal — the name is always
   written."*
4. A status the panel does not recognise gets **no mark** and is otherwise unchanged. The vocabulary
   belongs to the server; inventing a glyph for a word we have not seen would be a guess rendered as
   a fact.
5. A reviewer with no status still gets no second line at all, exactly as now.
6. The detail — findings, the queued note, duration, tokens — is unchanged in content and order.
7. The mark is decorative to a screen reader, because the word beside it already says it.

## The design

**One field, one span, four CSS rules.**

`rounds.ts` — `ReviewerRow` (`rounds.ts:272`) gains:

```ts
/** The status alone, normalised — the panel marks it; `said` keeps the status AND its detail. */
readonly status: string;
```

`restOf` already computes exactly this value (`const status = usableString(state.status);`) and then
welds it into `said`. It returns it separately as well. **`said` is left exactly as it is** — the
rounds-log page and the CSV export read the same rows, and changing the composed sentence would
change them too for no reason anybody asked for.

`panelView.ts` — the reviewer line gains a mark before the text:

```ts
`<div class="said">${statusMark(row.status)}${escapeHtml(row.said)}</div>`
```

with a pure helper beside it — **exported**, so the tests CALL it rather than reading the page it
ends up in:

```ts
/** The mark for a status we know, or an empty one that still holds the column. Decorative. */
export function statusMark(status: string): string
```

The plan round's point (codex) is the right one: this helper is a BRANCH, and a substring assertion
over generated markup cannot tell a branch that returned the wrong glyph from one that returned the
right one — the page contains a glyph either way. Exporting it makes the branch testable by calling
it, which is better evidence than running the page would give and needs no harness at all.

**An unrecognised status still gets a span — an EMPTY one** (`<span class="mark" aria-hidden="true">
</span>`), not nothing. Requirement 4 stands: no glyph is invented for a word we have not seen. But
returning nothing at all would start that row's text one glyph-width to the left of every other row,
so a single unknown status among six known ones reads as a ragged column — the plan round called this
out, and it is the same "hard to read" the issue is about. The space is reserved; the meaning is not
guessed.

### The mark needs a rule of its own, not only a colour

Five CSS rules, not four. `statusMark(...)` is concatenated directly onto `escapeHtml(row.said)`,
so with no rule the glyph renders flush against the word — `✓done`. Putting a space in the template
would leave a leading space on rows whose mark is empty. So:

```css
.reviewer .said .mark { display: inline-block; width: 1.1em; margin-right: 2px; }
```

`inline-block` with a fixed width is what reserves the column for the empty case above; the margin is
what separates the glyph from the word. (gemini, the plan round.)

### The four marks

| status | mark | why this one |
|---|---|---|
| `done` | `✓` green | The operator named it. |
| `running` | `⟳` blue | The same glyph the run/update buttons already use on a vendor card (`panelView.ts:984-994`). |
| `queued` | `…` yellow | Waiting, with more to come — and it pairs with the note the row already carries (*"2 ahead on this engine"*). |
| `failed` | `✗` red | The pair of `✓`, and already beside it on the rounds-log page. |

**Every one of these four glyphs already ships in this product** — `✓` and `✗` at `roundsLog.ts:1407`,
`⟳` on the vendor cards, `…` throughout the prose. So none of them is a new font risk on a machine
this has not been tried on, which is the thing that would otherwise need checking and cannot be
checked from here.

### The colours, and one inconsistency this does not make worse

`--vscode-charts-green`, `-blue`, `-yellow`, `-red` — theme variables, per the panel's own invariant
that every colour comes from one (`panelView.ts:27-42`).

Blue for `running` is deliberate: the rounds-log page's running badge is **blue**
(`roundsLog.ts:1179-1183`) while the sidebar's round-level badge is **green**
(`panelView.ts:2374`) — a divergence that predates this change. Since `done` must be green, making
`running` green too would put two meanings on one colour in one column. Blue agrees with the log
page and leaves the older divergence where it is.

**Not touched, and named so it is not mistaken for an oversight:** `.badge.running` at
`panelView.ts:2374` keeps its green. Re-colouring the round-level badge is a separate decision about
a different thing, and nobody asked for it.

## What this does NOT do

- It does not touch the rounds-log page, which already has real status badges.
- It does not touch the consultation cards (`consultations.ts`), whose `badgeClass` returns
  `running` or `idle` — and **`.badge.idle` has no CSS rule anywhere**, which is a real latent defect
  found while reading for this plan. It is written down here and left alone: it is a different
  surface, and folding it in would make this change about two things.
- It does not change `said`, the CSV export, or anything the log reads.

## Growth budget

No table, no file, no cache, no process. One string field on a row that already exists in memory for
the length of a repaint. No growth surface.

## Build order

1. **RED** — the tests below, watched failing.
2. `status` on `ReviewerRow`, returned by `restOf`.
3. `statusMark` and the four CSS rules in `panelView.ts`.
4. **GREEN** — the same tests, then the whole suite.
5. `research/module_extension.md` and `research/module_tests.md`; `CHANGELOG.md` under 0.47.0,
   naming issue #286. **All of this happens BEFORE the `git mv` of the promotion**, so the module
   doc is written while the plan it links is still in `todo/` and its link is fixed in the same pass
   rather than pointing at a file that has not moved yet.
6. `node .agents/conventions/tools/plan-lifecycle.mjs` and `pin-check.mjs` — run **after the code
   and the docs, before the final commit**, so a bad status line or a broken cross-folder link is
   found while it is still a working-tree edit rather than a red check on a pushed branch.

## Test plan

Home: `src_vs_code/src/test/activeRounds.test.ts`, which already pins this exact markup —
`:79` asserts `codex</span>/Architecture<div class="said">done` and `:100` asserts
`/<div class="said">done \(3 findings, 30 s\)<\/div>/`. **Both will have to change**, because the
markup genuinely changes; they are updated to assert the new shape rather than deleted.

**The mapping is asserted per status, exactly — not "four different classes" and not "some charts
variable".** Three reviewers converged on this independently, and they are right: a set-of-four
assertion passes when `running` shows the queued ellipsis and `queued` shows the running arrow, and
"names a `--vscode-charts-` variable" passes when `done` is red and `failed` is green. Every one of
those would ship inverted status indicators under a green suite.

1. **`statusMark` is tested by CALLING it**, one case per status, asserting the exact class AND the
   exact glyph: `done`→`mark-done`+tick, `running`→`mark-running`+circular arrow,
   `queued`→`mark-queued`+ellipsis, `failed`→`mark-failed`+cross.
2. `'a status nobody taught the panel gets a space, not a guess'` — `statusMark('thinking')` returns a
   span carrying `class="mark"` and NO `mark-*` modifier and no glyph. Both halves: the column is
   held, and nothing is invented.
3. `'a mark is never asked to render a value that is not a string'` — `statusMark('')` and the row
   built from a state whose `status` is a number or absent. `usableString` (`rounds.ts:341-343`)
   already normalises this before `restOf` composes anything, and this test is what keeps that true:
   a hand-edited or foreign session file reaches the renderer as-is, and this panel has already been
   blanked once by exactly that.
4. `'the mark is hidden from a screen reader, because the word is right there'` — `aria-hidden="true"`.
5. `'the sentence a reviewer says is unchanged'` — for each of the four statuses, `said` is still
   `"<status> (<detail>)"` byte for byte. This is the regression guard the plan round asked for: an
   implementer who made `said` detail-only *because the status is now rendered separately* would
   still pass the sidebar tests, while the rounds-log page silently lost the word. `reviewerRows`
   has exactly two consumers — `panelView.ts:2092` and `roundsLog.ts:450`, verified — and neither
   spreads the row, so the new field cannot leak into an export; the CSV goes through
   `reviewerLines`, a different function.
6. `'a reviewer with no status still gets no second line'` — the existing guarantee, re-asserted
   because this change is the one that could break it.
7. The CSS: each `.mark-*` rule names its OWN `--vscode-charts-` variable —
   done/green, running/blue, queued/yellow, failed/red — and `.reviewer .said .mark` carries the
   width and the margin, without which the glyph sits flush against the word.
8. `research/module_tests.md` gains this flow, including what is still not covered.
9. Whole suite: `cd src_vs_code && npm test`. Baseline on this branch's base: 2947 tests, 2946 pass,
   1 skipped, 0 fail.

## Definition of Done

- [x] All the new assertions were watched RED first — "done is not marked mark-done", "an unknown status gets nothing at all, so its line starts further left", "the glyph is read out beside the word it duplicates".
- [x] Four statuses carry four distinct marks, asserted per status by glyph and colour; an unknown one carries a held space and no meaning.
- [x] The word is still written in every case, and the mark is `aria-hidden`.
- [x] `said` is unchanged and asserted per status; the CSV goes through `reviewerLines`, a different function, and neither consumer of `ReviewerRow` spreads it.
- [x] Whole suite green: 2950 tests, 2949 pass, 1 skipped, 0 fail.
- [x] `research/module_extension.md`, `research/module_tests.md` and `CHANGELOG.md` updated.
- [x] `plan-lifecycle.mjs` and `pin-check.mjs` clean, run before the final commit.
- [x] Indexed — the `todo/README.md` row while it was open, the `research/README.md` row on promotion.

## Acceptance — the gate on both sides

1. `review_plan` over this document before the first line of code; `resolve` every finding.
2. RED test per defect, then the whole suite — never one file alone.
3. **Rebase onto `origin/main` immediately before the code round**, then `review_code` with this
   document as the scope and `baseRef` `origin/main`; `resolve` every finding.
4. Pull request only after the code round is resolved; at most three open on this repository.
5. Five minutes after opening: read CI and the automated reviewer, verify each comment against the
   code and the rules, fix or answer, resolve the threads, merge by rebase.
6. Promotion inverts the checklist's order — every edit while the file is still in `todo/`, the
   `git mv` LAST, `git add` the destination, one commit — and the Definition of Done is ticked, since
   an unticked box in `research/` reads as verification nobody did.

Specific to this plan: no new command and no new setting, so `helpCoverage.test.ts` has nothing to
demand.
