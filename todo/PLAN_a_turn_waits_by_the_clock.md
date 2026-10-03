# PLAN — a file turn waits by the clock, not by a count of tries

> Status: **plan only, nothing implemented yet, 2026-10-03.** Scope: `src_mcp/src/Server/SessionStore.cs`
> (`SessionTurn.Take`), its callers (`CadenceStore`, `ConsultationStore`, `Escalations`, `SessionStore`), and the
> review-tree setup's git timeout in the test harness.
>
> Related docs: [module_server.md](../research/module_server.md); found while building
> [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (epic 3).

## The symptom

Two whole-suite runs on 2026-10-03 (branch `feat/consultant-works-everywhere-e3`, 6528 tests) each failed ONE
test, a different one each time, in code that branch did not touch; both pass alone, and a third run was
clean:

- `CadenceStoreTests.ManyClosesAtOnce_AllLand` — `CadenceStoreException: the cadence record … is being written
  by another server and its turn could not be had`;
- `AReviewTreeIsGivenBackTests.ATreeWhoseRecordDescribesAnotherOne_IsRefusedWithoutTouchingGit` — `Expected
  made.Reason to be empty, but found "git_failed"`, in setup, after 1 min 02 s.

The rule is that a test passing alone and failing in the suite names a SHARED thing, never a flake. The
shared thing here is the machine's CPU under the suite's own parallelism, and the defect it exposes is in
the product:

`SessionTurn.Take` (`SessionStore.cs`) gives up after **40 attempts with 2–8 ms sleeps** — about 200 ms of
wall time, counted in attempts rather than against a clock. Its own remarks say a missed turn "is not fatal on
its own" because a READER answers "no session"; but `CadenceStore` THROWS on it, so twelve concurrent closes
under load lose one. A user's gate call on a loaded machine can meet the same sentence.

## The shape

- `SessionTurn.Take(file, TimeSpan budget)` waits until a stated DEADLINE (default ~2 s, the counter's
  contention budget), with jittered backoff, and its failure says so in its own words.
- Callers that throw on a missed turn (`CadenceStore`) get the deadline; readers keep their "no session".
- The review-tree test setup's git timeout is checked against the harness's budget under load.

## Build order

1. RED: a test that holds the turn for 500 ms from another thread and asserts `Take` still gets it (today it
   gives up at ~200 ms).
2. Deadline-based `Take`; callers reviewed one by one.
3. Re-run the whole suite three times; record the counts.

## Test plan

- The held-turn test above; a turn never released fails at the deadline with its sentence.
- `CadenceStoreTests.ManyClosesAtOnce_AllLand` under an artificial CPU load.

## Definition of Done

- [ ] No retry loop in the turn counts attempts instead of time.
- [ ] Three consecutive whole-suite runs clean.
