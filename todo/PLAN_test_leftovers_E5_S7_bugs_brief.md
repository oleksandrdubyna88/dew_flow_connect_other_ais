# Build brief — coai E5 story S7, the third drain PR: the `src_bugs/tests/` suite's `raw-temp` entries to zero

> Status: **plan only, 2026-11-11, by Opus.** Scope: the 26 `raw-temp` baseline entries of `src_bugs/tests/`. Tests only.
> A Sonnet coder builds from this brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain* ("`src_bugs` `Scratch.Delete`
callers ⇒ `TempDir`"); the earlier drain PRs are #977 ([bench](PLAN_test_leftovers_E5_S7_bench_brief.md)) and #979
([scripts](PLAN_test_leftovers_E5_S7_scripts_brief.md)). The parent is `dew_flow_conventions ·
todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-b to S7-e. Measured at `origin/main` `0107883e`, conventions
pin `09db328`.

## 1. Goal

The ingest suite drains (26 → 0; the file 760 → 734). Every folder a site makes is CREATED by the shared helper
`TempDir.For(<prefix>)` (`src_mcp/tests_leftovers/TempDir.cs`, referenced by this project); every path a site only names
(a file that must not exist, a file the test writes) is built under `TempDir.Root` (`Owned.TempRoot()`, the one helper
door). The entries leave the baseline in the same commit. The `src_bugs/tests/` scope stays, with zero entries.

**The remover stays `Scratch.Delete`, by decision.** `src_bugs/tests/Scratch.cs:21` calls
`SqliteConnection.ClearAllPools()` BEFORE it deletes, because a disposed `Corpus` still leaves the pool holding the
database file on Windows, and it reports a failed delete to the test's diagnostic channel. `TempDir.Dispose` does
neither. So in this suite `TempDir` makes the folder (exclusive create, the run folder as parent, a fresh name) and the
existing `Scratch.Delete` keeps removing it — the plan's line is honoured for the creation, which is what the guard reads,
without taking the pool clear away from the Windows leg. Where a site has no SQLite and no remover today (§ 4, shape D),
`using var … = TempDir.For(…)` adds one.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` holds no entry whose `file` starts with `src_bugs/`; 734 entries remain; nothing added or
   re-keyed; the `src_bugs/tests/` scope and its proof unchanged.
2. The C# guard (`LeftoversRepositoryGuardTests`) is green; the Node guard with `--baseline` is clean;
   `--baseline-shrinks --base origin/main` passes; no new `testsToDelete-exempt` marker; `shared/leftovers-exemptions.json`
   unchanged — it is keyed by LINE, and `src_bugs/tests/TheAuditTests.cs:191` is one of its rows, so every edit above line
   191 of that file keeps the file's line count (`src_vs_code/src/test/testLeftovers.test.mjs` goes red otherwise).
3. Every test keeps its name, its assertions and its remover; the ingest suite's counts are unchanged.

## 3. Constraints

- No change to `TempDir`, `RunTemp`, the gate, `Scratch`, the proof marker or the proof's child runs; no product code.
- Every prefix stays as it is (each site keeps its `what`); a site whose folder was a CHILD of a shared parent
  (`coai-bugs-tests/<guid>`) gets its own `TempDir` under the prefix `coai-bugs-tests-`.
- `TempDir.For(...)` creates the folder at once. Where today's code made the folder itself
  (`Directory.CreateDirectory(_dir)` in a constructor, or right after the path), that call stays (a no-op on an existing
  folder; removing it would move lines). Where today's code did NOT create it (`BothHalvesTests`, `TheServeLockTests`),
  the code under test now receives an existing empty folder: the tests decide whether that is fine; if one fails because
  the folder exists, stop and report.

## 4. The sites — four shapes

**A — a fixture field, removed by `Scratch.Delete(_dir)` in `Dispose`** (8): `BothHalvesTests.cs:33`,
`TheAuditTests.cs:26`, `TheBuiltBinariesTests.cs:37`, `TheCommentTests.cs:26`, `TheIngestTests.cs:20`,
`TheMigrationTests.cs:24`, `TheSchemaIsFrozenTests.cs:26`, `TheServeLockTests.cs:18`. Each is
`private readonly string _dir =` on one line and `Path.Combine(Path.GetTempPath(), "coai-<x>-" + Guid…[..8]);` on the next.
Replace the second line only with `TempDir.For("coai-<x>-").Path;` — two lines stay two lines. `Dispose` unchanged.
`TheServeLockTests`' `_dir + "-other"` stays: a sibling inside the run folder, removed by its own `Scratch.Delete(other)`.
Fields of the same shape made by `Directory.CreateTempSubdirectory("coai-<x>-").FullName` (2):
`TheHostsHelperIsTheOneThisDeployNeedsTests.cs:36`, `TheRootHelperFramesTwoRecordsTests.cs:26` → `TempDir.For("coai-<x>-").Path`.

**B — a local, removed by `Scratch.Delete(dir)` in a `finally`** (4): `TheAdminUploadTests.cs:209`, `:241`
(`Path.Combine(Path.GetTempPath(), "coai-bugs-tests", Guid…)`) and `TheRevokedKeyTests.cs:107`, `:153`
(`"coai-interleave-"`, `"coai-scope-"` + guid). → `var dir = TempDir.For("<prefix>").Path;` with `coai-bugs-tests-`,
`coai-interleave-`, `coai-scope-`. The `finally { Scratch.Delete(dir); }` stays.

**C — the test server's data folder** (1): `BugsServer.cs:61`, `DataDir = Path.Combine(Path.GetTempPath(),
"coai-bugs-tests", Guid…)`, removed by `Scratch.Delete(DataDir)` (`:231`). → `DataDir = TempDir.For("coai-bugs-tests-").Path;`.

**D — a work folder with no remover today, no SQLite** (7): `TheArchiveCheckTests.cs:175`, `:277`, `:288`, `:307`, `:328`,
`:348` (`var work = System.IO.Directory.CreateTempSubdirectory("archive-check-");`) and `ThePublishOutputCheckTests.cs:149`
(`"publish-output-check-"`). Two of them are `[Fact]` methods that use the folder only inside themselves —
`TheArchiveCheckTests.cs:175` (`AFileThatIsNotAZipIsNotAPass`) and `:277` (`AZipThatIsNotThereOrNotAZipIsNotAPass`): →
`using var work = TempDir.For("archive-check-");` and every `work.FullName` in that method becomes `work.Path`; the folder
is now removed when the test ends (it was left to the run folder before). The other five are `private static` HELPERS that
RETURN a path inside the folder to their caller — `:288` `ZipOf`, `:307` `FlatZip`, `:328` `Zip`, `:348` `Archive`, and
`ThePublishOutputCheckTests.cs:149` `Output` (plan gate, finding 2): a `using` there would delete the folder before the caller
reads it, so → `var work = TempDir.For("<same prefix>");` WITHOUT `using`, `work.FullName` → `work.Path`; the folder stays,
as today, inside the run folder, which `RunTemp.EndOfRun` removes.

**E — a path that is only named** (3): `TheArchiveCheckTests.cs:111` (`absent-<guid>.tar.gz`, must not exist),
`ThePublishOutputCheckTests.cs:128` (`absent-<guid>`, must not exist), `WhatTheArgumentsMeanTests.cs:103`
(`kw-<guid>.txt`, written and deleted in its `finally`) → `Path.Combine(TempDir.Root, <the same name>)`.

**F — the proof** (1): `RunFolderTests.cs:67` → `TempDir.Root.TrimEnd(…).Should().Be(RunTemp.Folder);`, as #977 did for the
bench proof (`TempDir.Root` returns `Path.GetTempPath()` unchanged, `Owned.cs:117-121`; line 72 still asserts the three
variables).

That is 26: A 10, B 4, C 1, D 7, E 3, F 1 — count them against the baseline's 26 `src_bugs/` entries before editing and stop if
the list and the file disagree. No file of this project but `RunFolderTests.cs` imports `CoaiMcp.Tests` and there is no global using: add `using CoaiMcp.Tests;`
to each other file you change — EXCEPT `TheAuditTests.cs`, whose inventory row at line 191 must not move (plan gate, finding
1): there write the type fully qualified, `CoaiMcp.Tests.TempDir.For("coai-audit-").Path;`, and add no line.

## 5. Steps — RED, GREEN, RED again

Lowered, one at a time: builds `nice -n 19 dotnet build <csproj> -c Debug -m:4 --disable-build-servers`; executables
through `node .agents/conventions/tools/low-priority.mjs`. Never `dotnet test`. Projects: `src_bugs/tests/*.csproj` and
`src_mcp/tests/*.csproj` (the C# guard lives there).

0. **Before.** Build both. Run the ingest suite's executable; record total/passed/failed/skipped. Run
   `CoaiMcp.Tests --filter-class "*LeftoversRepositoryGuardTests"`: green.
1. **RED.** Delete only the 26 `src_bugs/` entries; change no site. The guard class fails with exactly 26 `raw-temp`
   findings under `src_bugs/tests/`. Copy them.
2. **GREEN.** Rewrite the sites. Rebuild. The guard class green; the ingest suite's counts equal step 0's; the Node guard
   `--baseline` clean; `--baseline-shrinks --base origin/main` "only shrinks"; `plan-lifecycle` clean;
   `cd src_vs_code && node ../.agents/conventions/tools/low-priority.mjs node scripts/run-tests.mjs src/test/testLeftovers.test.mjs`
   green (the inventory's line keys held).
3. **RED again.** Put one shape-D line back (`TheArchiveCheckTests.cs`) → the guard class has exactly one `raw-temp`
   finding there; restore, green.
4. **Docs.** `research/module_tests.md`, the *S7, the drain* paragraph: the ingest suite third (26 → 0; 760 → 734) and the
   `Scratch.Delete` decision of § 1 in one sentence. The E5 status line's S7 sentence: the third PR. This brief's status
   and its `todo/README.md` row.

## 6. What this PR does not prove

- The other three scopes (734 entries) are untouched.
- Windows and macOS: the ingest suite's CI legs run as today; the pool clear on Windows is kept, not re-measured.

## 7. Definition of Done

- [ ] Step 1's 26 findings and step 3's one finding are in the PR description; step 2's counts equal step 0's.
- [ ] `raw-temp-baseline.json`: 734 entries, only `src_bugs/` lines removed.
- [ ] The inventory unchanged and `testLeftovers.test.mjs` green; no new marker.
- [ ] `research/module_tests.md`, the E5 status line and `todo/README.md` updated in the same commit as the code.
- [ ] The coai code round ran over the commit and every finding was resolved before the PR.
