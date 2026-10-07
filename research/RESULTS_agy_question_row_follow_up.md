# RESULTS — an antigravity question row: denied on its first act, answered when continued once

> Measured 2026-10-07 for [PLAN_a_question_row_on_agy_is_continued_once.md](PLAN_a_question_row_on_agy_is_continued_once.md).
> Harness: [`scripts/probe-agy-consult-follow-up.mjs`](../scripts/probe-agy-consult-follow-up.mjs) in its QUESTION-ROW mode
> (two more arguments, a root and a question). Subject: agy **1.3.1**, model `gemini-3.8-flash-low`, the flags a
> `question-disk` row is launched with (`--print= --input-format stream-json --output-format stream-json --mode plan
> --add-dir <root>`, run from the root), `--conversation <id>` on the follow-up. The person's agy settings were not
> touched. Sibling record for the stuck consultant: [RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md).

## 1. What a question row's first launch does

Turn 1 is the QUESTION, not a forced command: *"Which other project under `<root>` already has a reusable helper that
retries an outbound SMTP send with backoff? Answer in two sentences, naming a file or saying none exists."*

| side | runs | what happened |
|---|---|---|
| Windows, root `D:/rsd` | 6 | every time the model's first act was `run_command`; headless agy auto-denied it and the turn ended with `"response":""`, `denied_actions: [{"action":"command"}]`, 6–14 s |
| WSL, root `/home/jinx/git` | 1 by hand, 1 through the product | the same: `run_command ls -la /home/jinx/git`, denied, empty. Through `ask_consultants` (installed coai-mcp 0.43.0) the row ended `failed` in 18.6 s, "the consultant exited cleanly but answered nothing" — the operator's report |

stderr says why, every time: *"jetski: no output produced — a tool required the "command" permission that headless mode
cannot prompt for, so it was auto-denied."*

## 2. Does the stuck consultant's follow-up turn it into an answer?

Turn 2 resumes the conversation with `AntigravityFollowUps.NoCommands`, word for word.

| side | runs | answered | seconds | tools in turn 2 | denied again |
|---|---|---|---|---|---|
| Windows | 6 (3 by a scratch harness, 3 by the repository's probe) | **6 of 6** | 11–67 | `view_file` only (several ERROR on guessed paths) | 0 |
| WSL | 0 | — | — | — | — |

WSL was not measured past turn 1: partway through the session agy in WSL stopped and asked to sign in again
("Waiting for authentication… paste the authorization code"), which is the person's to do. A run that hung on that
prompt also ignored SIGTERM and had to be killed by pid; `timeout -s KILL` is the safe wrapper there.

**The answers are weak, and that is a property of plan mode, not of the follow-up.** With no directory listing
(`list_dir`, `find_by_name` and `grep_search` are declared by `init` and unavailable, as the sibling record found), a
question over a whole folder cannot be searched by reading alone: every turn-2 answer named a command for the caller to
run (`Get-ChildItem … | Select-String …`) and one said none was found in what it could open. That is an answer the caller
can act on; the empty `failed` row was not.

## 3. The fix, live

The branch build (`fix/question-row-agy-follow-up`, Debug) against a scratch data directory holding the operator's
Windows settings with one antigravity `question-disk` row and root `D:/rsd`: `ask_consultants` came back `complete`, the
row `answered` in 29.6 s, ONE ledger line (kind `question`, 55 681 tokens in). That build did not yet log whether the
turn was continued (the log line came after it), so this run alone does not prove the follow-up ran — what it shows is
the row answering where every uncontinued first launch above (7 of 7) did not.
`D:/rsd` is not a git checkout, so the invariant did not watch it and the follow-up's tree check had nothing to compare.
