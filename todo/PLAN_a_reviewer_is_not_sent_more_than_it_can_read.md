# PLAN — A reviewer is not sent more than it can read, and the ledger says when it did not read

> Status: **plan only, nothing implemented yet, 2026-10-16.** Scope: one optional field on a vendor row
> (`maxPromptKib`), a check in `RosterBuilder.AddReviewer` before the fan-out, ONE place where the plan-stage
> `PLAN_INCOMPLETE` fold settles a reviewer's outcome (inside the scheduler, so the ledger, the live view, the audit
> line and the local stand-down all read the same outcome), the model card's one input, and the docs.
>
> Related: [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md) (D12, D13 —
> this plan amends D12's "accepted mismatch", see § 4), [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [RESULTS_reviewer_input_sizes.md](../research/RESULTS_reviewer_input_sizes.md).

## 1. The symptom (measured by a scout, 2026-10-16, `coai-mcp` 0.58.0)

One vendor row, `chat-preset-mtwxbymp-4` (runtime `antigravity`, model `gemini-3.8-flash-low`), was a plan reviewer in
several rounds on the same day:

| Prompt sent (UTF-8 bytes) | What the reviewer did | `tokensIn` it reported |
|---:|---|---:|
| 107–157 KB | answered normally | — |
| 212,310 | ONE finding, `PLAN_INCOMPLETE <id>`, in about 17 s | 61.9k |
| 234,414 | ONE finding, `PLAN_INCOMPLETE <id>`, in about 17 s | 63.5k |

For the same two prompts, codex counted 12–18 % more input tokens. The two counts that stop near 62–64k for prompts
22 KB apart agree with a reviewer that reads a fixed window and loses the tail. The sentinel did its job: the
reviewer could not see `END OF PLAN <id>` and said so. **This is a detector working, not a proof of the window size.**

What went wrong around it, each verified on `origin/main` (`ce9cd298`):

1. **Nothing stops the prompt before it goes out.** `AntigravityRuntime.Build` puts the whole prompt on stdin
   (`src_mcp/runners/Reviewers/AntigravityRuntime.cs:66`). There is no size cap and no windowing on the reviewer path.
   The only ceiling is on the DOCUMENT, `DocumentRules.MaxBytes` = 256 KiB (`src_mcp/core/Rounds/DocumentRules.cs:31`),
   and a prompt is the document plus the rules and the instruction — so a prompt can pass 256 KiB with a plan under it.
2. **The run is spent before anyone knows.** The reviewer is launched, billed and only then found useless. The caller
   learns it from the round's reply, after the fan-out.
3. **The ledger says `ok`.** The progress callback writes the ledger line as each reviewer finishes
   (`src_mcp/src/Server/Rounds/RoundEngine.cs:416-424`). `PlanIncomplete.Fold` runs later, on `results`
   (`RoundEngine.cs:429`; the fold is `src_mcp/src/Server/Rounds/PlanIncomplete.cs:26-51`). The ledger's outcome word is
   `ok` for any `ReviewerOutcome.Ok` (`src_mcp/runners/Reviewers/LaunchParts.cs:69-70`), so the spending record says the
   reviewer answered. `PlanIncomplete.cs:12-13` and the parent plan's D12 record this as an "accepted mismatch".
4. **A second defect, found while verifying 3:** the local reviewer's stand-down (issue #485) reads the SAME raw
   outcome. `BoundedScheduler.Recording` hands every finished cloud outcome to `StandDown.Record`
   (`src_mcp/runners/Reviewers/BoundedScheduler.cs:390-400`), and `StandDown` counts an `Ok` answer's findings as
   remarks (`src_mcp/runners/Reviewers/StandDown.cs:30-44`, quiet at `MostRemarks = 1`, `:19`). A cloud reviewer that
   answered only the token is one remark, so with `COAI_STOP_LOCAL_WHEN_QUIET` on (`RoundEngine.cs:427`) the cloud can
   look "quiet" and the local reviewer is not started — the one reviewer that could still have read the plan.
5. **The coverage check cannot see it.** `InputCoverage.Of` returns nothing for any reviewer that is not on a local
   engine (`src_mcp/runners/Reviewers/InputCoverage.cs:8`); where it does run, it flags only `tokensIn` under half of
   `bytes / 4` (`InputCoverage.cs:18-22`). 61.9k is far above half of 212,310 / 4 ≈ 53k.

Out of scope, and said here so it is not lost: **Kimi is cancelled at the 10-minute round limit on prompts of ANY size**
(9 KB and 12 KB too, same scout). That is a slowness or a hang in that runtime, not a size issue; a cap would not touch
it. It needs its own measurement, and its home is the open
[PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md): a diagnosis story under its S4, whose completion
criterion is "a Kimi row reviews plans and code" (decision C4, § 10).

## 2. The goal — what must be true when this is done

1. **Before a round fans out,** a reviewer whose first prompt is larger than its row's stated cap is not launched.
   When lenses are dealt, a lens goes only to a reviewer whose cap fits it (D3). The round's reply names each row
   left out, says the prompt size and the cap, and names the cure. A caller can read every row's cap from
   `providers` before it calls. This is an admission check on the FIRST prompt, not a guarantee for every turn (D4).
2. **No run is wasted on it:** nothing is launched, nothing is billed, no ledger line is written (nothing was spent).
   If EVERY reviewer is over its cap, the stage's existing "nobody can serve this" policy applies before any reviewer
   starts: the plan and code stages refuse before a round is recorded (`NoReviewerRefusal`, `RoundEngine.cs:376-386`),
   the feature stage records a skip, as it does today for "nobody configured" (`WhenNobody = NobodyPolicy.RecordSkip`,
   `src_mcp/src/Server/Stages/FeatureStage.cs:144`; `RoundEngine.cs:383-385`).
3. **The ledger tells the truth:** a plan reviewer that answered `PLAN_INCOMPLETE` for this round's id is written as
   the failure it is (`unparseable: the plan did not reach this reviewer whole …`), with every token it spent kept.
4. **One outcome everywhere:** the ledger, the live view, the audit line, the stand-down and the round's results read
   the same settled outcome. The "accepted mismatch" of D12 is gone.
5. A row with no cap behaves exactly as today.

## 3. The design decision this plan must meet first

`DocumentRules.cs:26-29` says: checking against each selected model's real context window "is out of scope until model
capabilities are data: a check against a number we are guessing would refuse valid rounds and still miss invalid
ones." The parent plan's D13 repeats it ("No token preflight"). **A per-row cap set by the operator does not contradict
this, for four reasons — each a property the design must keep:**

| The rejected design | This design | Kept by |
|---|---|---|
| The PRODUCT knows each model's window (a table it ships) | The OPERATOR writes one number on one row, after it saw that row fail. The product ships no number and no default. | D1, D4 |
| A guess turns a valid round into a refusal | A cap only takes ONE reviewer out of a round, with a sentence. The round is refused (at the feature stage: skipped) only when every reviewer is out — and then before any reviewer starts, with the cure named. | D3 |
| A guessed unit (tokens, with no tokenizer here — `InputCoverage.cs:3,12`) | Bytes, which this side measures exactly: `ReviewerWork.PromptBytes` (`BoundedScheduler.cs:11-28`, set at `RosterBuilder.cs:398-402`). | D2 |
| A check that can "still miss invalid ones" | It may miss — and the `PLAN_INCOMPLETE` fold stays as the backstop, now recorded truthfully (§ 2 item 3). | S3 |

D13 itself already draws this line: it calls the security lane's `ContextTokens` (`src_mcp/src/Server/SecurityLaneSetting.cs:7`)
"an operator setting for that lane, not a model capability". `maxPromptKib` is the same kind of thing for a reviewer
row, next to the row's own `timeoutMinutes` (`src_mcp/src/Server/SettingsJsonContext.cs:63-64`). The 256 KiB document
ceiling stays as it is, the product's own bound.

## 4. Boundary with the committed-plan plan (named on both sides)

| Item | Built by | The other plan's part |
|---|---|---|
| The sentinel, the instruction, `PlanIncomplete` and its rule (any token for this id folds) | [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md) D12 — shipped in E1 (#837) | none: this plan does not change the rule or the prompt |
| WHERE the fold is applied: once, at the scheduler's terminal point, read by ledger, live view, audit, stand-down and results | **this plan**, S3 | D12's "accepted mismatch" (progress, audit and ledger saw the raw `Ok`) is superseded. D12's "the ledger row is right as it stands (the tokens were spent)" is kept for the TOKENS — only the outcome word changes |
| Any size check | **this plan**, S1–S2 (a per-row operator cap, bytes) | D13 "no token preflight" stands: no model capability, no tokens |
| The `rounds` column, `--log`, the rounds-log line (E2) | the committed-plan plan, E2 | disjoint — this plan adds no column and no `--log` field |

**Order:** independent. S3 here touches `RoundEngine.cs:409-429` and `BoundedScheduler.cs`; the committed-plan E2
branch touches the schema, `RoundsQuery`, `--log` and the extension's rounds log. Whichever merges second rebases.
Nothing else is shared.

## 5. Decisions (each checked against the code)

| # | Decision | Why — and what was checked |
|---|---|---|
| D1 | **New optional row field `maxPromptKib`**: whole KiB, 1 to 4096; anything else (absent, 0, negative, too large, not a number) is unset = no cap. `VendorDto.MaxPromptKib` (`int?`) → `ProviderSettings.MaxPromptKib` (`int`, 0 = unset). | The `timeoutMinutes` precedent end to end: `VendorDto` (`SettingsJsonContext.cs:63-64`) → `ProviderSettings.TimeoutMinutes` (`src_mcp/src/Server/PanelSettings.cs:19-23`) with an in-range-or-unset parse (`PanelSettings.cs:1393-1394`). **Not `maxPromptBytes`:** `CatalogLimits.MaxPromptBytes` (`src_mcp/core/Catalog/CatalogLimits.cs:13`) already means the row's SYSTEM-prompt limit (8 KiB, `PanelService.cs:409-420`); one name with two meanings on the same row is a trap. KiB, because a person types it; the check converts it to bytes exactly (`× 1024`). |
| D2 | **Bytes, not tokens.** No `maxInputTokens`. | This side has no tokenizer (`InputCoverage.cs:3` "never prove", `:12` "no tokenizer"). A token cap needs a bytes-per-token guess, and the measured case shows why that fails: codex and agy differ by 12–18 % on the same prompt. Bytes are exact and are already measured per reviewer (`RosterBuilder.cs:398-402`). |
| D3 | **The check is asked in two places, as `WhyNotCarried` already is** (`src_mcp/src/Server/Rounds/RosterBuilder.cs:598-605`, "one rule, asked in two places"). **(a) Before the deal:** a row whose cap is smaller than its composed first prompt for a dealt item is not a carrier of that item, so `Assemble` deals the lens only among rows that can read it (`RosterBuilder.cs:526-550`, grouping by `Carriers`, `:642-644`). `canCarry` is widened from `(provider, role)` to the dealt item `(provider, role, promptId)`, because the size depends on the item's prompt and the row's own system prompt. **(b) In the leaf, `AddReviewer`, after `ReviewerFor` builds the work** (`RosterBuilder.cs:320-328`): when the row has a cap and `PromptBytes > cap × 1024`, the reviewer is NOT added; `collector.Exclude(provider, role, reason)` (`src_mcp/src/Server/Rounds/RosterAssembly.cs:93-99`) records it instead. (b) is what the non-dealing fan-out uses, and the backstop when no row can carry an item (`Hand` then offers it to everyone, `:580-585`, and each leaf says why). Both read ONE number: the composed prompt's UTF-8 byte count, from one helper that `ReviewerFor` also uses for `PromptBytes` (`:398-402`) — two sizes that must agree are one value. | (a) is the existing convention, not a new idea: dealing first and excluding in the leaf "cost a custom role its whole round" (`RosterBuilder.cs:541-546`), and `TheRoundSaysWhatItCouldNotAskTests.cs:61-72` pins the fix over ten seeds. Found by the consultant (C3, § 10) and checked there. (b) is the seam the code already uses for "this (vendor, role) cannot run in THIS round, found while the work is built" (`RoundEngine.cs:397-402`). The exclusion then reaches, with no new member: the reviewer line of the reply through `ReviewerSummaryFactory.From` (`BoundedScheduler.cs:707-722`), the audit's "left out" line (`src_mcp/src/Server/RoundAudit.cs:54`), and — when nobody is left — the stage's nobody policy (goal 2). An excluded reviewer is not in `work`, so it never runs, writes no ledger line and spends no lens (`RoundEngine.cs:63-68`). **Limit, said plainly:** `Exclude` keeps ONE line per (vendor, role) (`RosterAssembly.cs:93-99`; `ExcludedRole` has no lens member, `src_mcp/src/Server/Rounds/ExcludedRole.cs:13`), so when a row holds two lenses of one role the reply names the vendor and the role, not each lens; naming each lens is not in this plan. Not in `PanelService.RowRefusal` (`PanelService.cs:384-386`): that check knows only the row, and this one needs the round's prompt. |
| D4 | **The cap applies at every stage**, to the first launch's prompt. | The check is stage-agnostic by its seam, and a code round with a large diff hits the same window: the code stage alone already lets a 192 KiB diff body through before any prompt overhead (`src_mcp/core/Context/DiffShaper.cs:28`), so a plan-only cap would leave the same exposure open (consultant C5, § 10, checked). The guarantee is therefore called what it is — **admission of the first prompt**, not protection of every turn. The repair launch is smaller than the review (`RosterBuilder.cs:387` composes it without the checkout material) and a feature follow-up's tail is added later (`:398`); neither is checked — named in § 8. |
| D5 | **The reason names the size, the cap and the cure** (part (e) of the brief, in this narrow form): `its prompt is 212,310 bytes and this row reads at most 184,320 (maxPromptKib 180) — send the plan in parts (one epic per round), or raise the row's cap`. | A refusal that names its cure is this repository's rule (`DocumentRules.cs:88-94` sends a too-large document to "reviewed in parts"). The split itself stays the caller's: `COAI_SPLIT_PLAN` (`PanelSettings.cs:1012`, read at `src_mcp/src/Server/Cadence/CadenceDesk.cs:450-451`) orders a split only AFTER a plan passed (`RoundEngine.cs:512-515`), so it cannot be the cure for a plan that has not been reviewed yet. |
| D6 | **`--features` lists `maxPromptKib`**, and the extension writes the field only to a binary that lists it. | The `timeoutMinutes` precedent: `src_mcp/src/Server/FeaturesMode.cs:26`, `src_vs_code/src/binaryFeatures.ts:23-24`, `src_vs_code/src/vendorsWire.ts:174-176`. An older binary never meets the field, so its rows read as today. |
| D7 | **`providers` shows the cap** as an optional `maxPromptKib` on `ProviderStatus`, absent when unset. | The `Fast` precedent: an init property with `JsonIgnore(WhenWritingNull)` (`src_mcp/src/Server/ServerJsonContext.cs:33-34`), so every row without a cap reads exactly as before. This is how a caller learns a cap BEFORE it sends a round. |
| D8 | **The fold settles the outcome once, inside the scheduler.** `BoundedScheduler.RunAllAsync` (`BoundedScheduler.cs:209-214`) gains an optional `Func<ReviewerOutcome, ReviewerOutcome>? settle`, applied in `LaunchAsync` right after `loop.RunAsync` (`:453`) — BEFORE the status word is chosen and the progress is reported (`:454`) and before the outcome is returned (`:455`). `RoundEngine` passes `o => PlanIncomplete.Settle(stage, title, o)`, the title computed once before the fan-out. The call at `RoundEngine.cs:429` is then removed. | `LaunchAsync` is the one terminal point of both lanes (`BoundedScheduler.cs:432-461`, "the same in both lanes"). Folding there makes ONE outcome for the ledger (`RoundEngine.cs:416-424`), the live view (`:411`), the audit (`:412`), the stand-down (`BoundedScheduler.cs:390-400`) and `results`. `PlanIncomplete.Settle` is the existing per-outcome `Folded` (`PlanIncomplete.cs:41-51`) made internal; `Fold` keeps its tests through it. Not a second fold "for the ledger only": two places that must agree are one place (`reuse-first`). |
| D9 | **A folded reviewer's ledger line keeps its tokens.** | Already true of the fold: `Failed` carries `Usage`, `EarlierTurns`, `EarlierLaunches`, `Launches`, warning and CLI version (`PlanIncomplete.cs:64-72`), and the ledger reads the usage from the outcome whatever it is (`src_mcp/runners/Reviewers/UsageLedger.cs:354-377`). Only the outcome word of the turn's last line changes, from `ok` to the failure's whole words (`LaunchParts.cs:69-70`). |
| D10 | **(d) is rejected: no cross-reviewer comparison in `InputCoverage`.** | (1) It runs AFTER the launch (`src_mcp/runners/Reviewers/TurnLoop.cs:59`), so it cannot save the run, which is goal 2. (2) It is scoped to local-engine reviewers (`InputCoverage.cs:8`); widening it is a change to the security lane's notes, not to this path. (3) For the measured case the sentinel already fires, and after S3 it is recorded truthfully. (4) There is no measured spread of `tokensIn` between vendors on PLAN prompts to set a threshold from: the one same-round sample is a code round, codex 42,096 against antigravity 42,546 (`research/RESULTS_reviewer_input_sizes.md:33-38`) — one sample is not a threshold. Re-open if a truncation is found that the sentinel misses. |

## 6. Stories and build order (one pull request, one commit per story, each green on its own)

Build order: **S3 → S1 → S2 → S4 → S5.** S3 first: it is a defect fix with no dependency, and it makes the backstop
honest before the cap starts to hide the cases it catches. S6 is optional and can run at any time before the owner
sets a number.

- **S3 — One settled outcome for a plan reviewer** (D8, D9). `settle` on `RunAllAsync`, applied in `LaunchAsync`;
  `PlanIncomplete.Settle`; `RoundEngine` passes it and drops the `:429` call; the remark at `PlanIncomplete.cs:10-13`
  rewritten (no mismatch left). Fixes symptoms 3 and 4.
- **S1 — The row carries a cap** (D1, D6, D7). `VendorDto.MaxPromptKib`, `ProviderSettings.MaxPromptKib` and its
  parse; `FeaturesMode` lists `maxPromptKib`; `ProviderStatus.MaxPromptKib`.
- **S2 — An over-cap reviewer is left out before the fan-out** (D3, D4, D5). The one prompt-size helper; the carrier
  test before the deal (`canCarry` widened to the dealt item); the check and the sentence in `RosterBuilder.AddReviewer`.
  Nothing else changes, because the exclusion path already reaches the reply, the audit and each stage's nobody policy.
- **S4 — The model card sets it** (D6). `Vendor.maxPromptKib` (`src_vs_code/src/vendors.ts:158` beside
  `timeoutMinutes`), `catalogFields.ts` (`:24`, `:42`, `:88` precedent), `vendorsWire.ts` gated by `binaryFeatures.ts`,
  one number input in `modelCardFields.ts` beside the timeout (`:148-149`), and the "set on this row" line in
  `modelCard.ts:155,171`. A setting, not an action: no new button.
- **S5 — Docs.** `research/module_server.md` § the plan-incomplete fold (`:1703-1713`: the fold now runs in the
  scheduler, and the ledger line reads the failure) and § *A CLI row's own timeout* (`:7023-7028`) gains its sibling
  § *A row's prompt cap*; `research/module_extension.md` for the card field; the parent plan's D12 amendment line.
- **S6 (optional) — Measure this row's boundary.** Real plan prompts of 160, 170, … 210 KB to
  `chat-preset-mtwxbymp-4`, three runs each (so one odd answer does not decide a step), answered vs `PLAN_INCOMPLETE`, written to
  `research/RESULTS_agy_flash_low_prompt_window.md`. About 18 runs × ~17 s. It gives the owner a measured number for
  the row instead of a guess (C1: run it before the 150 KiB is raised). Not needed for S1–S5 to ship.

## 7. Test plan

Run as the MTP executable, never `dotnet test`
(`node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe`), and
`npm test` in `src_vs_code` for S4. Fake-CLI rounds use the `FakeCliRoundTests` harness as
`src_mcp/tests/EveryPlanReviewerSeesWhereThePlanEndsTests.cs:60-94` does.

**Red first, per defect (S3).** Each test is written and run against today's code, and the red text is reported:

| # | Test (named for the guarantee) | Red today, because | Green after |
|---|---|---|---|
| T1 | `AReviewerThatDidNotGetThePlan_IsWrittenToTheLedgerAsAFailure_WithItsTokens` — plan round, one fake reviewer answers only the token, read `usage.jsonl` | its line's `outcome` is `ok` | `outcome` starts `unparseable: the plan did not reach this reviewer whole`; `tokensIn`/`tokensOut` equal what the fake reported |
| T2 | `ACloudReviewerThatDidNotGetThePlan_IsNotAQuietCloud` — `StopLocalWhenQuiet` on, one cloud row answers only the token, one engine row | the engine row is stood down ("the cloud reviewers found 1 remark(s)") | the engine row is started |
| T3 | `TheLiveViewAndTheAudit_ShowTheFoldedReviewerAsFailed` | the progress status is `done` | `failed`, the same reason as the reply |

T2 is written at the round level when the fake-CLI harness can host an engine row; otherwise in
`src_mcp/tests/TheLocalReviewerStandsDownTests.cs` against the `settle` parameter landed FIRST as an unused no-op, so
the test goes red on behaviour, never on a compile error.

**Feature tests (S1, S2, S4):**

| # | Scenario | Asserts |
|---|---|---|
| T4 | parse: `maxPromptKib` 180, 0, -1, 4097, `"180"`, absent | 180; the rest unset (0) — the `src_mcp/tests/ARowsOwnTimeoutTests.cs:25` shape |
| T5 | `--features` | lists `maxPromptKib` (`src_mcp/tests/ARowsOwnTimeoutTests.cs:68` shape) |
| T6 | `providers` with one capped row and one not | the capped row carries `maxPromptKib: 180`; the other has no such member |
| T7 | deal off; two rows, one capped under the prompt size | the capped row is never launched (no argv recorded); the reply's reviewer line names it with prompt bytes, cap and cure; 1 of 1 answered; no ledger line for it |
| T8 | prompt size exactly at the cap (`PromptBytes == cap × 1024`) | launched (the cap is inclusive) |
| T9 | every row capped under the prompt, plan stage and code stage | refused before any round: no round number used, no session round recorded, the refusal carries the exclusion sentence |
| T9b | every row capped under the prompt, feature stage | a skip is recorded with the exclusion sentence, as for "nobody configured"; no reviewer launched |
| T13 | deal on; two rows, one capped under the prompt; walked over ten seeds (the `TheRoundSaysWhatItCouldNotAskTests.cs:61-72` shape) | every lens is asked of the row that fits, on every seed; the capped row is never launched |
| T14 | deal on; every row capped under the prompt (code stage, so the round is refused) | the refusal names each (vendor, role) with size, cap and cure; no lens silently disappears |
| T10 | no row has a cap | the round's argv and reply are byte-identical to today's |
| T11 | a code round with a capped row | the same exclusion (D4) |
| T12 | extension: the card's input writes `maxPromptKib` to a binary that lists it, and nothing to one that does not | per `vendorsWire` tests; the card page run per `bundledPage.test.ts`, never a source-text assertion |

**Break-its** (mutate production, name the test that must go red, restore, re-run green — reported with the red text):
B1 apply `settle` after the progress report instead of before → T1, T3. B2 remove `settle` from `RoundEngine`'s call →
T1, T2, and the existing `EveryPlanReviewerSeesWhereThePlanEndsTests.cs:281-340` round tests. B3 compare with `>=`
instead of `>` → T8. B4 skip the check when the row has a cap → T7. B5 read `maxPromptKib` as bytes (drop `× 1024`) →
T7, T8. B6 write the field to every binary → T12. B7 drop the size test from the carrier test (keep only the leaf) →
T13.

## 8. Out of scope — named, not silently left

- **Kimi's ten-minute cancellations** (§ 1) — a diagnosis story under S4 of
  [PLAN_vibe_and_kimi_runtimes.md](PLAN_vibe_and_kimi_runtimes.md) (C4); this branch does not edit that plan.
- **The repair launch and feature follow-up turns** are not checked against the cap (D4). A follow-up only appends to
  a prompt that already passed; if a feature reviewer is found over its window on a follow-up, that is its own story.
- **The security lane** builds its own work (`src_mcp/src/Server/SecurityRoster.cs:159`) and has its own bounds
  (`SecurityContext.MaxPromptBytes`, `ContextTokens`); the cap is not applied there.
- **A Team server's own vendor configuration** (`src_server/src/Vendors/VendorConfig.cs`) gets no cap. A `remote` row's
  prompt is composed on THIS side, so the client row's cap already covers it; the server's ledger line is the
  server's own record and keeps today's behaviour.
- **Naming each lost lens.** When no row can carry a lens, the reply names the (vendor, role) that could not take it,
  once per pair (D3's limit), not each lens by id.
- **A shipped default per model** (`shared/feature-availability.json`) — that is "model capabilities as data", which
  `DocumentRules.cs:26-29` defers (C2).

## 9. Growth

None. One integer per vendor row in the settings file. No table, column, directory, cache or process. A capped
reviewer writes NO ledger line (nothing ran), and a folded one writes the same number of lines as today — only the
outcome word changes.

## 10. The open questions — answered by the consultants, checked in the code (2026-10-16)

Q1–Q5 went to `ask_consultants` first (consult `69ffa88c5fde4370aeccad8c15b99f9f`; one row answered, gpt-6-astra
reading this checkout; the second row is switched off). Each answer below was checked against the code before it was
taken; the check is named.

| # | Question | Decision | Why — and what was checked |
|---|---|---|---|
| C1 | Which number for `chat-preset-mtwxbymp-4`? | **150 KiB (153,600 bytes) now, as a provisional operator value; S6 before it is raised.** | 153,600 bytes is under the largest prompt seen to pass (157 KB) and far under the smallest seen to fail (212 KB) (§ 1). The two `tokensIn` counts do not set a portable bytes-per-token ratio (D2), so the number is a margin, not a proven window. Lower it if a failure is seen under it. |
| C2 | Ship NO default cap for any model? | **Yes — no shipped default, no per-model table.** | `DocumentRules.cs:26-29` defers model windows "until model capabilities are data"; `InputCoverage.cs:3` says its heuristic can never prove a whole read; `timeoutMinutes` is the same kind of optional operator value, unset outside its range (`PanelSettings.cs:1393-1394`). One measurement through one runtime is not a limit for every route of that model name. |
| C3 | A capped reviewer that held a dealt lens: leave it unasked, or re-deal? | **Re-deal: the cap is part of the carrier test before the deal (D3 a); only when no row fits is the lens unasked, and the reply says so.** | The plan's first position (leave it unasked) is the defect this code already fixed once: `RosterBuilder.cs:541-546` deals only among carriers because excluding after the deal "cost a custom role its whole round", pinned by `TheRoundSaysWhatItCouldNotAskTests.cs:61-72`. The consultant also found that the reply cannot name a lost lens (`ExcludedRole.cs:13` has no lens; `RosterAssembly.cs:93-99` keeps one line per (vendor, role)) — now stated as D3's limit. |
| C4 | Kimi's ten-minute cancellations: a new plan, or a story? | **A diagnosis story under S4 of `PLAN_vibe_and_kimi_runtimes.md`.** | That S4's own completion criterion is "a Kimi row reviews plans and code" (`todo/PLAN_vibe_and_kimi_runtimes.md:703`); cancellation at 9 KB means S4 is not met. Not called a model hang yet: Kimi launches are serialised (`src_mcp/runners/Kimi/KimiRuntime.cs:74`), so queue, launch and inference time must be timed apart. A separate cross-runtime plan only if the cause is the shared deadline (`src_mcp/src/Server/Rounds/RoundDeadline.cs`). |
| C5 | Every stage, or the plan stage only? | **Every stage, first prompt only (D4).** | The code stage admits a 192 KiB diff body before overhead (`DiffShaper.cs:28`). The consultant's two qualifications were checked and taken: the feature stage records a skip, not a refusal, when nobody is left (`FeatureStage.cs:144`; goal 2, T9b), and the security lane builds its own work outside `AddReviewer` (§ 8). |

**Still open for the owner (the residue):**

- **R1.** Set `maxPromptKib` 150 on row `chat-preset-mtwxbymp-4` when S1–S4 ship — yes or no? (This is a setting on
  your machine. The product does not set it.)
- **R2.** Do you agree with C3: give the lens to a reviewer that can read the prompt, instead of leaving it unasked?
  This changes the plan's first position.
- **R3.** Do you agree with C4: the Kimi diagnosis is a story in the Kimi plan? This branch does not add it there.

## Definition of Done

- [ ] A row may carry `maxPromptKib` (1–4096, else unset); `--features` lists it; `providers` shows it when set.
- [ ] A reviewer whose first prompt is larger than its row's cap is not launched; a dealt lens goes to a row whose cap
      fits; the reply names each row left out with the prompt size, the cap and the cure; no ledger line is written
      for it; all rows over cap → the plan and code stages refuse before any round, the feature stage records a skip.
- [ ] A plan reviewer that answered `PLAN_INCOMPLETE` for this round's id is ONE failed outcome everywhere — ledger
      (with its tokens), live view, audit, stand-down, results; `RoundEngine.cs:429`'s separate fold is gone.
- [ ] The model card sets the cap, and writes it only to a binary that lists the field.
- [ ] T1–T3 observed red with the real symptom, then green; T4–T14 green; B1–B7 each observed red with the named test,
      then green; `CoaiMcp.Tests.exe` and `npm test` green.
- [ ] `module_server.md`, `module_extension.md` updated; the parent plan's D12 records the amendment;
      `node .agents/conventions/tools/plan-lifecycle.mjs` clean; this plan promoted when S1–S5 ship.
