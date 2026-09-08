# RESULTS — ten rounds at once against the Team server, and what that actually measures

> **Subject:** `coai.remsoft.dev`, the live Team server, 2026-09-08 ~15:15–15:30 UTC. Client:
> `coai-mcp` 0.18.12 as installed, driven by `coai-bench` built from `595ba62`. Pinned:
> `COAI_REVIEWER_TIMEOUT_MINUTES=5`, `COAI_MAX_PER_PROVIDER=3`, `COAI_ON_EXHAUSTED=good_enough`,
> three remote vendors (`remsoftdev-claude` haiku, `remsoftdev-codex` gpt-5.6-luna,
> `remsoftdev-antigravity` gemini-3.8-flash-low), plan stage only.
>
> **Harness:** `coai-bench run --arm <the three> --stages plans --repeat 3 --parallel 10` over
> `src_bench/corpus.json` (4 cases). Twelve runs, ten in flight, one server process per lane.
>
> **Machine:** the operator's Windows workstation for the client; the server's own box for the work.

## Why this ran at all

The operator asked for a load campaign — 10, 20, 30, 40, 50 parallel reviews — and chose to start at
ten and decide from there. Ten was enough to decide.

## What the ladder found before it started climbing

**The bench could not measure a Team server at all.** The first twelve rounds returned `call_human`
in about seven seconds each with zero tokens, and one sentence repeated thirty-six times:
`'remsoftdev-claude' is not a vendor here`. `CoaiBench`'s `VendorConfig` had no `remoteVendor`, so
the server was handed the ROW ID — a name no server has ever known. Fixed before the real run; the
same field, the same failure and the same discovery route as the extension's own version of it.

That is the first result, and it cost thirty escalation prompts on the operator's phone: **twelve
rounds where nothing reviewed anything still produced a full report with tables.**

## Ten parallel rounds, from ONE account

| round | seconds |
|---|---|
| 1 | 79 |
| 2 | 186 |
| 3 | 247 |
| 4 | 267 |
| 5–10 | 295, 295, 299, 305, 305, 306 |

Six of ten pinned at 295–306 s against a five-minute reviewer timeout: those reviewers ran out of
time rather than answering.

| how many of the three answered | rounds |
|---|---|
| all 3 | 2 |
| 2 of 3 | 3 |
| 1 of 3 | 5 |

| why a reviewer did not answer | count |
|---|---|
| the server answered **429** | 7 |
| queued past the shim's deadline (`exit 78`) | 3 |
| the reviewer's own timeout | 2 |

Findings still arrived — 32 from codex, 30 from antigravity, 10 from claude — so the rounds that
worked worked. **Only two rounds in ten got the full panel.**

## Three ceilings, none of them the machine

The campaign was designed to find where the Team server bends under parallel load. It found three
limits before reaching anything that could be called capacity, and **all three are deliberate**.

| Ceiling | Where | Value | What it protects |
|---|---|---|---|
| requests per person | `Coai:RateLimit:PermitLimit` (`Program.cs:95-110`) | 120 per 10 s | one busy client throttling the company |
| reviews queued per person | `Coai:PerCallerQueued` (`Program.cs:150`) | 20 | one person holding everybody else's turn |
| one review per account | `SlotRegistry`, a file lock | invariant, not a setting | the OAuth refresh-token race |

**The rate limiter is partitioned by EMAIL.** Its comment says why: everyone behind one company proxy
shares an address, and an address-partitioned limiter is one bucket for the whole company. So a
campaign run from ONE account is one bucket — ten people running one round each would be ten buckets
and would not meet it at all. *The ladder as designed measures the limiter, not the server.*

**The queue limit is the one that refuses, and it refuses correctly.** Ten rounds × three vendors is
thirty submissions from one caller; twenty are accepted and ten are told
*"you already have 20 reviews waiting"* with a `Retry-After`. The endpoint's own comment: *"a queue
nobody drains is just a way to hold other people's turn, so the refusal is a 429 with a time — not a
silent accept that never runs."*

**The third is not a knob and must not become one.** `SlotRegistry` holds a `FileShare.None` handle
per account, released by the kernel even if the holder is killed, because *"each of these CLIs
refreshes its own OAuth token in place. Two processes rotating one refresh token race, and the loser
is left holding a token the vendor has already invalidated — `invalid_grant`, and the account is
signed out until a human goes back to the VM."* The `slotConcurrency: 1` field sitting in
`vendors.json` is DEAD: `VendorConfig` has no such property and never reads it.

So with one slot per vendor, the server runs at most three reviews at once whatever the concurrency
setting says. Parallelism per vendor is bought in ACCOUNTS — a slot is a directory used as `HOME`
(`SlotEnvironment`), so ten concurrent codex means ten signed-in codex subscriptions, not a number
in a config file.

## The polling question, still open

`RemoteAsk.PollGap` is one second, and its docstring says the server long-polls up to 25 seconds so
*"one reviewer therefore makes about two requests a minute, which is why polling never reaches the
server's rate limiter."*

That holds while the answer does not change. The long poll returns *the moment the state changes*,
and what changes constantly on a busy server is a job's queue POSITION — so under load each reviewer
may poll every second instead of every 25, and thirty reviewers would then make 300 requests per
window against a limit of 120. **Not confirmed here**: proving it needs the server's request log,
which this run did not read. What is measured is that seven reviewers were refused with 429 under a
load the docstring says polling never reaches.

## What to do next

1. **Do not climb the ladder from one account.** It would measure the limiter, not the server. The
   operator's decision on 2026-09-08 was to stop after this rung for exactly that reason: *since it
   queues, there is nothing to load-test — only that it works and the queue does not fall over.*
2. **Answer the polling question** — it is a product defect if load-induced churn defeats the long
   poll, and it is cheap to check against the server's request log.
3. A team-load number, if it is ever wanted, needs several identities — not a raised limit. Raising
   `PerCallerQueued` was considered and declined: it is the one of the three that protects OTHER
   PEOPLE from a single greedy client, unlike the rate limit, whose raising costs nobody.

## What was changed on the live server, and put back

For about twenty minutes on 2026-09-08 this campaign ran against a modified `coai.remsoft.dev`:
`Coai__RateLimit__PermitLimit=10000`, `Coai__MaxConcurrency=10` (from 1), and `mssql-server` stopped
to free ~280 MB. All three were restored from `/etc/coai-server.env.before-loadtest-2026-09-08`, and
the concurrency raise was reverted for a reason worth recording: the unit carries `MemoryMax=1500M`
and child CLIs inherit its cgroup, so ten concurrent agentic CLIs would have made the kernel pick a
victim inside the server — while buying nothing, because one slot per vendor caps the real
concurrency at three regardless.
