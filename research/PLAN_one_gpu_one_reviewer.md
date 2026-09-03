# PLAN — one GPU serves one reviewer, and a 404 is not a rate limit

> Status: **IMPLEMENTED, 2026-09-03**, in the server. Six of the plan's statements shipped; the
> panel's Limits line and the field re-run are the tail below.
>
> **Deviations, largest first.** The plan proposed keying the cap on the provider name and the gate
> refuted it twice over: two vendor ids can name one card (so the key is the canonicalised ENDPOINT),
> and the limiters were being built inside `RunAllAsync` — which made every cap per ROUND, so two
> rounds in one server allowed twice what the setting said. The implementation's own first lock
> ORDER was then wrong in the opposite direction: taking the engine before the machine slot let a
> local reviewer hold an idle card while queued behind hosted vendors. It is widest-first now.
>
> The plan also proposed a new `ReviewerOutcome.VendorError`. It was not built: `NonZeroExit` already
> carries the vendor's own line and is already not retried, so the 404 needed the classifier fixed
> and nothing else — a new outcome type would have been a second name for a state that exists.
>
> **What the tests found that no reviewer did:** a reviewer cancelled while RUNNING threw out of the
> fan-out exactly as a queued one did, so `Task.WhenAll` faulted and a round cancelled with five
> reviewers finished reported none of them. The accepted finding was about the queue; the defect was
> about both.
>
> **The open tail:** the panel's Limits section still does not say that `maxPerProvider` has no
> authority over a local engine — `src_vs_code` was being edited by another session in this shared
> checkout when this landed, and one line of UI is not worth committing over somebody's uncommitted
> work. The field re-run (three local roles in one round, answering one after another on the machine
> that produced the symptom) needs the released binary and is owed with it.
>
> Related docs: [module_server.md](module_server.md), [module_runners.md](module_runners.md),
> [PLAN_local_models.md](PLAN_local_models.md).

## The symptom, measured 2026-09-03

Two rounds in a row reported **fewer reviewers than they asked for**, and the sentences they reported
it with sent a reader to the wrong place.

### The local engine, three at once (code round, 16:03:24 → 16:14:26)

From the server's own log:

| reviewer | started | outcome |
|---|---|---|
| `local/Architecture` | 16:04:26 | answered in **30.6 s** — 3 findings, 10 388 in / 710 out |
| `local/SecurityReliability` | 16:04:33 | **FAILED after 590.0 s** — "the local engine at `http://127.0.0.1:11434/v1` did not answer within the round's deadline" |
| `local/UxDxPerformance` | 16:04:35 | **FAILED after 590.1 s** — the same sentence |

Three requests were in flight against **one** engine between 16:04:35 and 16:04:56. The machine's
live settings say why that was allowed: `COAI_MAX_PER_PROVIDER=3`
(`%LOCALAPPDATA%\coai-mcp\settings.json`). The engine is Ollama 0.33.2 serving
`Qwen3.5-35B-A3B-Q5_vk128` — 26.4 GB resident in VRAM, `OLLAMA_NUM_PARALLEL=4` — on one card.

**The number that governs both is the defect.** Three concurrent requests is right for a hosted
vendor: three HTTP calls to somebody else's fleet, bounded by a rate limit that the cap exists to
respect. It is wrong for a local engine, where "the provider" is one GPU: the three reviewers do not
queue, they *share* the card, each gets a third of the throughput, and two of them spend the round's
entire ten-minute deadline without finishing. The same reviewer, alone on the same card, answers in
half a minute — measured twice (30.6 s in this round, 29.9 s in the plan round before it).

**And the sentence it fails with names the wrong thing.** "The local engine did not answer within the
round's deadline" describes an engine that is down or unreachable — the diagnosis the WSL work built
for exactly that case ([PLAN_wsl_local_engine.md](PLAN_wsl_local_engine.md)). Here the
engine was up, loaded, and answering; it was answering to two other reviewers of the same round. A
person reading that line goes to check `127.0.0.1:11434`, finds it healthy, and learns nothing.

### codex, told it was rate limited when it was handed a 404 (plan round, 15:09:10)

```
reviewer codex/PlanCritique FAILED after 45.2s: rate limited (after one retry):
{"type":"error","message":"Reconnecting... 2/5 (unexpected status 404 Not Found: Unknown error,
 url: https://chatgpt.com/backend-api/codex/responses, cf-ray: a3…
```

`RateLimit.Phrases` (`ReviewerExecutor.cs:70`) is
`["429", "rate limit", "usage limit", "quota", "503", "unavailable", "high demand"]`, matched as
**case-insensitive substrings anywhere in stdout or stderr**. The visible part of that line contains
none of them, so the match is in the tail the log cut — and the tail is a Cloudflare ray id, which is
hexadecimal. `429` and `503` as bare substrings match any id, byte count, duration or token total
that happens to contain those three digits.

Whatever matched, the classification is wrong twice over: a **404** is not a rate limit, and
`Reconnecting... 2/5` is the CLI's own retry loop reporting a transient failure. The person is told to
wait for a quota that was never the problem, and the reviewer is retried once against a route that
answered 404 — which is the one shape of failure a retry cannot help.

## What must be true when this is done

1. **A local engine runs one reviewer at a time**, whatever `maxPerProvider` says, unless somebody
   deliberately raises it for that vendor.
2. **A hosted vendor is unaffected**: codex and gemini keep the cap they have, and the global cap
   still bounds the machine.
3. **A local reviewer that is cancelled by the deadline says what actually happened** — how long it
   waited, and that the engine was serving another reviewer of the same round if it was. The
   unreachable-engine diagnosis stays for the case it was built for.
4. **A status code is matched as a code, not as a substring.** `429` in a request id, a token count
   or a duration is not a rate limit.
5. **A transient vendor failure is reported as itself**, with the vendor's own words, distinct from a
   rate limit — and it is not retried on the strength of a phrase that never appeared.
6. **A real rate limit still behaves exactly as it does now**: gemini's `503 UNAVAILABLE / high
   demand` is retried once, a daily quota is not retried at all.

## Constraints

- The scheduler stays pure of vendor knowledge: it takes caps, it does not decide them. Which vendors
  are local is `PanelService`'s knowledge, from the vendor list it already holds.
- No new configuration surface unless it earns it: one setting (`COAI_LOCAL_CONCURRENCY`, default 1)
  rather than a per-vendor map nobody will fill in.
- `RateLimit` stays pure and table-tested — it is the only reason its phrases are auditable at all.
- Every phrase and every code stays **observed**, not imagined: the list's own docstring says so, and
  the two additions here come from this machine's logs.
- Behaviour on a machine with no local vendor must not change in any way.

## Build order

1. **RED first, two tests.** (a) The scheduler: three reviewers of a local provider never overlap
   while two of a hosted one do — fails today, where all three start. (b) `RateLimit.Hit` on the
   codex line above with a realistic `cf-ray: a3f4291e8b2c7d01-FRA` — if it answers true, the
   substring hypothesis is measured rather than argued, and the test names it.
2. **`BoundedScheduler` takes a cap per provider.** `Func<string, int>` or an
   `IReadOnlyDictionary<string, int>` overriding the default; the semaphore map is already built per
   provider name, so this is the value it is built with.
3. **`PanelSettings.LocalConcurrency`** (`COAI_LOCAL_CONCURRENCY`, default 1) and `PanelService`
   building the override map from vendors whose runtime is `local`.
4. **The deadline sentence.** `LocalRuntime`'s timeout message gains the elapsed seconds; the round's
   failure line says "the engine was also serving <other>" when another local reviewer overlapped it.
   Nothing here changes the unreachable-engine cure.
5. **`RateLimit`**: codes matched with a boundary (`\b429\b`, `\b503\b`, or the `status 429` shape the
   CLIs actually print), phrases unchanged; a new `ReviewerOutcome.VendorError` for a transient
   failure with the vendor's own line, reported and NOT retried.
6. **The panel's Limits section** says what the local cap is and why, in one line — a person who set
   `maxPerProvider` to 3 needs to know it does not apply to the card in their machine.
7. Docs: `module_server.md` (the caps and the classification), `module_runners.md` (the local
   engine's serialisation).

## Test plan

`src_mcp` — the MTP executable:

- the scheduler: a local provider's reviewers are serialised; a hosted provider's are not; the peak
  concurrency the scheduler records proves it rather than a timing assertion;
- `RateLimit`: the observed codex 404 line is NOT a rate limit; gemini's `503 UNAVAILABLE ... high
  demand` still is; `cf-ray: a3f4291e…` and `1429 tokens` are not; `HTTP 429` and
  `status 429` are;
- `Hopeless` unchanged for the daily-quota line;
- settings: `COAI_LOCAL_CONCURRENCY` is read, defaults to 1, and reaches the scheduler.

`src_vs_code` — `npm test`: the Limits section states the local cap.

By hand, on this machine: a code round with three local roles runs them one after another, each
answering in tens of seconds instead of two being cancelled at 590 s — the same round that produced
the symptom, re-run.

## Definition of Done

- [ ] Three local reviewers in one round answer, serialised, on this machine.
- [ ] A hosted vendor's concurrency is unchanged, and the global cap still holds.
- [ ] The codex 404 line is reported as a vendor error with its own words, not as a rate limit.
- [ ] A status code in an id or a token count no longer triggers a retry.
- [ ] The local deadline message says how long it waited and who else was using the engine.
- [ ] `CoaiMcp.Tests.exe` and `npm test` pass, with the two RED tests kept as regressions.
- [ ] `module_server.md` and `module_runners.md` record it; `todo/README.md` lists this plan.
