# PLAN — a round tree and a review tree survive a tracked path longer than Windows' MAX_PATH

> Status: **plan only, nothing implemented yet, 2026-10-16.** Scope: `src_mcp/runners/Worktrees/`
> (`WorktreeManager`, `ReviewTreeRoot`, `SubmodulePopulator`, one new helper), their tests, and two
> docs (`research/module_runners.md`, `research/module_server.md`).
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md), [architecture.md](../research/architecture.md).

## The symptom (measured 2026-10-15)

`review_code` on a branch that tracks a 187-character path failed with
`git worktree add: fatal: Could not reset index file to revision 'HEAD'.` The sentence names the
index, not the path, so it reads like a corrupt repository. Nothing was corrupt: the tree's root plus
that path passed Windows' path limit, and `core.longpaths` is set nowhere on that machine (checked
2026-10-16: `git config --show-origin --get-all core.longpaths` prints nothing, exit 1;
`HKLM\…\FileSystem\LongPathsEnabled = 0`; git 2.55.0.windows.3).

## What the code does today (verified at `origin/main` 2d98f2f8)

| Fact | Where |
|---|---|
| A round tree is `Path.Combine(storageRoot, $"coai-wt-{session}-r{round}-{ShortId()}")`; session and id are 8 hex each | `WorktreeManager.cs:66`, `:120`, `:420`; `PanelService.cs:499` |
| Its root is `%LOCALAPPDATA%\coai-mcp\round-worktrees` unless `COAI_ROUND_WORKTREES` is set | `WorktreeManager.cs:89`, `ReviewTreeRoot.cs:83-86`, `PanelSettings.cs:971-973` |
| The add: `git worktree add --detach <path> <sha>`, no `-c` of any kind | `WorktreeManager.cs:124`, launched by `Git(...)` at `:481-485` |
| A failed add throws `WorktreeException("worktree add", FatalLine(stderr))` | `WorktreeManager.cs:128` |
| `FatalLine` keeps ONLY the first `fatal:` line — git's `error: unable to create file <path>: Filename too long` is dropped | `WorktreeManager.cs:476-479` |
| `review_code` returns that message verbatim as the tool's error | `RoundEngine.cs:349-351`, `:663-666` |
| Submodules of the tree are filled by `git submodule update --init` run IN the tree | `SubmodulePopulator.cs:66-79`, called at `WorktreeManager.cs:139` and `ReviewWorktrees.cs:413` |
| The second `worktree add` (a person's review tree, `--tree-at`): root `%LOCALAPPDATA%\coai-mcp\review-worktrees`, name `coai-review-<8>-<12>` | `ReviewWorktrees.cs:346`, `ReviewTreeRoot.cs:26`, `:46`, `:93-94` |
| Every review-tree git call — add, `status` clean checks, unlock, remove, prune, mount discovery — goes through ONE wrapper | `ReviewTreeRoot.GitAsync`, `ReviewTreeRoot.cs:159-175`; callers include `ReviewWorktrees.cs:151`, `:256`, `:301`, `:308-309`, `:348`, `:365-373`, `ReviewTreeKeeper.cs:156`, `:274-276`, `:312-314`, `:409-411`, `SubmoduleMounts.cs:23` |
| Round-tree removal: `worktree remove -f -f`, then a .NET delete fallback | `WorktreeManager.cs:218-222` |

The brief said `…\coai-mcp\worktrees\coai-<session>-r<n>-<id>` at ~71 characters and blamed git's
`.lock` suffix. The code and the probe say otherwise: the leaf is `round-worktrees`, the prefix
`coai-wt-`, the tree path **82** characters on this machine, and the failing write is the working-tree
FILE itself (the index lives in the main repository's `.git/worktrees/<name>/`, a short path).

## The probe (2026-10-16, this machine, a throwaway repository in the session scratchpad)

Fixture commits were built with plumbing (`hash-object`, `update-index --cacheinfo`, `commit-tree`), so
no long path ever had to exist in the source checkout. Probe deleted by exact path afterwards.

| # | What | Result |
|---|---|---|
| P1 | `worktree add`, file at absolute 255…259 UTF-16 units, no flag | exit 0 |
| P2 | same, 260, 261, 262 | exit 128: `warning: unable to access '<dir>/.gitattributes': Filename too long` · `error: unable to create file <rel>: Filename too long` · `fatal: Could not reset index file to revision 'HEAD'.` — the reported symptom exactly |
| P3 | same, 300 (a DIRECTORY over the limit) | exit 128: `fatal: cannot create directory at '<rel>': Filename too long` |
| P4 | P2/P3 with `git -c core.longpaths=true worktree add …` | exit 0, file present — **honoured per command**, with `LongPathsEnabled = 0` |
| P5 | the unit: 259 UTF-16 units = 260 UTF-8 bytes (one `é`) / 260 units = 261 bytes | accepted / refused — the limit counts **UTF-16 code units** (`string.Length`), not bytes |
| P6 | a failed add (P2) | git removes the directory and the registration itself — nothing left |
| P7 | `git status --porcelain` in a tree holding a 270-unit file, no flag | ` M <that file>` — a CLEAN tree reads as DIRTY; with the flag: empty |
| P8 | `worktree remove --force` on that tree, no flag | exit 255 `failed to delete '<tree>': Filename too long`; registration GONE, directory LEFT. With the flag: exit 0, gone |
| P9 | `submodule update --init` in the tree, the submodule holding a 270-unit path, no flag | exit 128 `cannot create directory … Filename too long`; with `-c core.longpaths=true` on that one command: exit 0, file present — the `-c` reaches the nested clone and checkout |
| P10 | P9's parent `status --ignore-submodules=none`, no flag | ` M m` — the populated mount reads dirty; with the flag: empty |
| P11 | one path COMPONENT of 256 units, WITH the flag | exit 128 `error: unable to create file a/<256>.txt: Invalid argument` + the same `fatal:` — the flag lifts the total limit, not NTFS's 255-unit component limit |
| P12 | an explicit `-c core.longpaths=false` | fails like P2 whatever the system/global config says — what makes the tests' control portable |

Arithmetic for this machine: round tree 82 units → longest tracked path that works today is
`259 - 82 - 1 = 176`; the 187-unit path lands at 270. Review tree 88 → 170.

## What must be true when this is done

1. On Windows, every git command coai runs to create, populate, inspect or remove a tree **it made**
   carries `-c core.longpaths=true`: `WorktreeManager` (all calls), `ReviewTreeRoot.GitAsync` (all
   calls), `SubmodulePopulator.OneAsync`.
2. A tracked path whose absolute length in the tree exceeds 259 UTF-16 units checks out, statuses clean,
   populates in a submodule, and is removed — round tree and review tree alike.
3. A ROUND-tree `worktree add` (the `review_code` path, where the defect was met) that still fails
   NAMES the path git could not write, its length, and the reason git gave; for `Filename too long` it
   also names the limit (259 without long-path support), says coai already asked for long paths, and
   names `COAI_ROUND_WORKTREES` as the way to a shorter root. `Could not reset index file` is never the
   whole message again. A REVIEW-tree add keeps its wire word `git_failed` (plan round 1, codex: the
   requirement is narrowed to what D4 delivers, rather than promising it for both).
4. On Linux and macOS the argv of every one of those git calls is byte-for-byte what it is today.
5. `COAI_ROUND_WORKTREES` is documented as the operator's escape hatch, with its LOCAL-path rule.

## Design

**D1 — one helper, three seams.** New `runners/Worktrees/TreeGit.cs`:
`internal static IReadOnlyList<string> Argv(IReadOnlyList<string> args)` → `["-c",
"core.longpaths=true", ..args]` when `OperatingSystem.IsWindows()`, else `args` unchanged. The three
existing launch points call it: `WorktreeManager.Git` (`:481`), `ReviewTreeRoot.GitAsync` (`:170`),
`SubmodulePopulator.OneAsync` (`:66`, before its own two `-c`). Reuse-first move 1 (widen the existing
wrappers); no call site is touched, so a future git call in these classes inherits it. Applied to EVERY
call in those wrappers, not only the "creating" ones, because P7/P8/P10 show the inspecting and
removing ones fail too — a review tree that reads dirty can never be removed from the panel
(`ReviewTreeKeeper.cs:312-328`), and a failed remove drops the registration and leaves the files, the
exact state `ReviewTreeKeeper.cs:278-281` says it avoids. `-c` is per command — never a config write.
The same wrappers also run a few read-only calls in the PERSON's repository (`rev-parse` at
`WorktreeManager.cs:93`, `--git-common-dir` at `ReviewWorktrees.cs:151`, `worktree list` at `:373` and
`ReviewTreeKeeper.cs:156`); they carry the flag too, harmlessly — it changes nothing a read of a short
path does. Not extended to the OTHER launchers that work in a person's checkout (`ContextAssembler`,
`FilesystemInvariant`): their long-path behaviour is the person's own config, outside this defect.

**D2 — Windows only, explicitly.** `core.longpaths` is a Git-for-Windows key; elsewhere git ignores it,
but "ignored" is a claim about every git a Linux user might run, and (4) wants the argv unchanged, which
is checkable. The helper maps one platform; there is no `else` that inherits a Windows value.

**D3 — do not shorten the tree name. Measured gain, rejected.** The most that can go: the leaf
`round-worktrees` → `rw` (13) and `coai-wt-` → a 2-char prefix (6) = **19 units**, lifting the round
tree's ceiling from 176 to 195. That passes the 187 case on THIS machine only: a `C:\Users\first.last`
profile is +6, a 200-unit path still fails, and the flag removes the 259 limit entirely (P4). The cost is
real: the prefix is one of two guards keeping the sweep off a person's tree (`WorktreeManager.cs:61-62`,
`:357-364`), the session prefix is how a round finds its own leftovers (`:118`), and a new root orphans
every tree under the old one — `module_server.md:6343` already records older builds' trees left
unswept under `{DataDir}/worktrees`. The review tree's `coai-review-<digest>-<sha12>` is its identity
(`ReviewTreeRoot.cs:93-94`). Nothing renamed.

**D4 — the failure names the path, not the index.** New pure `WorktreeFailure.Sentence(string stderr,
string treePath)` in `runners/Worktrees/`, used at `WorktreeManager.cs:128` in place of the bare
`FatalLine`. It reads git's own lines — `error: unable to create file <rel>: <reason>` and
`fatal: cannot create directory at '<rel>': <reason>` (P2, P3, P11): the reason is the text after the
LAST `": "` on the line (git's reasons hold no `": "`; a Windows file name cannot hold `:`), the path is
what lies between the known prefix and that separator, one pair of surrounding `'` removed; when
several such lines appear, the first names the path. It says, e.g.:
`git worktree add: could not write '<tree>\<rel>' (270 characters): Filename too long. Windows allows
259 without long-path support; coai already passed core.longpaths=true, so this git did not honour it —
set COAI_ROUND_WORKTREES to a shorter local folder, or shorten the path. git: fatal: Could not reset
index file to revision 'HEAD'.` A reason other than `Filename too long` (P11's `Invalid argument`) still
names the path and git's reason, without the limit clause and without the root advice (a shorter root
does not cure a 256-unit component). No such line → today's `FatalLine`, so every
other failure reads exactly as now (`WorktreeManagerTests.cs:349` keeps passing). Lengths are
`string.Length` — P5's unit. The 259 is ONE named constant (`WindowsMaxPathUnits`), cited to P1/P2,
used only on Windows; on other platforms the clause is omitted rather than filled with an unmeasured
number (platform-limits §1.4). `<rel>` is git's forward-slash relative path joined to the tree with
`Path.Combine` after `/`→`\` on Windows. The review tree keeps its wire word `git_failed`
(`ReviewWorktrees.cs:394-398`): a new reason word is a contract change for the extension, and D1 removes
the cause; out of scope below.

**D5 — no pre-flight refusal.** Counting `ls-tree -r` lengths before the add would refuse checkouts the
flag makes work (P4), on every round, for every repository. The add is the probe; D4 explains its
failure.

**D6 — the escape hatch, documented.** `COAI_ROUND_WORKTREES` (`PanelSettings.cs:358-372`, `:971-973`) is
already honoured and already says LOCAL. Its paragraph in `research/module_server.md:6343` gains one
sentence (a shorter local root, e.g. `C:\coai-rt`, buys ceiling unit-for-unit, for a git that ignores
`core.longpaths`), the same sentence goes in `research/module_runners.md`'s worktree section, and D4's
message names it — the place an operator meets the problem. It moves the ROUND root only; review trees
have their own `COAI_REVIEW_ROOT` (`ReviewTreeRoot.cs:49`, `:62-77`, absolute paths only), and the docs
say so rather than implying one variable moves both (question consultant, 2026-10-16).

## Out of scope (named so nobody assumes it was done)

- A review-tree reason word that carries the path (an extension contract change; `git_failed` stays).
- Reviewer CLIs reading a >259 path inside the tree: their own runtimes, not measured here.
- The E1 fixtures guard `NoFixturePathIsLongerThan160Characters`
  (`ACallerTranscriptFixtureCarriesNoTextTests.cs:95`, `:662-667`): it keeps OUR fixtures short so this
  repository's own trees stay under the limit on a deep checkout. Unchanged — it guards a different
  thing (our repository) and stays useful for a git that ignores the flag.
- NTFS's 255-unit component limit (P11): reported by D4, not worked around.
- git run in a tree by ANYONE ELSE — the person's editor and language server in a review tree, their own
  `git status`, a reviewer CLI that runs git in a round tree — has no flag and will see a >259 file as
  ` M` (P7), a change nobody made. Curing that needs a per-worktree config (`extensions.worktreeConfig`
  written into the person's repository), which this plan does not write. Said, not solved.
- A submodule that still fails WITH the flag (e.g. a 256-unit component inside it) gets no D4 sentence:
  `SubmodulePopulator` never throws by design (`SubmodulePopulator.cs:44-56`), and a checkout that
  stopped part-way leaves a mount that is not empty, so `EmptyMounts` does not name it either.

**Growth surfaces:** none. No file, table, directory or process is added; trees, markers and sweeps are
unchanged.

## Build order

1. RED: T1 and T3 below, run on Windows against unchanged code; record both failure messages.
2. `TreeGit.Argv` + its three call sites (D1, D2). T1, T2 green.
3. `WorktreeFailure.Sentence` + `WorktreeManager.cs:128` (D4). T3, T4 green.
4. Break-it B1, B2 — record red, restore, record green.
5. Docs (D6), whole suite on Windows, `plan-lifecycle.mjs`, then promote this plan.

## Test plan

Real git, plumbing-built fixtures, temp roots the tests own; every Windows-only test starts with
`Assert.SkipUnless(OperatingSystem.IsWindows(), "MAX_PATH and core.longpaths are Windows' — Linux and
macOS have no 259-unit limit to cross")`, the repository's existing pattern (`AConversationPlaceTests.cs:91`).
Each deep fixture's RELATIVE length is computed from the test's own root so the absolute path is ≥ 300
units on any runner, with ≤ 40-unit components, and the test asserts that length before using it.

- **T1** `ARoundTreeChecksOutAPathLongerThanWindowsAllows` (`WorktreeManagerTests`, Windows): a commit
  with one deep file → `AddAsync` succeeds, the file is there, `git -c core.longpaths=true status
  --porcelain` in it is empty, and the lease's dispose leaves no directory. **Control in the same test:**
  `git -c core.longpaths=false worktree add` of that commit FAILS (P12) — proof the fixture needs the
  flag whatever the runner's system config says (whether `windows-latest` sets `core.longpaths`
  system-wide was NOT checked; the control makes the FIXTURE independent of it). Because a runner
  with a system-wide `core.longpaths` would pass T1/T2 with or without the fix, T1 and T2 run the real
  launcher through a RECORDING wrapper (the `WatchedLauncher` pattern, `AFeatureIsReviewedEndToEndTests.cs:706`)
  and assert that every git request they saw starts `-c core.longpaths=true`, and that the round
  tree's `worktree remove` itself exited 0 — not merely that the .NET fallback deleted the directory.
- **T2** `AReviewTreeWithALongPathIsMadeCleanAndGivenBack` (`AReviewTreeTests`, Windows): `PrepareAsync`
  makes it, a second call reuses it, `ReviewTreeKeeper` removes it as `removed` (not `dirty` — P7) and
  the directory is gone. A submodule variant with the deep file inside the mount covers P9/P10.
- **T3** `AnAddThatStillFailsNamesThePathAndTheLimit` (`WorktreeManagerTests`, Windows): a commit with a
  256-unit COMPONENT (P11 — fails even with the flag) → `WorktreeException` message contains the full
  tree path of that file, its length, `Invalid argument`, and NOT only the `fatal:` line.
- **T4** `WorktreeFailureTests` (pure, every OS): the three stderr shapes P2, P3, P11 copied verbatim
  from the probe → the sentence names the joined path, its `string.Length`, git's reason, and for
  `Filename too long` the limit and `COAI_ROUND_WORKTREES`; stderr with no such line → exactly
  `FatalLine`; a fixture path carrying `é` asserts characters ≠ UTF-8 bytes and that the count is the
  characters.
- **T5** `TreeGitTests` (every OS): on Windows `Argv` starts `-c core.longpaths=true`; elsewhere it is
  sequence-equal to the input (the Linux/macOS jobs check D2/(4)).
- **B1 break-it:** `TreeGit.Argv` returns `args` unchanged → T1 and T2 red with git's `Filename too
  long` (and T1's control still red — it tests the fixture, not the code). Restore → green.
- **B2 break-it:** `:128` back to `FatalLine` → T3 red naming only `Could not reset index file`.
- Whole suite on Windows and in CI; any fake launcher that matches git argv by POSITION is found by
  that run and fixed to match by content.

## Definition of Done

- [ ] Every git call in `WorktreeManager`, `ReviewTreeRoot.GitAsync` and `SubmodulePopulator` carries
      `-c core.longpaths=true` on Windows through `TreeGit.Argv`, and no other launcher gains it.
- [ ] Linux/macOS argv unchanged (T5 on their CI legs).
- [ ] T1–T5 green; the step-1 RED messages and B1/B2 red→green recorded in the PR.
- [ ] A failing add names the path, its length in UTF-16 units, git's reason, and (for `Filename too
      long`) the 259 limit and `COAI_ROUND_WORKTREES`; the limit is one named constant.
- [ ] No tree name, prefix or root changed (D3).
- [ ] `module_server.md` and `module_runners.md` document the escape hatch; `todo/README.md` row moved
      on promotion.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean.

## Stories

Three stories, no epics, in build order; each needs the one before. The gate runs ONCE over the whole branch
(one `review_code`), not per story. Cornerstones: 1 of 1 — S1.

**S1 — every git call on a tree coai made carries `core.longpaths` (CORNERSTONE).** Expensive if wrong: a
seam missed for `status` or `remove` leaves a review tree that reads dirty and can never be removed (P7/P8),
and a runner with system-wide `core.longpaths` hides the miss — so S1 owns the whole lifecycle of BOTH trees.
Changes: new `TreeGit.cs` (D1, D2); `WorktreeManager.Git`, `ReviewTreeRoot.GitAsync` and
`SubmodulePopulator.OneAsync` call `TreeGit.Argv`; no other launcher. Tests: step-1 RED for T1 first; T1 (with
its `longpaths=false` control and the recording-launcher assertion), T2 (with its submodule variant), T5; B1
red→green; positional fake launchers fixed to match by content. Done when: T1, T2, T5 green on Windows, T5 on
Linux/macOS, B1 recorded, suite green, DoD rows 1-2 ticked. Who: Fable spec, Sonnet code, Opus review.

**S2 — a round-tree add that still fails names the path, its length and git's reason.** Changes: new pure
`WorktreeFailure.Sentence` (D4; one constant `WindowsMaxPathUnits`) replaces the bare `FatalLine` at
`WorktreeManager.cs:128`; `git_failed` stays. Tests: step-1 RED for T3 recorded first (after S1: without the
flag P11's `Invalid argument` is never reached); T3; T4 (P2/P3/P11 verbatim, no match → exactly `FatalLine`,
the `é` case); B2 red→green; `WorktreeManagerTests.cs:349` still passes. Done when: T3 green on Windows,
T4 on every OS, B2 recorded, DoD row 4 ticked. Who: Opus scenarios+brief, Sonnet code, Opus review.

**S3 — the escape hatch is documented and the plan becomes a record.** Changes (D6, no code): one sentence
each in `research/module_server.md:6343` and `research/module_runners.md`'s worktree section — a shorter LOCAL
root buys ceiling unit-for-unit for a git that ignores the flag; `COAI_ROUND_WORKTREES` moves the ROUND root
only, `COAI_REVIEW_ROOT` the review root. Tests: none new; whole suite on Windows and in CI over the finished
branch; the PR body carries the step-1 RED messages and B1/B2 red→green. Done when: D3 confirmed (nothing
renamed), DoD rows 3 and 5-7 ticked, `plan-lifecycle.mjs` clean, `review_code` at `proceed`, plan promoted
with its deviations (promote-plan skill). Who: Opus scenarios+brief, Sonnet code, Opus review.
