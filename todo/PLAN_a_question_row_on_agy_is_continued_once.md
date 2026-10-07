# PLAN — an antigravity question row whose shell command was denied is continued once, as a consultation is

> Status: **plan only, nothing implemented yet.** Scope: `src_mcp/src/Server/QuestionConsult/QuestionRowLaunch.cs`
> (the CLI turn), `src_mcp/src/Server/QuestionConsult/QuestionFanOut.cs` (hands the row its tree check),
> `src_mcp/tests_fakecli/Program.cs` (recognises agy's argv), one scenario test.
>
> Related docs: [RESULTS_agy_consult_follow_up.md](../research/RESULTS_agy_consult_follow_up.md) (the follow-up text,
> measured 6 of 6 for the stuck consultant),
> [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (E1.3 built
> `ConsultantTurn` for the stuck consultant), [module_server.md](../research/module_server.md),
> [module_runners.md](../research/module_runners.md).

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
3. **Billed once per turn.** antigravity reports usage CUMULATIVELY across a conversation
   (`AntigravityConsultant.UsageIsCumulative`), so recording each launch would count turn 1 twice. The row records ONE
   ledger line per turn with `ConsultantTurnResult.TurnUsage` (`ConsultationUsage.OfTwoLaunches`), as `ConsultantCheck`
   does. An api row's follow-up turns stay one line each (one launch per turn, unchanged).
4. **The reason names the cause.** When the final launch still answered nothing, the row's reason is the adapter's own
   classification (`IConsultantRuntime.SilentFailure` → `ConsultFailure.What(vendor)`): for agy, "answered nothing: it
   reached for a shell command, which headless mode denies, and it did not answer even when told the command would not
   come". The `Cure` sentences are NOT used — they point at the stuck consultant's tab.

**Not in scope.** Adding agy's `Toolbox` sentence to the question prompts (it might avoid the denial in turn 1, but it
is a prompt change the measurement below did not test); an allow-rule in the person's agy settings (refused for the
stuck consultant: it is the person's file, and `RESULTS_agy_allow_rule.md` found the prefix rule works in WSL only).

## 4. Measured before building (2026-10-07)

`scratchpad` harness, the planner's flags, turn 1 as the question, turn 2 the shipped `AntigravityFollowUps.NoCommands`
on `--conversation <id>`. Recorded in [RESULTS_agy_question_row_follow_up.md](../research/RESULTS_agy_question_row_follow_up.md).
WSL could not be measured past turn 1: agy there asked to sign in again ("Waiting for authentication") partway through
the session, which is the person's to do.

## 5. Build order

1. **RED.** `QuestionRowOnAgyScenarioTests`: the question path (`AskConsultantsAsync`) with one antigravity
   `question-disk` row on the fake CLI — first launch prints the real denied stream
   (`fixtures/antigravity/consult-denied.ndjson` + its stderr), a follow-up (told apart by
   `AntigravityFollowUps.StaysDenied` on stdin) prints the real answering stream (`consult-advice.ndjson`). Assert the
   row is `answered` with that advice, exactly two launches, the second carrying `--conversation <id>`, ONE ledger line
   billed at the larger cumulative report. Before that, `tests_fakecli` learns agy's argv (`--print=` first) as a vendor
   shape, so a minimal-environment child reads its steering file. Watch it fail with today's reason.
2. **RED.** The same row whose follow-up also answers nothing: `failed`, two launches (never three), the reason is the
   command-denied sentence.
3. **RED.** A follow-up is NOT run when a watched root changed after turn 1 (`ChangesSoFar` reports a change): one
   launch.
4. **GREEN.** §3, items 1–4.
5. Whole C# suite; docs (§7).

## 6. Test plan

The three scenario tests above (fake CLI as a real child, minimal environment), plus the existing
`QuestionConsultScenarioTests`, `QuestionFanOutTests`, `ConsultantTurnTests` and `ConsultAntigravityDenialScenarioTests`
unchanged and green. A live `ask_consultants` on Windows with an antigravity `question-disk` row after the build,
recorded in the RESULTS file.

## 7. Definition of Done

- [ ] Measured live before the build, recorded in `research/RESULTS_agy_question_row_follow_up.md`.
- [ ] RED observed for each of the three tests with the real symptom, then GREEN.
- [ ] One road: a question row's CLI turn is `ConsultantTurn`; no second copy of the follow-up rule.
- [ ] One ledger line per CLI turn; antigravity's cumulative usage not counted twice.
- [ ] `module_server.md` (the question consultant) and `module_runners.md` (`ConsultantTurn` now serves question rows
      too) updated; `todo/README.md` row added, and the plan promoted when it ships.
- [ ] Whole C# suite green.
