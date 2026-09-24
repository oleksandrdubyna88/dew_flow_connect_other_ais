# PLAN — the consultant is not blamed for what a sibling worktree writes into shared git metadata

> Status: **plan only, nothing implemented yet.** Scope: the consultation's filesystem invariant in
> `src_mcp` (`runners/Consultation/FilesystemInvariant.cs`, `core/Consultation/FilesystemSnapshot.cs`) and
> its tests. Issue #376.
>
> Related docs: [module_server.md](../research/module_server.md), [PLAN_consultant.md](../research/PLAN_consultant.md),
> [architecture.md](../research/architecture.md).

## The symptom

Issue #376: a consultation came back withheld because *"the working tree changed while the consultant was
running … `<git>/common/config (.git metadata changed)`"*. The person asks: *is this normal — the
consultant runs read-only in a separate worktree, doesn't it?*

## What is actually true (read from the code)

- **The consultant runs in the caller's LIVE checkout, not a worktree** — by design
  (`research/PLAN_consultant.md:172`, `runners/Consultation/IConsultantRuntime.cs:23`; the cwd is the
  top level, `ConsultationService.cs:308`). Only the review gate uses pinned worktrees.
- **The consultant almost certainly did not write it.** Claude consults in plan mode with Bash/Edit/Write
  denied (`ClaudeConsultant.cs:43-44,58-59`); codex with `-s read-only` (`CodexConsultant.cs:52,58`).
- **What made it fire:** when the checkout is itself a LINKED worktree (the way this work is done, under
  `D:/rsd/_wt/…`), `GitDirectories` (`FilesystemInvariant.cs:184-200`) also watches the COMMON directory, and
  `GitMetadata` (`:142-156`) watches `HEAD` and `config` in it. Both are shared by every worktree of the
  repository:
  - `common/HEAD` is the MAIN worktree's HEAD — it moves whenever anybody switches branch there;
  - `common/config` is rewritten by ordinary work in any sibling: `git push -u` / `git switch -c X origin/…`
    (`branch.X.remote`/`.merge`), VS Code's `branch.X.vscode-merge-base`, GitLens' `branch.X.gk-*`.
  A consultation that lasts minutes, beside a parallel session or an IDE, trips on either.
- Any change is a hard refusal (`ConsultationService.cs:511-514, 552-564`): the advice is withheld.

## The rule

Watch what can make the next git command in THIS checkout run somebody else's code, or move THIS
checkout. Do not watch the bookkeeping other worktrees and tools legitimately write.

1. **The common directory's `HEAD` is not watched.** It is another worktree's. This checkout's own `HEAD`
   (in its own git directory) stays watched.
2. **`config` is fingerprinted by MEANING, not bytes** — in both directories. It is read with
   `git config --file <path> --list -z` (without `--includes`, so an `include.path` change is itself
   visible), the known-harmless branch bookkeeping is dropped, and the rest is sorted and hashed:
   - dropped: `branch.<name>.merge`, `.rebase`, `.vscode-merge-base`, `.gk-*` — tracking bookkeeping.
     `.description` is KEPT: it is free text of any size, and nothing is gained by leaving a place to
     smuggle a payload unwatched (gemini, the plan round);
   - `branch.<name>.remote` / `.pushremote` dropped ONLY when the value is `.` or the name of a remote whose
     `url` the SAME file defines — a URL there, or a name resolved from another scope, is where the next push
     goes, and stays visible (gemini, the plan round).
   - everything else — `core.*` (hooksPath, fsmonitor, sshCommand, pager, editor), `alias.*`, `filter.*`,
     `diff.*.textconv`, `merge.*.driver`, `credential.*`, `url.*`, `remote.*`, `include*`, `submodule.*` —
     is kept: those are the ways a repository is turned against its owner.
   - a config git cannot read falls back to the byte fingerprint, so a corrupted file is still seen.
     Measured 2026-09-24: an EMPTY file exits 0 with no output (so it has a meaning fingerprint of its own,
     not the fallback); a malformed one exits 128.
   - the variable name is the part after the LAST dot of the key, the subsection everything between the
     first and the last — branch names contain dots.
3. **The hooks stay watched** in both directories (a shared hook runs in every worktree).
4. **The sentence says what shared means.** A `<git>/common/…` change is described as *"shared git
   metadata changed — every worktree of this repository can write it: a push -u, a branch switch or the
   editor in another checkout"*, so the person reading a withheld consultation is not led to blame the
   consultant for what a sibling did (local, the plan round).

**Declined:** excluding the whole common directory (loses shared hooks and dangerous `core.*`);
excluding `config` entirely (the existing `EditedGitMetadata_IsSeen` would go silent); an allow-list of
dangerous keys (every new executable key would be missed). **Open tail:** a per-worktree HEAD commit check
(`rev-parse HEAD`) would also catch a consultant's `commit --allow-empty`, which neither snapshot sees today.

## Build order

1. Tests first, in `FilesystemInvariantTests` (real temp repositories, the `InALINKEDWorktree_*` style):
   - `InALINKEDWorktree_TheMainCheckoutSwitchingBranch_IsNotABreach` (red today);
   - `InALINKEDWorktree_ASiblingSettingAnUpstreamOrAMergeBase_IsNotABreach` (red today);
   - `AnEditorsMergeBaseInThisCheckout_IsNotABreach` (the same bookkeeping in an ordinary repository; red
     today);
   - still seen: `ASharedConfigThatRunsCode_IsStillSeen` (`core.fsmonitor` set from the main checkout),
     `ABranchPushRemoteSetToAURL_IsStillSeen`, `AnIncludeAddedToConfig_IsStillSeen`,
     `ABranchDescription_IsStillSeen`; the fingerprint's own edges — the same settings written in another
     ORDER are the same config, a MALFORMED config is still seen changing (byte fallback), an EMPTY config is
     stable (codex, the plan round); and the existing
     `EditedGitMetadata_IsSeen`, `InALINKEDWorktree_TheCommonConfigAndHooksAreWatchedToo`;
   - the sentence for a `common/` path names shared metadata (`FilesystemSnapshot` unit test).
2. `FilesystemInvariant`: the common directory without `HEAD`; `config` through the meaning fingerprint.
3. `FilesystemSnapshot`: the shared-metadata wording.
4. Docs: `research/module_server.md`, `research/PLAN_consultant.md` (its invariant section, which still
   lists `.git/index` and size+mtime), `research/module_tests.md`, CHANGELOG.

## Test plan

- `dotnet build`, `./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe` (whole suite), red first, each
  guard proved by breaking it.

## Definition of Done

- [ ] A sibling worktree or the editor writing branch bookkeeping, or the main checkout switching branch,
      does not withhold a consultation.
- [ ] A config change that can run code or redirect a push, an include, a hook — still does.
- [ ] A withheld consultation over shared metadata says it is shared.
- [ ] Tests red first; the C# suite green; docs updated; this plan promoted to `research/`.
