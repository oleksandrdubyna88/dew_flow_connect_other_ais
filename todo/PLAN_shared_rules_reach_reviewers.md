# PLAN — the family's shared rules must reach the reviewers

> Status: **phases 1 and 2 implemented 2026-09-04 on `fix/worktree-shared-rules`; phase 3 not
> started.** Scope: `src_mcp/runners/Worktrees/WorktreeManager.cs`,
> `src_mcp/runners/Context/RuleFiles.cs`, `src_vs_code/src/panelProvider.ts`,
> `src_vs_code/src/claudeSnippet.ts`, and one new shared rule file in the `dew_flow_conventions`
> submodule.
>
> Related docs: [review-gate.md](../.claude/rules/common/review-gate.md),
> [architecture.md](../research/architecture.md),
> [module_extension.md](../research/module_extension.md).
>
> **Plan round 1** (2026-09-04, all 3 reviewers answered, verdict `good_enough` on a one-round
> budget): 16 findings, 14 accepted, 2 rejected with reasons. What the accepted ones changed is
> recorded under *What the plan round changed* below.

## The symptom

**Round 1 of every code role is the conventions pass** — it judges the diff against the rules the
project wrote down, and a finding there must quote the sentence it breaks
(`src_mcp/core/Rounds/PromptCatalog.cs:47`). In this family those rules are a git submodule:
`dew_flow_conventions` mounted at `.claude/rules/shared` in all six consumers, 26 markdown files,
208 455 bytes. **The reviewers have never seen one line of them.**

Measured 2026-09-04, on `dew_flow_creds_for_devs` at `4d240bc`:

```
$ git worktree add --detach <tmp> HEAD
$ ls -a <tmp>/.claude/rules/           ->  shared
$ ls -a <tmp>/.claude/rules/shared/    ->  (empty)
```

`RuleFiles.Collect` (`src_mcp/runners/Context/RuleFiles.cs:71`) walks `.claude/rules/**/*.md`
recursively and correctly, from the round's worktree. The worktree is created by
`git worktree add --detach <path> <sha>` (`src_mcp/runners/Worktrees/WorktreeManager.cs:50`), and
git does not populate submodules in a linked worktree. So for `dew_flow_creds_for_devs` — whose
`.claude/rules/` contains **nothing but** the mount — the conventions pass is handed `CLAUDE.md`
and nothing else, and reports compliance with a body of rules it was never shown. That is the exact
failure `RuleFilesTests`' own header calls out ("a reviewer cannot flag what it was never told, and
its silence reads as approval"), reintroduced one directory deeper.

This repository is less badly hit — it has real local rules under `.claude/rules/common/` — but its
shared half is missing too.

### The second symptom, same root

`dew_flow_creds_for_devs/CLAUDE.md` carried gate snippet **v2** while the extension hands out **v5**:
three revisions of instructions the AI reading it never got, including the whole COMMANDS block. It
was found by hand, not by the check that exists for it, because that check only looks at repositories
somebody has opened in VS Code. The snippet is the one cross-repo rule in the family that is
deliberately duplicated as a paste — which the conventions repo's own editing discipline forbids
("a consumer repository never carries its own copy of a shared rule", `README.md:35`).

## What must be true when this is done

1. A round worktree of a repository with a rules submodule contains that submodule's **pinned**
   content — the commit the reviewed SHA records, never the tip and never the live checkout.
2. Populating it requires **no network**. A round must not fail, and must not silently lose the
   rules, because GitHub is unreachable, rate-limited, or the submodule is private.
3. A repository with no submodule behaves exactly as it does today; the added step is a no-op that
   cannot fail a round.
4. The conventions pass shows shared rules **in a stated order** and names what the budget dropped.
   With 208 KB of shared rules against a 40 KB budget most files will be omitted — the reviewer must
   be told which, and the ones most likely to be broken must survive first.
5. Rules that are not conventions do not reach the prompt: the submodule's own `todo/`, `settings/`,
   `tools/` fixtures, `README.md`, `ROLLOUT.md` and `POST_DEPLOY.md` are that repository's
   housekeeping, not rules the reviewed diff can break.
6. The gate snippet exists in exactly one place in the family, and a repository that is behind is
   detectable without opening it in VS Code.
7. `dew_flow_creds_for_devs` stops carrying a divergent copy.

## Constraints

- **Reviewers stay read-only and pinned.** Whatever populates the submodule writes only inside the
  round's own worktree, under the storage root, and is removed with it in the existing `finally`.
- **No stdout writes** in the server (JSON-RPC), per the repo's non-negotiables.
- **The 40 KB budget stays a budget.** Raising it silently is not the fix; a prompt is finite and
  `RuleBundle.Render` already names omissions for a reason.
- `git worktree add` has **no** `--recurse-submodules` in git 2.55.0.windows.3 (verified against
  `git worktree add -h`). The mechanism is a second command, not a flag.
- `protocol.file.allow` defaults to `user` since the CVE-2022-39253 mitigation; a local-path
  submodule clone needs it turned on for that one invocation and must never be turned on repo-wide
  or for a URL the reviewed repository controls.
- The extension's snippet check reads **only** the four root instruction files
  (`src_vs_code/src/panelProvider.ts:284`), and its comment gives the reason: no filesystem crawl on
  every repaint.

## The two candidate mechanisms (phase 1)

Measured on `dew_flow_creds_for_devs`, a fresh worktree each time:

| mechanism | time | network | result |
|---|---|---|---|
| `git submodule update --init <path>` | 2.45 s | **yes** — clones `https://github.com/.../dew_flow_conventions.git` | pinned SHA `9b94c01` checked out |
| the same with `-c protocol.file.allow=always -c submodule.<name>.url=<parent>/.claude/rules/shared` | **1.49 s** | **no** | identical pinned SHA |
| read the pinned tree out of `<parent>/.git/modules/<name>` with `ls-tree` + `cat-file` | not measured | no | no clone, no writes, no protocol override — but new code, and it must re-implement path walking |

**Recommendation: the second row.** It is git's own resolution, it is offline, it is faster than the
naive form, and it needs no new file-reading code. The URL override points at the parent checkout's
own submodule working directory — a path this server computed, never one the reviewed repository
supplied — so `protocol.file.allow=always` is scoped to a clone whose source we chose.

Rejected: leaving the worktree alone and reading the rules from the parent's **live** checkout. It
is the cheapest change and it breaks the one property `RuleFiles` documents — the reviewer sees the
rules as of the commit under review, not as of this afternoon.

## Build order

### Phase 1 — the worktree carries the pinned submodules (server)

1. `WorktreeManager.AddAsync`: after a successful `worktree add`, populate submodules.
   - Read the submodule paths from the worktree's own `.gitmodules` (absent => nothing to do, return).
   - For each, run `git -c protocol.file.allow=always -c submodule.<name>.url=<parentPath>/<path>
     submodule update --init -- <path>` in the worktree.
   - A failure is **logged and swallowed**: a round without shared rules is worse than today only in
     that it is now visible, while a round that cannot start is a regression. `RuleBundle` already
     tells the reviewer what it did not get.
2. Keep it inside `WorktreeManager` — it is the object that owns the worktree's lifecycle, and
   `RemoveAsync` already forces removal of everything under the path.

### Phase 2 — the bundle stays readable (server)

3. `RuleFiles`: exclude the submodule's non-rule directories by name (`todo`, `settings`, `tools`)
   and its own top-level `README.md` / `ROLLOUT.md` / `POST_DEPLOY.md` when they sit at the root of a
   mounted rules repository. Extend the existing `NotOurs` mechanism rather than adding a second one.
4. Ordering: instruction files, then the repository's OWN `.claude/rules`, then the mount. The local
   rules are the ones a diff in this repository is most likely to break, and alphabetical ordering
   already gives `common/` before `shared/` — pin it with a test rather than leaving it to luck.
5. Report the count: `RuleBundle.Render` already prints omissions; assert in a test that a 208 KB
   mount produces a named omission list rather than a silent truncation.

### Phase 3 — the snippet becomes a shared rule (both halves + conventions)

6. `dew_flow_conventions/common/coai-review-gate.md`: the snippet's text as a normal shared rule,
   carrying the same `<!-- coai-snippet vN -->` marker so the version stays machine-readable.
7. `panelProvider.pastedSnippet`: after the four root files, look at `.claude/rules/**/*.md` —
   bounded, not a crawl: the four roots first, then that one directory, and only when the roots did
   not carry the block.
8. `claudeSnippet.ts`: the copy button keeps handing out the same text; what changes is where a repo
   is allowed to keep it.
9. Delete the block from `dew_flow_creds_for_devs/CLAUDE.md`, keeping that repository's own
   "Where this bites in the existing rules" paragraph, and bump the conventions pin in every consumer
   per the cascade in `dew_flow_conventions/README.md`.

**Phase 3 must be built on `feat/commands-and-autonomy`,** not on `main`: snippet v5 and the COMMANDS
block exist only there (main still emits v4, and that branch is local-only, 13 commits ahead). Phases
1 and 2 touch neither file and branch off `main`.

## What the plan round changed

The 14 accepted findings land as five changes to the build order above; they are written here rather
than edited into it silently, because which of them came from a reviewer is part of the record.

1. **The failure is named, not swallowed into nothing** (findings 0, 6). The plan's claim that
   "`RuleBundle` already tells the reviewer what it did not get" was **false**: `Omitted` only lists
   files that were FOUND and dropped for length. An unpopulated mount leaves zero files, zero
   omissions and a bundle indistinguishable from a repository with no rules. `RuleBundle` therefore
   gains `MissingMounts` — declared rule mounts that are not in the tree — and `Render` prints them
   with "do not read their absence as compliance".
2. **The local source is verified before it is used** (findings 1, 11). The parent may never have
   initialised the submodule, in which case there is nothing to clone from; the populator checks for
   a `.git` at the parent's copy and skips the mount otherwise, which then surfaces via (1).
3. **The source path is contained** (finding 3). `.gitmodules` is a file inside the repository under
   review, so a declared path is input: absolute paths and traversal are rejected in `GitModules`,
   and the resolved source must sit under the parent checkout before `protocol.file.allow=always` is
   used at all.
4. **The mount's name is read, never assumed from its path** (finding 9). They are equal in this
   family and git does not require it; a wrong name means the URL override silently misses and git
   falls back to the network — the exact outcome this change exists to prevent.
5. **Housekeeping is excluded by MOUNT, not by word** (finding 2), and **the local rules are
   partitioned ahead of the mount** rather than trusted to sort that way (findings 8, 12). A
   repository is entitled to its own `.claude/rules/todo/`, and a local `workflows/` directory sorts
   after `shared/`.

Also accepted: a shorter timeout on the populate call (finding 15 — its premise that there was none
is wrong, `ProcessRequest.Timeout` bounds every git call at two minutes, but a local clone that has
not finished in 60 s is not going to), a test that proves the LOCAL source was used rather than
merely that a lease came back (finding 5), and a headless snippet check that does not depend on
somebody opening the repository in VS Code (findings 4, 7, 14) — that one belongs to phase 3.

**Rejected, with reasons:** finding 10 (fail fast or retry when population fails) — the defect is
accepted at 1 and 6, but failing the round turns an infrastructure hiccup into an outage for a review
that runs perfectly well today, and inverts this product's own principle that an absence must be
named rather than fatal. Finding 13 (a manual Definition-of-Done step for budget truncation) — the
DoD's first item already requires reading a real round's rendered bundle, and the unit test on
`Render` is the check that can actually fail in CI.

## Test plan

- `WorktreeManagerTests` (real git, temp repos — the existing fixture style):
  - a repo with a submodule => the round worktree contains the submodule's file **at the pinned
    commit**, proved by moving the submodule's tip afterwards and re-reading the worktree;
  - a repo with no `.gitmodules` => unchanged behaviour, no error;
  - a submodule whose URL is unreachable => `AddAsync` still returns a usable lease.
- `RuleFilesTests`:
  - a mounted rules repo => its `common/*.md` are collected, its `todo/`, `settings/`, `tools/` and
    root `README.md` are not;
  - local `.claude/rules/common` ranks before the mount;
  - 208 KB of rules against the 40 KB budget => every dropped file appears in `Omitted`.
- `ConventionsPassTests`: the rendered prompt for a repo with a mount cites a shared rule path.
- Extension (`npm test`): `snippetStatus` finds a v5 block that lives only in
  `.claude/rules/shared/common/coai-review-gate.md`; a repo with the block in neither place is still
  `absent`.
- Both suites are run as executables (`CoaiMcp.Tests.exe`), never `dotnet test`.

## Definition of Done

- [ ] A round worktree of a consumer repo carries the pinned shared rules; proved by running a real
      round and reading the rendered rule bundle, not only by the unit tests.
- [ ] Populating them makes no network call (verified with the remote unreachable).
- [ ] A repo without submodules, and a repo whose submodule cannot be fetched, both still review.
- [ ] The conventions prompt names its omissions; nothing is truncated in silence.
- [ ] The snippet exists once in the family; `creds_for_devs` no longer carries its own copy.
- [ ] Conventions pin bumped in every consumer in the same task (`pin-check.mjs` green after commit).
- [ ] `research/module_*.md` updated for both halves; this plan promoted with its deviations.
