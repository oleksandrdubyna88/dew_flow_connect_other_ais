# RESULTS — the round counter's open tail

> Measurements for [PLAN_the_round_counter_tail.md](../todo/PLAN_the_round_counter_tail.md). Each section is one measurement
> (M1, M2, M3), recorded with the raw blocks redacted (no email, no token, no local path). M1 and M2 are recorded; M3 waits for E7.

## M1 — the agy reviewer follow-up (2026-10-21)

> Epic E1. Harness: [`scripts/probe-agy-review-follow-up.mjs`](../scripts/probe-agy-review-follow-up.mjs) (paid; it launches the
> real `agy` CLI in the shape `AntigravityRuntime.Build` sends a reviewer). Subject: agy **1.4.4**, the current reviewer build
> (agent `coai-reader`, tool `view_file` only, the `PreToolUse` hook that allows nothing else). Scratch folder: under `%TEMP%`.
> The question: when a reviewer is asked again in the same conversation (`AntigravityRuntime.FollowUp`), does the second launch
> report CUMULATIVE usage (so the sum counts the first launch twice)?
>
> **Verdict: inconclusive.** The follow-up path is not reached on agy 1.4.4 with the current read-only agent, so there was no
> follow-up to measure. Cumulative reporting of a reviewer follow-up stays **unmeasured**.

### Attempt 1 — refused before any model call

| Item | Value |
|---|---|
| Model sent | `gemini-3.7-flash-high` (`AntigravityRuntime.DefaultModel`) |
| Launches | 7, all refused |
| Refusal | `invalid model selection ... not recognized` |
| Tokens spent | 0 |

`agy models` on 1.4.4 lists `gemini-4-argon-{low,medium,high}`, `gemini-3.8-flash-{low,medium,high}`, `claude-opus-5-5-*`,
`claude-sonnet-5-5-*` and `gpt-oss-120b-medium`; `gemini-3.7-flash-high` is not among them. The default is corrected in its
own pull request (the owner chose `gemini-3.8-flash-low`); it is not part of this record.

### Attempt 2 — model `gemini-3.8-flash-low` (the owner's live agy reviewer row)

Two launches, then the probe's own cap (400 000 input tokens by the reports' own sums) stopped it. **The cap was overshot:**
452 k were spent, because the probe then checked the cap only AFTER a launch, and launch 2 alone took 357 k. The probe now
checks BEFORE each launch, refusing one that would pass the cap if it were as large as the largest so far
(`scripts/lib/spendCap.mjs`, with its test). One launch can still exceed the largest seen so far.

The harness was then hardened after review (before any further paid run): a launch is an async child whose timeout ends its
whole process tree (`scripts/lib/launch-tree.mjs`, reusing `killTree`), every other external process has a timeout, and the
spend fails closed — a launch with no finite non-negative `input_tokens` stops the probe instead of counting as 0, and invalid
cap values refuse instead of slipping past a `NaN` comparison. The command line is validated up front (launch cap 1 to 12), a
Stop message never carries launch output, progress prints to stderr, and the stream reader counts the lines it cannot parse.

| Launch | Wall time | Outcome | Denied | Answer | Input | Output | Reasoning | Cached | `view_file` calls |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 65 s | SUCCESS | no | non-empty | 94 883 | 4 399 | 3 417 | 0 | 6 |
| 2 | 327 s | SUCCESS | no | non-empty | 357 039 (summed over 51 agent steps) | 16 522 | 12 346 | 383 516 | 9 |
| Total | | | | | 451 922 | 20 921 | 15 763 | 383 516 | 15 |

Cached tokens (launch 2) are larger than its input: in agy's report cached is not a subset of input, so the two must not be
subtracted from one another.

### What this shows

- On agy 1.4.4 with the current `view_file`-only agent the model never reaches for a shell. It says `run_command` is unavailable
  and answers in prose. So there is **no denial, no empty answer and no follow-up**: the three conditions that
  `ConcludedAsync` (local) and the server's `FollowUpOf` require before any follow-up never held in either launch.
- The follow-up path that E2.S1 would correct is therefore **not reached in today's product**. The 2026-10-02 denials
  ([RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md)) came from agy **1.2.15** with another agent
  configuration; they are evidence about that setup, not about this one.
- Step 2a (an elicited denial, three launches in one conversation) never started: no launch gave a denial plus a conversation id
  plus an empty answer. Step 2b (cumulative reporting through an explicit same-conversation continuation, apart from denial
  recovery) was **not run, by decision of the owner on 2026-10-21** (option A: record the result and defer E2).
- **Cost note.** One reviewer launch costs 90 k to 360 k input tokens here (launch 1: 95 k, launch 2: 357 k), not the plan's
  "~60 k per launch". The plan's 12-launch cap and its 400 k input-token cap do not fit together: 400 k holds two launches.

### Consequences

- [PLAN_the_reviewer_follow_up_is_billed_once.md](../todo/PLAN_the_reviewer_follow_up_is_billed_once.md) stays open on its one
  question; its step 1 records this outcome.
- E2 of the plan is **deferred** until the path fires again: an agy version or reviewer configuration under which a denial with
  an empty answer happens again. See the plan's E2 section.

## M2 — where Kimi states its plan usage (2026-10-21)

> Epic E4. Subject: Kimi Code **2.3.0** on Windows (the local CLI updated itself from 2.2.0 before this window; coai's confinement
> measurements are 2.2.0's). One window with no other `kimi` process, coordinated with the session that owns the Kimi probes;
> `KIMI_CODE_NO_AUTO_UPDATE=1`, an empty working folder under `%TEMP%`. No sign-in was touched; no credential file was opened.
>
> **Verdict: only the interactive TUI states the plan's windows.** No headless source carries them: not the shim's `/usage`
> (fact C), not `/usage` as a prompt, not Kimi's own files. So E5 is **cut**, and item 1 is recorded as *checked: the shim's
> reply, a usage-only prompt, the session log; the TUI only*.

**Starting point — fact C** (M-K5b re-run, M-K7b, 2.2.0): over ACP, `/usage` carries session totals only, and the highspeed 403 is
a subscription refusal. Cell (a) is answered by it and was not re-run.

| Cell | Prediction (the plan, written first) | Observed |
|---|---|---|
| (b) `/usage` as the ONLY prompt on a fresh session, 3 runs (`kimi -p "/usage" --output-format stream-json`) | the same session line, no plan line | **Not a command in prompt mode.** The text goes to the model as a prompt. All 3 runs were refused before any answer: `provider.api_error: 429 You've reached your monthly usage limit for this billing cycle. Your quota will be refreshed in the next cycle. To continue now, purchase extra usage or upgrade your plan: …`. Exit 127, then a libuv assertion on exit (`!(handle->flags & UV_HANDLE_CLOSING)`). stdout: only `{"role":"meta","type":"system.version","version":"2.3.0"}`. No plan line. No auth error. |
| (c) Kimi's session log and its other files, for a quota field | no quota | **No quota field.** 455 files of the last 14 days were scanned by field NAME only (the credentials folder was never opened). The per-turn `usage` object holds `inputOther`, `output`, `inputCacheRead`, `inputCacheCreation` with `usageScope: "turn"`. The log names the plan only inside refusals, with no percentage and no reset instant: the 5-hour text (*You've reached your 5-hour usage limit. Your quota will reset when the current 5-hour window ends.*), the monthly text above, and an *insufficient balance* suspension. |
| (d) the TUI's `/usage` wording, for the parser | the TUI alone has it | **The TUI has it, even before a session exists.** It shows the block below. |

The TUI's `/usage` block (Kimi 2.3.0, 2026-10-21, no session yet; the bars are drawn as text):

```text
Usage
  Session usage
    No token usage recorded yet.
  Context window
    0%  (0 / 256k)
  Plan usage
    5h limit         52% used    resets in 2h 23m
    Monthly limit    100% used   resets in 22d 4h…
                     kimi 0% · code 100%
```

The monthly row's reset was cut by the terminal's width. The line under it splits the monthly use between *kimi* and *code*.

### What this shows

- The plan's 5-hour and monthly windows, with their resets, are stated **only** by the interactive TUI. The TUI reads them before
  any session exists, so they come from a service call of the TUI's own. That call is not a launch coai may make: the plan forbids a
  read of its own and any use of the person's sign-in outside a turn.
- A **spent monthly window refuses every launch with HTTP 429** and its own wording (*monthly usage limit for this billing cycle …
  will be refreshed in the next cycle*). It differs from the 5-hour text that coai's spent-quota classifier recognises today. That
  is a defect of the spent-quota plan's area and is handed to it, not fixed here.

### Consequences

- E5 (the Kimi gauge) is **cut, provisionally**. Cell (a), `/usage` over ACP, is fact C and was measured on **2.2.0**. On 2.3.0 only prompt
  mode was measured, so 2.3.0's ACP answer is not established (the epics 4-6 cadence consultation, 2026-10-21). Cell (a) is re-run on 2.3.0
  inside the next Kimi probe series, under its confinement and hold procedure. E5 stays unbuilt unless that cell, or a later version,
  answers with the plan's windows.
- No fixture is added under `src_mcp/tests/fixtures/kimi/`: with E5 cut there is no parser for it. The verbatim block above is the
  record.
