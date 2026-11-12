# PLAN — the macOS CI job finishes inside its time limit

> Status: **plan only, nothing implemented yet, 2026-11-12.** Scope: `.github/workflows/ci.yml` (the `macos` job) and,
> if the measurement points there, the slow tests of the MCP suite. Found while the terminal-chat E2 was released; a
> side finding, kept as a todo (the owner's rule: side findings become todos, not builds).

## The symptom

The job `macOS · mcp · server · bench` (`.github/workflows/ci.yml:289`) has `timeout-minutes: 30`
(`.github/workflows/ci.yml:292`). It is not a required check, so it blocks no merge. But a merge here waits for the
head's checks to exist and pass, so a cancelled job costs a re-run of about 30 minutes, and it hides a real failure
behind a timeout.

Observed in the last 37 CI runs (2026-11-11 16:52 to 2026-11-12 17:22 UTC), step times read from the run records:

| Step | Time |
|---|---|
| `Build` (`ci.yml:330`) | 0 to 2 min |
| `Test` — the whole MCP suite (`ci.yml:350`) | **17 to 27 min**; most runs 23 to 25 min |
| `Test the Team server` (`ci.yml:373`) and `Test the bench` (`ci.yml:377`) | under 1 min each |

Three runs were cancelled with `Test` at 26 or 27 minutes: e060d77e (main, the E2 merge, run 43180499534), 78b45415
and 067a5d78. A fourth, attempt 2 of ce72df75, was cancelled in `Test the Team server` after a slow `Test` (reported by
the session that re-ran it; the run record keeps only the last attempt). Other cancelled runs
stopped at 0 to 8 minutes: those were superseded pushes (`concurrency: cancel-in-progress`, `ci.yml:10-12`), not the
limit.

A separate symptom, NOT this plan's: attempt 1 of the same job on ce72df75 failed one timing test
(`AChildThatExitedBeforeItsStartTimeWasReadTests…IsGivenAMomentToEnd`, a `Win32Exception` from the process read).

## What is not known yet

Why the MCP suite takes 17 to 27 minutes on macOS. The Windows job runs the same suite under `timeout-minutes: 45`
(`ci.yml:392`). The spread (17 to 27 min on the same code) suggests runner speed or a few slow tests, not the build.

## Build order

1. **Measure first.** Run the macOS `Test` step once with per-test durations (the test runner's report of the slowest
   tests), on a branch. Record the 20 slowest tests and their share of the step.
2. **Choose from the data**, one of:
   - a few tests take most of the time → make them faster, or move them out of the macOS job if they test nothing
     macOS-specific;
   - the time is spread evenly → split `Test` into two steps by namespace, or raise the limit to 45 minutes (as the
     Windows job has) with the measurement as its reason.
3. Do not raise the limit without step 1: a higher limit with no reason only moves the edge.

## Test plan

- Step 1's report is committed in this plan (the 20 slowest tests, the step total).
- After the change: 10 consecutive CI runs of the `macos` job finish green, and none is cancelled by the limit.
  The longest run is recorded here with its margin to the limit.

## Definition of Done

- [ ] The slowest tests on macOS are measured and recorded here.
- [ ] The chosen remedy is merged, with its reason from the measurement.
- [ ] 10 consecutive runs finish inside the limit; the longest is recorded.
- [ ] The timing test failure above has its own todo, or is shown to be the same cause.
