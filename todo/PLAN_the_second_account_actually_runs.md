# PLAN — the second account actually runs

> Status: **DEFERRED by the operator, 2026-09-11 — not built, and the reason is on the record.**
> The defect is real and reproduced; its effect today is **nil**, because this deployment runs one
> slot per vendor, so nothing observable is wrong until a second account is signed in. What shipped
> instead is the correction to the two documents that promised otherwise —
> [RESULTS_team_server_under_load.md](../research/RESULTS_team_server_under_load.md) said
> *"parallelism per vendor is bought in ACCOUNTS"* and `module_team_server.md` said the drain keeps
> starting while the vendor says yes. Both now say what the code does.
> **Re-open this the day a second account is added**, before it is added rather than after: the
> symptom is that nothing gets faster and nothing says why. Scope:
> `src_server/src/Jobs/JobRunner.cs`, `src_server/src/Slots/AccountSlot.cs` (`SlotSelector`).
> Finding 5 of [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md).
>
> Related docs: [module_team_server.md](../research/module_team_server.md) — story 2.3,
> [RESULTS_team_server_under_load.md](../research/RESULTS_team_server_under_load.md),
> [PLAN_team_server_submission_order.md](PLAN_team_server_submission_order.md) — the neighbouring queue
> question, which stays as it is.

## The symptom

A vendor with two signed-in accounts runs **one** review at a time.

`JobRunner.PumpAsync` (`JobRunner.cs:43`) asks `SlotSelector.Pick` for ONE candidate (`:54`) — the
least-recently-used account that `IsReady` (`AccountSlot.cs:31`), and `IsReady` knows about cooldowns
and sign-ins, not about a lock somebody holds. It then tries that one account with a zero wait
(`:63`) and, refused, returns false (`:64-68`). `LastUsedUtc` is written when a lease is RELEASED
(`SlotRegistry.cs:311`, called from `SlotLease.Dispose`), so for as long as account `a` is busy its
timestamp is the old one and it stays first in the ranking. The pump's drain loop
(`JobPump.cs:99-102`) — written, per its own comment, so that "a vendor with ten free accounts and a
full queue" does not "leave nine accounts idle" — is ended by that first refusal.

The audit reproduced it on the real `JobRunner` and `SlotRegistry`: two ready accounts, two queued
jobs, the first blocked inside a fake launcher; the second `PumpAsync` returned false —
**launched 1, queued 1, with two slots.**

Two documents say otherwise. `RESULTS_team_server_under_load.md:96` — *"Parallelism per vendor is
bought in ACCOUNTS"* — and `module_team_server.md`'s story 2.3 bullet *"The pump drains rather than
ticking … it now keeps starting while the vendor keeps saying yes, and the slot lock is what stops it."*
The campaign ran one slot per vendor (`RESULTS_team_server_under_load.md:164`), so it could not have
seen this; the deployment today has one slot per vendor, so nobody has felt it. It is the day a second
account is signed in — the documented way to scale — that nothing gets faster and nothing says why.

## The change

1. **`SlotSelector.Ranked(slots, now)`** — every ready account, least-recently-used first, name as the
   tie-break; `Pick` becomes `Ranked(…).FirstOrDefault()` so `Explain` and the rate-limit rotation in
   `JobRunner.RateLimited` keep their meaning unchanged.
2. **`PumpAsync` walks the ranking** and takes the first lease it is granted: a held lock is "next",
   not "none". Only when every ready account refused the zero-wait acquire does it answer false.
   `LastUsedUtc` stays a release-time stamp — with the walk, fairness follows on its own: the account
   that just released ranks LAST among the idle ones, and a busy account is skipped rather than
   waited for.
3. `module_team_server.md`'s bullet is corrected to say what the loop does now, and
   `RESULTS_team_server_under_load.md` gets a dated note beside the sentence it turned out to be
   promising ahead of the code.

No growth surface.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `JobRunnerTests.TwoAccountsRunTwoJobsAtOnce` — real `JobStore` and real `SlotRegistry` on a temp directory, two accounts marked signed in, two queued jobs, a fake `IReviewLauncher` that blocks on a `TaskCompletionSource`; `PumpAsync` twice → both `started` signals true, both jobs `Running`, on different slots | the audit's repro, as a test. RED today: the second returns false |
| 2 | `JobRunnerTests.AThirdJobWaitsWhenBothAccountsAreBusy` | the walk ends in false, not in a wait — the pump's back-pressure is still the lock |
| 3 | `SlotTests.RankedListsEveryReadyAccountLeastRecentlyUsedFirst` | the pure half, exhaustively, without a filesystem |
| 4 | The existing `SlotTests` for `Pick` and `Explain` | unchanged behaviour where nothing was meant to change |

## Definition of Done

- [ ] Tests 1–3 written, watched fail for the real symptom, passing; test 4 unedited and green.
- [ ] A vendor with N ready accounts starts up to N jobs in one pump tick.
- [ ] The two documents above no longer promise what the code did not do.
