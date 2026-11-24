# PLAN — `auto`: the gate never asks a person, logs what went wrong and moves on

> Status: **CLOSED, 2026-11-24 — what is done and what is not: § *Closure*.** Before that: **IMPLEMENTED, 2026-11-19.** E1: #1011, released as coai-mcp 0.66.0 (#1012), 2026-11-17. E2: #1018, released as extension 0.89.0 (#1022), 2026-11-18; verified (VSIX: default `auto`, the witness key and the migration bundled; Marketplace 0.89.0). E3: conventions #182 (rule text: `coai-snippet` v6, `coai-document` v4, `coai-feature` v4, `coai-question` v2) and #183 (the release pre-checks); `release` promoted to `5d2d5ec` on 2026-11-19 with the owner's approval; nine pin bumps merged — benchmark #229, mcp #83, sidecar_rust #82, vscode_kit #36, wsl_care #200, creds #384, amd_r9700 #38, rag_qln #134 (also its benchmark and mcp code pins) and this repository's #1024 (the regenerated snippet, v19); extension 0.89.1 released (#1025, tag `extension-v0.89.1`; the VSIX carries v6/v4/v4/v2 and "Copy the CLAUDE.md snippet (v19)"). The conventions post-deploy check against coai `main` `4ec6cf3d`, 2026-11-19: items 1–2 PASS; item 3, the native-agent smoke: the claude and codex inspect runs `allSourcesRead` true, and the held-gate behaviour smoke on the v6 text PASS (answers `ask_human`, `point_to_notice_card`, `resolve`, `ask_human`; USD 0.90). `review_feature` over the three epics (baseRef `e829bba8`): `proceed`, 2 of 2 reviewers, 0 findings; both noted their source budget did not reach every file. The change ledger is closed: epoch 3 stands as recorded, entry 3.1 its last change. **What shipped differently is in § *Deviations*, just before `## Epics`.** The rest of this block is the plan's history, kept as written. Before that: **E3: S1 merged in conventions 2026-11-18 as #182 (`25d551b8`); the release pre-checks recorded in conventions #183 and, with the owner's approval, `release` promoted to `5d2d5ec` on 2026-11-19. This repository's bump with the regenerated snippet (v19) is PR #1024 (pin `5d2d5ec`); the other eight bumps are open or merging. Remaining: the eight bumps and #1024 merged, the post-deploy check with the native-agent smoke, `review_feature`, the patch release, and the promotion of the plan.** Before that: **E3: S1 merged in conventions 2026-11-18 as #182 (`25d551b8`) and NOT yet promoted to the `release` branch (it waits for the owner's word on in-flight consumer releases and on the PR cap, § 5.1). This repository's bump is built on `docs/auto-mode-e3` (the pin at `25d551b8`, the snippet v19, the constants and the tests green; not yet a PR). Remaining: the promotion, the nine pin bumps, the post-deploy check with the native-agent smoke, `review_feature`, the patch release, and the promotion of the plan.** Before that: **E3 started 2026-11-18: its stories document, [PLAN_auto_never_asks_a_person_E3.md](PLAN_auto_never_asks_a_person_E3.md), written by Opus; nothing built. E2 merged 2026-11-18 as #1018 and released as extension 0.89.0 (#1022).** Before that: **E2 built 2026-11-17 in PR #1018 (S1, S2, S3; the coai code gate `proceed`); not yet merged or released. E3 not started.** Before that: **epoch 3 open, 2026-11-17 — approved by the owner (the revision is `Narrowed: yes`), after the interim document
> review of the whole plan returned `proceed` (4 of 4 reviewers; three wording findings folded). E2 resumes: the S1 fix
> forward and Fable's S2 spec revision, then S2, then S3.** Before that: **epoch 2 stopped for re-review, 2026-11-17, at its second change (entry 2.2, Settings Sync and the
> migration's witness). No new code on any epic until the whole plan is reviewed again. The whole plan was revised the
> same day (§ *Re-review for epoch 3*: consulted — consultation `70202c20…`, each point verified or marked unverified in
> § 11; the done work sorted by an Opus judge, nothing cut from `main`, one fix forward in E2.S1 and one in the E2.S2
> spec; `Narrowed: yes`, so the owner approves epoch 3 before any code resumes).** Before that: **E1 (the server) merged 2026-11-17 as #1011 and released as mcp 0.66.0 (#1012). E2 started the same day: its
> stories document, [PLAN_auto_never_asks_a_person_E2.md](PLAN_auto_never_asks_a_person_E2.md), the E2.S2 spec, E2.S1
> coded on `feat/auto-mode-e2` and the S3 page slice on `feat/auto-mode-e2-s3-page`. E3 not started.** Before that: **E1 built 2026-11-17, in PR #1011, code gate
> `proceed`.** Before that:
> **epoch 2 open, 2026-11-16 — re-approved by the gate the same day (the interim document review of the whole plan,
> `proceed`, 4 of 4 reviewers; four findings folded, one rejected with its reason in § 4.3). Epoch 1 stopped at its second
> change (entry 1.2), accepted while E1.S1's
> spec was written, and halted all code until the whole plan was reviewed again. The whole plan was revised the same day
> (§ *Re-review for epoch 2*: consulted, the done work sorted by an Opus judge, nothing cut, `Narrowed: no`); E1
> resumes from its stories document and spec, revised to this epoch.** Before the stop: **plan only, nothing implemented yet, 2026-11-16 — plan round 1 `proceed` (2 of 2, gating 6 of 6), its findings folded in (§ 12); split into three epics E1–E3 the same day (§ Epics), stories documents written when each epic starts. E1 started
> 2026-11-16 with its stories document, [PLAN_auto_never_asks_a_person_E1.md](PLAN_auto_never_asks_a_person_E1.md),
> and the E1.S1 cornerstone spec, [PLAN_auto_never_asks_a_person_E1_S1_spec.md](PLAN_auto_never_asks_a_person_E1_S1_spec.md); nothing is coded yet.** Scope: a fifth value `auto` for *When the rounds run out*
> (`coai.onExhausted` / `COAI_ON_EXHAUSTED`), the default for every installation including choices made before; the
> round machine's three `call_human` sites, the begin/resolve hold readers and `ask_human` in `src_mcp`; the setting, its
> one-time migration, the env block and the rounds-log page in `src_vs_code`; the shared gate snippet in
> `dew_flow_conventions` (a second PR there). No Team-server wire change.
>
> Related docs: [PLAN_only_a_person_releases_the_gate.md](../todo/PLAN_only_a_person_releases_the_gate.md) (the hold's security
> property this plan carves an exception out of — § *Boundaries*), [PLAN_a_signed_answer_releases_the_gate.md](../todo/PLAN_a_signed_answer_releases_the_gate.md),
> [PLAN_a_cancelled_review_stops_its_round.md](../todo/PLAN_a_cancelled_review_stops_its_round.md),
> [PLAN_review_plan_again_after_proceed.md](../todo/PLAN_review_plan_again_after_proceed.md),
> [module_server.md](module_server.md) (*`call_human` stops the review*), [module_core.md](module_core.md).

## Plan changes

> Epoch 1 · approved 2026-11-16 · by reviewers · state: closed

- 1.1 · 2026-11-16 · agent · epics, build-order — the gate's operator command split the three PRs into epics E1–E3
  (Fable, after the question consultants); notes N1–N5 and the § 4.3/§ 4.4/§ 4.7 wording repairs satisfy the plan as written.
- 1.2 · 2026-11-16 · agent · contract — the release of a legacy hold sets AwaitingResolve true instead of keeping it
  (E1.S1 spec, U1): a hold whose round was already resolved kept AdvanceOnResolve with nothing to consume it, so a
  later round under a non-auto policy advanced on a plain resolve, and a feature review held at its cap never closed.
  Counted on both question consultants' reading (consultation 4be988cc…); this second change stopped the plan.

### Re-review for epoch 2

Narrowed: no — DoD item 4 and § 8 step 4 grew; scope, goals, merge conditions and the release are unchanged

| Work | Where | Verdict | Action |
|---|---|---|---|
| the plan, 602cfb91 | docs/plan-auto-mode and feat/auto-mode-e1, pushed, unmerged (#1007) | fits | kept; revised in place by the re-review |
| marker kept while a legacy hold stands, § 6 access model, Auto label, e9213dfd | docs/plan-auto-mode, pushed | fits | kept — a fix that satisfies requirement 4 as written, so no entry |
| the stop record, entry 1.2, 025673d5 | docs/plan-auto-mode, pushed | fits | kept |
| E1 stories document, 1f0ae332 | feat/auto-mode-e1, pushed | fits | revised in place after re-approval (§ 4.3, N3, C4) |
| E1.S1 cornerstone spec, 4cab5375 | feat/auto-mode-e1, pushed | fits | revised in place after re-approval (U1 settled, the note, the tests) |

> Epoch 2 · approved 2026-11-16 · by reviewers · state: closed

- 2.1 · 2026-11-17 · agent · epics, approach — the copied MCP block carries only COAI_DATA_DIR and COAI_DATA_SIDE (since a0eee1ca; install.test.ts and theDirectoryAPersonChose.test.ts), so no pasted block pins a policy: E2 adds no wait for features and no COAI_ON_EXHAUSTED to it, and done-when 2's second clause becomes "the copied block carries no COAI_ON_EXHAUSTED"
  (found by the E2 stories document, § 2.1 and § 10 R1; verified by the coordinator against extension.ts and install.test.ts.)
- 2.2 · 2026-11-17 · owner · approach, contract — Settings Sync carries the user settings to every machine while the globalState marker stays per machine, so another machine's first run resets a choice made after the migration (E2.S2 spec, U1): a witness in the synced user layer is written before the user value is removed, and a machine that finds it leaves the user value alone
  (both question consultants read it as a change, consultation 36f6b75a…; the owner chose the witness and the stop over a narrowed done-when 3. This second change stopped the plan.)

### Re-review for epoch 3

Narrowed: yes — E2 done-when 3 and DoD item 6 keep every clause, but "two windows … one notification" and "a later choice is kept" now hold under a stated condition (the windows hear each other's globalState write within the claim's pause; Settings Sync has delivered before a machine's run): VS Code's globalState is one whole value per extension with no compare-and-set (extHostMemento.ts, verified) and Sync's timing is invisible to an extension, so nothing can make them unconditional; § 4.6 lists the residuals. Everything else grew or stayed: scope, goals, merge conditions and the release are unchanged

| Work | Where | Verdict | Action |
|---|---|---|---|
| E1, the server, #1011 (92f1fa8f) | main | fits | kept |
| mcp 0.66.0, #1012 (3bbd157b) | main, released | fits | kept |
| the changelog baseline, #1014 (692cbd44) | main | fits | kept |
| E2 stories document and entry 2.1, 111b34d6 | feat/auto-mode-e2, pushed | fits | kept; revised in place by the re-review (§ 4 C4–C6, § 6, § 7.3 the S1 fix forward, § 9 the kept sentence, § 10.1 the list for Fable) |
| E2 plan round 1 fix, 7f2e681a | feat/auto-mode-e2, pushed | fits | kept |
| E2.S1 value, default, wire, 8fd79b71 | feat/auto-mode-e2, pushed | fits | kept, with a fix forward: the reader ignores the registry's default for onExhausted (U3; stories § 7.3), a new commit on the branch |
| E2.S1 gate section, 91a62aa7 | feat/auto-mode-e2, pushed | fits | kept |
| E2.S1 held-gate leg and compat script, 44bb7a4c | feat/auto-mode-e2, pushed | fits | kept |
| E2.S2 spec, 998a2a39 — the reach, the overlay witness, the record before the clear, the turns, the scenarios | feat/auto-mode-e2, pushed | fits | kept; Fable revises it in a new commit before S2's code |
| E2.S2 spec, 998a2a39 — D8 (Sync not closed), userBegun in globalState, D11's one sentence, the claim as the coordination (D4), S2.6's wake by the user clear | feat/auto-mode-e2, pushed | cut | fix forward in the same new commit by Fable (stories § 10.1, the list); no revert, the spec is unbuilt |
| the stop, entry 2.2, 33e88bf4 | feat/auto-mode-e2, pushed | fits | kept |
| E2.S3 page slice, ff158372 | feat/auto-mode-e2-s3-page, pushed | fits | kept; merged into feat/auto-mode-e2 after re-approval |
| E2.S3 page test and module-tests row, 726a2981 | feat/auto-mode-e2-s3-page, pushed | fits | kept, with ff158372 |

> Epoch 3 · approved 2026-11-17 · by owner · state: open

- 3.1 · 2026-11-17 · agent · approach — Settings Sync merges a key added on two machines with different values as a conflict it asks the person to resolve (settingsMerge.ts, read by the S2 spec, U10), so a witness naming its run would put a conflict on a key the person never wrote: the witness is one fixed word on every machine, and the read-back after the await is the value test alone
  (the coordinator's decision; the first change of epoch 3.)
  (At the plan's promotion, 2026-11-19: 3.1 stayed the only change of epoch 3. The epoch stays `open`, as an epoch closes only when a later one opens — the conventions precedents keep it so at promotion.)

## 1. What the owner asked for (2026-11-16)

> *«сюда нужно добавить авто мод — и он должен быть по умолчанию на всех, кто установит новую версию. это значит, что
> человека никогда не спрашиваем вообще. делаем по раундам, что нашли, и все. если ревьюверы не отвечают, еще что-то —
> плевать. записали в логи и все. у меня оркестратор будет использовать это, чтоб его не блочило. отработает хорошо, нет —
> записали и пошли дальше.»*

An `auto` mode for *When the rounds run out*, the default for everyone who installs the new version. A person is never
asked by the gate. The rounds run; what they found is worked; if reviewers do not answer or anything else fails, that is
written to the logs and the work moves on. An unattended orchestrator drives coai and must never be blocked by it.

The owner's four decisions, asked the same day (the technical forks went to the consultant first — § 11):

| # | Question | Decision |
|---|---|---|
| O1 | Who gets `auto` on upgrade? | **Everyone, including people who explicitly chose another value.** A one-time migration; they can choose again afterwards. |
| O2 | A session already held on `call_human` before the upgrade? | **`auto` releases it itself**, and logs that it did. |
| O3 | `ask_human` with the AI's OWN question, under `auto`? | **The reply speaks for the person: "call the consultant, talk it through, and come back to me only if the two of you did not settle it."** When the AI comes back with the consultants' `consultId` and it is still open, the question **really goes to the person** (today's `ask_in_conversation`). |
| O4 | Rounds spent, findings still open? | **Good enough**: the AI applies the findings that are true, rejects the rest with reasons through `resolve`, and proceeds. |

## 2. The symptom today — every road by which the gate stops on a person

Verified on `origin/main` 4cb32a76. Today's default is `human` (`src_vs_code/src/settingsShape.ts:276`,
`src_vs_code/package.json:349`, `PanelSettings.PolicyOf` `src_mcp/src/Server/PanelSettings.cs:1082-1088` maps absent and
unknown to `Human`).

| # | Where | Trigger | Today |
|---|---|---|---|
| S1 | `RoundMachine.CompleteRound`, `src_mcp/core/Rounds/RoundMachine.cs:440-452` | plan/code/document round with `reviewers.Answered == 0` (every reviewer failed, timed out, hit quota, was cancelled, unparseable; or the round deadline fired) | `call_human` **whatever the policy** — `GoodEnoughPolicyTests.WhenNobodyAnswered_ItStillCallsAHuman` (`src_mcp/tests/GoodEnoughPolicyTests.cs:65`) pins it |
| S2 | `RoundMachine.CompleteFeatureRound`, `RoundMachine.cs:512-514` | feature review at its cap (round 2, or round 1 with a budget of one) with a ground: a blocking finding, a reviewer failure, or nobody answered (`GroundFor`) | `call_human` — the feature stage ignores `OnExhausted` entirely |
| S3 | `RoundMachine.Exhausted`, `RoundMachine.cs:632-657` | findings still gate after the budget (default budget is ONE round: `PanelConfig.PlanDefault`/`CodeDefault`) | `call_human` under `Human`, and under `Escalate` once the ladder is spent |
| S4 | the hold | after any of S1–S3: `HumanGate = true` | every `review_*` refused with `GateHeld` (`RoundMachine.cs:86-94`, checked at :132, :226, :286, :311); a fresh document review beside it refused (`RoundEngine.HeldSentence`, asked only by `PanelService.WhyNotAFreshReview`); `resolve` keeps the hold (`RoundMachine.cs:750-763`); only the person's answer file releases it (`CurrentAnswer`) |
| S5 | `ask_human`, card door, `AskHumanService.ThroughTheDoorAsync` `src_mcp/src/Server/AskHumanService.cs:215-241` | the session is held, or a feature session after round 1 may be asked for a second round (`RoundMachine.AsksForTheGate`, :377) | **blocks** up to `COAI_ESCALATION_MINUTES` (15) polling the answer file, then `no_answer_yet` — the hold stays |
| S6 | `ask_human`, the AI's own question, `AskHumanService.InTheConversationAsync` :128-146 | any question not the gate's | returns at once with `AskInTheConversation` (:202-208): *"ask the person yourself … and wait for their reply. Never decide alone"* — an orchestrator's agent stops here |

Nothing in the server knows whether a person or a VS Code panel exists at all (no handshake; the only coupling is the
`escalations/` folder). An orchestrator running `coai-mcp` alone gets a card nobody sees and a hold nothing releases.

Two more facts that make the change wider than one enum value:

- **A session freezes its policy.** `PanelConfig` is written into the session at creation (`PanelService.cs:523-526`,
  `FeatureStage.cs:202`) and only `SecurityLane` is refreshed per round (`RoundEngine.cs:276-280`). Sessions are never
  deleted, so every repo+branch opened before the upgrade would keep `Human` for ever.
- **The setting is sent only when it differs from the panel's default** (`settingsShape.ts:551-553`). Flipping the
  default changes what an absent key means on BOTH sides, and an old server given `auto` falls back to `Human` with a
  notice (`PanelSettings.WhyExhausted`, :1215-1224).

## 3. What must be true when it is done

Under `auto` (the default):

1. **No verdict is ever `call_human`, no hold is ever raised, and no notice card is ever written by a round.** S1–S3
   advance instead (§ 4.2).
2. **A round in which nobody answered proceeds with the new verdict `not_reviewed`** — never `proceed` or `good_enough`,
   so an absent review is never indistinguishable from a review that judged the work. Its instruction tells the caller to
   say in its summary that the stage was NOT reviewed and why.
3. **Rounds spent with findings open → `good_enough`** (O4), with the reason in the round's note.
4. **A hold raised by a build that predates `auto` is released by the next call that can write the release** (O2) — a
   review entry, `resolve`, `status`, `ask_human` — logged, its notice withdrawn so the panel stops showing it, and never
   answered with `GateHeld` under `auto` (§ 4.3) unless the person answered it first (their answer wins). A hold raised by THIS
   build (under `human`, or under a workspace value) is not released by configuration — § 4.3, § 6.
5. **`ask_human` never blocks, and the gate never asks.** The AI's own question without a `consultId` gets
   `consult_first` (O3); with the consultants' `consultId`, or when no consultant can be had, it gets today's
   `ask_in_conversation` — the owner's explicit choice: a question the consultants could not settle does reach the
   person. The card door is never opened. **The guarantee is therefore exactly:** under `auto` coai raises no new hold, never waits, and
   never writes a card — a hold this build raised under `human` before the switch stays the person's (item 4); the
   only road to a person is the AI's own question after the consultants, which the AI chose to
   ask. The help, and the migration's notification in a window that runs `auto`, say this sentence, not "never asks
   you anything"; in a window where another value stays in force the notification names that value instead (§ 4.6).
6. **Every one of these is written down three times**: a Serilog line (Warning for `not_reviewed` and a released hold,
   Information for the rest) beside the round's existing `Closing` line; the round's `note` in the session file and
   `coai.db`; and the rounds-log page, which shows the note for every round that has one.
7. **Everyone is on `auto` after upgrading** (O1): the panel default, the server's absent-key default, a one-time
   migration of saved values (§ 4.6) — the user settings once for every machine Settings Sync reaches, each side's
   overlay once per profile of a machine — and sessions that pick the policy up on their next begin (§ 4.1). A choice
   made after the migration is kept, within the residuals § 4.6 writes down. **The one
   exception is open question Q1**: a value a repository commits in its `.vscode/settings.json` is the team's file, not
   the person's saved choice; it is not edited, it stays in force, and the gate section names it. Until the owner
   decides Q1, "everyone" means every saved choice of the person's own — user settings and every side's overlay.

Under every other value nothing changes — `human`, `continue`, `good_enough` and `escalate` keep today's behaviour,
including S1 and S2 calling a person. And in every mode:

8. **The AI's own argument still never releases a hold.** `resolve(humanDecision: "proceed")` stays refused on a held
   gate (`RoundMachine.ClaimRefusal`, :674-708). What releases a hold under `auto` is the operator's configuration,
   read by the server — never a field the caller sends.
9. **An unknown policy word still stops** (`PolicyOf`'s `_ => Human`, its complaint unchanged). Only an ABSENT or empty
   key means `auto`.

## 4. Design

### 4.1 The value, how it is stored, and how a session picks it up

- **In memory**: `PanelConfig` gains a trailing, defaulted member `bool Autonomous` (`src_mcp/core/Rounds/SessionState.cs:80-82`).
  `auto` is `OnExhausted = GoodEnough, Autonomous = true`. No new `StagePolicy` member.
- **Why not `StagePolicy.Auto`** (consulted, § 11 C1): sessions persist the enum by NAME (`ServerJsonContext.cs:368`); an
  older `coai-mcp` sharing the data folder (WSL + Windows, or a downgrade) throws on `"Auto"`, `SessionStore.Load`
  swallows it (`SessionStore.cs:522-530`), treats the session as new and overwrites it — the branch's trail is lost. With
  `"GoodEnough"` + `"autonomous": true` the older build reads a policy it knows (it does not ask a person at exhaustion),
  and `PanelConfig`'s extension-data bag (`SessionState.cs:147-157`) with `SessionScrub.ConfigOf` keeps the unknown member
  across its save. An older build still calls a person on S1/S2 — the downgrade limit, written down, not hidden.
- **Parsing** (`PanelSettings.PolicyOf`, :1082-1088, and `Config`, :1679-1683): `"auto"` and absent/empty →
  `(GoodEnough, Autonomous: true)`; the four existing words → themselves with `Autonomous: false`; anything else →
  `(Human, false)` with `WhyExhausted`'s complaint as today. The help string at `Program.cs:2614` lists all five.
- **Defaults agree in one place each side**: `PanelConfig`'s record defaults and `PanelConfig.Uniform` (:277) move to
  `auto`; harnesses that need a person pass `StagePolicy.Human` explicitly (CodeRoundHarness.cs, EndToEndTests.cs,
  ARoundsNumberComesFromTheJournalTests.cs already take the parameter).
- **`Autonomous` is `bool?` on disk**: this build always writes `true` or `false`; `null` (the member absent) means the
  config was written by a build that predates `auto` — the marker § 4.3 reads. **While a hold stands whose marker is
  absent, the refresh never stamps it** (it may refresh `OnExhausted`): the marker stays absent until that hold is
  released — by `auto`, or by the person's answer — so a legacy hold first loaded and saved under `human` is still
  released once the policy becomes `auto` (the E1 plan round and CodeRabbit on #1007, 2026-11-16; one test for that
  sequence).
- **Refreshed on every load that decides anything** (§ 11 C2; plan round 1 finding 1): one helper,
  `CurrentPolicy(session)`, sets `OnExhausted` and `Autonomous` from the current settings and is called by the begin
  (beside the `SecurityLane` refresh at `RoundEngine.cs:276-280`), by `resolve`, `status`, `ask_human` and
  `HeldSentence` before any of them reads the hold. `EscalationsUsed`, `RoundsRunThisStage` and pending
  decisions are kept, so toggling the setting cannot refill the escalation ladder. A null config from an old file
  normalises in `Load` (`SessionStore.cs:702-704`) through the file's constructor, whose `autonomous` has no default, so
  the marker stays absent through the normalisation and `CurrentPolicy` reads it as it was on disk — never the record
  defaults, which now mean `auto` (CodeRabbit on #1007; the E1.S1 spec, input 1).

### 4.2 The three `call_human` sites, under `Autonomous`

| Site | Under `auto` | State |
|---|---|---|
| S1 `Answered == 0` (plan/code/document) | `RoundVerdict.NotReviewed(gate, reviewers, reason)` at once, whatever the budget left | `AwaitingResolve = true, AdvanceOnResolve = true`, `HumanGate` false |
| S2 feature, at its cap with a ground | ground is nobody-answered → `NotReviewed`; otherwise (a blocking finding, or some failed) → `GoodEnough` | same as above; `SecondRound = None` |
| S3 `Exhausted` | `GoodEnough` — already what the policy name gives | unchanged from today's `GoodEnough` arm |

`not_reviewed` advances like `good_enough`: `resolve` with no findings is accepted and opens the next stage, so an
unattended plan stage reaches the code stage (`review_code` refuses until `PlanProceeded`, `RoundMachine.cs:227-228`).

Why not retry an empty round while rounds are left: the default budget is one round, a code retry needs `again` and a new
commit, and the owner's words are *"записали в логи и все"*. Recorded as open question Q2.

Everything that lists verdicts learns the new one in the same commit:

- `RoundEngine.AnswerFor` (:916-949) — name `not_reviewed`, instruction: *"No reviewer answered — {failures}. Policy
  `auto` proceeds WITHOUT a review. Call resolve (there is nothing to decide), say in your summary that this stage was not
  reviewed and why, then proceed."* `VerdictInstructionTests.EveryVerdictInTheUnion_IsHandledByTheAnswer` fails until it is.
- `RoundCommands.MayProceed` (`src_mcp/src/Server/Rounds/RoundCommands.cs:16`) — or an operator's split order vanishes.
- `src_bench/CoaiBench/Running/RoundRunner.cs:31` — or the bench retries an advanced plan and never reaches code.
- `CadenceState` epic close (`src_mcp/core/Cadence/CadenceState.cs:5`) accepts any string; its "passed" wording is corrected, and an epic closed after a released legacy hold reads as "released by auto" (§ 4.3), not as a person's decision.
- The `review_plan` tool's verdict list (`src_mcp/src/Tools.cs:103`) and the feature text (:298-311).

### 4.3 A hold from before the upgrade (O2)

**Only holds from before the upgrade** (plan round 1 finding 0): the release applies when the current policy is `auto`
AND the session's stored config has `Autonomous == null` as loaded (§ 4.1) — a hold raised by a build that never knew
`auto`. A hold this build raised (the operator chose `human`, then something switched the setting) stays the person's:
otherwise the gated agent could release its own hold by writing `COAI_ON_EXHAUSTED=auto` into `settings.json`, which is
exactly the road [PLAN_only_a_person_releases_the_gate.md](../todo/PLAN_only_a_person_releases_the_gate.md) closed for arguments.
The marker is read BEFORE `CurrentPolicy` stamps the config, in the same helper, and as `Load` left it — absent stays absent (§ 4.1).

`RoundEngine.ApplyAnyHumanDecision` (:93-120) is the one reader every review entry passes; for such a hold it releases
the session before anything else. **The released state is `HumanGate = false, HoldQuestions = [], AwaitingResolve =
true, AdvanceOnResolve = true`** — the state every auto verdict leaves (§ 4.2), so the next `resolve` (with the
decisions still open, or none) advances the stage through the one path that already closes a stage
(`PanelService.Finish`, the cadence close included). *[epoch 2, 2026-11-16: entry 1.2 — epoch 1 said "`AwaitingResolve`
kept"; a held round the caller had already resolved has it `false`, which left `AdvanceOnResolve` with nothing to
consume it: a later non-auto round then advanced on a plain `resolve`, and a feature review held at its cap never
closed. Advancing at once on release was weighed and not taken: it would bypass `Finish` from read paths such as
`status`.]*

**The order, and what a failure leaves** (E1 plan round finding 1; the cadence consultation for epics 1–3). First each
recorded hold question is withdrawn in the `escalations/` folder, so the extension's watcher stops raising it; the
withdrawal tells *withdrawn*, *not there* (already gone or expired) and *failed* apart (`Escalations.Expire` returns
`false` for both of the last two today, `Escalations.cs:243-245`). Only when none failed is the released session saved.
Any failure — a withdrawal, the save, or a session claim another call holds — leaves the session held and unchanged on
disk, with a Warning saying why; the next deciding load tries again. A crash between the two steps therefore leaves a
held legacy session that the next load releases, never a released session with a live card. The card shows no reason
(`Expire` has none, and E1 does not touch the extension): an expired card simply leaves the active list; the reason is
in the Warning line *"auto released the {stage} hold of session {id} raised under {policy}"* and in the round's note.

**The release is written on the held round** (the re-review consultation `1d4733ae…`): the held round's `RoundRecord`
keeps its verdict (`call_human` or `escalated` — it is history) and gains the note *"released by policy auto: this hold
was raised by a build before auto"*, saved with the session and written into that round's existing `coai.db` row
(`rounds.note`, keyed by `session_id, stage, number` — `RoundsDb.cs:849-865`) by a one-column update; no schema step,
and the round's findings are not re-recorded. *(Reference repaired 2026-11-16 by the E1.S1 spec, U2: the writer this
sentence named, `RecordRoundRow`, is private and rewrites the whole row from a context the release does not have.)*
Writing it twice writes the same note. The cadence's epic close records the verdict of that round; its wording (§ 4.2) reads
`call_human` with this note as "released by auto", not as a person's decision.

The same release is made by `resolve`'s load (`PanelService.cs:1656`), `status`'s (`:567`), `ask_human`'s
(`AskHumanService.cs:100-101`, before the door is chosen) and the fresh-document check that asks `HeldSentence`
(`PanelService.WhyNotAFreshReview`, `:1152-1158`), so whichever the orchestrator calls first after the upgrade, it is
never answered with `GateHeld`. `begin` and `resolve` release under the session claim they already take; `status`,
`ask_human` and the fresh-document check take the claim for the release only and re-read under it — busy means nothing
is written. A hold with no recorded question (`WithTheHoldReissued`, :154-168) is released the same way instead of
being re-issued.

**What a review entry answers** (the re-review judge, findings 1 and 3). After a release the held round still needs
`resolve`, so a review entry is refused as today's `Unresolved` refusal is — but with a sentence of its own: *"policy
auto released this {stage} hold, raised by a build before auto; call resolve (with the decisions still open, or none)
and the stage moves on"*, never the sentence that says the findings were not resolved. When the release could not be
written (a busy claim, a failed withdrawal or save), the entry answers *"policy auto is releasing this {stage} hold,
raised by a build before auto; the release could not be written yet ({why}) — call again"*, never `GateHeld`, whose
words say a person must decide.

**A person's answer on file wins** (the re-review gate, finding 2). The release runs only when the hold's notice has no
answer on file; an answer the person already gave (Proceed, Continue, Fix, Discuss) is applied as today
(`ApplyAnyHumanDecision`), and auto does not override it. The race — an answer written after the check and before the
withdrawal — is closed by reading again: the release (1) withdraws the cards, after which the extension no longer
offers them; (2) reads each card's answer file AGAIN; (3) if an answer is now on file, applies it as today and writes
no release; otherwise saves the release. An answer written after step 2 is to a card already withdrawn, which the
extension does not offer. A test writes the answer between the withdrawal and the second read, and asserts the
person's decision is applied and no release note is written. "As today" includes the card: when step 3 finds an
answer and writes no release, the cards withdrawn in step 1 are restored to the state they had, so an answer that
leaves the hold standing (Discuss, or words only) finds its card as it would today and the person can answer again
(the E1.S1 spec, U3). A hold the person answered that way is the person's; it answers `GateHeld` as today — the
"never `GateHeld`" of requirement 4 is about holds auto releases.

**Residuals of the order, written down.** A card an older build wrote but never recorded on the session is not
withdrawn; the retention removes it seven days after it was asked. If one of several cards was withdrawn and the save
then failed, the hold stands with fewer live cards; should the policy then leave `auto` before a release is written,
the person sees the hold through the cards left, or through the card `ask_human` re-posts for a held session.

**A known residual — rare, and written down.** The marker survives an older build's save only through the config's
extension-data bag, and that bag exists since #868 (`fbf98e2a`, 2026-10-19; first released as `mcp-v0.63.0`). A
`coai-mcp` older than that drops a member it does not know. If such a build — sharing the data folder, or installed by a
downgrade — re-saves a session whose hold THIS build raised under `human`, the file loses `autonomous`; once the policy
is `auto`, this build reads that hold as one from before the upgrade and releases it. It takes three things together: a
hold raised under `human` by this build, a save by a build older than 0.63.0, and a later switch to `auto`. The release
is still logged as a Warning and its notice withdrawn, like every other. This plan does not close it. The opposite mix
fails closed: a build that has the bag keeps the member, so a hold it raises on a file this build stamped stays the
person's. *Why it is accepted, not closed (the re-review gate, finding 0, rejected):* every build older than 0.63.0
also predates the refusal of the AI's own claim (`ClaimRefusal`, first released in `mcp-v0.64.0`), so such a server
releases ANY hold on `resolve(humanDecision: "proceed")` from the gated agent itself; with it in the loop the hold has
no authority left to protect, and no member this build writes survives a build without the bag.

### 4.4 `ask_human` under `auto` (O3)

`AskGate` (`src_mcp/core/QuestionConsult/AskGate.cs`) gains the `Autonomous` input and one decision, `ConsultFirst`:

| Call | Reply |
|---|---|
| The gate's question (card door) | `auto` raises no hold, and a legacy hold is released before the door is chosen (§ 4.3). The feature second-round request is answered at once — *"policy auto: a second feature round runs only on a ground the server finds; this review closes on resolve"* — logged, no card. A hold THIS build raised under `human` keeps its card (it is the person's, § 4.3), but the call returns at once with `no_answer_yet` (waited 0) instead of polling — never a wait under `auto` (note N3). Its sentence tells the two held cases apart: a hold this build raised is the person's (answer on its notice); a hold from before `auto` whose release could not be written yet (a busy claim, a failed withdrawal or save, § 4.3) is released by the next call — call again. |
| The AI's own question, no `consultId` | `status: "consult_first"` (not an error; nothing spent, nothing counted): *"The person says: call the consultants — ask_consultants — talk it through, and come back to me only if the two of you did not settle it. Then call ask_human again with the consultId."* |
| … with a `consultId` the desk accepts as proof | today's `ask_in_conversation` — the question really reaches the person |
| … no consultant can be had (`none_available`, quota) | today's stand-down (`AskGate.cs:152-159`): `ask_in_conversation`, with the note saying why nobody was consulted |
| `productionRisk: true` | today's path (consultants asked beside, answers returned) |

Every `consult_first` is a Serilog Information line with the question id and the decision — **never the question's
text** (plan round 1 finding 4: an agent may paste a secret into a question; a test asks one carrying a key-shaped
string and asserts it is absent from the log file). The existing `InTheConversationAsync` line that logs the text
(`AskHumanService.cs:143`), and the card door's line that does the same (`:229`), are brought to the same rule in the same commit.

### 4.5 Logging (requirement 6)

- `RoundAudit` (`src_mcp/src/Server/RoundAudit.cs`) gains one method, `Autonomous(verdict, reason)`, called from
  `RoundEngine` beside `audit.Closing` (:650): Warning for `not_reviewed`, Information for an auto `good_enough`.
- The reason goes into `RoundRecord.Note` (`SessionStore.cs:90-200`) and therefore `coai.db`'s existing `rounds.note`
  column (schema step 16, `src_mcp/src/Store/Schema.cs:480-482`; written by `RoundsDb.RecordRoundRow`). **No schema step.**
- The rounds-log page shows `note` today only inside the skipped branch (`src_vs_code/src/roundsLog.ts` detail pane,
  ~:1790); it shows it for every row that has one. Tested by running the page (`bundledPage.test.ts`), per PROJECT.md.
- The verdict cell renders the raw string (`roundsLog.ts:1907`), so `not_reviewed` needs no label map.

### 4.6 The panel: value, default, migration, env block, old server

- **Manifest** `src_vs_code/package.json` `coai.onExhausted` (`:354-364`): enum gains `"auto"`, default `"auto"`,
  description names all five. **One new setting, `coai.onExhaustedResetToAuto`**: the user layer's witness (the
  migration, below) — a string, no default, NO `scope` (the scope of `coai.onExhausted` itself, for the reason below),
  described *"Written by ConnectOtherAIs, not by hand"*, as `coai.catalogMigration` is (`package.json:994-1001`).
- **`settingsShape.ts`**: `OnExhausted` (:31) gains `'auto'`; `DEFAULTS.onExhausted = 'auto'` (:276); `asOnExhausted`
  (:627-631) accepts it.
- **The value is read from the layers that HOLD one, never from the registry's default** (the E2.S2 spec, U3; E2.S1's
  fix forward). VS Code can update the extension in place before it activates, and that window keeps the OLD settings
  registry until a reload (observed 2026-10-09 for new keys, `research/PLAN_catalog_migration_waits_for_its_settings.md`).
  There `config.get('onExhausted')` answers the old default `human`, which now differs from `DEFAULTS`, so the mirror
  would write `COAI_ON_EXHAUSTED=human` for everyone who never chose anything — and a hold raised then is this build's
  and is never released by `auto` (§ 4.3). So the shared reader (`modelKeys.ts`, the door every side's reader is built
  on) answers `onExhausted` from `inspect`'s folder, workspace and user values, in that order, and answers nothing when
  none holds one; `asOnExhausted(undefined)` then answers `DEFAULTS` — `auto`. This side's overlay still wins over all
  of them (`sideSettings.ts`), and an overlay seeded from now on no longer freezes a default into itself (`seedOverlay`
  skips a key the reader does not answer). That the in-place window holds a changed DEFAULT is inferred from the
  2026-10-09 observation, not observed; the reader is right either way.
- **Select** (`src_vs_code/src/panelView.ts:1642-1648`): first option *"Auto — the gate raises no new hold and never waits for a person; it logs and moves on"* (an AI's own question can still reach the person after its consultants, requirement 5 — the help says so).
- **The one-time migration (O1)**, at activation, started after the settings mirror is set up and never from the panel.
  *[Revised at the re-review for epoch 3: entry 2.2 and the re-review consultation `70202c20…`, points (a)–(e), § 11 C5.
  Epoch 2 said "once per machine (a `globalState` marker …)" and one notification sentence.]*

  **What it clears.** The saved `coai.onExhausted` in the user layer (`config.update(key, undefined, Global)`) and in
  every per-side overlay (`seedOverlay` froze `'human'` into overlays of people who never chose it —
  `settingsShape.ts:403-413`, `sideSettings.ts:95`). Overlays are found by walking `globalState.keys()` for the overlay
  prefix (`overlayKeyOf('')`), which also reaches a side the registry pruned (N5). A location that already holds `auto`
  is left as it is: only this version can have saved it, and removing it from an overlay could hand that side to a
  workspace value. Workspace-scoped values are NOT edited (Q1, the owner's answer).

  **One witness per location, kept where that location's value lives.** A location with a witness is never cleared
  again, whatever it holds — that is what keeps a choice made after the clear.
  - *The user layer's witness is a setting in the user layer itself*, `coai.onExhaustedResetToAuto`, written
    before the user value is removed (entry 2.2), and written as well when the layer holds nothing to remove. A
    run that finds it, written on this machine or carried by Settings Sync from another, leaves the user value alone.
    It lives in the settings file because the user layer is one file on every machine Settings Sync reaches while
    `globalState` is per machine, and because a settings write is a per-key edit of that file that no `globalState`
    write can undo (point (a)). It is written when nothing is removed because a person on that machine may choose a
    value afterwards, and the next machine must keep it. Its value is one fixed word, the same on every machine, so Settings
    Sync merges two machines' witnesses without a conflict (entry 3.1); it is read from `inspect().globalValue` only.
  - *Its scope* (point (b)): none declared — the scope `coai.onExhausted` has — so the witness is synced exactly when the
    value it witnesses is, and lives in each profile's own settings beside it. `machine` and `machine-overridable` are
    left out of Settings Sync by default, so the next machine would not see it; `application` is shared by every
    profile, so a witness written in one profile would certify another profile's value that was never cleared. A
    workspace can set a key of this scope, which is why only the user layer's value is read.
  - *An overlay's witness is a member of the overlay record*, `onExhaustedAutoMigration` (the version), written in the
    SAME `globalState.update` that removes `onExhausted` — there is no instant between the clear and its witness, and a
    later choice made through `writeOverlay` keeps the member. Overlays are not synced, so a per-machine witness is right
    for them.
  - *The marker*, the `globalState` key `coai.onExhaustedAutoMigration`, is a record: its `completed` member is the
    version that completed the run on this profile of this machine; while a run is in flight it also holds a claim and
    what each location held before the first clear (N8).

  **What `globalState` cannot do, and what the design rests on instead** (point (a), verified in VS Code's
  `extHostMemento.ts`). `Memento.update` writes the extension's WHOLE `globalState` object as one stored value, and a
  window's copy learns another window's write only when the editor delivers it (`onDidChangeStorage`). A window whose
  copy is stale therefore puts that whole copy back with ANY key it writes, and a claim taken by write, pause and read-back
  reads the window's own copy: it excludes nothing. The claim stays, to make one window do the run in the usual case;
  nothing below rests on it. What does: (1) the user layer's witness is in the settings file, out of reach of a stale
  `globalState` write; (2) an overlay's value and its witness are one record in one write, so a stale copy brings both
  back or neither; (3) no value read from the record is carried across an `await` — every step reads again — and the
  marker is written only from a copy in which every recorded location is settled, checked with no `await` before the
  write, so a stored marker always stands in the same stored value as its cleared overlays.

  *A stale window that brings back an overlay it cleared.* Its copy predates the clear, so it predates the marker too:
  the stored value is that older copy, the overlay holds its old value with no witness, and the marker is gone. That side
  runs the old value — the gate section shows it — until the next activation, which runs the migration again, clears
  what came back and shows the notification again. A choice the person made IN that window, on its stale copy, carries no
  witness, and that run clears it too. It takes a window whose copy lags the whole run; it is written down, not closed:
  the windows of a profile have no compare-and-set to close it with.

  **The order.** Claim → record what each location holds (N8) → each overlay, one write with its witness, one turn of the
  catalog migration's queue (the two migrations rewrite the same record) → the user layer: when it has no witness, write
  the witness, await it, then read the value AGAIN — a value no
  longer the one recorded stops the step and leaves the value (point (c)); else remove the value; a removal the settings
  file refuses withdraws the witness → the mirror → the marker → the notification.

  **Settings Sync during a run** (point (c)). Sync can deliver another machine's file while the witness write is awaited;
  the read after the await sees it. A fixed pause proves nothing and none is relied on. What remains: a delivery that
  lands between the first read of the witness and its write, or after the read-back and before the removal is written — the time of one settings write; and a machine whose run comes
  before Sync has pulled the other machine's file at all, which clears its own copy, after which Sync's merge can drop a
  choice made elsewhere that equals the old value. Neither is measured; both are written down.

  **The mirror** (N7, point (d)). An overlay clear fires no configuration event, so the run calls `mirrorSettings` and the
  panel's redraw itself, once its clears are done and BEFORE the marker — also in a run that ends incomplete, because
  what it cleared already differs. Other open windows are woken by the configuration event of the run's own settings
  write (the witness, or the removal: `extension.ts:603-616` mirrors on any `coai` change). But when the witness was
  already there, the run writes no setting and no event fires; and an event can reach another window before that
  window's copy of `globalState` holds the cleared overlays. The promise is N5's and no more: another side's server
  settings file is rewritten at that side's next mirror after its copy holds the change — its next activation at the
  latest.

  **A window that does not know the new manifest** (updated in place) does not run: the run starts only when the
  registry knows `coai.onExhaustedResetToAuto` (a write of an unregistered key is refused). Its next activation runs it.

  **The notification**: one per completed run, non-modal, and true for the window that shows it (point (e)). When the
  value that window runs is `auto`:
  *"When the rounds run out is now Auto — the gate raises no new hold and never waits for you; it logs and moves on. Your AI may still ask you a question its consultants could not settle, or when no consultant can be had. Change it in Reviews › The gate."*
  When another value stays in force there — a choice made after the migration on this machine or another, a workspace
  value (Q1), a side's own value the run left because it had changed, or a user value a crashed run left (residual i) —
  it names it instead:
  *"When the rounds run out now defaults to Auto, but {label} stays in force in this window: {where} sets it. Change it in Reviews › The gate."*
  — `{label}` from the select's labels, `{where}` one of "this side's own settings", "this workspace's
  .vscode/settings.json", "your user settings". A fresh install is told too: nothing the extension keeps tells it from
  an upgrade, and both sentences are true for it. When the settings file refuses the change, no marker is written, the
  next activation tries again, and the person is told once per machine while it lasts:
  *"When the rounds run out could not be reset to Auto in your settings: the settings file refused the change. Your earlier choice there is still in force. ConnectOtherAIs tries again at the next start."*
  Anything else that ends a run early goes to the extension host's console, never a toast.

  **Residuals, written down and not closed** (each in `research/module_extension.md` when E2 ships):
  (i) a host that dies after the user witness is written and before the removal leaves that one value in place for good
  (N8) — the notification names it; (ii) the stale-window and Settings Sync windows above; (iii) two windows whose copies
  hear each other later than the claim's pause can both run and both notify, and can both ask for the user value's removal — the second removes a choice only
  if one was made in the time of one settings write; (iv) a side whose extension is OLDER
  rewrites its file with the old writer, so a side whose overlay ran `continue`, `good_enough` or `escalate` runs `human`
  on a server older than 0.66.0 until that side's extension updates — the CHANGELOG says to update every side's
  extension together; (v) an older extension that turns the per-side switch on after the run freezes its default
  `human` into a new overlay, which nothing clears; (vi) "once" is once per profile of a machine for overlays and the
  marker, and once for every machine Sync reaches for the user layer; (vii) a settings file that never accepts a write:
  the run never completes, the notification is never shown, and every start tries again; (viii) a value in a remote
  machine's own user settings (Remote-SSH, WSL): `globalValue` merges the remote file over the local one and a removal
  goes to the file that holds the key, so one run clears one of the two and the other stays, witnessed — it takes a
  hand edit of that remote file.
- **The env block** (`settingsShape.ts:551-553`) reads the binary's features (the fifth `envBlock` parameter, as
  `consultantsEnv` does):

  | Panel value | Server lists `onExhaustedAuto` | Server settled WITHOUT it (older) | Not settled yet |
  |---|---|---|---|
  | `auto` | nothing written (absent = auto) | `COAI_ON_EXHAUSTED=good_enough`, and the gate section says *"this coai-mcp predates Auto: it runs Good enough and still asks a person when no reviewer answers — update it"* | nothing written |
  | any other | written, as today | written, as today | written, as today |

  The settings FILE may be written while features are unsettled, because every settled answer rewrites it
  (`FeaturesCache` → `mirrorSettings`, `extension.ts`). *Changed by entry 2.1:* no block the panel hands out for a client's
  `env` carries a policy — the copy action passes only `COAI_DATA_DIR` and `COAI_DATA_SIDE` (`serverEnv()`, guarded by
  `install.test.ts`), so the policy reaches a server only through the settings file, and plan round 1 finding 3's risk
  does not arise. E2 leaves the copy action as it is.
- **A client `env` that still pins another policy** (an old pasted block wins over the file, `SettingsFile.cs:49-58`)
  is outside the migration's reach: the server writes a startup note when `COAI_ON_EXHAUSTED` comes from the process
  environment and is not `auto` (the `TheStartupNotesAreWrittenDown` road), naming where to remove it.
- **Features**: `src_mcp/src/Server/FeaturesMode.cs` lists `onExhaustedAuto` in the commit that makes it true;
  `src_vs_code/src/binaryFeatures.ts` (`FEATURES`, :18) learns it.

### 4.7 The words an AI reads

All of these are static or per-reply text that today promise a person; each is reworded to be true under every policy
(the instructions are sent once at `initialize`, and the policy can change mid-session):

- server instructions, `src_mcp/src/Program.cs:2443-2466` — "*`call_human` … appears only when the operator's policy asks
  for a person; under the default `auto` a round that could not judge says `not_reviewed`*";
- `Tools.cs` — `review_plan` verdicts (:103), `review_feature` (:298-311), `resolve` (:341-348), `ask_human`
  (:400-446: the `consult_first` status, and that it never blocks under `auto`);
- `GateHeld` and its siblings in `RoundMachine.cs:86-113` stay as they are. They are reachable only while a hold
  stands, and under `auto` one kind still can: a hold this build raised under `human` (or under a workspace value) before
  the setting became `auto` (§ 4.3). A legacy hold under `auto` never reaches them: its two sentences are § 4.3's.
  The promise is exactly this: **`auto` raises no new hold and releases the eligible
  legacy holds; a hold raised under `human` by this build keeps its authority**, and these sentences stay true for it
  until the person answers.
- **The shared snippet**, `src_vs_code/src/claudeSnippet.ts` (`coai-snippet v5`) and its source of truth
  `dew_flow_conventions · common/coai-review-gate.md`: step 6 gains *"`not_reviewed` → nothing was reviewed: say so in
  your summary, resolve, proceed"*, and `ask_human`'s `consult_first`. Version v6. The conventions half is its own PR in
  that repository, released and pinned as every rule change is (§ 8).

### 4.8 Docs

`README.md` § *When the rounds run out* (five answers; auto first), `src_vs_code/README.md:44-45`, `ARCHITECTURE.md:80-82`
(and its stale "plan 3 rounds, code 2"), `help.ts:163-164`, `helpContent.ts:143-149` and the four translations
(`helpDe/Es/Ru/Uk.ts`), `research/module_server.md` (*Only the `human` policy raises the gate*, ~:2304; env keys
~:1862), `research/module_core.md` (~:442-460), `research/module_extension.md`. `src_vs_code/CHANGELOG.md`: a
*Server* entry and an *Extension* entry in the was/now style of 0.31.6 (`src_vs_code/CHANGELOG.md:3584`), saying plainly that
**a value you chose is reset to Auto once** — a choice made afterwards is kept, also on the other machines Settings
Sync reaches, outside the rare windows § 4.6 names (residuals i–iii) — and that the extension is updated on every side (each WSL or SSH remote) together (§ 4.6, residual iv).
`research/module_extension.md` carries § 4.6's residuals.

## 5. Compatibility

| Pair | What runs | What a person sees |
|---|---|---|
| new panel + new server | `auto` | — |
| new panel + old server | `good_enough` (§ 4.6) — no person at exhaustion; still `call_human` when nobody answered | the "predates Auto" line in the gate section |
| old panel + new server | absent key → `auto`; an old panel's explicit non-`human` value is written and honoured; its explicit `human` was never written (it equalled the old default) → `auto`, as O1 wants | — |
| old server reading a new session file | `"GoodEnough"` + bag member kept; never loses the trail | — |
| new panel in a window updated in place (the old settings registry until a reload) | `auto`: the value comes from the layers that hold one, never the registry's default (§ 4.6); the migration waits for the next activation | — |
| new panel on one side, an older panel on another side of the same profile | the older side's writer, with its own default `human` written as nothing | residual (iv) of § 4.6 for an older server there |
| new panel on two machines, Settings Sync on | the user layer is cleared once for both (its witness travels with it); each machine clears its own overlays | the notification names a value that stays in force |
| Team server | untouched: the policy is evaluated by the local round machine (no `onExhausted` in `src_server`) | — |

## 6. Security

The hold was made a security property by [PLAN_only_a_person_releases_the_gate.md](../todo/PLAN_only_a_person_releases_the_gate.md):
the gated AI must not release it with an argument. This plan keeps that (requirement 8) and makes **configuration** a
second release road under `auto` (O2). The configuration lives in `<dataDir>/settings.json` and in the client's `env`,
both writable by any process running as the user — including the agent. That was already true of the policy for every
NEW session (and of the answer file itself, which the signed-answer plan exists for). The per-load refresh (§ 4.1) lets
a settings change decide how FUTURE rounds of an existing session end — but it never releases a hold this build raised
(§ 4.3): the configuration road releases only holds from before `auto` existed, which the owner chose to release (O2).
A hold raised under `human` stays the person's whatever the settings later say; the residual for that operator is the
answer-file forgery class, closed by [PLAN_a_signed_answer_releases_the_gate.md](../todo/PLAN_a_signed_answer_releases_the_gate.md),
which this plan names on its side — and, rarer, the downgrade edge written at the end of § 4.3. Fail-open is deliberate
and visible: `not_reviewed` is its own verdict, a Warning line and a note.

**The access model this claim is made under** (CodeRabbit on #1007, 2026-11-16). The session files under
`<dataDir>/sessions/` are as writable by a process running as the user as `settings.json` and the answer file are. A
process that writes the session file can delete the marker — or clear `HumanGate` itself, which needs no marker and no
`auto` at all. So the claim above is about the roads coai offers — a tool argument and the operator's configuration —
and not about a forged session file; that is the same file-forgery class as the answer file, and it is closed, if at
all, by the signed-answer plan, which must cover the session state too. This plan adds no protection against it and no
new way into it.

**An unusable settings file means `auto`** (the E1.S1 spec, § S1.8; decided at the re-review for epoch 2). A malformed,
empty or denied `settings.json` reads as no values (`SettingsFile.TryRead`, `SettingsFile.cs:110-127`), and no value
now means `auto` where it meant `human`. Kept so,
on the owner's words — *"если ревьюверы не отвечают, еще что-то — плевать. записали в логи"*: a damaged file must
not block the orchestrator. It opens no new road: a process that can damage the file can as well write
`COAI_ON_EXHAUSTED=auto` into it, and only holds from before `auto` are released either way. What is added is the
record: the server writes a startup note and a Warning when the file exists but gave no values, naming the file and
saying that the policy is `auto` by default. Failing closed (`human` on an unusable file) was weighed with the re-review
consultation and not taken.

**A read that failed by accident is read again** (the re-review judge, finding 2). At startup a BUSY file (a sharing
violation while the panel writes it) also gives no values, and the host records the file's stamp before reading
(`PanelServiceHost`), so nothing re-reads it until it changes: an operator who chose `human` would run `auto` until the
next settings change, and a legacy hold would be released meanwhile. So a read that was busy or unusable does not
record the stamp; the next deciding load reads the file again, and the policy is the operator's as soon as the file
can be read. The startup note names the busy case too.

**A failed read authorises no release** (the re-review gate, finding 1). Rounds run under such a read's default
(`auto`: no new hold), but the release of a legacy hold (§ 4.3) needs the policy to come from a read that succeeded —
a readable file, a file that is genuinely missing, or the process environment. While the read is busy or unusable a
legacy hold stays as it is, and the entry answers § 4.3's "could not be written yet — call again" sentence with the
reason "the settings file could not be read". Tests: a busy read with a readable `human` behind it releases nothing,
and the next deciding load runs `human`; a malformed file releases nothing until it is repaired or removed.

## 7. Boundaries with other plans

| Item | Built by | The other plan's part |
|---|---|---|
| Hold released only by the person's answer, `ClaimRefusal` | [only a person releases](../todo/PLAN_only_a_person_releases_the_gate.md) | this plan adds the `Autonomous` release in `ApplyAnyHumanDecision` and leaves every refusal of the AI's claim as it is |
| Signing the answer file | [a signed answer](../todo/PLAN_a_signed_answer_releases_the_gate.md) | must also treat `settings.json`'s policy as an input the agent can write (§ 6) |
| A cancelled round (`cancelled`, no verdict) — draft PR #923 | [a cancelled review](../todo/PLAN_a_cancelled_review_stops_its_round.md) | disjoint: a cancelled round produces no verdict, so `not_reviewed` never covers it; whichever merges second rebases `RoundMachine.cs`/`RoundEngine.cs` |
| `review_plan` again after `proceed` | [review_plan again](../todo/PLAN_review_plan_again_after_proceed.md) | disjoint; `not_reviewed` counts as a plan stage that advanced |

The same table, shortened, is added to each of those four documents in the plan's first PR.

## 8. Build order — three pull requests, RED first

**PR 1 — `coai-mcp` (release `mcp-v*`).**
1. RED: `AutoNeverCallsAPersonTests` — S1 (`Answered == 0`, budgets 1 and 3) → `not_reviewed`, `HumanGate` false,
   `AdvanceOnResolve`; S2 (feature cap: blocking → `good_enough`, nobody answered → `not_reviewed`, budget one); S3 →
   `good_enough`; empty `resolve` after `not_reviewed` advances the stage.
2. RED: `UnknownSettingValueTests` rewritten — `auto` and absent → auto; four words unchanged; unknown → Human + complaint.
   `GoodEnoughPolicyTests.WhenNobodyAnswered_ItStillCallsAHuman` stays (non-auto good_enough) and gains its auto twin.
3. RED: refresh — a session created under `Human` begins under `auto` and never holds; `EscalationsUsed` survives a
   toggle Escalate→auto→Escalate.
4. RED: a held session from an older build (config without `autonomous`; with and without recorded questions) is
   released under `auto` by whichever of begin, `resolve`, `status`, `ask_human`, the fresh-document check (`HeldSentence`) comes FIRST after the
   setting changes — one test per entry; its notice is withdrawn. A hold raised by this build under `human` is NOT
   released when the setting then becomes `auto`; `resolve(humanDecision: proceed)` is still refused on it.
   Added at the re-review for epoch 2: a legacy hold loaded and saved under `human`, then `auto`, is released; after the
   release one `resolve` through `PanelService` advances the stage exactly once in each of the four stages, whether the
   held round had been resolved or not, the decisions recorded before survive, and the code stage's cadence closes; a
   withdrawal that fails, a save that fails and a busy claim each leave the session held and unchanged on disk, and the
   next load releases it; the held round's note reads "released by policy auto" in the session file and in `coai.db`;
   an existing but unusable settings file runs `auto` and writes its startup note.
5. RED: `ask_human` under `auto` — `consult_first` without `consultId`, `ask_in_conversation` with an accepted one, the
   stand-down without consultants, the feature second-round answer; never waits (a test with a 0-second budget and no
   answer file returns at once).
6. RED: persistence — a session file written with `GoodEnough` + `autonomous` loads in a planted "older" shape (the
   `ASessionFileKeepsWhatANewerServerWroteTests` road) and keeps the member across a save.
7. GREEN in the order of § 4.1 → 4.5, 4.7 (server half); `FeaturesMode` entry; `RoundCommands`, bench.
8. CHANGELOG *Server* entry (the release check refuses an `mcp-v*` tag without it).

**PR 2 — extension (release `extension-v*`), after PR 1 is released.**
1. RED: `settingsShape.test.ts` default `'auto'`; manifest = `DEFAULTS`; `panelServerDefaultsAgreement.test.ts` gains
   the `onExhausted` case read from the C# defaults; env table of § 4.6 (all six cells). Added at the re-review for
   epoch 3 (U3): a window whose registry still answers the default `human` reads `auto` and writes no
   `COAI_ON_EXHAUSTED`, with the folder, workspace and user values and this side's overlay keeping their precedence.
2. RED: the migration — user value and overlays cleared once, each witness written with its clear (an overlay) or before
   it (the user layer, in the user settings), the marker last, a second activation does nothing, a choice made after it
   is kept; workspace value untouched. Added at the re-review for epoch 3: a second machine that shares only the user
   settings (its own `globalState`) keeps a choice made on the first after its run; a value Sync delivers during the
   awaited witness write is kept; a stale window's write that brings a cleared overlay back is cleared by the next
   activation; two windows over a fake of `globalState` as VS Code's source reads it (one whole value per extension);
   the notification names a value that stays in force, and says Auto only where the window runs it.
3. RED (page run): rounds-log detail shows a note on a non-skipped row; `not_reviewed` filters like any verdict.
4. GREEN; select option; help in five languages (`helpCoverage.test.ts`); README; CHANGELOG *Extension* entry.
5. `test:*-compat` seam scripts: a new panel against the previous `coai-mcp` writes `good_enough`.

The order inside PR 2 (the E2 stories document § 6): E2.S1, then its fix forward for U3; the E2.S2 spec revised by Fable
to epoch 3; E2.S2's code; the S3 page slice merged from its own branch; the S3 words last, because they describe S1 and
S2 as built.

**PR 3 — `dew_flow_conventions`**: `common/coai-review-gate.md` snippet v6; then the pin bumps in consumers — in-flight consumer
releases asked about and open PRs counted against the cap before the promotion starts.

## 9. What grows

Nothing that grows with use. One more Serilog line per round that runs under `auto` (beside the existing `Closing`
line, same files and the same retention); the reason in the existing `rounds.note` column. The migration adds three
things of fixed size, each kept for good on purpose — each is what stops a repeat, so retiring it would rerun the
reset:

- one `globalState` key per profile, `coai.onExhaustedAutoMigration` — a record of the version that completed the run
  and, while a run is in flight, the claim and what each location held (one short string per location: under 1 KB at
  ten sides);
- one member, `onExhaustedAutoMigration` (a version string), in each overlay record it cleared;
- one key in the person's user `settings.json`, `coai.onExhaustedResetToAuto` (one string), which Settings Sync carries.

No table, folder or process. The one in-flight state is the claim: a claim older than a minute is taken by the next run,
so a dead host's claim needs no sweep; a run that died leaves its record, which the next activation resumes.

## 10. Open questions, each with the assumption the plan takes

- **Q1 — workspace-scoped values.** A `coai.onExhausted` in a repository's `.vscode/settings.json` overrides user settings.
  Clearing it edits a file the repository's team owns. *Assumed:* not edited; the gate section says *"this workspace sets
  When the rounds run out to X"* when a workspace value overrides `auto`. The owner may flip this to "clear it too".
  Owner's answer 2026-11-17: not edited — the assumption stands.
- **Q2 — an empty round with rounds left.** *Assumed:* `not_reviewed` at once (§ 4.2). Alternative: one retry while the
  budget allows.
- **Beside the plan — `globalState` is one stored value per extension** (the E2.S2 spec, U4; verified at the re-review in
  VS Code's `extHostMemento.ts`). `src_vs_code/src/sidesSeen.ts` and `cliPathKeep.ts` say one key per side means two
  windows never overwrite each other; a stale window's write replaces every key. This plan's migration is built for it
  (§ 4.6); the two modules are not this plan's work. A todo of its own, not a build here.

## 11. Consultations (2026-11-16 and 2026-11-17, each with what it was checked against)

- **C1** persistence shape → GoodEnough + trailing `autonomous` member; verified `PanelConfig`'s bag (`SessionState.cs:147-157`)
  and `SessionScrub.ConfigOf`. Consultation `af73fef3…`, solved.
- **C2** refresh per begin → sound for future rounds; keep `EscalationsUsed` and pending decisions; flagged that a
  config release of a hold widens configuration authority — taken to the owner as O2. Consultation `21681354…`, solved.
- **C3** verdict split → `not_reviewed` for ANY auto round where nobody answered, including the feature stage (which
  bypasses S1 through `GroundFor`, verified `RoundMachine.cs` `GroundFor`); `good_enough` otherwise; `MayProceed` and the
  bench list verdicts explicitly (verified). Consultation `7e22427e…`, solved.
- **C4** (2026-11-17) the E2.S2 spec's U1, Settings Sync → both question consultants read the user layer's witness as a
  change to the plan; the owner chose the witness and the stop over a narrowed done-when 3 (entry 2.2). Consultation
  `36f6b75a…`.
- **C5** (2026-11-17) the re-review for epoch 3, codex, consultation `70202c20…`, folded into § 4.6:
  (a) `Memento.update` writes the extension's whole `globalState` object; a window learns other windows' writes only
  through `onDidChangeStorage`, so a stale window's write of any key overwrites another's witness or clear, and a
  write–pause–read-back claim excludes nothing — **verified** by the coordinator and re-read by the judge in
  `extHostMemento.ts` (`update` sets `_value[key]` and schedules `setValue(…, this._value)`; the listener replaces
  `_value`); (b) `machine`/`machine-overridable` are not synced by default and `application` is shared across profiles —
  VS Code's documented behaviour, **not read in its source here**; the choice (the witnessed value's own scope) holds
  whichever way those read; (c) Sync can deliver during the awaited witness write and a fixed delay proves nothing —
  accepted on reasoning; how Sync's merge treats two machines' witness values **not checked**; (d) the user-layer clear
  can be skipped, and it was what woke another side's mirror — **verified** in `extension.ts:603-616` (any `coai` change
  mirrors); that a configuration event can reach a window before its `globalState` copy changes is **not measured**
  (`research/RESULTS_side_registry_reaches_the_other_window.md` gives only upper bounds); (e) the notification must not
  say Auto where another value stays — accepted.

## 12. Plan round 1 (2026-11-16) — `proceed`, 2 of 2 reviewers, gating 6 of 6

| # | Finding | Decision |
|---|---|---|
| 0 | a policy change lets the gated agent release a hold | accepted — only holds from a build before `auto` are released (§ 4.3, § 6) |
| 1 | policy refreshed on begin only, not on every release path | accepted — `CurrentPolicy` on every load that decides (§ 4.1), one test per first call |
| 2 | unattended installs can still reach a person | accepted as a narrowing — the guarantee is stated exactly (requirement 5); the post-consultant road is the owner's O3 |
| 3 | an env block copied before features settle runs `human` on an old server | accepted — the copy waits for features, else writes `auto` explicitly (§ 4.6); superseded by entry 2.1: the copied block never carried a policy |
| 4 | `consult_first` would log the question's text | accepted — id and decision only, and the existing line too (§ 4.4) |
| 5 | a cancelled round has no auto continuation | rejected — the caller cancelled it; no hold, no card, the next call runs the round again |

## 13. Definition of Done

- [ ] Under `auto`, no code path sets `HumanGate`, writes a notice, or waits in `ask_human` — each of S1–S6 has a test.
- [ ] `not_reviewed` exists end to end: machine, `AnswerFor`, `MayProceed`, bench, tool text, snippet v6.
- [ ] Every auto decision is in the Serilog file, the round's `note` (session + `coai.db`) and on the rounds-log page.
- [ ] Old holds are released by whichever call comes first, logged, notices withdrawn before the release is saved, the
      release noted on the held round; a failed step leaves the hold for the next call; one `resolve` then advances.
- [ ] Panel and server defaults agree, and a test reads both.
- [ ] The migration cleared the user value once for every machine Settings Sync reaches (its witness in the user
      settings) and every overlay once per profile of a machine (its witness in the record); a later choice is kept on
      this machine and on the others, except in § 4.6's residuals (i)–(iii), each named; the announcement is true for the window that shows it; § 4.6's residuals are
      written down where E2 documents the migration.
- [ ] Old server + new panel runs `good_enough` and says so; old panel + new server runs `auto`; a downgrade keeps the trail.
- [ ] Under `human`, behaviour is byte-for-byte today's (the existing hold suites pass unchanged).
- [ ] Docs, help in five languages, CHANGELOG entries; boundary tables in the four related plans.
- [ ] Rollout reported as committed / built / released / running, per side.

## Deviations (recorded at promotion, 2026-11-19)

What shipped differently from the plan as approved, or was found while building it. Each line says where it is detailed.

- **P3 — an erratum in requirement 5 and § 4.4's parenthesis.** They were wider than what was built: a production-risk question reaches the person, in the Planning or Released phase or with the mode off, without the consultants first. Decided a wording erratum (consultants `df932f5e…`), not a plan change; corrected in the README and the help (five languages) in #1024 and in 0.89.1's CHANGELOG. The server's own text at `Tools.cs:410-411` is equally wide and was not changed. Detailed: the E3 stories document § 9 P3, and #1024.
- **P6 — `ask_human` is silent about a legacy hold it released** (review calls and `status` say it): an E1 deviation. Detailed: the E3 stories document § 9 P6 (it is an E1 residual).
- **P7 — the released feature rule is silent about a false finding.** It tells an AI to land a blocking `good_enough` finding as a new PR without saying a false one may be rejected. Queued for the next conventions release. Detailed: the E3 stories document § 9 P7.
- **P2 — `plan-change-control` is silent about what opens an epoch when every plan round ends `not_reviewed`.** A conventions follow-up, not built here. Detailed: the E3 stories document § 9 P2.
- **E2 deviations from its spec, as recorded in #1018's body:** no RED-first for some E2.S2 scenarios; `onExhaustedMigration.ts` is over the spec's size; an extra source scan; a second `ExtensionMode.Test` spread. Detailed: PR #1018 and the E2 stories document.
- **§ 4.8, residual A — an old rule promised a second feature round on the person's request until its bump.** Closed by the nine bumps of E3. Detailed: § 4.8 A and the E3 stories document (the stale-promise residual).
- **The coai MCP server lost the conventions gate session twice;** the plan and code rounds were rerun. Detailed: nowhere beyond this line (the rounds rerun left no document).

### After the promotion (2026-11-19, added 2026-11-24)

P7 and P2 above, and two conventions-side follow-ups of E3 (post-deploy item 3 could not show the four coai rules read;
the behaviour harness had no moment for `not_reviewed` and `consult_first`), were closed the same day by the conventions
plan `dew_flow_conventions · research/PLAN_coai_auto_followups.md` (#185, `release` `c598a11`). What it changed for this
repository, carried over:

- **P7 closed.** `coai-feature` v5: a blocking `good_enough` finding at the cap is verified like any other — a true one
  lands as a NEW pull request, a false one is rejected with its reason. Here: the pin bump #1027 regenerated the snippet
  (v20, `FEATURE_VERSION` 5, `SNIPPET_BODY_SHA` `edc9eea979fd2b2d`), and extension **0.89.2** shipped it (#1029, #1030).
- **P2 closed** in conventions: a plan round that ended `not_reviewed` under `auto` opens epoch 1 `by nobody` — a counting
  baseline, never an approval; the interim re-review path is open to such a branch. Nothing here changed.
- **Post-deploy item 3 met.** `smoke-rules --task implement --only <id>…`, run in this repository at `main` `8ff658d5`,
  showed all four coai gate rules read completely by Claude and by Codex.
- **P6 stays open** — an E1 residual of this repository; nothing above touched it.

Problems met on the way, each with where it is recorded:

- **The pin bump released nothing.** #1027 was squash-merged with a `chore(rules):` title, which release-please does not
  release. A CHANGELOG-only `fix(extension):` (#1028) was refused by the docs-only guard, as the operator's rule wants, and
  closed. The release went through a `Release-As: 0.89.2` commit that touches only `src_vs_code` code (#1029), the shape
  [PLAN_the_release_guards_contradict.md](PLAN_the_release_guards_contradict.md) records (#605). Recorded in
  [module_extension.md](module_extension.md) § *A pin bump that regenerates the snippet must be a releasing commit*.
- **The installed extension did not update itself.** On 2026-11-19 the owner's WSL window still ran 0.87.0 while 0.89.1
  was on the Marketplace, so the *Auto* option was not in the menu; on 2026-11-24 it still ran 0.89.1 with 0.89.2 out
  for five days. Both were installed by hand (`code --install-extension …@<version> --force`). Why the automatic update
  did not run there was not investigated. The first hand install on 2026-11-19 failed: the WSL `code` CLI could not
  reach the Windows side (`UtilAcceptVsock … accept4 failed 110`); it worked on 2026-11-24.
- **A snippet assertion cannot be seen red by moving the mount back.** `prepare-gate` refuses a conventions checkout
  that differs from the index gitlink, so the build fails before the test runs. The RED for the v5 assertions was taken by
  changing the generated half (`src/generated/featureRule.ts`). Recorded in [module_tests.md](module_tests.md).
- **The real-editor scenario *skip-list Add* (S3-H1) failed again** on #1027 and passed on its rerun.

## Closure (2026-11-24)

The plan is closed: no more work is done under it. Anything below under *Not done* that is taken up later gets a plan of
its own (or the plan named beside it); it does not reopen this one. The ledger stays as it is — epoch 3 was the last
approval, and no entry is added.

**Done**

- **E1, the server:** the whole `auto` contract — `not_reviewed`, `good_enough` with its reason, the legacy-hold release,
  `consult_first`, the logging — merged #1011, released as coai-mcp 0.66.0 (#1012).
- **E2, the extension:** `auto` is the default for everyone, with the one-time reset, its Settings Sync witness and the
  two-window claim; the rounds-log page — merged #1018, released as 0.89.0 (#1022), VSIX and Marketplace checked.
- **E3, the conventions text:** the four coai gate rules speak `auto` (snippet v6, document v4, feature v4, question v2),
  conventions #182 and #183, `release` `5d2d5ec`, nine pin bumps (#1024 here), extension 0.89.1 (snippet v19).
- **The feature review** of the three epics: `proceed`, 0 findings.
- **The follow-ups, P7 and P2, closed** by the conventions release whose `release` ref was promoted to `c598a11` on
  2026-11-19 (#185; the conventions have no version numbers, the promoted sha is the release): `coai-feature` v5 and the epoch `by nobody`; here
  the pin bump #1027 and extension 0.89.2 (#1029, #1030), snippet v20.
- **Post-deploy item 3 met** at `main` `8ff658d5`: Claude and Codex read all four coai gate rules completely.
- **The owner's machine** runs 0.89.2 (installed by hand on 2026-11-24, see below).

**Not done**

- **P6 — `ask_human` is silent about a legacy hold it released** (review calls and `status` say it). An E1 residual; not
  built.
- **The four E2 deviations** recorded in #1018's body stay as they are: no RED first for some E2.S2 scenarios,
  `onExhaustedMigration.ts` over the spec's size, an extra source scan, a second `ExtensionMode.Test` spread.
- **The short descriptions do not name the production-risk path.** The setting's description in `src_vs_code/package.json`
  and the matching help line in `src_vs_code/src/help.ts` say the AI "may still ask you a question its consultants could
  not settle, or when no consultant can be had"; neither says that a production-risk question reaches the person at once
  in the Planning or Released phase. The long help and the README were corrected in #1024; these two were not.
- **The help in German, Spanish, Russian and Ukrainian** (written by a Sonnet coder in E3) has not been read by a native
  speaker.
- **Why the extension did not update itself in the owner's WSL window** (0.87.0 on 2026-11-19, 0.89.1 on 2026-11-24) was
  not investigated; both updates were installed by hand.
- **The real-editor flake *skip-list Add* (S3-H1)** still fails now and then (#1027, #1040, each passed on a rerun). Its fix
  belongs to [todo/PLAN_the_skip_list_add_reads_back_after_the_write.md](../todo/PLAN_the_skip_list_add_reads_back_after_the_write.md),
  not to this plan.
- **The behaviour checks of the conventions** (`held-gate`, `auto-gate`) drive Claude only; Codex is not covered
  (`dew_flow_conventions · research/PLAN_coai_auto_followups.md` § *Closure*).

## Epics

Three epics on the three release boundaries of § 8, in that order: one pull request and one release each, and an epic
starts only after the release of the epic before it. The server is NOT split: `auto` is one behavioural contract, and
`--features` may not list `onExhaustedAuto` before `ask_human` is done, so half a server would be a release nobody can
use. Stories documents are NOT written now: one per epic, `research/PLAN_auto_never_asks_a_person_E<n>.md`, written by Opus
when that epic starts (`subagent-models.md`, pipeline step 3). The stories named under each epic are the intended cut,
one line each, so the scope can be checked today. Cornerstone quota: Q = ⌈2·E/5⌉ = ⌈2·3/5⌉ = 2. *DoD item n* below
counts the boxes of § 13 from the top (1–10).

*Question consultants (2026-11-16, two answers, consultation `8cb0dcd0…`), used for this split.* Both recommended three
epics on the release boundaries and an unsplit server. What is important, as areas: **(a)** hold authority and upgrade
provenance — which holds the configuration may release; the nullable `autonomous` member decides it, so it is
security-relevant and must be read BEFORE the refresh stamps it; **(b)** the persisted compatibility of the session file
— `GoodEnough` with a trailing nullable `autonomous`, through an older build's load and save; **(c)** the one-time
settings migration — a change to a person's saved choices that cannot be taken back.

*The two cornerstone stories, and why (Fable's pick; Opus marks each in its stories document and raises the count).*
**E1.S1 — the stored policy and the hold's authority, areas (a) and (b).** One story, because one member carries both:
an absent `autonomous` is at once the shape on disk and the proof that a hold predates `auto`. Two specs could disagree
on what "absent" means, and the error is silent both ways — a hold released that was the person's, or an orchestrator
still blocked. Who may release a hold is also an authorisation judgement (Fable by the security row).
**E2.S2 — the one-time migration, area (c).** It runs once on every installation and rewrites saved choices. A marker
written too early leaves values unmigrated for ever; a run that repeats resets a choice made after it; and no later
release can repair either for the people it already ran on.
No cornerstone in E3: it is rule text, reviewed as every rule change is, and a wrong word there is loud and repairable.

### E1 — The server: the whole `auto` contract (`coai-mcp`) · depends on nothing

- **Stories document.** [PLAN_auto_never_asks_a_person_E1.md](PLAN_auto_never_asks_a_person_E1.md) — written by Opus
  2026-11-16 when the epic started: the three stories below, E1.S1 marked `model: fable` (its spec,
  [PLAN_auto_never_asks_a_person_E1_S1_spec.md](PLAN_auto_never_asks_a_person_E1_S1_spec.md)), S2 and S3 briefed in
  full, notes N2–N4 settled; both revised to epoch 2 before any code.
- **Goal.** A `coai-mcp` that under `auto` — the meaning of an absent key — raises no hold, never waits and writes no
  card: a round nobody answered ends `not_reviewed`, spent rounds end `good_enough`, a hold from before `auto` is
  released by whichever call reads it first, `ask_human` answers at once, and every such decision is logged and noted
  on the round. Under the four other values nothing changes.
- **Scope.** `src_mcp`, `src_bench` and the documents: § 4.1–4.5, and the server halves of § 4.6 and § 4.7.
  *The value and where it lives* — `PanelSettings.PolicyOf` and `Config`, `PanelConfig.Autonomous`
  (`src_mcp/core/Rounds/SessionState.cs`), the defaults (`PanelConfig.Uniform`, the harnesses that need a person),
  `SessionStore` and `SessionScrub`, `CurrentPolicy` on every load that decides (`RoundEngine`, `PanelService`,
  `AskHumanService`, `HeldSentence`), and the bench's reader of the stored policy (note N2).
  *The hold* — `RoundEngine.ApplyAnyHumanDecision` and the withdrawal of its notice before the release is saved (`Escalations`, a result that tells withdrawn, not there and failed apart), and the note on the held round.
  *The verdicts* — the three sites in `src_mcp/core/Rounds/RoundMachine.cs`, `RoundVerdict.NotReviewed`, and every
  reader: `RoundEngine.AnswerFor`, `RoundCommands.MayProceed`, `src_bench/CoaiBench/Running/RoundRunner.cs`,
  `CadenceState`'s wording, the verdict lists in `src_mcp/src/Tools.cs`.
  *`ask_human`* — `src_mcp/core/QuestionConsult/AskGate.cs`, `src_mcp/src/Server/AskHumanService.cs`.
  *The record* — `RoundAudit.Autonomous`, `RoundRecord.Note` (no schema step).
  *The words* — the server instructions and the help string in `src_mcp/src/Program.cs`, the tool texts in `Tools.cs`,
  the startup note for a policy pinned in the process environment, `src_mcp/src/Server/FeaturesMode.cs`.
  *Documents* — `research/module_server.md`, `research/module_core.md`, the boundary tables in the four related plans
  (§ 7), the CHANGELOG *Server* entry. Not here: anything under `src_vs_code`, and the shared snippet.
- **Depends on.** Nothing. Draft PR #923 (a cancelled review) edits the same two files; whichever merges second
  rebases (§ 7).
- **Done when.** Steps 1–8 of PR 1 (§ 8) are green, each RED seen first, and:
  1. under `auto` each of S1–S6 has a test, and none sets `HumanGate`, writes a notice or waits (DoD item 1);
  2. `not_reviewed` is known to the machine, `AnswerFor`, `MayProceed`, the bench and the tool text (DoD item 2, all
     but the snippet);
  3. every auto decision is in the Serilog file and in the round's `note`, in the session file and in `coai.db`
     (DoD item 3, all but the page); no log line carries the text of a question;
  4. a hold from a build before `auto` is released by whichever of begin, `resolve`, `status`, `ask_human` and
     the fresh-document check (`HeldSentence`) comes first — one test each — and its notice is withdrawn (DoD item 4). A hold THIS build raised
     under `human`, or under a workspace-scoped value the migration does not touch (Q1), is NOT released by a later
     `auto`, and `resolve(humanDecision: "proceed")` is still refused on it (requirements 4 and 8);
  5. `ask_human` never waits, and it reaches a person only after the consultants, as the owner decided (O3):
     `consult_first` without a `consultId`; `ask_in_conversation` with one the desk accepts, or when no consultant can
     be had. The guarantee is requirement 5's sentence and nothing wider;
  6. a release condition: a session file of this build passes an older build's load and save with `autonomous`
     absent, `false` and `true`, and reads back with the same meaning each time (DoD item 7, the downgrade part);
  7. `--features` lists `onExhaustedAuto` only from the commit after which 1–6 hold;
  8. under `human` the existing hold suites pass unchanged (DoD item 8).
  The PR is merged, `mcp-v*` is released and verified. E2 does not start before that.
- **Cornerstone areas.** (a) and (b) — together the recommended cornerstone, E1.S1.
- **Intended stories.**
  - **E1.S1 — the stored policy and the hold's authority (cornerstone; spec by Fable).** § 4.1 and § 4.3: `auto` and
    an absent key parsed, `autonomous` nullable on disk, the defaults, the round-trip through an older build,
    `CurrentPolicy` on every deciding load with the marker read before the stamp, the legacy hold released at every
    first entry, this build's hold kept. Steps 2, 3, 4 and 6.
  - **E1.S2 — the verdicts and their record.** § 4.2 and § 4.5 (server): the three sites, `not_reviewed`, every reader
    of a verdict, the Serilog line and the round's note. Step 1.
  - **E1.S3 — `ask_human` never blocks, the words, the features entry.** § 4.4, § 4.7 (server) and the server items of
    § 4.6: `consult_first`, no question text in a log, instructions and tool texts that are true under every policy, the
    startup note, the documents, and `onExhaustedAuto` in `--features` as the last commit. Steps 5, 7 and 8.
- **Release.** `mcp-v*`, with the CHANGELOG *Server* entry (the release check refuses the tag without it); reported as
  committed / built / released / running.

### E2 — The extension: everyone is on `auto`, and can see what it did · depends on E1 released

- **Goal.** After the upgrade everyone is on `auto` (O1): the panel's default and its select, a one-time migration of
  saved values, an env block that is right for the binary it meets, and a rounds-log page and a help text that show and
  say what `auto` did.
- **Scope.** `src_vs_code` and the reader-facing documents: the panel half of § 4.6, the page item of § 4.5, § 4.8.
  Under `src_vs_code/`: `package.json` (enum, default, description; the witness setting `coai.onExhaustedResetToAuto`);
  `src/settingsShape.ts` (`OnExhausted`, `DEFAULTS`, `asOnExhausted`, `envBlock` reading the features);
  `src/modelKeys.ts` (the value read from the layers that hold one, § 4.6); `src/binaryFeatures.ts`; `src/panelView.ts` (the select; the gate
  section's two lines — "this coai-mcp predates Auto" and "this workspace sets …"); the copy action is left as it is (entry 2.1); the migration — a module of its own, started after the settings mirror,
  over the user value and EVERY side's overlay in `globalState` (`overlayKeyOf`, `src/coaiInstall.ts`;
  `src/sidesSeen.ts`). `src/catalogMigrationHost.ts` is the precedent for queued runs and for reading the user layer
  through `globalValue`, NOT for its reach: it migrates this side's overlay only. `src/roundsLog.ts` (the note on every
  row that has one); `src/help.ts`, `src/helpContent.ts` and the four translations; `README.md` there and a
  `test:*-compat` seam script. At the root: `README.md`, `ARCHITECTURE.md`, `research/module_extension.md`, and the
  CHANGELOG *Extension* entry (`src_vs_code/CHANGELOG.md`).
- **Depends on.** E1 RELEASED: the env table reads `onExhaustedAuto` from a real binary, and the seam script runs the
  new panel against the previous `coai-mcp`.
- **Done when.** Steps 1–5 of PR 2 (§ 8) are green, each RED seen first, and:
  1. the manifest, `DEFAULTS` and the C# defaults agree, and one test reads both sides; a window that still holds the
     old registry's default reads `auto` (DoD item 5);
  2. each of the six cells of the env table has a test; the copied block carries no `COAI_ON_EXHAUSTED` (entry 2.1); a new panel with the previous server writes `good_enough` and the gate
     section says so (DoD item 7, the panel part);
  3. the migration clears the user value and the value in every side's overlay, leaving a location that holds `auto`;
     each location's witness is written with its clear (an overlay) or before it (the user layer, in the user
     settings), so a resumed run, a second activation and a second machine Settings Sync reaches each keep a choice
     made afterwards — except in § 4.6's residuals (i) and (ii), each named in the documents (a host that dies between
     the user witness and its removal keeps the OLD value; a stale window or a Sync delivery in the windows § 4.6 names
     can lose a later choice); its marker is written only after every location is settled and the mirror ran, so an interrupted
     run resumes; two windows activating together leave one result; they show one notification when each hears the other's
     write within the claim's pause, and may show two when they do not (§ 4.6, residual iii) — the claim is a best
     effort, no protocol available to an extension selects one winner without that delivery, and none is promised; a stale window's write that brings a cleared overlay back is
     cleared by the next activation; a second activation does nothing. **A workspace-scoped value is NOT migrated
     (Q1)**: it stays, and the gate section names it. All of it is tested without the panel ever being opened, the two
     windows and the two machines through a fake of `globalState` as VS Code's source reads it (DoD item 6);
     *[narrowed at the re-review for epoch 3: the one notification and the kept choice hold within § 4.6's residuals]*
  4. the notification and the help state the guarantee as requirement 5 does — the gate does not hold, wait or write
     a card; the AI's own question still reaches the person after the consultants, or when no consultant can be had (O3) — and never "never asks you
     anything"; the notification says Auto only in a window that runs Auto, and elsewhere names the value in force and
     where it is set (§ 4.6);
  5. the rounds-log page shows the note on a row that was not skipped and filters `not_reviewed`, tested by RUNNING
     the page (DoD item 3, the page part);
  6. help in five languages (`helpCoverage.test.ts`), the documents, and a CHANGELOG entry that says a chosen value is
     reset to Auto once (DoD item 9, the extension part).
  The PR is merged, `extension-v*` is released and verified.
- **Cornerstone areas.** (c) — the recommended cornerstone, E2.S2.
- **Stories document.** [PLAN_auto_never_asks_a_person_E2.md](PLAN_auto_never_asks_a_person_E2.md) — written by Opus
  2026-11-17 when the epic started: the three stories below, E2.S2 marked `model: fable` (its spec, written by Fable
  after it, [PLAN_auto_never_asks_a_person_E2_S2_spec.md](PLAN_auto_never_asks_a_person_E2_S2_spec.md)), S1 and S3
  briefed in full, N5–N8 handed to the spec. Brought to epoch 3 by the re-review the same day: C4–C6 and the file table
  with the spec's X1–X5, the S1 fix forward for U3 briefed (its § 7.3), the S3 "kept" sentence, and the list of what
  Fable revises in the spec (its § 10).
- **Intended stories.**
  - **E2.S1 — the value, and what the server is sent.** The manifest, the shape, the select, the features, the six env
    cells, the two gate-section lines, the defaults-agreement test, the seam script; and the value read from the layers
    that hold one (U3, a fix forward after its first three commits). Steps 1 and 5.
  - **E2.S2 — the one-time migration (cornerstone; spec by Fable).** The user value and every side's overlay, the
    witnesses (the user layer's in the user settings, each overlay's in its record), the marker and the resume, two
    windows at once and two machines under Settings Sync, the workspace value left alone and named, the notification
    true for its window, adoption without the panel. Step 2.
  - **E2.S3 — what a person reads.** The note on the rounds-log page (page run), the help in five languages, the
    READMEs, `ARCHITECTURE.md`, `research/module_extension.md`, the CHANGELOG entry. Steps 3 and 4.
- **Release.** `extension-v*`, after E1's `mcp-v*` is out; reported as committed / built / released / running.

### E3 — The conventions: the words every consumer's AI reads · depends on E1 released, after E2

- **Stories document.** [PLAN_auto_never_asks_a_person_E3.md](PLAN_auto_never_asks_a_person_E3.md) — written by Opus
  2026-11-18 when the epic started: the two stories below, no cornerstone; S1 gives the wording of the four rule files
  verbatim, the markers (v6, v4, v4, v2) and the two-way reading; S2 the release, the nine pin bumps, this repository's
  snippet, the feature review and the promotion. N1 settled there (its § 2.3, § 4.5); its findings for this plan are its § 9.
- **Goal.** The shared gate text is true for `auto`: an AI that meets `not_reviewed` says so and goes on, one that
  meets `consult_first` consults and comes back, and nothing promises a person the default never calls.
- **Scope.** `dew_flow_conventions` (PR 3 of § 8, its own release) and the pins. `common/coai-review-gate.md`, step 6:
  `not_reviewed`, and `call_human` as the operator's choice rather than the default. The sibling files that name the
  same verdicts or `ask_human` — `common/coai-document-gate.md`, `common/coai-feature-gate.md`,
  `common/coai-question-consultant.md` (note N1; § 4.7 names only the first). In this repository: the pin bump, the
  generated snippet (`src_vs_code/scripts/prepare-gate.mjs`) and its version and body hash in
  `src_vs_code/src/claudeSnippet.ts`. Then the pin bump in every other consumer. The change is named in a
  `dew_flow_conventions` plan too, as a change that crosses a repository boundary must be.
- **Depends on.** E1 released: a rule may not describe a verdict or a status that no released server gives. After E2
  by order, not by need: coai's own pin bump changes the snippet the extension hands out, so it lands when no extension
  release is open.
- **Done when.**
  1. `not_reviewed` → say in the summary that the stage was not reviewed and why, `resolve`, proceed;
     `consult_first` → consult, then `ask_human` again with the `consultId` (DoD item 2, the snippet part);
  2. the text carries the plan's qualifications and no wider promise: under `auto` the gate itself asks nobody, and
     `ask_human` reaches the person only after the consultants (O3); a person is still called under `human`, and where
     a workspace-scoped value — which the migration does not touch (Q1) — keeps another policy;
  3. no contradiction in the window where E1 and E2 are live and a consumer has not bumped, read both ways and written
     into the PR: the old text against the new server (the reply's own instruction carries `not_reviewed` and
     `consult_first`; the `call_human` step simply never fires), and the new text against a server older than E1 (the
     `call_human` step is still there and still true);
  4. the conventions checks are green, the release is cut and promoted — in-flight consumer releases asked about and
     open PRs counted against the cap BEFORE the promotion — and every consumer's pin bump is merged, coai's with the
     regenerated snippet and `snippetVersion.test.ts` green;
  5. then `review_feature` over the three epics, and the plan is promoted with its deviations, the parent and the
     three stories documents together (DoD items 9 and 10).
- **Cornerstone areas.** None.
- **Intended stories.**
  - **E3.S1 — the rule text.** The wording in the four rule files, which version markers move (N1), and the two-way
    reading of done-when 3.
  - **E3.S2 — the release and the pins.** The conventions release and its promotion, the pin bump in every consumer,
    coai's with the regenerated snippet; then the feature review and the promotion of this plan.
- **Release.** A `dew_flow_conventions` release and its promotion, then one pin-bump PR per consumer. No `mcp-v*`. The
  regenerated snippet reaches people with the extension release that follows coai's pin bump (N1).

### Notes from the split (2026-11-16) — each one is settled in the stories document of its epic

- **N1 — the snippet is more than one file (E3).** § 4.7 names `common/coai-review-gate.md` and "v6". The same verdict
  lists stand in `dew_flow_conventions · common/coai-document-gate.md:60-62` and `common/coai-feature-gate.md:57,72`,
  and `ask_in_conversation` in `common/coai-question-consultant.md:55` — each file with a marker of its own
  (`coai-document v3`, `coai-feature v3`, `coai-question v1`). A document round and a feature review can end
  `not_reviewed` too (§ 4.2). Also `src_vs_code/src/claudeSnippet.ts:119-122` and
  `src_vs_code/scripts/prepare-gate.mjs:12-15` say the gate half stays v5 because its body is hashed against the
  conventions migration baseline `5d6984eb`. Checked at the pinned conventions commit 4c9a684: no `.mjs` under `tools/`
  names that baseline any more, and the rule file was last edited by conventions #83 (2026-11-05). NOT checked: whether
  a tool still reads `research/shared-rules-migration-map.json`, which does name it. E3.S1 decides which markers move,
  and which extension release carries the regenerated snippet.
- **N2 — one more reader of the stored policy (E1.S1).** `src_bench/CoaiBench/Running/SettingsApplied.cs:66-75`
  compares the asked `COAI_ON_EXHAUSTED` with the session's `onExhausted`. A bench run that asks `auto` would report
  `auto` against `GoodEnough` as a mismatch. It learns `autonomous` with the other readers.
- **N3 — `ask_human` on a hold that `auto` keeps (E1.S1 and E1.S3).** The first row of § 4.4's table says no holds
  exist. By § 4.3 one kind can: this build's own `human` hold. The reply for it is written in E1's stories — at once,
  pointing at the hold's own notice, never a wait (requirement 5); § 4.7 now states the promise exactly.
- **N4 — the window between the two releases (E1, E2).** An old panel that shows *Human* never wrote the key, so the
  new server runs `auto` for it (§ 5, as O1 wants), and nobody is told before E2's notification. E1's CHANGELOG *Server*
  entry says so, and E2 starts as soon as E1's release is verified.
- **N5 — the reach of the migration (E2.S2).** Every side's overlay is in the client's `globalState`
  (`src_vs_code/src/coaiInstall.ts:345-363`), so one window can clear them all. The spec says how every overlay key is
  found — the side registry (`src/sidesSeen.ts`) or the `coai.settingsOverlay@` prefix — and that another side's server
  settings file is rewritten only when a window of that side next mirrors it.
- **N6 — two windows at once (E2.S2).** A `globalState` marker is no lock: two windows can both read "absent" before
  either writes it (the cadence consultation for epics 1–3, `d5665d44…`, 2026-11-16; `catalogMigrationHost.ts:34`'s
  queue serialises one extension host only). The spec names the coordination across windows of every side of one
  profile, and the test that holds window B after its read while A finishes and the person chooses a value.
- **N7 — the mirror after the migration is explicit (E2.S2).** Overlays are written with `globalState.update`
  (`sideSettings.ts:36-43`), which fires no configuration change, so the listener of § 4.6 does not run. The migration
  calls `mirrorSettings` itself when it is done, as the catalog migration does (`extension.ts:445-448`).
- **N8 — a resumed run keeps a choice made after the interruption (E2.S2).** "A choice made afterwards is kept"
  (E2 done-when 3) must hold for a run that stopped between its clears and its marker, too (CodeRabbit on #1007): a
  rerun that blindly repeats every clear would reset a value the person chose in between. The spec records, before the
  first clear, what each location held, and a resumed run clears a location only while it still holds that recorded
  value; a test interrupts after the first clear, sets `human`, resumes, and finds `human` kept. *Added at the re-review
  for epoch 3 (the E2.S2 spec, U2):* a location whose clear was begun is never cleared again, so a host that dies
  between the user layer's witness and its removal leaves that one value in place (§ 4.6, residual i).

### Notes from the re-review for epoch 3 (2026-11-17) — settled in § 4.6 and the E2 stories document

- **N9 — Settings Sync (E2.S2; entry 2.2).** The user layer is one file on every machine Sync reaches; `globalState` is
  per machine. Settled by the user layer's witness in the user settings, with the scope of the value it witnesses.
- **N10 — `globalState` is one stored value per extension (E2.S2; § 11 C5, point (a)).** A stale window's write puts
  back every key. Settled by where each witness lives, by reading again after every `await`, and by writing the marker
  only from a copy in which every location is settled; what stays open is § 4.6's residuals.
- **N11 — a window updated in place (E2.S1; the spec's U3).** Settled by reading the value from the layers that hold one.

### The cornerstone ledger

The two stories named are the intended marks, within the quota. The count rises in the commit of the stories document
that marks each one (`subagent-models.md`, *The cornerstone ledger*).

Cornerstones: 2 of 2 — E1.S1, E2.S2
