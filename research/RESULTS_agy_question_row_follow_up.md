# RESULTS — an antigravity question row: denied on its first act; continued once, it answers on Windows, sometimes in WSL

> Measured 2026-10-07 and 2026-10-08 for [PLAN_a_question_row_on_agy_is_continued_once.md](PLAN_a_question_row_on_agy_is_continued_once.md).
> Harness: [`scripts/probe-agy-consult-follow-up.mjs`](../scripts/probe-agy-consult-follow-up.mjs) in its QUESTION-ROW mode
> (two more arguments, a root and a question). Subject: agy **1.3.1**, model `gemini-3.8-flash-low`, the flags a
> `question-disk` row is launched with (`--print= --input-format stream-json --output-format stream-json --mode plan
> --add-dir <root>`, run from the root), `--conversation <id>` on the follow-up. This measurement did not change the
> person's agy settings; in WSL they had been deleted earlier the same day by an unrelated cleanup and rebuilt by agy
> (§2). Sibling record for the stuck consultant: [RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md).

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

**By the probe, the follow-up does not cure it in WSL** (0 of 3; through the product it answered 1 of 3 — §4). Told the
shell will not come, the model has no way to list the folder, so it
went looking for paths where it hoped to find them — the shell history, its own transcript — outside the `--add-dir`
root. agy refused those reads (`denied_actions: [{"action":"read_file"}]`, stderr *"a tool required the "read_file"
permission that headless mode cannot prompt for"*) and the turn ended empty again. Two things follow:
- **The read boundary held.** No read outside the root succeeded; a refused step can still read `state: DONE` in the
  stream (the sibling record saw the same), and `denied_actions` is the evidence, not the step state.
- **The shipped fix still makes no third launch.** The row ends `failed`, and its reason is agy's own reading of the
  refusal (`ConsultFailure.ReadDenied`): "answered nothing: it reached for a read this consultation does not allow (the
  'read_file' permission was denied)" — instead of "exited cleanly but answered nothing". Telling a model refused a read
  to read more would not help it; `AntigravityFollowUps.For` sends a follow-up only for a refused command.

The first attempt at these follow-up runs (earlier the same day) could not start: agy in WSL stopped at launch and asked
to sign in again ("Waiting for authentication… paste the authorization code"), and a run that hung on that prompt ignored
SIGTERM and had to be killed by pid; `timeout -s KILL` is the safe wrapper there. The person signed in, and the three
probe runs in the table — each a turn 1 and a turn 2 — followed.

Why Windows answers and WSL mostly does not is not explained by this record: same agy version, same model, same
follow-up text, and no Windows turn 2 reported a refusal (which paths its failed `view_file` calls tried was not kept).
By the probe, six follow-ups on Windows and three in WSL — enough to say the WSL row did not answer then, not why.

**Every WSL agy run here ran on a freshly rebuilt agy state.** At about 12:00 local the same day, an unrelated agent
cleanup deleted most of `~/.gemini` in the WSL home (agy's settings, its sign-in and its conversation state). agy
recreated its state at 12:02 (`antigravity-cli/installation_id`), the person signed in again, and its `settings.json` now
holds only `trustedWorkspaces`. Every WSL run in this record — the operator's live failure included — came after that.
So there is no WSL result from agy's earlier state, and this record cannot say whether that state would have behaved
differently.

**What these WSL runs are, and are not.** They are the probe: agy launched by hand with a question row's flags and the
shipped follow-up text, not coai-mcp. The runs through the PRODUCT came the next day, once the fix was installed — §4:
there the WSL row answered once in three, and the reason quoted above was observed as written, on the other two.

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
after it), so this run alone does not prove the follow-up ran. What it shows is one row on Windows answering through
the product with the fix. For comparison, §1's first launches — 11, with no follow-up (6 on Windows; 5 in WSL: 1 by
hand, 1 through the product, 3 by the probe) — answered none. The released build's own runs, on both sides, are §4.
`D:/rsd` is not a git checkout, so the invariant did not watch it and the follow-up's tree check had nothing to compare.

## 4. The released build, three times on each side (2026-10-08)

The installed builds, through `ask_consultants`, with the operator's two `question-disk` rows (codex `gpt-6-astra` and
antigravity `gemini-3.8-flash-low`) and the same question as §1. Windows ran coai-mcp **0.44.2** (installed by then),
from a checkout under `D:/rsd`, root `d:/rsd`; WSL ran coai-mcp **0.44.0**, from `/home/jinx/git/SSO`, root
`/home/jinx/git`. agy was 1.3.1 on both sides; codex was 0.160.0 on Windows and 0.155.0 in WSL. Between the two builds
the agy follow-up path (#692) is unchanged, but #702 (in 0.44.1) added a tier probe before a codex question row launches —
so the codex timings below are not comparable across the sides. Neither root is a git checkout, so the tree check had
nothing to compare.

| side | run | antigravity row | seconds | what it said |
|---|---|---|---|---|
| Windows | 1 | answered | 30.8 | no listing tool; a PowerShell search to run |
| Windows | 2 | answered | 38.5 | "none could be found", and a PowerShell search to run |
| Windows | 3 | answered | 52.2 | no listing tool; two PowerShell searches to run |
| WSL | 1 | answered | 27.2 | no listing tool; a `grep -rnwiE` over `/home/jinx/git` to run |
| WSL | 2 | **failed** | 18.2 | "the consultant (antigravity) answered nothing: it reached for a read this consultation does not allow (the 'read_file' permission was denied)" |
| WSL | 3 | **failed** | 22.5 | the same sentence |

- **The follow-up ran every time in WSL**: the server logged "its first launch said nothing — continued once in the
  same conversation" for all three (the log line added by the code round). On Windows, whether it ran is not
  established: that log was not read for these runs. The §1 first launches on Windows never answered (6 of 6 empty),
  which suggests it did.
- **Windows: 3 of 3 through the product, as §2's follow-up runs found (6 of 6: 3 by a scratch harness, 3 by the probe).
  WSL: 1 of 3 through the product, against 0 of 3 by the probe** — the fix turns a WSL row into an answer sometimes,
  not reliably; when it does not, the reason now says the `read_file` permission was refused instead of "exited cleanly
  but answered nothing". Which paths were refused is known only from the probe (§2); a product run records the reason.
- The codex row answered 6 of 6 (40–149 s). In WSL all three named `alert-center`'s `delivery-retry.ts`; on Windows,
  where the root is `d:/rsd`, all three found none, and named design notes under `D:/rsd/ClaudeRag/email-switcher` — the
  two sides read different folders.
- In the first WSL run, two log lines for the agy row are stamped 05:53:17, between lines at 07:52:49 and 07:53:36 in the
  same process; the runs after it were steady. 05:53 is the UTC time less the host's +2 offset — a clock that stepped
  back, or a stamp converted to UTC twice; the cause is not established.
