

## E5 S6 — the two guards, the end-of-run audit, the `raw-temp` baseline and the CI legs (2026-11-05)

S6, the last part of the producer PR (`todo/PLAN_test_leftovers_E5_S6_brief.md`; the plan's § *S6*; the parent's § *Epoch 6* and entry 6.1). Built on `feat/test-leftovers-e5-producer` from head `c17ef377`, conventions pin `bd795847`, on the WSL machine (Ubuntu, `nice -n 19`, `dotnet build -m:4 --disable-build-servers`, one run at a time, a private `TMPDIR=/tmp/s6t`, empty after each whole run). Nothing was merged, the PR stays a draft, and condition (c) (the barrier scan) and the owner's scan are not S6's.

### What was built

| Part | Where |
|---|---|
| The C# guard: 8 library files and 2 test files, template copies at `197cc76`, manifest entries 41-52 (the manifest now has 52 entries) | `src_mcp/tests_leftovers/LeftoversGuard*.cs`, `src_mcp/tests/Leftovers/LeftoversGuardTests.cs`, `LeftoversGuardScriptsTests.cs` |
| The audit's copies: `LeftoversAuditTests.cs`, `LeftoversSelfTest.cs` (entries 41, 42) and the ONE ordered call (the fourth op of the `LeftoversSweepFixture.cs` entry) | `template-manifest.json` |
| The wrappers: `LeftoversRepositoryGuardTests` (coai's own), `LeftoversBaseline` (the shipped second pass through `node`, no port), `LeftoversInventory`, `LeftoversCoaiLayoutTests`, `LeftoversBaselineTests`, `LeftoversInventoryTests` | `src_mcp/tests/Leftovers/` |
| The Node wrapper `testLeftovers.test.mjs` (N1-N8); E9 and E10 in `runnerScripts.test.mjs`; F4 in `workflowsStartTestsThroughARunner.test.mjs` | `src_vs_code/src/test/` |
| The smoke script's trap before `mktemp` and an owner-form name | `src_server/smoke-image.sh` |
| The kind-5 pin `RunTemp.RequireChildTempInRunFolder` in four test classes | `RunTemp.cs`, `TheImageChecksEachInstallerTests`, `TheImageInstallsAgyFromItsManifestTests`, `TheCanaryFallbackRunsTests`, `src_bugs/tests/ShellScript.cs` |
| The inventory (112 rows, 28 pinned pairs) and the baseline (778 entries, six scopes) | `shared/leftovers-exemptions.json`, `raw-temp-baseline.json` |
| The CI legs | `.github/workflows/ci.yml` (build-test, macos, windows) |

### Deviations from the brief's numbers, each by file

- **The C# `raw-temp` count at creation is 549, the baseline is 778 (not 547 and 776).** `src_mcp/tests` has 458 entries (the brief: 456). Two of the 32 read-only sites, `ApiRuntimeTests.cs:28` and `LocalRuntimeTests.cs:131`, have a `"/tmp/…"` string literal on the SAME line as the `Path.GetTempPath()` call. The call went to `TempDir.Root` (same string), but the line stays a `raw-temp` hit for the literal. Both stay in the baseline. Server 57, bugs 26, bench 8, extension tests 219 and `scripts/` 10 are as the brief says.
- **R1's `raw-temp` was 642, not 638.** Five hits were in this story's own new test files (the C# guard JOINS neighbouring literals, so `"Create" + "TempSubdirectory"` is a hit). They were rewritten (`PlantedLines`: a tilde inside the name, taken out when the test runs) and add no baseline entry; the smoke script's line was already exempt (-1).
- **E10 found four pairs the brief does not list**: `deploy/bugs/deploy-cmd.sh`, `deploy/bugs/release.sh`, `deploy/coai-deploy-cmd.sh` and `deploy/systemd-release.sh` are named in `src_mcp/tests_leftovers/template-manifest.json` (it holds the frozen list as an op's `new` text, and the folder is a test root). The file is data read by `TheAdaptedCopyTests`; it starts nothing. The four pairs are on the list, so it holds 28 pairs (16 + 8 + 4). This is the brief's own rule for a pair found at creation (§ 10.6), not a new kind.
- **`RunTemp.RequireChildTempInRunFolder` did not exist** (the brief's assumption: it is S6's new code). `TheRootHelperFramesTwoRecordsTests` exists and reaches `install-env.sh` through `ShellScript.Fed`.

### The `why` cells of the pinned pairs, confirmed by reading

Each of the 16 pairs that exist today was confirmed by opening the test: `install.test.ts` reads `deploy/bugs/deploy-cmd.sh` (`deployScript('bugs/deploy-cmd.sh')`, lines 1329 and 1353) and names `release.sh` in a comment (line 1356); it RUNS `coai-deploy-cmd.sh` with ten refused inputs (`:470-500`, exit 90 from the first `case`, line 49; the `mktemp` at line 166 is after it) and reads `systemd-release.sh`. `TheCanaryFallbackRunsTests` sources `systemd-release.sh` (line 120) and calls only `canary`; the file returns at its line 267 (`[[ "${BASH_SOURCE[0]}" == "$0" ]] || return 0`) before `stage_from` (line 290) is even defined, and `preflight_token` (line 133) is never called. `TheHostsHelperIsTheOneThisDeployNeedsTests.cs:24` names `deploy-cmd.sh` in a comment. The ten `release.yml` pairs: five name it in a comment (`ShellScript.cs:26`, `TheArchiveCheckTests.cs:13`, `ThePublishOutputCheckTests.cs:11`, `TheWordListIsLoadedBeforeTheTransportTests.cs:22`, `changelogSection.test.ts:13`) and five read its text. No test starts a workflow. The other 12 pairs (8 + 4) are this story's own files and the manifest.

### Counts, per kind (the shipped Node guard and the C# guard on this tree)

| | Node guard, `--baseline --profile node` | C# guard + baseline |
|---|---|---|
| files read | 628 | see `LeftoversRepositoryGuardTests` |
| findings | 0 (clean) | 0 |
| `raw-temp` hits before the baseline | 229 (252 at the pin, minus 5 read-only, 6 `scripts/` markers, 9 `deploy/**`/`src_server`, 3 `raw-temp` elsewhere) | 549 |
| `owner-blind`, `legacy-name`, `exemption`, `token`, `docker-*`, `shared-store`, `ryuk-off` | 0 | 0 |

The inventory: 112 rows = 77 `raw-temp` (21 entry/library/read kinds as the brief's § 10.4-10.5, plus the 4 `reached`), 23 `raw-worktree`, 9 `legacy-name`, 2 `shared-store`, 1 `docker-run`. `RawTempRowsAtCreation` is 77.

### RED, observed, and the plant that shows each test has teeth

| Test | RED observed | Then |
|---|---|---|
| R19 `AFailureBeforeTheContainer_LeavesNoFolder` | before the move: "Expected Directory.GetFileSystemEntries(tmp) to be empty because the trap is set before the folder is made … found at least one item {…/tmp/tmp.eLoWvRVGHV}" (premise held: `chmod` was reached) | green after the trap moved |
| R20 the smoke folder's owner form | "Expected string to start with `testsToDelete-u1000-coai-smoke-` … but `tmp.TWp2iqhlFl` is too short" | green |
| R16 `AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds` (audit wired, `RunTempFixture.Dispose` still ending the run) | "Did not expect result.ExitCode to be 0 because the audit fails the run" (commit `77db4917`) | the ONE ordered call: green in `CoaiMcp.Tests`; the same test in server, bugs and bench |
| R5 the template's `RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())` | "coai tests: this run left 1 folder(s) it claimed — 1 never disposed" | reverted, green |
| R7 `RegistryWithoutTheRunFolder` returns all | "Expected collection {…run folder…} to not contain the run folder" (the test failed) | green |
| R18 `RunTempFixture` made `IDisposable` | "Expected typeof(RunTempFixture).GetInterfaces() {System.IDisposable} to not contain System.IDisposable" | green |
| R17 `EndOfRun()` in `DisposeAsync` | "Expected Directory.Exists(RunTemp.Folder) to be True … but found False" | green |
| R8 `Apply` returns the report unchanged | 7 of 15 `LeftoversBaselineTests` failed, including all four arms of `TheProofCheck_ClosesForEachScope` ("Expected collection to contain a single item, but the collection is empty") | green |
| R9 nothing caught | `System.ComponentModel.Win32Exception : An error occurred trying to start process 'no-such-node-zz9'` | green |
| R2 `IsFolded` answers false | the `BreakIt` rows of `LeftoversGuardTests` (the word split over `+`, `string.Concat`, interpolation holes, a middle piece, …): "but \"\" has a length of 0" | green |
| R11 only `tests/` a test root | "Expected value to be 7 … but found 1" | green |
| R4 the audit reads the shared root under the run's record, without the run-record clause | child a: "coai tests: this run left 1 folder(s) it claimed — 1 claimed in the temp root" (its audit named b's folder), so a exited non-zero | green |
| R10 on the real tree | a valid marker with no row: "in the tree, not in the inventory: src_mcp/tests/ApiRuntimeTests.cs:2:raw-temp" (C#); a blank line above `smoke-image.sh`'s marker: both "in the tree, not in the inventory: …:96" and "in the inventory, not in the tree: …:95" (C# and N3); the site's text changed: "the site changed under its marker: …:95:raw-temp — the inventory holds fa63c2a1…" (C# and N3); the row removed: "in the tree, not in the inventory" (C# and N3) | restored |
| R12, R14 the baseline file absent | R12 "Expected File.ReadAllText(outFile) to be \"True\" … but \"False\""; R14 "Expected file.Exists to be True" | green |
| R23 a 78th `raw-temp` row | "Expected rows.Count(row => row.Class == \"raw-temp\") to be less than or equal to 77 … but found 78" | restored |
| R21 E9 | a new `src_vs_code/scripts/zz-importer.mjs` importing `./seam-catalog.mjs`: E9 red (64 of 65) | removed, 65 of 65 |
| R22 E10 | a comment naming `probe-agy-write-block.mjs` in an existing `.cs`: red, a pair not on the list; a removed `namedBy` row: red; a row no test holds: red | restored |
| R24 the kind-5 pin, by hand once | `System.InvalidOperationException : TMPDIR='/elsewhere' is outside this run's folder '…/testsToDelete-u1000-coai-run-UgS918': a script of the inventory's kind 5 must be given a temp inside it.` | reverted, 4 of 4 |
| R15 N8 against a patched copy of the guard, by hand once | with `legacyNameHits(line)` replaced by `[]`: N8's first row "actual: [] expected: [ 'legacy-name' ]"; with `verdicts[index] === "broad"` replaced by `false`: its second row "actual: [] expected: [ 'owner-blind' ]" | the copy deleted, never committed |
| R6 the audit self-test's teeth | with the audit wired to do nothing, the step's loop (run by hand with `RUNNER_TEMP` set) exits 1: "the end-of-run audit did not fail the run (1)"; with the audit on it exits 0 for both values | audit restored |
| N5-N8 | these test the SHIPPED tool on private repositories. They have no standing RED: N8's is R15 above; N6 and N7 were green on first run because the tool they ask exists. Said plainly. | |

The seven planted violations of the brief's § 13 were each red naming the plant's file and line, in both guards where both read the root: the six C# roots (`src_mcp/tests`, `tests_leftovers`, `tests_fakecli`, `src_server/tests`, `src_bugs/tests`, `src_bench/CoaiBench.Tests`), `src_vs_code/src/test/` and `scripts/` (Node), a workflow with `mktemp` (C#) and with a bare-prefix `rm -rf` (`owner-blind` in both), `.github/scripts/` (both), `http/` and `src_vs_code/scripts/` (C#), and a new `deploy/zz/zz.sh` (both). A missing proof (the marker of `src_server/tests/RunFolderTests.cs`, then of `scripts/runFolder.test.mjs`) gave "proof missing — `…` is not a test file this scan discovered, or does not carry …" and every entry of the scope stood as `raw-temp`; a changed baselined line gave the new line `raw-temp` and the old entry "entry has no line" (`src_bugs/tests/TheAuditTests.cs`, `atomicFile.test.ts`). The break-its: B3 (a legacy literal in a `.cs` is `legacy-name` and `owner-blind`; in a `.mjs` `legacy-name`), B4 (the mount not initialised: the wrapper is red with "uninitialised submodule registers nothing, run `git submodule update --init .agents/conventions`" and no exception), B5 (a duplicate of a baselined line is red on the last occurrence), B6 (an entry removed: the site is red; `--baseline-shrinks` passes), B7 (`new key`, `raised count`, `new scope`, `changed proof`: exit 1 each), B8 (the baseline a symlink: both guards say "is a symlink", the shrink check exits 2), B9 (without the `RunTempFixture` line of the bench: `TheProducerAndTheSweep_LandTogether` goes red), B12 (R10). B10 and B11 are the grammar of the shipped reader (`exemption` and the site stays red) and are covered by its own tests.

### F24's second acceptance check, the C# half

The Node half and the mcp forced runs were recorded by the coordinator on #945 ([the comment](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/pull/945#issuecomment-6656216417): head `e8355d2b`, pin `bd795847`, the WSL machine: Node proof check 2 `proof missing` then 0; `RunTempTests` 21 of 21; both Node proof files 6 of 6). They were not run again. The C# half is `LeftoversBaselineTests.TheProofCheck_ClosesForEachScope`, a theory over the four `csharp` scopes: a private git repository holds a copy of the REAL proof file at its real path and a baseline with that one scope and no entries; with the marker line taken out the result is one `baseline` finding naming `proof missing`, with the real copy none. `M --filter-method "*TheProofCheck_ClosesForEachScope*"`: with the marker lines taken out of the four real proof files, 4 of 4 fail (the held arm: `proof missing`); with the markers in place, 4 of 4 pass. (The same test is also the part of R8 that fails 4 of 4 when `Apply` returns the report unchanged.) The other three suites' forced runs (`--filter-class "*RunFolderTests"`) are run by CI: @@CIFORCED@@.

### The audit found no leak

Turning the audit on in four suites found no run that left a folder it claimed: server, bench and bugs ended with nothing named, and the `CoaiMcp.Tests` run ended with the one unrelated failure below. No test had to be changed for a leak.

### The unattended full runs (not from CI)

Run on the WSL machine after the last code change (`72707ef6`; lowered, a private `TMPDIR`, one executable at a time, SIGINT left at its default for the child that tests a signal):

| Suite | Total | Passed | Skipped | Failed |
|---|---|---|---|---|
| `CoaiMcp.Tests` | 14633 | 14479 | 154 | 0 |
| `CoaiServer.Tests` | 628 | 622 | 6 | 0 |
| `CoaiBugs.Tests` | 346 | 333 | 3 | **10**: all `TheArchiveCheckTests`, "unzip: not found" (this WSL image has no `unzip`; CI's Ubuntu has it; not S6's) |
| `CoaiBench.Tests` | 131 | 129 | 2 | 0 |
| `cd src_vs_code && npm run lint` | clean | | | |
| `cd src_vs_code && npm test` | 6800 | 6779 | 21 | 0 (513 compiled, 24 source files) |
| `node scripts/run-lib-tests.mjs` | 437 | 433 | 4 | 0 |

An earlier unattended run (started with `setsid … &`, so SIGINT was ignored) failed one test, `ARunThatDiesIsRecordedTests.AServerStoppedByASignal_ClearsItsMarker…(signal: "INT")`, a timeout at 30 s: the ignored signal is inherited by the child. In the foreground it passes (4 of 4), and the second full run above, with the default signal, has 0 failed.

**Against the last documented round (the S5 section: 14341 / 14192 / 149 / 0; npm 6789 / 6768 / 21; lib 437 / 433 / 4), by name.** `--list-tests` at `ab789672` and at this head: 14285 names against 14580 (the list holds one line per distinct name). Added: `LeftoversGuardTests` 191, `NoSourceFileCarriesAControlByteTests` 28 (a theory over every source file: the new files), `LeftoversAuditTests` 15, `LeftoversBaselineTests` 15, `LeftoversGuardScriptsTests` 14, `TheAdaptedCopyTests` 12 (one row per new manifest entry), `LeftoversCoaiLayoutTests` 7, `LeftoversInventoryTests` 5, `LeftoversSelfTest` 2 (skipped without the variable), `RunTempTests` 1 (`AnOrphanClaim_…`), `OrphanClaimChild`, `SiblingChild` and `BaselineLocationChild` 1 each (child-only, skipped), `LeftoversRepositoryGuardTests` 1, `VibePathBudgetTests` 1 (S5's fixture test, added after its section was written). Removed and re-added with another random or path-bearing display name: `ZeroKnowledgeTests` 24 and `RunTempTests.AMemberDataReadAtDiscovery…` 1. The run totals grew by 292 (14341 to 14633); the 3 left over between 295 listed and 292 counted are rows with the same display name. The skipped grew 149 to 154: the two self-test facts and the three child-only classes. `npm test`: +11 = N1-N8 (8), E9 and E10 (2), F4 (1), and one more source file (24 against 23).

### The creation of the baseline, and the record for the PR

**How the file was made (once, by hand; nothing used is committed).** (1) `node .agents/conventions/tools/leftovers-check.mjs --repo . --print-baseline --profile node --token coai --identity "the multi-model review extension and server"` for the Node half (229 entries); (2) a scratch console project outside the repository with a `ProjectReference` to `CoaiMcp.Tests.Leftovers.csproj` that runs `LeftoversGuard.Scan(root, root)` and prints `file<TAB>line` for each `raw-temp` finding (549, all under the four C# roots); (3) a scratch Node script that reads each such line and hashes it with the shipped `siteSha256`, merges the two halves with the six scopes of the brief's § 8.1 and writes one entry per line, sorted by file and line; (4) the guard with `--baseline` answered clean (628 files) and `LeftoversRepositoryGuardTests` was green. The scratch files are deleted. The baseline: 778 entries = 458 (`src_mcp/tests/`) + 57 (`src_server/tests/`) + 26 (`src_bugs/tests/`) + 8 (`src_bench/CoaiBench.Tests/`) + 219 (`src_vs_code/src/test/`) + 10 (`scripts/`). The commit that adds it is `14370fdd`.

**The one creation in CI.** A temporary step on `build-test` (commit `b29c00c7`) ran `leftovers-check.mjs --repo . --baseline-shrinks --base "$BASE_SHA"` with the creation flag after `git fetch --unshallow origin`: CI run **42026897727**, job **127243249147** (`build · test · family checks`, green), the line `raw-temp-baseline.json: the one creation, allowed — it never had history behind b9dd72291ded3728e90756c1b509ee7d4cd3d201` (the PR's base). On that head `F4 no workflow holds the creation flag` was red, as designed (job 127243249632, `extension`, the only failing test of that job). The step is removed in `3fcba8a0`; F4 is green; `ci.yml` holds no creation flag. The standing step `The raw-temp baseline only shrinks` skips PR 945 by number (S7's first PR removes the clause). (q), the shrink check against the creation commit in a full clone at the last head, is the main session's (brief § 12.4).

**Audit self-test, RED on all three legs** (the brief's DoD "red for the right reason on three legs"): a throwaway branch `tmp/e5-s6-audit-red` (deleted) with the end-of-run audit set to do nothing and a workflow running the three steps: run **42026952168**, ubuntu job 127243430262, macOS job 127243429924, Windows job 127243430376: each failed in the step "The audit fails a run that leaves a claimed folder" with "the end-of-run audit did not fail the run (1)". The GREEN is the same step in `ci` run 42026897727 on Linux and macOS (the Windows one is in the next run).

**Windows.** In `ci` run 42026897727 the `Windows · mcp` `Test` step has 14631 tests, 1 failed: `AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds` (job 127243250229): on the elevated hosted runner a new file is owned by BUILTIN\Administrators, `Scope.IsMine` is false, and the audit's claim scan is blind to an orphan claim (the template's own note). Reported to the coordinator as a possible plan change; see the decision recorded below.

@@WINDOWS@@

