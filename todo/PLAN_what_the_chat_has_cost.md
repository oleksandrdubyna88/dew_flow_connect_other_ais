# PLAN — what the chat has cost, beside what the reviewers cost

> Status: **plan only, nothing implemented yet.** Scope: the spending tab
> (`src/roundsLog.ts`, `src/panelView.ts`, `src/panelProvider.ts`), the chat usage ledger
> (`src/chatUsage.ts`, `src/chatUsageFile.ts`), the five doors in `src/extension.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_usage_that_compares.md](PLAN_usage_that_compares.md),
> [PLAN_team_usage_by_person.md](PLAN_team_usage_by_person.md).

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
| **All time** | the same sum over the WHOLE file, deliberately outside the window |
| **Asked** | invocations of `take the question` + `add the question` |
| **Opened** | invocations of every door |

and a section total of its own: tokens and money, in the shape the Reviewers total already uses.

**4. A door record**, which is the new field. One line per invocation, in the file that already
exists:

```json
{"kind":"door","utc":"2026-09-12T10:00:00.000Z","door":"take","provider":"antigravity","model":"gemini-3.8-flash"}
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

**The same file, not a second one.** `chat-usage.jsonl` is already read whole, cached on a
`size:mtime` stamp ([panelProvider.ts:434](../src_vs_code/src/panelProvider.ts#L434)) and appended
through one writer with its own flush. A second file doubles the reader, the cache and the flush on
shutdown for four fields. The cost is that the parser must route two shapes — paid for by a
discriminator that DEFAULTS to the old shape, so every line already on disk goes on meaning what it
meant.

**A line with no `kind` is a turn.** Old lines carry no discriminator and must not become unreadable
or, worse, readable as something else. This is the compatibility rule the whole change rests on and
it gets its own test.

**Vendor AND model, where the reviewer cards show vendor alone.** A chat switches model
mid-conversation — that is the feature the picker exists for — and a price is per model. A row keyed
on the vendor could not show a price at all without averaging two rates into a number nobody is
charged.

**All time sits beside a windowed row on purpose.** It was asked for in those words, and it is the
number behind the decision the running total in the tab was built for: carry on with this
conversation, or start a fresh one. It costs nothing extra — the whole file is already in memory.

**The window buttons stay shared.** One control over both sections: *"день месяц год оставляем одни
и теже"*.

## Build order

1. **`chatUsage.ts` — the record.** `ChatDoorRecord`, `chatDoorLine`, the discriminated parse, and
   `parseChatUsage` returning both kinds. Tests first, including a line with no `kind`.
2. **`chatUsageFile.ts` — the writer.** `recordChatDoor`, through the same queue and flush as
   `recordChatTurn` ([chatUsageFile.ts:90](../src_vs_code/src/chatUsageFile.ts#L90)).
3. **The doors record themselves.** `deliverPassage`
   ([chatCommand.ts:2226](../src_vs_code/src/chatCommand.ts#L2226)) is the funnel every door already
   passes through and it holds `ready.providerId`; it takes the door as an argument rather than
   guessing it from `send`/`append`, because two of the five are indistinguishable that way.
4. **A pure module for the rows** — `chatSpendRows.ts`: the grouping, the two counters, the window
   and the all-time column. No `vscode`, so the arithmetic is a test rather than a squint at a panel.
5. **`panelView.ts` — the section.** The Chat rows, the two headings, the rule, the per-section
   totals.
6. **`panelProvider.ts` / `roundsLog.ts` — the wiring.** The chat records and the price lookup
   through `usageTabHtml` into `usageRegion`.
7. **The help, in all five languages, in the same commit.** The spending tab's section of
   `helpContent.ts` and its four translations — the rule that exists because a stale translation is
   invisible.
8. **CHANGELOG, version, package, install.**

## Test plan

Pure, in `node:test`, beside the modules they cover:

- a door line round-trips: written, parsed, same fields;
- **a line with no `kind` parses as a turn** — the compatibility rule, red if the discriminator is
  required;
- a door line is never counted as a turn: no phantom zero-token run in any total;
- grouping puts two models of one vendor in two rows, and one model of two vendors in two;
- **Asked** counts `take` and `add` and nothing else; **Opened** counts all five;
- the window bounds tokens, cost and both counters — and does NOT bound the all-time column;
- a vendor that billed and a vendor that did not: one exact total, one tilde, never summed silently;
- the rendered section carries both headings, the rule, and a total line each (against the same
  bundled-page discipline the chat page is checked with).

## Definition of Done

- [ ] The spending tab shows Reviewers and Chat as two named sections with a rule between them, each
      with its own tokens-and-money total.
- [ ] A chat row names vendor, model, tokens, both prices, the windowed cost, the all-time cost, and
      the two counts.
- [ ] The two counts come from a door record that did not exist before, and every one of the five
      doors writes one.
- [ ] Lines already on disk read exactly as they did.
- [ ] `npm run typecheck` clean, whole suite green, and every new behaviour has a test that was seen
      to fail without it.
- [ ] Help updated in all five languages in the same commit as the English.
- [ ] The gate: a plan round to `proceed`, then a code round, both resolved.
- [ ] A PR, and the plan promoted to `research/` when it ships.
