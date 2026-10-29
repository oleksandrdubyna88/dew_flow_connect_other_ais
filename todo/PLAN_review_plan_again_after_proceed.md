# PLAN — A plan that changed after `proceed` is reviewed again as a plan, on its own branch

> Status: **plan only, nothing implemented yet, 2026-10-29 — plan round 1 `proceed` (2 of 2, gating 4 of 6), its findings folded in (§ Plan round 1).** Scope: `review_plan` gains `again: true`
> (`src_mcp/src/Tools.cs`, `PanelService.ReviewPlanAsync`), one new transition and one new refusal in
> `src_mcp/core/Rounds/RoundMachine.cs`, a plan epoch on `SessionState`, on every round record and in `status`, the
> approved plan's text id kept on the session, two readers of `PlanProceeded` in the question gate, the tool
> descriptions, and one detail line in the extension's rounds log. No schema step, no new tool, no Team server change.
>
> Related: [module_server.md](../research/module_server.md), [module_core.md](../research/module_core.md),
> [module_extension.md](../research/module_extension.md), [architecture.md](../research/architecture.md),
> [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md),
> [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md). The other side of the boundary is
> `dew_flow_conventions · todo/PLAN_plan_change_control.md`. That plan is on its branch `feat/plan-change-control-e2`
> at `59543d1` and is not on its `main` yet.

## 1. The symptom (verified 2026-10-29 on origin/main `e1ed0ed6`)

A branch session gets one plan stage. Once that stage has passed, nothing can put a changed plan back in front of the
plan reviewers on the same branch:

1. **`review_plan` refuses after `proceed`.** `resolve` moves the session out of the plan stage once a plan round
   passed (`RoundMachine.Resolve`, `src_mcp/core/Rounds/RoundMachine.cs:679-692`: `Stage = Stages.Of(s.Stage).AdvancesTo`,
   which for the plan stage is `CodeReview`, `core/Rounds/Stages.cs:71-72`). After that, `BeginPlanRound` refuses every
   plan round (`RoundMachine.cs:91-92`): *"the plan stage is over for this session (stage: {s.Stage}); open a new
   session for a new plan"*.
2. **The "new session" that refusal names cannot be opened.** `open` loads the stored session for the same repo and
   branch and creates one only when none exists (`src_mcp/src/Server/PanelService.cs:523-532`), so `open` is
   idempotent per repo+branch. The same dead end was found and fixed for the code stage (issue #490). `CodeDone`'s
   remark says so (`RoundMachine.cs:202-205`): *"`open` is idempotent per repo+branch: that door never existed"*.
3. **The code stage has a way back and the plan stage has none.** `review_code` takes `again: true`
   (`src_mcp/src/Tools.cs:125-127`, description `:160-164`), and `PanelService.cs:800` routes it to
   `BeginCodeRoundAgain` (`RoundMachine.cs:225-236`). That transition reopens a finished code stage with a fresh count
   and keeps what was agreed. `review_plan` has no such argument: its arguments are `planText`, `committedPlan`,
   `expectedCommit` and the four cadence keys (`Tools.cs:83-87`).

**What that costs.** A plan that changes after approval cannot be reviewed as a plan on its own branch. The
conventions repository's new rule (a plan's second change after approval stops it for a whole re-review) therefore
uses an **interim path**. The plan file goes to `review_document`, and the revised plan becomes the next
`review_code`'s `planText`: a non-empty `planText` is the round's scope (`PanelService.cs:1362-1363`). That path was
seen working twice, on the conventions plan itself:

- `dew_flow_conventions · todo/PLAN_plan_change_control.md:64-66`: the re-review for epoch 2 on 2026-10-28,
  `review_document` with `newReview: true`, `proceed`, 4 of 4 reviewers, gating 3 of 5;
- `… :106-108`: the re-review for epoch 3 on 2026-10-29, the same path, `proceed`, 4 of 4, gating 1 of 5;
- and its second half: `review_code` given a revised `planText` was accepted as the scope (`… :510-517`).

The interim path works, but it has a gap. A document round leaves the plan gate's own state as it was. The server
never learns that the plan changed, `review_code` stays open throughout, and the halt the rule asks for is something
the agent must keep by itself.

## 2. The goal — what must be true when this is done

1. `review_plan` with `again: true`, given `planText` **or** `committedPlan`, reopens the plan stage of a branch
   session whose plan reached `proceed`. That holds whether the session is now in its code stage or done.
2. It is **refused**, with a sentence that names the cause, in each of these cases:
   - a round awaits `resolve`;
   - a person's decision is held (`call_human`);
   - no plan round of this session has ever reached `proceed`;
   - the plan sent is the one already approved;
   - the session is a document or feature session.

   A refused call saves nothing.
3. Reopening gives the plan stage a **fresh round budget**. `review_code` (with or without `again`) is then
   **refused until the new `proceed`**, so the server enforces the halt itself.
4. On the new `proceed`, the session's stored plan text, which is the scope `review_code` falls back to, is the newly
   approved plan. The approved plan is the session's last completed plan round, which carries its text id and its
   epoch (D6).
5. The session carries a **plan epoch**: 1 for the first plan review, plus 1 for each reopening. Every round records
   the epoch it ran under. `status` shows the epoch, and so do the server's log lines and the extension's rounds log.
6. The cadence arguments (`plan`, `epic`, `riskItems`, `riskNote`) work on an `again` call exactly as on any plan
   round.
7. The `review_plan` and `review_code` tool descriptions, and the refusal of a plain `review_plan` after `proceed`, tell
   the caller about `again: true`. The canonical snippet carries the step *a plan changed after proceed →
   `review_plan` with `again: true`* once the conventions repository writes it (§4, D11).
8. A session file written by this build stays safe for the released `coai-mcp` 0.63.0 that shares the data directory
   (several servers per machine: `research/architecture.md` § *Many servers on one machine*). The older binary must
   **fail closed**: it never admits a code round on a reopened plan.

## 3. Decisions (each checked against the code)

| # | Decision | Why — and what was checked |
|---|---|---|
| D1 | The argument is **`again`** (optional `bool`, C# default `false`). | It is the word `review_code` (`Tools.cs:125`) and `review_feature` (`Tools.cs:239`) already use for "reopen this finished stage of the same session". `newReview` is `review_document`'s word for a FRESH session that leaves the old one on the record. Here the session continues: its rounds, rejections and cadence keys stay. An optional argument without a C# default is published as required (`Tools.cs:79-82`). |
| D2 | **`RoundMachine.BeginPlanRoundAgain`**, pure, its refusals in this order: `AwaitingResolve` → `Unresolved`; document session → `ThisIsADocumentSession`; feature session → `ThisIsAFeatureSession`; `HumanGate` → `GateHeld`. Then by stage: `PlanReview` in epoch 1 → refused, *nothing to reopen* (D8); `PlanReview` in epoch ≥ 2 → `Moved(s)` **unchanged**, i.e. **idempotent** (plan round 1, finding 1): the call runs the next round of the reopened epoch exactly as a plain `review_plan` would. The epoch does not move, the count and escalations are not reset, and D5 is not applied. `CodeReview` or `Done` with `PlanProceeded` → `Transition.Reopened` with the reopened state (D3). The engine treats it as `Moved` and sets `StageRun.Reopening` (D5). | This is the shape of `BeginCodeRoundAgain` (`RoundMachine.cs:225-236`): the same holds refuse first, and asking `again` of a stage that is already open changes nothing (`:216-218`). **Idempotent, not refused:** an agent keeps `again: true` on every call of its re-review loop, and a refusal there would cost a round trip and teach nothing. **No reset, either:** a repeat `again` that reset the count would be a way to buy fresh rounds without a person, which is the defect `GateHeld` exists for (`RoundMachine.cs:93-95`). T17 pins both. The held gate comes BEFORE the stage check here, the reverse of `BeginPlanRound` (`:88-96`). A code stage on `call_human` must answer `GateHeld`, not an epoch sentence. |
| D3 | **The reopened state:** `Stage = PlanReview`, `RoundsRunThisStage = 0`, `EscalationsUsed = 0`, `AdvanceOnResolve = false`, **`PlanProceeded = false`**, `PlanEpoch = EpochOf(s) + 1`. A new `SessionState.PlanEpoch` (int, init) is read through `EpochOf(s) = max(1, PlanEpoch)`, so a file written before the field is epoch 1. A derived `HasProceededOnce => PlanProceeded \|\| EpochOf(this) > 1` serves readers that mean "ever". | **`PlanProceeded = false` is the fail-closed half.** Today's `BeginCodeRound` refuses on `{ PlanProceeded: false }` (`RoundMachine.cs:191-192`), and that line is in the released 0.63.0 binary as well. If the flag stayed `true` with `Stage = PlanReview`, an older binary on the same data directory would reach `_ => Moved` (`:195`) and run a code round against the plan stage. `CompleteRound` would then complete the PLAN stage with code findings (`:387-437` reads `s.Stage`). `Resolve` sets the flag again at the new `proceed` (`:691`). The flag's meaning becomes "the current plan epoch proceeded", which is also what `status` should report (`ServerJsonContext.cs:166`). |
| D4 | **Fresh budget, kept agreement.** Kept: `Rejections` (reasoned rejections stay discounted), `UsedPrompts`, the cadence keys `Plan`/`Epic`, the whole round journal. Not kept: the code stage's in-progress count. `Resolve` restarts it at 0 when the new `proceed` advances (`RoundMachine.cs:687`). **`EscalationsUsed` is per epoch** (code round 1, finding 4), reset by D3 exactly as `BeginCodeRoundAgain` resets it (`RoundMachine.cs:232`). | `BeginCodeRoundAgain` keeps exactly what was agreed (`:219-223`). **Why the escalation ladder starts over:** it climbs reviewer effort and model for ONE text that would not converge (`Ladder`, `:66-67`; `Exhausted`, `:609-611`). A revised plan is a different text. A ladder spent on epoch 1 would otherwise send epoch 2 straight to `call_human` at its first exhausted budget, having never escalated for it. Each epoch is still bounded by its own budget and by `call_human` once its ladder is spent. A spent lens pool deals again from the full pool (`Pool`, `PanelService.cs:1331-1340`), so keeping `UsedPrompts` never starves a round. Round NUMBERS continue from the journal (`RoundNumber.Next`, `src/Server/RoundNumber.cs:27-39`), not from the reset counter. The database upserts on (session, stage, number), so epoch 2's first plan round can never overwrite epoch 1's row (`RoundEngine.cs:316-323`). |
| D5 | **The same plan is refused, and only on the reopening call.** **First, the input:** `PlanInputs.PrepareAsync` runs before anything else (`PanelService.cs:668-672`). Its refusals answer first and the comparison never sees them (plan round 1, finding 2): both or neither input, an unreadable / dirty / oversized / credential-shaped `committedPlan`, a path not in the commit, an unresolvable branch, a stale `expectedCommit`. **Only a `Ready` input** is compared, by **text id**: `PlanInputOutcome.Ready.TextId` (`src/Server/Stages/PlanInput.cs:25`; `DocumentRules.ArtifactIdOf`, `core/Rounds/DocumentRules.cs:117-118`) against the approved plan's (D6). **Only on a reopening, decided ONCE, before the transition, and carried** (code round 1, finding 0; code round 2, findings 1 and 2).

- `BeginPlanRoundAgain` returns a distinct `Transition.Reopened(State)` beside `Moved` when it reopens.
- On that answer the engine builds **`PlanReopening(ReopenedEpoch, NewEpoch, Approved)`**. `Approved` is D6's `ApprovedPlan(Epoch, Round, TextId)`, or null when the journal identifies none.
- It builds it from the session read under the claim BEFORE the begin (`RoundEngine.cs:251`, before the switch at `:260`), so from the journal as it stood before the reopening round.
- It carries it on its copy of the run as `stage with { Reopening = … }`.

Nothing infers a reopening from a `Stage` read after the begin, which is always `PlanReview`. The comparison runs under the claim in the plan stage's refusal hook, before the cadence's own check (`PanelService.cs:740-744`). It runs only when `Reopening` is set and its `Approved` has a text id. **The hook receives that context explicitly** (finding 1): `RefuseBeforeBuilding` changes from `(PersistedSession, string sha)` (`StageRun.cs:64`) to `(BeforeBuilding, string sha)`, where `BeforeBuilding(PersistedSession PreBegin, PlanReopening? Reopening)`. It is never handed a bare `loaded` whose meaning depends on where in the call it was read. The code stage's two uses of `loaded` (`SinceWhenFinished`, the cadence, `PanelService.cs:934-939`) read `PreBegin` and are unchanged in meaning. | One id serves both inputs. A committed read and a pasted text carry the same id when their text is equal: `PlanSource.TextId` is the id of the text SENT for both origins (`core/Rounds/PlanSource.cs:17`). So a committed file equal to a pasted approval is refused, which is the right answer. The comparison uses the approval carried on `Reopening`, which was identified from the pre-begin session under the claim. The hook runs after the begin and before anything is built (`RoundEngine.cs:310-314`), and a refusal there saves nothing. The begun state is only ever saved by a round that runs (`LiveRound.cs:252-257`). Inside an epoch that is already reopened, the approved text is allowed: a re-review may conclude that the change was wrong and return to it (D2: no comparison there). When `Approved` is null, or its round carries no `PlanSource` (a round from before #837, first released in `mcp-v0.58.0`), nothing is compared and the reply says so in one clause. |
| D6 | **The approved plan is READ from the journal, never stamped** (revised after plan round 1, findings 3 and 4). It is the last plan round **of the epoch being reopened** (code round 2, finding 0) whose **verdict reached `proceed` and advanced the stage** (code round 1, finding 1), together with its `PlanSource.TextId` and its round number. **It is computed exactly once, by D5's pre-transition step, over the pre-begin journal, and carried on `StageRun.Reopening`.** Nothing re-scans the journal later: not the comparison, not D12's log line, not the round's completion. The rule:

- **Scan from the tail and stop at the first match** (finding 5). A match is a `PlanReview` round with `Status` `done` whose verdict word is an advancing one: `proceed`, `good_enough` or `continue_anyway`.
- **Scoped to the reopened epoch k = `EpochOf(pre-begin state)`.** A plan round whose recorded `PlanEpoch` is below k ends the scan with no match: an earlier epoch's approval is never this epoch's. A plan round with NO recorded `PlanEpoch` (from before the field, or written by 0.63.0) is read as part of the span being scanned, because it came after every older round. That is what lets the reverse sequence (D15) find 0.63.0's approval of epoch 2.
- **A person's override:** a `call_human` round matches only when it is the LAST plan round AND the pre-begin stage has left `PlanReview`. The override is not recorded on the round (checked: `Finish` saves only the state, `PanelService.cs:1782-1787`), so its position is the one fact that proves it.
- **Never a match:** `revise`, `escalated`, `interrupted`, or any other `call_human`. A `revise` round is never read as approved. `RoundEngine.cs:501` stamps a `PlanSource` on every plan round. Nothing is added to `PersistedSession`: the first draft's `ApprovedPlan` stamp at `resolve` is gone. | **Why the journal decides by construction:** a plan round can only run in the plan stage (`BeginPlanRound` refuses elsewhere, `RoundMachine.cs:91-92`), and the stage leaves `PlanReview` only through the `resolve` of the round just completed (`:679-692`). So when the stage is `CodeReview` or `Done`, the last completed plan round is, in every file this product writes, the advancing one. The verdict filter keeps that true for a file it did not write: a hand-edited or mixed-version journal whose last plan round is a `revise` still yields the earlier approval (T20). That holds whichever binary wrote the approving round. **What the stamp would have cost:** an approval recorded by the released 0.63.0, which knows no stamp, would leave it stale (finding 4). It would also need a build order in which the stamp lands before the route (finding 3). Neither problem exists when nothing is stored. **"On proceed, the stored plan text is replaced" needs no new write either; it holds by construction.** The engine saves `PlanText = planText` with EVERY completed round (`RoundEngine.cs:611`). `AwaitingResolve` refuses any other round between a verdict and its `resolve` (`RoundMachine.cs:88`, `:193`), so the text stored at the advancing `resolve` is the proceeding round's text. During a reopened epoch's `revise` rounds, `PlanText` holds unapproved text, but nothing can read it as scope then: the code round is refused (D7). Test T7 pins the fallback scope (`PanelService.cs:1362-1363`) after the new `proceed`. |
| D7 | **The code gate refuses during a re-review with its own sentence.** In `BeginCodeRound`, `{ Stage: PlanReview } when EpochOf(s) > 1` comes before `{ PlanProceeded: false }`: *"the plan of this session is being reviewed again (plan epoch k) — review_code waits for that review's proceed; finish it with review_plan and resolve"*. `BeginCodeRoundAgain` reaches it through its fall-through (`RoundMachine.cs:235`). | Without this case, the caller would read *"no plan round has reached 'proceed' in this session"* (`:191-192`), which is false for a session with an epoch 1 approval. The scope floor at `PanelService.cs:792-796` is guarded by `PlanProceeded: true` and so does not fire first. |
| D8 | **The sentences.** (a) `BeginPlanRound`'s refusal (`RoundMachine.cs:91-92`) becomes: *"the plan stage of this session has passed (stage: X). To review a plan that changed after its proceed, call review_plan with again: true — it reopens the plan stage with a fresh set of rounds, and review_code waits for the new proceed"*. (b) Nothing to reopen: *"again: true reopens a plan that already reached proceed, and this session's plan has not — call review_plan without again"*. (c) The same plan: *"this is the plan this session already approved (text id …, plan epoch k) — again: true reviews a plan that CHANGED after proceed; nothing was reopened. Save your revision before asking again: send the revised text as planText, or commit it and pass committedPlan"* (code round 1, finding 6). | The #490 lesson: a refusal that names no way forward is a stall (`GateHeld`'s remark, `RoundMachine.cs:72-76`). Every sentence goes through `Error` → `Refusal.Answer`, the one recorded road (`architecture.md` § *The server became a PRODUCER*). |
| D9 | **The cadence arguments pass through unchanged** (`Tools.cs:83-87` → `Cadence(plan, epic, riskItems, riskNote)`). The cadence's own check stays in `RefuseBeforeBuilding`, after D5's. | Nothing in the cadence keys a plan round to "first". `FirstCodeRound` asks only about code rounds (`Server/Cadence/CadenceDesk.cs:423-428`). One effect is named rather than changed: `Reconciled`'s catch-up of a missed epic close reads `Stage == Done` (`CadenceDesk.cs:404-406`), so during a re-review a close that was missed is reconciled only once the session is back at `Done`. The record is caught up then, and nothing is lost. |
| D10 | **Only the question gate's batch floor reads "ever proceeded"** (code round 1, finding 2). `AskGateDesk.cs:58` chooses `Planning` vs `Building`/`Released` by `HasProceededOnce` instead of `PlanProceeded`. **`StageRelease.cs:26` keeps reading `PlanProceeded`**, the current epoch: work whose plan is under review again is not released. | With D3 alone, reopening a plan would turn the question phase back to `Planning` (`AskGateDesk.cs:58`). There the batch count is not read at all (`:61`), so an agent could reopen its plan to send questions straight to the person again: a bypass of the free-batch floor. The work is already under way, so the phase stays `Building`. **Why `StageRelease` keeps the current epoch:** "released" means the approved plan's work went through its code gate. During a re-review there is no approved plan for the current epoch, so `PlanProceeded false` answers *not released* (`StageRelease.cs:26`). The phase is then `Building`: ever proceeded, not released. After the new `proceed` the release is read as before (by epic, `:33`, or `Stage == Done`, `:35`). Reading "ever" there would let a re-review keep the `Released` phase that a session already at `Done` had earned. |
| D11 | **The canonical snippet's words are not this repository's.** The paste is generated from `.agents/conventions/common/coai-review-gate.md` (`src_vs_code/scripts/prepare-gate.mjs:7`, output `:27` → `src_vs_code/src/generated/gateRule.ts`). This repository owns the tool descriptions (`Tools.cs:92-117`, `:132-170`) and the refusal sentences (D8). | Writing the step into a copy here would be a copy of a shared rule, which `gate-snippet-check.mjs` fails (`common.rule-ownership` § *Where product-specific material goes*). So this plan's part of "the canonical snippet" is: after the conventions release carries the step, bump the `.agents/conventions` pin, regenerate (`npm run prepare:gate`, `src_vs_code/package.json:1331`) and bump `ARTEFACT_VERSION` (`src_vs_code/src/claudeSnippet.ts:68`) if the paste text changed. The boundary table owes the conventions plan this correction (§4). |
| D12 | **The epoch on records and in `status`.** `RoundRecord.PlanEpoch` (int, init, written only when ≥ 1) on every round of a branch session, plan and code, taken from the state the round BEGAN in, beside `PlanSource` (`RoundEngine.cs:491-502`). It is absent on document and feature rounds and on rounds from before the field. `SessionAnswer` gains `PlanEpoch` (init, `ServerJsonContext.cs:161-169`; filled in `SessionAnswerFor`, `PanelService.cs:1928-1944`) for a branch session. The round's log lines name the epoch, and a reopening logs one line, *"session X: plan stage reopened for plan epoch k (the approved plan: round n, text id …)"*, when its round completes. Round n and its text id are read from `StageRun.Reopening.Approved`, carried from D5's pre-transition step (code round 2, finding 2). The completion never scans the post-round journal, where the new round would be the last plan round. When `Approved` is null, the line says *the approved plan: not identified*, never a null. | Absent ≠ zero (`common.coding-style` § *Absent is not zero*): a round written before the field says nothing about its epoch. `status` already serialises `RoundRecord`s, so the per-round epoch needs no new member there (`PanelService.cs:1938`). The log line is written at completion, because a refused begin saves nothing. |
| D13 | **No `--log` column and no schema step.** | The rounds log page reads the SESSION FILE for its detail lines, not the database (`roundsLog.ts:157-160`; `reviewedFrom` from the session file's `planSource`, `rounds.ts:98`). So the epoch reaches the page with no column. `--log` rows of a re-review stay distinct: they carry their own stage, a number that continues the journal, and their start time. A column can follow when somebody asks to filter by epoch. |
| D14 | **Extension: display only.** `RoundRecord` in `rounds.ts` gains optional `planEpoch`. The rounds log shows one detail line, *plan epoch k*, beside `reviewedFrom` (`roundsLog.ts:491-492`), only when k ≥ 2. Nothing is shown for 1 or for absent. | A session that was never reopened looks exactly as it does today. Nothing in `src_vs_code` calls `review_plan` (`PLAN_the_plan_gate_reads_the_committed_plan.md` D16), so no button is added. |
| D15 | **The older binary on the same data directory** (released coai-mcp 0.63.0) reads a reopened session as an ordinary open plan stage: `Stage PlanReview`, `PlanProceeded false`. Its `review_code` is refused, and a plain `review_plan` from it runs a round of the reopened epoch. It keeps `planEpoch` on save (the session-file guard, #868; `git tag --contains fbf98e2a` → first in `mcp-v0.63.0`). Servers up to 0.62.x drop it on save. This build then reads epoch 1 with `PlanProceeded false`: `again` answers *nothing to reopen* until the next `proceed`, and plain `review_plan` keeps working. **The reverse sequence** (plan round 1, finding 4): this build reopens epoch 2, then 0.63.0 runs a plan round on text B and resolves it to `proceed`. The stage is then `CodeReview` with `PlanProceeded true`, and `planEpoch` 2 is kept. B's round is the last completed plan round, with a `PlanSource` (0.63.0 writes one). On this build, `again` with B is refused as *the same plan* (correct: B is now the approved plan). `again` with any other text reopens epoch 3. Nothing stored can be stale (D6). That round's record carries no `planEpoch` (0.63.0 does not know the field), so it reads as *not recorded*, never as 1. | The failure is closed in every case, and the reverse sequence needs no recovery sentence: it is decided from the journal. T19 and L1 pin it. **Not verified:** whether 0.63.0 refuses or ignores an unknown `again` argument on `review_plan` (the MCP SDK's binding). Step 4 of §6 measures it. |
| D16 | **The Team server is untouched.** | The round machine and the session files exist only in coai-mcp (`src_mcp/core/Rounds/`, `src_mcp/src/Server/SessionStore.cs`). The Team server is sent one review prompt per launch, and nothing about a session (`architecture.md` § *A version that rides in both directions*, contract 2). |

## 4. Boundaries (named on both sides)

### 4.1 With the conventions plan — the same table as `dew_flow_conventions · todo/PLAN_plan_change_control.md:379-389`

| Item | Built by | The other plan's part |
|---|---|---|
| The rule, the ledger, the count, the check, the interim path | `dew_flow_conventions · todo/PLAN_plan_change_control.md` | none |
| `review_plan` with `again: true` after `proceed`: back to the plan stage with a fresh round budget, `review_code` refused until the new `proceed`, the stored plan text replaced on `proceed`, an epoch shown in `status`, the tool description and the canonical review snippet updated | this plan | none |
| The rule's interim paragraph replaced by `again: true` | a later change in `dew_flow_conventions`, after this server release | ships the feature |

**Order:** the conventions plan first, then this plan, then the rule update. This plan is written after that plan's
E2 exists and its interim paragraph is there to be replaced. **Disjoint:** the server never reads a ledger, never
counts changes and never decides when a plan must stop. It refuses a code round while a reopened plan awaits its
`proceed`, and only because the caller reopened it. The conventions plan changes no server code.

**One cell of that table cannot be built here as written** (D11). The canonical snippet's text is the conventions
repository's own `common/coai-review-gate.md`, mounted here and copied into the paste by `prepare-gate.mjs`. This
plan's part of that cell is the tool descriptions, the refusal sentences, and the regeneration at the pin bump. The
words of the step belong to row 3's change.

**Owed by the conventions plan's authors on their side:**

1. Move "the canonical review snippet updated" from row 2 to row 3, and name this repository's part as "the paste
   regenerated at the pin bump".
2. In row 3's change, say which server version first accepts `again` on `review_plan` (this plan's mcp release). Also
   say what an older server answers: its old refusal *"the plan stage is over … open a new session"*, or whatever
   L1's observation 3 records for the unknown argument (§8.1). The interim path stays the fallback for that case.
3. Note that a re-review of an approved plan on a branch whose plan round passed then goes to `review_plan` with
   `again: true`, and that the server halts `review_code` itself until the new `proceed`. Their plan round 1,
   finding 0, already counted on that (`… PLAN_plan_change_control.md:484`).

### 4.2 With [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md)

| Item | Built by | The other plan's part |
|---|---|---|
| `committedPlan` / `expectedCommit`, the reader, its refusals, `PlanSource`, the `END OF PLAN` sentinel and its fold | that plan (E1 merged #837, E2 merged #841) | none — an `again` round calls `PlanInputs.PrepareAsync` exactly as any plan round does (`PanelService.cs:668`) |
| `again: true` accepted with `committedPlan` as well as `planText`; the text-id comparison (D5) | this plan | its `TextId` (`PlanInput.cs:25`, `PlanSource.cs:17`), reused unchanged |
| The approved plan (D6) is read as the last completed plan round's `PlanSource.TextId` | this plan | `PlanSources.Stamp` (`Server/Rounds/PlanSources.cs:17-20`), unchanged |

**Order:** that plan first. It is merged. **Disjoint:** this plan changes nothing in the reader, the refusals, the
prompt or the record that plan built. An `again` round records its `PlanSource` like any plan round.
**Owed on that side:** a row in its § 8 *Out of scope*: *"review_plan with again: true reuses `PlanInputs.PrepareAsync`
and `PlanSource` unchanged — `todo/PLAN_review_plan_again_after_proceed.md`."* Also observed, and not this plan's to
fix: its status line and its `todo/README.md` rows still say E2 is *not merged*, while #841 (`095ebd6d`) is on
`main`. That plan is due for its completion check.

### 4.3 With [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md)

| Item | Built by | The other plan's part |
|---|---|---|
| The plan stage's round budget reset on reopening (`RoundsRunThisStage`, `EscalationsUsed`, D3), and the plan epoch | this plan | none |
| Counting rounds: `--round-counts`, `rounds.round_key`, E6's per-store snapshot and its `stage` field, launches per round | that plan | none — an epoch-k plan round is an ordinary plan round: its own number from the journal (D4), its own `round_key` (`RoundEngine.cs:327`, minted per round that runs), stage `PlanReview` |

**Order:** independent. Neither waits for the other. **Disjoint:** the counter never reads the budget or the epoch,
and this plan adds no column, no ledger field and no stage value. **Owed on that side:** one row in its *Boundaries
with the open plans*. It should say that a re-reviewed plan adds plan rounds to a session that has already run code
rounds. Nothing there may assume that a session's plan rounds come before all its code rounds. The epoch is not
counted, and is in the session file if a figure ever wants it.

## 5. What is not built, named

- No `review_document` change. The interim path keeps working, and the conventions rule decides when to stop using it.
- No change to `open`. A NEW session per plan is still not offered: the session is the branch's, and `again` is the
  way back into it.
- No cap on epochs. Every epoch costs at least one full plan panel round, its rounds are bounded by the stage budget
  and `call_human`, and the operator sees the epoch in `status` and in the rounds log.
- The server instructions (`Program.Instructions`) are not changed. Their 2 KiB budget test
  (`tests/TheInstructionsFitWhatAClientKeepsTests.cs`) sends detail into tool descriptions, as committed-plan D15 did.
- No `--log` column (D13).

## 6. Build order (one pull request, commits each green on its own)

No commit publishes a route that runs without its guards (plan round 1, finding 3). Commit 1 adds transitions that no
tool reaches. The `again` argument appears only in commit 2, together with D5's comparison and everything D5 reads.
D5 needs no stored stamp (D6): the journal it reads already exists on `main`. So no build exists in which `again`
runs without the same-plan refusal, and no intermediate state needs a test of its own beyond T15's (in commit 1,
`review_plan` publishes no `again`).

1. **Core: reachable by no tool.** `SessionState.PlanEpoch`, `EpochOf`, `HasProceededOnce`;
   `RoundMachine.BeginPlanRoundAgain` (D2, D3); the reopened refusal in `BeginCodeRound` (D7); the three sentences
   (D8). Tests T1–T6 and T17's pure half first, each watched RED. T15's commit-1 assertion: the published
   `review_plan` schema has no `again`.
2. **Server: the route and its guards, in one commit.**
   - `RoundRecord.PlanEpoch` stamped beside `PlanSource` (D12);
   - `SessionAnswer.PlanEpoch`, with the `ServerJsonContext` registrations;
   - the approved-plan reader over the pre-begin journal, scoped to the reopened epoch (D6); `PlanReopening` carried on
     `StageRun.Reopening`; `RefuseBeforeBuilding` takes `BeforeBuilding(PreBegin, Reopening)` (D5);
   - `ReviewPlanAsync` gains `again`: `again ? BeginPlanRoundAgain : BeginPlanRound`, and D5's comparison on a
     `Ready` input, ahead of the cadence in `RefuseBeforeBuilding`;
   - `Tools.cs`: the `again` argument (D1) and both descriptions (§7);
   - `AskGateDesk` and `StageRelease` read `HasProceededOnce` (D10);
   - the reopening log line.

   Tests T7–T15, T17's whole-round half and T18–T22.
3. **Server documentation.** `research/module_core.md` (the transition, the epoch) and `research/module_server.md`
   (the tool, the approved plan read from the journal, the refusals).
4. **The older other side, measured, not argued** (`.agents/PROJECT.md`: a field is measured against the OLD other
   side before it ships). Add `src_vs_code/scripts/live-plan-again-compat.mjs` (L1, specified in §8.1) and run its
   pre-release phase (L1a). Recorded with versions and dates in `module_tests.md`. **L1a is a release prerequisite,
   not a CI step.** It must have answered `0` on the merged tree before the mcp release tag is pushed. An answer of
   `2` (no released artefact could be obtained) blocks the release, the same as `1`. L1b runs after the extension
   release (below).
5. **Extension.** `planEpoch?` on `rounds.ts`'s `RoundRecord`; the rounds log's one line (D14); T16; then
   `research/module_extension.md` and one paragraph in `research/architecture.md` § *A field added to the round list*
   (the session file gains a member the extension reads; same rule, absent reads as nothing).

**After the merge.** One coai-mcp release and one extension release (release-please), each tag pushed alone and its
run observed (`common.task-lifecycle` § 3). `POST_DEPLOY.md` runs against this installation, and L1b runs once both new releases are published. Then tell the conventions
repository which mcp version carries `again`; that is its row 3 trigger. Once its rule ships the step, a follow-up
bumps the pin and regenerates the paste (D11).

## 7. The tool descriptions (the text a caller learns it from)

- `review_plan` gains a paragraph:
  > *A plan that changed after its `proceed`: call again with `again: true` and the revised plan, as `planText` or
  > `committedPlan`. It reopens the plan stage with a fresh set of rounds. `review_code` is then refused until the new
  > `proceed`, and the kept scope it falls back to becomes the new plan. It is refused while a round awaits `resolve`,
  > while a person is asked, when no plan round of this session reached `proceed`, and when the plan is the one
  > already approved.*
- `review_code`'s first sentence, *"REFUSES until a plan round reached `proceed`"*, gains *"— and again while a plan
  reopened with `review_plan` `again: true` awaits its new `proceed`"*.

## 8. Test plan

Run as the MTP executable, never `dotnet test`. Pure transitions go in `tests/RoundMachineTests.cs` beside the
`BeginCodeRoundAgain` cases. Whole rounds use FakeCli, following `tests/FakeCliRoundTests.cs` and
`tests/EndToEndTests.cs`. Session-file shape cases follow `tests/ASessionFileKeepsWhatANewerServerWroteTests.cs`.

| # | Scenario | Asserts (the RED symptom before the fix in brackets) |
|---|---|---|
| T1 | `BeginPlanRoundAgain` from `CodeReview` and from `Done`, `PlanProceeded: true` | `Transition.Reopened` to `PlanReview`: counts 0, escalations 0, `PlanProceeded false`, epoch 2; again from epoch 2's close → epoch 3 [method absent; `review_plan` refused "the plan stage is over"] |
| T2 | the same with `AwaitingResolve`, `HumanGate`, a document session, a feature session | `Unresolved`, `GateHeld`, the two session sentences, in D2's order; a held code stage answers `GateHeld`, not an epoch sentence |
| T3 | `PlanReview` in epoch 1 / in epoch 2 | refused *nothing to reopen* / `Moved(s)` unchanged, the same instance |
| T4 | `BeginCodeRound` and `BeginCodeRoundAgain` on a reopened session | refused with D7's sentence naming the epoch ["no plan round has reached 'proceed'"] |
| T5 | `Resolve` of epoch 2's `proceed` | `CodeReview`, `PlanProceeded true`, epoch 2 kept, code count 0 |
| T6 | a plain `BeginPlanRound` after `proceed` | the refusal names `again: true` [names "open a new session"] |
| T7 | end to end: plan `proceed` → resolve → code round → resolve (`Done`) → `review_plan again:true` with a changed plan → round runs → `review_code` refused → resolve `proceed` → `review_code` with no `planText` | the epoch-2 plan round is numbered after epoch 1's, its record `planEpoch: 2`; the code round's prompt carries the epoch-2 plan's unique line and not epoch 1's; the code round's record `planEpoch: 2` |
| T8 | from a session in `CodeReview` and one in `Done`: `again` with the approved text verbatim; with `committedPlan` whose blob holds that same text; with a changed text | refused before any round, session file byte-identical, D8(c)'s sentence naming *save* / *commit* / refused / runs. The refusal is decided from the approval carried on `Reopening`, identified before the transition, proven by B11 |
| T9 | `again` with `committedPlan` | the record's `PlanSource.Origin` is `commit`, epoch 2 |
| T10 | `again` with `plan` and `epic` and the cadence switched on | the cadence check runs under the claim, the record carries `PlanKey`/`EpicNumber` (`SessionStore.cs:118-127`) |
| T11 | epoch 1 left the plan stage on `good_enough` with its rounds spent, then `again` | the reopened epoch's round 1 can answer `revise` (rounds left = `MaxRounds` − 1) [exhausted at once] |
| T12 | `status` on a reopened session and on a file from before the field | `planEpoch` 2 / 1; the old rounds carry no `planEpoch` |
| T13 | a reopened session file handed to today's `BeginCodeRound` with `PlanEpoch` stripped (an older reader's view) | refused on `PlanProceeded: false`, never `Moved` — the fail-closed half of D3 |
| T14 | `ask_human` facts during a reopened epoch, for a session reopened from `Done` (no epic) and one with an epic whose groups were all closed | phase `Building`, batches counted, `StageRelease.Of` false in both [phase `Planning`, or `Released` kept]; after the new `proceed` both read as before |
| T15 | the tool schema | `review_plan` publishes `again` as an optional boolean; `TheInstructionsFitWhatAClientKeepsTests` green |
| T16 | extension: rounds with `planEpoch` 2, 1 and absent, run per `bundledPage.test.ts` | the line for 2; nothing for 1 or absent |
| T17 | `again: true` inside a reopened epoch: pure, `PlanReview`, epoch 2, `RoundsRunThisStage` 2; then whole-round, a second `again` call after a `revise` with the APPROVED text | pure: `Moved(s)` with count 2, epoch 2, escalations unchanged; whole-round: the round runs as round 3 of the epoch, not refused as *the same plan*, epoch still 2 [a reset to 0 or an epoch 3] |
| T18 | `again: true` with a `committedPlan` that is dirty, not in the commit, or behind a stale `expectedCommit`, whose committed text equals the approved one | each answers the reader's own refusal, never *the same plan*; no round, session file byte-identical [the comparison ran first] |
| T19 | the reverse sequence as a session-file fixture: this build reopens epoch 2; a plan round on text B and its `resolve` to `proceed` are written in 0.63.0's shape (`PlanSource` present, no `planEpoch`); then this build calls `again` with B, then with C | B → refused *the same plan* naming B's text id; C → reopens epoch 3; B's round reads *not recorded* for its epoch [no comparison, or one against the epoch 1 text] |
| T20 | D6's lookup over pre-begin journals, k = the epoch being reopened. Scope: [epoch 1 `proceed` A, epoch 2 `revise` B, epoch 2 `call_human` C, stage `CodeReview`] reopening epoch 2 → C (override), never A; [epoch 1 `proceed` A, epoch 2 `revise` B] with stage `Done` (hand-made) → none, never A; [epoch 1 `proceed` A, epoch 2 round with no `planEpoch` `proceed` B] → B. Verdicts: [plan `revise` A, plan `proceed` B, code …] → B; [plan `proceed` A, code …, plan `revise` C] with the stage `Done` (a shape this build never writes, hand-made) → A, never C; [plan `call_human` D last, stage `CodeReview`] → D (a person's override); [plan `call_human` D, plan `proceed` E] → E; a journal with no advancing plan round → none, nothing compared | the scan stops at the first match from the tail [C read as approved] |
| T22 | the carried context: reopen epoch 2 (approved by a person's override, a final `call_human` round) and let the reopening round complete as `proceed`, then `revise`; reopen a session whose journal identifies no approval | the log line names epoch 2's approving round and text id, never the new round; the no-approval reopening logs *not identified* and does not throw; the refusal hook received `BeforeBuilding` with `PreBegin` equal to the file as it stood before the call [the new round named, or a null crash] |
| T21 | epoch 1 used the whole escalation ladder before its `proceed`; reopen; let epoch 2's budget run out under `escalate` | epoch 2 escalates from the ladder's first step [`call_human` at once] |
| L1a | §8.1's pre-mcp-release check | its observations 1–5, recorded with versions and dates |
| L1b | §8.1's post-extension-release check | its observations 6–7, recorded |

**Break-its** (mutate production, name the test that goes red, restore, re-run green, report the red text):

| # | Mutation | Goes red |
|---|---|---|
| B1 | keep `PlanProceeded true` on reopening | T13 (and T4 with D7's case removed) |
| B2 | drop D5's comparison | T8 |
| B3 | keep `RoundsRunThisStage` on reopening | T11 |
| B4 | `AskGateDesk` reads `PlanProceeded` | T14 |
| B5 | stamp `PlanEpoch` on plan rounds only | T7's code-round assertion |
| B6 | the page renders epoch 1 | T16 |
| B7 | `BeginPlanRoundAgain` checks the stage before `HumanGate` | T2 |
| B8 | a repeat `again` in a reopened epoch resets the count | T17 |
| B9 | D5's comparison moved before `PlanInputs.PrepareAsync` (reading the raw file) | T18 |
| B10 | D6 reads a stored stamp, or the FIRST plan round, instead of the last advancing one | T19, T20 |
| B11 | D5 decides a reopening from `Stage` after the begin instead of `Reopening` | T8 (no comparison ever runs) |
| B15 | D12's log line re-runs D6's lookup over the post-round journal | T22 |
| B16 | D6's scan ignores the epoch scope | T20's scope cases |
| B17 | the refusal hook is handed the begun session instead of `PreBegin` | T22's hook assertion |
| B12 | D6 matches any `done` plan round, ignoring its verdict | T20 |
| B13 | `StageRelease` reads `HasProceededOnce` | T14 |
| B14 | reopening keeps `EscalationsUsed` | T21 |

### 8.1 L1 — the released other side, live (`src_vs_code/scripts/live-plan-again-compat.mjs`)

**How it gets the binaries.** It uses `releasedHalves.mjs` and adds no downloader of its own. The released coai-mcp is
`releasedServer` (`src_vs_code/scripts/releasedHalves.mjs:87-118`): the newest PUBLISHED `mcp-v*` release (drafts
excluded), downloaded with `gh release download --repo oleksandrdubyna88/dew_flow_connect_other_ais`. The asset is
picked for the host's RID: `win-x64`/`win-arm64` `.zip`, `linux-x64`/`linux-arm64` or `osx-*` `.tar.gz`. Overrides:
`COAI_OLD_SERVER=<path>` or `--tag=mcp-vX.Y.Z`, with `--tag=mcp-v0.63.0` once this change has shipped. The released
extension is `releasedExtension` (`:161`), BUILT from its tag because the `.vsix` bundle cannot be called function by
function. This build's binary comes from `newServer()` (`:55`) after `dotnet build`.

**How it runs them.** It starts each server over stdio as an MCP client, as `seam-ledger-fields.mjs` does, against
FakeCli reviewers (`src_mcp/tests_fakecli`, built first). It uses a temporary git repository and ONE shared
`COAI_DATA_DIR`, so both binaries read and write the same session file, and every child has the module's deadline.

**Two checks, because each can only claim what the artefacts it holds can show** (code round 1, finding 8).
**L1a** runs before the mcp release against the RELEASED extension. That extension cannot draw an epoch, so L1a claims
only that it parses the new files and draws them as before. **L1b** runs after this change's extension release and is
the only one that claims the new reader.

**L1a: before the mcp release tag** (`--phase=pre`). This build's server, the released coai-mcp and the released
extension. It observes:

1. this build proceeds and reopens; 0.63.0's `review_code` is refused;
2. 0.63.0's plain `review_plan` runs a round, and its save keeps `planEpoch`;
3. what 0.63.0 answers to `review_plan` with `again: true`: refused as an unknown argument, or ignored and refused as
   *"the plan stage is over"*. Recorded verbatim; either answer is acceptable, and the sentence goes to the
   conventions plan (§4.1, item 2);
4. **the reverse sequence:** 0.63.0 completes a round on text B of the reopened epoch and resolves it to `proceed`.
   Then this build refuses `again` with B as *the same plan* and reopens epoch 3 with C (D15, T19 live);
5. the released extension (0.83.0 at the time of writing) parses every session file written above, round for round,
   ignores `planEpoch`, and draws each round's detail lines exactly as for a file without it. Nothing more is claimed
   about the extension here: that build has no epoch line.

**L1b: after this change's extension release** (`--phase=post`). The newly released coai-mcp and the newly released
extension, both obtained through `releasedHalves.mjs` with their new tags. It observes:

6. the new extension's reader draws *plan epoch 2* for an epoch-2 round written by the released server, and nothing
   for epoch 1 or for a round written by 0.63.0 (no `planEpoch`);
7. the new extension reads a session file written by the PREVIOUS released server (0.63.0) unchanged.

L1b's record goes in `module_tests.md`, with the `POST_DEPLOY.md` run of that release.

**What it answers** (each phase). Exit `0`: all of that phase's observations hold. `1`: the boundary does not hold, naming which observation. `2`: an
artefact could not be obtained, meaning no `gh`, no network, no asset for this RID, or a build of the released
extension failed. It says which and never reports success. It is not in CI (the precedent's reason,
`live-feature-schema-compat.mjs:28-35`): it needs the network and a released artefact. It is a release prerequisite
(§6 step 4). L1a's `2` blocks the mcp release until it is run where the artefact can be had. L1b's `2` leaves the
extension release unverified, and it is reported so, never as passed.

## 9. Growth surface

| Surface | Size at stated volumes | Retired by | Interrupted |
|---|---|---|---|
| `SessionState.PlanEpoch` — the epoch counter, one int per branch session | ≈ 15 B of JSON per session file; the value grows by 1 per reopening, and a reopening needs a prior `proceed` (each at least one full plan panel round), so a branch session realistically ends at single digits; no cap (§5) | its owner is `SessionStore`; session files are kept for ever today (checked: `SessionStore.cs` and `PanelService.cs` hold no session sweep, only the escalation and phase sweeps, `PanelService.cs:1820-1821`) | the begun state is held in memory until the round's first progress save, which writes it with the running record (`LiveRound.cs:252-257`); a refusal before building saves nothing; a crash after that leaves the plan reopened, which fails closed (code refused, the plan re-review continues), and the running record is swept to `interrupted` as today |
| `RoundRecord.PlanEpoch` | ≈ 15 B per round; at ~1 500 rounds/month (the promoted round counter's *Growth*, cited by `PLAN_the_round_counter_tail.md:126`) ≈ 22 KB/month across all session files | the session file's owner, as above | written with the round record |

No table, column, directory, cache or process is added.

## 10. Risks

1. **An older binary admits a code round on a reopened plan.** Held by D3: `PlanProceeded false` is refused by the
   released `BeginCodeRound`. T13 and B1 pin it, and L1 observes it on the real 0.63.0.
2. **Reopening as a way round the question floor.** Held by D10. T14 and B4 pin it.
3. **The refusal sentence of an older server misleads a caller of the new rule.** The conventions change (row 3) names
   the server version and the old sentence (§4.1, owed item 2).
4. **D5 compares against the wrong approval.** Held by D6: the approval is read from the journal, which every binary
   since 0.58.0 writes the same way, so there is no stored copy to go stale. T19, B10 and L1's observation 4 pin it.

## 11. Post-deploy

The completion question of `common.post-deploy-checks`: does this change something that must be true on the deployed
thing, in a way a green build would hide? **No.** The change is in the stdio server's state machine, and the suite and
L1 cover it. `POST_DEPLOY.md` runs at the release as always, and no item is added.

## Plan round 1 (2026-10-29) — findings and decisions

`proceed`, 2 of 2 reviewers, gating 4 of threshold 6; the plan as committed in `b9609598`.

| # | Finding | Decision |
|---|---|---|
| 0 | During a reopened epoch's `revise` rounds the session stores an unapproved `PlanText`, which could become a code round's scope | **Rejected:** it is never a scope. `review_code` is refused until the new `proceed` (D7), and on `proceed` the last round's text is the approved one, as in epoch 1 (`RoundEngine.cs:611`; D6). T7 pins the fallback scope after the new `proceed` |
| 1 | What `again: true` means while a reopened epoch is still in `PlanReview` was not stated | Accepted: **idempotent**, with no reset and no new epoch; D5 is not applied (D2). T17, B8 |
| 2 | D5 could compare before the input was even read | Accepted: `PlanInputs.PrepareAsync`'s refusals answer first, and only a `Ready` input is compared (D5). T18, B9 |
| 3 | The approval stamp has to exist before the reopening route can be reached | Accepted, and resolved by removing the stamp: the approved plan is read from the journal (D6). The `again` argument is published only in the commit that carries D5 (§6). T15's commit-1 assertion |
| 4 | The reverse sequence: the released 0.63.0 proceeds after this build stamped an approval | Accepted: with D6 nothing is stored, so nothing can be stale. The new build refuses the text 0.63.0 just approved and reopens for any other (D15). T19, B10, L1 observation 4 |
| 5 | How L1 obtains and runs the released binaries | Accepted: §8.1 — `releasedHalves.mjs` (`gh`, published releases, the host's RID, `COAI_OLD_SERVER`/`--tag`), stdio with FakeCli on one shared data directory, exit `2` when an artefact is unavailable, and L1 a release prerequisite |

## Code round 1 (2026-10-29) — findings and decisions

`proceed`, 8 of 8 reviewers, over `e1ed0ed6..5c65fb12`.

| # | Finding | Decision |
|---|---|---|
| 0 | D5 decided "this call reopens" by reading `loaded.State.Stage`, which a reader can confuse with the state after the begin, which is always `PlanReview` | Accepted: `BeginPlanRoundAgain` returns `Transition.Reopened`, the engine sets `StageRun.Reopening` beside the pre-begin `loaded` (`RoundEngine.cs:251`), and D5 runs only on that flag. T8, B11 |
| 1 | The approved plan was "the last finished plan round", so a `revise` round could be read as approved | Accepted: the last plan round whose verdict advanced (`proceed`, `good_enough`, `continue_anyway`, or a final `call_human` a person overrode). D6, D12. T20, B12 |
| 2 | `StageRelease` reading "ever proceeded" would keep a reopened plan's work *released* | Accepted: only `AskGateDesk`'s phase choice reads `HasProceededOnce`; `StageRelease` keeps `PlanProceeded`. D10. T14, B13 |
| 3 | Re-approving the previously approved text inside a reopened epoch should be refused or counted | **Rejected:** it is a legitimate outcome — a revision withdrawn after its review. The same-plan refusal exists to stop a reopening that changes nothing, and a reopened epoch has already changed something. Counting changes is the conventions rule's job; the server never reads a ledger (§4.1, *Disjoint*) |
| 4 | Whether `EscalationsUsed` resets on reopening was not argued | Accepted: per epoch, as `BeginCodeRoundAgain` already does (`RoundMachine.cs:232`). The ladder escalates for one text that would not converge, and a revised plan is another text. D4. T21, B14 |
| 5 | The approved-plan lookup's cost and order were not stated | Accepted: a scan from the tail that stops at the first match. D6 |
| 6 | The same-plan refusal did not say how to recover | Accepted: D8(c) adds *save the revision: send it as planText, or commit it and pass committedPlan* |
| 7 | The rounds log should show a badge for the epoch's state | **Rejected:** outside this plan's scope. The rounds log already shows each round's stage and verdict, and the epoch line (D14) says which review a round belongs to |
| 8 | L1 claimed a new extension reader before that reader was released | Accepted: split into L1a (before the mcp release; it claims only what the released extension can show) and L1b (after the extension release; the new reader). §8.1 |

## Code round 2 (2026-10-29) — findings and decisions

`proceed`, 8 of 8 reviewers, gating 2 of 5, over `a87de9b6`. All three accepted, answered with ONE design move: the
approved plan is identified once, before the transition, and carried on `StageRun.Reopening`.

| # | Finding | Decision |
|---|---|---|
| 0 | D6's lookup could reach an approval from an EARLIER epoch | Accepted: scoped to the epoch being reopened. A plan round of a lower recorded epoch ends the scan; a round with no recorded epoch belongs to the span. D6. T20's scope cases, B16 |
| 1 | `RefuseBeforeBuilding` was handed a `loaded` whose meaning depended on call order | Accepted: it receives `BeforeBuilding(PreBegin, Reopening)` explicitly (`StageRun.cs:64`'s signature changes; the code stage reads `PreBegin`). D5. T22, B17 |
| 2 | D12's log line re-scanned the journal at completion: the new round would be read, or a `call_human`-approved epoch missed and a null crash the log | Accepted: D12 reads `Reopening.Approved`, identified once from the pre-begin journal; a null approval logs *not identified*. D5, D6, D12. T22, B15 |

## Definition of Done

- [ ] `review_plan` takes `again: true` with `planText` or `committedPlan`. It reopens a proceeded plan stage with a fresh
      budget, `PlanProceeded false` and epoch + 1. It refuses (unresolved, held, never proceeded, the same plan,
      document or feature session) and saves nothing when it refuses.
- [ ] `review_code` refuses during a re-review with D7's sentence. After the new `proceed`, its fallback scope is the
      newly approved plan, which is the last completed plan round of the journal (D6).
- [ ] Every round of a branch session records its `planEpoch`. `status` shows the session's epoch, the log names it,
      and the rounds log shows *plan epoch k* for k ≥ 2 and nothing otherwise.
- [ ] The cadence arguments work on an `again` call. The question gate's batch floor reads `HasProceededOnce`;
      `StageRelease` still reads the current epoch's `PlanProceeded`.
- [ ] The same-plan refusal is decided from the pre-transition state. The approved plan is the last plan round whose
      verdict advanced in the epoch being reopened, found by ONE tail scan of the pre-begin journal and carried on
      `StageRun.Reopening` to the comparison and the log line. The refusal hook receives `BeforeBuilding` explicitly.
      `EscalationsUsed` resets per epoch.
- [ ] Both tool descriptions and the plain refusal name `again: true`. The server instructions still pass their budget
      test.
- [ ] `again: true` inside a reopened epoch is idempotent (no reset, no new epoch). D5 runs only on a `Ready` input.
- [ ] T1–T22 green from red; B1–B17 each observed red with its named test, then green. L1a answered `0` before the mcp release tag; L1b
      answered `0` after the extension release (or is reported as not run, never as passed).
- [ ] `module_core.md`, `module_server.md`, `module_extension.md`, `module_tests.md` and `architecture.md` describe what
      runs.
- [ ] One coai-mcp release and one extension release, each tag observed. The conventions repository was told the
      version, and §4's owed rows were handed to the three plans' authors.
- [ ] `node .agents/conventions/tools/plan-lifecycle.mjs` clean; this plan promoted.
