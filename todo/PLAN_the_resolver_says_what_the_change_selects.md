# PLAN — the resolver says what the change selects

> `todo/PLAN_the_resolver_says_what_the_change_selects.md`
>
> Status: **plan only, nothing implemented yet.** Epic A **can be built and merged today**; only its
> *benefit* waits on dependency **E1** (see *Can I start?* below). Epic B cannot start until **E2**.
> Extracted 2026-09-15 from
> [PLAN_the_rules_a_round_shows_are_drawn_at_random.md](../research/PLAN_the_rules_a_round_shows_are_drawn_at_random.md),
> which shipped everything that did not need these dependencies.
>
> Scope: `src_mcp/runners/Context/` (a new `RuleManifest` and `RuleResolver`), the code stage in
> `src_mcp/src/Server/PanelService.cs`, and their tests.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md),
> [RESULTS_rules_selection_budget.md](../research/RESULTS_rules_selection_budget.md) (the measurement
> that makes the case for this plan).

## Why this exists

Rule selection is deterministic now, and its priority is written down — but it is **generic**. A round
is judged against the same eight tier rules whatever it changed, plus a tail rotated by branch. The
measurement says what that costs: the corpus is 272 121 bytes against an 80 000-byte budget, so **24 of
32 rules reach no code round at all**, and *which* 24 depends on a branch name rather than on the change.

A diff that edits a workflow file should get `git-workflow.md` BECAUSE it edits one. The shared
conventions repository already answers exactly that question — `tools/rules.mjs` reads `paths:`,
`tasks:` and `depends:` frontmatter and returns the rules that apply to a set of files and a task.

## Boundary with the parent plan

Written on both sides, as `planning-docs.md` § *A boundary between two plans is named on BOTH sides*
requires. The parent carries the same table.

| Item | Built by | The other plan's part | Order |
|---|---|---|---|
| Deterministic order, tier table, `RuleOrder` seam | **Parent** (epic 1, shipped) | this plan consumes it as the fallback | parent first |
| Stage tiers for the plan and document gates | **Parent** (epic 1, shipped) | unchanged here; this plan touches the CODE stage only | parent first |
| Removal of the random draw | **Parent** (epic 3, shipped) | this plan must not reintroduce a non-deterministic order | parent first |
| Resolver manifest, batching, the fallback boundary | **This plan** (epic A) | parent records the fallback it falls back TO | after E1 for benefit; buildable before |
| `topics:` vocabulary + symbol triggers | **This plan** (epic B) | parent rejected BM25/embeddings; that rejection stands | after E2 |
| Rule modularization (splitting >15 KB rules) | **Neither** — a named follow-up in `dew_flow_conventions` | both plans cite the same measurement for it | independent |

Disjoint: the parent owns *how the mount is ordered when nothing selects*; this plan owns *what selects*.

## Can I start? — the honest answer

**Epic A: yes, today.** The manifest reader, the batching, the launch and the whole fallback boundary
are testable with a fake process launcher; none of them needs a working resolver. What waits on E1 is
the *benefit* — a real round actually getting targeted rules instead of falling back. Build it, merge
it behind the fallback, and it starts paying the day the pin moves.

**Epic B: no.** It needs E2, and E2 is breaking if done out of order (below).

**Running the resolver by hand today**, to see what it would answer: `npm ci --ignore-scripts` inside
`.agents/conventions` gives that checkout its dependencies, and the script then runs against the parent
checkout. That is a local experiment, not the production path — do not make the server depend on a
developer having done it.

## The dependencies, with owners

| | What must change | Where | Acceptance | Reaches us by |
|---|---|---|---|---|
| **E1** | `revision()` accepts a sibling worktree: relax PATH identity to SHA identity — accept a `--repo` whose index gitlink equals `installedRoot`'s HEAD and whose mount is clean at that SHA | `dew_flow_conventions`, `tools/lib/rule-cli.mjs:84` | the parent checkout's script answers `explain` for a round worktree, with no `node_modules` inside that worktree | a `release` promotion, then a pin bump here |
| **E2** | a `topics:` key the catalog accepts | `dew_flow_conventions`, `tools/lib/rule-catalog.mjs:83` | an unknown-key throw no longer fires for `topics:`; rules without one still select by `tasks:`/`paths:` | same |

**Owner: unassigned.** Both are conventions-repository changes; the operator deferred raising them on
2026-09-15. Whoever picks this plan up raises E1 first — nothing here is worth measuring without it.

### Why E1 exists, measured 2026-09-15

Two independent refusals, either one sufficient:

1. `node_modules/` is git-ignored in `dew_flow_conventions` and `rule-catalog.mjs` imports `yaml` and
   `picomatch`, so **any** fresh checkout of the submodule — `SubmodulePopulator`'s and an ordinary
   `git submodule update --init` alike — dies with `Cannot find package 'yaml'`. CI is unaffected
   (`ci.yml` installs the shared instruction dependencies); `coai-mcp` runs where nobody has.
2. Running the PARENT checkout's script against `--repo <worktree>` is refused by `revision()`:
   `realpath(<repo>/.agents/conventions) !== realpath(installedRoot)` → *"Run this repository's own
   mounted resolver"*.

### Why E2 is breaking, not merely missing

`rule-catalog.mjs:83` **throws** on an unknown metadata key. A `topics:` key added before the consumers
can read it does not degrade — it takes the resolver down for all six repositories that mount the
rules. E2 therefore ships first, and epic B does not start before it.

## Build order

### Epic A — `RuleFiles` asks the resolver what this change selects

1. **Launch from the checkout that HAS the dependencies.** The executable is the PARENT checkout's
   mounted script — `<installedRoot>/tools/rules.mjs`, where `installedRoot` is
   `<repoPath>/.agents/conventions` — and the round's worktree is passed only as `--repo`. The process
   working directory is the parent checkout. Launching the relative path from inside a round worktree
   runs that worktree's dependency-less copy and falls back every time, which is the trap E1 is about.
   Through the existing `IProcessLauncher`; no second process helper.
2. **The task is `implement`** for the code stage — the vocabulary is
   `inspect|audit|plan|implement|docs|policy|test|git|pr|release|deploy|dependencies|http|gpu|benchmark|logging|storage|ui`,
   and a code round reviews an implementation. (The plan and document gates use `plan` and `docs`; they
   are the parent plan's, already shipped, and unchanged here.)
3. **Use `explain`, not `read`.** `read` refuses a payload over 32 KiB (`rule-cli.mjs:112`), less than
   half this gate's budget, and would force one launch per rule. `explain` returns `id`, `source`,
   `bytes`, `hash` and `reasons` per selected rule; the gate reads those files itself, keeping the
   existing whole-file budget and omission logic.
4. **Batch, never drop.** More changed paths than the resolver's 256-file limit are sorted canonically,
   split into deterministic batches and merged by rule id — **reasons are UNIONED and de-duplicated**
   when a rule appears in more than one batch. A path that still cannot be resolved is named in the
   prompt beside the omissions: a selector silently discarded is a rule silently missing, which reads
   to a reviewer as compliance.
5. **The manifest REPLACES the mount's selection; it does not add to it.** When the resolver answers,
   its rules ARE what the mount contributes — the tier is not applied on top, because the resolver's
   own `load: always` rules already carry the unconditional core, and layering the tier over it would
   re-spend the budget the targeting just freed. Unchanged either way: the instruction files and this
   repository's own rules still lead and are still outside any order, and the 80 000-byte whole-file
   budget with its omission note still applies.
6. **Canonical order** within the manifest: reason strength (path match, then topic, then task, then
   always-loaded), ties by rule id — never enumeration order. Each rule carries its `reasons` into the
   prompt so a finding can say why its rule was shown.
7. **One fallback boundary covering every failure mode**: `node` absent or not executable, a launch
   that throws, a bounded timeout with the process tree killed, a non-zero exit, malformed or partial
   JSON, a manifest naming an unreadable file, an empty or dirty mount, a worktree the resolver
   refuses. All land on `RuleOrder.ForBranch` — the order the parent plan shipped — with the reason
   logged and a sentence in the prompt saying the selection was untargeted. A partial payload is never
   sent as a complete selection.

> **The fallback is the CURRENT state, not a future safety net.** Until E1 lands, every code round
> takes it. That is why the parent plan built it to be good enough to live on, and why epic A is
> mergeable before E1: it changes nothing observable until the resolver can answer.

### Epic B — symbol triggers widen the selection (BLOCKED on E2)

8. **Conventions half, its own pull request**: a `topics:` key beside `tasks:`, with a domain
   vocabulary backfilled across the rules, and **additive** — a rule without one keeps being selected
   by its `tasks:` and `paths:`, so no rule becomes unreachable by not being tagged.
9. **This repository's half**: a table over the diff's **added lines only**, mapping tokens to topics.
   The contract, so two implementers cannot produce different selections:
   - **Input**: the text of lines beginning `+` and not `+++`, per `FileDiff.Text`. Removed and context
     lines are not inspected.
   - **Matching**: whole-word, case-SENSITIVE, on the identifier only — `HttpClient` matches
     `new HttpClient(` and `IHttpClientFactory` does NOT match `HttpClient`. Comments and string
     literals are NOT excluded; excluding them needs a parser, and a rule shown because a symbol was
     named in a comment is a cheaper error than a parser in this path.
   - **The table is versioned data**, one row per trigger with its topic and a reason string, and every
     row has a test.

**Rejected already, with the reason recorded in the parent plan**: BM25 or embedding ranking. It
reintroduces the property the parent removed — a one-line change silently permutes the ranking and
nobody can answer "why was this rule shown?".

## Test plan

xUnit v3 on Microsoft Testing Platform — build, then run the **executable**; never `dotnet test`:

```bash
dotnet build dew_flow_connect_other_ais.slnx
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*RuleResolverTests"
```

Every failure class gets an assertion over what a HUMAN would see, not merely over the fallback:

| Test | Asserts |
|---|---|
| `TheResolverIsLaunchedFromTheParentCheckout_NotTheWorktree` | argv[0] is `<installedRoot>/tools/rules.mjs`, cwd is the parent, worktree only in `--repo` |
| `TheCodeStageAsksForTheImplementTask` | `--task implement` |
| `TheManifestsSelection_IsTheBundle_InCanonicalOrder` | order by reason strength, ties by id |
| `TheManifestReplacesTheTier_RatherThanAddingToIt` | no tier rule appears unless the manifest selected it |
| `MoreChangedFilesThanTheResolverTakes_AreBatched_NotDropped` | every path in exactly one batch |
| `ARuleInTwoBatches_KeepsBothItsReasons` | reasons unioned, de-duplicated |
| `APathThatCouldNotBeResolved_IsNamedInThePrompt` | the prompt names it |
| `EveryFailureMode_FallsBack_LogsItsReason_AndSaysUntargetedInThePrompt` | one parametrised test over: missing node, throwing launch, timeout, non-zero exit, malformed JSON, unreadable manifest file, empty mount, refused worktree — each asserting the logged reason, the prompt wording, and that no partial bundle was sent |
| `ATimedOutResolver_LeavesNoChildProcess` | the process tree is killed |
| `ATokenInAnAddedLine_SelectsItsTopic` / `…InARemovedLine_DoesNot` | added lines only |
| `IHttpClientFactory_DoesNotMatchHttpClient` | whole-word matching |

## Definition of Done

- [ ] **Epic A merged** — buildable and mergeable before E1; its tests use a fake launcher and do not
      require a working resolver.
- [ ] Every story opened with a failing test whose message names the real symptom.
- [ ] No round can fail because of a resolver, a submodule, `node` or a file read — asserted per
      failure class, including the logged reason, the untargeted prompt sentence, no partial bundle and
      no surviving child process.
- [ ] **E1 landed** in `dew_flow_conventions` and reached this repository through a `release` pin —
      required before the re-measurement below, not before epic A.
- [ ] **Epic B** built only after E2 shipped; otherwise it stays here and says why.
- [ ] A re-measurement against [RESULTS_rules_selection_budget.md](../research/RESULTS_rules_selection_budget.md):
      how much of the corpus a targeted round reaches, against the 8-of-32 the tier reaches today.
- [ ] `research/module_runners.md` and `module_server.md` updated; `architecture.md` gains the node
      resolver as an external dependency of `coai-mcp`.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations recorded — and on that move,
      the links in this file change: `../research/X` becomes `X`, and `../todo/Y` for anything still
      open. The parent's boundary table is updated in the same commit.
