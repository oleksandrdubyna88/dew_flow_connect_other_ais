# PLAN — the rounds log is a page with a table, not a markdown file

> Status: **plan only, nothing implemented yet.** Scope: `src_vs_code/src/{extension.ts, roundsLog.ts (new),
> roundsLogPanel.ts (new), rounds.ts}`, their tests, the help entry for *Show review rounds*, CHANGELOG.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_rounds_collapse_and_vendor_colour.md](../research/PLAN_rounds_collapse_and_vendor_colour.md) — the
> sidebar history this replaces.

## The symptom, reported by the operator on 2026-09-05

*"Переделай на нормальный веб-вью, чтобы там были таблицы, фильтры, сортировки, поиск. Пользоваться тяжело
сейчас."* — said over a screenshot of `rounds.md` open in the editor: fifty-three lines of markdown tables,
one per session, each row a single unwrapped line that runs off the right edge, no way to sort by time
across sessions, no way to find "every round on branch X" or "every round where local failed", and every
finished round from every repository on the machine in one flat scroll.

And the second half of the same ruling: **the sidebar shows only what is running.** The 72-hour history it
used to carry — disclosures, an open-set policy, a document-level toggle listener — is what flickered (a
list replaced through innerHTML every five seconds fires `toggle` for every open card exactly as a click
does, and the provider answered each with another patch). That half shipped the same day
(`activeRounds.test.ts`). This plan is the other half: where the history went.

## What exists today, verified

| What | Where |
|---|---|
| The command | `coai.showRounds` → `showRounds(watcher)` — [extension.ts:76](../src_vs_code/src/extension.ts#L76), [:217](../src_vs_code/src/extension.ts#L217) |
| The file it writes | `writeRoundsFile` renders `renderEscalations(...) + renderRounds(sessions)` to `<dataDir>/rounds.md` and opens it as a text document — [extension.ts:231-246](../src_vs_code/src/extension.ts#L231) |
| The rewrite | `refreshRoundsFile` is called from the watcher's `onChanged` **every five seconds** and rewrites the file whenever its tab is open — [extension.ts:42](../src_vs_code/src/extension.ts#L42), [:260](../src_vs_code/src/extension.ts#L260); the tick is [escalationWatcher.ts:55](../src_vs_code/src/escalationWatcher.ts#L55) |
| The data | `SessionFile` (`state.{sessionId, repoPath, branch, stage, awaitingResolve}`, `rounds[]`) and `RoundRecord` (`stage, number, verdict, gatingCount, reviewers, completedUtc, status?, startedUtc?, reviewerStates?[], subject?, tokensIn?, tokensOut?, costUsd?`) — [rounds.ts:9-56](../src_vs_code/src/rounds.ts#L9) |
| A webview panel to copy | `helpPanel.ts` — a singleton `createWebviewPanel`, `reveal()` when it already exists, `onDidReceiveMessage` for its buttons — [helpPanel.ts:48-69](../src_vs_code/src/helpPanel.ts#L48) |
| The reviewer line renderer | `reviewerLines(round)` / `reviewerRows(round)` in `rounds.ts` / `panelView.ts` — reused, not rewritten |
| The vendor colour | `vendorColour(name)` — [vendorColour.ts](../src_vs_code/src/vendorColour.ts) — reused |

## What must be true when this is done

1. *Show review rounds* opens a **page** (a `WebviewPanel`, one per window, revealed if already open)
   with one table over **every round of every session** in the data directory.
2. Columns: when (started), repository, branch, stage, round, subject, status, verdict, gating, findings,
   duration, tokens in / out, cost, reviewers (answered / total). Every column **sorts** on click, both
   directions, and the sort survives a refresh.
3. **Filters** for repository, branch, stage, status and verdict (each a select filled from the data), and a
   **search** box matching subject, branch, repository and reviewer names — all client-side, no
   round-trip to the extension host.
4. A row **expands in the page** to its reviewers (with duration, tokens, note — `reviewerLines`) and, when
   the session file carries them, its findings. Expansion is page state only: it is never posted to the
   provider, so the loop that this plan's sibling removed cannot be rebuilt here.
5. **Live**: the watcher tick posts the rows as JSON **only when they changed** (a hash of the serialised
   rows, kept in the provider); the page re-renders the table and **keeps** its sort, filters, search text,
   scroll and expanded rows.
6. Open questions (escalations) stay at the top of the page with their *Answer…* button, exactly as
   `rounds.md` carried them.
7. `rounds.md` is no longer written. The file writer, the tab check and the five-second rewrite go.
8. Nothing out of a session file reaches the page unescaped; the page's script has no backticks (it
   lives inside a template literal) and every sort/filter/search predicate is a pure function under test.

## Constraints

- **Pure module + glue module**, as the sidebar already does: `roundsLog.ts` holds the row model
  (`rowsFrom(sessions)`), the predicates (`sortRows`, `matches(row, filters, search)`), and the HTML;
  `roundsLogPanel.ts` holds `vscode`. The tests never import `vscode`.
- One renderer for a reviewer line — `reviewerLines` from `rounds.ts` — not a second one.
- Client-side state lives in the page; the provider only ever pushes data. There is no message from the
  page to the provider except *Answer…* (already a command) and *copy*.
- No new dependency. A table of a few hundred rows sorts in the page in under a millisecond; no grid
  library, no framework.
- Theme colours only (`--vscode-*`); the vendor word is coloured through `vendorColour`.

## Epics

### Epic 1 — the page (the deliverable)

1. **RED**: `roundsLog.test.ts` — `rowsFrom` flattens two sessions into rows with repository, branch and
   the round's fields, newest first; `sortRows` by every column both ways; `matches` for each filter and
   for search across subject/branch/repository/reviewers; a `<` in a subject is escaped; the page script
   contains no backtick.
2. `roundsLog.ts`: the row model, the predicates, `roundsLogHtml(rows, questions, nonce)`.
3. `roundsLogPanel.ts`: the singleton panel; `show(rows, questions)`; `update(rows, questions)` posting
   `{type:'rows'}` only when the hash moved; *Answer…* routed to `coai.answerQuestion` semantics via the
   existing `answerCommand`.
4. `extension.ts`: `coai.showRounds` opens the panel; the watcher's `onChanged` calls `panel.update(...)`
   instead of `refreshRoundsFile`.
5. The page script: render table from rows; sort on header click; filters and search from inputs; row
   expansion; state kept across re-renders.

### Epic 2 — retire `rounds.md`

1. **RED**: a test that `extension.ts` no longer exports/contains `writeRoundsFile` / `refreshRoundsFile`
   (string-level, since the module imports `vscode`), and that `roundsFile()` is gone.
2. Delete `showRounds` (file version), `writeRoundsFile`, `refreshRoundsFile`, `roundsViewIsOpen`,
   `renderRounds`/`renderEscalations` markdown renderers if nothing else uses them.
3. Help entry for *Show review rounds* describes the page; CHANGELOG.

### Epic 3 — findings in the row (optional, if the file carries them)

The server's session file has `pending` (unresolved findings) and the round record may carry resolved
ones. If `parseSession` can expose them without a server change, the expanded row lists them
(severity, title, file:line, who found it); otherwise this epic is extracted into its own plan after
Epic 1 ships, and the row says "findings are in the session file" rather than pretending.

## Test plan

`npm test` (node:test): the pure module in full; page-script assertions by string (no backticks, the
sort/filter handlers present, `escapeHtml` on every field). Manual: open the page, sort by duration,
filter branch, search a vendor name, expand a row, let a round run and watch the row advance without the
sort resetting.

## Definition of Done

- [ ] *Show review rounds* opens a page with a sortable, filterable, searchable table over every round.
- [ ] Rows expand in the page; nothing about expansion is posted to the provider.
- [ ] The tick pushes rows only when they changed; sort/filter/search/scroll survive a push.
- [ ] `rounds.md` is not written any more, and nothing rewrites anything every five seconds.
- [ ] `npm test` passes; help and CHANGELOG updated; `module_extension.md` describes the page; this
      plan promoted.
