# PLAN — the round engine leaves the panel service

> Status: **plan only, nothing implemented yet, 2026-09-25.** Scope: `src_mcp/src/Server/PanelService.cs`
> and new files beside it; `shared/refusal-sites.json`; tests that call the moved members. No behaviour
> change anywhere.
>
> Related docs: [PLAN_feature_review.md](PLAN_feature_review.md) (D16 — why this goes first),
> [module_server.md](../research/module_server.md),
> [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md) (the same method, on
> the extension's largest file).

## 1. The problem

`src_mcp/src/Server/PanelService.cs` is **3 056 lines** (`origin/main` 21aba62e) against the 800 the
coding rule allows, and it is one `public sealed partial class PanelService` (`:24`) that is at once
the MCP surface's facade (open, review_*, resolve, status, ask_human, consult), the **round engine**,
and a set of sweeps and helpers. The round engine alone is roughly 1 300 lines:

| Member | Line | What it is |
|---|---|---|
| `RunStageAsync` | `:1237` | the one engine every stage runs through — claim, begin, deadline, assemble, schedule, dedup, gate, verdict, save, project |
| `BuildWork` | `:1913` | the roster: eligible vendors × roles → `ReviewerWork` (with `Items`, `Lens`, `Assemble`, `Everyone`, `Hand`, `CanCarry`, `WhyNotCarried`, `Carriers`, `:2136-2322`) |
| `RolesWithRulesInMind`, `RolesNotAsked` | `:2324`, `:2339` | roster helpers, `internal static`, called by tests |
| `WhatEndedIt`, `ConfiguredReviewers`, `RoundDeadlineFor` | `:2370-2418` | the round deadline |
| `MayProceed`, `CallerFor`, command texts, `CommandStageOf` | `:2419-2496` | the orders a round carries |
| `ComposePrompt`, `WithoutTheStaleClaim`, `WhatYouHave` | `:2498-2564` | the reviewer's prompt |
| `AnswerFor` | `:2566` | the verdict → wire answer |
| `NotifyIfAPersonMustDecide`, `RulesSection`, `ChoiceFor`, `WhereTheDocumentWent` | `:1789-1912` | round-time helpers |
| `RoundWork`, `ExcludedRole`, `StageRun` | `:2958`, `:2993`, `:3013` | the engine's records |

The feature review (a fourth stage) adds hooks to exactly these members — `StageRun` fields, a skip
branch in `RunStageAsync`, a three-valued `ReaderMaterial` in `ComposePrompt`/`WhatYouHave`, a
continuation in `BuildWork`. Landing them in the 3 056-line file makes it bigger; moving the engine
AFTER them means moving freshly written, freshly reviewed code. So the move goes first.

## 2. The shape

- `src/Server/Rounds/RoundEngine.cs` — `internal sealed class RoundEngine`, owning `RunStageAsync` and
  the deadline, the orders and the answer. Its dependencies are passed in its constructor (settings,
  store, claim, scheduler, projection, notifier, logger) — **each moved member takes its fields with
  it**, and a member that still reaches back into `PanelService` has not found its seam yet (the
  method `PLAN_the_panel_provider_is_too_big.md` settled for the extension).
- `src/Server/Rounds/RosterBuilder.cs` — `BuildWork` and its roster helpers.
- `src/Server/Rounds/ReviewerPrompt.cs` — `ComposePrompt`, `WithoutTheStaleClaim`, `WhatYouHave`
  (static where they already are).
- `src/Server/Rounds/StageRun.cs`, `RoundWork.cs` — the records, moved verbatim.
- `PanelService` keeps the MCP-facing methods and calls `_engine.RunStageAsync(...)`; the `partial`
  stays only if a `[GeneratedRegex]` remains in it.

Target: `PanelService.cs` under 1 800 lines after this plan; each new file under 800. Under 800 for
`PanelService` itself is NOT promised here — the stages' own entry points (`ReviewDocumentAsync` and
its helpers, `:724-1011`) are a later extraction, named in §6.

## 3. Build order — one extraction per pull request

1. **Records** (`StageRun`, `RoundWork`, `ExcludedRole`) to their own files. Zero logic.
2. **Prompt** (`ReviewerPrompt`) — static members, the lowest cost of being wrong; proves the method.
3. **Roster** (`RosterBuilder`) — `BuildWork` and helpers; the `internal static` test entry points
   keep their names (tests call them) — re-pointed, not rewritten.
4. **Engine** (`RoundEngine`) — `RunStageAsync`, deadline, orders, `AnswerFor`,
   `NotifyIfAPersonMustDecide`.

Each step: `node src_vs_code/scripts/prove-move.mjs origin/main <original> <new files…>` shows every
new line came from the original; every unmatched line (a constructor, a field, a changed call site) is
justified one by one in the PR body. **Rebase and re-prove immediately before merging** — git resolves
delete-versus-modify in favour of the delete, which silently reverts whatever main changed in the moved
region meanwhile (the extension's first split nearly did exactly that, with every check green).
`shared/refusal-sites.json` is re-recorded (`COAI_RECORD_REFUSAL_SITES=1`) in the step that moves a
refusal — the counts are per file.

The feature review's epic A1 defect fixes that touch these members (`Finish`, `CommandStageOf`,
round numbering) land BEFORE the move, so the move carries them.

## 4. Test plan

- **No test is edited except to re-point a call** (`PanelService.BuildWork` → `RosterBuilder.…`). A
  test whose assertion had to change would mean behaviour changed — stop and find out why.
- The whole `CoaiMcp.Tests` suite, through its executable, green before and after each step; the
  count of tests identical.
- `prove-move.mjs` clean (or every residue line justified) at merge time, after the last rebase.
- `ArchitectureTests` stay green; a new one pins that `RoundEngine` does not reference `PanelService`.

## 5. Definition of Done

- [ ] Four PRs, one extraction each, every one proved a move after its last rebase.
- [ ] `PanelService.cs` under 1 800 lines; `RoundEngine`, `RosterBuilder`, `ReviewerPrompt` each under 800.
- [ ] No test assertion changed; suite count unchanged; all green through the executable.
- [ ] `refusal-sites.json` re-recorded where a refusal moved.
- [ ] `research/module_server.md` describes the engine as its own unit; this plan promoted.

## 6. Not in this plan

- The stages' entry points (`ReviewDocumentAsync` … `DocumentContext`, `:724-1011`) and the resolve
  path (`:2610-2786`) — the next extraction, once the engine has left.
- Any behaviour change. Defects seen on the way are recorded here and fixed elsewhere.
