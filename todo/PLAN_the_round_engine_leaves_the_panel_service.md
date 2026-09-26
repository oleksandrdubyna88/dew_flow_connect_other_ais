# PLAN — the round engine leaves the panel service

> Status: **steps 1–4 of 7 landed 2026-09-25 (records, prompt, roster, engine — the feature review's
> prerequisite), as ONE pull request of four proved commits; steps 5–7 (sweeps, resolve path, document
> stage) are open.** See §7 for what shipped differently. Scope: `src_mcp/src/Server/PanelService.cs`
> and new files beside it; `shared/refusal-sites.json`; tests that call the moved members. No behaviour
> change anywhere.
>
> Related docs: [PLAN_feature_review.md](PLAN_feature_review.md) (D16 — why this goes first),
> [module_server.md](../research/module_server.md),
> [PLAN_the_panel_provider_is_too_big.md](PLAN_the_panel_provider_is_too_big.md) (the same method, on
> the extension's largest file).

## 1. The problem

`src_mcp/src/Server/PanelService.cs` is **3 056 lines** (`origin/main` 21aba62e) against the 800 the
coding rule allows, and it is one `public sealed partial class PanelService` (`:24`) that is at once
the MCP surface's facade (open, review_*, resolve, status, ask_human, consult), the **round engine**,
and a set of sweeps and helpers. The round engine alone is roughly 1 300 lines:

| Member | Line | What it is |
|---|---|---|
| `RunStageAsync` | `:1237` | the one engine every stage runs through — claim, begin, deadline, assemble, schedule, dedup, gate, verdict, save, project |
| `BuildWork` | `:1913` | the roster: eligible vendors × roles → `ReviewerWork` (with `Items`, `Lens`, `Assemble`, `Everyone`, `Hand`, `CanCarry`, `WhyNotCarried`, `Carriers`, `:2136-2322`) |
| `RolesWithRulesInMind`, `RolesNotAsked` | `:2324`, `:2339` | roster helpers, `internal static`, called by tests |
| `WhatEndedIt`, `ConfiguredReviewers`, `RoundDeadlineFor` | `:2370-2418` | the round deadline |
| `MayProceed`, `CallerFor`, command texts, `CommandStageOf` | `:2419-2496` | the orders a round carries |
| `ComposePrompt`, `WithoutTheStaleClaim`, `WhatYouHave` | `:2498-2564` | the reviewer's prompt |
| `AnswerFor` | `:2566` | the verdict → wire answer |
| `NotifyIfAPersonMustDecide`, `RulesSection`, `ChoiceFor`, `WhereTheDocumentWent` | `:1789-1912` | round-time helpers |
| `RoundWork`, `ExcludedRole`, `StageRun` | `:2958`, `:2993`, `:3013` | the engine's records |

The feature review (a fourth stage) adds hooks to exactly these members — `StageRun` fields, a skip
branch in `RunStageAsync`, a three-valued `ReaderMaterial` in `ComposePrompt`/`WhatYouHave`, a
continuation in `BuildWork`. Landing them in the 3 056-line file makes it bigger; moving the engine
AFTER them means moving freshly written, freshly reviewed code. So the move goes first.

## 2. The shape

- `src/Server/Rounds/RoundEngine.cs` — `internal sealed class RoundEngine`, owning `RunStageAsync` and
  the deadline, the orders and the answer. Its dependencies are passed in its constructor (settings,
  store, claim, scheduler, projection, notifier, logger) — **each moved member takes its fields with
  it**, and a member that still reaches back into `PanelService` has not found its seam yet (the
  method `PLAN_the_panel_provider_is_too_big.md` settled for the extension).
- `src/Server/Rounds/RosterBuilder.cs` — `BuildWork` and its roster helpers.
- `src/Server/Rounds/ReviewerPrompt.cs` — `ComposePrompt`, `WithoutTheStaleClaim`, `WhatYouHave`
  (static where they already are).
- `src/Server/Rounds/StageRun.cs`, `RoundWork.cs` — the records, moved verbatim.
- `PanelService` keeps the MCP-facing methods and calls `_engine.RunStageAsync(...)`; the `partial`
  stays only if a `[GeneratedRegex]` remains in it.

Target: **`PanelService.cs` at or under 800 lines** — the coding rule's ceiling — and every new file
under 800. The engine alone does not get there (it leaves ~1 800), so this plan has a second phase
(§3, steps 5–7) that moves the rest of what the class is not: the document stage's entry points, the
resolve path and the sweeps. (Plan round, 2026-09-25: a plan that set 1 800 as its target was
planning a file the rule forbids.)

- `src/Server/Stages/DocumentStage.cs` — `ReviewDocumentAsync` … `DocumentContext` (`:724-1011`).
- `src/Server/Rounds/ResolvePath.cs` — `ResolveAsync`, `ResolveUnderClaim`, `WithHumanDecision`,
  `Finish`, `WhatTheCallerWasDoing` (`:2610-2786`).
- `src/Server/Sweeps.cs` — the scratch and answer-dir sweeps (`:1626-1744`).

## 3. Build order — one extraction per pull request

1. **Records** (`StageRun`, `RoundWork`, `ExcludedRole`) to their own files. Zero logic. — **done
   2026-09-25.**
2. **Prompt** (`ReviewerPrompt`) — static members, the lowest cost of being wrong; proves the method.
   — **done 2026-09-25.**
3. **Roster** (`RosterBuilder`) — `BuildWork` and helpers; the `internal static` test entry points
   keep their names (tests call them) — re-pointed, not rewritten. — **done 2026-09-25.**
4. **Engine** (`RoundEngine`) — `RunStageAsync`, deadline, orders, `AnswerFor`,
   `NotifyIfAPersonMustDecide`. — **done 2026-09-25** (the orders as `RoundCommands`, §7 D2).
5. **Sweeps** (`Sweeps`) — static, no state; low risk.
6. **Resolve path** (`ResolvePath`).
7. **Document stage** (`DocumentStage`) — last, because it is the largest behaviour surface.

Steps 1–4 are the feature review's prerequisite (D16); 5–7 may run beside that plan's Epic 2, one PR
each, and are what brings the file under 800.

Each step: `node src_vs_code/scripts/prove-move.mjs origin/main <original> <new files…>` shows every
new line came from the original; every unmatched line (a constructor, a field, a changed call site) is
justified one by one in the PR body. **Rebase and re-prove immediately before merging** — when main has
changed lines inside the moved region, the rebase stops on a modify/delete conflict, and the tempting
resolution (take the deletion) silently drops main's change, because the moved copy in the new file
never received it; `prove-move` then reports main's lines as missing. So a conflict inside a moved region
is resolved by re-cutting the step from the new main, never by taking either side (the extension's first
split nearly shipped exactly that loss, with every check green). (Wording corrected at this plan's own
plan round, 2026-09-26: git does not pick a side on its own — the person resolving the conflict does.)
`shared/refusal-sites.json` is re-recorded (`COAI_RECORD_REFUSAL_SITES=1`) in the step that moves a
refusal — the counts are per file.

**The boundary with [PLAN_feature_review.md](PLAN_feature_review.md) §7.1:** that plan's Epic 1 merges
FIRST — it carries every defect fix that touches these members (`Finish`, `CommandStageOf`, round
numbering), so the move carries them. Steps 1–4 land next, and that plan's Epic 2 branches only after
step 4 is on `main`. Steps 5–7 run beside its Epic 2 or between its Epics 2 and 3, never concurrently
with its story S2.2 (which edits `resolve`/`status`/`ask_human`); whichever lands second rebases and
re-proves.

## 4. Test plan

- **No test is edited except to re-point a call** (`PanelService.BuildWork` → `RosterBuilder.…`). A
  test whose assertion had to change would mean behaviour changed — stop and find out why.
- The whole `CoaiMcp.Tests` suite, through its executable, green before and after each step; the
  count of tests identical.
- `prove-move.mjs` clean (or every residue line justified) at merge time, after the last rebase.
- `ArchitectureTests` stay green; a new one pins that `RoundEngine` does not reference `PanelService`.

## 5. Definition of Done

- [ ] Seven PRs, one extraction each, every one proved a move after its last rebase. *(Steps 1–4: one
      pull request of four commits, each proved, and re-proved after the last rebase — §7 D1.)*
  - [x] Step 1 — records.
  - [x] Step 2 — prompt.
  - [x] Step 3 — roster.
  - [x] Step 4 — engine, with `ArchitectureTests.TheRoundEngine_DoesNotReferenceThePanelService`.
  - [ ] Step 5 — sweeps.
  - [ ] Step 6 — resolve path.
  - [ ] Step 7 — document stage.
- [ ] `PanelService.cs` at or under 800 lines; every file this plan creates under 800. *(Every new file
      is under 800; `PanelService.cs` is 1 727 lines after step 4 — steps 5–7 are what bring it down.)*
- [x] No test assertion changed; all green through the executable. *(The count is NOT unchanged — §7 D5.)*
- [x] `refusal-sites.json` re-recorded where a refusal moved (step 4: `PanelService.cs` 29 → 19,
      `Rounds/RoundEngine.cs` 10).
- [x] `research/module_server.md` describes the engine as its own unit.
- [ ] This plan promoted (after step 7).

## 6. Not in this plan

- Any behaviour change. Defects seen on the way are recorded here and fixed elsewhere.

Seen on the way through steps 1–4, and left as they were:

- **Stacked doc comments.** Several members carry another member's `<summary>` above their own, so both
  attach to the lower one: `WhatYouHave`'s above `WithoutTheStaleClaim`'s (now in `ReviewerPrompt`),
  `RolesWithRulesInMind`'s above `NoWrittenRules`' (`RosterBuilder`), the "how long this round may take"
  summary above `WhatEndedIt`'s (`RoundEngine`); in `PanelService`, three sweep summaries and an orphaned
  "which prompt each role gets THIS round" above `ScratchPrefixes`, three summaries above
  `RulesAreCriteria`, and two `<remarks>` on `DocumentContext`. Two were moved because the split forced
  it: `StageRun`'s summary (it sat above `RoundWork`'s — step 1) and `WithHumanDecision`'s (it sat above
  `WhatTheCallerWasDoing`, which left in step 4).
- **A dead cref.** `StageRun.RolesPerVendor`'s remarks cite `<see cref="IsPlanStage"/>`, a member that no
  longer exists; with no documentation build, nothing reports it.
- **`EveryWrapperOnTheRefusalRoad_ForwardsItsOwnCaller` does not list `RoundEngine.Error`,** the one new
  wrapper on the refusal road the move introduced. It does forward `[CallerMemberName] from`; a row for it
  would be a new test, and this plan adds none but the architecture rule.

## 7. What shipped differently (steps 1–4, 2026-09-25)

- **D1 — one pull request, four commits.** §3 says one extraction per pull request. Steps 1–4 land as ONE
  pull request whose four commits are the four steps, each proved by `prove-move.mjs` against its own
  parent, with its residue justified line by line in its message. Merged by REBASE, the four stay
  separate commits on `main` — what a pull request per step existed to buy — and the feature review's
  Epic 2 waits for one merge instead of four.
- **D2 — the orders are a unit of their own, `Rounds/RoundCommands.cs`.** With `MayProceed`,
  `CallerFor`, `CommandTextsNow`, `CustomOrdersFor`, `CommandStageOf` and the log helpers inside it,
  `RoundEngine.cs` would pass the 800-line ceiling; without them it is 768 lines. `PanelService`
  constructs it and hands it to the engine, because the consultation cadence's check before a code round
  (`CadenceBeforeTheCode`, which reached `main` while this work was in flight — D6) reads the same order
  texts. `CommandStageOf` is now `RoundCommands.CommandStageOf` (its test is re-pointed there). `RoundOrders` was not available as a name: `CoaiMcp.Store` already has one.
- **D3 — "what only they use" moved with the engine,** including members §2 placed elsewhere:
  `WhatTheCallerWasDoing` (listed under the resolve path, used only by `RunStageAsync`),
  `ApplyAnyHumanDecision`, `WarmRemoteRolesAsync`/`AskWhichRolesAsync`, `StageGate`, `ModelOf`,
  `SpentPrompts` and `WhereTheDocumentWent`.
- **D4 — what stays in `PanelService` is HANDED, never reached for.** `CanRun` and `RuntimeFor` go to the
  roster as delegates (`ExcludedFrom` reads the same predicate, and the tests call `RuntimeFor`/`AuthOf` on
  the service); `ExcludedFrom` and `NoReviewerRefusal` go to the engine the same way; the scratch sweep
  `PruneOldAnswerDirs` goes to the roster until step 5 moves the sweeps. The collaborators only the engine
  uses now (`BoundedScheduler`, `ReviewerExecutor`, `RolePrompts`, `UsageLedger`, `CallerSessions`) are
  constructor locals in `PanelService` rather than fields.
- **D5 — the test count grows, for two reasons only.** `NoSourceFileCarriesAControlByteTests` is a
  theory with one row per source file, so each new file adds one (seven), and step 4 adds the two
  architecture tests (the rule and its companion). No assertion changed. Instance members are reached
  through `internal` accessors — `service.Roster.BuildWork(…)`, `service.Engine.WhereTheDocumentWent(…)`
  — and four source-reading tests read the files their subject moved to.
- **D6 — `main` moved under the work, so the steps were re-extracted, not rebased.** The consultation
  cadence's epic 3 landed in `RunStageAsync`, `StageRun`, the constructor and `Finish` while steps 1–4
  were being built. A rebase across a move stops on modify/delete conflicts whose easy resolution drops main's change (§3), so
  instead each step was cut again from the new `main` with the same line-range splice — `main`'s lines
  carried by construction, and visible in the moved files — then re-proved and re-tested on its own parent.
- **Where it stands after step 4:** `PanelService.cs` 3 209 → 1 727 lines (from `main` as step 1 found
  it); `RoundEngine.cs` 768, `RosterBuilder.cs` 546, `RoundCommands.cs` 93, `ReviewerPrompt.cs` 84, the
  three records 146 together.
