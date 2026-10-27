# RESULTS — what a round's rules budget actually holds (80 KB on 2026-09-15; 90 KB since 2026-10-16, see the last section)

> **Subject:** this repository at `c6ff353f`, conventions pin `126f58d7` (that day's release tip).
> **Harness:** a byte-count over the mounted corpus applying `RuleOrder`'s documented order, run from
> the repository root; the same order is asserted by `src_mcp/tests/RuleOrderTests.cs`, and the file
> sizes come from `find .agents/conventions/{common,csharp,rust,typescript} -name '*.md' | xargs wc -c`.
> **Date:** 2026-09-15 (re-measured at 90 000 bytes on 2026-10-16: last section). **Pinned:** budget
> 90 000 bytes (`RuleFiles.DefaultBudgetBytes`, raised from 80 000 on 2026-10-16; the 2026-09-15 sections
> below were measured at 80 000), whole-file selection, no resolver (it cannot run — see condition 2).
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
| A round's whole-file budget (`RuleFiles.DefaultBudgetBytes`) | **80 000 bytes** on 2026-09-15 (90 000 since 2026-10-16) |
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

`StageRulesTests.TheRotatedTail_CurrentlyFitsAtMostOneRule` (since renamed ...ThreeRules, at 90 000 bytes) pinned the fact and failed — as
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
in two places — its summary and its epic 3.

**And that grep was not enough, which is the part worth keeping.** A code round then found two more
sites it could not have matched, because they were not prose: the canary's own remarks still said
*the answer is zero*, and two test NAMES still carried the withdrawn claim —
`TheRotatedTail_CurrentlyFitsNothing_...` and `TwoBranches_SeeDifferentTails_SoTheCorpusIsStillRead`.
An identifier asserts as loudly as a sentence and no search for a phrase will find it. Both are
renamed here, to `TheRotatedTail_CurrentlyFitsAtMostOneRule` and
`TwoBranches_SeeDifferentTails_OnAFixtureWithRoomForThem`.

## RE-MEASURED, 2026-10-16 — the operator raised the budget to 90 000 bytes

> **Subject:** this repository at `origin/main` `2d98f2f8`, conventions pin `e29883f8`. **Harness:** the
> production collector (`RuleFiles.Collect`) over the real mounted corpus, driven by a throw-away loop
> (not kept): for `i` in 0..2999 collect with `RuleOrder.ForBranch(name)`, name = `feat/branch-<i>` for even
> `i` and `fix/issue-<i>-thing` for odd, at 80 000 and at 90 000, counting the mount rules that are not in
> `RuleOrder.TierRules`. Windows checkout (CRLF on disk); the collector normalises line endings, so the
> numbers are the same on Linux. The pieces that matter are kept as tests (below); the 3 000-name
> distribution is the only figure with no test of its own — the first hundred names of the same loop are
> pinned (at most 3, at least 1).

**Why.** The budget above is the 2026-09-15 figure. The corpus and this repository's own files grew
(`.agents/PROJECT.md` 7 452 -> 10 331 bytes; conventions pin moved on), and the base plus the eight tier
rules had reached **79 944 of 80 000 bytes — 56 bytes left**. Nothing outside the tier could fit on any
branch, so the new non-tier rule `common/test-leftovers.md` (4 706 bytes) reached no reviewer. The operator
(owner decision 2026-10-16, replacing the 80 000-byte budget) set `RuleFiles.DefaultBudgetBytes` to
**90 000**.

| | at 80 000 (before) | at 90 000 (after) |
|---|---|---|
| rule files in the corpus (5 base + 8 tier + 29 tail) / total size | 42 / 333 341 bytes | same |
| base + all 8 tier rules | 79 944 | 79 944 |
| **left for the rotated tail** | **56** | **10 056** |
| smallest rule outside the tier (`common/durable-status.md`) | 2 197 | 2 197 |
| files shown on the canary branch (`canary/the-tail`) | 13, 79 944 bytes, 29 omitted | 16, 88 666 bytes, 26 omitted |
| tail rules shown on the canary branch | 0 | 3: `common/gpu-lease.md`, `csharp/nuget-packages.md`, `common/durable-status.md` |
| tail rules shown, 3 000 branches | 0 on every branch | 1 on 908 (30 %), 2 on 1 847 (62 %), 3 on 245 (8 %); mean **1.78**, max 3 |
| `common/test-leftovers.md` shown | **0 of 3 000** | **294 of 3 000 (9.8 %)** |

**Reading it.** The budget bought the tail its first room since the base grew: one to three non-tier rules
on every round. It did **not** make `common/test-leftovers.md` reach reviewers on a typical branch — it is
outside the tier, so it is drawn, not owed, and it is drawn on about one branch in ten. Reach per rule on the
3 000 names: `durable-status` 877, `nuget-packages` 729, `gpu-lease` 397, `coai-caller-model` 364,
**`test-leftovers` 294**, `utc-timestamps` 246, `automated-checks` 229, then the rest at 216 and under. A rule
that must reach every reviewer belongs in `RuleOrder.TierRules`; that is a separate decision and it spends
budget (4 706 bytes of the 10 056 left).

**What is pinned, and how to reproduce it.** Run the built test executable, no `dotnet test`:
`src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*.StageRulesTests"` (and
`--filter-class "*.RuleFilesTests"` for the number itself).
- `RuleFilesTests.TheDefaultBudget_IsTheOperatorsNinetyKilobytes` — the constant is 90 000.
- `StageRulesTests.AtNinetyKilobytes_TheBaseAndTheTier_SpendExactlyTheirPinnedBytes` (named ...79944Bytes until 2026-10-18 and ...79967Bytes until 2026-10-19) — **the measuring test and the
  drift guard**: base 17 947 + tier 62 061 = 80 008 (17 883 + 62 061 = 79 944 until 2026-10-18, 17 906 + 62 061 = 79 967 until 2026-10-19, 17 925 + 62 061 = 79 986 until 2026-10-20, see the last three measurements). Its failure message prints the three numbers it found,
  so a conventions bump or an edit to this repository's own instruction files that moves them turns it red and
  the figures in this file are re-measured from that message. The unit is the collector's own: characters of
  the line-ending-normalised text.
- `AtNinetyKilobytes_TheRotatedTail_FitsAtMostThreeRules` — on `canary/the-tail` the tail is exactly
  `common/gpu-lease.md`, `csharp/nuget-packages.md`, `common/durable-status.md` (in that order), and none of the
  first hundred branch names of the loop above reaches more than three.
- `AtNinetyKilobytes_EveryBranchReachesAtLeastOneTailRule` — none of those hundred reaches fewer than one
  ("at most 3" alone is satisfied by 0, the 80 000 state).
- `AtNinetyKilobytes_TheTestLeftoversRule_IsShownOnTheBranchesThatDrawIt` — `fix/x` and `feat/e5` draw it,
  `main` and `feat/a` do not.

The old canary was named `TheRotatedTail_CurrentlyFitsAtMostOneRule`.

**Re-measured 2026-10-19 (E7, `--backfill-authors`), on the tree that merges E5 and `main`.** `.agents/PROJECT.md` names the new one-shot
mode, which `ARequestFaultIsNotAnOldBinaryTests` requires: +22 on E7's own branch (base 17 905, total 79 966 before the merge), and
on top of E5's `--kimi-clear-marker` entry (+23, the 2026-10-18 section below) the merged base is **17 928** and base + tier **79 989**,
leaving 10 011 of 90 000. The tail is unchanged — the three smallest tail rules (2 197, 3 065, 3 421 = 8 683) still fit and the fourth
(3 460, 12 143 together) still does not — and `StageRulesTests` pins the merged numbers. The figures above stay as the 2026-10-16
measurement. E6 re-baselines the same pin on its own branch (to 79 963); the branch that merges last re-measures once more.

**What this supersedes.** The "80 000", "1 328 spare", "77 562 / 2 438" and "1 of 24" figures above are the
dated measurements of 2026-09-15 and 2026-09-16 and stay as history. The "D18" of the feature plan named
the FULL budget (not its size) and still does: the feature stage now collects at 90 000.

**Not measured.** The prompt grows by up to 10 000 bytes a rule-carrying round; no vendor's context or cost
effect of that was measured here.

## RE-MEASURED AGAIN, 2026-10-18 — 23 characters of this repository's own base (E5's one-shot-mode entry)

> **Subject:** the E6 server branch (`feat/wsl-kimi-e6-server`, from E5's merge `056d1ce1`), conventions pin `e29883f8`. **Cause:** E5 added the `--kimi-clear-marker` mode to the one-shot list in
> `.agents/PROJECT.md` (the base rules, which `AGENTS.md`'s rule-selection procedure and the collector both read) — a deliberate growth that met the 90 KB pin (#848) and turned
> `StageRulesTests.AtNinetyKilobytes_TheBaseAndTheTier_SpendExactly79944Bytes` red on every platform: base 17 906 where 17 883 was pinned. **Harness:** the production collector over the real mounted corpus,
> through a throw-away loop inside `StageRulesTests` (written, run and deleted; not kept — the same method as the 2026-10-16 section above): the measuring test's own two sums with no budget in the way, then
> the canary branch at 90 000, then for `i` in 0..2999 a collect with `RuleOrder.ForBranch(name)`, name = `feat/branch-<i>` for even `i` and `fix/issue-<i>-thing` for odd, counting mount rules that are not in
> `RuleOrder.TierRules`. Windows checkout; the collector normalises line endings, so the numbers are the same on Linux and macOS (the CI jobs printed the same 17 906 and 79 967).

| | 2026-10-16 | 2026-10-18 |
|---|---|---|
| base (instruction files + this repository's own rules) | 17 883 | **17 906** (+23, `.agents/PROJECT.md`) |
| the eight tier rules | 62 061 | 62 061 |
| base + tier | 79 944 | **79 967** |
| **left for the rotated tail**, of 90 000 | 10 056 | **10 033** |
| smallest rules outside the tier | 2 197, 3 065, 3 421, 3 460, 4 706 | the same |
| files shown on the canary branch (`canary/the-tail`) | 16, 88 666 bytes | **16, 88 689 bytes** — the same three tail rules in the same order |
| tail rules shown, 3 000 branches | 1 on 908, 2 on 1 847, 3 on 245; mean 1.78, max 3 | **the same: 908 / 1 847 / 245, mean 1.78, max 3** |
| reach per rule, 3 000 branches | `durable-status` 877, `nuget-packages` 729, `gpu-lease` 397, `coai-caller-model` 364, `test-leftovers` 294, `utc-timestamps` 246, `automated-checks` 229, `dotnet-build` 216 … | **identical, rule by rule** |

**Reading it.** 23 characters moved nothing that matters: the leftover is still more than the three smallest rules together (8 683) and less than the fourth smallest added to them (12 143), so the tail is still
one to three rules, the distribution and every rule's reach are unchanged, and `common/test-leftovers.md` is still drawn on 294 of 3 000 (9.8 %). What changed is the two numbers the pin states and the byte count of the
canary branch. Re-pinned: `StageRulesTests.AtNinetyKilobytes_TheBaseAndTheTier_SpendExactly79967Bytes` (base 17 906 + tier 62 061 = 79 967); the at-most-three test, the at-least-one test and the
test-leftovers test are unchanged because the measurement leaves them true. The 2026-10-16 table above stays as history, with its 79 944 / 10 056.

## RE-BASELINED, 2026-10-19 — 19 more characters of the base (E6's one-shot-mode entry)

> **Subject:** the merged tree of `feat/round-authors-e6` (E5 final `8172eb193` and `main` at mcp 0.61.0 merged in), conventions pin `e29883f8`. **Cause:** E6 names its one-shot mode `--round-authors` in
> `.agents/PROJECT.md`'s one-shot list in the same commit that wires it (`ARequestFaultIsNotAnOldBinaryTests` requires it). **Harness:** the measuring test itself — its failure message printed
> `base 17925 + tier 62061 = 79986`; the 3 000-branch loop of the section above was NOT re-run.

| | 2026-10-18 | 2026-10-19 |
|---|---|---|
| base | 17 906 | **17 925** (+19, `.agents/PROJECT.md`) |
| the eight tier rules | 62 061 | 62 061 |
| base + tier | 79 967 | **79 986** |
| **left for the rotated tail**, of 90 000 | 10 033 | **10 014** |
| files shown on the canary branch | 16, 88 689 bytes | 16, 88 708 bytes (computed: the same three tail rules, asserted by the canary) |

**Reading it.** The leftover is still more than the three smallest rules outside the tier together (8 683) and less than the fourth smallest added to them (12 143), so the tail is still one to three rules.
The three canaries (`AtNinetyKilobytes_TheRotatedTail_FitsAtMostThreeRules`, `…EveryBranchReachesAtLeastOneTailRule`, `…TheTestLeftoversRule_IsShownOnTheBranchesThatDrawIt`) were re-run and are green unchanged.
The 3 000-branch distribution and the per-rule reach are not re-measured: a set of tail rules whose size falls between 10 014 and 10 033 would now be refused, so those figures above are the 2026-10-18 ones.
The measuring test is renamed `…SpendExactlyTheirPinnedBytes` so the next re-baseline changes its pins, not its name. Re-pinned: base 17 925 + tier 62 061 = 79 986.

**Re-measured once more, 2026-10-20, on the merge of E7 (`feat/round-authors-e7`) with `main` (which holds E6):** `.agents/PROJECT.md` names both `--round-authors` (+19) and `--backfill-authors` (+22), and the measuring test printed `base 17947 + tier 62061 = 80008` — 9 992 left of 90 000, still above the three smallest tail rules (8 683) and below the fourth added (12 143); the three canaries are green unchanged, and this supersedes both branch pins (E6 79 986, E7 79 989). Re-pinned: base 17 947 + tier 62 061 = 80 008.

## What this does not settle

- Whether a real team's branch names cover the real tail. The rotation is measured on a synthetic
  fixture; the population of branches a team actually opens is not measured here.
- Whether the tier is the RIGHT eight. It is the eight most findings are written against, chosen by
  reading the corpus, not by counting findings — the defect corpus that would let somebody count is
  itself an open plan (`research/PLAN_a_corpus_of_real_defects.md`).
- Anything about the resolver's selection, which has never run here.

## Three doctrines, not four

The 2026-09-06 note recorded "all four language doctrines" as starved. There are **three** — `csharp`,
`rust`, `typescript`. Corrected in the code comments and the plan; the measurement above counts what
exists.
