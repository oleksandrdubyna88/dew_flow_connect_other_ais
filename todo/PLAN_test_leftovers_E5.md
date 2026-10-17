# PLAN — Test leftovers, E5: the tests move to `testsToDelete-coai-…` and the product's `coai-*` directories stay untouched

> Status: **plan only, nothing implemented yet, 2026-10-16; plan-gate round 1 findings applied 2026-10-17; blocked
> on the E1 release of `dew_flow_conventions`.**
> Stories of epic E5 of `dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md` (§ *Epics*, E5). Scope: the
> shared sweep rule `shared/temp-sweep.json`, the two test sweeps that read it (`src_vs_code/scripts/sweepTemp.mjs`
> through `run-tests.mjs`, and the assembly fixture `src_mcp/tests/TempDirsAreSwept.cs`), one C# and one Node
> owned-resource helper, the test temp sites of `src_mcp/tests`, `src_server/tests`, `src_bugs/tests`,
> `src_bench/CoaiBench.Tests`, `src_mcp/tests_fakecli`, `src_vs_code/src/test`, `src_vs_code/scripts`, `scripts/lib`,
> `http/`, one guard test, and the submodule pin `.agents/conventions`. The PRODUCT's sweep
> (`PanelService.PruneOldScratchDirs` and its callers) is out of scope and is proven unchanged.
>
> Split by Opus, 2026-10-16. **One cornerstone: S2** (the sweep), `model: fable` — its spec is a separate document,
> `todo/PLAN_test_leftovers_E5_S2_spec.md`, written by Fable before S2 is coded. Models per story below.

**Committed together with this document** (one commit, the shared `common/planning-docs.md` § *A plan split into
epics and stories*): its row in `todo/README.md` under *Currently open* —

```
| [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) | Stories of epic E5 of the family's test-leftovers rollout — **plan only, 2026-10-16; plan-gate round 1 findings applied 2026-10-17; blocked on the E1 release of the conventions.** Tests create only `testsToDelete-coai-<what>-<id>` folders, claimed by a sibling `.owner`; both test sweeps collect them on owner liveness and keep the old `coai-` test names only until a written end date; the product's `coai-*` runtime directories and their own six-hour sweep are untouched and proven so. Five stories: the pin, the sweep (**S2 is a cornerstone**, spec by Fable), the two helpers, the C# sites, the Node sites and the guard. |
```

and, in `dew_flow_conventions` (a separate commit there, by the parent's owner), the parent's ledger line becomes

```
Cornerstones: 2 of 3 — E1.S1 (the contract text in common/test-leftovers.md, model: fable), E5.S2 (coai's test sweep: temp-sweep.json, both sweeps, owner liveness, model: fable); reserved, not yet marked: E7 (rag_qln's shared store).
```

**Parent and contract are cited as paths, never links** (the shared `common/planning-docs.md` § *Across
repositories*): the parent `dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`; the contract
`dew_flow_conventions · common/test-leftovers.md` (mounted here at `.agents/conventions/common/test-leftovers.md`
once S1 moves the pin); the Node reference helper and sweep `dew_flow_conventions · tools/lib/owned.mjs`,
`tools/lib/leftovers-sweep.mjs`; the reference guard `dew_flow_conventions · tools/leftovers-check.mjs`; E1's stories
`dew_flow_conventions · todo/PLAN_test_leftovers_E1.md`. *Rule §n* below is a section of that contract.

## Goal

Every folder a coai test, test script or test-run CI step creates is named `testsToDelete-coai-<what>-<id>` by one
of two helpers (C# for the four .NET test suites, Node for the extension and the scripts), claimed by a sibling
`<folder>.owner`, removed by its scope on every path, and — when a run was killed — collected by the next run's
start-of-run sweep, which removes it only when its owner is provably gone. During a migration window with a written
end date the sweeps still collect the OLD `coai-` test names exactly as they do today. The product's own `coai-*`
working directories (a live chat, a round's answers, a Team server job) are never reachable by the new branch of
the sweep at all, and the old branch is today's code, unchanged.

**Done when:** the pin carries the E1 contract (S1); `temp-sweep.json` names the new test prefix, the old one with
its end date, and the unchanged `neverSwept` list, and both sweeps follow it with owner liveness (S2); the helpers
exist with their unwind, kill, open-handle and claim proofs, and the editor-host run leaks nothing (S3); no C# test
site creates a temp path outside the helper (S4); no Node site does, and the guard is red on every class of rule §7,
on an unregistered token and on a scan of nothing (S5).

## Facts the split rests on (read on `origin/main` 1fe25d19, 2026-10-16)

### The shared rule and the two test sweeps

- `shared/temp-sweep.json`: `prefix` `"coai-"`, `keepMinutes` 10, `neverSwept` = `coai-answers-`, `coai-repair-`,
  `coai-noworkspace-`, `coai-plan-`, `coai-server-job-`, `coai-chat-`, `coai-question-`, `coai-check-`, and a `why`
  array that names both readers.
- **The extension's sweep** is `src_vs_code/scripts/sweepTemp.mjs`: `readRule` `:20-22`; `leftover` `:30-36` takes
  an entry when it is a directory, starts with `prefix` (case-sensitive `startsWith`), is NOT a version cache
  (`coai-<digit>…`, `:24-27`), does not start with any `neverSwept` entry, and was last written more than
  `keepMinutes` ago — **age only, no owner**; `entriesOf` `:53-62` stats only names with the prefix; `removed`
  `:65-72` is `fs.rmSync(recursive, force)`; `sweep` `:79-86` returns `{ removed, failed }`. It is called by
  `sweepFirst` (`src_vs_code/scripts/run-tests.mjs:102-110`) as the first line of `main` (`:113`), which
  `sweepTemp.test.mjs:132-138` pins.
- **The C# sweep is not a sweep of its own.** `TempDirsAreSwept` is an xUnit v3 assembly fixture
  (`src_mcp/tests/TempDirsAreSwept.cs:5`) whose constructor (`:58-60`) calls the PRODUCT's
  `PanelService.PruneOldScratchDirs(Path.GetTempPath(), now − Rule.Keeps, ["coai-*"], Rule.NeverSwept)` — the same
  function the product's six-hour sweep calls (`src_mcp/src/Server/PanelService.cs:1416`, default prefixes
  `ScratchPrefixes` `:1363-1364` = `coai-answers-*`, `coai-repair-*`, `coai-noworkspace-*`). `PruneOldScratchDirs`
  (`:1449-1462`) → `SweepOne` (`:1465-1480`) → `DeleteIfDue` (`:1488-1494`: last write older than the cutoff, not
  `Kept` — version cache or an `except` prefix, `:1369-1375` — and `KimiHomesDisposed` `:1502-1512`) →
  `GitScratch.DeleteEvenIfReadOnly`. **So liveness cannot be added to the C# test sweep without either changing the
  product's sweep or giving the tests a sweep of their own** — S2 does the second.
- `src_mcp/tests/SweepRule.cs:13-25` reads `prefix`, `keepMinutes` and `neverSwept` from the file, climbing five
  folders from the test binary.
- **Only two suites sweep.** `src_server/tests`, `src_bugs/tests` and `src_bench/CoaiBench.Tests` have no
  start-of-run sweep; their leftovers are collected only when a `src_mcp` or extension run happens on the same
  machine.

### What pins what — the parent's "three places" are two different kinds

| Line | What it pins | After E5 |
|---|---|---|
| `src_mcp/tests/TempDirsAreSwept.cs:38` `Prefixes = ["coai-*"]` (used at `:264`) | the TEST prefix | moves (S2) |
| `TempDirsAreSwept.cs:65` `Rule.Prefix.Should().Be("coai-")` | the TEST prefix, read from the file | moves (S2) |
| `TempDirsAreSwept.cs:60`, `:112` `[$"{Rule.Prefix}*"]` | the TEST prefix, read from the file | moves (S2) |
| `src_vs_code/src/test/sweepTemp.test.mjs:32` `assert.equal(rule.prefix, 'coai-')` | the TEST prefix | moves (S2) |
| `sweepTemp.test.mjs:54-73`, `:99-130`, `:170` (`coai-old-1`, `coai-panel-abc`, an inline `{ prefix: 'coai-' … }`) | the TEST prefix in fixtures | move or stay as old-branch cases (S2) |
| `TempDirsAreSwept.cs:91` regex `CreateTempSubdirectory\(\s*"(coai-[a-z-]+)"\s*\)` over `src`, `runners`, `core` | the PRODUCT's prefixes | **stays `coai-`** — it proves `neverSwept` covers the product |
| `sweepTemp.test.mjs:46` regex `mkdtemp…(coai-[a-z-]+)` over the extension's non-test sources | the PRODUCT's prefixes | **stays** |
| `src_server/tests/TheTestSweepLeavesAJobAloneTests.cs:27` regex over `src_server/src`; reads only `neverSwept` (`:33-37`) | the PRODUCT's prefix `coai-server-job-` | **stays** — it never reads `prefix` |

The three product-prefix scans are incomplete today and E5 does not depend on them being complete (S2's new branch
never matches `coai-`): they see only a LITERAL in the one call shape — `RoundScratch.cs:79`
(`Path.Combine(GetTempPath(), $"coai-answers-{…}")`), `ConsultCheckScratch.cs:42`/`:62` (`Prefix` constant
`coai-check-`), `QuestionRowLaunch.cs:80`/`:83` (`ScratchPrefix` `coai-question-`) and `chatFastSettings.ts:44`
(`join(tmpdir(), 'coai-chat-fast-')`, no `path.`) are invisible to them and covered by `neverSwept` only because
their names happen to start with a listed prefix.

### The helper that exists

`src_mcp/tests/TempDir.cs`: `TempDir.For(prefix)` (`:31-32`) = `Directory.CreateTempSubdirectory(prefix)` — any
prefix; `Dispose` (`:41-54`) deletes through the product's `Runners.Files.GitScratch.DeleteEvenIfReadOnly`
(read-only files and folders, a link unlinked by its own name and never entered) and swallows `IOException` /
`UnauthorizedAccessException` — except `LinkTargetLostException`, which it re-throws on purpose. No claim, no
sibling marker. `src_bugs/tests/Scratch.cs:18-35` is a second, smaller helper (clears SQLite pools, reports a
failed delete through `TestContext.SendDiagnosticMessage`).

### The sites (counted on 1fe25d19; counts are lines, not distinct folders)

| Tree | Creation calls | Prefixes today |
|---|---|---|
| `src_mcp/tests` | 270 `CreateTempSubdirectory`, 119 `TempDir.For`, 182 lines with `GetTempPath` (many are `Path.Combine(GetTempPath(), "<fixed or guid name>")`) | 337 `coai-…`, **46 `testsToDelete-<no token>-…`**, 1 `human-wt-` (`WorktreeManagerTests.cs:116`) |
| `src_server/tests` | 24 `CreateTempSubdirectory`, 32 `GetTempPath` | 23 `coai-…` |
| `src_bugs/tests` | 4 `CreateTempSubdirectory`, 16 `GetTempPath` | 2 `coai-…`, `publish-output-check-`, `archive-check-` |
| `src_bench/CoaiBench.Tests` | 7 `CreateTempSubdirectory` | 7 `coai-…` |
| `src_mcp/tests_fakecli` | `Program.cs:471`, `Serving.cs:104` — FIXED file names in `GetTempPath()` | — |
| `src_vs_code/src/test` | 236 `mkdtemp`/`mkdtempSync`, 210 `tmpdir()` | 161 `coai-…`, 4 `testsToDelete-<no token>-…`, ~24 foreign: `baselinegrows-` (`baselineOnlyGrows.test.ts:32`, `:97`, `:191`, `:223`), `changelogguard-` (`changelogNamesTheRelease.test.ts:52`, `:180`, `:396`), `changelogsection-` (`changelogSection.test.ts:31`, `:178`, `:297`), `anchors-` (`releaseAnchors.test.ts:46`, `:127`, `:138`), `suppressions-` (`suppressionsOnlyShrink.test.ts:36`, `:135`), `draftrelease-`/`draftchain-` (`draftReleaseNotes.test.ts:50`, `:218`, `:269`), `docsonly-` (`docsOnlyTitle.test.ts:23`, `:88`), `resolver-` (`pathResolver.test.ts:34`, `:51`), `sweeptest-` (`sweepTemp.test.mjs:80`) — none of these is swept by anything |
| `src_vs_code/scripts` (test runners: `run-host`, `run-seam`, `run-contract`, `seam-*`, `live-*`, `measure-*`, `browserLayout`) | 83 lines | mostly `coai-…` |
| `scripts/lib/*.test.mjs` | 13 `mkdtempSync` | 3 `testsToDelete-<no token>-…` (`caller-transcripts.test.mjs:18`, `kimi-shim.test.mjs:24`, `vibe.test.mjs:11`), the rest `coai-…` or none |
| `http/run-contracts.mjs:127` | 1 | `coai-contracts-` |
| `.github/workflows/release.yml:307`, `:1154` | `mktemp -d` in two release-job steps on a hosted runner | — |

- **The `testsToDelete-<no token>-` names are not compliant and nobody's sweep matches them.** Under rule §1 the
  segment after the prefix is a repository TOKEN, so `testsToDelete-kimi-…`, `-vh-…`, `-junc-…`, `-sweep-…`,
  `-kimilock-…`, `-profile-owned` (a fixed name, `KimiAcpProfileTests.cs:92-93`) read as six unregistered tokens.
  Only the host-wide janitor (unmarked, older than an hour) ever removes them. Representative sites:
  `KimiFixture.cs:29`, `JunctionsTests.cs:27`, `:337-338`, `:376-377`, `KimiCredentialsLockTests.cs:25`,
  `AVibeHomesNeverLiveUnderTempTests.cs:23-24`, `:97`, `TempDirsAreSwept.cs:187`, `sweepTemp.test.mjs:147`, `:189`.
- **No cleanup at all, file level** (a file that creates or composes a temp path and contains no delete, `Dispose`,
  `TempDir.For` or `rmSync`): 43 C# test files (e.g. `src_mcp/tests/ACliRowsEffortTests.cs`,
  `ConsultantTurnTests.cs`, `KimiAcpProfileTests.cs`, `src_server/tests/JsonFileStoreTests.cs`,
  `SessionStoreConcurrencyTests.cs`, `src_bugs/tests/TheArchiveCheckTests.cs`) and 2 Node files
  (`src_vs_code/src/test/dataDirWriters.ts`, `testDiscovery.test.mjs`). An upper bound: some only read
  `GetTempPath()` as a path; S4 and S5 settle each.
- **Linked worktrees live as TOP-LEVEL leaves of the temp root**, registered in a test repository that is itself a
  temp folder: `FilesystemInvariantTests.cs:189`, `:219`, `:415` (`coai-linked-<guid8>`), `CadenceStoreTests.cs:161`
  (`coai-cadence-wt-<guid8>`); the others are temp folders of their own (`AReviewTreeTests.cs:219`,
  `AReviewTreeIsGivenBackTests.cs:268`, `:313`, `WorktreeManagerTests.cs:116`, `:392-402`, `:420-434`). Twelve
  `worktree add` call sites in `src_mcp/tests` (`AReviewTreeIsGivenBackTests.cs:270`, `:315`; `AReviewTreeTests.cs:221`;
  `CadenceStoreTests.cs:162`; `FilesystemInvariantTests.cs:190`, `:220`, `:416`; `WorktreeManagerTests.cs:117`, `:258`,
  `:360`, `:403`, `:434`). The parent does not mention worktrees for E5.
- **Tests that drive product code make PRODUCT-named directories in the real temp root** — about a thousand
  `coai-answers-*`/`coai-repair-*`/`coai-noworkspace-*` per server-suite run (`research/module_tests.md:415-419`, "the
  trade said out loud"); they are named by `RosterBuilder.cs:278`, `:280`, `RosterAssembly.cs:42`,
  `SecurityRoster.cs:153-154`, not by any test. No test helper can prefix them.

### The editor-host run (`src_vs_code/scripts/run-host.mjs`)

- `:39` `mkdtempSync(join(tmpdir(), 'coai-host-ws-'))`, `:49` `… 'coai-host-data-'`; `runTests` with
  `launchArgs: [workspace, '--disable-extensions']` (`:89`) — **no `--user-data-dir`**, although the comment at `:87-88`
  says "a throwaway user-data dir". `@vscode/test-electron`'s default profile is a persistent folder under the
  package's `.vscode-test/` cache (the coder confirms the default in the installed version and records it).
- **The two temp folders are never removed.** `main().then(… process.exit(0))` / `.catch(… process.exit(1))`
  (`:115-123`) end the process before the `.finally(() => { rmSync(workspace…); rmSync(dataHome…) })` (`:124-127`)
  can run — a `finally` callback is queued after the handler returns, and `process.exit` never returns. Every
  `npm run test:host` leaks both, pass or fail.

### Owner-liveness precedents in this repository (shape, not reuse)

- `WorktreeManager.cs:33` `TreeOwner(Pid, StartedUtc)`, `WriteOwner` `:431-440` (sibling `<tree>.owner`, written to a
  temp name and renamed over — a reader never sees half a marker), `OwnerIsAlive` `:446-458` — **two-valued: an
  unreadable marker reads as a DEAD owner**, the opposite of rule §3.
- `ProcessTracking.StartedAt(pid)` (`src_mcp/runners/Processes/ProcessTracking.cs:160-173`) returns `null` for "no
  such process" AND for "access denied" (`catch (Exception)`); `OrphanSweep.Same` (`OrphanSweep.cs:109`) compares
  start times within its `Slack`. Neither can be the test sweep's probe as is: rule §3 needs `gone` and `unknown`
  apart. They are the shape; S2's spec decides whether to widen `ProcessTracking` (product code, a new method beside
  the old one) or write the three-valued probe in the test library.

### Tests, CI, the pin

- C# tests run as MTP executables, never `dotnet test` (`.agents/PROJECT.md:36-37`); CI `build-test`
  (`.github/workflows/ci.yml:18-58`, ubuntu) runs `src_mcp`, bench, Team server and ingest-server suites; `macos`
  (`:260-290`) and `windows` (`:297-317`) run `src_mcp` (and on macOS server and bench). The extension job
  (`:319-373`) runs `npm ci --ignore-scripts --prefix .agents/conventions` (`:346-350`) — so a test there can import a
  tool from the mount — then `npm test`. `scripts/**/*.test.mjs` run in `build-test` (`:196-197`).
- **The pin.** `.agents/conventions` is at `e29883f`, which is today's `release` tip: the pre-E1
  `common/test-leftovers.md` (no owner, no start-of-run sweep, no `## Tokens` table). `pin-check.mjs` runs in
  `build-test` (`:209-210`); the moment E1 is released, every PR here is red until the pin moves (S1).
- `.claude/settings.json` and `.claude/hooks/load-instructions.mjs` are byte-identical copies of
  `.agents/conventions/settings/` (`.agents/PROJECT.md:118-120`), checked by `adapter-check.mjs` (`ci.yml:214-216`);
  `.claude/hooks/build-flags.mjs`, `ensure-user-guard.mjs`, `subagent-model.mjs` and `lib/user-guard-install.mjs`
  mirror `settings/hooks/` too. Between `e29883f` and the E1 branch (`feat/test-leftovers-e1`) nothing under
  `settings/` changed (`git diff --stat` empty, 2026-10-16) — S1 re-checks at the actual release tip.
- **The reviewer-rules budget.** `RuleFiles.DefaultBudgetBytes = 80_000` (`src_mcp/runners/Context/RuleFiles.cs:171`);
  the canary `StageRulesTests.cs:142-178` measures the REAL mounted corpus (tier plus rotated tail). PR #848
  (`feat/rules-budget-90kb`, "raise the reviewer-rules budget to 90 KB") is OPEN, every check green, mergeable
  (2026-10-16 22:02 UTC); it changes `RuleFiles.cs`, `RuleOrder.cs`, `StageRulesTests.cs`, `RuleFilesTests.cs`,
  `RuleOrderTests.cs`. `common/test-leftovers.md` is not a tier rule (`RuleOrder.cs:153-163`), but it IS in the rotated
  tail's candidate set, and E1 makes it several kilobytes longer — so the canary must be re-run on whichever of S1
  and #848 merges second.
- **E1's guard cannot be run over this repository as is.** `tools/leftovers-check.mjs` takes `--repo`/`--token`/`--rule`,
  but its discovery `isScanned` reads the CONVENTIONS' layout (`tools/*.test.mjs`, `.github/…`, `*.sh`, `*.ps1`), and
  its `RAW_TEMP` patterns have no `CreateTempSubdirectory` and no `GetTempFileName` — the C# family's main temp APIs.
  Its exemption grammar (`readExemptions`) and token check (`tokenFindings`) are exported and reusable.
- **The memory lesson this plan is built on:** coai tests share the temp root with live product data; a test sweep
  that selects by prefix alone has to be kept away from it by a deny-list (`neverSwept`) that only covers what someone
  remembered to add. The new prefix removes the overlap instead of listing it.

## Decisions

**D1 — The name.** `testsToDelete-coai-<what>-<id>`; the token `coai` is written once per helper (C#: one constant
in the shared test library; Node: one constant in `owned.cjs`) and is a row of the contract's `## Tokens` table.
`<what>` is today's prefix without its `coai-` and its trailing `-` (`coai-panel-` → `panel`, `testsToDelete-kimi-` →
`kimi`, `baselinegrows-` → `baselinegrows`), so every test keeps its meaning; the whole name ≤ 63, so `<what>` ≤ 36
with a 6-character id, ≤ 33 with .NET's `CreateTempSubdirectory` id on Windows (its length is measured by S3 and the
limit recorded). *Loses:* keeping `coai-` for tests (it IS the product's prefix — the whole risk); a second token for
the extension (one repository, one token).

**D2 — Where the C# helper lives.** A new test-support library, `src_mcp/tests_owned/CoaiMcp.TestsOwned.csproj`
(`IsTestProject=false`, `IsPackable=false`), referencing `CoaiMcp.Runners` (for `GitScratch`); `src_mcp/tests`,
`src_server/tests`, `src_bugs/tests` and `src_bench/CoaiBench.Tests` reference it; `TempDir` moves into it with its
namespace `CoaiMcp.Tests` kept, so 119 call sites compile unchanged. *Loses:* linked source files
(`<Compile Include="..\..\src_mcp\tests\…" />`) — four compilations of one helper, and `src_bench` does not reference
`Runners`; four copies — the drift `SweepRule.cs:9-11` was written against. Added to
`dew_flow_connect_other_ais.slnx`. The bench test project gains a `Runners` dependency through it; S3 records why.

**D3 — Where the Node helper lives.** `src_vs_code/scripts/owned.cjs` with a declaration `owned.d.cts`: CommonJS, so
the compiled tests (`tsconfig.json` `"module": "commonjs"`, out of `out/test/`) `require` it, and every ESM consumer
(`run-tests.mjs`, the `.test.mjs` sources, `scripts/lib/*.test.mjs`, `http/run-contracts.mjs`) `import`s it. The
relative path `../../scripts/owned.cjs` resolves the same from `src/test/` and from `out/test/`. *Loses:* an `.mjs`
helper (a CommonJS test cannot `require` ESM on every Node 22 a developer may run); a TypeScript helper (the ESM
scripts and the root `scripts/lib` tests could not load it without a compile). `owned.mjs` of the conventions is the
shape; its code is not imported (its token is `conventions`).

**D4 — The old names are collected, not renamed in place.** The OLD `coai-` test leftovers keep today's age-only
collection — through the SAME code that collects them today — until the end date S2's spec writes into
`temp-sweep.json`. The token-less `testsToDelete-<x>-` leftovers (Facts) are NOT this repository's sweep's business
(rule §4: own token only; `kimi`/`vh`/`junc` may be some other repository's token one day): S4/S5 list them before
and after, and the owner decides whether `bash .agents/conventions/tools/janitor.sh --dry-run` then a real run removes
them. `janitor.sh --token coai` is the post-suite check of the new names (Test plan).

**D5 — Product-named directories made by product code under test stay out of E5.** About a thousand per server-suite
run (`research/module_tests.md:415-419`) are named by the product; the product's own six-hour sweep collects them and
is unchanged. Giving the product a temp-root seam so tests can point it into an owned folder is a product change with
its own risk; S4 extracts it into `todo/PLAN_the_product_scratch_root_is_a_seam.md` (plan only) and records the
measured count. Recorded as a deviation from the parent's "tests create only `testsToDelete-coai-…`".

**D6 — One guard, a Node test, scanning the whole repository** — `src_vs_code/src/test/testLeftovers.test.mjs`, run
by `npm test` (it reads C# as text). It imports `readExemptions` and `tokenFindings` from the mounted
`.agents/conventions/tools/leftovers-check.mjs` (the extension job installs the mount's dependencies, `ci.yml:346-350`)
and supplies its own discovery and patterns. *Loses:* a C# guard (the C# suites cannot see the extension tree's
idioms as cleanly, and three C# suites would each need it); running E1's tool with `--repo .` (its discovery is the
conventions' layout and its patterns miss `CreateTempSubdirectory` — reported to the parent).

**Growth surface** (the shared planning rule § *A plan that creates something that GROWS*). Owned folders: about 650
creation sites, a few thousand folders per full run of all suites (measured before E5 by S4/S5: today's counts); each
carries one marker of ~50 bytes. Retired by the scope's teardown on every path; after a kill by the next run's
start-of-run sweep in any of the five suites (10 minutes, owner `gone`); by `janitor.sh --token coai` (one hour) when
no suite runs again. The old `coai-` test leftovers retire through the old branch until the end date, then through
the janitor only if the owner runs it — they never carried the prefix.

## Order

| Story | Model (spec · code · review) | Depends on | Lands |
|---|---|---|---|
| S1 — the pin bump to the E1 release | Opus (this section) · main session · Opus | E1 S5 released | first; every other merge waits for it (the required `pin-check`) |
| **S2 — the sweep: rule file, owner liveness, both sweeps — cornerstone** | **Fable** (spec) · Sonnet · Opus | S1 | after S1 |
| S3 — the two helpers, the per-suite fixtures, the editor-host run | Opus · Sonnet · Opus | S2 (its owner module) | after S2 |
| S4 — the C# sites | Opus · Sonnet · Opus | S3 | after S3, parallel with S5 |
| S5 — the Node sites, the guard, CI | Opus · Sonnet · Opus | S3 | after S3; its guard merges last — after S4's C# sites and S5's Node sites, every token-less `testsToDelete-<x>-` name among them, are merged (plan-gate round 1) |

S2's spec decides; where this document and S2's spec differ, the spec wins and the difference is a defect of this
document. Where this document and the contract differ, the contract wins: report it to the parent, do not resolve it
in code.

### Boundary with E1 (both sides — the shared planning rule § *A boundary between two plans*)

| Item | Built by | The other's part |
|---|---|---|
| The contract text, the `## Tokens` table (`coai` row under its `owns:` marker), `tools/janitor.sh` (with `--token`) | E1 (`dew_flow_conventions`) | E5 reads them through the mount; never edits them here |
| `tools/lib/owned.mjs`, `leftovers-sweep.mjs` | E1 | E5 mirrors their shape and order in C# and in `owned.cjs`, not their code |
| `leftovers-check.mjs` | E1 | E5's guard imports its `readExemptions`/`tokenFindings`; its discovery and patterns are E5's |
| The C# and Node helpers, both sweeps, the guard, the CI wiring here | E5 | — |
| The pin bump | E5 (S1) | E1 S5 releases |

## S1 — The pin bump to the E1 release

- **Models.** This section is the brief; the main session makes the commit; Opus reviews the diff.
- **Goal.** `.agents/conventions` points at the `release` tip that carries E1, so the mounted
  `common/test-leftovers.md` is the text S2–S5 encode, `janitor.sh --token` exists, and S5 can read the `## Tokens`
  table — with every family check and the reviewer-rules canary green on the new corpus.
- **Files.** `.agents/conventions` (the gitlink); `.claude/settings.json`, `.claude/hooks/**` only if the release
  changed `settings/` (they are copied byte for byte, never edited); `CLAUDE.md`/`AGENTS.md` only if
  `gate-snippet-check.mjs` says the gate block moved; `src_mcp/tests/StageRulesTests.cs` only if its canary must be
  re-baselined, with the measured numbers in the comment it already carries (`:146-160`).
- **Change.** `git -C .agents/conventions fetch origin release` → `checkout <release sha>`; `diff -r` the seven copied
  files against `.agents/conventions/settings/` and copy any that differ; commit in the existing form (#816):
  `chore(rules): pin shared conventions to release <short sha> — test leftovers: owner, start-of-run sweep, guard grammar, tokens table`.
- **#848's order.** Look at #848 first. If it is merged, S1 is based on it. If it is open, S1 does not wait for it;
  whichever of the two merges second rebases and re-runs `StageRulesTests`, `RuleFilesTests` and `RuleOrderTests` on
  the combined tree before merging, and its PR body records the canary's numbers (tier bytes, what the rotated tail
  fitted) on a CRLF and an LF checkout as the canary's comment already does.
- **Scenarios.**
  1. `node .agents/conventions/tools/pin-check.mjs` → 0 (RED first: on the old pin, after the release, it names
     `e29883f` as behind).
  2. `node .agents/conventions/tools/rules.mjs check --repo .`, `adapter-check.mjs .`, `gate-snippet-check.mjs`,
     `build-flags-check.mjs`, `plan-lifecycle.mjs` → 0.
  3. The mounted rule carries the table and this repository's row:
     `git -C .agents/conventions grep -n "^| \`coai\` |" common/test-leftovers.md` → one line; and
     `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` runs (Git for Windows' bash on Windows) and
     exits 0 or 3.
  4. `StageRulesTests`, `RuleFilesTests`, `RuleOrderTests` green on Windows (CRLF) and in CI (LF):
     `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*StageRulesTests" --filter-class "*RuleFilesTests" --filter-class "*RuleOrderTests"`.
  5. Every required check green; the full `src_mcp` suite and `npm test` unchanged (the pin changes no product code).
- **DoD.** Pin equals the release tip; the seven copied files byte-identical to the mount; the canary green on the
  combined tree with #848 whichever order; this document's status line still says *plan only*.

## S2 — The sweep: `temp-sweep.json`, owner liveness, both test sweeps — **cornerstone (2 of 3), `model: fable`**

- **Models.** Spec by Fable in `todo/PLAN_test_leftovers_E5_S2_spec.md` (its own row in `todo/README.md`, the shape
  of `PLAN_the_plan_gate_reads_the_committed_plan_E1_S2_spec.md`); code by a Sonnet coder from the spec; review by
  Opus against the spec and rule §3–§4.
- **Goal.** One rule file, read by both test sweeps, under which (a) the new `testsToDelete-coai-` branch removes a
  folder only when it is old enough AND its owner is provably gone, and preserves on `live` and `unknown`; (b) the old
  `coai-` test branch behaves exactly as today until a written end date and not at all after it; (c) no product
  directory can be removed by either branch that today's code would not remove, and the product's own sweep is
  byte-for-byte unchanged — so that being wrong here can never delete a live chat, a round's answers, a Team server
  job or a person's credentials behind a junction.
- **Files (expected; the spec fixes them).** `shared/temp-sweep.json`; `src_vs_code/scripts/sweepTemp.mjs`,
  `run-tests.mjs`; the owner/liveness half of `src_vs_code/scripts/owned.cjs` + `owned.d.cts`; the new
  `src_mcp/tests_owned/` library (owner module, the C# sweep) and its `.slnx`/`.csproj` wiring;
  `src_mcp/tests/SweepRule.cs`, `TempDirsAreSwept.cs`; `src_vs_code/src/test/sweepTemp.test.mjs`;
  `research/module_tests.md` (the paragraph at `:405-419`).

**What the Fable spec must settle** (nothing below is decided by this document):

1. **The shape of `temp-sweep.json`.** The new test prefix (`testsToDelete-coai-`, matched `^teststodelete[-_]coai[-_]`
   case-insensitively per rule §1, against `prefix`'s case-sensitive `startsWith` today); the old test prefix (`coai-`)
   and its END DATE (a calendar date in the file; how long — the rule's premise "nothing live makes such a name any
   more" is FALSE here while older checkouts and agent worktrees keep running the old suites, so the date counts from
   when old branches stop running, not from the merge — **plan-gate round 1:** the date is a VERIFIABLE condition,
   *no branch and no agent worktree still runs the old suites*, and the spec records the evidence it checked (for
   example: the open PRs and remote branches whose merge base is older than S2's merge, the agent worktrees of the
   family's machines and the commit each one is on, the last CI runs per branch) and the concrete calendar date it
   computed from that evidence; until the condition holds, the date is moved forward by a one-line PR and the old
   branch stays); what each reader does on and after the date (stop matching
   and say so in one report line — never a test that turns red on a calendar day and blocks unrelated PRs); whether
   `prefix` is renamed or a new key added (two runners and old checkouts read the file — an old checkout's
   `SweepRule` reading a renamed key must not crash or widen); `keepMinutes` and `neverSwept` unchanged in value.
2. **The old branch is today's code.** The spec proves the old branch reaches `PanelService.PruneOldScratchDirs` (C#)
   and `toSweep`'s deny-list (Node) with the same arguments as on 1fe25d19 — prefix, version-cache exclusion,
   `neverSwept`, 10 minutes — and that the new branch can never match a name that does not start with the new prefix.
   Whether the old branch also stops at a `testsToDelete-` name (it does today only because `startsWith("coai-")` is
   false for it — the spec says whether that is enough). **Plan-gate round 1:** "the same arguments" is proven by a
   TEST, not by reading — the old branch is driven with an injected clock and controlled ages (Node: the pure
   `toSweep(entries, now, rule)` with fixed `lastWriteMs`; C#: the `cutoff` argument of `PruneOldScratchDirs` with
   `Directory.SetLastWriteTimeUtc` on folders in a private root), never with a real wait, and it collects exactly the
   names today's code collects at the same `now`: one minute either side of the 10-minute boundary, a version cache,
   every `neverSwept` entry, a `testsToDelete-coai-` name (never the old branch's).
3. **Owner liveness, in both languages, three-valued.** The owner string `<host>@<os>:<pid>[:<start>]` per rule §3;
   the host is the **DNS host name** — Node `os.hostname()`, C# `System.Net.Dns.GetHostName()`, the janitor's
   `hostname` — compared case-insensitively, **never C#'s `Environment.MachineName`** (the NetBIOS name: upper-case,
   cut at 15 characters, so every marker would read as a foreign host to Node and the janitor); `<start>` where the OS
   gives it without spawning (C# `Process.StartTime` on Windows, Linux and macOS; Node only on Linux from `/proc`),
   omitted otherwise; the probe's `gone` (no process with that pid — C# `ArgumentException` from
   `Process.GetProcessById`, Node `ESRCH`) apart from `unknown` (access denied, any other failure) — which
   `ProcessTracking.StartedAt` (`:160-173`) and `WorktreeManager.OwnerIsAlive` (`:446-458`) do not keep apart; the
   2-second start tolerance (against `OrphanSweep.Same`'s `Slack`); a marker written by Node (no start) read by C#
   and the reverse; Windows and WSL sharing one host name with two pid spaces (`@windows` vs `@linux`); whether a
   test-run CI step ever writes a marker (then its pid is the runner worker, `$PPID`) or every CI site is exempt.
4. **The new branch, step by step** (rule §4): one listing; minimum age 10 minutes by LAST WRITE (Windows hands a
   recreated name its predecessor's creation time — `sweepTemp.mjs:41-42`); claim read from the sibling `.owner`;
   `gone` → remove folder then marker; `live`/`unknown` → keep with one reason line; unmarked and old → `gone` (the
   helper claims within milliseconds or does not create); an orphan `.owner` without its folder; a prefixed folder
   holding a `.git` FILE (a linked worktree of some clone — never judged as unmarked); a FILE with the prefix (not a
   directory); a name of another token or a token-less `testsToDelete-x-`; never failing the run.
5. **How it deletes.** Through the link-safe deleters only — C# `GitScratch.DeleteEvenIfReadOnly` with
   `LinkTargetLostException` reported and the folder kept, Node `rmSync` (which does not follow a junction — the
   spec cites or re-measures `sweepTemp.test.mjs:181-200`), plus whether the new branch must call
   `KimiHome.DisposeAllUnder` first as the product sweep does (`PanelService.cs:1502-1512`): a test leftover can hold
   a Kimi home whose `credentials` is a junction — to a fake person's folder in every test today; the spec states the
   guarantee that it can never be the real one, or makes the sweep safe if it were. **Plan-gate round 1:** a folder or
   marker that is already gone when the new branch deletes it is NOT a failure — C# swallows
   `DirectoryNotFoundException` and `FileNotFoundException` silently (no report line, nothing counted as failed), Node
   swallows `ENOENT` silently (`rmSync`'s `force` covers the folder; the marker's `unlinkSync` needs its own catch),
   so two suites that sweep the same temp root at once never fail and never report each other's removals.
6. **Where the C# sweep runs and who calls it.** The assembly fixture in `src_mcp/tests` today; whether the three
   other C# suites get the same fixture (rule §4: every run that creates the kind sweeps it at its start) and how —
   an `[assembly: AssemblyFixture]` is per assembly, the sweep code is the library's; whether `TempDirsAreSwept` keeps
   calling `PruneOldScratchDirs` for the old branch only.
7. **All pinning tests in one change** — the Facts table: the four TEST-prefix lines move; the three PRODUCT-prefix
   scans (`TempDirsAreSwept.cs:91`, `sweepTemp.test.mjs:46`, `TheTestSweepLeavesAJobAloneTests.cs:27`) stay
   `coai-` and green; the spec names which existing fixture tests become old-branch tests and which are rewritten.
8. **"Provably unchanged" as a check, not a sentence.** `git diff --exit-code origin/main -- src_mcp/src/Server/PanelService.cs`
   in every E5 PR (or the spec's narrower form); the product sweep's own tests (`ScratchDirsTests`,
   `TheScratchSweepNeverDeletesAKimiHomeUnderALiveTreeTests`, `ConsultCheckSweepTests`, `ConsultantHomeSweepTests`,
   `AScratchSweepRunsOnTheConsultationBeatTests`, `chatStoreSweep.test.ts`) unchanged and green. **Plan-gate round 1:**
   the diff check alone is not the proof — a CHARACTERIZATION test of `PanelService.PruneOldScratchDirs`
   (`PanelService.cs:1449-1462`, read on `origin/main` ce9cd298) lands on `main` FIRST, before any other S2 change: a
   private root with controlled inputs — the default `ScratchPrefixes` and an explicit prefix list, an `except` list,
   a version cache, a folder each side of the cutoff set by `Directory.SetLastWriteTimeUtc`, a read-only file — driven
   through the function's own `cutoff` argument (its injected clock), asserting exactly which names remain. It stays
   green, unchanged, in every E5 PR.
9. **The scenarios with their RED symptoms**, at least: a `testsToDelete-coai-…` folder OLDER than the window whose
   marker names a live process (this test process) survives — the case age alone gets wrong; one with a
   foreign-host marker and one with an unreadable marker survive; a probe-`gone` owner's folder and marker go; an old
   unmarked new-form folder goes; one touched within the window is not considered; `coai-chat-…`, `coai-answers-…`,
   `coai-server-job-…`, `coai-check-…` (every `neverSwept` entry) and `coai-0.58.0` of any age survive both branches;
   an old `coai-panel-…` goes before the end date and survives after it; `TESTSTODELETE-COAI-x-abc123` and
   `teststodelete_coai_x_abc123` are considered; `testsToDelete-kimi-x` and `testsToDelete-mcp-x-abc123` are not
   looked at; a junction inside a gone owner's folder is removed by its own name and its target keeps its bytes; a
   sweep error is one line and the suite still runs; *(plan-gate round 1)* a gone owner's folder or marker removed by
   another sweep between the listing and the delete (an injected `io`/deleter that removes it first, and once two
   real sweeps over one private root at once) ⇒ no failure, no report line, no throw; the old branch at an injected
   `now` collects exactly what today's code collects (item 2). Never the REAL probe on a dead child's pid (Windows hands
   exited pids to the next process — E1 met it mid-suite): a kill is simulated with an injected probe.
10. **Break-its**, each observed red: `neverSwept` emptied; the new prefix unanchored (`includes`); the old prefix in
    the new branch; the end date in the past; the probe's `unknown` mapped to `gone`; the case-insensitive match made
    case-sensitive.

- **Commands** (the spec may add): the `src_mcp` suite as in S1 with `--filter-class "*TempDirsAreSwept"` then whole;
  the server, ingest-server and bench executables; `cd src_vs_code && npm test`; on Windows, inside WSL, and the macOS
  CI leg for the C# start time.
- **DoD.** The spec's scenarios RED then GREEN on Windows and Linux; the break-its red; `PanelService.cs` unchanged
  AND the `PruneOldScratchDirs` characterization test (item 8) merged on `main` before the rest of S2 and green
  unchanged in every E5 PR; the old branch's equivalence proven with an injected clock (item 2); the end date's
  evidence and computed date written in the spec (item 1); concurrent sweeps silent on not-found (item 5); the three
  product-prefix scans unchanged and green; `module_tests.md` describes the two branches and the end date.

## S3 — The two helpers, the per-suite fixtures, the editor-host run

- **Models.** Brief by Opus (this section); code by a Sonnet coder; review by Opus against rule §1–§3, D1–D3 and S2's
  owner module.
- **Goal.** One C# and one Node way to make a temp folder: named `testsToDelete-coai-<what>-<id>`, cleanup registered
  at once, claimed beside itself, removed by exact name on every path, never throwing on the way out (except the one
  re-throw `TempDir` already has for a lost link target) — and the editor-host run stops leaking.
- **Files.** `src_mcp/tests_owned/` (S2 made it): `TempDir.cs` moved from `src_mcp/tests/`; `src_mcp/tests/TempDir.cs`
  deleted; the four test `.csproj` files reference the library (S2 may already have); `src_vs_code/scripts/owned.cjs`,
  `owned.d.cts` (S2 wrote the owner half); `src_vs_code/scripts/run-host.mjs`; new tests
  `src_mcp/tests/TempDirIsOwnedTests.cs`, `src_vs_code/src/test/owned.test.mjs`, `runHost.test.mjs`;
  `research/module_tests.md`.

**The change — C#.** `TempDir.For(string what)`: `what` validated (non-empty, no path separator, NUL or control
character, no leading `-`, ≤ the D1 length — an `ArgumentException` before anything exists); during S4's migration
it ACCEPTS the old call shape — a trailing `-` is stripped, and a leading `coai-` or `testsToDelete-` is stripped with
it, so `TempDir.For("coai-panel-")` makes `testsToDelete-coai-panel-<id>` and no call site has to change in this
story (S4 then rewrites the literals and S5's guard forbids the old shape in literals). Creation:
`Directory.CreateTempSubdirectory("testsToDelete-coai-" + what + "-")` (atomic, random id) → the `TempDir` object
exists and owns it (the caller's `using` is the registration) → write `<path>.owner` with S2's owner string through
`new FileStream(path + ".owner", FileMode.CreateNew, FileAccess.Write, FileShare.None)` — the C# twin of Node's `wx`:
a claim that already exists is somebody else's, and the helper never overwrites it (*plan-gate round 1*: no temp name
and `File.Move` over, the `WorktreeManager.WriteOwner` shape — a move replaces whatever claim is there; a reader that
meets a half-written claim reads it as unreadable ⇒ `unknown` ⇒ kept, the safe side). A claim that cannot be written —
`IOException` because it exists, or any other failure: delete the folder, throw. `TempDir.Root` is the library's ONE
`Path.GetTempPath()` call (the parent of every `TempDir`, and the root a test passes where the product only READS a
temp root — S4).
`Dispose`: `GitScratch.DeleteEvenIfReadOnly(path)` up to **5 tries 100 ms apart** (rule §2), the marker only after the
folder is gone; on giving up, ONE line through `TestContext.Current.SendDiagnosticMessage` (the `src_bugs`
`Scratch.cs:31-33` shape) naming the path, folder and marker left prefixed and claimed; `LinkTargetLostException`
still re-thrown (`TempDir.cs:49-53`, E4.1 of the Vibe-and-Kimi plan). `src_bugs/tests/Scratch.Delete` keeps its pool
clearing and calls into the library's removal, so the ingest-server suite has one deleter. `TempDir.At(...)` and the
implicit `string` conversion stay.

**The change — Node.** `owned.cjs` exports `ownedDir(scope, what, { parent } = {})` and `ownedDirSync(what)` +
`removeQuietly(dir)`, mirroring `owned.mjs` (`ownedDir` `:285-297`, `removeQuietly` `:267-283`): `fs.mkdtempSync`
of `testsToDelete-coai-<what>-` → `scope.after(() => removeQuietly(dir))` as the next statement (`scope` is node:test's
`t`, or any object with `after`) → `writeOwner` with `flag: 'wx'`; a claim that cannot be written removes the folder
and throws. `ownedDirSync` is for the few module-level `const base = mkdtempSync(…)` sites (e.g.
`scripts/lib/caller-transcripts.test.mjs:18`): it registers removal on `process.on('exit')`, and the S2 sweep is its
backstop after a kill. `removeQuietly` = `rmSync(dir, { recursive, force, maxRetries: 5, retryDelay: 100 })`, then the
marker; on failure one `console.warn` line, never a throw.

**The change — the editor-host run.** `run-host.mjs`: `workspace`, `dataHome` and a NEW `userData` come from
`ownedDirSync('host-ws')`, `('host-data')`, `('host-profile')`; `launchArgs` gains `--user-data-dir=<userData>` (the
comment at `:87-88` becomes true); the cleanup moves BEFORE `process.exit` — `main()` is awaited in a `try/finally`
whose `finally` removes the three folders, and only then the process exits with the code. The second launch keeps
its `named-but-missing` child of `dataHome`. If the default `.vscode-test/user-data` held state a scenario relied on,
the scenario says so and is fixed — no scenario may depend on a previous run's profile.

**Scenarios** (RED first: each written against the names above before the bodies exist):

1. `ATempDir_IsNamedForThisRepository_AndClaimedBesideItself` — `TempDir.For("panel")` → leaf matches
   `^testsToDelete-coai-panel-[A-Za-z0-9]{6,12}$`; `<path>.owner` exists in the temp root and parses (S2) to this
   process; the folder itself is empty. `TempDir.For("coai-panel-")` → the same shape. The measured id length on
   Windows and Linux recorded in the PR.
2. `AnInvalidWhat_CreatesNothing` — `""`, `"a/b"`, `"a\\b"`, `"-x"`, `"a\u0001b"`, one too long → `ArgumentException`,
   and no new `testsToDelete-coai-` entry in the temp root.
3. `ATempDir_IsGoneAfterAThrowInsideItsScope` — `using` scope that throws; after the catch, folder and marker gone.
4. `ATempDirHeldOpen_StaysPrefixedAndClaimed_AndSaysSoOnce` — Windows: a file inside opened with `FileShare.None`;
   `Dispose` returns without throwing after five tries; folder and marker remain; one diagnostic line; the handle
   closed, a second `Dispose` removes both. (Linux twin: a parent made read-only, the premise asserted.)
5. `ALostLinkTarget_StillFailsTheTest` — the existing junction case keeps re-throwing (the E4.1 guarantee survives the
   move).
5a. *(plan-gate round 1)* `AClaimThatAlreadyExists_IsNeverOverwritten` — through an injected name source, the helper
   is handed a name whose `<path>.owner` already holds another owner: the helper throws, removes only the folder it
   made, and the existing claim keeps its bytes. RED with a `File.Move`/`File.WriteAllText` claim (it overwrites).
6. Node `owned.test.mjs`: the same five in Node idiom (name and claim; invalid `what`; throw inside `t` scope via a
   subtest; held file on Windows with an exclusive open; `ownedDirSync` removed on `exit` by a child process run with
   `node -e`), plus `the claim is never inside the folder`.
7. `runHost.test.mjs` — `run-host.mjs` is refactored so its launch is injectable (`runTests` passed in, as
   `sweepTemp.mjs` takes `io`); with a stub that resolves and one that rejects, the three owned folders and their
   markers are gone after `main` settles, and the profile argument names the third. RED on `main`: the stub run leaves
   `coai-host-ws-*` and `coai-host-data-*` behind (the `process.exit` before `finally`).
8. **A killed run is collected** — a child test process (C#: the suite's own executable with a filter and an env flag,
   as E4's harness; Node: `node -e`) creates through the helper and exits without cleanup (`Environment.FailFast` /
   `process.abort()`); the folder and marker remain and name the child's pid; S2's sweep, given an injected probe
   answering `gone` for that pid and `now + 11 min`, removes both; answering `live`, keeps both.

- **Commands.** As S2, plus `npm run test:host` once on Windows and once under `xvfb-run` in WSL, listing
  `coai-host-*`/`testsToDelete-coai-host-*` in the temp root before and after (counts in the PR).
- **DoD.** Every scenario RED then GREEN; the C# claim is written with `FileMode.CreateNew`/`FileShare.None` and
  never overwrites (sc. 5a); the 119 `TempDir.For` callers compile unchanged and the four suites are
  green; `npm run test:host` leaves nothing in temp and passes `--user-data-dir`; `module_tests.md` names both helpers.

## S4 — The C# sites move to the helper

- **Models.** Brief by Opus; code by a Sonnet coder (may be split by suite across coders — one branch per suite, one
  PR each); review by Opus against the Facts and D1, D5.
- **Goal.** No C# test, test fixture or fake CLI creates a temp path except through `TempDir.For` (or under a valid
  inline exemption), and every test that today leaves a folder behind on any path stops doing so.
- **Files.** `src_mcp/tests/**/*.cs`, `src_server/tests/**/*.cs`, `src_bugs/tests/**/*.cs`,
  `src_bench/CoaiBench.Tests/**/*.cs`, `src_mcp/tests_fakecli/Program.cs`, `Serving.cs`; new
  `todo/PLAN_the_product_scratch_root_is_a_seam.md` (D5); `research/module_tests.md`.

**The change** (each site keeps its meaning; `what` per D1):

| Shape today | Becomes |
|---|---|
| `TempDir.For("coai-x-")`, `TempDir.For("testsToDelete-x-")` (119 lines in `src_mcp/tests`) | `TempDir.For("x")` |
| `Directory.CreateTempSubdirectory("coai-x-").FullName` with a `Dispose`/`finally` delete (the "forty classes", `TempDir.cs:9-10`) | a `TempDir` field or `using var` + `.Path`; the hand-written delete goes |
| the same with NO delete (the 43 files of the Facts) | the same — this is the leak fix; each file's test that proves it is scenario 1 |
| `Path.Combine(Path.GetTempPath(), "<fixed or guid name>")` used as a folder or file the test writes (e.g. `FilesystemInvariantTests.cs:189`, `:597`, `CadenceStoreTests.cs:161`, `AReviewTreeTests.cs:556`, `KimiAcpProfileTests.cs:92-93`) | a path INSIDE the test's own `TempDir` (`dir.At("linked")`) — nested, so no name and no claim of its own (rule §1) |
| `Path.GetTempPath()` passed as an argument the product only READS as a root and never writes under (e.g. `ForeignKimiTests.cs:28`, `:155`) | `TempDir.Root` (S3) — *plan-gate round 1:* no `Path.GetTempPath` stays outside the library, so S5's guard needs no exemption for it — or an owned folder when the reviewer cannot confirm "never writes" |
| a linked worktree as a top-level temp leaf (the four `coai-linked-`/`coai-cadence-wt-` sites) | a path inside the test's own `TempDir` (nested; rule §1 — no lock needed); a test whose subject is a worktree OUTSIDE the repository's folder keeps that by placing both under one owned folder |
| `src_bugs/tests` own `Scratch.Delete` callers | `TempDir` (S3 rewired `Scratch` to the library) |
| `tests_fakecli` fixed file names in `GetTempPath()` | the path comes from an environment variable the test sets to a file inside its `TempDir`; the fake CLI refuses to start without it (a fixed name in the shared root is how two parallel runs collide) |

Every `CreateTempSubdirectory` left in a C# test tree after this story is either inside the library or carries an
inline exemption the reviewer accepted; every `GetTempPath` is inside the library.

**The token-less names go first** (*plan-gate round 1*). Every C# creation site of a token-less
`testsToDelete-<x>-` name — 46 of the ~53 on 1fe25d19 (`KimiFixture.cs:29`, `JunctionsTests.cs:27`, `:337-338`,
`:376-377`, `KimiCredentialsLockTests.cs:25`, `AVibeHomesNeverLiveUnderTempTests.cs:23-24`, `:97`,
`TempDirsAreSwept.cs:187`, the fixed `testsToDelete-profile-owned` of `KimiAcpProfileTests.cs:92-93`, …), recounted
by `git grep` on the `main` the story starts from — becomes `testsToDelete-coai-<x>-<id>` through the helper in THIS
story, and the PR lists the count before and after (zero). S5's guard flags a token-less name (its `tokenless-name`
class) and lands only after S4 and S5's own Node sites are merged, so it never lands red. The existing junction and Kimi fixtures keep their
fake-person folders INSIDE their own `TempDir` (as `KimiFixture.cs:9-29` already promises).

**Scenarios** (RED first — scenario 1 is written and run on `main` before any site moves):

1. `NoTestLeavesAFolderBehind` (in `src_mcp/tests`, a test over a child run): snapshot the temp root's
   `testsToDelete-coai-*` and `coai-*` names minus `neverSwept` minus version caches; run the suite's own executable
   filtered to a sample of the 43 no-cleanup classes (one per file, chosen by the brief's coder and listed in the PR);
   snapshot again. RED on `main`: the new `coai-…` names the sample left, listed by name. GREEN: zero new names of
   either form.
2. **Nothing else moved:** every suite green on Windows, Linux and the macOS CI leg, with the same test count (the
   counts before and after in the PR).
3. **No leftover after a full run** (by hand, recorded in the PR): before, count by prefix family in `%TEMP%` and in
   WSL's `/tmp` — `coai-*` minus product, `testsToDelete-<no token>-*`, `testsToDelete-coai-*`, and the product
   `coai-answers-*`/`coai-repair-*`/`coai-noworkspace-*` (the D5 measurement); run all four C# suites; after:
   `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` lists nothing older than its window, zero new
   test-named `coai-*` or token-less names; the D5 product count recorded in the new follow-up plan.
4. The token-less leftovers listed before/after; removed only on the owner's word (D4).

- **Commands.** Build `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`; the four executables through
  `node .agents/conventions/tools/low-priority.mjs`; `dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes`.
- **DoD.** Scenario 1 RED then GREEN; 2–4 recorded; zero token-less `testsToDelete-<x>-` creation sites and zero
  `GetTempPath` outside the library in the C# trees (counts before/after in the PR); the follow-up plan in `todo/` with
  its README row; S5's guard (when it lands) finds nothing in the C# trees.

## S5 — The Node sites, the guard, the CI wiring

- **Models.** Brief by Opus; code by a Sonnet coder; review by Opus against rule §1, §7, D1, D3, D6.
- **Goal.** No extension test, test script, root script test, contract runner or test-run CI step creates a temp
  path outside `owned.cjs` (or under a valid exemption); one guard keeps every tree of the repository so, refuses an
  unregistered token, and is red on a scan of nothing.
- **Files.** `src_vs_code/src/test/**` (TS and `.mjs`), `src_vs_code/scripts/*.mjs` (the runners `run-seam`,
  `run-contract`, `seam-*`, `live-*`, `measure-*`, `browserLayout`), `scripts/lib/*.test.mjs`, `http/run-contracts.mjs`,
  `.github/workflows/release.yml` (`:307`, `:1154`); new `src_vs_code/src/test/testLeftovers.test.mjs`;
  `research/module_tests.md`.

**The change — sites.** `fs.mkdtempSync(path.join(os.tmpdir(), '<old>-'))` → `ownedDir(t, '<what>')` inside a test,
`ownedDirSync('<what>')` at module level, `what` per D1 — including the ~24 foreign-prefix sites of the Facts and the
4 + 3 token-less ones (recounted on the `main` the story starts from; *plan-gate round 1:* every token-less Node site
is migrated to `testsToDelete-coai-…` in this story's site PR, which merges BEFORE the guard's PR). A `try/finally` or `t.after` that only removed the folder goes. `dataDirWriters.ts` and
`testDiscovery.test.mjs` (no cleanup) get one. The runners in `src_vs_code/scripts` and `http/run-contracts.mjs` use
`ownedDirSync` and remove in a `finally` that runs BEFORE any `process.exit` (S3's run-host lesson — the reviewer
checks every runner for the same `then(exit)…finally` order). `release.yml`'s two `mktemp -d` run on a GitHub-hosted
runner whose VM dies with the job: each gets `# testsToDelete-exempt: raw-temp — a hosted runner's job-scoped
folder, gone with the VM` — or, if the reviewer prefers, `mktemp -d "$RUNNER_TEMP/testsToDelete-coai-<what>-XXXXXX"`
with a sibling owner `$(hostname)@linux:$PPID` (the runner worker is the owner of a CI step); the PR says which.
Deploy and install scripts (`deploy/**`, `src_server/install-*.sh`) are product, not tests — out of discovery,
listed by name in the guard's frozen exclusion list with the reason.

**The guard** — `testLeftovers.test.mjs`, pure functions over `(relative path, text)` plus one test over the real tree:

- **Discovery** (`git ls-files -z --cached --others --exclude-standard` from the repository root, as E1's): C# under
  `src_mcp/tests/`, `src_mcp/tests_owned/`, `src_mcp/tests_fakecli/`, `src_server/tests/`, `src_bugs/tests/`,
  `src_bench/CoaiBench.Tests/`; TS/JS under `src_vs_code/src/test/`, `src_vs_code/scripts/`, `scripts/` (tests and
  probes), `http/`, `.github/scripts/`; `.github/workflows/*.yml`; `**/*.sh`, `**/*.ps1`. Frozen exclusion list, pinned
  by a test: the guard's own file (it carries fixture strings), `deploy/**`, `src_server/install-agy.sh`,
  `src_server/install-cli.sh` (product installers), each with its reason. **Scan of nothing:** fewer than one file in
  each of `src_mcp/tests`, `src_vs_code/src/test` and `.github/workflows` ⇒ red, `scanned nothing under <tree> —
  discovery broke; the repository is not clean`.
- **Classes** (rule §7; whole-line comments skipped for matching, read for exemptions): `raw-temp` — `mkdtemp(Sync)?\(`,
  `tmpdir\(\)`, `process\.env\.(TEMP|TMP|TMPDIR)\b`, `GetTempPath`, **`CreateTempSubdirectory`**, `GetTempFileName`,
  and `mktemp` in shell and `run:` blocks — outside `src_mcp/tests_owned/` and `src_vs_code/scripts/owned.cjs`;
  `raw-worktree` — `"worktree",\s*"add"`, `worktree\s+add` — a finding unless its path argument is visibly inside an
  owned folder or it carries an exemption (the twelve sites of the Facts get one each, naming the owned folder they
  now live in); `docker-run`, `docker-name` (`src_server/smoke-image.sh:105` has `--rm`; its `--name`, if any, gets the
  prefix); `shared-store`; `ryuk-off`; plus this repository's own `old-prefix` — a string literal starting
  `coai-`/`testsToDelete-` passed to `TempDir.For`, `ownedDir` or `ownedDirSync` (the helper still accepts it, the
  guard does not).
- **Plan-gate round 1 additions.** `raw-temp` also refuses, in every test file outside `src_vs_code/scripts/owned.cjs`,
  IMPORTING `tmpdir` from `os` or `node:os` under any alias (`import { tmpdir } from 'node:os'`,
  `import { tmpdir as t } from 'os'`, `const { tmpdir: t } = require('node:os')`) and a bare `os.tmpdir` reference
  passed without a call; and every `Path.GetTempPath` (qualified, `System.IO.Path.GetTempPath`, or through
  `using static System.IO.Path`) outside `src_mcp/tests_owned/` — S4 leaves none (`TempDir.Root`). A new repository
  class **`tokenless-name`**: a string literal matching `^teststodelete[-_]` case-insensitively whose next segment is
  not `coai` followed by `-` or `_` (the exemption marker `testsToDelete-exempt:` and the label key
  `testsToDelete.owner` are not names); the sweep tests' deliberate foreign-token cases (`testsToDelete-kimi-x`,
  `testsToDelete-mcp-x-abc123`) are built from parts or listed in the frozen exclusion list with their reason.
  **Ordering:** S4's C# PR(s) and S5's site PR merge first; the guard's PR is rebased on them and lands with
  `tokenless-name` at zero on the real tree.
- **Exemptions and the token** through the mounted `readExemptions` and `tokenFindings`
  (`.agents/conventions/tools/leftovers-check.mjs`); the token is read from the two helper constants (exactly one in
  `src_mcp/tests_owned/**/*.cs`, one in `owned.cjs`, equal) and must be a row of the mounted `## Tokens` table; the
  mount missing ⇒ red with `the shared rules are not checked out — git submodule update --init .agents/conventions`.
- **Output:** one line per finding `<file>:<line> — <class> — <text>`, a cure sentence per class naming rule §7.

**Scenarios** (RED first: the real-tree test is written and run on the last `main` commit BEFORE S4 and S5's sites
moved):

1. `this repository creates no temp path outside its helpers` — RED before: the counts per tree and class recorded
   in the PR (roughly the Facts' table); GREEN after S4 and S5: zero.
2. `every class is refused once and a valid exemption passes` — fixture strings: `Directory.CreateTempSubdirectory("x")`
   in `src_server/tests/X.cs` ⇒ 1 `raw-temp`; `Path.GetTempFileName()` ⇒ 1; `mkdtempSync(path.join(os.tmpdir(), 'a-'))`
   in a `.ts` ⇒ 1; `mktemp -d` in a workflow `run:` ⇒ 1, with a well-formed exemption above ⇒ 0; `git worktree add`
   ⇒ 1; `TempDir.For("coai-x-")` ⇒ 1 `old-prefix`; `ownedDir(t, 'testsToDelete-x')` ⇒ 1; `TempDir.For("x")` ⇒ 0;
   `TESTCONTAINERS_RYUK_DISABLED=true` with an exemption ⇒ still 1; `testsToDelete-exempt: raw-temp — needed` ⇒ 1
   `exemption`; a commented-out `// Directory.CreateTempSubdirectory("x")` ⇒ 0; the same call inside
   `src_mcp/tests_owned/TempDir.cs` ⇒ 0; *(plan-gate round 1)* `import { tmpdir as t } from 'node:os'` in a test
   `.ts` ⇒ 1 `raw-temp`, the same in `owned.cjs` ⇒ 0; `System.IO.Path.GetTempPath()` in `src_server/tests/X.cs` ⇒ 1,
   inside `src_mcp/tests_owned/` ⇒ 0; `TempDir.For("testsToDelete-kimi-")` ⇒ 1 `tokenless-name` (and 1 `old-prefix`);
   `mkdtempSync(join(tmpdir(), 'testsToDelete-vh-x-'))` ⇒ 1 `tokenless-name`; `testsToDelete-coai-x` ⇒ 0;
   `// testsToDelete-exempt: raw-temp — …` ⇒ no `tokenless-name`.
3. `a scan of nothing is red` — an empty listing, one with no `src_mcp/tests` file, one with no workflow ⇒ red each.
4. `the token is a registered row and written once` — the real mount: `coai` ⇒ none; fixture rule texts without the
   table, with the token only in the repository column, or listed twice ⇒ 1 each; the two helper constants different
   ⇒ 1; `Coai` ⇒ 1.
5. `the exclusion list is exactly these files` — pinned.
6. **Break-it on the real tree** (by hand, recorded in the PR): one `Directory.CreateTempSubdirectory("coai-x-")` added
   to a `src_server` test ⇒ red naming `file:line`; reverted ⇒ green; the submodule de-initialised ⇒ red with the
   submodule sentence.

- **Commands.** `cd src_vs_code && npm ci && npm test` (the guard runs inside it); `node --test "scripts/**/*.test.mjs"`;
  `npm run test:contract`, `test:seam` once each; `actionlint` on `release.yml` (the `workflows` job).
- **DoD.** Scenario 1 red with the recorded counts, then green; every break-it red; the guard merged only after S4's
  and S5's site PRs, with zero `tokenless-name`, zero `tmpdir` imports and zero `GetTempPath` outside the helpers on
  the real tree; the extension job green WITH the guard; after a full run of every suite `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` on Windows
  (Git for Windows' bash) and in WSL finds only folders of a live run; `module_tests.md` updated.

## Test plan

| The parent's "proven by" | Where |
|---|---|
| Concurrent-owner survival: an old `testsToDelete-coai-…` with a live owner, a foreign-host and an unreadable marker survive | S2 (spec scenario list, item 9) |
| A fresh `testsToDelete-coai-…` and a `coai-chat-…` of any age survive; an old `testsToDelete-coai-…` and an old `coai-panel-…` go | S2 item 9 — the `coai-panel-…` only BEFORE the end date |
| The product dirs' own sweep is separate and unchanged | S2 item 8 (a diff check plus the product sweep's own tests) |
| The three pinning tests go RED on the config change and GREEN with the list | S2 item 7 — corrected: four TEST-prefix pins move; the three PRODUCT-prefix scans must stay green throughout |
| The guard's break-it on an extension test with a new un-prefixed `mkdtemp` | S5 sc. 2, 6 |
| A `run-host` test that the profile directory it passed is gone after the run | S3 sc. 7 — and the two temp folders it leaks today |
| (contract, not in the parent) unwind, open handle, a killed run collected, claim beside the folder | S3 sc. 3, 4, 6, 8 |
| (parent § Test plan) the janitor after a full suite finds only prefixed names | S4 sc. 3, S5 DoD (`janitor.sh --token coai --dry-run`) |

## Definition of Done

- [ ] The pin carries the E1 release; the mounted rule lists `coai`; the copied settings and hooks are byte-identical;
      the reviewer-rules canary is green on the tree that carries both S1 and #848 (S1).
- [ ] `temp-sweep.json` names the new test prefix, the old one with its end date, and the unchanged `neverSwept`;
      both test sweeps remove a `testsToDelete-coai-` folder only on owner `gone`; the old branch is today's code
      until the end date; `PanelService.cs` and the product sweep's tests are unchanged (S2).
- [ ] Every C# and Node test folder is made by a helper, named `testsToDelete-coai-<what>-<id>`, claimed beside
      itself, removed on every path, never throwing (but the lost-link re-throw); every C# suite sweeps at its start;
      `npm run test:host` leaks nothing and passes its own `--user-data-dir` (S3–S5).
- [ ] The guard is red on every class, on `old-prefix`, on `tokenless-name`, on a `tmpdir` import or a `GetTempPath`
      outside the helpers, on an unregistered token and on a scan of nothing; it landed after every token-less site
      was migrated (S4, S5).
- [ ] The product sweep's characterization test landed first and stayed green; the old branch is proven equal to
      today's with an injected clock; the end date rests on recorded evidence; concurrent sweeps never fail (S2).
- [ ] `research/module_tests.md` describes the helpers, both branches of the sweep, the end date and the guard; the
      D5 follow-up plan is in `todo/`.
- [ ] When the last story merges, this document's status becomes `E5 shipped, <date>; promotes with its parent` —
      never *implemented* while it sits in `todo/` — and the parent's E5 entry gets the same sentence. Parent and
      stories documents promote together, after E7. A separate dated item is left open for the END DATE: on that
      date a one-line PR removes the old branch from `temp-sweep.json` and both readers.

## Plan-gate round 1 (2026-10-17)

The coai plan gate over this document; the judge (Opus) accepted seven findings and rejected five.

**Accepted** (each applied where it belongs):

1. Every token-less `testsToDelete-<x>-` creation site (~53) moves to `testsToDelete-coai-…` in S4 (C#) and S5 (Node)
   before the guard lands; the guard flags token-less names (`tokenless-name`) — S4, S5, Order, DoD.
2. A characterization test pins `PanelService.PruneOldScratchDirs` on `main` before S2 changes anything, through its
   `cutoff` argument, and stays green — not only `git diff --exit-code` — S2 item 8, S2 DoD.
3. The old `coai-` branch is proven equal to today's by a test with an injected clock and controlled ages, never a
   real wait — S2 items 2 and 9.
4. The old-prefix end date is a verifiable condition (no branch or agent worktree runs the old suites), with the
   evidence recorded and a concrete date; until then the old branch stays — S2 item 1.
5. The C# claim is written with `FileMode.CreateNew`/`FileShare.None`, like Node's `wx` — no temp name and
   `File.Move` — S3 (change, sc. 5a, DoD).
6. The guard also refuses importing `tmpdir` from `os`/`node:os` under any alias outside the helper, and
   `Path.GetTempPath` outside the helper library (`TempDir.Root` replaces the read-only-root exemption) — S3, S4, S5.
7. Sweep deletions swallow `DirectoryNotFoundException`/`FileNotFoundException` (C#) and `ENOENT` (Node) silently, so
   two concurrent sweeps never fail — S2 items 5 and 9.

**Rejected:**

1. Claim before the folder exists (no unmarked window) — the contract's folder order is create → register teardown →
   claim, and an unmarked new-form folder is collected only after the 10-minute minimum age, which covers the window.
2. A Node start time off Linux — the contract makes the start optional, only where readable without spawning; an
   owner without a start is judged `live` while its pid exists, the safe side.
3. WSL and Windows confusing one host — the owner carries `@<os>`; a foreign `host@os` is `unknown` and kept.
4. A timeout on the liveness probe — the probes are non-blocking system calls (`Process.GetProcessById`,
   `process.kill(pid, 0)`, a `/proc` read) and never spawn, so there is nothing to time out.
5. A `Dispose` that throws on a leak — it contradicts the contract's never-throwing teardown; a leak is left prefixed
   and claimed with one warning line, and the audit surfaces it (S4 sc. 1, S4 sc. 3 and S5's DoD:
   `janitor.sh --token coai --dry-run` after a full run).

## Notes for the parent (`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`, E5 entry)

1. **The "three tests that pin the literal" are two kinds.** Of the cited lines only `TempDirsAreSwept.cs:38` and
   `:65` pin the TEST prefix (plus `:60`, `:112`, `:264`, which use it); the extension's TEST-prefix pin is
   `sweepTemp.test.mjs:32`, not `:45`. `TempDirsAreSwept.cs:91`, `sweepTemp.test.mjs:46` and
   `TheTestSweepLeavesAJobAloneTests.cs:27` scan PRODUCT sources for PRODUCT prefixes (the server test never reads
   `prefix` at all) — they must stay `coai-` and green; moving them would weaken the one check that keeps live
   product directories in `neverSwept`.
2. **The C# test sweep is the product's sweep.** `TempDirsAreSwept` calls `PanelService.PruneOldScratchDirs`
   (`:58-60`), the same function as the product's six-hour sweep (`PanelService.cs:1416`). "The product's own sweep
   untouched" and "the test sweep gains owner liveness" are only both true if the tests get a sweep of their own (S2).
3. `run-tests.mjs:102-113` is `sweepFirst` `:102-110` plus the call at `:113`; `sweepTemp.mjs:32-35` is `:30-36` and
   has a FOURTH filter the entry omits: the downloaded-server cache `coai-<digit>…` (`:24-27`; C# `PanelService.cs:1369-1371`).
4. **About fifty test sites already use `testsToDelete-` WITHOUT the token** (46 in `src_mcp/tests`, 4 in the
   extension, 3 in `scripts/lib`; `testsToDelete-kimi-`, `-vh-`, `-junc-`, `-sweep-`, `-kimilock-`, a fixed
   `testsToDelete-profile-owned`). Under rule §1 they read as unregistered tokens; no repository's sweep matches them,
   only the host-wide janitor. The entry's "missing prefix: all" hides them.
5. **`run-host.mjs` leaks more than the profile.** Its `.finally` (`:124-127`) runs after `process.exit` (`:115-123`)
   and therefore never: `coai-host-ws-*` and `coai-host-data-*` leak on every run. The profile is the
   `@vscode/test-electron` default under `.vscode-test/` — persistent, not a temp leftover — and the comment at
   `:87-88` claims otherwise.
6. **Product code under test names its own directories** (~1 000 `coai-answers-*`/`coai-repair-*`/`coai-noworkspace-*`
   per server-suite run, `research/module_tests.md:415-419`). No test helper can prefix them without a product
   temp-root seam; E5 extracts that into a follow-up plan (D5). "Tests create only `testsToDelete-coai-…`" is
   therefore true of what tests NAME, not of everything a test run makes.
7. **Three of the five suites have no start-of-run sweep** (`src_server/tests`, `src_bugs/tests`, the bench); the
   entry names two runners. S2's spec decides whether each gets the fixture (rule §4 says it should).
8. **Worktrees are in scope and the entry omits them**: twelve `worktree add` sites, four of them leaving a
   linked worktree as a top-level temp leaf (`FilesystemInvariantTests.cs:189`, `:219`, `:415`,
   `CadenceStoreTests.cs:161`). S4 nests them inside owned folders (rule §1), which needs no lock.
9. **For E1 (the contract and its guard), not this repository.** `leftovers-check.mjs`'s `RAW_TEMP` has no
   `CreateTempSubdirectory` and no `GetTempFileName` — .NET's main temp APIs, and E3's nineteen sites are all
   `CreateTempSubdirectory`; rule §7's example list has the same gap. And rule §4's premise for the old-prefix
   branch ("nothing live makes such a name any more") is false in a repository whose older checkouts keep running
   the old suites — harmless here (the old branch is today's behaviour, test folders only), but the end date has to
   be counted from when old branches stop running.
10. **"~49 sites with no cleanup"** is, at file level on 1fe25d19, 43 C# test files and 2 Node files with no removal
    call at all (an upper bound — some only pass `GetTempPath()` as a root); the per-site figure is S4's and S5's
    RED count.
11. **The owner host**, settled in the contract after the E4 split: the DNS host name (`os.hostname()`,
    `Dns.GetHostName()`, `hostname`), compared case-insensitively — never `Environment.MachineName`. coai's existing
    owner marker (`WorktreeManager.cs:431-458`) stores no host at all and reads an unreadable marker as a dead owner;
    it is product code, out of E5, and is named here so nobody copies it as the shape.
12. **Cornerstone:** E5.S2 is marked (2 of 3); the ledger line above replaces the parent's.
