# The `claude-usage` mode stays under the runner's 30 s ceiling

> Status: TODO, not planned. Found 2026-11-25 by the review of PR #1047 (CodeRabbit thread T46, accepted by the judge).
> It must be settled BEFORE `USAGE_POLICY` is turned on; E1 ships the policy `off`, so no run uses this mode today.
> **Taken up, 2026-11-25, not built:** the revision for epoch 3 of
> [the accounts plan](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md) decides it in its § 4, as part
> of revision 2 of the script (story E3.S2): the mode `claude-usage` reads Claude only, each mode's worst path is
> counted with its KILL seconds, and a test holds it at least 2 s under the ceiling. The owner approved that revision as
> epoch 3 on 2026-11-26. This file is closed by the stories document of E3; until then it stays the record of the finding.

## The finding

`remoteAccountsScript.test.ts` checks the sum of the readers' limits (24 s) and the sum of the vendor limits (26 s)
separately. Neither counts the grace second of `timeout -k 1`: a command that ignores TERM lives one second past its
limit. In the `claude-usage` mode the version stage and the agy stage can each run one second past their reader limit,
so one sequential path is 24 + 2 + 5 = 31 s, and the runner's ceiling (`ACCOUNTS_LIMITS.ms`, 30 s) kills the call before
`COAI-ACCOUNTS-END` is written. That host's round is lost until the next one, 10 minutes later.

## Why it is a plan change

The time table of S2 (§ A2.3) is a decided table (24 s of readers, 5 s of ssh connect, a 30 s ceiling); it does not
count the KILL second. Changing a limit or the ceiling changes the table and the script's text, whose sha256 is pinned.

## What a fix needs

- Either count the KILL second in the table, or prove that the stages cannot overlap that way, with a run that has a
  command ignore TERM. Shortening one reader limit by one second gives exactly 30 s, which fails the runner's strict
  `<` check and leaves no margin: the fix must bring the worst path at least 2 s under the ceiling (so, shorten the
  readers by 2 s or more in total), or raise the runner's ceiling with a margin (33 s or more for a 31 s path). Which of the two is chosen is the plan change's decision, not the coder's.
- The test sums the limits PLUS one KILL second for each stage, in each mode, and asserts the total is under the
  ceiling.
- The script's sha256 is repinned if the script changes; the spec's table follows.
