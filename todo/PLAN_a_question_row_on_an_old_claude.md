# PLAN — a question consultant's claude disk row on a claude that has no `--restricted`

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/core/QuestionConsult/ConfinementPlanner.cs`, the
> question row's launch in `src_mcp/runners/Consultation/ClaudeConsultant.cs` (`Planned`), and
> `src_mcp/runners/Reviewers/ClaudeCapability.cs`.
>
> Related docs: [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md)
> (epic 3 solved this for the STUCK consultant),
> [RESULTS_claude_consultant_confinement.md](../research/RESULTS_claude_consultant_confinement.md),
> [module_runners.md](../research/module_runners.md).

## 1. The symptom (expected, not yet observed live)

The question consultant (S2 of the question-consultant plan) launches a claude row with `Capability.Disk` using
`--permission-mode plan --restricted --tools <ClaudeDiskTools> --add-dir …`, unconditionally
(`ConfinementPlanner.cs:139-143`). claude **2.1.197**, the version measured in WSL on 2026-10-02, does not know
`--restricted` and refuses the launch (`RESULTS_claude_consultant_confinement.md`). So a disk-capable claude question
row on such a CLI would fail every time, and the failure would read as a vendor error rather than "this CLI is too
old for the confinement you picked".

The stuck consultant already handles this. `ClaudeCapability.ProbeAsync` reads the installed CLI's `--help` on every
launch: Declared → `--restricted`; NotDeclared → the allowlist without it, with the limitation saying so; Unknown →
refuse. The question row goes through the planner path instead (the rebase onto main kept the two apart: `AsShipped`
for the stuck consultant, `Planned` for question rows), so it never asks.

## 2. Build order

1. **Measure first:** one disk-capable claude question row against WSL claude 2.1.197, through the product path.
   Record the exact failure in `research/`. If it does not fail, close this plan with the measurement.
2. **RED:** a planner/launch test with a fake claude whose `--help` lacks `--restricted`, asserting what the
   operator should see. Today that is a launch refused by the CLI.
3. **Decide with the operator.** On a NotDeclared CLI, should a `Disk` row:
   - (a) launch without `--restricted`, like the stuck consultant, with the limitation shown; or
   - (b) be refused before launch, with a cure ("update claude", or "pick `None`/`Web`")?

   The question consultant's admission rules may favour (b): a disk grant without a path boundary is wider than what
   the person granted.
4. **GREEN:** the planner takes the probed capability (`ClaudeCapability`, the one probe; never a second copy), and
   `ConfinementPlannerTests` pins both argv shapes.

## 3. Test plan

- The RED test above; `ConfinementPlannerTests` for Declared / NotDeclared / Unknown.
- A live re-run of step 1 after the fix, recorded in `research/`.
- Whole C# suite green.

## 4. Definition of Done

- [ ] Measured on WSL claude 2.1.197 and recorded.
- [ ] The operator's decision (a or b) recorded here before code.
- [ ] RED → GREEN; one probe shared by both claude paths.
- [ ] `module_runners.md` says how a question row behaves on an old claude.
