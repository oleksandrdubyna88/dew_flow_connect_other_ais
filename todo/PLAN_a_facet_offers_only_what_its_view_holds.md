# PLAN — a facet offers only what its view holds

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope:
> `src_vs_code/src/roundsLog.ts` — `facetOptions` and the one place it is called.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_the_log_pages_and_keeps_conversations_apart.md](../research/PLAN_the_log_pages_and_keeps_conversations_apart.md)
> — this is that plan's open tail, recorded there under *Open tail*,
> [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) — the same file's other
> open plan; the boundary is named below.
>
> **Citations verified at `252813d3`**, each against the symbol named beside it.

## The symptom

Issue #297 moved conversations out of the Rounds table into a tab of their own: one table, two views.
The facets a conversation cannot answer — repo, branch, stage, verdict — are hidden per view by the
stylesheet at [roundsLog.ts:1406](../src_vs_code/src/roundsLog.ts#L1406) (`.view-conversations
.facet-repoPath, …`).

The facets that BOTH views keep are still built once, from the merged rows — the `const filters = FACETS
.map(…)` at [roundsLog.ts:1249](../src_vs_code/src/roundsLog.ts#L1249):

```ts
`<select data-filter="${f.key}"><option value="">any</option>${facetOptions(rows, f.key)}</select>`
```

with `function facetOptions(rows, key)`
([roundsLog.ts:962](../src_vs_code/src/roundsLog.ts#L962)) deriving its values from whatever rows it is
handed. The comment above it states the promise the code no longer keeps: *"A select's options for one
facet, from the rows themselves — a filter offers only what exists."*

So in the conversations view the **Vendor** select can offer a vendor that only ever answered a review
round. Choosing it gives an empty table. That is honest — the filter did what it said — but it is an
option that can never match anything in the view it is shown in, which is what a derived facet list
exists to prevent. The same holds for **Status** and **Kind** wherever the two views' value sets differ.

## Why it was left

The plan that shipped #297 filters rows at render time and switches views in the page, without a round
trip. Rebuilding the option lists per view is therefore not a server-side change: either the page
carries both lists and swaps them, or the view switch asks for a re-render. Both are real design
choices, which is why the tail said it *wants its own plan*.

## The boundary with the plan this came from

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| The two views, the per-view row filter, the CSS that hides round-only facets | [PLAN_the_log_pages_and_keeps_conversations_apart.md](../research/PLAN_the_log_pages_and_keeps_conversations_apart.md) | this plan consumes all of it unchanged | shipped first |
| The OPTION LISTS inside the facets both views keep | **this plan** | recorded it as an open tail | after the parent |
| Clearing a selection the view switch hides | **this plan** | not noticed by the parent | with the above |

**Disjoint**: the parent decides which ROWS a view shows; this plan decides which VALUES its selects
offer. Nothing here changes the row filter, the search, the sort or the slice.

## The boundary with the other open plan on this file

| Item | Which plan builds it | The other plan's part | Order |
|---|---|---|---|
| The tab strip's selected-state, ARIA and arrow keys | [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md) | none | either |
| What the facet selects CONTAIN, and clearing a hidden selection | **this plan** | none | either |

**Disjoint**: one plan is about the control that switches views, the other about the controls beside it.
They touch the same file and no same function; whichever lands second rebases without conflict.

## The two candidate shapes

**A. Both lists are rendered, one is hidden.** Each select carries its options for both views, tagged
`data-view="rounds|conversations"`, and the view switch enables/disables them. No round trip, no state
to lose, and the cost is a larger page — bounded by the number of distinct facet values, which is small.

**B. The view switch re-renders.** Conceptually cleaner and it deletes the duplication, but the page
currently switches views without going back for anything, and a re-render has to preserve the search
text, the sort and the page number.

**Recommendation: A**, unless the measurement shows the page growing meaningfully. It keeps the view
switch a pure page-side gesture, which is the property the shipped design is built on.

**A selection that is no longer offered must be cleared, not merely hidden** — a vendor chosen in the
rounds view and then hidden by a switch to conversations would otherwise keep filtering invisibly. This
is the defect's mirror image and it is the part most likely to be got wrong; it is the first test below.

## Build order

1. A failing test first: in the conversations view, a vendor that only a round has must not be among the
   Vendor options.
2. A second failing test: choosing that vendor in the rounds view and switching to conversations must
   leave the conversations view unfiltered, with the select showing `any`.
3. Split the option source per view; wire the switch.

## Test plan

- The two RED tests above, observed red with the real symptom before the change.
- The page is RUN, not asserted over as text, per
  [generated-code-tests.md](../.agents/conventions/common/generated-code-tests.md) and
  [PROJECT.md:102](../.agents/PROJECT.md#L102) — these are behavioural assertions, so the carve-out for
  stylesheets does not apply to them.
- A fixture where the two views' vendor sets differ in BOTH directions — a vendor only rounds have and a
  vendor only conversations have — so a change that swaps the lists cannot pass.
- The existing roundsLog suite, unchanged.

## Definition of Done

- [ ] Neither view offers a facet value that cannot match a row in it.
- [ ] A selection dropped by a view switch is cleared, and the test proves it.
- [ ] Both tests were seen red first.
- [ ] The boundary tables above are mirrored in
      [PLAN_the_log_pages_and_keeps_conversations_apart.md](../research/PLAN_the_log_pages_and_keeps_conversations_apart.md)
      and [PLAN_the_tabs_announce_themselves.md](PLAN_the_tabs_announce_themselves.md).
