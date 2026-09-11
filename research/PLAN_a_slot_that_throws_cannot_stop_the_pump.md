# PLAN — a slot that throws cannot stop the pump

> Status: **IMPLEMENTED, 2026-09-11.** Story 3.1. Scope as built:
> `src_server/src/Jobs/JobRunner.cs`, `JobPump.cs`.
>
> Related docs: [module_team_server.md](module_team_server.md) — story 2.3.

## What shipped differently

Nothing of substance: both guards landed as planned. The third arm the plan round asked for — the host's
token in the wait — proved unnecessary, because `ct` is already passed into the run where it reaches the
lease and the claim, so `WhenAny(promise, run)` cannot outlive a cancelled tick. The reproduction was the
audit's own: a DIRECTORY where the `.lock` file goes, which raises `UnauthorizedAccessException` past
`TryOpen`'s `IOException` catch.

## The symptom

`JobPump.CanStartAsync` (`JobPump.cs:108-118`) starts `runner.PumpAsync` and then awaits ONLY the
`started` promise (`:118`), with no cancellation token. `JobRunner.PumpAsync` (`JobRunner.cs:43-84`)
completes that promise on its four ordinary exits (`:47`, `:56`, `:65`, `:74`, `:81`) and on nothing
else. Between the first and the last of those sit three calls that touch the filesystem and can
throw on an ordinary bad day — `slots.SlotsOf` (`:54`) reads every slot's `state.json`,
`AcquireAsync` (`:63`) creates the directory, chmods it and opens `.lock` — and an exception from any
of them faults the task, is logged by the continuation at `:111-115`, and leaves `started` pending
**for ever**.

What that costs: the pump's tick is stuck inside `StartWhatCanRunAsync`, so

- no vendor after this one is pumped again;
- `Expire` never runs again — a running job whose deadline passes goes on running and spending, the
  exact failure the sweep exists to prevent (`JobPump.cs:5-12`);
- the `catch (Exception)` at `JobPump.cs:38`, written so that "the pump survives anything, loudly",
  never sees the exception, because the exception went into a task nobody awaited;
- `StopAsync` waits its whole host timeout, because the await ignores `stoppingToken`;
- `/api/health` keeps answering, and the server keeps answering 202.

The audit's reproduction: a slot whose `.lock` path is a DIRECTORY. `AcquireAsync` throws
`UnauthorizedAccessException`; the task faults; `startSignalCompleted = false`.

## The change

Two guards, at the two ends of the promise, because a contract enforced at one end is what the next
edit to the other end breaks:

1. **The producer keeps its promise on every exit.** `PumpAsync`'s body goes inside
   `try … finally { started?.TrySetResult(false); }`. After a `TrySetResult(true)` the `finally` is a
   no-op; after a throw it is the answer the pump was waiting for. The exception still travels — the
   task still faults and the continuation still logs it.
2. **The consumer does not await a promise it cannot see broken.** `CanStartAsync` awaits
   `Task.WhenAny(started.Task, run)`: a run that faulted before signalling reads as "nothing started",
   and the loop moves to the next vendor. This is the reliability rule's *no unobserved
   fire-and-forget*, applied to a promise instead of a task.

No growth surface.

### What the gate's plan round changed (2026-09-10, accepted)

**The wait takes the host's token as well** (local, Blocking). Guard 1 already means the promise is always
answered, so the deadlock the finding describes cannot survive both guards — but the wait itself still
ignored `stoppingToken`, and a wait that CANNOT be interrupted is a wait whose correctness depends
entirely on somebody else keeping a promise. That is the same reasoning as guard 2 and it belongs at the
same seam: `CanStartAsync` takes `ct`, and the `WhenAny` includes its cancellation. Three independent ways
out of one wait is the point, not redundancy — each of the three is what the other two's next edit
breaks.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `JobRunnerTests.AThrowBeforeTheClaimStillAnswersTheStartSignal` — the audit's slot with a directory at `.lock`; `started` completes (false) within seconds, and the task's own fault is observed | RED today: the await times out |
| 2 | `JobPumpTests.ABrokenSlotOnOneVendorDoesNotStopTheNextVendor` — the real `JobPump` (`StartAsync`, a 50 ms tick), a catalog of two vendors, the first with the broken slot, one queued job each, a fake launcher; the second vendor's job reaches `Running` | the pump keeps going. RED today: it never starts |
| 3 | `JobPumpTests.ThePumpStopsWhenTheHostStops` — same broken slot; `StopAsync` returns inside its timeout | a wedged pump no longer wedges shutdown |

## Definition of Done

- [ ] Tests 1–3 written, watched fail for the real symptom, passing.
- [ ] Guard 1 in `JobRunner`, guard 2 in `JobPump`; the existing continuation still logs the fault.
- [ ] `module_team_server.md` story 2.3 records the invariant: *the start signal is answered on every exit, and the pump does not trust that it will be*.
