# PLAN — ConsultationService.cs and Program.cs under 800 lines

> Status: **plan only, nothing implemented yet, 2026-10-02.** Scope: `src_mcp/src/Server/Consultation/ConsultationService.cs`,
> `src_mcp/src/Program.cs`.
>
> Related docs: [module_server.md](../research/module_server.md),
> [PLAN_round_engine_and_panel_service_under_800_lines.md](PLAN_round_engine_and_panel_service_under_800_lines.md) (the same rule, other files).

## The symptom

The coding-style rule caps a file at 800 lines. On 2026-10-02 `ConsultationService.cs` was **1031** lines
and `Program.cs` **2168**. [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md)
adds only wiring to both and moves the consultation's failure branches out of the service; the split
itself was put to the operator and given its own plan (2026-10-02). After that plan's epic 5 the service had grown to
1070; its whole-branch review (2026-10-03) moved the failure application (decide, apply, give back, refuse before the
launch, fault after it) into `ConsultationFailedTurns.cs`, so on 2026-10-03 `ConsultationService.cs` is **928** lines and
`Program.cs` **2202** (the one-shot modes' help grew by two lines). The service's remaining split is still this plan's.

## The shape

- `ConsultationService.cs`: the turn (`RunTurnAsync` + launch + settle), the record lifecycle
  (`NewRecordAsync`, `Existing`, `Ended`, `Answered`) and the prompt composition (`PromptAsync`,
  `ShapedTreeAsync`) are three named units with their own responsibilities — extract them, never a
  `partial`.
- `Program.cs`: every one-shot mode already has a natural unit; move each mode's body into its own
  `*Mode.cs` beside its feature (the precedent is `Cadence/CadenceReadMode.cs`), leaving `Program.cs` the
  dispatcher and the stdio host.

## Build order

1. Mode bodies out of `Program.cs`, one commit per mode, no behaviour change.
2. The three units out of `ConsultationService.cs`.
3. `shared/refusal-sites.json` updated as call sites move.

## Test plan

- The whole suite unchanged and green after every step (pure moves).
- `ARequestFaultIsNotAnOldBinaryTests` and the one-shot scenario tests still pass.

## Definition of Done

- [ ] Both files under 800 lines, no `partial` used to get there.
- [ ] No behaviour change; suite green after each step.
