# PLAN — the Settings tabs use the width of a wide editor

> Status: **IMPLEMENTED, 2026-10-09.** (branch `fix/settings-two-columns`; no release yet). Scope: the Settings page's
> stylesheet (`catalogCss.ts`), one new helper (`cardColumns.ts`), the builders of Reviews (Stages, Roles & prompts,
> Prompts per round, The gate, Commands), Security lane and Chat, one test file, `module_extension.md`, the changelog.
>
> Deviations: Roles & prompts was first laid out with its ROLES in the columns; rendered, Plan stage (one role) stayed
> as narrow as before, so its role's PROMPTS flow instead and the role spans the page. Security lane's two numbers went
> into the columns too, because a full-width number input reads as a bar. The plan round asked that every card family be
> counted against its own source as well as its parent checked (a family drawing nothing passed the parent check), and
> that Models' `.catalog .block` stop reaching Chat's model blocks — both done, each with a test seen red first. Open
> tail: none.
>
> Related docs: [module_extension.md](module_extension.md) (*The Settings tabs lay out in two columns on a wide editor*),
> [architecture.md](architecture.md).

## 1. The goal

The operator, 2026-10-09, on extension 0.65.0: on the Settings page, **Models** lays its cards out in two columns on a
wide editor and in one on a narrow one. Every other tab was one ~760 px column with the right half of the screen empty.
The same responsive layout — the same mechanism, reused, not a second one — was asked for on:

- **Reviews**, every sub-tab except **Limits**: Stages, Roles & prompts, Prompts per round, The gate, Commands;
- **Security lane** — the prompt cards and the settings above them;
- **Chat** — the models a chat opens on, Sending, Prompt presets.

Consultants was not named.

## 2. The design

- **One class, one rule.** Models' grid (`.catalog .cards`, one column, two from `min-width: 1100px`) becomes the page's
  `card-columns`: `CARD_COLUMNS` and `cardColumns(cards)` at
  [cardColumns.ts:11](../src_vs_code/src/cardColumns.ts) and [cardColumns.ts:18](../src_vs_code/src/cardColumns.ts),
  which wraps drawn cards and draws nothing for none. The rule is `COLUMNS` at
  [catalogCss.ts:183](../src_vs_code/src/catalogCss.ts) (base at :184, two columns at :186), appended last in
  `CATALOG_CSS` ([catalogCss.ts:195](../src_vs_code/src/catalogCss.ts)). Models' container carries both classes
  ([modelsTab.ts:98](../src_vs_code/src/modelsTab.ts)).
- **The 760 px column lifts only where it must.** In the same media query `.catalog .moved:has(.card-columns)` drops the
  cap ([catalogCss.ts:187](../src_vs_code/src/catalogCss.ts)); a narrow editor keeps the column it had. The cells'
  single-column margins are cleared and a fieldset gets `min-width: 0` ([catalogCss.ts:190](../src_vs_code/src/catalogCss.ts)).
- **What flows, by place** — the repeated cards; an intro, a heading or a single setting row spans both columns:
  - Stages and Prompts per round: each stage's role boxes ([panelView.ts:2275](../src_vs_code/src/panelView.ts)).
  - Roles & prompts: each role's prompts ([rolesBlocks.ts:304](../src_vs_code/src/rolesBlocks.ts)).
  - The gate: its fields ([catalogSections.ts:77](../src_vs_code/src/catalogSections.ts)).
  - Commands: Yours and Shipped, each list ([commandsEmbed.ts:37](../src_vs_code/src/commandsEmbed.ts),
    [commandsEmbed.ts:41](../src_vs_code/src/commandsEmbed.ts)).
  - Security lane: the two numbers, the prompt cards and the pairs
    ([securityLaneView.ts:53](../src_vs_code/src/securityLaneView.ts), :62, :68).
  - Chat: the models ([chatTabEmbed.ts:77](../src_vs_code/src/chatTabEmbed.ts)), the sending fields (:34), the presets
    (:37); a stranded choice and a conflict's table span.
- **Not touched:** Limits (five numbers, one a row), Consultants (single settings, no repeated card), Setup.
- **The leak found on the way.** Models' card-block rule was the page-wide `.catalog .block`, and Chat draws its models
  as `.block` too, so they wore a card block's top line and padding. Narrowed to `.catalog .card .block`
  ([catalogCss.ts:158](../src_vs_code/src/catalogCss.ts)).

## 3. Build order, as it happened

1. RED test: each place's cards must sit in the shared class; the stylesheet must hold one two-column rule on it.
2. `cardColumns.ts`; Models' rule moved to `COLUMNS`; Models' container given the class.
3. Each builder wrapped (Stages/Prompts, Roles, Gate, Commands, Security, Chat); GREEN.
4. Rendered every tab in headless Edge at 1500 px and 900 px (`scripts/render-page.mjs`): Roles moved from role level to
   prompt level, Security's two numbers into the columns.
5. Docs: `module_extension.md`, the changelog's unreleased section.
6. Plan round: every family counted against its source; `.catalog .block` scoped to the card, RED first; this record.

## 4. Test plan

[theSettingsTabsFlowInTwoColumns.test.ts](../src_vs_code/src/test/theSettingsTabsFlowInTwoColumns.test.ts) runs the page
(`catalogHtml` → `pageTree` → `runPageHtml`) on each place:

- every family — Stages role boxes, Prompts per round role boxes, Roles & prompts prompt cards, The gate fields,
  Commands Yours and Shipped, Security lane numbers, prompt cards and pairs, Chat models, sending fields and presets —
  is drawn exactly as many times as its source of truth says (the state, the shipped catalogue, or the builder's own
  output), and each card's PARENT is `card-columns` (:152);
- Limits draws no column grid (:169); Models' cards sit in the same class (:176);
- the stylesheet holds exactly one two-column rule, on the shared class at 1100 px, with the cap lifted there (:200);
- no top-border rule reaches a Chat model block, while one still reaches a Models card block (:242).

Observed: RED before each change, GREEN after, and break-it runs — Roles' prompts unwrapped (*a role's prompt on
reviews/roles sits in `<details class="role role-plan">`*), Chat's presets drawn as none (*Chat prompt presets on chat:
the page drew 0, its source has 2*), the block rule put back (*a Models card rule reaches the Chat tab's model block*).

## 5. Definition of Done

- [x] One class and one rule carry the columns for Models and every named place; no second grid.
- [x] Reviews (all but Limits), Security lane and Chat flow their repeated cards; Limits, Consultants and Setup unchanged.
- [x] A narrow editor keeps the 760 px column; a wide one lifts it only where a place has columns.
- [x] Every family is counted against its source and its parent checked, by running the page.
- [x] Models' card-block rule no longer reaches Chat.
- [x] Rendered in a real Chromium at both widths.
- [x] `module_extension.md` and the changelog updated; this record in `research/` with a README row.
- [x] Full `npm test` and `npx eslint src` green; `plan-lifecycle.mjs` clean.
