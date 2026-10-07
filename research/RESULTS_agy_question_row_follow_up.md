# RESULTS — an antigravity question row: denied on its first act; continued once, it answers on Windows and not in WSL

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
| WSL, root `/home/jinx/git` | 1 by hand, 1 through the product, 3 by the repository's probe | the same every time: `run_command` (`ls -la /home/jinx/git`), denied, empty, 8–28 s. Through `ask_consultants` (installed coai-mcp 0.43.0) the row ended `failed` in 18.6 s, "the consultant exited cleanly but answered nothing" — the operator's report |

stderr says why, every time: *"jetski: no output produced — a tool required the "command" permission that headless mode
cannot prompt for, so it was auto-denied."*

## 2. Does the stuck consultant's follow-up turn it into an answer?

Turn 2 resumes the conversation with `AntigravityFollowUps.NoCommands`, word for word.

| side | runs | answered | seconds | tools in turn 2 | denied again |
|---|---|---|---|---|---|
| Windows | 6 (3 by a scratch harness, 3 by the repository's probe) | **6 of 6** | 11–67 | `view_file` only (several ERROR on guessed paths) | 0 |
| WSL | 3 (the repository's probe) | **0 of 3** | 9–17 | `view_file` — on the root folder itself (refused as invalid: it is a directory), then on files OUTSIDE the root: `~/.bash_history`, and once its own `~/.gemini/antigravity-cli/brain/…/transcript.jsonl` | `read_file`, 3 of 3 |

**In WSL the follow-up does not cure it.** Told the shell will not come, the model has no way to list the folder, so it
went looking for paths where it hoped to find them — the shell history, its own transcript — outside the `--add-dir`
root. agy refused those reads (`denied_actions: [{"action":"read_file"}]`, stderr *"a tool required the "read_file"
permission that headless mode cannot prompt for"*) and the turn ended empty again. Two things follow:
- **The read boundary held.** No read outside the root succeeded; a refused step can still read `state: DONE` in the
  stream (the sibling record saw the same), and `denied_actions` is the evidence, not the step state.
- **The shipped fix still makes no third launch.** The row ends `failed`, and its reason is agy's own reading of the
  refusal (`ConsultFailure.ReadDenied`): "answered nothing: it reached for a read this consultation does not allow (the
  'read_file' permission was denied)" — instead of "exited cleanly but answered nothing". Telling a model refused a read
  to read more would not help it; `AntigravityFollowUps.For` sends a follow-up only for a refused command.

The first WSL attempt (earlier the same day) could not be measured past turn 1: agy in WSL stopped and asked to sign in
again ("Waiting for authentication… paste the authorization code"), and a run that hung on that prompt ignored SIGTERM
and had to be killed by pid; `timeout -s KILL` is the safe wrapper there. The person signed in, and the three runs above
followed.

Why Windows answers and WSL does not is not explained by this record: same agy version, same model, same follow-up
text, and no Windows turn 2 reported a refusal (which paths its failed `view_file` calls tried was not kept). Six
follow-ups on Windows and three in WSL — enough to say the WSL row does not answer, not why.

**What these WSL runs are, and are not.** They are the probe: agy launched by hand with a question row's flags and the
shipped follow-up text, not coai-mcp. No WSL run through the PRODUCT with the fix (coai-mcp 0.44.0) has been made. The
reason quoted above for the WSL row is what the shipped code would write for this stream — derived from
`AntigravityConsultant.SilentFailure` → `ConsultFailure.ReadDenied`, not observed in a product run.

**The answers are weak, and that is a property of plan mode, not of the follow-up.** With no directory listing
(`list_dir`, `find_by_name` and `grep_search` are declared by `init` and unavailable, as the sibling record found), a
question over a whole folder cannot be searched by reading alone: every turn-2 answer named a command for the caller to
run (`Get-ChildItem … | Select-String …`) and one said none was found in what it could open. That is an answer the caller
can act on; the empty `failed` row was not.

## 3. The fix, live

The fix shipped in **coai-mcp 0.44.0** (PR #692). Before the release, the branch build (`fix/question-row-agy-follow-up`,
Debug, Windows) ran against a scratch data directory holding the operator's Windows settings with one antigravity
`question-disk` row and root `D:/rsd`: `ask_consultants` came back `complete`, the row `answered` in 29.6 s, ONE ledger
line (kind `question`, 55 681 tokens in). That build did not yet log whether the turn was continued (the log line came
after it), so this run alone does not prove the follow-up ran — what it shows is the row answering where every
uncontinued first launch in §1 did not: 11 of 11 (6 on Windows; 5 in WSL — 1 by hand, 1 through the product, 3 by the
probe). There is no product run in WSL with the fix (§2).
`D:/rsd` is not a git checkout, so the invariant did not watch it and the follow-up's tree check had nothing to compare.
