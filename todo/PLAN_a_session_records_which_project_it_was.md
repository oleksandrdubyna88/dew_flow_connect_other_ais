# PLAN — a session records which project it was

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope: `src_mcp` (the `sessions`
> table, `RoundsDb`, `--pairs-json`), then `src_vs_code/src/projectIdentity.ts`.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [PLAN_the_review_page_can_be_read.md](PLAN_the_review_page_can_be_read.md) (story 2.2, which this
> completes).

## The symptom

The review page groups pairs by project. Story 2.2 built that grouping, and it derives the project
from `sessions.repo_path` plus **today's filesystem** — the `.git` a path points at right now. Two
code reviewers on that story's code round named what that cannot do, and they are right:

1. **Two clones of one project are two tabs.** Both `.git` entries are directories, the paths
   differ, so the keys differ. Nothing about a checkout location says which project it is a copy of.
2. **A reused directory misfiles history.** Somebody collects pairs in `d:/rsd/_wt/review` for
   project A, deletes the worktree, and makes a new one there for project B. Opening the page now
   reads B's `.git` for A's sessions and files A's pairs under B. **This one is worse than a split
   tab: it is wrong rather than merely untidy**, and no rule that reads the filesystem later can fix
   it, because the evidence of which checkout produced the session is gone.

Both follow from the same thing: the identity is *reconstructed* at read time from a location,
rather than *recorded* when the session happened.

## What the measurement says about urgency

Measured on the live store (`%LOCALAPPDATA%/coai-mcp/coai.db`, `sessions.repo_path`, 106 distinct
values, 2026-09-18), and this is why story 2.2 shipped without it rather than growing a server
change:

| | |
|---|---|
| distinct live paths | 90 |
| unresolvable (the directory is gone) | **36 (40 %)** |
| distinct repository roots | **10** |
| distinct identities **by `origin` URL** | **10** |

**Reading `.git/config`'s `[remote "origin"] url` — a file read, no process — would merge nothing on
this corpus.** Every root already has its own origin, and 9 of the 10 have one at all. So symptom 1
has **zero instances** in the measured data, and buying it costs a read per root. That was checked
before deciding, precisely because it looked like the cheap answer.

Symptom 2 has no instances that can be *detected* either — which is the point. A misfiled session
looks exactly like a correct one.

## The fix, and why it belongs on the server

The identity must be captured **when the session is recorded**, by the side that already runs git:

1. `sessions` gains a column — `project_id` — written at `open`, alongside `repo_path`.
2. Its value is the repository's canonical identity as git reports it at that moment. `origin`'s URL
   normalised (scheme, credentials and a trailing `.git` stripped, lower-cased) when there is one;
   the **root commit's sha** when there is not, which is stable across clones and remotes and is
   what `creds_corp`-style local-only checkouts need; empty when neither can be read.
3. `RoundsDb.Pairs()` projects it; `ReviewPair` carries it; `pairOf` fills `''` for an older server,
   which is already how every field story 2.1 added behaves.
4. `projectIdentity.ts` prefers it: a recorded identity groups directly, with no filesystem call at
   all. The current path-and-`.git` rule stays as the **fallback for legacy rows**, which is 100 %
   of today's 106 sessions and will remain most of them for a long time.
5. The tab says which it is. A project grouped by a recorded identity is certain; one grouped by
   today's filesystem is an inference, and the page already distinguishes *not on disk any more*
   from a reachable path, so it has somewhere to put this.

**Where git already runs, and what must not change.** `WorktreeManager` and `GitHistory` are the
server's git surface; the extension spawns nothing and story 2.2 kept it that way. `open` already
runs `git worktree prune` against `repoPath`, so the process that would read the identity is a
process that is already being started.

## The seam this must NOT cross

Story 2.1's boundary holds here too: `Sendable()`, `StoredPair` and `UploadRun.Wire` are the UPLOAD's
types and must stay byte-identical, guarded by `OnlyThreeFieldsLeaveTests`. A project identity is a
repository URL — it names a person's private repository, and it must not reach the ingest server.
It belongs only in `ReviewPair`, the page-facing record.

## Build order

1. **RED**: a test that two sessions of one project in two different clones group together, and that
   a session recorded in a directory later reused by another project does NOT follow the new
   occupant. Both fail today; the second cannot be made to pass by any read-time rule, which is the
   test that proves this plan is needed rather than nice.
2. The schema column + migration (additive, defaulting to `''`).
3. The write at `open`: origin, else root commit, else empty. One process, at a moment that already
   starts one.
4. The projection, `ReviewPair`, `pairOf`.
5. `projectIdentity.ts`: prefer the recorded identity, keep the current rule as the legacy fallback,
   and keep every one of story 2.2's tests green — they describe the fallback, which does not change.
6. The page's marking of certain against inferred.

## Test plan

| Suite | What it must catch |
|---|---|
| `projectIdentity.test.ts` | a recorded identity ignored in favour of the filesystem; a legacy row with no identity losing the fallback |
| a new `TheSessionIdentityTests` (`src_mcp`) | a repository with no remote recorded as empty rather than as its path; two clones answering different identities; the identity read from a directory that is not the session's |
| `OnlyThreeFieldsLeaveTests` | **unmodified** — the upload must not learn a repository URL |
| `bugzLiveContract.test.ts` | the field absent from an older server's document read as `''` rather than as malformed |

## Definition of Done

- [ ] Two clones of one project are one tab; a reused directory does not steal the previous
      occupant's history.
- [ ] Legacy sessions (every one that exists today) still group by the story 2.2 rule, and its tests
      are untouched.
- [ ] The extension still spawns no process.
- [ ] `OnlyThreeFieldsLeaveTests` and the three upload types are byte-identical.
- [ ] `research/module_server.md` and `module_extension.md` record the column, the write and the
      precedence; this plan is promoted per
      [planning-docs.md](../.agents/conventions/common/planning-docs.md).
