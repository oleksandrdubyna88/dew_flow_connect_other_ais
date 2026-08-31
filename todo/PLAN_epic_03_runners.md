# PLAN — epic 03: reviewer runners — spawn, isolate, survive

> Status: **plan only, nothing implemented yet.** Epic 3 of 6 under
> [PLAN_connect_other_ais.md](PLAN_connect_other_ais.md) (its Phase 2). Depends on epic 02 (the
> `Finding` contract and parse outcomes it feeds). This is where processes appear; the process-level
> tests run against a **fake CLI executable**, never against the real vendors.

## Goal

Turn "run six reviewers" from a sentence into something that survives a moving checkout, a killed
session, a hung CLI and a rate limit — with every failure mode named.

---

## Story 3.1 — The worktree manager

*As a round, I want one read-only tree pinned to a SHA that all my reviewers share, so every
reviewer reviews the same bytes and a crash never blocks the next run.*

Work: resolve branch → SHA at round start; `git worktree add --detach <storagePath> <sha>` once per
round, path carrying the session id, located outside the repository; `try/finally` removal with
`--force`; `git worktree prune` on `open`; best-effort removal on shutdown.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `RoundWorktree_IsPinnedToTheShaResolvedAtRoundStart` | commit to the branch mid-round → worktree unchanged |
| 2 | `Fanout_Throws_FinallyRemovesTheWorktree` | no leftover dir, no leftover `.git/worktrees` entry |
| 3 | `Open_PrunesAnOrphanFromAKilledSession` | pre-plant a stale worktree entry → `open` clears it, next round works |
| 4 | `HumanWorktree_IsNeverRemoved` | a worktree without our session-id path shape survives prune untouched |
| 5 | `LiveCheckout_IsByteIdenticalBeforeAndAfterARound` | hash the tree before/after |
| 6 | `WorktreeLivesOutsideTheRepository` | path is under storage, not under `repoPath` |

## Story 3.2 — Context assembly: the diff a reviewer deserves

*As a reviewer, I want the plan, the base ref and a diff with the noise already removed, so my
context window is spent on code and my review is against the stated intent.*

Work: `git diff <base>...<sha>` with pathspec exclusions (lock files, build output, minified, maps —
configurable, on top of `.gitignore`); binaries named with sizes, never inlined; the size cap with an
honest tail naming elided files; the bundle = plan text + branch + base + shaped diff.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `LockFiles_NeverReachTheDiff` | fixture repo with `package-lock.json` changed → absent |
| 2 | `RepoGitignoredOutput_NeverReachesTheDiff` | respects the target repo's own ignore rules |
| 3 | `Binary_IsNamedWithSize_NotInlined` | one line, no bytes |
| 4 | `OverCapDiff_NamesEveryElidedFileWithItsSize` | the honest tail |
| 5 | `UnderCapDiff_HasNoElisionNote` | no scary footer when nothing was cut |
| 6 | `Bundle_CarriesPlanBranchAndBase` | a reviewer can state what it reviewed against |

## Story 3.3 — Vendor runtimes: argv in, parsed outcome out

*As the scheduler, I want one `IReviewerRuntime` per vendor turning role + context into argv and raw
output into a parse outcome, so adding a vendor is one class and a config row — modelled on
`AgentRuntimes.cs`, refusal over default.*

Work: `codex` (`exec -s read-only --ephemeral -C <worktree> --output-schema <schema> -o <file>`),
`gemini` (`-p … -o json --approval-mode plan`), `deepseek` = codex runtime + `-c
model_provider=…`/`base_url`/`env_key` overrides; key applied to child env, never argv; unknown
provider refuses.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `CodexArgv_IsReadOnlyEphemeralAndSchemaBound` | every safety flag present, asserted literally |
| 2 | `GeminiArgv_IsHeadlessJsonAndPlanMode` | same for the other vendor |
| 3 | `DeepseekArgv_OverridesProviderAndBaseUrl_OnTheCodexRuntime` | one runtime, config-shifted |
| 4 | `Key_LandsInChildEnv_NeverInArgv` | argv joined string contains no key material |
| 5 | `UnknownProvider_Refuses_NamingTheCatalog` | never a silent default |
| 6 | `WorkingDirectory_IsTheWorktree_NeverTheLiveCheckout` | `-C`/cwd asserted |

## Story 3.4 — The bounded scheduler and the five failure modes

*As the server, I want the fan-out capped globally and per vendor with every failure a named
outcome, so six simultaneous launches degrade into a queue instead of into mystery timeouts.*

Work: global semaphore (default 3) + per-provider cap (default 2); per-reviewer timeout; one retry
with backoff on a rate-limit outcome; outcomes: `ok | nonZeroExit | timeout | unparseable |
rateLimited` — the fake CLI can produce each on demand.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `ConcurrentProcesses_NeverExceedTheGlobalCap` | fake CLI reports its own concurrency high-water mark |
| 2 | `OneVendor_NeverHoldsMoreThanItsPerProviderCap` | 6 jobs, one slow vendor → others still flow |
| 3 | `RateLimited_RetriesExactlyOnce_ThenReports` | 2 launches, then `rateLimited`, never a third |
| 4 | `Timeout_KillsTheProcessTree_AndNamesTheOutcome` | no orphaned child, outcome ≠ `nonZeroExit` |
| 5 | `UnparseableAfterOneRepair_IsItsOwnOutcome` | repair attempted once, then named |
| 6 | `FourOfSixAnswer_RoundResultListsWhoFailedAndWhy` | partial rounds are honest |

## Definition of Done

- [ ] All process tests run against the fake CLI; the suite touches no vendor and needs no network.
- [ ] The five outcomes are exhaustive in the type system (closed union) — a sixth cannot appear silently.
- [ ] A full fan-out leaves the machine clean: no worktree, no child process, no temp files.
