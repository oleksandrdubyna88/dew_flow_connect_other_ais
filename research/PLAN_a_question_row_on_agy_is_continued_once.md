# PLAN — an antigravity question row whose shell command was denied is continued once, as a consultation is

> Status: **IMPLEMENTED, 2026-10-07.** Scope: `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs`
> (the CLI turn), `src_mcp/src/Server/QuestionConsult/QuestionFanOut.cs` (hands the row its tree check),
> `src_mcp/tests_fakecli/Program.cs` (recognises agy's argv), one scenario test.
>
> **Deviations from the plan as written:** (1) `ConsultFailures.EmptyOf` was made public — the silent launch's sentence
> without the adapter's refusal reading — so the no-follow-up reason is not a second copy of `ConsultFailures.Silent`'s
> expression; (2) a log line says when a row's first launch said nothing and whether it was continued, because the
> record keeps no launch count and the live check could not tell; (3) the measurement harness is the existing
> `scripts/probe-agy-consult-follow-up.mjs`, widened with a QUESTION-ROW mode, not a new script; (4) the RED for test 3
> could not be red before the fix (one launch was all there was), so its teeth were proved the other way: the fan-out's
> `ChangesSoFar` left out turned it red; (5) the code round (codex) found that a turn cut short between the launches
> billed its first launch as `ok` — fixed RED-first: the line is now written `interrupted`, and two more tests drive the
> caller giving up during the follow-up and during the tree check; (6) `research/architecture.md` gained a note (code
> round): `ConsultantTurn` has three callers now; (7) on the pull request (#692): the row's ledger line carries the whole
> `Usage` — a scalar call had dropped codex's cached count (RED-first, `ARowsCachedTokens_ReachTheLedger_…`);
> `QuestionResolution.For` and `RowAdmission.Admitted.Runtime` are typed `IConsultantRuntime`, which every arm already
> returned, so the plain one-launch branch the split had kept was unreachable and is gone, and `ChangesSoFar` is
> `required`; the fake CLI's vendor-shape check reads a set (CodeRabbit); `scripts/**` — the paid live probes — joined
> Sonar's coverage list, analysis on, because no CI job can run them.
>
> **Open tail:** the follow-up in WSL is unmeasured (agy there asked to sign in again mid-session — the person's to
> do); agy's `Toolbox` sentence is not in the question prompts.
>
> Related docs: [RESULTS_agy_question_row_follow_up.md](RESULTS_agy_question_row_follow_up.md) (this plan's
> measurement), [RESULTS_agy_consult_follow_up.md](RESULTS_agy_consult_follow_up.md) (the follow-up text, measured 6 of
> 6 for the stuck consultant), [PLAN_the_consultant_works_on_every_vendor.md](PLAN_the_consultant_works_on_every_vendor.md)
> (E1.3 built `ConsultantTurn` for the stuck consultant), [module_server.md](module_server.md),
> [module_runners.md](module_runners.md), [module_tests.md](module_tests.md).

## 1. The symptom

Seen live by the operator on 2026-10-07, on the first day the question consultant had rows at all. In WSL, with two
`question-disk` rows — codex `gpt-6-astra` and antigravity `gemini-3.8-flash-low`, root `/home/jinx/git` —
`ask_consultants` came back `partial`: codex answered in 111.6 s, and the antigravity row ended

> `failed` in 18.6 s — "the consultant exited cleanly but answered nothing"

Run by hand with the planner's own flags (`--print= --input-format stream-json --output-format stream-json --mode plan
--add-dir /home/jinx/git --model gemini-3.8-flash-low`, agy 1.3.1), the model's first act is
`run_command ls -la /home/jinx/git`; headless agy auto-denies it, ends the turn with `"response":""` and
`denied_actions: [{"action":"command"}]`, and says so on stderr:

> jetski: no output produced — a tool required the "command" permission that headless mode cannot prompt for, so it
> was auto-denied.

Reproduced on Windows the same day (agy `agy.exe`, root `D:/rsd`): turn 1 in 11.1 s, `denied: ["command"]`, empty.

## 2. Why — the cure exists, on the other road

The stuck consultant (`consult`) met exactly this on 2026-10-02 and was cured by E1.3 of
PLAN_the_consultant_works_on_every_vendor.md: `ConsultantTurn.RunAsync` (`src_mcp/runners/Consultation/ConsultantTurn.cs:81`)
continues a launch that exited cleanly and said nothing ONCE, in the same conversation, with the text the adapter offers
(`AntigravityConsultant.FollowUp`, `AntigravityConsultant.cs:71` → `AntigravityFollowUps.NoCommands`).

A question row never reaches it. `QuestionRowLaunch.TurnsAsync` (`QuestionRowLaunch.cs:73`) launches a CLI row with
`executor.LaunchOnceAsync(...)` (`:81`) exactly once, and `Unanswered` (`:107-111`) turns the empty answer into
"exited cleanly but answered nothing" — a sentence that also hides WHY (the denied command the stream reported).

## 3. What changes

1. **A CLI row's turn is a `ConsultantTurn`.** In `TurnsAsync`, a runtime that is an `IConsultantRuntime` (every CLI
   adapter, and the api row too) launches through `ConsultantTurn.RunAsync` instead of `LaunchOnceAsync`. One road, not
   a second copy of the one-or-two rule: the conditions (answered NOTHING, the adapter OFFERS a follow-up, time is left,
   the tree is clean, time is still left), the "never a third launch" rule and the lesser-timeout rule all come with it.
   An adapter that offers no follow-up (codex, claude, local, api) makes exactly one launch, as today.
2. **The tree check is the fan-out's own.** `ConsultantTurn` asks `changesSoFar` before a follow-up. The fan-out already
   snapshots every watched disk root before any launch (`QuestionFanOut.cs:96`) and compares after (`BreachAsync`,
   `:348`); the comparison is extracted into one method that returns the changes, used by `BreachAsync` and handed to
   the row as `RowLaunchInput.ChangesSoFar`. A row whose roots are not watched (no git checkout) gets the empty answer,
   which is what the invariant already says about them (`NotWatched`).
3. **Billed once per turn, and never lost.** antigravity reports usage CUMULATIVELY across a conversation
   (`AntigravityConsultant.UsageIsCumulative`), so recording each launch would count turn 1 twice. The row records ONE
   ledger line per turn with `ConsultantTurn.UsageOf(runtime, landed)` (`ConsultationUsage.OfTwoLaunches`), as
   `ConsultantCheck` does — computed over the launches `ConsultantTurn`'s `landed` callback has handed back, and written
   in a `finally`, so a second launch that throws (the caller's cancellation, the row's backstop deadline) cannot drop
   the first launch's usage (plan round, gemini). An api row's follow-up turns stay one line each.
4. **The reason names the cause — and only what happened.** When the final launch still answered nothing:
   - a follow-up RAN: the adapter's own classification (`IConsultantRuntime.SilentFailure` → `ConsultFailure.What`); for
     agy "answered nothing: it reached for a shell command, which headless mode denies, and it did not answer even when
     told the command would not come";
   - NO follow-up ran: that same classification would claim a follow-up that never happened (plan round, gemini), so
     the reason is the first launch's own (`ConsultFailure.Empty.What`, with what the CLI said on stderr), plus why no
     follow-up ran — a watched root changed (the changes named), or no time was left.
   The `Cure` sentences are NOT used — they point at the stuck consultant's tab.

**Not in scope.** Adding agy's `Toolbox` sentence to the question prompts (it might avoid the denial in turn 1, but it
is a prompt change the measurement below did not test); an allow-rule in the person's agy settings (refused for the
stuck consultant: it is the person's file, and `RESULTS_agy_allow_rule.md` found the prefix rule works in WSL only).

## 4. Measured before building (2026-10-07)

A harness with the planner's flags, turn 1 as the question, turn 2 the shipped `AntigravityFollowUps.NoCommands` on
`--conversation <id>`. **Windows, agy 1.3.1, `gemini-3.8-flash-low`, 3 of 3:** turn 1 empty every time (10–12 s,
`denied: ["command"]`); turn 2 answered every time (11–67 s, 468–730 characters, only `view_file`, no second denial).
The answers are weak — plan mode has no directory listing, so the model names a command for the caller instead of
searching — but they are answers the caller can use, not an empty failure. Recorded in
[RESULTS_agy_question_row_follow_up.md](RESULTS_agy_question_row_follow_up.md) — 6 of 6 by the time the repository's
probe had run its own three. WSL could not be measured
past turn 1: agy there asked to sign in again ("Waiting for authentication") partway through the session, which is the
person's to do.

## 5. Build order

Three stories (the gate's split order; the split was done on Opus, not Fable — Fable's monthly spend limit was hit on
2026-10-02): **S1** the fake CLI knows agy and the three RED tests (steps 1–3); **S2** the turn, the tree check, the
billing and the reason (step 4); **S3** the measurement record, the docs and the whole suite (step 5). One code round
over the whole branch.

1. **RED.** `QuestionRowOnAgyScenarioTests`: the question path (`AskConsultantsAsync`) with one antigravity
   `question-disk` row on the fake CLI — first launch prints the real denied stream
   (`fixtures/antigravity/consult-denied.ndjson` + its stderr), a follow-up (told apart by
   `AntigravityFollowUps.StaysDenied` on stdin) prints the real answering stream (`consult-advice.ndjson`). Assert the
   row is `answered` with that advice, exactly two launches, the second carrying `--conversation <id>`, ONE ledger line
   billed at the larger cumulative report. Before that, `tests_fakecli` learns agy's argv (`--print=` first) as a vendor
   shape, so a minimal-environment child reads its steering file. Watch it fail with today's reason.
2. **RED.** The same row whose follow-up also answers nothing: `failed`, two launches (never three), the reason is the
   command-denied sentence, and ONE ledger line.
3. **RED.** A follow-up is NOT run when a watched root changed after turn 1 (`ChangesSoFar` reports a change): one
   launch, and a reason that says the tree changed rather than claiming a follow-up.
4. **GREEN.** §3, items 1–4.
5. Whole C# suite; docs (§7).

## 6. Test plan

The three scenario tests above (fake CLI as a real child, minimal environment), plus the existing
`QuestionConsultScenarioTests`, `QuestionFanOutTests`, `ConsultantTurnTests` and `ConsultAntigravityDenialScenarioTests`
unchanged and green. A live `ask_consultants` on Windows with an antigravity `question-disk` row after the build,
recorded in the RESULTS file.

## 7. Definition of Done

- [x] Measured live before the build, recorded in `research/RESULTS_agy_question_row_follow_up.md` (Windows; WSL is
      the open tail).
- [x] RED observed for each of the three tests with the real symptom, then GREEN.
- [x] One road: a question row's CLI turn is `ConsultantTurn`; no second copy of the follow-up rule.
- [x] One ledger line per CLI turn; antigravity's cumulative usage not counted twice.
- [x] `module_server.md` (the question consultant), `module_runners.md` (`ConsultantTurn` now serves question rows
      too) and `module_tests.md` (the new scenario flow, plan round) updated; promoted in the change that ships it, so it
      never had a `todo/README.md` row — it has a `research/README.md` one.
- [x] Whole C# suite green (`CoaiMcp.Tests`, 2026-10-07 on the final code after the rebase onto #688 and the code
      round's and the PR review's fixes: 8 090 total, 0 failed, 8 skipped), with the server, bugz and bench suites that reference `src_mcp`.
