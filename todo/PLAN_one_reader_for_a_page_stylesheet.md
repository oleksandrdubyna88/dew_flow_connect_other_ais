# PLAN — one reader for a page stylesheet

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope:
> `src_vs_code/src/test/chatPage.test.ts` (the two private readers) against
> `src_vs_code/src/test/cssRules.ts` (the one that already exists).
>
> Related docs: [module_tests.md](../research/module_tests.md),
> [PLAN_a_copy_that_says_so_in_green.md](../research/PLAN_a_copy_that_says_so_in_green.md) — issue #322,
> where this tail ORIGINATES, and
> [PLAN_a_copy_you_can_see_landing.md](../research/PLAN_a_copy_you_can_see_landing.md) — issue #313,
> which restates it; both record it under *Open tail*,
> [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md) — the boundary with it is
> named below, because it is the objection a reviewer raised against this plan.
>
> **Citations verified at `252813d3`**, each against the symbol named beside it, not only against the
> file's existence.

## The symptom

`cssRules.ts` exists because a code round called a third private stylesheet parser **Blocking**. It was
written for issue #322, the copy that prompted it was converted, and the two that were already there
were left — so the file created to end the duplication currently sits beside two survivors of it, in
the same test file that imports it.

The two, both in `src_vs_code/src/test/chatPage.test.ts`:

- **`function rules(css)` — [chatPage.test.ts:261](../src_vs_code/src/test/chatPage.test.ts#L261)** —
  splits on `}`, then on `{`. It is honest about its limit: an `assert.ok` at the top refuses a
  stylesheet containing `@media` or `@supports`, because a nested at-rule would be mis-read silently.
  That guard is the whole reason it is tolerable, and it is also the reason it will one day fail a page
  that gains a media query for a legitimate purpose.
- **the inline reader inside the test `'hiding the arrows actually hides them, against the rule that
  lays them out'` — [chatPage.test.ts:2582](../src_vs_code/src/test/chatPage.test.ts#L2582)** — the
  same `.split('}')`, with no guard at all, and it decides which rule wins **by array index**
  (`assert.ok(hide > layout, …)`). Source order is a real tie-breaker in CSS, but only between rules of
  EQUAL specificity; this test reads order as if it were the whole answer. It happens to be right about
  `.askingHead[hidden]` against `.askingHead`, and it would be wrong the moment either selector gained
  a class.

`cssRules.ts` already answers both properly — `stylesheet()`
([cssRules.ts:50](../src_vs_code/src/test/cssRules.ts#L50)) parses, `specificity()`/`outranks()`
([cssRules.ts:121](../src_vs_code/src/test/cssRules.ts#L121)) rank, and `beating()`/`painters()`
([cssRules.ts:325](../src_vs_code/src/test/cssRules.ts#L325)) say which rule actually paints. Three
consumers already use it: `chatPage.test.ts`, `panelPhrasesScript.test.ts`, `roundsLogPaging.test.ts`.

## The boundary with the page-test backlog (MANDATORY, both sides)

A reviewer read this plan as investing in assertions that
[PROJECT.md:102-112](../.agents/PROJECT.md#L102) forbids. It does not, and the division is written here
so the objection does not have to be re-made.

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| Reading a page's STYLESHEET — parsing rules, ranking by specificity, saying which paints | **this plan** | none — a DOM shim does not implement the cascade, so there is no program to run | either |
| BEHAVIOURAL assertions over page source text — a control wired to the wrong branch, a missing `return` | none here | **[PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md)**, which converts them to run against the shim | either |
| `cssRules.ts` itself, and its three current consumers | already shipped with #322 | — | done |

**Disjoint**, and this is the whole point: a stylesheet question is never answered by running the page,
and a behavioural question is never answered by reading its text. [`PROJECT.md:110`](../.agents/PROJECT.md#L110) states the carve-out
— *"Source assertions stay legitimate where there is no program to run"* — and `cssRules.ts` was created
by #322's code round **after** the 2026-09-14 operator ruling, which is what makes parse-and-rank the
sanctioned shape for stylesheets rather than an exception to be tolerated.

## The boundary with the plan this came from

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| The two survivors in `chatPage.test.ts` | **this plan** | both parents recorded it as an open tail | parents shipped first |
| `cssRules.ts` and the converted third copy | [PLAN_a_copy_that_says_so_in_green.md](../research/PLAN_a_copy_that_says_so_in_green.md) (#322) | this plan consumes it unchanged | done |
| The chat tab's copy controls, and the double `signatureOf` pass per message | [PLAN_a_copy_you_can_see_landing.md](../research/PLAN_a_copy_you_can_see_landing.md) (#313) | the double pass was measured on that round and declined; nothing here revisits it | closed |

**Disjoint**: the parent's work is complete; this plan adds no capability to `cssRules.ts` and changes
nothing under `src/`.

## What ships

1. **`rules()` and `ruleFor()` are re-expressed over `stylesheet()`.** `ruleFor` keeps its name and its
   assertion message — every call site stays as it is. The at-rule `assert` is dropped, because
   `stylesheet()` does not need it.
2. **The arrow-hiding test asks `beating()` instead of comparing indices.** It becomes a question about
   which rule paints `display` on an element that has `[hidden]`, which is the question the test's own
   comment says it is asking.
3. **Nothing in `src/` changes.** This is entirely a test-side consolidation.

## What this does NOT do

- **It does not touch the single-rule extractions** in `panelView.test.ts:1653`,
  `phrasesPage.test.ts:157` and `phrasesSection.test.ts:178`. Those are
  `css.split('.foo {')[1].split('}')[0]` — not parsers, a one-rule lookup, and converting them would be
  a larger diff than the defect justifies. If one of them ever has to rank two rules, it joins this plan
  rather than growing its own reader.
- **It does not add capability to `cssRules.ts`.** If a conversion needs something the module cannot
  answer, that is a finding to report, not a widening to slip in here.

## Build order

1. Re-express `rules()`/`ruleFor()` over `stylesheet()`; run `chatPage.test.ts`; it must stay green with
   no assertion edited.
2. Convert the arrow-hiding test to `beating()`.
3. Delete the private readers and the now-unused local `Rule` type if it shadows the imported one.

## Test plan

- The whole extension suite, before and after: the count and the pass set must be identical, because
  this changes how a test reads a stylesheet and not what it asserts.
- **Teeth, on the converted arrow test** (this is the point of the change): give `.askingHead` a class
  in the page so the two selectors are no longer of equal specificity, and confirm the converted test
  still answers correctly while the index-comparison version would have flipped. Restore.
- `npm run typecheck` — the private `Rule` type and the imported one share a name today.

## Definition of Done

- [ ] `chatPage.test.ts` contains no `.split('}')` stylesheet reader.
- [ ] The arrow-hiding test ranks by specificity, not by array position, and its teeth check is recorded.
- [ ] The suite's pass set is unchanged.
- [ ] The boundary tables above are mirrored in
      [PLAN_a_copy_you_can_see_landing.md](../research/PLAN_a_copy_you_can_see_landing.md) and
      [PLAN_the_page_tests_run_the_page.md](PLAN_the_page_tests_run_the_page.md).
