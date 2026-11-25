# A press re-reads the consent before each ssh spawn

> Status: TODO, not planned. Found 2026-11-25 by the review of PR #1047 (CodeRabbit thread T9, accepted by the judge as a
> plan change, not a fix in E1).
> **Taken up, 2026-11-25, not built:** the revision for epoch 3 of
> [the accounts plan](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md) takes it into its § 3 (story
> E2.S1): the session road asks the one door before EVERY spawn of a press, and a later spawn never asks a person.
> That revision is not approved yet. This file is closed by the stories document of E2; until then it stays the
> record of the finding.

## The finding

`remoteRoad` (`remoteRead.ts`) reads the consent once per press, before the first call. Every later spawn of the same
press — the titles fallback, the tail fallback, a picked call — goes through `readOnce`, which checks only that the
terminal is still open and still has the same name (`stillThere`). A press makes at most `CALLS_MAX` = 3 spawns. If a
“Not this host” is saved from another window of the side while the first spawn is in flight, the next spawns still go
out: the refused host can receive up to two more read commands.

## Why it is a plan change, not a fix

The S1 spec, § S1.6, ends: *"Nothing else of the road changes: the name re-check, the …"*. The road reads the consent
once per press by decision, and the A1 tests pin that. A re-read before each spawn changes what a press promises, and it
has an open question the spec does not answer: the first call prompts the person when the answer is `ask`; a fallback
spawn must not prompt again without a stated rule (stop with the refusal of the first path, or ask once more).

## What a fix needs

- A decision for `ask` in the middle of a press (recommended: a fallback treats anything but `yes` as a stop and says
  so; it never prompts).
- `readOnce` re-reads the consent (the keeper's door) before `stillThere` and before `seams.run`, and returns the
  refusal the first path gives for that answer.
- Tests: a refusal saved during the first spawn → no second spawn; consent still `yes` → the fallbacks still run
  (S3-T13's companions).
- The entry in the plan's ledger, because it edits a decided sentence of S1 § S1.6.
