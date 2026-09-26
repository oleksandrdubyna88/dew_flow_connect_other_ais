# PLAN — `RoundEngine.cs` and `PanelService.cs` back under 800 lines

> Status: **plan only, not started, 2026-09-26.** The tail of the feature review's epic 2, asked by the
> operator on 2026-09-26 ("a plan as the tail, and that is all"). Scope: `src_mcp/src/Server/Rounds/RoundEngine.cs`,
> `src_mcp/src/Server/PanelService.cs`, new files beside them, `shared/refusal-sites.json`, the tests that
> call moved members. No behaviour change anywhere.
>
> Related docs: [PLAN_the_round_engine_leaves_the_panel_service.md](PLAN_the_round_engine_leaves_the_panel_service.md)
> (steps 5–7, which this plan extends and does not repeat), [PLAN_feature_review.md](PLAN_feature_review.md)
> §7.6, [module_server.md](../research/module_server.md).

## 1. The symptom

The repository's coding-style rule caps a file at 800 lines. After the feature review's epic 2
(measured 2026-09-26 on `feat/feature-review-e2` at `cc252f9b`):

| File | Lines | Why it grew |
|---|---|---|
| `src_mcp/src/Server/Rounds/RoundEngine.cs` | **838** | epic 2's skip-before-building, the held base and the fresh review (`HeldToBase`, `:588`) landed in the engine; the skip recording already moved to `Rounds/RoundSkips.cs` (873 → 820 before the code-review fixes) |
| `src_mcp/src/Server/PanelService.cs` | **1760** | the round-engine move took it from 3209 to ~1670; epic 2 added `ReviewFeatureAsync` (`:1385`) and the `feature` address on status/resolve/ask_human (`:434`, `:487-501`) |

No test enforces the ceiling, which is why every suite is green. The round-engine plan's open steps 5–7
(sweeps `:1239-1371`, resolve path `:1412-1603`, document stage `:869-1143`) move about **600** lines —
**not enough on their own**: the file would stand near 1160.

## 2. The shape

The same method as the round-engine plan: one extraction per pull request, each PROVED a move with
`node src_vs_code/scripts/prove-move.mjs origin/main <original> <new files…>`, every unmatched line
justified in the PR body, rebased and re-proved immediately before merging (a conflict inside a moved
region is re-cut from the new main, never resolved by taking a side), `refusal-sites.json` re-recorded
(`COAI_RECORD_REFUSAL_SITES=1`) in the step that moves a refusal.

## 3. Build order

The round-engine plan's steps 5–7 come first and stay in that plan. This plan adds:

**`PanelService.cs`**

8. **Provider probing** (`ProviderProbe`) — `ProvidersAsync` `:241`, `ProbeAsync` `:261`, `AuthFor`/`AuthOf`
   `:288-305`, `HasServerToken` `:306`, `RuntimeFor` `:310`, `CanRun` `:322`, `ExcludedFrom` `:337`,
   `ReasonFor` `:355`. About 135 lines; reads settings only.
9. **The code stage's prelude** (`CodeStage`) — `ReviewCodeAsync` ×2 `:617-789`, `CadenceBeforeTheCode`
   `:790`, `CodeRound`/`SinceWhenFinished`/`LastCodeRound` `:801-818`, `NothingSince` `:819`,
   `NothingOver` `:836`. About 250 lines; mirrors `FeatureStage`, which already lives in `Server/Stages/`.
10. **The plan stage and its lenses** (`PlanStage`) — `ReviewPlanAsync` ×2 `:521-616`, `UnspentPlanLenses`
    `:1144`, `Pool` `:1168`, `StableSeed` `:1187`, `Scope` `:1199`. About 190 lines. Only if the file is
    still over 800 after step 9 (the estimate after steps 5–9 is ~775 — too close to call).

**`RoundEngine.cs`**

R1. **Remote roles** (`RemoteRoles`) — `WarmRemoteRolesAsync` `:121`, `AskWhichRolesAsync` `:151`. ~48 lines.
R2. **The round's deadline** (`RoundDeadline`) — `WhatEndedIt` `:720`, `ConfiguredReviewers` `:730`,
    `RoundDeadlineFor` `:733`. ~52 lines, pure but for settings.
R3. **The answer** (`RoundAnswer`) — `AnswerFor` `:772`, `WhatTheCallerWasDoing` `:821`. ~64 lines.

R1–R3 bring the engine to ~675. `RunStageAsync` (`:169-580`, ~410 lines) is the real weight and is
**not** split here — cutting its phases apart changes control flow, which is not a move; it deserves its
own plan if the operator wants it.

Order: R1–R3 in one PR of three proved commits (small, one file); steps 8–10 one PR each. None of them
runs beside the feature review's S3.2/S3.4, which edit `RunStageAsync` and the round machine — whichever
lands second rebases and re-proves.

## 4. Test plan

- No test assertion changes. Tests that call a moved `internal static` member follow it (a using or a
  type name), and the PR body lists each.
- The whole suite through the executables after every step: `CoaiMcp.Tests.exe`, `CoaiServer.Tests.exe`,
  `CoaiBugs.Tests.exe`; `npm test` in `src_vs_code` (the seam test drives the built server).
- `dotnet format --verify-no-changes` on the touched projects; `refusal-sites.json` byte-identical
  unless a refusal moved.

## 5. Definition of Done

- [ ] `RoundEngine.cs` ≤ 800 lines; every new file ≤ 800.
- [ ] `PanelService.cs` ≤ 800 lines (with the round-engine plan's steps 5–7).
- [ ] Every step proved a move after its last rebase; no test assertion changed; all suites green.
- [ ] `research/module_server.md` names each new unit.
- [ ] This plan and the round-engine plan promoted together once both files are under the ceiling.
