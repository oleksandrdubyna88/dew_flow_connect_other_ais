# PLAN — `ask_human` puts a card in VS Code only for the review gate; the AI's own questions are asked in its own chat

> Status: **in progress — epic 1 (the server) built 2026-10-03 on `fix/ask-human-is-not-a-gate-hold`, epic 2 (the extension, help, the rest of the docs) open.** Scope: `src_mcp` (`AskHumanService`, `AskGate`,
> `AskGateDesk`, `Escalations`, `PanelService.SessionAnswerFor`, the `ask_human` / `ask_consultants` tool texts),
> `src_vs_code` (`escalations.ts`, `escalationAnswer.ts`, help texts), `shared/commands/command-question-consult.md`,
> the knowledge base.
>
> Related: [PLAN_question_consultant.md](PLAN_question_consultant.md) (still open at S5 — this plan changes its
> D8 and the "waiting for you" stage; the boundary is written into both documents, §6),
> [module_server.md](../research/module_server.md), [module_extension.md](../research/module_extension.md).

## 1. The symptom (the operator, 2026-10-03)

The sidebar showed **"A REVIEW IS WAITING ON YOU"** over a question an AI had asked about its own work — *"should the
api row read A) the uncommitted working tree … or B) only the last commit … Reply A, B, or 'decide later'"* — and
**Answer…** offered the gate's three decisions: *Keep going — more rounds*, *Stop and act on the findings*, *Stop and
talk to me*. None of them answers A or B. The operator's words: *"two logics got mixed. Claude's own questions that
are not about the gates must be asked in Claude's interface, not in coai."*

What the files say (escalation `f42b5305a0f9`, session `3bbbd5b5`, branch `feat/question-consultant`):

- the card was written by `ask_human` at 09:17 UTC and marked `expired` at the budget, with `openFindings: []`;
- the installed extension (0.61.0) predates `expired` and the "type an answer" choice, so it kept the card up and
  offered the three decisions only;
- the operator pressed **Stop and act on the findings** at 13:20 UTC — `decision: "fix"` was written for a
  question that had nothing to do with findings.

## 2. Root cause — one file shape, two producers, nothing to tell them apart

1. A gate `call_human` notice (`RoundEngine` → `Escalations.Notify`, `src_mcp/src/Server/Escalations.cs:127`) and an
   AI's own question (`AskHumanService.ThroughTheDoorAsync` → `Escalations.Post`,
   `src_mcp/src/Server/AskHumanService.cs:109`) write the SAME `EscalationQuestion`
   (`Escalations.cs:16`) with no field saying which one it is. Every `ask_human` call — whatever it is about — becomes
   a card under "a review is waiting on you".
2. The extension guesses the kind from `openFindings.length` (`src_vs_code/src/escalationAnswer.ts:90`): with findings
   it offers only the gate decisions; without them it offers "Type an answer…" AND the gate decisions. Either way the
   gate's buttons are on a question that is not the gate's.
3. **A second leak, server side.** `status` reports `humanDecision` / `humanAnswer` from the NEWEST answered card of
   the session, whatever it was (`PanelService.cs:1728-1735` → `Escalations.DecisionFor` / `AnswerTextFor`,
   `Escalations.cs:276`, `:293`). Today `status` for session `3bbbd5b5` says `humanDecision: "fix"` — the press on
   the A/B question. The gate itself is safe (holds read the answer by identity, `CurrentAnswer`,
   `src_mcp/src/Server/Rounds/CurrentAnswer.cs`), but an AI resuming the session reads a gate decision nobody made.
   This is reuse-first's *a decision applied at SOME of its sites*: `CurrentAnswer` exists because two readers
   applied whatever came back, and `status` is a third reader that was never moved onto it.

## 3. Decisions (the operator's answers, 2026-10-03)

| # | Decision | Why |
|---|---|---|
| G1 | **A question is the gate's when the session it is asked on is held by `call_human`, or is a feature review in the one state where the person's answer can ask for its second round** — exactly `RoundMachine.ApplyPersonsRequest`'s precondition (`src_mcp/core/Rounds/RoundMachine.cs:360-364`: feature session, not held, one round run, no second-round ground yet). Extracted as ONE predicate, `RoundMachine.AsksForTheGate(SessionState)`, used by `RecordQuestion` (`:338`) too. That NARROWS today's binding (`RoundsRunThisStage >= 1`) to the state where the binding can do anything — after round two, or with a ground already set, a "Keep going" card would be the same dead button as §1 (own review, finding 1). The card path re-reads the session under its claim before recording (`AskHumanService.cs:251`), so a hold released between the decision and the record is the one residual race: the card is then posted unbound, as today. **Residual (own review, 2026-10-03):** in the request window ANY question the AI files with `feature` becomes a card with the gate's buttons and skips the phase rule — the AI has to name the feature review deliberately, and a person still has to press, so this is no hold bypass | the binding defines which answers the gate acts on; a second definition would drift |
| G7 | **A gate question is not subject to the question consultant's phase rule.** The rule (D7) is about the AI's own questions; a held gate's question is the gate asking the person, and `GateHeld` (`RoundMachine.cs:77-82`) tells the AI to call `ask_human` — under `require` past the free batches that call was refused with "ask the consultants first", and no consultant can release a hold (own review, finding 2). On `AskDoor.Card`: a production risk keeps D8 (card at once, consultants beside); anything else is `Allowed`, not counted, no note. A `consultId` passed anyway is still verified and spent | a pre-existing defect this plan would otherwise enshrine |
| G2 | **Any other `ask_human` call writes no card.** The gate in front of the person runs unchanged (phase rule, consultId verified and spent, the batch counted), and the call returns AT ONCE with `status: "ask_in_conversation"` and an `instruction`: ask the person yourself, in this conversation, with your own question tool, offer the options you see, wait for the reply, never decide alone. The instruction also names the one way an AI lands here by mistake: a question about a DOCUMENT or FEATURE review the gate is holding must pass `document` / `feature`, because the branch's own session is not the held one (own review, finding 3) — and `GateHeld`'s instruction says the same. The reply is its own record, `ConversationAnswer` (status, instruction, note, consultId, consultantAnswers), registered in `ServerJsonContext`; `HumanAnswer` keeps its shape, so `answered` / `no_answer_yet` gain no empty fields. A spent proof marks its record `person_asked_in_conversation`, not `person_asked`, so the Logs tab does not claim a card was shown | the operator's choice ("В чат Claude") |
| G3 | **A production risk outside the gate consults FIRST, then returns** — in the building phase only; in the plan stage, after the release and under `off`, `AskGate` asks no consultant, as today. The record's outcome is `production_risk_consulted_first`, so the Logs tab does not say "the person was asked at once". When nobody can be asked (`BesideAsync` answers null: switched off, unreadable rows, arguments refused) the reply carries no answers and a `note` saying so. The consultants run to their row budget (5 min by default) inside the call, and the reply carries their answers — fenced `advisory_only`, one per row, exactly as `ask_consultants` returns them — beside `ask_in_conversation`, so the AI shows them to the person with its question. Inside a held gate D8 is unchanged: card at once, consultants beside, answers folded under it | the operator's choice ("Сначала консультанты"), knowingly trading D8's "never delay the person" for one place to read everything |
| G4 | **The card says which producer wrote it**: `kind` = `notice` (a round's `call_human`) or `question` (an `ask_human` on a held / requesting session). A `question` card always offers **Type an answer…** first, then the decisions. A typed answer carries no decision: it goes back to the asking AI as its `answer`, and it releases nothing — `Escalations.DecisionOf` reads it as `None`, so the hold stays held until a decision is pressed (the existing rule of `CurrentAnswer`, made explicit after gate round 1, finding 3, and pinned by a test). A `notice` keeps today's rule. A card with no `kind` (an older server) falls back to today's `openFindings` guess | the extension must not guess what the server knows; the fallback keeps a new extension correct against an old server |
| G5 | **`status` reports only the answer the gate would act on**: the current hold's (`CurrentAnswer.For`) or, failing that, the feature request's (`CurrentAnswer.ForRequest`). An answer to any other card no longer surfaces as `humanDecision` | §2.3 |
| G6 | `ask_human`'s name, arguments and the `answered` / `no_answer_yet` replies are unchanged; `ask_in_conversation` is a third reply. No new tool | a caller that knows two replies sees a third with an `instruction` that says what to do |

**Not in scope:** the escalation's 15-minute wait and expiry (unchanged, now only for gate cards); the Team server
(it serves no escalations); the WSL/Windows data-dir split.

**The trade-off the operator accepted** (raised by the cadence consultation b6e9df3c): an AI running UNATTENDED — a
headless `claude -p`, a background agent with no chat a person reads — used to reach the person through the VS Code
card for any question; now its own questions come back `ask_in_conversation`, and nobody may read that conversation. The
gate's own questions still reach VS Code. If headless runs need their own questions carded again, that is a setting for
a later plan (an opt-in "AI questions → card"), not a silent fallback here.

## 4. The change, file by file

**Server (`src_mcp`)**

1. `core/Rounds/RoundMachine.cs` — `AsksForTheGate(SessionState)`; `RecordQuestion` keeps its behaviour, written over it.
2. `core/QuestionConsult/AskGate.cs` — `AskGateInput.Door` (`AskDoor.Card` | `AskDoor.Conversation`, default
   `Conversation` — the AI's own question is the ordinary call every other default describes, so the existing table
   keeps its meaning); on `Card` the phase rule is skipped (G7) and a risk is not counted; the production-risk note reads it (`Risk`, `:146`): in the
   conversation the note says the consultants were asked first and their answers come with the reply. **The door is
   decided BEFORE the gate** (gate round 1, finding 1): `AskGateDesk.Facts` evaluates `AsksForTheGate` on the session it
   loaded and sets `Door`, so the note `Decide` writes is the note for the door actually taken.
3. `src/Server/AskHumanService.cs` — after `AskGate.Decide`: `Allowed` on `AskDoor.Card` → the card path as today
   (`ThroughTheDoorAsync`); `Allowed` on `AskDoor.Conversation` → `InTheConversationAsync`, in this ORDER (gate round 1,
   finding 0, then the own reviewers): the proof spent FIRST and atomically under a fresh id — a second call holding the
   same consultId is refused before it pays for any consultant; then, when `ConsultBeside`, the consultants, the proof
   GIVEN BACK if they throw or the call is cancelled (`ct.ThrowIfCancellationRequested()` after them, because a fan-out
   may settle its rows as failed rather than throw); the batch counted last; then the reply. A question that never
   reached the person has spent and counted nothing. The token is the MCP SDK's, now taken by the `ask_human` lambda in
   `Tools.cs` (it passed none, so a client's cancel or tool timeout never reached the consultants). Each method stays
   ≤ CC 4.
4. `src/Server/QuestionConsult/AskGateDesk.cs` — `AfterTheCard` split so the conversation path can count without a
   card; `ConsultFirstAsync` awaits `QuestionConsultService.BesideAsync` on the call's token (the conversation path has
   a caller to report to, so the detached edge is not needed there).
5. `src/Server/QuestionConsult/QuestionConsultService.cs` — the per-row fencing (`Answer`, `:349`) exposed as one
   `internal static` helper both replies use (no second fencer).
6. `src/Server/ServerJsonContext.cs` — a new `ConversationAnswer` record, registered; `HumanAnswer` unchanged.
   `QuestionConsultRecord.cs` — outcomes `person_asked_in_conversation` and `production_risk_consulted_first`;
   `QuestionConsultStore.Spend` takes the outcome it writes. The fencing helper mints its own nonce.
   `Program.cs:2067` — the server `Instructions` sentence about `ask_human` (inside the 2 KiB budget);
   `Commands/QuestionConsultOrder.cs:41`; the `CurrentAnswer` remark naming `AnsweredFor`.
7. `src/Server/Escalations.cs` — `EscalationQuestion.Kind` (`EscalationKinds.Notice` / `.Question`, null-normalised in
   the accessor like every S3 member); `RoundEngine` sets `Notice` on both notices, `AskHumanService.Card` sets
   `Question`.
8. `src/Server/PanelService.cs:1728` — `SessionAnswerFor` reads through `CurrentAnswer`; `Escalations.DecisionFor` /
   `AnswerTextFor` / `LatestAnswerFor` are removed if nothing else calls them.
9. `src/Tools.cs` — the `ask_human` description (three replies, which questions reach VS Code) and the
   `ask_consultants` sentences that call `ask_human` "the only door to the person".

**Extension (`src_vs_code`)**

10. `escalations.ts` — `Escalation.kind?: 'notice' | 'question'`.
11. `escalationAnswer.ts` — `answerChoices` reads `kind` first (G4), the `openFindings` guess only when it is absent
    (its one caller, `escalationWatcher.ts:189`, receives `kind` through `parseEscalation`'s spread unchanged).
    `qconsultLog.ts` — labels for the two new outcomes.
12. Help (`helpContent.ts` + `helpRu/Uk/De/Es.ts`, keys `questions-waiting`, `the-question-consultant`,
    `the-protocol`) and `help.ts` tooltips that say every question becomes a card.

**Shared text and docs**

13. `shared/commands/command-question-consult.md` (+ regenerated `commandTexts.generated.ts`) — the person is asked in
    the conversation; a production risk consults first.
14. `research/module_server.md`, `module_extension.md`, `module_core.md`, `module_tests.md`, `architecture.md`, the root
    `ARCHITECTURE.md:48-51`, `README.md` — wherever they describe `ask_human` → card; `ScenarioCoverageTests.cs:48`'s
    flow description.

## 5. Growth

Nothing new grows. A conversation question writes no escalation file; it writes the phase record and, when a proof is
spent or a risk consulted, the existing `question-consults/<id>.json` — both already swept (30 days / the store's own
retention). Fewer escalation files than today.

## 6. The boundary with PLAN_question_consultant.md

| Item | Built by | The other plan's part |
|---|---|---|
| The gate in front of `ask_human` (D7, D9, D14) | PLAN_question_consultant (shipped S3) | unchanged here; it now guards two doors |
| D8 production risk | PLAN_question_consultant | this plan: inside a held gate unchanged; outside it the consultants run FIRST (G3) |
| "Waiting for you" stage of Active questions | PLAN_question_consultant S4 | this plan: only gate cards reach it |
| The card's `kind`, `status`'s `humanDecision` | this plan | none |

Order: this plan lands before PLAN_question_consultant's S5 release, so the release the operator tests carries it.
The same table is added to PLAN_question_consultant.md §3 as D15.

## 7. Epics and build order

The gate's split order (round 1, 2026-10-03) asked for 2–3 epics; the work is two, by the boundary the two halves
already ship on. Split on Opus — the order asked for Fable, whose monthly spend limit was reached on 2026-10-02.

| Epic | Branch | Stories | Gate |
|---|---|---|---|
| **E1 — the server decides the door** | `fix/ask-human-is-not-a-gate-hold` | S1.1 `AsksForTheGate` + `Door` + `kind` on every card (G1, G4 field, G7); S1.2 the conversation path and the production risk consulting first (G2, G3); S1.3 `status` through `CurrentAnswer` (G5); S1.4 everything the SERVER says about it — the `ask_human` / `ask_consultants` descriptions, the server `Instructions`, `GateHeld`, and the shared order text (+ its regenerated TypeScript copy), because the order is sent by the server | plan round 1 (this document) · one code round over E1 |
| **E2 — what the person reads** | `fix/ask-human-is-not-a-gate-hold-e2`, from E1's commit | S2.1 the extension reads `kind` (G4 reader) and labels the two new outcomes; S2.2 help in five languages and the tooltips; S2.3 knowledge base, D15 in PLAN_question_consultant | its own plan round · one code round over E2 |

Inside each epic:

1. RED: the epic's tests from §8, each watched failing for the real symptom.
2. The code. GREEN.
3. Whole suites: `CoaiMcp.Tests.exe`, `npm test`, `npm run typecheck`, `npm run lint`, `dotnet format --verify-no-changes`,
   `plan-lifecycle.mjs`.
4. Commit the epic; `review_code` over it with the previous epic's commit as `baseRef`, declaring `plan` and
   `epic: k/2`; own reviewers in parallel; resolve, fix, fold the fixes into the epic's commit.

Then one pull request carrying both epics.

## 8. Test plan

Server (`AskHumanServiceTests`, `AskGateTests`, `EscalationsTests`, a status test):

1. `AnAiQuestionOutsideTheGate_WritesNoCard_AndIsAskedInTheConversation` — plan stage and building stage; reply
   `ask_in_conversation`, `instruction` names the conversation, no escalation file, the batch counted as before.
2. The card path on a held gate, `kind=question` (asserted in `AGateQuestionPastTheFreeBatches_…` and the document
   review's `AskHumanForADocumentReview_…`); `ATypedAnswerToAHoldQuestion_ReachesTheAsker_AndReleasesNothing` — the
   words come back as `answer`, the decision reads `None`, the gate stays held; and
   `AWordOnlyAnswerGivenAfterADecision_DoesNotHideTheDecision` — a sentence typed after a pressed decision leaves the
   decision standing (`CurrentAnswer` prefers a decided answer; found by the cadence consultation).
3. `kind=notice` on a round's notice — asserted on the document review's `call_human` notice.
4. `AProductionRiskOutsideTheGate_AsksTheConsultantsFirst_AndReturnsTheirAnswers` — fenced advice in the reply, no
   card, record outcome `production_risk_consulted_first`; a secret in the question still reaches no consultant and
   the note says none answered; `ACancelledProductionRiskOutsideTheGate_SpendsNothing_AndCountsNothing`.
5. `StatusDoesNotReportAnAnswerToAQuestionTheGateNeverAsked` — an answered `fix` on a non-hold card leaves
   `humanDecision` empty; the hold's own answer still shows.
6. `AGateQuestionPastTheFreeBatches_IsNotSentToTheConsultants` (G7) — a held session, `require`, batches spent: the
   card is written, nothing refused, nothing counted.
7. `AskGate` table: the conversation door changes only the production-risk note; the card door bypasses the phase rule
   except for a production risk.
8. `RoundMachine.AsksForTheGate` table, and `RecordQuestion` records exactly where it is true (G1, the narrowing).

**Existing tests that change meaning** (enumerated by the own review — every one asks on a session that is not held):

- `AskHumanServiceTests` — every test except `TwoAskHumanCallsInFlight…`: the phase/proof/mode tests assert
  `ask_in_conversation` and no card; the give-back, production-risk-card, 4 KB, secret, disk-root and wait tests move
  to a held session, where the card path they describe still exists.
- `AskHumanScenarioTests.cs:75-118` (stdio) — becomes the stdio scenario of the conversation path.
- `McpContractTests.cs:225`, `:255` — seeded with a held session, the only over-the-wire cover of card + wait.
- `ADocumentIsReviewedEndToEndTests.cs:507` — a document round that ends `call_human`, then `ask_human` with
  `document`; `:556` — the branch's unheld session answers `ask_in_conversation`.
- `AFeatureIsReviewedEndToEndTests.cs:946` — the question before the first round no longer writes a card.
- `HumanDecisionTests`, `EscalationsTests.cs:254,291,378` — move with `DecisionFor` / `AnswerTextFor` /
  `LatestAnswerFor` to `CurrentAnswer` or go with the methods.

Extension (`answerChoices.test.ts`):

9. a `question` card with findings offers "Type an answer…" first; a `notice` with findings offers the three
   decisions only; a card with no `kind` keeps today's guess.

Scenario: `research/module_tests.md` — the `ask_human` flow's entry updated (conversation reply covered by the stdio
`McpContractTests` if it drives `ask_human`; otherwise recorded as covered by the service tests, with the reason).

## 9. Two halves on their own clocks

| Pair | What happens |
|---|---|
| new server + old extension (0.61) | non-gate questions write no card, so nothing wrong is shown; gate cards look as today |
| old server + new extension | no `kind` → the `openFindings` guess, i.e. today's behaviour |
| an AI that does not know `ask_in_conversation` | the `instruction` is the whole contract: it says to ask in the conversation |

## 10. Definition of Done

- [ ] An `ask_human` outside a held gate / feature request writes no card and replies `ask_in_conversation` (G2).
- [ ] A production risk outside the gate returns the consultants' fenced answers in the reply (G3).
- [ ] Every card carries `kind`; the picker offers the gate decisions only where they mean something (G4).
- [ ] `status.humanDecision` comes only from the hold's or the request's own questions (G5).
- [ ] One predicate decides "is this the gate's question" for the binding and for the card (G1).
- [ ] Every defect test was watched RED for the real symptom, then GREEN; both suites, typecheck, lint, format,
      plan-lifecycle green.
- [ ] Tool texts, the autonomy command text, help in five languages, module docs and architecture updated.
- [ ] PLAN_question_consultant.md carries D15 with the boundary table.
- [ ] Gate: plan round `proceed`, code round resolved; own reviewers run; PR merged; release per the operator's OK.
