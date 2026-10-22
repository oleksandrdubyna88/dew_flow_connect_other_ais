# PLAN — only a person releases the gate

> Status: **coded, 2026-10-22 — S1–S4 and the plan round's §11 built test-first on one branch; not merged and not released, so not yet promoted. §12 records what shipped differently; §13 records the two defects the cadence consultation found afterwards and their fixes.** Found by the security lane's judge on 2026-10-22; pre-existing in every
> released `coai-mcp`. Scope: `src_mcp/core/Rounds` (`RoundMachine`, `HumanDecision`), `src_mcp/src/Server`
> (`PanelService.resolve`, `CurrentAnswer`, `Escalations`, `RoundEngine`'s hold notice), `src_mcp/src/Tools.cs`
> and `Program.cs` (what the tools promise), `src_vs_code` (`escalationAnswer.ts`, `escalations.ts`, help in five
> languages), the knowledge base.
>
> Related docs: [module_server.md](../research/module_server.md) (*`call_human` stops the review*, *Reaching a
> person*), [module_core.md](../research/module_core.md) (*verdict at completion, stage advance at resolve*),
> [module_extension.md](../research/module_extension.md) (*Active questions*, *Answer…*),
> [PLAN_ask_human_is_for_the_gate.md](../research/PLAN_ask_human_is_for_the_gate.md) (G1, G4 — which answers the gate
> acts on), [PLAN_escalation_loopback.md](../research/PLAN_escalation_loopback.md) (the answer file),
> `todo/PLAN_a_cancelled_review_stops_its_round.md` (G4 — the withdrawal hold reuses this hold; the plan and its
> S2 spec exist only on branch `feat/cancel-stops-the-round`, not on `main`, so they are named rather than linked).

## 1. The symptom

A `call_human` verdict is the gate's **enforced stop**: the rounds for a stage are spent, findings still gate, and
the product says a person has to decide before anything moves. Every surface says so, in the same words:

- the server's instructions to the calling AI — *"`call_human` is an ENFORCED stop … `resolve` does not clear it;
  only a person's answer does, and `humanDecision: "proceed"` applies ONLY after that verdict — so you cannot grant
  yourself the override"* (`src_mcp/src/Program.cs:2436-2438`);
- the `resolve` tool's description — *"After a call_human verdict ONLY: when the PERSON has explicitly decided to
  proceed despite the open findings, pass humanDecision: "proceed" … Never pass it on your own judgement"*
  (`src_mcp/src/Tools.cs:340-344`);
- `GateHeld`, the refusal every `review_*` answers while the gate stands — *"If they would rather ship with the
  findings open, they say so and you pass humanDecision: "proceed" to resolve"* (`src_mcp/core/Rounds/RoundMachine.cs:77-84`).

Read those three together and the defect is in the sentence itself: **the person decides, and the AI reports the
decision.** Nothing verifies the report. The gated party — the calling agent — is the only party that can speak
the word that releases the gate.

### The attack sequence

1. The AI's plan (or code) goes through its rounds; a finding stays `blocking`; the rounds are spent; the verdict is
   `call_human`. The state is `HumanGate = true, AwaitingResolve = true`, a notice card is written for the person
   and its id is the hold's question (`src_mcp/src/Server/Rounds/RoundEngine.cs:596-599`, `:798-819`).
2. The AI calls `resolve(repoPath, branch, decisions, humanDecision: "proceed")`. No person has seen the card.
3. `Tools.cs:324-328` turns the argument into `humanSaysProceed: true` and hands it to
   `PanelService.ResolveAsync` (`src_mcp/src/Server/PanelService.cs:1612-1627`), which passes it through
   `ResolveUnderClaim` (`:1629-1707`) into `Finish` (`:1753`) and on to `RoundMachine.Resolve` (`:1758`).
4. `RoundMachine.Resolve` (`RoundMachine.cs:629-696`) has one guard on the flag: `if (humanSaysProceed &&
   !s.HumanGate && !s.AdvanceOnResolve) refuse` (`:650-655`). The gate IS held, so the guard passes. Then
   `HumanGate = s.HumanGate && !humanSaysProceed` (`:675`), `HoldQuestions = humanSaysProceed ? [] : …` (`:677`),
   and the advance branch runs (`:679-693`): the stage moves on, `PlanProceeded` is set.
5. `review_code` is now admitted. The gate recorded a "person's override" nobody gave. The notice card stays on the
   person's panel, pointing at a hold that no longer exists.

The whole thing is one tool call, needs no file access, and is **exactly what the protocol text tells the AI to do
once the person says so in chat** — so a model does it in good faith after a sentence it misread, and a model under
pressure to finish does it without the sentence. The guard the code describes as *"the reachable bypass"* closed
(`RoundMachine.cs:644-649`) is the case where the AI claims the override BEFORE `call_human`; the case AFTER it was
never guarded, because the design assumed the word came from a person.

## 2. What is true today (verified on `main`, 2026-10-22)

**The argument's road.**

| Step | Where | What it does |
|---|---|---|
| the tool | `src_mcp/src/Tools.cs:324-328` | `humanDecision` (optional, default `null` — `:319-323` explains the SDK made it REQUIRED otherwise) → `string.Equals(humanDecision, "proceed", OrdinalIgnoreCase)` → `ResolveAsync(…, humanSaysProceed, …)` |
| the service | `src_mcp/src/Server/PanelService.cs:1612-1627` → `:1629-1707` | takes the session claim, loads the session, validates the decisions; `Finish(WithHumanDecision(session), decisions, humanSaysProceed)` at `:1651` (no decisions) and `:1706` |
| the person's answer, read for Continue/Fix only | `PanelService.cs:1736-1751` `WithHumanDecision` | while `HumanGate`: `CurrentAnswer.DecisionFor(session, _escalations)`; Continue/Fix reset the round count; **a `proceed` is never looked for here** |
| the machine | `PanelService.cs:1758` → `src_mcp/core/Rounds/RoundMachine.cs:629-696` | the guard at `:650`; `HumanGate` cleared at `:675`; `HoldQuestions` emptied at `:677`; the stage advanced at `:679-693` |

**The person's road (the one that exists for Continue / Fix / Discuss).**

- A hold writes ONE notice card (`EscalationQuestion`, `Kind = notice`, `src_mcp/src/Server/Escalations.cs:16-57`,
  `:60-67`) through `HoldNotice` (`RoundEngine.cs:804-819`). Its text asks *"Proceed anyway, or fix the findings and
  review again?"* — and **no button on the card is "proceed anyway"**: the person is being asked a question whose
  first answer they can only give to the AI in chat.
- Its id is saved as the hold's question before the file is written (`RoundEngine.cs:596-599`;
  `SessionState.HoldQuestions`, `src_mcp/core/Rounds/SessionState.cs:455-459`). Every `ask_human` asked while the
  hold stands joins it (`RoundMachine.RecordQuestion`, `RoundMachine.cs:360-365`, called under the claim by
  `AskHumanService.RecordOnTheSessionAsync`, `src_mcp/src/Server/AskHumanService.cs:324-374`).
- The extension shows the card (Active questions, the modal, the status bar) and **Answer…** opens a quick pick
  (`src_vs_code/src/escalationWatcher.ts:189-250`) whose choices are `answerChoices` (`src_vs_code/src/escalationAnswer.ts:108-122`):
  a `notice` offers the three decisions only, a `question` offers *Type an answer…* first. The three decisions are
  `continue` / `fix` / `discuss` (`decisionChoices`, `escalationAnswer.ts:40-63`), each with a `detail` saying what it
  causes; the module comment (`:26-36`) says the fourth — *"ship it with the findings open"* — is **deliberately not
  offered**, *"a human override meaning 'ignore all this' would be an off switch on the gate"*. The same rationale is
  in `HumanDecision`'s remarks (`src_mcp/core/Rounds/HumanDecision.cs:13-17`).
- The answer is written atomically beside the question as `escalations/<id>.answer.json`
  (`answerJson`, `escalationAnswer.ts:71-73`: `{ id, answer, decision, answeredUtc }`; `Escalations.AnswerPath`,
  `Escalations.cs:131`), first writer wins (`escalationWatcher.ts:252-265`).
- The server reads it by identity: `CurrentAnswer.For` → the newest answer among the hold's own questions, an
  answer with a decision first (`src_mcp/src/Server/Rounds/CurrentAnswer.cs:42-47`, `:89-96`); `Escalations.DecisionOf`
  parses `continue` / `fix` / `discuss`, **anything else is `None`** (`Escalations.cs:255-266`) — so a `decision:
  "proceed"` written into the file today is prose, not a decision.
- Two readers act on it: the engine before a round (`RoundEngine.ApplyAnyHumanDecision`, `RoundEngine.cs:86-110`:
  Continue/Fix open the gate through `RoundMachine.ApplyHumanDecision`, `RoundMachine.cs:585-594`), and `resolve`
  (`WithHumanDecision` above). A third reports it: `status.humanDecision` / `humanAnswer`
  (`PanelService.cs:1917-1935`, `CurrentAnswer.WordFor`, `CurrentAnswer.cs:71-77`).
- `PersonsRequest` (`src_mcp/src/Server/Stages/PersonsRequest.cs:10-15`) is the precedent for the rule this plan
  generalises: *"read from the ONE road only a person writes … `humanDecision: proceed` is read by
  `RoundMachine.Resolve` and touches no ground. So a caller passing every argument it has cannot open round 2."* The
  feature review's second round was made argument-proof on 2026-09-27; the stage advance never was.

**"The phone".** Four comments name a phone as a second writer of the answer file (`RoundEngine.cs:77`,
`PersonsRequest.cs:10`, `Tools.cs:305`, `escalationAnswer.ts:34`). Checked: `src_mcp/src`, `src_vs_code/src`,
`research/architecture.md`, `research/module_extension.md` — **no phone or inbox surface exists in this repository**;
the comments describe the file contract (anything that writes `<id>.answer.json` is a writer), not a component.
There is nothing to change on a phone here; §4 G3 says what any such writer gets for free.

**What already pins today's behaviour (these go red, by design — §6 names what each becomes).**

| Test | Line | Claims |
|---|---|---|
| `RoundMachineTests.AfterCallHuman_TheHumansProceed_AdvancesTheStage` | `src_mcp/tests/RoundMachineTests.cs:134-147` | the flag advances the stage after `call_human` |
| `RoundMachineTests.TheOverrideDoorCloses_BehindTheHumanWhoWalkedThrough` | `:197-207` | the flag clears `HumanGate` |
| `HumanGateHoldsTests.AHumanProceedStillClearsIt_AndAdvancesTheStage` | `src_mcp/tests/HumanGateHoldsTests.cs:85-94` | the same, in the holds suite |
| `GateReportingTests.WhenNobodyAnswered_TheHumansProceed_CanStillBeDelivered` | `src_mcp/tests/GateReportingTests.cs:347-360` | the flag after a nobody-answered `call_human` |
| `TheAnswerBelongsToTheCurrentHoldTests.TheHoldsQuestions_AreReleasedWithTheHold` | `src_mcp/tests/TheAnswerBelongsToTheCurrentHoldTests.cs:155-163` | the flag empties `HoldQuestions` |
| `TheServerSaysWhatOnlyTheRuleSaidTests.OnlyAPersonClearsTheGate` | `src_mcp/tests/TheServerSaysWhatOnlyTheRuleSaidTests.cs:68-73` | the instructions contain `humanDecision: "proceed"` |
| `humanDecision.test.ts` — *none of the choices ships a change over open findings* | `src_vs_code/src/test/humanDecision.test.ts:24-31` | no choice's text matches `/ignore\|anyway\|skip the/` |

Tests that **stay green and must**: `RoundMachineTests.TheHumanOverride_IsRefused_WhileRoundsRemain` (`:149-165`),
`…_WhenTheExhaustedStageEscalatesInstead` (`:167-182`), `ARedundantOverride_IsIgnored_NotRefused` (`:184-195`),
`HumanGateHoldsTests.ResolveNoLongerClearsTheGateOnItsOwn` (`:74-83`), `McpContractTests.Resolve_WithoutTheHumanOverride_IsAnAnswer_NotAnSdkError`
(`src_mcp/tests/McpContractTests.cs:378`), `TheSecondFeatureRoundTests` `:239-240` and `:305`,
`AFeatureIsReviewedEndToEndTests.TheCallerCannotClaimThePersonsRequest…` (`:537-565`).

## 3. Root cause — a decision with no provenance

Three of the four things a person can decide travel by a file only a person's surface writes, bound to the hold by
the question's identity. The fourth — the one that SHIPS with findings open, the strongest of the four — travels as
a boolean the gated party sets. `HumanDecision` has no `Proceed` member precisely because the design refused to put
"ignore all this" on a button (`HumanDecision.cs:13-17`); the override then went to the only other place it could,
the tool argument, and became the off switch the button was refused for being — with the AI's hand on it.

The fix is not a new guard on the flag. It is to give the fourth decision the provenance the other three have, and
to stop reading the flag as a decision at all.

## 4. Goal

- **G1 — A held gate is released only by a person's recorded answer.** `humanSaysProceed` is DERIVED by the server
  from the answer file of the hold's own notice (`CurrentAnswer`, identity-bound), never from a tool argument. No
  argument of `resolve`, `review_*`, `ask_human` or `status` can move `HumanGate` from `true` to `false`.
- **G2 — The tool argument is judged by what it would change.** `humanDecision` stays in the schema (optional,
  default `null`, so every existing client keeps calling — `Tools.cs:319-323`). A value of `"proceed"` is **refused
  with a sentence** whenever honouring it would have changed the outcome — the gate is held and no person's Proceed
  is on file, or (as today, `RoundMachine.cs:650-655`) the gate is not held and the stage would not advance anyway —
  and **ignored** when it is redundant: the person's Proceed is on file, or the gate already decided to advance
  (`AdvanceOnResolve`, today's `ARedundantOverride_IsIgnored_NotRefused`). A refusal records nothing: the decisions
  are re-sent without the argument. Any other value is ignored, as today.
- **G3 — The person gets a fourth decision, "Proceed despite the findings", on the notice card.** `decision:
  "proceed"` in the answer file is parsed (`Escalations.DecisionOf`), reported by `status` (`WordFor`), and applied
  by `RoundMachine.Resolve` exactly as the flag was: `HumanGate = false`, `HoldQuestions = []`, the stage advances,
  `PlanProceeded`. Any writer of the answer file — a future phone or inbox included — gets it by writing that one
  word; there is nothing surface-specific in the contract.
- **G4 — Proceed is offered and honoured on the gate's own NOTICE only.** The card that lists the findings being
  overridden is the card that can override them. An `ask_human` `question` card on a held gate keeps its three
  decisions and never offers Proceed; the server honours a Proceed only when the answered question's `kind` is
  `notice` (defence in depth: a `proceed` written against a question card is `None`). This also bounds the judge's
  second remark (§5 D5).
- **G5 — The server says which decisions a card offers.** The notice carries `decisions: ["continue","fix",
  "discuss","proceed"]`; the extension offers Proceed only when the file lists it. A card an older server wrote has no
  list → the three, as today. A newer extension against an older server therefore never offers a button the server
  would read as prose — the *"card leaves and nothing changed"* dead end this repository has already paid for twice
  (`escalationAnswer.ts:1-12`, `PLAN_ask_human_is_for_the_gate.md` §1).
- **G6 — A Proceed given after the AI already resolved is reachable.** Today the override can be delivered only
  inside the one `resolve` call that records the round's decisions (`RoundMachine.Resolve` refuses when
  `!AwaitingResolve`, `:631-642`); a person who decides later has no call that carries it. `resolve` with no
  decisions on a held, already-resolved gate applies the person's Proceed; a `review_*` call in that state is refused
  with a sentence naming `resolve`, not `GateHeld`. The stage still advances in ONE place — `Resolve` — which is the
  invariant `module_core.md` records (*verdict at completion, stage advance at resolve*).
- **G7 — Sessions held when the server is upgraded keep working.** `HumanGate`/`HoldQuestions` on disk are unchanged
  in shape. Their existing notice has no `decisions` list, so the extension offers the three on it; Keep going and
  Stop and act release the hold as before. A Proceed on such a hold needs a notice written by the new server — the
  engine already re-issues a notice for a hold with no recorded question (`RoundEngine.WithTheHoldReissued`,
  `RoundEngine.cs:111-`); it is NOT extended to rewrite standing notices (D7).
- **G8 — Every surface stops telling the AI to relay the person's decision.** `Program.cs` instructions,
  the `resolve` and `review_feature` descriptions, `GateHeld`, the `HoldNotice` question text, `HumanDecision`'s and
  `EscalationAnswer.Decision`'s remarks, `SessionAnswer.HumanDecision`'s doc (`ServerJsonContext.cs:172-173` — it
  already claims `proceed` is a value, which is false today and true after this plan), the extension module
  comments, the help in five languages, the knowledge base.

**Not in scope:** the Team server (serves no escalations); the 15-minute `ask_human` wait and expiry; who may WRITE
into the data directory (§8 R1 — named, not solved).

## 5. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | **The fourth decision exists, and it is the person's.** This reverses the recorded rationale of `HumanDecision.cs:13-17` / `escalationAnswer.ts:26-29` ("no off switch"), knowingly: the product already PROMISES the override on three surfaces (§1) and ships it — through the AI. Removing it instead (three decisions only; findings open means fix or argue) was weighed: it would make a `call_human` on a reviewer that is plainly wrong, or a nobody-answered round (`GateReportingTests:347-360` — *"must not also refuse the person it just called: that is a dead end, not a gate"*), unresolvable except by *Keep going* and another full panel. The off-switch objection is to WHO holds the switch; a button only a person can press, on the card that shows the findings, with a detail that names the consequence, is the gate asking the person — which is what `call_human` means | the promise stays; its provenance changes |
| D2 | **`RoundMachine.Resolve(SessionState, decisions, HumanDecision personsDecision = None, bool callerClaimsProceed = false)`.** The bool `humanSaysProceed` is REPLACED by the person's decision; the caller's claim rides beside it so the one pure place that judged the flag (`:644-655`) still judges it (G2), testably. `personsDecision == Proceed` with `!HumanGate` is treated as `None` (cannot arise when the server reads correctly; pinned so a future reader cannot make it arise). Continue/Fix in `Resolve` do what `WithHumanDecision` does today (reset the count) — the server passes the decision instead of pre-mutating the session, so `PanelService` loses lines rather than gaining them (it is at 1,969 against the 800 cap, with its own open plan) | one judge, one signature, no second flag |
| D3 | **Refuse the claim rather than silently ignore it, except when redundant.** Silent ignore sends the AI into `Decisions recorded → review_* → GateHeld → ask the person → resolve again…` with no sentence saying the road moved. The refusal names the new road: *a person's decision is read from their card, never from this argument — the gate is still held. Ask them to press "Proceed despite the findings" on the ConnectOtherAIs notice (ask_human puts the question to them if no card is open), or resolve without humanDecision to record your decisions.* Redundant claims are ignored for the reason `:184-195` already gives: refusing would throw away a legitimate round's decisions | a refusal with no door is a stall; a silent ignore is a maze |
| D4 | **The claim is judged in the core, the decision is read in the server.** `CurrentAnswer.DecisionFor` gains the notice check (G4): the hold's answer is Proceed only when `escalations.Read(id)` says the question's `Kind == Notice`; a missing question file makes it `None` (logged). `CurrentAnswer` is 109 lines — it is THE place (its own remarks: *ONE place, on purpose*). No new file for the reader | reuse-first: the rule already exists, it gains one word |
| D5 | **A Continue/Fix pressed on an `ask_human` question card asked during the hold keeps releasing the hold — out of scope as a defect.** It is G1/G4 of the ask_human plan, shipped 2026-10-03: the card is labelled the gate's, shows the hold's findings, and each decision's `detail` says it acts on the review. *(Revised, §13.2: the card used to show the hold's findings only while the round's `Pending` list was non-empty, so after an ordinary `resolve` — which clears `Pending` and leaves the hold standing — it showed NONE for a gate it can still release. That premise of this justification was false in the most common order of events; the card now carries the findings of the hold's own notice, and D5 holds only with that.)* The residual the judge names — the AI words a question so that the person presses *Keep going* for an unrelated reason — needs a person to press a button whose consequence is written under it, on a card that shows the findings. What this plan DOES take from the remark: the new, strongest decision is never on that card (G4), and a `question` card's detail text for Continue/Fix says *…for the held gate* in so many words (S5). Recorded as a residual in module_server | the strongest button is kept off the card an AI can word |
| D6 | **The withdrawal hold of the cancel plan is covered by construction and needs one sentence and one test.** On `feat/cancel-stops-the-round`, `WithdrawnSession` raises the SAME hold — `HumanGate = true, AwaitingResolve = true, HoldQuestions = [notice.Id]`, the landed findings as `Pending`, a `HoldNotice` — and its S2 spec says it does so *"so `resolve` can record decisions and take a person's `humanDecision: "proceed"`"*. That makes the bypass a two-step there: cancel past an adverse panel → hold → release it yourself — the exact move the revised G4 (2026-10-22) exists to prevent. Because the hold is the same `HumanGate`, this plan closes it without touching the cancel branch; whichever lands second (a) reword that spec sentence to *take a person's Proceed from the notice*, (b) add `AWithdrawalHold_IsNotReleasedByTheArgument` (S1 writes it against a withdrawal-shaped state today — `HumanGate`, `AwaitingResolve`, `Pending` non-empty, last round not `call_human`). **Ship order:** this plan's server stories release no later than the cancel plan's PR A+B release | the cancel plan widens the hold's reach; the hold must be person-only first |
| D7 | **No rewrite of standing notices on upgrade.** A notice written before this plan offers three decisions on the new extension (G5). Extending `WithTheHoldReissued` to re-issue every pre-upgrade notice would write a second card for every held session on the first call after upgrade, and the person would see two. A held session's person still has Keep going / Stop and act; the help and the release note say a Proceed needs a card from the new server | one card per hold is the guarantee worth more |
| D8 | **`status` reports `proceed` but applies nothing.** `status` is read-only (`ReadOnly = true`, `Tools.cs:376`); the word tells a resumed conversation what the person chose, and `resolve` is where it takes effect (G6) | the invariant: the stage moves in `Resolve` only |
| D9 | **The `HoldNotice` text keeps asking "Proceed anyway, or fix the findings and review again?"** — now every clause has a button. No translation layer exists any more (`helpContent.ts:243`), so the English sentence is the contract | the question finally matches its answers |

**Growth.** No new table, directory, process or cache. One optional array of ≤ 4 short strings on each notice
file; the escalations directory is retained as it is today.

## 6. Stories, test-first

Each story opens with its RED tests, named after the guarantee; the fix follows; the whole suites run after.
C# tests in `src_mcp/tests` (xUnit, FluentAssertions, run as the test executable); extension tests under
`src_vs_code/src/test` (`node:test`).

### S1 — the machine: a decision, not a flag (core)

RED (new file `OnlyAPersonReleasesTheGateTests.cs`, plus the rewritten ones):

- `AnAgentPassingHumanDecisionProceed_WithNoPersonsAnswer_IsRefused_AndTheGateStaysHeld` — **the attack as a
  test, through the service**: drive a session to `call_human` the way `HumanGateHoldsTests` and
  `AFeatureIsReviewedEndToEndTests` do (`PanelConfig.Uniform(1, 2, StagePolicy.Human)`, a fake reviewer answering
  one `Blocking` finding), then `ResolveAsync(repo, branch, "[]", humanSaysProceed: true)`. Expect: the reply is an
  `error` whose sentence names the card and `ask_human`; the saved session has `HumanGate == true`, the same `Stage`,
  `HoldQuestions` intact, `Pending` intact, `PlanProceeded == false`. **RED today:** the reply says `stage:
  "CodeReview"`.
- `APersonsProceedOnTheNotice_ReleasesTheHold_AndAdvancesTheStage` — same set-up; write
  `escalations/<notice>.answer.json` with `decision: "proceed"` (the e2e suite's `ThePersonAnswers` helper, widened
  to take the id); `ResolveAsync(…, "[]")` with NO argument. Expect: `Moved`, `HumanGate == false`, `HoldQuestions`
  empty, stage advanced, `PlanProceeded`. **RED today:** `DecisionOf` reads `proceed` as `None`; the gate holds.
- `TheArgumentIsIgnored_WhenThePersonAlreadyProceeded` — both the file and the argument → advanced, no refusal.
- `TheArgumentIsStillRefused_BeforeCallHuman` — keeps `TheHumanOverride_IsRefused_WhileRoundsRemain` green and
  names it in this suite's terms.
- `AProceedOnAnUnheldGate_IsNotADecision` (core) — `Resolve(s with { HumanGate = false }, [], Proceed)` behaves as
  `None`.
- `ApplyHumanDecisionLeavesAProceedToResolve` (core) — `RoundMachine.ApplyHumanDecision(held, Proceed)` returns the
  state unchanged: the stage moves in `Resolve` only (D8).
- `ResolveWithNoDecisions_OnAHeldResolvedGate_AppliesThePersonsProceed` (G6) — `AwaitingResolve == false`,
  `HumanGate == true`, Proceed on file → `Moved`, advanced. **RED today:** *"there is no completed round awaiting
  decisions"*.
- `ResolveWithNoDecisions_OnAHeldResolvedGate_WithoutAProceed_IsStillRefused` — the no-round refusal stands when
  nothing is on file.
- `AWithdrawalHold_IsNotReleasedByTheArgument` (D6) — a withdrawal-shaped state (`HumanGate`, `AwaitingResolve`,
  `Pending` non-empty, last round's verdict not `call_human`) + the argument → refused, held.
- Rewrites, same guarantees under the new signature: `AfterCallHuman_ThePersonsProceed_AdvancesTheStage`
  (from `RoundMachineTests:134`), `TheOverrideDoorCloses_BehindThePersonWhoWalkedThrough` (`:197`),
  `APersonsProceedStillClearsIt_AndAdvancesTheStage` (`HumanGateHoldsTests:85`),
  `WhenNobodyAnswered_ThePersonsProceed_CanStillBeDelivered` (`GateReportingTests:347`),
  `TheHoldsQuestions_AreReleasedWithTheHold` (`TheAnswerBelongsToTheCurrentHoldTests:155` — the Proceed branch),
  `TheSecondFeatureRoundTests:239-240`, `:305` and `AFeatureIsReviewedEndToEndTests:547-550` keep asserting that the
  CALLER's `proceed` opens nothing — their set-up changes from the flag to the claim.

GREEN: `HumanDecision.Proceed` (`HumanDecision.cs`, with the remarks rewritten per D1); `RoundMachine.Resolve` per
D2 (the `!AwaitingResolve` guard admits a held gate with a Proceed on file and no decisions; the claim guard refuses
per G2 with D3's sentence; the advance branch keys on `personsDecision == Proceed || AdvanceOnResolve`; Continue/Fix
reset the count here); `Escalations.DecisionOf` parses `"proceed"`; `CurrentAnswer.WordFor` returns `"proceed"`;
`CurrentAnswer.DecisionFor` applies the notice check (D4, G4) — `AProceedOnAQuestionCard_IsNotAProceed` and
`AProceedOnAnEarlierHoldsNotice_DoesNotReleaseThisHold` (the identity theory in
`TheAnswerBelongsToTheCurrentHoldTests`, extended with Proceed) are its RED tests. `PanelService.ResolveAsync`
renames its parameter to `callerClaimsProceed`, `WithHumanDecision` becomes the one read that returns the decision,
`Finish` passes both to `Resolve`. Net lines in `PanelService.cs`: negative.

### S2 — the engine: the hold knows its own answer

RED:

- `TheBeginAfterAPersonsProceed_SaysToResolve_NotGateHeld` — `HumanGate`, Proceed on the notice's answer file,
  `review_plan`/`review_code` → refused with *the person chose to proceed despite the findings — call resolve (with
  your decisions if the round still awaits them, or none) and the stage advances*; the session is unchanged.
  **RED today:** `GateHeld`.
- `ContinueAndFixBeforeARound_StillOpenTheGate` — `ApplyAnyHumanDecision`'s existing behaviour pinned beside the
  new branch.
- `TheNoticeListsTheDecisionsItOffers` (G5) — a `call_human` notice, a re-issued notice and (on the cancel branch,
  when merged) a withdrawal notice carry `decisions == ["continue","fix","discuss","proceed"]`; an `ask_human`
  question card on a held gate carries `["continue","fix","discuss"]` (no Proceed — G4); the field is absent-safe
  (`[]` from a file without it, like `Kind`).

GREEN: `EscalationQuestion.Decisions` (normalised accessor, `Escalations.cs`), filled by `HoldNotice` and by
`AskHumanService`'s card path; `RoundEngine.ApplyAnyHumanDecision` returns a refusal-shaped result for Proceed that
the begin turns into the sentence above (the engine's existing `Error` boundary).

### S3 — what the server says (texts)

RED (`TheServerSaysWhatOnlyTheRuleSaidTests`, `McpContractTests`):

- `OnlyAPersonClearsTheGate` rewritten: the instructions contain *only a person's answer* and *card*, do NOT contain
  `pass humanDecision`; the `resolve` description says the argument is not a decision and is refused while a gate
  is held; the `review_feature` description no longer names a phone.
- `GateHeldNamesTheCard_NotTheArgument` — `RoundMachine.GateHeld` contains *Proceed despite the findings* and
  `ask_human`, not *you pass humanDecision*.
- `Resolve_WithHumanDecisionProceed_AndNoCard_IsARefusalSentence_NotAnSdkError` — the stdio contract test's shape,
  with the argument.

GREEN: `Program.cs:2436-2438`, `Tools.cs:340-344`, `Tools.cs:305`, `RoundMachine.GateHeld`, the remarks in
`HumanDecision.cs`, `EscalationAnswer.Decision`, `SessionAnswer.HumanDecision`, `PersonsRequest.cs:10-15`,
`RoundEngine.cs:77`.

### S4 — the extension: the fourth choice, only where the server offers it

RED (`humanDecision.test.ts`, `escalations.test.ts`):

- *a notice that lists proceed offers it as the fourth choice, last, and says what it ships* — `decisionChoices`/
  `answerChoices` with `kind: 'notice', decisions: [...,'proceed']` → four, the last `decision: 'proceed'`, label
  *Proceed despite the findings*, detail matching */stage advances[\s\S]*findings open/i* and naming the branch.
- *a notice from an older server offers the three* — no `decisions` → three, as today.
- *a question card never offers proceed, whatever the file lists* (G4).
- *the three keep the findings alive; the fourth says plainly that it does not* — the existing *none of the choices
  ships a change over open findings* is rewritten: the regex guard applies to the first three; the fourth MUST
  match */findings open/i* (a button whose consequence is unstated is a button nobody presses twice,
  `escalationAnswer.ts:19`).
- *a question card's Continue and Fix say they act on the held gate* (D5).
- *answerJson writes decision proceed* — the file shape is unchanged.
- `parseEscalation` keeps `decisions` (`escalations.test.ts`).

GREEN: `Escalation.decisions?: readonly string[]` (`escalations.ts:36-65`); `escalationAnswer.ts` — a fourth
`DecisionChoice` offered by `answerChoices` only when `kind === 'notice'` and the list names it; the module comment
rewritten per D1; `escalationWatcher.ts` unchanged (it writes whatever choice was picked). Help: `helpContent.ts:243`
(*"three buttons"* → *its decisions*), the `questions-waiting` article (`:473-485`: the fourth decision, on the
notice only, what it does, and that a card from an older server offers three), and the same two articles in
`helpDe.ts:141`, `:213-217`, `helpEs.ts:141`, `:213-217`, `helpRu.ts:141`, `:213-217`, `helpUk.ts:93`, `:205-209`.

### S5 — the knowledge base (with S3/S4, same pull requests)

`module_server.md` — the *`call_human` stops the review* section (`:2272-2297`: *"`Resolve` clears `HumanGate`
only for `humanSaysProceed`"* → only for a person's Proceed read from the notice), the `resolve` row, the *Reaching a
person* section (`:1840-1849`), the answer-file diagram at `:7196-7212`; the D5 residual. `module_core.md` — the
`RoundMachine` row (`:74`), the invariant (`:214-216`: stage advance at resolve, now including the person's Proceed),
`HumanDecision`'s four members. `module_extension.md` — *Active questions* / *Answer…* (`:11186-11215`): the
`decisions` list, the fourth choice, the older-server rule. `architecture.md` — the server→file→extension contract
gains `decisions`. The cancel plan's spec sentence (D6) on whichever branch lands second. Mermaid re-rendered where
touched.

## 7. Build order and release

1. **S1 + S2 + S3 + S5(server half)** — one branch, one pull request, one plan round and one code round of the gate
   (the dog-food session is a held gate's user; expect the gate to exercise exactly this road). Releases as
   `coai-mcp` — **before or with** the cancel plan's PR A+B release (D6).
2. **S4 + S5(extension half)** — its own pull request after 1 merges; its tests do not depend on a running server.
   Releases as the extension.
3. **The shared gate rule — a conventions step, BEFORE the release (§13.3).** `.agents/conventions/common/coai-review-gate.md`
   is a pinned submodule that generates the extension's `gateRule.ts` and the snippets, and it still tells the AI to pass
   `humanDecision: "proceed"`. It is NOT edited from this repository. A follow-up pull request in BOTH conventions
   repositories (the family has two — a change to one alone leaves the other's snippets telling the AI the old road) must
   replace that sentence with *a person answers on the notice card*, and the pin must be bumped here (regenerating
   `gateRule.ts` and the snippets) before the server and extension release. Until then the server answers the old instruction
   with the refusal sentence naming the card, so nothing breaks — but every AI that follows the shared rule is sent to a road
   that refuses it first.

**Mixed versions during the window between 1 and 2.** New server + old extension: the card offers three; a Proceed
has no road until the extension updates — the `resolve` refusal and the release note say so; Keep going / Stop and
act work. Old server + new extension: the old server writes no `decisions` list → the extension offers three → no
dead button. Either order is safe; 1 before 2 is the only order that closes the bypass at once.

## 8. Risks

- **R1 — the file road's trust boundary is the data directory.** An agent with a shell or a file tool on the same
  machine, running as the same user, can write `<notice>.answer.json` itself. This plan closes the road the tools
  PROMISED closed (the argument) and makes the person's road the only one the server reads; it does not make that
  road unforgeable against a local process. Named here so nobody reads §4 as "an agent cannot proceed": it cannot
  proceed THROUGH THE PROTOCOL. A signed answer (an extension-held secret, verified by the server) is a plan of its
  own — recommended as the follow-up, not folded in.
- **R2 — behaviour change for operators.** "Tell the AI to proceed" stops working; the person presses the card.
  The refusal sentence (D3), `GateHeld`, the help and the release note all say so in the same words.
- **R3 — the no-Proceed window (§7).** Bounded by shipping 2 soon after 1; acceptable because the window's failure
  mode is "cannot ship with findings open", which is the gate's default.
- **R4 — reading the question file for `kind` (D4).** One extra read per answer with a decision; a deleted or
  unreadable question file makes a Proceed `None` and is logged — the person presses again on a card the engine can
  re-issue; never a silent release.
- **R5 — `PanelService` is over the file cap.** The change is net-negative there (D2); nothing new is added to it.
- **R6 — a race between the person's press and an in-flight `resolve`.** The answer is read under the session claim
  (`SessionClaim`, `PanelService.cs:1620-1626`), as `WithHumanDecision` reads it today; the extension's first-writer-wins
  guard covers two windows.
- **R7 — two designs of the same hold on two branches** (this one and `feat/cancel-stops-the-round`). The hold's
  shape is unchanged by either; D6 names the one sentence and the one test that cross.

## 9. Definition of Done

- [ ] `ResolveAsync(…, humanSaysProceed: true)` on a held gate with no person's answer is refused with D3's sentence
      and changes nothing (`HumanGate`, `Stage`, `HoldQuestions`, `Pending`, `PlanProceeded`) — the attack test is
      green, and was red first with the symptom *stage: "CodeReview"*.
- [ ] A `decision: "proceed"` on the hold's NOTICE answer file releases the hold and advances the stage through
      `resolve` — with the round's decisions, or on a held gate already resolved (G6).
- [ ] A `proceed` on an `ask_human` question card, or on an earlier hold's notice, releases nothing (G4, identity).
- [ ] The argument is ignored when redundant and refused when it would have changed the outcome; the four
      pre-existing refusal/ignore tests stay green unmodified in meaning.
- [ ] `review_*` on a held gate whose notice is answered Proceed is refused naming `resolve`, not `GateHeld`.
- [ ] Every notice lists its decisions; a question card lists three; the extension offers Proceed only on a notice
      that lists it, last, with its consequence written under it; an older server's card offers three.
- [ ] `status.humanDecision` reports `proceed`; nothing but `resolve` moves the stage.
- [ ] No surface — instructions, tool descriptions, `GateHeld`, remarks, module comments, help in five languages —
      tells the AI to pass `humanDecision` for the person. **Open until §7 step 3 lands:** the shared rule
      `.agents/conventions/common/coai-review-gate.md` still does, in both conventions repositories and the pin here.
- [ ] A person's Proceed is not a standing waiver: a finding it carried the stage over is recorded as `proceeded over`
      (its own disposition), never as a rejection, and gates in full when raised again — in a later round and through
      `again: true` (§13.1).
- [ ] A question card asked while a gate is held lists the findings the hold was raised for, also after the AI resolved
      them (§13.2).
- [ ] A withdrawal-shaped hold is not released by the argument; the cancel plan's spec sentence is reworded on
      whichever branch lands second (D6).
- [ ] `module_server.md`, `module_core.md`, `module_extension.md`, `architecture.md` describe the person-only road,
      the `decisions` list and the D5 residual; R1 is recorded in `module_server.md` as an open risk.
- [ ] Both suites green (`src_mcp/tests` executable; `npm test` in `src_vs_code`); the server released no later than
      the cancel plan's PR A+B; the extension after it.
- [ ] This plan promoted to `research/` with its deviations; a follow-up plan opened for R1 if the owner wants it.

## 10. Open questions for the owner

1. **D1 — keep the override at all?** Recommendation: yes, as a person's button on the notice (the promise exists on
   three surfaces today; removing it makes a wrong reviewer or a nobody-answered round unresolvable without another
   full panel).
2. **D3 — refuse or ignore the argument?** Recommendation: refuse with a sentence when it would have changed the
   outcome, ignore when redundant.
3. **D5 — Continue/Fix on an `ask_human` question card.** Recommendation: out of scope as a defect; Proceed never on
   that card; its Continue/Fix say *for the held gate*.
4. **D6 — ship order.** Recommendation: this server release before or with the cancel plan's, because the cancel
   plan makes the hold reachable by a cancel.
5. **R1 — the file road against a local agent.** Recommendation: a separate plan (a signed answer); not this one.

## 11. What the plan round changed (2026-10-22, coai session 00b03b61)

These override the sections above where they differ.

1. **The claim is narrowed (finding 0).** This plan closes the PROTOCOL road: the tool argument no longer releases a gate, and only a written answer to the hold's own notice does. It does NOT make that answer person-authored. An agent with shell or file access as the server's user can still write `<notice>.answer.json` (R1). Every surface that promises "only a person" (the tool descriptions, `GateHeld`, the help, the Program text) says exactly that: *released only by an answer on the notice card — not by any argument of the calling AI*. None of them claims the answer is verified. The FILE road is closed by a follow-up plan, `todo/PLAN_a_signed_answer_releases_the_gate.md`, created as a stub in the same PR as this plan's first code PR, with this R1 as its symptom.
2. **A live contract leg (finding 1).** A seam script, a sibling of `src_vs_code/scripts/seam-ledger-fields.mjs` with HOME/USERPROFILE/CODEX_HOME isolated, runs the BUILT coai-mcp to a held gate over FakeCli. It reads the notice file the server wrote and feeds it to the extension's real notice parser. It asserts the card offers exactly the `decisions` the notice lists, Proceed included. The leg is wired into `run-seam.mjs`, and a renamed field fails it on either side.
3. **Held sessions from before the upgrade (finding 2).** When a held session is read and its standing notice carries no `decisions` list, the server REWRITES that notice in place: the same id, the same questions, `decisions` added, via an idempotent `AtomicFile` write. No duplicate card, and the answer file is untouched. Test: `AHoldFromAnOlderServer_IsOfferedProceedAfterTheUpgrade`.
4. **A scenario through the real product (finding 3).** `APersonsProceedOnAHeldGate_AdvancesTheRealStage`, in the repo's scenario harness, drives the full flow: review → `call_human` → the notice file → an answer file with `proceed` → `resolve` → the stage advanced. The attack is a scenario too: `resolve` with the argument and no answer → refused, still held. Both are catalogued in `research/module_tests.md` with the runner command (the CoaiMcp.Tests executable, `--filter-class`).
5. **Pending findings under a person's Proceed (finding 4).** `resolve` with no decisions, consuming a recorded Proceed, records every still-Pending finding with the disposition `proceeded over by a person` (a decision record, not a silent drop). Earlier recorded decisions are kept. The stage then advances. Test: `AProceedOverUndecidedFindings_RecordsThemAsProceededOver`.
6. **The agent learns of the Proceed (finding 5).** While a Proceed is on file and not yet consumed, `status` and every refused `review_*` say: *a person pressed Proceed on the held gate — call resolve to advance*. The `resolve` reply then names the decision as the person's.
7. **One refusal for every review entry (finding 6).** `review_plan`, `review_code`, `review_document` and `review_feature` share one hold check in the engine. They answer `GateHeld` while no person has answered, and the item-6 sentence once a Proceed is on file. Test over all four tools.

## 12. What was built, and what shipped differently (2026-10-22, one branch, not merged)

Every story of §6 and the seven items of §11 are built test-first; the knowledge base (`module_server.md`, `module_core.md`,
`module_extension.md`, `module_tests.md`, `architecture.md`) describes them. The plan stays in `todo/` until the server and the
extension have released, because §9's last boxes (the release order, this document's promotion) are not done. Deviations, each with
its reason:

1. **The attack test carries decisions.** §6 S1 specifies `ResolveAsync(…, "[]", humanSaysProceed: true)`. With a finding pending, `[]`
   is answered *"1 finding(s) await a decision"* before the flag is ever read, so that call was never the dangerous one; the AI's
   real attack records its decisions and passes the flag. `AnAgentPassingHumanDecisionProceed_WithNoPersonsAnswer_…` therefore
   passes a rejection and was RED first on `"stage": "CodeReview"`; the `[]` form is a second test (RED on the wrong sentence), and
   the service now judges the claim BEFORE it counts findings, so the refusal names the card in both.
2. **`RoundMachine.ClaimRefusal` is public** and is asked by the service as well as by `Resolve` (the one judge, called early) —
   the plan had it inside `Resolve` only.
3. **`PanelService.cs` is net +5 lines, not negative** (D2/R5). The judging moved into the core and the new record logic into
   `ProceededOver`; the remaining growth is wiring and the comments the touched methods needed.
4. **§11.3 reaches `status` and `resolve` as well as the review entries** (the plan said only "when a held session is read"), and
   refreshes a notice only when its `kind` is `notice`; a card with no kind cannot be told from an `ask_human` card and is left, so a
   Proceed on it needs a notice from this server. This supersedes G7/D7 ("no rewrite of standing notices"). `status` is annotated
   read-only for MCP clients and now, once per old notice, rewrites that card file — idempotent, and the only write it makes.
5. **§11.5 was first built as a rejection with the reason `proceeded over by a person`** — **superseded by §13.1**: a rejection entered
   the session's standing rejections and made a later round discount a re-raise of the same finding, i.e. a standing waiver nobody
   granted. It is now its own disposition, `Decision.ProceededOver`.
6. **`humanNote` is a new, additive member of `status`** (§11.6 named the sentence, not the field). It is empty unless a Proceed is on
   file; nothing in the extension reads `status`.
7. **§11.7's test over all four tools is four tests in four harnesses** (the plan/code pair in `OnlyAPersonReleasesTheGateTests`, the
   document and feature ones in their own end-to-end suites), because a held document or feature review can only be reached with
   those suites' fixtures.
8. **The scenario tests of §11.4 run over the shared fake-CLI round harness in process**, with the stdio contract test and the seam
   leg covering the wire.
9. **`HumanDecision.cs` was rewritten whole**: the committed file had a blank line between every line.
10. **Not changed: `.agents/conventions/common/coai-review-gate.md`** — the shared gate rule (a pinned submodule, generated into
    `gateRule.ts`) still tells the AI to pass `humanDecision: "proceed"`. The server answers that with the refusal sentence naming the
    card, so nothing breaks; the text needs the conventions repository changed and the pin moved (`module_server.md` records it).
11. **Two existing extension tests were rewritten** (`answerChoices.test.ts`): they pinned a question card's decisions as *identical* to
    the plain ones, which D5's *for the held gate* sentence deliberately changes.

## 13. What the cadence consultation changed (2026-10-22, consultation 1da4d9f4)

A consultation on a cadence, held after §12, found two defects in what was built. Both were fixed test-first; these override §11.5,
§12.5 and D5's justification.

1. **A person's Proceed is not a standing waiver.** §11.5 recorded each still-pending finding as a REJECTION with the reason
   `proceeded over by a person`. A rejection joins the session's standing rejections, and `GateRule` discounts a finding that matches one
   (`IsStandingRejection`) in every later round — `BeginCodeRoundAgain` keeps them too. So one press on the plan notice silently waived
   the same defect in the code stage and in every `again: true` round: the press authorised far more than the stage transition it was for.
   - **Built.** `Decision.ProceededOver(Finding)` is its own disposition in the core (`RoundMachine.cs`). `RoundMachine.Resolve` adds
     only `Decision.Rejected` to the standing rejections, so `GateRule` never sees it, and it refuses a `ProceededOver` record when no
     person's Proceed is being consumed (`RoundMachine.ProceededOverNeedsAProceed`) — only the server builds one
     (`ProceededOver.Recorded`, through `DecisionAt.ProceedOver`). The projection stores `findings.resolution = 'proceeded'` with the reason
     `proceeded over by a person` (`DecisionAt.Resolution` / `.Reason`), so it is persisted in `coai.db` and reported in the round's decisions
     and by `resolve`.
   - **Readers.** `RoundsDb.DecidedEarlier` (the re-raise marker) and the round's `accepted` / `rejected` counts read only `accept` /
     `reject`, so a proceeded-over finding is in neither; the log page's *What it keeps missing* now counts judgements only
     (`resolution IN ('accept','reject')`); the gate history lists only real rejections; the log page draws the finding as *proceeded over*
     with a mark of its own and the CSV writes `proceeded over` (it would have read `open`); `CarriedDecision` spells it `proceeded`;
     `status` lists no decisions. One consequence worth knowing: because `DecidedEarlier` ignores it, a later round does not flag the same
     finding *raised again* — the gate counting it in full is the guarantee, the marker is not.
   - **Tests.** `AProceedOverUndecidedFindings_RecordsThemAsProceededOver` (extended),
     `AFindingAPersonProceededOver_IsStillGatingWhenAReviewerRaisesItAgainInALaterRound`,
     `AFindingAPersonProceededOver_IsStillGatingInAReopenedCodeStage` (`again: true`), the core pair and the refusal in
     `OnlyAPersonReleasesTheGateMachineTests`, `RoundsDbTests.AFindingAPersonProceededOver_IsStoredAsItsOwnDisposition_…`, and the
     extension's `roundsCsv` / `roundsDb` / `humanDecision` tests. The Proceed button's detail now says *this one stage change only*.
2. **A question card asked during a hold shows the hold's findings.** After an ordinary `resolve`, `PanelService` clears `Pending` and the
   hold stays; `AskHumanService.Card` copied findings only from `Pending`, so a question card asked then listed NONE although its
   Continue / Fix still release the hold (D5). That undermined D5's justification (above).
   - **Built.** `AskHumanService.OpenFindingsOf`: the pending gating findings when there are any, else — while `HumanGate` is true — the
     `OpenFindings` of the hold's own notice (the first of `HoldQuestions` whose card is not a question card). A hold whose notice is gone
     lists none rather than guessing. Continue / Fix wording is unchanged: still *for the held gate*; Proceed is still never on the card.
   - **Test.** `AQuestionCardAskedDuringTheHold_ShowsTheFindingsTheHoldWasRaisedFor_AfterTheAiResolvedThem`.
3. **The shared rule is a delivery step, not a footnote** — §7 step 3: `.agents/conventions/common/coai-review-gate.md` (pinned submodule;
   generates `gateRule.ts` and the snippets) still tells the AI to pass `humanDecision: "proceed"`. A follow-up pull request in BOTH
   conventions repositories plus the pin bump here must replace that with *a person answers on the notice card* before the release.
   Not edited from this repository.

The *questions-waiting* help article carries both facts in all five languages; the extension's Proceed detail is English only, like the
rest of the card.
