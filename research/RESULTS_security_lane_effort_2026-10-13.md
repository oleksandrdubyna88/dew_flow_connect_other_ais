# RESULTS — does reasoning effort above `none` buy a better security-lane answer? (2026-10-13)

> Status: **measured 2026-10-13 — pilot plus the decision-point arm; `none` recommended, the shipped default kept.** Epic 2 of
> [PLAN_local_effort_reaches_every_local_launch.md](PLAN_local_effort_reaches_every_local_launch.md).
> This file is committed with the prediction BEFORE the first call (measurement rule 4); the observations are added
> after, beside it, never in place of it.

## The question

On 2026-10-13 every `redteam-*` local security reviewer on the owner's machine read ~17 000 prompt tokens in ~6 s and
generated exactly **10 tokens** — a strict empty `{"status":"SECURE","findings":[]}` — sent with
`reasoning_effort: none`, the product's default for local launches. The owner asked whether that is normal, and to
experiment. The earlier campaign ([RESULTS_security_lane_local_llm_windows.md](RESULTS_security_lane_local_llm_windows.md))
already showed the same 10-token answer on all fifteen preset feature slices (§6.1), the same model at `none` reporting
a planted tenant bypass and staying empty on its safe twin (§6.2–6.3), and `low` thinking at temperature 0 looping to
the 8 192 ceiling with no content (§6.3, `sql-v2-low-run`). What it did not do is vary the effort ALONE through the
product's own round with everything else pinned. This does.

## Conditions (pinned)

| Variable | Value |
|---|---|
| Subject | branch `measure/security-lane-effort`, on epic 1's commit (every local launch is told the panel's effort; a cut answer is named by `--ask-local`) |
| Harness | `src_mcp/tests/SecurityLaneEffortMeasurementTests.cs` (explicit), through `SecurityLaneCalibrationTests.RunCell` — real `PanelService.ReviewCodeAsync`, four clean FakeCli ordinary reviewers, `redteam-authz` + `redteam-sql` paired to one `local` row |
| Model | `Gemma4-26B-A4B-Uncensored_vk128:latest`, digest `6548c373a4c1a01c84d5bb3166f0971ae14d3e8216bb676c2f9f393527b6dc2e`, Q5_K_M, 25.2 B |
| Engine | Windows Ollama 0.40.3, `http://localhost:11434/v1`, context 131 072, model resident (`size_vram` 22 313 749 708) |
| Sampling | the product's: temperature 0, a seed derived from the prompt bytes, frequency penalty 0.2, `json_schema` strict |
| The variable | `reasoning_effort`: `none`, `low`, `medium`, `high` (panel `LocalReasoningEffort`, read back from every cell's argv) |
| Ceiling | 8 192 (panel `LocalMaxTokens`), except the one decision-point arm below |
| Fixtures | **unsafe**: the safe invoice controller with the tenant guard removed and `search` interpolated into the SQL (two planted defects); **safe**: the same controller with `ORDER BY Id` added to its parameterised query (correct answer: empty) |
| GPU | the family GPU lease daemon was NOT listening (`ECONNREFUSED 127.0.0.1:5455`); the product's cross-process `EngineLease` serialised local calls, the owner's live reviews included — so a cell's wall time includes queueing and is not a latency figure |

## Prediction (written before the first call)

| Arm | Unsafe fixture | Safe fixture |
|---|---|---|
| `none` | each prompt answers in a few hundred tokens with a finding for its own class (authz: the removed tenant guard; sql: the interpolated search), `finish_reason: stop`; quality as §6.2 — detection yes, explanations may overclaim | 10-token `SECURE`, empty |
| `low` | most cells loop in reasoning to the 8 192 ceiling: exit 70 with the epic-1 sentence (`cut at the token limit (max_tokens 8192)`, `reasoning_effort was low`), no finding recorded | same — the length of the reasoning does not depend on there being a defect |
| `medium`, `high` | as `low`, cut at least as often | as `low` |

**Basis.** §6.3's `sql-v2-low-run` (temperature 0) used all 8 192 tokens with no content; the only thinking arm that
finished there changed sampling to temperature 0.6 / top-p 0.95, which the product does not send. The product's
`none` is the documented escape (`module_server.md` § *A local reviewer is told not to think*).

**What would refute it.** Any thinking arm finishing under the ceiling on most unsafe cells — then that arm and `none`
get three repeats on both fixtures and are hand-scored (correct trigger and mechanism, an impact the source supports,
complete preconditions, no invented values; and abstention on the safe twin).

## Procedure

1. Pilot: one repeat per arm on the unsafe fixture — 4 arms × 2 prompts = 8 local calls.
2. Decision: if every thinking arm is cut at the ceiling, ONE extra arm — `low` at 16 384, unsafe, one repeat — then stop.
   Otherwise the three-repeat comparison above.
3. Corroborate each cell's generated-token count against Ollama's own `server.log` (`print_timing … n_gen` / eval tokens).

## Observations (2026-10-13, 11:50–12:14 UTC)

Pilot, one repeat per arm, unsafe fixture; then the decision-point arm. Every cell's recorded `--ask-local` argv carried
its arm's `--reasoning-effort` and `--max-tokens` (the harness's own assertion, green on every arm). Generated tokens
are the shim's usage line, and each one equals Ollama's own `server.log` eval count for that request.

| Arm | `redteam-authz` | `redteam-sql` | Planted defects found | Round says | Wall |
|---|---|---|---|---|---|
| `none` · 8 192 | stop, **664** tok — tenant bypass (blocking) **and** SQL injection (major) | stop, **816** tok — SQL injection (major) **and** cross-tenant exposure (blocking) | **2 of 2, by both prompts** | `revise`, all 6 answered | 76 s |
| `low` · 8 192 | stop, 4 048 tok — tenant bypass only | **cut at 8 192**, 0 chars, exit 70 | 1 of 2 | `revise`, 5 of 6 | 220 s |
| `medium` · 8 192 | **cut at 8 192**, exit 70 | **cut at 8 192**, exit 70 | **0** | **`proceed`**, 4 of 6 | 300 s |
| `high` · 8 192 | stop, 4 746 tok — tenant bypass only | **cut at 8 192**, exit 70 | 1 of 2 | `revise`, 5 of 6 | 272 s |
| `low` · **16 384** | stop, 3 700 tok — tenant bypass only | **cut at 16 384**, exit 70 | 1 of 2 | `revise`, 5 of 6 | 448 s |

Every cut arrived as epic 1's sentence, e.g. *"the answer from the local engine at http://localhost:11434/v1 was cut at
the token limit (max_tokens 8192): 8192 tokens generated, 0 characters of content arrived — reasoning_effort was low, and
a thinking model can spend the whole ceiling thinking: set it to none, or raise COAI_LOCAL_MAX_TOKENS; the fragment was not
kept"*, and the round named the failed reviewer in its count.

**Against the prediction.** Confirmed: `none` answered both prompts with both planted defects in a few hundred tokens;
4 of the 6 thinking cells at 8 192 were cut, the SQL prompt in every thinking arm. Not predicted: the authz prompt
FINISHED under `low` and `high` (4 048 and 4 746 tokens) — but found less than `none` did (one defect, not two).

**The decision point.** No thinking arm finished most of its cells, so the procedure's single extra arm ran: `low` with
the ceiling doubled to 16 384. The SQL prompt was cut again, at 16 384 — the ceiling is not what stops it; the reasoning
does not converge at temperature 0. Per the procedure, the measurement stops here; the three-repeat comparison and the
safe-twin arms were not run (that branch was for a thinking arm that answers on most cells, and none did).

## Conclusion and recommendation

Under these conditions, **`none` is the best setting for the security lane on this model, by every measure taken**: it
found both planted defects from both prompts, in 664–816 tokens and 76 s for the whole round, where every thinking arm
found at most one, took 3–6× the time, and lost at least one of the two reviewers to the ceiling. **Keep the shipped
default.** The 10-token `SECURE` answers the owner saw on real pull requests are what this model at `none` says when it
finds nothing — the same model at the same setting reported both planted defects here — so a quiet lane is not by itself
a broken one. Whether it MISSES real defects in real pull requests is a recall question this run cannot answer (below).

**A finding about the gate, not about effort — decided by the owner.** The `medium` round lost BOTH security reviewers
to the ceiling and its verdict was **`proceed`** ("4 of 6 reviewers answered"): a security lane that answered nothing
let the round through, because it had no findings to count. The reviewer count is honest; the verdict is not a security
statement. **The owner's decision, 2026-10-13: that is intended — a lane whose reviewers all failed does not hold the
round.** Nothing changes.

## What this does not settle

One model, one quantisation, one engine version, one machine; one synthetic fixture pair, and only its unsafe half was
run (the safe twin was not needed by the procedure); two of the thirteen prompts; one repeat per arm (fixed seed and
temperature make a repeat close to identical, which the earlier campaign measured but did not prove byte for byte). A cut
or an empty answer says nothing about recall on real code. Wall times include queueing behind other work on the same card
(the first `none` request waited ~44 s before its own 11 s of generation). Thinking with different sampling (temperature
0.6, top-p 0.95 — §6.3 of the earlier campaign) is outside the product's request and was not varied here.

Raw cells (requests, argv, process results, answers, replies) were kept under the session scratch directory
`…/scratchpad/effort/<arm>-<ceiling>-unsafe-<utc>/` and are deleted once this file is committed (growth rule of the plan).

**A note on the harness, from its code round.** These cells ran before the harness's non-strict mode checked that a
failed reviewer was a cut and nothing else; every failed cell above was read by hand from its `process.json` and each is
exit 70 with the cut sentence. The harness now enforces it (`SecurityLaneEffortArm.UnexpectedFailures`, `NotAnswered`).
