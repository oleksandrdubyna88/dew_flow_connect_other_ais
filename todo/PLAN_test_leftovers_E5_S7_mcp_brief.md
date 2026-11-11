# Build brief — coai E5 story S7, the sixth and last drain PR: the coai-mcp suite's `raw-temp` entries to zero, and S7-d — the baseline file deleted

> Status: **plan only, 2026-11-12, by Opus.** Scope: the 458 `raw-temp` baseline entries of `src_mcp/tests/` (253 files) —
> the last suite — and S7-d: the baseline file deleted and `--baseline` dropped from every invocation, in the same commit.
> Tests and their wiring only. Sonnet coders build from this brief alone.

The plan is [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S7 — The drain*; the earlier drain PRs are #977, #979,
#980, #981 and #984 (briefs `PLAN_test_leftovers_E5_S7_{bench,scripts,bugs,server,extension}_brief.md`). The parent is
`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`, § *Epoch 8*, rows S7-b to S7-e. Measured at `origin/main`
`9a429757`, conventions pin `d23a2ea`.

**One PR, as the plan says (one per suite).** The gate's standing order on this work is not to split a piece of a split
again; this suite is large (253 files) but its edits are mechanical, so it is built as ONE unit by several coders on
disjoint file sets plus one integration pass, and reviewed as one diff. The summary says how large it was.

## 1. Goal

The coai-mcp suite drains (458 → 0) — and because that leaves the file with no entries, **this PR is S7-d too**: the baseline
rule says *"A file at zero entries is deleted with its last entry; a repository with none ships none"*
(`common/test-leftovers-baseline.md`, *The ratchet*), and the parent's row S7-d says the last drain PR deletes the file AND
drops `--baseline` from every invocation, in one commit (§ 4, shape Z). Shapes, read from each entry's hashed line
at `9a429757`: 270 `Directory.CreateTempSubdirectory(…)`, 142 `Path.GetTempPath()`, 1 temp variable, 45 others — almost
all a `/tmp/…` or `C:/tmp/…` path written as DATA (a string the code under test never touches on disk). The authoritative
site list is step 1's RED output (the C# guard names every real file:line).

**Behaviour-preserving swaps.** This suite already uses `TempDir` 365 times and clears SQLite pools in 66 files, so every
site keeps its CURRENT remover and lifetime; only the call that names or makes the temp path changes, to the helper door.

## 2. What must be true when it is done

1. `raw-temp-baseline.json` is DELETED (with its 458 last entries), and no repository-level invocation asks for a baseline
   any more (§ 4, shape Z). The proof files keep their markers and child runs: the run-folder gate stays.
2. The C# guard class (`LeftoversRepositoryGuardTests`) green with NO baseline applied — so it is now red on ANY raw temp
   call outside the helpers (E5's DoD); the Node guard WITHOUT `--baseline` clean; `--baseline-shrinks --base origin/main`
   passes (`absent now — drained`, exit 0, as the parent's S7-d row measured); no new `testsToDelete-exempt` marker; `shared/leftovers-exemptions.json` unchanged — it is keyed by
   LINE: list its rows under `src_mcp/tests/` at step 0, and every file holding one keeps its line count above each row.
3. Every test keeps its name and assertions, except the three of shape Z; the coai-mcp suite's counts are step 0's minus
   the two retired tests of shape Z (record the failing set at step 0,
   if any, by name; the same set after).
4. No raw temp call moves into a file the guard does not read.

## 3. Constraints

- No change to `TempDir`, `RunTemp`, `Owned`, the gate, the proof markers or their child runs, or product code.
- Every site keeps its remover (`Directory.Delete`, `DeleteEvenIfReadOnly`, `SqliteConnection.ClearAllPools()` + delete,
  a `TempDir`'s `Dispose`, or none) exactly as today. Prefixes keep their text.
- **The line-keyed inventory, measured at `9a429757`: 24 rows in 16 files** — `ACallerTranscriptFixtureCarriesNoTextTests.cs` (713), `AReviewTreeIsGivenBackTests.cs` (271, 317), `AReviewTreeTests.cs` (222, 264), `AReviewTreeWithALongPathTests.cs` (133), `AVibeDiskRowExpandsAParentFolderTests.cs` (432), `AVibeDiskRowReadsAParentFolderThroughTheServiceTests.cs` (60), `AVibeDiskRowSaysWhatItSkippedTests.cs` (117), `BackfillAuthorsBackupTests.cs` (149), `CadenceStoreTests.cs` (163), `FilesystemInvariantTests.cs` (191, 222, 419), `RepositoryFactsTests.cs` (81, 118), `TreeGitTests.cs` (44, 51), `WorktreeManagerTests.cs` (119, 261, 430, 456, 500, 532). In THESE files no line is added or
  removed above a row: the type is written fully qualified (`CoaiMcp.Tests.TempDir`), never a new `using` line, and a swap
  stays on its own line(s) (plan gate, finding 0). In every other file a `using CoaiMcp.Tests;` may be added.
- **No Python** (the owner's rule). Edit with the Edit tool or a Node script; never `dotnet test` (run the MTP executable).
- Coders on the edit pass do not build; only the integration pass builds and runs (one build at a time, lowered).

## 4. The shapes

**C — `Directory.CreateTempSubdirectory(<prefix>)`.** The helper makes the same kind of folder (a fresh, exclusively
created folder under the run folder, named for the prefix):
- followed by `.FullName` → `TempDir.For(<prefix>).Path`;
- used as a `DirectoryInfo` (`.Name`, `.Delete(…)`, `.EnumerateFiles(…)`, passed where a `DirectoryInfo` is taken) →
  `new DirectoryInfo(TempDir.For(<prefix>).Path)`, so the type and every member use stay exactly as they are.

`TempDir.For` returns a disposable whose `Dispose` deletes; here it is NOT disposed — the site's existing remover stays the
only one (`TempDir` has no finalizer and registers nothing: `src_mcp/tests_leftovers/TempDir.cs`).

**G — `Path.GetTempPath()`** (inside `Path.Combine(…)`, as an argument, or compared with) → `TempDir.Root`
(`Owned.TempRoot()`, the one helper door, which returns `Path.GetTempPath()` unchanged — `Owned.cs:117-121`). The path is
the same string; nothing else on the line changes.

**V — the one temp-variable site** and **P — planted fixture lines** (`Leftovers/TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests.cs`:
lines that BUILD the spelling of a temp API or variable for a guard to find, already split with `+`, which the C# guard
joins back): → the `PlantedLines` precedent of `Leftovers/LeftoversBaselineTests.cs:256-260` — the spelling written whole
with a `~` inside and `.Replace("~", string.Empty, StringComparison.Ordinal)` at run time, one comment per block.

**D — a `/tmp/…` or `C:/tmp/…` path written as DATA:**
- *neutral data* — the test is not about temp (a fake worktree `"/tmp/wt"` handed to an argument builder, a fake checkout,
  a denied-action fixture `"C:/tmp/coai-check-1/…"`): replace the `/tmp` segment with a neutral fake root that names no temp
  folder — `"/fake/…"` on POSIX shapes, `"C:/fake/…"` on Windows shapes — CONSISTENTLY within the test (every literal and
  every expected value that carries it), so what the test asserts is unchanged;
- *temp-semantic data* — the test is ABOUT how a temp root is read or written (`RunTempHardeningTests` theory data that
  trims `"/tmp/"`, `TheTestSweepIsRetiredTests`' sentence over `"/tmp"`, any case-sensitivity check the test names as a
  property of `/tmp`): keep the text and use the `~` precedent, with a comment saying it is data.
- If a D site touches the disk at that path, it is NOT data: treat it as shape G, or stop and report.

**Z — S7-d: the file goes, and nothing asks for it** (the integration coder, in step 2b):
- delete `raw-temp-baseline.json` (`git rm`);
- `src_vs_code/src/test/testLeftovers.test.mjs:63` (N1, the shipped guard over THIS tree): drop `'--baseline'` from its arguments;
  the other N tests build private repositories with their own baseline files to test the tool, and stay;
- `src_mcp/tests/Leftovers/LeftoversRepositoryGuardTests.cs:37`: stop applying a baseline — the guard's own findings are the
  result (today `LeftoversBaseline.Apply` already passes every hit through when the file is absent, but S7-d drops the call);
- `src_mcp/tests/Leftovers/LeftoversBaselineTests.cs:212` `The_baseline_file_exists_and_is_a_regular_file` becomes
  `The_drained_baseline_file_stays_gone`: no `raw-temp-baseline.json` at the root (*a repository with none ships none*); the
  ratchet already refuses a re-creation, this pins it in the suite. The private-repository tests of that class stay;
- `src_mcp/tests/Leftovers/LeftoversCoaiLayoutTests.cs:128` `TheBaselineIsFound_FromBothPlaces` and its child
  `src_mcp/tests/RunTempChildren.cs:150-160` `BaselineLocationChild` RETIRE: they prove the file is found from two working
  folders, and there is no file. Say so in the PR;
- `.github/workflows/ci.yml`'s step *The raw-temp baseline only shrinks* STAYS (it answers `absent now — drained` and refuses
  a re-creation); its comment says the file was drained and deleted by S7-d;
- `research/module_tests.md`: the guard is now red on ANY raw temp call outside the helpers (E5's DoD), and the wiring
  paragraphs that describe the baseline say it is drained and deleted.

If a site fits no shape, or a swap would change what a test asserts, stop and report it.

## 5. Steps and who does them

**Integration coder, first (step 0 and step 1):**
0. **Before.** Build `src_mcp/tests` (`nice -n 19 dotnet build src_mcp/tests/<its .csproj> -c Debug -m:4
   --disable-build-servers`). Run the whole suite once, lowered
   (`node .agents/conventions/tools/low-priority.mjs src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests`); record
   total/passed/failed/skipped and the failing set by name. The guard class green. List the inventory rows under
   `src_mcp/tests/`.
1. **RED.** Remove every `src_mcp/` entry from `raw-temp-baseline.json` (leaving `"entries": []` for this step only); change no site. Run the
   guard class: it fails with exactly 458 `raw-temp` findings under `src_mcp/tests/`. Save them as a work list (file:line,
   one per line) to the scratchpad, sorted by file, and split it into THREE lists by file (no file in two lists).

**Three edit coders, in parallel (step 2a):** each takes one list and rewrites every site in its files by § 4 — Edit tool
or Node only, no build. Each reports per site: shape, and for D whether neutral or temp-semantic; and any stop.

**Integration coder, last (steps 2b–4):**
2b. **GREEN.** Apply shape Z. Check that no repository-level invocation still asks for the baseline: `git grep -n -E
   "leftovers-check[^\n]*--baseline([^-]|$)|LeftoversBaseline\.Apply\(report, root, root\)|raw-temp-baseline\.json"` over
   everything but `todo/`, `research/` and the private-repository tests that write their own baseline file — list what
   remains and why each is allowed (plan gate, finding 1). Build; fix only compile errors the swaps caused (a missing `using CoaiMcp.Tests;` — except where an inventory
   row forbids a new line, then fully qualify — or `.Path` vs `DirectoryInfo`). The guard class green; the whole suite's
   counts equal step 0's minus the two retired tests (the same failing set); the Node guard WITHOUT `--baseline` clean; `--baseline-shrinks --base origin/main`
   only shrinks; `plan-lifecycle` clean; `cd src_vs_code && node ../.agents/conventions/tools/low-priority.mjs node
   scripts/run-tests.mjs src/test/testLeftovers.test.mjs` green.
3. **RED again.** (a) Put one shape-C line back → exactly one finding (with no baseline, nothing can carry it); restore.
   (b) In one shape-D neutral test, put the original `/tmp` literal back → exactly one finding; restore. (c) Re-create an
   empty `raw-temp-baseline.json` → `The_drained_baseline_file_stays_gone` fails; remove it. (d) Put one shape-P planted
   line back in its old `+`-split form → the guard class has exactly one finding there (the C# guard joins the pieces);
   restore (plan gate, finding 2).
4. **Docs.** `research/module_tests.md`, the *S7, the drain* paragraph: the coai-mcp suite sixth and last (458 → 0) and S7-d
   (the file deleted with its last entries; `--baseline` dropped; the shrink step kept), the C/G/D/V/P shapes in one sentence each. The E5 status line: S7 DONE with this PR (all six suites
   drained, S7-d done); the DoD box `S7: the baseline drained to zero and deleted.` ticked. This brief's status and its `todo/README.md` row.

## 6. What this PR does not prove

- Windows and macOS: the coai-mcp suite runs on the Windows and macOS CI legs today; they are this PR's evidence there.

## 7. Definition of Done

- [ ] Step 1's 458 findings (count, per-file totals), step 3's two REDs and the per-shape counts are in the PR description;
      step 2b's counts equal step 0's.
- [ ] `raw-temp-baseline.json` deleted; no repository-level `--baseline`; the two shape-Z retirements named in the PR.
- [ ] No new marker; the inventory unchanged; `testLeftovers.test.mjs` green.
- [ ] Every D site is listed as neutral or temp-semantic with its replacement.
- [ ] Docs in the same commit as the code; the coai code round ran and every finding was resolved before the PR.
