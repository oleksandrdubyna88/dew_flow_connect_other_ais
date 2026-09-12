# PLAN — the round-limit row lines up with the other four

> Status: **IMPLEMENTED, 2026-09-12.** The round limit's derived note moved out of its
> `.field.inline` row and became a sibling `<div class="hint">` under it, which is what every other
> description in the panel already was. `roundLimitNote` is untouched, no CSS changed, and the
> input now sits in the same column as the other four.
>
> Two deviations, both from the plan round. The structural test covers all THREE wordings the note
> can take rather than two — a fix verified on two states leaves the third free to keep the old
> shape — and a second test holds **every** limits row to exactly two children rather than naming
> the round limit, because the alignment is a property of the set of rows and the next one to grow a
> third item is the one nobody would think to test. Both were raised as findings and both were right.
>
> The explanation lives as a TypeScript docstring on `limitsBody`, not as an HTML comment: the first
> draft put it in the emitted markup, where it shipped to the webview and separated the row from its
> own hint — caught by the test that asserts the hint follows immediately.
>
> The code round added one thing worth having and nothing else: the input now names its note with
> `aria-describedby`. Moving the note out of the row took away the visual proximity that tied the
> two together, and a screen reader never had that proximity in the first place. Its other ten
> findings were rejected — all from one reviewer, and all reading the diff's REMOVED lines as the
> shipped state ("the note is a third flex child", "uses a span instead of a div" — one of them
> works out mid-sentence that the diff is compliant and says so). The exceptions were a script
> injection through a string built from three integers behind `escapeHtml` and a CSP, a try/catch
> around a total function over numbers, a `data-testid` in a suite whose convention is to assert
> what the browser gets, and memoising a string concatenation on a path that already serialises the
> whole state.
>
> Of the plan round's nine findings four were accepted and five rejected with reasons: the in-page
> script never queries `.hint` (it delegates by `[data-setting]`, `[data-prompt]`, `.section` and
> `[data-command]`), `staticKey` is `JSON.stringify` over state and reads no DOM, and the two calls
> for manual visual verification are answered by the `.inline` rule being untouched and already
> proving itself on four rows.
>
> Related docs: [module_extension.md](module_extension.md).
>
> Issue [#118](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/118): *"Round
> limit, minutes — put the number in line with the others; give the description as a line below."*

## The symptom

The **Limits** section of the sidebar is five numeric rows — *Reviewers at once*, *Per vendor*,
*Reviewer timeout, minutes*, *Round limit, minutes*, *Wait for you, minutes* — each a
`<div class="field inline">` holding a label and a 64-px number input, laid out by
`.inline { display: flex; justify-content: space-between }` (`src_vs_code/src/panelView.ts:1653`) so
every input sits at the right edge under the one above it.

The fourth row breaks the column. `limitsBody` (`panelView.ts:845-869`) puts the round limit's
derived note — *worked out: at most 4 waves × 10 min = 40 min*, or *set by hand*, or *shorter than
one reviewer's 10 min — reviewers will be cut off* — INSIDE that row as a third flex item
(`<span class="hint">`, `panelView.ts:862`). The flex row then shares its width three ways: the
label shrinks, the input is pushed left of the other four, and the note is squeezed into whatever
is left beside it. The screenshot on the issue shows exactly that — one input out of the column,
and a description crammed on the same line.

Every other description in the panel is a **sibling** `<div class="hint">` placed after its row
(`keysBody`, `panelView.ts:876`; `sideBody`; the chat section) — a line of its own, under the
control it describes. The round-limit note is the only one put inside the row it describes.

## What must be true when this is done

1. The round limit's `<div class="field inline">` holds exactly what the other four hold — the
   labelled label and the number input — so its input sits in the same column.
2. The derived note is rendered as a `<div class="hint">` **immediately after** that row, on a line
   of its own, with the same text it has today (`roundLimitNote` is untouched; every branch of its
   arithmetic and its three wordings stay exactly as the existing test pins them).
3. No CSS change: `.hint` (`panelView.ts:1662`) already styles a sibling hint, and `.inline`'s
   layout is right once the row holds two items again.
4. The section's `staticKey` is unaffected (nothing new is a control), and the help article's
   sentence about the limits (`helpContent.ts:181`) still describes them accurately.

## The change

- `panelView.ts:858-863`: move the `<span class="hint">…</span>` out of the round limit's
  `<div class="field inline">` and render it as `<div class="hint">…</div>` right after the row's
  closing tag. One element moves one level up; the note's text and `escapeHtml` are unchanged.
- `src/test/panelView.test.ts`: the test below, beside the existing round-limit test.
- `research/module_extension.md`: the sentence describing the limits rows mentions that the round
  limit's note is a line under the row, like every other hint.
- `src_vs_code/CHANGELOG.md`: one paragraph under `## Unreleased`; the release commit at the end of
  this batch names the version.
- This file: promoted to `research/` in the branch's last commit, its row moved in `todo/README.md`.

## Test plan (RED first)

| # | Test (`src_vs_code/src/test/panelView.test.ts`) | RED symptom expected |
|---|---|---|
| 1 | *the round limit's input sits in the column, and its note is a line under the row*: for **all three** wordings `roundLimitNote` can produce — derived (`worked out:`), by hand (`set by hand`) and cut off (`shorter than one reviewer's`) — the `<div class="field inline">` that holds `id="roundTimeoutMinutes"` contains no `class="hint"`, and the element directly after that div's closing tag is a `<div class="hint">` carrying that wording. Raised on the plan round (codex, gemini): the first draft named two of the three states. | the row's div contains `<span class="hint">worked out:` |
| 2 | *every limits row holds exactly a label and an input*: each `<div class="field inline">` in the Limits section has exactly two child elements — the `<label>` and the `<input type="number">` — so no row can grow a third flex item again without this going red. Raised on the plan round (local): the structural property, asserted for all five rows rather than one. | the round-limit row has three children |
| 3 | the existing test *the round limit says what it works out to…* stays green untouched — it pins the note's three wordings, and this change must move the note without rewording it | green before and after |

Run, from a checkout with the extension's dependencies installed (`npm ci` in `src_vs_code`, Node 22
as CI uses): `cd src_vs_code && npm test` — the whole suite; `settingsAreDeclared`, `helpCoverage`
and the duplicate-selector test all read this page.

## Definition of Done

- [ ] Test 1 written first and watched fail with the symptom above; then green; then red again with
      the fix reverted, and green with it restored.
- [ ] `npm test` green in the worktree; the count reported in the pull request.
- [ ] The diff through the `coai` code round, every finding resolved.
- [ ] `research/module_extension.md` and `CHANGELOG.md` updated as named above.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
