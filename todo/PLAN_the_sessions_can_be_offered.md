# PLAN — the sessions can be offered, when no name reaches them

> Status: **plan only, nothing implemented yet. Deferred by operator decision, 2026-09-16.** Extracted
> from [PLAN_the_tab_finds_its_session.md](../research/PLAN_the_tab_finds_its_session.md) when its epics A and C
> shipped: after the lookup was fixed this is needed for **5 sessions in 101**, and the operator chose
> to wait for the refusal to be met again on a live session rather than build for it now.
>
> Scope: `src_vs_code/src/claudeSessions.ts` (a reading half to extract), a new pure rows module, a
> new thin `vscode` widget, and the Asked region in `chatPage.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## Why it still exists

The lookup now answers under every name a session has worn and, for a conversation that was pinned
once, by the session id itself. What it cannot do is find a session that has **no name on disk at
all** — Claude Code names such a tab from the first prompt and writes that name nowhere. Measured on
the operator's own folder, 2026-09-16: **5 of 101** sessions carry no title row of any kind.

For those, and for the case where two sessions genuinely wear one name, the honest answer is the one
this product gives everywhere else: refuse to guess, and ask the person. The refusal is a dead end
today — text in a region, with nothing to press.

## The trigger to build it

Any of these brings it back:

- the refusal is met again on a session that really exists (the id path and the two name tiers having
  both missed it);
- the share of sessions with no title row rises — it is worth re-measuring before building, since the
  three that had none on 2026-09-16 were minutes old and gained one later;
- a second person uses this on a machine whose folder is not the one all these measurements come from.

## What it would be

Written up in full in the parent plan's epic B, and summarised here so this file stands alone.

### B1 — the sessions of a folder can be listed, every one with a label

- `sessionsIn(home, cwd, caseBlind, budget)` → `SessionList {cards, cut}`. The reading half of
  `claudeSessions.ts` moves to `claudeSessionFiles.ts` first: the file is past 700 lines and the 800
  ceiling is the repository's own.
- A card carries `file`, `sessionId`, `names`, `at` and `opening` — the first thing the person said,
  cut short. That is what gives a titleless session a row a human can choose; the enumerating pass
  already parses every line, so it costs nothing. **It is a LABEL, never a match**: deriving a name
  and matching on it stays refused, for the reason the parent plan records.
- Telling "the folder answered and nothing is called this" from "it would not say" is DONE — epic C
  gave `Found.none` a typed `why`, and the door opens for `unmatched` and for `several`, never for
  `unreadable`. Nothing more is needed here than to read it.
- Rows are decided in a pure module: label, age, folder when there is more than one root, the id's
  first eight characters as the tie-break, former names in the detail so typing an old name finds it,
  a notice row for a folder that would not answer and one for a list that was cut.
- `sessionPrefill` returns the de-ellipsised name **only when it would leave rows standing** — a
  prefill that filters everything away is worse than none. `unshortened` already exists for it.

### B2 — the refusal offers the choice

- The `asked` message gains `door: 'choose' | 'none'`; the Asked region gains one button, unhidden
  only for the two refusals a choice can answer.
- `createQuickPick` (not `showQuickPick`, which has no `value` to prefill), `matchOnDescription`,
  `matchOnDetail`, the title naming the conversation. No new manifest command: one would pull in a
  help article and five translations for a control reachable from one sentence.
- A row whose file has gone since the listing is refused at the press and removed; Escape writes
  nothing and leaves the refusal and its door on screen; a notice row cannot be chosen.

### B3 — the choice is kept

- `adoptFound` with the folder the card came from — `reorigin` files a claude source under the first
  root, which is why `pinSession` bypasses it. That helper now exists (epic A).
- The write is AWAITED and its real outcome reported: a store that was busy is said to be *not yet
  written* rather than looking kept.
- An end-to-end scenario test: the shipped page, the press, the choice, the save, a second store
  reading it back, the id resolving to the file — and the record containing no path under the home
  directory. This was accepted at the parent plan's gate round and is the piece that is genuinely
  missing from what shipped.

## The boundary with [the lookup plan](../research/PLAN_the_tab_finds_its_session.md)

| Item | Built by | The other plan's part |
|---|---|---|
| Reading a session's names (`namesOf`, `collectNames`, `namedAmong`) | the lookup plan, epic A — shipped | uses it; the picker's rows show `latest` and make `former` findable by typing |
| Resolving by session id (`sessionFileOf`, `isSessionId`, the containment checks) | the lookup plan, epic A — shipped | uses it to re-check a chosen session at the press, and after a reload |
| `adoptFound` — the one road that writes a session onto a record | the lookup plan, epic A — shipped | calls it with the folder the chosen card came from |
| The scan budget (`ScanBudget`, `ScanCut`, the size cap) | the lookup plan, epic A — shipped | passes it to `sessionsIn` and renders the cut as a notice row |
| `Found.why` (`unmatched` against `unreadable`) | the lookup plan, epic C — shipped | the picker's door opens for `unmatched` and for `several`, never for `unreadable` |
| The two *go to* sentences and the narrowed-list filter | the lookup plan, epic C — shipped | nothing; they are independent of the picker |
| **Listing a folder's sessions** (`sessionsIn`, `SessionCard`, `claudeSessionFiles.ts`) | **this plan — NOT BUILT** | the lookup plan only MATCHES; it never enumerates |
| **The rows, the prefill and the QuickPick** | **this plan — NOT BUILT** | the lookup plan has none |
| **The door in the Asked region, and keeping the choice** | **this plan — NOT BUILT** | the lookup plan pins in memory and writes only what a complete walk found |
| **The end-to-end scenario test** of page → choice → reload | **this plan — NOT BUILT** | the lookup plan's tests stop at the module boundary, which is what leaves that gap |

**Order:** the lookup plan first, and it has shipped. Everything the picker needs from it — the names, the id
path, the budget, `adoptFound`, `Found.why` — exists now. This plan adds only enumeration and a way to
ask; it changes nothing the lookup plan decided.

**Disjoint:** the table is complete, not a sample. Nothing else is shared between the two.

## How it would be verified

```bash
cd src_vs_code
npm ci                 # first time in a fresh worktree
npm test               # clean, tsc, then node --test over every out/test/*.test.js
npm run typecheck      # tsc -p ./ --noEmit — read the EXIT STATUS, tsc emits despite errors
node --test out/test/sessionPicker.test.js        # after npm run compile, for one file
```

The suite is `node:test`; there is no `dotnet test` in this half and no extension-host harness, so
the QuickPick itself is asserted against its own source and the page is RUN through
`bundledPage.test.ts`.

## Definition of Done

- [ ] The trigger above actually fired, and is written down here with the date and what was seen.
- [ ] Every rule has a RED test first, watched failing with the real symptom.
- [ ] The page half is tested by RUNNING the page (`bundledPage.test.ts`), never by reading its source.
- [ ] `research/module_extension.md` and `research/module_tests.md` updated; `architecture.md` too,
      since the door adds a page→host message.
- [ ] The coai gate: a plan round to `proceed`, then a code round.
