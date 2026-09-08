# PLAN — the Team server's queue is not served in the order everybody typed

> Status: **IMPLEMENTED, 2026-09-08.** `SubmissionOrder.For` permutes a round's REMOTE reviewers
> among the positions remote reviewers already hold, and `BoundedScheduler` dispatches in that order
> while returning results in the caller's. Local reviewers keep their exact place; the server's
> `JobStore.TryClaim` was not touched.
>
> Deviations from the plan. The open question "shuffle or rotate by a per-client offset" was settled
> as SHUFFLE: a rotation needs a stable client id this side does not have, and the reporting
> requirement it was meant to protect is met a better way — by shuffling the DISPATCH order and
> reordering the results, so a person watching sees no movement at all. The remote marker is the
> invocation's `JobFile`, which only `RemoteRuntime` writes, rather than a new flag: a second field
> saying the same thing is a second field to forget. The plan's own test plan was tightened after
> CodeRabbit's note on it — the distribution test asserts flatness within a tenth of the fair share
> rather than "not always the same vendor", which a badly skewed shuffle would also satisfy.
>
> The FIFO guard the plan asked for already existed as `JobTests.TheOldestQueuedJobGoesFirst`; it
> gained the remark saying why it must not be relaxed now that the submitting side is random.
>
> Still open, deliberately: the depth-aware alternative below — a client submitting to the SHORTEST
> queue first, which needs the server to report per-vendor queue depth. Random removes the systematic
> bias; depth-aware would actually balance. Do the cheap one first and measure whether the second
> still earns its cost.
>
> Related docs: [module_runners.md](module_runners.md), [module_team_server.md](module_team_server.md),
> [PLAN_team_server.md](PLAN_team_server.md).

## The symptom, before anyone has felt it

A Team server holds one company account per vendor (`vendors.json` today: `codex`, `antigravity`,
`claude`, each with `slotConcurrency: 1`). A round submits every enabled reviewer at once — nine of
them, once three remote vendors are configured — and the server queues them per vendor.

**Everybody's reviewers arrive in the same order**, because everybody builds their list the same way:
the panel writes vendors in the order they were added, the settings file preserves it, and the client
fans out over that list. The catalog offers the vendors in a fixed order too, so the person adding
reviewers reads them top to bottom and adds them top to bottom.

With one person that is invisible. With ten people submitting rounds in the same minute, the first
vendor in everybody's list takes ten jobs into a queue of depth one while the third vendor's account
sits idle — and every one of those ten people waits behind nine strangers for the *same* account,
while an account that could have answered them immediately is doing nothing.

The load is not skewed because of demand. It is skewed because of a **list order nobody chose**.

## What must be true when this is done

1. Two clients submitting the same set of reviewers at the same instant do **not** reliably send them
   in the same order.
2. The randomisation applies **only to the Team-server call** — the order in which remote reviewers
   are SUBMITTED. Local reviewers, the round's own concurrency caps, and the order findings are
   reported in are untouched.
3. `JobStore.TryClaim` stays **FIFO**. This is the thing to get wrong: fairness on the server is what
   makes queue position mean something, and randomising the claim would make a person's wait
   unpredictable rather than merely long. The fix belongs entirely on the submitting side.
4. A round's REPORTED order (the rounds list, the log, the findings) is unchanged — a person watching
   should not see reviewers shuffle between runs.
5. It is measurable: with N clients and V vendors, the distribution of "which vendor got the first
   submission" is flat rather than a spike on the first list entry.

## Open questions to settle before building

- **Where exactly the order is fixed today.** It has to be read, not assumed: the panel's vendor list,
  the settings file, `ParseVendors`, and whatever the scheduler does with it. The randomisation goes
  at the LAST point that still knows the whole set, so nothing downstream re-sorts it.
- **Shuffle, or rotate by a per-client offset?** A shuffle is simpler; a rotation keyed on a stable
  client id spreads load just as well and keeps one client's own order stable between rounds, which is
  friendlier to read. Worth deciding on the reporting requirement (4) above.
- **Does the server need to help?** It could report per-vendor queue depth in the catalog and let a
  client submit to the shortest queue first — strictly better than random, and a bigger change. Random
  is the cheap fix that removes the systematic bias; depth-aware is the one that actually balances.
  Do the cheap one first and measure whether the second is still worth it.

## Test plan

- A pure unit over the submission order, asserting the FLATNESS the requirement above actually
  states rather than merely that the first vendor varies: over N submissions across V vendors, each
  vendor leads within an explicit tolerance of N/V. "Not always the same one" would pass a
  distribution that still sends 80 % of first submissions to one account, which is the defect.
  Seeded, so the assertion is deterministic. (CodeRabbit, PR 93.)
- A test that `JobStore.TryClaim` is still FIFO — the guard for requirement 3.
- A test that the round's reported reviewer order is unaffected.

## Definition of Done

- [ ] The submission order for REMOTE reviewers is randomised (or rotated), at one named place.
- [ ] `TryClaim` is untouched and has a test saying so.
- [ ] Local reviewers and reported order are unchanged.
- [ ] `research/module_team_server.md` records the decision and why the server side stayed FIFO.

## Where this came from

Asked for by the operator on 2026-09-07, the same evening the Team server completed its first review
ever ([research/PLAN_team_server_reviewer_never_called.md](PLAN_team_server_reviewer_never_called.md)
and the three defects fixed after it). It is a load question that only appears once the feature works
at all — which, until that day, it did not.
