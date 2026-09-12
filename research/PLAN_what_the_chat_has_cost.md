# PLAN — what the chat has cost, beside what the reviewers cost

> Status: **IMPLEMENTED, 2026-09-12.** Scope: the spending tab
> (`src/roundsLog.ts`, `src/panelView.ts`, `src/panelProvider.ts`), a new door ledger
> (`src/chatDoors.ts`, `src/chatDoorsFile.ts`), the five doors in `src/extension.ts`.
>
> Related docs: [module_extension.md](module_extension.md),
> [PLAN_usage_that_compares.md](../todo/PLAN_usage_that_compares.md),
> [PLAN_team_usage_by_person.md](../todo/PLAN_team_usage_by_person.md).
>
> The plan round ran on 2026-09-12: three reviewers, fifteen findings, **nine taken and six
> rejected with reasons**. Two of the taken ones reversed a decision this plan had made the other
> way — the door records were going to share `chat-usage.jsonl`, and the recording was going to
> happen inside `deliverPassage`. Both are marked below where they were changed.

## The symptom

**The page that exists to answer "where did the money go" does not count the chat at all.**

*What each AI has used* reads one ledger — `usage.jsonl`, which `coai-mcp` appends a line to per
reviewer ([panelProvider.ts:2251](../src_vs_code/src/panelProvider.ts#L2251) →
[panelProvider.ts:466](../src_vs_code/src/panelProvider.ts#L466)). Every chat turn this extension runs
is written down too, to a file of its own
([chatUsageFile.ts:67](../src_vs_code/src/chatUsageFile.ts#L67)), and that file is read on the same
page for a different purpose — the rounds table draws a row per conversation. The spending tab never
looks at it. So a day spent asking a second model costs nothing on the only screen that adds money up.

And two of the numbers the operator asked for are recorded **nowhere**:

- how many times `CoAI: take the question` and `CoAI: add the question` were used;
- how many times the chat was opened at all, by any door.

Neither can be derived from what is on disk. A `ChatTurnRecord` is written when a turn FINISHES
([chatUsage.ts:210](../src_vs_code/src/chatUsage.ts#L210)); `add the question` normally finishes no
turn — it fills the composer and stops — so counting turns counts something else entirely.

## What ships

The spending tab gains two named sections under the same Today/Week/Month/Year buttons it has now.

**1. Reviewers** — the cards that are there today ([panelView.ts:1259](../src_vs_code/src/panelView.ts#L1259)),
under a heading, unchanged otherwise, with their existing "All vendors" line kept as the section's
own total (tokens and money).

**2. A rule between the two.** Not a margin — a line, because the two halves are different ledgers
written by different programs and a reader must not add them up by eye without noticing.

**3. Chat** — a row per **vendor and model**, each carrying:

| Column | Where it comes from |
|---|---|
| Vendor | `ChatTurnRecord.provider`, coloured from the same `vendorPalette` the cards use |
| Model | `ChatTurnRecord.model` — the model that ACTUALLY answered |
| Tokens in / out | summed over the window |
| Price in / out | the rate in force for that model, per million ([panelProvider.ts:331](../src_vs_code/src/panelProvider.ts#L331)) |
| Cost in the window | billed where the vendor billed, worked out from the rates where it did not, marked with a tilde exactly as `usage.ts` already marks an estimate |
| **All time** | the same money over the WHOLE file, deliberately outside the window |
| **Asked** | invocations of `take the question` + `add the question`, in the window |
| **Opened** | invocations of every door, in the window |

and a section total of its own: tokens, money, and **both counts summed**.

**A model with no rate shows no money and is counted as unpriced** — the shape `spendSoFar` already
uses ([chatSpend.ts:47](../src_vs_code/src/chatSpend.ts#L47)), so a row nobody can price says so
instead of reading as free. (Taken from the plan round: a total that silently drops what it cannot
price is the most confident number on the page and the least true.)

**The counts are WINDOWED, like the tokens beside them; only the money has an all-time column.**
The plan left this ambiguous and a reviewer asked which it was. (Taken.)

**4. A door record — in a file of its own.** One line per invocation, in `chat-doors.jsonl`:

```json
{"utc":"2026-09-12T10:00:00.000Z","door":"take","provider":"antigravity","model":"gemini-3.8-flash"}
```

Five doors, from [extension.ts:193–223](../src_vs_code/src/extension.ts#L193): `key`
(`coai.chatWithOtherAi`), `default` (`coai.chatNow`), `choose` (`coai.chatChoose`), `take`
(`coai.takeTheQuestion`), `add` (`coai.addTheQuestion`). **Asked** counts `take` and `add`;
**Opened** counts all five. One field answers both, and a sixth door added later is counted by the
second without anybody remembering to.

## The decisions, and what was refused

**A door EVENT, not a field on the turn record.** A field would have made this a one-line change,
and it would have counted the wrong thing twice over: `add the question` usually produces no turn to
carry the field, and a conversation answered ten times would carry its door ten times. The question
is "how often was this reached for", which is about invocations and nothing else.

**Its own file — reversed on the plan round, by two vendors independently.** The first draft put
door lines into `chat-usage.jsonl` to save a read. `parseChatUsageLine`
([chatUsage.ts:246](../src_vs_code/src/chatUsage.ts#L246)) requires a non-empty `utc` **and nothing
else**, coercing every other field, so a door line in that file is read by every existing caller as a
TURN with zero tokens — a phantom conversation row in the rounds table today, and the same in any
older build somebody rolls back to. There is no shape that avoids it while keeping a timestamp. A
second file costs one reader and one stamped cache; it cannot corrupt a file that four call sites
already read.

**Recorded at the command, before the work — reversed on the plan round.** The first draft recorded
inside `deliverPassage` ([chatCommand.ts:2226](../src_vs_code/src/chatCommand.ts#L2226)) because
every door passes through it. Every door that GETS there: a door that cannot resolve a CLI, or that
the person dismisses, returns first — and those are invocations too, which is the whole question the
count answers. So the line is written in the command handler, before anything can refuse.

**`readyForChat()` is synchronous** ([chatCommand.ts:2335](../src_vs_code/src/chatCommand.ts#L2335)),
which is what makes the provider and model available at the command rather than only after the tab
exists. A door that resolves neither is written with empty ones and grouped into **one named bucket**
rather than an invented row; the section total carries both counts as well, so the per-row numbers
never have to add up to it by luck. (Taken: two reviewers, independently, on attribution.)

**Vendor AND model, where the reviewer cards show vendor alone.** A chat switches model
mid-conversation — that is the feature the picker exists for — and a price is per model. A row keyed
on the vendor could not show a price at all without averaging two rates into a number nobody is
charged. A door's count belongs to the model that was IN FORCE when it was used, which is a different
claim from the model that answered, and the summary is what makes the difference harmless.

**All time sits beside a windowed row on purpose.** It was asked for in those words, and it is the
number behind the decision the running total in the tab was built for: carry on with this
conversation, or start a fresh one. It costs nothing extra — the whole file is already in memory.

**The window buttons stay shared.** One control over both sections: *"день месяц год оставляем одни
и теже"*.

### What was rejected, and why

- **A price snapshot per turn, so an old turn re-prices at its old rate.** What a vendor BILLED is
  already persisted per turn (`costUsd`), and a turn nobody billed is shown as an estimate with a
  tilde — the convention the section above it on the same page already uses. A second convention for
  the lower half would make one page disagree with itself. The useful half of the finding — say what
  happens when there is no rate at all — was taken, above.
- **"Usage charged before a timeout is lost."** It is not: a turn carries its outcome, and
  `ChatOutcome` is `answered | stopped | failed` ([chatUsage.ts:173](../src_vs_code/src/chatUsage.ts#L173));
  the recorder writes whatever it is given. A hard process kill loses the in-flight line for reviewers
  too.
- **A startup migration that rewrites old lines to carry `kind: "turn"`.** With the doors in their own
  file nothing about the old file changes, and a read-modify-write over a year of history to add a
  constant field is the one operation that can lose it.
- **A temp-file-and-rename for every append.** Nothing pairs a door with a turn, so there are no
  orphans; the tearing concern was MEASURED when this ledger was built and did not hold
  ([chatUsage.ts:24](../src_vs_code/src/chatUsage.ts#L24)).
- **A cache-reset button.** The `size:mtime` stamp is deliberate and documented; the page already has
  a control for "this is not the number I want to see" in the per-vendor forget button.
- **"Opened is inflated by invocations that produced no answer."** That is what the number means.
  Opened larger than the turns is the information: a tab was opened and nothing was asked.

## Build order

1. **`chatDoors.ts` — the record, pure.** `ChatDoorRecord`, `Door`, `chatDoorLine`,
   `parseChatDoorLine`, `parseChatDoors`. Tests first.
2. **`chatDoorsFile.ts` — the writer**, the same shape as
   [chatUsageFile.ts:90](../src_vs_code/src/chatUsageFile.ts#L90): one queue, one flush on
   deactivate, `readChatDoors` for the reader.
3. **The five commands record themselves** in [extension.ts:193](../src_vs_code/src/extension.ts#L193),
   each before it does anything that can refuse.
4. **`chatSpendRows.ts` — pure.** Grouping by vendor+model, the window, the all-time money, both
   counts, the unpriced count, and the section totals. No `vscode`.
5. **`panelView.ts` — the section.** The Chat rows, the two headings, the rule, the totals.
6. **`panelProvider.ts` / `roundsLog.ts` — the wiring.** Chat turns, door records and the price
   lookup through `usageTabHtml` into `usageRegion`, on the same stamped cache the chat ledger uses.
7. **The help, in all five languages, in the same commit.**
8. **CHANGELOG, version, package, install.**

## Test plan

Pure, in `node:test`, beside the modules they cover:

- a door line round-trips: written, parsed, same fields; a torn or foreign line is dropped, not fatal;
- **the turn ledger is untouched**: a door file and a usage file are read by their own parsers, and no
  door record can reach a caller that expects a turn;
- **each of the five commands writes exactly one door record**, and writes it even when the door then
  refuses (no CLI, nothing captured) — the invocation is the thing being counted;
- grouping puts two models of one vendor in two rows, and one model of two vendors in two;
- **Asked** counts `take` and `add` and nothing else; **Opened** counts all five;
- a door with no provider or model lands in the named bucket rather than inventing a row, and the
  section totals still equal the sum of every record;
- the window bounds tokens, cost and both counters — and does NOT bound the all-time money;
- a vendor that billed and a vendor that did not: one exact total, one tilde, never summed silently;
  a model with no rate at all is counted as unpriced and shows no money;
- an integration fixture: a mixed pair of ledgers rendered through the usage tab, asserting both
  headings, the rule, both section totals and one chat row.


## What shipped differently

**Two reversals came from the plan round itself** and are written into the sections above where they happened: the door records went into a file of their own rather than sharing `chat-usage.jsonl`, and the recording moved up from `deliverPassage` to the five command handlers.

**A third changed during the build, and a test found it.** A row was to be drawn when the pair had anything in the window **or any all-time money** - which made a row appear or not depending on whether its model happened to have a rate. The rule is now the one the reviewer cards above it follow: a row is drawn for what happened IN THE WINDOW, and the all-time column is context for a pair that is already on the page.

**`jsonlLedger.ts` was not in the build order.** The door writer wanted the turn writer's queue, its drain and its read verbatim; per the reuse rule that half was extracted rather than copied, so both chat ledgers share one chain and `flushChatUsage` in `deactivate` drains them together.

**`within` in `usage.ts` became generic** over anything carrying a `utc`, so what "this week" means is one answer for both halves of the page rather than two that can drift.

**One thing found and deliberately NOT fixed here:** `usageRegion` returns the Team-server blocks and the me/company scope control only on the branch where this machine has NO recorded usage - so they disappear as soon as it has any. That predates this change, it is untouched by it, and it is reported rather than repaired inside a change nobody asked to include it in.

## Definition of Done

- [x] The spending tab shows Reviewers and Chat as two named sections with a rule between them, each
      with its own tokens-and-money total.
- [x] A chat row names vendor, model, tokens, both prices, the windowed cost, the all-time money, and
      the two counts.
- [x] The two counts come from a door record that did not exist before, and every one of the five
      doors writes one before it can refuse.
- [x] `chat-usage.jsonl` is read by exactly the code that read it before, and lines already on disk
      mean what they meant.
- [x] `npm run typecheck` clean, whole suite green, and every new behaviour has a test that was seen
      to fail without it.
- [x] Help updated in all five languages in the same commit as the English.
- [x] The gate: the plan round is resolved (done, 2026-09-12), then a code round, resolved.
- [x] A PR, and the plan promoted to `research/` when it ships.
