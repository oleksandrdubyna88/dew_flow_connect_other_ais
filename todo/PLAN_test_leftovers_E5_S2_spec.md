# PLAN — E5.S2 spec: the test sweep, in two halves (CORNERSTONE) — stage A

> Status: **plan only, nothing implemented yet, 2026-11-02 — cornerstone spec by Fable, written before its code; revised the
> same day with the five accepted findings of the S2-A plan gate (coai session f478a03b, verdict proceed), each marked
> `[S2-A gate finding n]` where it lands, and with the decision of P4 (A.9). This file holds the S2-A half (stage A: the age-only `coai-` test sweep is retired); the S2-B half (the owner-aware sweep)
> is added to this same file before S2-B is coded, after S2-A has merged.** Story S2 of
> [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S2-A* (D4, D7, must-not-copy #13; plan-gate rounds 1 and 2
> of 2026-11-01). Its own file because the stories document is sent whole to a review gate and must stay short; it
> promotes with E5's. Where this spec and E5 differ, this spec wins and the difference is a defect of E5 (E5 § *Stages
> and order*).
>
> Scope: `shared/temp-sweep.json`; `src_vs_code/scripts/sweepTemp.mjs` (deleted), new `src_vs_code/scripts/startOfRun.mjs`,
> `run-tests.mjs` (`sweepFirst`); `src_mcp/tests/TempDirsAreSwept.cs` (the constructor), `SweepRule.cs`, new
> `src_mcp/tests/StartOfRun.cs`, new `src_mcp/tests/PruneOldScratchDirsCharacterizationTests.cs`, new
> `src_mcp/tests/TheTestSweepIsRetiredTests.cs`; `src_vs_code/src/test/sweepTemp.test.mjs` (deleted), new
> `src_vs_code/src/test/startOfRun.test.mjs`; new `scripts/old-coai-leftovers.mjs`, `scripts/lib/old-coai-leftovers.mjs`,
> `scripts/lib/old-coai-leftovers.test.mjs`; `research/module_tests.md`. **Nothing under `src_mcp/src`, `src_mcp/runners`,
> `src_mcp/core`, `src_server`, `src_bugs` or `src_bench` changes.** One PR, merges alone, before S5's PRs and before the
> producer PR.

## A.0 — Facts this spec rests on (read on branch `docs/test-leftovers-e5-stories` at `e3063eef`, 2026-11-02)

| # | Fact | Where |
|---|---|---|
| F1 | The Node sweep: `leftover` (directory ∧ `startsWith(rule.prefix)` ∧ not `^coai-[0-9]` ∧ no `neverSwept` prefix ∧ last write older than `keepMinutes`), `toSweep`, `entriesOf` (stats only prefixed names), `sweep(dir, now, rule, io = fs)` → `{ removed, failed }` | `src_vs_code/scripts/sweepTemp.mjs:25-27`, `:30-37`, `:44-46`, `:53-63`, `:79-86` |
| F2 | `sweepFirst()` calls `sweep(tmpdir(), Date.now(), readRule('..'))`, prints one line, never throws; it is the first statement of `main()` | `src_vs_code/scripts/run-tests.mjs:103-111`, `:241-242` |
| F3 | The C# fixture's constructor calls `PanelService.PruneOldScratchDirs(RunTemp.Real, DateTime.UtcNow - Rule.Keeps, [$"{Rule.Prefix}*"], Rule.NeverSwept)` on EVERY construction (the class is a test class too: one construction per Fact) | `src_mcp/tests/TempDirsAreSwept.cs:76-85` |
| F4 | `SweepRule(string Prefix, TimeSpan Keeps, IReadOnlyList<string> NeverSwept)`; `Shared()` reads `prefix`, `keepMinutes`, `neverSwept` with `GetProperty` (a missing key throws `KeyNotFoundException`, inside the static initializer of `Rule` at `TempDirsAreSwept.cs:64`) | `src_mcp/tests/SweepRule.cs:13-25` |
| F5 | `shared/temp-sweep.json`: `why` (`:2-21`), `prefix` `"coai-"` (`:22`), `keepMinutes` `10` (`:23`), `neverSwept` eight prefixes (`:24-33`) | `shared/temp-sweep.json` |
| F6 | `PanelService.PruneOldScratchDirs(tempRoot, cutoff, prefixes = null, except = null, warn = null)`: `prefixes ?? ScratchPrefixes` (the four product prefixes `:1389-1390`), `EnumerateDirectories(tempRoot, p)`, `DirectoryNotFoundException` swallowed; `SweepOne` catches `IOException`/`UnauthorizedAccessException`; `DeleteIfDue` returns when `GetLastWriteTimeUtc(dir) >= cutoff` or `Kept(dir, except)`; `Kept` = `VersionCache` (`char.IsDigit(name[5])`) or an `except` prefix; a `coai-question-` folder goes through `FolderLock.RemoveIfFree`; the throttle (`_sweptUtc`, `ForgetTheLastSweep`) is NOT inside `PruneOldScratchDirs` (the tests at `TempDirsAreSwept.cs:144-158`, `:160-178` call it repeatedly without the forget and assert removal) | `src_mcp/src/Server/PanelService.cs:1389-1390`, `:1395-1401`, `:1475-1489`, `:1491-1507`, `:1514-1532` |
| F7 | The three PRODUCT-prefix scans read only `neverSwept` and hard-code `coai-` in their regex | `src_mcp/tests/TempDirsAreSwept.cs:104-116`, `src_vs_code/src/test/sweepTemp.test.mjs:38-52`, `src_server/tests/TheTestSweepLeavesAJobAloneTests.cs:24-38` |
| F8 | The E2 run folder is `testsToDelete-coai-run-<6 hex>` with a sibling claim FILE `<folder>.owner` (C# `RunTemp.cs:85`, `RunTempFolder.cs:72`; Node `run-tests.mjs:158`, removed `:190`); its text is `<host>@<os>:<pid>[:<start>]` with `<os>` in Node's spelling | `src_mcp/tests/RunTemp.cs:80-85`, `src_vs_code/scripts/run-tests.mjs:117-122`, `:158` |
| F9 | The Node reference exports `isTakenRename(error, thereAtTarget, platform)`, `readOwner`, `legacyOwnerState`, `processProbe`, `isAskablePid`, `tempRoot`; its `identityOf` (lstat, bigint `dev`+`ino`+`directory`), `quarantineName` and `moveExclusively` (POSIX: exclusive `mkdir` reserve → identity pre-check → `rename`; Windows: `renameSync`, `isTakenRename`) are NOT exported | `.agents/conventions/tools/lib/leftovers-sweep.mjs:126-132`, `:159-161`, `:173-176`, `:207-233`; `tools/lib/owned.mjs` exports (`:332`, `:344`, `:542`, `:580`) |
| F10 | `sweeper-scan.mjs --root .` today: **2 owner-blind line(s)** — `scripts/lib/transcript-sanitise.test.mjs:686` (an `includes('testsToDelete')` assertion) and `src_vs_code/src/test/runTemp.test.mjs:149` (a `startsWith('testsToDelete-')` filter in a test) — both test-file literals of S5's class, neither a sweeper; the age-only `coai-` sweep is invisible to the scan (E5 § *Facts*, R6) | run 2026-11-02 in this worktree |
| F11 | `scripts/lib/*.test.mjs` run bare under `node --test "scripts/**/*.test.mjs"` in CI (the glob covers `scripts/*.test.mjs` too) | `.github/workflows/ci.yml:196-197` |
| F12 | `TempDir.For(prefix)` makes a folder inside the run folder (E2): a name inside it carries no prefix (rule §1) | `src_mcp/tests/TempDir.cs:31` |

## A.1 — The six decisions

### 1. The shape of `shared/temp-sweep.json`: `neverSwept` stays byte-for-byte; `prefix` and `keepMinutes` GO; `why` is rewritten

- `neverSwept` keeps its key, its order and its eight values. It is what the three product-prefix scans (F7) hold
  complete, what the start-of-run COUNT (decision 2) leaves out, and what the one-off clean-up (decision 5) refuses.
- `prefix` and `keepMinutes` are deleted (E5 Q7's recommendation, which the plan gate carried to PROCEED). After this
  PR nothing selects by them: the count's `coai-` and the one-off clean-up's `coai-` are constants of a program that
  never deletes by them (decision 2), and a constant that selects nothing for removal is not the drift the file was
  written to prevent. A key left in the file is read as a decision by the next reader; `keepMinutes: 10` beside a
  sweep that no longer exists would read as "something still deletes after ten minutes".
- `why` is rewritten in full: the file DOCUMENTS the product's own temp directories under the `coai-` prefix; no test
  sweep deletes by this file (retired by E5 S2-A, this PR's number, date); the list is held complete by the three
  scans (name them); the one-off clean-up `scripts/old-coai-leftovers.mjs` refuses every name here; the run folder
  (`testsToDelete-coai-run-<6>`) is what a test run makes and removes itself. Keep the sentence about `coai-<version>`
  being a cache. Drop every sentence about ten minutes, "older than", and `research/PLAN_the_tests_take_their_temp_directories_with_them.md`'s
  sweep as a live thing (cite it as history: "the sweep it describes was retired by …").
- Readers after the change: C# `SweepRule(IReadOnlyList<string> NeverSwept)` with `Shared()` reading only `neverSwept`;
  Node `readRule(repoRoot)` unchanged in code (it returns the parsed object; callers read `.neverSwept` only).
- **An old reader fed the new file fails SAFE, and this is observed once and recorded in the PR, not tested** (no
  "old-branch" test is kept): (a) Node — `git show origin/main:src_vs_code/scripts/sweepTemp.mjs > <scratch>/old.mjs`,
  then `node -e` calling its `sweep(<private root with an 11-minute-old coai-panel-x>, Date.now(), <the new rule>)`
  prints `{ removed: 0, failed: 0 }` and the folder stays (`'x'.startsWith(undefined)` is `startsWith('undefined')`,
  so `entriesOf` matches nothing); (b) C# — the old `SweepRule.Shared()` throws `KeyNotFoundException` on `prefix`,
  which surfaces as a `TypeInitializationException` of the fixture: every test of that old checkout FAILS and nothing
  is deleted. Both are the safe side; the PR description quotes the two observations. An old checkout keeps its OWN
  copy of the file, so its age-only sweep stays enabled there — the residual risk D4 records; the PR restates it.

### 2. The sweeps stop deleting: both runners COUNT and REPORT; neither removes anything

**Why a count and not nothing.** The operator's ruling of 2026-09-18 was about a pile that cost `BuildWork` four
minutes (`PanelService.cs:1403-1409`). Under E2 everything a `src_mcp` or extension run makes is inside its run folder,
so the pile after this PR grows only from the three suites without a run folder (S3 gives them one) and from the
product's own `coai-*` the product sweeps on its own clock. Small — but a retired sweep that says nothing hides the
day it is not small. One line per run, with the number, is what tells a person to run the one-off clean-up, and it is
what the stage-A record quotes. A count reads NAMES ONLY (`readdir` with file types, no `stat`, no age): the ten-minute
distinction was the sweep's, not the person's — the clean-up listing (decision 5) shows ages.

**Node.** New module `src_vs_code/scripts/startOfRun.mjs`:

```js
/** What earlier runs and the product left under `coai-` in `dir`, by name alone: `{ leftovers }`. Removes nothing — ever (E5 S2-A). */
export function startOfRun({ dir, rule, io = fs }) {
  const entries = io.readdirSync(dir, { withFileTypes: true });
  const leftovers = entries.filter((e) => e.isDirectory() && isOldTestName(e.name, rule.neverSwept)).length;
  return { leftovers };
}
```

`isOldTestName(name, neverSwept)` = `name.startsWith('coai-')` ∧ `!/^coai-[0-9]/.test(name)` ∧ no `neverSwept` prefix.

**[S2-A gate finding 1] The legacy run folders are counted too, on their own line.** The folder an E2 run makes and removes
itself is `testsToDelete-coai-run-<6 hex>` (F8) — a LEGACY name of the rule (§1: its second segment is not an owner), and
a KILLED run leaves one plus its `.owner` file. Nothing sweeps them after this PR, and `coai-` does not match them, so a
count that only reads `coai-` would hide the other half of the pile. `startOfRun` therefore returns
`{ leftovers, legacyRuns }`, `legacyRuns` being the directories whose name matches `/^testsToDelete-coai-run-[0-9a-f]{6}$/`
(the shape the one-off clean-up lists as `legacy-run`, decision 5); still names only, no `stat`, no age, no claim read. A
count of names cannot tell a dead run's folder from a LIVE run's (another session's run in the same temp root), so the line
says "of other runs, some may be live" and sends the person to the clean-up's listing, which reads the claim. `reportLeftoversFirst`
prints a SECOND line after the first, only when `legacyRuns > 0`:
`run-tests: <n> legacy testsToDelete-coai-run-* director(y|ies) in <tmpdir()> — a killed run leaves one; some may belong to a run still going; the one-off clean-up lists them with their claim (E5 S2-A); see research/module_tests.md § The one-off clean-up`.
The module still imports `node:fs` and `node:path` only and has no remover (N6 holds unchanged).
**[S2-A code 0]** The Node runner captures the REAL temp root once, when `run-tests.mjs` loads (`REAL_TEMP_ROOT` — `beginRun` later moves `TEMP`/`TMP`/`TMPDIR` into the run folder, after which `os.tmpdir()` answers that folder), reads and names THAT root in the count, makes the run folder in it, and `startOfRun` takes an `ownFolder` to leave out, as C# passes `RunTemp.Real` and `RunTemp.Folder`; tests N9 and N10.
The module imports `node:fs` and `node:path` only; it has no `rm`, `rmSync`, `rmdir`, `unlink`, `rename` or `exec` of
any kind (a structural assertion, A.3 N6). `run-tests.mjs`: `sweepFirst()` becomes `reportLeftoversFirst()`, still the
first statement of `main()`, still inside `try/catch`, printing
`run-tests: <n> old coai-* director(y|ies) in <tmpdir()> — no test sweep removes them (E5 S2-A); see research/module_tests.md § The one-off clean-up`
(`run-tests: no old coai-* leftovers in <tmpdir()>` when `n === 0`); a throw prints
`run-tests: the leftover count did not run (<message>); the tests still will`. `sweepTemp.mjs` is DELETED (its `sweep`,
`toSweep`, `entriesOf`, `removed`, `leftover`, `versionCache`): nothing else imports it (verify with
`grep -rn sweepTemp src_vs_code --include=*.mjs --include=*.ts --include=*.json`).

**C#.** New `src_mcp/tests/StartOfRun.cs`, `internal static class StartOfRun` with
`internal static int Run(string tempRoot, SweepRule rule)`: `Directory.EnumerateDirectories(tempRoot, "coai-*")`
filtered by the same three conditions on `Path.GetFileName`, `DirectoryNotFoundException` → 0. It references nothing
in `CoaiMcp.Server` (A.8 check 3). The `TempDirsAreSwept` constructor replaces the `PruneOldScratchDirs` call (F3) with
ONE count per process behind an `Interlocked.Exchange` flag (the pattern of `_steeringLeftoverChecked`, `:74-81`),
writing the same sentence to `Console.Error`. **[S2-A gate finding 1]** `StartOfRun` has a second entry,
`internal static int RunFolders(string tempRoot, string ownFolder)`: the directories matching
`^testsToDelete-coai-run-[0-9a-f]{6}$` in `tempRoot` EXCEPT `ownFolder` (`RunTemp.Folder` — in C# the run's folder exists in the
real temp root before the fixture's constructor runs, and a count that included it would read 1 on a clean machine; the Node
runner counts before it makes its own, so it excludes nothing). The constructor writes it as a second `Console.Error` line, only when
it is above 0, in the Node line's words. Same rules as `Run`: names only, `DirectoryNotFoundException` → 0, no `CoaiMcp.Server`. The `using CoaiMcp.Server;` at `TempDirsAreSwept.cs:2` stays only while a
remaining test in the file needs it (the product-sweep tests that stay, A.5) — never for the constructor.

**The class keeps its name `TempDirsAreSwept` in this PR**, with its `<summary>` rewritten ("Since E5 S2-A this fixture
sweeps NOTHING: it deletes a killed run's fake-CLI steering file and counts the old `coai-*` leftovers once; S2-B
replaces it with the copied `LeftoversSweepFixture`"). Reason: E5 cites the class by this name in S2-B, the stages
table, note 1 and its commands, and S2-B deletes the class — a rename now is churn paid twice. The Opus reviewer reads
the summary, not the name.

### 3. The invariant and its RED-first scenario, in C# and Node

**The invariant:** every `coai-*` name is outside the test start-of-run, at any age, whatever `neverSwept` holds. It is
tested through the ENTRY POINT each runner calls (`StartOfRun.Run`; `startOfRun`), never through a selector function
— so a removal re-added anywhere on the entry path is red (A.1 § 6). A seam is extracted FIRST (commit 2) so that the
RED tests call the same entry point before and after the change (A.2).

**Fixtures.** The protected names: `coai-answers-x`, `coai-repair-x`, `coai-noworkspace-x`, `coai-plan-x`,
`coai-server-job-x`, `coai-chat-x`, `coai-question-x`, `coai-check-x`, `coai-0.17.5`; the old test name `coai-panel-x`;
the invented `coai-futurething-x`. Each a directory holding one file `sentinel.txt` with distinct bytes
(`$"still {name}"`), last write set 10 days ago (`Directory.SetLastWriteTimeUtc` / `fs.utimesSync`). The rule:
`SweepRule.Shared() with { NeverSwept = [] }`; Node `{ ...readRule(repoRoot), neverSwept: [] }`.

**The RED-first scenario (must-not-copy #13).** A private root; ONE unmarked directory `coai-chat-live` holding
`sentinel.txt` = `"still somebody's chat"`; last write 11 minutes ago; the rule as shared with `coai-chat-` OMITTED
(`NeverSwept = [.. Rule.NeverSwept.Where(p => p != "coai-chat-")]`; Node `neverSwept.filter((p) => p !== 'coai-chat-')`);
the COMPLETE entry point run — C# `StartOfRun.Run(root, rule)` (what the constructor calls, F3's call wrapped by
commit 2); Node `startOfRun({ dir: root, rule })` (what `reportLeftoversFirst` calls). Assert: `coai-chat-live` exists
and `sentinel.txt` reads the same bytes. **RED on commit 3 in both** (the seam still deletes: 11 minutes is older than
ten and `coai-chat-` is not excepted — the consultant's observation E5 #13 records). No positive control: stage A has
no removal to prove (E5 S2-A item 3).

### 4. The characterization test of `PanelService.PruneOldScratchDirs` lands FIRST, on the unchanged function

New `src_mcp/tests/PruneOldScratchDirsCharacterizationTests.cs`, commit 1 of this PR (A.2), green on `main`'s code
before any other S2-A edit; it changes in NO later E5 PR (A.8 check 2). Every case: a private root `TempDir.For("prune-characterization-")`
(F12), the folders made with `Directory.CreateDirectory`, last-write set with `Directory.SetLastWriteTimeUtc`, the
cutoff an explicit `DateTime` argument (never `DateTime.UtcNow` arithmetic on the product's side), the assertion
`Directory.EnumerateDirectories(root).Select(Path.GetFileName).Order().Should().Equal([...])` — the EXACT remaining
set, never "contains". No `[Collection]` (F6: no process static is touched). A characterization test records what the
function DOES: if an observed result differs from the expected column, the test asserts the observed result and the
PR says so in one line — it never corrects the function. The cases are A.4.

### 5. The one-off clean-up of old-prefix test leftovers (D4): list, pick, re-check, EXCLUSIVE rename, remove by identity

A person's tool, run with no coai session and no VS Code window open on the machine. Never wired into a runner, never
a sweep, never the janitor's (D4). Three files:

- `scripts/old-coai-leftovers.mjs` — the CLI. `list [--tmp <dir>] [--out <file>]` prints one line per entry — **FIRST [S2-A code 3]: a `--out` that cannot be written then exits 2 with the listing already shown** — and
  writes the listing JSON (default `./old-coai-leftovers-listing.json`); `remove --listing <file> [--tmp <dir>] <name>...` removes
  the named picks and prints one line per pick: `removed <name>` or `refused <name>: <check>`. Exit 0 when every pick
  was removed, 1 when any was refused, 2 on a usage error (naming the legal forms). `--tmp` defaults to the reference's
  `tempRoot()` (F9). No other flags. **[S2-A gate finding 2] The listing records its temp root.** The JSON is
  `{ "tmp": "<the absolute temp root the listing read>", "entries": [ ... ] }`; `remove` uses `listing.tmp` unless `--tmp` is
  given, and **refuses a `--tmp` that differs** from it (`path.resolve` of both compared) with exit 2 and one line naming both
  paths — a listing taken in one root is evidence about THAT root only, and a pick judged `unchanged` against another
  root's folder of the same name would be a removal on a name read elsewhere. A listing without `tmp`, or with a `tmp` that is
  not an absolute path, is a usage error (exit 2) too.
- `scripts/lib/old-coai-leftovers.mjs` — the logic, pure over an `io` seam (`readdirSync`, `lstatSync`, `statSync`,
  `readFileSync`, `mkdirSync`, `renameSync`, `rmSync`, `rmdirSync`, `unlinkSync`) and `hooks` (`drawName(dir)`,
  `afterReserve`, `beforeRename`, `afterRename`, `probe`, `platform`) and the options `claimPlatform` (the platform a legacy claim is judged as: a Linux reader never says `gone`, rule §3, so a test reaches `gone` through it — [S2-A Opus 7], absent from the first text) and `onResult` (each result handed over as soon as it is decided — [S2-A code 6/9]) so every scenario below runs over a private root.
  - `listOldLeftovers({ tmp, neverSwept, now, io, probe })` → `{ tmp, entries }`, entries `{ name, kind, lastWriteMs, ageMinutes, dev, ino, claim }`
    (`tmp` is the root's absolute path — **[S2-A gate finding 2]**):
    `kind: 'coai'` for a directory `coai-*` that is not `^coai-[0-9]` and starts with no `neverSwept` prefix;
    `kind: 'legacy-run'` for a directory `/^testsToDelete-coai-run-[0-9a-f]{6}$/` with `claim` = the sibling `.owner`
    file's state — `gone`, `live`, `unknown` from the reference's `readClaimText` + `parseLegacyOwner` + `legacyOwnerState`
    (F9; the claim of a legacy name is in the OLD grammar, so `readOwner`, which parses the owner form, is not the
    reader; a `win32`/`darwin` tag parses as `unknown`, E5 #15; and `legacyOwnerState` answers `unknown` for EVERY legacy record
    on a Linux reader, because a legacy record carries no pid namespace — the listing therefore never calls a Linux claim `gone`, and a
    test reaches `gone` through the `platform` seam, as the reference's own tests do), `none` when no claim file, `unreadable`
    otherwise. **[S2-A gate finding 3] `kind: 'quarantine'`** for a directory
    `/^\.(coai-.+|testsToDelete-coai-run-[0-9a-f]{6})\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}$/i` whose original name (group 1) is
    NOT a protected name — an interrupted clean-up of THIS script (killed between its rename and its removal), listed
    with `of: <original name>`, `empty` (it holds nothing: the placeholder of a move killed before its rename) and `claim` as
    for a `legacy-run` when `of` is one. Protected prefixes, the version cache, files, links and every other name are LEFT OUT (not
    listed as refused — they were never candidates). `dev`/`ino` from `lstatSync(…, { bigint: true })` (as the reference's
    `identityOf`), serialised as decimal strings.
  - `removePicked({ tmp, listing, picks, neverSwept, io, hooks, probe })` → `[{ name, outcome: 'removed' | 'refused', check }]`.
    Per pick, in this order, the first failing check refuses with its name and the command goes on to the next pick:
    **`bad-name` FIRST [S2-A Opus 1] (the BLOCKING finding of the Opus review, also code gate 2: a hand-edited entry `coai-x/../../outside/victim`, picked by name, was moved and removed OUTSIDE the temp root)** — a name must be ONE path segment, refused when it is empty, `.`, `..`, holds `/` or `\`, or differs from `path.basename(name)`, before any `path.join`; the quarantine pattern's groups exclude separators; `not-listed` (the name is not in the listing's entries, indexed by name once [S2-A code 7]); `protected` (a `neverSwept` prefix, `^coai-[0-9]`, or any name not
    matching `kind`'s shapes — re-judged NOW, not trusted from the listing; for a `quarantine` the ORIGINAL name is judged);
    `missing` (`lstatSync` fails);
    `not-a-directory` (a link or a file is at the name); **`foreign-uid` [S2-A Opus 4]** (on POSIX the lstat uid is not `process.getuid()` — the kernel owner of rule §3; no check on Windows); `changed` (`dev`, `ino` or the last-write time **in nanoseconds, `mtimeNs`** differ from the listing's — [S2-A Opus 7] the first text said `mtimeMs`; the listing also carries `mtimeNs` and, for a quarantine, `empty`);
    `owner-<state>` (**[S2-A Opus 3]** for a `quarantine`, first the renamer: the pid in its name is probed and a live or unaskable process — or a dead one in a process list that may hide others — refuses it, so a live sweep's quarantine waiting to be restored is never removed; then, for a `legacy-run` or the `quarantine` of one, a claim that is now anything but `gone` or `none`; `probe` injected in tests; a
    `coai` name has no claim and skips this check — the pick plus the unchanged check is the whole authority, D4);
    `quarantine-taken` (**[S2-A gate finding 0]** the exclusive move found every one of its `MOVE_DRAWS = 5` drawn names occupied);
    `moved-but-changed` (**[S2-A gate finding 4]** the quarantined object's `dev`+`ino` OR last-write time differ from the listing's —
    re-read on the quarantined path after the move; the quarantine is then LEFT in place and listed, never removed).
    **[S2-A Opus 5] After the move the claim of a legacy run folder is read again** (as the reference does); a claim that is no longer `gone`/`none` keeps the quarantine and the claim file (`owner-<state>`). **[S2-A code 4]** an `ENOENT` from the rename is the `missing` refusal (Windows and POSIX). Then `rmSync(quarantined, { recursive: true, force: true })`
    and, for a `legacy-run` (or the `quarantine` of one), `unlinkSync(<original name>.owner)` LAST. A pick of kind `quarantine` skips the move
    (it IS the quarantined object — **[S2-A gate finding 3]** `remove` accepts it by its dot-name, under `not-listed`, `protected`, `missing`,
    `not-a-directory`, `changed` and `owner-<state>` exactly as above) and goes straight to the post-check and the removal; the scenario
    is "kill after the rename, a second run finishes it" (A.3 S15).
    The exclusive move is the reference's shape (F9, `moveExclusively`) with the two changes of finding 0: the quarantine name
    `.<name>.teststodelete-quarantine-<pid>-<hex8>` drawn by `hooks.drawName` (default `crypto.randomBytes(4)`); POSIX —
    `mkdirSync(to, { mode: 0o700 })` (EEXIST ⇒ this draw is taken), re-`lstat` the placeholder (ours, a directory, empty)
    immediately before `renameSync(from, to)`; **[S2-A gate finding 0] the placeholder is removed in a `finally` whenever the rename did
    not complete, and only while it is still OUR empty reservation** (a directory, not a link, the reserved `(dev, ino)`, empty;
    `rmdirSync`, never recursive — a name taken over in between is never touched), and **a taken draw draws a new name, up to `MOVE_DRAWS = 5`
    times, before the pick is refused `quarantine-taken`** (the reference's `QUARANTINE_DRAWS`); Windows — `renameSync` and
    `isTakenRename(error, identityOf(to) !== undefined, 'win32')` (imported from the reference), the same bounded draws. Any other error
    from `io` refuses the pick with `error-<code>`; nothing throws out of `removePicked`.
    **[S2-A gate finding 4] The residual window, recorded in the module's header and in `module_tests.md`.** The `changed` check and the
    `rename(2)` are two calls: an object swapped in between is moved aside under the quarantine name instead of the one the person picked.
    The post-move re-check sees it (identity or last-write time differs from the listing) and KEEPS it — nothing is removed — but the swapped-in
    object has been displaced from its name for the time until the person looks at the next `list`; and between the post-move re-check and
    `rmSync` only the same user can swap the dot-named quarantine, which is the threat this tool does not guard against (the reference's own
    accepted window, F9). A directory's last-write time does not move when the directory itself is renamed within one parent (observed on
    the build machine in commit 5; if a platform moves it, the post-check would refuse every pick there and the observation is recorded).
    **P4 — decided:** the reference's `identityOf`/`moveExclusively` are not exported, and stage A merges BEFORE any new conventions
    release, so this module carries its OWN copy of those ~40 lines now, with a comment naming their source
    (`.agents/conventions/tools/lib/leftovers-sweep.mjs`, at pin `105d53c`); **follow-up, recorded in A.7 item 7 and in `module_tests.md`:**
    when the conventions export them, this module switches to the exported function and its copy is deleted (E5 § *Notes for the parent*,
    new note 19). The script imports token-free functions straight from the mount (`.agents/conventions/tools/lib/…`): D3's
    one-module rule is about the `coai` token being written once, and this tool carries no token; the mount missing ⇒
    one line naming `git submodule update --init .agents/conventions`, exit 2.
- `scripts/lib/old-coai-leftovers.test.mjs` — A.3 S1–S10 and S11–S16, run by CI's `node --test "scripts/**/*.test.mjs"` (F11).

`research/module_tests.md` gets the section § *The one-off clean-up of old `coai-*` test leftovers* (A.6).

### 6. Break-its — each observed red, then reverted before the commit; both runs quoted in the PR

| # | Mutation | Red test |
|---|---|---|
| B1 | In `StartOfRun.Run` add `Directory.Delete(dir, true)` for each counted name | A.3 C-I1 and C-R1 |
| B2 | In `startOfRun.mjs` add `io.rmSync(path.join(dir, e.name), { recursive: true })` for each counted name | A.3 N-I1, N-R1 and N6 |
| B3 | `neverSwept` emptied in `shared/temp-sweep.json` | the three product-prefix scans red (F7) — and A.3 C-I1 / N-I1 stay GREEN, which is the point of the invariant |
| B4 | `PanelService.cs:1516` `>=` → `>` | A.4 C5 red; `git diff --exit-code origin/main -- src_mcp/src/Server/PanelService.cs` red |
| B5 | In `removePicked`, replace the exclusive move + identity re-check with `io.rmSync(path.join(tmp, name), { recursive: true, force: true })` | A.3 S5, S6, S8, S11, S12 and S16 red (the checks they pin are the exclusive move, the identity re-check and the post-move re-check; the others pass for a body that removes by name only if they assert nothing about those — each is confirmed at build time and recorded) |

## A.2 — The order of commits (one branch `feat/test-leftovers-e5-s2a`, one PR; every commit green on its own)

| # | Commit | Contains | Observed before the commit |
|---|---|---|---|
| 1 | `test(sweep): characterize PanelService.PruneOldScratchDirs on the unchanged function (E5 S2-A item 4)` | only `src_mcp/tests/PruneOldScratchDirsCharacterizationTests.cs` (A.4) | all seven green on `main`'s `PanelService.cs`; B4 red then restored |
| 2 | `refactor(sweep): the start-of-run entry points, behaviour unchanged (E5 S2-A seam)` | `src_mcp/tests/StartOfRun.cs` with `internal static void Run(string tempRoot, SweepRule rule)` whose body is F3's call verbatim, the constructor calling `StartOfRun.Run(RunTemp.Real, Rule)`; `src_vs_code/scripts/startOfRun.mjs` exporting `startOfRun({ dir, rule, io = fs, now = Date.now() })` whose body is `return sweep(dir, now, rule, io)` from `sweepTemp.mjs`, `run-tests.mjs:105` calling it | the whole C# suite and `npm test` green; the Node sweep still prints its "swept N" line |
| 3 | `test(sweep): RED — every coai-* name is outside the start of a run; an unmarked coai-chat-live survives (E5 #13)` | `src_mcp/tests/TheTestSweepIsRetiredTests.cs` C-I1, C-R1; `src_vs_code/src/test/startOfRun.test.mjs` N-I1, N-R1 (A.3) | **the four observed RED**, each with its failure line quoted in the commit body (`coai-chat-live` missing; `coai-panel-x` and `coai-futurething-x` missing with `neverSwept` empty) |
| 4 | `feat(sweep): the age-only coai- test sweep is retired — both runners count and report, nothing is deleted (E5 S2-A)` | decisions 1 and 2 (`temp-sweep.json`, `SweepRule.cs`, `StartOfRun.cs` → `Run` returns `int` and `RunFolders` counts the legacy run folders [S2-A gate finding 1], `startOfRun.mjs` counts both, `sweepTemp.mjs` and `sweepTemp.test.mjs` deleted, A.5's rewrites and deletions, A.3's remaining tests including C-N4 and N7–N8 each seen RED before its code, the `TempDirsAreSwept` summary) | commit 3's four tests GREEN; B1–B3 red then restored; the old-reader observations of decision 1 |
| 5 | `feat(scripts): old-coai-leftovers — list, pick, re-check, quarantine by identity, remove (E5 D4)` | the three files of decision 5; A.3 S1–S16 written FIRST against a body that does `rmSync` by name, S5/S6/S8/S11/S12/S16 observed red (B5), then the implementation | S1–S16 green on Linux (WSL) and on Windows (the owner's machine or the Windows CI leg's `node --test` — add the step if the leg lacks it, else record "Windows: not run" in the PR) |
| 6 | `docs(tests): module_tests.md — the retired sweep, the invariant, the one-off clean-up, the residual risk (E5 S2-A)` | A.6 | `node .agents/conventions/tools/plan-lifecycle.mjs .` clean |

Commits 3 and 4 are never squashed into each other locally: the PR is squash-merged by the repository's rule, and the
RED commit is what the reviewer and the stage-A record cite. Commit 2's seam is a pure refactor: `git diff` of commit 2
shows F3's call moved, not changed.

## A.3 — Tests: names, fixtures, assertions (RED symptom in the last column)

**C#** — `src_mcp/tests/TheTestSweepIsRetiredTests.cs` (xUnit v3 + FluentAssertions; `[Collection("fakecli-env")]` is
NOT needed: `StartOfRun` touches no process static).

| # | Test | Fixture | Assertion | RED on commit 3 |
|---|---|---|---|---|
| C-I1 | `EveryCoaiName_IsOutsideTheStartOfRun_AtAnyAge_WithNeverSweptEmpty` | A.1 § 3's eleven directories in `TempDir.For("retired-sweep-")`, 10 days old; `Rule with { NeverSwept = [] }` | after `StartOfRun.Run(root, rule)` every one of the eleven exists and its `sentinel.txt` reads its bytes | `coai-panel-x`, `coai-futurething-x` and the eight protected names are gone (`coai-0.17.5` stays by `Kept`) |
| C-R1 | `AnUnmarkedChatFolder_SurvivesTheStartOfRun_WhenItsPrefixIsNotExcepted` | `coai-chat-live/sentinel.txt` 11 minutes old; the shared rule minus `coai-chat-` | the folder and its bytes survive | gone |
| C-N1 | `TheCount_IsTheOldTestNames_AndNothingProtected` (commit 4) | the eleven directories plus a FILE `coai-3.png` and `other-x` | `StartOfRun.Run(root, Rule) == 2` (`coai-panel-x`, `coai-futurething-x`); with `NeverSwept = []` it is 10 (the cache stays out) | — (new) |
| C-N2 | `TheCount_OfAMissingRoot_IsZero` (commit 4) | a path under the private root that does not exist | `== 0`, no throw | — (new) |
| C-N3 | `TheStartOfRun_ReferencesNothingOfTheProduct` (commit 4) | the text of `src_mcp/tests/StartOfRun.cs` | contains neither `PanelService` nor `CoaiMcp.Server`; and contains `EnumerateDirectories` (the scan must see what it knows is there) | — (new) |
| C-N4 | `TheLegacyRunFolders_AreCountedOnTheirOwnLine_ExceptThisRunsOwn` (commit 4, **[S2-A gate finding 1]**) | `testsToDelete-coai-run-abc123`, `testsToDelete-coai-run-def456` (passed as `ownFolder`), `testsToDelete-coai-run-xyz`, `testsToDelete-coai-runabc123`, `testsToDelete-u1000-coai-run-abc123`, `coai-panel-x`, and a FILE `testsToDelete-coai-run-aaaaaa` | `StartOfRun.RunFolders(root, ownFolder) == 1` (`abc123` only); a missing root → 0; `StartOfRun.Run(root, Rule)` on the same root is 1 (`coai-panel-x`) — the two counts never mix | — (new) |
| C-P1 | `EveryTempDirectoryThisProgramMakes_IsNeverTheTestsToSweep` | unchanged, in `TempDirsAreSwept.cs:103-110` | unchanged | — (stays green throughout) |

**Node** — `src_vs_code/src/test/startOfRun.test.mjs` (`node:test`, `node:assert/strict`; the repo root and
`readRule` as `sweepTemp.test.mjs:21-23` had them).

| # | Test | Fixture | Assertion | RED on commit 3 |
|---|---|---|---|---|
| N-I1 | `every coai-* name is outside the start of a run, at any age, with neverSwept empty` | the eleven directories on a REAL private root (`fs.mkdtempSync(path.join(os.tmpdir(), 'retired-sweep-'))`, removed in `t.after`), `utimesSync` 10 days ago; `{ ...rule, neverSwept: [] }`; PLUS a recording `io` run over a fake listing of the same names | on disk: all eleven exist with their bytes; with the recording io: `io.rmSync` never called, `io.statSync` never called | `coai-panel-x`, `coai-futurething-x`, the eight protected names removed |
| N-R1 | `an unmarked coai-chat-live survives the start of a run when coai-chat- is not excepted` | `coai-chat-live/sentinel.txt`, 11 minutes old; `neverSwept.filter((p) => p !== 'coai-chat-')` | the folder and `sentinel.txt`'s bytes survive | gone |
| N1 | `the shared file names the product's directories and nothing that selects` (rewrites `sweepTemp.test.mjs:31-36`) | `readRule(repoRoot)` | `neverSwept` includes `coai-chat-`; `'prefix' in rule` and `'keepMinutes' in rule` are both false | — |
| N2 | `every temp directory the extension makes at runtime is one the product's list names` | `sweepTemp.test.mjs:38-52` moved verbatim (its regex and its `coai-chat-` must-find) | unchanged | — |
| N3 | `the count is the old test names, never a protected one, a cache or a file` | fake `io.readdirSync` returning the eleven as directories plus `coai-3.png` as a file | `startOfRun({ dir, rule, io }).leftovers === 2`; with `neverSwept: []` it is 10 | — |
| N4 | `a name is never stat-ed: the count reads names only` (rewrites `:114-130`) | recording io with `statSync` that throws | `startOfRun` returns and `statSync` was never called | — |
| N5 | `the runner reports leftovers FIRST, before it makes its run folder` (rewrites `:132-138`) | the text of `run-tests.mjs` from `function main() {` | matches `/^function main\(\) \{\s*reportLeftoversFirst\(\);\s*\n/` | — |
| N6 | `the start-of-run module cannot delete: it imports no remover` | the text of `scripts/startOfRun.mjs` | does not match `/\b(rm|rmSync|rmdir|rmdirSync|unlink|unlinkSync|rename|renameSync|exec|spawn)\b/`; does match `readdirSync` (the scan sees what it knows is there) | — |
| N7 | `the legacy run folders are counted on their own line, the run's own gone from it` (commit 4, **[S2-A gate finding 1]**) | fake `io.readdirSync` returning `testsToDelete-coai-run-abc123`, `testsToDelete-coai-run-def456`, `testsToDelete-coai-run-xyz`, `testsToDelete-u1000-coai-run-abc123` (directories) and `testsToDelete-coai-run-aaaaaa` (a file), plus the eleven | `startOfRun({ dir, rule, io })` is `{ leftovers: 2, legacyRuns: 2 }`; `statSync` never called | — (new) |
| N8 | `the runner prints the legacy line only when there is something to say` (commit 4) | `reportLeftoversFirst` over an injected `log` and `startOfRun` result: `{ leftovers: 0, legacyRuns: 0 }`, `{ leftovers: 0, legacyRuns: 3 }`, `{ leftovers: 2, legacyRuns: 0 }` | the first prints one line (`no old coai-* leftovers`), the second two (the second names `testsToDelete-coai-run-*`, "3" and the module_tests section), the third one | — (new) |
| N9 | `the runner counts the REAL temp root it captured at its start, not the one TEMP/TMP/TMPDIR were moved to` (**[S2-A code 0]**) | a private root holding an old `coai-*` name that TEMP/TMP/TMPDIR are pointed at; `reportLeftoversFirst` with no `root`, then with `root` | the line names `REAL_TEMP_ROOT` and nothing of the redirected root; `root` is honoured | names the redirected root |
| N10 | `the count leaves out the run's own folder` (**[S2-A code 0]**) | `startOfRun({ ownFolder })` over two legacy run folders | `legacyRuns` 1 (2 without `ownFolder`) | 2 |

**The one-off clean-up** — `scripts/lib/old-coai-leftovers.test.mjs`, every case over a fresh private root
(`fs.mkdtempSync(path.join(os.tmpdir(), 'old-coai-leftovers-'))`, removed in `t.after` after links are unlinked by
name), `now` fixed, `probe` injected.

| # | Test | Fixture | Assertion | RED with B5 |
|---|---|---|---|---|
| S1 | `the listing holds coai-* names with age and identity, and leaves out the product's, the cache, files and links` | `coai-panel-a` (60 min), `coai-ctx-b` (3 days), `coai-answers-x`, `coai-chat-x`, `coai-0.31.0`, `coai-3.png` (a file), a symlink `coai-link` → `coai-panel-a`, `other-dir` | `listing.tmp` is the root's absolute path ([S2-A gate finding 2]); `listing.entries` are exactly `coai-panel-a` and `coai-ctx-b`, each with `kind: 'coai'`, `ageMinutes` 60 / 4320, `dev`/`ino` equal to `lstatSync(…, {bigint:true})` of the same path as strings | — |
| S2 | `a legacy run folder is listed with its claim state` | `testsToDelete-coai-run-abc123` + `.owner` = `host@linux:99999:1700000000` with `probe` → `dead` (listed `gone`); a second with `probe` → `alive` (`live`); a third with `.owner` = `host@win32:5` (`unknown`, E5 #15); a fourth with no `.owner` (`none`) | the four, `kind: 'legacy-run'`, the four states | — |
| S3 | `a pick that is not in the listing is refused` | S1's root; pick `coai-ctx-c` | `refused`, check `not-listed`; nothing on disk changed | — |
| S4 | `a product prefix, a cache and a name of another shape are refused even when picked` | a listing JSON hand-edited to carry `coai-chat-x`, `coai-0.31.0`, `other-dir` with their true identities | three `refused` with `protected`; all three exist afterwards with their bytes | — |
| S5 | `a pick changed after the listing is refused` | listing taken; then `utimesSync(coai-panel-a, …)` to a new mtime | `refused`, check `changed`; the folder and its sentinel stay | removed |
| S6 | `a pick swapped after the listing is refused and the swapped-in object keeps its bytes` | listing taken; `coai-panel-a` renamed to `aside`; (a) a NEW directory `coai-panel-a` with `sentinel.txt` = `"the swapped-in one"`; (b) in a second case a symlink `coai-panel-a` → `aside` | (a) `refused`/`changed`, the new directory and its bytes stay, `aside` stays; (b) `refused`/`not-a-directory`, the link stays, `aside` and its bytes stay; `hooks.beforeRename` never called in either | (a) the swapped-in sentinel is gone |
| S7 | `a legacy run folder with a live or unknown claim is refused; one with a gone claim goes with its claim file` | S2's four | `live` → `refused`/`owner-live`; `unknown` → `refused`/`owner-unknown`; `gone` and `none` → `removed`, their `.owner` files gone, the live and unknown ones' `.owner` files intact | the live one is removed |
| S8 | `when every drawn quarantine name is taken the pick is refused and nothing is renamed` | `hooks.drawName` returns the SAME fixed name on every call (so five draws, all taken); the test pre-creates an EMPTY directory there (POSIX also, in a second case: a file there) | `refused`/`quarantine-taken`; `drawName` was called exactly `MOVE_DRAWS` (5) times; the pre-created object untouched (same identity), `coai-panel-a` untouched; on POSIX no placeholder left behind | removed |
| S9 | `an unchanged pick is removed through the quarantine name by identity` | `hooks.drawName` records; `hooks.beforeRename` asserts the placeholder exists and is empty (POSIX) | `removed`; `coai-panel-a` gone; the recorded name matches `/^\.coai-panel-a\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}$/`; nothing else in the root changed (`readdirSync` before/after differ by exactly that name) | (passes for the wrong reason — which is why S5/S6/S8 exist) |
| S10 | `the CLI: list writes the listing, remove refuses a usage error with the legal forms, exit codes 0/1/2` | `execFileSync(process.execPath, [cli, 'list', '--tmp', root, '--out', file])`; then `remove --listing file coai-panel-a coai-ctx-c` | the JSON file is `{ tmp, entries }` with S1's two entries; stdout has `removed coai-panel-a` and `refused coai-ctx-c: not-listed`; exit 1; `remove` with no `--listing` exits 2 naming the forms | — |
| S11 | `a rename that fails removes its own empty placeholder and refuses the pick` (**[S2-A gate finding 0]**, POSIX) | `io.renameSync` throws `EACCES` (an `io` that fails on purpose); `hooks.drawName` records the name | `refused`, check `error-EACCES`; `coai-panel-a` and its sentinel untouched; the drawn placeholder name is NOT in the root afterwards | the placeholder is still there |
| S12 | `a taken draw draws a new name, and the move lands on the second` (**[S2-A gate finding 0]**) | `hooks.drawName` returns a pre-created occupied name first, a free one second | `removed`; `drawName` called twice; the occupied object untouched (same identity); only the second name was ever a placeholder | removed with one draw |
| S13 | `remove uses the listing's temp root, and refuses a --tmp that is another root` (**[S2-A gate finding 2]**) | a listing taken in root A with `coai-panel-a`; a root B holding ANOTHER folder `coai-panel-a` (same name, own sentinel); the CLI run from a third working directory: (1) `remove --listing <A's file> --tmp B coai-panel-a`; (2) `remove --listing <A's file> --tmp A/ coai-panel-a` (A spelled with a trailing separator); (3) a listing file with no `tmp` key, `remove --listing <it> coai-panel-a` | (1) exits 2, one line naming both roots, A's and B's folders and sentinels untouched; (2) exits 0 and `removed coai-panel-a` (the same root, spelled another way), A's folder gone, B's untouched; (3) exits 2 naming the legal forms; S10's `remove` without any `--tmp` removes from the listing's root, never from `os.tmpdir()` | B's folder is removed in (1) |
| S14 | `the listing reports this script's own quarantines, and remove accepts them by name` (**[S2-A gate finding 3]**) | `.coai-panel-a.teststodelete-quarantine-4242-0a1b2c3d` (a directory with a sentinel), `.testsToDelete-coai-run-abc123.teststodelete-quarantine-4242-0a1b2c3d` with its `.owner` (probe `gone`), `.coai-chat-x.teststodelete-quarantine-4242-0a1b2c3d` (a PROTECTED original), `.coai-panel-b.teststodelete-quarantine-4242-0a1b2c3d` as a FILE | the listing carries the first two as `kind: 'quarantine'` with `of`; not the protected one, not the file; `remove` of the first: `removed`; of the second: `removed` and `testsToDelete-coai-run-abc123.owner` gone; a hand-edited listing naming the protected quarantine: `refused`/`protected`; one of them modified after the listing: `refused`/`changed` | removed (every case passes for the wrong reason; the refusals are red) |
| S15 | `kill after the rename, a second run finishes it` (**[S2-A gate finding 3]**) | a child process (`execFileSync` of a tiny ESM script that imports the library and passes `hooks.afterRename` that sends itself `SIGKILL`) removes `coai-panel-a`; then `list` in the parent, then `remove` of the quarantine's name | the child dies by `SIGKILL`; the root holds the quarantine name and no `coai-panel-a`; the second `list` shows it as `kind: 'quarantine'`, `of: 'coai-panel-a'`; the second `remove` answers `removed`; the root is empty | removed by the first run — the child never reaches its kill |
| S16 | `the quarantined object is re-checked after the move by identity AND last-write time; either change keeps it` (**[S2-A gate finding 4]**, strengthened **[S2-A Opus 2]**) | `hooks.afterRename(from, to)` (a) `utimesSync(to, …)` to a new mtime; (b) the REPLACEMENT directory is made BEFORE the original is removed (so its inode differs: ext4 and tmpfs reuse a freed one), given the LISTED last-write time, and renamed onto `to` — only the dev/ino comparison can see it | (a) and (b): `refused`/`moved-but-changed`; the quarantined object and its bytes stay in the root under the quarantine name; the next `list` shows it as `kind: 'quarantine'`; nothing else removed | removed; and with the dev/ino comparison deleted from the post-move check, (b) is RED |
| S17 | `a name that is not ONE path segment is refused before any path is built: the victim outside the root survives` (**[S2-A Opus 1]**, BLOCKING) | the temp root a child of a private folder holding `victim/` with a sentinel; a hand-edited listing whose entries are `coai-x/../../victim` (with the victim's real identity), the backslash form, a quarantine-shaped one, `..` and `.` | every pick `refused`/`bad-name`; the victim keeps its bytes; nothing is made beside it or inside the root | the victim is removed |
| S18 | `a quarantine whose renamer is alive or cannot be asked is refused; one whose renamer is gone is removed` (**[S2-A Opus 3]**) | three quarantines naming pids 88888 (live), 77777 (unaskable), 99999 (gone) of the injected probe | `owner-live`, `owner-unknown`, `removed` | all removed |
| S19 | `a pick the caller does not own (kernel owner, rule §3) is refused` (**[S2-A Opus 4]**, POSIX) | an `io` whose `lstatSync` answers uid + 1 | `refused`/`foreign-uid`; the folder stays | removed |
| S20 | `a legacy run folder whose claim turned live after the move is kept, with its claim` (**[S2-A Opus 5]**) | `hooks.afterRename` rewrites the `.owner` to a live claim | `refused`/`owner-live`; the quarantined folder and the claim stay | removed |
| S21 | `a rename that answers ENOENT is the missing refusal, on Windows and elsewhere` (**[S2-A code 4]**) | an `io` whose `renameSync` throws `ENOENT`, with `hooks.platform` `win32` and the real one | `refused`/`missing`; no placeholder left | `error-ENOENT` |
| S22 | `the listing is indexed by name once, not searched for every pick` (**[S2-A code 7]**) | `listing.entries.find` throws | three picks answered | throws |
| S23 | `each pick's result is reported as soon as it is decided` (**[S2-A code 6/9]**) | two picks; `afterRename` of the second records what `onResult` has seen | the first was already reported; every result reported once, in order | nothing reported |
| S24 | `list shows the listing first and writes --out after` (**[S2-A code 3]**) | `--out` under a folder that does not exist | exit 2; stdout holds the entry lines; stderr names the failure | nothing shown |

Run: C# `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*TempDirsAreSwept" --filter-class "*PruneOldScratchDirs*" --filter-class "*TheTestSweepIsRetired*"`
then the whole suite; Node `cd src_vs_code && npm test` (lowered: `nice -n 19`); the script `node --test "scripts/**/*.test.mjs"`; the scan
`node .agents/conventions/tools/sweeper-scan.mjs --root .` before commit 1 and after commit 6.

## A.4 — The characterization cases of `PruneOldScratchDirs` (A.1 § 4; commit 1)

| # | Test | Folders (all last write = cutoff − 2 days unless said) | Call | Remain |
|---|---|---|---|---|
| C1 | `TheDefaultPrefixes_TakeExactlyTheFourProductKinds` | `coai-answers-a`, `coai-repair-a`, `coai-noworkspace-a`, `coai-question-a` (no lock file inside), `coai-panel-a`, `coai-chat-a`, `coai-0.17.5`, `other-a` | `(root, cutoff)` | `coai-0.17.5`, `coai-chat-a`, `coai-panel-a`, `other-a` |
| C2 | `AnExplicitPrefixList_TakesOnlyItsMatches` | `coai-panel-a`, `coai-answers-a`, `coai-ctx-a` | `(root, cutoff, ["coai-panel-*", "coai-ctx-*"])` | `coai-answers-a` |
| C3 | `TheExceptList_KeepsItsPrefixes_WhateverTheAge` | `coai-chat-a` (cutoff − 30 days), `coai-panel-a`, `coai-chatter-a` | `(root, cutoff, ["coai-*"], ["coai-chat-"])` | `coai-chat-a` (prefix match is ordinal, so `coai-chatter-a` goes) |
| C4 | `AVersionCache_IsKeptUnderAnyPrefixList` | `coai-0.17.5`, `coai-9`, `coai-panel-a` | `(root, cutoff, ["coai-*"])` | `coai-0.17.5`, `coai-9` |
| C5 | `TheCutoffIsExclusive_OlderGoes_AtTheCutoffAndYoungerStay` | `coai-panel-older` (cutoff − 1 min), `coai-panel-at` (= cutoff), `coai-panel-younger` (cutoff + 1 min) | `(root, cutoff, ["coai-panel-*"])` | `coai-panel-at`, `coai-panel-younger` |
| C6 | `AReadOnlyFileInside_DoesNotKeepTheFolder` | `coai-panel-a/.git/objects/e9` read-only | `(root, cutoff, ["coai-panel-*"])` | (nothing) |
| C7 | `AMissingRoot_IsNotAFailure` | `root` deleted before the call | `(root + "/gone", cutoff)` | (no throw) |

C1 expects `coai-question-a` to go: `FolderLock.RemoveIfFree` takes a free lock and removes (`FolderLock.cs:19-20`).
Break-it (A.1 § 6): with the `>=` at `PanelService.cs:1516` changed to `>` C5 is red; restored before commit.

## A.5 — Existing tests: rewritten, moved, deleted — and why each deleted one pinned only the retired selection

| File:line | Test | Fate | Reason |
|---|---|---|---|
| `TempDirsAreSwept.cs:46` | `Prefixes = ["coai-*"]` | deleted | used only by `:289`, below |
| `:64` | `Rule = SweepRule.Shared()` | stays (type narrowed to `NeverSwept` only) | the scans and the count read it |
| `:88-92` | `TheSharedRuleIsTheOperatorsTenMinutes` | deleted | asserts `Prefix == "coai-"` and `Keeps == 10 min` — the retired selection and nothing else |
| `:103-110` | `EveryTempDirectoryThisProgramMakes_IsNeverTheTestsToSweep` | stays, renamed `…_IsNamedInTheProductsList`; its remark's "would be removed out from under a live round" becomes "is what the one-off clean-up refuses" | a product-prefix scan (F7) |
| `:127-141` | `AProductWorkingDirectory_SurvivesTheTestSweep_WhateverItsAge` | deleted; its "chat survives" half is C-I1/C-R1, its "`coai-panel-old` goes" half asserts the retired removal | half of it pins the age-only selection |
| `:144-158` | `TheSweepRemovesWhatIsOldAndLeavesWhatIsInUse` | deleted | its case is A.4 C5 (with the Windows-tunnelling remark carried into C5's comment); the `coai-panel-*` prefix made it read as a test-sweep test |
| `:160-178` | `ADirectoryGitHasBeenIn_IsSweptToo` | stays | a product-sweep behaviour (the shared deleter) — not a selection; `coai-ctx-*` is only its explicit prefix |
| `:180-200`, `:207-244` | the Vibe-home and Kimi-home cases | stay | product-sweep behaviours |
| `:252-273` | `EveryProductDeleterIsTheSharedOne` | stays | a product scan |
| `:275-294` | `ADownloadedServerIsNotScratch_AndSurvives` | deleted | its case is A.4 C4; the comment "the sweep matches coai-*" describes the retired sweep |
| `:304-315` | `TheProductSweeps_AtMostOncePerWindow` | stays | the product's throttle |
| `:317-356` | the three `TempDir` RAII tests | stay | `TempDir`, not the sweep |
| `sweepTemp.test.mjs:31-36` | the rule test | rewritten as N1 | pinned `prefix` and `keepMinutes` |
| `:38-52` | the extension's product-prefix scan | moved verbatim as N2 | a product-prefix scan (F7) |
| `:54-59` | `older than the window is taken, younger is kept` | deleted | the age selection |
| `:61-65` | `only the prefix is considered, whatever the age` | deleted | selection; "other software's directories are untouched" is implied by N-I1 (nothing is touched) |
| `:67-73` | `the product's own … are never swept` | deleted | asserts `coai-panel-abc` IS taken; the never-swept half is N-I1 |
| `:75-77` | `a FILE with the prefix is not a directory to sweep` | rewritten into N3 (the file is not counted) | — |
| `:79-97` | `on a real disk: the old one goes …` | deleted | removal |
| `:99-112` | `a directory that cannot be removed is counted and stepped over` | deleted | removal |
| `:114-130` | `a name without the prefix is never even stat-ed` | rewritten as N4 (nothing is stat-ed) | — |
| `:132-138` | `the runner sweeps FIRST` | rewritten as N5 | — |
| `:140-200` | the junction and symlink cases | deleted | removal through `rmSync`; the leftover-with-a-link scenario is the reference sweep's (`leftovers-sweep.test.mjs`) and returns in S2-B item 5 |
| `src_server/tests/TheTestSweepLeavesAJobAloneTests.cs` | unchanged in code; its `<summary>`/`<remarks>` ("Both test runners sweep `coai-*` …") rewritten in commit 6 to "No test runner sweeps a `coai-*` name (E5 S2-A); the list is what the one-off clean-up refuses" | a product-prefix scan (F7); the doc text is the only change, and it is a docs commit |

## A.6 — Docs (commit 6)

- `research/module_tests.md:410-426`: the paragraph *Both runners sweep BEFORE they start* is kept as history with
  its date and gets a new opening sentence "**Retired 2026-11 (E5 S2-A, #<PR>):** …"; a new paragraph after it says
  what runs now (the count line in both runners, the sentence), states the invariant and names its tests (C-I1/N-I1,
  C-R1/N-R1), names the characterization test and the three product-sweep tests that prove `PanelService.cs` unchanged,
  and records the residual risk (an old checkout keeps its own file and its own age-only sweep; update or rebase it,
  or run it with a private temp root). A new section § *The one-off clean-up of old `coai-*` test leftovers*: the two
  commands verbatim, the checks in order, the quarantine shape, "run with no coai session and no VS Code window open",
  the legacy `testsToDelete-coai-run-*` line — **[S2-A gate finding 1]** the section says the one-off clean-up handles BOTH the
  `coai-*` leftovers and the legacy `testsToDelete-coai-run-*` run folders, that both runners count both (two lines), and that a Linux
  reader's legacy claims are always `unknown` (rule §3) — the listing temp root and the `--tmp` refusal (finding 2), the interrupted
  clean-ups the listing reports and `remove` finishes (finding 3), the post-move re-check by identity and last-write time and the
  residual window (finding 4), the bounded redraw and the placeholder's removal (finding 0), the follow-up to switch to the
  reference's exported identity helpers (P4) — and what it never does (never a sweep, never the janitor's, never wired
  into a runner).
- `shared/temp-sweep.json` `why` — decision 1.
- `TheTestSweepLeavesAJobAloneTests.cs` doc text — A.5's last row.
- `todo/PLAN_test_leftovers_E5.md`: no text change in this PR beyond the link this spec's commit adds; the S2-A DoD
  boxes are ticked by the PR that merges.

## A.7 — The PR description

1. The scope in the review-gate's form (goal, what must be true, constraints — `local.common.review-gate`): the goal is
   D7 in one sentence; what must be true is the S2-A DoD of E5 `:561-564`; the constraints are "nothing under
   `src_mcp/src`, `runners`, `core` changes" and "no owner-form producer, no owner-aware sweep in this PR".
2. The commit list of A.2 with, per commit, what was OBSERVED: the characterization run on `main`'s code (seven
   green, B4 red/restored); commit 3's four failure lines verbatim; commit 4's green run and B1–B3; commit 5's
   S5/S6/S8 red lines under B5 and the green run after.
3. The old-reader observations of decision 1 (Node: `{ removed: 0, failed: 0 }`; C#: the `TypeInitializationException`).
4. **The stage-A record for the parent's R6** (`dew_flow_conventions · todo/PLAN_test_leftovers_merged_core_release.md`,
   the coai row): "coai stage A (sweep-only): the age-only `coai-` test sweep — `sweepTemp.mjs` through `run-tests.mjs`
   and `TempDirsAreSwept` through `PanelService.PruneOldScratchDirs` — retired in #<PR> (`<sha>`), merged alone. The
   barrier scan before (`<sha of main>`): 2 owner-blind lines, `scripts/lib/transcript-sanitise.test.mjs:686` and
   `src_vs_code/src/test/runTemp.test.mjs:149`, both test-file literals of S5's class, neither a sweeper, and the
   age-only sweep NOT listed (it matched `coai-`, which the scan does not look for — E5 § *Facts*, note 9). After
   (`<sha>`): the same 2 lines, no new line; S5 removes them. The evidence that the age-based sweeper is gone is this
   PR's diff and its tests C-I1/N-I1 and C-R1/N-R1, not the scan." The scan's two outputs pasted verbatim.
5. The `git diff --exit-code origin/main -- src_mcp/src/Server/PanelService.cs` line (empty) and the green run of the
   product sweep's own tests (`ScratchDirsTests`, `TheScratchSweepNeverDeletesAKimiHomeUnderALiveTreeTests`,
   `ConsultCheckSweepTests`, `ConsultantHomeSweepTests`, `AScratchSweepRunsOnTheConsultationBeatTests`, `chatStoreSweep.test.ts`).
6. The residual risk (D4) restated; the platforms the script's tests ran on.
7. A note for E5 § *Notes for the parent*, new note 19: the reference's `identityOf`/`moveExclusively` are duplicated
   here because they are not exported (decision 5, P4 decided: copy now, stage A merges before any new conventions release).
   **Follow-up to record:** once the conventions export them, `scripts/lib/old-coai-leftovers.mjs` switches to the exported
   function and deletes its copy; the same note names S4's wrap as the second consumer.

## A.8 — What the Opus reviewer checks

1. **Commit 1 stands alone** and its file is byte-identical in every later commit of the PR (`git log --oneline -- src_mcp/tests/PruneOldScratchDirsCharacterizationTests.cs` shows one commit).
2. **`PanelService.cs` is unchanged**: `git diff origin/main...HEAD -- src_mcp/src/Server/PanelService.cs` is empty; so
   is the diff under `src_mcp/src`, `src_mcp/runners`, `src_mcp/core`, `src_server`, `src_bugs`, `src_bench`.
3. **Nothing in the test start can delete**: `grep -n 'PanelService\|Delete\|Remove' src_mcp/tests/StartOfRun.cs`
   finds nothing; `grep -nE '\b(rm|rmSync|rmdir|unlink|rename|exec|spawn)' src_vs_code/scripts/startOfRun.mjs` finds
   nothing; `TempDirsAreSwept`'s constructor calls `StartOfRun.Run` and `FakeCliSteering.DeleteALeftover` only.
4. **Commit 3 was RED for the right reason**: the quoted failure lines name the missing folders, not a compile error or
   a wrong path; the tests in commit 3 and in HEAD are the same text (`git diff <commit 3>..HEAD -- <the two test files>`
   shows only additions of N1–N6 / C-N1–C-N3).
5. **The invariant tests use `neverSwept` EMPTY** (not the shared list) and the protected names at 10 days — a test
   that passes because the list protects them proves nothing.
6. **Every deleted test is in A.5's table with a reason that is "selection" or "removal"**; no product-sweep test, no
   product-prefix scan and no `TempDir` test was deleted; the three scans still assert their must-find prefix.
7. **The script**: no `rmSync` by the picked NAME anywhere (`grep -n 'rmSync' scripts/lib/old-coai-leftovers.mjs` shows
   only the call on the quarantined path); the check order of decision 5 in code and in the module's doc comment;
   `identityOf` is `lstat` with `bigint`; the placeholder is removed in a `finally` only while it is still our empty reservation, and a taken draw redraws at most `MOVE_DRAWS` times; the post-move check compares `dev`, `ino` AND `mtimeMs`; the Windows branch uses the reference's `isTakenRename`; nothing throws out of
   `removePicked`; S6 has BOTH the directory and the link variant; the CLI has no `--force`, no `--all`, no glob.
8. **`temp-sweep.json`**: `neverSwept` byte-identical to `origin/main`'s (`git diff` of the file shows only `why`,
   `prefix` and `keepMinutes` lines); the `why` names the PR and says "no test sweep deletes by this file".
9. **No owner-form producer and no owner-aware sweep** slipped in: `grep -rn 'testsToDelete-u\|ownedFor\|sweepLeftovers' src_vs_code/scripts src_mcp/tests scripts` is empty apart from the mount.
10. The PR description carries items 1–7 of A.7, with the two scan outputs verbatim.

## A.9 — Points the owner may overrule (non-blocking; each has its answer above)

- **P1** — keep `prefix`/`keepMinutes` as documentation after all (E5 Q7). Spec: delete (decision 1); reverting is a
  five-line change in commit 4 and adds `C-N1`'s constant to the file.
- **P2** — no count line at all (decision 2's "why a count"). Spec: count; a silent retirement hides the pile the
  operator's ruling was about.
- **P3** — rename `TempDirsAreSwept` now. Spec: keep until S2-B deletes it (decision 2).
- **P4** — the script's identity helpers duplicated from the reference (decision 5). **Decided 2026-11-02:** duplicate ~40 lines NOW
  with a source comment (stage A merges before any new conventions release, so an import of a function that does not exist yet
  cannot be built); the follow-up to switch to an exported function is recorded (A.7 item 7, note 19, `module_tests.md`).
  P1–P3 stay as the spec's recommended answers: delete `prefix`/`keepMinutes` (P1), count (P2), keep the class name (P3).

## A.10 — The changes after the code gate and the Opus review (2026-11-02)

The S2-A code gate (proceed) and the Opus review returned the items below; each was built RED first (a test commit, then the fix, then the RED confirmed again with the fix stashed), and each is marked where it lands above. Numbers are the reviews' own: `code n` is the coai code gate, `Opus n` the Opus review.

| Mark | Change | Where |
|---|---|---|
| **Opus 1** (BLOCKING; code gate 2) | a name is ONE path segment (`bad-name`), checked before any path is built; the quarantine pattern excludes separators | decision 5, S17 |
| code 0 | the Node runner captures `REAL_TEMP_ROOT` before `beginRun`, counts it, names it, and leaves its own folder out | decision 2, N9, N10 |
| code 3 | `list` prints first, writes `--out` after; a write failure exits 2 | decision 5, S24 |
| code 4 | an `ENOENT` from the rename is `missing` | decision 5, S21 |
| code 6/9 | each pick's result is printed as soon as it is decided (`onResult`) | decision 5, S23 |
| code 7 | the listing's entries are indexed by name once | decision 5, S22 |
| Opus 2 | S16 tests the dev/ino part of the post-move check (replacement made before the original goes, listed mtime put back) | S16 |
| Opus 3 | a quarantine's pid is probed; live or unknown refuses | decision 5, S18 |
| Opus 4 | on POSIX a pick whose lstat uid is not the caller's is refused (`foreign-uid`) | decision 5, S19 |
| Opus 5 | a legacy run folder's claim is read again after the move | decision 5, S20 |
| Opus 6 | `.gitignore` names the default listing `old-coai-leftovers-listing.json` | `.gitignore` |
| Opus 7 | the spec's drift is recorded: `mtimeNs` (not `mtimeMs`), the `mtimeNs` and `empty` fields, the `claimPlatform` and `onResult` seams | decision 5 |

### A.10 round 2 — the second code round (2026-11-02, proceed)

Accepted 0, 1, 2, 4, 5, 6, 7, 8; rejected 3. Built RED first (tests S25–S31), then the fix, then the RED confirmed with the fix stashed.

| Mark | Change | Test |
|---|---|---|
| **[S2-A code2 0]** | `judge()` returns null unless the quarantine's original is ONE segment, before any claim is read or unlinked (defence in depth; `judge` is exported) | S25 — green by construction through the listing; RED by mutation (pattern relaxed and the check removed) through `judge()` |
| **[S2-A code2 1]** | `removeQuarantined` re-checks the uid where the object is now: `foreign-uid`, kept | S26 |
| **[S2-A code2 2]** | `claimState` treats any read failure (a throw, `EACCES`, a directory or FIFO in the claim's place) as `unreadable`: refuses a pick, never aborts a listing; a `readClaim` seam | S27 |
| **[S2-A code2 4/8]** | `list` prints one start line `scanning <root>` before the scan | S28 |
| **[S2-A code2 5]** | `--tmp` and the listing's root are compared by realpath (`sameRoot`); when they differ both are shown as given | S29 |
| **[S2-A code2 6]** | a folder of our shape whose name is not one segment on THIS system (a POSIX name with a backslash) is reported in `listing.refused` as `bad-name` and printed, never offered | S30 |
| **[S2-A code2 7]** | `removePicked` derives its root from `listing.tmp` (absolute, else `bad-listing`) and refuses a `tmp` argument that is another root (`wrong-root`): the deletion API itself holds the confinement; the CLI passes only the listing | S31 |

**Rejected:** code2 3 — no streaming of the listing (as code 5 before: a one-off script).

### A.10 round 3 — the third code round (2026-11-02, proceed)

Accepted 0, 3, 4; rejected 1, 2, 5. Built RED first (S32, S33, S29 extended; S10 and S22 changed), then the fix, then the RED confirmed with the fix stashed.

| Mark | Change | Test |
|---|---|---|
| **[S2-A code3 0]** | when the removal fails AFTER the move (and for every other refusal after it), the result carries `quarantine` and `describeResult` says the folder was moved aside as that name and that `list` then `remove` finishes it | S32 |
| **[S2-A code3 3]** | every pick is checked against the listing BEFORE any removal: one unlisted pick removes nothing (`not-listed` for it, `not-attempted` for the rest), every unlisted name reported | S33, S10 |
| **[S2-A code3 4]** | the `--tmp` mismatch message names the way out: omit `--tmp`, or run `list --tmp <dir>` for that root | S29 |

### A.10 round 4 — the fourth code round (2026-11-02, proceed)

Accepted 8 and 9; every other item of the round is rejected (no change). Built RED first (S34, S35), then the fix, then the RED confirmed with the fix stashed.

| Mark | Change | Test |
|---|---|---|
| **[S2-A code4 8]** | `listOldLeftovers` resolves `tmp` ONCE at its start and uses that absolute path for the scan, every later look and the recorded root | S34 |
| **[S2-A code4 9]** | a legacy run folder removed whose claim's unlink fails is outcome `partial`, with `claimLeft` (the claim's path) and the cause, printed by `describeResult`; the CLI exits 1 | S35 |

**Rejected, with the reason (no change):** code 1 (the two digit checks — `[0-9]` in a regex and the first-character test — mean the same); code 5 (no streaming of the listing: a one-off script over a temp folder); code 8 and the P4 copy follow-up stay as decided (decision 11: the pinned mount is the Node consumers' interface).

## Definition of Done (S2-A)

- [ ] Commit 1 landed first, green on the unchanged `PanelService.PruneOldScratchDirs`, and is byte-identical at HEAD.
- [ ] Commit 3's four tests were observed RED (lines quoted), then GREEN at commit 4, unchanged in text.
- [ ] C-I1 and N-I1 green with `neverSwept` empty; B1–B3 observed red and restored.
- [ ] No test runner deletes anything: A.8 checks 3 and 9 pass; both runners print the count line.
- [ ] `PanelService.cs` and the product sweep's tests unchanged and green (A.8 check 2, A.7 item 5).
- [ ] The three product-prefix scans unchanged in code and green.
- [ ] `shared/temp-sweep.json`: `neverSwept` unchanged; `prefix`, `keepMinutes` gone; `why` rewritten; the old-reader observations in the PR.
- [ ] The one-off clean-up script with S1–S16 green (S5/S6/S8/S11/S12/S16 observed red under B5), documented in `module_tests.md`, with the residual window and the P4 follow-up.
- [ ] Both start-of-run counters print the legacy `testsToDelete-coai-run-*` line (C-N4, N7, N8) — [S2-A gate finding 1].
- [ ] The stage-A record (A.7 item 4) in the PR, with the scan before and after.
- [ ] `plan-lifecycle.mjs` clean; E5's S2-A DoD boxes ticked in the merging PR; this file's status line updated to "S2-A shipped #<PR>; S2-B half pending".
