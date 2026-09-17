# PLAN — A release says what it shipped, and the guard finishes the job it started

> Status: **IMPLEMENTED, 2026-09-17.** Scope: `.github/workflows/ci.yml`, `release.yml` (three
> drafting jobs), `.github/scripts/changelog-section.mjs` and `baseline-only-grows.mjs` (both
> new), `draft-release.sh`, the `LINES` registry in `changelog-names-the-release.mjs`, and four
> test files. No product code.
>
> Related docs: [PLAN_the_server_line_gets_its_notes_back.md](PLAN_the_server_line_gets_its_notes_back.md),
> [module_tests.md](module_tests.md).

## Where this comes from

[The Server line gets its notes back](PLAN_the_server_line_gets_its_notes_back.md)
shipped a guard that refuses an `mcp-v*` release whose changelog entry is missing, and a baseline so
the line cannot lose a note it already has. Re-reading it against the repository turned up three
things that plan did not see. One is a defect **in the work just done**, and it fails CI rather than
anything else.

## Symptom 1 — the phantom-tag check cannot run where it was put (DEFECT, blocks PR #347)

`changelogNamesTheRelease.test.ts` asks `git tag --list mcp-v*` and refuses a baseline naming a
version that was never released. That case exists because two entries were once written for
`0.26.0` and `0.27.0`, versions that live in the manifest and have no tag — release notes for
software nobody can install.

**It cannot answer the question in CI.** No checkout in this repository asks for tags:

- [`ci.yml:22`](../.github/workflows/ci.yml#L22) and [`ci.yml:159`](../.github/workflows/ci.yml#L159)
  are bare `actions/checkout@v7`;
- `grep -n "fetch-depth\|fetch-tags"` over both workflow files returns **nothing**.

`actions/checkout` fetches one commit by default and brings no tags with it, so `git tag --list`
answers empty, every baseline version is classified a phantom, and the case fails on a tree that is
perfectly correct. It passes on this machine only because a developer's clone has tags — which is
the worst shape a test can have: green where it is written, red where it is enforced.

**This is mine, from today, and it is the strongest argument in the plan for running a check where
it will actually run before believing it.**

## Symptom 2 — the entry the guard now demands still reaches nobody

The point of forcing an entry is that a person reading the release learns what shipped. They do not.
Every release body is a **literal in the workflow**, identical for every release of its line:

| Job | Call site | Body |
|---|---|---|
| `mcp-draft` | [`release.yml:104`](../.github/workflows/release.yml#L104) | *"Native AOT builds of the ConnectOtherAIs MCP server."* |
| `server-draft` | [`release.yml:124`](../.github/workflows/release.yml#L124) | *"Native AOT builds of the ConnectOtherAIs Team server, and the multi-arch image…"* |
| `bugs-draft` | [`release.yml:943`](../.github/workflows/release.yml#L943) | *"Native AOT builds of the ConnectOtherAIs corpus ingest server…"* |

`draft-release.sh` takes the body as its third argument
([`draft-release.sh:26`](../.github/scripts/draft-release.sh#L26)) and hands it to
`gh release create --notes` ([`:42`](../.github/scripts/draft-release.sh#L42)). Nothing reads
`CHANGELOG.md`.

So **65 `mcp-v*` releases carry byte-identical bodies**, and the guard shipped today makes somebody
write eight paragraphs of prose that the reader of the release never sees. The changelog is the only
place the text exists, and the changelog is not where a person looking at a release goes.

This is the half of the job the previous plan did not name, and it is worth more than the half it
did: writing the notes was already possible, and people did not. Making the notes *arrive* is what
turns the guard from bookkeeping into something with a reader.

## Symptom 3 — the honest limit of the ratchet

`PLAN_the_server_line_gets_its_notes_back` claims the line "cannot lose them again". Precisely, it
cannot lose them **by accident**: deleting a `## Server` heading reddens
`every version in the baseline still has its note` in CI. One commit that removes the heading *and*
its row in `.github/changelog-baseline.json` passes everything, because both sides of the comparison
move together. That is the residual hole in any ratchet kept in the repository it guards, and it
should be either closed or written down rather than left implied.

## What this plan does

### Story 1 — the tags reach the check *(P0, shipped into PR #347 itself)*

Add `fetch-tags: true` to the extension job's checkout
([`ci.yml:159`](../.github/workflows/ci.yml#L159)). It is one input, it works alongside the default
shallow fetch, and it costs a tag list rather than a history.

**And make the tagless case loud instead of wrong.** The test currently treats an empty tag list as
"every version is a phantom". It must instead refuse to report: a checkout with no tags at all
cannot answer this question, and saying so names the cause and the fix. The repository's own
precedent is the right one — `StageRulesTests.RequireTheMount` fails where the thing must be there
and skips where it legitimately cannot be — and the comment at
[`ci.yml:166`](../.github/workflows/ci.yml#L166) states the house rule out loud: *"it FAILS here
rather than skipping, because a skip in CI is how a changed snippet merges behind a green tick."*

### Story 2 — the release body is the changelog section

**Not inside `draft-release.sh`.** That script has one job — create the draft once, or reuse the
one already there — and it is deliberately shared by three release lines. Reading and slicing
markdown inside it would put untestable text handling in a bash file that only runs on a release
day, which is the exact shape `TheArchiveCheckTests` exists to punish.

Instead: `.github/scripts/changelog-section.mjs <tag>` prints that release's section to stdout, or
the fallback sentence when there is none, and the workflow passes its output as the third argument
`draft-release.sh` already takes. `draft-release.sh` does not change at all.

**It reuses the guard's registry rather than parsing headings a second time.** A second
implementation of "which heading word belongs to this tag" is a defect from the moment it compiles,
because the two will drift and nothing will notice. `changelog-names-the-release.mjs` already
exports `lineOf`; the extractor imports it.

That needs one widening of `LINES`, and it is a better model than what is there now: **`word` and
`guarded` are orthogonal.** `word` says which heading this line's entries carry; `guarded` says
whether a missing entry blocks the release. Today only the guarded line has a `word`, which
conflates the two. After:

| prefix | `word` | `guarded` |
|---|---|---|
| `mcp-v` | `Server` | **true** |
| `extension-v` | `Extension` | false |
| `server-v` | `Team server` | false |
| `bugs-v` | *(none — this line has no headings in the file)* | false |

Measured from the file itself: 52 `## Extension`, 18 `## Server`, 1 `## Team server`, 51 bare `## `
(the early extension releases), and **zero** headings for `coai-bugs`. Adding a `word` changes no
guard behaviour, because the guard keys on `guarded`.

**Falling back to the literal sentence when the section does not exist** is not a nicety:
`extension-v*` and `server-v*` have 51 and 5 releases with no entry, `bugs-v*` has no heading shape
at all, and a release must not fail because of a gap this plan is not fixing.

Three details that decide whether this works:

- **The heading may be joint.** `## Extension 0.32.3 · Server 0.18.17 — 2026-09-10` describes both
  halves; used as the body of the Server release it is accurate, and it is what somebody wrote at
  the time. Use it as-is.
- **The section ends at the next `## `**, not at a blank line.
- **The backfill banner travels with it.** A reconstructed note should say so on the release page
  exactly as it says so in the file.

The extension line does not go through `draft-release.sh` at all and is out of scope here; it is
listed in the open tail.

### Story 3 — the baseline cannot shrink unnoticed

**Taken, by the operator on 2026-09-17.** A check that compares `.github/changelog-baseline.json`
against the copy on `origin/main` and refuses a row that disappeared. Roughly fifteen lines, and it
is what makes the claim "cannot lose them again" exact rather than nearly exact: without it the
promise holds against an accident and not against one commit that removes a note and its baseline
row together.

## Build order

1. **RED first for story 1**: run the phantom-tag case against a checkout with no tags — a temporary
   clone with `--depth 1 --no-tags` reproduces CI exactly — and watch it fail *for the wrong reason*
   (every version reported a phantom). That failure message is the evidence; it is what CI would
   have printed.
2. Make the case refuse to report on a tagless checkout; watch the message change to name the cause.
3. `fetch-tags: true` in `ci.yml`; re-run against the shallow clone and watch it pass for the right
   reason.
4. **RED for story 2**: cases over `changelog-section.mjs` — a tag whose section exists, a tag
   whose section does not, a joint heading, a section that runs to the next `## `, and a line
   with no `word` at all.
5. Widen `LINES` with `word` for every line; implement the extractor and the fallback; green.
6. Wire the three `*-draft` jobs to pass its output as the notes argument.
7. Story 3: the baseline cannot shrink.
8. `npm test` from a clean `out/`, the family checks, the MTP executable — no `src_mcp` change, so
   its count must not move from **2147**.
9. Commit by path; PR; the gate's code round.

## Test plan

| What | Where | Teeth |
|---|---|---|
| a tagless checkout makes the phantom case REFUSE, not mis-report | `changelogNamesTheRelease.test.ts` | the defect this plan opens with; reproduce with `--depth 1 --no-tags` |
| CI's own checkout now carries tags | `install.test.ts` workflow assertions | a test that only passes locally is the thing being fixed |
| the body of a release with an entry IS that entry | new cases over `changelog-section.mjs` | otherwise the guard makes people write into a void |
| a line with no entry, and a line with no `word`, fall back to the literal | same | 51 extension and 5 server releases have no entry and must not break |
| a joint `## Extension A · Server X` heading is used whole | same | it is accurate and it is what somebody wrote at the time |
| the section stops at the next `## ` | same | a body carrying the entire changelog is worse than the sentence |
| the baseline cannot shrink | new check over the origin/main comparison | the residual hole |

## Definition of Done

- [x] The phantom-tag case passes in CI for the right reason, and refuses to report on a checkout
      that cannot answer it.
- [x] A release body for a documented release is that release's changelog section.
- [x] A release with no entry still drafts, carrying the literal sentence.
- [x] Every new behaviour was observed RED before it was implemented, and the RED message is quoted
      in the summary.
- [x] `npm test` green (3290); family checks green; MTP count still 2147.
- [x] Symptom 3 is CLOSED by story 3: a commit removing a baseline row is refused.

## Decisions taken, 2026-09-17

Three questions were put to the operator before building, and all three are settled. They are
recorded here rather than in a chat log because each one is a thing a later reader will otherwise
reopen:

1. **The baseline row stays a required per-release edit.** Shipping `0.29.0` means writing the
   entry *and* adding `"0.29.0"` to `.github/changelog-baseline.json`; forget the second and the
   release is refused although the note is there. That friction is the ratchet closing behind each
   release, and it is accepted deliberately — the refusal names the file and the exact string, so
   the repair is seconds. **Do not "simplify" this later without reopening the decision.**
2. **`extension-v*` and `server-v*` stay unguarded.** Their gaps — 51 and 5 — are the ordinary
   kind: a patch release beside a documented one, which is this file’s long-standing and
   legitimate convention. Guarding them would make many historical releases retroactively
   unshippable for no gain.
3. **Story 3 is taken**, so the ratchet’s remaining hole is closed rather than documented.

## Deviations from the plan as written

**`draft-release.sh` DOES change, and the plan said twice that it would not.** The plan argued that
markdown handling does not belong in a bash file shared by three release lines — still true, and the
extraction is still a separate Node script. What the plan got wrong is the transport: it had the
extractor print to stdout and the workflow pass that output as an argument. Three reviewers
independently said a release body cannot travel as a shell argument — multiline, quotes, backticks,
and a line beginning with `-` that an argument parser reads as a flag. So the body is written to a
file and `draft-release.sh` takes a PATH and uses `gh release create --notes-file`.

**A re-run over an existing draft now rewrites its notes, which the plan never considered.** Raised
by one reviewer, and it is the finding worth the round: `draft-release.sh` deliberately reuses a
draft it has already made rather than creating a second one. A first run that drafted with the
fallback and then failed would leave that body on the release for ever, however often it was
retried. It calls `gh release edit --notes-file` on the reused draft now.

**The three drafting jobs gained `actions/setup-node`.** They call `node` and named no version, so
they ran on whatever the runner image happened to ship — an undeclared dependency on a job whose
failure mode is a release with no notes. That was already true of the guard shipped earlier the same
day; this is where it got fixed.

**Story 3's base revision is the pull request's own `base.sha`, not `origin/main`.** Two reviewers
called the plan's `origin/main` Blocking, independently and for two different reasons: a shallow CI
checkout has no such ref at all, and once a deletion has landed on main the comparison is a commit
against itself. It runs only on `pull_request`, and a base carrying no baseline is a PASS —
otherwise the commit introducing the file could never merge.

**The section ends at the next RELEASE heading, not at the next `## `.** Measured first: every `## `
in this changelog today IS a release heading, so the naive rule is not yet wrong. It would break on
the first note carrying its own `## Breaking changes`, which costs nothing to prevent now.

**`word` and `guarded` became orthogonal in the registry.** Giving `extension-v` and `server-v` a
heading word does not start guarding them — the operator's decision that they stay unguarded is
unchanged, and a test asserts exactly that, because "it now has a word" is the shape of an accident
that would start blocking 51 releases.

## What was built

| | |
|---|---|
| `.github/scripts/changelog-section.mjs` | a tag → its changelog section, or the fallback sentence |
| `.github/scripts/baseline-only-grows.mjs` | the ratchet's other direction |
| `draft-release.sh` | `--notes-file`, and a reused draft has its notes refreshed |
| `release.yml` | three jobs: `setup-node`, extract to a file, pass the path |
| `ci.yml` | `fetch-tags: true`; the baseline-shrink check on pull requests |
| `changelog-names-the-release.mjs` | `LINES` exported, `word` for every documented line |

Tests: `changelogSection.test.ts` (12), `draftReleaseNotes.test.ts` (5, running the real script
against a stub `gh`), `baselineOnlyGrows.test.ts` (9), plus two cases in
`changelogNamesTheRelease.test.ts`. RED observed for every one before it was implemented: 12/12, 4/5
and 9/9 failing respectively.

## Verification

- `npm test` — **3290 passed, 0 failed**, 1 skipped, from a cleaned `out/`
- `plan-lifecycle`, `pin-check`, `adapter-check` — green
- `CoaiMcp.Tests.exe` — **2147 total, 0 failed**, unchanged: nothing here touches `src_mcp`
- `release.yml` and `ci.yml` parse as YAML, and the step order was read back per job

## The limit worth stating

None of this runs on GitHub's runners before it is merged. The shape of a release is asserted by
READING `release.yml`; the first real proof that a release body is now its changelog section is the
next release. That is the honest status, and it is why `draft-release.sh` is exercised by running the
real script against a stub `gh` rather than by reading it.

## The open tail

- The **extension release line** does not use `draft-release.sh`, so story 2 does not reach it. Its
  39 documented releases out of 90 are the largest readership of the four.
- The 33 `mcp-v*` releases below 0.18.15 keep no notes, by the previous plan's accepted decision.
