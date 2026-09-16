# RESULTS — what a round's 80 KB of rules actually holds

> **Subject:** this repository at `c6ff353f`, conventions pin `126f58d7` (that day's release tip).
> **Harness:** a byte-count over the mounted corpus applying `RuleOrder`'s documented order, run from
> the repository root; the same order is asserted by `src_mcp/tests/RuleOrderTests.cs`, and the file
> sizes come from `find .agents/conventions/{common,csharp,rust,typescript} -name '*.md' | xargs wc -c`.
> **Date:** 2026-09-15. **Pinned:** budget 80 000 bytes (`RuleFiles.DefaultBudgetBytes`), whole-file
> selection, no resolver (it cannot run — see condition 2).
>
> Evidence for epic 3 of
> [PLAN_the_rules_a_round_shows_are_drawn_at_random.md](PLAN_the_rules_a_round_shows_are_drawn_at_random.md)
> — whether the draw can be deleted — and the input the rule-modularization follow-up needs.

## The prediction, written before the run

Epic 3 could only delete the draw if the deterministic order showed the rules the draw was installed to
rescue. **Predicted:** the tier would cover `testing.md`, `security.md`, `reuse-first.md` and the
language doctrines within the budget, and `development-workflow.md` and `http-contracts.md` — the two
that caused the 2026-09-06 starvation by sorting first — would fall outside it. **No prediction was
made about how much budget would be left**, and that is where the surprise came from.

**Observed against predicted:** the covering half held exactly. The unpredicted part decided the epic:
the tier consumes 63 486 of the 80 000 bytes and the whole shown set 78 672, leaving **1 328 bytes** —
so a fixed order has no room for anything beyond the tier, and the other 24 rules would reach no code
round at all. That is not what "delete the draw" was expected to mean, and it is why the tail now
rotates by branch instead.

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

**It also priced a trade that was then refused.** A purely fixed order shows a FIXED set: the 24 rules
outside the tier — `git-workflow`, `pull-requests`, `reliability`, `platform-limits`, `task-lifecycle`
and the rest — would move from *shown roughly every second or third round* under the draw to **never
shown** on this path. Three reviewers of epic 3's plan round said independently that this ships a
coverage regression on the only path a code round takes today, and they were right.

**What shipped instead: the tail rotates by BRANCH.** The tier is fixed — every round, on every branch,
gets the eight rules above. The rest are ordered by a SHA-256 of *(branch, rule name)*, so:

- two rounds of one fix show **identical** rules, which is the defect the plan was opened for, because
  a branch is what a round is about and it does not change while a developer fixes what a round found;
- different branches read different parts of the corpus **as a mechanism** — measured on a twenty-rule
  fixture over sixty branch names, where every rule appeared
  (`AcrossEnoughBranches_EveryFamilyRuleGetsRead`). **In THIS repository the tail currently fits at
  most one rule, and none at all on a CRLF checkout; see the correction below.**
- nothing consults a clock, a counter or a random source, and anyone holding the branch name can
  reproduce the order exactly. `string.GetHashCode` would NOT do: .NET randomises it per process, which
  would have reintroduced the very defect inside its own fix.

The test that justified the draw — *"a rule that is never drawn is a rule that is never applied"* —
survives as `AcrossEnoughBranches_EveryFamilyRuleGetsRead`, with sixty branch names in place of sixty
seeds and the same assertion.

Three things still bear on the budget:

1. **The omissions are not silent.** Every bundle names them — *"N further rule file(s) omitted for
   length: …"* — and tells the reviewer that a rule it was not shown is not a rule the change complies
   with. A reviewer can say so; it cannot apply the rule.
2. **This path is the only path for code rounds today.** The resolver that would select by what a change
   actually touches cannot run: its dependencies are absent from a fresh submodule checkout and it
   refuses a sibling worktree (dependency E1, in the conventions repository). Until that lands, a code
   round always takes this path — which is exactly why the tail rotates rather than staying fixed.
3. **`testing.md` alone is 25 082 bytes — 31 % of the budget.** Splitting the rules over 15 KB is what
   would make room for more of the corpus without touching the order. That follow-up's case rests on
   this table.

## CORRECTION, 2026-09-16 — the rotated tail is all but inert here

Measured at conventions pin `5126421b`, after the rotation shipped:

| | CRLF (this Windows checkout) | LF (Linux, and CI) |
|---|---|---|
| base (instruction files + this repository's own rules) + all 8 tier rules | **78 855** | **77 562** |
| budget | 80 000 | 80 000 |
| **left for the rotated tail** | **1 145** | **2 438** |
| smallest rule outside the tier (`common/durable-status.md`) | **2 247** | **2 213** |
| **tail rules that fit** | **0 of 24** | **1 of 24** (always `durable-status.md`) |

`RuleFiles.Collect` skips an oversized file and keeps walking, so it tries every one of the 24 and omits
all of them here — all but the single smallest one on a checkout with LF line endings. **The branch
rotation therefore orders a queue that at most one rule is ever taken from.** The mechanism is sound
and the tests above are honest about their fixture; the claim that it preserves coverage *in this
repository* was not, and is withdrawn here.

**The line endings are not a footnote.** Selection counts each file's own bytes, so a CRLF checkout
inflates the corpus by one byte per line: the base and the tier grow 1 293 bytes while the smallest
tail rule grows only 34, and that difference is the whole distance between *nothing fits* and *one
thing fits*. A canary written as `min(tail) > leftover` would therefore have passed on this machine
and failed in CI. That is why the test named below asserts a BOUND through the production collector
instead of recomputing the arithmetic beside it.

What this means for the argument that produced it: three plan-round reviewers said a fixed order would
take 24 rules from "shown every second or third round" to "never". The rotation was the answer to that
objection, and at this corpus size **it does not answer it** — all 24 are unshown here, 23 of 24
under LF, and the one rule that does fit there is `common/durable-status.md` on EVERY branch, because
it is the only one small enough to be eligible at all. A queue of one does not rotate. The reviewers
were right and the remedy did not reach the problem.

What actually would: **rule modularization** (splitting the rules over 15 KB — `testing.md` alone is
25 082 bytes, 31 % of the budget) or **targeted selection** (the resolver, blocked on E1). Both are
already recorded as the follow-ups; this measurement is the strongest case yet for the first of them.

`StageRulesTests.TheRotatedTail_CurrentlyFitsNothing_AndSaysSoOutLoud` pins the fact and fails — as
news, not as a defect — the day the tail becomes reachable. Its message carries the re-baselining
procedure, because a test that fails on GOOD news has to say what to do about it: re-measure this
file, update the claim in `RuleOrder.ForBranch` and `module_runners.md`, and raise the bound in the
same change.

**The inventory, because a correction in three files is worth nothing while a fourth still asserts
the old thing.** `grep -rn "read different parts of the corpus" --include='*.md' --include='*.cs'`
found the claim in five documents and every one is corrected in the same change as this section:
this file (the bullet above), `RuleOrder.ForBranch`'s remarks, `research/module_runners.md`,
`RuleOrderTests.TwoBranches_SeeDifferentTails_...`'s remarks, and
[PLAN_the_rules_a_round_shows_are_drawn_at_random.md](PLAN_the_rules_a_round_shows_are_drawn_at_random.md)
in two places — its summary and its epic 3. `StageRulesTests` already read correctly.

## What this does not settle

- Whether a real team's branch names cover the real tail. The rotation is measured on a synthetic
  fixture; the population of branches a team actually opens is not measured here.
- Whether the tier is the RIGHT eight. It is the eight most findings are written against, chosen by
  reading the corpus, not by counting findings — the defect corpus that would let somebody count is
  itself an open plan (`todo/PLAN_a_corpus_of_real_defects.md`).
- Anything about the resolver's selection, which has never run here.

## Three doctrines, not four

The 2026-09-06 note recorded "all four language doctrines" as starved. There are **three** — `csharp`,
`rust`, `typescript`. Corrected in the code comments and the plan; the measurement above counts what
exists.
