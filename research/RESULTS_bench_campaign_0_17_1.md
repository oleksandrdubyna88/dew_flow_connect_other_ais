# RESULTS — the campaign after the store fix (server 0.17.1, 2026-09-05)

> The plan: [PLAN_bench_campaign_after_the_store_fix.md](../todo/PLAN_bench_campaign_after_the_store_fix.md) — promoted here when this report is complete.
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
| split-once | 1 | _pending_ | _pending_ | |
| split-once | 2 | _pending_ | _pending_ | |

Against the same two cases on 0.17.0 the day before (`artifacts/bench/epic1/runs.json`):

| | 0.17.0 (2026-09-04) | 0.17.1 (2026-09-05) |
|---|---|---|
| runs that produced a verdict | 4 of 4 | _pending_ of 4 |
| `NOTHING RAN` | 0 | _pending_ |
| code-round median | 277.9 s | _pending_ |
| plan-round median | 79.4 s | _pending_ |

And against 0.15.0, the release that died: 4 of 4 code rounds against 0 of 6.

### What the numbers do not say on their own

- **The code rounds are slower than yesterday's, and the cause is named.** 506 s and 475 s against
  277 s. In both runs the local reviewer failed one of its three roles with *unparseable after one
  repair attempt* — the model reasoned inside the `why` string until the token ceiling, twice per
  failure (the repair launch repeats it). That is roughly three minutes of GPU per failed role, and
  the round is as slow as its slowest reviewer. See *The two ways the local model fails* below.
- **`[clean]` is the disk's word now, and it was not before.** The first print of these runs said
  `NOT RESOLVABLE: 0 still running, 0 pending`; zero pending is what a run that resolved everything
  leaves behind, and the definition that demanded more could only be true for somebody else's
  session. Re-derived from the recorded fields: every finished round is written `done`, no file torn.

## Epic 2 — five windows, one data directory, server 0.17.1

<!-- TABLE: filled after the --parallel 5 run -->

_pending_

## Epic 3 — Fable's judgement, and the table the operator asked for

<!-- time · findings · useful findings · tokens, per arm and per run -->

_pending_

## The two ways the local model fails, and what each needed

| date | symptom | mechanism | fix | measured |
|---|---|---|---|---|
| 2026-09-04 | `The client retries again.` for 40 KB, 6.7 min of GPU | greedy decoding (`temperature: 0`) with no penalty loops on a sentence; the schema cannot stop it — a repeated sentence inside a string value is schema-valid until the last token | `frequency_penalty: 0.2`, deterministic (server 0.17.1) | no repetition loop in any of today's local answers |
| 2026-09-05 | `why` becomes *"The plan *is* the instruction… Is there a violation? Maybe… No. Wait"* for 30 KB; unterminated string at char 482 | a reasoning model reasoning inside a string field — semantically varied, so a penalty is blind to it | `maxLength` on `title`/`why`/`fix` in the schema the LOCAL route sends, so the grammar forces the string to close; the shared schema untouched because OpenAI strict mode rejects the keyword | _live probe pending: does Ollama 0.33.3 honour `maxLength` on `/v1/chat/completions` json_schema?_ |

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
- [ ] Fable's judgement over both run files.
- [ ] This report complete, and the plan promoted.
