# PLAN — the resolver says what the change selects

> Status: **plan only, nothing implemented yet — and BLOCKED on a dependency in another repository.**
> Extracted 2026-09-15 from
> [PLAN_the_rules_a_round_shows_are_drawn_at_random.md](../research/PLAN_the_rules_a_round_shows_are_drawn_at_random.md)
> (epics 2 and 4 of it), which shipped everything that did not need this dependency.
>
> Scope: `src_mcp/runners/Context/` (a new `RuleManifest` and `RuleResolver`), the code stage in
> `src_mcp/src/Server/PanelService.cs`, and their tests.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [module_server.md](../research/module_server.md),
> [RESULTS_rules_selection_budget.md](../research/RESULTS_rules_selection_budget.md) (the measurement
> that makes the case for this plan).

## Why this exists

Rule selection is now deterministic and its priority is written down, but it is still **generic**: a
round is judged against the same eight tier rules whatever it changed, plus a tail rotated by branch.
The measurement says why that is not the end of the story — the corpus is 272 121 bytes against an
80 000-byte budget, so **24 of 32 rules reach no code round at all** on this path, and which 24 depends
on a branch name rather than on the change.

The answer is to select by what the change actually touches: a diff that edits a workflow file should
get `git-workflow.md` BECAUSE it edits one, not by luck. The shared conventions repository already has
the machinery — a metadata-driven resolver (`tools/rules.mjs`) that answers "which rules apply to these
files and this task", with `paths:`, `tasks:` and `depends:` frontmatter on every rule.

## The blocker, stated first because nothing here starts without it

**E1 — the resolver cannot run against a round's checkout.** Two independent refusals, both measured
2026-09-15:

1. `node_modules/` is git-ignored in `dew_flow_conventions` and `tools/lib/rule-catalog.mjs` imports
   `yaml` and `picomatch`, so **any** fresh checkout of the submodule — `SubmodulePopulator`'s and an
   ordinary `git submodule update --init` alike — dies with `Cannot find package 'yaml'` before it
   reaches a single git check. CI is unaffected (`ci.yml` installs the shared instruction
   dependencies); `coai-mcp` runs where nobody may have.
2. Running the PARENT checkout's script against `--repo <worktree>` is refused by `revision()`
   (`rule-cli.mjs:84`): `realpath(<repo>/.agents/conventions) !== realpath(installedRoot)` →
   *"Run this repository's own mounted resolver"*.

**The fix belongs in `dew_flow_conventions`, not here**: relax `revision()` from PATH identity to SHA
identity — accept a `--repo` whose index gitlink equals `installedRoot`'s HEAD and whose mount is clean
at that SHA — so the parent's script (which HAS its dependencies) can serve a worktree. Until that
lands and reaches this repository through a `release` promotion, every code round falls back to the
deterministic walk, which is why that walk was built to be good enough to live on.

**E2 — the `topics:` vocabulary.** `rule-catalog.mjs:83` **throws** on an unknown metadata key, so a
`topics:` key added before its consumers can read it does not degrade — it takes the resolver down for
all six repositories. E2 must therefore ship before anything here reads a topic.

## Build order

### Epic A — `RuleFiles` asks the resolver what this change selects

1. **A `RuleManifest` reader.** Launch `node .agents/conventions/tools/rules.mjs explain --repo <root>
   --task <task> --file <path> …` through the existing `IProcessLauncher` — no second process helper.
   Use `explain`, not `read`: `read` refuses a payload over 32 KiB (`rule-cli.mjs:112`), less than half
   this gate's budget, and would force one launch per rule. `explain` returns `id`, `source`, `bytes`,
   `hash` and `reasons` per selected rule, and the gate reads those files itself, keeping the existing
   whole-file budget and omission logic.
2. **Batch, never drop.** More changed paths than the resolver's 256-file limit are sorted canonically,
   split into deterministic batches and merged by rule id; a path that still cannot be resolved is
   named in the prompt beside the omissions. A selector silently discarded is a rule silently missing,
   which reads to a reviewer as compliance.
3. **Canonical order.** Manifest rules ordered by reason strength (path match, then topic, then task,
   then always-loaded), ties by rule id — never by enumeration order. Each rule carries its `reasons`
   into the prompt so a finding can say why its rule was shown.
4. **One fallback boundary covering every failure mode**: `node` absent or not executable, a launch
   that throws, a bounded timeout with the process tree killed, a non-zero exit, malformed or partial
   JSON, a manifest naming an unreadable file, an empty or dirty mount, a worktree the resolver
   refuses. All land on the deterministic walk, with the reason logged and a sentence in the prompt
   saying the selection was untargeted. A partial payload is never sent as a complete selection.

### Epic B — symbol triggers widen the selection (CONDITIONAL on E2)

5. **Conventions half, its own pull request**: a `topics:` key beside `tasks:`, with a domain
   vocabulary (`efcore`, `threading`, `validation`, `http-client`, …) backfilled across the rules, and
   **additive** — a rule without one keeps being selected by its `tasks:` and `paths:`, so no rule can
   become unreachable by not being tagged.
6. **This repository's half**: a table over the diff's **added lines only** — `CancellationToken`,
   `IAsyncDisposable`, `lock`, `HttpClient`, `Channel<T>` → topics, passed as additional selectors.
   Deterministic, explainable, no index, no model.

**Rejected already, with the reason recorded**: BM25 or embedding ranking over the corpus. It
reintroduces the property the parent plan removed — a one-line change to a diff silently permutes the
ranking, and nobody can answer "why was this rule shown?".

### Follow-up, not in this build order

**Rule modularization** — split rules over 15 KB into sections, in `dew_flow_conventions`. Selection is
whole-file, so one rule can take a third of the budget: `testing.md` 25 082, `git-workflow.md` 22 141,
`reliability.md` 16 871. Tagging makes the payload relevant; it does not make it small. Note
`tools/rules.test.mjs` SHA-pins rule bodies, so a split updates that baseline deliberately.

## Test plan

xUnit v3 on Microsoft Testing Platform — build, then run the **executable**; never `dotnet test`:

```bash
dotnet build dew_flow_connect_other_ais.slnx
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*RuleResolverTests"
```

| Test | Guarantee |
|---|---|
| `TheManifestsSelection_IsTheBundle_InCanonicalOrder` | the wiring and the order |
| `MoreChangedFilesThanTheResolverTakes_AreBatched_NotDropped` | no silent selector loss |
| `AMissingNode_AThrownLaunch_ATimeout_ABadExitAndMalformedJson_AllFallBack` | one boundary, every mode |
| `AManifestNamingAnUnreadableFile_FallsBack_RatherThanSendingAPartialBundle` | the read phase |
| `ARepositoryWithoutTheMount_IsNotAskedAtAll` | no process start for a legacy repo |
| `AResolverRefusal_IsCarriedVerbatimAsTheReason` | the diagnosis reaches a human |
| `ATokenInAnAddedLine_SelectsItsTopic` / `…InARemovedLine_DoesNot` | added lines only |

## Definition of Done

- [ ] E1 has landed in `dew_flow_conventions` and reached this repository through a `release` pin.
- [ ] No round can fail because of a resolver, a submodule, `node` or a file read — every mode lands on
      the deterministic walk with a logged reason, and the prompt says the selection was untargeted.
- [ ] Every story opened with a failing test whose message names the real symptom.
- [ ] Epic B built only if E2 has shipped; otherwise it stays here and says why.
- [ ] `research/module_runners.md` and `module_server.md` updated; `architecture.md` gains the node
      resolver as an external dependency of `coai-mcp`.
- [ ] A re-measurement against `RESULTS_rules_selection_budget.md`: how much of the corpus a targeted
      round reaches, compared with the 8-of-32 the tier reaches today.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations recorded.
