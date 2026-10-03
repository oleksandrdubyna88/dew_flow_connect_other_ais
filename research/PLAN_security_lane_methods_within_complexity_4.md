# PLAN — the security lane's methods within cyclomatic complexity 4

> Status: **IMPLEMENTED, 2026-10-03.** Every method in the scoped files is at cyclomatic complexity ≤ 4, held there by CA1502 as a build error for exactly those files; deviations in the section below. Scope: the security lane's own C# files
> (`src_mcp/core/Security/*`, `src_mcp/src/Server/Security*.cs`, `src_mcp/runners/Reviewers/SecurityAnswerLimit.cs`,
> `InputCoverage.cs`, `src_mcp/src/Store/SecurityFindingStore.cs`). Behaviour-neutral refactor.
>
> Related docs: [module_security_lane.md](module_security_lane.md), [architecture.md](architecture.md).

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

- Validation chains (`SecurityLaneSetting.Read*`, `ProtocolRefusal`) are decomposed into named private
  helpers, each ≤ 4 — or, where a chain is long and uniform, an ordered table of rules whose MESSAGE is lazy
  (`Func<T, string>`), evaluated in order, first failure wins, no message built for a rule that does not fire.
  Switch expressions are not a reduction technique: each conditional arm counts. (Plan round 1, findings 1–2.)
- Loops with several guards (`SecuritySources.ReadAsync`, `SecurityRoster.Append`, `SecurityContext.Collect`)
  split into "choose" and "process one" units; per-unit `try/catch` placement stays where reliability.md puts it.
- No new types beyond small private records/helpers; no public signature changes except where a private
  helper moves; Native-AOT safe (no reflection).

## Build order

1. Turn the bound into a build check: `CA1502` (cyclomatic complexity) as an ERROR for the scoped files only,
   via `.editorconfig` path sections and a `CodeMetricsConfig.txt` threshold, first as a warning to produce the
   offender list (the enumeration comes before the fix), then as an error once the files are fixed. (Plan round 1,
   finding 4.)
2. File by file: characterization tests for untested branches → refactor → that file's tests green.
3. Whole Release suite + `dotnet format --verify-no-changes` + extension untouched (no TS change planned).
4. Code gate: `review_code` over the whole committed diff with this plan as the scope (kept with the session and
   passed explicitly); then the PR.

## Test plan

- Existing: `Security*Tests`, `SecurityLane*Tests`, `ASourceRequestIsServedOrRefusedTests`, `SecurityPresetTests`,
  `SecurityProtocolTests`, `SecurityEvidence*Tests`, `ASecurityPreviewDatabaseKeepsBothStepsTests` — must pass
  untouched.
- New characterization tests only for branches found uncovered in step 2, each observed green before the
  refactor and green after.
- Full Release `CoaiMcp.Tests.exe` run, all green together. A failure in the full run is this change's defect
  unless the SAME failure reproduces on a clean `origin/main` checkout, recorded with that evidence. Passing alone
  is not an acquittal. (Plan round 1, finding 0.)

## Stories (split per the gate's command; on Opus — Fable's monthly limit was hit on 2026-10-02)

| Story | What | Files |
|---|---|---|
| S1 | The guard: `CA1502` scoped to the lane files, threshold 5 (fires above 4), warning → inventory | `.editorconfig`, `CodeMetricsConfig.txt`, csproj `AdditionalFiles` |
| S2 | Validation: settings parsing, answer protocol, evidence readers | `SecurityLaneSetting.cs`, `SecurityAnswerLimit.cs`, `AttackEvidence.cs`, `SecurityEvidence.cs`, `SecurityFindingStore.cs` |
| S3 | The pipeline: roster, source reader, context composer, signals, input coverage | `SecurityRoster.cs`, `SecuritySources.cs`, `SecurityContext.cs`, `SecuritySignals.cs`, `InputCoverage.cs` |
| S4 | Guard to error, docs, full verification, promotion | `module_security_lane.md`, this plan |

The gate runs once, over the whole diff, after S4 is committed.

## Growth surfaces

None. No table, file, cache or process is added.

## Deviations — what shipped differently (2026-10-03)

- **One more file:** `SecurityRound.cs` (`Clause` 9, `Notice` 6) was in scope but missing from the candidate table; it was refactored with the rest. The offender list was **20 methods** (from 5 to 10), measured by CA1502 itself, not by the rough count above.
- **How CA1502 counts, measured:** every `if`, loop, `&&`, `||`, `?:`, `??` and `?.` adds one — including inside a lambda, charged to the method that holds it; `is`/`or` patterns and `catch` add nothing. `CodeMetricsConfig.txt` `CA1502: 4` reports complexity 5 and above (probed with a 4 and a 5).
- **Characterization tests: 178** in nine new files, each observed green against the unrefactored code; teeth shown by deliberately breaking refusal priority, the source reader's deadline `break`, the slice note and the local-engine turn count (10 tests went red, restored).
- **Refusal priority kept by reversal:** where a later `if` used to overwrite an earlier refusal (`ReadPrompt`, `ReadRun`), the new rule lists are evaluated in reverse so the same refusal wins.
- **A slow test, on purpose:** the source reader's deadline characterization waits out the real 30-second budget — making the budget injectable would have changed the code under test.
- Full Release suite 7196, 0 failed; the guard's teeth: one extra `if` in `InputCoverage.Of` fails the build with `error CA1502`.

## Definition of Done

- [x] Every scoped method ≤ 4, enforced by `CA1502` as an error for the scoped files; before/after table in the PR.
- [x] No existing test edited; full Release suite green together (a failure only acquitted by the same failure on clean main).
- [x] Characterization tests added for every newly-split branch that lacked one.
- [x] `dotnet format --verify-no-changes` clean; Release build 0 warnings.
- [x] `module_security_lane.md` updated; this plan promoted to `research/` with its deviations.
- [ ] Code gate run over the diff; PR merged after CI and review comments are handled.
