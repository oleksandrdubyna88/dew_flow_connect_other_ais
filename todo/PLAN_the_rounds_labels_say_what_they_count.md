# PLAN — the Rounds labels say what they count

> Status: **built on its branch, 2026-10-19 — not merged.** Plan gate `proceed` and code gate `proceed` (session c098d202); the epic's cadence and risk consultations closed as solved; an Opus code review's help corrections folded in. Scope: the extension's Rounds block and its help —
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

1. A model line's median is labelled **per launch** when EVERY line of its group (captured or not — one rule for the
   median and the division) is a launch line: it carries a non-empty `attempt`, that attempt is not `followUp`, and it
   carries a non-empty `cli` that is not `remote`. Otherwise it is **per recorded turn**, because the line can hold more
   than one launch:
   - a line from before coai-mcp 0.48.0 has no `attempt` and may sum a repair or a follow-up;
   - a `remote` row's local line is the Team-server job's total — one line with `attempt: first` that can hold a first
     launch and its follow-up (`UsageLedger.cs:397–406` writes the remote outcome, which has no launch parts, as one
     line; `ReviewLauncher.cs:279–281` sums the two launches on the server). The line itself says so: its `cli` is
     `remote` (`RuntimeResolution.NameOf`, `RuntimeResolution.cs:94–100`, stamped by `RowLaunchOptions.cs:31` and written
     by `UsageEntry.RanWith`, `UsageLedger.cs:290–300`), so a deleted row or a row that changed runtime inside the week
     is judged by what the line recorded, not by today's settings;
   - an antigravity `followUp` continues the same conversation (`AntigravityRuntime.FollowUp`, `AntigravityRuntime.cs:99`;
     every other runtime has none, `ReviewerRuntime.cs:462`), and antigravity reports usage cumulatively in a
     conversation — measured for consultations, unmeasured for a reviewer follow-up
     ([PLAN_the_reviewer_follow_up_is_billed_once.md](PLAN_the_reviewer_follow_up_is_billed_once.md)). Until that plan
     measures it, a `followUp` line is not claimed as one launch;
   - a line with no `cli` is not claimed either (nothing says where it ran).

   The column header says per launch, and a cell that is not says *per recorded turn* beside its number, with a
   tooltip.
2. The model line's fallback division (rounds unknown) follows the same rule: **per launch** when every line is a
   launch line, else **per recorded turn**; its tooltip says *launches* (plural), never "launchs". Consultation, question, chat and probe lines keep **per recorded turn** — they
   are still one line per turn (census rows 2 and 4 of the round-counter plan).
3. The footnote says what is true, without claiming "always": from coai-mcp 0.48.0 a review run on this machine writes
   one line per launch; a line from before 0.48.0, a Team-server row's line and a follow-up in the same conversation can
   hold more than one launch, and a figure that includes such a line says *per recorded turn*.
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

- **1.1 (model) — a group knows whether its lines are launches.** `LedgerLine` (`roundMoney.ts:34`) gains `attempt` and
  `cli` (both already parsed by `usage.ts`). A pure `isLaunchLine(line)` =
  `(line.attempt ?? '') !== '' && line.attempt !== 'followUp' && (line.cli ?? '') !== '' && line.cli !== 'remote'`.
  `Rollup` (`roundMoney.ts:61`, which `MoneyGroup` extends at `:69`) gains `linesAreLaunches: boolean`. Exactly:
  group: `lines.length > 0 && lines.every(isLaunchLine)`;
  roll-up: `groups.length > 0 && groups.every((one) => one.linesAreLaunches)`. A group is never empty in practice (it is
  built from its members), and an empty roll-up is the "nothing in this window" case, so both answer false — the label
  then falls back to the safe *per recorded turn*. A chat turn read into the `LedgerLine` shape carries no `attempt` and
  so stays per recorded turn.
- **1.2 (labels) — the median and the fallback division.** New `PER_LAUNCH = 'launch'`. `MEDIAN_OUTPUT_LABEL` becomes
  `median output per launch`. `medianCell` adds ` <span class="hint">per recorded turn</span>` and a `title` when the
  group's `linesAreLaunches` is false. `unitDivisor('review', undefined, turns, linesAreLaunches)` answers
  `per: PER_LAUNCH` when it holds, else `PER_RECORDED_TURN`; `modelPerRoundCell` shows the hint for either unit (it
  already shows it for `PER_RECORDED_TURN`). `perUnit`'s plural (`roundMoney.ts:225`, `${divisor.per}s`) takes a plural
  table so `launch` reads `launches`. `kindLine` and the vendor row's per-round cell are unchanged. No row lookup is
  used: the decision is the lines' own.
- **1.3 (footnote) — `FOOTNOTE` rewritten** per *What must be true* 3.
- **1.4 (message) — `ROUNDS_UNAVAILABLE`** becomes: *"this Team server answered without the 5 h / 24 h / 7 d windows —
  Team servers count rounds from 0.12.0"*.
- **1.5 (help) — two articles in five languages.** (a) `what-each-ai-has-used` (`helpContent.ts:465`; `helpDe.ts`,
  `helpEs.ts`, `helpRu.ts` at `:208`, `helpUk.ts:200`, each the `usage` of its `"what-each-ai-has-used"` entry) says *"the median
  output per recorded turn"* and *"Every paid launch is its own ledger line"* — the first becomes *per launch, or per
  recorded turn where a line can hold more*, the second gains *for a row run on this machine; a Team-server row's line
  here is its job's total, and a follow-up in the same conversation is not yet split*. (b) `the-rounds-log`: the reason
  list gains *fewer than two readings*,
  *a launch sent no round id*, *no round ran between the readings*; "launches from an older extension" becomes "launches
  whose client sent no round id (a coai-mcp older than 0.49.0, or another caller of the server's API)". The phrases, which
  the help test pins per language:

  | | fewer than two readings | a launch sent no round id | no round ran between the readings | no round id (replaces "older extension") |
  |---|---|---|---|---|
  | en | `fewer than two readings` | `a launch sent no round id` | `no round ran between the readings` | `sent no round id` |
  | de | `weniger als zwei Lesungen` | `ein Start schickte keine Runden-ID` | `zwischen den Lesungen lief keine Runde` | `keine Runden-ID` |
  | es | `menos de dos lecturas` | `un lanzamiento no envió id de ronda` | `no corrió ninguna ronda entre las lecturas` | `cuyo cliente no envió id de ronda` |
  | ru | `меньше двух чтений` | `запуск не прислал id раунда` | `между чтениями не было раунда` | `чей клиент — coai-mcp до 0.49.0` |
  | uk | `менше двох читань` | `запуск не надіслав id раунду` | `між читаннями не було раунду` | `без id раунду` |

  The translations follow each file's existing terms for *reading*, *round* and *launch* (checked by reading the
  article's surrounding text first), and a second agent reads each back into English before commit.

## Build order

1.1 → 1.2 → 1.3 → 1.4 → 1.5, one PR. Tests first in each story (RED observed, then GREEN).

## Risk (for the gate's `riskItems`)

- **A per-launch claim on a mixed window.** A seven-day window that still holds pre-0.48.0 lines must not be labelled
  per launch. 1.1's rule (EVERY line of the group carries `attempt`) is what prevents it; the test pins a mixed group.
- **A line that carries `attempt` and still sums launches.** Found by the Opus plan review and the epic's risk
  consultation (1a46fd0c), each checked in the code. (a) A `remote` row: the block reads only this side's `usage.jsonl`
  (`roundCountsCache.ts:31`), and coai-mcp writes a remote turn as ONE line with `attempt: first` holding the job's
  total — excluded by the line's own `cli: remote`, pinned by a test with no configured row. (b) An antigravity
  `followUp` may be cumulative — excluded by its attempt until it is measured. (c) A local turn whose launch parts do not
  add up to its usage is written folded, as one `first` line (`LaunchParts.cs:87–91`); nothing on the line shows it.
  Rare, guarded by a test in `src_mcp`; **accepted as a residual**, and the footnote does not claim "always".
- **Translation drift.** Four hand-written translations of one paragraph; a wrong word misleads silently. Mitigation:
  read each back in English through a second agent before commit.

## Test plan

All in `src_vs_code/src/test/`, node test runner (`npm test` in `src_vs_code`):

| Story | Test (name says the guarantee) | RED before the fix because |
|---|---|---|
| 1.1 | `roundMoney.test.ts` — `AGroupOfLaunchLines_AreLaunches_AndOneOlderLineIsNot` (all lines with `attempt` → true; one without → false; an empty roll-up → false; a roll-up of one true and one false group → false) | `linesAreLaunches` does not exist |
| 1.2 | `roundMoney.test.ts` — the existing `ARepairedTurn_IsOneRecordedTurn_NotOneLaunch` keeps its old-line fixture and asserts it is not per launch (its `doesNotMatch(/launch/)` on `MEDIAN_OUTPUT_LABEL`, `roundMoney.test.ts:85–86`, moves to the cell); `unitDivisor` answers `launch` only when told the lines are launches | the division is per recorded turn for every line |
| 1.2 | `roundsBlock.test.ts` — `TheMedianHeader_SaysPerLaunch_AndAnOlderLineSaysPerRecordedTurnBesideItsNumber` | header says per recorded turn; no hint exists |
| 1.2 | `roundsBlock.test.ts` — `AModelWithUnknownRounds_AndAMixedWeek_IsDividedPerRecordedTurn` (codex gate finding: one pre-0.48.0 line and one launch line in one group, rounds unavailable → the fallback cell says *per recorded turn*), and `…_AndOnlyLaunchLines_IsDividedPerLaunch` | `…_AndOnlyLaunchLines_…`: no per-launch division exists. The mixed-week test is a GUARD — it passes on the old code too (the old fallback always said per recorded turn) |
| 1.1 | `roundMoney.test.ts` — `ALineIsALaunch_OnlyWhenItSaysSo` (`isLaunchLine`: `attempt: first, cli: codex` → true; `cli: remote` → false; `attempt: followUp` → false; no `cli` → false; no `attempt` → false) | `isLaunchLine` does not exist |
| 1.2 | `roundsBlock.test.ts` — `ATeamServerLine_StaysPerRecordedTurn_EvenWithItsRowDeleted` (lines `attempt: first, cli: remote`, `ranOn: ranOnOf([])` → median and fallback say *per recorded turn*), and `ALocalRow_StaysPerLaunch_AfterItsRowIsDeleted` (`cli: codex` lines, no configured row → per launch) | `ALocalRow_…` is RED (no per-launch label existed); `ATeamServerLine_…` is a GUARD for the median cell and the fallback (the old code said per recorded turn for every line) — its teeth are shown by removing the `remote` condition |
| 1.2 | `roundMoney.test.ts` — `TwoLaunches_ReadAsLaunches_NotLaunchs` (`perUnit` over `{ per: 'launch', count: 2 }` with unpriced money → the note says `2 launches`) | `perUnit` appends `s` |
| 1.5 | `helpCoverage.test.ts` — `EveryLanguagesUsageArticle_SaysTheMedianIsPerLaunch` (each `what-each-ai-has-used` article no longer says only *per recorded turn* for the median: en `per launch`, de `pro Start`, es `por lanzamiento`, ru `на запуск`, uk `на запуск`) | the article says per recorded turn |
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

- [x] 1.1–1.5 built, each test observed RED before its fix and GREEN after (or named as a guard, see the test table); the whole extension suite green (6420 pass, 0 fail, 2026-10-19).
- [x] coai gate: plan round `proceed`, code round `proceed`, every finding resolved with a reason (session c098d202).
- [x] `research/module_extension.md` says the median and fallback division are per launch when the lines are launches.
- [x] The round-counter plan's *Open tail* item "Labels that name the wrong thing" points to this plan as closing it.
- [ ] A `## Extension <version>` changelog section on the release PR that ships it.
- [ ] This plan promoted to `research/` with its deviations.
