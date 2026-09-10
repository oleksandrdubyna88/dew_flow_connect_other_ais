# PLAN — the gate reviews a diff against a base that moved under it

> Status: **IMPLEMENTED, 2026-09-10.** Shipped in pull request #176. Scope as built:
> `src_mcp/runners/Context/ContextAssembler.cs` and its one caller,
> `src_mcp/src/Server/PanelService.cs`.
>
> Related docs: [module_runners.md](module_runners.md) — *What a code round is a diff OF* — and
> [PLAN_the_server_has_a_release_line.md](PLAN_the_server_has_a_release_line.md), the round that
> found it.

## What shipped differently

1. **There were THREE call sites, not the two this document counts.** `cat-file -s` sizes the OLD
   side of a binary and takes a rev rather than a range, so it has no three-dot form at all — it
   would have gone on reading a blob from somebody else's commit while both diffs were fixed.
2. **The merge base is resolved explicitly rather than written as `A...B`.** Identical diff, three
   gains: the commit can be NAMED in the round's audit (three dots leave it inside git), the
   `cat-file` site is reachable by the same value, and the fallback is a branch of a conditional
   instead of an error parsed out of stderr.
3. **A SHALLOW checkout is told apart from unrelated histories.** `git merge-base` fails identically
   for both, and falling back silently on a truncated clone would have reproduced the entire defect
   on a repository whose histories DO meet. `rev-parse --is-shallow-repository` is asked only on the
   path that already failed. From the plan round.
4. **The fallback pins the ref to a commit** — the code round's finding, said by three reviewers
   independently, and it is this same defect one layer down: `origin/main` is read three times per
   round, and another session advancing it between two of them is a review of two snapshots that
   nobody could reproduce from a log naming only the ref.
5. **Which closed an argument injection two reviewers named.** Every value reaching a `{x}..{sha}`
   range or a `{x}:{path}` argument is an object id by construction now: a "ref" beginning with a
   dash is an OPTION to git, and `--output=` is one that writes a file. A base naming no commit is
   refused rather than handed to a command line.
6. **The reviewer is told when the diff is tip-to-tip**, in the diff's own header rather than only in
   our log — the one state in which a deletion below may be somebody else's commit.

RED was watched by restoring the two-dot form: the branch's diff carried `somebody-elses.cs` and
`theirs.png`, both committed to the base after the branch was cut.

## The open tail

The per-file `git diff` — one process per changed text file, plus one `cat-file` per binary — was
raised twice as a performance finding. It is real and it is pre-existing, and it is also
LOAD-BEARING: each text file rides its own diff so that elision stays whole-file, which is what lets
`DiffShaper` drop a file rather than a hunk when the budget runs out. Batching would mean re-splitting
one combined output back into files, which is the parsing this design deliberately does not do. Worth
its own change, not this one.

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
