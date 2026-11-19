# PLAN — the skip-list Add reads back what it wrote

> Status: **plan only, nothing implemented yet, 2026-11-18.** Scope: `src_vs_code` only — `src/skipShell.ts`
> (`addIds`, the read-back after the write) and `src/test/host/skipShellScenario.ts` (S3-H1). Follow-up of
> [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md), outside that plan:
> making the read-back wait was a change to its E3 text ("`inspect()` once more"), so extension 0.88.0 shipped
> without it and says so in its changelog (consult 593e4bab, both consultants).

## The symptom

When the person presses **Add** on the skip-list notice, `addIds` writes the user setting
`terminal.integrated.commandsToSkipShell` with one `update()` and then calls `inspect()` once
(`src_vs_code/src/skipShell.ts`, `if (!userListHolds(seams.inspect(), ids))`). When that one read does not show the ids,
the person is told "… were written to terminal.integrated.commandsToSkipShell, but the setting does not show them when
it is read back", and the answer is NOT remembered — so the notice can come back.

In CI the host scenario S3-H1 (`src_vs_code/src/test/host/skipShellScenario.ts:55`) fails with
"the answer was never remembered as added for both ids, so the write was not read back (waited 15s)":

| Head | Run | Result |
|---|---|---|
| 2b7847fe, c72f49c7 (E3 branch) | — | passed |
| 550ec09d (#1019 after merging main) | 44157675340, attempt 1 and its re-run | failed twice |
| b7173359 (main, #1019 merged) | — | passed |
| ec52e27f (release PR #1013) | 44350882163 | failed, passed on re-run |

The code under test was the same in every row. The ids WERE written each time (the scenario reads the setting after);
only the memory was missing.

Reported by the session that shipped extension 0.89.0 (2026-11-18): the scenario also failed on #1018 (a66e7420,
passed on re-run) and twice in a row on release PR #1022 (13c93522). Attempt 1 failed with the same "never remembered
as added … waited 15s". Attempt 2 failed at a LATER assertion: "the offer came back for ids the person had
already answered … 2 !== 1". S3-H1 reaches it only after the answer WAS remembered as added (so that run's read-back
passed) and the ids were removed again; the memory then did not stop a second offer. That may be a different cause
(for example the change listener's check and the explicit check racing over the memory), so step 1 measures it
separately and this plan does not assume it shares the read-back's cause. Over the last 25 CI runs, 7 first attempts failed, including on 5b9ab66c, 838fdf8d,
ec52e27f and 550ec09d. Main with that release's E2 (a5855ec1, 97372ed6) passed.

## What is not known yet

The likely cause: `WorkspaceConfiguration.update()` resolves before `inspect()` shows the new value — the settings file
is written, and the configuration model is refreshed on the file watcher's event, a moment later. Not yet shown: whether
a fresh `getConfiguration()` (instead of the one held by the seam) sees the value at once, and whether
`onDidChangeConfiguration` for the key always fires after the write.

## Build order

1. **Measure first**, in the host scenario on a branch: after `update()` resolves, log what (a) the held configuration's
   `inspect()`, (b) a fresh `getConfiguration('terminal.integrated').inspect()` return, and when the change event
   arrives, over 20 CI runs. In the same runs, log every check the change listener and the explicit `door.check()` start
   after the ids are removed, and what the memory held when each decided (the second failure shape above). Record both here.
2. **Choose from the data**, one of:
   - a fresh `getConfiguration()` always sees the write → read back through a fresh one;
   - only the change event is reliable → read back on the event, with a stated limit (for example 2 s), and say
     "not read back" only when the limit passes;
   - neither → a bounded retry of `inspect()` (a stated count and pause), the limit in the sentence.
3. Red-green-red: a unit test over the seams where the first read-back shows the old value and the second the new
   one — red today (the answer is not remembered), green after.

## Test plan

- The unit test of step 3, with its first RED recorded.
- S3-H1 green in 20 consecutive runs of `extension · a real editor` (the 25 runs reported above: 7 first-attempt failures).
- The "not read back" sentence still appears when the setting truly does not hold the ids (the existing unit test
  stays green).

## Definition of Done

- [ ] Step 1's measurement recorded here.
- [ ] The chosen read-back merged, with its reason from the measurement, through its own plan gate.
- [ ] S3-H1 green in 20 consecutive CI runs; the count recorded here.
- [ ] `research/module_extension.md` (the skip list section) and the help say what the read-back waits for.
- [ ] The changelog entry of the release says the false warning is fixed.
