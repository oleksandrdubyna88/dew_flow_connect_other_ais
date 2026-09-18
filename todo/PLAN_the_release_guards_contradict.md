# PLAN — the two halves of the notes guard stop contradicting each other

> Status: **plan only, nothing implemented yet, 2026-09-18.** Scope:
> `.github/scripts/changelog-names-the-release.mjs`,
> `src_vs_code/src/test/changelogNamesTheRelease.test.ts`, and the release workflow's `mcp-draft`
> job.
>
> **Found while cutting `mcp-v0.29.0`**, the first release under the guard that
> [PLAN_a_release_says_what_it_shipped.md](../research/PLAN_a_release_says_what_it_shipped.md)
> shipped on 2026-09-17. Nothing is broken in either half on its own; together they leave no order
> in which a release can be cut without one of them failing.
>
> Related: [module_tests.md](../research/module_tests.md).

## The symptom, measured on the real scripts

Two checks, both good, both pointing the other way.

| Check | What it refuses | Measured |
|---|---|---|
| `.github/scripts/changelog-names-the-release.mjs mcp-v0.29.0` | a tag whose version `changelog-baseline.json` does not name | **exit 1** without the entry, **exit 0** with it |
| `changelogNamesTheRelease.test.ts`, *the baseline names no release that was never tagged* | a baseline entry for a version with no tag | **fails** naming `0.29.0`, and `ci.yml:189` sets `fetch-tags: true`, so it really runs in CI |

`extension · typecheck · test · package` is a required check on `main`, and it runs that suite. So:

- a commit carrying the notes AND the baseline entry is **red until the tag exists**;
- the tag cannot exist until that commit is on `main`;
- and `main` only takes it through a green required check.

There is no order. The deadlock is real, not theoretical: it is what `mcp-v0.29.0` met.

## Why `mcp-v0.28.0` did not meet it

Because it predates the guard by hours. Its tag was created **2026-09-17 11:45** and its baseline
entry landed at **16:15** — four and a half hours later, in
`6d32deea fix(changelog): the backfill counted one heading shape of three`. The guard that now
refuses that order shipped the same day. 0.29.0 is the first release to run under both halves, and
the first to discover they do not compose.

## What was done for 0.29.0, and what it cost

Four steps, with one deliberate failure:

1. the notes merged alone;
2. the tag pushed — `mcp-draft` **failed** on the missing baseline entry;
3. the baseline entry merged, now green because the tag exists;
4. the tag deleted and pushed again — the recovery the guard itself prints.

It works, it is loud, and it leaves a failed workflow run in the history of every release. The
alternative considered and rejected was tagging the PR branch's head before merging: no failure, but
this repository allows only rebase and squash merges, so the tag would point at a commit that is not
an ancestor of `main` — and `mcp-v0.28.0` and every tag before it is one. Traceability is worth more
than a clean run.

## Three ways out, and the one to take

**A. The release guard accepts a tag the baseline does not name, when the CHANGELOG does.** The
baseline exists to stop a note being LOST — `baseline-only-grows.mjs` is the ratchet, and it works
on its own. Whether the version being released is named there is a different question from whether
its notes exist, and the guard already reads the changelog to find them. **This is the one to take**:
it removes the demand that creates the deadlock without weakening either purpose, and the baseline
entry then follows the tag naturally, which is what 0.28.0 did.

**B. The test ignores the version currently being released.** Would work and is worse: the test
would need to know what "currently" means, which is a tag that does not exist yet — the same
circularity, moved.

**C. `mcp-draft` adds the baseline entry itself and pushes it.** A workflow that writes to `main` to
satisfy its own check is a workflow nobody can reason about, and this job deliberately runs with
`persist-credentials: false`.

## Test plan

| # | Test | Why it has teeth |
|---|---|---|
| 1 | The guard passes for a tag whose notes exist and whose version the baseline does NOT name. | The deadlock, as a case. Red today. |
| 2 | The guard still refuses a tag whose notes do NOT exist, baseline or no baseline. | Or A has simply removed the guard. This is the half that must survive. |
| 3 | The ratchet still refuses a baseline that LOST an entry. | The baseline's actual purpose, untouched by A. |
| 4 | The phantom test still refuses a baseline naming a version that was never tagged. | Also untouched: it is about entries that outlive their release, which is the defect it was written for. |
| 5 | Every assertion watched failing first. | `testing.md`. |

## Definition of Done

- [ ] A release can be cut in one order, with no step that is expected to fail.
- [ ] The guard still refuses undocumented notes; the ratchet still refuses a lost one; the phantom
      check still refuses an entry with no tag.
- [ ] `research/module_tests.md` records the order a release is cut in, as a sequence somebody can
      follow.
- [ ] Each assertion watched failing first, and both observations reported.
- [ ] Promotion per `common/planning-docs.md`, then `plan-lifecycle.mjs`.
- [ ] Through `review_plan` and `review_code`.

## What this will NOT do

It will not change what goes IN the notes, or who writes them. And it does not touch the
`extension-v` and `server-v` lines, which are unguarded by a decision recorded in the guard's own
header — that is a policy question about those lines, not this one.
