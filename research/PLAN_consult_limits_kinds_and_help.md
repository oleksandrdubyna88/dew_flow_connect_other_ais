# PLAN — The consultant's three limits work while the server runs, every surface says the consultation's kind, and every consultant setting explains itself

> Status: **IMPLEMENTED, 2026-09-26.** All three stories shipped in one epic through one gate: the plan
> round (`good_enough`, 6 of 12 accepted) and the code round (`proceed`, 12 reviewers, 3 of 17 accepted).
> **Deviations**, each from the code round:
> - **The sweep decides under the repository's lock, on a fresh read.** It used to take the lock only to see
>   that it was free, release it, and write from the copy it had enumerated. A follow-up starting in that
>   window could have its `asking` overwritten by a stale close. The window was rare once per start and worth
>   closing once the sweep ran every minute (`ConsultationStore.SweepOne`, pinned by a stale-copy test and
>   broken by compiling code).
> - **A checkbox's `?` sits after its label, not inside it.** A click anywhere inside a label toggles its
>   checkbox, so the `?` on *Let an AI consult another vendor* flipped consulting off when someone read its
>   help. So did nine more in the panel (the five gate/side switches and each reviewer's plan and document
>   boxes), fixed in the same PR on the operator's word. Because that is BEHAVIOUR, it is measured in real
>   Chromium (`scripts/measure-help-clicks.mjs`, `npm run measure:help-clicks`): all 61 `?` are clicked, none
>   flips, and a planted wrong control must flip. Against `main` it reported exactly the nine. It is not a
>   markup assertion, since a new behavioural assertion over page text is refused (`.agents/PROJECT.md`).
> - **The record-outcome pick looks the consultation up first,** so one gone from the log is said before an
>   outcome is chosen; `ConsultFor` moved into the shared scenario base rather than being copied.
>
> **Open tail:** the four existing checkboxes in `panelView.ts` (*Separate settings for each side*, *Stop the
> local reviewer…*, *Work autonomously*, *Split the plan…*) carry their `?` inside the label and so have the
> same toggle-on-click defect. That is outside this plan, named and not changed. The Spending page still
> shows a consult turn without its kind, as D2 decided. Scope: the idle sweep's schedule
> (`src_mcp/src/Program.cs` serve path, `src_mcp/src/Server/PanelService.cs`), the consultation's kind on
> every surface a person reads (`src_vs_code/src/roundsLog.ts`, `consultations.ts`, `panelProvider.ts`,
> `ConsultationService.cs` log lines, `ServerJsonContext.cs` `OpenConsultation`), the `?` on every consultant
> and cadence setting (`help.ts`, `consultantView.ts`, `cadenceSettings.ts`), and the docs (README, the
> help article in five languages, the `consult` tool description).
>
> Related docs: [PLAN_consult_on_a_cadence.md](PLAN_consult_on_a_cadence.md) (the kinds and the
> exemption this documents), [PLAN_consultant.md](PLAN_consultant.md) (the three caps),
> [module_server.md](module_server.md), [module_extension.md](module_extension.md).

## The symptom

The operator, 2026-09-26, looking at the Consultant section (*Turns per consultation 5 · Calls per
session 10 · Close an idle consultation after, minutes 15*): "these limits must work". They also asked
for the kind (stuck, cadence, risk) in the logs, and a `?` on every new setting.

A read of the code, every claim checked:

1. **The idle close does not happen while the server runs.**
   - A follow-up after the idle time IS refused (`ConsultationRules.cs:35-37`).
   - The RECORD is closed only by `ConsultationService.Sweep` (`ConsultationService.cs:238-239`), and
     that is called only from the `PanelService` constructor (`PanelService.cs:130`).
   - So an idle consultation stays `open` on the sidebar card and in `status` until the next server
     start or a settings change. A person reads "open" long after "close after 15 minutes".
2. **Three limit behaviours have no test that would notice their loss.**
   - The idle follow-up refusal (`ConsultationRules.cs:35`): no test says "sat idle".
   - A cadence or risk follow-up after its turn cap: turns apply to every kind (`ConsultationService.cs:581`),
     but only a stuck consultation is tested at the cap.
   - A cadence follow-up not counted against calls-per-session (`ConsultationService.cs:522-524`):
     `AFollowUpKeepsTheKindItWasOpenedWith` (`ConsultKindsScenarioTests.cs:142-153`) passes even if it IS
     counted, because the first cadence call spent nothing.
   - No test asserts that each of `COAI_CONSULT_TURNS`, `COAI_CONSULT_CALLS_PER_SESSION` and
     `COAI_CONSULT_IDLE_MINUTES` crosses from the panel (`settingsShape.ts:509-517`).
     `settingsReach.test.ts:74-86` changes all three at once and checks only that the env differs.
3. **The kind is shown on one surface of nine.**
   - It IS on the log's *Consultations* table (the *For* cell, `roundsLog.ts:1041-1063`), merged with what
     the consultation covered, so `stuck` reads as a bare word nobody explained.
   - It is NOT shown on:
     - the sidebar card (`consultations.ts:128-142`: no kind field, although the record holds `Kind`,
       `ConsultationRecord.cs:131-137`);
     - the server's log lines (`ConsultationService.cs:610, 676, 712`);
     - `status`'s open consultations (`OpenConsultation`, `ServerJsonContext.cs:41-50`);
     - the *How did this consultation end?* quick pick (`panelProvider.ts:2607`).
4. **No consultant setting has a `?`, and three of the four cadence numbers have none.**
   - `consultantView.ts` does not import `help`. The consultant settings are the switch (182), vendor,
     model, endpoint and CLI path (409-419), the three caps (189-198) and the prompt (461-464).
   - Of the cadence numbers only the mode has one (`cadenceSettings.ts:106`), not the group size, the risk
     threshold or the risk cap (111-120).
   - `help.ts` has no consult keys.
   - No test requires a control to HAVE a `?`: `helpTooltips.test.ts` checks only that every tooltip sits
     on a control.
5. **The docs never name the kinds or the exemption.**
   - README (`src_vs_code/README.md:160-171, 246-248`) and the help article (`helpContent.ts:291-304` and
     its four translations at line 48) say nothing of:
     - `stuck` / `cadence` / `risk`;
     - that cadence and risk consultations spend no calls-per-session, bounded to one per group or item
       instead;
     - that turns and the idle close apply to every kind;
     - that an idled cadence consultation lapses and no longer satisfies the gate.
   - The master switch still reads "Let a stuck AI consult another vendor" (`consultantView.ts:182`),
     yet it switches the cadence off too (`ConsultationService.cs:309-311`).
   - The `consult` tool description says only "Turns per consultation and calls per session are capped"
     (`Tools.cs:386`).

## Decisions (open to the gate)

- **D1. The idle sweep runs every minute while the server serves,** on the serve path's stopping token
  (`Program.cs:1813`), through `PanelServiceHost.Current` (a settings reload keeps the loop). It is the same
  `Sweep` the constructor runs, so a dead process's `asking` record and an expired one are swept on the
  same beat. A failure is logged and the next tick tries again, the way `RunLife.Beaten` does. One minute,
  because the setting is in minutes: the card is at most a minute late, and the sweep reads one small
  directory.
- **D2. Kind and "for" become two columns in the log**, *Kind* (`stuck` / `cadence` / `risk`) and *For*
  (epics or story, and the plan). The kind leads the sidebar card, the server log lines and the quick
  pick, and `OpenConsultation` gains `Kind`. The Spending page is out of scope: the usage ledger records a
  consult turn without its kind (`ConsultationService.cs:852-854`), and adding it is a schema step for
  a question nobody asked. That is stated, not built.
- **D3. Every control in the Consultant section and the cadence block carries a `?`,** with a `HELP` entry
  that says what it does and why. A new test requires it: every `data-setting` control in those two
  sections has a `help(...)` beside it. The tooltip census then works in both directions for the part of
  the panel this plan touches.
- **D4. The master switch is renamed "Let an AI consult another vendor"**, and its hint says it covers
  the cadence too. Help in all five languages; README; the tool description names the exemption.

## From the plan round (2026-09-26, `good_enough`, 6 of 12 accepted)

- **The loop itself is tested, not only the sweep** (codex). The real `PanelServiceHost` runs over a temp
  data directory with the loop at a short interval, and a backdated record lapses without any direct call.
  Then the panel's settings file is rewritten (a reload), a second backdated record is written, and it
  lapses too. A loop that never starts, or dies on a reload, turns this red.
- **Risk as well as cadence**: the turn-cap refusal and the calls-per-session exemption are each tested
  for both ordered kinds (codex).
- **A record from before the kinds reads `stuck` everywhere** (local): in `OpenConsultation.Kind` (never
  null), on the card and in the log's Kind column, each pinned by a legacy-record test.
- **The census goes control by control** (local): every `data-setting` id in the Consultant section and
  the cadence block is named in the failure when its `?` is missing.
- **The promotion updates `todo/README.md`**, and `plan-lifecycle` runs after the move (codex).

Declined, with reasons recorded at the gate:
- a probe mode for the loop, since the host-level test covers it;
- a restart re-init test, since a restart is a new serve;
- restating the Spending deferral, which D2 already states;
- a per-language label test, since the labels are English-only;
- retention bounds, which the sweep already has (`ConsultationStore.Retention`, and the dead-pid check);
- wire compatibility for `OpenConsultation`, which the extension never parses.

## Build order (one epic, three stories, one gate)

1. **Story 1: the limits.** Red first: a consultation idle past its budget in a RUNNING server lapses
   without a restart, driven through `PanelService.SweepConsultations()` over a record backdated on disk.
   Then the sweeper loop, with an interval seam for tests, that survives a throwing sweep. Then the missing
   tests: the idle follow-up refusal; a cadence follow-up refused after its turn cap; a cadence follow-up
   not counted (calls cap 1, one stuck call spends it, the cadence follow-up still goes); each of the three
   values crossing from the panel on its own.
2. **Story 2: the kind.** Red first on each surface:
   - the log table's *Kind* column (a run-the-page test);
   - the sidebar card's kind (`parseConsultation` reads `Kind`; an old record without it reads `stuck`);
   - `status`'s `OpenConsultation.Kind`;
   - the log line's kind (captured Serilog);
   - the quick pick's item text.
3. **Story 3: the `?` and the docs.** Red first: the new census fails on the missing icons. Then the
   `HELP` entries, the `help(...)` calls, the switch's label and hint, the README, the article in five
   languages, the tool description, `research/module_server.md` and `module_extension.md`, and the
   `CHANGELOG`.

## Test plan

- Every story opens RED with the real symptom, then goes GREEN. Each new guard gets a break-it check by
  compiling code.
- Full suites before the code round: `dotnet build` + `CoaiMcp.Tests`, `npm test` (after `rm -rf out`),
  `npm run lint`, and the family checks (`plan-lifecycle`, `rules check`, `pin-check`, `adapter-check`,
  `prepare-gate --check`).
- `helpCoverage.test.ts` keeps every setting in the article. The new census keeps every consultant and
  cadence control carrying a `?`.

## Definition of Done

- [x] An idle consultation reads `lapsed` within a minute of its budget in a running server, pinned by a
      test watched red.
- [x] Each of the three limits has a test that fails when its enforcement is removed, for every kind it
      applies to; each value's crossing from the panel is tested on its own.
- [x] The kind is on the log (its own column), the sidebar card, `status`, the server log lines and the
      quick pick.
- [x] Every Consultant and cadence control has a `?`, enforced by a test.
- [x] README, the help in five languages, the tool description, module docs and CHANGELOG say the kinds,
      the exemption, and what applies to every kind.
- [x] This plan promoted to `research/`.
