# PLAN — Vibe reads a folder of repositories

> Status: **plan only, nothing implemented yet, 2026-10-18.** Scope: `src_mcp/src/Server/Consultation/ConsultantPlaces.cs`
> (the copy loop of a Vibe consultant row), its tests, `research/module_runners.md`, the help sentence for a Vibe disk row.
>
> Related docs: [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (D6 — Vibe reads a copy coai makes through Git),
> [module_runners.md](../research/module_runners.md).

## 1. The symptom

The owner's question-consultant row "Projects on this disk" on Vibe is refused before it starts, on both sides:

> the folder d:\rsd is not inside a Git work tree — this runtime reads a copy coai makes through Git (D6 of
> todo/PLAN_vibe_and_kimi_runtimes.md), and a plain folder cannot be copied that way yet; grant a folder of a repository

`ConsultantPlaces.OutsideAGitTreeAsync` (`src_mcp/src/Server/Consultation/ConsultantPlaces.cs:~388-400`) refuses any granted
root that `git rev-parse --is-inside-work-tree` does not call a work tree. The owner's roots are PARENT folders of many
repositories: `d:\rsd` on Windows, `~/git` on WSL (20 checkouts within two levels, measured 2026-10-18). Every other
runtime reads those roots directly under its own confinement; Vibe cannot, because it has no trustworthy read-only mode (a
committed `.vibe/hooks.toml` runs commands in a trusted folder; plan mode is not read-only), so D6 gives it only a clean Git copy.

## 2. The goal

A Vibe disk row whose granted root is a parent folder answers "projects on this disk" questions, with exactly D6's
confinement: Vibe reads only coai-made copies of Git repositories, each through the existing snapshot (tracked files only;
`SnapshotExclusions.Default` strips `.git`, `.vibe`, `.agents/skills`, `.agents/agents`, `.mcp.json`, `.kimi-code`, `AGENTS.md` —
`src_mcp/runners/Snapshots/SnapshotOptions.cs:62`), in a folder coai owns, disposed after the turn. The owner's own words:
"the copy is simply a worktree" — the snapshot already is that, minus the configuration a checkout would carry.

## 3. Decisions

- **D1 — Expand, do not widen.** In `OneShotAsync`'s source list (`ConsultantPlaces.cs:~293`), a granted root that is NOT inside a
  work tree is expanded into the Git work trees beneath it before copying; a root that IS a work tree is copied as today. Each
  found repository goes through the unchanged `CopyOfAsync` (link refusal, snapshot). Nothing else reads the parent folder.
- **D2 — What counts as a repository beneath a root.** A folder holding `.git` (a directory, or a file — a worktree or a
  submodule's top) at depth 1 or 2 below the root. The walk does not descend INTO a found repository (its nested repos and
  submodules are the repository's own business; the snapshot lists what git tracks). It skips hidden folders (`.`-prefixed),
  `node_modules`, `bin`, `obj`, and any link (never followed — the D6 link rule). Depth 2 covers the measured layouts
  (`~/git/<repo>`, `d:\rsd\<repo>` and one grouping level).
- **D3 — Bounds.** At most 50 repositories per root (beyond that the row is refused with a sentence naming the count and the cure:
  grant the project folders themselves). The copies stay bounded TOGETHER by the row's existing budget (`OutOfBudget`, the
  #811 checkpoint rule): past it the place is refused naming how many were copied of how many. A root with NO repository beneath
  it is refused: "the folder <root> holds no Git repository within two levels — Vibe reads copies coai makes through Git".
- **D4 — One snapshot tree, one name per repository.** The copies land side by side under the existing tree folder, each in its
  own subfolder named after its path relative to the root (collisions impossible: relative paths are unique), so Vibe sees
  `<tree>/<repo>/…` and its answer can cite paths a person recognises. `ConsultantPlace.Owned.Roots` lists every copy, as today.
- **D5 — Order and determinism.** Repositories are copied in ordinal order of their relative path, so the same root gives the
  same copy order and the same refusal sentence on every run.

## 4. Scenarios (each red is the message the test fails with before the fix)

- S1 *Given* a root that is a parent of two repositories, *when* a Vibe disk row is placed, *then* Owned with two copies under
  the tree, each holding its tracked files and none of `.git`/`.vibe`/`AGENTS.md`. Red: `"the folder … is not inside a Git work tree"`.
- S2 *Given* a repository at depth 2 and one at depth 3, *then* only the depth-2 one is copied.
- S3 *Given* a hidden folder, `node_modules`, and a LINK to a repository beneath the root, *then* none of them is copied.
- S4 *Given* a repository with a nested repository and a submodule, *then* one copy (the walk does not descend into a repo).
- S5 *Given* 51 repositories, *then* refused with the count and the cure; *given* none, *then* refused with D3's sentence.
- S6 *Given* a root that IS a work tree, *then* exactly today's behaviour (one copy) — a regression guard.
- S7 *Given* the budget runs out after k of n copies, *then* refused naming k of n, and every made copy is disposed.
- S8 *Given* a committed `.vibe/hooks.toml` in a found repository, *then* the copy holds no `.vibe` (the exclusion applies per copy).

## 5. Test plan

Unit/scenario tests in `src_mcp/tests` over temporary repositories made with `git init` in an owned `testsToDelete-` folder (the
existing snapshot tests' pattern), Windows and Linux CI; red-first per scenario. Run as MTP executables, never `dotnet test`.

## 6. Build order

1. The repository walk (pure over a directory seam) with S2–S5's discovery arms.
2. The expansion in `OneShotAsync` and the per-repository subfolder (S1, S6, S7, S8).
3. Docs: `research/module_runners.md` (the D6 consultant copy now expands a parent folder), the Vibe disk-row help sentence.

## Definition of Done

- [ ] S1–S8 green, each seen red first; the existing consultant/snapshot suites green.
- [ ] A Vibe disk row on `d:\rsd` / `~/git` is Owned, live, with the copies disposed after the turn.
- [ ] `research/module_runners.md` and the help updated; this plan promoted when shipped.

## Epics

One epic, E1 — the expansion (ordinary; the confinement is D6's, unchanged, and every copy goes through the existing snapshot).
