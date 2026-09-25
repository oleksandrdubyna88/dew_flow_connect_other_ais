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

Target: **`PanelService.cs` at or under 800 lines** — the coding rule's ceiling — and every new file
under 800. The engine alone does not get there (it leaves ~1 800), so this plan has a second phase
(§3, steps 5–7) that moves the rest of what the class is not: the document stage's entry points, the
resolve path and the sweeps. (Plan round, 2026-09-25: a plan that set 1 800 as its target was
planning a file the rule forbids.)

- `src/Server/Stages/DocumentStage.cs` — `ReviewDocumentAsync` … `DocumentContext` (`:724-1011`).
- `src/Server/Rounds/ResolvePath.cs` — `ResolveAsync`, `ResolveUnderClaim`, `WithHumanDecision`,
  `Finish`, `WhatTheCallerWasDoing` (`:2610-2786`).
- `src/Server/Sweeps.cs` — the scratch and answer-dir sweeps (`:1626-1744`).

## 3. Build order — one extraction per pull request

1. **Records** (`StageRun`, `RoundWork`, `ExcludedRole`) to their own files. Zero logic.
2. **Prompt** (`ReviewerPrompt`) — static members, the lowest cost of being wrong; proves the method.
3. **Roster** (`RosterBuilder`) — `BuildWork` and helpers; the `internal static` test entry points
   keep their names (tests call them) — re-pointed, not rewritten.
4. **Engine** (`RoundEngine`) — `RunStageAsync`, deadline, orders, `AnswerFor`,
   `NotifyIfAPersonMustDecide`.
5. **Sweeps** (`Sweeps`) — static, no state; low risk.
6. **Resolve path** (`ResolvePath`).
7. **Document stage** (`DocumentStage`) — last, because it is the largest behaviour surface.

Steps 1–4 are the feature review's prerequisite (D16); 5–7 may run beside that plan's Epic 2, one PR
each, and are what brings the file under 800.

Each step: `node src_vs_code/scripts/prove-move.mjs origin/main <original> <new files…>` shows every
new line came from the original; every unmatched line (a constructor, a field, a changed call site) is
justified one by one in the PR body. **Rebase and re-prove immediately before merging** — git resolves
delete-versus-modify in favour of the delete, which silently reverts whatever main changed in the moved
region meanwhile (the extension's first split nearly did exactly that, with every check green).
`shared/refusal-sites.json` is re-recorded (`COAI_RECORD_REFUSAL_SITES=1`) in the step that moves a
refusal — the counts are per file.

**The boundary with [PLAN_feature_review.md](PLAN_feature_review.md) §7.1:** that plan's Epic 1 merges
FIRST — it carries every defect fix that touches these members (`Finish`, `CommandStageOf`, round
numbering), so the move carries them. Steps 1–4 land next, and that plan's Epic 2 branches only after
step 4 is on `main`. Steps 5–7 run beside its Epic 2 or between its Epics 2 and 3, never concurrently
with its story S2.2 (which edits `resolve`/`status`/`ask_human`); whichever lands second rebases and
re-proves.

## 4. Test plan

- **No test is edited except to re-point a call** (`PanelService.BuildWork` → `RosterBuilder.…`). A
  test whose assertion had to change would mean behaviour changed — stop and find out why.
- The whole `CoaiMcp.Tests` suite, through its executable, green before and after each step; the
  count of tests identical.
- `prove-move.mjs` clean (or every residue line justified) at merge time, after the last rebase.
- `ArchitectureTests` stay green; a new one pins that `RoundEngine` does not reference `PanelService`.

## 5. Definition of Done

- [ ] Seven PRs, one extraction each, every one proved a move after its last rebase.
- [ ] `PanelService.cs` at or under 800 lines; every file this plan creates under 800.
- [ ] No test assertion changed; suite count unchanged; all green through the executable.
- [ ] `refusal-sites.json` re-recorded where a refusal moved.
- [ ] `research/module_server.md` describes the engine as its own unit; this plan promoted.

## 6. Not in this plan

- Any behaviour change. Defects seen on the way are recorded here and fixed elsewhere.
