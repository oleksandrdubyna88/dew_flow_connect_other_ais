# PLAN — E1 stories: the server, the whole `auto` contract (`coai-mcp`)

> Status: **plan only, nothing coded — revised to epoch 2 of the parent, 2026-11-16.** Written by Opus when the epic
> started, on branch `feat/auto-mode-e1`; the parent then stopped at its second change (entry 1.2, the released
> state), was revised whole and re-approved as epoch 2 the same day, and this document and the S1 spec were brought to
> it before any code (U1 is entry 1.2; U2 and U3 are settled in the parent as non-changes, § 10). Epic E1 of
> [PLAN_auto_never_asks_a_person.md](PLAN_auto_never_asks_a_person.md) (its `## Epics`, § *E1*; the design is its § 4).
> A stories document keeps no change ledger: a change to this epic is an entry in the parent.
>
> Scope: `src_mcp`, `src_bench` and the server's documents. Three stories, the parent's intended cut, kept. **Cornerstone:
> E1.S1** (`model: fable`) — the stored policy and the hold's authority. Its scenarios and brief are Fable's, in
> [PLAN_auto_never_asks_a_person_E1_S1_spec.md](PLAN_auto_never_asks_a_person_E1_S1_spec.md) (written 2026-11-16,
> before its code, and revised to epoch 2 the same day — 67 scenarios; it refined C1–C4 below). This document fixes S1's boundary and the
> contracts S2 and S3 build on; S2 and S3 are briefed here in full. The parent's ledger line reads
> `Cornerstones: 1 of 2 — E1.S1, E2.S2`.

## 1. Goal, and why the cut holds

When E1 is done, a `coai-mcp` under `auto` (also the meaning of an absent `COAI_ON_EXHAUSTED`) raises no hold, never
waits and writes no card. A round nobody answered ends `not_reviewed`. Spent rounds end `good_enough`. A hold from a
build before `auto` is released by the first call that reads it. `ask_human` answers at once. Every auto decision is
in the Serilog file and in the round's `note`. Under `human`, `continue`, `good_enough` and `escalate` nothing changes.
The parent's done-when 1–8 for E1 are the bar (§ 11 maps them).

The parent's three stories are kept. The code confirms the cut:

- **S1** owns one member, `PanelConfig.Autonomous`, which is both the shape on disk and the proof that a hold predates
  `auto`. Everything that reads or writes it, and every load that decides on a hold, is one story.
- **S2** is the round machine and what reads a verdict. It needs from S1 only a flag that says "this session is under
  `auto`" and a session refreshed before `CompleteRound` runs.
- **S3** is `ask_human` and the static text. It needs from S1 a session that is refreshed and from which a legacy hold
  is already released — and, where the release could not be written this time, why (C4). Its last commit lists
  `onExhaustedAuto` in `--features`.

One correction to the parent's split of the § 8 steps: the auto twin of
`GoodEnoughPolicyTests.WhenNobodyAnswered_ItStillCallsAHuman` (step 2) can only go green with S2's machine change, so
it moves to S2. Step 2's parsing half stays in S1.

## 2. What exists — verified at `602cfb91`

`git diff 4cb32a76 602cfb91` touches only `todo/`, so every code line below is the same on `origin/main` 4cb32a76.
Paths are from the repository root. **Re-checked at `47550a7c` for epoch 2 (2026-11-16):** the code files that differ
since `602cfb91` are the agy hook's (`src_mcp/runners/Reviewers/AntigravityReadOnly.cs` and three test files), and
none is named here; `research/module_server.md` changed one line at `:1527`, so `:1862` and `:2304` below hold. The
line numbers are those of the tree before S1: S1 grows `RoundEngine.cs` by at most twelve lines (its constructor and
the begin's hold check, § 6), so S2 and S3 find each site by the method named beside it.

| Fact | Where |
|---|---|
| The policy enum (`Continue`, `Human`, `Escalate`, `GoodEnough`) | `src_mcp/core/Rounds/SessionState.cs:9-28` |
| `PanelConfig(Roles, OnExhausted = Human)`; its extension-data bag; `Uniform(max, threshold, onExhausted = Human)` | `SessionState.cs:80-82`; `:147-157`; `:273-278` |
| `SessionState`: `HumanGate`, `HoldQuestions`, `AdvanceOnResolve`, `AwaitingResolve` | `SessionState.cs:409-460` (`HumanGate` `:436`, `HoldQuestions` `:455-459`) |
| The verdict union (`GoodEnough` has no reason; `CallHuman` has one) | `src_mcp/core/Rounds/RoundMachine.cs:26-54` (`:45`, `:48`) |
| `GateHeld` and its siblings; the four begins that refuse with it | `RoundMachine.cs:86-113`; `:132`, `:226`, `:286`, `:311` |
| `AsksForTheGate` / `CanBeAskedForASecondRound` / `RecordQuestion` | `RoundMachine.cs:377`, `:380-381`, `:396-401` |
| S1 site — nobody answered, plan/code/document | `RoundMachine.cs:440-452` |
| S2 site — feature cap with a ground; `GroundFor`; `FeatureCallHumanReason` (private) | `RoundMachine.cs:493-515` (call_human `:512-514`); `:573-576`; `:578-591` |
| S3 site — `Exhausted` | `RoundMachine.cs:632-657` |
| `ClaimRefusal` and the resolve refusals; `Resolve` keeps the hold | `RoundMachine.cs:674-708`; `:750-763` |
| The file is 782 lines; `common/coding-style.md` caps a file at 800 | `RoundMachine.cs` |
| Policy parsing (absent and unknown → `Human`); the unknown-word complaint; the config built from it | `src_mcp/src/Server/PanelSettings.cs:1082-1088`; `:1215-1224`; `:1679-1683` |
| `PanelSettings.Rounds` defaults to `new()` (the record defaults); an unrelated `Autonomous` switch already exists (`COAI_AUTONOMOUS`, the autonomy ORDER) | `PanelSettings.cs:253`; `:474`, `:1035` |
| The engine: `ApplyAnyHumanDecision`, `ProceedOnFile`, `HeldSentence`, `WithTheHoldReissued` | `src_mcp/src/Server/Rounds/RoundEngine.cs:93-120`, `:126-129`, `:135-140`, `:154-168` |
| The begin: load under the claim, the hold check, the `SecurityLane` refresh | `RoundEngine.cs:258-280` (refresh `:277-280`) |
| Completion: `CompleteRound` → `AnswerFor` → `FeatureSecondRound.Apply` → the record (`with { … }`) | `RoundEngine.cs:495`, `:500`, `:505`, `:525-536` |
| The call_human notice minted, the save, `audit.Closing`, the notice written | `RoundEngine.cs:633-646`, `:650`, `:700-704` |
| `AnswerFor` (switch `:926-945`, the throw for an unknown verdict `:944`) | `RoundEngine.cs:916-949` |
| `NoticeIfAPersonMustDecide` (only for `CallHuman`), `HoldNotice` | `RoundEngine.cs:847-850`, `:853-871` |
| `RoundEngine.cs` is 982 lines (already over 800) | — |
| The feature stage's own instruction per verdict; `OneRoundUnless` names "the person asks" | `src_mcp/src/Server/Stages/FeatureSecondRound.cs:101-121`; `:123-125` |
| `RoundAudit.Closing`; `ForALogLine` | `src_mcp/src/Server/RoundAudit.cs:139-148`; `:207-208` |
| `RoundRecord.Note` ("why a SKIPPED round did not run") → `coai.db` `rounds.note` via `LoggedNote` | `src_mcp/src/Server/SessionStore.cs:135-136`, `:175`; `src_mcp/src/Store/RoundsDb.cs:923`; schema step `src_mcp/src/Store/Schema.cs:480-482` |
| `--log` already reads `note` for every row | `src_mcp/src/Store/RoundsQuery.cs:509`, `:549` |
| `MayProceed` (proceed, good_enough, continue_anyway) | `src_mcp/src/Server/Rounds/RoundCommands.cs:16-17` |
| The bench's verdicts that open the code stage; its test | `src_bench/CoaiBench/Running/RoundRunner.cs:30-31`; `src_bench/CoaiBench.Tests/PlanLoopTests.cs:17-31` |
| The bench compares the asked `COAI_ON_EXHAUSTED` with the session's `onExhausted` (N2) | `src_bench/CoaiBench/Running/SettingsApplied.cs:66-75` |
| The epic-close record's verdict ("passed: proceed, good_enough or continue_anyway"); where it is written | `src_mcp/core/Cadence/CadenceState.cs:3-7`; `src_mcp/src/Server/PanelService.cs:1790-1793` |
| `CadenceState.IsClosed` says "code gate has passed"; the desk stores whatever verdict string it is handed — `Finish` passes `Rounds[^1].Verdict`, the reconcile the last code round's — and nothing renders it (its readers take the epic numbers only, `CadenceDesk.cs:336`, `QuestionConsult/StageRelease.cs:40-45`) | `CadenceState.cs:48-49`; `src_mcp/src/Server/Cadence/CadenceDesk.cs:302`, `:393-399` |
| `resolve` with no decisions is refused while findings are pending; `Finish` is the one path that advances a stage and closes the cadence | `PanelService.cs:1675-1682`; `:1775-1810` |
| `review_plan`'s verdict list; the `review_feature` D23 paragraph; `resolve`'s hold paragraph; `ask_human` | `src_mcp/src/Tools.cs:103-104`; `:298-311`; `:341-348`; `:399-446` |
| The server instructions (1,935 characters; budget 2,000) | `src_mcp/src/Program.cs:2443-2466`; `src_mcp/tests/TheInstructionsFitWhatAClientKeepsTests.cs` |
| The help's `COAI_ON_EXHAUSTED (continue|human|escalate)` — three of four words today | `Program.cs:2614` |
| Startup notes: storage notes read from the PROCESS environment, then `StartupNotices.Record` | `Program.cs:2188-2189`; `src_mcp/src/Server/StartupNotices.cs:52-57`, `:69-81` |
| A client `env` value wins over the settings file | `src_mcp/src/Server/SettingsFile.cs:87-88` (`Over`) |
| `ask_human`: entry, load, desk facts, gate decision; the conversation door; the card door | `src_mcp/src/Server/AskHumanService.cs:85-108` (load `:100-101`); `:129-146`; `:215-241` |
| Two log lines that carry the question's TEXT | `AskHumanService.cs:143` (conversation), `:229` (card) |
| `AskInTheConversation`; `Expired` (`no_answer_yet`) | `AskHumanService.cs:202-208`; `:326-340` |
| `HumanAnswer`, `ConversationAnswer` (`AskInConversation = "ask_in_conversation"`) | `src_mcp/src/Server/ServerJsonContext.cs:332-336`, `:349-353` |
| The pure gate: input, door, decision union, `Decide`, the stand-down | `src_mcp/core/QuestionConsult/AskGate.cs:38-68`, `:71-78`, `:81-92`, `:115-125`, `:152-165` (stand-down `:154-159`) |
| The desk: the facts, the door from `AsksForTheGate`, the proof (only an `ask_consultants` record counts) | `src_mcp/src/Server/QuestionConsult/AskGateDesk.cs:53-81`, `:156-157`, `:178-191` |
| `status`, `resolve` loads; the fresh-document refusal beside a hold (the only caller of `HeldSentence`) | `PanelService.cs:559-577` (load `:567`); `:1653-1662` (load `:1656`); `:1152-1158` |
| Where sessions are created with `_settings.Rounds` | `PanelService.cs:523-526`; `src_mcp/src/Server/Stages/FeatureStage.cs:201-202` |
| Session load (a parse error is a NEW session); a null `config` normalised to `new PanelConfig` | `SessionStore.cs:522-531`; `:697-707` |
| Session JSON options: camelCase, enums by name, null not written | `ServerJsonContext.cs:364-371` |
| `Escalations.Expire(id, now)` — status `expired`, no reason member | `src_mcp/src/Server/Escalations.cs:244-245` |
| `--features` list (append-only, each entry in the commit that makes it true) | `src_mcp/src/Server/FeaturesMode.cs:19-50` |
| Tests that pin TEXT this epic changes | `TheGateSaysWhenToConsultTests.cs:97-112` (every verdict, derived from the union, in `review_plan`), `:143-163` (the phase rule, whole, in `ask_human` and `ask_consultants`), `:167-177` ("15 minutes by default", "expired"); `GoodEnoughPolicyTests.cs:96-114` (`VerdictInstructionTests`: every verdict has a case in `RoundEngine.cs`); `TheInstructionsFitWhatAClientKeepsTests.cs` (≤ 2,000 characters, ends with "the `consult` PROMPT.", contains "COMMANDS", "Reject in round ONE", "ENFORCED stop", "answer on the notice card"); `TheServerSaysWhatOnlyTheRuleSaidTests.cs:47-76`, `:91-105` |
| Test harnesses | `src_mcp/tests/CodeRoundHarness.cs:107-133` (`Service(threshold, onExhausted = Human, maxRounds, …, log)`); `AskHumanServiceTests.cs:64-97` (`Build`); `RoundAuditTests.cs:16` (`ListSink`); `ARoundsNumberComesFromTheJournalTests.cs:141-152` (reads `coai.db`) |

### 2.1 The parent's references, checked

Every code reference in the parent's § 2–§ 8 and notes was opened. These were wrong or incomplete:

| Parent says | The tree says |
|---|---|
| § 4.1, § 11 C1: `PanelConfig`'s extension-data bag is `SessionState.cs:61` | `:61` is `RoleGate`'s bag. `PanelConfig`'s is `:147-157`. |
| § 4.2: `RoundEngine.AnswerFor` `:926-948` | The method is `:916-949`; `:926` is its switch. |
| § 4.4: the line that logs the question's text is `AskHumanService.cs:145` | It is `:143`. A second line logs the text too, on the card door: `:229`. Done-when 3 ("no log line carries the text of a question") covers both; S3 fixes both. |
| § 4.3: "`resolve`'s and `status`'s loads (`PanelService`, :1636-1822)" | `resolve` is `:1636-1822` (load `:1656`). `status` is `:559-577` (load `:567`). |
| § 2 S4: a new session beside a held one is refused by `RoundEngine.HeldSentence` | `HeldSentence` has one caller, `PanelService.WhyNotAFreshReview` (`:1152-1158`): a FRESH DOCUMENT review beside a held one. No other new-session path asks it. |
| § 4.3: the notice is withdrawn "with a reason the card shows" (`Escalations.Expire`, :244) | `Expire(id, now)` takes no reason and `EscalationQuestion` has no member for one (`Escalations.cs:16-60`, `:244-245`). A reason the CARD shows needs the extension to read a new member, and E1 does not touch `src_vs_code`. S1's spec says where the reason goes (input S1-6). |
| § 4.4 (`ask_human` table): the `consult_first` sentence offers "ask_consultants, or consult for this checkout … then call ask_human again with the consultId" | The desk accepts as proof ONLY an `ask_consultants` record (`AskGateDesk.cs:178-191`: `questions.Store.Read(id)`). A `consult` consultation id is rejected — "it is not a question this caller session asked in this repository". An orchestrator that takes the `consult` road gets `consult_first` again, for ever. The sentence now names only `ask_consultants`, in the parent too (§ 10, item 1). |
| § 4.8 (E2's): the 0.31.6 was/now entry is `CHANGELOG.md:3470-3495` | `## Extension 0.31.6` is at `src_vs_code/CHANGELOG.md:3584`. |

Everything else matched, to the line or within one line: `RoundMachine.cs` `:86-94`, `:132/:226/:286/:311`, `:227-228`,
`:377`, `:440-452`, `:512-514`, `:632-657`, `:674-708`, `:750-763`; `PanelSettings.cs:1082-1088`, `:1215-1224`,
`:1679-1683`; `SessionState.cs:80-82`, `:277`; `ServerJsonContext.cs:368`; `SessionStore.cs:522-530`, `:702-704`;
`PanelService.cs:523-526`; `FeatureStage.cs:202`; `RoundEngine.cs:93-120`, `:154-168`, `:276-280`, `:650`;
`AskHumanService.cs:128-146`, `:202-208`, `:215-241`; `AskGate.cs:152-159`; `RoundCommands.cs:16`;
`RoundRunner.cs:31`; `CadenceState.cs:5`; `Tools.cs:103`, `:298-311`, `:341-348`, `:400-446`;
`Program.cs:2443-2466`, `:2614`; `Schema.cs:480-482`; `SettingsFile.cs:49-58`; `GoodEnoughPolicyTests.cs:65`;
`SettingsApplied.cs:66-75`; the three harnesses that take the policy parameter (`CodeRoundHarness.cs:109`,
`EndToEndTests.cs:148`, `ARoundsNumberComesFromTheJournalTests.cs:114`). E2's and E3's references
(`settingsShape.ts:31/:276/:404-414/:551-553/:627-631`, `package.json:349`, `sideSettings.ts:95`,
`panelView.ts:1642-1648`, `binaryFeatures.ts:18`, `extension.ts:407`, `coaiInstall.ts:345-363`, `roundsLog.ts:1791`,
`:1907`, `README.md:44-45`, `ARCHITECTURE.md:80-82`, `help.ts:163-164`, `helpContent.ts:143-149`,
`claudeSnippet.ts:119-122`, `prepare-gate.mjs:12-15`) were also checked and hold, but for the CHANGELOG line above.

**Epoch 2 (2026-11-16): all eight rows above are repaired in the parent's revised text** — the bag at `:147-157`,
`AnswerFor` at `:916-949`, both text-logging lines `:143` and `:229`, the `status`/`resolve` loads at `:567`/`:1656`,
`HeldSentence`'s one caller, a withdrawn card that shows no reason, the `consult_first` sentence naming only
`ask_consultants`, and the CHANGELOG line `:3584`. The references the revision added were opened too and hold:
`PanelService.cs:1152-1158`, `:1656`, `:567`; `AskHumanService.cs:100-101`; `Escalations.cs:243-245`;
`SettingsFile.cs:110-127` (`TryRead`); `RoundsDb.cs:849-865` (`RecordRoundRow`, the row and its key). One stale
claim was not in the parent but here: § 4 C4 said `LegacyHold.Stands` tells `ask_human`'s two held cases apart, which
U3 made untrue (a hold from before `auto` that the person answered stands as well); C4 and N3 now read the reason the
release returns (§ 10).

## 3. Conventions every story follows

- **Branch.** `feat/auto-mode-e1`, one PR for the epic. One coder at a time on the branch (§ 6 for the one exception).
- **Models.** S1: spec by **Fable**, code by Sonnet, review by Opus. S2 and S3: spec and brief by Opus (this document),
  code by Sonnet, review by Opus. Fixes by Sonnet.
- **RED first.** Every scenario below is written before its code and watched failing for the right reason (the
  assertion, not a compile error — add the empty member first when a test cannot compile without it). Then GREEN.
  Then break-it: remove the production line named in the scenario, see the stated failure, restore it. Report all
  three in the summary.
- **Existing tests keep their meaning.** S1 flips the defaults to `auto` (the record defaults, `Uniform`, an absent
  key). A test that goes red ONLY because it relied on a default meaning `human` gets `human` pinned in its setup
  (`StagePolicy.Human` named explicitly — a named policy is never auto, C2 — or `COAI_ON_EXHAUSTED=human` for an
  env-built one; the S1 spec's § S1.9 has the rewrite table and names the hold suites pinned BEFORE the flip). Its assertions
  never change. List every such test in the PR. This applies in S2 and S3 too, for tests their change reddens.
- **Temp folders.** New tests use `TempDir.For(...)`, `TempGitRepo` or `CodeRoundHarness` — never
  `Path.GetTempPath()` (the leftovers guard in `src_mcp/tests_leftovers` refuses it). A test that launches the fake CLI
  is in `[Collection("fakecli-env")]`.
- **File size.** `RoundMachine.cs` is at 782 of 800 lines and `RoundEngine.cs` is over. New logic goes into new small
  files named below; the two big files get only the call sites and the `case` arms that must live there. S1 adds at
  most twelve lines to `RoundEngine.cs` and none to `RoundsDb.cs` (the S1 spec's checklist item 14).
- **No `src_vs_code` change** in E1, and no new notice code (the extension renders codes it knows).

Commands, from the repository root (`/home/jinx/git/dew_flow_connect_other_ais`). Never `dotnet test`
(`.agents/conventions/common/testing.md`).

```bash
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore           # what CI runs first
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
#   read the exit code: a failed build leaves the previous binaries, and a suite run over them proves nothing
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class "*AutoNeverCallsAPersonTests"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests          # the whole suite
node .agents/conventions/tools/low-priority.mjs ./src_bench/CoaiBench.Tests/bin/Debug/net10.0/CoaiBench.Tests
# before the PR — the configuration CI runs (.github/workflows/ci.yml:52-83):
node .agents/conventions/tools/low-priority.mjs dotnet build dew_flow_connect_other_ais.slnx -c Release -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests
node .agents/conventions/tools/low-priority.mjs ./src_bench/CoaiBench.Tests/bin/Release/net10.0/CoaiBench.Tests
node .agents/conventions/tools/plan-lifecycle.mjs .                                        # before a commit that touches todo/
```

Filters are xUnit v3 MTP: `--filter-class "*Name"`, `--filter-method "*Name*"`. On Windows the runner ends in `.exe`.

## 4. Shared contracts — S1 delivers them; S2 and S3 rely on nothing else

Fable's spec may refine a name below. If it does, it updates this section and the S2/S3 briefs in the same commit.
**Refined 2026-11-16 by the S1 spec** (its table *Changes to § 4*, X1–X6): C1, C2 and C4 below, and the two lines of
§ 8.2 and § 9.2 that used them. **Revised the same day to epoch 2 of the parent** (the spec's X7–X10): C3 and C4 —
the released state (the parent's entry 1.2), the person's answer, the note on the held round, what a review entry
answers, and a settings read that failed. **Then by this document, the same day:** C4's U2 and U3 lines say how each
was settled, and its last paragraph keeps the `Release` at `AskHumanService.cs:100`, because after U3 `Stands`
cannot tell `ask_human`'s two held cases apart (§ 2.1).

- **C1 — the flag.** `PanelConfig` carries `bool? Autonomous { get; private init; }`, written to the session file as
  `autonomous` (`true` or `false` by this build; absent means a build before `auto`), and a read-only
  `[JsonIgnore] bool IsAutonomous => Autonomous == true`. **S2 and S3 read only `IsAutonomous`.** Invariant:
  `IsAutonomous` implies `OnExhausted == StagePolicy.GoodEnough` — kept by construction: `OnExhausted` has a
  `private init` too, a config is made in code by a constructor that writes the pair, and changed only by
  `UnderPolicyOf(current)`, which S1 alone calls. S1's own readers are `PredatesAuto` (`Autonomous is null`) and
  `LegacyHold`. The name sits beside `PanelSettings.Autonomous`
  (`COAI_AUTONOMOUS`, the autonomy ORDER — `PanelSettings.cs:474`), which is a different thing; every new comment and
  log line says "policy auto", never "autonomous", so the two cannot be read as one.
- **C2 — building a config in a test.** `PanelConfig.Uniform(max, threshold, StagePolicy.X)` with an EXPLICIT policy
  means that policy with `Autonomous == false`, so `GoodEnoughPolicyTests` keeps meaning "non-auto good_enough". An auto
  config in a test is `PanelConfig.Uniform(max, threshold)` — no policy named is policy auto, in `Uniform` and in
  `new PanelConfig()` alike (the parameter is `StagePolicy? onExhausted = null`). `with { Autonomous = … }` and
  `with { OnExhausted = … }` do not compile outside the type.
- **C3 — the settings.** `PanelSettings.FromEnvironment` gives `Rounds.IsAutonomous == true` for `auto`, absent and
  empty; `false` for `human`, `continue`, `escalate`, `good_enough`; and `Human`, `false` plus today's complaint for an
  unknown word (`UnknownSettingValueTests`). A settings file that is there and cannot be read — busy at the start,
  malformed, empty, denied — gives no values, so it reads as `auto` (parent § 6); the settings such a read gave carry
  `PolicyUnread == true`, unless the process environment names the policy. Rounds run on them. No hold is released
  on them, and the host reads the file again on the next call (the S1 spec's § S1.5.2). **S2 and S3 do not read
  `PolicyUnread`.** The note that says the file could not be read is S1's — one call that writes the Warning and the
  notice; the note for a policy pinned in the process environment stays S3's (§ 9.3).
- **C4 — the refreshed session.** Every load that decides — the begin in `RoundEngine.RunStageAsync` (`:267` and
  `:277-280`), `resolve` (`:1656`), `status` (`:567`), `ask_human` (`AskHumanService.cs:100-101`, BEFORE `_desk.Facts`)
  and the one caller of `HeldSentence` (`PanelService.WhyNotAFreshReview`, `:1152-1158`) — passes the session through
  the parent's `CurrentPolicy` before it reads the hold. Its name here is `SessionPolicy`
  (`src_mcp/src/Server/Rounds/SessionPolicy.cs`): `Current(session)` for a caller that holds the session's claim,
  `CurrentUnclaimed(session, repoPath, at)` for one that does not (it takes the claim for the release alone and reads
  the session again under it); the begin calls the two halves, `Released` before the person's decision is read and
  `LegacyHold.Stamped` after it. Each answers a `Release(Session, NotWritten)`: the session to go on with, and — for
  a hold from before `auto` that could not be released this time — why. A caller that only needs the session reads
  `.Session`. After it:
  - `Config.OnExhausted` and `Config.Autonomous` are the current settings' — **except while a hold from before `auto`
    stands** (plan round F0): that session's config is left exactly as loaded, so its marker stays absent until the
    hold is released, by `auto` or by the person's answer. A hold this build raises is stamped by the begin of its own
    round, so its marker is never absent;
  - a hold from before `auto` is released under `auto`. **The released state** (the parent's entry 1.2, § 4.3) is
    `HumanGate = false`, `HoldQuestions = []`, `AwaitingResolve = true`, `AdvanceOnResolve = true` — whether the held
    round had been resolved or not — saved, a Warning logged. S1 does not move the stage: the next `resolve` does,
    once, through `PanelService.Finish`, so the cadence closes and the decisions are recorded there. `PanelService`
    refuses a `resolve` with no decisions while findings are pending (`PanelService.cs:1675-1682`), so a caller whose
    held round still has open findings passes its decisions in that call;
  - **the notices first, the session second** (plan round F1). `Escalations.Withdraw` answers `Withdrawn`, `NotThere`
    (absent, or expired already) or `Failed`; any `Failed`, a save that fails, or a claim another call holds leaves
    the hold and its `HoldQuestions` unchanged on disk with a Warning, and the next deciding load tries again;
  - **the person's answer on file wins.** No release is written while any question of the hold has an answer on
    file; the answers are read again after the withdrawal and before the save, and an answer found either time is
    applied as today, by today's readers;
  - **the release is noted on the held round**: `Rounds[^1].Note` becomes `LegacyHold.Note` (*"released by policy
    auto: this hold was raised by a build before auto"*), its verdict unchanged, in the same save — and in
    `coai.db`'s `rounds.note`, best effort, after the save, by a one-column update of that round's row
    (`RoundNotes.Write`, `src_mcp/src/Store/RoundNotes.cs`; never `RecordRound`) — the S1 spec's U2, settled in the
    parent's § 4.3 (§ 10);
  - **a settings read that failed releases nothing** (C3): the hold stays as it is until the file can be read;
  - **what a review entry answers**, as constants of `LegacyHold`, the parent's words: after a release and until the
    `resolve`, `ReleasedSentence(stage)` — never `Unresolved`; while a release could not be written,
    `NotWrittenSentence(stage, why)` — never `GateHeld`. `why` is one of four: the settings file could not be read;
    another call holds this session; a notice could not be withdrawn; the session could not be saved;
  - a hold this build raised stays, and so does a hold from before `auto` that the person answered with Discuss or
    with words: it answers as today, and the cards the release had withdrawn are put back as they were
    (`Escalations.Restore` — the S1 spec's U3, settled in the parent's § 4.3, § 10).

  So `RoundMachine.CompleteRound` sees the current policy. In `ask_human`, **whether the policy is `auto` is read from
  the settings** (`_settings.Rounds.IsAutonomous`), not from the session. A session that is still `HumanGate` while the
  settings say `auto` is one of two held cases (the parent's § 4.4, first row; S3 words each, N3):
  - **the person's** — a hold this build raised, or a hold from before `auto` that the person answered (its release
    yields to the answer). The `Release` the load returned has an EMPTY `NotWritten`;
  - **a hold from before `auto` whose release could not be written this time** — the `Release` carries the reason
    (one of the four above).

  `LegacyHold.Stands(session.State)` does NOT tell these apart: a hold from before `auto` that the person answered
  stands as well. So S1's wiring at `AskHumanService.cs:100` keeps the whole `Release` in a local —
  `var release = _policy.CurrentUnclaimed(_store.Load(…), repoPath, at); var session = release?.Session;` — the same
  behaviour as the spec's one-line `?.Session`, and S3 reads `release?.NotWritten` without touching S1's line.
  Among the person's holds, `Stands` only picks the words (raised by this build, or answered by the person).
- **C5 — the harness.** `CodeRoundHarness.Service(...)` gains `bool autonomous = false`; with it true, `Rounds` is the
  C2 auto config (`PanelConfig.Uniform(maxRounds, threshold)`). S1 adds it (its refresh test T24 uses it).

## 5. The notes the parent left to this document

- **N2 — the bench's reader of the stored policy (S1).** `SettingsApplied.cs:66-75` learns `autonomous`. Asked `auto`
  matches only `onExhausted: "GoodEnough"` with `autonomous: true`. Asked `good_enough` matches `GoodEnough` with
  `autonomous` `false` or absent (an older server never writes it). Asked `human`, `continue`, `escalate`: compared as
  today, and a session with `autonomous: true` is a mismatch. Not asked: nothing compared, as today. Each case is a row
  in `src_bench/CoaiBench.Tests/SettingsAppliedTests.cs`. S1's spec carries these rows.
- **N3 — `ask_human` on a held gate while the policy is `auto` (S3).** The parent's § 4.4, first row: the reply tells
  the two held cases of C4 apart. In both, no card is written, nothing is recorded on the session, no consultId is
  spent, no batch is counted, and the call returns at once:
  `{"status":"no_answer_yet","answer":"","answerOriginal":"","instruction":…}` (`HumanAnswer`, no new wire member).
  Which case it is comes from the `Release` S1's load returned (C4), never from `Stands` alone:

  | The `Release` and the session | Case | The instruction |
  |---|---|---|
  | `NotWritten` empty, `HumanGate`, `LegacyHold.Stands` false | a hold this build raised — the person's | *"policy auto: coai waits for nobody, and nothing was waited for (0 seconds). This {stage} gate was held by this server under a policy that asks a person, before the setting became auto. That hold is the person's: only their answer on its ConnectOtherAIs notice (escalation {noticeId}) releases it, and no new card was written. Say in your summary that this gate is held for a person and why; work that does not need this gate may go on. Do not run the review again."* |
  | `NotWritten` empty, `HumanGate`, `LegacyHold.Stands` true | a hold from before `auto` that the person answered — the release yielded to their answer (parent § 4.3: *a person's answer on file wins*), so it is the person's | the same, with the second sentence *"This {stage} gate was held by a build before auto, and the person has answered its notice, so policy auto leaves the hold to them."* in place of the one about this server |
  | `NotWritten` not empty | a hold from before `auto` whose release could not be written yet — NOT the person's | *"policy auto: coai waits for nobody, and nothing was waited for (0 seconds). "* + `LegacyHold.NotWrittenSentence(stage, why)` (the S1 spec's § S1.3, the parent's § 4.3 words: *"policy auto is releasing this {stage} hold, raised by a build before auto; the release could not be written yet ({why}) — call again"*) + *". No card was written and nothing was shown to a person. The next call that reads this session — ask_human, status, resolve or a review — releases it."* |

  `{noticeId}` is `HoldQuestions[0]` (the round's notice, or the re-issued one); when the list is empty the
  parenthesis is left out. `{stage}` is `RoundSubject.StageName(...)`, as `HoldNotice` names it. `{why}` is the
  `Release`'s own reason, one of `LegacyHold`'s four constants, quoted, never reworded. Each case writes one
  Information line with the question id, the stage, the session id and — for the third — the reason; never the
  question's text. The same answer is given with `productionRisk: true`: the parent's "today's path" for a production
  risk is the conversation door's; on the card door today's path is a card and a wait, which requirement 5 and N3
  forbid. A hold `auto` RELEASED is not held, so it is not the card door at all: the question takes the conversation
  road (`consult_first`, or the person after the consultants), as the S1 spec's § S1.5 table, row 1, says.
- **N4 — the window between E1's and E2's releases (S3).** The CHANGELOG *Server* entry says it in one paragraph:
  *"An extension older than the next one never writes `COAI_ON_EXHAUSTED` while it shows Ask a human — that was its
  default — so this server runs Auto for it: when the rounds run out or nobody answers, no person is asked, and the
  round says `good_enough` or `not_reviewed`. Choose Ask a human again (any other value is written and honoured) to
  keep a person in the loop; the next extension release says this in a notification."* The entry is written in the
  release pull request, as every `## Server <version> — <date>` section is (`release-please-config.json` `$order`);
  S3 puts its text in the E1 PR's description so the releaser copies it.

## 6. Order, and what may run in parallel

1. **S1 first.** Fable wrote `todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md` (the cornerstone ledger already counts
   it) and revised it to epoch 2. Then a Sonnet coder builds it, in the spec's commit order (§ S1.11: 1–9 with 6a and
   6b). S2 and S3 do not start coding before S1's contracts C1–C5 are committed.
2. **S2 after S1.** It changes `RoundMachine.cs`, two new core files, `RoundEngine.cs` (`AnswerFor`, the record, the
   audit call), `FeatureSecondRound.cs`, `RoundAudit.cs`, `RoundCommands.cs`, the bench, and two hunks of `Tools.cs`.
3. **S3 after S2.** It changes `AskGate.cs`, `AskHumanService.cs`, `ServerJsonContext.cs`, two other hunks of
   `Tools.cs`, `Program.cs`, `PanelSettings.cs`, `StartupNotices.cs` and the documents; then, as the **last commit
   of the epic**, `FeaturesMode.cs`.

**The one parallel slice.** S3's pure gate (`src_mcp/core/QuestionConsult/AskGate.cs` and its new test class) touches
no file S2 touches. It may be coded in a second worktree branched from S1's last commit while S2 is coded, and merged
into `feat/auto-mode-e1` after S2's commits. Nothing else runs in parallel.

**The files, and who owns which lines** (revised for epoch 2 from the S1 spec's scope line and § S1.5). A file or a
range one story owns is not edited by another; where two stories add to one file, each adds a member of its own and
leaves the other's alone.

| File | S1 | S2 | S3 |
|---|---|---|---|
| `src_mcp/core/Rounds/LegacyHold.cs` (new) | all | — | reads `Stands`, `NotWrittenSentence` and the four reason constants; never edits |
| `src_mcp/src/Server/Rounds/SessionPolicy.cs` (new), `src_mcp/src/Store/RoundNotes.cs` (new) | all | — | — |
| `src_mcp/core/Rounds/SessionState.cs` (`PanelConfig`) | all | — | — |
| `src_mcp/core/Rounds/RoundMachine.cs` | — (spec checklist 12: no verdict, no refusal sentence changes) | all | — |
| `src_mcp/src/Server/Rounds/RoundEngine.cs` | the constructor's parameter and field (`:23-58`), the begin's hold check and stamp (`:261-280`) | `:495-536`, `:650-651`, `:916-949` | — |
| — the same file, `:93-168` (`ApplyAnyHumanDecision`, `ProceedOnFile`, `HeldSentence`, `WithTheHoldReissued`) | keeps their bodies (spec checklist 9) | — | — |
| `src_mcp/src/Server/PanelService.cs` | all (`SessionPolicy` built and handed on, `status` `:567`, `WhyNotAFreshReview` `:1152-1158`, `resolve` `:1656`) | — | — |
| `src_mcp/src/Server/AskHumanService.cs` | the constructor's `SessionPolicy`, and `:100-101` (the load, with the `Release` kept, C4) | — | everything else, including `:143` and `:229` |
| `src_mcp/src/Server/PanelSettings.cs` | `PolicyOf` `:1077-1088`, `Config` `:1679-1683`, the new `PolicyUnread` | — | the new `PinnedPolicy`, beside `WhyExhausted` `:1215-1224` |
| `src_mcp/src/Server/StartupNotices.cs` | the new `SettingsUnread` | — | the new `PinnedPolicy(…)`, placed after `SettingsUnread`; neither edits the other or `Record` |
| `src_mcp/src/Server/PanelServiceHost.cs`, `SettingsFile.cs`, `SettingsRead.cs` | all (§ S1.5.2) | — | — |
| `src_mcp/src/Server/Escalations.cs` (`Withdraw`, `Restore`, the `Writer` seam) | all | — | — |
| `src_mcp/src/Server/SessionStore.cs` | `:704` (the null-config path) | `RoundRecord.Note`'s summary, `:135` | — |
| `src_mcp/src/Store/Schema.cs` | — | the step-16 remarks, `:470-479` (no SQL) | — |
| `src_mcp/src/Store/RoundsDb.cs` | not edited (spec checklist 14) | not edited | not edited |
| `src_mcp/src/Program.cs` | not edited (spec checklist 18) | — | all (instructions, help, the pinned-policy note's call) |
| `src_mcp/src/Tools.cs` | — | `:103-104`, `:298-311` | `:341-348`, `:399-446` |
| `src_mcp/core/Cadence/CadenceState.cs`, `RoundCommands.cs`, `FeatureSecondRound.cs`, `RoundAudit.cs`, `src_bench/CoaiBench/Running/RoundRunner.cs` | — | all | — |
| `src_bench/CoaiBench/Running/SettingsApplied.cs` | all (N2) | — | — |
| `AskGate.cs`, `ServerJsonContext.cs`, `FeaturesMode.cs` | — | — | all |
| `research/module_tests.md` | its one row (spec commit 9) | a row of its own beside S1's, if it adds a flow | the same |
| `research/module_server.md`, `research/module_core.md`, the four related plans | — | — | all (§ 9.3) |

`TheGateSaysWhenToConsultTests.ReviewPlanNamesEveryVerdictTheMachineCanEmit` turns red the moment `NotReviewed`
exists, so S2's verdict list in `Tools.cs` moves in the same commit. The stories run in order on one branch, so each
starts from the previous one's commits and nothing is merged by hand but the AskGate slice.

## 7. E1.S1 — the stored policy and the hold's authority · `model: fable` (CORNERSTONE)

Scenarios and brief: **Fable**, in
[PLAN_auto_never_asks_a_person_E1_S1_spec.md](PLAN_auto_never_asks_a_person_E1_S1_spec.md) — its § S1.1 decides the
seven inputs below (D1–D7), folds the plan round's F0 and F1 (D8, D9) and, revised to epoch 2 of the parent, inputs
8–13 (D10–D15). This section is the boundary only.

**What it builds.** Parent § 4.1 and § 4.3 as revised for epoch 2, the release half of § 4.4's first row (which hold
is which; the reply's words are S3's), § 6's three rules for a settings file that cannot be read, § 8 steps 2 (the
parsing half), 3, 4 — with the tests added at the re-review — and 6, and N2. Done-when items 4, 6 and the security
half of 8; the parent's § 13 item 4.

**Inputs it must decide, each named here because the code makes it a trap:**

1. **The marker is absence.** `autonomous` absent must read as `null`, and must be read BEFORE `CurrentPolicy` stamps
   the config (parent § 4.3). Two ways the code can turn absence into a value: a positional record parameter with a
   default (`System.Text.Json` fills a missing constructor parameter with its declared default), and the null-config
   path `SessionStore.cs:704`, which builds `new PanelConfig { … }` from the record defaults. The spec says what each
   yields and a test reads a planted file with no `autonomous` and asserts `null`.
2. **A whole session file of this build through an older build's load and save** (parent done-when 6: `autonomous`
   absent, `false`, `true`, same meaning after). The road is `ASessionFileKeepsWhatANewerServerWroteTests` and the bag
   at `SessionState.cs:147-157`. `SessionStore.Load` turns a parse failure into a NEW session (`:528-530`), so the
   enum stays `GoodEnough` on disk, never a new name.
3. **Which loads take the session claim.** `begin` and `resolve` hold one (`RoundEngine.cs:252`, `PanelService.cs:1647`).
   `status` (`:559-577`), `HeldSentence` (`:1152-1158`) and `ask_human`'s load (`AskHumanService.cs:100-101`) do not.
   A release WRITES the session. The spec says under which claim each load releases, and what happens when the claim
   is busy (the parent: whichever call comes first is not refused).
4. **The hold with no recorded question.** `WithTheHoldReissued` (`RoundEngine.cs:154-168`) re-issues a notice. Under
   `auto`, for a legacy hold, it releases instead (parent § 4.3). For a hold this build raised it stays as it is.
5. **The defaults and the existing tests.** The record default, `Uniform` (`:277`), `PanelSettings.Rounds = new()`
   (`PanelSettings.cs:253`) and an absent key all become `auto`. 108 `new PanelSettings`, 108 `new PanelConfig(` and
   about 40 `Uniform(a, b)` calls in the tests rely on today's default. The spec says whether S1 pins `human` in all
   of them in one mechanical commit before the flip, or only in the ones that go red (§ 3's rule either way).
6. **Where the withdrawal's reason goes** (§ 2.1). `Expire` has no reason member. Either a reason is written into a
   member the card already shows, or the reason is in the Warning line only and the card simply leaves the active
   list. No `src_vs_code` change in E1.
7. **`CurrentPolicy`'s home.** It is called from `RoundEngine`, `PanelService` and `AskHumanService`; the last has no
   engine. A small class of its own (for example `src_mcp/src/Server/Rounds/SessionPolicy.cs`) keeps it out of the
   982-line `RoundEngine.cs`.

**Added at the re-review for epoch 2 (2026-11-16)** — the parent's § 4.3 and § 6 as they now stand:

8. **The released state.** `AwaitingResolve = true` and `AdvanceOnResolve = true`, always (the parent's entry 1.2).
   A held round the caller already resolved has `AwaitingResolve == false` (`RoundMachine.cs:754-777`), and nothing
   in S1 may advance a stage itself: `PanelService.Finish` (`PanelService.cs:1775-1810`) is the one path that closes
   the cadence and records the decisions. An empty `resolve` is refused while findings are pending (`:1675-1682`).
   The spec has `PanelService`-level tests that one `resolve` after a release advances exactly once in each of the
   four stages, resolved before or not, with the decisions kept and the code stage's cadence closed.
9. **The person's answer on file wins.** The release reads the hold's answer files before it withdraws anything and
   AGAIN after the withdrawal; an answer found either time is applied as today and no release is written. The spec
   has the race test: an answer written between the withdrawal and the second read. When the second read finds the
   answer, the cards this call withdrew are put back as they were (`Escalations.Restore`, its D16) — its U3, settled
   in the parent's § 4.3 (§ 10).
10. **The note on the held round.** `Rounds[^1]` keeps its verdict and gains the release note, in the session file
    and in `coai.db`. `RoundsDb.RecordRoundRow` is private, and `RecordRound` rewrites the whole row from a context
    the release does not hold (`RoundsDb.cs:196-210`, `:849-933`) — the spec's writer is a one-column update,
    `RoundNotes.Write` (its § S1.5.1); reported upstream as its U2 and settled in the parent's § 4.3 (§ 10).
11. **What a review entry answers.** After a release, a sentence of its own in place of `Unresolved`; while a release
    could not be written, "could not be written yet ({why}) — call again" in place of `GateHeld`. The engine has one
    hold check for every review entry (`RoundEngine.cs:267-274`), and the fresh-document check its own `switch`
    (`PanelService.cs:1152-1158`).
12. **A settings read that failed.** `SettingsFile.TryRead` answers a malformed, empty or denied file as no values
    (`SettingsFile.cs:110-128`), and the host records the stamp before its first read (`PanelServiceHost.cs:88-89`).
    Such a read releases nothing and is not recorded: busy, then a readable `human` → `human`, nothing released; a
    malformed file → nothing released until it is repaired or removed.
13. **Who owns the startup note for such a read.** S1 owns everything that decides a release; the spec says whether
    the note's words are S1's or S3's (it says S1's — its D15 — and S3 keeps the pinned-policy note of § 9.3).

**Outputs S2 and S3 rely on:** C1–C5 of § 4, and nothing else. **What S1 does not do:** no verdict changes, no
`ask_human` reply changes, no tool text, no `--features` entry. It does add words of its own, and only these: the
two sentences a review entry answers for a hold from before `auto` (input 11) and the startup note of input 12.
**Reported upstream by the spec, and settled in the parent the same day, neither counted as a change (§ 10):** U2 —
the note reaches `coai.db` by a one-column update of `rounds.note` (`RoundNotes.Write`), so commits 6a and 7 wait for
nothing; U3 — a Discuss, or words, found at the second read of input 9 gets the cards the release withdrew put back
(`Escalations.Restore`), and such a hold is the person's and answers `GateHeld` as today.

**Files it touches** (the spec's scope line; § 6 has who owns which lines): new `LegacyHold.cs`, `SessionPolicy.cs`
and `src_mcp/src/Store/RoundNotes.cs`; changed `SessionState.cs`, `PanelSettings.cs`, `SessionStore.cs:704`,
`Escalations.cs`, `SettingsRead.cs`, `SettingsFile.cs`, `PanelServiceHost.cs`, `StartupNotices.cs` (one method),
`RoundEngine.cs`, `PanelService.cs`, `AskHumanService.cs:100-101`, `SettingsApplied.cs`, tests, and its row in
`research/module_tests.md`. Not `Program.cs`, not `RoundsDb.cs`.

## 8. E1.S2 — the verdicts and their record · Opus brief, Sonnet code

**What it builds.** Parent § 4.2 and the server half of § 4.5; the cadence wording of § 4.3 (a released hold's epic
reads as released by auto); § 8 step 1; the auto twin of step 2. Done-when 1 (S1–S3 sites), 2 (all but the tool text
of S3's paragraphs and the snippet), 3 (the Serilog line and the note of a round that ran — the release's own Warning
and note are S1's).

### 8.1 The change

1. **The verdict.** In `RoundMachine.cs`, after `GoodEnough` (`:45`):

   ```csharp
   /// <summary>No reviewer answered and policy auto proceeds WITHOUT a review — never a pass, said out loud.</summary>
   public sealed record NotReviewed(GateResult Gate, ReviewerSummary Reviewers, string Reason) : RoundVerdict;
   ```

   and `GoodEnough` gains a trailing `string Reason = ""` — empty for every non-auto `good_enough`, set only by an
   auto decision. Make `FeatureCallHumanReason` (`:578`) `internal` so the new file can reuse its sentences.

2. **The decisions, in a new file** `src_mcp/core/Rounds/AutoVerdicts.cs` (`internal static class AutoVerdicts`):

   - `NobodyAnswered(SessionState s, GateResult gate, ReviewerSummary reviewers, int roundsRun)` →
     `Transition.Ok(s with { RoundsRunThisStage = roundsRun, AwaitingResolve = true, AdvanceOnResolve = true },
     new RoundVerdict.NotReviewed(gate, reviewers, $"policy auto: no reviewer answered — nothing was reviewed, and the stage proceeds without a review. {reviewers.Sentence}"))`.
     `HumanGate` is not touched.
   - `FeatureAtItsCap(SessionState next, GateResult gate, ReviewerSummary reviewers, SecondRoundGround ground, int roundsRun)`
     → state `next with { AdvanceOnResolve = true, SecondRound = SecondRoundGround.None }`; reason
     `"policy auto: " + FeatureCallHumanReason(ground, gate, reviewers, roundsRun)`; verdict `NotReviewed` when
     `reviewers.Answered == 0`, else `GoodEnough(gate, reviewers, reason)`.
   - `ExhaustedReason(GateResult gate, int roundsRun)` →
     `$"policy auto: {gate.GatingCount} finding(s) still gate after {roundsRun} round(s) — good enough"`.

3. **The three sites call it** (each a one-line branch):
   - `CompleteRound`, `:440`: `if (reviewers.Answered == 0) return s.Config.IsAutonomous ? AutoVerdicts.NobodyAnswered(s, gate, reviewers, roundsRun) : <today's return>;`
   - `CompleteFeatureRound`, `:512`: `return s.Config.IsAutonomous ? AutoVerdicts.FeatureAtItsCap(next, gate, reviewers, ground, roundsRun) : <today's return>;`
     The round-1 retry above it (`:507-510`) is NOT changed: below its cap a feature round with nobody answering still
     answers `revise` — that is not a `call_human` site, and the parent's § 4.2 row is "at its cap".
   - `Exhausted`, `:632`: a FIRST arm `_ when s.Config.IsAutonomous => new Transition.Ok(<GoodEnough's state>, new RoundVerdict.GoodEnough(gate, reviewers, AutoVerdicts.ExhaustedReason(gate, roundsRun)))`.
     It is first so that an auto config can never reach the `call_human` arm, whatever `OnExhausted` says.

4. **Every reader of a verdict, in the same commit as the record:**
   - `RoundEngine.AnswerFor` (`:926-945`), a new arm before `CallHuman`:
     `RoundVerdict.NotReviewed => ("not_reviewed", null, $"No reviewer answered — {summary.Sentence}. Policy `auto` proceeds WITHOUT a review. Call resolve (there is nothing to decide), say in your summary that this stage was not reviewed and why, then proceed.")`.
     The `GoodEnough` arm is unchanged.
   - `FeatureSecondRound.Instruction` (`:101-121`): before the `GoodEnough g` arm, `RoundVerdict.GoodEnough { Reason.Length: > 0 } g =>`
     *"{g.Reason}. Under policy auto no person is called: READ the open findings and apply the ones that are true and
     useful — as new pull requests, since the epics have merged — reject the rest with reasons via resolve, and say in
     your summary what you took and what you declined; the review closes on resolve."* And `OneRoundUnless` gets an
     auto twin used when `after.Config.IsAutonomous`: *"A second round runs only for a reviewer failure or a blocking
     finding — at most two rounds, ever (D23); under policy auto no person is asked for one."* `NotReviewed` falls
     to `_ => string.Empty`, so the engine's sentence stands.
   - `RoundCommands.MayProceed` (`RoundCommands.cs:16-17`): add `or RoundVerdict.NotReviewed`.
   - `RoundRunner.Passed` (`src_bench/CoaiBench/Running/RoundRunner.cs:30-31`): add `or "not_reviewed"`; its summary
     says "the four the server acts on".
   - `CadenceState.cs:3-7` and `:48`. The verdict an epic is closed with is `Rounds[^1].Verdict` (§ 2), and after a
     release of a hold from before `auto` that round keeps its verdict — `call_human`, or `escalated` — with the note
     *"released by policy auto: this hold was raised by a build before auto"* (`LegacyHold.Note`, the parent's § 4.3);
     the next `resolve` moves the code stage to done through `Finish` and the cadence records that verdict (the S1
     spec's T57 pins the close, once). So the wording must not read it as a person's decision:
     - `ClosedEpic`'s summary: *"An epic whose code stage moved to done — the gate passed, policy auto proceeded
       without a review, or policy auto released a hold a build before auto had raised."*
     - its `Verdict` param: *"The verdict of the round that moved its code stage to done: proceed, good_enough,
       continue_anyway or not_reviewed — or call_human or escalated when policy auto released that round's hold (its
       note says "released by policy auto"): such an epic was released by auto, not passed by a person's decision."*
     - `IsClosed`'s summary: *"Whether an epic's code stage has moved to done."*
     No logic changes, and no test of its own: nothing renders the stored verdict (§ 2), and S1's T57 pins the close.
   - `Tools.cs:103-104`: `verdict (proceed | revise | continue_anyway | good_enough | not_reviewed | call_human | escalated)`.
   - `Tools.cs:298-311` (`review_feature`): the second round's last two sentences become true under every policy —
     *"… or the person asks for one — through `ask_human` with `feature`, answered on their panel with 'Keep going —
     more rounds' (never under policy `auto`, which asks no person); … A second round that still carries a `blocking`
     finding, or fails again, is `call_human`, which does block — under `auto` it is `good_enough`, or `not_reviewed`
     when nobody answered. A third round is never run."* Keep "or phone" absent.

5. **The record, in a new file** `src_mcp/src/Server/Rounds/AutoNote.cs`:
   `internal static string Of(RoundVerdict verdict) => verdict switch { RoundVerdict.NotReviewed n => n.Reason, RoundVerdict.GoodEnough { Reason.Length: > 0 } g => g.Reason, _ => string.Empty };`
   In `RoundEngine.cs:525-536` add `Note = AutoNote.Of(completed.Verdict),` to the `with { … }`. `LoggedNote` then
   carries it to `coai.db` (`RoundsDb.cs:923`) with no schema step.

6. **The log line.** `RoundAudit` gains:

   ```csharp
   /// <summary>What policy auto decided for this round, and why — a Warning when nothing was reviewed.</summary>
   public void Autonomous(string verdict, string reason)
   ```

   Nothing for an empty reason. `verdict == "not_reviewed"` → `_log.Warning`, else `_log.Information`, with the template
   `"round {Round} {Stage} {Verdict} under policy auto: {Reason}"` and `ForALogLine(reason)`. Called in
   `RoundEngine.cs` right after `audit.Closing` (`:650`): `audit.Autonomous(answer.Verdict, record.Note);`.

7. **The docs on the record.** `RoundRecord.Note`'s summary (`SessionStore.cs:135`): "Why a SKIPPED round did not run,
   or what policy auto decided for a round that ran — empty otherwise." One sentence of the same in the `Schema.cs`
   step-16 remarks (`:470-479`); no SQL changes.

### 8.2 Scenarios — RED first

**`AutoNeverCallsAPersonTests`** (new, `src_mcp/tests/AutoNeverCallsAPersonTests.cs`, pure machine). Helpers:
`Auto(int maxRounds, Stage stage)` = `new SessionState("s", "D:/r", "main", PanelConfig.Uniform(maxRounds, 0)) { Stage = stage, PlanProceeded = stage == Stage.CodeReview }`
(a document session also sets `Document = "doc.md"`; a feature session `Feature = "todo/PLAN_x.md"` and
`Branch = SessionKey.FeatureBranch`). `Nobody = new ReviewerSummary(2, 0, ["codex: exit 1", "gemini: exit 1"])`.

| # | Test | Input | Expected | Break-it |
|---|---|---|---|---|
| 1 | `NobodyAnswered_IsNotReviewed_AtOnce` (Theory: PlanReview, CodeReview, DocumentReview × budget 1, 3) | `CompleteRound(Auto(b, stage), GateResult.Empty, Nobody)` | `NotReviewed`, `Reason` contains "no reviewer answered" and "codex: exit 1"; `HumanGate` false; `AwaitingResolve` true; `AdvanceOnResolve` true; `RoundsRunThisStage` 1 | drop the `IsAutonomous` branch at `:440` → `CallHuman` |
| 2 | `NotReviewed_ThenAnEmptyResolve_OpensTheCodeStage` | #1 on PlanReview, then `Resolve(state, [])`, then `BeginCodeRound` | `Moved`, `Stage == CodeReview`, `PlanProceeded`; the begin is `Moved`, not "plan gate comes first" | set `AdvanceOnResolve = false` in `NobodyAnswered` |
| 3 | `FeatureAtItsCap_ABlockingFinding_IsGoodEnough` (budget 1 round 1; budget 2 round 2 with `SecondRound = BlockingFinding`) | one `Blocking` finding, both answered | `GoodEnough`, `Reason` starts "policy auto:" and contains "blocking"; `HumanGate` false; `SecondRound` None; `AdvanceOnResolve` true | drop the branch at `:512` → `CallHuman` |
| 4 | `FeatureAtItsCap_AFailure_IsGoodEnough` | round 2, `new ReviewerSummary(2, 1, ["gemini/FeatureReview: exit 1"])`, one Major | `GoodEnough`, reason names the failure | as #3 |
| 5 | `FeatureAtItsCap_NobodyAnswered_IsNotReviewed` (budget 1 round 1; round 2) | `Nobody` | `NotReviewed`, `HumanGate` false | swap the `Answered == 0` test in `FeatureAtItsCap` |
| 6 | `FeatureRoundOne_NobodyAnswered_UnderItsCap_StillRetries` | budget 2, round 1, `Nobody` | `Revise`, `SecondRound == ReviewerFailure` — unchanged | — (a guard on the unchanged path) |
| 7 | `Exhausted_IsGoodEnough_WithItsReason` | `Auto(1, PlanReview)`, two Major findings, threshold 0, both answered | `GoodEnough`, `Reason` contains "still gate after 1 round(s)" | drop the first arm of `Exhausted` → the `GoodEnough` arm answers with an empty `Reason`, and the reason assertion fails |
| 8 | `UnderEveryOtherPolicy_NobodyAnsweredStillCallsAPerson` (Theory: Human, Continue, Escalate, GoodEnough — each named in `Uniform`, so none is auto) | `Nobody` | `CallHuman`, `HumanGate` true — today's | — |
| 9 | `ANonAutoGoodEnough_CarriesNoReason` | `Uniform(1, 0, GoodEnough)`, two Majors | `GoodEnough` with `Reason == ""` | — |
| 10 | `NotReviewedMayProceed` | `RoundCommands.MayProceed(new RoundVerdict.NotReviewed(GateResult.Empty, Nobody, "x"))` | true | remove the `or` |

**`GoodEnoughPolicyTests`** gains `WhenNobodyAnswered_UnderAuto_ItIsNotReviewed` beside `:65` (the parent's auto
twin); `:65` itself stays and stays green. **`VerdictInstructionTests.EveryVerdictInTheUnion_IsHandledByTheAnswer`**
is the RED for `AnswerFor`: it fails the moment `NotReviewed` exists without its arm.
**`TheGateSaysWhenToConsultTests.ReviewPlanNamesEveryVerdictTheMachineCanEmit`** is the RED for `Tools.cs:103-104`.

**`FeatureSecondRoundUnderAutoTests`** (new, pure): `FeatureSecondRound.Apply(answer, new RoundVerdict.GoodEnough(gate, reviewers, "policy auto: a blocking finding still stands"), reviewers, after, [])`
with `after.Config` auto → the instruction contains "no person is called" and contains neither "none is blocking" nor
"the person asks". The same verdict with an empty reason and a non-auto `after` → today's sentence, unchanged.

**`AutoRoundsAreWrittenDownTests`** (new, `[Collection("fakecli-env")]`, `CodeRoundHarness`, a `ListSink` logger):

| # | Test | Setup | Expected |
|---|---|---|---|
| 11 | `APlanRoundNobodyAnswered_IsNotReviewed_AndWrittenDownThreeTimes` | `Service(autonomous: true, log: sink)`; `FAKECLI_STDOUT`/`FAKECLI_OUTFILE_TEXT` empty, `FAKECLI_EXIT=1`, `FAKECLI_STDERR=boom` (no `429`: that path retries); `OpenAsync`, `ReviewPlanAsync` | `verdict == "not_reviewed"`; instruction contains "WITHOUT a review"; no notice file under `<data>/escalations`; the session file's last round `note` and `coai.db`'s `rounds.note` (read as `ARoundsNumberComesFromTheJournalTests.cs:141-152` does) both contain "no reviewer answered"; one Warning line containing "not_reviewed under policy auto"; then `ResolveAsync("[]")` answers stage `CodeReview`, and `ReviewCodeAsync` is not refused with "plan gate comes first" |
| 12 | `AnAutoGoodEnough_IsAnInformationLineAndANote` | `SetAnswer(OneMajor)`, `Service(threshold: 0, maxRounds: 1, autonomous: true, log: sink)`, a plan round | `good_enough`; note contains "still gate after 1 round(s)"; one Information line "good_enough under policy auto" |
| 13 | `UnderHuman_TheRoundGetsNoAutoNote` | #11's setup with `autonomous: false` | `call_human`; note empty; no line containing "under policy auto" |

**Bench:** `PlanLoopTests.TheVerdictsThatOpenTheCodeStage` gains `[InlineData("not_reviewed")]`.

**Must stay green, unchanged in their assertions:** `GoodEnoughPolicyTests` (all), `HumanGateHoldsTests`,
`OnlyAPersonReleasesTheGateMachineTests`, `OnlyAPersonReleasesTheGateTests`, `TheSecondFeatureRoundTests`,
`AFeatureRoundIsSkippedNotRefusedTests`, `PanelServiceTests.MaxRoundsExhausted_YieldsCallHuman_WithTheOpenCount`,
`ADocumentIsReviewedEndToEndTests.ADocumentRoundNobodyAnswered_AsksThePersonAboutTheDocumentGate_NotAboutDone`,
`RoundAuditTests`, `SecurityLaneRoundTests`, `SecurityLaneBudgetTests`, and the whole bench suite. Any of them that goes
red only because S1 made the default `auto` gets `human` pinned (§ 3).

## 9. E1.S3 — `ask_human` never blocks, the words, the features entry · Opus brief, Sonnet code

**What it builds.** Parent § 4.4 — its first row's two held cases included (N3) — the server items of § 4.6, § 4.7
(server), § 4.8's server documents, § 7's four boundary tables; § 8 steps 5, 7 (the features entry) and 8. Done-when
1 (S5, S6), 2 (the tool text), 3 (no question text in a log), 5, 7. Every text it writes is true under every policy,
and beside the two sentences S1 adds for a review entry after a release (`LegacyHold.ReleasedSentence` and
`NotWrittenSentence`, the parent's § 4.3), which S3 quotes and never rewords. **Not S3's:** the startup note for a
settings file that exists and could not be read — S1 words, writes and tests it (`StartupNotices.SettingsUnread`,
called by `PanelServiceHost`; the spec's § S1.5.2 and D15).

### 9.1 The pure gate (may run beside S2 — § 6)

In `AskGate.cs`:

- `AskGateInput` gains `public bool Autonomous { get; init; }` (default false: every existing table row is unchanged).
- `AskDecision` gains `public sealed record ConsultFirst(string Note) : AskDecision;` — `Note` is the rejected-consultId
  clause or empty.
- `Decide`, in this order: (1) a production risk with no reason is refused, as today, in every mode and policy;
  (2) under `Autonomous` on the **card door**: `Allowed(string.Empty, Counted: false, ConsultBeside: false)` whatever
  else is set — the service answers it at once (§ 9.2) and no consultant runs beside a card that is never written;
  (3) under `Autonomous` on the **conversation door** with `ProductionRisk`: exactly today's decision for the same
  input with `Autonomous = false`; (4) under `Autonomous`, conversation door, no risk — a `Verified` proof →
  `Allowed(verified.Note, Counted: CountsToday(input), ConsultBeside: false)`; else a non-empty `Unavailable` →
  today's stand-down sentence (`:154-159`) with `Counted: CountsToday(input)`; else `ConsultFirst(Rejected(input).Trim())`.
  `CountsToday(input)` is `input.Phase == AskPhase.Building && input.Mode != QuestionMode.Off && !input.PhaseUnreadable`
  — the batches stay a record of what reached the person, so a later switch back to `human` reads true counts.
  The phase rule and the free batches do not decide anything under `auto`: the owner's O3 is unconditional.
- Keep each method small: put the auto table in its own private method, `Autonomously(AskGateInput input)`.

**`AskGateUnderAutoTests`** (new, pure):

| # | Test | Input | Expected |
|---|---|---|---|
| 1 | `TheAIsOwnQuestion_WithNoProof_IsConsultFirst` (Theory: every mode × Planning, Building, Released; and `PhaseUnreadable`) | `Autonomous`, conversation door | `ConsultFirst`, empty note |
| 2 | `ARejectedProof_IsConsultFirst_AndSaysWhy` | `Proof = new ConsultProof.Rejected("abc", "too old")` | `ConsultFirst`, note contains "abc" and "too old" |
| 3 | `AVerifiedProof_ReachesThePerson` | `Proof = new ConsultProof.Verified("abc", "")`, Building, Require | `Allowed`, `Counted` true; in Planning `Counted` false |
| 4 | `NoConsultantCanBeHad_StandsDown` | `Unavailable = "switched off"` | `Allowed`, note contains "stood down" and "switched off" |
| 5 | `AProductionRisk_IsTodaysDecision` (Theory over the existing risk rows) | `ProductionRisk`, a reason | equals `Decide(input with { Autonomous = false })` |
| 6 | `ARiskWithNoReason_IsStillRefused` | `ProductionRisk`, no reason | `Refused` |
| 7 | `TheCardDoor_UnderAuto_RunsNoConsultant` | card door, with and without `ProductionRisk` | `Allowed`, `ConsultBeside` false, `Counted` false |

Break-it for #1: return `Batches(input)` instead of the auto table → #1 fails in Planning and in Building batch 0.
`AskGateTests` (every existing row) stays green unchanged.

### 9.2 The service

In `AskHumanService.AskHumanAsync` (`:85-108`), after S1's refreshed load (`release` and `session`, C4):

```csharp
var autonomous = _settings.Rounds.IsAutonomous;      // the SETTINGS, not the session — C4
var facts = _desk.Facts(...);
facts = facts with { Input = facts.Input with { Autonomous = autonomous } };
return AskGate.Decide(facts.Input) switch
{
    AskDecision.Refused refused => Error(refused.Sentence),
    AskDecision.ConsultFirst first => ConsultFirst(first.Note),
    AskDecision.Allowed when autonomous && facts.Input.Door == AskDoor.Card
        => AnsweredAtOnce(session!, release?.NotWritten ?? string.Empty),
    AskDecision.Allowed allowed => await AskedAsync(...),     // today's two doors
    _ => throw new InvalidOperationException("the union is closed"),
};
```

(`AskFacts` is a record, `AskGateDesk.cs:25`.) No session means no
card door (`AskGateDesk.cs:156-157`), so `session!` is safe there; say so in a comment.

- **`ConsultFirst(string note)`** mints an id (`NewQuestionId()`), logs
  `_log.Information("question {Id} under policy auto: consult_first — the consultants come before the person", id)`,
  and returns `ConversationAnswer(ConversationAnswer.ConsultFirst, ConsultFirstInstruction, note, string.Empty, [])`.
  Nothing is spent, counted, posted or recorded. `ConsultFirstInstruction` is the parent's § 4.4 sentence:
  *"The person says: call the consultants — ask_consultants — talk it through, and come
  back to me only if the two of you did not settle it. Then call ask_human again with the consultId."*
  (Settled 2026-11-16, § 10 item 1: only `ask_consultants` is named, because only its id opens the door.)
- **`AnsweredAtOnce(PersistedSession session, string notWritten)`**: on a held session, N3's reply (§ 5) for the
  case the `Release` names — `notWritten` not empty → the "call again" reply, with
  `_log.Information("question {Id} under policy auto: the {Stage} hold of session {SessionId} is from before auto and its release could not be written yet ({Why}) — answered at once: call again", …)`;
  empty → the person's reply (its second sentence chosen by `LegacyHold.Stands(session.State)`), with
  `_log.Information("question {Id} under policy auto: the {Stage} hold of session {SessionId} is the person's — answered at once, pointing at its notice {Notice}", …)`.
  On a feature session in its request window (not held), `HumanAnswer("no_answer_yet", "", "", NoSecondRoundUnderAuto)`
  with the parent's sentence *"policy auto: a second feature round runs only on a ground the server finds; this
  review closes on resolve. Nothing was shown to a person and nothing was waited for."* and an Information line with
  the id. Neither writes a card, records a question, spends a proof or counts a batch.
- **`ServerJsonContext.cs:349-353`**: `public const string ConsultFirst = "consult_first";` beside `AskInConversation`;
  the `Status` param doc says it is one of the two.
- **No question text in any log line.** `:143` becomes
  `"question {Id} is the caller's own — asked in its conversation, not in VS Code"` and `:229`
  `"escalating {Id} to a person"`. The text stays on the card and in the consult record, where it was always kept.

**`AskHumanUnderAutoTests`** (new; copy `AskHumanServiceTests.Build` and give it a `bool autonomous` that sets
`Rounds` to the C2 auto config, and a `ListSink` logger):

| # | Test | Setup | Expected |
|---|---|---|---|
| 1 | `TheOwnQuestion_IsConsultFirst_AndNothingIsSpent` | auto; `Proceeded(h)`; no consultId | `status == "consult_first"`; instruction contains `ask_consultants` and `consultId`; `Cards(h) == 0`; `Batches(h) == 0` |
| 2 | `BeforeAnySession_TooItIsConsultFirst` | auto; no session saved | `consult_first` |
| 3 | `TheConsultantsId_ReachesThePerson` | auto; `Proceeded(h)`; `consultId = await Consulted(h)` | `ask_in_conversation`; the consult record is spent (`h.Questions.Store.Read(id)!.EscalationId` not empty) |
| 4 | `NoConsultant_StandsDownToThePerson` | auto; `Build(enabled: false)` | `ask_in_conversation`; note contains "switched off" |
| 5 | `AProductionRisk_AsksTheConsultantsFirst_AsToday` | auto; `productionRisk: true`, a reason | `ask_in_conversation` with `consultantAnswers`; `Cards(h) == 0` |
| 6 | `AHoldThisBuildRaised_IsAnsweredAtOnce_PointingAtItsNotice` | auto settings; a held session saved with `Config = PanelConfig.Uniform(3, 2, StagePolicy.Human)` (a named policy: the marker is `false`), `HoldQuestions = ["n-1"]`; `EscalationBudget = 10 min` | returns in under 5 s (a `Stopwatch`); `status == "no_answer_yet"`; instruction contains "n-1" and "nothing was waited for"; `Cards(h) == 0`; the session still has `HumanGate` and `HoldQuestions == ["n-1"]` |
| 7 | `AFeatureRequestWindow_IsToldTheServerDecides` | auto; a feature session, `RoundsRunThisStage = 1`, `SecondRound = None`, not held | `no_answer_yet`; instruction contains "runs only on a ground the server finds"; `Cards(h) == 0`; `RequestQuestions` empty |
| 8 | `NoLogLineCarriesTheQuestion` (Theory: auto conversation; human conversation; human held card with a 300 ms budget) | the question `"Is sk-ant-api03-SECRETSECRETSECRET the right key?"` | the sink's lines contain the question id and never `SECRETSECRETSECRET` |
| 9 | `AHoldFromBeforeAutoNotYetReleased_IsToldToCallAgain` | auto settings; a held session saved with a config that has NO marker (built as the S1 spec's tests build one, `autonomous: null`), `HoldQuestions = ["n-1"]` and its notice open; the session's claim held by the test (`SessionClaim.TryTake`) for the call; `EscalationBudget = 10 min` | under 5 s; `no_answer_yet`; instruction contains `LegacyHold.NotWrittenSentence(<stage>, LegacyHold.ClaimBusy)` and "call again", and not "the person's"; `Cards(h) == 0`; the session on disk still held with `HoldQuestions == ["n-1"]`. Then the claim disposed and the same call again: `consult_first` (S1 released the hold; not the card door) |
| 10 | `AHoldFromBeforeAutoThePersonAnswered_IsThePersons` | #9's session, no claim held, an answer with words only written for `n-1` (as `TheAnswerBelongsToTheCurrentHoldTests` writes one) | under 5 s; `no_answer_yet`; instruction contains "the person has answered its notice" and "n-1", and not "call again"; `Cards(h) == 0`; the session still held |

Break-it for #6: route the auto card door to `AskedAsync` → the call waits 10 minutes (the test's own timeout,
`--timeout 2m` on the run, fails it) and a card appears. Break-it for #9 and #10: choose the case by
`LegacyHold.Stands(session.State)` instead of the `Release`'s reason → #10 answers "call again" for a hold the person
answered (the reason C4 keeps the `Release`). **Must stay green unchanged:** `AskHumanServiceTests`,
`AskHumanScenarioTests`, `AQuestionIsTheGatesTests`, `TheAnswerBelongsToTheCurrentHoldTests`,
`ThePersonAsksForASecondFeatureRoundTests` (pin `human` where S1's default reddens them, § 3).

### 9.3 The words

- **`Tools.cs:341-348` (`resolve`).** Open the paragraph with *"Under a policy that asks a person, a call_human
  verdict holds the gate; policy `auto` raises no hold, and `not_reviewed` and `good_enough` are resolved like
  `proceed`."* Keep "is not a decision", "refused while a gate is held" and "answer on the notice card" (pinned by
  `TheServerSaysWhatOnlyTheRuleSaidTests:91-105`). Close it with the release, so a caller that meets S1's sentence
  finds the same instruction here: *"Policy auto also releases a hold left by a build before auto, and every review
  call then says so: call resolve — with your decisions, or none — and the stage advances, once."* (With findings
  still pending, "none" is refused, as today — `PanelService.cs:1675-1682`; the sentence says "your decisions" first.)
- **`Tools.cs:399-446` (`ask_human`).** Add one paragraph before *THE GATE'S QUESTION*: *"UNDER POLICY `auto` (the
  default of When the rounds run out) this call never waits and writes no card. Your own question without a
  `consultId` is answered `status: "consult_first"` — ask the consultants, settle it with them, and come back with
  the consultId only if you did not. A held gate is answered `no_answer_yet` at once: a hold the person owns names
  its notice; a hold left by a build before auto that could not be released yet says why — call again."* Then
  qualify *"THIS CALL BLOCKS"* with *"under any other policy"*, and the closing *"`no_answer_yet` means nobody was at
  the keyboard: ask the person directly … and wait"* with *"under any other policy; under `auto` it comes at once,
  and its instruction says what to do instead"*. Keep the phase-rule paragraph whole and word for word, "15 minutes
  by default" and "expired" (`TheGateSaysWhenToConsultTests:143-177`). The reply list at the end names four
  statuses: add `consult_first`.
- **`Program.cs:2443-2466` (the instructions).** The text is 1,935 of 2,000 characters. This version is 1,995 (counted
  as the raw string reads, LF line ends, 2026-11-16) and keeps every pinned phrase — "COMMANDS", "outrank", "empty
  list", "Reject in round ONE", "converging", "call_human", "REFUSE", "does not clear it", "ENFORCED stop", "answer
  on the notice card", "not any argument of yours", the protocol's tool names, and the closing "the `consult`
  PROMPT."; use it or one no longer. Item 3 is true under every policy: `call_human` and its hold come only from a
  policy that asks a person, and the one hold `auto` releases — one a build before `auto` raised — is released with
  a sentence that says so (S1's two review-entry sentences); "a card for the gate's question" stays true, because a
  gate's question is only ever a hold's notice card, which `auto` never writes and keeps for a hold the person owns.

  ```text
  ConnectOtherAIs: a review gate run by OTHER vendors' models over your plan and your code.
  It is ADDITIONAL: run your own review as you would have, and start both AT ONCE. A
  different vendor's model answers what your own is worst placed to answer; your reviewers read
  the change in context. A `call_human` verdict stops the SHIPPING, not the task.
  The protocol, in order: `open` a session for the repo+branch. `review_plan` sends the plan to
  every enabled provider; `resolve` records an accept/reject for EVERY finding (a rejection needs
  a reason). Repeat until the verdict is `proceed`, implement, then `review_code`, `resolve`,
  fix — the same loop. `review_code` REFUSES until a plan round reached `proceed`.
  `providers` says what is configured; `status` re-orients a resumed conversation;
  `ask_consultants`, then `ask_human`: a card for the gate's question, `ask_in_conversation` for yours.
  Three rules:
  1. A reply can carry COMMANDS — a `commands` list from the operator's panel switches, with a
     preamble. They outrank your own defaults and are about HOW to work. Say which you applied.
     An empty list is the default, not a fault.
  2. Reject in round ONE, not when the rounds run out. Accepting to be agreeable stops the loop
     converging: each accepted finding rewrites the plan, so the next round is handed fresh text
     and the count never falls.
  3. `call_human` comes only from a policy asking a person; under the default `auto` an
     unreviewed round says `not_reviewed`, and an older build's hold is released, saying so.
     It is an ENFORCED stop: `review_plan` and `review_code` refuse while it stands and `resolve`
     does not clear it. Only an answer on the notice card releases it — not any argument of yours.
  `consult` is for when YOU are stuck: an independent model reads this checkout read-only with its
  uncommitted diff and answers advice, not orders — verify it, then report back on the same
  consultationId. The person can ask for one themselves with the `consult` PROMPT.
  ```

  `TheInstructionsFitWhatAClientKeepsTests` and `TheServerSaysWhatOnlyTheRuleSaidTests` are the checks.
- **`Program.cs:2614` (the help).** `COAI_ON_EXHAUSTED (auto|human|continue|good_enough|escalate; absent = auto)`.
- **The pinned-policy startup note** (parent § 4.6, server half). In `PanelSettings.cs`, beside `WhyExhausted`
  (`:1215-1224`): `internal static IReadOnlyList<UnrecognisedSetting> PinnedPolicy(Func<string, string?> processEnv)`
  — when the PROCESS environment holds a `COAI_ON_EXHAUSTED` that is a known word other than `auto`, one
  `UnrecognisedSetting(Key.Exhausted, …)` saying *"COAI_ON_EXHAUSTED is '{value}' in this server's own environment —
  your MCP client's `env` block — which overrides the panel; Auto is the default now. Remove it from that `env` block
  to follow the panel."* An unknown word is left to `WhyExhausted`, so one value is never two notes. Written at
  `Program.cs:2189`, after `StartupNotices.Record`, through the existing `unrecognised-setting` code (no new code):
  `StartupNotices` gains `PinnedPolicy(IReadOnlyList<UnrecognisedSetting>, Noticing, ILogger)` that logs each at
  Warning and offers it as `Unrecognised` does (`StartupNotices.cs:69-81`). Test in
  `TheStartupNotesAreWrittenDownTests` style: `human` in the process env → one note, subject `COAI_ON_EXHAUSTED`;
  `auto`, absent and `nonsense` → none; the same value only in the settings file → none. This is the only startup
  note S3 adds. The note for a settings file that exists and could not be read is S1's — another fact, on another
  subject (the file's path), written by `PanelServiceHost` through `StartupNotices.SettingsUnread` (the spec's
  § S1.5.2, D15); S3's method sits after it in `StartupNotices.cs`, does not call it, and its test does not assert it.
  When both hold (an unreadable file and a policy in the process environment), each writes its own note.
- **Documents.** `research/module_server.md`: replace the paragraph at `:2304-2305` ("Only the `human` policy raises
  the gate") with the five policies and what each does at the three sites, `auto` first, then the release of a hold
  from before `auto` as built (the marker read before the stamp, the person's answer first, the notices before the
  save, the note on the held round, one `resolve` to move on, a settings read that failed releases nothing); add
  `auto` and the absent meaning at `:1862`; and, handed on by the S1 spec (§ S1.8), one paragraph in § *Persistence*:
  `autonomous` absent is a meaning, not a default, and a release that renames it must keep reading the old name. `research/module_core.md`: add the auto outcomes to the feature flowchart and bullets at
  `:436-470`. The boundary table of the parent's § 7, shortened, in each of
  [PLAN_only_a_person_releases_the_gate.md](PLAN_only_a_person_releases_the_gate.md),
  [PLAN_a_signed_answer_releases_the_gate.md](PLAN_a_signed_answer_releases_the_gate.md),
  [PLAN_a_cancelled_review_stops_its_round.md](PLAN_a_cancelled_review_stops_its_round.md) and
  [PLAN_review_plan_again_after_proceed.md](PLAN_review_plan_again_after_proceed.md) — a boundary section, not a
  change to their scope (the both-sides rule). Run `plan-lifecycle.mjs` after.
- **The CHANGELOG *Server* entry**: drafted in the PR description, with N4's paragraph (§ 5), the five values, the
  sentence that a value chosen before is reset by the NEXT extension, not by this server, one sentence on the release
  of a hold an older server raised (released under Auto unless the person answered it, noted on its round), and one
  on a settings file that exists and cannot be read (Auto by default, a note, nothing released until it reads).

### 9.4 The last commit of the epic

Append to `FeaturesMode.Listed` (`FeaturesMode.cs:49`):

```csharp
// The policy `auto` for When the rounds run out, the meaning of an absent COAI_ON_EXHAUSTED: no hold, no wait, no card;
// not_reviewed, consult_first (todo/PLAN_auto_never_asks_a_person.md, E1).
"onExhaustedAuto",
```

with a test beside the others (`FeaturesMode.Listed.Should().Contain("onExhaustedAuto")`, in
`AutoNeverCallsAPersonTests`). Only after every scenario of S1, S2 and S3 is green in Release, the whole suite and the
bench suite are green, and `dotnet format --verify-no-changes` is clean. Nothing follows it on the branch but review
fixes, which rebase under it or come before it.

## 10. Reported to the coordinator, and how each was settled

1. **The `consult_first` sentence offered a road that cannot open the door.** Parent § 4.4's reply named "ask_consultants,
   or consult for this checkout" and then "the consultId". The desk accepts only an `ask_consultants` record as proof
   (`AskGateDesk.cs:178-191`); a `consult` id is rejected and the caller gets `consult_first` again. **Settled
   2026-11-16 as a wording repair, not a change:** requirement 5, § 4.4's third row and O3 already require "the
   consultants' `consultId`" that the desk accepts; the sentence now names only `ask_consultants` (parent and § 9 here).
   Both question consultants (consultation `8e8e4b7d…`) read it the same way; teaching the desk to accept a `consult` id
   would have been a contract change and is not done.
2. **U1 — the released state (from the S1 spec).** Epoch 1's § 4.3 kept `AwaitingResolve` on release; a held round
   the caller had already resolved has it `false`, so `AdvanceOnResolve` had nothing to consume it — a later non-auto
   round advanced on a plain `resolve`, and a feature review held at its cap never closed. **Settled as a change: the
   parent's entry 1.2** (contract), its second change, which stopped epoch 1 for the whole re-review. Epoch 2's § 4.3
   fixes the released state at `AwaitingResolve = true, AdvanceOnResolve = true`, consumed by one `resolve` through
   `PanelService.Finish`; C4 and § 7 input 8 here, the spec's D10, J11 and T19–T21, T56–T58 are written to it.
3. **U2 — the writer of the note in `coai.db` (from the S1 spec).** The parent named `RoundsDb.RecordRoundRow`, which is
   private; its one caller, `RecordRound`, rewrites the whole row, the reviewers and the findings from a `RoundContext`
   the release does not hold. **Settled 2026-11-16 as a reference repaired, not a change:** what the parent asks — the
   note in the round's existing `rounds.note`, no schema step, the findings not re-recorded — is unchanged; only the
   named writer was wrong. The parent's § 4.3 now says *a one-column update*; the spec builds it as
   `RoundNotes.Write` (§ S1.5.1). C4 and § 7 input 10 here.
4. **U3 — the cards of an answer that raced (from the S1 spec).** A Discuss, or words, found at the second read is
   applied as today and keeps the hold — but the release had just withdrawn its cards, so the person had nothing left
   to press. **Settled 2026-11-16 in the parent as a non-change:** "applied as today" already includes the card the
   person answers on, so restoring the withdrawn cards satisfies § 4.3 and requirement 4 ("unless the person answered
   it first") as written; the parent's § 4.3 now says so in words, and the spec builds `Escalations.Restore` (D16).
   Such a hold is the person's and answers `GateHeld` as today. C4, § 7 input 9 and N3's second row here.

Settled here as a fix that satisfies the parent as written (not a change): N2–N4 (§ 5); the feature round-1 retry stays
`revise` (§ 8.1, item 3); the second text-logging line `:229` (§ 2.1); the instructions trimmed to fit the 2,000
budget (§ 9.3); the CHANGELOG entry written in the release PR (§ 5, N4); the auto wording of
`FeatureSecondRound` (§ 8.1, item 4). For epoch 2: N3's two held cases told apart by the `Release`'s reason, and S1's
line at `AskHumanService.cs:100` keeping that `Release` (C4) — the parent's § 4.4, first row, as written, which
`Stands` alone could not deliver once U3 left a person-answered hold from before `auto` standing; the cadence wording
for a released hold (§ 8.1, item 4; the parent's § 4.2 and § 4.3); the startup notes split, S1's for a file that
could not be read and S3's for a pinned policy (§ 9.3; the spec's D15). At build, 2026-11-17, after the Opus
judges of S1–S3: a `not_reviewed` round whose security lane answered says that the lane's findings await a decision,
instead of "nothing to decide" or "nothing was reviewed", because `resolve` refuses an empty decision list there.
`review_feature`'s text names `not_reviewed` for a retry nobody answered (§ 8.1). Under `auto` the stand-down note of
`ask_human` names a rejected consultId (D14(a)) and drops "required here". § 9.3 left this open, and every other
policy keeps today's words byte for byte. § 9.2's tests 9 and 10 assert the card count unchanged rather than
`Cards(h) == 0`, because their setup posts the hold's notice. The S1 spec's residual (d) and D13 are kept by code: a
notice is put back only under the session's claim, read again, and a released hold with no rounds still names its
release.

## 11. Definition of Done for E1

- [ ] S1's spec — written by Fable, revised to epoch 2 — built; T1–T67 each seen RED, GREEN and broken; C1–C5 hold as
      edited (the spec's X1–X10 and this document's C4); N2's rows green.
- [ ] Under `auto`, each of S1–S6 has a test, and none sets `HumanGate`, writes a notice or waits (parent done-when 1).
- [ ] `not_reviewed` is known to the machine, `AnswerFor`, `FeatureSecondRound`, `MayProceed`, the bench, `CadenceState`'s
      wording — which reads an epic closed on a released `call_human` as released by auto — and `review_plan`'s list
      (done-when 2).
- [ ] Every auto decision is a Serilog line (Warning for `not_reviewed` and a released hold), a session-file `note` and a
      `coai.db` `rounds.note` — the release's note on the held round by a one-column update; no log line carries a
      question's text (done-when 3).
- [ ] A legacy hold is released by whichever of begin, `resolve`, `status`, `ask_human` and the fresh-document check
      (`HeldSentence`'s caller) comes first, one test each — including a legacy hold first loaded and saved under
      `human`, then `auto`; logged; its notices withdrawn BEFORE the release is saved; the release noted on the held
      round, in the session file and in `coai.db`. A withdrawal that fails, a save that fails and a busy claim each
      leave the session held and unchanged on disk, and the next load releases it. Then ONE `resolve` through
      `PanelService` advances the stage exactly once in each of the four stages, whether the held round had been
      resolved or not, the decisions recorded before survive, and the code stage's cadence closes. The person's answer
      on file wins, read again after the withdrawal (the race test), and the withdrawn cards are put back when it is
      found. A review entry answers the release's own two sentences, never `Unresolved` or `GateHeld` for such a hold.
      An existing but unusable settings file runs `auto`, writes its startup note, and releases nothing until it
      reads. A hold this build raised under `human` is not released, and `resolve(humanDecision: "proceed")` is still
      refused on it (done-when 4; the parent's § 13 item 4 and § 8 step 4).
- [ ] `ask_human` never waits under `auto`: `consult_first`, `ask_in_conversation` with an accepted id or when no
      consultant can be had, and N3's reply on a held gate — the person's for a hold this build raised or a hold from
      before `auto` the person answered, "call again" for a hold from before `auto` whose release could not be written
      yet — each with its test (done-when 5).
- [ ] A session file passes an older build's load and save with `autonomous` absent, `false` and `true` (done-when 6).
- [ ] `onExhaustedAuto` is the last commit (done-when 7).
- [ ] Under `human`, the existing hold suites pass with their assertions unchanged; every test that needed `human`
      pinned is listed in the PR (done-when 8).
- [ ] The server instructions and every tool text S3 changes are true under every policy, beside S1's two
      review-entry sentences; the instructions fit 2,000 characters with every pinned phrase.
- [ ] Release build and both suites green; `dotnet format` clean; `plan-lifecycle.mjs` clean; the docs, the four
      boundary tables and the CHANGELOG draft are in the PR. Then the epic's code review, merge, `mcp-v*` release —
      reported as committed / built / released / running.
