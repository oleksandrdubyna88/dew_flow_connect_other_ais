# PLAN — the log pages, and keeps conversations apart

> Status: **plan only, nothing implemented yet, 2026-09-16.** Scope: the rounds-log page —
> `src_vs_code/src/roundsLog.ts` (the pager's appearance, a second view over the one table), and its
> tests.
>
> Issue: [#297](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/297).
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_rounds_log_view.md](../research/PLAN_rounds_log_view.md).
>
> **Depends on [#313](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/pull/326)**, which
> teaches `cssRules.ts` to walk braces. This page's stylesheet carries an `@media` block
> (`roundsLog.ts:1218`) and the parser on `main` refuses it, so the CSS half of the test below cannot
> be written until that lands. Not stacked on it — this branch is cut from `main` and rebased.

## Two complaints, one page

> «и пофикси пагинацию, кнопки активны, а при нажатии ничего не происходит (там кажется до 200
> записей на стр). когда некуда листать — кнопки должны быть неактивны»
>
> «Conversations logs into separate tab … в бд смотри сам как удобно, в отд таблицу или нет, а на стр
> в отдельный таб вынести»

### A. The pager — the buttons ARE inactive, and nothing says so

This is not a logic bug, and saying that first matters because the obvious fix would be to the wrong
half. The paging is correct and covered:

- `PAGE_SIZE = 200` (`roundsLog.ts:1100`), the slice at `:1475-1479` clamps `state.page` into range;
- `prev.disabled = state.page === 0` and `next.disabled = state.page >= pages - 1` (`:1556-1557`);
- every filter returns to page 1 (`firstPage()`, `:1342`), deliberately — "next page" of a
  client-side filter over a server-side window cannot be kept honest;
- four tests in `roundsLogPaging.test.ts` press the controls and assert the clamping.

**What is missing is the styling.** The whole button stylesheet is four rules (`:1149-1157`) and none
of them mentions `:disabled`. So a disabled *◀ Newer* renders in full secondary-button colour, keeps
the hand cursor, and **still highlights on hover** — Chromium matches `:hover` on a disabled element
and suppresses only the pointer events. A control that looks pressable, invites the pointer, lights
up under it, and does nothing is exactly the report.

**A contributing cause worth fixing in the same breath.** The page opens filtered to **today**
(`setToday()`, `:1710`), so `matched.length` is usually under 200, `pages === 1`, and both buttons are
disabled — while `#pageinfo` says nothing at all about paging in that case (`:1559-1562` appends
*page N of M* only when `pages > 1`) and the line directly under it announces *"The database has N
rounds…"* with an N in the thousands. Two true statements that together read as a broken pager.

**The fix — and the existing hover rules are REPLACED, not added beside.** This is the round's
sharpest finding and the first draft had it wrong. Adding `button:hover:not(:disabled)` leaves
`button:hover` in the sheet, and that selector still MATCHES a disabled button: the new rule simply
does not apply there, so nothing overrides the old one and a dead control still lights up under the
pointer. The two hover selectors are rewritten in place.

```css
button:disabled, button.secondary:disabled { opacity: .5; cursor: default; }
button:hover:not(:disabled) { background: var(--vscode-button-hoverBackground); }
button.secondary:hover:not(:disabled) { background: var(--vscode-button-secondaryHoverBackground); }
```

`opacity` + `cursor: default` is this repository's own way of drawing a disabled control — six sites,
including `bugzReviewPage.ts:133`, which is the identical rule for the identical case. A reviewer
proposed `--vscode-disabledForeground` instead; it is a FOREGROUND colour, and these are filled
buttons whose background is what makes them look pressable, so recolouring the label while leaving
the fill at full strength would be closer to the symptom than to a fix.

**And `#pageinfo` always states the position** — *page 1 of 1* rather than silence — so the control
explains itself instead of leaving the footer's much larger number to be read as its own.

`#exportpicked` (`:1265`) carries the identical defect and is fixed by the same rules, since it is
disabled the same way.

### B. Conversations get their own tab

Conversations are already on this page: `chatRows()` (`:276`) turns `chat-usage.jsonl` into `LogRow`s
and `mergedRows()` (`:259`) folds them into the same table, distinguished by a `Kind` column
(`COLUMNS[2]`) and a `Kind` facet (`FACETS[0]`). A conversation row deliberately leaves
repo/branch/stage/verdict/findings empty (`:271-274`), because filling them would make it read as a
kind of review round.

**The decision (operator, 2026-09-16): move, not duplicate.** *Rounds* becomes review-only and
conversations get a tab of their own.

**The shape: one table, two VIEWS — not two tables.** The alternative was a second pushed HTML region
like *Consultations* (`consultationsHtml`), which is how the other three tabs work. Rejected: the
second half of this very issue is about paging, and a conversations tab with no paging, no sort and
no search would be the smaller surface a week later. Everything the table already does — sort,
facets, search, the date range, the pager — is worth exactly as much to a conversation as to a round,
and it is already written.

So:

| | |
|---|---|
| `state.view` | `'rounds'` \| `'conversations'`, set by the tab strip |
| the tab handler | `#tab-rounds` stays visible for BOTH; `state.view` changes and `firstPage()` runs, which is what every other filter change already does |
| rows | the merged array stays; the view filters it by `kind`, so `logRows()` in `extension.ts` is untouched |
| columns | a set per view. `ROUND_COLUMNS` loses `Kind`; `CONVERSATION_COLUMNS` is *When · Who answered · What · Turn · Status · Took · Tokens in · Tokens out · Cost* |
| facets | `Kind` goes — the view says it now; `Stage` and `Verdict` are offered only in the rounds view, where they mean something |
| the pick column and `#exportpicked` | **hidden in the conversations view.** The plan says there is no Export for a conversation, and with one shared table saying so is not enough: the tick-boxes and the button would still be on screen, and a person selecting conversation rows and pressing Export would drive a flow that reads findings a conversation does not have |
| the footer line | `#recorded` reads *"The database has N rounds and M findings"* on every view today. It is about `coai.db`, which holds no conversations at all, so in the conversations view it says something true about the wrong thing. It is scoped to the rounds view |

**THE VIEW FILTERS FIRST, before everything else in the pipeline.** One `visibleRows` sequence is
taken by `state.view` and only then searched, faceted, sorted, counted and sliced. Filtering after
the slice would give the Conversations tab one row out of a page of 200 rounds with *Older* enabled
onto an empty page, while the counts and the search still spoke for both kinds — and a test written
over a single mixed page would pass while it was broken. Each view is therefore tested with MORE than
one page, and with a search that matches rows of both kinds.

**The conversations source, named: `<dataDir>/chat-usage.jsonl`**, read by `readChatUsage` through
`jsonlLedger.readLedger`, which answers `[]` on `ENOENT` and logs-and-answers-`[]` on anything else.
So an installation that has never held a conversation renders an empty table rather than an error,
and `#pageinfo` already says *nothing to show* for that case (`:1559`). That is a test case, not an
assumption. The DB window (`MAX_LIMIT = 1000`) does not bound conversations: it bounds the ROUNDS
read out of `coai.db`, and the ledger is read whole.

A conversation's *Who answered* is its vendor and model, which the row already carries for the
spending chart; *What* is `subject`, the conversation's title; *Turn* is `number`.

**What does NOT change:** the database. A conversation is not a round and has no row in `coai.db` —
it lives in `chat-usage.jsonl`, and `Schema.cs`'s own argument for giving consultations a table
rather than making them a kind of round applies here word for word. The issue leaves this open («в бд
смотри сам как удобно»), and the answer is that nothing needs to move.

## Build order

0. **Rebase onto a `main` that contains
   [#326](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/pull/326) FIRST**, and prove
   the parser before writing anything that depends on it: `cssRules.stylesheet()` must read this
   page's stylesheet, `@media` block and all, without refusing it. Two reviewers raised this
   independently and they are right that the plan as first written could not pass its own suite —
   the parser on `main` refuses an at-rule, and the alternative they proposed (assert computed style
   instead) is not available, because nothing in this suite has a layout engine.
1. **RED** for the pager: the page is RUN (`roundsLogPaging.test.ts`'s `open()` harness) with one
   page of rows, and the stylesheet is parsed with `cssRules.ts` — assert that a rule disables the
   pointer on a disabled button, that `:hover` no longer reaches one, and that `#pageinfo` states the
   position even at one page. Then the rules.
2. **RED** for the view: with rows of both kinds, the conversations tab shows only conversations and
   the rounds tab only rounds; the column headers differ between them; switching views returns to
   page 1.
3. The view state, the tab handler, the two column sets, the facet trim.
4. **Teeth**: delete each rule and each filter in turn and watch the matching test go red.
5. `rm -rf out && npm run compile && node scripts/run-tests.mjs`, then
   `node .agents/conventions/tools/plan-lifecycle.mjs` — and its **exit code is checked**, not its
   output skimmed. It is a gate on the promotion below, and a non-zero exit that nobody read is the
   failure mode it exists to prevent.

## Test plan

- No behavioural assertion over page source text — `.agents/PROJECT.md:73-83`,
  `.coderabbit.yaml:115-126`, operator ruling 2026-09-14. `roundsLogPaging.test.ts` already runs the
  page's script against a stub DOM; the new assertions extend that harness.
- The four existing paging tests must stay green untouched — the logic they cover is not what is
  being changed.
- `bundledPage.test.ts` must stay green: it runs the MINIFIED page, and `EMBEDDED` guards the
  functions whose source is interpolated. A new embedded function would have to be added there.

## What this plan does NOT do

- It does not change `PAGE_SIZE`, the server-side window (`MAX_LIMIT = 1000`), or the rule that a
  filter returns to page 1.
- It does not move conversations into `coai.db`.
- It does not give the conversations view an Export — the export writes a round's findings, and a
  conversation has none.

## Definition of Done

- [ ] A RED test observed failing with a message naming the real symptom, for both halves.
- [ ] Every rule and filter proved by deleting it and watching the test go red.
- [ ] The pager's disabled state is visible AND the hover no longer lights a dead control.
- [ ] `#pageinfo` states the position at one page as well as at ten.
- [ ] Rounds shows no conversations; Conversations shows no rounds; the headers differ — asserted
      over MORE than one page, and with a search matching both kinds.
- [ ] An installation with no `chat-usage.jsonl` shows an empty conversations table, not an error.
- [ ] The tick-boxes and Export are gone in the conversations view; the footer's rounds count is too.
- [ ] Colours from the theme; no hex.
- [ ] Whole extension suite green from a cleaned `out/`; `plan-lifecycle.mjs` clean.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations, both READMEs updated.
