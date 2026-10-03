# RESULTS — what a headless antigravity can read, and whether a denied consultation answers when continued

> Measured 2026-10-02 for E1.1 of [PLAN_the_consultant_works_on_every_vendor.md](PLAN_the_consultant_works_on_every_vendor.md).
> Harness: [`scripts/probe-agy-consult-follow-up.mjs`](../scripts/probe-agy-consult-follow-up.mjs). Subject: agy
> **1.2.15**, model `gemini-3.1-pro-high`, the consultant's flags (`--print= --input-format stream-json
> --output-format stream-json --mode plan --add-dir <repo>`, `--conversation <id>` on a resumed turn), on
> Windows 11 and in WSL2 Ubuntu. The person's agy settings were not touched.

## 1. Which read tools does the model actually have?

agy's `init` event DECLARES `view_file`, `grep_search`, `find_by_name` and `list_dir` (the 2026-10-02
consultation transcript lists all four). One turn per side asked the model to use only `grep_search`,
`find_by_name` and `list_dir`.

| side | what happened |
|---|---|
| Windows | the model said it does **not** have those three tools; it delegated to a `schedule` subagent, wrote a `plan.md` artifact into its own `~/.gemini/antigravity-cli/brain/…` directory (outside the repository) and tried `run_command`, which was denied |
| WSL | the model said it does **not** have those three tools and stopped; no tool ran |

**So the declared list is not the model's list.** In `--mode plan` the only read tool observed working is
`view_file` (it read `BackupRunner.cs` and `BackupStore.cs` in the real consultation). The consultation's
prompt and follow-up name `view_file` alone, and tell the model not to plan, schedule or delegate.

This reverses a decision taken an hour earlier: the plan round's gemini reviewer said those three tools do
not exist for the model, and the finding was rejected on the strength of the `init` declaration. The
reviewer was right; the declaration was the wrong evidence.

## 2. Does one prose follow-up turn a denied consultation into an answer?

Each repeat: turn 1 asks for `run_command git grep -n QUOKKA .` (always denied headless); turn 2 resumes the
same conversation with the follow-up text the product ships —

> Shell commands are not available in this consultation: run_command was denied and will stay denied. Do
> not call it again, and do not plan, schedule or delegate the work. Read the files you need with
> view_file, then answer now, in plain prose, from what you have read. If a check needs a command, name the
> exact command for the caller to run.

| side | repeat | turn 1 | turn 2 (the follow-up) |
|---|---|---|---|
| Windows | 1 | empty, `command` denied | **answered**, no denial (tried `view_file` on a guessed name) |
| Windows | 2 | a plan text (it wrote `plan.md` first), `command` denied | **answered**, no denial |
| Windows | 3 | empty, `command` denied | **answered**, no denial |
| WSL | 1 | empty, `command` denied | **answered**, no denial (`view_file` ×4) |
| WSL | 2 | empty, `command` denied | **answered**, named `git grep -n QUOKKA .` for the caller to run |
| WSL | 3 | a plan text, `command` denied | **answered**, no denial |

**6 of 6 follow-ups answered in prose; none reached for the shell again.** The answers here say the model
could not find the marker — expected, since the probe gives no file names and the model has no way to list
a directory; a real consultation's prompt carries the working tree and the suspected files, which is what
`view_file` needs.

## 3. Side observations

- A denied `run_command` step reported `state: DONE` in every WSL repeat (and in one Windows P0 run) while
  `result.denied_actions` named the command. Denial is read from `denied_actions`, never from step state.
- Turn 1 is not always empty: twice the model answered with a plan text AND had a command denied. The
  follow-up is still taken only when the answer is EMPTY; a non-empty first answer is returned as it is.
- Usage on the follow-up was larger than on turn 1 every time (e.g. 13 574 → 20 748 input tokens). That is
  consistent with the cumulative reporting measured on 2026-09-12 (`AntigravityConsultant.cs`), but does
  not prove it — a resent history grows the input too. Billing a two-launch turn as the field-wise MAX of
  the two reports never double-bills under either reading; under a per-turn reading it under-bills by the
  first launch's share. Not settled here.

## 4. What is not settled

The follow-up's answer QUALITY on a real question (this probe's question is unanswerable without a file
list); other models; agy versions other than 1.2.15; macOS.
