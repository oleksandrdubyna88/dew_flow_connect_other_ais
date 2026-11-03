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
  `readFileSync`, `mkdirSync`, `renameSync`, `rmSync`, `rmdirSync`, `unlinkSync`) and `hooks` ([S2-A code6 6]: no `drawName` — the quarantine name is the reference's own, never ours;
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
    `.<name>.teststodelete-quarantine-<pid>-<hex8>` drawn by the reference's `moveToQuarantine` ([S2-A code6 6]: no name seam of ours; a test drives its redraws through the `io`); POSIX —
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
    **P4 — done [S2-A code5 2/8/9]:** the first release copied ~40 lines of the reference's `identityOf`/`moveExclusively` because they were not exported; since release
    `5863c60` (coai main #939) the reference exports them with a last `{ io, platform }` argument, and this module IMPORTS `moveExclusively` and `moveToQuarantine`
    from `.agents/conventions/tools/lib/leftovers-sweep.mjs` — the copy is deleted and no follow-up remains. One small thing stays local: a stat helper that also reads
    `mtimeNs` and `uid` (which the reference's `identityOf` does not carry); the `hooks.drawName` seam and the local draw loop of round 5 are GONE [S2-A code6 6]. The script imports token-free functions straight from the mount (`.agents/conventions/tools/lib/…`): D3's
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
| S8 | `when every drawn quarantine name is taken the pick is refused and nothing is renamed` ([S2-A code6 6]: driven through the `io`) | an `io` whose `mkdirSync` of a quarantine name (the POSIX reservation) answers `EEXIST` every time, and a second whose `renameSync` onto one does | `refused`/`quarantine-taken`; five draws (the pinned reference's bound); `coai-panel-a` untouched; no placeholder left | removed |
| S9 | `an unchanged pick is removed through the quarantine name by identity` | the `io` records the drawn name; `hooks.beforeRename` asserts the placeholder exists and is empty (POSIX) | `removed`; `coai-panel-a` gone; the drawn name matches `/^\.coai-panel-a\.teststodelete-quarantine-[0-9]+-[0-9a-f]+$/`; nothing else in the root changed | (passes for the wrong reason — which is why S5/S6/S8 exist) |
| S10 | `the CLI: list writes the listing, remove refuses a usage error with the legal forms, exit codes 0/1/2` | `execFileSync(process.execPath, [cli, 'list', '--tmp', root, '--out', file])`; then `remove --listing file coai-panel-a coai-ctx-c` | the JSON file is `{ tmp, entries }` with S1's two entries; stdout has `removed coai-panel-a` and `refused coai-ctx-c: not-listed`; exit 1; `remove` with no `--listing` exits 2 naming the forms | — |
| S11 | `a rename that fails removes its own empty placeholder and refuses the pick — as an ERROR, never as quarantine-taken` (**[S2-A gate finding 0]**, **[S2-A code6 1/3]**, POSIX) | an `io` whose `renameSync` onto a quarantine name throws `EACCES` | `refused`, check `error-EACCES` (not `quarantine-taken`); `coai-panel-a` and its sentinel untouched; one draw; the placeholder is NOT in the root afterwards | the placeholder is still there |
| S12 | `a taken draw draws a new name, and the move lands on the draw after the taken ones` (**[S2-A gate finding 0]**, [S2-A code6 6]) | an `io` whose `mkdirSync` answers `EEXIST` for the first two draws | `removed`; three distinct draws; no quarantine name left behind | removed with one draw |
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
| S37 | `the PINNED reference says "no free name" when every draw is taken` (**[S2-A code6 1/3]**) | the reference's `moveToQuarantine` over an `io` whose reservation always answers `EEXIST` | it throws exactly `cannot move <dir> into quarantine: no free name in 5 draws — the folder is kept`; a release that changes it turns this red | — |
| S38 | `there is no name seam` (**[S2-A code6 6]**) | a `hooks.drawName` passed to `removePicked` | never called; the pick is removed | called |

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
  residual window (finding 4), the bounded redraw and the placeholder's removal (finding 0), the import of the reference's exported helpers (P4, done) — and what it never does (never a sweep, never the janitor's, never wired
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
7. ~~A note for E5 § *Notes for the parent*, new note 19~~ — superseded **[S2-A code5 2/8/9]**: the helpers are exported (release `5863c60`) and imported; there is no copy and no follow-up to record.

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
- **P4** — the script's identity helpers. First decided 2026-11-02: copy ~40 lines (not exported yet). **Done [S2-A code5 2/8/9]:** imported from the reference at `5863c60`; the copy and the follow-up are gone.
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

### A.10 round 5 — the fifth code round (2026-11-02, proceed)

Accepted 2/8/9 (drop the copied helpers) and 3 (refuse `:`); the rest rejected. The merge of coai main brought the pin to `5863c60`.

| Mark | Change | Test |
|---|---|---|
| **[S2-A code5 2/8/9]** | the copied `identityOf`, `isOwnEmptyPlaceholder`, `removeOwnEmptyPlaceholder`, `moveExclusively` and `moveToQuarantine` are deleted; the module imports `moveExclusively` and `moveToQuarantine` from the reference and passes `{ io, platform }`; the P4 follow-up is done and removed from A.7 item 7 and A.9 | S1–S36 unchanged and green (a refactor of call shapes; S21's forced `win32` already uses a fake io) |
| **[S2-A code5 3]** | `isOneSegment` also refuses `:` and NUL (`C:foo` is drive-relative on Windows) | S36 (RED first) |

### A.10 round 6 — the sixth code round (2026-11-02, proceed)

Accepted 0, 1, 3, 4 and 6; rejected 2 (the call order matches) and 5 (pin tags in the measurement history stay).

| Mark | Change | Test |
|---|---|---|
| **[S2-A code6 6]** | `hooks.drawName`, `MOVE_DRAWS` and the local draw loop are removed: every quarantine move goes through the reference's `moveToQuarantine`; the collision tests drive its redraws through the `io` (`mkdirSync` of the reservation or `renameSync` answering `EEXIST` for the first k draws, or every draw) | S8, S9, S11, S12 rewritten; S38 RED first |
| **[S2-A code6 1, 3]** | only the reference's name-exhaustion failure (`no free name` in its message) is `quarantine-taken`; every other error is rethrown and reported as `error-<code>` (an `EACCES` rename is `error-EACCES`); the exact message of the PINNED reference is pinned by a test | S11, S37 |
| **[S2-A code6 4]** | `research/architecture.md` records that the clean-up imports its identity and move helpers from the pinned reference (decision 11), not a copy | — |
| **[S2-A code6 0]** | the Definition of Done no longer says "and the P4 follow-up" | — |

### A.10 round 7 — CodeRabbit on PR #940 (2026-11-02, all seven valid)

| Mark | Change | Test |
|---|---|---|
| **[S2-A CodeRabbit 1]** | `research/architecture.md` names the direct imports accurately: `moveToQuarantine` from `leftovers-sweep.mjs`, the owner and identity helpers from `owned.mjs` | — |
| **[S2-A CodeRabbit 2]** | `module_tests.md` limits "neither runner removes anything" to the temp-folder sweep and names the MCP suite's removal of the leftover fake-CLI steering file | — |
| **[S2-A CodeRabbit 3]** | the look at a pick is strict: only ENOENT is `missing`, any other lstat error is `error-<code>`; the quarantine-side look keeps its catch-all | S39 |
| **[S2-A CodeRabbit 4]** | an unreadable quarantine is listed with `empty: undefined`; the listing continues | S40 |
| **[S2-A CodeRabbit 5]** | the legacy claim (device, inode, text) is re-checked after the folder is removed and before it is unlinked; a changed claim is kept, `partial` with cause `claim-changed` | S41 |
| **[S2-A CodeRabbit 6]** | S36's fixture block is POSIX-only (`coai-a:b` cannot be a directory name on Windows); the `isOneSegment` loop runs everywhere | S36 |
| **[S2-A CodeRabbit 7]** | `list` rejects stray positional names with exit 2 and the usage line | S42 |

### A.10 round 9 — code gate on PR #940 (2026-11-02)

Accepted 1; rejected 0 (`decide()` catches the strict look's error and maps it to `error-<code>`; S39 covers it).

| Mark | Change | Test |
|---|---|---|
| **[S2-A code9 1]** | the window between the second claim snapshot and the unlink is closed: the claim is never unlinked by its public name — it is moved to a private name (`<claim>.coai-cleanup-<pid>-<hex>`; reserved with an exclusive create, renamed onto our own reservation), compared THERE (dev, ino, text against `claimBefore`), unlinked only if the same; a different one is put back without replacing (`link` then unlink), else kept at the private name; `partial`/`claim-changed` with `claimLeft` = where it is; ENOENT on the move is `removed`; any other error is `partial` with its code; hooks `afterFolderRemoved`, `afterClaimMoved`. The conventions' `moveExclusively` is not reused: it is directory-only (a file given to it throws EISDIR on the pin; on Windows a file rename replaces), so the file takes the same shape in miniature | S35 (the private name fails to unlink), S41, S43 (the race, both swap kinds), S44 (cannot be put back), S45 (gone), S46 (EACCES) |

**Rejected, with the reason (no change):** code 1 (the two digit checks — `[0-9]` in a regex and the first-character test — mean the same); code 5 (no streaming of the listing: a one-off script over a temp folder); code 8 and the P4 copy follow-up stay as decided (decision 11: the pinned mount is the Node consumers' interface).

## Definition of Done (S2-A)

- [ ] Commit 1 landed first, green on the unchanged `PanelService.PruneOldScratchDirs`, and is byte-identical at HEAD.
- [ ] Commit 3's four tests were observed RED (lines quoted), then GREEN at commit 4, unchanged in text.
- [ ] C-I1 and N-I1 green with `neverSwept` empty; B1–B3 observed red and restored.
- [ ] No test runner deletes anything: A.8 checks 3 and 9 pass; both runners print the count line.
- [ ] `PanelService.cs` and the product sweep's tests unchanged and green (A.8 check 2, A.7 item 5).
- [ ] The three product-prefix scans unchanged in code and green.
- [ ] `shared/temp-sweep.json`: `neverSwept` unchanged; `prefix`, `keepMinutes` gone; `why` rewritten; the old-reader observations in the PR.
- [ ] The one-off clean-up script with S1–S36 green (S5/S6/S8/S11/S12/S16 observed red under B5), documented in `module_tests.md`, with the residual window. **[S2-A code6 0]**
- [ ] Both start-of-run counters print the legacy `testsToDelete-coai-run-*` line (C-N4, N7, N8) — [S2-A gate finding 1].
- [ ] The stage-A record (A.7 item 4) in the PR, with the scan before and after.
- [ ] `plan-lifecycle.mjs` clean; E5's S2-A DoD boxes ticked in the merging PR; this file's status line updated to "S2-A shipped #<PR>; S2-B half pending".

# Stage B — S2-B: the owner-aware sweep at the start of every C# suite and of the extension runner (commit 2 of the producer PR)

> Status: **plan only, nothing implemented yet, 2026-11-03 — the S2-B half of the cornerstone spec, by Fable, written
> after S2-A merged (#940) and after S3 was built on the producer branch `feat/test-leftovers-e5-producer` (read at
> `4bc90f52`; the plan at #943, epoch 4). Story S2-B of [PLAN_test_leftovers_E5.md](PLAN_test_leftovers_E5.md) § *S2-B*
> with its epoch-4 note and its re-review note (finding e1: the order is PROVEN), D3, D4, D8 (the merge-gate table and
> conditions k–m), D10, D11, the stages table, § *Must not copy back in* (rows 3, 4, 5, 7, 8, 12 and the sweep half of
> 10) and the DoD. Where this section and E5 differ, this section wins and the difference is a defect of E5; where it
> and the contract differ, the contract wins; where it and the template differ, the template wins unless a
> must-not-copy row names the difference (E5 § *Stages and order*).** Stage A above is history: nothing of it is
> changed here, and what this section overrules of it is said in B.1 § 6 and B.9.
>
> Scope: `src_mcp/tests_leftovers/` (the library: `LeftoversSweepFixture.cs` gets two manifest operations; new coai files
> `StartOfRunSweep.cs`, `StartOfRunOrderAttribute.cs`; `template-manifest.json` gains two entries); `src_mcp/tests/Leftovers/`
> (the copied `LeftoversSweepTests.cs`, `LeftoversJanitorTests.cs`; new coai `StartOfRunSweepTests.cs`); the four
> `RunTempStart.cs` (two assembly attributes each); `src_mcp/tests/RunTempTests.cs`, the three `RunFolderTests.cs`,
> `src_mcp/tests/TempDirsAreSwept.cs`, `StartOfRun.cs`, `TheTestSweepIsRetiredTests.cs`; new `shared/leftovers-sweep.json`
> (the ONE switch of D11); `src_vs_code/scripts/run-tests.mjs`, `startOfRun.mjs`, new `owned.mjs` (D3), new
> `src_vs_code/src/test/leftoversSweep.test.mjs`, `startOfRun.test.mjs`; new `scripts/run-lib-tests.mjs` and
> `scripts/run-lib-tests.test.mjs`; `.github/workflows/ci.yml` (one line); `research/module_tests.md`,
> `research/module_runners.md`. **Nothing under `src_mcp/src`, `src_mcp/runners`, `src_mcp/core`, `src_server/src`,
> `src_bugs/src` or `src_bench/src` changes; `PanelService.cs` and the characterization test are untouched; no Node file
> outside `src_vs_code/scripts/owned.mjs` writes the token.** Commit 2 of the ONE squash-merged producer PR
> (S3 → S2-B → S4 → S5 → S6); never merged alone, never without S3 (D8's invariant).

## B.0 — Facts this half rests on (read on `feat/test-leftovers-e5-producer` at `4bc90f52`, 2026-11-03)

| # | Fact | Where |
|---|---|---|
| F13 | S3 built the library and the run folder: `RunTemp.Start()` (idempotent, never throws) is called by each assembly's own `[ModuleInitializer]` in `RunTempStart.cs`; `RunTemp.Gate(args, run)` in each `TestEntryPoint.cs` refuses before discovery (exit 78); `RunTemp.Real` is the machine's temp root (trimmed), `RunTemp.Folder` the run's; `RunTemp.IsActive` is true only after the gate passed a test run; `RunTemp.EndOfRun()` restores (idempotent) from `RunTempFixture.Dispose` AND the process-exit handler | `src_mcp/tests_leftovers/RunTemp.cs:62-83`, `:97-138`, `:161-185`, `:188`; `src_mcp/tests/RunTempStart.cs:4`, `:11-12`; `TestEntryPoint.cs:20-23` |
| F14 | `RunTempFolder.Create(realRoot, seams)` records the three variables, calls `OwnedDir.Create("run", realRoot, seams.Warn, seams.NextName, seams.BeforeClaimWrite, seams.AfterClaim, seams.RequireLayout)` — the claim FIRST, exclusively, then the exclusive `mkdir`, in ONE call — then registers the restore, then moves `TEMP`, `TMP`, `TMPDIR`. `RunTempSeams.AfterClaim` is called with the CLAIM path after the claim is written and flushed and BEFORE the folder is made: the hook of the race test (B.1 § 2) | `src_mcp/tests_leftovers/RunTempFolder.cs:11-33`, `:82-97` |
| F15 | `RunTempFixture` is registered as an assembly fixture in each of the four assemblies (`[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.RunTempFixture))]` in each `RunTempStart.cs`); it has no test methods; its constructor is the fallback refusal; its `Dispose` is `RunTemp.EndOfRun()`. The mcp suite has two more assembly fixtures, `TempDirsAreSwept` (also a test class, the `coai-*` count and the fake-CLI steering file) and `TestClaudeHome` | `RunTempFixture.cs:14-31`; `src_mcp/tests/RunTempStart.cs:4`, `TempDirsAreSwept.cs:5`, `TestClaudeHome.cs:4` |
| F16 | The copied `LeftoversSweepFixture` (manifest entry, two operations: the namespace and the fully qualified type of its `[assembly: AssemblyFixture]` line, which is INERT in a non-test library): its public constructor is `this(RealSweep, Console.Error.WriteLine, () => RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed()))`; `RealSweep` is `GuardedSweep(DarwinLayout.Require, () => LeftoversSweep.Run(new SweepOptions(Owned.TempRoot(), DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine)))`; the constructor never throws (a thrown sweep is ONE reported line and `LastRun = SweepResult.Empty`); it says nothing when `run.Count == 0`; `DisposeAsync` runs the audit (which THROWS). `Owned.TempRoot()` is `Path.GetTempPath()`, which after `RunTemp.Start` is the RUN folder (round-1 finding 1) | `src_mcp/tests_leftovers/LeftoversSweepFixture.cs:3`, `:20-44`, `:65-78`; `template-manifest.json:50-53`; `Owned.cs:117` |
| F17 | `LeftoversSweep.Run(SweepOptions)` never throws; `SweepOptions(Tmp, Now, MinAge, Probe, Mine = null, Hooks = null)`; `SweepHooks(AfterCapture, AfterVerdict, AfterRename, AfterRemove, AfterReserve, QuarantineName)`; `SweepResult(Removed, Preserved (Path, Reason), Listed, Errors)` is a public record with a public constructor and `Empty`. Order: quarantines, folders, orphan claims. `RelationOf` reads `Owned.Classify`: owner form of token `coai` → `Own` (segment = `OwnerToken.This`) or `Foreign`; legacy form of token `coai` (`^teststodelete[-_]coai[-_]`) → `Legacy`; everything else, every `coai-*` name included → `Other`, never looked at. A `Legacy` folder: `young` while under the window, else `Keep(dir, "legacy")` and the listed line `legacy: folder <name> — remove with: <rm -rf -- '…' '….owner' | Remove-Item -Recurse -Force …>`; a legacy claim is never READ. An `Own` folder: `Captured` (identity + claim) BEFORE the verdict; `Judge`: `young` → `foreign-uid` → `unmarked` → `unreadable` → `Liveness.State` = `Gone` remove / `Live` keep `live` / else `unknown` or `foreign host`; removal through the quarantine by identity; `replaced` when anything changed in between. An orphan claim: a regular file only; silent when its folder exists, when it is young, or when its folder sits in a sibling quarantine; else the same table (`gone` removes the file). The quarantine name is `.<name>.teststodelete-quarantine-<pid>-<hex8>` with `Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))`, drawn at most `Owned.MaxDraws` (3) times through `Native.RenameExclusively`; a restore is `MakeDirectoryExclusively` + `RenameOnto` (Linux/macOS) or `Directory.Move` (Windows) | `src_mcp/tests_leftovers/LeftoversSweep.cs:33-85`, `:91-123`, `:138-160`, `:163-205`, `:207-245`, `:275-291`, `:297-330`, `:332-343`, `:350-377`, `:440-465`, `:489-520` |
| F18 | `Liveness.State`: own pid → `Live` (the probe answers `Live` for `Environment.ProcessId` on every OS; start times within 2 s, or no start on macOS); `Owner.This.Host` ends `@linux|@macos|@windows`; on a Linux reader a record without `<ns>` or with another `<ns>` is `Unknown`; `Dead` is `Gone` only when `hidepid` is off; `AskablePid(digits)` refuses `0`, a leading zero, non-digits, past `2^53-1`, past `int.MaxValue` on POSIX, past a DWORD or `1` on Windows | `src_mcp/tests_leftovers/Liveness.cs:36-96` |
| F19 | `Scope.IsMine`: POSIX — the entry's uid equals the real uid; Windows — the entry's final path is strictly inside the caller's profile AND the owner SID equals `WindowsIdentity.GetCurrent().User`. `Scope.ReadClaimText` reads ONE verified regular file through a handle, never a link, never a FIFO (`Native.ReadVerifiedRegular`); another uid's claim reads as `Unreadable("foreign-uid")`. MEASURED on `windows-latest` (mcp #58; coai's Step 0 asserts the same facts): the runner is elevated and a new file's owner is `BUILTIN\Administrators`, not the caller — so there `Scope.IsMine` is FALSE for the run's own files; the copied `WindowsOwnership` fixtures (`OwnedByCaller`, `ForeignToCaller`) make both outcomes on any runner | `Scope.cs:40-89`; `LeftoversSweepTests.cs:584-607` (template); `ci.yml:340` (Step 0) |
| F20 | The template's `LeftoversSweepTests` (860 lines, 31 facts) take `LeftoversSweepFixture` as a constructor parameter (xUnit v3 injects a REGISTERED assembly fixture; an unregistered one fails the class at construction), use only PRIVATE roots (`DarwinLayout.FreshOwnedScope()`), `UnmatchablePid` for dead owners, `_ => true` as the default `Mine` seam, `Old` = 11 minutes; `The_fixture_ran_before_this_test_over_the_real_temp_root` asserts only `fixture.LastRun` is not null; its mcp-specific strings are the namespace and two "another token" fixtures (`-mcpx-a-abc123`, `mcp-workspace-abc123`), which stay (they are another token under `coai` too). `LeftoversJanitorTests` (106 lines, Linux only) runs the mounted `janitor.sh --token <RepoToken> --dry-run` over a private root; its mount path is at `:17` and `:32`. Upstream blobs: `LeftoversSweepTests.cs` `2caec6fbe79d8b4c0a75a96e9ed60af589e43bb2`, `LeftoversJanitorTests.cs` `f5ec23b2ccdf729ac08189866421f91345a6b8ea` | `dew_flow_mcp · tests/Mcp.Tests/Leftovers/LeftoversSweepTests.cs:14-30`, `:161-162`, `:577-581`, `:800-860`; `LeftoversJanitorTests.cs:17`, `:32`, `:91` at `197cc76` |
| F21 | xUnit v3 4.0.1 (pinned): `IBeforeAfterTestAttribute`s are read from the ASSEMBLY as well as the class and the method ("BeforeAfterTest attributes are only valid at the assembly, method, or class level"; `IXunitTestAssembly.BeforeAfterTestAttributes`; `ExtensibilityPointFactory.GetAssemblyBeforeAfterTestAttributes`); assembly fixtures are created by the assembly runner before any test collection runs and injected by type into test class constructors. The S3 brief measured: an assembly fixture runs AFTER discovery; the gated `Main` runs BEFORE discovery. Collections run in parallel (`maxParallelThreads: 8`) with no enforced order | `~/.nuget/packages/xunit.v3.extensibility.core/4.0.1/lib/netstandard2.0/xunit.v3.core.xml:30`, `:1078-1080`, `:8654-8656`, `:9-18`; S3 brief § 2; E5 `:1015-1017` |
| F22 | The Node reference at the pin (`cc24f9e`, F22): `sweepLeftovers({ repo, tmp = tempRoot(), now, minAgeMs, probe, mine, hooks, platform = process.platform, token = REPO_TOKEN })` — `checkToken(token)` first; `mutate: platform !== "win32"` (on Windows it lists `unverified:` lines and changes nothing, F16 interim); the passes quarantines → worktrees (`git worktree list` of `repo`) → folders → orphan claims; never throws; `result.listed` carries `foreign:` / `legacy:` / `unmarked:` / `unverified:` / `notice:` lines. A legacy folder is removed only when `mine` AND its old-grammar record reads AND `legacyOwnerState(...) === "gone"`; else listed `legacy: <kind> <name> — remove with: …`. `legacyOwnerState` answers `unknown` for EVERY record on a Linux reader (a legacy record has no `<ns>`), and `parseLegacyOwner` reads only the tags `linux|macos|windows` — E2's `win32`/`darwin` claims are unreadable | `.agents/conventions/tools/lib/leftovers-sweep.mjs:26-42`, `:356-365`, `:384-401`, `:636-662`; `tools/lib/owned.mjs:421`, `:580-587` |
| F23 | The creds E2.1 wrap (the family's precedent for a Node consumer, creds `3da714e`): the runner `import()`s `sweepLeftovers` from the mount by FILE URL inside a `try` (`loadReferenceSweep`: a load failure is ONE line and no sweep, never a failed run), and calls it as `(options) => reference({ ...options, token: helper.REPO_TOKEN })` — the token AFTER the caller's options so nothing can override it; seams `sweep`, `sweepOptions`, `loadSweep`; one report line per root only when the sweep found something; a thrown sweep is one line | `dew_flow_creds_for_devs · src_vs_code/scripts/run-tests.mjs` at `3da714e` (`loadReferenceSweep`, `sweepFor`, `sweepAll`) |
| F24 | The extension runner after S2-A: `REAL_TEMP_ROOT = tmpdir()` captured at load; `reportLeftoversFirst({ log, root, ownFolder, count })` prints the `coai-*` count line and, when `legacyRuns > 0`, the legacy `testsToDelete-coai-run-*` line; `RUN_PREFIX = 'testsToDelete-coai-run-'`; `beginRun()` still falls back to the machine temp (S4 withdraws it); `main()` is `reportLeftoversFirst(); const folder = beginRun(); … finally removeRunTemp(folder); process.exit(status)`. `startOfRun.mjs` exports `readRule(repoRoot)` and `startOfRun({ dir, rule, io, ownFolder })` → `{ leftovers, legacyRuns }`, imports `node:fs` and `node:path` only (N6). The C# twin: `StartOfRun.Run` (the `coai-*` count), `StartOfRun.RunFolders(tempRoot, ownFolder)` (the legacy count), `Sentence`, `LegacySentence`; the `TempDirsAreSwept` constructor prints both, once per process | `src_vs_code/scripts/run-tests.mjs:102`, `:109-122`, `:125`, `:221-230`, `:252-263`; `startOfRun.mjs:14-38`; `src_mcp/tests/StartOfRun.cs:21-38`; `TempDirsAreSwept.cs:52-63` |
| F25 | The stage-A tests that pin the legacy count: C# `TheTestSweepIsRetiredTests.TheLegacyRunFolders_AreCountedOnTheirOwnLine_ExceptThisRunsOwn` (C-N4) and the legacy half of `TheSentences_NameTheNumberTheRootAndWhereToGo`; Node `startOfRun.test.mjs` N7 (`:141`), N8 (`:153`, the `legacyRuns: 3` arm), N10 (`:202`), and N5 (`:126`, `main` starts with `reportLeftoversFirst()`), N9 (`:178`) | `src_mcp/tests/TheTestSweepIsRetiredTests.cs:114`, `:130`; `src_vs_code/src/test/startOfRun.test.mjs:126`, `:141`, `:153`, `:178`, `:202` |
| F26 | `ci.yml` on the branch: `build-test` (ubuntu, the mount with its dependencies) runs the four executables (`:58`, `:64`, `:77`, `:88`) and `node --test "scripts/**/*.test.mjs"` (`:197`, "Probe script tests"); `macos` runs `The stat layout self-test` (`:293`), `Leftovers` (`:303-305`, `--filter-namespace "CoaiMcp.Tests.Leftovers"`), `Test` (`:307-309`), the Team server (`:311-313`), the bench (`:315-317`); `windows` runs `Step 0` (`:340`), `Test` (`:375-376`, the mcp executable whole), `Run folder — Team server` (`:380-381`, `--filter-class "*RunFolderTests"`); `extension` (`:383`, **ubuntu-latest**) runs `npm test` (`:435-437`); no job runs the extension's tests on Windows or macOS; the macOS and Windows jobs install nothing from the mount (no `node` tooling for the mount there until S6). Node is 22 on every Node job | `.github/workflows/ci.yml` (the lines named) |
| F27 | The diff check `TheAdaptedCopyTests` reads every `*.cs` under `src_mcp/tests_leftovers/` and `src_mcp/tests/Leftovers/`: each is a manifest target or on the `CoaiOwn` list (`TheAdaptedCopyTests.cs:31-41`); a plant in a copied file turns it red (expected during a RED-GREEN-RED, recorded). `TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests` (#11) reads every `*.cs` under `src_mcp/tests/Leftovers/` for a raw temp API. `.gitattributes` keeps `src_mcp/tests/Leftovers/*.cs` raw on Windows. The manifest has 38 entries; S3's brief § 13 leaves `LeftoversSweepTests.cs` and `LeftoversJanitorTests.cs` to S2-B and 13 guard/audit files to S6 | `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs:28-41`, `:105`; `template-manifest.json`; `.gitattributes` |
| F28 | S3's children harness: `SuiteChild.Run(args, env, timeout)` → `Result(TimedOut, ExitCode, Pid, StdOut, StdErr)`, `SuiteChild.MatchesNothing`; `RunTempChildren.cs` holds `KilledRunChild` (`COAI_S3_KILLED_CHILD=1`, `Environment.FailFast`), `DiscoverySentinel`, `GateBypassedSentinel`; S3's scenario 7 (`AKilledRun_IsCollected_ByTheSweep`) already runs `LeftoversSweep.Run` with an injected probe over the child's leftover | `src_mcp/tests_leftovers/SuiteChild.cs:13-25`; `src_mcp/tests/RunTempChildren.cs:67-74`; `RunTempTests.cs` |
| F29 | The run folder is an `OwnedDir` in the library's registry (`OwnedDir.Unreclaimed()`) until `RunTempFixture`/ProcessExit disposes it; xUnit v3 does not specify the disposal order between two assembly fixtures; the audit (`LeftoversAudit.Run`) reports a registered, undisposed folder as "never disposed" and THROWS (S3 brief § 12; D9: the audit's wiring is S6's) | `OwnedDir.cs:76`; `LeftoversAudit.cs:3-21`; E5 `:556-564` |
| F30 | `shared/temp-sweep.json` (after S2-A) holds `why` and `neverSwept` only, and its `why` says no test sweep deletes by it; it is read by `SweepRule.Shared()` (climbs five folders from the binary) and by `readRule(repoRoot)` | `shared/temp-sweep.json`; `src_mcp/tests/SweepRule.cs`; `startOfRun.mjs:14-16` |

## B.1 — The decisions

### 1. Where the C# sweep runs: the copied `LeftoversSweepFixture`, registered in each of the four assemblies — and the order is PROVEN on every test of every run

**Decision.** The sweep stays an ASSEMBLY FIXTURE (the plan's primary design), registered in EACH test assembly by its own
line `[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.Leftovers.LeftoversSweepFixture))]` in that assembly's
`RunTempStart.cs`, beside the `RunTempFixture` line. The copied fixture's inert attribute line in the library stays as
the manifest has it. The sweep does NOT move into `RunTemp.Gate` — unless step 0 of B.3 fails (plan B, below).

**Why the fixture holds on 4.0.1 (F21):** xUnit v3 creates every assembly fixture in the assembly runner BEFORE it starts
any test collection — that is what lets a fixture be injected into any class's constructor — and the sweep runs in the
fixture's CONSTRUCTOR, synchronously, to its end. So the sweep ends before the first collection starts, by xUnit's own
construction. The re-review (finding e1) asks that this be proven, not assumed, and proven on every OS; the proof below
runs on EVERY test of EVERY run, so a future xUnit bump that breaks the order is red on the day it lands.

**The edit points of the copied fixture** (D2: operations of the manifest entry `LeftoversSweepFixture.cs`, added to the
two S3 wrote; the ownership, liveness and deletion algorithms are untouched — the fixture is wiring):

| Op | `old` (exact, once) | `new` | Why |
|---|---|---|---|
| A | `LeftoversSweep.Run(new SweepOptions(Owned.TempRoot(), DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine))` | `StartOfRunSweep.Run(RunTemp.Real)` | round-1 finding 1: the sweep root is the MACHINE's root (`RunTemp.Real`), the run folder's parent, never `Owned.TempRoot()` (= the run folder after `RunTemp.Start`); and the ONE switch of D11 and the end-of-sweep timestamp live in coai's own `StartOfRunSweep` (below). `GuardedSweep(DarwinLayout.Require, () => …)` around it is NOT touched: `Require()` stays first (must-not-copy #10, the sweep half) |
| B | `() => RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())` | `audit: null /* the end-of-run audit is wired in E5 S6 (D9); until then this fixture's disposal does nothing */` | D9 and F29: registering the fixture registers its DISPOSAL, which is the audit, which throws on the run folder still in the registry (the disposal order between `RunTempFixture` and this fixture is unspecified). The audit's tests, wiring and self-test steps stay in S6; S6 replaces this operation with its bounded audit |

Both are exact replaces with `count: 1`; the diff check stays green with them. `RunTemp` and `StartOfRunSweep` are in
`CoaiMcp.Tests`, the fixture in `CoaiMcp.Tests.Leftovers`: an enclosing namespace, so no `using` line is added.

**`StartOfRunSweep`** — new, coai-owned, `src_mcp/tests_leftovers/StartOfRunSweep.cs`, namespace `CoaiMcp.Tests`, on the
`CoaiOwn` list of the diff check (F27):

```csharp
public static class StartOfRunSweep
{
    public const string SwitchFile = "shared/leftovers-sweep.json";   // the ONE switch (D11) — B.1 § 4
    public const string SwitchKey = "startOfRunSweep";
    public enum Mode { Collect, ListOnly }

    /// Stopwatch.GetTimestamp() taken when Run RETURNED (result or error); 0 until then. Read by the order proof.
    public static long EndedAt => Volatile.Read(ref _endedAt);
    /// The earliest Before timestamp of any test of this process (Interlocked min); 0 until the first test.
    public static long FirstTestStart => Volatile.Read(ref _firstTestStart);
    /// The root the real sweep was handed (B.4 B3); empty until Run ran.
    public static string LastRoot { get; private set; } = string.Empty;

    /// The real entry: the mode from the switch file, the real sweep, the real listing. Never throws out of the finally.
    public static SweepResult Run(string tmp) =>
        Run(tmp, ReadMode(ReadSwitchFile, Console.Error.WriteLine), Collect, ListOnly);

    internal static SweepResult Run(string tmp, Mode mode, Func<string, SweepResult> collect, Func<string, SweepResult> listOnly)
    {
        LastRoot = tmp;
        try { return mode == Mode.Collect ? collect(tmp) : listOnly(tmp); }
        finally { Volatile.Write(ref _endedAt, Stopwatch.GetTimestamp()); }
    }

    internal static void RecordTestStart(long timestamp);   // Interlocked min into _firstTestStart (0 = unset)
    internal static Mode ReadMode(Func<string?> readSwitchFile, Action<string> warn);   // B.1 § 4
    internal static SweepResult Collect(string tmp) =>
        LeftoversSweep.Run(new SweepOptions(tmp, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));
    internal static SweepResult ListOnly(string tmp);   // B.1 § 4: Listed lines only, nothing read beyond names, nothing removed
}
```

`Collect` is the template's `RealSweep` call verbatim with `tmp` for the root — the five arguments, the real probe, the
real `Scope.IsMine`. `ListOnly` never throws out (a missing root is one `Errors` line, as the sweep's own).

**The order proof — two parts, both coai-owned, both in every assembly:**

1. **`StartOfRunOrderAttribute`** (`src_mcp/tests_leftovers/StartOfRunOrderAttribute.cs`, namespace `CoaiMcp.Tests`,
   `[AttributeUsage(AttributeTargets.Assembly)]`, `: BeforeAfterTestAttribute`), applied in each `RunTempStart.cs` as
   `[assembly: CoaiMcp.Tests.StartOfRunOrder]`. Its `Before(MethodInfo methodUnderTest, IXunitTest test)`:
   `var now = Stopwatch.GetTimestamp(); var ended = StartOfRunSweep.EndedAt; if (ended == 0 || ended > now) throw new
   InvalidOperationException($"coai tests: the start-of-run sweep had not ended when {test.TestDisplayName} started (E5
   S2-B, the order proof)"); StartOfRunSweep.RecordTestStart(now);`. `After` is empty. A throw from `Before` fails THAT
   test with that line (xUnit's contract), so a sweep that did not end before a test is a red test, in every collection,
   on every OS the suite runs on, on every run — the proof is not a sample.
2. **The per-assembly fact `TheProducerAndTheSweep_LandTogether`** (D8's merge condition; B.4 B1) in each of the four
   suites, and **`TheSweep_EndedBeforeTheFirstTestStarted`** (B.4 B2), which asserts `EndedAt != 0`, `FirstTestStart != 0`,
   `FirstTestStart > EndedAt`, and writes the margin as a test warning
   (`TestContext.Current.AddWarning($"E5 S2-B order: the start-of-run sweep ended {ms:F1} ms before the first test started")`)
   so the CI log of every leg carries the measurement.

**The measurement plan** (the re-review asked for one): (a) step 0 of B.3 — a 10-minute scratch probe on xunit.v3 4.0.1
that an assembly-level `BeforeAfterTestAttribute`'s `Before` runs for a test in ANOTHER class of the assembly, and that a
throw from it fails that test (not the host); the observed output is quoted in the PR; (b) the attribute and B2 land, RED
first (B.3 step 1: with nothing registered, `EndedAt` is 0, so every test of the assembly fails with the attribute's line —
record the count of failed tests and three of the lines); (c) GREEN on the four suites locally (Linux), and on every CI
leg that runs a suite (the merge-gate table, B.5): the B2 warning line per suite per leg is copied into `module_tests.md`
(the margin in ms: Linux, macOS, Windows). The prediction, written before the run (the measurement rule): the margin is
positive on every leg and larger than the sweep's own duration; a negative or zero margin anywhere is the failure the
plan's "if the pinned version does not guarantee it" clause names.

**Plan B (only if step 0 of B.3 shows that 4.0.1 does not run an assembly-level `Before`, or if (c) shows a test start
before `EndedAt` on any leg):** the sweep moves into the gate. `RunTemp.Gate` calls `StartOfRunSweep.Run(Real)` AFTER
`Refusal()` passed and BEFORE `run(args)` (never for an information-only command line; `--list-tests` sweeps like a run);
the report lines are printed there through a coai copy of the fixture's `ReportLines` shape; operation A becomes
`StartOfRunSweep.LastResult` (the gate's result, handed to the fixture so `LastRun` and the copied tests still read it)
and the fixture stays registered (D8's test keeps its meaning); the proof is then S3's `DiscoverySentinel` pattern (the
sweep's result file is written before the sentinel). The PR says which branch of this decision was taken and why, with
the probe's output. Nothing else of this section changes under plan B.

### 2. The claim-first race (re-review finding 1): the sweep never touches the current process's own claim or folder, between the claim and the folder, on every OS

**The startup order this design relies on, per process** (S3 built it; S2-B asserts it): `[ModuleInitializer]
RunTemp.Start()` — claim written exclusively → folder made exclusively (ONE `OwnedDir.Create`) → restore registered →
`TEMP`/`TMP`/`TMPDIR` moved — THEN `Main` → `RunTemp.Gate` → the platform is built → discovery → the assembly fixtures
(the sweep, over `RunTemp.Real`) → the collections. So THIS process's sweep never sees its own claim without its folder:
when the sweep runs, both exist, the claim names this live pid, and both are seconds old. The race is with ANOTHER
process's sweep — a second C# suite starting at the same moment (the four suites run one after another in CI, but a
developer or the macOS job's three suites can overlap), the extension runner's Node sweep, or the janitor — which may
list `RunTemp.Real` after this process's claim is written and before its `mkdir`. What it sees is an orphan claim of the
owner form.

**What protects it, two keys, each enough alone:** (1) age SELECTS — an orphan claim younger than ten minutes is passed
over silently (`SweepOrphanClaim`: `Young(written)` → return; the reference: `ageMs < minAgeMs` → `young`); (2) past the
window, liveness — the claim names THIS pid with this start time (and `<ns>` on Linux): `Live` → kept with the reason
`live`. On Windows a third key applies on the hosted runner: the claim is owned by `BUILTIN\Administrators`, so
`Scope.IsMine` says `foreign-uid` → kept and listed. Nothing in either implementation removes an orphan claim on a name,
an age or a missing folder (rule § Never).

**The scenario, B.4 B5**, uses S3's hook: `RunTempFolder.Create(privateRoot, seams with AfterClaim = claim => …)`; inside
the hook, with the claim on disk and the folder NOT there (asserted), run: (a) `LeftoversSweep.Run(new SweepOptions(
privateRoot, DateTimeOffset.UtcNow, MinAge, Liveness.ProbeProcess, Scope.IsMine))` — the real clock, the real probe, the
real scope key; (b) the same with `Now = DateTimeOffset.UtcNow + 11 min` — past the window, so the liveness key alone
decides; (c) when `node` is on PATH, the Node reference through coai's wrap (`node --input-type=module -e` importing
`src_vs_code/scripts/owned.mjs`'s loader, `sweep({ repo, tmp: privateRoot, now: Date.now() + 11 min })`) — the real cross-
process case; skip with the reason without `node`. After each: the claim file exists with the same bytes, `Removed` is
empty, no quarantine entry in `privateRoot`; the reason recorded for (b) is `live` on POSIX and on a Windows machine
whose files the caller owns, `foreign-uid` on the hosted Windows runner (asserted as one of the two, and the observed one
written as a warning). Then `Create` completes: folder and claim both exist. RED by the break-it of B.1 § 8 (B-B5).

### 3. What each runner does with each name class — the table (D4, Q3, rule §1 and §4)

Both runners judge `^testsToDelete-<owner>-coai-` (every spelling, any case) and the legacy form `^testsToDelete-coai-`
of THIS token only. Every other name — `testsToDelete-<owner>-mcp-…`, `testsToDelete-kimi-…`, and EVERY `coai-*` name at
any age — is `Other`: not read, not listed, not in any count of the sweep (the S2-A invariant, now structural: C-I1/N-I1
stay green and B.4 B6 asserts it against the sweep itself).

| Name class | Example | C# sweep (the copy, all four suites) | Node reference (the extension runner, the `scripts/lib` runner) |
|---|---|---|---|
| owner-form, ours, record `live` | `testsToDelete-u1000-coai-run-aB3xYz` + `.owner` naming a live pid | kept, `preserved: … — live` | the same (`live`) |
| owner-form, ours, record `gone` | the claim names a pid no process has (hidepid off) | after 10 min: folder renamed to the quarantine, re-checked by identity, removed; the claim deleted last under its captured identity and text; `removed` | the same, by the reference's `moveToQuarantine`; on **Windows: listed `unverified:`, nothing changes** (F16 interim, until F16's Fix is released and pinned) |
| owner-form, ours, record `unknown` (unreadable, too long, another ns, start times apart, a probe that cannot answer, hidepid) | | kept, `preserved: … — unknown` (or `unreadable`, `foreign host`) | the same |
| owner-form, ours, no claim (unmarked) | the folder alone | kept, listed `unmarked: folder <name>` | the same |
| owner-form, ours, young (< 10 min, folder OR orphan claim) | this run's own folder; another suite's claim between its claim and its `mkdir` | passed over silently | the same (`young`) |
| owner-form, another owner segment | `testsToDelete-u1001-coai-x-abc123` | listed `foreign: folder <name> — owner u1001`, never read | the same |
| owner-form, ours by name, another kernel owner | made by another uid, or by `BUILTIN\Administrators` on the elevated runner | kept `foreign-uid`, listed `foreign:` | the same on POSIX; on Windows `unverified:` |
| owner-form, ours, a `.git` FILE inside | a linked worktree | kept, `other clone` | `other clone` unless `git worktree list` of `repo` shows it (then the worktree pass) |
| owner-form, another token | `testsToDelete-u1000-mcp-x-abc123` | `Other`: not looked at | `other`: not looked at |
| **legacy** (E2's era) | `testsToDelete-coai-run-1a2b3c` + `.owner` `host@linux:pid:start` (or `@win32`, `@darwin`) | LISTED with the exact command (`legacy: folder … — remove with: rm -rf -- '<dir>' '<dir>.owner'`, PowerShell on Windows), `preserved: … — legacy`; **never collected, the claim never read**; young ⇒ silent | listed `legacy: …`; collected ONLY when the kernel owner is the caller AND the old-grammar record is `gone` — which at the pin is **never**: on Linux `legacyOwnerState` is always `unknown` (no `<ns>`), on Windows the sweep lists only, and E2's `win32`/`darwin` tags are unreadable (F22). So in practice: listed |
| legacy orphan claim | `testsToDelete-coai-run-1a2b3c.owner` alone | listed `legacy: claim … — remove with: rm -f -- '…'`, kept | the same |
| quarantine, this token, renamer gone, past the window, name free | `.testsToDelete-u1000-coai-run-aB3xYz.teststodelete-quarantine-4242-0a1b2c3d` | restored under its name, `notice: restored …` | the same |
| quarantine, renamer alive / unaskable pid / inside the window | | untouched, unlisted | the same |
| quarantine whose name is taken | | kept, listed `quarantine-conflict: … remove by hand with: …`, every run | the same |
| a FILE or a LINK under any of these names | | never a candidate (a claim must be a regular file; a folder must be a real directory) | the same |
| `coai-*`, any age, any list | `coai-chat-live`, `coai-answers-x`, `coai-0.58.0`, `coai-panel-x`, `coai-futurething-x` | never looked at; the `coai-*` COUNT line of stage A (B.1 § 6) still says how many there are | never looked at; the runner's count line still counts them |

**What the person does with a legacy name:** the one-off clean-up of stage A (`scripts/old-coai-leftovers.mjs`, decision 5)
— the only remover of E2's folders. Both runners now print the sweep's `legacy:` line for each, with the `rm` command;
the stage-A legacy COUNT line is retired (B.1 § 6).

### 4. The ONE switch of D11: `shared/leftovers-sweep.json`, `"startOfRunSweep": "collect"`

A rollback of the producer PR starts by switching off the DESTRUCTIVE part of the sweep in every suite and in both Node
runners with ONE reviewed one-line change, while creation and teardown stay on (D11 step 1). Two languages, six runners,
one line: the switch is a shared data file, read at the start of every run, never a constant per language.

**The file** `shared/leftovers-sweep.json` (new; beside `temp-sweep.json`, which stays what S2-A made it):

```json
{
  "why": "E5 S2-B, D11: the ONE switch of the owner-aware start-of-run sweep in every C# suite and in both Node runners. 'collect' — the sweep removes a killed run's folders on the two keys of the rule (scope and gone), by identity, through the quarantine. 'list-only' — it removes nothing and lists every name it would have judged; the run folder's creation and teardown are untouched. A rollback of the producer PR changes this ONE value in ONE reviewed line before anything is reverted (todo/PLAN_test_leftovers_E5.md D11). Any other value, a missing key or an unreadable file reads as list-only, with one line.",
  "startOfRunSweep": "collect"
}
```

**Readers.** C# — `StartOfRunSweep.ReadMode(readSwitchFile, warn)`: the file is found as `RepositoryRoot.Path` +
`SwitchFile` (the copied helper the diff check already uses from the binary folder); `"collect"` (exact, case-sensitive)
⇒ `Collect`; anything else — `"list-only"`, `"COLLECT"`, `"yes"`, a missing key, a missing file, bad JSON, an unreadable
file — ⇒ `ListOnly`, and for everything but the literal `"list-only"` ONE warn line
`coai tests: the start-of-run sweep is list-only because shared/leftovers-sweep.json startOfRunSweep is <value|missing|unreadable: reason>`.
Node — `readSweepSwitch(repoRoot)` in `startOfRun.mjs` (the shared-file reader of S2-A; it still imports `node:fs` and
`node:path` only, N6 holds) with the same table and the same line prefixed `run-tests:` (or `run-lib-tests:`).

**What `list-only` does.** C# `StartOfRunSweep.ListOnly(tmp)`: `Directory.EnumerateFileSystemEntries(tmp)`, names only;
every entry whose `Owned.Classify` is the owner form of token `coai` (any owner) or the legacy form of token `coai`, and
every quarantine entry of such a name, is a `Listed` line
`switched-off: <folder|claim|quarantine> <name> — startOfRunSweep is list-only (shared/leftovers-sweep.json, D11); nothing is removed`;
`Removed`, `Preserved`, `Errors` empty (a missing root is one `Errors` line). No claim is read, no identity taken,
nothing renamed. Node: the runner does NOT call `sweepLeftovers`; it lists the same way from `readdirSync` with the
wrap's `ownedPattern()`/`legacyPattern()` (`owned.mjs` of the mount, through coai's one module) and prints the same lines.
`coai-*` names are in neither listing.

**The switch governs every platform on its own terms** *(S2-B spec gate, 2026-11-03, finding 1)*. `list-only` is
decided by coai's runners BEFORE the reference is reached: the C# suites call `ListOnly`, and the Node runner does not call
`sweepLeftovers` at all — so it holds on Windows too, whatever a later reference release does there. `collect` on Windows
is non-mutating today only because the pinned reference lists there (F16 interim); the spec does not rely on that. The
Windows CI step of N-S9 runs BOTH values: `collect` asserts the reference's own Windows answer (`unverified:`, nothing
removed); `list-only` asserts that no sweep was called (the `switched-off:` lines, and a planted gone-owner folder
untouched). If a later pin makes the reference collect on Windows, this test is where that shows.

**The safe side is list-only** (a typo switches collection OFF, never ON), and a test in every suite and in Node pins the
repository's value to `collect` (B.4 B9, N-S5), so a typo on `main` is red in CI, never silent. The PR description's
rollback procedure (D8 (m), B.7) quotes the one line.

### 5. Windows: the evidence is the CI legs only, until P1; the template's SID keys apply unchanged

Until the owner's Windows barrier record (P1, D8 (c)) exists, no C# suite of this branch runs on the Windows side of an
unrecorded machine (S3's rule, kept). Windows evidence of S2-B is: the `windows` job's `Test` step (the mcp executable
whole: the copied `LeftoversSweepTests`, coai's `StartOfRunSweepTests`, the order attribute on every test) and its
`Run folder — Team server` step (`*RunFolderTests`, which now holds B1 and B2 for the server suite). The copied
`WindowsOwnership` fixtures do the work the elevated runner cannot: the collection scenarios on Windows plant their
folder AND claim with `OwnedByCaller` (the template's `Collection_is_proven_with_the_real_scope_key_whatever_the_runners_elevation`),
and the preservation scenario plants `ForeignToCaller` and expects `foreign-uid`; coai's scenarios that run the REAL
`Scope.IsMine` on Windows (B5 (b), B4) accept `live` or `foreign-uid` and record which, as F19 says. Step 0 stays the
first step of the job. The Node sweep on Windows lists only (F16 interim) — asserted in S2-B through the reference's
`platform` seam on Linux (B.4 N-S7), because the extension's tests have no Windows leg (F26; B.10 item 3).

*[2026-11-03, after the question consultants `08beb732`/`76d09c26` on B.10 item 3 — met AS WRITTEN]* **The `windows` job gets
a NEW step that runs the Node sweep test file on Windows, through the actual wrapper and the pinned reference.** The job
already fetches the mount (`ci.yml:365-366`, `Conventions submodule`); it has no Node setup and no `npm ci` today, and it
needs neither package: the test file, `run-tests.mjs`, `owned.mjs`, `startOfRun.mjs` and the mount's `leftovers-sweep.mjs`,
`owned.mjs`, `owned-token.mjs`, `git.mjs` import `node:` modules and repository files only (L5 pins that import graph, so the
step keeps running without `npm ci`). The step, after `Conventions submodule` and before `Build`:

```yaml
      - uses: actions/setup-node@820762786026740c76f36085b0efc47a31fe5020 # v7.0.0
        with:
          node-version: 22
      # E5 S2-B (plan Q6, D8): the extension's start-of-run sweep is the MOUNTED reference with the coai token; on Windows it
      # must list and change nothing (F16 interim). The test file runs the real wrapper against the real reference here, with a
      # fixture that is collectible on every other key — a valid owner-form claim of this owner, an old mtime, a gone owner,
      # inside the profile — so that survival can come only from the Windows gate, never from a malformed claim.
      - name: The Node sweep lists and changes nothing on Windows (F16, Q6)
        working-directory: src_vs_code
        run: node --test src/test/leftoversSweep.test.mjs
```

The scenario it carries is **N-S9** (B.4): on `win32` only, no `loadSweep`, `sweepOptions`, `mine`, `probe` or `platform` seam
— the default loader (`owned.loadStartOfRunSweep`), the default probe, the default `isMine`; the fixture inside the private
root under `os.tmpdir()` (on `windows-latest` `C:\Users\runneradmin\AppData\Local\Temp`, inside the profile — asserted
with the mount's `isMine(root)` as a premise); an owner-form folder of THIS owner and token with a sentinel file; a claim
written with the mount's `formatOwner({ ...thisOwner(), pid })` where `pid` is a child `node -e 0` that has EXITED (the
premise `processProbe(pid).state === 'dead'` asserted through the mount, and `parseOwner(claim)` not null, and
`ownerState(record, processProbe, { nameOwner: thisOwnerToken(), callerOwner: thisOwnerToken(), kernelMine: true })
=== 'gone'`); folder and claim `utimesSync` 11 minutes back. Then `startOfRunSweep({ root, repo, log })` returns the
result: `removed` empty, `listed` holds `unverified: <folder> — the owner SID is not read on Windows (F16)`, the folder,
its sentinel and its claim intact byte for byte, no quarantine entry in the root. Every premise that fails skips the test
WITH its reason (never a silent pass) — except on `win32`, where a failed premise is a failed test (the leg exists to run
it). N-S7 (the `platform: 'win32'` seam on Linux) stays as an extra. When F16's Fix is released and pinned, this step
turns red (the reference then collects): that red is the signal to rewrite N-S9 as a collection test, recorded in
`module_tests.md`'s "what this does not prove".

*[2026-11-03, the same consultation on B.10 item 8 — met AS WRITTEN, on a condition]* **Row 12's C# half gets a RED proof
on the Windows CI leg**: the root-resolution mutation in the copied `Scope`, observed red there for THAT reason (B.4 B13-W;
B.3 step 5's Windows throwaway branch). The fixture establishes the positive ownership premise FIRST with the template's
`WindowsOwnership.OwnedByCaller` on the folder AND its claim (Step 0 shows a new file owned by `BUILTIN\Administrators`
on the elevated runner, so without it `foreign-uid` would hide the path key), and the test is red only when the REASON
is the path key. **The condition:** if the throwaway-branch run cannot show this mutation red for that reason (both arms
of B13-W, the reason strings quoted), the coder reports the evidence, the main session records item 8 as the parent's
entry 4.2, and the plan STOPS (plan-change-control's second change). The coder reports the evidence either way.

### 6. What stage A's "count and report" becomes, and what stays a report

| Stage A | After S2-B | Why |
|---|---|---|
| C# `StartOfRun.Run` + `Sentence` (the `coai-*` count line, once per process, in `TempDirsAreSwept`'s constructor) | **stays**, unchanged | the `coai-*` pile is still nobody's but the one-off clean-up's; the sweep never looks at it (B.1 § 3); the line is what tells a person to run the clean-up (stage A decision 2) |
| C# `StartOfRun.RunFolders` + `LegacySentence` (the legacy `testsToDelete-coai-run-*` count line) | **deleted**, with C-N4 and the legacy half of `TheSentences…` | the sweep now lists each legacy run folder by name with its `rm` command (`legacy:` lines, read from the claim's side, never by age) — a count by name beside it would be a second, weaker answer to the same question |
| Node `startOfRun()` → `{ leftovers, legacyRuns }`, `ownFolder`, `LEGACY_RUN_FOLDER`; `reportLeftoversFirst`'s second line | **`{ leftovers }` only**; `legacyRuns`, `ownFolder` and the legacy line go; N7, N10 and the `legacyRuns` arms of N8 are deleted; N1–N6, N9 and the first-line arms of N8 stay | the same reason; the reference's `legacy:` lines replace it |
| The C# class `TempDirsAreSwept` (stage A said "S2-B replaces/deletes it", P3) | **kept, under its name**: it stays the mcp suite's assembly fixture for the fake-CLI steering file and the `coai-*` count, and the home of the product-sweep tests that stage A kept; its `<summary>` is rewritten ("the owner-aware sweep is `LeftoversSweepFixture` (E5 S2-B); this class counts the old `coai-*` leftovers once and deletes a killed run's fake-CLI steering file") | deleting it moves ~15 product-sweep tests and the steering-file delete for no safety gain; the two fixtures are two jobs. Overrules stage A's sentence (B.9 P7) |
| The runner's order `reportLeftoversFirst(); beginRun();` | **`await loadOwned()` → `startOfRunSweep()` → `reportLeftoversFirst()` → `beginRun()`** (N5 rewritten) | the sweep's `legacy:` lines come first; the count line after; both before anything is made |

Everything that stays is a REPORT: counts and listed lines; the only remover is the sweep, on its two keys, and only
while the switch says `collect`.

### 7. The Node side: the extension runner wraps the reference (D3, F22, F23); the `scripts/lib` command gets a tiny runner

**`src_vs_code/scripts/owned.mjs`** — new, D3's ONE module (S4 extends it; S2-B creates it):

```js
import { ownedFor } from '../../.agents/conventions/tools/lib/owned.mjs';   // the mount; a missing mount throws HERE, at load
export const REPO_TOKEN = 'coai';                                              // written ONCE in Node (D3)
export const REPO_ROW = 'the multi-model review extension and server';         // the `coai` row of the detail's ## Tokens table
export const owned = ownedFor({ token: REPO_TOKEN, row: REPO_ROW });
export { REPO_TOKEN as token };
/** The mounted reference's sweep with THIS repository's token bound AFTER the caller's options (F23). Loaded by file URL in its own try: a load failure is the caller's one line. */
export async function loadStartOfRunSweep() {
  const file = path.join(REPO_ROOT, '.agents', 'conventions', 'tools', 'lib', 'leftovers-sweep.mjs');
  const { sweepLeftovers } = await import(pathToFileURL(file).href);
  if (typeof sweepLeftovers !== 'function') throw new Error(`${file} exports no sweepLeftovers`);
  return (options = {}) => sweepLeftovers({ ...options, token: REPO_TOKEN });
}
```

**`run-tests.mjs`.** `main()` becomes `async`: (1) `const owned = await loadOwned()` — a dynamic `import('./owned.mjs')`
in a `try`; on failure ONE stderr line
`run-tests: the shared conventions are not mounted (<first line of the error>) — run: git submodule update --init .agents/conventions`
and `process.exit(2)` before anything is spawned or made (D3; S4 keeps the same door for `ownedDir`); (2)
`await startOfRunSweep({ owned })` — exported, with the seams `log`, `root = REAL_TEMP_ROOT`, `repo = REPO_ROOT`,
`loadSweep = owned.loadStartOfRunSweep`, `sweepOptions` (the reference's own `probe`, `now`, `platform`, `mine`, `hooks`
for tests; `token` is bound after them and cannot be overridden), `mode = readSweepSwitch(REPO_ROOT)`; it: reads the
mode; in `collect` loads the sweep (a load failure ⇒ one line `run-tests: leftovers sweep not run (<message>); the tests
still will`, nothing swept) and calls `sweep({ ...sweepOptions, repo, tmp: root })` in a `try` (a throw ⇒ the same shape
of line); in `list-only` lists (B.1 § 4). Report: silence when `removed + preserved + listed + errors === 0`; else
`run-tests: leftovers in <root> — removed N, preserved N, listed N`, then one `run-tests: <line>` per `listed` line, one
`run-tests: preserved: <path> — <reason>` per preserved entry up to 20 and then one `run-tests: … N more preserved entries
are not shown`, one `run-tests: error: <line>` per error (the C# fixture's shape, so the two logs read alike). It never
changes the exit code. (3) `reportLeftoversFirst()` (the `coai-*` count, one line). (4) `beginRun()` and the rest as
today (S4 changes `beginRun`). `RUN_PREFIX`, `claimText`, `makeRunTemp`, `removeRunTemp` are NOT touched: the runner's own
folder stays the legacy name until S4 (so after S2-B the runner's sweep LISTS its own previous killed run's folder as
`legacy:` — correct, and S4 ends it).

**`scripts/run-lib-tests.mjs`** — new, the runner in front of the bare `node --test "scripts/**/*.test.mjs"` (round-1
finding 5; the plan's item 2 offered a `--import` preload or a tiny runner: **the runner**, because `--import` is in
`process.execArgv`, which `node --test` hands to every test-file child, so a preload would sweep once per test file,
concurrently, over the real temp root — B.9 P8). `runLibTests({ loadOwned, sweep, sweepOptions, spawn, log, repoRoot,
root = tempRoot of the reference, mode })` exported; the script: load the wrap (failure ⇒ the one line, exit 2), the sweep
over the REAL temp root exactly as `startOfRunSweep` does (same lines, prefixed `run-lib-tests:`; the switch read from
the same file), then `spawnSync(process.execPath, ['--test', 'scripts/**/*.test.mjs'], { cwd: repoRoot, stdio: 'inherit' })`
(the glob is node's own, as the CI step's comment says), and `process.exit(status ?? 1)`. It makes no run folder and sets
no marker — S4 adds the owned folder and the marker its refusal reads (D10's direct-start rule), and until then a direct
`node --test scripts/lib/x.test.mjs` simply runs without a sweep, as today. `ci.yml:197` becomes
`node scripts/run-lib-tests.mjs` (B.5). No `package.json` script exists for this command today; none is added.

**The Node report lines on Windows:** the reference lists `unverified:` lines and removes nothing (F22); the wrapper passes
no `platform`, so a real Windows `npm test` lists only — asserted through the seam (N-S7) AND, since the consultation of
2026-11-03, on the real `windows` leg through the real wrapper and reference (N-S9, B.1 § 5). `startOfRunSweep` RETURNS the
result object (or `undefined` when nothing was swept) so N-S9 can read it.

### 8. Break-its — each observed red, then reverted before the commit; both runs quoted in the PR

| # | Mutation | Red test |
|---|---|---|
| B-B1 | the owner segment dropped from the pattern: `Owned.Pattern` → `^teststodelete[-_]coai[-_]` (a plant in the copied `Owned.cs`) | the copied `Only_this_owners_names_of_this_token_are_swept…` and `A_live_foreign_unknown_or_unmarked_folder…` (the `otherowner` arm), B6; and the diff check (expected, recorded). (S6's guard would also say `owner-blind`; not yet landed) |
| B-B2 | `Liveness.State`: `OwnerState.Unknown` branches → `OwnerState.Gone` (one plant: the final `return … Unknown` of the start-time compare → `Gone`) | the copied `LivenessStateTests` (S3's), `A_live_foreign_unknown…` (the `otherowner`/`handsoff` arms) |
| B-B3 | the age used to AUTHORISE: in `Judge`, `if (options.Now - written >= options.MinAge) return (true, "", read);` before the liveness switch | B4 (b) — the live run folder past the window is removed; the copied test 2's `live` arm |
| B-B4 | the quarantine rename replaced by a plain move: `MoveToQuarantine` → `Directory.Move(dir, quarantine); return quarantine;` | must-not-copy #3: the copied `The_rename_into_quarantine_never_replaces_an_existing_empty_folder…` (the empty folder at the first draw is replaced) |
| B-B5 | the race: in `SweepOrphanClaim` drop `|| Young(written)`, and in `Judge` `OwnerState.Live => (true, "", read)` | B5 (a) and (b): this process's own claim is removed between claim and folder |
| B-B6 | the sweep handed the RUN folder instead of its parent: operation A's `RunTemp.Real` → `RunTemp.Folder` | B3 (a) (`LastRoot` is the run folder); and B4's model does not change (it is a private root) — which is why B3 (a) exists |
| B-B7 | operation A reverted to `Owned.TempRoot()` (the template's unedited root) | B3 (a): `LastRoot` equals `Path.GetTempPath()` trimmed = the run folder, not `RunTemp.Real` |
| B-B8 | the `[assembly: AssemblyFixture(typeof(…LeftoversSweepFixture))]` line removed from ONE suite's `RunTempStart.cs` | that suite's B1 (the attribute is missing) and, in mcp, every copied `LeftoversSweepTests` (the fixture cannot be injected); the order attribute fails every test of that suite (`EndedAt` is 0) |
| B-B9 | the `[assembly: StartOfRunOrder]` line removed from one suite | that suite's B1 and B2 |
| B-B10 | `StartOfRunSweep.Run`: the `finally` timestamp removed | B2 (`EndedAt` is 0 after a sweep) and every test of every suite through the attribute |
| B-B11 | `ReadMode`: `"collect"` compared case-insensitively, or an unknown value mapped to `Collect` | B9 (c) |
| B-B12 | the repository's switch set to `"list-only"` (a one-line change — D11's own rollback, rehearsed) | B9 (a), N-S5 (a): the pinned value; everything else stays green, and a planted gone-owner folder survives — the rehearsal's record goes in the PR |
| B-B13 | Node: `{ token: REPO_TOKEN, ...options }` (the token BEFORE the options) | N-S1's override arm |
| B-B14 | Node: `main` calls `beginRun()` before `startOfRunSweep()` | N-S6 |
| B-B15 | `scripts/run-lib-tests.mjs`: the spawn moved before the sweep | L1 (the spawn stub sees the planted folder still there) |

## B.2 — Files (exact)

| File | Change |
|---|---|
| `src_mcp/tests_leftovers/template-manifest.json` | TWO entries added (B.2 § manifest); the `LeftoversSweepFixture.cs` entry gains operations A and B (after its two existing ones, in that order) |
| `src_mcp/tests/Leftovers/LeftoversSweepTests.cs` | COPIED from `197cc76` with the one operation (the namespace) |
| `src_mcp/tests/Leftovers/LeftoversJanitorTests.cs` | COPIED with three operations (the namespace; the janitor path at `:17`; the submodule sentence at `:32`) |
| `src_mcp/tests_leftovers/LeftoversSweepFixture.cs` | operations A and B applied (nothing else) |
| `src_mcp/tests_leftovers/StartOfRunSweep.cs` | new, coai-owned (B.1 § 1, § 4) |
| `src_mcp/tests_leftovers/StartOfRunOrderAttribute.cs` | new, coai-owned (B.1 § 1) |
| `src_mcp/tests/Leftovers/StartOfRunSweepTests.cs` | new, coai-owned: B3–B13 of B.4 (namespace `CoaiMcp.Tests.Leftovers`, `DarwinLayout.FreshOwnedScope()` for every private root, no raw temp API — #11's scan reads it) |
| `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs` | `CoaiOwn` += the three new coai files |
| `src_mcp/tests/RunTempStart.cs`, `src_server/tests/RunTempStart.cs`, `src_bugs/tests/RunTempStart.cs`, `src_bench/CoaiBench.Tests/RunTempStart.cs` | two attribute lines added to each: `[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.Leftovers.LeftoversSweepFixture))]` and `[assembly: CoaiMcp.Tests.StartOfRunOrder]` |
| `src_mcp/tests/RunTempTests.cs` | B1 and B2 for the mcp suite |
| `src_server/tests/RunFolderTests.cs`, `src_bugs/tests/RunFolderTests.cs`, `src_bench/CoaiBench.Tests/RunFolderTests.cs` | B1 and B2 for each (the Windows filtered step covers the server's) |
| `src_mcp/tests/StartOfRun.cs` | `RunFolders`, `LegacyRunFolder`, `LegacySentence` deleted; `Run`, `Sentence`, `Count` stay |
| `src_mcp/tests/TempDirsAreSwept.cs` | the constructor prints the `coai-*` line only; the `<summary>` rewritten (B.1 § 6) |
| `src_mcp/tests/TheTestSweepIsRetiredTests.cs` | C-N4 deleted; `TheSentences…` loses its legacy arm; C-I1, C-R1, C-N1–C-N3 unchanged |
| `shared/leftovers-sweep.json` | new (B.1 § 4) |
| `src_vs_code/scripts/owned.mjs` | new (B.1 § 7) |
| `src_vs_code/scripts/run-tests.mjs` | `loadOwned`, `startOfRunSweep` (exported, with seams), `main` async and reordered; `reportLeftoversFirst` prints one line; nothing of the run folder touched |
| `src_vs_code/scripts/startOfRun.mjs` | `readSweepSwitch(repoRoot)` added; `startOfRun` returns `{ leftovers }`; `LEGACY_RUN_FOLDER`, `ownFolder` removed; still `node:fs` and `node:path` only |
| `src_vs_code/src/test/leftoversSweep.test.mjs` | new: N-S1–N-S8 |
| `src_vs_code/src/test/startOfRun.test.mjs` | N5 rewritten, N7 and N10 deleted, N8's `legacyRuns` arms deleted, N3/N9 unchanged |
| `scripts/run-lib-tests.mjs`, `scripts/run-lib-tests.test.mjs` | new (B.1 § 7; L1–L4) |
| `.github/workflows/ci.yml:197` | `run: node scripts/run-lib-tests.mjs` (the step's name and comment updated: the sweep in front, then node's own glob) |
| `research/module_tests.md`, `research/module_runners.md` | B.6 |

**The two manifest entries** (exact; the strings are UTF-8, the `count`s are the forward count on the upstream bytes and
the reverse count on the result — the coder re-verifies both with the diff check before committing):

```json
    { "upstream": "tests/Mcp.Tests/Leftovers/LeftoversSweepTests.cs", "target": "src_mcp/tests/Leftovers/LeftoversSweepTests.cs", "blob": "2caec6fbe79d8b4c0a75a96e9ed60af589e43bb2", "replace": [
        {"old": "namespace Mcp.Tests.Leftovers;", "new": "namespace CoaiMcp.Tests.Leftovers;", "count": 1}
    ] },
    { "upstream": "tests/Mcp.Tests/Leftovers/LeftoversJanitorTests.cs", "target": "src_mcp/tests/Leftovers/LeftoversJanitorTests.cs", "blob": "f5ec23b2ccdf729ac08189866421f91345a6b8ea", "replace": [
        {"old": "namespace Mcp.Tests.Leftovers;", "new": "namespace CoaiMcp.Tests.Leftovers;", "count": 1},
        {"old": "private const string Janitor = \".claude/rules/shared/tools/janitor.sh\";", "new": "private const string Janitor = \".agents/conventions/tools/janitor.sh\";", "count": 1},
        {"old": "git submodule update --init .claude/rules/shared", "new": "git submodule update --init .agents/conventions", "count": 1}
    ] }
```

**The fixture entry's two added operations:**

```json
        {"old": "LeftoversSweep.Run(new SweepOptions(Owned.TempRoot(), DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine))", "new": "StartOfRunSweep.Run(RunTemp.Real)", "count": 1},
        {"old": "() => RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())", "new": "audit: null /* the end-of-run audit is wired in E5 S6 (D9); until then this fixture's disposal does nothing */", "count": 1}
```

The manifest then has 40 entries; S6 adds the remaining 13 (the guard and audit files). `.gitattributes` already covers
the two new copies (`src_mcp/tests/Leftovers/*.cs -text`).

## B.3 — The order of work (local commits on the producer branch, after S3's; each named; the PR squash-merges once)

| # | Step / commit | Contains | Observed before the commit |
|---|---|---|---|
| 0 | **Probe** (scratch; not committed; quoted in the PR) | on xunit.v3 4.0.1, a throwaway assembly-level `BeforeAfterTestAttribute` whose `Before` throws for one named test: that test fails with the line, the host does not abort, a test in ANOTHER class also ran `Before` (a counter) | the two facts, or plan B (B.1 § 1) |
| 1 | `test(leftovers): RED — E5 S2-B: the copied sweep tests, the order proof and coai's sweep scenarios, against a suite with no registered sweep` | the two copied test files and their manifest entries; `StartOfRunSweep.cs` and `StartOfRunOrderAttribute.cs` (so the tests compile); `StartOfRunSweepTests.cs` (B3–B13); B1/B2 in the four suites; the Node tests N-S1–N-S8 and L1–L4 (they import exports that do not exist yet) — NO registration, NO operation A/B, NO runner change | RED: the copied `LeftoversSweepTests` fail at construction ("fixture … not found" or the platform's words — quoted); B1 fails on the missing attributes; B2 and every test under the attribute would fail only once the attribute is registered, so B2 fails on `EndedAt == 0`; B3 (a) fails on an empty `LastRoot`; the Node files fail to import. Each failure line quoted in the commit body |
| 2 | `feat(tests): E5 S2-B — the owner-aware sweep is the assembly fixture of every C# suite, over the machine temp root, behind the ONE switch; the order is proven on every test` | operations A and B; the four `RunTempStart.cs`; `shared/leftovers-sweep.json`; the `CoaiOwn` list; `StartOfRun.cs`, `TempDirsAreSwept.cs`, `TheTestSweepIsRetiredTests.cs` trimmed (B.1 § 6) | the four suites GREEN locally (Linux), the diff check green; the B2 warning line per suite copied; break-its B-B1–B-B12 each red then restored |
| 3 | `feat(scripts): E5 S2-B — the extension runner's start-of-run sweep is the mounted reference's with the coai token bound last, behind the same switch; the count keeps one line` | `owned.mjs`, `run-tests.mjs`, `startOfRun.mjs`, `startOfRun.test.mjs` edits, `leftoversSweep.test.mjs` | `npm test` green; N-S* green; B-B13, B-B14 red then restored |
| 4 | `feat(scripts): E5 S2-B — scripts/lib tests run behind a start-of-run sweep (run-lib-tests.mjs); ci.yml runs it` | `scripts/run-lib-tests.mjs`, its test, `ci.yml:197` | `node scripts/run-lib-tests.mjs` green locally (the whole `scripts/**/*.test.mjs` set); L1–L4 green; B-B15 red then restored; `actionlint` if installed |
| 5 | must-not-copy RED-GREEN-RED (B.4 § the rows) — no code lands; the records go in the PR and `module_tests.md`; the macOS arms (#3's `renamex_np` fail-closed, #5's Darwin child) once on a throwaway branch `tmp/e5-s2b-red-macos` with both plants, `gh workflow run ci.yml --ref …`, the red `macos` job recorded, the branch deleted; **and the Windows arm of row 12** (B13-W's `Scope` root-resolution mutation, B.1 § 5's condition) once on `tmp/e5-s2b-red-windows`, the red `windows` job's `Test` step recorded with the two reason strings — or, when it is NOT red for that reason, the evidence reported to the main session (entry 4.2, the stop) | — | each plant red, then reverted; the diff check red beside it (recorded) |
| 6 | `docs(tests): E5 S2-B — module_tests.md and module_runners.md: the one sweep in two languages, the switch, the order proof, the legacy listing` | B.6 | `node .agents/conventions/tools/plan-lifecycle.mjs` clean; `rules check` resolved |

Commit 1 is never squashed into commit 2 locally: the RED commit is what the Opus reviewer cites. Every commit from 2 on
is green on its own (the four executables, `npm test`, the lib runner).

## B.4 — Tests: names, fixtures, assertions (RED symptom in the last column)

Conventions: every private root is `DarwinLayout.FreshOwnedScope().Dir(…)` (the template's way; never the real temp root,
never a raw temp API — #11's scan reads `src_mcp/tests/Leftovers/`); names are built from `Owned.NameBase` (never a spelled
prefix); dead owners are `UnmatchablePid.Next()` with `AssertNoSuchProcess`; "old" is `now + 11 min` through
`SweepOptions.Now` or `Directory.SetLastWriteTimeUtc`; the kernel-owner seam `mine: _ => true` unless the scenario is about
the real key. Windows arms follow F19.

**C# — in every suite** (`RunTempTests.cs` for mcp; `RunFolderTests.cs` for server, bugs, bench):

| # | Test | Assertion | RED on commit 1 |
|---|---|---|---|
| B1 | `TheProducerAndTheSweep_LandTogether` (D8's merge condition) | `RunTemp.IsActive`; `RunTemp.Folder`'s leaf matches `^` + `Regex.Escape(Owned.NameBase + "run-")` + `[A-Za-z0-9]{6}\z` (the producer); `Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyFixtureAttribute>()` contains one whose `AssemblyFixtureType == typeof(LeftoversSweepFixture)` AND one for `RunTempFixture`; `GetCustomAttributes<StartOfRunOrderAttribute>()` is exactly one | no `LeftoversSweepFixture` attribute, no order attribute |
| B2 | `TheSweep_EndedBeforeTheFirstTestStarted` (finding e1) | `StartOfRunSweep.EndedAt != 0`; `FirstTestStart != 0`; `FirstTestStart > EndedAt`; `LastRoot == RunTemp.Real`; the margin written as a warning (`Stopwatch.GetElapsedTime(EndedAt, FirstTestStart)`) | `EndedAt == 0` |

**C# — `src_mcp/tests/Leftovers/StartOfRunSweepTests.cs`** (mcp; runs on the three legs):

| # | Test | Fixture | Assertion | RED |
|---|---|---|---|---|
| B3 | `TheSweepRoot_IsTheMachineTempRoot` (round-1 finding 1) | (a) the real run; (b) a private root `R` holding: an owner-form run folder `inner` with a LIVE claim (this pid), a gone-owner folder `g1` at depth 1 of `R` (old), and a gone-owner folder `g2` INSIDE `inner` (old) | (a) `StartOfRunSweep.LastRoot == RunTemp.Real`, `RunTemp.Real != RunTemp.Folder`, `Trimmed(Path.GetTempPath()) == RunTemp.Folder`; (b) `LeftoversSweep.Run(Options(R, now+11))`: `Removed == [g1]`, `g2` and `inner` untouched (the sweep is depth 1: the run folder's parent is swept, never the run folder's inside), `inner` kept `live` | (a) `LastRoot` empty (commit 1) / the run folder (B-B6, B-B7) |
| B4 | `AFreshRunFolder_Survives_TheSweep_ByAgeAndByLiveness` | `RunTempFolder.Create(R, seams)` with a dictionary for the variables (S3's scenario 1 shape); then (a) `LeftoversSweep.Run(new SweepOptions(R, UtcNow, MinAge, Liveness.ProbeProcess, Scope.IsMine))`; (b) the same with `UtcNow + 11 min` | (a) the folder and its claim are in neither `Removed` nor `Preserved` (young is silent); (b) kept with the reason `live` (POSIX; Windows: `live` or `foreign-uid`, the observed one a warning); after both, folder and claim exist with the same bytes; `Restore()` then removes both | B-B3 |
| B5 | `TheSweep_NeverTouchesTheCurrentProcessOwnClaim_BetweenClaimAndFolder` (re-review finding 1; B.1 § 2) | `RunTempFolder.Create(R, seams with AfterClaim = claim => { assert claim exists and the folder does not; run (a), (b), (c) })`; (c) only with `node` on PATH | after each of (a)/(b)/(c): the claim exists with the same bytes; `Removed` empty; no quarantine entry in `R`; (b)'s reason `live` (or `foreign-uid` on the hosted Windows runner); (c)'s `removed` empty and its `preserved` names the claim with `live` (Linux: the Node reader needs this reader's `<ns>`, which the C# claim carries); then `Create` returns and folder + claim exist | B-B5 |
| B6 | `OnlyThisOwnersCoaiNames_AreJudged_EverythingElseIsNotEvenLookedAt` (coai's addition to the template's test 4; the S2-A invariant against the sweep) | in `R`, all old, all with a dead-pid claim where a claim applies: `testsToDelete-<owner>-mcp-x-abc123`, `TESTSTODELETE-<OWNER>-COAI-X-ABC123`, `teststodelete_<owner>_coai_x_abc123`, a FILE named `testsToDelete-<owner>-coai-f-abc123`, and the eleven `coai-*` names of stage A (`coai-chat-live` with its sentinel, `coai-answers-x`, …, `coai-0.17.5`, `coai-panel-x`, `coai-futurething-x`), each a directory with `sentinel.txt`; `neverSwept` plays no part (the sweep does not read it) | `Removed` = exactly the two coai-spelling folders; the `mcp` one, the file and every `coai-*` entry appear in NO list (`Preserved`, `Listed`, `Errors` name none of them) and every `coai-*` sentinel reads its bytes | commit 1 cannot run it (the file exists); break-it B-B1 (the `mcp` folder becomes `Own`? no — it becomes `Other` still; B-B1 reds the owner arms). RED symptom for this one: a plant `RelationOf` that returns `Own` for `NameForm.Legacy` and for `Other` with a `coai-` prefix ⇒ `coai-chat-live` removed — observed once, reverted |
| B7 | `LegacyNames_AreListedWithTheirCommand_AndNeverCollected` (D4, Q3, item 3) | `testsToDelete-coai-run-1a2b3c` + `.owner` = `host@linux:<dead>:1700000000` (old); `…-2b3c4d` + `host@win32:5`; `…-3c4d5e` + `host@darwin:5:17`; `…-4d5e6f` with NO claim; a legacy orphan claim `testsToDelete-coai-run-5e6f7a.owner`; a legacy folder 1 minute old; `mine` a recording seam | every old legacy folder: `Preserved` reason `legacy`, one `Listed` line `legacy: folder <name> — remove with: ` + (`rm -rf -- '<dir>' '<dir>.owner'` on POSIX, `Remove-Item -Recurse -Force '<dir>', '<dir>.owner'` on Windows); the orphan: `legacy: claim … — remove with: rm -f -- '…'` / `Remove-Item -Force '…'`; the young one: `young`, no line; `Removed` empty; every folder and claim byte-identical; the `mine` seam was never asked about a legacy claim (the claim is never read) | a plant that sends `Legacy` through `Judge` ⇒ the `linux` one is removed (its pid is dead) — observed once, reverted |
| B8 | `TwoSweepsAtOnce_NeverFail_AndNeverRemoveEachOthersWork` (round-1 item 7) | (a) a gone-owner folder; `hooks.AfterVerdict = dir => { delete the folder and its claim (the OTHER sweep got there first) }`; (b) `hooks.AfterRename = (dir, q) => { Directory.Delete(q, true) }` (the other removed the quarantined object); (c) twenty gone-owner folders in `R`; two REAL `LeftoversSweep.Run(Options(R))` started at once on two threads (`Parallel.Invoke`) | (a) no throw, `Errors` empty, `Removed` does not name it, at most one `Preserved` entry for it, reason `replaced` (the template's shape — E5 item 4's "no report line" is read as "no ERROR line", B.10 item 8); (b) no throw, `Errors` empty or one line naming the quarantine (recorded), nothing left in `R` for it; (c) the union of the two `Removed` lists is the twenty, no duplicates, both `Errors` empty, `R` empty afterwards | a plant that makes `RemoveFolder` throw on `Unchanged == false` ⇒ (a) throws into `Errors` — observed once, reverted (the template is green here already) |
| B9 | `TheSwitch_IsOnInTheRepository_AndListOnlyRemovesNothing` (D11, B.1 § 4) | (a) the real file; (b) `R` with a gone-owner folder (old), a legacy folder, a quarantine of an own name, `coai-x`; `StartOfRunSweep.Run(R, Mode.ListOnly, collect: _ => throw, listOnly: StartOfRunSweep.ListOnly)`; (c) `ReadMode` over injected contents; (d) `Run` with a `collect` that throws | (a) `ReadMode(real) == Collect`; the JSON has exactly the keys `why` and `startOfRunSweep`; (b) `collect` never called; `Removed`, `Preserved`, `Errors` empty; `Listed` has one `switched-off: folder …`, one `… folder <legacy>`, one `… quarantine …` line, and no line for `coai-x`; no quarantine made, every byte intact; (c) `"collect"` ⇒ `Collect` with no warn line; `"list-only"` ⇒ `ListOnly`, no line; `"COLLECT"`, `"yes"`, `{}` (no key), `null` (no file), `"{"` (bad JSON), a reader that throws ⇒ `ListOnly` with ONE warn line naming `shared/leftovers-sweep.json` and the value or the cause; (d) `EndedAt != 0` after the throw, and the throw propagates (the fixture's constructor turns it into its one line — the copied `A_sweep_that_throws_in_the_fixture…` covers that) | B-B11, B-B12 |
| B10 | `TheQuarantineName_IsAlwaysLowerHex_AndTheRestorePatternReadsEveryOne` (#4) | (a) 5 000 draws of the private `Pass.QuarantinePath` through reflection (`typeof(LeftoversSweep).GetNestedType("Pass", NonPublic)!.GetMethod("QuarantinePath", NonPublic \| Static)`; a null is a failure naming the manifest — the member moved); (b) 50 real removals of gone-owner folders with `hooks.AfterRename` recording the drawn name | every name matches `^\.(.+)\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}\z` (the pid part equals `Environment.ProcessId`); for 20 of them a directory is made under that name in `R` and `LeftoversSweep.QuarantinedNames(R)` contains the original | a plant with a `[a-z0-9]` alphabet (`Convert.ToHexStringLower` → a base-36 draw) ⇒ (a) red within the first draws |
| B11 | `AFifoAtTheClaimPath_IsUnreadable_AndTheFolderIsKept_WithinFiveSeconds` (#5; POSIX; skip on Windows with the reason) | a gone-owner folder, old; its `.owner` replaced by a FIFO (`PosixFifo`, the copied helper; the premise `(Mode & 0xF000) == 0x1000` asserted as the Darwin child does); the sweep on a task | the task completes within 5 s; `Preserved` reason `unreadable`; the folder and the FIFO still there; `Removed` empty; no writer was ever needed | a plant `Scope.ReadClaimText` → `File.ReadAllText` ⇒ the task does not complete in 5 s (the test fails on the wait) |
| B12 | — (#8, #7 and the junction case are the copied tests: `An_orphan_claim_whose_folder_sits_in_a_quarantine_is_kept_with_it`, `A_quarantine_whose_renamer_is_no_process_id_is_never_restored_and_never_probed`, `A_killed_runs_folder_is_collected_and_a_link_inside_it_never_costs_its_target`; nothing is duplicated — decision 11's reasoning applied to the C# copy) | | | the plants of B.4 § rows |
| B13 | `TheSweep_JudgesTheSame_UnderTheRootAsGivenAndAsResolved` (#12) | `R`; a symlink `alias` → `R` in the owned scope (skip with the reason where links cannot be made); a gone-owner folder `g` and a live-owner folder `l`, old; the sweep run over `alias` then (after re-planting `g`) over `R` | both runs: `g` removed, `l` kept `live`, no `Errors`; the `Removed` entry of the first run starts with `alias`, of the second with `R` (each names what it was given). **On macOS, exactly what row 12 says** *[2026-11-03, the consultation on B.10 item 8]*: the REAL run folder `RunTemp.Folder` is judged with `Scope.IsMine` under the root as `Path.GetTempPath()` returns it (`/var/folders/…`) AND under its resolved form (`/private/var/folders/…`, from `Directory.ResolveLinkTarget("/var", returnFinalTarget: true)`, the premise that the two differ asserted) — `mine` BOTH ways; `LeftoversSweep.Run` over `RunTemp.Real` as given and over its resolved form each list the run folder as `preserved … live` (the sweep's listing agrees), and `Native.IdentityOf` of the claim under both spellings is one identity (dev + ino equal: the claim identity agrees) | a plant `IsMine` comparing `path.StartsWith(Environment.GetFolderPath(UserProfile))` on POSIX ⇒ the aliased run is `foreign-uid` — observed once, reverted (Linux) |
| B13-W | `OnWindows_TheScopeKey_ReadsTheFinalPath_NotTheGivenOne` (#12's C# RED proof on the Windows leg; B.1 § 5's condition) *[2026-11-03]* | Windows only (skip elsewhere with the reason). Premise FIRST: `RUNNER_TEMP` is set and is NOT inside the caller's profile (`Native.FinalPathOfFollowing` of both, `IsStrictlyInside` false; else skip with the reason — the leg exists to run it, so on `windows-latest` a skip is reported as a failed premise in the PR). Two arms. (i) a private root `R` inside the profile (the owned scope); a junction `A` = `RUNNER_TEMP\e5s2b-<8 hex>` → `R` (`DirectoryLinks.CreateOrSkip`); a gone-owner folder `g` + claim planted under `R`, both `WindowsOwnership.OwnedByCaller` (the positive ownership premise: `Scope.IsMine(R\g)` asserted TRUE before the sweep), old; the sweep over `A` with the REAL `Scope.IsMine` and the real probe. (ii) a folder `o` + claim in `RUNNER_TEMP` (outside the profile), both `OwnedByCaller` (so only the PATH key separates); a junction `B` under `R` → the parent of `o`; the sweep over `B`, real keys. The junctions removed in `finally` by their own names | (i) `g` is REMOVED (the final path of `A\g` is inside the profile and the SID is the caller's); (ii) `o` is KEPT with the reason `foreign-uid` and the listed `foreign:` line (its final path is outside the profile), bytes intact. **The mutation** (the Windows throwaway branch, B.3 step 5): `Scope.cs:77` `IsStrictlyInside(entry.FinalPath, profile)` → `IsStrictlyInside(Path.GetFullPath(path), profile)` (the GIVEN path, not the resolved one) ⇒ (i) `g` kept `foreign-uid` (RED, reason quoted), (ii) `o` REMOVED (RED, the dangerous direction). Both arms must be red for THOSE reasons on `windows-latest`; otherwise the condition of B.1 § 5 applies (evidence reported, entry 4.2, the stop) | the mutation above, on `tmp/e5-s2b-red-windows` |

**Node — `src_vs_code/src/test/leftoversSweep.test.mjs`** (`node:test`; runs under `npm test` on the `extension` job):

| # | Test | Fixture | Assertion | RED on commit 1 |
|---|---|---|---|---|
| N-S1 | `the runner sweeps the REAL temp root before it makes anything, with the coai token bound last` | `startOfRunSweep({ log, root: '/r', repo, loadSweep: async () => recordingSweep, sweepOptions: { token: 'mcp', now: 7 } })` | the sweep was called once with `{ ...sweepOptions, repo, tmp: '/r', token: 'coai' }` — `token` equals `'coai'` though the caller said `mcp` (B-B13's arm); `now: 7` passed through | no export `startOfRunSweep` |
| N-S2 | `the reference collects a gone owner's coai folder, keeps a live one, lists a foreign one and a legacy one, and never looks at coai-*` | a private root (`fs.mkdtempSync` under `os.tmpdir()`, removed in `t.after` — this file is outside `src_mcp/tests/Leftovers`; S6's baseline carries it); planted: `testsToDelete-<owner>-coai-run-abc123` + claim of a dead pid (`owned.formatOwner({ ...thisOwner(), pid: 4000000 })`, probe injected `dead`), one with this pid (`live`), one of owner `u1001`, a legacy `testsToDelete-coai-run-1a2b3c` + `host@linux:4000000:17`, `coai-chat-live` with a sentinel, all `utimesSync` 11 min back; `sweepOptions: { probe, mine: () => true }`; `now: Date.now()` | `removed` = the dead one (and its claim is gone); `preserved` names the live one `live`; `listed` has `foreign: folder … — owner u1001` and `legacy: folder testsToDelete-coai-run-1a2b3c — remove with: rm -rf -- …` (on Linux the legacy record is `unknown`: listed, never removed — F22); `coai-chat-live` and its sentinel intact and in no list; on `win32` (real platform) the removed list is empty and `listed` has `unverified:` for the dead one instead (the assertions branch on `process.platform`) | the export is missing |
| N-S3 | `the report is silent when there is nothing, one summary and one line per entry otherwise, and a thrown or unloadable sweep is one line that never stops the run` | injected sweeps: `{}` empty; one with 1 removed, 2 preserved, 1 listed, 1 error; one that throws; a `loadSweep` that rejects | silence; `run-tests: leftovers in /r — removed 1, preserved 2, listed 1` then the listed line, the two `preserved:` lines, the `error:` line, in that order; `run-tests: leftovers sweep not run (boom); the tests still will` for both failures; the function returns in every case | — |
| N-S4 | `the mount missing is one line and exit 2 before anything is made` | `main` with `loadOwned` injected to reject and `exit` injected | the line names `git submodule update --init .agents/conventions`; `exit(2)`; `beginRun` and the spawn never called | — |
| N-S5 | `the switch: the repository says collect; list-only calls no sweep and lists the owner-form and legacy names only` | (a) `readSweepSwitch(repoRoot)` on the real file; (b) `startOfRunSweep({ mode: 'list-only', root, loadSweep: () => { throw } })` over a fake `readdirSync` with an owner-form name, a legacy name, a quarantine, `coai-x`; (c) the table of B9 (c) through an `io` seam | (a) `'collect'`; (b) `loadSweep` never called, three `run-tests: switched-off: …` lines, none for `coai-x`, nothing on disk touched; (c) each value ⇒ `list-only` with the one line, `collect` ⇒ `collect` silently | B-B12 (a) |
| N-S6 | `main loads the mount, sweeps, counts, then makes its folder — in that order` (N5 rewritten) | the text of `run-tests.mjs` from `async function main() {` | matches `/^async function main\(\) \{\s*const owned = await loadOwned\(\);\s*await startOfRunSweep\(\{ owned \}\);\s*reportLeftoversFirst\(\);\s*const folder = beginRun\(\);/` | B-B14 |
| N-S7 | `on win32 the reference lists and changes nothing (F16 interim, Q6) — through the platform seam` | N-S2's dead-owner plant; `sweepOptions: { platform: 'win32', probe, mine: () => true }` on any OS | `removed` empty; `listed` contains an `unverified:` line naming the folder; the folder and claim intact. *[2026-11-03]* An EXTRA beside N-S9, not its substitute (the consultation on B.10 item 3) | — |
| N-S8 | `the same verdicts under the root as given and as resolved` (#12) | a symlinked alias of the private root (skip where links cannot be made); N-S2's dead and live plants | both runs: the dead one removed, the live one `live`, no errors; **on macOS, as row 12 says**: the wrap's `isMine` is TRUE for the root as `os.tmpdir()` returns it and as `fs.realpathSync.native` returns it (`/var/…` vs `/private/var/…`, the premise that they differ asserted), and the sweep over each lists the live plant `live` | — |
| N-S9 | `on windows the REAL wrapper and the PINNED reference keep an otherwise collectible folder, listing it unverified (F16, Q6)` *[2026-11-03]* | Windows only (`process.platform === 'win32'`; elsewhere skip with the reason). NO seams but `root`, `repo`, `log`: the default loader, probe and `isMine`. The private root under `os.tmpdir()` (premise: the mount's `isMine(root)` is true — inside the profile); `testsToDelete-<thisOwnerToken()>-coai-win-abc123` with `sentinel.txt`; the claim = `formatOwner({ ...thisOwner(), pid })` of an EXITED `spawnSync(process.execPath, ['-e', '0'])` child (premises through the mount: `processProbe(pid).state === 'dead'`, `parseOwner(text) !== null`, `ownerState(record, processProbe, { nameOwner, callerOwner, kernelMine: true }) === 'gone'`); folder and claim `utimesSync` 11 min back | the result: `removed` is empty; `listed` has one `unverified: <folder> — the owner SID is not read on Windows (F16)` line; the folder, `sentinel.txt` and the claim intact byte for byte; no quarantine entry in the root; the `log` lines carry the same. On `win32` a failed premise FAILS the test (the leg exists to run it) | the `windows` job's NEW step (B.5) — RED on commit 1 by the missing export; after F16's Fix is pinned this test turns red by design (the signal to rewrite it as a collection test) |

**`scripts/run-lib-tests.test.mjs`** (runs under the runner it tests, through the injected seams — never the real spawn):

| # | Test | Assertion | RED on commit 1 |
|---|---|---|---|
| L1 | `the sweep runs over the real root before node --test is started` | `runLibTests({ loadOwned, sweep, spawn: stub, log, repoRoot, root: privateRoot, mode: 'collect' })` with a gone-owner plant (injected probe, `now + 11 min` through `sweepOptions`): the stub asserts the folder is GONE when it is called; argv is exactly `[process.execPath, ['--test', 'scripts/**/*.test.mjs'], { cwd: repoRoot, stdio: 'inherit' }]` | the module does not exist |
| L2 | `ci.yml runs the lib tests through the runner, never bare` | the text of `.github/workflows/ci.yml`: a step runs `node scripts/run-lib-tests.mjs`; no step's `run:` matches `node --test "scripts/` | the bare command is there |
| L3 | `the child's status is the runner's; a thrown sweep is one line and the child still runs` | spawn stub `{ status: 3 }` ⇒ 3; `status: null` ⇒ 1; a throwing sweep ⇒ one `run-lib-tests: leftovers sweep not run (…)` line and the stub still called | — |
| L4 | `the mount missing is one line and exit 2, nothing spawned` | `loadOwned` rejects ⇒ the submodule line, exit 2, no spawn | — |
| L5 | `the Windows sweep step needs no npm package: the sweep test's import graph is node: modules and repository files only` *[2026-11-03]* (in `src_vs_code/src/test/leftoversSweep.test.mjs` itself, or beside it) | walks the static `import` lines of `leftoversSweep.test.mjs`, `scripts/run-tests.mjs`, `scripts/owned.mjs`, `scripts/startOfRun.mjs` and the mount's `tools/lib/leftovers-sweep.mjs`, `owned.mjs`, `owned-token.mjs`, `git.mjs` (and any file they import): every specifier starts with `node:` or is a relative path; a bare package name is red (the `windows` step runs without `npm ci`, B.1 § 5) | — |

**Must-not-copy rows of S2-B — RED-GREEN-RED** (GREEN = the test passes on the copy; RED = the defect re-planted by hand
once, the test red, the plant reverted; a plant in a copied file also turns the diff check red — expected, recorded; no
plant stays on a pushed branch but the throwaway macOS one):

| # | Test (GREEN) | Plant (RED) | Where RED is seen |
|---|---|---|---|
| 3 | the copied `The_rename_into_quarantine_never_replaces_an_existing_empty_folder_and_draws_another_name`, `A_folder_whose_every_quarantine_name_is_taken_stays_where_it_is`, `A_rename_into_quarantine_that_the_file_system_refuses_keeps_the_folder_with_one_error`; the copied `NativeTests` for `renameat2` and its reserve-then-rename fallback (S3) | (a) B-B4 (`Directory.Move`); (b) Linux: `LinuxRenameExclusively` made to ignore `EINVAL`/`ENOSYS` and return false (the fallback never reached) ⇒ the `NativeTests` fallback case; (c) macOS: `DarwinRenameExclusively` falling back to a plain `rename` on `ENOTSUP` | (a), (b) Linux; (c) the throwaway macOS branch (B.3 step 5) |
| 4 | B10 | the base-36 alphabet | Linux |
| 5 | B11 and the copied `ScopeTests.On_posix_a_fifo_swapped_in_after_the_identity_check_is_unreadable_and_never_blocks` (S3); the Darwin child (`DarwinFifoChild`, S3) | `Scope.ReadClaimText` → `File.ReadAllText` | Linux; the Darwin arm on the throwaway macOS branch |
| 7 | the copied `A_quarantine_whose_renamer_is_no_process_id_is_never_restored_and_never_probed` | `Liveness.AskablePid` → `long.Parse(digits)` | Linux |
| 8 | the copied `An_orphan_claim_whose_folder_sits_in_a_quarantine_is_kept_with_it` | the `quarantined.Contains(…)` line of `SweepOrphanClaim` removed | Linux |
| 10 (sweep half) | the copied `LayoutGateTests.The_real_sweep_asks_for_the_layout_first…` and `…runs_once_the_layout_holds` (S3 copied them; they now read the REGISTERED fixture's `GuardedSweep`); the diff check holds operation A inside `GuardedSweep(DarwinLayout.Require, …)` | `requireLayout();` removed from `GuardedSweep` | Linux (the seam injects the failing layout); the macOS leg runs `The stat layout self-test` first |
| 12 | B13 (with its macOS arm: both root forms `mine`, the sweep's listing and the claim identity agree); N-S8 (the same in Node); B13-W | a profile-path `IsMine` on POSIX (B13's plant) — Linux; **and the root-resolution mutation of the copied `Scope` (`Scope.cs:77`, the given path for the final path) — Windows, B13-W, both arms red for THAT reason, on `tmp/e5-s2b-red-windows`; not red for that reason ⇒ the evidence reported, the parent's entry 4.2, the plan stops (B.1 § 5)** *[2026-11-03]* | Linux; the macOS leg (B13's and N-S8's macOS arms run GREEN there, no plant); `windows-latest` (the throwaway branch) |

**Run:** the four executables through `low-priority.mjs` with a private `TMPDIR`/`TEMP`/`TMP` (S3 brief § 10's `run`
function): `--filter-namespace "CoaiMcp.Tests.Leftovers"` first, then `--filter-class "*RunTemp*" --filter-class
"*RunFolder*"`, then whole; `cd src_vs_code && npm test`; `node scripts/run-lib-tests.mjs`; after a full run of every
suite, `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` in WSL lists only folders of a live run (the DoD's
janitor line; the Windows half of that line waits for P1). The "left in" listing of each private root is empty after
every whole run.

## B.5 — CI: the merge-gate table's rows that concern the sweep (D8 (k); one changed line, and — since 2026-11-03 — one NEW step on `windows`)

| What is proven | Linux | macOS | Windows |
|---|---|---|---|
| the copied sweep (`LeftoversSweepTests`, `LeftoversJanitorTests` — Linux only by its own skip), coai's `StartOfRunSweepTests`, the order attribute on every mcp test, B1/B2 of mcp | `build-test` `Test` (`ci.yml:58`) | `macos`: `Leftovers` (`:305`) then `Test` (`:309`) | `windows`: `Test` (`:376`) |
| the server's registration and order (B1, B2 in `RunFolderTests`) and the attribute on every server test | `:77` | `:313` | `Run folder — Team server` (`:381`, the filtered class) |
| the bench's | `:64` | `:317` | — |
| the ingest (bugs) suite's | `:88` | — | — |
| the extension runner's sweep (N-S1–N-S8; N-S9 skips here) | `extension` `Test` (`:437`) | — | — (no Windows leg for `npm test` as a whole; the sweep file has its own step, next row) |
| the Node sweep on a real Windows runner: the real wrapper, the pinned reference, an otherwise collectible fixture survives, listed `unverified:` (N-S9; Q6 as written) *[2026-11-03]* | — | — | `windows`: NEW `The Node sweep lists and changes nothing on Windows (F16, Q6)` (B.1 § 5), after `Conventions submodule`, before `Build` — a merge gate |
| the C# RED proof of row 12 on Windows (B13-W; a throwaway branch, not a standing step; its GREEN runs in `Test`) | — | — | `windows`: `Test` (`:376`) green; `tmp/e5-s2b-red-windows` red for the named reason, recorded (or the stop of B.1 § 5) |
| the `scripts/lib` runner (L1–L4) and the sweep in front of the lib tests | `build-test` `:196-197` → `run: node scripts/run-lib-tests.mjs` | — | — |

The B2 warning line (the margin) appears in each of the C# cells' logs; the PR copies one per cell. `LeftoversJanitorTests`
needs the mount's `janitor.sh` and `bash` — both on `build-test` (`:29-39`). No `npm ci --prefix .agents/conventions` is
added on macOS or Windows (S6's).

## B.6 — Docs (commit 6)

- `research/module_tests.md`: a new section after the S3 sections (append-only; the dated E5 S3 rows stay),
  **"E5 S2-B — the owner-aware start-of-run sweep in every C# suite and in both Node runners (2026-11-…)"**: the one
  sweep in two languages (the copied `LeftoversSweep` behind the registered fixture; the mounted reference behind the
  wrap), the root it sweeps (`RunTemp.Real` / `REAL_TEMP_ROOT`), the two keys and B.1 § 3's table (what each runner does
  with each name class, the legacy `rm` lines, the Windows list-only interim), the ONE switch and D11's one line, the
  order proof (the attribute, B2, the margins per suite per leg, step 0's probe output), the claim-first race and the
  startup order, the RED/GREEN records of B.3 and the must-not-copy rows, the break-its, the counts reconciled by name
  (the two copied files' facts, B1/B2 ×4, B3–B13, C-N4 removed, the Node tests added and removed), the janitor dry-run
  output, and what is NOT proven here (the Node sweep on a real Windows machine; the janitor on macOS and Windows; S6's
  audit). The stage-A paragraphs stay; the sentence "S2-B replaces it with the copied `LeftoversSweepFixture`" gets a
  dated note *[2026-11-…, S2-B: the fixture was added beside it; `TempDirsAreSwept` stays the count's home]*.
- `research/module_runners.md:627`: a dated note — the extension runner's start of run is now the mounted reference's
  sweep with the token `coai` (listing E2's legacy folders), then the count, then its own (still legacy-named until S4)
  folder; `scripts/run-lib-tests.mjs` fronts the lib tests.
- `todo/README.md`: the spec's row text gains "the S2-B half added 2026-11-03".
- `todo/PLAN_test_leftovers_E5.md`: no text change in this commit (its S2-B boxes are ticked by the merging PR).

## B.7 — The PR description, "Commit 2 — S2-B"

1. The scope in the review gate's form (`local.common.review-gate`): the goal (E5 S2's goal (B), one sentence); what must be
   true (the DoD below); the constraints ("nothing under the product folders; `PanelService.cs` and the characterization
   test unchanged; the copied algorithms unchanged — operations A and B only; the token written once per language; no
   fallback to the machine temp added or removed; the audit not wired").
2. Step 0's probe output and the decision (fixture, or plan B with its reason).
3. Commit 1's RED lines (the fixture-not-found line, B1's, B2's, B3 (a)'s, the Node import failures); commit 2's green
   runs and the B2 margins (Linux locally; each CI cell after the push); break-its B-B1–B-B15, each red line quoted.
4. The must-not-copy RED-GREEN-RED records (rows 3, 4, 5, 7, 8, 10, 12), the throwaway macOS run's link.
5. **D11's rollback procedure** (merge condition (m)), verbatim: "1. `shared/leftovers-sweep.json`: `"startOfRunSweep":
   "collect"` → `"startOfRunSweep": "list-only"` — one line, reviewed, merged alone; every C# suite and both Node runners
   then list and remove nothing; creation and teardown of the run folder are untouched (rehearsed: B-B12, green suites,
   the planted folder survives). 2. `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` on every machine
   that ran the suites, and the CI legs' logs: the owner-form folders and claims that exist. 3. Then revert or repair in
   a NEW PR with its own gates; the listed folders go through the janitor or the next good sweep, never by hand by name."
6. The janitor dry-run listing after a full run (WSL).
7. The D10 inventory delta: the `scripts/lib` command now has a sweep in front (`ci.yml:197`), still no run folder (S4).

## B.8 — What the Opus reviewer checks

1. **The two manifest operations are the ONLY edits of a copied file**, and the diff check is green: `git diff
   <S3 head>..HEAD -- src_mcp/tests_leftovers/*.cs src_mcp/tests/Leftovers/*.cs` touches `LeftoversSweepFixture.cs` on
   exactly the two lines of operations A and B, adds the two copied test files and the three coai files, and nothing else.
2. **Four registrations, four order attributes**: each `RunTempStart.cs` carries both lines; B1 is in all four suites and
   green on every cell of B.5.
3. **The order proof is real**: `StartOfRunOrderAttribute.Before` THROWS (not logs) when `EndedAt` is 0 or later than now;
   `EndedAt` is written in a `finally`; the probe of step 0 is quoted; the margin lines are in the PR for every cell.
4. **The root is `RunTemp.Real`** (operation A) and `StartOfRunSweep.Collect` is the template's five-argument call with the
   real probe and the real `Scope.IsMine` — no seam substituted in the real path.
5. **The switch**: one file, one key, `collect` on `main`, the safe side `list-only`, the one warn line; both languages read
   the same file; B9 and N-S5 green; B-B12 rehearsed and quoted.
6. **The race**: B5 uses `AfterClaim` (not a sleep, not a flag); arms (a), (b), (c); the reason recorded; the startup order
   paragraph of B.1 § 2 is in `module_tests.md`.
7. **Legacy**: no code path of either runner removes a legacy name — `grep -n 'Legacy' src_mcp/tests_leftovers/LeftoversSweep.cs`
   shows only `Keep`/`_listed` under `Relation.Legacy`; the Node wrap passes nothing that changes the reference's legacy
   verdict; B7 and N-S2's legacy arm green; the stage-A legacy COUNT is gone from both runners (C-N4, N7, N10 deleted,
   `RunFolders`/`LegacySentence`/`LEGACY_RUN_FOLDER` gone).
8. **Nothing fails the run**: the fixture's constructor and `StartOfRunSweep.Run` catch nothing new (the fixture already
   turns a throw into one line); the Node `startOfRunSweep` returns on every path; only the mount-missing door exits (2),
   before anything is made (D3).
9. **The token once**: `grep -rn "'coai'" src_vs_code/scripts scripts --include=*.mjs` finds it in `owned.mjs` only (and in
   tests' expected values); `{ ...options, token: REPO_TOKEN }` with the token LAST.
10. **No producer change**: `run-tests.mjs`'s `RUN_PREFIX`, `makeRunTemp`, `removeRunTemp`, `beginRun` and the C# run folder
    are byte-identical to S3's head (S4's work is not pulled in).
11. **The audit is off** (operation B) and S6's brief is told so (a line in the PR's "for S6" list: replace operation B
    with the bounded audit; decide the disposal order against `RunTempFixture`, F29).
12. Every deleted test is named with its reason (C-N4, N7, N10, N8's arms: "pinned the legacy count, replaced by the
    sweep's `legacy:` lines"); no product-sweep test, no product-prefix scan, no `TempDir` test, no invariant test (C-I1,
    C-R1, N-I1, N-R1) was touched.
13. The PR carries B.7 items 1–7.

## B.9 — Points the owner may overrule (non-blocking; each has its answer above)

- **P5** — the sweep in the GATE (before discovery) rather than the assembly fixture, without waiting for the proof to
  fail. Spec: the fixture, proven on every test (B.1 § 1); plan B is written and switches in one step if step 0 says so.
- **P6** — the switch's home: a key in `shared/temp-sweep.json` instead of a new file. Spec: a new file, because
  `temp-sweep.json`'s `why` (stage A decision 1) says "no test sweep deletes by this file", and a switch of a deleting
  sweep inside it would make that sentence false.
- **P7** — delete `TempDirsAreSwept` now, as stage A's P3 said S2-B would. Spec: keep it (B.1 § 6); the fixture is added
  beside it; S6 may rename it when it wires the audit.
- **P8** — the `--import` preload instead of `scripts/run-lib-tests.mjs`. Spec: the runner (one sweep per command; a
  preload is inherited by every `node --test` child through `execArgv`).
- **P9** — drop the `coai-*` count line now that a sweep reports. Spec: keep it (B.1 § 6) — the sweep never looks at
  `coai-*`, so without the line the old pile is invisible again.
- **P10** — an unknown switch value REFUSES the run (exit 78 through the gate) instead of list-only. Spec: list-only with
  one line and a pinned test; a refusal would make a data-file typo stop every suite, and the rollback's own value must
  never be the refusing one.
- **P11** — assert in B8 (a) that the other sweep's removal produces NO report line at all (E5 item 4's words). Spec:
  the template's `replaced` line stands (the template wins where no must-not-copy row names the difference); the
  assertion is "no error, no throw, not in `Removed`".

## B.10 — Found in the plan: wrong, contradictory or impossible (reported, not fixed here)

1. **D4 and S2-B item 3: "the Node reference's sweep collects [a legacy name] only where the kernel owner is the caller AND
   its old-grammar record is `gone` — so a killed old run of either runner is collected by the next extension run of the
   same user."** At the pin (`cc24f9e`) this never happens: on a Linux reader `legacyOwnerState` is ALWAYS `unknown`
   (`owned.mjs:580-587`, no `<ns>` in a legacy record — the same fact the stage-A clean-up recorded); on Windows the sweep
   lists and changes nothing (F16 interim); and E2's claims carry `win32`/`darwin`, which `parseLegacyOwner` does not read.
   The contract's clause is correct and the behaviour is the safe side; the plan's conclusion ("collected by the next
   extension run") and Q3's reasoning are wrong. The only remover of E2's folders is the one-off clean-up. S2-B asserts
   the listing (B7, N-S2) and nothing more.
   *[2026-11-03, the question consultants `08beb732`/`76d09c26`]* **A CHANGE — the parent's entry 4.1** (dew_flow_conventions
   #121). D4, S2-B item 3 and Q3 of the E5 plan carry the dated correction; Q3 is marked decided (item 12).
2. **D11 "a one-line, reviewed change" with two implementations.** The plan does not say where the switch lives; a
   constant per language is two lines. Settled here as the shared file (B.1 § 4). If the owner prefers a per-language
   constant, D11's wording must change to "two lines".
3. **Q6 / D8: "its Windows CI leg asserts that a planted gone-owner folder survives the Node sweep."** There is no Windows
   leg for the extension's tests (`ci.yml:383-437` is `ubuntu-latest`; the `windows` job has no Node tooling for the
   mount). S2-B asserts it through the reference's `platform: 'win32'` seam on Linux (N-S7); a real Windows `npm test` is
   P1's record or a new leg S6 would have to add (with `npm ci --prefix .agents/conventions` on Windows, which S6 adds).
   *[2026-11-03, the question consultants]* **Met AS WRITTEN, not changed:** the `windows` job (which already fetches the
   mount) gets a step that runs `src/test/leftoversSweep.test.mjs` with Node 22; N-S9 exercises the actual wrapper and the
   pinned reference against an otherwise collectible fixture; N-S7 stays as an extra (B.1 § 5, B.4, B.5). The sentence
   "there is no Windows leg" above is history.
4. **S2-B Files: "`src_vs_code/src/test/sweepTemp.test.mjs` → `leftoversSweep.test.mjs`".** `sweepTemp.test.mjs` was
   deleted in S2-A (#940); its survivors are `startOfRun.test.mjs`. The new file is `leftoversSweep.test.mjs` beside it;
   nothing is renamed.
   *[2026-11-03]* Repaired in the E5 plan with a dated note under S2-B's Files (not a change).
5. **The epoch-4 note "S3 brings the sweep's source only" and D9 "the audit's wiring stays in S6" versus "S2-B registers
   `LeftoversSweepFixture`".** Registering the copied fixture registers its DISPOSAL, which is the end-of-run audit, which
   throws on the run folder still in the registry (F29) and whose order against `RunTempFixture`'s disposal xUnit does
   not fix. The plan does not say what S2-B does with the audit half of the fixture. Settled here as operation B (the
   audit off until S6); S6's brief must replace it with a bounded audit and settle the disposal order.
6. **S2-B item 4: "no report line for the other's removal."** The template keeps a folder that vanished between the
   capture and the removal as `preserved: … — replaced` — a report line. The template wins (E5 § *Stages and order*);
   B8 (a) asserts no error, no throw and nothing in `Removed`; P11 records the choice.
7. **Must-not-copy #4 "5 000 draws of the quarantine name".** The drawer is `private static` inside the sweep's private
   `Pass` class; without an edit to the copied file (which D2 would have to list) the draws are reachable only by
   reflection or through real removals. Settled as reflection plus 50 real removals (B10); if reflection is unacceptable,
   D2's manifest must take one visibility operation.
8. **Must-not-copy #12's C# half.** On POSIX `Scope.IsMine` compares uids, never paths; the only path compare of the copy is
   Windows-only (profile + SID), so a `/var`-vs-`/private/var` defect cannot exist in the C# judge. B13 proves symmetry
   under an alias (the scenario as planned) and nothing more; the Node half is N-S8 and S4's `isMine` compare.
   *[2026-11-03, the question consultants]* **Met AS WRITTEN, on a condition:** row 12's macOS assertions are kept exactly
   (B13's and N-S8's macOS arms: both root forms `mine`, the sweep's listing and the claim identity agree), and a C# RED
   proof is ADDED on the Windows leg — B13-W, the root-resolution mutation of the copied `Scope` (`Scope.cs:77`), with the
   `OwnedByCaller` premise first. If the throwaway run cannot show it red for that reason, the coder reports the
   evidence, the main session records item 8 as the parent's entry 4.2, and the plan stops (B.1 § 5). The coder reports
   the evidence either way.
9. **S2-B item 2 "a preload … or a tiny runner (the spec picks one)" and the plan's S4 Files line naming
   `scripts/lib/leftovers-preload.mjs`.** The runner is picked (B.1 § 7, P8); S4's Files list should say
   `scripts/run-lib-tests.mjs` (and add the owned folder and the marker to it).
   *[2026-11-03, the question consultants]* Not a change (S2-B item 2 already allows the runner). The E5 plan's S2-B
   item 2, S4 Files and Q5 carry dated notes reconciling every "preload" to `scripts/run-lib-tests.mjs`.
10. **The DoD line "each C# suite has its own initializer and its first-ordered run-folder test"** is already superseded
    by S3 (no order; the gate is the guarantee; F24 for the proof rule); it still reads as if an order existed.
    *[2026-11-03]* Repaired in the E5 plan's DoD with a dated note (not a change).
11. **D8's table row "server's wiring — Windows: `*RunFolderTests`"** proves the server's sweep registration on Windows
    ONLY because this spec puts B1/B2 into that class; the table does not say so. Recorded in B.5.
12. **Q3 is marked "non-blocking, recommended: keep the copy byte-identical (list only)"** — the copy is no longer
    byte-identical (D2, epoch 4) and the answer stands for a different reason now (the legacy branch of the copy is kept
    as the template has it; nothing coai adds collects a legacy name). Q3 should be marked decided by this spec.
    *[2026-11-03]* Done: Q3 carries the dated "DECIDED" note in the E5 plan (with entry 4.1).

## Definition of Done (S2-B)

- [ ] Step 0's probe recorded; the sweep is the registered `LeftoversSweepFixture` in all four assemblies (or plan B, with
      the reason), with operations A and B in the manifest and the diff check green.
- [ ] The order proof: `StartOfRunOrderAttribute` on all four assemblies, B1 and B2 green in every C# cell of B.5, the
      margin per cell in `module_tests.md`; commit 1's RED lines quoted.
- [ ] The sweep root is `RunTemp.Real` (B3); a fresh run folder survives by age and by liveness (B4); the claim-first race
      scenario (B5) green on the three OSes with the reason recorded; the startup order written down.
- [ ] Every scenario of B.4 RED then GREEN (C#: Linux, macOS, Windows through the legs; Node: Linux); break-its
      B-B1–B-B15 observed red and reverted; must-not-copy rows 3, 4, 5, 7, 8, 10 (sweep half), 12 RED-GREEN-RED recorded
      (the macOS arms on the throwaway branch).
- [ ] *[2026-11-03]* The `windows` job's new step runs `src/test/leftoversSweep.test.mjs` with Node 22 and the mount; N-S9
      green there through the real wrapper and the pinned reference with every premise asserted; N-S7 green on Linux.
- [ ] *[2026-11-03]* Row 12 as written: B13's and N-S8's macOS arms green on the macOS leg; B13-W green on the Windows
      leg; the `Scope.cs:77` mutation observed RED for the path-key reason on `tmp/e5-s2b-red-windows` (both arms, the
      reason strings quoted) — or the evidence reported and the stop of B.1 § 5 recorded.
- [ ] Legacy names: listed with the exact command in C# and in Node, never collected; the stage-A legacy COUNT retired in
      both runners; the `coai-*` count line kept; the invariant tests of stage A untouched and green.
- [ ] The ONE switch `shared/leftovers-sweep.json` at `collect` on `main`, pinned by a test in every suite and in Node;
      list-only removes nothing in both languages; D11's rehearsal (B-B12) and procedure in the PR.
- [ ] The extension runner: `owned.mjs` holds the token once; the mounted sweep with the token bound last; the report
      lines; never the exit code; the mount missing ⇒ one line, exit 2; `main`'s order pinned.
- [ ] `scripts/run-lib-tests.mjs` fronts the lib tests; `ci.yml:197` runs it; L1–L4 green.
- [ ] Concurrent sweeps never fail (B8); the sweep never fails any run (the fixture's line, the Node line).
- [ ] After a full run of every suite, `janitor.sh --token coai --dry-run` (WSL) lists only folders of a live run; every
      private root empty after every whole run.
- [ ] `PanelService.cs`, the characterization test and the product sweep's tests unchanged and green (stage A's A.8 check 2
      repeated).
- [ ] `module_tests.md` and `module_runners.md` updated append-only; `plan-lifecycle.mjs` clean; the PR carries B.7.
- [ ] Nothing of S4 (the Node producer), S5 or S6 (the guard, the audit's wiring, the baseline) is in this commit; the
      commit is never merged alone (D8).
