# PLAN — CI hardening across the dew_flow family: formatting gates, supply chain, PR culture, releases

> Status: **partially implemented, 2026-09-18.** Repository settings applied 2026-09-05. Epic 3 (pins,
> the persisted credential, dependabot, PR template, semantic titles) is shipped, and so are Epic 5
> steps 0–2 — step 0's table is in this document as of 2026-09-18, which is what that step asks for
> and what the previous status line claimed before there was one. Epic 2 is shipped, its one blocked
> line resolved 2026-09-18. Epic 1 is AUDITED but NOT APPLIED: the gaps are tabled in its own section
> and writing branch protection needs the operator. Epic 4 is NOT started, and Epic 5 step 3 — the
> step that actually closes the write-token hole — waits on it.
> Scope: every `dew_flow_*` repository's `.github/` (workflows, dependabot, PR template),
> `.editorconfig` where missing, and `dew_flow_conventions` for the rule that binds them.
>
> Related docs: [research/PLAN_local_db.md](../research/PLAN_local_db.md) (unrelated, same week);
> `dew_flow_conventions/common/pull-requests.md` (the rule this plan enforces mechanically);
> [module_bench.md](../research/module_bench.md) — the bench stays out of every release.

## The ask, 2026-09-05

The operator connected CodeRabbit to the three public repositories, closed `main` to direct pushes,
and then listed what a serious repository has beside a reviewer: formatting checked in CI without
auto-formatting; secret scanning with push protection; Dependabot; a PR template and semantic PR
titles; release-please; and a branch ruleset that requires every check, an up-to-date branch, and no
bypass for admins. *"Вот это всё тоже включи и настрой для всех дев флоу проектов."*

## The family, audited 2026-09-05

| repo | stack | CI today | formatting check | dependabot | PR template | notes |
|---|---|---|---|---|---|---|
| `dew_flow_connect_other_ais` | .NET 10 AOT + TS extension + .NET bench | build·test·family checks, extension | none | none | none | public, CodeRabbit |
| `dew_flow_conventions` | markdown + node tools | **none until PR #1** (tools selftest) | none | none | none | public, CodeRabbit |
| `dew_flow_creds_for_devs` | .NET 10 server + TS extension + .NET cli/mcp/broker | server, extension (path-filtered), docs | eslint (extension only) | none | none | public, CodeRabbit; **cli/mcp/broker tests never run in CI** |
| `dew_flow_rag_qln` | .NET 10 + TS extension | build-test, contract (postgres), extension, plans | none | none | none | **private** — secret scanning unavailable without Advanced Security |
| `dew_flow_mcp` | .NET 10 | build-test, contract, plans | none | none | none | public |
| `dew_flow_benchmark` | .NET 10 | build-test, contract (postgres), plans | none | none | none | public |
| `dew_flow_sidecar_rust` | Rust | build-test (matrix), contract, plans | `cargo fmt --check` ✓ | none | none | public |

No `.editorconfig` anywhere; no Python (ruff is not applicable).

## Done already — repository settings, via the API (2026-09-05)

- **Branch protection on `main`** (classic protection; the same flags a ruleset carries): pull request
  required with zero approvals (one human, who cannot approve their own PR), `enforce_admins`,
  required linear history, force-push and deletion blocked, **required conversation resolution**,
  **required status checks with `strict: true`** (the branch must be up to date with `main`).
  Applied to `connect_other_ais`, `conventions`, `creds_for_devs`; the other four in epic 1.
- **Merge methods**: merge commits off; rebase and squash on; delete branch on merge; auto-merge allowed.
- **Secret scanning + push protection**: enabled on every public repository. `dew_flow_rag_qln` is
  private and GitHub refuses it there (422) — it needs Advanced Security or the repository made public.
- **Dependabot alerts + automated security fixes**: enabled on all seven.

## Measured since — CodeRabbit does not review these repositories, and the workflow built to fix that does not work either (2026-09-06)

The plan above counts CodeRabbit as one of the three things a PR is tested by. On every PR opened
since, it answers instead:

> This repository does not receive automatic reviews because it has **fewer than 10 stars**.

`coderabbit-review.yml` exists precisely for this — it posts `@coderabbitai review` on every opened
PR — and it **does not work**. Observed on PR #48:

| | |
|---|---|
| `07:53:09Z` | `coderabbitai[bot]` posts the skip notice and a *Trigger review* checkbox |
| `07:53:10Z` | `github-actions[bot]` posts `@coderabbitai review` — one second later |
| result | no review; the `ask CodeRabbit` job is **green** |

**The cause is now known, and it is the author.** The experiment this entry asked for was run the
same morning, by the operator, on PR #45 — the same command text posted twice by different accounts:

| | | |
|---|---|---|
| `2026-09-05 21:31:12Z` | `github-actions[bot]` posts `@coderabbitai review` | **nothing happens** |
| `2026-09-06 08:25:13Z` | `oleksandrdubyna88` posts `@coderabbitai review` | CodeRabbit replies in **4 seconds** and reviews the PR |

Identical text, identical repository, identical PR. CodeRabbit ignores the command from a bot account
— as most such integrations do, to avoid loops — and honours it from a person. So timing was never
the problem and no amount of retiming would have helped: `coderabbit-review.yml` posts as
`github-actions[bot]` and therefore cannot work as written, in any repository of this family.

It also means the reviews are NOT gone below ten stars: a human `@coderabbitai review` gets a full
review, which is how #45's three findings were raised. The threshold removes the *automatic* review,
not the ability to ask.

And: **a green check for a review that did not happen is worse than no check**, because green next to
the words "ask CodeRabbit" is exactly what a reader takes for "a reviewer looked at this".

Now that the cause is certain, the options are concrete — and the first two are no longer about the
stars at all:

| | Costs | Buys |
|---|---|---|
| post the comment as a PERSON — a PAT in a secret, used by the workflow instead of `GITHUB_TOKEN` | one token to mint and rotate | the workflow does what it was written to do |
| delete `coderabbit-review.yml` | nothing | one less green check that means nothing; asking becomes a human habit |
| ten stars, or a paid plan | asking people, or money | automatic review with no comment needed |

**Which of the first two is the operator's call** — a PAT in CI is a real credential with a real blast
radius, and trading it for an automatic comment is a judgement about this repository, not a technical
fact. What must not survive either way is the present state: a job that reports success for a comment
that provably does nothing.

## What must be true when this is done

1. Every repository's CI fails a PR whose code is not formatted to the repository's rules, without
   reformatting anything: `dotnet format --verify-no-changes` (with an `.editorconfig` that states the
   rules, committed once with the one-time reformat), `eslint` for every TS package, `cargo fmt --check`
   (already), `actionlint` for workflows.
2. Every runnable test project runs on every PR — including `creds_for_devs`' cli, mcp and broker
   tests, which have never run in CI — and the path filters that let a PR skip a job are gone from the
   `pull_request` triggers, so every job can be a **required** check.
3. Dependabot version updates: `.github/dependabot.yml` per repository, grouped (NuGet minor+patch in
   one PR, npm in one, cargo in one, github-actions in one), weekly, with the family's pinned
   exceptions respected (FluentAssertions stays 7.2.2; Aspire SDK and hosting bump as a pair).
   Renovate is not added: Dependabot with groups covers the ask without a second bot to configure.
4. A PR template with the checklist the rules already demand: what changed, the red-green test, the
   docs/README/manifest touched, the DoD of the rule the change is under, what the reviewer said that
   was not acted on and why.
5. Semantic PR titles enforced by `amannn/action-semantic-pull-request` (types: feat, fix, docs, test,
   chore, refactor, perf, ci, build) — a required check.
6. **release-please in manifest mode**, one component per released artefact with the tag shapes the
   release workflows already trigger on (`mcp-v*` / `extension-v*` in `connect_other_ais`; `server-v*`
   and the extension in `creds_for_devs`; `v*` in the sidecar): it opens a release PR that bumps the
   manifest (and `package.json` for the extensions), and on merge cuts the tag that the existing
   release workflow builds. **The narrative `CHANGELOG.md` stays hand-written**; release-please writes
   its generated notes to `RELEASES.md` (`changelog-path`), because a changelog assembled from PR
   titles is the record this family deliberately does not keep.
7. The branch protection on all seven repositories requires every job above.
8. **BLOCKED on one operator decision — do not build this before it is answered.**
   `dew_flow_conventions` carries the rule (`common/pull-requests.md`, already in PR #1) and a tool,
   `tools/repo-settings-check.mjs`, that reads a repository's protection and settings through `gh api`
   and fails when a flag above is off — so the settings cannot drift back silently.

   The RULE is settled and belongs there. **The TOOL's home is not.** The operator ruled on
   2026-09-17 that conventions says how to develop and "must not be responsible for following the
   rules" — and a settings checker is exactly enforcement. Against that: the submodule already hosts
   `pin-check`, `plan-lifecycle`, `adapter-check` and `ownership-check`, so the practice exists and
   the ruling was given about a POLICY, not about those tools. The two readings differ and only the
   operator can settle it. The choices and what each costs:

   | | where the tool lives | cost |
   |---|---|---|
   | A | `dew_flow_conventions/tools/` | consistent with four existing tools; one conventions commit buys a six-consumer pin cascade every time it changes |
   | B | one repository, run against all seven | consistent with today's ruling; a seventh place to look, and nothing mounts it |

   Until one is recorded here, Epic 1 item 3 is blocked.
9. **The release path holds a write token and runs code a tag chooses.** Every checkout that does
   not push sets `persist-credentials: false`; every `uses:` line is pinned to a full 40-character
   commit SHA with the version in a trailing comment; and release tags are protected so a `mcp-v*`
   tag cannot be pushed against an arbitrary commit. See Epic 5 — raised by a security review on
   2026-09-17, with the cheap half already shipped.

   **Measured in `connect_other_ais` only.** The other six repositories have release workflows of
   their own and none of them has been inventoried, so this requirement is stated for the family and
   EVIDENCED for one member. Epic 5 step 0 is that inventory, and it comes before any of the rest —
   a builder who skips it will protect the wrong tag pattern somewhere.

## Epics

### Epic 1 — every PR runs every test, and the settings cover every repo

1. `creds_for_devs`: `ci-clients.yml` for cli/mcp/broker tests; `paths` filters removed from the
   `pull_request` triggers of `ci-server.yml` and `ci-extension.yml`; protection requires all jobs.
2. Protection + merge settings on `rag_qln`, `mcp`, `benchmark`, `sidecar_rust`, requiring their
   existing jobs (contract jobs included — they run on every PR with their own services).

   **AUDITED 2026-09-18, and the gap is wider than this line assumed.** The required-check list is
   hand-maintained and its failure mode is a GREEN result: a job not on the list runs, goes red, and
   the pull request merges anyway. Measured against the check names GitHub **actually reported** on
   recent merged pull requests — not against the workflows, because the string a check reports under
   is the whole contract, and a required name that never appears blocks every pull request forever:

   | repository | required today | runs but is NOT required |
   |---|---|---|
   | `rag_qln` | **none — and it CANNOT have any**, see below | `build-test`, `contract`, `extension`, `plans`, `pr · semantic title`, `workflows · actionlint` |
   | `creds_for_devs` | 3 | `build · test`, `clients · build · test` (×2 legs), `compose · scripts`, `http · contract suite`, `typecheck · test · package`, `SonarCloud Scan`, `workflows · actionlint` |
   | `connect_other_ais` | 3 | `SonarCloud Scan`, `pr · semantic title`, `workflows · actionlint` |
   | `mcp` / `benchmark` / `sidecar_rust` | 3 / 3 / 4 | `CodeQL`, `pr · semantic title`, `workflows · actionlint` |
   | `conventions` | 4 | `workflows · actionlint` |

   Verified safe to require: no `pull_request` trigger in any of the seven carries a `paths` filter,
   so none of these can fail to report; and `pr · semantic title` runs on Dependabot pull requests
   too, which is the case that would otherwise have been blocked permanently.

   **Deliberately NOT to be required**, because an unexplained omission is the next person's puzzle:
   `ask CodeRabbit` (a third-party free tier that runs out — requiring it puts every merge behind
   somebody else's quota), `github-advanced-security` and the `Analyze (…)` legs (umbrella and
   per-language parts of `CodeQL`, which the family already requires), `SonarCloud Code Analysis`
   (Sonar's own gate, distinct from the `SonarCloud Scan` job the family requires — making the gate
   blocking is its own decision), `extension · a real editor` (its workflow says in writing to promote
   it after twenty consecutive green runs on main), and `submit-nuget` (produced by no workflow in the
   repository, so when it runs is not something this plan knows).

   **`rag_qln`'s row is corrected, 2026-09-18, and the correction matters more than the row.** "No
   branch protection at all" reads as an omission. It is not one: the API answers **HTTP 403,
   *"Upgrade to GitHub Pro or make this repository public to enable this feature"*** — the repository
   is PRIVATE on a plan without Pro, and branch protection is unavailable there entirely. Same root
   cause as the secret scanning this plan already records it cannot have. Nothing can be applied to
   it until it is public or the plan changes, and that is a decision rather than a task. The audit
   said "nobody configured it" because the first pass asked the workflows instead of the API.

   **THE DESIRED STATE IS NOW A FILE IN EVERY REPOSITORY**, which is what this line should have said
   from the start: `.github/branch-protection.json` plus `.github/scripts/branch-protection.mjs`,
   shipped 2026-09-18. Branch protection is settings rather than content, so nothing makes GitHub
   read the file — what it buys is that the intent is REVIEWED and the drift from it is something a
   command prints. `--selftest` runs in CI without a token and caught three bugs in the tool's own
   normalisation on its first run; the comparison against the real branch needs repository admin and
   stays a command somebody runs, which is also what item 3 below asks for.

   **STILL NOT APPLIED.** Writing branch protection is a privileged action on the operator's
   repositories and needs their say-so; the file is the whole of the decision, so applying it is
   `--apply` once per repository once that is given.
3. **BLOCKED — see requirement 8.** `tools/repo-settings-check.mjs`, with its selftest; run by hand
   for now (it needs a token), documented in the README. Its HOME is the open decision: conventions
   (as written) or one repository. Do not build it until requirement 8 records the answer — the
   choice changes where the file goes, who mounts it, and whether changing it costs a pin cascade.

### Epic 2 — formatting gates

Per .NET repository: an `.editorconfig` encoding the rules `CLAUDE.md` already states (file-scoped
namespaces, `var`, expression bodies, records) — then ONE reformat commit (`dotnet format`), then the
`--verify-no-changes` step. Per TS package: eslint where missing (flat config, typescript-eslint,
recommended + no-floating-promises), then the `lint` step. `actionlint` everywhere.

**State, 2026-09-17.** `actionlint` is in all seven (one job, no new third-party action: the binary is
pinned by version and verified by checksum, because every `uses:` line had just been pinned to a SHA and
an unpinnable action would undo that). The formatting gate is merged in three of five .NET repositories
and open in `connect_other_ais` and `rag_qln`.

**What actionlint actually found, and the measurement that was wrong first.** The number reported when
this job was written — zero across all seven — came from a local binary with **no shellcheck on PATH**,
which makes actionlint silently drop the shellcheck-backed analysis of `run:` blocks: no warning, exit
zero. Its own rules survive (the injection and undefined-input checks both still fire); the shell
analysis is where everything was. True count with shellcheck present: **eighteen**, thirteen in
`connect_other_ais` and five in `creds_for_devs`, **every one in a release or deploy workflow** — the
files no CI run exercises because they only execute on a tag. All eighteen were fixed by changing the
shell, none by an ignore list, and the step now asserts `command -v shellcheck` so the same silent loss
cannot recur.

**The one blocked item is UNBLOCKED, 2026-09-18, by asking a question nobody had put to the code.**
`connect_other_ais/src_vs_code` was on `typescript ^7.0.2`; `typescript-eslint` 8.70.0 declares peer
`typescript: ">=4.8.4 <6.1.0"` — true of `latest` and of the alpha canary alike. So the largest TS
package in the family could not take the type-aware linter this epic specifies, and
`no-floating-promises` — named above as the acceptance criterion — is type-aware. Plain eslint would
have passed a gate while dropping the one rule that motivated it, so it was never a fallback.

It was recorded as blocked the same day, pending typescript-eslint. The question that resolved it was
the operator's: **does this package actually need TypeScript 7?** The bump was an ordinary
`chore(deps)` (`7c08d6a8`), and on the morning of 2026-09-18 the answer measured NO: on 5.9.3 the
source typechecked with zero errors and the suite passed 3583 to 0.

**That answer had a shelf life of six hours, and the correction is the more useful record.** Later
the same day `codeHighlight.ts` landed on main with `shiki@^4.4.3` — ESM-only (`"type": "module"`),
imported by subpath from a CommonJS module. `require()` of an ESM package is legal under TS 7's
semantics and NOT under 5.9.3, which answers `TS1479` five times; a rebase is what surfaced it. Both
escapes were measured and rejected: `moduleResolution: node16` fixes the resolution and then
TypeScript says the honest thing instead, and the page depends on the **synchronous**
`createHighlighterCoreSync`, so `await import()` would turn a synchronous highlighter asynchronous
through the page builder and its tests — a redesign of a day-old feature, not a CI change. Forcing
the peer range with `--legacy-peer-deps` was measured too: typescript-eslint refuses at RUN time, by
name, pointing at issue #10940.

**`typescript@6.0.3` is the one version that does both** — compiles this source including shiki
(6.0 already models `require(esm)`), and satisfies the parser's `<6.1.0`. Measured: zero typecheck
errors, suite **3756 to 0**. TypeScript 7 returns the day the parser supports it.

What the linter found on first contact is worth recording, because two of the three arguments were
settled by counting rather than taste:

| | measured | decision |
|---|---|---|
| `no-floating-promises` in `src/test` | 3,544 | `node:test`'s `test`/`it`/`describe` return a promise by design — `allowForKnownSafeCalls` names the PACKAGE, so a floating promise *inside* a `test()` body still fails |
| `complexity: 4` / `max-lines-per-function` / `no-console` | 451 / 49 / 109 | LEFT OUT. In `rag_qln` the same set found 33 across 7 files — a boundary you can name. 451 is a wholesale rejection of how the package is written, and belongs to its own decision |
| `no-await-in-loop` | 73, against 5 `eslint-disable` markers | LEFT OUT; the five decorative markers were removed instead |
| `max-lines` (800) | 8 files | kept — nameable, and each file says its measured size at the top |

And the escape findings were real defects rather than lint noise: `'C:\Users\strug'` in a JavaScript
string is `C:Usersstrug`, so **two tests asserted on Windows paths that were never Windows paths** and
passed because both sides carried the same mangling.

> **Consequence for Dependabot, still standing.** `rag_qln` has an open PR bumping ITS extension to
> TS 7, which would break the eslint config being added there. A compiler bump past the parser's
> supported range must not merge on its own: require a clean install plus a lint run on the proposed
> lockfile, with a floating-promise fixture proving the rule still fires.

### Epic 3 — dependabot, PR template, semantic titles

Three files per repository from one template each; the semantic-title workflow made a required check.

### Epic 4 — release-please

> **Planned separately, 2026-09-18:**
> [PLAN_release_please_meets_a_narrative_changelog.md](PLAN_release_please_meets_a_narrative_changelog.md).
> Configuring this epic as written below would have written over the thing it was meant to protect —
> `connect_other_ais`'s changelog is 122 entries of hand-written PROSE, and a guard shipped this week
> reads it to refuse a release whose version is not named in it. That plan measures the collision,
> gives four options with their costs, and answers the sidecar tag-shape question this section opens.
> **Read it before configuring anything here.**

`release-please-config.json` + `.release-please-manifest.json` per repository that releases, the
`release-please.yml` workflow, `changelog-path: RELEASES.md`, `include-component-in-tag: true`,
`tag-separator: "-"` so the tags are `mcp-v0.17.4`. Measured on one release before the others adopt
it: the tag it cuts must trigger the existing release workflow and produce the same artefacts.

**The action it adds is pinned like every other**, by requirement 9: `googleapis/release-please-action`
at a full commit SHA with `# v4` after it, not `@v4`. This epic used to say `@v4`, which requirement 9
now forbids — a plan that introduces a tag-pinned action while requiring SHA pins teaches the next
builder that the requirement is optional.

**The sidecar's tag shape is an open conflict, not an oversight.** Requirement 6 records that
`dew_flow_sidecar_rust` already releases on `v*`, and `include-component-in-tag: true` produces a
component-prefixed tag instead — so applying this epic unchanged cuts a tag its own release workflow
does not trigger, and the release silently produces nothing. Decide one of two before configuring it:
keep `v*` for the sidecar as a per-repository override, or change that workflow's trigger to the
prefixed shape and say so in its own PR. Whichever is chosen, the acceptance test below must run
against the sidecar as well as `dew_flow_mcp`.

### Epic 5 — the release path stops handing a write token to tag-controlled code

**Raised 2026-09-17** by CodeRabbit's security review on PR #347, as CWE-522, *Insufficiently
Protected Credentials*. The shape of it: the release jobs run scripts **from the checkout** while
holding `contents: write` and a `GH_TOKEN`, and a tag can point at any commit. A tag pushed against
an unreviewed commit therefore runs that commit's scripts with the credentials that publish
releases.

**Measured in `connect_other_ais`, 2026-09-17** — and the finding is not "unpinned", it is
**half-pinned**, which is worse because it reads as deliberate:

| how it is pinned | actions |
|---|---|
| by SHA | `setup-node` ×7, `docker/login-action` ×3, `setup-buildx-action` ×2, `docker/build-push-action` ×2, `action-semantic-pull-request` ×1, **`actions/checkout` ×2** |
| by tag | **`actions/checkout@v7` ×13**, `actions/setup-dotnet@v6` ×5, `actions/setup-java@v6` ×1 |

`actions/checkout` is pinned both ways in one repository. zizmor reports the tag form as
`unpinned-uses` under a blanket policy, and `artipacked` for the checkouts that persist credentials.

**Already done, and it is only the cheap half** (shipped in #347): `persist-credentials: false` on
the drafting checkout, so the job that runs scripts while holding a write token no longer leaves
that token in `.git/config` for them to read. Nothing in those jobs needs it — `gh` is handed
`GH_TOKEN` explicitly and no step pushes.

**What is left. Step 0 first — the rest is stated for the family and measured in one repository.**

0. **Inventory the other six — DONE, 2026-09-18.** Measured from `origin/main` in every repository,
   not from a working copy: the first attempt at this read stale checkouts and missed a whole release
   line. The question is narrow — which jobs a TAG can start while holding `contents: write` or a
   token, and what they run out of the checkout, because a tag can point at any commit.

   | repository | tag patterns | privileged jobs a tag starts | scripts run from the checkout | checkout keeps the credential |
   |---|---|---|---|---|
   | `connect_other_ais` | `mcp-v*` `extension-v*` `server-v*` `bugs-v*` | **13** — 3 draft, 3 binaries, 3 release-complete, extension, 2 images, manifest | `changelog-names-the-release.mjs`, `changelog-section.mjs`, `draft-release.sh`, `publish-output-carries.sh`, `archive-carries.sh`, `verify-and-publish-release.sh` | no |
   | `creds_for_devs` | `server-v*` `extension-v*` `cli-v*` `mcp-v*` | 8 — image, manifest, 3 binaries, 3 release | none | no |
   | `sidecar_rust` | **`v*`** | 1 — `cpu` | none | no |
   | `conventions` | — | none | — | — |
   | `mcp` | — | none | — | — |
   | `rag_qln` | — | none | — | — |
   | `benchmark` | — | none | — | — |

   **Three repositories release, not seven**, and the four that do not have no tag-triggered job
   holding a write permission at all — so steps 1 to 3 have three subjects, not seven. The sidecar's
   `v*` is the row Epic 4 has to decide about: it is the only unprefixed pattern in the family.

   **`connect_other_ais` is the one that matters**, and not because it has the most jobs: it is the
   only repository whose release jobs execute SCRIPTS from the checkout while holding a write token.
   Six of them. That is the CWE-522 surface in one sentence — a tag against an unreviewed commit runs
   that commit's six scripts with the credentials that publish releases.
1. `persist-credentials: false` on **every** checkout that does not push, across the family. A
   checkout that DOES push keeps it, and the inventory says which those are.
   **DONE — verified 2026-09-18:** every checkout in every tag-triggered job in all three releasing
   repositories carries `persist-credentials: false`. None of them pushes, so there is no exception
   to record.
2. **Every `uses:` pinned to a full 40-character commit SHA, with the version in a trailing comment**
   — `actions/checkout@8f4b7f8… # v5`. Not "one policy per repository": the family picks SHA, because
   a tag is mutable and zizmor reports it as `unpinned-uses` under a blanket policy. The mixed state
   measured above is the defect precisely because it leaves the reader unable to tell which lines
   were a decision. **This is only maintainable with the `github-actions` Dependabot group** in
   requirement 3; a frozen SHA with no bot behind it rots into an unpatched action, which is a worse
   place than a tag. Do not do step 2 in a repository before that group exists there.
   **DONE — measured 2026-09-18: 127 `uses:` lines across the seven repositories are pinned to a
   40-character commit SHA and ZERO are pinned to a tag or a branch.** The `github-actions`
   Dependabot group exists in all seven, which is what keeps a frozen SHA from rotting into an
   unpatched action. One line regressed and was caught the same week — a job that reached main with
   `actions/checkout@v7` — which is the argument for Epic 1's check rather than against the policy:
   nothing in CI asks whether a `uses:` is pinned, so the answer is only as current as the last
   person who looked.
3. **Protected release tags** — the one that actually closes CWE-522; the others narrow the blast
   radius, this removes the entry. The mechanism, because "protected" alone is not actionable:
   a GitHub **tag ruleset** over each release pattern from the step 0 table (`mcp-v*`,
   `extension-v*`, `server-v*`, `bugs-v*`, and whatever the sidecar settles on in Epic 4), with
   **Restrict creations** on and an explicit bypass list. Classic branch protection cannot do this —
   it does not govern tags.
   - The bypass list is the whole design: it must contain the release actor and nothing else. Today
     that is the operator; after Epic 4 it is release-please, which cuts the tag from a merged and
     therefore reviewed release PR. **Order matters: land Epic 4 in a repository before restricting
     its tags, or the restriction blocks the automation that was going to satisfy it.**
   - **Restrict updates** and **Restrict deletions** on as well, or a protected tag can simply be
     moved to another commit, which is the same exposure with extra steps.
   - Note the honest limit: a ruleset restricts WHO may create a tag, not which commit it points at.
     The commit becomes trustworthy because the only permitted creator cuts it from a merged PR. If
     the operator keeps a personal bypass, the exposure is reduced to "an actor we trust", not
     removed — say which of the two was chosen when this ships.
4. Release logic from a reviewed, pinned ref rather than from the tagged checkout. **In scope for
   this plan, sequenced last on purpose**: steps 0-3 make it an improvement rather than a rescue,
   and if the operator stops after step 3 the CWE is already closed. It is the only step that may be
   dropped without reopening the finding — record it here if it is.

**This lives here and NOT in `dew_flow_conventions`, by the operator's ruling of 2026-09-17:**
*"there lie the general rules on how to develop; it must not be responsible for following the
rules."* Conventions says how to work; the branch protections, action pins, Dependabot groups and CI
gates that make people follow it belong to the repositories being protected. Checked while
answering: `.agents/conventions/common/` mentions `uses:`, pinning and actions **nowhere**, so there
was no shared policy to extend — and the submodule has six workflows of its own that such a rule
would have bound.

> **This ruling sits against requirement 8 of this plan**, which proposes
> `tools/repo-settings-check.mjs` **in** `dew_flow_conventions`. The submodule already hosts
> enforcement tools (`pin-check`, `plan-lifecycle`, `adapter-check`, `ownership-check`), so the
> practice exists and the ruling was given about a POLICY rather than about those tools. Only the
> operator can settle which reading wins.
>
> **The tension is not resolved here, but it is no longer only mentioned here.** A document round
> pointed out that a note buried in Epic 5 does not stop a builder who reads Epic 1 and starts
> typing — so requirement 8 and Epic 1 item 3 are both marked BLOCKED, requirement 8 carries the two
> options and what each costs, and the Definition of Done does not close until one is recorded. What
> is deliberately NOT done is choosing on the operator's behalf.

## Test plan

Each epic lands as a PR per repository; the PR is the test — CI, CodeRabbit, and the protection
itself refusing what it should. `repo-settings-check.mjs` has fixtures for a compliant and a drifted
repository.

**Release-please (Epic 4)** is verified on `dew_flow_mcp` first (smallest release surface), and the
verification is not "release-please cut a tag": it is that **the tag it cut triggered the existing
release workflow and that workflow produced the same artefacts as before**. A tag nothing builds
from is the failure this is watching for, and it is silent. Run the same check against
`dew_flow_sidecar_rust`, whose tag shape is an open conflict recorded in Epic 4.

**Epic 5 needs tests a pull request cannot give**, because tag protection is a repository setting
and CI never exercises it. Each is one command and an expected outcome, run once per repository
after the ruleset is applied:

| what | how | expected |
|---|---|---|
| an unauthorized actor cannot create a release tag | push a throwaway tag matching the pattern, as a non-bypass actor | **refused** by the ruleset |
| the permitted actor still can | let release-please cut one, or push as the recorded bypass actor | created, and the release workflow runs |
| a protected tag cannot be moved | force-push the tag to a different commit | **refused** |
| a protected tag cannot be deleted | delete it | **refused** |
| no job persists a credential it does not need | grep every workflow for `actions/checkout` and read the `with:` beside it | every non-pushing checkout sets `persist-credentials: false` |
| every action is pinned | `grep -rhoE "uses: [^ ]+" .github/workflows/` per repository | every line is a 40-character SHA |

**Delete the throwaway tag afterwards, and check that the deletion itself was refused or permitted
as the ruleset intends** — a test tag left behind on a release pattern is a release nobody meant.

## Definition of Done

- [ ] Every job of every repository is a required check, and no PR can skip one.
- [ ] `dotnet format --verify-no-changes` / eslint / cargo fmt / actionlint fail a badly formatted PR.
- [x] **Epic 2's blocked line is resolved**, 2026-09-18: `src_vs_code` has the type-aware linter,
      reached by moving TypeScript to 6.0.3 rather than by weakening the gate.
- [ ] `dependabot.yml`, the PR template and the semantic-title check exist in every repository.
- [ ] release-please cuts the tags the release workflows already build from, with the narrative
      changelog untouched.
- [ ] `repo-settings-check.mjs` passes against all seven repositories.
- [ ] Epic 5 step 0: the other six repositories are inventoried, in a table in this document.
- [ ] No checkout persists a credential it does not need, and every `uses:` line in every repository
      is a 40-character SHA with its version in a comment, with the `github-actions` Dependabot group
      keeping them current.
- [ ] Release tags are protected by a ruleset whose bypass list contains only the release actor, and
      the six tests in the test plan were run and their outcomes recorded — including the two that
      must be REFUSED.
- [ ] Whether Epic 5 step 4 shipped or was dropped is recorded, with the reason.
- [ ] **Requirement 8's home is settled by the operator**, and Epic 1 item 3 is unblocked or dropped
      accordingly. Nothing in Epic 1 item 3 is built before that.
- [ ] This plan promoted with what shipped differently.
