# PLAN — the seams story 3.3's round named, and the two gates that stopped meaning anything

> Status: **IMPLEMENTED, 2026-09-21.** All seven items built on `story/the-seams-the-round-named`.
>
> **What shipped differently from the plan**, which is the part worth reading:
>
> - Item 3 does NOT replace `{id, html}` with a typed payload, and the plan says why: the literal
>   reading would move `callsBlock.ts`'s markup generation into a template literal of ES5. What
>   shipped is `livePatch.ts` owning the message type, the container attribute and the selector,
>   with the page script's query INTERPOLATED from the same constants. Better than planned, as it
>   turned out: planting a hardcoded attribute back into the generator is refused by the COMPILER,
>   not by a test.
> - Item 7 was not in the original plan at all. It came out of plan-round finding 0 and is true:
>   `timeout-minutes` was on ONE job across eight workflows.
> - Two tests were WRONG before they were right, and both are recorded in `module_tests.md`
>   because the lesson belongs to the test: the retry scenario measured its own harness
>   (`spawnSync` blocks the thread its local server runs on), and the ratchet's `--base-absent`
>   case failed against a script that would have refused the very commit introducing the file.
> - `bugzLiveContract.test.ts` read `ReviewPair`'s declaration out of `bugzReviewPage.ts` by path.
>   The suite named it the moment the declaration moved, which is the mechanism item 2 was for.
>
> Scope: `src_vs_code/src`
> (the review page's live-patch channel, `ReviewPair`'s home, `eslint.config.mjs`),
> `.github/workflows` (`sonarcloud.yml`, `ci.yml`), and one decision recorded into `research/`.
>
> Related docs: [module_extension.md](module_extension.md),
> [module_tests.md](module_tests.md),
> [PLAN_the_review_page_can_be_read.md](PLAN_the_review_page_can_be_read.md) (story 3.2c lives there
> and this plan closes it), [PLAN_family_ci_hardening.md](../todo/PLAN_family_ci_hardening.md) (the actionlint
> retry is mirrored from here).

## Why this exists

Story 3.3's code rounds produced five findings that were **right about the code and wrong about the
scope**: each named a real defect that lives in a SIBLING as well, so fixing only the new half would
have left the panel holding two conventions. They were rejected in the round with that reason and
carried out as questions. The operator has now answered all of them, and this plan is the answer.

Two of the items are not about the review page at all. They are about **gates that have stopped
carrying information** — a Sonar gate that is permanently red for another repository's code, and a
retry window two orders of magnitude too short for the outage it was written for. A gate nobody can
read is worse than no gate, because it is still consulted.

Nothing here changes what the product does for a person. Every item is a seam.

## What the plan round changed, 2026-09-21

The round returned **14 findings** and `good_enough` (one round, threshold 6). **Ten accepted, four
rejected with reasons.** What they changed, because a plan that hides its own corrections is worth
less than the round that produced them:

- **A contradiction in this document's own Definition of Done**, found by two reviewers
  independently: item 5 said *add* `.agents/conventions/**` to `sonar.exclusions`, and the checkbox
  said it must be *out of* `sonar.exclusions`. Read as a checklist that is the opposite instruction,
  and it would have left the red gate exactly as it is. Fixed below.
- **Item 2's "compiler-verified" was a claim, not a mechanism**: leaving the declaration in
  `bugzReviewPage.ts` as a re-export would let a missed importer compile. The declaration is now
  DELETED from the renderer, which is what turns a missed importer into a compile error.
- **Item 4's monotonic count could be gamed** — fix one old violation, add one new, regenerate, and
  the count falls while new code is suppressed. The check is now entry-level against a baseline,
  deletions only.
- **Items 5 and 6 had observations where they needed checks.** Both now carry a structural assertion
  over the whole condition, so removing the exclusion or the retry flags goes red.
- **A seventh item appeared** out of finding 0, and it is true: `timeout-minutes` is set on exactly
  ONE job in `ci.yml` (`:359`, the actionlint job). Every other job falls back to GitHub's 360-minute
  default, so a hung test burns six hours of runner time before anything stops it.

Rejected, each checked rather than argued: the claim that ESLint has no suppressions flags and never
reached 10.10.0 (measured: `v10.10.0`, the flags are in `--help`, and a generated file makes a plain
`eslint src` exit 0); the claim that the suppressions file is ignored without
`--suppressions-location` (measured: it is read from the default location, so `npm run lint` needs no
change); a request to assert `document.activeElement` in tests that run against a shim with no focus
model; and a request for a commit reference inside the very commit that records it.

## The seven items, in build order

Build order is by blast radius, smallest first, so that a mistake is found while the diff is still
small. Items 1–4 are one package; 5, 6 and 7 are independent and can land in any order.

### 1 — `showRevisionActions` skips identical HTML

**Symptom.** `.coderabbit.yaml:112` states this page's rule: *live patches must skip identical HTML*.
`showCalls` obeys it since story 3.3's second round. `showRevisionActions`, **two functions above it
in the same file**, does not — `bugzReviewPage.ts:549-553` assigns `innerHTML` unconditionally.

**Why it matters.** Assigning `innerHTML` the same string still destroys and recreates the subtree, so
a person who has tabbed onto a revision button loses it whenever a repeated or superseded answer
arrives. `RevisionPanel` fans one answer out per repository, so the repeated post is the normal case
here, not an edge.

**The change.** The same guard `showCalls` has: compare before assigning.

**The test.** `bugzReviewPage.test.ts` can now DRIVE a live patch — story 3.3's second round taught the
shim `[data-calls-for]` and gave `Note` a write counter, and `Host.push` was always able to deliver a
message. The shim's `querySelector` already answers `[data-revision]`, so this needs the same two
tests `showCalls` has: a patch lands in its own container and no other; an identical patch is skipped.

### 2 — `ReviewPair` moves to a module that owns the model rather than renders it

**Symptom.** `ReviewPair` is declared in `bugzReviewPage.ts:68` — the module whose job is to RENDER the
page. Ten modules import it (`bugzReviewPanel`, `callsPanel`, `corpusLanguage`, `realMethodView`,
`reviewAbout`, `reviewTabs`, `revisionActions`, `revisionPanel`, `roundsDbRead`, and the page itself),
so loading the calls STATE also pulls in the page renderer and everything it needs to make HTML.

**Why it matters.** It is a layering inversion that is invisible until it bites: an alternate view, or
a second page over the same rows, would have to import the renderer to get the row type. A code-round
reviewer named it on `callsPanel.ts` and I rejected it there for a reason that still holds — doing it
for the newer of two siblings would leave the panel holding two row models. Doing it for both is the
move that was always correct.

**The change.** A new `reviewPair.ts` holding `ReviewPair` and nothing else. **The declaration is
DELETED from `bugzReviewPage.ts` and not re-exported** — that is the whole mechanism, and the plan
round was right to say so: a re-export would let a missed importer keep compiling against the
renderer, and "compiler-verified" would be a claim rather than a check. With the declaration gone,
every one of the ten importers must point at the new module or the build fails by name.

**The test.** None of its own — this is a move, with no behaviour to assert. The compiler is the
check once the declaration is deleted, and `bundledPage.test.ts` already proves the shipped bundle
still loads. Said here rather than skipped silently, per the testing rule.

### 3 — one owner for the live-patch channel, used by BOTH panels

**Symptom.** A live patch is three facts that must agree, and they are written in three places with
nothing tying them together:

| fact | written in | and again in |
|---|---|---|
| the container attribute | `reviewAbout.ts:95` (`data-revision`), `:96` (`data-calls-for`) | `bugzReviewPage.ts:551`, `:560` (the page script's `querySelector`) |
| the message type | `bugzReviewPanel.ts:320` (`'calls'`), `:322` (`'revisions'`) | `bugzReviewPage.ts:567-568` (the page script's dispatch) |
| the item shape | `callsPanel.ts:25-28`, `revisionPanel.ts:53-56` | the page script's `items[i].id` / `.html` |

**Why it matters, and what it is NOT.** The reviewer asked for a *typed* payload instead of
`{id, html}`. Taken literally that means the PAGE renders, which means `callsBlock.ts`'s markup
generation moves into a script that is a template literal of hand-written ES5 — a large step
backwards. What the finding is actually about is that **a renamed attribute is a runtime failure
today**: the generator and the query drift, the patch writes nowhere, and the row silently keeps
showing a stale answer. That is worth fixing, and it is fixable without moving the rendering.

**The change.** A `livePatch.ts` owning, per channel, the message type string, the container attribute
and a `containerFor(id)` selector builder — and the page script's `querySelector` string is
INTERPOLATED from the same constants when the page is generated, not typed out a second time. Renaming
then breaks the compile in one place and changes both sides together. `{id, html}` stays; it becomes a
named, shared type instead of two structurally identical anonymous ones.

**The test.** A page test that renames nothing but proves the two sides agree: the container the page
GENERATES is the container a patch FINDS, for both channels. Teeth: change the attribute in the
generator only, and the test must go red. (That is the whole condition — pinning only the string would
survive its own break.)

### 4 — `complexity: 4` and `max-lines-per-function: 50`, on for new code

**Symptom.** `eslint.config.mjs:37-41` records why they are off: measured at **451** and **49**
violations, and the note reasons that *"451 is not a boundary, it is a wholesale rejection of how this
package is written"*. The reasoning was right and the conclusion has expired — the package has grown
since, and the rules now report **770** across **236 files** (measured 2026-09-21:
complexity **644**, max-lines-per-function **126**; **615** in production across 167 files, **155** in
tests across 69).

**Why an allowlist is the wrong mechanism.** "For new files" has no ESLint expression. A hand-kept
`files:` allowlist is a list somebody must remember to extend, and the day it is forgotten the rule
silently stops applying to exactly the code it was turned on for.

**The change.** ESLint here is **10.10.0**, which has a suppressions file
(`--suppress-all`, `--suppressions-location`, `--prune-suppressions`). So: turn both rules ON for
`src/**`, leave them OFF for `src/test/**` (tests narrate — the same reason `max-lines` is already off
there, `eslint.config.mjs:101-107`), and commit `eslint-suppressions.json` holding the 615 existing
production violations. New code is covered with nothing to remember; existing violations are recorded
rather than forgiven.

**The commands**, named rather than implied — `npm run lint` itself needs NO flag, which was
measured: after generation, a plain `eslint src` exits 0 because ESLint reads
`eslint-suppressions.json` from its default location.

| purpose | command |
|---|---|
| generate the baseline, once | `npx eslint src --suppress-rule complexity --suppress-rule max-lines-per-function` |
| drop entries whose violation was fixed | `npx eslint src --prune-suppressions` |
| everyday check, CI included | `npm run lint` (unchanged) |

**The test.** Not a count — **entries**. A monotonic count is gameable and a reviewer showed how: fix
one old violation, add one new, regenerate, and the count falls while new code is suppressed. So the
test compares the committed suppressions against the baseline **entry by entry** and permits only
DELETIONS: a file/rule pair that was not suppressed before may never become suppressed. Same shape as
`notification-sites.json`'s "the count only ever falls", one level more precise because here the
identity of the entry is what matters.

### 5 — Sonar stops scanning another repository's code

**Symptom.** The PR gate and the main-branch gate say different things, and only the PR one is looked
at. Measured 2026-09-21 on `main`: gate **ERROR**, `new_reliability_rating=4` and
`new_security_rating=3` against a threshold of 1, from **32** new-code bugs and vulnerabilities.
Where they live:

| count | area |
|---|---|
| **17** | `.agents/conventions` — a SUBMODULE, its own repository, its own Sonar project |
| 5 | `src_mcp/core` |
| 4 | `src_vs_code/src` |
| 3 | `.github/workflows` |
| 3 | `src_mcp/src`, `src_mcp/runners`, `src_bench` |

**Why it matters.** More than half of a permanently red gate is code that cannot be fixed from this
repository. A gate that is always red reports nothing, so a real regression on `main` arrives
invisible. (Story 3.3's own PR passed at 1/1 with 99.2 % new-code coverage while `main` was red the
whole time — that is the failure mode, exactly.)

**The change.** Add `.agents/conventions/**` to `sonar.exclusions` (`sonarcloud.yml:179`) — the
ANALYSIS list, not the coverage list, because the point is that these files are not this project's to
analyse. The existing comment block at `:55-62` explains every exclusion already present; this one
gets the same treatment, because an exclusion is a decision to stop looking and must say why.

**The check**, deterministic rather than observed — the plan round was right that "re-query and
record" is an observation that can silently not happen. A test asserts the exclusion is PRESENT in
`sonarcloud.yml`'s `sonar.exclusions` value, pinning the whole condition so that deleting it goes red.
(An assertion over Sonar's own API from CI was the reviewer's suggestion; it needs a token, a network
call and a scan to have finished, to guard a one-line list. The structural pin catches the only way
this regresses — somebody editing the line.)

**The observation, made 2026-09-21 after the change landed on `main`.** It did what it claimed and
not more: **32 new-code findings became 15**, and **every one of the 17 in the submodule is gone**.
The gate is still `ERROR` — `new_reliability_rating 3` and `new_security_rating 3` — because the 15
that remain are real and unfixed. That is the point. They are now a list somebody can work instead
of noise nobody could:

| severity | where | what |
|---|---|---|
| MAJOR VULNERABILITY | `.github/workflows/ci.yml:336` | Omitting "--ignore-scripts" allows lifecycle scripts to run during package installation. |
| MAJOR VULNERABILITY | `.github/workflows/ci.yml:273` | Omitting "--ignore-scripts" allows lifecycle scripts to run during package installation. |
| MAJOR VULNERABILITY | `.github/workflows/release.yml:519` | Omitting "--ignore-scripts" allows lifecycle scripts to run during package installation. |
| MINOR VULNERABILITY | `src_bench/CoaiBench/Running/Git.cs:11` | Use an absolute path for this command. |
| MINOR VULNERABILITY | `src_mcp/core/Commands/PlanShape.cs:60` | Pass a timeout to limit the execution time. |
| MINOR VULNERABILITY | `src_mcp/core/Commands/PlanShape.cs:61` | Pass a timeout to limit the execution time. |
| MINOR VULNERABILITY | `src_mcp/core/Commands/PlanShape.cs:62` | Pass a timeout to limit the execution time. |
| MINOR VULNERABILITY | `src_mcp/core/Commands/PlanShape.cs:63` | Pass a timeout to limit the execution time. |
| MINOR VULNERABILITY | `src_mcp/core/Commands/PlanShape.cs:67` | Pass a timeout to limit the execution time. |
| MINOR VULNERABILITY | `src_mcp/runners/Reviewers/ReviewerExecutor.cs:129` | Pass a timeout to limit the execution time. |
| MAJOR VULNERABILITY | `src_mcp/src/Store/RoundsQuery.cs:790` | Use a parameterized query instead of string formatting. |
| MAJOR BUG | `src_vs_code/src/cliChatSession.ts:657` | This conditional operation returns the same value whether the condition is "true" or "false". |
| MINOR VULNERABILITY | `src_vs_code/src/installer.ts:345` | Make sure the "PATH" variable only contains fixed, unwriteable directories. |
| MINOR VULNERABILITY | `src_vs_code/src/installer.ts:356` | Make sure the "PATH" variable only contains fixed, unwriteable directories. |
| MAJOR VULNERABILITY | `src_vs_code/src/panelProvider.ts:3629` | Make sure that using this pseudorandom number generator is safe here. |

Reading them: **three are one rule** — `npm ci` without `--ignore-scripts` in the two workflows that
install; **six are one rule** — a process started without a timeout, five of them in `PlanShape.cs`;
**two are one rule** — `PATH` in the installer. So fifteen findings are really **seven decisions**,
and one of them (`RoundsQuery.cs:790`, string formatting in a query) is the kind that deserves
looking at first. None belongs to this change; `new_coverage` is 95.9 %.

### 6 — the actionlint download survives an outage that lasts longer than six seconds

**Symptom.** `ci.yml:360-362` fetches actionlint with
`--retry 3 --retry-delay 2 --retry-all-errors`: one attempt plus three retries at a FIXED two-second
delay. Measured failure on `main`, 2026-09-21: four consecutive `504`s between 14:53:19.86 and
14:53:25.89 — **6.08 seconds for the whole budget**.

**Why it matters.** The retry was added for this exact class of failure; the comment at `:355-359`
says so, naming a 500 from the release host that failed an unrelated Dependabot PR. But a 504 from a
CDN is a server-side outage measured in minutes. The guard was built and then given a window two
orders of magnitude too short to do its job, so it fails looking like it worked.

**The change.** Drop the fixed `--retry-delay 2` so curl uses its own exponential backoff
(1, 2, 4, 8, 16 s), raise `--retry` to 5, and cap with `--retry-max-time 120`. That is ~31 s of
backoff under a 120 s ceiling, inside a job whose `timeout-minutes` is 5. The checksum still decides
what runs, so this widens the window without widening what is trusted.

**The check.** Two, because the flags are the whole fix and a silently dropped flag looks like
success: a structural assertion pinning the WHOLE argument list (`--retry 5`, no `--retry-delay`,
`--retry-max-time 120`, `--retry-all-errors`) in `ci.yml`, and a runnable scenario that stands up a
local server answering `504` three times and then `200`, runs curl with exactly that argument list,
and asserts it succeeds. The second is what makes the first more than a spelling check.

**Scope: this repository only.** The plan round was right that the text claimed family-wide hardening
while only one workflow was in scope. The same step exists in `dew_flow_rag_qln`, `dew_flow_mcp`,
`dew_flow_benchmark`, `dew_flow_sidecar_rust`, `dew_flow_creds_for_devs` and `dew_flow_conventions`;
each is a separate checkout and a separate PR, so this change lands HERE and is recorded as an
enumerated item in [PLAN_family_ci_hardening.md](../todo/PLAN_family_ci_hardening.md) naming those six repos
and this fix. `conventions` holds shared *guidance*; CI hardening belongs to the repos themselves.

### 7 — every CI job has a timeout

**Symptom.** `timeout-minutes` appears **once** in `ci.yml` — at `:359`, on the actionlint job. Every
other job, `build · test · family checks` and `extension · typecheck · test · package` included, falls
back to GitHub's default of **360 minutes**.

**Why it matters.** The suites here are two to four minutes. A test that hangs — a webview message
that never arrives, a Testcontainers image that never pulls, a process spawned and never reaped —
burns six hours of a runner before anything stops it, and the pull request looks like it is still
working the whole time. The plan round found this while reading the test plan, and it is the one item
here that nobody asked for.

**The change.** `timeout-minutes` on every job, generously above the measured duration — twenty
minutes for the suites, five for the short jobs. Not a performance target; a ceiling on a hang.

**The check.** A test that every job in every workflow declares `timeout-minutes`. Structural, and it
covers workflows added later, which a one-time fix does not.

## Also in this change, and not a code item

**Story 3.2c is CLOSED, not built** — the operator's decision, 2026-09-21. It was *"the new window
lands on the method"*. The reason it is closed rather than done is worth recording in `research/`
beside the two stories that were built:

- VS Code gives an extension **no way to run a command in another window**, so file-and-line cannot be
  handed to the window `openTreeFolder` opens. `vscode.openFolder` takes a folder URI; a
  `vscode://file/path:line` URI opens in the LAST FOCUSED window, which is a race, not a target.
- The only honest route is a handoff through disk — a record beside 3.2a's tree record, read by the
  new window at activation, acted on and deleted — which brings its own lifetime rules: the window
  that never opened, the person who changed their mind, the tree removed in between. Lifetime is
  precisely what made story 3.2 need a three-way split.
- And the justification that carried 3.2a and 3.2b does not carry this one. Those rest on a measured
  fact — `head_sha` orphaned **55.7 %** of the time — that says *without a tree you cannot reach the
  code at all*. Here the code is already reachable; what is saved is navigation.

Epic 3 of [PLAN_the_review_page_can_be_read.md](PLAN_the_review_page_can_be_read.md) is therefore
**complete**, and its row says so. The plan itself stays in `todo/` because epic 4 is open.

## Test plan

| item | how it is proved |
|---|---|
| 1 | two page tests, driven through `Host.push`; teeth by removing the guard |
| 2 | the compiler, plus the existing bundle test; no behavioural test, said explicitly |
| 3 | a page test that the generated container is the container a patch finds, both channels; teeth by renaming the attribute in the generator alone |
| 4 | a test that the suppression count only falls; teeth by adding one |
| 5 | a test that the exclusion is present in `sonar.exclusions`; then the `main` gate re-queried once and the remainder recorded |
| 6 | a structural pin of the whole argument list, plus a scenario with a local server answering 504 three times then 200 |
| 7 | a test that every job in every workflow declares `timeout-minutes` |

Whole-suite before anything is reported done: `npm test` in `src_vs_code`, the `CoaiMcp.Tests`
executable, `npm run lint`, `npx tsc --noEmit`, and the five family checks.

## Definition of Done

- [ ] `showRevisionActions` skips identical HTML, with the same two tests `showCalls` has.
- [ ] `ReviewPair` lives in its own module and no importer reaches through the renderer for it.
- [ ] One module owns each live-patch channel's type, attribute and selector, and the page script's
      query is interpolated from it rather than written a second time.
- [ ] Both rules are on for `src/**`, off for `src/test/**`, with a committed suppressions file and a
      test that compares its ENTRIES against the baseline and permits only deletions.
- [ ] `.agents/conventions/**` is **present in** `sonar.exclusions` — so those files are no longer
      analysed as this project's — with the comment block saying why, a test pinning it, and the
      remaining main-gate findings recorded.
- [ ] The actionlint fetch backs off exponentially under a 120 s ceiling, pinned structurally and
      exercised against a local 504 server, and the mirror is recorded in the family CI plan naming
      the six other repositories.
- [ ] Every job in every workflow declares `timeout-minutes`, with a test that keeps it true.
- [ ] Story 3.2c's closure is recorded in `research/`, and epic 3's row says it is complete.
- [ ] `research/module_extension.md` and `module_tests.md` updated; every suite green.
