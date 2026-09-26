# PLAN — the two halves of the notes guard stop contradicting each other

> Status: **IMPLEMENTED, 2026-09-26.** Way out A shipped: the release guard no longer demands the
> release's own baseline row, so a release is cut in one order with no step expected to fail.
> Alongside it, two things the plan did not have on 2026-09-18: `release-anchors.mjs`, which stops
> release-please when a tag sits outside its package, and `docs-only-title.mjs`, the operator's
> rule that Markdown alone never opens a release.
>
> **Deviations:**
> - A missing tag PASSES the anchor check (the plan said it fails): it is the state of the very
>   run that cuts it.
> - The Markdown rule is per PACKAGE, not per pull request — and, by the operator's word the same day,
>   pictures count as documentation beside Markdown (`DOCUMENTATION` in `docs-only-title.mjs`).
> - It runs as a step of the already-required `pr · semantic title` job, not a job of its own.
>
> **The open tail closed the same day:** `mcp 0.39.0` (#580) was the first real mcp release cut in the
> new order. Its tag sits on the squash commit of the release pull request (`9a3374ca`, which touches
> `src_mcp/version.txt`), `release.yml` ran once and succeeded, the release carries its twelve assets,
> and no tag was moved. `release-anchors.mjs` reported every line anchored before and after it. The
> Markdown rule applies from its merge onward: `pull_request_target` runs main's workflow.
> Scope: `.github/scripts/changelog-names-the-release.mjs`, `release-anchors.mjs`,
> `docs-only-title.mjs`, `release.yml`, `release-please.yml`, `pr-title.yml`,
> `release-please-config.json`.
>
> **Found while cutting `mcp-v0.29.0`**, the first release under the guard that
> [PLAN_a_release_says_what_it_shipped.md](PLAN_a_release_says_what_it_shipped.md)
> shipped on 2026-09-17. Nothing is broken in either half on its own; together they leave no order
> in which a release can be cut without one of them failing.
>
> Related: [module_tests.md](module_tests.md).

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

## 2026-09-26: the recovery broke release-please

The four-step recovery ends by moving the tag to the head of `main` — the baseline commit. Under
release-please, the tag's commit is also the anchor the NEXT release is counted from. Twice on
2026-09-26 that opened an empty `mcp 0.39.0` (#562, #566, #572), listing every mcp feature since long
before 0.37.0. The cause was found in release-please's own source (`src/manifest.ts`,
`buildPullRequests`):

- it finds each line's release commit by the tag (`mcp-v0.38.0` → `ad06b589`, the baseline commit);
- it splits the commits since the oldest line's release by package path (`CommitSplit`), and then
  `commitsAfterSha(splitCommits['src_mcp'], releaseSha)` looks for the release commit INSIDE the
  `src_mcp` list;
- `ad06b589` touches only `.github/changelog-baseline.json`, so it is not in that list;
  `findIndex` answers −1 and the function returns EVERY `src_mcp` commit in the window.

Checked on all four lines: `extension-v0.56.2`, `server-v0.8.0` and `bugs-v0.4.0` sit on commits
that touch their package; `mcp-v0.38.0` did not. The ref was moved back to its release commit
`5cb1bcb3` (the same `src_mcp` tree — an empty diff), with the release left published and its twelve
assets untouched. The next run logged `No user facing commits found since 5cb1bcb3 - skipping` for
`src_mcp`. The same shape comes back from a REBASE merge of a release pull request that has a
commit added on top, which is how #560 merged: the tag lands on the last commit, which is the
CHANGELOG one.

And the operator's rule, stated the same day: **a release pull request opens only for a change to
CODE; a change to Markdown files alone never opens one.** release-please cannot say that itself —
`exclude-paths` entries are directory prefixes, not globs. What decides a release is the commit
TYPE: `docs`/`chore`/`test` release nothing, while `feat`/`fix`/`perf`/`revert` or a `!` do. So the
rule has to be enforced where the type is chosen.

### The work, in build order

1. **Way out A**, as costed above. `verdict` in `changelog-names-the-release.mjs` stops refusing a
   tag whose version the baseline does not name yet — the changelog section is still demanded, and
   the `lost` check over every recorded version stays. The release order becomes:
   1. release-please opens the pull request;
   2. the CHANGELOG section goes onto it;
   3. merge it by SQUASH, and dispatch;
   4. the bot tags the squash commit, which touches `src_mcp/version.txt`, and `mcp-draft` passes
      first time;
   5. the baseline row follows in its own pull request, now that the tag exists.

   No tag is ever moved again.
2. **`release-anchors.mjs`**, a new script beside the guard. For every package in
   `release-please-config.json`, the tag `<component>-v<manifest version>` must exist and sit on a
   commit that touches a file under that package. When it does not, the script exits 1, naming the
   tag, the commit and the files, and the repair: the newest commit at or before the tag that touches
   the package, whether its package tree is identical to the tag's, and the exact `gh api -X PATCH`
   of the ref (a PATCH, not a delete — deleting the tag of a published release turns it into a
   draft). It never moves a tag itself. A version whose tag has not been cut yet PASSES: that is the
   state of the very run that cuts it. It runs as
   the first step of `release-please.yml`, with a full-history checkout carrying tags. So a moved tag
   or a rebase-merged release pull request stops the next run loudly, instead of it opening an empty
   release. A pure `anchorVerdict(lines)` holds the decision; the CLI only gathers the git facts.
3. **`docs-only-title.mjs`**, a new script run as a STEP of the required `pr · semantic title` job
   (a job of its own would be advisory until branch protection named it). PER PACKAGE: a title of a
   releasing type (`feat`, `fix`, `perf`, `revert`, any `!`) is refused when the pull request's files
   under some package are all `.md` — which also catches a `feat:` for the extension carrying
   `src_mcp/README.md`, since that commit lands on the mcp line too. Markdown outside every package
   opens no release and is not refused. The repair: say it as `docs:`, or move the Markdown into its
   own `docs:` commit. The same holds per commit, for a commit whose
   own files are all `.md`, because a rebase merge keeps each commit's message and release-please
   reads those. A pure `docsOnlyVerdict({ title, files, commits })` holds the decision; the job gets
   files and commits from the API with `pull-requests: read`, and never checks out the pull
   request's code (`pull_request_target` runs the base branch's script).
4. **Docs.**
   - `release-please-config.json`'s `$order` note is corrected: it tells a releaser to put the
     baseline entry on the release pull request, which is the deadlock.
   - `research/module_tests.md` records the release order as a sequence to follow.
   - The CHANGELOG is not touched: this ships nothing a person installs.

### Tests, each watched failing first

| # | Test | Red today because |
|---|---|---|
| 1 | The guard passes a tag whose notes exist and whose version the baseline does not name. | `verdict` pushes the baseline problem. |
| 2 | The guard still refuses a tag with no notes, baseline or not; the ratchet and the phantom test still refuse theirs. | These must survive A. They are green today and stay green, and planting A too wide turns them red. |
| 3 | `anchorVerdict`: a tag on a commit touching its package passes; a tag on a `.github`-only commit fails naming it; a missing tag fails. | New. |
| 4 | `release-anchors.mjs` against this repository's real tags passes — and names `mcp-v0.38.0` when pointed at `ad06b589`. | That run IS 2026-09-26. |
| 5 | `docsOnlyVerdict`: an all-`.md` PR titled `feat:`/`fix:`/`perf:`/`revert:`/`feat!:` fails; `docs:` passes; a PR with one `.ts` passes whatever its type; an all-`.md` COMMIT with a `fix:` message fails even under a `docs:` title. | New. |
| 6 | The workflows wire them: `release-please.yml` runs the anchor script before the action, with `fetch-depth: 0` and tags; `pr-title.yml` runs the title script. | A script nobody runs guards nothing. |

### Definition of Done, for the widened scope

- [ ] Items 1–3 built, each test red first and red again with its fix planted out.
- [ ] `release-please-config.json`'s `$order` and `research/module_tests.md` describe the order that works.
- [ ] Every suite green before the pull request.

## What this will NOT do

It will not change what goes IN the notes, or who writes them. And it does not touch the
`extension-v` and `server-v` lines, which are unguarded by a decision recorded in the guard's own
header — that is a policy question about those lines, not this one.
