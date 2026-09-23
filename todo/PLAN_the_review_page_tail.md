# PLAN — the review page's tail: a decision that says it is running, and the ranking model picker

> Status: **plan only, nothing implemented yet, 2026-09-23.** Scope: `src_vs_code/src/bugzReviewPanel.ts`
> and `src_mcp`'s `--pairs-decide` (item 1); `src_mcp/core/Collecting/RankingModels.cs`, its pin test
> and the ranking picker in the extension (item 2).
>
> **Extracted from [PLAN_the_review_page_can_be_read.md](../research/PLAN_the_review_page_can_be_read.md)
> when it was promoted on 2026-09-23**, because all four of its epics shipped and these two items were
> never in any of them. Related: [PLAN_the_corpus_tail.md](PLAN_the_corpus_tail.md) §1 (the ranking
> pass has no transport), [PLAN_a_comment_crosses_the_machine_boundary.md](../research/PLAN_a_comment_crosses_the_machine_boundary.md)
> (the decide path as epic 4 left it).

## The goal

1. **A decision says it is running, and says so across a reload.** Pressing *Keep selected* or
   *Drop selected* clears the selection, disables the buttons and runs `coai-mcp --pairs-decide` with
   nothing on screen saying so. Two reviewers of story 1.1's code round said it; it was rejected there
   as out of scope and carried, and `research/module_extension.md:7648` still records it as owed. The
   parent plan named the shape: a status-changing action must reflect its real state, not a flag in a
   webview that dies with it.
2. **The ranking model picker — story 7 of the parent plan.** The operator decided on 2026-09-18 that
   the ranking pass MAY use a remote model (*whoever does not want it will choose a local LLM*). That
   settles the policy and does not shrink the work.

## What is already here

- The decide path: the page posts `decide` (`src_vs_code/src/bugzReviewPanel.ts:419`), the panel
  queues it through `inFlight` and calls `this.hooks.decide` (`:566`), which spawns `--pairs-decide`.
  A per-row in-flight state already exists for *Open at &lt;sha&gt;* (commit `4625f1c6`, *a press says it
  is working*) and is the pattern to reuse rather than invent.
- The ranking boundary: `RankingModels.Local` (`src_mcp/core/Collecting/RankingModels.cs:41`) and
  `IsAllowed` (`:49`) enforce local-only; `ThePinThatMustNotDriftTests` pins that file.
- The ranking pass has no caller: `Ranking.Order` orders a reply nothing produces
  ([PLAN_the_corpus_tail.md](PLAN_the_corpus_tail.md) §1).

## Build order

1. **Item 1 first**, alone: the in-flight state for a decision, reusing the *Open at* pattern, with
   the durable half decided in the plan round — whether the write is short enough that an optimistic
   state which a redraw replaces is honest, or whether it needs a persisted status and a startup sweep.
2. **Item 2 after the corpus tail's transport exists**, because a picker for a pass nothing calls
   ships a control that does nothing:
   1. `RankingModels.Local`/`IsAllowed` stop being a boundary and become a default — an explicit
      *allowed* list, not a deleted check.
   2. `ThePinThatMustNotDriftTests` moves with that file, deliberately, in one commit.
   3. The picker: provider first, then model, every provider (the operator's item 11).
   4. **The picker marks what leaves the machine at the point of choosing.**

**And what leaves is not what the upload sends.** The ranking pass runs BEFORE the pairs are collected,
so before `Normalise` exists: it sees the reviewers' text, the real path and the real method name. The
upload's four-field promise (`OnlyFourFieldsLeaveTests`) cannot guard it. Wherever this is documented,
it is documented in those words.

## Test plan

- Item 1: a page test that a pressed decision shows its running state and that EVERY ending — landed,
  refused, too-old binary, failed spawn — replaces it; a redraw during the write keeps it.
- Item 2: `IsAllowed` against an explicit list, the pin moving in the same commit, and a page test
  that each picker entry says whether it leaves the machine.

## Definition of Done

- [ ] A decision in flight is visible, and every way it ends replaces that state.
- [ ] Whether the decision's state must survive a reload was decided in the plan round and recorded.
- [ ] The ranking boundary is an explicit allowed list, and its pin moved deliberately.
- [ ] The picker says, per entry, whether the choice sends finding text off the machine.
- [ ] `research/module_extension.md` updated; this plan promoted when both items ship.
