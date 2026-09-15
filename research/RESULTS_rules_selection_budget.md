# RESULTS — what a round's 80 KB of rules actually holds

> Measured 2026-09-15 on `dew_flow_connect_other_ais`, conventions pin `126f58d7` (the release tip of
> that day). Evidence for epic 3 of
> [PLAN_the_rules_a_round_shows_are_drawn_at_random.md](../todo/PLAN_the_rules_a_round_shows_are_drawn_at_random.md)
> — whether the draw can be deleted — and the input the rule-modularization follow-up needs.

## The corpus against the budget

| | |
|---|---|
| Mounted rule files (`common`, `csharp`, `rust`, `typescript`) | **32** |
| Their total size | **272 121 bytes** |
| A round's whole-file budget (`RuleFiles.DefaultBudgetBytes`) | **80 000 bytes** |
| Share that can fit | **~29 %** |

Selection is whole-file — half a rule is a rule cut mid-sentence — so the ORDER decides what a reviewer
is judged against and what it never sees. That is the whole subject of the plan.

## What the deterministic walk shows

`RuleOrder.Walk`: the instruction files, then this repository's own rules, then the mount by tier
(the language doctrines, then `security`, `testing`, `reuse-first`, `coding-style`, `knowledge-base`),
then ordinal path.

**Shown — 13 files, 78 672 of 80 000 bytes:**

| bytes | file |
|---|---|
| 12 | `CLAUDE.md` |
| 549 | `AGENTS.md` |
| 7 452 | `.agents/PROJECT.md` |
| 4 456 | `.agents/rules/common/review-gate.md` |
| 2 717 | `.agents/rules/common/vendor-routing.md` |
| 4 696 | `csharp/doctrine.md` |
| 4 456 | `rust/doctrine.md` |
| 6 014 | `typescript/doctrine.md` |
| 8 748 | `common/security.md` |
| 25 082 | `common/testing.md` |
| 7 907 | `common/reuse-first.md` |
| 2 960 | `common/coding-style.md` |
| 3 623 | `common/knowledge-base.md` |

**Omitted — 24 files**, among them `common/development-workflow.md` (14 425) and
`common/http-contracts.md` (11 429).

## What this settles, and what it costs

**It settles the thing epic 3 had to prove.** The rules the 2026-09-06 starvation lost — `testing.md`,
`security.md`, `reuse-first.md` and every language doctrine — are shown on EVERY round under the walk,
and the two files that caused that starvation by sorting first are among the ones that fall off the end.
Deleting the draw therefore cannot restore the failure the draw was installed against. The tier fills
the budget almost exactly: 78 672 of 80 000, with 1 328 bytes to spare.

**It also prices the trade, which is the part not to bury.** The walk shows a FIXED set. The 24 rules
outside it — `git-workflow`, `pull-requests`, `reliability`, `platform-limits`, `task-lifecycle` and the
rest — move from *shown roughly every second or third round* under the draw to **never shown** on this
path. Determinism buys a gate a developer can trust and spends coverage to do it.

Three things bear on whether that is the right trade today:

1. **The omissions are not silent.** Every bundle names them — *"N further rule file(s) omitted for
   length: …"* — and tells the reviewer that a rule it was not shown is not a rule the change complies
   with. A reviewer can say so; it cannot apply the rule.
2. **This path is the only path for code rounds today.** The resolver that would select by what a change
   actually touches cannot run: its dependencies are absent from a fresh submodule checkout and it
   refuses a sibling worktree (dependency E1, in the conventions repository). Until that lands, a code
   round always falls back.
3. **`testing.md` alone is 25 082 bytes — 31 % of the budget.** Splitting the rules over 15 KB is what
   would make room for more of the corpus without touching the order. That follow-up's case rests on
   this table.

## Three doctrines, not four

The 2026-09-06 note recorded "all four language doctrines" as starved. There are **three** — `csharp`,
`rust`, `typescript`. Corrected in the code comments and the plan; the measurement above counts what
exists.
