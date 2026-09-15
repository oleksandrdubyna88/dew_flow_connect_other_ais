# PLAN — the rules a round shows are drawn at random

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/runners/Context/RuleFiles.cs`,
> the three stage entry points in `src_mcp/src/Server/PanelService.cs`, and their tests. The topic
> vocabulary belongs to `dew_flow_conventions` and ships as its **own pull request** (epic 3);
> nothing here edits that submodule beyond a pin.
>
> **Boundary with [PLAN_shared_rules_adoption.md](PLAN_shared_rules_adoption.md)** (in progress): that
> plan owns *discovery* — which layout a repository has, which folders are read, how the gate text is
> distributed. It has already delivered the neutral mount and the rule-directory allowlist. This plan
> owns *selection*: which of the discovered rules a round actually shows, and why that one. Neither
> changes the other's half.
>
> **Plan gate round 1** (2026-09-15, session `d9e01697`, all 3 reviewers answered, verdict
> `good_enough` on a one-round budget): 18 findings, **17 accepted, 1 rejected with a reason**. What
> the accepted ones changed is recorded under *What the plan round changed* below — they altered the
> shape of epic 4, not only its detail.
>
> Related docs: [PLAN_shared_rules_reach_reviewers.md](../research/PLAN_shared_rules_reach_reviewers.md)
> (shipped — it made the rules reach a reviewer at all),
> [PLAN_conventions_is_its_own_role.md](../research/PLAN_conventions_is_its_own_role.md),
> [architecture.md](../research/architecture.md), [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md).

## The symptom

A developer pushes a fix, runs a second code round, and gets findings from a different part of the
rule book than round 1 — because the rules a round shows are **drawn at random**, and the second draw
was different. `RuleFiles.Collect` shuffles the mounted family rules with `Random.Shared` on every
round ([RuleFiles.cs:257](../src_mcp/runners/Context/RuleFiles.cs#L257), reached from
[:224](../src_mcp/runners/Context/RuleFiles.cs#L224)) against an 80 000-byte budget
([RuleFiles.cs:117](../src_mcp/runners/Context/RuleFiles.cs#L117)), whole files or nothing — and the
family corpus is far larger than the budget, so a round sees part of it, a different part each time.
An unstable gate is a gate nobody trusts: a fix answered with findings from an area the developer
never touched reads as noise, and noise is what stops a gate being run.

**The draw was a cure, not a disease — but it is not the only cure.** It was installed on 2026-09-06
against a measured starvation: in stable enumeration order the first two files took a quarter of the
budget, and `testing.md`, `security.md`, `reuse-first.md` and all four language doctrines were shown to
*no reviewer, ever*. Randomness fixed that by giving every rule a chance. **A deterministic priority
order fixes it too**, and keeps the property the draw destroys — so the draw is replaced, not merely
deleted, and nothing in this plan leaves an ordering to chance or to the filesystem.

### What is already fixed, and is therefore NOT in this plan

The adoption canary has moved this ground since the draw was written:

- **A mount contributes rule directories only.** `NeutralRuleFolders`
  ([RuleFiles.cs:81-82](../src_mcp/runners/Context/RuleFiles.cs#L81-L82)) collects `common/`, `csharp/`,
  `rust/` and `typescript/` under `.agents/conventions` and nothing else, so the conventions
  repository's own `research/`, `todo/` and `coverage/` can no longer take budget from a real rule.
- **A mount that produced no rules is named to the reviewer.** `MissingRuleMount`
  ([RuleFiles.cs:296](../src_mcp/runners/Context/RuleFiles.cs#L296)) reports the neutral mount when
  nothing was collected under it.
- **The adapter migration is complete here**: root `AGENTS.md`, `CLAUDE.md` reduced to `@AGENTS.md`,
  this repository's own rules at `.agents/rules/`, the submodule mounted at `.agents/conventions`
  tracking the conventions **`release`** branch. That is the precondition the shared resolver refuses
  without, and it is already satisfied.

## The second symptom: two of the three gates get no rules at all

Only the code stage is given the rules ([PanelService.cs:531](../src_mcp/src/Server/PanelService.cs#L531)).

- The **plan gate** sends none — *"context for review: plan {PlanBytes} bytes; no diff and no rules at
  this stage"* ([PanelService.cs:433](../src_mcp/src/Server/PanelService.cs#L433)).
- The **document gate** sends none either, in the same words
  ([PanelService.cs:825](../src_mcp/src/Server/PanelService.cs#L825)).

So "split plan from code" is not a narrowing of an existing payload — it is giving two gates a rules
block they have never had.

## What must be true when this is done

1. The same change, reviewed twice **at the same rule-source revision**, shows the **same rules, byte
   for byte** — with no cache, no session hash and no seeded draw. Selection is a pure function of
   *(stage, changed paths, symbols in the added lines, rule-source revision)*, and **so is every
   fallback path**. The revision is part of the input, not an assumption: the plan and document gates
   read rules from `repoPath` at HEAD while the code stage reads them from the worktree, and a promoted
   `release` pin changes the corpus under both — so two rounds across such a change are *different*
   inputs and are expected to differ. The tests assert identity within one revision and say so.
2. A rule is in the prompt **because something in the change selected it**, and the round can say which
   reason.
3. What did not fit is still **named**, and so is a mount that gave nothing. A reviewer told nothing
   about what it was not shown reports compliance with rules it never saw.
4. The plan gate is judged against high-level rules; the code gate against low-level ones; the document
   gate against the rules that govern documents.
5. **Nothing here can fail a round.** Every new dependency — a resolver, a submodule, `node`, a file
   read — degrades to a deterministic walk with a logged reason and a sentence in the prompt.

## Constraints that shape the design

- **The plan and document stages have no worktree, deliberately** — `NeedsWorktree: false,
  ReadsCheckout: false` ([PanelService.cs:426](../src_mcp/src/Server/PanelService.cs#L426)), because an
  agentic CLI handed a checkout goes exploring and that cost a ten-minute plan round. Their rules are
  read from `repoPath` at HEAD. Acceptable: at plan time there is no commit under review.
- **The code stage reads from the round's worktree on purpose** — the rules as of the commit under
  review, not as of this afternoon. **Not to be traded away**, which is why epic 2 fixes the resolver
  rather than falling back to HEAD (see *What the plan round changed*, finding 16).
- **One root, named once.** The manifest's `source` paths are relative to the `--repo` root the
  resolver was given, and the gate reads them against **that same root** — the round's worktree for the
  code stage, `repoPath` for the other two. No path is ever resolved against a different root than the
  one that produced it, and a read that escapes the root is refused.
- **Sequence at the code stage is fixed**: `WorktreeManager.AddAsync` → `SubmodulePopulator` →
  resolver → reads. A resolver launched before population sees an empty mount, which is why it is not
  launched there.
- **`read` cannot serve this gate; `explain` can.** The resolver refuses a payload over 32 KiB per read
  (`rule-cli.mjs:112`) and directs the caller to `explain` plus one `--only` read per rule — N launches
  a round. `explain` returns a manifest with `id`, `source`, `bytes`, `hash` and `reasons` per selected
  rule; the gate reads those files itself, keeping the budget logic and the omission note. One `node`
  launch per round.
- **The budget is the bundle's, not the prompt's.** `PlanBytes`/`DocumentBytes` in the log line are
  measurements, not caps; the rules bundle carries its own 80 000-byte budget at every stage, and
  nothing silently truncates one to make room for the other.
- **The three selection paths are alternatives, not pools that merge.** Exactly one answers a round,
  and the precedence is fixed: *(a)* a resolver manifest, when the stage has selectors and the resolver
  answered — candidates are the manifest's rules in canonical order, each carrying its `reasons`;
  *(b)* the **static stage set**, for a stage with no diff to select from (plan, document); *(c)* the
  **deterministic fallback walk**, only when a path that should have produced a manifest failed. A
  round never unions (a) with (c) — a fallback that quietly added rules to a successful selection would
  reintroduce exactly the "why is this rule here" the manifest's `reasons` exist to answer — and the
  prompt names which path answered.

## Build order

Four epics. The gate's operator commands ask for the epic/story split to be made with Fable at
implementation start and for `review_code` after **every** story — the phases below are the intended
shape, to be confirmed by that split rather than to replace it.

### Epic 1 — the plan and document gates get the rules they are judged against

1. Give `ReviewPlanAsync` a rules bundle read from `repoPath` at HEAD, rendered through the existing
   `RulesSection`. The stage keeps `NeedsWorktree: false` — a bundle is text, not a checkout.
2. The same for the document gate ([:825](../src_mcp/src/Server/PanelService.cs#L825)). Its inputs are
   the document's own repository-relative path (it has one) and `--task docs`; where the document is
   not in the repository, the static set of step 3 is the whole answer.
3. A **static stage set**, held in one place: the plan gate gets architecture, layering, reuse-first,
   planning-docs, testing strategy and security; the document gate gets the documentation and planning
   rules; neither gets `dotnet-build`, `nuget-packages` or `logging-serilog`. **This set is not deleted
   when epic 2 lands** — it is the baseline for a stage with no diff, and it is retired only when a
   `--task plan` invocation with no `--file` has been shown, against the promoted `release` vocabulary,
   to return the same rules or better (finding 15). **Each baseline is retired against its own task**:
   the plan baseline needs the `--task plan` equivalence, the document baseline a `--task docs` one.
   They are different sets, so one equivalence proves nothing about the other, and a baseline whose
   equivalence has not been run stays permanent.
4. Re-check `RolesWithRulesInMind` — the Conventions-role gating was written when only the code stage
   had rules, and a stage that now has them must not inherit the rule-less assumption. The sentence
   that tells the caller WHY a Conventions reviewer was skipped already ships
   ([PLAN_a_skipped_role_reaches_the_ai.md](../research/PLAN_a_skipped_role_reaches_the_ai.md),
   implemented 2026-09-10); it has to stay accurate once two more stages can carry rules.

### Epic 2 — `RuleFiles` asks the resolver what this change selects

5. A `RuleManifest` reader: launch `node .agents/conventions/tools/rules.mjs explain --repo <root>
   --task <task> --file <path> …` through the existing `IProcessLauncher` — no second process helper
   (reuse-first).
6. **More changed paths than the resolver takes are batched, never dropped.** Paths are sorted
   canonically, split into deterministic batches of 256, and the manifests merged by rule id; a path
   that still cannot be resolved is named in the prompt beside the omissions. A selector silently
   discarded is a rule silently missing, which reads to a reviewer as compliance (finding 10).
7. `Collect` takes a manifest when one is available: candidates are the manifest's rules in
   **canonical order** — reason strength first (path match, then topic, then task, then always-loaded),
   ties broken by rule id, never by enumeration order. The budget, the whole-file rule and the omission
   note are unchanged; each rule carries its `reasons` into the prompt.
8. **One fallback boundary, covering every way this can go wrong** (findings 0, 1, 12): `node` absent
   or not executable, a launch that throws before any output, a bounded timeout with the process tree
   killed, a non-zero exit, malformed or partial JSON, a manifest naming a file that cannot be read,
   a mount that is empty or dirty, a worktree the resolver refuses. Every one of them lands on the same
   path — the deterministic walk of epic 4 — with the reason logged and a sentence in the prompt saying
   the selection was untargeted. A partial payload is never sent as if it were a complete selection.

> **Experiment before step 7 is written, and its outcome is not a fallback to HEAD.** Does the resolver
> accept a linked worktree once `SubmodulePopulator` has filled it? It requires the mounted checkout to
> match the index gitlink and be clean. If it refuses, the fix is in `dew_flow_conventions` — teach
> `rule-cli.mjs` to resolve from a git worktree — because resolving against the parent checkout would
> select rules "as of this afternoon" for a commit under review, which this plan's own constraints
> forbid (finding 16). Until that lands, the code stage falls back to the deterministic walk **of the
> worktree**, which is still the rules as of the commit.

### Epic 3 — topics and symbol triggers, a pure function of the diff

9. **Conventions half, separate pull request**: a `topics:` key beside the existing `tasks:`, with a
   domain vocabulary (`efcore`, `threading`, `validation`, `http-client`, …) backfilled across the 24
   rules. Today's vocabulary is lifecycle-shaped and only 9 of 24 rules carry `paths:` at all, so this
   axis is new. It must stay language-neutral: the corpus serves Rust and TypeScript too. Delivery
   cost: the mount tracks the `release` branch, so the vocabulary arrives by release promotion.
10. **`topics:` is additive, never a gate** (finding 4). A rule without one keeps being selected by its
    `tasks:` and `paths:` exactly as today; no rule can become unreachable by not being tagged, and the
    backfill is therefore an improvement in recall rather than a migration that can lose rules.
11. **This repository's half**: a symbol trigger table over the diff's **added lines only** —
    `CancellationToken`, `IAsyncDisposable`, `lock`, `HttpClient`, `Channel<T>` → topics, passed to the
    resolver as additional selectors. Deterministic, explainable, no index, no model.
12. Where that table lives is a decision to take, not to assume. Recommended: in `dew_flow_conventions`
    beside the rules, so every consumer shares one copy and it is pinned with them; the cost is a
    release promotion and a pin cascade per change. A C# table here is cheaper to change and starts
    drifting the day a second consumer wants it.

**Rejected, with the reason recorded**: BM25 or embedding ranking over the corpus. It reintroduces the
property this plan exists to remove — a one-line change to a diff silently permutes the ranking, and
nobody can answer "why was this rule shown?". Path globs and topic tags answer it by construction.

### Epic 4 — the draw dies, and nothing replaces it with chance

13. **A deterministic overflow policy first** (findings 8, 14). When the selected rules exceed the
    budget, the order above decides who fits — reason strength, then rule id — and everything that did
    not fit is named in the existing omission note. Whole files stay whole. This is what makes step 14
    unconditional: the corpus will keep exceeding 80 000 bytes (`testing.md` 24 687 +
    `git-workflow.md` 21 754 + `reliability.md` 16 520 is 62 KB before anything else is selected), and
    a plan that waits for it not to would never remove the draw at all.
14. **Delete `Shuffled` and the `seed` parameter** ([RuleFiles.cs:257](../src_mcp/runners/Context/RuleFiles.cs#L257)),
    unconditionally, once steps 7 and 13 are in. Two tests go with it —
    `TwoRoundsSeeTwoDifferentHalvesOfTheFamilyRules`
    ([RuleFilesTests.cs:334](../src_mcp/tests/RuleFilesTests.cs#L334)) and
    `AcrossEnoughRounds_EveryFamilyRuleGetsRead` ([:354](../src_mcp/tests/RuleFilesTests.cs#L354)) —
    replaced by the determinism tests. `ADozenRealRuleFiles_FitTheDefaultBudget`
    ([:313](../src_mcp/tests/RuleFilesTests.cs#L313)) stays and gains a selected-payload sibling.
15. **The fallback walk is deterministic too, and it does not starve** (findings 11, 17). `FolderFiles`
    ([RuleFiles.cs:263](../src_mcp/runners/Context/RuleFiles.cs#L263)) de-duplicates its candidates and
    then sorts them by case-insensitive PATH alone — which is an order, but not a priority: it is the
    alphabet that put `development-workflow.md` and `http-contracts.md` first in 2026-09-06 and starved
    the doctrines. **The canonical fallback order, defined here and asserted in the tests**:

    1. the instruction files, in `InstructionFiles` order (they are the entry points and always fit);
    2. this repository's own rules (`.agents/rules`), by ordinal path;
    3. the mount's rules by CATEGORY — `common/` first, then the language directory matching the
       change's own file extensions, then the remaining language directories in ordinal order;
    4. within a category, by ordinal path;
    5. ties, which after 4 can only be a duplicate path, resolved by rule id.

    Ordinal, not culture- or case-sensitive comparison, so the order does not depend on the machine.
    Without this, deleting the draw restores the starvation it was installed against — on exactly the
    path that runs when the resolver is unavailable.
16. **Record the measurement** the draw's removal is no longer gated on: for a representative diff, how
    much of the budget the selected payload uses and which rules were omitted. It is evidence about the
    policy's quality, and the input to the modularization follow-up — not a precondition for landing.

**Rejected, with the reason recorded**: sticky rules across rounds ("a rule present in round N stays in
round N+1"). Stateful where a pure function will do, monotonically growing against a fixed budget so a
third round evicts anyway, and it needs per-branch persistence nothing here has. Purity gives the same
stability for free.

### Follow-up, not in this build order

**Rule modularization** — split rules over 15 KB into sections, in `dew_flow_conventions`. Selection is
whole-file, so one selected rule can take a third of the budget: `testing.md` 24 687,
`git-workflow.md` 21 754, `reliability.md` 16 520. Tagging makes the payload relevant; it does not make
it small. The interaction to respect: `tools/rules.test.mjs` SHA-pins the migrated rule bodies against
a baseline, so a split updates that baseline deliberately rather than as a side effect. Own plan, own
pull request, informed by step 16's measurement.

## What the plan round changed

The gate's reviewers changed the plan's shape in four places, not merely its wording:

- **Epic 4 was conditional and is now unconditional.** Three reviewers independently attacked the same
  sentence — "remove the draw only once a measured round shows the payload fits" — and codex put it
  best: that gates the plan's one guarantee on a condition the arithmetic says will not arrive, since
  62 KB of the budget goes to three rules before anything else is selected. A deterministic overflow
  policy (step 13) replaces the evidence gate, and the measurement stays as evidence rather than as a
  precondition.
- **The fallback path was going to restore the starvation.** Gemini traced it: step 8 falls back to
  "today's walk", and after the draw is deleted that walk is the unshuffled enumeration whose first two
  files took a quarter of the budget in the first place. Step 15 gives the fallback its own priority
  order and a test.
- **The plan gate's static set is no longer deleted in epic 2.** Gemini again: `--task plan` with no
  `--file` against a vocabulary that has not yet been promoted to `release` can return less than the
  static list does, regressing the gate that epic 1 just fixed. The set is retired only against
  measured output.
- **The worktree experiment had a fallback that contradicted a stated constraint.** Resolving the
  manifest against the parent checkout would select rules as of this afternoon for a commit under
  review, which the constraints call non-negotiable. The fix moves to `rule-cli.mjs`; the interim
  fallback is the deterministic walk *of the worktree*.

One finding was **rejected**: that `SubmodulePopulator` needs an idempotency check for parallel rounds.
It targets the discovery plan's code rather than this one, and its premise does not hold here — every
round gets its own linked worktree (`_worktrees.AddAsync(repoPath, sha, sessionId, round)`), and `open`
prunes what a killed session left behind, so two rounds cannot populate one directory.

**The automated reviewer on the pull request** (CodeRabbit, 2026-09-15) added four, all accepted: the
byte-identical guarantee had to be scoped to a rule-source revision, since the corpus moves under a
`release` promotion; each static baseline is retired against its OWN task equivalence, plan and
document being different sets; the three selection paths are alternatives with a stated precedence
rather than pools that merge; and the fallback needed its category order written down, because
`FolderFiles` sorts by path alone — an order, but not a priority, and it is the alphabet that caused
the starvation in the first place.

## Test plan

xUnit v3 on Microsoft Testing Platform — build, then run the **executable**; never `dotnet test`:

```bash
dotnet build dew_flow_connect_other_ais.slnx
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*RuleFilesTests"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*ConventionsPassTests"
```

| Epic | Test | Guarantee |
|---|---|---|
| 1 | `APlanRound_IsGivenTheRulesItIsJudgedAgainst` | **RED first** — no rules today |
| 1 | `ADocumentRound_IsGivenTheRulesItIsJudgedAgainst` | **RED first** — same gap, second gate |
| 1 | `APlanRound_GetsTheHighLevelRules_AndNotTheBuildRecipes` | the stage split |
| 1 | `APlanRound_StillGetsNoWorktree` | protects the ten-minute-round fix |
| 1 | `TheRulesBundleHasItsOwnBudget_AndDoesNotTruncateThePlanText` | the budget question |
| 2 | `TheManifestsSelection_IsTheBundle_InCanonicalOrder` | the wiring and the order |
| 2 | `MoreChangedFilesThanTheResolverTakes_AreBatched_NotDropped` | no silent selector loss |
| 2 | `APathThatCouldNotBeResolved_IsNamedInThePrompt` | the disclosure for step 6 |
| 2 | `AMissingNode_AThrownLaunch_ATimeout_ABadExitAndMalformedJson_AllFallBack` | one boundary, every mode |
| 2 | `AManifestNamingAnUnreadableFile_FallsBack_RatherThanSendingAPartialBundle` | the read phase |
| 2 | `AnEmptyMount_FallsBackAndNamesTheMount` | `MissingMounts` survives selection |
| 2 | `AnOverBudgetManifest_NamesEveryOmittedRule` | `Omitted` survives selection |
| 3 | `ATokenInAnAddedLine_SelectsItsTopic` / `…InARemovedLine_DoesNot` | added lines only |
| 3 | `ARuleWithNoTopics_IsStillSelectedByItsTasksAndPaths` | topics are additive |
| 3 | `TheSameDiffTwice_SelectsTheSameTopics` | purity |
| 4 | `TwoRoundsOverTheSameChange_AtOneRuleRevision_ShowByteIdenticalRules` | replaces the two shuffle tests |
| 4 | `TwoRoundsThroughTheFallbackPath_ShowByteIdenticalRules` | the path finding 11 named |
| 4 | `TheFallbackOrder_ShowsTheDoctrines_NotOnlyTheTwoLongestFiles` | the 2026-09-06 starvation |
| 4 | `TheFallbackOrder_TakesCommonBeforeTheLanguageDirectories_ByCategoryNotAlphabet` | the category order of step 15 |
| 4 | `ARuleRevisionChange_IsADifferentInput_AndIsNotAssertedIdentical` | scopes the guarantee honestly |
| 1 | `TheDocumentBaselineIsRetired_OnlyAgainstATaskDocsEquivalence` | one baseline, one equivalence |

Epic 1 reports the RED failure message before the fix as well as the pass after it; every epic reports
the runner's own output.

## Definition of Done

- [ ] Epics 1–4 landed in order, each story reviewed with `review_code`, its findings resolved, its
      documentation and tests updated, and committed before the next one starts.
- [ ] Epic 1 began with a failing test whose message describes the real symptom.
- [ ] The conventions half of epic 3 is a **separate pull request**; this repository's pull request
      touches the submodule only as a pin.
- [ ] No round can fail because of a resolver, a submodule, `node` or a file read — every mode lands on
      the deterministic walk with a logged reason, and the prompt says the selection was untargeted.
- [ ] The omission and missing-mount notes are asserted, not assumed.
- [ ] `Random.Shared` is gone from `RuleFiles`, and both the selected path and the fallback path are
      pinned by a byte-identical-twice test.
- [ ] The step 16 measurement is recorded in `research/`.
- [ ] `research/module_runners.md` and `research/module_server.md` updated; `architecture.md` updated if
      the resolver dependency crosses a container boundary; Mermaid re-rendered.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>`, its deviations recorded, the follow-up
      extracted into its own `todo/` plan, and `todo/README.md` updated.
