# PLAN — the local reviewer takes the first slot, because it is the slowest

> Status: **IMPLEMENTED, 2026-09-12.** `OneLocalFirstTheRestLast` runs over the seeded shuffle's
> output in `BuildWork`: the first local vendor leads, every other local vendor goes to the tail, and
> the hosted vendors keep the shuffle's relative order.
>
> **The plan round found a hole in this plan's own mitigation.** The first draft promoted one local
> row and left the others where the shuffle put them — which leaves a second local row free to land
> inside the first `MaxConcurrency` rows, take a machine slot and then block on a card it cannot
> have: the exact trap the plan spends a section describing. Every other local row goes to the back
> now, where waiting is free.
>
> Two smaller ones from the same round: the method is named for what it does rather than for half of
> it (`LocalFirst` reads as "all the local ones first", the opposite of the rule), and what the change
> promises is stated as the order reviewers are SUBMITTED in rather than as a start time — if another
> round holds the engine lease, the promoted row waits for the card like anything else.
>
> Related docs: [module_server.md](module_server.md),
> [PLAN_one_gpu_one_reviewer.md](PLAN_one_gpu_one_reviewer.md).
>
> Issue [#155](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/155): *"I was
> watching. Local starts when its turn comes round. We respect the settings, but if there is a local
> model it should always take the first slot — start first, because it is the slowest."*

## The symptom

`BuildWork` orders a round's vendors with `SeededShuffle.Of(eligible, seed)`
(`src_mcp/src/Server/PanelService.cs:1218`), and that order is the order reviewers reach the
scheduler: `BoundedScheduler` starts one task per row against a single semaphore, and the slots FREE
when the round opens are taken in list order, deterministically, because each task runs synchronously
to its first await.

The shuffle exists for a good reason and it is not this one. It was added on 2026-09-08 so that ten
clients starting a round at nine in the morning do not all queue for the SAME vendor's shared Team
accounts while another vendor's sit idle. It is about fairness between remote accounts.

A local reviewer is the opposite kind of thing. It is the slowest reviewer in any round — minutes
where a hosted vendor takes tens of seconds — and it holds a resource nobody else can use: one GPU,
`LocalConcurrency = 1`. When the shuffle puts it last, the round's wall-clock is the hosted reviewers
finishing quickly and then everybody waiting for the local one to start and run. Started first, the
same work overlaps.

## The trap, which is why this is not "sort local to the front"

`BoundedScheduler` acquires **global → per-provider → shared resource**
(`BoundedScheduler.cs:205, 218, 238`), in that order and deliberately: taking the engine first made a
local reviewer hold an idle card while queued behind hosted vendors
(`research/PLAN_one_gpu_one_reviewer.md`). The consequence for THIS change is the mirror image:

**a local reviewer holds a machine-wide slot while it waits for the GPU.** With `MaxConcurrency = 3`,
`MaxPerProvider = 2` and `LocalConcurrency = 1`, fronting a local vendor's three roles would put two
of them in the three machine slots while only ONE can be on the card — so one slot sits blocked, and
the hosted vendors are left with one slot between them until the local queue drains. That is a slower
round than the one the issue is complaining about.

## What must be true when this is done

1. When a round has a local reviewer, **a local reviewer is first in the list** — it is submitted
   before any hosted one.
2. **Exactly one local row is promoted, and every OTHER local row goes to the END.** The first draft
   promoted one and left the rest where the shuffle put them, which the plan round caught: a second
   local row landing anywhere inside the first `MaxConcurrency` rows takes a machine slot and then
   blocks on the engine it cannot have, which is the very trap this plan opens by describing. Sending
   them to the back means they take a slot only once everything else has been served.
3. **The shuffle is otherwise untouched.** Every hosted vendor keeps its `SeededShuffle` position
   relative to the others, so the Team-account fairness that ordering exists for is unchanged and a
   replayed seed still replays. The transform is a stable remove-and-insert, so the rows that shift
   do so only by the moves above — asserted, not assumed.
4. A round with no local reviewer is byte-for-byte what it is today.
5. `SubmissionOrderTests` keeps testing what it believes it tests. Its fixture builds **every** vendor
   with `Runtime = "local"` (`SubmissionOrderTests.cs:34`), so under a local-first rule they would all
   be in one partition and the tests would stay green while covering nothing. It gains a mixed case.

## The change

In `BuildWork`, after the shuffle:

```csharp
var runnable = OneLocalFirstTheRestLast(SeededShuffle.Of(eligible, seed));
```

A pure static, named for what it does rather than for half of it (the plan round called `LocalFirst`
ambiguous, and it was — it reads as "all the local ones first", which is the opposite of the rule):

- the FIRST local vendor in the shuffled order moves to the head;
- every OTHER local vendor moves to the tail, keeping their order relative to each other;
- every hosted vendor keeps its position relative to the other hosted ones.

"Local" is `RuntimeResolution.NameOf(vendor) == "local"` — a CALL to the one authority rather than a
second copy of it; re-deriving the runtime from the two settings fields is what its docstring
forbids, and what three drifting copies once did.

With no local vendor the list comes back unchanged, and with exactly one local vendor already first
it is unchanged too.

That is the whole change. No new setting: the issue asks for a rule, not a preference, and a switch
for "should the slowest reviewer start first" is a question nobody has a reason to answer `no` to.

## What this plan promises, exactly

**The order reviewers are SUBMITTED in** — which is what `BuildWork` decides and all it decides. It
is not a promise about wall-clock start times, and the plan round was right to press on that: if
another round already holds the single engine lease, the promoted local row takes a machine slot and
then waits for the card like anything else. What changes is that it is asked FIRST rather than after
the hosted vendors, which is the complaint in the issue.

## What this plan does NOT do

The issue has a second half: *"if there are several rounds it should carry straight on — its queue
must not be interrupted by other models."* That is about the scheduler's limiters, which outlive a
round (`BoundedScheduler.cs:123-135`), and about the cross-process engine lease — not about the order
a round is built in. It is a different mechanism and is left out of this plan deliberately rather
than silently; if a round still stalls behind another round's hosted work after this ships, that is
the change to make, and it needs a measurement first.

## Test plan (RED first)

| # | Test (`src_mcp/tests`) | RED symptom expected |
|---|---|---|
| 1 | *a round with a local reviewer asks it first* — a mixed round puts a local row at index 0 for SEVERAL different seeds, so it is not the shuffle agreeing by chance | the local row's position follows the seed |
| 2 | *one local row leads and the rest go last* — with two local vendors and two hosted, index 0 is local, the LAST row is the other local, and the hosted rows sit between them in their shuffled order. Asserted as a stable remove-and-insert: the plan round pointed out that "the others keep their positions" is impossible once a row moves, and it was right | the second local row sits inside the first `MaxConcurrency` rows |
| 3 | *a round with no local reviewer is unchanged* — the list equals `SeededShuffle.Of(...)` exactly, for several seeds, so the Team-account fairness is provably untouched | (guard; green before and after) |
| 3b | *the hosted order survives the transform* — in a mixed round the hosted vendors appear in the same RELATIVE order as in the raw shuffle, for several seeds | (guard; the fairness property, asserted after the transform rather than before) |
| 4 | `SubmissionOrderTests` gains a MIXED-vendor case, since its existing fixture makes every vendor local and would stop covering the property it is named for | its cases are all-local and say nothing about the rule |

Run: `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4` then
`./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*SubmissionOrder*|*BuildWork*"`
for the loop and the whole executable before the commit — never `dotnet test`, which has no VSTest
host here.

## Definition of Done

- [x] Tests 1 and 2 written first and watched fail — `Expected order[0] to start with "local" ... but
      "bravo" differs near "bra"` and `Expected ...[0] to be "local" ... but "charlie" has a length of
      7` — then green; then red again (2 of 4) with the one expression reverted, and green restored.
- [x] The whole C# suite green in **both configurations**, since this change touches ordering:
      Debug **1304 tests, 1303 pass, 1 skipped, 0 fail**; Release the same. Running only the
      configuration one happens to build is the overstatement `testing.md` names.
- [x] The diff through the `coai` code round — `proceed`, 12 gating against a threshold of 5, all
      12 reviewers answered; 5 findings accepted, 13 rejected with reasons. It found a real
      defect: the transform reordered PROVIDERS, and the role expansion is vendor-major, so a
      local vendor's four roles led as four rows and filled every machine slot. Moved to the
      flattened rows, and the test now reads rows rather than distinct providers — which is
      what had hidden it.
- [x] `research/module_server.md` records the rule and the slot-holding trap; `CHANGELOG.md` under
      `## Unreleased`.
- [x] This plan promoted to `research/` with `IMPLEMENTED` and the date.

**Deviation from this list:** test 4 — the mixed case for `SubmissionOrderTests` — was NOT added
there. That file's own fixture makes every vendor local, and the property it is named for (two
clients do not ask the same vendor first) is about the shuffle, which this change leaves alone. The
mixed coverage it asked for lives in `TheLocalReviewerIsAskedFirstTests` instead, where every case IS
mixed — putting it in both would be one property asserted twice. The concern the plan round raised —
that an all-local fixture would silently stop covering anything — does not arise: with every vendor
local, `OneLocalFirstTheRestLast` promotes one and demotes the rest, so those tests still exercise a
real reordering rather than an identity.
