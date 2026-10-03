# PLAN — the security lane's methods within cyclomatic complexity 4

> Status: **plan only, nothing implemented yet, 2026-10-03.** Scope: the security lane's own C# files
> (`src_mcp/core/Security/*`, `src_mcp/src/Server/Security*.cs`, `src_mcp/runners/Reviewers/SecurityAnswerLimit.cs`,
> `InputCoverage.cs`, `src_mcp/src/Store/SecurityFindingStore.cs`). Behaviour-neutral refactor.
>
> Related docs: [module_security_lane.md](../research/module_security_lane.md), [architecture.md](../research/architecture.md).

## Goal

The family's C# doctrine (`.agents/conventions/csharp/doctrine.md` §6) bounds every method at cyclomatic
complexity **4**. The security lane (PR #634, merged 2026-10-03 as `5c9fecd2`) shipped with roughly twenty
methods above that bound; the independent review of that PR named it and the operator asked for a separate
pull request. Methods over the bound are where a branch gets missed in review and in tests — several of the
lane's defects fixed in #634 (a refusal recorded as a skip, a budget folded into the wrong gate) sat in exactly
such methods.

## What must be true when done

1. Every method in the scoped files has cyclomatic complexity ≤ 4 (1 + each `if`, `case`/switch arm with a
   condition, `&&`, `||`, `?:`, `??`, `catch`, loop, `when`). The count is written per method in the PR.
2. **No observable behaviour changes**: the same refusals, complaints, omissions, ordering, caps, budgets and
   texts, byte for byte where a string is produced. The whole existing suite passes unchanged — no test is
   edited to make a refactor pass.
3. Where a method is split, the new units are named for their job (`ReadContext`, `RefusalOfRun`, …),
   `private static` where they read no state, and the file stays under 800 lines.
4. Any branch the refactor touches that no existing test exercises gets a characterization test FIRST
   (written against the current code and observed green), so a regression is red rather than silent.
5. Docs: `module_security_lane.md` unchanged in substance; a sentence noting the bound is now met.

## Candidates (rough count, verified per method during the work)

| File | Methods over 4 (approx.) |
|---|---|
| `src_mcp/src/Server/SecuritySources.cs` | `ReadAsync` (~10) |
| `src_mcp/src/Server/SecurityLaneSetting.cs` | `ReadPrompt` (~9), `ReadRun` (~8), `ReadRuns` (~7), `ReadPrompts`, `Read`, `Number` |
| `src_mcp/src/Server/SecurityRoster.cs` | `Append` (~7), `ReadPrompt` (~6) |
| `src_mcp/runners/Reviewers/SecurityAnswerLimit.cs` | `ProtocolRefusal` (~8) |
| `src_mcp/core/Security/SecurityContext.cs` | `Collect` (~7), `Compose` (~6), `Place`, `PatchOnly` |
| `src_mcp/core/Security/AttackEvidence.cs`, `SecurityEvidence.cs` | `Read` (~7), `Reproduction.Read` |
| `src_mcp/core/Security/SecuritySignals.cs` | `Classify` (~6) |
| `src_mcp/runners/Reviewers/InputCoverage.cs` | `Of` (~6) |
| `src_mcp/src/Store/SecurityFindingStore.cs` | `Write`, `Read` (~5) |

## Approach

- Validation chains (`SecurityLaneSetting.Read*`, `ProtocolRefusal`) become ordered tables of
  `(predicate, refusal text)` evaluated by one small helper, or switch expressions — the shape the TypeScript
  side already uses in `securityLane.ts`. First failing rule wins, exactly as the `if` chains do now.
- Loops with several guards (`SecuritySources.ReadAsync`, `SecurityRoster.Append`, `SecurityContext.Collect`)
  split into "choose" and "process one" units; per-unit `try/catch` placement stays where reliability.md puts it.
- No new types beyond small private records/helpers; no public signature changes except where a private
  helper moves; Native-AOT safe (no reflection).

## Build order

1. Measure each method exactly; record before-counts.
2. File by file: characterization tests for untested branches → refactor → that file's tests green.
3. Whole Release suite + `dotnet format --verify-no-changes` + extension untouched (no TS change planned).
4. Code gate over the diff; PR.

## Test plan

- Existing: `Security*Tests`, `SecurityLane*Tests`, `ASourceRequestIsServedOrRefusedTests`, `SecurityPresetTests`,
  `SecurityProtocolTests`, `SecurityEvidence*Tests`, `ASecurityPreviewDatabaseKeepsBothStepsTests` — must pass
  untouched.
- New characterization tests only for branches found uncovered in step 2, each observed green before the
  refactor and green after.
- Full Release `CoaiMcp.Tests.exe` run; the two load-sensitive tests already listed in
  `todo/PLAN_question_consultant_tails.md` §6 are judged by running them alone if they fail in the full run.

## Growth surfaces

None. No table, file, cache or process is added.

## Definition of Done

- [ ] Every scoped method ≤ 4, with the per-method before/after table in the PR.
- [ ] No existing test edited; full Release suite green (or the known §6 flakes shown green alone).
- [ ] Characterization tests added for every newly-split branch that lacked one.
- [ ] `dotnet format --verify-no-changes` clean; Release build 0 warnings.
- [ ] `module_security_lane.md` updated; this plan promoted to `research/` with its deviations.
- [ ] Code gate run over the diff; PR merged after CI and review comments are handled.
