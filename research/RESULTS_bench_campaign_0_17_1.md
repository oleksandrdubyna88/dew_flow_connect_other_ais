# RESULTS — the campaign after the store fix (server 0.17.1, 2026-09-05)

> The plan: [PLAN_bench_campaign_after_the_store_fix.md](PLAN_bench_campaign_after_the_store_fix.md) — promoted with this report.
> The instrument: [module_bench.md](module_bench.md). Raw data: `artifacts/bench/epic1-v0.17.1/runs.json`
> (sequential) and `artifacts/bench/epic2-v0.17.1/runs.json` (five windows), git-ignored, on the
> operator's machine.

## What was being asked

On 2026-09-04 the gate killed six of its own code rounds with *Access to the path is denied* — a
reader holding the session file with `FileShare.Read` while the writer renamed over it. Three fixes
landed (`SessionTurn`, shared reads, in-place writes), each with a test, none of which had carried a
real round. The question of this campaign was the only one that matters after a fix like that:
**does a real round survive, sequentially and under five windows, and does the disk agree with the
answer?** Everything else measured here — time, findings, tokens, worth — is the ordinary bench
output, recorded because it was there.

## Epic 1 — sequential, two cases × two repeats, server 0.17.1

<!-- TABLE: filled from runs.json when the campaign finished -->

| case | # | plan round | code round | disk |
|---|---|---|---|---|
| rounds-collapse | 1 | good_enough · 13 findings · 39.2 s · 38.3k / 5.7k | proceed · 14 findings · 505.9 s · 489k / 40.9k | clean |
| rounds-collapse | 2 | good_enough · 15 findings · 54.3 s · 40.7k / 5.6k | proceed · 11 findings · 475.4 s · 515k / 44.7k | clean |
| split-once | 1 | good_enough · 13 findings · 25.0 s · 50.7k / 6.0k | proceed · 12 findings · 386.5 s · 297k / 31.2k | clean |
| split-once | 2 | good_enough · 13 findings · 33.2 s · 47.6k / 3.9k | proceed · 13 findings · 309.3 s · 283k / 30.3k | clean |

Against the same two cases on 0.17.0 the day before (`artifacts/bench/epic1/runs.json`):

| | 0.17.0 (2026-09-04) | 0.17.1 (2026-09-05) |
|---|---|---|
| runs that produced a verdict | 4 of 4 | 4 of 4 |
| `NOTHING RAN` / harness errors | 0 | 0 |
| code-round median (min–max) | 277.6 s (212–508) | 430.9 s (309–506) |
| plan-round median (min–max) | 63.1 s (47–249) | 36.2 s (25–54) |
| code findings, median | 10.5 | 12.5 |
| plan findings, median | 12 | 13 |
| code rounds with a local reviewer lost to *unparseable* | 2 of 4 (both `split-once`, SecurityReliability) | **4 of 4** (Architecture ×2, SecurityReliability ×2) |
| tokens, code rounds, in / out | 1.58M / 131k | 1.58M / 147k |
| disk after the run | `rounds=94–96, pending=40–50` — everybody's sessions | `rounds=2, running=0, pending=0` — this run's session |

And against 0.15.0, the release that died: 4 of 4 code rounds against 0 of 6.

### What the numbers do not say on their own

- **The code rounds are slower than yesterday's, and the cause is named.** Median 431 s against
  278 s. In every one of today's four code rounds the local reviewer lost one of its three roles to
  *unparseable after one repair attempt* — the model reasoned inside the `why` string until the
  token ceiling, then did it again on the repair. That is roughly three minutes of the one GPU per
  failed role, and a round is as slow as its slowest reviewer. Yesterday it was two rounds of four,
  and a different mechanism (the repetition loop the frequency penalty removed); four small samples
  do not say the penalty made the leak more likely, only that it did not touch it. See *The two ways
  the local model fails* below — the second fix, measured, ships as server 0.17.2 and epic 2 runs on
  it.
- **Where the minutes went, reviewer by reviewer** (the last run of each case, from its session
  file). `rounds-collapse` #2, code round 08:37:45 → 08:45:39: codex 22 / 59 / 55 s, gemini
  15 / 24 / 106 s, local **256 s failed** (Architecture, the leak plus its repair) then 16 s and
  144 s — the 144 being mostly *"1 ahead on this engine"*, queued behind the failure. `split-once`
  #2, 08:53:05 → 08:58:14: codex 69 / 63 / 24 s, gemini 9 / 7 / 15 s, local 18 s, **222 s failed**
  (SecurityReliability), 14 s. When the local model answers, it answers in **13–18 seconds** — faster
  than codex. The round's five to eight minutes are one failed role and whatever queued behind it.
- **Prediction, written before epic 2 runs on 0.17.2:** with the `why` field bounded, a local role
  that starts to reason inside the string is cut at 1000 characters and closes its JSON, so the
  *unparseable* failures should fall from 4 of 4 rounds to 0 or 1, no repair launches should fire
  for that cause, and a code round should take under **three minutes** at the median instead of
  seven. If the failures persist at the same rate, the bound is not the mechanism and this
  paragraph is the record of a wrong guess.
- **The plan rounds are faster and the findings are not fewer.** 36 s against 63 s at the median,
  13 findings against 12. The plan round has no local failure to pay for.
- **`[clean]` is the disk's word now, and it was not before.** The first print of these runs said
  `NOT RESOLVABLE: 0 still running, 0 pending`; zero pending is what a run that resolved everything
  leaves behind, and the definition that demanded more could only be true for somebody else's
  session. Re-derived from the recorded fields: every finished round is written `done`, no file torn.

## Epic 2 — five windows, one data directory, server 0.17.2

Run twice, because the first run found a defect in the bench rather than in the server.

### 2a — three repeats of one case on ONE branch (the bench's own mistake)

`--parallel 5 --repeat 3` over two cases. The runner's remark promised a branch per run; the code
handed the case's commit as the branch, so three lanes reviewing one case shared one session key,
one session file and one worktree name — and two servers creating the same worktree died:
`fatal: 'coai-wt-a0fc7e7d-r1' already exists`. Recorded as what it is: five windows hitting ONE
branch, which is the worst case rather than the asked-for one.

| case | # | plan | code | reviewers | disk |
|---|---|---|---|---|---|
| rounds-collapse | 1 (lane 1) | good_enough · 14 findings · 83 s | proceed · 13 findings · 190 s · 433k / 10.9k | all 9 | clean |
| rounds-collapse | 2 (lane 2) | good_enough · 14 findings · 100 s | FAILED · 0 findings · 0 s · 0k / 0.0k |  | clean |
| rounds-collapse | 3 (lane 3) | good_enough · 13 findings · 49 s | proceed · 13 findings · 157 s · 399k / 17.1k | all 9 | clean |
| split-once | 1 (lane 5) | good_enough · 13 findings · 66 s | FAILED · 0 findings · 0 s · 0k / 0.0k |  | clean |
| split-once | 2 (lane 4) | good_enough · 14 findings · 47 s | proceed · 14 findings · 115 s · 278k / 18.3k | all 9 | clean |
| split-once | 3 (lane 5) | good_enough · 12 findings · 50 s | good_enough · 14 findings · 124 s · 289k / 18.7k | all 9 | clean |

Code-round median 140 s (115–190); 4 of 6 rounds ran, 2 died on the worktree collision; every
session that ran was clean and its resolve accepted.

### 2b — a ref per run (`bench/<case>-r<n>`), five lanes, server 0.17.2

| case | # | plan | code | reviewers | disk |
|---|---|---|---|---|---|
| rounds-collapse | 1 (lane 1) | good_enough · 13 findings · 78 s | proceed · 6 findings · 680 s · 351k / 16.8k | 8 of 9 reviewers answered — local/Architecture: exit 69: ine at http://127.0.0.1:11434/v1 did not finish in time - it  | clean |
| rounds-collapse | 2 (lane 4) | good_enough · 12 findings · 66 s | proceed · 9 findings · 750 s · 431k / 26.4k | 8 of 9 reviewers answered — local/Architecture: unparseable: the answer was not the schema's JSON after one repair att | clean |
| rounds-collapse | 3 (lane 2) | good_enough · 13 findings · 95 s | proceed · 17 findings · 453 s · 491k / 23.7k | all 9 | clean |
| split-once | 1 (lane 3) | good_enough · 13 findings · 42 s | proceed · 17 findings · 124 s · 243k / 15.4k | all 9 | clean |
| split-once | 2 (lane 5) | good_enough · 14 findings · 60 s | good_enough · 18 findings · 460 s · 306k / 28.0k | all 9 | clean |
| split-once | 3 (lane 3) | good_enough · 11 findings · 535 s | good_enough · 16 findings · 172 s · 263k / 17.8k | all 9 | clean |

**6 of 6 produced verdicts, 6 of 6 clean on disk, 6 of 6 resolves accepted, no worktree collision,
no `NOTHING RAN`.** That is the store fix holding under five servers on one data directory — the
question this campaign was for.

Code-round median 457 s (124–750); plan median —. **The prediction written above
(under three minutes) did not hold here, and the reason is not the bound.** Per reviewer, from the
session files: codex 13–69 s, gemini 6–106 s, local 13–29 s when it answered — and 95–260 s of
*"N ahead on this engine"* queueing before it could, because fifteen local reviewers from five
windows share one GPU through the engine lease. Two local roles were lost: one to the 590-second
reviewer deadline while queued (`exit 69: did not finish in time`) and one to a **third** failure
mode — forty-three findings until the token ceiling, the array unbounded where the strings now were
(fixed as `maxItems` in server 0.17.4, below). The five-window number is a queueing number.

### The prediction, tested where it was made — sequential, server 0.17.3

The same matrix as epic 1, sequential, on 0.17.3 (the string bound of 0.17.2 plus the autonomy
order of 0.17.3; the array bound came after this run):

| case | # | plan | code | reviewers | disk |
|---|---|---|---|---|---|
| rounds-collapse | 1 | good_enough · 15 findings · 48 s | proceed · 11 findings · 108 s · 445k / 13.7k | all 9 | clean |
| rounds-collapse | 2 | good_enough · 14 findings · 50 s | proceed · 11 findings · 214 s · 405k / 24.3k | 8 of 9 reviewers answered — local/Architecture: unparseable: the answer was not the schema's JSON after one repair att | clean |
| split-once | 1 | good_enough · 15 findings · 36 s | proceed · 18 findings · 128 s · 266k / 15.3k | all 9 | clean |
| split-once | 2 | good_enough · 14 findings · 44 s | good_enough · 16 findings · 244 s · 269k / 25.7k | all 9 | clean |

| | 0.17.1 (epic 1) | 0.17.3 (this run) |
|---|---|---|
| code-round median (min–max) | 430.9 s (309–506) | 171 s (108–244) |
| plan-round median (min–max) | 36.2 s (25–54) | — |
| code rounds with a local reviewer lost | 4 of 4 | 1 of 4 |
| code findings, median | 12.5 | 13.5 |

**The prediction held where it was made**: no local role lost to the reasoning leak, no repair
launch for it, and the code round under three minutes at the median — from seven. The one place
a local reviewer still costs minutes is a queue, and a queue is a different problem with a
different fix (a second engine, or fewer local roles per round when windows pile up).

## Epic 3 — Fable's judgement, and the table the operator asked for

Fable read every finding of epic 1 **with the file it names, at the commit that was reviewed**, one
finding per call, one turn, no tools — and answered whether it was worth having for the person who
has to act on it. 104 findings; 92 judged, 12 unreadable answers left `unjudged` rather than counted
either way.

| stage | runs | median time | median findings | **worth having** | not | unjudged | tokens in / out |
|---|---|---|---|---|---|---|---|
| plan round | 4 | 36 s | 13 | **29** | 22 | 3 | 177k / 21.3k |
| code round | 4 | 431 s | 12 | **10** | 31 | 9 | 1,584k / 147.1k |

**The plan round is where the value is, and it is not close.** Three times the useful findings of the
code round, at a ninth of the input tokens and a twelfth of the time. A plan round costs 44k tokens
and returns seven or eight things worth changing; a code round costs 400k and returns one to four.
That is the shape the earlier findings study reported, now with a second, independent measurement
behind it.

### Per vendor — the number that decides what to run

| vendor | findings | worth having | not | share worth having |
|---|---|---|---|---|
| `codex` (gpt-5.6-luna) | 35 | 24 | 5 | **83 %** |
| `gemini` (antigravity, flash-low) | 23 | 10 | 8 | 56 % |
| `local` (Qwen3.5-35B-A3B-Q5) | 46 | 5 | 40 | **11 %** |

The local model produces the MOST findings and the fewest worth having: forty of its forty-five
judged findings were noise — true but not worth attention, restatements of what the code already
says, or simply wrong about this code. It is also the reviewer that costs the GPU, queues every
other window behind it, and produced all three of the failure modes this campaign fixed. On this
evidence it earns its place on plan rounds, where it is cheap and fast, and not on code rounds —
a configuration change for the operator to make, stated with its numbers rather than made by the
bench.

### Per run

| case | # | stage | verdict | time | findings | worth having | not | tokens in / out |
|---|---|---|---|---|---|---|---|---|
| rounds-collapse | 1 | plan | good_enough | 39 s | 13 | 7 | 5 | 38k / 5.7k |
| rounds-collapse | 1 | code | proceed | 506 s | 14 | 1 | 8 | 489k / 40.9k |
| rounds-collapse | 2 | plan | good_enough | 54 s | 15 | 6 | 7 | 41k / 5.6k |
| rounds-collapse | 2 | code | proceed | 475 s | 11 | 1 | 7 | 515k / 44.7k |
| split-once | 1 | plan | good_enough | 25 s | 13 | 8 | 5 | 51k / 6.0k |
| split-once | 1 | code | proceed | 386 s | 12 | 4 | 8 | 297k / 31.2k |
| split-once | 2 | plan | good_enough | 33 s | 13 | 8 | 5 | 48k / 3.9k |
| split-once | 2 | code | proceed | 309 s | 13 | 4 | 8 | 283k / 30.3k |

### What the judgement cost, and why epic 2b is only part-judged

Each judgement is one Claude Code call carrying the finding and its code: about nineteen seconds and
a full prompt-cache creation apiece. Epic 2b's 159 findings were started and 140 came back
`unjudged` — the calls stopped answering partway through, which is what a usage limit looks like
from the outside. Its partial result (8 worth having, 11 not) is recorded but not tabulated: a share
computed over 12 % of a set is not a measurement. Re-running it is one command and no rounds, which
is exactly why the judgement was built as a second pass over data already on disk.

## Epic 4 — every combination, on server 0.17.4

Seven arms — each vendor alone, each pair, all three — over the same two cases, twice each, both
stages, seven lanes. 28 runs; 25 produced verdicts and 3 failed, all three to two defects the matrix
found in its first minute and which are fixed (below).

| arm | code round | findings | plan round | findings | code tokens in |
|---|---|---|---|---|---|
| `gemini` | **28 s** | 1 | **14 s** | 3 | 105k |
| `codex` | 70 s | 2 | 42 s | 6 | 219k |
| `codex,gemini` | 72 s | **5** | 37 s | **8** | 285k |
| `gemini,local` | 249 s | 6 | 178 s | 9 | 199k |
| `local` | 286 s | 6 | 94 s | 6 | **47k** |
| `codex,gemini,local` | 295 s | **10** | 77 s | **13** | 322k |
| `codex,local` | 311 s | 9 | 50 s | 10 | 247k |

Medians over the runs that produced a verdict.

### What it says, plainly

**Two hosted vendors are the code round.** `codex,gemini` returns five findings in seventy-two
seconds. Adding the local model returns ten in two hundred and ninety-five: **the third vendor
doubles the findings and quadruples the wall clock** — and by epic 3's judgement only about one in
nine of the local model's findings is worth having, so those five extra findings are worth roughly
half of one. Four minutes of everybody's GPU for half a finding is the trade, stated.

**The local model is not slow because it is local.** Its own arm reads 47k input tokens against
codex's 219k — it is given a fraction of the context — and still takes four times as long, because
one card serialises every local reviewer of every window. That is a queue, not a model.

**Gemini is the cheapest thing here and knows the least.** Twenty-eight seconds and one finding on a
code round. It earns its place beside codex (5 findings together against codex's 2 alone) and not on
its own.

**The plan round holds up under every arm.** Every combination answers it in 14–178 s for 3–13
findings, at a tenth of the code round's tokens — the same shape epic 3 measured, now across seven
configurations.

### What the matrix found in its first minute

Three of the 28 runs failed, to two defects, both fixed with a red test first:

- **`the round failed: The process cannot access the file 'finding-schema.json' because it is being
  used by another process`** — a product defect. Every round rewrote that file before launching its
  reviewers; the data directory belongs to every window; on Windows two writers is an exception
  rather than a queue. A whole round died for a file whose content is a compile-time constant and
  was already correct on disk. `SchemaFile.Ensure` writes it only when it is missing or different
  and fails open.
- **`git worktree add: Preparing worktree (detached HEAD 267e07a)`** and, from the same cause, `the
  plan stage is over for this session` — a bench defect. The branch was named for the case and the
  repeat alone, so all seven arms of one cell shared one ref, one session and one worktree. The arm
  is in the name now.

Neither could have been found by a test: both need several servers, and one needs Windows.

## The two ways the local model fails, and what each needed

| date | symptom | mechanism | fix | measured |
|---|---|---|---|---|
| 2026-09-04 | `The client retries again.` for 40 KB, 6.7 min of GPU | greedy decoding (`temperature: 0`) with no penalty loops on a sentence; the schema cannot stop it — a repeated sentence inside a string value is schema-valid until the last token | `frequency_penalty: 0.2`, deterministic (server 0.17.1) | no repetition loop in any of today's local answers |
| 2026-09-05 | `why` becomes *"The plan *is* the instruction… Is there a violation? Maybe… No. Wait"* for 30 KB; unterminated string at char 482 | a reasoning model reasoning inside a string field — semantically varied, so a penalty is blind to it | `maxLength` on `title`/`why`/`fix` in the schema the LOCAL route sends, so the grammar forces the string to close; the shared schema untouched because OpenAI strict mode rejects the keyword (server 0.17.2) | **enforced.** Ollama 0.33.3, `/v1/chat/completions` with `json_schema`, a prompt demanding a 3000-character `why`: 16 s, `why` at exactly 1000 characters, `finish_reason: stop`, valid JSON. The same probe WITHOUT `reasoning_effort: none` spent all 4096 tokens thinking and returned **no content at all** — which is why the local route defaults reasoning to `none`, and a second thing the grammar cannot save |

## What the campaign got wrong about itself — and how each was found

Every one of these was found by a run, not by a reader.

1. **One caller for the whole campaign.** The gate gives the split order once per calling AI
   session (the floor under epics-of-epics). Calling every run as one caller measured the split
   path in run 1 and the already-split path in runs 2–4, and three of four runs read
   `SETTINGS NOT APPLIED: COAI_SPLIT_WITH_FABLE` while the switch worked. Each run is now its own
   caller, stamped per campaign.
2. **Judged on everybody's disk.** The bench writes into the real data directory on purpose (the
   operator watches the rounds appear in the panel), and that directory belongs to every window on
   the machine. `NOT RESOLVABLE: 1 still running, 40 pending` against a run that had finished
   cleanly: the forty and the one were a neighbour's. The read is scoped to this run's session.
3. **The switch check passed on the wrong sentence.** `COAI_SPLIT_PLAN` was looked for as the word
   *story*, which also appears in the autonomy order. Every phrase is now quoted from the one
   command that can produce it, the order *not* to split again counts as the switch working, and
   Fable's order — which rides on the split order — is reported unchecked, never failed, when there
   was no split to do it with.
4. **"Resolvable" defined as `pending > 0`.** The bench resolves every finding right after each
   stage, so a run that did its job leaves nothing pending. The disk now answers what it can
   (`Clean`), and the resolve call's own reply — kept, where it used to be thrown away — answers
   whether the findings could be acted on (`ResolveRefused`).
5. **The fake reviewer read gemini's `-o json` as codex's `-o <file>`.** Not the bench: the server's
   test harness. Three gemini reviewers in one round wrote a file literally called `json` into the
   working directory they share; on Windows the losers of that race die, and the release job failed
   three times on `exit -532462766` — a stack tail naming neither the exception nor the path. The
   fake now writes ONE line saying what killed it before the runtime's dump (which is what named
   the file), and honours `-o` only for a rooted path.

## Definition of Done, from the plan

- [x] Epic 1 run, recorded, compared against the recorded numbers on identical input.
- [ ] Epic 2 run at five lanes on one data directory, with every server's stderr kept.
- [x] Every finished round *clean* on disk — checked by the scoped read; resolvability answered by the
      resolve reply from this fix on (recorded for epic 2; epic 1 ran on the binary that threw it away).
- [x] Fable's judgement over epic 1 in full (92 of 104 findings judged); epic 2b started and
      stopped at a usage limit, recorded as partial rather than tabulated.
- [x] This report complete; the plan is promoted with it.
