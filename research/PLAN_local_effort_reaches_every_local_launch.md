# PLAN — the local engine's effort reaches every local launch, and a thinking model that runs out says so

> Status: **IMPLEMENTED, 2026-10-13.** Epic 1 merged as PR #797; epic 2 measured — results in
> [RESULTS_security_lane_effort_2026-10-13.md](RESULTS_security_lane_effort_2026-10-13.md): `none` found both planted
> defects from both prompts, every thinking arm found at most one and lost a reviewer to the ceiling, so the shipped
> default stays. Scope: `src_mcp` (consultant and question-consultant launch settings, the `--ask-local` shim),
> `src_vs_code` (the Models card's ✓ Check badge), the explicit security-lane calibration harness, and one
> `research/RESULTS_*.md`. Two epics.
>
> Related docs: [module_server.md](module_server.md) (§ *A local reviewer is told not to think*),
> [module_security_lane.md](module_security_lane.md),
> [RESULTS_security_lane_local_llm_windows.md](RESULTS_security_lane_local_llm_windows.md),
> [PLAN_security_lane_calibration_tail.md](../todo/PLAN_security_lane_calibration_tail.md).

## What shipped differently from this plan

- **The tripwire is the compiler, not a scan.** S1.1's source scan went RED naming both sites and was then replaced in
  the code round: `EffortFor` takes `PanelSettings`, and all three composers go through `RosterBuilder.WithLocalEngine`.
  Checked: the scan missed a named argument (`localEffort: string.Empty`), a call-shaped first argument, and the ceiling.
- **`engine` reads as "none sent".** Found by the code round's correctness review: `CutCure` switched on the raw flag,
  so `COAI_LOCAL_REASONING_EFFORT=engine` (which sends nothing) was said as "reasoning_effort was engine". It now reads
  `ApiDialect.EffortToSend` — RED first, then fixed.
- **Usage before judgement**, from the cadence consultation: a refused cut would otherwise have reported zero tokens.
- **`CutSentence(source, answer, dialect, maxTokens, cure)`** takes the dialect rather than its two fields.
- **No CHANGELOG lines in the feature PRs** — the release pull request writes them, as for every feature PR here.
- **Epic 2 ran the pilot and the decision-point arm only.** No thinking arm finished most of its cells, so the
  procedure's three-repeat branch and the safe-twin arms were not run; the safe twin exists in the harness, unexercised
  on hardware.

## Open tail

- **Decided, nothing to build:** the `medium` round lost both security reviewers to the ceiling and still answered
  `proceed` ("4 of 6 reviewers answered"). The owner's decision, 2026-10-13: intended — a lane whose reviewers all
  failed does not hold the round.
- **Proposed, not done (code round, reuse-first):** the three near-identical `new ReviewerSettings { … }` initialisers
  (reviewer, consultant, question row) could share one base builder; only the local effort and ceiling were unified.

## The symptom (observed 2026-10-13, owner's machine)

The owner asked two questions about the `local` row (Ollama, `Gemma4-26B-A4B-Uncensored_vk128:latest`, a THINKING
model, 131072 context, fully resident in VRAM):

1. **"✓ Check, pressed twice, gave no answer — timing, or the model?"** The card reads `checked: no answer` and
   nothing else. Ollama's own log (`%LOCALAPPDATA%\Ollama\server.log`) holds both checks:

   | finished (local) | prompt | generated | wall |
   |---|---:|---:|---:|
   | 09:33:39 | 1 437 | **8 192** | 2 m 21 s |
   | 09:36:18 | 1 445 | **8 192** | 2 m 8 s |

   Exactly `max_tokens` each time: the model thought until the ceiling and wrote no content. A direct request
   without `reasoning_effort` reproduced it (`content: ""`, all output in `message.reasoning`,
   `finish_reason: "length"`); the same request with `think:false` answered `OK` in 0.02 s of generation. The
   check's budget is 4 minutes (`ConsultantCheck.cs:16`), so it was **not** timing.

2. **"The security lane makes the GPU work and returns nothing — is that normal?"** Every `redteam-*` reviewer of
   today's rounds read ~17 000 prompt tokens in ~6 s and generated **exactly 10 tokens** — a strict empty
   `{"status":"SECURE","findings":[]}` — sent with `reasoning_effort: none`. That is the same 10-token answer
   [RESULTS_security_lane_local_llm_windows.md](RESULTS_security_lane_local_llm_windows.md) §6.1
   recorded on all fifteen preset slices; its §6.2/§6.3 show the same model at `none` DOES report a planted
   tenant bypass (1 511/391) and stays empty on the safe twin. So the plumbing works; what is unknown is whether
   any effort above `none` buys a better security answer on this model at the product's pinned sampling.

## Why — verified in the code at `d6fa4c57`

- **Reviewers are told not to think; consultants and question consultants are not.** `RosterBuilder.cs:417`
  passes the panel's `LocalReasoningEffort` (default `none`, `PanelSettings.cs:401`) into `EffortFor`
  (`RosterBuilder.cs:677-684`). `ConsultantTurnInputs.cs:64` (consult AND ✓ Check — the check reuses it,
  `ConsultantCheck.cs:237`) and `QuestionFanOut.cs:322` pass `string.Empty`, so a `local` row with no effort of its
  own sends no `reasoning_effort` and the engine's default (thinking) applies. `module_server.md` § *A local
  reviewer is told not to think* records why `none` is the default: on Ollama's OpenAI route only `none` returns
  `finish_reason: stop`; `low` still burns the budget. The decision was applied at one of its three sites — the
  shape `common.reuse-first` § *A decision applied at SOME of its sites* names. Neither consultant path sets
  `MaxTokens` either; it falls to the record default 8192 (`ReviewerRuntime.cs:121`), equal to the panel's today
  by coincidence, not by reading it (`PanelSettings.cs:413`, `COAI_LOCAL_MAX_TOKENS`).
- **The local shim does not read how the answer stopped.** `CompletionReader` already reads `finish_reason`
  (`CompletionReader.cs:19`, `ChatAnswer.WasCut`), and the api shim refuses a cut answer with a sentence naming the
  ceiling and the tokens (`AskApiMode.cs:270-280`). `--ask-local` drops it: `LocalAsk.ReadResponse` returns only
  content and usage (`LocalAsk.cs:249-254`), `Program.cs:1888-1891` reports a cut-with-no-content as "the local
  engine returned no message content: {raw json}", and a cut WITH content (`Program.cs:1894`) is written as the
  answer — a fragment the round then parses.
- **The card shows no reason.** A failed check sets `FailureKind`/`FailureWhat`/`FailureCure` but no `Reason`
  (`ConsultantCheck.cs:281-292`); the card's badge reads only `reason` (`consultantHealth.ts:277`,
  `modelCardWorld.ts:32`, `:55`), so it renders a bare `checked: no answer`.

## What must be true when it is done

1. A `local` consultant turn, a ✓ Check of a `local` row, and a `local` question-consultant row each send the
   panel's `LocalReasoningEffort` (unless the row carries its own `CliEffort`, which still wins) and the panel's
   `LocalMaxTokens` — exactly what a `local` reviewer sends. Other runtimes are unchanged (`EffortFor`'s
   non-local arms are not touched).
2. No site in `src_mcp` computes a launch effort with an empty local default. *(Deviation, code round: the
   planned source scan went RED naming both sites and was then replaced — `EffortFor` takes the `PanelSettings`
   and every composer goes through `RosterBuilder.WithLocalEngine`, so the compiler holds it; the scan had missed
   a named argument, a call-shaped first argument and the ceiling.)*
3. `--ask-local` refuses an answer whose `finish_reason` is `length` — with or without content — exit 70, the
   fragment NOT written to the answer file, and a stderr sentence naming: the ceiling sent, tokens generated, how
   many characters of content arrived, and the `reasoning_effort` that was sent (or that none was, so the engine
   default applied), with the cure (`none`, or raise `COAI_LOCAL_MAX_TOKENS`). The sentence is built by ONE
   function in `core/Api` that the api shim's cut branch also uses; the api shim's behaviour is unchanged.
4. A failed ✓ Check's badge reads `checked: no answer — <failureWhat>` when the record has no `reason`; a
   `reason` still wins when present. Nothing changes for `answered` or `checking`.
5. Epic 2's measurement exists: `research/RESULTS_security_lane_effort_2026-10-13.md`, with the prediction written
   before the run, every arm read back once, and a recommendation the owner decides on. No shipped default and no
   Git prompt changes in this plan.

## Constraints

- `none` stays the default for every local launch; no reviewer behaviour changes except that a cut answer is now
  a named failure instead of a parsed fragment.
- No change to the request body's shape (`LocalRequestBodyIsPinnedTests` stays unmodified), to the api shim's
  exit codes or wording order, or to the Team server.
- `ConsultantTurnInputs.Settings` takes the `PanelSettings` the two callers already hold instead of three loose
  pieces of it (`DataDir`, `ApiOverrides`, now also the local effort and ceiling) — a parameter change, the five
  test call sites updated, no other refactor of neighbouring code.
- Every GPU call in epic 2 goes through `dewflow-gpu run` (`common.gpu-lease`); if the lease daemon is not
  listening, that is said and the run is serialised by the product's own engine lease, as the previous campaign
  recorded.

## Epic 1 — the effort reaches every local launch, and a cut is named

Order: RED tests first for each story. Every story carries its own docs (no docs-only story; the `CHANGELOG` is
written by the release pull request from the commits, as for every feature PR here — a deviation from the first draft);
the split was decided on Fable after the plan round (operator order), merging the scan into the story it guards
and dissolving the docs story.

- **S1.1 — consultant, check and question rows send the panel's local effort and ceiling, and a scan keeps it so.**
  `ConsultantTurnInputs.Settings(row, model, timeout, panel, keys)`; `Plain` sets
  `ReasoningEffort = RosterBuilder.EffortFor(row, panel.LocalReasoningEffort)` and `MaxTokens = panel.LocalMaxTokens`.
  `QuestionFanOut.SettingsFor` the same from `panel`. Tests use a panel with NON-default values (effort `medium`,
  ceiling 4096) so a path that ignores the panel and hard-codes `none`/8192 cannot pass (plan round, finding 0):
  for each of the three paths — consultant turn, ✓ Check, question-consultant row — the composed `--ask-local`
  argv carries `--reasoning-effort medium` and `--max-tokens 4096` (observed through the built launch, not the
  settings record alone); a row with `CliEffort: low` keeps `low` on all three; a `claude`/`codex` row is
  unchanged. The tripwire was first a source scan for `EffortFor(…, "")` (RED naming both sites); the code round
  replaced it with `RosterBuilder.WithLocalEngine(settings, row, panel)`, the one helper all three composers call,
  and `EffortFor(row, panel)` — see item 2. Docs: `module_server.md` (*told not to think* gains the two sites),
  `module_tests.md`.
- **S1.2 — `--ask-local` names a cut.** `CompletionReader.CutSentence(source, answer, dialect, maxTokens, cure)`
  in `core/Api` — the effort clause rides in `cure`, which each caller writes, so the api shim's sentence stays
  byte-identical (its existing tests are the check). `Program.AskLocalAsync` reads `LocalAsk.ReadAnswer` (the
  full `ChatAnswer`), prints the usage line BEFORE judging the answer (as the api shim does,
  `AskApiMode.cs:249-255` — otherwise a refused cut reports zero tokens, `LocalRuntime.cs:203`; cadence
  consultation, point 2), then refuses a cut first and no-content as today. Tests drive the shim mode end to end
  against a loopback HTTP listener answering a recorded Ollama body (`content: ""`, `finish_reason: "length"`,
  8192 completion tokens): exit 70, no answer file, stderr names 8192 and the effort, and the executor outcome
  carries 8192 tokens out. A cut WITH content: exit 70, no answer file. A `stop` answer: unchanged.
  `ConsultFailures` classification of the new sentence is asserted (it must not read as a rate limit). The engine
  lease the shim takes is checked to write to the test's own store, not the machine's.
  **Recorded trade-off** (cadence consultation, point 3): a cut fragment with content today exits 0, fails to
  parse and gets one repair launch (`ReviewerExecutor.cs:546`); refusing it removes that retry. Accepted because
  the repair is asked under the same ceiling and effort that produced the cut, and because a syntactically whole
  but cut answer (`{"findings":[]}` before its findings) is the defect the api route already refuses; a test pins
  the intended outcome (one launch, a named failure). Docs: `module_server.md`.
- **S1.3 — the badge says why.** `modelCardWorld.ts` `judged()` falls back to `record.failureWhat`. Test in
  `modelCheck.test.ts` over `checkFactsOf` with a failed record carrying only `failureWhat`, and one carrying
  `reason` (reason wins). Docs: `module_extension.md`.

## Epic 2 — does effort above `none` buy a better security answer? (measurement only)

Harness: `SecurityLaneCalibrationTests` (explicit, never CI) — real `PanelService.ReviewCodeAsync` rounds,
`redteam-authz` + `redteam-sql` on the planted invoice fixture (`SecurityLaneCalibrationTests.cs:125-141`).

- **S2.1 — harness inputs (testable without hardware).** `COAI_SECURITY_CALIBRATION_EFFORT` (default `none`),
  `COAI_SECURITY_CALIBRATION_MAX_TOKENS` (default 8192) and `COAI_SECURITY_CALIBRATION_REPEATS` (default 3) reach
  the harness's `PanelSettings` (`:110-120`), its loop (`:64`) and the manifest;
  `COAI_SECURITY_CALIBRATION_FIXTURE=safe` makes the head a benign change of the safe source that still touches
  the guard method and the query (`ORDER BY Id` added to the parameterised query), so both prompts route and the
  correct answer is empty. The recording launcher also writes each `--ask-local` argv (`turn-N.arguments.json`).
  A new explicit **effort-measurement** test records every cell — a predicted exit-70 cut included — instead of
  asserting `all N reviewers answered` (`:90`, cadence consultation, point 1); it still FAILS on a missing prompt,
  an argv whose `--reasoning-effort`/`--max-tokens` differ from the arm (plan round, finding 1), or an
  infrastructure error. The existing qualification test keeps its strict assertion.
- **S2.2 — prediction, pilot, decision.** The prediction is committed into the RESULTS file BEFORE the first call
  (the story's first commit): from §6.3 of the earlier campaign, `low`/`medium`/`high` at temperature 0 are
  predicted to loop to the ceiling (exit 70 with the S1.2 sentence) on most cells; `none` reproduces 391-ish-token
  findings on unsafe and 10-token `SECURE` on safe. Pilot: one repeat per arm (`none`, `low`, `medium`, `high`) on
  the unsafe fixture, 8 calls, every cell's argv verified against its arm. Decision: if every thinking arm fails
  at the ceiling, ONE extra arm — `low` at 16 384, unsafe, one repeat — then stop. If any thinking arm answers,
  that arm and `none` get three repeats on BOTH fixtures (3 × 2 × 2 prompts = 12 calls per arm), hand-scored
  against the earlier campaign's adequacy criterion (correct trigger and mechanism, supported impact, complete
  preconditions, no invented values) and safe-twin abstention.
- **S2.3 — record and recommend.** RESULTS file with subject sha, harness, date, model digest, pinned variables
  (model, temperature 0, prompt-derived seed, context, ceiling), per-cell tokens/finish/wall, Ollama log
  corroboration, what it does NOT settle (one fixture, one model). The recommendation is the owner's to take: keep
  `none`, or a security-only `local` row with its own `CliEffort` (possible today with no code change, because a
  row's `CliEffort` wins in `EffortFor`).

## Boundary with [PLAN_security_lane_calibration_tail.md](../todo/PLAN_security_lane_calibration_tail.md)

| Item | Built by | The other plan's part |
|---|---|---|
| Effort as the variable, one model, the existing invoice fixture | this plan (epic 2) | none — the tail varies prompt/model/fixture at fixed `none` |
| Prompt fidelity, a second fixture, the 30-PR campaign, the final feature gate | the tail plan | none here; this plan's RESULTS is one more input it may cite |
| The harness env inputs `…_EFFORT`, `…_MAX_TOKENS`, `…_FIXTURE` | this plan (S2.1) | the tail uses them at their defaults, which reproduce today's runs |

This plan goes first only because it is smaller; the two are otherwise disjoint. The same table is added to the
tail plan.

## Test plan

- Unit/integration (CI): S1.1–S1.3 and the harness half of S2.1 as above, each written RED first and the failure message recorded; the whole
  `CoaiMcp.Tests.exe` suite through `low-priority.mjs`; extension `npm test` with a clean `tsc` read first.
- Explicit hardware (not CI): epic 2 only.

## Growth surfaces

Epic 2 writes raw requests/answers per cell under an operator-chosen output directory outside the repository
(the harness requires `COAI_SECURITY_CALIBRATION_OUT`): ~20 cells × (request ~70 KB + answer ≤ 40 KB) ≈ 2–3 MB,
kept until the RESULTS file is reviewed, then deleted by the operator. Each arm writes its own directory
(`<out>/<arm>-<fixture>-<utc>`), so an interrupted run leaves one identifiable partial directory: it is moved
aside before that arm is rerun (never merged into a result), and every directory of the campaign — finished or
partial — is deleted by whoever ran it when the RESULTS file is committed or the campaign is abandoned (plan
round, finding 2). Nothing in the repository grows; no new table, cache or directory in the product.

## Definition of Done

- [ ] S1.1–S1.3 each have a test observed RED for the real symptom, then GREEN; the whole suite and the extension
      suite pass; both observations are in the PR.
- [ ] Every launch composer takes its local effort and ceiling from `RosterBuilder.WithLocalEngine` (item 2).
- [ ] Module docs updated (the changelog comes with the release pull request).
- [ ] The RESULTS file exists with prediction-before-run, read-back, conditions and limits; the owner has the
      recommendation.
- [ ] Plan gate `proceed`; code gate `proceed`; PR merged; this plan promoted to `research/` with its deviations.
