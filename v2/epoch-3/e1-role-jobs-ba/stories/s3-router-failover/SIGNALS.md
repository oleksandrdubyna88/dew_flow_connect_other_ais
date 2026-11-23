# Stop signals of the vendor CLIs — what was real, what was not (epoch 3 E1, s3 t1)

> Recorded 2026-11-23 with claude 2.1.330, codex-cli 0.171.0 and agy 1.5.14 on the developer machine (WSL2). The files are in
> `src_mcp/tests/fixtures/role-stops/`; every file names its CLI, its version, the day, the argv and who recorded it.
> `src_mcp/tests/ARoleStopSignalsAreRecordedTests.cs` holds this document to the files: a table row here that the files do not
> support fails a test.

## 1. Rule of this task

A stop is a signal only if a file in `role-stops/` shows it. The failover loop (t3) reads nothing that is not in this document or
in the code that exists today (`RateLimit` phrases, `ClaudeRuntime.WhyItFailed`, `CooldownParser`). No phrase was added by guess.

## 2. What could be triggered, and was (10 recordings, live)

None of these needs a paid quota: each stops before a model answers, or fails at sign-in. Exception: see section 6 (two claude
runs were paid by mistake — the CLI did not refuse an unknown model name).

| Vendor | Trigger | Exit | Where the signal is | Today's classifier (`ConsultFailures.Classify`) |
|---|---|---|---|---|
| claude | bad flag `--no-such-flag` | 1 | stderr: `error: unknown option '--no-such-flag'` | `vendor-refused` |
| claude | no sign-in (scratch `HOME` and `CLAUDE_CONFIG_DIR`, nothing real) | 1 | stdout JSON envelope: `is_error: true`, `result: "Not logged in · Please run /login"`, `api_error_status: null`, `total_cost_usd: 0`; stderr empty | `exit` (crash) |
| claude | unknown full model name + `--max-budget-usd 0.02` | 1 | stdout envelope: `is_error: true`, `subtype: error_max_budget_usd`, `terminal_reason: budget_exhausted`, `errors: ["Reached maximum budget ($0.02)"]`, **no `result` field** | `exit` (crash, no sentence) |
| claude | unknown short model name | 0 | a normal success envelope; `modelUsage` names the default model that ran | answered (see section 6) |
| codex | bad flag | 2 | stderr: `error: unexpected argument '--no-such-flag' found` + usage | `exit` |
| codex | unknown model `-m no-such-model-xyz` (real sign-in) | 1 | stdout JSON events: `item.completed` (type `error`: "Model metadata … not found"), then `turn.failed` with `error.message` "The 'no-such-model-xyz' model is not supported when using Codex with a ChatGPT account."; stderr empty | `exit` |
| codex | no sign-in (scratch `CODEX_HOME`) | 1 | stdout events `error` ("Reconnecting… unexpected status 401 Unauthorized: Missing bearer or basic authentication"), last `turn.failed` with the same 401 text; stderr has WARNING and ERROR log lines; it took 15.7 s (five reconnects) | `exit` |
| agy | bad flag | 2 | stderr: `flags provided but not defined: -no-such-flag` + usage | `exit` |
| agy | unknown model | 1 | stdout `result` event: `status: "ERROR"`, `error: "invalid model selection …"`, `usage.total_tokens: 0`; stderr repeats the sentence and lists the known models. Refused before any model call, 2.9 s | `exit` |
| agy | no sign-in (scratch `HOME`) | 1 | stdout `result` event: `status: "ERROR"`, `error: "authentication failed or timed out"`, `finished_reason: "error"`; stderr: `Error: authentication required. Run 'agy' to log in, then retry.` | `exit` |

Findings from the table:

1. **Today's classifier cannot tell a lost sign-in from a crash.** All three sign-in failures read `exit`. Spec A says a lost sign-in skips
   the vendor's other models, so the role layer (t3) reads them on top, from the three recorded sentences above (claude `result`
   starting "Not logged in", codex `401 Unauthorized`, agy `authentication required` / `authentication failed`).
2. **A claude 429 envelope also reads `exit`** (see section 3). The role layer reads the structured integer `api_error_status: 429`, not prose.
3. **A bad flag is ours; a bad model is the list's.** Only claude's wording of a bad flag is read as `vendor-refused` (it fails the same way
   for every model of that vendor, so the vendor's other models are skipped). The bad flags of codex and agy read as a plain `exit`, so
   each of that vendor's models fails in turn — a known limit, it costs one fast launch per model. An unknown model of codex or agy is refused
   for that model only (agy before any model call), so it is a plain crash: the next model is tried.
4. **Where the failure text is differs per vendor**: claude on stdout (envelope), codex on stdout (events), agy on stdout (`result` event)
   and on stderr. Only a bad flag puts the sentence on stderr alone.

## 3. What could NOT be triggered, and was not

- **A real rate limit or a spent quota.** It cannot be made on purpose without spending the quota of the owner.
- **A real model refusal** (the model declines to work). It cannot be made on purpose, and no vendor was seen to do it in a recording.

For these two the only limit signals are the shapes already in the code, and two older real recordings carried over (marked
`EARLIER WORK, not this story` inside the files — they are not new measurements):

| File | What it is | Source |
|---|---|---|
| `claude-spend-limit-429-envelope.json` | claude 2.1.284, exit 1, envelope `is_error: true`, `api_error_status: 429`, `result: "You've hit your monthly spend limit…"` | `AClaudeReasonInItsEnvelopeTests.cs`, benchmark of 2026-09-29 |
| `agy-quota-exhausted-stderr.json` | agy stderr `AGY_ERROR … RESOURCE_EXHAUSTED (code 429) … Individual quota reached … Resets in 33m52s` (agy version not recorded) | `tests/fixtures/antigravity/consult-quota.stderr.txt`, 2026-10-03 |

Limit signals the code reads today, and nothing else: `RateLimit` phrases and status-code shapes (`ReviewerOutcome.RateLimited`, the
classifier's `rate-limited` and `quota`), `RateLimit.Hopeless` (a spent allowance), the claude envelope read by `ClaudeRuntime.WhyItFailed`,
the Kimi shim names, and `CooldownParser.StatedReset` for a stated reset time. Of the recorded files only agy's quota line is read as a limit
(`quota`) by that code. The claude 429 envelope is not (it carries no phrase the code knows), so the role layer adds ONE structured reading:
envelope `is_error: true` with `api_error_status` the integer 429 = a limit that skips the vendor's other models (the envelope names a spend limit).
A 503 or 529 status was not recorded and is not read.

## 4. Does a structured refusal field exist? (per vendor)

| Vendor | Answer |
|---|---|
| claude | **No field on file.** The envelope has `stop_reason` (seen: `end_turn`, `stop_sequence`), `terminal_reason` (seen: `completed`, `api_error`, `budget_exhausted`), `subtype` and a `safety_stops` counter (always 0). None of these ever held a refusal word in a recording, and a refusal cannot be provoked, so none is read. |
| codex | **No.** A failure is a `turn.failed` event with a message; no refusal event is on file. |
| agy | **No.** The `result` event has `status` (`ERROR` recorded; `SUCCESS` in the older advice fixture) and `finished_reason` (`error` recorded). No refusal word on file. |

So the only refusal signal in E1 is the role's own output arm: `status: "refused"` with a `reason` (spec D risk 1). No prose is matched.

## 5. This shell is inside a Claude Code session — and claude ran

`CLAUDECODE`, `CLAUDE_CODE_SESSION_ID` and other `CLAUDE_CODE_*` variables were set in the shell. `claude -p` ran anyway; it did not refuse.
No session variable was removed or changed (no `env -i`, no `unset`); the two sign-in runs only moved `HOME` and `CLAUDE_CONFIG_DIR` to an empty folder.
The codex and agy sign-in runs moved `CODEX_HOME` and `HOME` the same way. No real credential file was read or written by this task.

## 6. Two paid runs by mistake — and a finding

An unknown model name was meant to give an error envelope. It did not: claude 2.1.330 **ran the default model** (`claude-opus-5-5` on this machine)
for `--model no-such-model-xyz` and for `--model claude-no-such-model-9-9`, and each run cost about 0.81 USD (a 100 000-token prompt cache write).
`--max-budget-usd 0.02` did not prevent the first call; it only turned the answer into the budget error shown in the table. The files are kept:
`claude-unknown-model-name-runs-on-the-default.json` and `claude-unknown-full-model-name-budget-capped.json`.

Consequence for the router (t2) and for roles: a wrong model name in a claude list is **not** a crash and not a refusal — the CLI answers with another model and bills it.
`COAI_ROLES_MODELS` names are therefore the owner's to get right; `VendorRouting` checks only the family. A later story may compare `modelUsage` with the asked model.
No more paid calls were made for this task.

## 7. How the role layer reads a stop (what t3 builds on this)

| Stop | Read from | Failover |
|---|---|---|
| limit | `ConsultFailure.Quota` / `RateLimited`; claude envelope `api_error_status` 429 | next model at once; a spent quota or the 429 envelope also skips the vendor's other models; only the last model waits (`RetryLadder`) |
| refusal | the output arm `status: "refused"` | next model at once |
| crash | any other `ConsultFailure` (`exit`, `empty`, `timeout`, `deadline`, `conversation-dropped`…) | next model at once; `vendor-refused`, `cli-not-found` and `launch-refused`, and the three recorded sign-in failures, also skip the vendor's other models |
| no failover | `cancelled`, `tree-changed`, `output-invalid` | the job ends |
