# PLAN — the Rounds labels say what they count

> Status: **plan only, nothing implemented yet (2026-10-19); plan gate `proceed` (session c098d202), findings folded in.** Scope: the extension's Rounds block and its help —
> `src_vs_code/src/roundMoney.ts`, `roundsBlock.ts`, `teamServerApi.ts`, `helpContent.ts` and its four translations
> (`helpDe.ts`, `helpEs.ts`, `helpRu.ts`, `helpUk.ts`). No server or coai-mcp change.
>
> Related docs: [PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) (its *Open tail*, item
> "Labels that name the wrong thing", found by the Opus whole-feature review of 2026-10-19),
> [module_extension.md](../research/module_extension.md).

## The symptom

Four texts on the Rounds block, or in the help that explains it, say something that is not true for what a person sees
today. Each was checked in the code on 2026-10-19 (main at `ea9ccd1c8`):

1. **The median column says "per recorded turn" for lines that are launches.** The header is
   `MEDIAN_OUTPUT_LABEL = "median output per recorded turn"` (`roundMoney.ts:96`, used at `roundsBlock.ts:144`). The
   median is drawn only on a vendor row's model lines, which hold `review` lines only (`roundsBlock.ts:289`,
   `medianCell` at `roundsBlock.ts:326`). Since story 3.3 (coai-mcp 0.48.0, 2026-10-11) a review line is ONE launch —
   a first try, a retry, a repair and a follow-up each write their own line, and each such line carries `attempt`
   (`usage.ts:47`). Only a line written before 0.48.0 can hold a repair or a follow-up summed with its first launch.
   The label was right when it was written; for every line written in the last eight days it names the wrong unit.
2. **The footnote repeats it** (`roundsBlock.ts:114–117`): *"the median output is per recorded turn: one ledger line
   can hold a repair or a follow-up, so a per-launch figure is not claimed"*.
3. **The model line's fallback division says "per recorded turn" too.** When a model's rounds are unknown, its money is
   divided by its line count and labelled `per recorded turn` (`unitDivisor` at `roundMoney.ts:215`,
   `modelPerRoundCell` at `roundsBlock.ts:319–323`). For review lines written since 0.48.0 that count is a count of
   launches.
4. **"does not count rounds yet" names no version.** `ROUNDS_UNAVAILABLE` (`teamServerApi.ts:558`) says *"this Team
   server does not count rounds yet — it answered without the 5 h / 24 h / 7 d windows"*. A person cannot tell what to
   upgrade to. Server 0.12.0 is the first whose `/api/usage` answers the windows (checked:
   `src_server/src/Usage/UsageRounds.cs` exists at `server-v0.12.0` and not at `server-v0.11.0`).
5. **The help's list of reasons is short, and one phrase is wrong.** The `the-rounds-log` article
   (`helpContent.ts:427`; `helpDe.ts:197`, `helpEs.ts:197`, `helpRu.ts:197`, `helpUk.ts:181`) lists why a rounds-per-1 %
   figure can be unavailable as *"the window reset, a reading is missing or stale, or the percentage has not risen
   yet"*. `REASON_WORDS` (`gaugeWords.ts:117–126`) holds eight codes; three the server sends are not in the help:
   `too-few-readings`, `run-without-round` and `no-rounds`. (The eighth, `local-capture-deferred`, is already said by
   the same article: *"a local codex card says this machine does not read the gauge"*.) The same article says the **runs without a round** are *"launches from an older extension"*; the round id
   is sent by coai-mcp (from 0.49.0, 2026-10-12, story 4.1), not by the extension, and any other caller of the server's
   API sends none either.

Nothing is miscounted. Every number is right; three labels name the wrong unit, one names no version, and one help list
is incomplete.

## What must be true when this is done

1. A model line's median is labelled **per launch** when its row runs on this machine AND every line of its group
   carries `attempt` (captured or not — one rule for the median and the division). Otherwise it is **per recorded
   turn**: a line from before coai-mcp 0.48.0 may sum a repair or a follow-up, and a `remote` row's local line is the
   Team-server job's total — one line with `attempt: first` that can hold a first launch and its follow-up
   (`UsageLedger.cs:397–406` writes the remote outcome, which has no launch parts, as one line; `ReviewLauncher.cs:279–281`
   sums the two launches on the server). The column header says per launch, and a cell that is not says *per recorded
   turn* beside its number, with a tooltip.
2. The model line's fallback division (rounds unknown) follows the same rule: **per launch** when every line carries
   `attempt`, else **per recorded turn**. Consultation, question, chat and probe lines keep **per recorded turn** — they
   are still one line per turn (census rows 2 and 4 of the round-counter plan).
3. The footnote says what is true, without claiming "always": from coai-mcp 0.48.0 a review run on this machine writes
   one line per launch; a line from before 0.48.0, and a Team-server row's line, can hold a repair or a follow-up, and a
   figure that includes such a line says *per recorded turn*.
4. `ROUNDS_UNAVAILABLE` names **0.12.0** as the first Team server that counts rounds, without claiming the answering
   server is older (it says what it saw and what would answer).
5. The help article, in all five languages, lists every reason code `REASON_WORDS` names for a figure that is not a
   number (the seven server codes in its reason list; `local-capture-deferred` stays where the article already says it),
   in words a reader understands, and says a run without a round is a launch whose client sent **no round id**
   (coai-mcp older than 0.49.0, or another caller of the API).

## Out of scope

- Building any new figure (the "launches per round" figure of story 2.1 stays unbuilt).
- The consumer-scope, local-ledger and live-check items of the round-counter *Open tail*.
- Any change to what is counted or priced. Only labels, a footnote, one message and the help change.

## Epics and stories

One epic, branch `fix/rounds-labels-say-what-they-count`.

### E1 — the labels say what they count

- **1.1 (model) — a group knows whether its lines are launches.** `LedgerLine` (`roundMoney.ts:34`) gains `attempt`;
  `Rollup` (`roundMoney.ts:61`, which `MoneyGroup` extends at `:69`) gains `linesAreLaunches: boolean` — the LINE fact
  only; whether the row runs here is the block's fact and is added in 1.2. Exactly:
  group: `lines.length > 0 && lines.every((one) => (one.attempt ?? '').length > 0)`;
  roll-up: `groups.length > 0 && groups.every((one) => one.linesAreLaunches)`. A group is never empty in practice (it is
  built from its members), and an empty roll-up is the "nothing in this window" case, so both answer false — the label
  then falls back to the safe *per recorded turn*. A chat turn read into the `LedgerLine` shape carries no `attempt` and
  so stays per recorded turn.
- **1.2 (labels) — the median and the fallback division.** New `PER_LAUNCH = 'launch'`. `MEDIAN_OUTPUT_LABEL` becomes
  `median output per launch`. The block computes `perLaunch = ranOn(provider) === '' && group.linesAreLaunches` for a
  model line. `medianCell` adds ` <span class="hint">per recorded turn</span>` and a `title` when it is false.
  `unitDivisor('review', undefined, turns, perLaunch)` answers `per: PER_LAUNCH` when it holds, else
  `PER_RECORDED_TURN`; `modelPerRoundCell` shows the hint for either unit (it already shows it for
  `PER_RECORDED_TURN`). `kindLine` and the vendor row's per-round cell are unchanged.
- **1.3 (footnote) — `FOOTNOTE` rewritten** per *What must be true* 3.
- **1.4 (message) — `ROUNDS_UNAVAILABLE`** becomes: *"this Team server answered without the 5 h / 24 h / 7 d windows —
  Team servers count rounds from 0.12.0"*.
- **1.5 (help) — the `the-rounds-log` article in five languages**: the reason list gains *fewer than two readings*,
  *a launch sent no round id*, *no round ran between the readings*; "launches from an older extension" becomes "launches
  whose client sent no round id (a coai-mcp older than 0.49.0, or another caller of the server's API)". The phrases, which
  the help test pins per language:

  | | fewer than two readings | a launch sent no round id | no round ran between the readings | no round id (replaces "older extension") |
  |---|---|---|---|---|
  | en | `fewer than two readings` | `a launch sent no round id` | `no round ran between the readings` | `sent no round id` |
  | de | `weniger als zwei Messwerte` | `ein Start ohne Runden-ID` | `zwischen den Messwerten lief keine Runde` | `keine Runden-ID` |
  | es | `menos de dos lecturas` | `un lanzamiento sin id de ronda` | `ninguna ronda entre las lecturas` | `sin id de ronda` |
  | ru | `меньше двух замеров` | `запуск без id раунда` | `между замерами не было раунда` | `без id раунда` |
  | uk | `менше двох замірів` | `запуск без id раунду` | `між замірами не було раунду` | `без id раунду` |

  The translations follow each file's existing terms for *reading*, *round* and *launch* (checked by reading the
  article's surrounding text first), and a second agent reads each back into English before commit.

## Build order

1.1 → 1.2 → 1.3 → 1.4 → 1.5, one PR. Tests first in each story (RED observed, then GREEN).

## Risk (for the gate's `riskItems`)

- **A per-launch claim on a mixed window.** A seven-day window that still holds pre-0.48.0 lines must not be labelled
  per launch. 1.1's rule (EVERY line of the group carries `attempt`) is what prevents it; the test pins a mixed group.
- **A line that carries `attempt` and still sums launches.** Two cases, both found by the Opus plan review of
  2026-10-19 and checked in the code. (a) A `remote` row: the block reads only this side's `usage.jsonl`
  (`roundCountsCache.ts:31`), and coai-mcp writes a remote turn as ONE line with `attempt: first` holding the job's
  total, which can include a follow-up — handled by the row condition in 1.2, pinned by a test. (b) A local turn whose
  launch parts do not add up to its usage is written folded, as one `first` line (`LaunchParts.cs:87–91`); `attempt`
  cannot see it. Rare, guarded by a test in `src_mcp`; **accepted as a residual** and the footnote does not claim
  "always". (c) The row condition reads the row's runtime TODAY (`ranOnOf`, `roundsBlock.ts:55`); a row that moved
  between local and remote inside the week is judged by its current runtime — accepted, said in the footnote's tooltip.
- **Translation drift.** Four hand-written translations of one paragraph; a wrong word misleads silently. Mitigation:
  read each back in English through a second agent before commit.

## Test plan

All in `src_vs_code/src/test/`, node test runner (`npm test` in `src_vs_code`):

| Story | Test (name says the guarantee) | RED before the fix because |
|---|---|---|
| 1.1 | `roundMoney.test.ts` — `AGroupOfLaunchLines_AreLaunches_AndOneOlderLineIsNot` (all lines with `attempt` → true; one without → false; an empty roll-up → false; a roll-up of one true and one false group → false) | `linesAreLaunches` does not exist |
| 1.2 | `roundMoney.test.ts` — the existing `ARepairedTurn_IsOneRecordedTurn_NotOneLaunch` keeps its old-line fixture and asserts it is not per launch (its `doesNotMatch(/launch/)` on `MEDIAN_OUTPUT_LABEL`, `roundMoney.test.ts:85–86`, moves to the cell); `unitDivisor` answers `launch` only when told the lines are launches | the division is per recorded turn for every line |
| 1.2 | `roundsBlock.test.ts` — `TheMedianHeader_SaysPerLaunch_AndAnOlderLineSaysPerRecordedTurnBesideItsNumber` | header says per recorded turn; no hint exists |
| 1.2 | `roundsBlock.test.ts` — `AModelWithUnknownRounds_AndAMixedWeek_IsDividedPerRecordedTurn` (codex gate finding: one pre-0.48.0 line and one launch line in one group, rounds unavailable → the fallback cell says *per recorded turn*), and `…_AndOnlyLaunchLines_IsDividedPerLaunch` | no per-launch division exists |
| 1.2 | `roundsBlock.test.ts` — `ATeamServerRow_StaysPerRecordedTurn_EvenWhenItsLinesCarryAnAttempt` (a `remote` row, every line `attempt: first` → median and fallback say *per recorded turn*) | — (guards the row condition; observed RED by removing it) |
| 1.3 | `roundsBlock.test.ts` — `TheFootnote_SaysAReviewLineIsOneLaunch` (and no longer contains "a per-launch figure is not claimed") | old footnote |
| 1.4 | `teamRounds.test.ts` — `ACountlessServer_IsToldTheVersionThatCounts` (`ROUNDS_UNAVAILABLE` contains `0.12.0`); the existing assertion at `teamRounds.test.ts:85` (`/does not count rounds/`) is updated to the new text | no version in the text |
| 1.5 | `helpCoverage.test.ts` — `EveryLanguagesRoundsArticle_NamesEveryReasonTheGaugeWordsKnow` (each of the five `the-rounds-log` articles contains each phrase of its row in 1.5's table; the English one no longer says "launches from an older extension") | the reasons are missing |

The Rounds block's labels are static text from the pure function `roundsBlock(input)`, with no script and no control,
so they are asserted on its output as `roundsBlock.test.ts` already does (the repository rule allows source assertions
"where there is no program to run", `.agents/PROJECT.md:99–109`; the gate's finding asking for `bundledPage.test.ts`
was rejected with this reason).

The existing tests that assert `per recorded turn` for consultation/question/chat/probe lines (`roundsBlock.test.ts:92–110`)
must stay green unchanged — they are the guard that 1.2 did not over-reach. The whole extension suite runs at the end.

## Definition of Done

- [ ] 1.1–1.5 built, each test observed RED before its fix and GREEN after; the whole extension suite green.
- [ ] coai gate: plan round `proceed`, code round `proceed`, every finding resolved with a reason.
- [ ] `research/module_extension.md` says the median and fallback division are per launch when the lines are launches.
- [ ] The round-counter plan's *Open tail* item "Labels that name the wrong thing" points to this plan as closing it.
- [ ] A `## Extension <version>` changelog section on the release PR that ships it.
- [ ] This plan promoted to `research/` with its deviations.
