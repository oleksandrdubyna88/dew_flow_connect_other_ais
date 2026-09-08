# PLAN — every client submits its reviewers in a different order

> Status: **IMPLEMENTED, 2026-09-08.** Scope: `src_mcp/src/Server/PanelService.cs` (`BuildWork`),
> `SeededShuffle` in the core, and tests on the ordering, the distribution and the invariant.
>
> Related docs: [module_server.md](module_server.md), [module_runners.md](module_runners.md),
> [PLAN_team_server_reviewer_never_called.md](PLAN_team_server_reviewer_never_called.md).
>
> **Deviations from the plan as written, which are the valuable part of this record:**
>
> 1. **The goal was overstated and had to be weakened.** It promised that two clients get DIFFERENT
>    orders. A seeded shuffle cannot promise that — with two vendors there are two possible orders,
>    so half of all client pairs collide however good the hash is. Three reviewers said so
>    independently on the plan round. What it delivers is SPREADING, measured as a distribution:
>    a hundred sessions at two and three vendors, no single order above 70 %.
> 2. **The plan's one open question was settled by measurement, not argument.** "Shuffle always, or
>    only when a remote vendor is present" — always, because the whole suite is green without an
>    expectation change, so no existing test encoded a meaningful provider order.
> 3. **The assumption underneath it became a test.** That list order is dispatch order was a
>    paragraph; `SubmissionOrderIsTheDispatchOrderTests` pins it with the machine cap at one. Its
>    scope was then narrowed again by the code round: the slots FREE when a round opens are taken in
>    list order deterministically — that prefix is what decides who is asked first — while who gets a
>    RELEASED slot afterwards is `SemaphoreSlim`'s business, and .NET documents no order for it.
> 4. **The reuse step grew a proof.** Folding two private Fisher-Yates loops into one `SeededShuffle`
>    picks one algorithm, and a reviewer asked what would happen if they had differed. They differed
>    only in their signatures — and a characterization test now carries the ORIGINAL loop so the next
>    refactor goes red against the algorithm rather than against a constant nobody can re-derive.

## The symptom

Everybody submits their reviewers in the same order.

`BuildWork` (`src_mcp/src/Server/PanelService.cs:916-930`) builds the round vendor-major: for each
runnable provider in `_settings.Providers` order, one item per role. The scheduler
(`src_mcp/runners/Reviewers/BoundedScheduler.cs:177`) starts a task per row and each one waits on the
same `SemaphoreSlim`, which hands out slots in the order they were requested. So the order of the
list is the order reviewers reach a Team server.

Every client ships the same default vendor list in the same order. Ten people starting a round at
9 a.m. therefore all ask for the first vendor's shared accounts first, wait, and only then ask for
the second — while the second vendor's accounts sit idle. The server's own queue is fair and is not
the problem: `JobStore.TryClaim` is FIFO by design, and a job it never received cannot be claimed
early.

**This is a Team-server problem specifically.** A local CLI has no shared account to contend for, and
a local engine is already serialised by its own lease.

## The goal

The order in which one client offers its reviewers must be SPREAD across clients rather than
identical for all of them, without giving up either of two properties the round already has:

1. **Reproducibility.** A round must be replayable — `PromptDeal` is already seeded from
   `StableSeed(sessionId, round)` (`PanelService.cs:397`) for exactly this reason.
2. **Fairness within one client.** Shuffling must not starve a vendor of its own round: every row
   still runs, exactly once.

## The shape

Shuffle the PROVIDER order, not the flat work list, and seed the shuffle the way the deal is
already seeded.

```
providers  = Shuffle(runnable, StableSeed(sessionId, round))
work       = providers × items          // unchanged otherwise
```

- **Providers, not rows.** Roles within one vendor are independent; interleaving them buys nothing
  and would make a round's progress log unreadable. What matters is which vendor is asked first.
- **Seeded, not random.** `StableSeed(sessionId, round)` differs per session (so two clients differ)
  and is stable per round (so a replay is a replay). This is the same function the deal uses, which
  is also the reason not to invent a second seeding scheme.
- **No server change.** `JobStore.TryClaim` stays FIFO. A fair queue fed in a biased order is fixed
  at the feeding end.

## What this does NOT promise, said before somebody assumes it

Three reviewers on the plan round arrived at the same objection from three directions, and they
were right. A seeded shuffle spreads orders; it cannot GUARANTEE that two clients differ. With two
vendors there are two possible orders, so half of all client pairs collide however good the hash
is; with three there are six. An earlier draft of this document claimed clients "get different
orders", which overstated it. The measured claim is the one in the tests: over a hundred sessions
at two and three vendors, no single order takes more than 70 %.

A guarantee would need the clients to coordinate — with each other or through the server — which is
a much larger change than the contention it would remove.

**Two more limits, stated rather than discovered later:**

- **Replay means the same session, the same round AND the same runnable set.** The seed is
  `StableSeed(sessionId, round)` and does not include which providers were runnable. A provider
  whose health flips mid-round changes the shuffle's INPUT, not just its order.
- **A deliberately arranged provider order stops meaning anything.** Nothing in this product treats
  the settings order as a priority today — it is the order rows appear in the panel — but somebody
  could reasonably have arranged it cheapest-first. After this change that arrangement is
  reshuffled every round. Accepted, and if it is ever wanted back the shape is a per-provider
  "pinned first" flag that survives the shuffle rather than a switch that disables it.

## The assumption this rests on, now a test

That the work list's order is the order reviewers actually start. It holds because `RunAllAsync`
builds its tasks with `work.Select(async ...)` — an async lambda runs synchronously to its first
await, which is the GLOBAL semaphore — and `Task.WhenAll` enumerates in list order, so every
reviewer reaches the machine's gate in list order. **The FIRST start is what that guarantees**, and
it is the one this change is about: which vendor a client queues for before anybody else's. Who gets
a RELEASED slot afterwards is `SemaphoreSlim`'s business and .NET documents no order for it, so the
tail is not asserted. `SubmissionOrderIsTheDispatchOrderTests` pins the first start with the cap at
one, and that every reviewer still runs exactly once.

## Open question, to settle before building

Should the shuffle apply to ALL rounds or only when a remote vendor is present? Applying it always
is one code path and no branch; applying it only to remote rounds keeps local rounds byte-identical
to today, which makes several existing tests' expectations about ordering keep holding without
being rewritten. **Assumption for the build: shuffle always**, because a vendor-order that depends
on the vendor list's contents is the kind of conditional that is discovered later by a test that
cannot explain itself. If the local-round tests turn out to encode a meaningful order rather than an
incidental one, that assumption is what changes.

## Build order

1. **RED — the ordering test.** Two sessions with different ids, the same providers and round: the
   provider order of `BuildWork`'s output differs for at least one of a handful of session ids.
   Fails today because the order is always `_settings.Providers`.
2. **RED — the invariant test.** For any seed, the multiset of `(provider, role)` rows is exactly
   the same as before the shuffle: nobody is dropped, nobody runs twice.
3. **RED — the replay test.** The same session id and round produce the same order twice.
4. Implement the seeded shuffle in `BuildWork`.
5. Run the whole suite; fix any test that was relying on `_settings.Providers` order incidentally,
   and say which ones did.
6. Update `research/module_server.md` (the `BuildWork` description) and
   `research/module_runners.md` (the scheduler's ordering note).

## Test plan

| Test | Where | Asserts |
|---|---|---|
| Two session ids give two provider orders | `src_mcp/tests` | the point of the change |
| One session id gives one order, twice | `src_mcp/tests` | replayability is not lost |
| Every (provider, role) row survives the shuffle | `src_mcp/tests` | no vendor is starved |
| A single-provider round is unchanged | `src_mcp/tests` | the degenerate case is not reordered into nonsense |
| No single order takes >70 % of 100 sessions, at 2 and 3 vendors | `src_mcp/tests` | the real, weaker claim |
| A fixed seed deals the hand it dealt before the shuffles were merged | `src_mcp/tests` | the refactor changed no permutation |
| With one slot, reviewers start in list order | `src_mcp/tests` | the assumption the whole change rests on |

## Definition of Done

- [ ] Two clients with different session ids submit their reviewers in different provider orders.
- [ ] The same session and round replay to the same order.
- [ ] No row is added, dropped or duplicated by the shuffle, asserted over several seeds.
- [ ] `JobStore.TryClaim` is untouched — the server's queue stays FIFO.
- [x] The guarantee is stated as spreading rather than distinctness, with the residual collision named.
- [x] The dispatch-order assumption is a test rather than a paragraph.
- [x] The merge of the two shuffles is proved to change no permutation.
- [x] Module docs describe the ordering and why it is seeded rather than random.
- [ ] Any existing test that was silently depending on provider order is named in the summary.
