# PLAN — the rules a round shows are drawn at random

> Status: **stories 1.1 and 1.2 IMPLEMENTED 2026-09-15; the rest is plan.** `RuleOrder`, its tier table and
> `RuleFiles.Collect(repoPath, budgetBytes, order)` have shipped — no production path has changed
> behaviour yet, because `Drawn()` is still the default. Epics 1 (stories 1.2–1.4), 2, 3 and 4 remain
> planned. Scope: `src_mcp/runners/Context/RuleFiles.cs`,
> the three stage entry points in `src_mcp/src/Server/PanelService.cs`, and their tests. The topic
> vocabulary belongs to `dew_flow_conventions` and ships as its **own pull request** (epic 4, and see
> *External dependencies*);
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
> the accepted ones changed is recorded under *What the plan round changed* below; the epic/story split
> that followed corrected four more things, under *What the split changed*.
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
   read rules from the `repoPath` working tree while the code stage reads them from the worktree, and a promoted
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

- **The plan and document stages have no worktree, deliberately** - `NeedsWorktree: false,
  ReadsCheckout: false` ([PanelService.cs:426](../src_mcp/src/Server/PanelService.cs#L426)), because an
  agentic CLI handed a checkout goes exploring and that cost a ten-minute plan round. Their rules are
  read from `repoPath` - and `Collect` reads the **working tree**, not HEAD. That is the honest input
  at plan time: the plan under review describes work about to happen in that tree.
- **The code stage reads from the round's worktree on purpose** - the rules as of the commit under
  review, not as of this afternoon. Not to be traded away.
- **One root, named once.** A manifest's `source` paths are relative to the `--repo` root the resolver
  was given, and the gate reads them against **that same root**. No path is resolved against a root
  other than the one that produced it, and a read that escapes the root is refused.
- **The resolver cannot serve the code stage yet, and this is settled by reading rather than by an
  experiment.** Two independent refusals, both verified 2026-09-15:
  1. **A populated mount is not a callable resolver, and this is not about worktrees.** `node_modules/`
     is git-ignored in `dew_flow_conventions` and `tools/lib/rule-catalog.mjs` imports `yaml` and
     `picomatch`, so ANY fresh checkout of the submodule - `SubmodulePopulator`'s and an ordinary
     `git submodule update --init` alike - dies with `Cannot find package 'yaml'` before it reaches a
     single git check. Measured both ways on 2026-09-15. CI is unaffected (`ci.yml` installs the
     shared instruction dependencies), but `coai-mcp` runs where nobody may have. Epic 2 therefore
     PROBES rather than assumes, and its log names `npm ci --ignore-scripts` as the resolver's own
     error does.
  2. Running the PARENT checkout's script against `--repo <worktree>` is refused by `revision()`
     (`rule-cli.mjs:84`): `realpath(<repo>/.agents/conventions) !== realpath(installedRoot)` ->
     *"Run this repository's own mounted resolver"*.

  So the resolver is live **today** exactly where the mount carries its dependencies: the live parent
  checkout, which is the root of the plan and document stages. The code stage - the only stage that has
  the draw - runs the deterministic walk until **dependency E1** lands. The build order follows the
  capability: document, then plan, then code, with the code launch written in its FINAL shape so it
  begins succeeding the day `release` carries the fix, with no change here.
- **`read` cannot serve this gate; `explain` can.** The resolver refuses a payload over 32 KiB per read
  (`rule-cli.mjs:112`) and directs the caller to `explain` plus one `--only` read per rule - N launches
  a round. `explain` returns a manifest with `id`, `source`, `bytes`, `hash` and `reasons` per selected
  rule; the gate reads those files itself. One `node` launch per round.
- **The budget is the bundle's, not the prompt's.** `PlanBytes`/`DocumentBytes` in the log lines are
  measurements, not caps; the rules bundle carries its own 80 000-byte budget at every stage.
- **The three selection paths are alternatives, not pools that merge.** Exactly one answers a round:
  *(a)* a resolver manifest, ordered canonically, each rule carrying its `reasons`; *(b)* the **stage
  tier**, for a gate with no diff to select from; *(c)* the **deterministic walk**, when a path that
  should have produced a manifest failed. A round never unions (a) with (c) - a fallback that quietly
  added rules to a successful selection would reintroduce exactly the "why is this rule here" the
  manifest's `reasons` exist to answer - and the prompt names which path answered.

### External dependencies - both `dew_flow_conventions` pull requests, both arriving by release promotion

- **E1 - the resolver accepts a sibling worktree.** Relax `revision()` from PATH identity to SHA
  identity: accept a `--repo` whose index gitlink equals `installedRoot`'s HEAD and whose mount is
  clean at that SHA. The parent's script then serves a worktree, and no `node_modules` is needed inside
  it. Until E1, the code stage falls back by design, not by accident.
- **E2 - the `topics:` vocabulary.** `rule-catalog.mjs:83` **throws** on an unknown metadata key, and
  `selectRules` takes only `--task` and `--file`. So a `topics:` key added out of order does not
  degrade - it breaks the resolver for all six consumers. E2 is why epic 4 is conditional and why its
  triggers map to the vocabulary that exists today.

## Build order

Four epics, from the split made with Fable at the gate's instruction, after it corrected four things in
the intended shape - recorded under *What the split changed*. Every story: implement, test, update
`research/module_*.md`, `review_code` on that story's own diff, resolve every finding, commit. Build and
run with `dotnet build dew_flow_connect_other_ais.slnx` then
`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<Class>"`, then the whole
executable; never `dotnet test`.

`RuleFiles.cs` is already 354 lines, so new behaviour goes in new files (`RuleOrder.cs`,
`StageRules.cs`, `RuleManifest.cs`, `RuleResolver.cs`) and the discovery walk (`FolderFiles`,
`NeutralRuleFolders`, `MissingRuleMount`) is not touched - which is what keeps the boundary with
[PLAN_shared_rules_adoption.md](PLAN_shared_rules_adoption.md) physical rather than merely stated.

### Epic 1 - selection becomes a seam, and every deterministic order exists before the draw goes

*Done when* `Collect` takes an explicit candidate order; the walk has a pinned priority that shows the
doctrines under a tight budget; the plan and document gates carry a rules bundle from an ordered stage
tier; the draw is still the code stage's order and every existing `RuleFilesTests` is green.

**1.1 - `RuleOrder`: the seam, and the walk's priority.** *(Fable - the order decides who starves, and
being wrong here is the 2026-09-06 regression on exactly the path that runs when the resolver is
unavailable.)* `RuleFiles.Collect(repoPath, budgetBytes, RuleOrder order)` replaces the `int? seed`
parameter; new `RuleOrder.cs` carries `Walk` (instruction files, then this repository's own rules, then
the mount by tier: the language doctrines, then `security`, `testing`, `reuse-first`, `coding-style`,
`knowledge-base`, then the rest by ordinal path) and `Drawn(int?)`, which wraps today's shuffle and
stays the default so nothing changes yet.
**RED:** `TheFallbackOrder_ShowsTheDoctrines_NotOnlyTheTwoLongestFiles` - real sizes, a 45 000-byte
budget, `order: RuleOrder.Walk` built first as the plain alphabetical walk, so it fails with
`Expected paths to contain ".agents/conventions/common/testing.md"`: the measured symptom itself.
**Reviewer looks at:** that the default path is unchanged; the tier table's names and rationale; that
`Walk` is total - an unknown name falls through, nothing is dropped.

**1.2 - `StageRules`: the plan and document tiers, as data.** **IMPLEMENTED 2026-09-15.** Ordered
lists of MOUNT-RELATIVE paths, matched exactly, so one entry names one rule whether it is filtering a
walk or ordering a manifest. Deviation from the plan: the entries were to be matched by path SUFFIX,
and that is wrong - `common/legacy/common/security.md` also ends with `/common/security.md`, so one
entry could pull in a file nobody meant and spend the budget of the rule it impersonated.
`RuleCandidate` now carries the mount-relative name, stripped in `RuleFiles` where the mounts are
known. `security.md` also leads the plan tier rather than sitting fourth, for the reason it leads the
walk. `rule-ownership.md` is deliberately absent: its frontmatter scopes it to shared-RULE files, so
the resolver may select it in epic 2 when the document under review IS a rule, but it is not what a
document round here is always judged against.
**RED:** `APlanRound_GetsTheHighLevelRules_AndNotTheBuildRecipes`, with `dotnet-build`, `nuget-packages`
and `logging-serilog` present in the mount and absent from the result.
**Reviewer looks at:** the lists themselves - is `git-workflow` rightly absent from the plan tier, is
`testing` at 24.7 KB worth a third of the plan budget.

**1.3 - The plan gate is given the rules it is judged against.** *(Opus - wiring behind decided
policy.)* `ReviewPlanAsync` collects with `RuleOrder.Staged(StageRules.Plan)`; the log line loses
"no rules at this stage" and gains the bytes and the omission count.
**RED:** `APlanRound_IsGivenTheRulesItIsJudgedAgainst`, read through the fake CLI's recorded stdin,
failing with `Expected prompt to contain "## The rules this project has written down"`. With
`APlanRound_StillGetsNoWorktree` and `TheRulesBundleHasItsOwnBudget_AndDoesNotTruncateThePlanText`.
**Reviewer looks at:** `NeedsWorktree`/`ReadsCheckout` untouched; the comment saying WORKING TREE.

**1.4 - The document gate is given the rules that govern documents.** *(Opus.)* Same shape;
`DocumentOutcome.Ready` gains a repo-relative `Path` as a field - never a parse of the name - empty when
the document is outside the repository.
**RED:** `ADocumentRound_IsGivenTheRulesItIsJudgedAgainst`, plus
`ADocumentOutsideTheRepository_StillGetsTheStaticDocumentTier`.
**Reviewer looks at:** no root escape on the out-of-repo path; the purpose still leads the prompt.

### Epic 2 - the resolver says what the change selects, and every way it can fail lands on the walk

*Done when* a manifest drives selection on the document and plan stages; the code stage asks in its
final shape and falls back with the reason logged and named in the prompt; omissions and missing mounts
survive both paths.

**2.1 - `RuleManifest`: parse, batch, merge.** *(Opus - pure functions with exhaustive tests.)* A
`RuleSelection` union of `Resolved(manifest)` and `Unavailable(reason)` - no nulls, no flags.
**RED:** `MoreChangedFilesThanTheResolverTakes_AreBatched_NotDropped`,
`MalformedOrPartialJson_IsNotASelection`, `TwoBatchesNamingOneRule_MergeItsReasons`.
**Reviewer looks at:** unknown JSON fields tolerated - the manifest carries `instructions`,
`taskVocabulary` and `version` this code does not model.

**2.2 - `RuleResolver`: the launch, and the one fallback boundary.** *(Fable - it executes a script from
the repository under review with the server's privileges, kills a process tree on timeout, and confines
reads to one root; being wrong here is a security defect, not a quality one.)* Not launched at all when
the mount's script is absent, so a legacy repository costs no process start.
**RED:** `AMissingNode_AThrownLaunch_ATimeout_ABadExitAndMalformedJson_AllFallBack` - five launchers,
one assertion shape - with `TheResolverIsLaunchedFromTheRootItReads_AndNowhereElse` and
`ARepositoryWithoutTheMount_IsNotAskedAtAll`.
**Reviewer looks at:** the trust boundary - WHICH mounts may have their script run, decided and written
down; the timeout with a tree kill; that no exception type escapes the boundary.

**2.3 - `Collect` from a manifest: canonical order, reasons in the prompt, omissions that survive.**
*(Fable - this is the overflow policy, which is the plan's one guarantee, plus root containment of every
manifest path.)* Order: instruction files, own rules, manifest rules carrying a `path:` reason, the
stage tier, then the rest by id. A `source` outside the root makes the whole selection `Unavailable`
rather than a bundle with a hole in it.
**RED:** `AManifestNamingAnUnreadableFile_FallsBack_RatherThanSendingAPartialBundle`,
`AnOverBudgetManifest_NamesEveryOmittedRule`, `AnEmptyMount_FallsBackAndNamesTheMount`,
`TheManifestsSelection_IsTheBundle_InCanonicalOrder`, `EachSelectedRule_SaysWhyItIsInThePrompt`.
**Reviewer looks at:** a partial bundle impossible by construction; the tie-break chain exactly as
documented; nothing enumerating the filesystem on this path.

**2.4 - Three stages ask; one of them falls back on purpose today.** *(Opus - the decisions are made.)*
Document: root `repoPath`, `--task docs --file <its own path>`. Plan: root `repoPath`, `--task plan`.
Code: root the round's worktree after population, script from the parent mount,
`--task implement --file <every changed path>` - written in its final shape, falling back until E1.
**RED:** `TheCodeStage_AsksWithTheWorktreeAsRoot_AndTheParentsScript`,
`AResolverThatIsDown_StillGivesEveryStageItsRules`.
**Reviewer looks at:** the sequence `AddAsync` then populate then launch then read; the log line naming
which mode ran.

### Epic 3 - the draw dies, and nothing replaces it with chance

*Done when* `Random.Shared` is gone from the selection path, both paths are pinned byte-identical-twice
at one rule revision, and the measurement is recorded.

**3.1 - Delete the draw.** *(Opus - a deletion behind two already-pinned orders.)* `Drawn` goes, `Walk`
becomes the default, the `S2245` pragma and the draw's remarks go with it. The two shuffle tests are
deleted and replaced, not merely removed. `SeededShuffle` itself stays - `PromptDeal` uses it.
**RED:** `TwoRoundsThroughTheFallbackPath_ShowByteIdenticalRules` - twenty equal-sized mount files under
a 40 000-byte budget, collected twice, failing today with `Expected second.Files to equal first.Files`.
**Reviewer looks at:** `Random` absent from `runners/Context`; the 1.1 starvation test still green.

**3.2 - The record.** *(Opus. No RED test - nothing behavioural, and the summary says so.)*
`research/RESULTS_rules_selection_budget.md`: bytes selected, rules omitted and the mode per stage, from
the log lines rather than from arithmetic, as the evidence the modularization follow-up needs.

### Epic 4 - symbol triggers widen the selection through the vocabulary that exists (CONDITIONAL, severable)

*Done when* a deterministic token-to-task table over the diff's ADDED lines adds tasks to the code
stage's launch, a token in a removed line selects nothing, and the prompt says which symbol selected a
rule.

**Decision rule, taken at 3.2 rather than now:** epic 4's effect on the code gate is invisible until E1
ships, and its `topics:` half needs E2. If `release` carries neither by the time 3.2 lands, this epic is
extracted into its own `todo/` plan at promotion instead of being built blind.

**4.1 - `AddedLines` and `SymbolTriggers`.** *(Opus.)* Triggers map to the **existing** task vocabulary -
`HttpClient` to `http`, `ILogger`/`Serilog` to `logging`, `DbContext`/`Migration` to `storage`,
`.razor`/`StateHasChanged` to `ui`, `PackageReference` to `dependencies` - because E2 does not exist and
an unknown key throws.
**RED:** `ATokenInAnAddedLine_SelectsItsTask` / `ATokenInARemovedLine_DoesNot`.

**4.2 - The code stage passes them.** *(Opus.)* Derived tasks deduplicated and sorted so the argv is
byte-stable; the reason renders as `task:http <- HttpClient added in src/X.cs`.
**RED:** `ARuleSelectedByASymbol_SaysWhichSymbol`.

### Follow-up, not in this build order

**Rule modularization** - split rules over 15 KB into sections, in `dew_flow_conventions`. Selection is
whole-file, so one selected rule can take a third of the budget: `testing.md` 24 687,
`git-workflow.md` 21 754, `reliability.md` 16 520. Tagging makes the payload relevant; it does not make
it small. `tools/rules.test.mjs` SHA-pins the migrated rule bodies, so a split updates that baseline
deliberately rather than as a side effect. Informed by story 3.2's measurement.

## What the plan round changed

The gate's reviewers changed the plan's shape in four places, not merely its wording:

- **The draw's removal — epic 3 after the split renumbered it — was conditional and is now
  unconditional.** (Epic 4, the symbol triggers, stays conditional on E1 and E2; only the measured
  payload-fit precondition is gone.) Three reviewers independently attacked the same
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

## What the split changed

The epic/story split was made with Fable, as the gate's operator command requires, and it corrected
four things in the shape the plan gate had approved. Each was verified against the source before it was
taken:

- **The stage tier cannot be a "baseline to retire" - it is the overflow ORDER.** `--task plan` against
  today's vocabulary selects roughly 110 KB: the always-loaded core plus ten `task:plan` rules including
  `git-workflow` 21.8 KB and `development-workflow` 14.2 KB. They all tie on reason strength, so
  "reason strength, then rule id" fills the budget alphabetically and omits `reuse-first`,
  `subagent-models`, `task-lifecycle` and `post-deploy-checks` - the 2026-09-06 failure with a different
  alphabet. The static set therefore stays permanently, as a priority tier inside one canonical order.
  This supersedes the plan round's finding 15 AND the automated reviewer's retirement-equivalence
  comment: there is no retirement to test.
- **Epic 1 could not ship before a selection seam existed.** The plan tier plus the instruction files is
  about 90 KB against an 80 KB budget, so a plan gate built before `RuleOrder` would either inherit the
  draw or grow a second private ordering. The seam is now story 1.1, and the fallback priority moved
  into epic 1 from the old epic 4 - the constraint already said it had to exist before the draw went.
- **The worktree question was answered by reading, and the answer inverts the build order.** Both
  refusals are in *Constraints* above. The code stage is the resolver's LAST consumer, not its first;
  document and plan can use it today.
- **The topics axis is not merely absent, it is breaking if added early.** `rule-catalog.mjs:83` throws
  on an unknown metadata key, so a `topics:` key shipped before the consumers can read it takes the
  resolver down for all six. Epic 4's triggers map to the existing task vocabulary, and the epic is
  conditional and severable.

Smaller corrections taken: the measurement is a docs story, not a code story; the
`RolesWithRulesInMind` re-check is one assertion because no Conventions role is scheduled on the plan or
document stages; and "read from `repoPath` at HEAD" was simply wrong - `Collect` reads the working tree,
which is the honest input at plan time.

## Test plan

xUnit v3 on Microsoft Testing Platform - build, then run the **executable**; never `dotnet test`:

```bash
dotnet build dew_flow_connect_other_ais.slnx
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*RuleOrderTests"
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
```

Every story's RED test is named in its entry above, and each opens with the failure message that
describes the real symptom rather than a setup error. Three of them are the load-bearing ones:

| Story | Test | What its RED proves |
|---|---|---|
| 1.1 | `TheFallbackOrder_ShowsTheDoctrines_NotOnlyTheTwoLongestFiles` | the 2026-09-06 starvation, reproduced under a tight budget |
| 1.3 | `APlanRound_IsGivenTheRulesItIsJudgedAgainst` | the plan gate is judged against nothing today |
| 3.1 | `TwoRoundsThroughTheFallbackPath_ShowByteIdenticalRules` | the draw itself, observed as two different bundles |

Determinism is asserted **within one rule-source revision**; a promoted `release` pin is a different
input, and `ARuleRevisionChange_IsADifferentInput_AndIsNotAssertedIdentical` says so rather than
pretending otherwise.

## Definition of Done

- [ ] Epics 1-3 landed in order, story by story, each reviewed with `review_code` on its own diff, its
      findings resolved, its documentation and tests updated, and committed before the next one starts.
- [ ] Epic 4 built only if E1 and E2 have arrived; otherwise extracted into its own `todo/` plan, with
      the decision recorded at story 3.2.
- [ ] Each story that fixes a defect began with a failing test whose message names the real symptom, and
      both the RED message and the GREEN result are reported.
- [ ] No round can fail because of a resolver, a submodule, `node` or a file read - every mode lands on
      the deterministic walk with a logged reason, and the prompt says the selection was untargeted.
- [ ] The omission and missing-mount notes are asserted, not assumed.
- [ ] `Random.Shared` is gone from the selection path, and both the selected and the fallback paths are
      pinned by a byte-identical-twice test at one rule revision.
- [ ] `research/RESULTS_rules_selection_budget.md` records the measurement.
- [ ] `research/module_runners.md` and `research/module_server.md` updated as the stories land;
      `architecture.md` gains the node resolver as an external dependency of `coai-mcp`, Mermaid
      re-rendered.
- [ ] E1 and E2 are raised as their own `dew_flow_conventions` pull requests, and this repository's pin
      follows them rather than anticipating them.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>`, its deviations recorded, the follow-up
      extracted, and `todo/README.md` updated.
