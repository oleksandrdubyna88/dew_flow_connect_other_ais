# PLAN — the gate reviews a diff against a base that moved under it

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/runners/Context/ContextAssembler.cs`,
> and whatever in `src_mcp/src/Server/PanelService.cs` decides what a code round is a diff OF.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [PLAN_the_server_has_a_release_line.md](../research/PLAN_the_server_has_a_release_line.md) — the
> round that found it.

## The symptom, measured

On 2026-09-08 a code round on `feat/the-server-has-a-release-line` produced **three Blocking
findings**, from two different vendors, saying the branch deleted `src_vs_code/src/chatPrompt.ts`,
`processLauncher.ts` and `sessionKey.ts` and reverted the extension from 0.31.8 to 0.31.7. codex
wrote: *"Installing the extension produced from this release therefore removes unrelated
user-facing features."* gemini asked for the PR to be split.

None of it was true. The branch touched nine files and not one of them was under `src_vs_code/src`
except a test.

**What actually happened:** the branch was cut from `origin/main` at `bc51a5b`. While the work was
in progress another session merged its own PR and `origin/main` became `3a42749`. The gate then
diffed:

```
git diff --numstat {baseRef}..{sha}        ← ContextAssembler.cs:53, TWO dots
```

Two dots is a diff between two *endpoints*. When the base has commits the branch does not, those
commits appear **inverted** — as deletions performed by the branch. Three dots
(`{baseRef}...{sha}`) diffs against the **merge base** instead, which is the question a reviewer is
actually asking: *what did this branch change?*

Measured on that branch, at that moment:

| | files | insertions | deletions |
|---|---|---|---|
| `origin/main..HEAD` (what the gate sent) | 17 | 239 | **1616** |
| `origin/main...HEAD` (the branch's own change) | 9 | 988 | 12 |

## Why this is worth fixing rather than working around

1. **The findings are confident and wrong.** Two vendors independently reported the same phantom
   defect as Blocking. A reviewer cannot tell a phantom deletion from a real one — the diff says the
   line was removed.
2. **It costs a whole round.** That round spent 932k input tokens, and three of its 24 gating
   findings were about work someone else had merged.
3. **It gets worse the busier the repository is.** Several agents work here at once; a base that
   moves during a review is the normal case, not the exception.
4. **The reviewer is asked to judge the change against a SCOPE.** A diff carrying somebody else's
   commits inverted makes the scope look violated, which is the one thing this gate exists to check.

## The change

`ContextAssembler.CollectAsync` takes `baseRef` and `sha` and runs `git diff {baseRef}..{sha}` for
the numstat and again per file. Both become `{baseRef}...{sha}`.

That is the whole fix, but it needs three checks around it:

1. **The merge base must exist.** `A...B` fails when the two have no common ancestor. Fall back to
   the two-dot form and SAY so in the round's log, rather than failing the review.
2. **Reviewing one commit still works.** The documented usage *"pass the commit as `branch` and its
   parent as `baseRef`"* — `HEAD~1...HEAD` — is identical to the two-dot form, since the parent IS
   the merge base. No behaviour to preserve separately, but a test should hold it.
3. **The audit line should name the base it resolved.** `git merge-base` once, logged beside the
   reviewer count: a round that says which commit it compared against is a round whose findings can
   be re-checked later.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `AChangeIsDiffedAgainstTheMergeBase_NotTheTipOfMain` — a fixture repo where the base has one commit the branch does not; the collected diff must not contain that commit's file | The exact failure above, reproduced from git rather than described |
| 2 | `AReviewOfOneCommit_IsStillItsOwnDiff` | The documented single-commit usage does not change |
| 3 | `UnrelatedHistoriesFallBackAndSaySo` | The one case three dots cannot answer degrades instead of failing the round |

`src_mcp/tests` already builds fixture repositories for `ContextAssembler`; these go beside them.

## Definition of Done

- [ ] Both `git diff` invocations use `...`.
- [ ] Unrelated histories fall back to `..` with a line in the round log.
- [ ] The round's audit names the resolved merge base.
- [ ] Tests 1–3 written, watched fail, and passing.
- [ ] `research/module_runners.md` records what a code round is a diff OF, and why.
