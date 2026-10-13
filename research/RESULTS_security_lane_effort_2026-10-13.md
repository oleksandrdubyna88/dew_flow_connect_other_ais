# RESULTS — does reasoning effort above `none` buy a better security-lane answer? (2026-10-13)

> Status: **prediction recorded, nothing measured yet.** Epic 2 of
> [PLAN_local_effort_reaches_every_local_launch.md](../todo/PLAN_local_effort_reaches_every_local_launch.md).
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

## Observations

*(none yet)*

## What this does not settle

One model, one quantisation, one engine version, one machine; one synthetic fixture pair; two of the thirteen prompts.
A cut or an empty answer says nothing about recall on real code. Wall times include queueing behind other work on the
same card.
