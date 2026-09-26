# PLAN — The consultant is called on a cadence, not only when an agent admits it is stuck

> Status: **IMPLEMENTED, 2026-09-26.** All six epics shipped; released as extension 0.56.0 · server
> 0.38.0 (2026-09-26), and the live check ran under `require` the same day (its row is in *The symptom*).
> Epic 1 PR #540; epic 2 PR #548; epic 3 PR #549; epic 4 and story 5.2 PR #556; story 5.1 conventions PR #53;
> story 5.3 — all six consumers pinned to conventions `release` 8355757; epic 6 OrgMeter `ai_conventions`
> PR #26 (94fe09a), all ten of its consumers pinned to it, the three that deploy on push merged on the
> operator's word and their deploys verified. **Deviations:** `consult`'s `kind`/`plan`/`epics` arguments and
> one sentence of its description landed in epic 2, not story 3.1, because the kinds cannot be tested end to
> end without them; the round's projection takes plan key, epic and note from the ROUND, not `RoundContext`;
> `segmentedRadio`/`help` moved to `panelControls.ts` and `repoNameOf` to `pathTail.ts` rather than copied;
> `WorktreeManager.ShaOrNoneAsync` and `PanelSettings.WorktreeRoot` are shared with `status`; two tests were
> corrected to assert their own rule — the tooltip census and the radio-name check; the conventions rule
> gained a Definition of Done on CodeRabbit's review, and the pointer in `coai-review-gate.md` names the
> rule's id rather than linking it; story 5.2 rode in epic 4's PR because the mount pin had to move with it;
> OrgMeter's `review-gate.md` says three things the dew_flow rule does not — the groups are numbered in threes
> from 1 (1–3, 4–6), `abandoned` counts as an outcome as `solved` and `not_solved` do, and a lapsed
> consultation can still be closed; the live check found that the sidebar line ran the branch on as the
> sentence's last word (`…due epic-1`) and it now reads `· branch epic-1`. **Open tail:** bringing those three
> clarifications into `dew_flow_conventions`' `common/coai-consultant.md`; the risk path (five epics or
> more) and a second group were exercised by the scenario tests, not by the live check; a Claude Code
> session whose tool list was cached before 0.38.0 still shows the old `consult`/`review_code` schemas until
> `/mcp` reconnects — the new arguments pass through regardless. Scope: the gate's orders and refusals
> (`src_mcp/core/Commands`, a new `src_mcp/core/Cadence`, `src_mcp/src/Server/PanelService.cs`,
> `src_mcp/src/Server/Consultation`), the plan-size heuristic (`PlanShape.cs`), the panel's settings
> and sidebar (`src_vs_code`), and — last — the consultant rule's move into BOTH conventions
> repositories (`dew_flow_conventions` and OrgMeter's `ai_conventions`).
>
> **Plan gate: round 1, 2026-09-25 — `good_enough`, 3 of 3 reviewers, 11 findings: 7 accepted and
> folded in below (each marked *gate round 1*), 4 rejected with reasons.** The gate's split order sized
> this plan `Huge` — "4-5 EPICS … never more" — and it keeps SIX, which is this plan's own symptom:
> the heuristic reads 21 steps and has no size above five; Epics 5 and 6 are different repositories
> in different organisations and do not merge honestly.
>
> Every decision below marked **(operator)** was taken by the operator on 2026-09-25 and is not open
> for the gate to re-litigate; findings against them are rejected with that reason. What is open is
> HOW each is built.
>
> Related docs: [PLAN_consultant.md](PLAN_consultant.md),
> [PLAN_a_finding_that_changes_everything_calls_the_consultant.md](PLAN_a_finding_that_changes_everything_calls_the_consultant.md),
> [PLAN_consultant_defaults_from_phase_0.md](../todo/PLAN_consultant_defaults_from_phase_0.md),
> [PLAN_the_split_is_sized_and_gated_once.md](PLAN_the_split_is_sized_and_gated_once.md),
> [module_server.md](module_server.md), [module_core.md](module_core.md),
> [module_extension.md](module_extension.md).

## The symptom

**Nobody calls the consultant.** Measured on 2026-09-25 across both data directories on this
machine (Windows `%LOCALAPPDATA%\coai-mcp\coai.db`, WSL `~/.local/share/coai-mcp/coai.db`):

| | gate sessions | rounds | `consult` calls |
|---|---|---|---|
| Windows, since 2026-09-05 | 425 | 872 | 15 — the last on 2026-09-18 |
| WSL, since 2026-09-07 | 176 | 390 | 3 — the last (a diagnostic probe) on 2026-09-22 |
| of which `email-service`, 14 epics over two plans | 20 | 51 | **0** |
| **Live check, `require`, 2026-09-26** — a 4-epic throwaway plan, released 0.38.0 | 1 | 2 run, 1 refused | **1** `cadence` (epics 1–3), closed `solved`; 0 `risk` (below 5 epics); 0 stood down |

Claude Code transcripts agree: from 2026-09-19 to 2026-09-25, 56 `review_plan` and 61 `review_code`
calls on Windows and **zero** `consult`. Of the ~12 first turns that did happen, about five were a
person saying "ask the consultant"; the ~7 the agent started on its own all fell on 17–18.09, while
the consultant itself was being built. And 13 of the 15 Windows consultations closed on the
15-minute idle; **not one** carries an `outcome`, so the phase-0 table
([PLAN_consultant_defaults_from_phase_0.md](../todo/PLAN_consultant_defaults_from_phase_0.md)) has no rows
because nothing ever wrote one.

**The live check (2026-09-26).** A four-epic plan in a throwaway repository, the released server 0.38.0,
the panel on `require`. The plan round, declared `epic: 1/4`, passed and carried the order *CONSULT BEFORE
YOU BUILD this group of epics. Epics 1-3* with the call written out. Epic 1 was built red-first, and its
code round was **refused** — *"epic 1 of todo/PLAN_wordcount.md cannot go through its code round yet: the
operator's cadence owes a consultation first … Nothing was reviewed."* — while `coai-mcp --cadence` read
both groups (1–3, 4) unconsulted. `consult` with `kind: cadence`, `epics: 1-3` was answered by codex
`gpt-6-astra`; its claims were verified against the code, two epics of the plan were sharpened with
explicit fixtures, and `close_consult` recorded `solved`. `--cadence` then read group 1–3 `consulted: true`
with NO round in between, the same code round was let through (`proceed`, 9 reviewers, 3 findings
accepted and fixed red-first, 12 rejected with reasons), and after `resolve` epic 1 read closed and group
4 still owed. The one thing a person noticed on the way was the sidebar line's wording, fixed in the same
promotion.

### Why — three causes, each verified

1. **Every trigger is reactive.** The six in `src_vs_code/src/consultantRule.md` fire when the agent
   RECOGNISES it is stuck (a test red twice, a contradiction, an unmeasurable fork, "still broken"
   twice, a finding that changes its mind, a person asking). An agent that is confidently wrong
   never recognises it — that is what being confidently wrong is.
2. **The rule does not reach the agent.** The consultant half is pasted nowhere on this machine
   (its marker `coai-consultant` occurs in no `CLAUDE.md`, `AGENTS.md`, `.claude/` or `.agents/` of any
   `dew_flow_*` repository) and `dew_flow_conventions/common/coai-review-gate.md` never says "consult".
   The tool DESCRIPTIONS carry the triggers (`src_mcp/src/Tools.cs:321-367`), but Claude Code defers
   MCP tool schemas: until something loads them the model knows `mcp__coai__consult` by name only.
3. **Prose in conventions is necessary and not sufficient.** OrgMeter's `ai_conventions` has carried
   five consult triggers in `common/review-gate.md` since 2026-09-16, mounted in nine repositories —
   `email-service` and `scoreMeter` among them. They produced the WSL row above.

The one channel that verifiably reaches every caller is the gate's own reply: the `commands` list
built by `GateCommands.For` (`src_mcp/core/Commands/GateCommands.cs:98`), which the server
instructions tell the caller outranks its defaults. And the one thing a caller cannot ignore is a
refusal.

### A second symptom found on the way: the size heuristic tops out at five epics

`PlanShape.Split` ends at `Huge` — "4-5 EPICS … never more" (`shared/commands/command-split-huge.md`).
The operator reports about ten cases where the caller disagreed and split into 9–15 epics, and was
right to. `email-service` is the measured one: `todo/PLAN_stage_foundation.md` (4 epics, 16 stories,
1452 lines) then `todo/PLAN_first_application_live.md` (10 epics, 50 stories, 1358 lines).

Neither signal the reader has can tell them apart — run through `PlanShapeReader`'s longest-run rule
(neither has a `Build order` heading), the **10-epic plan counts 5 steps and the 4-epic plan counts
31**. Length is backwards too. Both plans are written as `### Epic N` / `#### Story N.M` headings,
which the reader does not look at.

## What is decided (operator, 2026-09-25)

1. **Channel: a gate ORDER plus a server REFUSAL.** Prose alone has been measured not to work (cause 3).
2. **The caller declares where it is**: `review_plan` and `review_code` gain `plan` (the plan file's
   repo-relative path, e.g. `todo/PLAN_x.md`) and `epic` (`"k/N"`, e.g. `"5/14"`). The server does not
   count rounds: an `again: true` code round (`Tools.cs:136-142`, S3 #490, 2026-09-24) is as often a
   mid-epic checkpoint or a retry as a new epic.
3. **Cadence state is keyed by repository + plan, never by session.** With `gatePer = task` one plan
   gate carries several code rounds in one session; with `gatePer = epic` every epic is its own
   session on its own branch. Both must count into the same plan.
4. **One consultation per triple of epics** — 1–3, 4–6, 7–9, … so a plan of N epics owes `ceil(N/3)`.
   Two epics owe one; fourteen owe five. The consultation happens **BEFORE the triple is built**, and
   asks: *here is the plan and epics k..k+2 — is this right, where is it weak, what did it forget?*
5. **The server refuses the first `review_code` of any epic in a triple** that has no CLOSED cadence
   consultation for that triple.
6. **At N ≥ 5 the server ASKS whether any epics and/or stories are critical** — a critical story may
   sit inside an otherwise ordinary epic, and two may sit in two different epics. The CALLING AI
   answers (today that is Claude); the answer may be an empty list, with a reason. Every item owes its
   own consultation, before the code round of the epic it lives in.
7. **N > 14 is refused at once**: split the plan into two plans.
8. **A new plan size, `Massive`: 6–14 epics.** Over 14, the order says to split the PLAN.
9. **The numbers 3 and 5 are editable in the panel.**
10. **A cadence or risk consultation counts only once it is CLOSED WITH AN OUTCOME** (`close_consult`).
11. **Cadence consultations do not spend the stuck budget** (`ConsultCallsPerSession`).
12. **A consultant that cannot be had never deadlocks the work.** The refusal stands down, the round
    proceeds, and the server records *cadence not met: <reason>* and raises a notice.
13. **The order explains the call itself** — the fields, and what goes into `problem` (the plan path,
    the epic numbers of the triple) — because the caller has usually never loaded `consult`'s schema.
14. **`status` and the sidebar show the cadence**: *epics closed 4/14 · consultation for triple 4–6:
    due.*
15. **The consultant rule is SHARED** and moves into conventions — `dew_flow_conventions` and OrgMeter's
    `ai_conventions` — at the END of this work. This reverses the 2026-09-13 ruling recorded in
    [PLAN_consultant.md](PLAN_consultant.md) ("a rule about one tool of one server is one
    product's own"): `coai` gates every repository of the family, so a rule about its tools is as
    shared as the gate rule itself.

## Decisions taken in this plan (open to the gate)

| # | Decision | Why |
|---|---|---|
| D1 | The risk list is a PARAMETER (`riskItems`) accepted by `review_plan` and `review_code`, stored once per plan — not a new tool | In `gatePer = epic` there is no single plan-wide call to hang a tool on, and an eleventh tool is one more deferred schema the caller will not load. The order that asks the question names the parameter. |
| D2 | The kind is `risk`, not `critical`; the word "critical" appears in no tool description, order or rule text | `critical` is the canonical INVENTED severity: `ReviewParser` rejects it by name, and two guards forbid the word — `TheGateSaysWhenToConsultTests.cs:164` over every tool description, `snippetVersion.test.ts:268` over the consultant rule. The guards stay as they are. The PANEL may say "критично / critical" to the person; nothing a reviewer reads does. |
| D3 | An epic is CLOSED when its code stage moves to `Done` — on `proceed`, `good_enough` or `continue_anyway` | All three move the stage (`RoundMachine.Resolve`); counting only two would leave "4/14" permanently behind for an epic that ended on the policy. The verdict is recorded beside it. |
| D4 | Default mode is **`remind`** (order, no refusal); `require` is the operator's switch; `off` exists | The extension is on the Marketplace. A refusal by default would change every user's gate on update. The operator turns `require` on for this machine in the same change that ships it. |
| D5 | The budget exemption is bounded by structure: a SECOND cadence consultation for an already-satisfied triple, or a second risk consultation for a satisfied item, is refused | Unlimited calls to a paid vendor were the architect's first risk; this makes the ceiling `ceil(N/3) + |riskItems|` per plan. Follow-up TURNS of one consultation remain governed by the turn cap. |
| D6 | `riskItems` is capped (setting, default 3) | A caller listing every story as risky defeats the question and the budget. Editable with the other two numbers. |
| D7 | A store that cannot be READ fails CLOSED (refuses, names the file); a consultant that cannot be HAD fails OPEN with a notice (decision 12) | Opposite intents; they are two code paths and two tests, never one `catch`. |
| D8 | `Massive` is decided by DECLARED structure first: `### Epic N` headings when present, `Story N.M` headings second, the old steps/length rule only for a plan with neither | Measured above: steps and length both invert on the two real examples. A plan that already names its epics has answered the question. |
| D9 | `Huge` keeps "never more"; the escape valve is `Massive` plus the existing `command-split-measured.md` ("if it is wrong for this plan, say so … and do what is right") | Softening the wording brings back the reading #131 removed — the AI treating the ceiling as a suggestion. |
| D10 | The consultant rule becomes its own file `common/coai-consultant.md` in `dew_flow_conventions`, not a section of `coai-review-gate.md` | NOT because the gate rule is frozen — it is not since 2026-09-15 (`rule-bodies.json` + `--update`). Because the pasted artefact is composed of HALVES, each with its own marker and version (`claudeSnippet.ts` `KNOWN_HALVES`), and the gate half's marker `coai-snippet v5` is pinned. A fourth half keeps `CONSULTANT_VERSION` meaningful. |

## Design

### Core — `src_mcp/core/Cadence/` (pure, no I/O)

- `EpicRef.Parse(epic, plan)` → `None` | `Some(Number, Last, Plan)` | refusal: malformed (`"'5-14' does
  not read as k/N"`), one of `plan`/`epic` without the other, `k < 1 || k > N`. **k is the epic's own
  number as the plan writes it and N the plan's LAST epic number**, because a plan may continue another
  (email-service's second plan runs 5–14) — so the ceiling of decision 7 is NOT here: it is
  `CadenceRule.RefuseIfTooMany`, on the COUNT of epics in the file (*as built in epic 1*).
- **The declared N is checked against the plan, not trusted** (gate round 1, codex + local). The
  server reads the plan file at `plan` — at the reviewed sha (the epic-1-3 consultation, point 9) — and
  reads its `Epic N` headings with `PlanOutlineReader`, the reader `PlanShape` uses. Headings present:
  N must be the plan's last heading number and k one of its numbers, else refused naming both (*"todo/PLAN_x.md
  names epics 1–14; you declared 1/1"*). No headings → N is the caller's word, recorded as such in the round. A later
  call with a different N re-derives the triples; a consultation satisfies the triple whose RANGE it
  names, so 1–3 stays satisfied while 4–6 may move. Consultations are NOT bound to the plan's
  content hash: plans are edited all the time (status lines, deviations), and a fresh consultation per
  edit would make the cadence unpayable — the count check is what stops a caller shrinking the plan
  to dodge it.
- **The key is the plan's FILE NAME, not its folder** (gate round 1, gemini): `todo/PLAN_x.md` and
  `research/PLAN_x.md` are one plan, because promotion moves a finished plan and an unfinished tail
  may still be gated after it. Two different plans with one file name in one repository would share a
  record; the lifecycle convention (one `PLAN_<topic>.md` per topic) makes that a defect of its own.
- `CadenceRule` (*as built in epic 1*) — `GroupOf(epic, every, firstEpic)`: groups of `every` counted
  from the plan's OWN first epic (floor division, so an epic before it still falls in a group that holds
  it); `GroupsOwed(epicNumbers, every)`: one per group the plan's epics fall in, cut at its last
  (1–14 → 5; 5–14 → 5-7, 8-10, 11-13, 14); `AsksForRisk(count, threshold)`; `RefuseIfTooMany(count, plan)`.
- `CadenceState` (record, *as built*): `Plan`, `Closed` (`ClosedEpic(Number, Verdict, ClosedUtc)`,
  number-ordered, `WithClosed` idempotent, first verdict kept), `RiskItems` (`{ Epic, Story, Reason }`), `RiskAnsweredUtc` (empty = not yet
  asked-and-answered), `RiskNote` (the reason when the list is empty).
- `CadenceOrders.For(facts)` → which orders this reply carries: the forecast on the split round
  ("this plan owes N consultations: triples … plus risk items"), *consult before this triple* on the
  first epic of an unsatisfied triple, *name the risky epics and stories* at N ≥ threshold until
  answered. Consumed by `GateCommands.For` through a new `CommandContext.Cadence` field
  (`GateCommands.cs:27`), each order with its own marker constant and its own text id in
  `CommandTexts` / `shared/commands/command-cadence-*.md`, rewordable through the #467 page like every
  other order.

### Plan size — `src_mcp/core/Commands/PlanShape.cs`

- `Split.Massive` declared after `Huge` (`PlanShape.cs:14`; `Verdict` relies on declaration order).
- `PlanShape` gains `Epics` and `Stories`, read from `^#{2,4} Epic \d+` and `^#{2,5} Story \d+(\.\d+)?`
  headings; `Verdict` = by epics when `Epics > 0` (1 → `Small`, 2–3 → `Medium`, 4 → `Large`, 5 → `Huge`,
  6+ → `Massive`), else the existing `Math.Max(BySteps, ByLength)` (`:53`, `:62`) with no new top band.
- `shared/commands/command-split-massive.md`: *Split this plan into 6-14 EPICS, each of 3-5 logically
  complete STORIES. Fewer is fine when the work is smaller. If it needs more than 14, stop and split
  the PLAN into two plans, each gated on its own.* `JudgementId` (`GateCommands.cs:157`) maps it;
  `HasEpics` (`:166`) already answers `>=` and needs nothing.
- Recalibration run, recorded in the docstring the way #131's was: every `PLAN_*.md` in this
  repository plus the two `email-service` plans, before and after, with the count of verdicts that
  moved.

### Server — `src_mcp/src/Server/Cadence/`

- `CadenceStore` — one JSON file per plan, `<dataDir>/cadence/cadence-<sha256(repo#plan)[..16]>.json`,
  the key built exactly as `SessionStore`'s is; written under `SessionTurn` (`SessionStore.cs:251`,
  generic over the path) so several servers on one data directory do not tear it. Unreadable → throws;
  callers refuse (D7).
- `CadenceGate.Check(repo, epicRef, callerKind)` → `Satisfied` | `Blocked(sentence)` |
  `StoodDown(reason)`. Satisfied means a consultation record exists with `Kind` = `cadence`,
  `Plan` = this plan, `Epics` = this triple, `IsOver` and `ConsultationOutcomes.IsVerdict(Outcome)`
  (`ConsultationClosing.cs:45`) — which already excludes `lapsed`, so the 13 idle closes of today would
  not count, with no change to the closing code. Risk items the same, per item.
- `ConsultationService.Preflight(callerKind)` — the three availability checks `AskAsync` makes today
  (switched off, consultants unreadable, `ConsultantResolver.Resolve` → unavailable) extracted into one
  method both use. Unavailable → `StoodDown`: the round proceeds, a `ServerNoticeCodes.CadenceStoodDown`
  notice with subject `cadence:4-6` / `risk:7/7.2` is raised (the extension already groups by code +
  subject), and the round's record says *cadence not met: <reason>*. Extracted FIRST, with a test that
  the three existing refusal sentences survive byte for byte.

### Wiring — `PanelService` and `Tools.cs`

- `PersistedSession` gains `Plan` and `Epic` beside `PlanText` (`SessionStore.cs:177`), carried the
  way `planText` is: an empty argument keeps what the session already holds.
- `ReviewCodeAsync`: after the substance check (`PanelService.cs:543`) and before any worktree or
  launcher, parse `epic`; refuse a plan of more than 14 epics (`RefuseIfTooMany`, on the count); on the
  FIRST code round of ANY epic of an unconsulted group (or one carrying a risk item) in `require` mode,
  ask `CadenceGate`; `Blocked` → the refusal; `StoodDown` → proceed and record.
- **Leaving `epic` out is not a way round it** (gate round 1, gemini). In `require` mode a `review_code`
  with no `epic` — none passed and none on the session — is refused when the session's plan text
  names two or more `Epic N` headings, or when this session was given a split order with epics
  (`GateCommands.ShapeOrdered` is recorded on the round). A plan with no epics owes no cadence and is
  never asked for one.
- `RunStageAsync`: build `CommandContext.Cadence` beside the existing context (`PanelService.cs:1434`)
  from `CadenceState` plus the settings, so `review_plan`, `review_code` and `resolve` replies carry
  the orders.
- `Finish()` (`PanelService.cs:2764`): on `CodeReview → Done` with a parsed `epic`, record the epic
  closed (D3). A write failure returns an error rather than a silently unclosed epic.
- `consult` gains `kind` (`stuck` default | `cadence` | `risk`), `plan`, `epics` (`"4-6"` or `"7"` /
  `"7/7.2"`); `kind != stuck` requires both. `PromptId` (`ConsultationService.cs:34`) becomes a
  function of kind; `src_mcp/src/consultant/consult-cadence.md` and `consult-risk.md` join `consult.md`
  as embedded resources (`src_mcp/src/CoaiMcp.csproj:52` lists them one by one). The budget line
  (`ConsultationService.cs:410`) takes the counter only for `stuck` (decision 11) and D5 refuses the
  duplicate.
- `status` gains an optional `plan` and answers a `cadence` block (closed/total, each triple's state,
  risk items outstanding) — readable for a plan from any branch, which is what the sidebar needs.
- Tool descriptions: `review_plan`, `review_code`, `consult`, `status` each get ONE short paragraph,
  opening *"When the operator has switched the cadence on…"*, so a caller in a repository without it
  is not confused. The header comment at `Tools.cs:7` still says nine tools; it becomes ten.

### Refusal texts (in `CadenceRefusals`, the `RoundRefusals` style: the problem, then the cure)

- **Too many epics:** *epic '15/15' declares a plan of 15 epics; a plan is gated a triple at a time up
  to 14. Split `todo/PLAN_x.md` into two plans — by phase or by area — each with its own count, and
  call again with the plan and epic you are building. Nothing was reviewed.*
- **Triple not consulted:** *epic 4/14 opens triple 4–6 of `todo/PLAN_x.md`, and no closed cadence
  consultation exists for it. Call `mcp__coai__consult` with `kind: "cadence"`, `plan:
  "todo/PLAN_x.md"`, `epics: "4-6"`, and a `problem` that names the plan file and asks whether epics
  4–6 are right, where they are weak and what they forget. Verify the answer, then `close_consult` with
  an outcome — only a CLOSED consultation with an outcome counts. Nothing was reviewed.*
- **Risk item not consulted**, **record unreadable**, **malformed epic**, **plan without epic**,
  **declared N differs from the plan's headings**, **epics in the plan but no `epic` declared** — same
  shape.

Every order and refusal that asks for a consultation carries the call **literally** (decision 13,
sharpened by gate round 1, gemini): the line to load the schema first —
`ToolSearch select:mcp__coai__consult,mcp__coai__close_consult` in Claude Code — then the call with
every argument filled in except the one sentence only the caller can write:
`mcp__coai__consult({"repoPath": "<this checkout>", "kind": "cadence", "plan": "todo/PLAN_x.md",
"epics": "4-6", "problem": "Plan todo/PLAN_x.md, epics 4-6 (<their titles, read from the plan>): are
they right, where are they weak, what did they forget? <what you already doubt>"})`, and the
`close_consult` that must follow with its outcome values. The titles are filled by the server from the
headings it already read.

### Database — `src_mcp/src/Store/Schema.cs`

One step appended to `Steps` (`Schema.cs:31`): `rounds.plan TEXT DEFAULT ''`, `rounds.epic TEXT
DEFAULT ''`, `rounds.cadence TEXT DEFAULT ''` (*met* / *stood down: reason* / empty);
`consultations.kind TEXT DEFAULT 'stuck'`, `.plan`, `.epics`; table `risk_items (repo_path, plan, epic,
story, reason, declared_utc, UNIQUE(repo_path, plan, epic, story))`. A projection, as everything in
that database is; the file stays the truth. The log page shows the consultation's kind.

**Size, kept forever — stated rather than left unbounded** (gate round 1, codex). A cadence record is
one JSON file per plan, under 2 KB even at 14 epics and 3 risk items, and only a plan that declares
epics gets one — a thousand such plans would be under 2 MB. Cadence and risk consultations add at most `ceil(14/3) + 3 = 8` rows per plan to a
table that already keeps every consultation. Neither is retired: both are the evidence the phase-0
table and this plan's own DoD read, and they are small enough that a retention rule would cost more
than the bytes.

### Extension — `src_vs_code`

- `settingsShape.ts` beside `gatePer` (`:165`, `:326`, `:349`, `:447`): `cadenceMode`
  (`off | remind | require`, default `remind`), `cadenceEvery` (3), `cadenceRiskThreshold` (5),
  `cadenceRiskMax` (3) → `COAI_CADENCE_*` in `PanelSettings.cs` beside `GatePer` (`:368`, `:768`),
  unrecognised values named the way `GateScopeOf` names them. The TS/C# defaults agreement test covers
  them.
- The panel's settings section, next to *Split the plan into epics and stories* (`panelView.ts:1212`).
- The session card reads the new `status` block: *epics closed 4/14 · consultation for triple 4–6:
  due* — no new polling.

### Conventions — the last epic

**`dew_flow_conventions`:** new `common/coai-consultant.md`, body starting `<!-- coai-consultant v3 -->`
then its heading; the six v2 triggers verbatim, a SEVENTH (*every triple of epics, before you build
it; at five or more, name the risky epics and stories*), the `plan`/`epic`/`kind` fields and the
mandatory outcome. Its own `owns:` lines (`coai`, `ConnectOtherAIs`, `mcp__coai__consult`), a fourth
entry in `tools/canonical-markers.test.mjs`, `node tools/rule-bodies.mjs --update common.coai-consultant`,
and one pointer line added to `coai-review-gate.md` (its `rule-bodies.json` entry updated in the same
commit). PR → green → `promote-release` → the cascade.

**In this repository**, after the bump of `.agents/conventions`: `prepare-gate.mjs` reads the consultant
half from the mount (`CONSULTANT_SOURCE`, `:43`) through the same `ruleBody` path as the other three;
`src_vs_code/src/consultantRule.md` is deleted; `CONSULTANT_VERSION` 2 → 3 (`claudeSnippet.ts:157`),
`ARTEFACT_VERSION` 11 → 12 (`:66`), the `copyClaudeSnippet` title in `package.json`; the tests that
read the local file read the mount (`snippetVersion.test.ts`, `prepareGate.test.mjs`); the docblock of
`TheGateSaysWhenToConsultTests.cs` stops saying the half "cannot be pasted into a family repository at
all" — it now travels like the others, and the tool descriptions remain the channel that needs no
mount.

**The five other consumers**, in the README's order: `dew_flow_mcp`, `dew_flow_benchmark`,
`dew_flow_sidecar_rust`, `dew_flow_creds_for_devs`, `dew_flow_rag_qln` last (it pins two of the
others; its code pins go stale and its build is run). A worktree per consumer off `origin/main`,
paths added explicitly.

**OrgMeter `ai_conventions`:** `common/review-gate.md` § *When you are stuck, ask another vendor* is
brought level — the sixth trigger, the cadence, the fields, the outcome — in place (no body hash
there; `tools/conventions-check.mjs` checks links and bookkeeping). Its consumers (the nine its own
2026-09-16 plan names, plus `email-service`) are bumped per its `common/shared-rules-pin.md`; **three
of them deploy on a push to their default branch** —
`scoreMeter`, `orchestrator` and `llm-jira-estimates` per its own 2026-09-16 plan — so they go last,
separately, each through its own PR, never merged by this work without the operator's word.

## Cadence consultation for epics 1–3 (2026-09-25, codex `gpt-6-astra`, by hand)

This plan's own rule, applied to itself before the feature exists. Every point below was checked
against the code before it was taken, and all of them were; they override the design above where
they disagree.

1. **The check runs under the session claim.** `SessionClaim` is taken before the session is read
   (`PanelService.cs:1254`); the cadence check belongs in `review_code`'s `RefuseBeforeBuilding`
   (`:671`, called at `:1313` with the loaded session and the resolved sha), not ahead of the claim.
2. **Each ROUND carries its epic identity**, stamped before the live round is first persisted:
   `RoundRecord` gains the plan key and the epic NUMBER. "First code round of this epic" is *no earlier
   `CodeReview` round on this session with the same plan key and k* — `interrupted` rounds included
   (they passed admission); k compared, never the literal `k/N`, so a changed N does not make a
   started epic new. Legacy rounds carry no identity and never match, so the first identity-carrying
   round of an old session is checked once. This is what makes `gatePer = task` right: one session,
   many epics.
3. **The refusal covers ANY epic of an unsatisfied triple**, not only its first number — a first
   call of `2/6` with no consultation is refused. (The design above said "an epic that opens a
   triple"; that was wrong.)
4. **"Epics closed" is "epics through the code gate".** `CodeReview → Done` also happens for a
   passed CHECKPOINT, so the counter says what it measures, in the panel as well.
5. **Closing is reconciled, not written once.** `Finish` saves the session before anything else
   (`:2765`), so a failed cadence write cannot un-move the stage. The epic-closed record is idempotent
   and reconciled from the session's own rounds on the next `review_code` and on `status`.
6. **The store holds its lock across read–modify–write**, and refuses (fail closed) when the turn is
   not held after its retries; a write-only lock loses one of two simultaneous closes.
7. **The preflight covers every refusal the vendor path makes**, including the runtime resolution and
   the missing answer schema (`ConsultationService.cs:389-401`) — otherwise `require` could demand a
   consultation that cannot be had.
8. **Repository identity is `git rev-parse --git-common-dir`, not the checkout path.** This plan is
   being built in a worktree whose path differs from the main checkout's while both resolve to
   `D:/rsd/dew_flow_connect_other_ais/.git`. The cadence key is common-dir + plan file name; the
   consultant still runs in the actual checkout.
9. **The plan is read at the resolved sha** (`git show <sha>:<plan>`), the revision under review,
   never the working file of whatever branch the checkout holds. A plan not in that commit → the
   caller's N, recorded as such.

Tests added by it: `AnAgainRoundForTheSameEpic_IsNotAskedAgain_ButOneForEpicFour_Is`,
`TheSameAfterAServerKill`, `AFirstCallOfEpicTwo_IsRefusedWithoutTheTriplesConsultation`,
`AFailedCadenceWrite_IsReconciledOnTheNextCall`, `TwoSessionsClosingTogether_BothEpicsLand`,
`AMissingAnswerSchema_StandsTheCadenceDown`, `TwoWorktreesOfOneRepository_ShareOneRecord`,
`ThePlanIsCountedAtTheReviewedSha_NotTheCheckout`.

## Cadence consultation for epics 4–6 (2026-09-25, codex `gpt-6-astra`, by hand)

The second group's consultation, before epic 4. Each point was checked against the code before it was
taken; they override the build order below where they disagree.

1. **The sidebar never calls `status`.** `panelProvider.readSessions` reads `<dataDir>/sessions/*.json`
   through `rounds.parseSession`; the `cadence` block epic 3 put on `status` cannot reach the card that way.
   Story 4.2 therefore gains a **one-shot CLI mode, `coai-mcp --cadence <repo> <plan>`**, answering the same
   `CadenceAnswer` JSON (the sanctioned one-shot pattern `--log` and `--close-consult` already follow), read
   by the extension on its refresh. A consultation closed WITHOUT another review round must still flip the
   line — asserted.
2. **The settings agreement test does not cover the cadence yet.** `panelServerDefaultsAgreement.test.ts`
   checks the role and consultant defaults only, and the C# numbers are `CadenceRule` constants rather than
   literals its regex reads. Story 4.1 extends it, `settingsReach.test.ts` and `settingsAreDeclared.test.ts`,
   and declares the four settings in `package.json` (without which VS Code cannot persist them).
3. **The log drops a consultation's `kind`.** `roundsDb.ts` rebuilds each consultation from named fields;
   `kind` joins `DbConsultation` and that projection, asserted through `parseLog` itself, not a page fixture.
4. **Epic 5's safe order**: the conventions release first; then, in ONE commit here, the gitlink bump, the
   generator's source switch (`consultantBody` stripping the new file's frontmatter), the deletion of
   `consultantRule.md`, the version and hash updates and the re-pointed tests; the other consumers after.
   Another consumer's older mount cannot break this extension's build — `prepare-gate.mjs` reads under its own
   repository root — but new generator code beside an old local pin would.
5. **Epic 5 forgot discovery.** `readSnippetStatus` finds the first file carrying the gate marker and reads
   halves from that file alone, so a mount carrying all four rules and no paste would still be reported as
   missing the consultant half. Story 5.2 aggregates the mounted siblings in the mount fallback, keeping an
   actual (stale) paste's precedence — with a fixture of four mounted rules and no paste.
6. **The risky pieces** (the operator's rule at five or more epics): **story 4.2** (its assumed data path did
   not exist — point 1), then **story 5.2** (generation and discovery together). Story 6.3 carries the
   largest deployment consequence and is gated on the operator's word already.

## Risk consultation for story 4.2 (2026-09-25, codex `gpt-6-astra`, by hand)

The operator's rule gives each piece named as risky its own consultation; story 4.2 was named, and this was
it. Checked against the code, taken:

1. **`--cadence` builds no `PanelService`.** Its constructor sweeps rounds and consultations, reprojects
   into SQLite and sweeps orphan processes (`PanelService.cs:105-125`) — a sidebar that probed it every few
   seconds would run that maintenance every time. The mode builds the READER only: the session store, the
   cadence store, the consultation evidence. A `CadenceDesk` factory for reading does exactly that.
2. **An unreadable cadence record must not read as zero.** `CadenceDesk.AnswerAsync` dropped the record's
   `Readable` flag and answered empty state — a believable "0 epics through the gate". The answer carries the
   reason (`unreadable`) and the line says it.
3. **Probes are bounded**: only sessions touched in the last day and holding a plan, one probe at a time, a
   30 s TTL, never awaited by the render, the last answer kept on a torn session read. The consultation
   watcher's change resets the cache for the common case; the watcher compares LIVE consultations only, so an
   outcome recorded on a lapsed one is picked up by the TTL instead — asserted with an injected clock.
4. **The extension does not compute the line itself**: the cadence record holds neither the plan's epics nor
   the grouping, and the status answer also needs the plan at a commit, the repository identity and the
   consultation evidence. One computation, in C#.

## Risk consultation for story 5.2 (2026-09-25, codex `gpt-6-astra`, by hand)

Story 5.2 was the second piece named as risky (generation and discovery together), and this was its
consultation. Every point was checked against the code before it was taken:

1. **The malformed-source test will fail for the wrong reason.** `prepareGate.test.mjs:87` corrupts the
   consultant source and expects a `coai-consultant` error. Once the source is in the mount, the resolver
   at `prepare-gate.mjs:144` runs FIRST and rejects the now-dirty mount with "uncommitted changes". So the
   malformed-text case moves to `consultantBody` directly, and a clean, correctly pinned fixture that
   LACKS the consultant file is its own case.
2. **The menu title carries the version.** `package.json:221` reads `Copy the CLAUDE.md snippet (v11)`, and
   `snippetVersionIsVisible.test.ts:30` checks it separately from the constants: the bump to v12 changes
   the title in the same commit.
3. **The byte-identity test joins THREE mounted bodies** (`snippetVersion.test.ts:180`); the consultant rule
   is the fourth. The composite hash is computed from the final four bodies, including 5.1's pointer
   change in `coai-review-gate.md`.
4. **Packaging reads the mount at BUILD time only.** `prebundle` regenerates and esbuild embeds the
   constants; the VSIX excludes `src/**`. An installed extension needs no mount to copy the snippet. CI
   initialises the committed pin (`ci.yml:305`), so a new generator beside an old pin fails closed — the
   gitlink and the generator change go in one commit, as point 4 of the epics 4–6 consultation said.
5. **`prepare-gate.mjs --check` is not a check**: its entry point generates whatever the arguments
   (`prepare-gate.mjs:184`). Nothing here depends on it; noted so nobody reads it as read-only.
6. **Discovery**: the aggregation in point 5 of the epics 4–6 consultation is right, with three fixtures —
   four mounted rules and no paste → `current`; the same plus a CLAUDE paste with consultant v2 → `older`,
   behind `coai-consultant`; a paste with no consultant half → still `older`. The existing fixtures put the
   whole snippet in the gate file (`snippetDiscovery.test.ts:31`), which no real mount does. A missing half
   is never filled from ANOTHER mount.
7. **No other reader** of `consultantRule.md` / `CONSULTANT_RULE` / `CONSULTANT_SOURCE` exists. The
   ownership premise does survive in prose: `research/module_extension.md:3443`, which 5.2 rewrites.

## Build order

Six epics, so this plan is its own first customer: it owes two cadence consultations (epics 1–3,
4–6) and, at six, the risk question. Gate once per epic, per the standing order.

### Epic 1 — The arithmetic and the size (core, pure)

1. **Story 1.1 — `EpicRef`.** RED: `EpicRefTests` — `FifteenEpics_IsRefusedWithSplitThePlan`,
   `AMalformedEpic_IsRefusedNamingTheShape`, `APlanWithoutAnEpic_IsRefused`, `EpicZeroOrPastTheTotal_IsRefused`.
2. **Story 1.2 — `CadenceRule` and `CadenceState`.** RED: `TwoEpics_OweOneConsultation`,
   `FourteenEpics_OweFive`, `EpicSeven_OpensTripleSevenToNine`, `EveryFour_MovesTheTriples`,
   `FourEpics_AskNothingAboutRisk_FiveDo`, `ARecordWithoutRiskItems_ReadsAsAnEmptyList`.
3. **Story 1.3 — `CadenceOrders` through `GateCommands`.** RED in `GateCommandsTests`:
   `TheSplitRound_ForecastsTheConsultationsOwed`, `TheFirstEpicOfAnUnsatisfiedTriple_OrdersTheConsultation`,
   `AMiddleEpic_OrdersNothing`, `AtFiveEpics_TheRiskQuestionIsAskedUntilAnswered`,
   `TheConsultOrder_NamesEveryFieldTheCallNeeds` (kind, plan, epics, problem — decision 13),
   `NoCadenceOrder_EverSaysCritical` (D2).
4. **Story 1.4 — `Massive`.** RED: `APlanWithTenEpicHeadings_IsMassive_WhateverItsSteps`,
   `APlanWithFourEpicHeadingsAndThirtyOneSteps_IsLarge`, `APlanWithNoHeadings_IsStillSizedByStepsAndLength`
   (the #131 table unchanged), `TheMassiveOrder_SaysSplitThePlanPastFourteen`. Then the recalibration
   run and its table in the docstring.

### Epic 2 — The record and the gate (server, I/O)

5. **Story 2.1 — `ConsultationService.Preflight`**, extracted first. RED: `ThePreflight_ReportsEachOfTheThreeReasons`,
   `TheThreeExistingRefusals_AreByteIdentical`.
6. **Story 2.2 — `CadenceStore`.** RED: `TwoServersWritingOnePlan_BothLand`,
   `AnUnreadableRecord_FailsClosed_NamingTheFile`, `ThePlanKey_IsTheSameFromEveryBranch`.
7. **Story 2.3 — `consult` kinds.** RED: `ACadenceConsultation_SpendsNoStuckBudget`,
   `AStuckConsultation_StillDoes`, `ACadenceKindWithoutPlanOrEpics_IsRefused`,
   `ASecondCadenceConsultationForASatisfiedTriple_IsRefused` (D5), `EachKind_UsesItsOwnPrompt`.
8. **Story 2.4 — `CadenceGate`.** RED: `ALapsedConsultation_DoesNotSatisfyTheTriple`,
   `AClosedConsultationWithAnOutcome_Does`, `AConsultationForAnotherPlan_DoesNot`,
   `AnUnavailableConsultant_StandsDown_AndRaisesTheNotice`.

### Epic 3 — The gate enforces it (`PanelService`, `Tools.cs`, schema)

9. **Story 3.1 — parameters and persistence.** `plan`, `epic`, `riskItems` on `review_plan` /
   `review_code`; `kind`, `plan`, `epics` on `consult`; `plan` on `status`. RED:
   `AnEmptyPlanArgument_KeepsTheSessionsPlan`, `RiskItemsBeyondTheCap_AreRefused`,
   `AnEmptyRiskListWithoutAReason_IsRefused`, `ADeclaredCountThatDisagreesWithThePlansHeadings_IsRefused`,
   `APlanWithoutHeadings_TakesTheCallersCount_AndSaysSo`, `APromotedPlan_KeepsItsCadenceRecord`.
10. **Story 3.2 — the refusal.** RED: `RequireMode_RefusesTheFirstCodeRoundOfAnUnconsultedTriple`,
    `RemindMode_OrdersButNeverRefuses`, `OffMode_SaysNothing`, `ACheckpointAgainRound_IsNotAskedTwice`,
    `ARiskItemsEpic_IsRefusedUntilItsOwnConsultationCloses`,
    `RequireMode_RefusesACodeRoundThatLeavesOutTheEpicOfAPlanWithEpics`,
    `APlanWithNoEpics_IsNeverAskedForOne`, `TheOrder_CarriesTheLiteralCallWithTheTriplesTitles`.
11. **Story 3.3 — closing an epic.** RED: `ProceedGoodEnoughAndContinueAnyway_AllCloseTheEpic` (D3),
    `CallHuman_DoesNot`, `AFailedCadenceWrite_IsAnErrorNotASilentPass`.
12. **Story 3.4 — schema and `status`.** RED: `ADatabaseAtTheLastStep_GainsTheCadenceColumns`,
    `Status_ForAPlan_CountsEpicsClosedOnEveryBranch`.

### Epic 4 — The person sees it (extension)

13. **Story 4.1 — the four settings**, defaults agreed TS ↔ C#. RED: the defaults-agreement test
    extended; `AnUnknownCadenceMode_IsNamedNotIgnored`.
14. **Story 4.2 — the sidebar line and the log's kind column.** RED: run-the-page tests (never
    source-text asserts — `.agents/PROJECT.md`): `TheCard_SaysEpicsClosedAndTheTripleDue`,
    `TheLog_ShowsAConsultationsKind`.
15. **Story 4.3 — docs**: `research/module_server.md`, `module_core.md`, `module_extension.md`, the
    README's consultant section, `CHANGELOG`.

### Epic 5 — The rule moves to `dew_flow_conventions`

16. **Story 5.1 — `common/coai-consultant.md`** with the canonical-marker entry, `owns:` lines,
    `rule-bodies.json`, the pointer from `coai-review-gate.md`. PR, merge, `promote-release`.
17. **Story 5.2 — this repository takes the half from the mount**; `consultantRule.md` deleted; the
    version constants; the tests re-pointed. RED: `snippetVersion.test.ts` asserts the seventh trigger
    and reads the MOUNTED file; `prepareGate.test.mjs` fails when the mount lacks the consultant rule.
18. **Story 5.3 — the five other consumers**, one PR each, `rag_qln` last with its build run.

### Epic 6 — OrgMeter `ai_conventions`

19. **Story 6.1 — `common/review-gate.md` brought level** (read the live file first — its current text
    is quoted from a WSL read on 2026-09-25, not from a review). `conventions-check` green; PR.
20. **Story 6.2 — the consumers that do not deploy on push.**
21. **Story 6.3 — the three that do** — PRs opened, merged only on the operator's word.

## Test plan

- Every story above opens RED, the failure message naming the real symptom, then GREEN; every bug
  found on the way gets its own red-green-red.
- Full suites before each epic's code round: `dotnet build` + the `CoaiMcp.Tests` and `CoaiMcp.Core.Tests`
  executables, `npm test` in `src_vs_code`, `generate-command-texts.mjs --check`, `prepare-gate` check;
  in conventions `npm test` and `node tools/rule-bodies.mjs`.
- **Scenario harness** (gate round 1, codex) — the person-visible flows are end-to-end paths, and
  unit tests plus page tests can all pass while one is broken. Two new flows in the scenario harness,
  recorded in `research/module_tests.md` with what they cover and what they cannot:
  *require → refused → consult(cadence) → close_consult with an outcome → review_code passes*, and
  *require → consultant switched off → the round proceeds, the notice is raised, the round records
  "cadence not met"*. Both run against a real server build, not a fake of `CadenceGate`.
- **The live check the symptom deserves:** after Epic 3 ships, switch this machine to `require`, run
  one real multi-epic plan, and add a row to the table in *The symptom* — consultations per plan, how
  many closed with an outcome, how many stood down and why. That table is the Definition of Done's
  first line, not a nice-to-have.

## Boundaries with other plans

| Item | Owner |
|---|---|
| the turn cap, the stuck budget, which vendor answers which caller | [PLAN_consultant_defaults_from_phase_0.md](../todo/PLAN_consultant_defaults_from_phase_0.md) — unchanged. This plan FEEDS it: every cadence and risk consultation arrives with an outcome |
| whether the SERVER ever calls a consultant by itself (`consult_missed`, phase 2) | still that plan. Nothing here fires a consultation; it orders the CALLER and refuses the round, which the caller can answer or have stood down |
| the six reactive triggers | shipped ([PLAN_a_finding_that_changes_everything_calls_the_consultant.md](PLAN_a_finding_that_changes_everything_calls_the_consultant.md)); moved, not changed, by Epic 5 |
| the split sizes Small–Huge and the gate cadence per epic or per task | [PLAN_the_split_is_sized_and_gated_once.md](PLAN_the_split_is_sized_and_gated_once.md); this plan adds `Massive` and a structural reading ahead of its rule, and leaves its table as it is for plans without headings |

## Risks

- **Two epics of one triple on two branches at once** can both pass the gate before either closes —
  harmless, because the gate asks about the triple's CONSULTATION, not its neighbours; only the
  "closed x/N" counter may lag for a moment.
- **The descriptions grow.** One paragraph each, gated on the switch in its first words; anything
  longer belongs in the order, which only callers with the switch on receive.
- **The reversal of 2026-09-13.** `KNOWN_HALVES`, the mount test and three docblocks were written on the
  premise that the consultant half is this product's own; Epic 5 changes all of them in one PR so no
  window exists where the snippet is built from a file that is gone.
- **OrgMeter is another organisation.** Its merge rules and deploy-on-push consumers are not ours to
  shortcut; Epic 6 ends in open PRs if the operator does not merge them.

## Definition of Done

- [x] A real plan of ≥ 4 epics ran under `require` on this machine, and *The symptom* carries its row:
      cadence and risk consultations, each closed with an outcome, or stood down with a reason.
- [x] `review_code` refuses the first code round of an unconsulted triple in `require`, orders in
      `remind`, is silent in `off` — each pinned by a test watched red.
- [x] A plan of more than 14 epics is refused; `Massive` sizes both `email-service` plans correctly; the recalibration table
      is in `PlanShape.cs`'s docstring.
- [x] Cadence consultations spend no stuck budget, cannot be duplicated, count only with an outcome.
- [x] An unavailable consultant never blocks a round, and says so in the round and in a notice.
- [x] `status` and the sidebar show *epics closed k/N* and the next triple's state.
- [x] `common/coai-consultant.md` is on `dew_flow_conventions`' `release`, all six consumers pinned to it,
      `consultantRule.md` gone from this repository, `ARTEFACT_VERSION` 12.
- [x] OrgMeter `review-gate.md` level with it; its PRs merged or waiting on the operator, stated which.
- [x] The two scenario-harness flows run green and are listed in `research/module_tests.md`.
- [x] Module docs, README, CHANGELOG updated; this plan promoted to `research/` with `IMPLEMENTED <date>`
      and its deviations; `todo/README.md` updated both ways.
