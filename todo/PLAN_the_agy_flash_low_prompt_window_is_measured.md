# PLAN — measure where `chat-preset-mtwxbymp-4` stops reading a plan prompt, before its prompt cap is raised

> Status: **plan only, nothing measured yet, 2026-10-17 — a follow-up measurement; nothing waits for it.** Scope: no product
> code. One RESULTS file, `research/RESULTS_agy_flash_low_prompt_window.md`, and — only if the owner asks for it — a new
> `maxPromptKib` on one row of the owner's own settings (a setting on that machine, never a product default).
>
> Extracted from S6 of [PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md](../research/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md)
> when S1–S5 shipped (C1 of that plan: "150 KiB now, as a provisional operator value; S6 before it is raised").
> Related: [module_server.md](../research/module_server.md) § *A row's prompt cap*.

## 1. Goal

The prompt cap shipped as an operator's number with no product default (D1, D2, C2 of the parent plan). The one row seen
to lose the end of a long plan prompt — `chat-preset-mtwxbymp-4`, runtime `antigravity`, model `gemini-3.8-flash-low` —
answered prompts of 107–157 KB and returned only `PLAN_INCOMPLETE <id>` on 212,310 and 234,414 bytes (the parent's § 1).
The owner's provisional value is 150 KiB (153,600 bytes), a margin under the largest prompt seen to pass. Nobody knows
where between 157 KB and 212 KB the row really stops. This plan measures that, so a cap on this row can be a measured
number rather than a margin.

Nobody is hurt while this is open: a cap that is too low only leaves this one reviewer out of a large round, with a
sentence that names the cure (`RosterBuilder.OverTheCap`, `src_mcp/src/Server/Rounds/RosterBuilder.cs:427-440`), and the
`PLAN_INCOMPLETE` fold stays the backstop for a cap that is too high (`src_mcp/src/Server/Rounds/PlanIncomplete.cs:46-47`).

## 2. Measurement (a scout's brief)

**Question.** For this row, at which composed first-prompt size (UTF-8 bytes, the unit the cap compares —
`src_mcp/core/Catalog/PromptCap.cs`) does a plan reviewer stop seeing `END OF PLAN <id>`?

**Method.**

1. Real plan prompts at 160, 170, 180, 190, 200 and 210 KB (six steps), made by padding one real committed plan with more
   real plan text — never random bytes, which a model reads differently.
2. **Three runs per step**, so one odd answer does not decide a step (18 runs, about 17 s each when the reviewer fails fast,
   longer when it answers).
3. Each run is an ordinary plan round with ONLY this row enabled and no cap set on it, on a throw-away branch whose name
   starts with `testsToDelete-`, so its sessions and rounds are recognisable and can be removed.
4. Per run record: the prompt bytes the round audit logs for the reviewer (`src_mcp/src/Server/RoundAudit.cs`), the
   reviewer's `tokensIn`, and whether it answered or returned only `PLAN_INCOMPLETE` (after S3 the ledger line says
   `unparseable: the plan did not reach this reviewer whole …` for the second case).
5. The `agy` CLI version and the model id of every run are written beside the numbers: a different route of the same model
   name is a different measurement.

**Decision rule, written before the runs.** The row's measured limit is the largest step where all three runs answered.
A step with a mixed result (1 or 2 of 3) is reported as mixed, and the limit is the step below it. The recommendation to
the owner is that limit minus one step (10 KB), as KiB, rounded down.

**Cost.** Each run is a real, billed agy call. 18 runs; the owner confirms before the first one.

## 3. Build order

1. Write the six prompts and check their byte sizes from the round audit of a dry run with a different, cheap row.
2. Run the 18 rounds, one step at a time, smallest first. Stop early only if a step below 160 KB fails (then the row's
   limit is under the current provisional value, and the owner is told at once).
3. Write `research/RESULTS_agy_flash_low_prompt_window.md`: the table, the versions, the decision rule, the result.
4. Ask the owner whether to change `maxPromptKib` on the row. Do not change it unasked.
5. Remove the `testsToDelete-` branch's sessions and rounds.

## 4. Test plan

No product code changes, so no new tests. The measurement itself is checked by its own rule: every number in the RESULTS
file comes from a round audit line or a ledger line of a named run, and a reader can find each run by its round key.

## Definition of Done

- [ ] 18 runs (6 steps × 3), each with prompt bytes, `tokensIn`, answered or `PLAN_INCOMPLETE`, the CLI version and the model.
- [ ] `research/RESULTS_agy_flash_low_prompt_window.md` written, with the decision rule stated before the table.
- [ ] The owner was given the measured number and decided whether to change the row's cap.
- [ ] The `testsToDelete-` sessions and rounds were removed.
- [ ] This plan promoted to `research/` with its status `IMPLEMENTED <date>`.
