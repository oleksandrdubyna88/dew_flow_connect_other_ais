# PLAN — every client submits its reviewers in a different order

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/PanelService.cs`
> (`BuildWork`), one new helper, and tests on both the ordering and the invariant it must not break.
>
> Related docs: [module_server.md](../research/module_server.md),
> [module_runners.md](../research/module_runners.md),
> [PLAN_team_server_reviewer_never_called.md](../research/PLAN_team_server_reviewer_never_called.md).

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

The order in which one client offers its reviewers must differ from the order another client offers
theirs, without giving up either of two properties the round already has:

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

## Definition of Done

- [ ] Two clients with different session ids submit their reviewers in different provider orders.
- [ ] The same session and round replay to the same order.
- [ ] No row is added, dropped or duplicated by the shuffle, asserted over several seeds.
- [ ] `JobStore.TryClaim` is untouched — the server's queue stays FIFO.
- [ ] Module docs describe the ordering and why it is seeded rather than random.
- [ ] Any existing test that was silently depending on provider order is named in the summary.
