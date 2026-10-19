# PLAN — Vibe reads a folder of repositories

> Status: **built, in review, 2026-10-19: stories E1.1–E1.4 and decision D9 are on branch `feat/vibe-folder-of-repos` (E1.1–E1.3 and D9
> on 2026-10-18; E1.4 — the product-flow test S15, the docs and the help — on 2026-10-19). The epic-1 code gate ran on 2026-10-19
> (verdict good_enough; its entry is the last section, "Code gate (2026-10-19)", with the fixes G-A to G-P it caused); the promotion
> to `research/` follows the live row.** Scope: `src_mcp/src/Server/Consultation/ConsultantPlaces.cs`
> (the copy loop of a Vibe consultant row), a new repository walk beside it, `QuestionRowLaunch.cs` (the skip notes on the
> row), `PanelService.ScratchPrefixes` (the crash sweep), their tests, `research/module_runners.md`, `research/module_server.md`,
> `research/module_tests.md`, the help sentence for a Vibe disk row in `helpContent.ts` and its four translations
> (`helpDe/Es/Ru/Uk.ts`). Spec corrections C1–C9 of the story split were accepted 2026-10-18 and are folded into D1–D8 below. D9
> (linked worktrees) and the review fixes R1–R9 (section "Review fixes", end of the file) were added the same day.
>
> Related docs: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (D6 — Vibe reads a copy coai makes through Git),
> [module_runners.md](../research/module_runners.md).

## 1. The symptom

The owner's question-consultant row "Projects on this disk" on Vibe is refused before it starts, on both sides:

> the folder d:\rsd is not inside a Git work tree — this runtime reads a copy coai makes through Git (D6 of
> todo/PLAN_vibe_and_kimi_runtimes.md), and a plain folder cannot be copied that way yet; grant a folder of a repository

`ConsultantPlaces.OutsideAGitTreeAsync` (`src_mcp/src/Server/Consultation/ConsultantPlaces.cs:387-401`) refuses any granted
root that `git rev-parse --is-inside-work-tree` does not call a work tree. The owner's roots are PARENT folders of many
repositories: `d:\rsd` on Windows, `~/git` on WSL (20 checkouts within two levels, measured 2026-10-18). Every other
runtime reads those roots directly under its own confinement; Vibe cannot, because it has no trustworthy read-only mode (a
committed `.vibe/hooks.toml` runs commands in a trusted folder; plan mode is not read-only), so D6 gives it only a clean Git copy.

## 2. The goal

A Vibe disk row whose granted root is a parent folder answers "projects on this disk" questions, with exactly D6's
confinement: Vibe reads only coai-made copies of Git repositories, each through the existing snapshot of the LIVE tree
(`SnapshotSource.Live`, `src_mcp/runners/Snapshots/SnapshotSource.cs:55-62`: tracked files plus untracked files git does not
ignore — the same listing a work-tree root gets today, unchanged by this plan), with `SnapshotExclusions.Default` stripping
`.git`, `.vibe`, `.agents/skills`, `.agents/agents`, `.mcp.json`, `.kimi-code`, `AGENTS.md` (`src_mcp/runners/Snapshots/SnapshotOptions.cs:62`),
in a folder coai owns, disposed after the turn. The owner's own words: "the copy is simply a worktree" — the snapshot already
is that, minus the configuration a checkout would carry.

## 3. Decisions

- **D1 — Expand, do not widen.** In `OneShotAsync`'s source list (`ConsultantPlaces.cs:293`), a granted root that is NOT inside a
  work tree is expanded into the Git work trees beneath it before copying; a root that IS a work tree is copied as today. Each
  found repository goes through `CopyOfAsync` (git validation, link refusal, snapshot) — the same method, widened only by an
  optional link root (D5). Nothing else reads the
  parent folder, and Vibe never sees a path of it. `OutsideAGitTreeAsync` (`:387-401`) folds two answers into one today;
  it is split, because only "not inside a work tree" expands — a folder that does not exist stays a refusal (C3). The two
  existing assertions that expect the work-tree sentence for a plain folder (`AVibeQuestionRowReadsACopyOfItsRootsTests.cs:160`,
  `:187`) change to D3's "holds no Git repository" sentence by design (C4).
- **D2 — What counts as a repository beneath a root.**
  - **Depth** counts directory levels below the granted root: its direct children are depth 1, their children depth 2. The walk
    looks at depths 1 and 2 only. The root itself is depth 0 and is never a candidate (it is not a work tree, or D1 would not expand it).
  - A **candidate** is a folder holding `.git` as a real directory, or as a regular FILE (a worktree's or a submodule's top). A
    `.git` that is a LINK (symlink or junction) does not make a candidate — the D6 link rule.
  - The walk does **not** descend into a candidate (its nested repositories and submodules are the repository's own business; the
    snapshot lists what git lists).
  - The walk skips hidden folders (`.`-prefixed — and, on Windows, a folder whose attributes carry Hidden or System: `$RECYCLE.BIN`,
    `System Volume Information` when a drive root is granted; `FolderEntry.Hidden`, never set elsewhere), `node_modules`, `bin`, `obj`,
    and any link (never followed, never listed). A link
    to a FOLDER (a cloud provider's on-demand folder looks like one) is named in ONE note per root — its count and its first five
    names; a link to a file stays silent (review fix R8).
  - A folder the walk cannot list (access denied, vanished, I/O error) is **skipped with a note** naming it; the walk goes on.
  - At depth 2 the walk only asks whether `.git` exists (`FolderListings.GitEntryOf`, one lookup of one name) — it never lists a
    depth-2 folder, so a folder of a million files costs one lookup (R8). The root and a depth-1 folder ARE listed, and a listing is
    bounded (G-E): at most `RepositoryWalk.MaxEntries` = 100,000 entries are read as they stream, only what the walk needs is kept
    (a folder, a link to a folder, a `.git` file), and a bigger folder refuses the row — "the folder X holds more than 100000
    entries — grant the project folders themselves".
  - A candidate git confirms must be the **top** of a work tree: `--is-inside-work-tree` is `true` AND `--show-cdup` is empty. A
    folder with an empty or foreign `.git` inside another repository is "inside" that work tree and is skipped with a note that says
    so (R1, G-H);
    `--show-toplevel` strings are never compared (git changes slashes, case and resolves links). A work-tree root granted
    DIRECTLY keeps the plain "inside a work tree" check — a sub-folder of a repository is still copied as before.
  - Depth 2 covers the measured layouts (`~/git/<repo>`, `d:\rsd\<repo>` and one grouping level).
- **D3 — Bounds.**
  - The walk examines at most **10,000 folders** per root; past that the row is refused: "the folder <root> holds more than
    10000 folders within two levels — grant the project folders themselves". (50 repositories alone would not bound the walk:
    a parent can hold thousands of folders and no repository.)
  - At most **50 CONFIRMED repositories** per ROW (git-validated, de-duplicated — a broken candidate never counts, and a linked
    worktree left out by D9 does not count); beyond that the row is refused with a sentence naming the bound and the same cure:
    "the granted folders hold more than 50 Git repositories, more than a Vibe disk row copies — grant the project folders
    themselves". It names the row, never the last root (R3). Work trees granted directly count toward the same bound, and are
    counted before any parent is walked, whatever their place in the list.
  - A root that cannot be listed at all (denied, vanished between the existence check and the listing) is refused by name:
    "the folder X could not be listed (reason) — grant a folder coai can read" — never reported as a folder with no repository
    (R2). Every refusal of the walk keeps the notes made before it (the too-many-folders refusal and a root whose children were
    all unreadable included), and the place hands them to the row.
  - A root with NO repository beneath it is refused: "the folder <root> holds no Git repository within two levels — Vibe reads
    copies coai makes through Git".
  - The walk AND the copies run inside the row's existing budget token (`copying` in `OneShotAsync`, the #811 checkpoint rule);
    past it the place is refused naming how many were copied of how many. The "of" counts copy targets planned: every
    directly granted work-tree root counts 1 and is counted FIRST, before any parent is walked (whatever its place in the list);
    an expanded root then counts its repositories. A budget that runs out BEFORE the first copy (during a walk, or while git
    confirms candidates) does not say "0 of 0 copied", which reads as "nothing found"; it says what was going on (G-F): "…the
    budget ran out while confirming K candidates with git, before anything was copied — grant fewer or smaller folders, or give
    the row more time (COAI_QCONSULT_ROW_MINUTES)" (or "…while reading the granted folders…" when no candidate was reached yet).
    `AConversationPlaceTests.cs:136` ("1 of 2" over two direct roots) stays green (C6).
- **D4 — Candidate validation versus copy failure (the one boundary that must not blur).**
  - **Discovery skips, with a note:** a candidate `git rev-parse --is-inside-work-tree` does not confirm (a broken `.git` file,
    a moved worktree), and a folder the walk cannot list. One broken checkout never costs the other nineteen. The note names the
    cause (G-H, `RepositoryFacts.Said`): git failed or gave no answer — "{path} was skipped: git did not confirm it ({reason})" — or
    git ran and the folder is inside another repository's work tree — "{path} was skipped: it is inside another repository's work
    tree, not the top of its own".
  - **Copy refuses the whole row, as today:** once a confirmed repository is handed to `CopyOfAsync`, any refusal (a link found,
    a snapshot refusal) refuses the row and disposes every copy made (`Unmade`). Cancellation and the budget keep their
    existing handling. No catch-and-skip is put around `CopyOfAsync`.
  - The skip notes are carried on the place (`ConsultantPlace.Notes`) AND still sent to the `note` sink. Today that sink
    reaches the server LOG only (`QuestionRowLaunch.cs:89`), and a row's `note` is written only by `TurnsAsync` and `Ended`;
    so the row carries the notes into its `note` field, redacted through `Redaction.SafeText`, on the answered and the
    refused arm alike (story E1.3).
- **D5 — The link boundary reaches up to the grant.** `LinkUpToTheWorkTree` (`:368-382`) stops at the first folder holding
  `.git`, which for an expanded repository is the repository itself (C2). A new `LinkUpTo(repository, root)` checks every
  folder from the repository up to and including the GRANTED root, immediately before its copy — a grouping folder that became
  a link between the walk and the copy is refused like any other link. The granted root itself is link-checked BEFORE the
  walk (a junction at the root would otherwise be walked through, since the walk only refuses links beneath it). The remaining
  check-to-open race is the one `SnapshotCopier` already documents (`src_mcp/runners/Snapshots/SnapshotCopier.cs:150-154`);
  this plan claims no more than that.
- **D6 — Layout: one subfolder per repository.** Each copy is made with `Maker.MakeAsync(source, Path.Combine(tree, <grant
  index>, <relative path>))`, so it lands at `<tree>/<n>/<relative path>/snapshot-<id>/…`. The intermediate folders are made with
  `Directory.CreateDirectory`; the relative path is split on the walk's own segments (never a string from git). The `snapshot-<id>`
  leaf makes collisions impossible even across two grants; the grant index keeps two grants' `group/repo` apart for a reader.
  De-duplication is by full path (the platform's path comparison) across the whole row, directly granted work-tree roots
  included. A DIRECT grant always wins, wherever it stands in the list (`OneShotTally.Planned` is filled with the direct grants first);
  among parents the first in source order wins (a repository granted directly and found again under a parent grant is copied once,
  as the direct grant, whichever grant came first).
  `ConsultantPlace.Owned.Roots` lists every copy, as today. Destination case and length hazards inside a copy are the existing
  `SnapshotPaths` rules. The layout adds `<n>/<group>/<repo>/` to every copied path (C8): a path that grows too long is a
  refusal with a sentence (`SnapshotCopier.Failed`, `:225-226`, "the path is too long") and so refuses the row by D4's copy
  side — never a silent miss. Vibe's own interpreter is not long-path aware (`VibePathBudget.cs:14-16`); the help and the
  module doc say both.
- **D7 — Order and determinism.** Repositories are copied in ordinal order of their relative path, so the same root gives the
  same copy order, the same notes and the same refusal sentence on every run.
- **D8 — A killed process.** A question row's copies live in the row's own scratch, `coai-question-<id>`
  (`QuestionRowLaunch.cs:80`, `:83`), removed in `finally`. A killed server strands it for ever TODAY (C1):
  `PanelService.ScratchPrefixes` (`:1364-1365`) sweeps only `coai-answers-*`, `coai-repair-*`, `coai-noworkspace-*`, although
  `shared/temp-sweep.json` says the product sweeps its never-swept prefixes "itself, on its own clock". This plan adds
  `"coai-question-*"` to `ScratchPrefixes`: `PruneOldAnswerDirs` (`PanelService.cs:1404`) then removes it after six hours on
  the consultation beat. **Age alone is not enough** (cadence consult 5f02ed0f, verified): `RowBudget` is
  `IntVar(COAI_QCONSULT_ROW_MINUTES)` with no upper bound (`QuestionConsultSettings.cs:149`), so a row set to 480 minutes
  outlives six hours, and a deep write does not refresh the scratch folder's own timestamp. So the row HOLDS a lock file
  in its scratch for its whole life (`HeldFile.Take(Path.Combine(scratch, ".coai-question.lock"))`, exclusive on Linux
  through `LinuxHeldFile`), and the sweep removes a `coai-question-*` folder only when it is older than six hours AND it
  can take that lock itself — the `VibeHomes.cs:189` pattern (`NewestWrite >= cutoff || lock not taken → keep`), reused,
  not copied. S10 pins both sides, red first.
- **D9 — Linked worktrees (added 2026-10-18; the consultants agreed, the lead decided).** The owner's real root holds about 400
  candidates, 392 of them agent worktrees under one folder, so without this the row is refused by D3's bound — and a worktree
  is a full second copy of a project whose main checkout is already in the row.
  - **One git call per candidate** (`RepositoryFacts`): `git rev-parse --is-inside-work-tree --show-cdup --absolute-git-dir
    --path-format=absolute --git-common-dir`. It answers D2's "top of a work tree" question (R1) and this one.
  - **A candidate is a LINKED worktree when its absolute git dir differs from its common dir.** Both are compared as identity
    keys (`RepositoryIdentity.KeyOf`): the full path, the trailing separator trimmed, case-blind on Windows and exact elsewhere —
    the comparison `ConversationSnapshots.cs:24-25` uses. `RepositoryIdentity.Normalised` was widened with a comparison argument
    (its old form lowercases and swaps slashes, which is wrong on Linux); it was not copied. **The text of a `.git` file is never
    the authority**: a submodule checkout holds the same kind of file, and a submodule inside a linked worktree writes a gitdir
    that merely contains `/worktrees/` (`src_vs_code/src/projectIdentity.ts` documents the trap; S18 mirrors its scenarios).
    An ordinary checkout and a submodule have the two dirs equal, so they are never linked. A **main** checkout is a top whose git
    dir is KNOWN and equals its common dir (`IsMain`, G-K): a common dir alone, with no git dir, is no identity and never makes a main.
  - **Selection.** A linked worktree found beneath a granted parent is left out when a confirmed MAIN checkout — the top of a
    work tree, not linked, identity known — with the same common-dir identity is copied in the SAME row, found under any parent
    or granted directly. A linked worktree with no such main stays eligible, and so does one whose identity is uncertain (git
    failed for the main, so the main is not confirmed). A worktree granted DIRECTLY is always copied, and one that is both
    granted and found is copied once, as the direct grant.
  - **Order is the contract.** Every candidate of every parent is validated first, then the linked worktrees are selected out,
    then the bound counts what is left, then the copies are made — so the bound is the row's and a refusal comes before anything
    is made. Validation stops early only when the bound is certainly passed by repositories no selection can remove.
    **Accepted behaviour (code gate, L):** that early stop never changes Owned versus refused, but it can change WHICH refusal
    sentence and which notes appear — parent 1 with 51 repositories and parent 2 a junction says "more than 50" instead of parent 2's
    link sentence, and the skip notes and the worktree summary of later candidates are not made. Both are refusals of the same row, and
    the cure ("grant the project folders themselves") is the same; the code is not changed.
  - **One summary note** for the row: "skipped {n} linked worktrees whose main checkouts are copied — their branches and
    uncommitted files were not read; grant a worktree directly to include it" (singular for one). The same sentence reaches the
    PROMPT of a Vibe row, where the prompt names the copies (`ConsultantPlace.Limitations`, `QuestionPromptInput.Limitations`,
    `CopiedRoots`), so the model knows what its copies do not hold.
  - **Measured 2026-10-18 on the owner's real root, read-only:** 405 candidates, 405 confirmed by git, 392 linked worktrees left
    out, 13 selected; the 405 git calls took about 34 s one after another (the walk itself 46 ms). Counts only — no path of the
    owner's disk is written down here. The calls are sequential and the 34 s is spent before Vibe starts, inside the row's
    budget; confirming a few candidates at a time is the first thing to do if a root grows.

## 4. Scenarios (each red is the message the test fails with before the fix)

- S1 *Given* a root that is a parent of two repositories, *when* a Vibe disk row is placed, *then* Owned with two copies under
  the tree, each holding its files and none of `.git`/`.vibe`/`AGENTS.md`. Red: `"the folder … is not inside a Git work tree"`.
- S2 *Given* a repository at depth 2 and one at depth 3, *then* only the depth-2 one is copied.
- S3 *Given* a hidden folder, `node_modules`, and a LINK to a repository beneath the root, *then* none of them is copied.
- S4 *Given* a repository with a nested repository and a submodule, *then* one copy (the walk does not descend into a repo).
- S5 *Given* 51 repositories, *then* refused with the count and the cure; *given* none, *then* refused with D3's sentence;
  *given* more than 10,000 folders, *then* refused with the folder-count sentence.
- S6 *Given* a root that IS a work tree, *then* exactly today's behaviour (one copy) — a regression guard.
- S7 *Given* the budget runs out after k of n copies, *then* refused naming k of n, and every made copy is disposed;
  *given* the budget runs out DURING the walk, *then* refused, nothing made.
- S8 *Given* a committed `.vibe/hooks.toml` in a found repository, *then* the copy holds no `.vibe` (the exclusion applies per copy).
- S9 *Given* a folder whose `.git` is a FILE pointing at a live worktree, *then* it is copied; *given* a `.git` that is a
  SYMLINK, *then* it is not a candidate.
- S10 *Given* a stranded `coai-question-*` folder holding copies, older than six hours, *then* the consultation beat's sweep
  removes it (reuse `AScratchSweepRunsOnTheConsultationBeatTests`'s pattern; red first — D8). *Given* the same folder aged
  seven hours but with its lock HELD (a live row), *then* it survives the sweep, and is removed once the lock is released.
- S11 *Given* a candidate whose `.git` file points nowhere, and an unreadable folder, *then* both skipped with a note each, and
  the other repositories copied. The broken one says "{path} was skipped: git did not confirm it (git failed or gave no answer)";
  a folder with an empty `.git` inside another repository says "{path} was skipped: it is inside another repository's work
  tree, not the top of its own" (G-H).
- S12 *Given* two repositories where the SECOND copy is refused (a committed mode-120000 link INSIDE it that git lists, the
  pattern of `AVibeQuestionRowReadsACopyOfItsRootsTests.cs:388-399`; a link at its top would only hide it from the walk — C5),
  *then* the row is refused and the first copy disposed — the copy-failure side of D4.
- S13 *Given* a grouping folder replaced by a link after the walk and before the copy, *then* the row is refused (D5).
- S14 *Given* two grants that both hold `group/repo`, and two overlapping grants holding the same repository, *then* distinct
  folders for the first, one copy for the second (D6).
- S15 **The product flow:** a real Vibe question-consultant row (the played-CLI pattern of
  `src_mcp/tests/AVibeQuestionRowReadsACopyOfItsRootsTests.cs`) granted a parent of two repositories, one carrying
  `.vibe/hooks.toml` → the launch stands in the copy tree, sees both repositories' files and no `.vibe`, the prompt names no
  original path — in either spelling: the Windows path and, on the WSL2 arm, its `/mnt/<drive>/…` form (G-P) — and the copies are
  gone after the turn.
- S16 *Given* a main checkout and three linked worktrees of it under one granted parent, *then* one copy (the main) and one
  summary note ("skipped 3 linked worktrees…"); the prompt carries the same sentence (D9). Red: four copies, no note.
- S17 *Given* a linked worktree whose main checkout is OUTSIDE the grant, *then* it is copied and nothing is noted. (Guard: green
  from the start.) *Given* the main's own git is broken, so it is not confirmed, *then* the worktree is kept — an uncertain
  identity keeps it.
- S18 *Given* a checkout whose `.git` is a FILE but whose git dir is its common dir (`git init --separate-git-dir`; a submodule's
  top; a submodule INSIDE a linked worktree), *then* it is never treated as a linked worktree — the facts say so, not the file's
  text. (Guard: green from the start; `RepositoryFactsTests` runs real git for all three.)
- S19 *Given* a linked worktree granted DIRECTLY beside its main checkout's parent, *then* it is copied; *given* the same
  worktree granted directly AND found under the parent, *then* it is copied once and not counted as skipped. (Guards.)

## 5. Test plan

- The walk: unit tests over a directory seam (entries carry kind: folder / file / link / unreadable), S2–S5, S9, S11's walk arm —
  no disk.
- The place: scenario tests over temporary repositories made with `git init` in an owned `testsToDelete-` folder (the existing
  snapshot tests' pattern): S1, S6–S8, S11–S14.
- S15 through the real service with a played CLI; S10 a sweep guard.
- Windows and Linux CI; red-first per scenario (S6, S8, S15 are guards: stated green from the start). Run as MTP executables,
  never `dotnet test`. New tests are added to `research/module_tests.md`.

## 6. Build order

1. The repository walk (pure over a directory seam) with S2–S5's discovery arms, S9, S11's walk arm.
2. The expansion in `OneShotAsync`: candidate validation and notes, the link chain to the grant, the per-repository subfolder,
   de-duplication (S1, S6–S8, S11–S14).
3. The product flow S15, the sweep guard S10; docs: `research/module_runners.md` (the D6 consultant copy now expands a parent
   folder), `research/module_tests.md`, the Vibe disk-row help sentence.

## Definition of Done

- [x] S1–S19 green, each seen red first (guards stated: S6, S8, S15, S17–S19); the existing consultant/snapshot suites green —
  the whole `CoaiMcp.Tests` run on 2026-10-19: 11206 tests, 0 failed, 56 skipped (Windows/POSIX arms).
- [ ] A Vibe disk row on the owner's real root / `~/git` is Owned, live, with the copies disposed after the turn. The product flow is
  pinned by S15 with a played CLI, and the sizes are measured read-only (E1.4 outcome below); a live row with a real Vibe (a paid turn)
  has not run, so this stays open until it does.
- [x] `research/module_runners.md`, `research/module_server.md`, `research/module_tests.md` and the help (five languages) updated.
- [ ] This plan promoted when shipped (`/promote-plan`, after the code gate).

## Epics

One epic, E1 — the expansion (ordinary; the confinement is D6's, unchanged, and every copy goes through the existing snapshot).

## Stories (E1)

> Written 2026-10-18 from the code at `ced2ef768` plus this plan's uncommitted folds. Four stories, built in order. Each
> leaves the suite green. One cornerstone: E1.2. Every `file.cs:line` below was read, not guessed. The *Spec corrections*
> at the end list where the plan does not match the code; nothing in sections 1–6 was changed.

### Where the `note` sink goes today (D4's last bullet, settled)

- `ConsultantPlaces` is built twice. For a question row: `QuestionRowLaunch.cs:89` —
  `new(string.Empty, string.Empty, launcher, note => log.Information("question row: {Note}", note))`. For a consultant
  check: `ConsultantCheck.cs:156` — `new(string.Empty, string.Empty, launcher, warn)`.
- The same sink is handed to `ReviewSnapshots` as its `note` (`ConsultantPlaces.cs:108`), which says a retry
  (`ReviewSnapshots.cs:156`) and a directory that would not delete (`:423`).
- The answer row's `Note` field (`QuestionConsultRecord.cs:85`) is written in two places only: `QuestionRowLaunch.TurnsAsync`
  (`:196` starts it empty; `:220`, `:223` take an `AnsweringTurn`'s note — the api row's source turns) and `Ended` (`:370`,
  the usage-not-captured text). Neither reads the sink.
- **So: the sink reaches the server log only. A skip note does NOT reach the row.** E1.2 puts the notes on the place
  (`ConsultantPlace.Notes`), and E1.3 carries them into the row's `Note`. The sink keeps getting them too, so the log and
  the check's warnings stay as they are.

### E1.1 — The repository walk (pure, over a listing seam)

**Owns:** D2 (all bullets), D3's folder bound and its "no candidate" sentence (the 50-repository bound is E1.2's, over
confirmed repositories), D7 (the order). Scenarios S2, S3 (the walk's arm), S4 (the walk's arm), S5a–c, S9, S11's walk arm.

**Files (new):**
- `src_mcp/src/Server/Consultation/FolderListing.cs` — the seam.
- `src_mcp/src/Server/Consultation/RepositoryWalk.cs` — the walk. Pure: no git, no `IProcessLauncher`.
- `src_mcp/tests/RepositoryWalkTests.cs`.

**Seam and types:**
```csharp
public enum EntryKind { Folder, File, Link }
public sealed record FolderEntry(string Name, EntryKind Kind);
public abstract record FolderListing            // closed union, like SnapshotOutcome
{
    public sealed record Listed(IReadOnlyList<FolderEntry> Entries) : FolderListing;
    public sealed record Unreadable(string Reason) : FolderListing;
}
public static class FolderListings
{
    /// The real one: DirectoryInfo.EnumerateFileSystemInfos; ReparsePoint → Link (a symlink on Linux, a symlink or a
    /// junction on Windows — the rule TreeWalk.cs:34 of wsl_care states); DirectoryInfo → Folder; else File.
    /// IOException / UnauthorizedAccessException → Unreadable(e.Message).
    public static FolderListing Read(string path);
}
public sealed record FoundRepository(string Path, IReadOnlyList<string> Segments);   // Segments: the walk's own names below the root, e.g. ["group","repo"]
public abstract record WalkOutcome
{
    public sealed record Found(IReadOnlyList<FoundRepository> Repositories, IReadOnlyList<string> Notes) : WalkOutcome;
    public sealed record Refused(string Reason) : WalkOutcome;
}
internal static class RepositoryWalk
{
    public const int MaxDepth = 2;
    public const int MaxFolders = 10_000;
    public const int MaxRepositories = 50;
    public static WalkOutcome Find(string root, Func<string, FolderListing> list, CancellationToken ct);
}
```

**Rules the walk follows (so the tests have one reading):**
- The root is depth 0, listed first, never a candidate. Before every listing: `ct.ThrowIfCancellationRequested()`.
- Each listing's entries are sorted ordinally by name before they are visited — the order and the notes are then the same
  on every run (D7). `Found.Repositories` is sorted ordinally by `string.Join('/', Segments)`.
- A folder is a **candidate** when its listing holds an entry named `.git` of kind `Folder` or `File`. An entry `.git` of
  kind `Link` does not make a candidate; the folder is then an ordinary folder (descended if depth allows, no note — a
  link is skipped silently like every other link, D2).
- A candidate is added and NOT descended (S4).
- A non-candidate at depth < `MaxDepth` is descended; at depth `MaxDepth` it is not (S2).
- Skipped without a note: a `Link` entry; a folder whose name starts with `.`; `node_modules`, `bin`, `obj`
  (`StringComparison.OrdinalIgnoreCase` for these three names; `.git` is compared ordinally).
- `Unreadable` → note `"{path} was skipped: it could not be listed ({reason})"`; the walk goes on (S11).
- Every folder listed counts (the root included). When the count would pass `MaxFolders`, the walk stops and the outcome
  is `Refused("the folder {root} holds more than 10000 folders within two levels — grant the project folders themselves")`.
- After the walk: no CANDIDATE at all →
  `Refused("the folder {root} holds no Git repository within two levels — Vibe reads copies coai makes through Git")`.
- The walk does NOT apply the 50-repository bound (cadence consult 5f02ed0f, verified): a candidate is not yet a
  repository, so "50 good ones plus one broken `.git` file" must not refuse the row. The bound counts CONFIRMED
  repositories and lives in E1.2. `RepositoryWalk` keeps `MaxRepositories` only as the shared constant.
- The walk does not ask git. Whether a candidate IS a work tree is E1.2's question (D4's discovery side).

**Red-first tests** (`RepositoryWalkTests`, over a `Dictionary<string, FolderListing>` seam — no disk — and one small test
of `FolderListings.Read` over a `TempDir.For("testsToDelete-walk-")` holding a folder, a file and a link made with
`SnapshotTestKit.LinkNow`). The coder first adds the types with `Find` returning `new Found([], [])`, sees each red, then
implements:
- S2 `ARepositoryAtDepthTwo_IsFound_OneAtDepthThree_IsNot` — red: `Expected found.Repositories to contain 1 item(s), but found 0`.
- S3 `AHiddenFolder_NodeModules_AndALink_AreNeverEntered` — the seam records which paths were listed; red:
  `Expected listed not to contain ".hidden"…` is NOT the right red (nothing is listed by the stub). Red instead:
  `Expected found.Repositories to contain 1 item(s), but found 0` (the one plain repository beside them). Then the
  listed-paths assertion guards the skip once the walk exists.
- S4 `ACandidateIsNotDescended_SoItsNestedRepositoryAndSubmoduleAreNotFound` — red: `…to contain 1 item(s), but found 0`.
- S5a `FiftyOneCandidates_AreAllFound_TheBoundIsNotTheWalks` — the walk returns all 51 (the bound is E1.2's, over confirmed
  repositories); red: `Expected found.Repositories to contain 51 item(s), but found 0`.
- S5b `NoRepository_IsRefusedWithTheGitSentence` — same red shape; the assertion also checks `holds no Git repository within two levels`.
- S5c `TenThousandAndOneFolders_AreRefused_AndTheWalkStops` (101 folders at depth 1 × 100 children, from the seam) — same red
  shape; a second assertion: the seam was asked fewer than 10 002 times.
- S9 `AGitFile_MakesACandidate_AGitLink_DoesNot` — red: `…to contain 1 item(s), but found 0`.
- S11 `AnUnreadableFolder_IsSkippedWithANote_AndTheOthersAreFound` — red: `Expected found.Notes to contain 1 item(s), but found 0`.
- D7 `TheOrderIsOrdinalByRelativePath_WhateverTheListingOrder` — red: `…to contain 3 item(s), but found 0`.
- The budget `ACancelledToken_StopsTheWalkWithOperationCanceledException` — red: `Expected a <OperationCanceledException> to be thrown, but no exception was thrown`.

**Done when:** every test above is green; `FolderListings.Read` is the only line that touches the disk; the walk file is
under 300 lines; `research/module_tests.md` gets its row in E1.4 (not here — E1.4 owns the docs).

### E1.2 — The expansion in `OneShotAsync` (CORNERSTONE)

This is the cornerstone because its mistakes break D6's confinement (a copy made through a link, a path of the parent
folder reaching Vibe) or blur the one boundary D4 draws: a candidate git does not confirm is SKIPPED, a copy that is
refused REFUSES THE ROW. Every other story reads what this one produces.

**Owns:** D1, D3's last bullet (the budget's counting), D4 (both sides; the notes land on the place), D5, D6, D7's "same
refusal sentence". Scenarios S1, S6 (guard), S7, S8 (guard), S11's place arm, S12, S13, S14.

**Files:**
- `src_mcp/src/Server/Consultation/ConsultantPlaces.cs`:
  - `ConsultantPlace` (`:18-82`): add `public virtual IReadOnlyList<string> Notes => [];`. `Owned` (`:59`) takes a fifth
    argument `IReadOnlyList<string> notes` and overrides `Notes`; `Refused` gets a second constructor
    `Refused(string reason, IReadOnlyList<string> notes)`. The two existing `Owned` callers (`:252`, `:271`) pass `[]`.
  - `OneShotAsync` (`:293-314`): unchanged shape; the `made` list becomes a small tally (below); `Owned` gets the notes;
    `Unmade` gets them too.
  - `CopiedAsync` (`:317-337`): per source — `Directory.Exists` false → return the existing "does not exist" sentence;
    `IsAWorkTreeAsync` true → `CopyOfAsync` as today (S6); else → `ExpandedAsync`.
  - `OutsideAGitTreeAsync` (`:387-401`): split into `Missing(source)` (the `:391` sentence) and
    `IsAWorkTreeAsync(source, ct)` (the `:394-397` git call). The `:399-400` sentence is no longer reachable from a question row
    (a plain folder is now walked and refused with D3's sentence); keep it only if the check (`ConsultantCheck.cs:232`,
    always a work tree) still needs a text — otherwise delete it with its test assertions (see the two tests below).
  - `Unmade` (`:340-345`): unchanged; note that a `ReviewSnapshot.Dispose` removes `snapshot-<id>` only — the empty
    `<n>/<segments>` folders above it stay and go with the row's scratch (`QuestionRowLaunch.cs:134`). Tests assert on
    `snapshot-*` folders, not on an empty `tree`.
  - `OutOfBudget` (`:347-349`): same sentence; the counts come from the tally.
- `src_mcp/tests/AVibeQuestionRowReadsACopyOfItsRootsTests.cs:160` and `:187`: both assert `"not inside a Git work tree"` for
  a plain folder with one file. After this story that folder is walked and holds no repository, so the sentence is D3's.
  Change both to `"holds no Git repository within two levels"` — the scenario is S5's "given none", through the service.
- `src_mcp/tests/AConversationPlaceTests.cs:114-139` (the only direct `OneShotAsync` test today) keeps passing: two direct
  work-tree roots, "1 of 2", `tree` empty after the refusal.
- New: `src_mcp/tests/AVibeDiskRowExpandsAParentFolderTests.cs` — place-level, `ConsultantPlaces.OneShotAsync` directly
  (the `AConversationPlaceTests.Places()` shape, `:47`), repositories made with `git init` under
  `TempDir.For("testsToDelete-vibe-parent-")` (the `RepositoryAsync` helper at `:141-151`, copied with the prefix; the
  `testsToDelete-` prefix is `.agents/conventions/common/test-leftovers.md`'s rule).

**Seams and private shape inside `ConsultantPlaces`:**
```csharp
/// The walk's listing — a test's seam for an unreadable folder (S11) and for a budget that runs out during the walk (S7).
internal Func<string, FolderListing> Listing { get; set; } = FolderListings.Read;
/// Called with the repository's path right before its link chain is checked — a test's seam for S13 (the shape of SnapshotOptions.BeforeCopyOf).
internal Action<string> BeforeCopyOf { get; set; } = static _ => { };

private sealed class Tally                       // replaces the bare List<ReviewSnapshot> made
{
    public List<ReviewSnapshot> Made { get; } = [];
    public List<string> Notes { get; } = [];
    public int Found { get; set; }               // copy targets known so far: +1 when a work-tree root is reached, +n (CONFIRMED, not yet copied) when a root's candidates are validated
    public HashSet<string> Copied { get; } = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);  // the comparer ConversationSnapshots.cs:24-25 uses
}
private async Task<string> ExpandedAsync(int index, string root, string tree, Tally tally, CancellationToken copying);
private static string LinkUpTo(string repository, string root);   // the first link among repository, its parents, up to and INCLUDING root; else empty
```

**`ExpandedAsync`, in order:**
1. `OwnedPaths.IsLinkIfThere(root)` → return the existing link sentence (`:364`'s shape, naming the root). The walk never
   follows a link, so a root that IS one must be refused here, not walked (D5 reaches the grant).
2. `RepositoryWalk.Find(Path.GetFullPath(root), Listing, copying)` → `Refused` → return its sentence (D3). `Found` → add
   its notes to `tally.Notes` and say each through `note(...)` (the log keeps them).
3. **Validate first, then copy** (cadence consult 5f02ed0f, verified — so the "of" in the budget sentence is the real total
   and the bound counts confirmed repositories). For each `FoundRepository` in order: `IsAWorkTreeAsync(repo.Path)` false →
   note `"{repo.Path} was skipped: it holds a .git, but git does not confirm it as a work tree"` (D4 discovery side; S11 — split
   in two by the code gate, G-H: "git did not confirm it ({reason})" and "it is inside another repository's work tree, not the top of
   its own") and
   continue; already in `tally.Copied` or already confirmed in this row → continue (D6 de-duplication, S14); else add it to
   the row's confirmed list. Each confirmation adds 1 to `tally.Found` at once, so a budget that runs out DURING validation
   says "0 of k" with k = confirmed so far.
4. More than `RepositoryWalk.MaxRepositories` confirmed (count the row's whole confirmed list, stop validating at the 51st)
   → return `"the folder {root} holds more than 50 Git repositories within two levels, more than a Vibe disk row copies —
   grant the project folders themselves"`. NONE confirmed → return D3's "holds no Git repository within two levels"
   sentence; the skip notes stay on the place (the refused arm carries them, E1.3).
5. For each confirmed repository in order: `BeforeCopyOf(repo.Path)`; `CopyOfAsync(repo.Path, destination, copying, root)`
   — the SAME method a work-tree root goes through, so D1's promise holds. `CopyOfAsync` gains an optional `linkRoot`
   argument: empty (a work-tree root) keeps today's `LinkUpToTheWorkTree`; non-empty uses `LinkUpTo(repo.Path, linkRoot)`
   (D5, S13 — the row is refused). Its git check is repeated there (cheap, and it closes the walk→copy window).
   destination = `Path.Combine([tree, index.ToString(CultureInfo.InvariantCulture), .. repo.Segments])`. `Made` →
   `tally.Made.Add`, `tally.Copied.Add`; `copying.ThrowIfCancellationRequested()`; `Refused` → return its reason (D4 copy
   side, S12 — no catch-and-skip). `ReviewSnapshots.MakeAsync` creates the destination folder itself
   (`ReviewSnapshots.cs:175`); no extra `CreateDirectory`.
6. Return empty.

A directly granted work-tree root also goes into `tally.Copied` and counts `tally.Found++`, so a repository granted
directly AND found under a parent grant is copied once — a direct grant wins wherever it stands in the list (D6, as built).

**Red-first tests** (`AVibeDiskRowExpandsAParentFolderTests`; `Places()` with a real `ProcessLauncher`, budget
`TimeSpan.FromSeconds(60)` unless the test is about the budget):
- S1 `AParentOfTwoRepositories_IsOwnedWithTwoCopies_EachWithoutItsGit` — red:
  `Expected place.Refusal to be empty, but found "the folder … is not inside a Git work tree — this runtime reads a copy coai makes through Git …"`.
  Green asserts: two `Roots`, each `…/tree/0/<repo>/snapshot-<8 hex>`, each holding the repository's file and no `.git`,
  `.vibe`, `AGENTS.md`.
- S6 `AWorkTreeRoot_IsCopiedAsToday_AtTreeSnapshot` — a guard, green from the start: one root `…/tree/snapshot-<id>`, no
  `<n>` folder. Said so in the summary.
- S7a `TheBudgetRunsOutInTheSecondOfThreeCopies_RefusesNamingOneOfThree_AndLeavesNoSnapshot` (the slow-copy `Maker`
  of `AConversationPlaceTests.cs:121-131` over a parent of `a-fast`, `b-slow`, `c-fast`) — red: `Expected place.Refusal to contain "1 of 3"`
  (today the sentence is the work-tree refusal; a per-candidate counter would say "1 of 2" — this test is what catches it). Green also asserts no `snapshot-*` folder anywhere under `tree`
  (`Directory.EnumerateDirectories(tree, "snapshot-*", SearchOption.AllDirectories)` empty) and `budget.Held == 0`.
- S7b `TheBudgetRunsOutDuringTheWalk_RefusesWithNothingMade` — `Listing` sleeps 2 s on its first call, budget 1 s — red:
  `Expected place.Refusal to contain "budget"`; green: "0 of 0" in the sentence, no `snapshot-*` folder.
- S8 `ACommittedVibeHooksFile_IsNotInTheCopy` — a guard of the snapshot's exclusions (`SnapshotOptions.cs:62`); green from
  the start once S1 is; said so.
- S11 `ABrokenGitFile_AndAnUnreadableFolder_AreSkippedWithANoteEach_AndTheOthersCopied` — a folder holding a `.git` FILE
  with `gitdir: /nowhere`, plus `Listing` answering `Unreadable` for one path — red: `Expected place.Notes to contain 2 item(s), but found 0`
  (the property does not exist yet: the coder adds `Notes => []` first, sees this red, then implements).
- S12 `TheSecondRepositorysCopyIsRefused_RefusesTheRow_AndDisposesTheFirst` — the second repository commits a symlink entry
  (mode 120000, the three git commands of `AVibeQuestionRowReadsACopyOfItsRootsTests.cs:390-392`) — red:
  `Expected place.Refusal to contain "escape", but found ""`… is wrong before the story (the refusal is the work-tree one);
  the right red is `Expected place.Refusal to contain "escape"` with the work-tree sentence shown. Green: refused, no
  `snapshot-*` folder under `tree`.
- S13 `AGroupingFolderReplacedByALinkBetweenTheWalkAndTheCopy_RefusesTheRow` — `BeforeCopyOf` moves `group` aside and makes
  a link at its name with `DirectoryLink.MakeAsync` — red: `Expected place.Refusal to contain "link", but found ""` (without
  `LinkUpTo`, git works through the junction and the copy succeeds). Windows and Linux both: `DirectoryLink` has both arms.
- S13b `AGrantedRootThatIsALink_IsRefusedBeforeTheWalk` — the root is a `DirectoryLink` to a parent of one repository — red:
  `Expected place.Refusal to contain "link"` (without step 1 the walk lists through it and the copy succeeds).
- S11b `AParentWithOnlyABrokenRepository_IsRefusedWithTheNoneSentence_AndKeepsItsNote` — red: `Expected place.Refusal to
  contain "holds no Git repository"`, but found "" (a skip-everything loop would return Owned with zero copies).
- S5d `FiftyGoodRepositoriesAndOneBrokenGitFile_AreOwnedWithFiftyCopies` — slow (50 inits); the cadence consult's case;
  red: the work-tree sentence. Merge it with the D3/D7 sentence test below if the suite's time matters (51 good → refused).
- S14a `TwoGrantsEachHoldingGroupRepo_GiveTwoCopies_UnderDifferentGrantIndexes` — red: the work-tree sentence; green:
  roots under `tree/0/group/repo/…` and `tree/1/group/repo/…`.
- S14b `TwoOverlappingGrants_CopyTheSharedRepositoryOnce` — grants `parent` and `parent/group` — red: the work-tree
  sentence; green: exactly one root.
- D3/D7 `TheRefusalSentence_IsTheSameOnTwoRuns` over a parent with 51 empty `git init` repositories — slow (51 inits ≈ 10 s);
  mark it with the suite's slow trait if one exists, else keep it (it is the only full count test with git).

**Done when:** all of the above green; `AConversationPlaceTests`, `AVibeQuestionRowReadsACopyOfItsRootsTests` (with the two
assertions changed), `ConsultantCheckTests` green; `ConsultantPlaces.cs` stays under 800 lines (it is 403 now — if the
expansion pushes it past ~550, move the expansion out; **never a `partial` used to duck the limit** — the 2026-10-18 review
made it `ParentExpansion.cs`, with `RepositoryCopier.cs` as the one copy method both a direct root and a found repository go
through, `OneShotTally.cs` and `GrantedPaths.cs`; `ConsultantPlaces.cs` is 450 lines); no `catch` around `CopyOfAsync` or
`Maker.MakeAsync`.

### E1.3 — The skip notes reach the row's `note`

**Owns:** D4's last bullet (the path to the person). Scenario: S11's product arm (the person sees what was skipped).

**Files:**
- `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs`:
  - `:114-120`: the refused arm becomes `Ended(start, RowOutcomes.Refused, place.Refusal, started.Elapsed, Usage.None, PlaceNotes(place))`;
    the answered arm passes `PlaceNotes(place)` into `TurnsAsync`.
  - `TurnsAsync` (`:191`): a new parameter `string seed`; `:196` becomes `var note = seed;`. Every later `Joined(note, …)`
    then carries the seed.
  - New `private static string PlaceNotes(ConsultantPlace place) => place.Notes.Count == 0 ? string.Empty : Redaction.SafeText(string.Join("; ", place.Notes), Redaction.DetailLimit);`
    — redacted like `Reason` (`:365`), because a note names folders on the person's disk and is written down.
- New: `src_mcp/tests/AVibeDiskRowSaysWhatItSkippedTests.cs` — the service flow of
  `AVibeQuestionRowReadsACopyOfItsRootsTests` (its `ServiceFor` / `AskAsync` / `TheRow` helpers, `:66-103`, copied — or, better,
  lifted into a small shared fixture `VibeQuestionRowFixture` in `src_mcp/tests/` that both files use; the lift is
  reuse-first's step 2.2 and is this story's one refactor), the `VibeFlowHost.Enter(mode, …)` theory of `:110-131` so the
  `Wsl2` arm runs on Linux CI.

**Red-first tests:**
- `AParentWithOneGoodAndOneBrokenRepository_Answers_AndTheRowsNoteNamesTheSkippedOne` — red:
  `Expected row.GetProperty("note").GetString() to contain "was skipped", but found <empty string>` (E1.2 has the note on the
  place; nothing carries it yet).
- `AParentWithOnlyABrokenRepository_IsRefused_AndTheRowsNoteStillNamesIt` — red: same shape on the refused arm.
- `TheNoteIsRedacted` — a folder name holding a token-shaped string (`ghp_` + 36 chars) — red:
  `Expected … not to contain "ghp_…"`; green through `Redaction.SafeText`.

**Done when:** both arms carry the notes; an api row's `Note` (the source turns) is unchanged — `QuestionFanOutTests` and the
api-row scenario tests green; no other caller of `TurnsAsync` exists (it is private, one call at `:120`).

### E1.4 — The product flow, the sweep, the docs and the help

**Owns:** D8 (C1 accepted, plus the lock of the cadence consult), S10 (both sides), S15, the docs and the help sentence, the plan's
promotion.

**Files:**
- `src_mcp/tests/AVibeQuestionRowReadsACopyOfItsRootsTests.cs` (or the fixture E1.3 lifted): S15 as a
  `[Theory]` over `VibeFlowHost.Windows` / `VibeFlowHost.Wsl2` (`:110-112`).
- `src_mcp/src/Server/PanelService.cs:1364-1365` — C1 ACCEPTED: `ScratchPrefixes` gains `"coai-question-*"`, and
  `PruneOldAnswerDirs` (`:1404`) skips a `coai-question-*` folder whose `.coai-question.lock` it cannot take (D8; extract
  the `VibeHomes.cs:189` keep-test into one shared helper if it is private there — reuse-first step 2.2).
- `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs` — the row takes `HeldFile.Take` on `.coai-question.lock` right
  after `MakeScratch` (`:83`) and releases it in the same `finally` that removes the scratch, BEFORE the delete.
  `shared/temp-sweep.json` is unchanged (`coai-question-` stays `neverSwept` for the TEST sweep; the product sweeps it on
  its own six-hour clock, as it does `coai-answers-`).
- `src_mcp/tests/AScratchSweepRunsOnTheConsultationBeatTests.cs` — S10 as a second test beside `:43-66`, over
  `Directory.CreateTempSubdirectory("coai-question-")`.
- `research/module_runners.md` — a new `##` section after `:3182` (the Vibe consultant) or at the end: "A Vibe disk row
  expands a parent folder into its repositories (2026-10-18, PLAN_vibe_reads_a_folder_of_repositories.md)" with the walk's
  rules, the layout, the two D4 sides, a flowchart — and D9 (the one git call, the linked-worktree rule, the order: validate,
  select, count, copy) and the review fixes R1–R9 with their measured counts.
- `research/module_server.md:7362-7372` (the question row's one-shot copies bullet), `:7405` (the mermaid node
  `OneShotAsync(row folder, granted roots) — a copy per root; no Git work tree: refused` → "…a parent folder expanded into
  its repositories; none found: refused"), the table at `:7413-7420` (the walk's file, the new tests).
- `research/module_tests.md` — rows for `RepositoryWalkTests`, `AVibeDiskRowExpandsAParentFolderTests`,
  `AVibeDiskRowSaysWhatItSkippedTests`, the S15 theory, the S10 test, each with its red.
- `src_vs_code/src/helpContent.ts:335` — the sentence "…and a folder outside a Git repository is refused for it." becomes
  "…a folder that is not itself a repository is searched two levels down for the repositories in it — each is copied the same
  way, and more than 50 refuses the row: grant the project folders themselves; a broken one is skipped and named in the row's
  note; a folder with none is refused." (The first draft said "at most 50", which reads as a cap; the row is REFUSED above 50 — G-I,
  all five languages.) The same
  sentence in `helpDe.ts:63`, `helpEs.ts:63`, `helpRu.ts:63`, `helpUk.ts:127` (a stale translation is invisible: there is
  a test over the four bodies' shape — run `src_vs_code` tests after).

**Red-first tests:**
- S15 `AParentOfTwoRepositories_OneWithVibeHooks_LaunchesInTheCopyTree_SeesBoth_SeesNoVibe_NamesNoOriginal_AndTheCopiesAreGone`
  — red before E1.2: `Expected status to be "answered", but found "refused"`. Because E1.2 and E1.3 land first, this one is
  green when written; say so, and keep it: it is the only test that drives the real service over a parent folder. Its
  assertions: `turn.Files.Keys` hold `0/<repoA>/snapshot-…/…` and `0/<repoB>/snapshot-…/…` paths (do NOT reuse the
  `Split('/')[0]` idiom of `:126`, `:146` — the first segment is now the grant index), none holds `/.vibe/`, `StdIn` names
  each copy root and not the parent's path, `RowFolderOf(turn.Request)` is gone after the answer.
- S10 `AnOldQuestionRowScratch_HoldingCopies_IsGoneAfterOneBeat` — red (with C1): `Expected Directory.Exists(scratch) to be False because the beat sweeps …, but found True`.
  Without C1 this test cannot go green and the story drops it, writing the leak down instead (see C1).

**Done when:** S15 and S10 green on Windows and Linux CI; the docs and the five help bodies updated; the summary states the
guards (S6, S8, S15 green from the start) and the reds (S10 if C1); `/promote-plan` run — the plan moves to
`research/` with `IMPLEMENTED 2026-10-xx`, its deviations (C1–C9 as accepted) recorded; `PlanLifecycleTests` green.

**Outcome of E1.4 (2026-10-19).**
- **Step 0, measured read-only before anything else** (a throw-away test over `ParentExpansion.PrepareAsync` and `git ls-files`, deleted
  afterwards; counts only): on the owner's real root the walk and D9 select 13 repositories and leave 394 linked worktrees out (392 the day
  before). The 13 would copy 10,734 files and 504.5 MiB in total (live listing minus `SnapshotExclusions.Default`); the largest repository
  is 376.2 MiB in 1,606 files, the one with most files has 2,517. That is under the 2 GB and 50,000-file limits the lead set, under the
  shared 2 GiB snapshot budget and under each repository's own 512 MiB ceiling; `C:` had 920 GiB free. The copy's wall time was NOT
  measured (the largest repository is the one to watch against the 60 s per-repository ceiling; about 34 s of the row's 5 minutes go to
  git's confirmations first). No size bound per row is added; the first live row decides whether one is needed.
- **S15 is a guard, green from the start** (E1.2 and E1.3 landed first): `AVibeDiskRowReadsAParentFolderThroughTheServiceTests`, a Theory
  over the Windows and WSL2 hosts. It is a new small class, not an addition to `AVibeQuestionRowReadsACopyOfItsRootsTests` (that file is
  431 lines and its temp folder is `coai-qvibe-`; the new one uses `testsToDelete-` and the shared `VibeQuestionRowFixture`). Teeth shown
  once by breaking the code (`tally.Limit` made `tally.Say`): both arms went red on the prompt assertion; restored.
- **S10 was already built** with E1.2's merge of the D8 branch (`AScratchSweepRunsOnTheConsultationBeatTests`,
  `AQuestionRowHoldsItsScratchLockWhileItRunsTests`), so E1.4 only documented it.
- **Docs and help:** `module_runners.md` has the section "A Vibe disk row expands a parent folder into its repositories" (rules, D9, layout,
  D4, link rule, sweep, measurements, a Mermaid flowchart); `module_server.md` has the question-row bullet, the flowchart node and the files
  table; `module_tests.md` has a row for each test class of this plan with its recorded red (taken from this plan's red lists where the
  coders' own run output was not kept). The Vibe disk-row help sentence is rewritten in `helpContent.ts` and the four translations.
- **Checks run:** full `CoaiMcp.Tests` 11206 / 0 failed / 56 skipped; extension typecheck, lint and `npm test` green (6348 tests in the main
  run, 0 failed); `dotnet format --verify-no-changes` clean; `plan-lifecycle.mjs` clean.
- **Not done on purpose:** the promotion (the code gate comes first), and the live row.

### Spec corrections (the plan against the code) — ACCEPTED 2026-10-18, folded into D1, D3–D6, D8, S10, S12 and the scope line

- **C1 — D8 is wrong.** A question row's copies are NOT in a `coai-answers-*` folder. They are in the row's own scratch,
  `coai-question-<id>` (`QuestionRowLaunch.cs:80`, `:83`, `:188`). `PanelService.ScratchPrefixes` (`:1364-1365`) is
  `coai-answers-*`, `coai-repair-*`, `coai-noworkspace-*` — `coai-question-*` is not swept by the product, and the test
  sweep lists it `neverSwept` (`shared/temp-sweep.json`). So a killed server strands a row's copies for ever — today too,
  for a single root. S10 as written would guard the wrong folder. **Proposed fix:** add `"coai-question-*"` to
  `ScratchPrefixes` (a live row is bounded by `RowBudget` minutes, far under six hours), and make S10 a red-first test over
  a `coai-question-` folder. D8's text "This plan adds nothing there" then becomes "this plan adds the prefix".
- **C2 — D5 names the wrong method.** `LinkUpToTheWorkTree` (`ConsultantPlaces.cs:368-382`) climbs from the folder up to
  the FIRST folder holding `.git` and stops (`:377-380`). For an expanded repository that first folder is the repository
  itself, so the method checks one folder and never reaches the grouping folder or the root. The story adds `LinkUpTo(repository, root)`
  (E1.2). Also: the root itself can be a link (a junction at `d:\projects` pointing at a parent folder); the walk never
  follows links, so the root must be checked before the walk (E1.2 step 1) — the plan does not say this.
- **C3 — `OutsideAGitTreeAsync` folds two answers into one.** `:389-392` says "does not exist", `:397-400` says "not inside a
  Git work tree". D1 expands the second case only; a missing folder must stay a refusal. E1.2 splits the method.
- **C4 — Two existing assertions go red by design.** `AVibeQuestionRowReadsACopyOfItsRootsTests.cs:160` and `:187` expect
  `"not inside a Git work tree"` for a plain folder. After E1.2 the folder is walked and refused with D3's "holds no Git
  repository within two levels". The story changes both (they are S5's "given none" through the service). The plan's S1 red
  message stays right (it is the place-level red before the fix).
- **C5 — S12's set-up cannot happen as written.** "A link planted at its top" makes the repository invisible to the walk
  (links are never listed, D2), so it would be skipped, not refused. The copy-side refusal needs a link INSIDE a found
  repository that git lists — the committed mode-120000 entry of `AVibeQuestionRowReadsACopyOfItsRootsTests.cs:388-399`.
  E1.2's S12 uses that.
- **C6 — The budget sentence's "of".** `OutOfBudget(made.Count, sources.Count, budget)` (`:307`, `:347`) counts granted
  SOURCES; `AConversationPlaceTests.cs:136` pins "1 of 2" over two direct roots. The plan's "repositories found so far" is
  kept, and spelled out: a work-tree root counts 1 (counted first, before any parent is walked — as built), an expanded root counts
  its found repositories — the existing test stays green, and a budget that runs out before the first copy says what was going on
  (G-F) instead of "0 of 0".
- **C7 — Scope misses the translations and `module_server.md`.** The help sentence lives in `src_vs_code/src/helpContent.ts:335`
  AND `helpDe.ts:63`, `helpEs.ts:63`, `helpRu.ts:63`, `helpUk.ts:127`; the question row's copy is documented in
  `research/module_server.md:7362-7372`, `:7405`, `:7413-7420` as well as `module_runners.md`. E1.4 lists all of them.
- **C8 — Path length is understated in D6.** The layout adds `<n>/<group>/<repo>/` to every copied file's path under the
  row's TEMP folder. `SnapshotCopier.Failed` (`:225-226`) refuses with "the path is too long" — a repository that copied as
  a direct grant can be refused once expanded, and the whole row with it (D4's copy side). Vibe's own interpreter is not
  long-path aware (`VibePathBudget.cs:14-16`), so a copy that .NET writes can still be a file Vibe cannot open. Not a
  blocker; the help and the module doc should say it, and the plan's "rely on .NET's long-path support" should say it is
  a refusal with a sentence, never a silent miss.
- **C9 — Small citation slips.** `SnapshotCopier.cs:145` is `TookTooLong`'s last line; the documented check-to-open race is
  the remark at `:150-154`. `SnapshotSource.Live` is `:55-62`, not `:55-59`. Harmless; fix on promotion.
- **One spec refinement, not a correction:** D6's de-duplication is spelled as "by full path across the whole row, direct
  work-tree roots included, first in source order wins" (E1.2's `Tally.Copied`). D6 says only "a repository found under two
  overlapping grants"; the refinement covers a repository granted directly and found again under a parent grant. Say yes
  or no in the gate.

## Review fixes (2026-10-18) — what the review of E1.1–E1.3 changed, and what it left

Each was seen red first (the message is in `AVibeDiskRowExpandsAParentFolderTests`, `AQuestionRowKeepsWhatItsPlaceSkippedWhateverEndsItTests`
and `AVibeDiskRowSaysWhatItSkippedTests`); R4 is a timing change with no behaviour to see red.

- **R1 — A found candidate is a repository TOP.** `--is-inside-work-tree` true AND an empty `--show-cdup` (D2). Red: a folder with an
  empty `.git` under a folder whose `.git` is a link to another repository was copied as `0/X/sub/snapshot-…`.
- **R2 — A root that cannot be listed has its own refusal, and a refusal keeps its notes** (D3). `WalkOutcome` carries `Notes` on
  both arms; `ParentExpansion` says them before it returns the reason. Red: "holds no Git repository" for an unreadable root, and no
  notes on the place for an all-unreadable root and for the too-many-folders refusal.
- **R3 — The bound names the row, and a direct grant counts** (D3). Red: the sentence named the second root of two that held 26
  and 25; a 51st work tree granted directly after a parent of 50 was copied.
- **R4 — S7a's timing.** The budget is 10 s and the slow copy sleeps 12 s (it was 4 s and 6 s, tight on a busy runner).
- **R5 — The row's note on every arm.** The timed-out and the failed arm of `QuestionRowLaunch.RunAsync` carry what the place
  skipped; `NoteAfter` joins the seed with a follow-up turn's note instead of replacing it. Red: an empty note on both arms.
- **R6 — The root is normalised once** (`GrantedPaths.Keyed`) and that spelling is what `Directory.Exists`, the link check, git and
  the walk all use.
- **R7 — Links before git.** A found repository's links up to its root are checked before git runs in its folder (in discovery and
  again in the copy). A link on the way refuses the row, as at the copy before.
- **R8 — The walk's cost and its silence.** A depth-2 folder is probed for `.git`, never listed (red: the seam saw depth-2 listings);
  a link to a folder is named in one note per root (red: a junction to a folder was skipped without a word).
- **R9 — File size.** `ConsultantPlaces.cs` is 450 lines; the expansion is `ParentExpansion.cs`, the copy method `RepositoryCopier.cs`
  (`CopyOfAsync` of this plan's text), the tally `OneShotTally.cs`, the link rule `GrantedPaths.cs`, the git answer `RepositoryFacts.cs`,
  the worktree selection `LinkedWorktrees.cs` — each with one job; no `partial`.

**Known limits, left on purpose.** De-duplication is by path TEXT: two spellings of one folder (8.3 short names, `\\?\` prefixes, a
junction the walk did not follow) are two repositories, and a sub-folder of a repository granted beside the parent that holds it is
copied a second time, as its own root — it costs budget and bytes, never correctness. The git confirmations run one after another
(D9's measurement). A row cancelled by its own deadline INSIDE the walk loses the walk's notes (the place is never returned).
Identity edge cases fail toward KEEP: 8.3 short names and a drvfs or macOS case mismatch make two keys of one folder, which costs bound
slots and bytes, never a missed repository. One rare reverse case exists: case-blind keys on Windows can MERGE two case-sensitive folders
made in WSL (`App` and `app`) in de-duplication and in the main-checkout identity, so one of them may be left out as a duplicate.

## Code gate (2026-10-19)

The epic-1 code round (session `6c326eb7`, scope = E1.1–E1.4 and D9 at `9c1a55fa3`) ended **good_enough** — 12 of 16 reviewers
answered (Kimi's four lenses failed: not signed in), 22 gating findings after de-duplication, 30 findings in the record taken apart.
A second review (Opus, parallel, no BLOCKING) added findings I–P. Ten of the 30 were taken and the code fixed, each seen red first (a
timing, documentation or pure refactor item has no behaviour to see red — said where so); the other twenty were declined.

**Taken — the letters are the fix order; the gate's finding number is in brackets (A–H); I–P came from the second review.** Elsewhere in this plan "G-X" means fix X of this list.
- **A (finding 0) — `research/architecture.md` showed no changed interaction.** A subsection and a Mermaid flowchart now draw the
  Server's consultation flow (`QuestionRowLaunch` → `ConsultantPlaces` → `ParentExpansion` / `RepositoryWalk` / `RepositoryFacts` /
  `RepositoryCopier`) reaching into the Runners' `ReviewSnapshots`, `RepositoryIdentity`, `HeldFile` and `FolderLock`, and the row
  scratch's lock and the sweep; the module-map row of the snapshot says it. The diagram parses with Mermaid 11.
- **B (finding 2) — `FolderLock.RemoveIfFree` took a nullable callback.** Two overloads now; the step is never null. Pinned by the new
  `FolderLockTests` (a backfill: no test named the type).
- **C (finding 4) — only dot folders were hidden.** On Windows a folder with the Hidden or System attribute (`$RECYCLE.BIN`, a drive
  root's `System Volume Information`) is skipped silently too: `FolderEntry.Hidden` (a bool), set by `FolderListings.Read`.
- **D (findings 5, 13) — the sink got raw notes.** `OneShotTally.Say` redacts once; the log and the place's list read the same
  text. `PlaceNotes` keeps `Redaction.SafeText` because it is also the CUT of the joined text — redacting redacted text changes nothing.
- **E (finding 6) — a listing materialised every entry.** Bounded at `RepositoryWalk.MaxEntries` = 100,000, streamed, keeping only
  what the walk needs; past the bound the row is refused by name.
- **F (finding 12) — "0 of 0 copied" for a budget spent while confirming.** The refusal says which phase it ended.
- **G (findings 14, 15) — `RepositoryWalk.Visit` and `ParentExpansion.ConfirmedAsync` above complexity 4.** Split into small steps;
  behaviour unchanged, the existing tests prove it.
- **H (finding 29) — one skip sentence for two causes.** `RepositoryFacts.Said` (`GitSaid`) carries why; two sentences.
- **I** the help said "at most 50", which reads as a cap — all five languages now say that more than 50 refuses the row.
- **J** the sweep test aged a folder while its lock was still free; it is now young, locked, then aged (a race: no deterministic red).
- **K** `IsMain` needs a KNOWN git dir equal to the common dir (red: an answer with an empty git dir was a main).
- **L, M** written down as accepted behaviour: D9's early stop (it may change which refusal sentence appears, never Owned versus
  refused) and the Known limits line on identity edge cases.
- **N** the Mermaid in `module_runners.md` draws the real order (existence, git, link check, walk). **O** D3 and D6 text corrected
  (direct grants are counted first and always win) and the missing `[parent, worktree]` test order added, plus `[parent, repository]`.
- **P** `module_server.md` D8 names the two cases the lock does not protect a live row beyond six hours; S15 also checks the
  `/mnt/<drive>/…` spelling of every original path.

**Declined (20), one line each** — the reasons are the gate's `resolve` reasons:
- Confirming candidates in parallel is deferred until the first live row measures the copy time (D9 measured 34 s for 405 calls; the
  13 selected repositories are far under every ceiling) — a speed-up with a race surface, built on a number nobody has yet.
- Making the copies in parallel is deferred for the same reason, and because the shared byte budget makes the order matter.
- Reporting progress while a row copies is a separate feature (it needs a record field and a panel change), not a fix to this epic.
- The re-check of links and git right before each copy is deliberate: it closes the walk-to-copy window (D5, R7) and costs one cheap call.
- The remaining sixteen declined findings (duplicates of the above, style preferences with no behaviour, and claims the code refutes)
  are recorded one by one in the gate's resolve record for session `6c326eb7`; this plan carries the four whose reason is design.

**Tests added in this round:** `OneShotTallyTests` (2), `FolderLockTests` (4), and in the existing classes `RepositoryWalkTests` (+5),
`RepositoryFactsTests` (+2), `AVibeDiskRowExpandsAParentFolderTests` (+5) — each in `research/module_tests.md`.
