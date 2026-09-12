# PLAN — the local reviewer takes the first slot, because it is the slowest

> Status: **plan only, nothing implemented yet.** Scope: one expression in
> `src_mcp/src/Server/PanelService.cs` (`BuildWork`'s ordering) and its tests.
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

- [ ] Tests 1 and 2 written first and watched fail; then green; then red again with the fix reverted.
- [ ] The whole C# suite green, count reported in the pull request.
- [ ] The diff through the `coai` code round, every finding resolved.
- [ ] `research/module_server.md` records the rule and the slot-holding trap; `CHANGELOG.md`.
- [ ] This plan promoted to `research/` with `IMPLEMENTED` and the date.
