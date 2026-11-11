# Build brief — coai E5 story S7, the first drain PR: the bench suite's `raw-temp` entries to zero, and the creation bypass goes

> Status: **built 2026-11-11, on branch feat/test-leftovers-e5-s7-first.** Scope: the 8 `raw-temp` baseline entries of `src_bench/CoaiBench.Tests/`,
> the `number != 945` clause of the standing shrink step, and the docs that count them. A Sonnet coder builds from this
> brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain*. The parent is
`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-a to S7-e of item 3.1. Rule:
`.agents/conventions/common/test-leftovers-baseline.md` (*The ratchet*, *Never*). Every count below was measured at
`origin/main` `409e2cfe` with the conventions pin `09db328`.

## 1. Goal

S7 drains the ONE `raw-temp-baseline.json` to zero, one pull request per suite (the plan's stage C; not a producer).
This is the first of those pull requests. It does two things:

1. **S7-a — the creation bypass goes.** `.github/workflows/ci.yml:211`, step *The raw-temp baseline only shrinks*,
   carries `&& github.event.pull_request.number != 945`. PR #945 was the file's one creation and is merged; the clause
   now only names a closed number. S7's first PR removes it, so the step reads
   `if: github.event_name == 'pull_request'`.
2. **The bench suite drains.** `src_bench/CoaiBench.Tests/` has 8 entries (of 778). Each site moves to the shared
   helper `TempDir` (`src_mcp/tests_leftovers/TempDir.cs`), which the bench project already references
   (`CoaiBench.Tests.csproj:21`). The 8 entries leave the baseline in the same commit as their sites.

Why the bench suite first: it is the smallest C# scope, every one of its sites already deletes its folder, and it
proves the drain's whole loop (C# guard RED, rewrite, GREEN, ratchet) on 8 lines before the large suites.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` holds no entry whose `file` starts with `src_bench/`; 770 entries remain; no other entry,
   and no scope, changed. The `src_bench/CoaiBench.Tests/` scope STAYS (with zero entries): removing a scope is a legal
   shrink, but keeping it changes nothing the ratchet allows and keeps `TheProofCheck_ClosesForEachScope`'s bench row
   (`src_mcp/tests/Leftovers/LeftoversBaselineTests.cs:228`) about a scope that exists. S7-d deletes the whole file at
   the end.
2. No line under `src_bench/CoaiBench.Tests/` is a `raw-temp` finding of the C# guard, and none carries a new
   `testsToDelete-exempt` marker. The expected-exemption inventory (`shared/leftovers-exemptions.json`) is unchanged
   (S7-e).
3. `ci.yml`'s shrink step has no `number != 945` clause; its comment says the bypass was removed by S7's first PR.
4. Each test keeps its name, its assertions and its cleanup on every path; no test is removed, skipped or renamed. The
   bench suite's test count is the same before and after.
5. `research/module_tests.md` and the E5 plan's status line record the drain: bench 8 → 0, the file 778 → 770.

## 3. Constraints

- **No new baseline key and no raised count** (the ratchet refuses both; *Never* of the baseline rule). Entries are only
  deleted.
- **No change to `TempDir`, `RunTemp`, the gate, the proof files' child runs or the marker
  `testsToDelete-baseline-proof:`** (the rule's last *Never*: the gate stays while a baseline names the scope).
- **No inline exemption** replaces an entry.
- No product code changes; tests only, plus `ci.yml` line 209-211 and the docs.
- `TempDir.Dispose` uses the product's read-only-aware deleter and swallows `IOException` and
  `UnauthorizedAccessException` — except `LinkTargetLostException`, which it lets through on purpose (a link's target that
  lost a file is a failing test, `TempDir.cs`); the hand-written `try { Directory.Delete } catch (IOException)` blocks it
  replaces go with it (they are the same thing written out per class — `TempDir.cs`'s own remarks). No bench site makes a
  link, so that one exception cannot arise here.

## 4. The sites — one shape each

| # | File:line | Today | After |
|---|---|---|---|
| 1 | `JudgeLaunchOptsOutOfTheGuardTests.cs:15`, `:17` | `private readonly string _dir = Directory.CreateTempSubdirectory("coai-judge-fake-").FullName;` and `Dispose() => Directory.Delete(_dir, recursive: true);` | `private readonly TempDir _dir = TempDir.For("coai-judge-fake-");` and `Dispose() => _dir.Dispose();` |
| 2 | `OnDiskTests.cs:18`, `Dispose` at `:20-` | the same field; `Dispose` wraps `Directory.Delete` in a `try` | the same field shape as #1; `Dispose() => _dir.Dispose();` |
| 3 | `OneRunIsOneSessionTests.cs:20`, `Dispose` at `:22-` | as #2, prefix `coai-campaign-` | as #2 |
| 4 | `PanelSettingsFileTests.cs:17`, `Dispose` at `:19-` | as #2, prefix `coai-settings-` | as #2 |
| 5 | `SessionsTests.cs:18`, `Dispose` at `:20-` | as #2, prefix `coai-sessions-reset-` | as #2 |
| 6 | `VendorsTests.cs:22`, `Dispose` at `:24-` | as #2, prefix `coai-vendors-` | as #2 |
| 7 | `JudgementSurvivesBeingInterruptedTests.cs:105` | a local `var dir = Directory.CreateTempSubdirectory("coai-save-").FullName;` with `try … finally { Directory.Delete(dir, recursive: true); }` | `using var dir = TempDir.For("coai-save-");`; the `try`/`finally` goes; the body is unchanged |
| 8 | `RunFolderTests.cs:67` | `Path.GetTempPath().TrimEnd(…).Should().Be(RunTemp.Folder);` | `TempDir.Root.TrimEnd(…).Should().Be(RunTemp.Folder);` — `TempDir.Root` is `Owned.TempRoot()`, the one helper door, which returns `Path.GetTempPath()` (`src_mcp/tests_leftovers/Owned.cs:117-121`); the assertion is the same, read through the helper. Line 72 still asserts that `TEMP`, `TMP` and `TMPDIR` each name the run folder, so the proof file's TEMP assertion stands |

Every use of `_dir` / `dir` as a `string` keeps compiling through `TempDir`'s implicit conversion
(`TempDir.cs`, `implicit operator string`). Where a call needs a `string` and the conversion does not apply
(a `params`, a generic, an interpolation), use `_dir.Path`. Add `using CoaiMcp.Tests;` only where a file does not
already see that namespace. The prefixes stay as they are: each site keeps its `what`.

## 5. Steps — RED, GREEN, RED again

Run everything from the repository root, lowered (`common/local-cpu-budget.md`): builds with `-m:4
--disable-build-servers`, test executables through `node .agents/conventions/tools/low-priority.mjs`, one at a time.

0. **Before.** Build `src_bench/CoaiBench.Tests` and `src_mcp/tests` (Debug). Run the bench suite; write down its total,
   passed, failed and skipped. Run `CoaiMcp.Tests --filter-class "*LeftoversRepositoryGuardTests"`: green.
1. **RED.** Delete only the 8 `src_bench/` entries from `raw-temp-baseline.json`; change no site. Rebuild nothing (the
   guard reads the tree). Run the guard class again: it must FAIL, listing 8 `raw-temp` findings, one per site of § 4
   (`src_bench/CoaiBench.Tests/<file>:<line> … raw-temp`). Copy the failure lines into the PR description.
2. **GREEN.** Rewrite the 8 sites as § 4 says. Rebuild. The guard class is green. The bench suite is green with the
   SAME total as step 0. `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token
   coai --identity "the multi-model review extension and server"` → `clean`, exit 0.
   `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline-shrinks --base origin/main` → exit 0 (a
   shrink).
3. **RED again (teeth).** In a scratch edit, put site #7's old line back; the guard class is red with exactly one
   `raw-temp` finding at that file. Restore; green again. Do not commit the scratch edit.
4. **ci.yml.** Remove the clause; update the comment on line 209 to say PR 945 was the one creation and S7's first
   drain PR removed its bypass. `node .agents/conventions/tools/plan-lifecycle.mjs` is clean.
5. **Docs.** `research/module_tests.md`, after the *Per-suite wiring and proof files* paragraph: one paragraph *S7, the
   drain* — the rule, the order (one PR per suite, this one first, the last deletes the file and drops `--baseline`), and
   the count (bench 8 → 0; file 778 → 770). `todo/PLAN_test_leftovers_E5.md`'s status line: S7 started, this PR. The
   README row of this brief.

## 6. What this PR does not prove

- The other five scopes (770 entries) are untouched; their PRs follow, one per suite.
- Windows and macOS: the bench suite runs on the CI legs that run it today; nothing here adds a leg.
- `NoTestLeavesAFolderBehind` (the plan's S7 scenario) is not added for this suite. Every one of the 8 bench sites
  already TRIED to remove its folder on every path before this change, and `TempDir` keeps that — but neither removal is
  guaranteed: the old blocks swallowed an `IOException`, and `TempDir.Dispose` swallows `IOException` and
  `UnauthorizedAccessException`, so a folder whose delete loses a race can stay. What bounds such a folder is where it
  lives, not the delete: it is inside the run's own claimed folder, which `RunTemp.EndOfRun` removes with its claim at
  the end of the run (and the owner-aware sweep collects after a killed run). The end-of-run audit of S6 (D9) reads the
  run folder before that, for folders the helper still holds; it does not make a failed delete succeed. This PR changes
  none of that, so the scenario would prove nothing about this change that the run folder does not already bound.

## 7. Definition of Done

- [ ] Step 1's RED output (8 findings) and step 3's one-finding RED are in the PR description; step 2's GREEN totals
      equal step 0's.
- [ ] `raw-temp-baseline.json`: 770 entries, the bench scope kept, nothing else changed (`git diff` shows only removed
      entry lines).
- [ ] `ci.yml` has no `945` clause; the shrink step runs on every pull request.
- [ ] No new exemption marker; the inventory test is green.
- [ ] `research/module_tests.md`, the E5 status line and `todo/README.md` updated in the same commit as the code.
- [ ] The coai code round ran over the commit and every finding was resolved before the PR.
