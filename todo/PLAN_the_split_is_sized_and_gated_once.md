# PLAN — the split order sizes the work honestly, gates it once per epic or per task, and says what it ordered

> Status: **plan only, nothing implemented yet, 2026-09-23.** Scope: the *Split the plan into epics
> and stories* switch — `PlanShape`'s verdict, `GateCommands.SplitCommand`/`AlreadySplitCommand`, a new
> gate-granularity setting, and a record of the orders a round gave (rounds DB, `--log`, the rounds log
> page). Issue #131; the "1 gate per epic / 1 gate per task" half of issue #467.
>
> Related: [PLAN_commands_and_autonomy.md](../research/PLAN_commands_and_autonomy.md) (the design record
> of the three switches), [PLAN_the_caller_names_its_strongest_model.md](../research/PLAN_the_caller_names_its_strongest_model.md)
> (issue #117, which this builds on), [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md).

## The symptom

The owner, after a week with *Split the plan into epics and stories* on (issue #131, 2026-09-08):

1. **It always cuts 4–5 epics of 4–5 stories.** Whatever the plan.
2. **It is very slow**, and it seems to run the gate after every STORY. The owner wants the gate once
   per EPIC, and one commit per epic.
3. **The split should be honest and transparent**, with the database and the logs reviewed so it can
   be seen what was ordered and why. Sizing the owner asked for:

   | size | cut into |
   |---|---|
   | relatively small | stories only, 3–5 |
   | medium-large | 2–3 epics × 2–3 stories |
   | large | 3–4 epics × 3–4 stories |
   | huge | 4–5 epics × 3–5 stories |

Issue #467 asks for the same two choices as switches: "1 gate per epic" and "1 gate per task", and for
the per-story wording to go from the prompts that carry it.

## What the code does today — measured, not assumed

**The verdict.** `PlanShape.Verdict` (`src_mcp/core/Commands/PlanShape.cs:38-41`):

```
Epics   when (Lines > 300 && (Steps >= 6 || Areas >= 4)) || Files >= 14
Stories when Steps >= 4 || Lines > 100
AsItIs  otherwise
```

It was calibrated on 23 plans (its own remarks). Run over the **187** `PLAN_*.md` in `research/` and
`todo/` on 2026-09-23 with the real `PlanShapeReader` (a scratch console referencing
`CoaiMcp.Core`, not a re-implementation), it answers **Epics for 106, Stories for 69, AsItIs for 12**.
Two of its four inputs no longer discriminate:

| metric | p10 | p25 | p50 | p75 | p90 | max |
|---|---|---|---|---|---|---|
| lines | 90 | 115 | 162 | 264 | 440 | 1372 |
| build steps | 0 | 3 | 4 | 6 | 7 | 13 |
| files named | 6 | 9 | **15** | 24 | 40 | 127 |
| areas | **4** | 5 | 6 | 8 | 8 | 10 |

The median plan names 15 files, so `Files >= 14` alone sends most plans to epics; and `Area`
(`PlanShape.cs:66-68`) matches words like `src`, `tests`, `research` in prose, so nine plans in ten
"touch four areas". That is symptom 1.

**The order.** `SplitCommand` (`src_mcp/core/Commands/GateCommands.cs:92-111`) says "2-4 EPICS, then
each epic into 2-4 … STORIES" and then **"After EVERY story: call review_code on that story's diff …
and commit. Only then start the next one."** A gate session is keyed by repo+branch and ends at `Done`
after its code round (`src_mcp/core/Rounds/RoundMachine.cs:132-141`, `:326`), and a code round is
refused until a plan round proceeded — so "review_code after every story" costs a new branch, a plan
round AND a code round per story. That is symptom 2. The same words are in the help: `help.ts:23-24`,
`package.json:567-571` (`markdownDescription`), `helpContent.ts:148` and `help{Ru,De,Es,Uk}.ts:88`.

**The record.** Nothing records what a round ordered: the `rounds` table (`src_mcp/src/Store/Schema.cs`)
has no column for it, `RoundContext` does not carry it, and the only trace is one Serilog line,
`split ordered to caller …` (`PanelService.cs`). That is symptom 3.

## What must be true when it is done

1. **Five sizes, and the order names the owner's numbers.** `PlanShape.Split` becomes `AsItIs`, `Small`,
   `Medium`, `Large`, `Huge`, decided by build steps and length only (files and areas stay in the
   reported numbers, and out of the verdict), with thresholds fixed from the corpus above:

   | size | when | the order says |
   |---|---|---|
   | AsItIs | fewer than 3 steps and ≤ 120 lines | build it as it stands, one gate |
   | Small | 3–6 steps, or 121–350 lines | 3–5 STORIES, no epics |
   | Medium | 7–9 steps, or 351–600 lines | 2–3 EPICS × 2–3 STORIES |
   | Large | 10–12 steps, or 601–900 lines | 3–4 EPICS × 3–4 STORIES |
   | Huge | 13+ steps, or more than 900 lines | 4–5 EPICS × 3–5 STORIES |

   Over the 187 plans that is 15 / 129 / 28 / 10 / 5 (8 % / 68 % / 14 % / 5 % / 2 %); the five it calls
   Huge include `PLAN_team_server.md`, `PLAN_who_holds_a_key.md` and
   `PLAN_every_message_is_written_down.md`, each of which was in fact built as several epics. The order
   also says **"fewer is fine when the work is smaller; never more"** and keeps saying the numbers are a
   heuristic it may argue with.
2. **A gate-granularity setting, `coai.gatePer`: `epic` (default) or `task`.** Two radio buttons under
   the split switch — *One gate per epic* / *One gate for the whole task*. The "After EVERY story"
   sentence is gone from every place listed above.
   - **per epic**: each epic is one unit of review — its own branch, one `review_plan` with the epic's
     plan, its stories built WITHOUT a gate each, one `review_code` over the epic's whole diff, and the
     epic committed as ONE commit before the next begins. A Small plan (stories, no epics) is one unit:
     one code round over the whole diff at the end.
   - **per task**: this plan round is the only plan gate; every epic and story is built on this branch
     with no gate of its own, one `review_code` over the whole task's diff at the end, and each epic
     still committed as ONE commit.
   - `AlreadySplitCommand` (a piece coming back) follows the same setting: under `epic` it is today's
     "build as one unit, one review, one commit"; under `task` it says the task is gated once as a
     whole and this piece is not gated on its own.
3. **Only a non-default crosses**: `COAI_GATE_PER=task`; `epic` sends nothing.
4. **What a round ordered is written down.** A new schema step adds `rounds.commands` (the JSON array of
   the orders exactly as the caller received them, `[]` when none) and `rounds.plan_shape` (the size and
   its numbers, `''` when no verdict was computed). `--log` carries both on `LoggedRound`; the rounds
   log page shows them on the round's detail as **Orders given**, one line per order, the size first.
5. **Old halves degrade honestly**: an older extension ignores the two new members; a newer extension
   reading a database an older server wrote finds no column (`pragma_table_info`, as `RoundsQuery`
   already asks) and shows nothing rather than "no orders"; the panel says beside the radio buttons when
   the installed server predates `COAI_GATE_PER` (the #117 skew-note shape).

## Constraints

- The stored key `coai.splitPlan` and `COAI_SPLIT_PLAN` keep their names and meaning.
- The bench's split check reads `"Split this plan into"` (`src_bench/CoaiBench/Running/SettingsApplied.cs`)
  — every sized order keeps that opening; AsItIs keeps its own words.
- Schema: a new step appended to `Schema.Steps`, never an edit of an earlier one; readers open the
  database read-only and ask which columns exist.
- The per-caller model order (#117) is untouched. Editable command text and custom commands are #467's.
- C#: records, no null in business logic, complexity ≤ 4. TS: eslint suppressions may only shrink. The
  webview is tested by running it.

## Boundary with the neighbouring plans

| Item | Built by | The other plan's part |
|---|---|---|
| Per-caller model names | #117 (shipped) | — |
| Sizes, gate granularity, the orders record | **this plan (#131)** | — |
| The two #467 switches "1 gate per epic / 1 gate per task" | **this plan**, as the `coai.gatePer` radio | #467 does not add them again |
| Editable command text, custom commands, export/import | #467 | will expose these orders as editable templates; `coai.gatePer` is one of the settings it exports |

## Growth

`rounds` gains two TEXT columns per round: the orders are at most three sentences (~2 KB) and the
shape ~100 bytes. At the rounds this machine records (a few hundred a week) that is under 1 MB a
year, retired with the rows they belong to — the table has no retention of its own today, and this
plan does not change that.

## Build order

1. **Sizes** — `PlanShape.Split` and `Verdict` (tests from the corpus table); `SplitCommand`'s five
   texts; the bench fixtures.
2. **Gate granularity** — `COAI_GATE_PER` in `PanelSettings`, `CommandContext.GatePer`, the two
   endings of `SplitCommand` and `AlreadySplitCommand`; panel radio, `envBlock`, skew note, help in five
   languages, `package.json`.
3. **The record** — schema step `WhatItWasTold`, `RoundContext`/the round writer, `RoundsQuery` +
   `LoggedRound`, `--log`; the extension's `parseLog`, the log page's round detail; compat tests.
4. Docs: `research/module_server.md` (commands), `research/module_extension.md` (gate section, log
   page), `research/architecture.md` if the `--log` seam paragraph needs the new members, CHANGELOG
   `## Unreleased`.

## Test plan

- **C#** `PlanShapeTests`: each size at its boundaries (2/3 steps, 120/121 lines, 6/7, 350/351, …); a
  plan naming 40 files with 3 steps and 150 lines is Small, not epics (the regression). `GateCommandsTests`:
  each size's numbers in the order; `epic` and `task` endings; no "After EVERY story" in any output;
  AlreadySplit under both. `SettingsAreLiveTests`: `COAI_GATE_PER` live. Store: a round written with
  orders reads back through `--log` with both members; a database WITHOUT the columns (built by the
  previous step list) reads back with empty members and no exception. End to end (`SplitOrderTests`):
  a round's `commands` column equals the `commands` the reply carried.
- **TS**: `settingsShape` — `gatePer` pristine → no key; `task` → `COAI_GATE_PER=task`; the radio,
  RUN, posts the write; `parseLog` with and without the members; the log page, RUN, shows *Orders
  given* for a round that has them and nothing for one that does not.
- **Bench**: fixtures updated to the new wording; the split check still recognises every size.
- Whole suites: `CoaiMcp.Tests.exe`, `CoaiBench.Tests.exe`, `npm test`, `npm run lint`, plan-lifecycle.

## Definition of Done

- [ ] Five sizes from steps and length, thresholds as in the table, the corpus test pinned.
- [ ] The order names the owner's numbers and says fewer is fine, never more.
- [ ] `coai.gatePer` epic/task in the panel; "After EVERY story" gone from the order, the help (five
      languages) and `package.json`.
- [ ] Each round's orders and size are in the database, on `--log` and on the rounds log page.
- [ ] Older extension / older database / older server each degrade as described.
- [ ] Tests above green; whole suites run; module docs and CHANGELOG updated; this plan promoted.
