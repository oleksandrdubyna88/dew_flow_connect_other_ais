# Build brief — coai E5 story S7, the fourth drain PR: the Team server suite's `raw-temp` entries to zero

> Status: **built 2026-11-11, on branch feat/test-leftovers-e5-s7-server.** Scope: the 57 `raw-temp` baseline entries of `src_server/tests/`. Tests
> only. A Sonnet coder builds from this brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain*; the earlier drain PRs are #977
([bench](PLAN_test_leftovers_E5_S7_bench_brief.md)), #979 ([scripts](PLAN_test_leftovers_E5_S7_scripts_brief.md)) and #980
([ingest](PLAN_test_leftovers_E5_S7_bugs_brief.md)). The parent is `dew_flow_conventions ·
todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-b to S7-e. Measured at `origin/main` `395332cf`, conventions
pin `09db328`.

## 1. Goal

The Team server suite drains (57 → 0; the file 734 → 677). Every folder a site makes is created — and, where the site
removes it today or can own it, removed — by the shared helper `TempDir` (`src_mcp/tests_leftovers/TempDir.cs`, referenced
by this project); every path a site only NAMES is built under `TempDir.Root` (`Owned.TempRoot()`). Unlike the ingest suite
(#980), no server test touches SQLite (`grep -il sqlite src_server/tests/*.cs` → none), so `TempDir.Dispose` (the
product's read-only-aware deleter) can be the remover here. The entries leave the baseline in the same commit; the
`src_server/tests/` scope stays with zero entries.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` holds no `src_server/` entry; 677 remain; nothing added or re-keyed; the scope and its proof
   unchanged.
2. The C# guard (`LeftoversRepositoryGuardTests`) green; the Node guard `--baseline` clean; `--baseline-shrinks --base
   origin/main` passes; no new `testsToDelete-exempt` marker; `shared/leftovers-exemptions.json` unchanged. It is keyed by
   LINE, and `src_server/tests/TheImageSmokeUsesTheServerTests.cs:104` (`docker-run`) is a row: every edit above line 104
   of that file keeps its line count (`testLeftovers.test.mjs` goes red otherwise).
3. Every test keeps its name and assertions; the server suite's counts are unchanged.
4. No folder a test hands to code that must find it ABSENT is created early: where today's path does not exist when the
   code under test receives it, it still does not (shape A2).

## 3. Constraints

- No change to `TempDir`, `RunTemp`, the gate, the proof marker or product code. Prefixes stay (each site keeps its
  `what`); a child of the shared `coai-server-tests/<guid>` parent gets its own `TempDir` under `coai-server-tests-`.
- A `using` only where the folder's every user ends inside that scope; a helper or property that hands the folder out
  keeps it without `using` (it stays in the run folder, as today).
- Add `using CoaiMcp.Tests;` to a file only if it does not see the namespace — except
  `TheImageSmokeUsesTheServerTests.cs`, which takes the fully qualified `CoaiMcp.Tests.TempDir` (§ 2, item 2) — see its exact edit in shape A1.

## 4. The sites — shapes

**A1 — a fixture field of a named path that the fixture creates itself** (`Directory.CreateDirectory(_dir)` in the
constructor or an init) **and removes in `Dispose`** (a `Directory.Delete` there): → `private readonly TempDir _dir =
TempDir.For("<prefix>");`, the `CreateDirectory` line stays or goes as § 2, item 2 allows (it is a no-op on an existing folder),
and the `Directory.Delete(...)` statement (with its `try`/`catch` if it only guards that delete) becomes `_dir.Dispose()`.
Uses of `_dir` as a `string` compile through `TempDir`'s implicit conversion; use `_dir.Path` where they do not.
Candidates (check each): `AMalformedTokenIsAnUnknownExpiryTests.cs:13`, `AStatedResetParksTheSlotTests.cs:27`,
`ATakenPortTests.cs:25` (already `CreateTempSubdirectory`), `EachServerLineSaysWhatItRanWithTests.cs:32`,
`EveryServerLaunchIsWrittenTests.cs:26`, `GaugeCaptureTests.cs:30`, `GaugeReaderCrashHarnessTests.cs:36` (`_root`),
`HowAJobEndedIsSaidTests.cs:32`, `JobPumpTests.cs:36`, `NoStartWithoutBudgetTests.cs:34`, `ReaderBarrierTests.cs:23`,
`ReviewEndpointTests.cs:501`, `SlotRegistryTests.cs:13`, `TheCanaryFallbackRunsTests.cs:52`, `TheCanaryIsACanaryTests.cs:21`,
`TheImageChecksEachInstallerTests.cs:30`, `TheImageInstallsAgyFromItsManifestTests.cs:34`,
`TheImageSmokeUsesTheServerTests.cs:26` (EXACT edit, plan gate: its inventory row at line 104 must not move — change line 26 ONLY, to `private readonly string _dir = CoaiMcp.Tests.TempDir.For("coai-smoke-").Path;`; the constructor at :28 and the `Dispose` at :32-40 with its `Directory.Delete` stay exactly as they are, so not one line above 104 is added or removed), `TheServerRecordsTheRoundTests.cs:36`, `TheWholeUsageTravelsTests.cs:27`,
`UsageTests.cs:281`, `UsageTests.cs:417`, `VendorCatalogTests.cs:14-15` (the expression spans two lines: keep two lines),
and the fixture property `TheCallerCanStillWaitTests.cs:150` (`Dir { get; } = …`, removed by its class's `Dispose`).

**A2 — the same field, but the path must NOT exist when the code receives it** (no `CreateDirectory` before the code under
test runs, and the code or an assertion depends on its absence — e.g. it is created by the code, or a test asserts it is
missing): → `private readonly TempDir _scratch = TempDir.For("<prefix>");` and `private string _dir => _scratch.At("<what>");`
(or a readonly field initialised in the constructor) — the leaf stays absent until the code makes it, the parent is owned
and removed. Decide A1 or A2 per site by READING how `_dir` is first used; when in doubt, A2 (it keeps today's state
exactly). Name the choice per site in the report.

**B — a local the test creates and removes in a `finally`** (`ADocumentRoleRunsHereTests.cs:100`,
`ReviewLauncherTests.cs:245`, `RoundsPerPercentServedTests.cs:42`, `UsageLedgerFixtureTests.cs:121`, `:155`): →
`using var <name> = TempDir.For("<prefix>");` (the `finally`'s delete goes; anything else in the `finally` stays) — unless a
server, a process or a task that uses the folder outlives the method, then `var … = TempDir.For(...)` without `using` and
the existing delete kept. A2's rule applies to a local the same way.

**C — a local with no remover today** (`JsonFileStoreTests.cs:35`, `:50`, `:83`; `SessionStoreConcurrencyTests.cs:32`;
`SessionTests.cs:146`, `:163`, `:190`, `:207`, `:222`, `:243`, `:271`, `:292`, `:314`, `:331`): → `using var … =
TempDir.For("<prefix>")` where every user of the folder (a store, a `TeamServer`, a session object) is disposed or done
before the method ends — a `using var` declared FIRST is disposed LAST; otherwise without `using`. Where the original wraps
the call (`Path.Combine(Directory.CreateTempSubdirectory(p).FullName, "record.json")`), split it: the `TempDir` first, then
`.At("record.json")`.

**D — the shared server parent `coai-server-tests/<guid>`** (`TeamServer.cs:34` `DataDir`, `PeopleTests.cs:175`, `:223`,
`PeopleAndUsageAgreeTests.cs:113`, `SessionTests.cs:113`): → `TempDir.For("coai-server-tests-")`, with shape B or C's
`using` rule for locals; `TeamServer.DataDir` keeps its existing remover with `TempDir.For("coai-server-tests-").Path`.

**E — a helper that RETURNS a new folder** (`PeopleTests.cs:299` `FreshDir()`, `StartupGuardTests.cs:33` the default of a
`dataDir` parameter, `StartupGuardTests.cs:156` a file path inside a fresh folder): → `TempDir.For("<prefix>").Path` (and
`.At("a-file")` for `:156`, still a path), no `using`.

**F — a path that is only named, never created by the test** (`JsonFileStoreTests.cs:63` `coai-store-never/absent.json`;
`ReviewLauncherTests.cs:187` `coai-slot-a`, `:302` `coai-slot-agy`, slot folders passed to `SlotEnvironment.For` and only
read): → `Path.Combine(TempDir.Root, <the same name>)`.

**G — the proof** (`RunFolderTests.cs:67`): → `TempDir.Root.TrimEnd(…)`, as in #977 and #980.

**H — `TheImageChecksEachInstallerTests.cs:64`**: a regex over `src_server/Dockerfile` that spells the IMAGE path
`/tmp/install-agy.sh` (a path inside the container image, not this machine's temp). → derive it from the Dockerfile's own
line: `var copiedTo = Regex.Match(dockerfile, @"COPY src_server/install-agy\.sh (\S+)").Groups[1].Value;`,
`copiedTo.Should().NotBeEmpty("the image copies the installer it runs");` and the `MatchRegex` uses
`$@"RUN sh {Regex.Escape(copiedTo)}\s*\\?\s*&&\s*""\$HOME/\.local/bin/agy"" --version"` with the same because-text. This
is a stronger assertion (the RUN runs what the COPY placed) and spells no temp path.

**I — `TheTestSweepLeavesAJobAloneTests.cs:26`**: a regex that SCANS this program's source for
`CreateTempSubdirectory("coai-…")` calls — the API name is data, not a call. → the repository's precedent for a line that
must spell a temp API as data (`src_mcp/tests/Leftovers/LeftoversBaselineTests.cs:256-260`, `PlantedLines`: the name is
written with a `~` that is removed at run time): `new Regex(@"Create~TempSubdirectory\(\s*""(coai-[a-z-]+)""\s*\)".Replace("~",
string.Empty, StringComparison.Ordinal))`, with a one-line comment saying it is a scan pattern, not a call, and pointing at
`PlantedLines`. The test's own positive check (`made.Should().Contain("coai-server-job-")`) keeps the pattern honest.

Total 57: count the sites you assign against the baseline's 57 `src_server/` entries before editing; stop if they differ.

## 5. Steps — RED, GREEN, RED again

Lowered, one at a time: builds `nice -n 19 dotnet build <csproj> -c Debug -m:4 --disable-build-servers`; executables
through `node .agents/conventions/tools/low-priority.mjs`. Never `dotnet test`.

0. **Before.** Build `src_server/tests` and `src_mcp/tests`. Run the server suite; record total/passed/failed/skipped (and
   which tests fail, if any, with the reason). The C# guard class green.
1. **RED.** Delete only the 57 `src_server/` entries; change no site. The guard class fails with exactly 57 `raw-temp`
   findings under `src_server/tests/`.
2. **GREEN.** Rewrite the sites. Rebuild. The guard class green; the server suite's counts equal step 0's (the same failing
   tests, if any, by name); Node guard clean; `--baseline-shrinks` only shrinks; `plan-lifecycle` clean;
   `testLeftovers.test.mjs` green.
3. **RED again.** (a) Put one shape-C line back → exactly one finding; restore. (b) In shape H, point the regex at a wrong
   `COPY` source (`install-agy-x\.sh`) → that test fails on `NotBeEmpty`; restore.
4. **Docs.** `research/module_tests.md`, the *S7, the drain* paragraph: the Team server suite fourth (57 → 0; 734 → 677),
   and one sentence each on shapes A2 (an absent leaf under an owned parent), H (derived from the Dockerfile) and I (a scan
   pattern written as data, as `PlantedLines` does). The E5 status line's S7 sentence: the fourth PR. This brief's status
   and its `todo/README.md` row.

## 6. What this PR does not prove

- The other two scopes (677 entries: `src_vs_code/src/test/`, `src_mcp/tests/`) are untouched.
- The server suite's Windows and macOS legs run as today; nothing here adds one.

## 7. Definition of Done

- [ ] Step 1's 57 findings and step 3's two REDs are in the PR description; step 2's counts equal step 0's.
- [ ] `raw-temp-baseline.json`: 677 entries, only `src_server/` lines removed.
- [ ] The inventory unchanged and `testLeftovers.test.mjs` green; no new marker.
- [ ] A per-site table (site → shape, `using` or not, A1 or A2) is in the PR description.
- [ ] `research/module_tests.md`, the E5 status line and `todo/README.md` updated in the same commit as the code.
- [ ] The coai code round ran over the commit and every finding was resolved before the PR.
