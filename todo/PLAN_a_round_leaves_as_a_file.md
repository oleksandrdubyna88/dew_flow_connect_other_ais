# PLAN — a round leaves the page as a file, and the log says what the deciding cost

> Status: **in progress, 2026-09-14 — stories A1, A2, B1, B2 and C1 shipped; C2 open.** Scope: the rounds-log page
> (`src_vs_code/src/roundsLog*.ts`), one new one-shot mode and one widened DTO on the server
> (`src_mcp/src/Store/RoundsQuery.cs`, `src_mcp/src/Program.cs`).
>
> Related docs: [research/architecture.md](../research/architecture.md),
> [research/module_server.md](../research/module_server.md),
> [research/PLAN_local_db.md](../research/PLAN_local_db.md) (the writing half, shipped).

## The three asks, in the operator's words

1. *«нужно на каждую запись доб кнопку експорт — выгружать в цсв, теже данные, что видны в таблице,
   плюс все замечания (пометки принято или нет тоже нужны)»* — an export button on every row,
   producing a CSV of that row's data plus every finding it raised **with its accept/decline mark**.
2. *«так же нужно сделать галочки напротив каждой записи, что б можно было делать балк екпорт»* —
   a checkbox per row, so several rounds export as one file.
3. *«и заодно сразу тут нужно писать 2 времени: сколько ушло на анализ (то что сечас) плюс сколько
   ушло на оценку»* — the **Took** column shows two numbers, not one.

## The goal, stated so a reviewer can read a diff against it

Today the rounds log is a screen and only a screen. What a review found, and what this repository's
author decided about each finding, can be looked at and cannot be taken anywhere — there is no
export, no download and no clipboard path on this page, and the markdown export that predated it was
deleted when the page replaced it (`src_vs_code/src/roundsLog.ts:17-33`). And the **Took** column
answers only half the question it looks like it answers: it measures the reviewers running, while the
part that actually costs a person their afternoon — reading the findings and deciding on each one —
is measured nowhere, although the database has stamped it since the day `resolve` was written.

## What already exists — verified, with references

### The page, and what it holds

- One pure module renders the whole document: `roundsLogHtml`, `src_vs_code/src/roundsLog.ts:973-1553`.
  `LogRow` (`roundsLog.ts:36-140`) is the row type; `COLUMNS` (`roundsLog.ts:818-835`) is the
  sixteen-column header; the `<tr>` template is `roundsLog.ts:1291-1308`.
- The rows are serialised into the page as `var ROWS = ${jsonForScript(rows)}` (`roundsLog.ts:1152`),
  and **sorting, filtering, searching, the date range, paging and expansion are page state that never
  returns to the host** — stated as a decision at `roundsLogPanel.ts:42-47`.
- Six pure functions are injected **by their source text** (`roundsLog.ts:1158-1162, 1181`) and must
  therefore reference nothing outside their own parameters and use no template literal
  (`roundsLog.ts:614-618`). `src_vs_code/src/test/bundledPage.test.ts:27` lists them and
  `:133-142` fails if a minifier can rename one — this shipped broken twice (0.29.10, 0.29.12).
- Anything `roundsLog.ts` imports lands in the **webview** bundle. `roundsDbRead.ts:4-11` exists as a
  separate module for exactly this reason: a spawn in the same file dragged `node:child_process` into
  the page and the bundled-page test caught it with `require is not defined`.

### The findings, and why they are not on the page

- The list stopped carrying findings — measured at **3.78 MB of a 3.83 MB payload**, for rounds nobody
  had opened (`roundsDb.ts:46-52`). They are fetched **one round at a time, on expand**, by spawning
  the server: `readFindings` → `--findings --session … --stage … --number …`
  (`roundsDbRead.ts:110-131`), answering `loaded | absent | failed` so that an empty list is never
  mistaken for "this round was clean" (`roundsDbRead.ts:86-95`).
- Host side: `PanelProvider.roundFindings` (`panelProvider.ts:392-398`), reached through the
  `onFindings` hook (`extension.ts:90-101`).
- Server side: `Startup.Findings` → `FindingsJson` (`Program.cs:122, 155-156, 288`), which reads named
  flags through `Flags(args)` and answers `LoggedRoundFindings` (`RoundsQuery.cs:69, 134-148`).

### The decision timestamp — written since day one, read by nothing

- `findings.resolved_utc` is a real column (`src_mcp/src/Store/Schema.cs:92`) and `resolve` writes it:
  `RoundsDb.RecordDecisions` stamps one `DateTime.UtcNow` for the whole batch
  (`RoundsDb.cs:131-155`, the stamp at `:134`).
- **Nothing reads it.** It appears in no query, no DTO and no session file — `LoggedFinding`
  (`RoundsQuery.cs:6-20`) and `LoggedRound` (`RoundsQuery.cs:23-52`) both omit it, and
  `RoundRecord` on the extension side (`rounds.ts:45-62`) has no resolve time at all.
- Because the whole batch shares one stamp, **a round's decision time is one subtraction**:
  `MAX(findings.resolved_utc) − rounds.completed_utc`.

### What the round-level columns actually come from

`LoggedRound` carries **none** of `verdict, status, subject, gating, completedUtc, tokensIn,
tokensOut, costUsd` (`RoundsQuery.cs:23-52`). The page recovers those from the **session files**
(`rounds.ts:45-62`, read by `readSessions`, `extension.ts:798-812`) and joins the two halves in
`rowsFrom`. **An export therefore reads both sources**, exactly as the page does — which is why the
CSV is built from `LogRow`, the type where the join has already happened, and not from the database.

### Cost is three numbers, and two of them are hedged

`cost3` renders *in / out / total* (`roundsLog.ts:692-702`); `~` means priced from a public list
rather than billed (`costIsEstimate`) and `+` means at least one reviewer's model had no listed price
so the total is a **floor** (`costPartial`, `roundsLog.ts:71-74`). These are qualifiers, not
decoration: dropping them turns a hedged figure into a claim.

## Decisions taken with the operator, 2026-09-13

| Question | Answer |
|---|---|
| What is the second time? | **Decision time** — from the round finishing to the last `resolve`. |
| CSV shape | **One row per finding**, the round's columns repeated on each; a round that found nothing still gets one row. |
| Where bulk findings come from | **A new server batch mode**, one spawn for many rounds, falling back to the existing per-round read on an older binary. |
| Where the file goes | **A save dialog** — the person picks the path. |

## The plan round, 2026-09-13 — `good_enough`, all 3 reviewers, 14 findings

Session `84512dac`. 11 gating against a threshold of 6 with one round budgeted, so `good_enough` is
the pass and the instruction is to apply what is true. **13 accepted, 1 rejected.** What changed:

- **The fallback in phase 4 did not exist** (local, Blocking). `--findings --keys-file` on an older
  binary is not an unknown *mode*: `Classify` sees `--findings`, `FindingsJson` finds no `--session`,
  and it exits **69 — "no such round in the rounds database — its findings were never recorded"**
  (`Program.cs:288-316`). The client maps 69 to `absent`, so a bulk export against an old server
  would have declared every selected round empty rather than saying the server was too old. The batch
  is therefore its **own `args[0]`, `--findings-many`**, which an old `Classify` answers with exit
  **64** (`Program.cs:142-144`) — the one code the fallback can actually read. Verified in the source,
  not assumed.
- **`MAX()` over no rows is `NULL`, not `''`** (codex, Minor) — `COALESCE`, and a test for a round
  whose findings table is empty.
- **The formula guard was scoped to prose fields only** (codex, Major). Repository, branch, file,
  role and vendor are user- or model-controlled too. It applies to **every string cell**.
- **A failed findings read and a genuinely empty round were about to become the same CSV row**
  (codex, Major) — the exact lie `readFindings`'s three-state answer exists to prevent.
- **Cancellation and write failure had no defined state** (codex, local, gemini — three reviewers).
- **Nobody had said how the host gets the `LogRow` for a selected key** (gemini, Blocking).
- **`decideSeconds` semantics were undefined for a round resolved in two sittings** (gemini, Blocking).
- **Bulk had no progress, no cancel, no cap, and an unthrottled fallback** (gemini, two findings).

Rejected: *"ambiguity in CSV generation for zero-findings rounds"* (local, Minor) — it asks for a
requirement to be stated rather than for a behaviour to change, and it is already stated in the shape
decision, repeated in phase 2 and enforced by a named test.

**Reuse-first note on the save dialog.** `vscode.window.showSaveDialog` appears **nowhere** in this
extension today; the two existing ways of handing a person text are the clipboard
(`extension.ts:697, 720, 731`; `chatCommand.ts:2250`) and writing to a fixed path then opening it
(`rolesPanel.ts:406-431` + `chatCommand.ts:2266-2293`). Both were considered and neither answers the
requirement, which is a file **the person chooses the location of**: the clipboard produces no file,
and a fixed path cannot be chosen. `writeFileAtomically` (`atomicFile.ts:120-132`) is still reused for
the write itself; only the *path* comes from the dialog.

## Constraints this change has to respect

1. **The webview cannot write a file.** CSP is `default-src 'none'` (`roundsLog.ts:992`),
   `localResourceRoots: []` (`roundsLogPanel.ts:119`), and a VS Code webview honours no
   `<a download>`. **The CSV text is built by a pure module and written host-side.**
2. **The host does not know what the page is showing.** The selection must travel *in* the message;
   `this.latest.rows` is the unfiltered set.
3. **`logCommandOf` drops any `type:'command'` with an empty `id`** (`roundsLogMessages.ts:94-97`).
   A toolbar button with no `data-id` is a silently dead button.
4. **A column added outside `COLUMNS` breaks the detail row.** `COLUMN_COUNT` (`roundsLog.ts:1169`)
   is `COLUMNS.length` and is the `colspan` of the expanded row (`roundsLog.ts:1310`).
5. **A checkbox click must not expand the row.** The delegated handler ends with `tr[data-key]`
   (`roundsLog.ts:1338-1377`); a `[data-select]` branch goes **above** it and returns.
6. **A tick every five seconds re-pushes the rows.** The `rows` handler merges rather than replaces
   (`roundsLog.ts:1494-1513`); selection lives in `state` (`roundsLog.ts:1164`) so it survives, but
   **keys that no longer match any row must be pruned** or the "n selected" counter lies.
7. **`null` is not zero.** `findings`, `seconds`, `tokensIn`, `costTotalUsd` are `null` when nobody
   recorded them (`roundsLog.ts:58-91`). A CSV writing `0` fabricates a measurement; these cells stay
   empty.
8. **UTC in storage, local only at the edge** (`.agents/conventions/common/utc-timestamps.md`). The
   CSV carries `started_utc` as the ISO-8601 instant **and** a separate `started_local` column, so
   neither a spreadsheet nor a re-import has to guess.
9. **CSV injection — on EVERY string cell, not the prose ones.** RFC-4180 quoting throughout, and a
   leading `=`, `+`, `-`, `@`, tab or CR is prefixed with an apostrophe
   (`.agents/conventions/common/security.md`). The guard is applied by the ONE function that writes a
   cell, so no column can be forgotten: a repository called `=HYPERLINK(…)` or a branch called
   `@foo` is as executable as a model-written title, and `file`, `role` and the vendor list are
   model-controlled too. (Plan round, codex.) UTF-8 **with a BOM**, so Excel does not read Cyrillic
   as mojibake.
14. **A failed read is not an empty round — in the file as much as on the page.** `readFindings`
    answers `loaded | absent | failed` precisely so an empty list never reads as "this round was
    clean" (`roundsDbRead.ts:86-95`). The export carries that state through: a placeholder row is
    written **only** for a round confirmed to have no findings, and a round whose findings could not
    be read aborts the export with what failed named. An export that quietly writes blank finding
    cells for a timed-out read is the defect this codebase already paid for once. (Plan round, codex.)
10. **Adding a one-shot mode means amending `.agents/PROJECT.md:46-56`** — the paragraph that lists
    them is a non-negotiable, and a reviewer read it literally on 2026-09-07 and was right to.
11. **No new `contributes.command` and no new `coai.*` setting.** `helpCoverage.test.ts:173-199`
    turns either into a help article that must then exist in **five languages**. The buttons are
    page-local; the feature needs no command and no setting, so that chain is avoided by design.
12. **The page is English-only** (`roundsLog.ts:989`, `<html lang="en">`). New labels are English
    literals, like every other label on it.
13. **Durable status** (`.agents/conventions/common/durable-status.md`): a bulk export of many rounds
    is a process-spawning job. The button shows *Exporting…* while it runs, reports what it wrote,
    and must never stick on the in-flight state if it fails.

## Epics and stories, and the three things the split had to decide

The split was made separately (Fable, 2026-09-13) because deciding the seams shapes everything after
them. It changed three of this plan's own boundaries, and each change is kept here with its reason:

- **Phase 2 cannot be a story.** A tested `csvOf` with no caller is dead code waiting for a later
  story. The seam is **what the file claims**, not which layer: B1 exports the round's own columns
  end to end; B2 adds the findings and constraint 14.
- **Phase 4 had the fallback backwards.** The throttled per-round pool is built FIRST (C1) and works
  against every server shipped today; the batch mode (C2) is then a pure optimisation in front of a
  path that already ships and is already tested. That makes C2's skew claim literally true — an old
  server gets last release's behaviour — instead of a bolt-on nobody exercises.
- **"Both touch `RecordDecisions`" was wrong.** Phase 1 touches the READ side (`RoundsQuery`). Phase 0
  goes first for data integrity — it must precede B2, the first thing that publishes the marks off
  this machine — not for diff hygiene.

| | Story | Model |
|---|---|---|
| **A1** | An ordinal is not a position: a resolve out of order lands every mark on the finding it was about | expensive-to-be-wrong |
| **A2** | The Took column shows two times | ordinary |
| **B1** | Every row has an Export button, and it writes the row's own columns to a path the person chose — **SHIPPED 2026-09-14** | expensive-to-be-wrong |
| **B2** | The file carries every finding with its mark, and a failed read is never written as a clean round — **SHIPPED 2026-09-14** | expensive-to-be-wrong |
| **C1** | A checkbox on every row, and the selection exports as one file with progress you can cancel — **SHIPPED 2026-09-14** | ordinary |
| **C2** | A bulk export is one spawn, and an older server is still many | expensive-to-be-wrong |

Order: **A1 → A2 → B1 → B2 → C1 → C2**, every one a hard dependency of the next except A1/A2.

### The three decisions the split needed, taken rather than asked

Engineering choices, not product ones; recorded here so a reviewer reads them as decisions.

1. **How the ordinal reaches `RecordDecisions`** — it is passed. `RecordDecisions` takes
   `(int Ordinal, Decision)` pairs; `PanelService.Resolve` already holds the number in `dto.Finding`
   and carries it through `Finish`. Rejected: changing `Decision` itself (23 construction sites for a
   field only the projection wants) and `Pending.IndexOf(decision.Finding)` (leans on `Finding` record
   equality, which includes an `ImmutableArray` whose equality is the underlying array's reference).
   **Verified before choosing:** `session.Pending` and the list given to `RecordRound` are the same
   list in the same order (`PanelService.cs:1331, 1345`), so `dto.Finding` IS the stored ordinal.
2. **How much of a row the decoder validates** — `key`, the `dbKey` shape, and that `row` is an
   object. Not 30 fields: the ONE cell writer in `csvOf` already treats every value as untrusted
   (a string is coerced, a number must be finite or the cell is empty, anything else is empty), which
   is where constraint 9 puts the trust boundary anyway.
3. **The keys-file contract** — a JSON array of `{ "session", "stage", "number" }` written to the
   coai data directory and removed in a `finally`; answered with
   `{ "rounds": [{ "session", "stage", "number", "known", "findings": [] }] }`, so a key the server
   has never heard of comes back `known: false` rather than missing (constraint 14). More than
   `MaxLimit` (1000) keys is exit 64 with a note.

## Build order

### Phase 0 / story A1 — an ordinal is not a position — **SHIPPED 2026-09-13**

> Landed as `fix(store): an ordinal is not a position`. Its code round: `proceed`, all 12 reviewers,
> 23 findings, 11 accepted. What the round changed beyond the plan: `DecisionAt` moved out of
> `Core.Rounds` into `Store` (the state machine discards the number, so the core should not carry
> it); it lost its public constructor for two factories that read the finding out of `pending` by
> the number, so an inconsistent pair is now unconstructible; `resolve` refuses a finding index sent
> twice in one call, which used to close a one-finding round as one accepted AND one rejected; and
> `RoundsDb` takes an injected `TimeProvider`, which the next story needs anyway. Both fixes were
> re-broken afterwards and seen red again, per the RED-GREEN-RED order.

0. **RED first.** A test in `src_mcp/tests/RoundsDbTests.cs` that records a round of three findings,
   resolves them **out of order** (`finding 2` rejected, then `finding 0` accepted) and reads the rows
   back. It must fail describing the real symptom — the mark on the wrong finding — and not a setup
   error.
1. `RoundsDb.RecordDecisions` (`src_mcp/src/Store/RoundsDb.cs:131-155`) binds `$ordinal` from the
   **decision's own finding**, not from the loop counter. `Decision.Accepted`/`Rejected` carry the
   `Finding` (`src_mcp/core/Rounds/RoundMachine.cs:8-15`); the index the caller used is resolved
   against `session.Pending` in `PanelService` (`PanelService.cs:2237-2262`), so the ordinal has to
   travel with the decision from there rather than be re-derived from list position.
2. Green, then the whole suite.

Phase 1 depends on this only in the sense that both touch `RecordDecisions`; land it first so the
diff the gate reads is one change at a time.

### Phase 1 — the second time (independent, ships alone)

**What `decideSeconds` MEANS, defined before it is computed** (plan round, gemini — it was not, and a
number nobody can define is a number nobody can trust). It is **the wall-clock from the round
finishing to the LAST decision recorded against it** — not active attention. A round resolved in one
`resolve` call, which is the ordinary case because `RecordDecisions` stamps one `UtcNow` for the whole
batch (`RoundsDb.cs:134`), measures exactly the deciding. A round returned to after lunch measures the
lunch too. The tooltip therefore says *"from the round finishing to the last decision"* rather than
"time spent deciding", and the column is never presented as effort.

1. `RoundsQuery.cs`: add `ResolvedUtc` to `LoggedRound`, filled by
   **`COALESCE((SELECT MAX(resolved_utc) FROM findings WHERE round_id = r.id), '')`**. The
   `COALESCE` is load-bearing and was missing: `MAX()` over **zero rows is SQL `NULL`**, not `''`, so
   a round whose findings table is empty would serialise `null` against a non-nullable `string`.
   (Plan round, codex.) A test covers a round with no finding rows at all.
2. `roundsDb.ts`: `DbRound.resolvedUtc`, defaulted to `''` in `round()` — **an older server simply
   sends nothing and the page shows one time, exactly as today.** No fallback machinery is needed.
3. `roundsLog.ts`: `LogRow.decideSeconds: number | null`, computed beside `secondsOf`
   (`roundsLog.ts:592-612`). **The empty string is turned into `null` by an explicit test before any
   arithmetic happens** — not left to `Date.parse('')` and a `NaN` check, because the one thing this
   must never produce is `0`, which reads as "decided instantly" rather than "nobody knows"
   (constraint 7; plan round, local). It then refuses, as `null`: a `completedUtc` that will not
   parse, a **negative** difference (clock skew, or a decision recorded against an earlier round),
   and anything over `MAX_PLAUSIBLE_SECONDS`, exactly as `secondsOf` already does.
4. The `Took` cell (`roundsLog.ts:1303`) renders `took(r.seconds)` and, when `decideSeconds` is not
   null, ` · ` + `took(r.decideSeconds)`, with a `title` naming which is which. The column header
   stays **Took**; the tooltip says *analysis · from the round finishing to the last decision*.

### Phase 2 — the CSV builder (pure, no UI)

5. New module `src_vs_code/src/roundsCsv.ts` — **pure, importing only types**, so it can be reached
   from a unit test and cannot drag `node:` into the page bundle. One exported function

   ```ts
   csvOf(rounds: readonly ExportRound[]): string
   ExportRound = { readonly row: LogRow; readonly found: Found }   // Found = { state, findings }
   ```

   **The findings arrive with their STATE attached, not as a bare array** — the signature took a
   `Map<string, DbFinding[]>` and a missing key then meant two different things at once
   (constraint 14; plan round, codex). `csvOf` writes the placeholder row only for
   `state === 'loaded'` with no findings, and **refuses to build a file at all** if any round is
   `failed` — returning which ones, so the caller reports that instead of a success. `absent` is a
   real answer and gets a placeholder row whose `decision` column reads `not recorded`.

   Every cell goes through **one** writer that applies the formula guard and RFC-4180 quoting, so no
   column can be forgotten (constraint 9).
   - Round columns: `started_utc, started_local, kind, repository, repository_path, branch, stage,
     round, subject, status, accepted, rejected, verdict, gating, findings_count, analysis_seconds,
     decide_seconds, tokens_in, tokens_out, cost_in_usd, cost_out_usd, cost_total_usd,
     cost_is_estimate, cost_partial, reviewers_answered, reviewers`.
   - Finding columns: `finding_ordinal, severity, category, file, line, title, why, fix, role,
     is_gating, vendors, decision, reason, re_raised`.
   - `decision` is the word the page shows — `took` / `declined` / `open` — from `resolution`
     (`roundsLog.ts:1266`), so the file and the screen say the same word.
   - `reviewers` joins the per-reviewer lines with `; `, built from `reviewerRows`
     (`rounds.ts:234-265`) — **the uncoloured half of the seam that was kept for exactly this**
     (`rounds.ts:141-147`).

### Phase 3 — one row's export

6. A per-row `Export` button in the `<tr>` template, in its own `<td>`, with a matching
   non-sorting `<th>` — **added to `COLUMNS` with a key that `compareRows` ignores**, so
   `COLUMN_COUNT` stays right (constraint 4). It carries `data-command="export"` and
   `data-id="<row.key>"` plus the three `dbKey` fields, exactly as `ask()` does
   (`roundsLog.ts:1394-1397`).
7. `roundsLogMessages.ts`: `LogCommand` gains `{ kind: 'export'; rounds: ExportRequest[] }`; the
   decoder reads an array and ignores a message whose array is empty or malformed.
   **The page sends the ROWS, not only their keys** (plan round, gemini — nobody had said where the
   host would get them). The host's `latest.rows` is the unfiltered set as of the last tick and a row
   the person selected may already have left it; looking a selection up in host-held mutable state is
   the exact pattern three reviewers of the *previous* code round objected to, which is why `dbKey`
   already travels with the `findings` request (`roundsLog.ts:1394-1397`). Each entry carries the
   `LogRow` and its `dbKey`; the decoder validates shape and drops what it cannot read.
8. `roundsLogPanel.ts`: `RoundsLogHooks.onExport`; dispatch in `received`.
9. `extension.ts`: the hook reads the findings (`panelRef.roundFindings` for one round), builds the
   CSV with `csvOf`, offers `showSaveDialog` with a suggested name, writes with
   `writeFileAtomically`. **Three endings, all of them defined** (plan round — codex, local and
   gemini raised this independently):
   - **Cancelled** (`showSaveDialog` answers `undefined`): a clean no-op. No error, no success
     message, in-flight state cleared.
   - **Failed** (the read failed, or the write threw — a read-only drive, a locked file): an error
     notification naming what failed, and **no success report**. Never a success message for a file
     that is not there.
   - **Written**: says how many rounds and how many findings it wrote, and to where.

   The in-flight state is cleared in a `finally`, so no path can leave a button on *Exporting…*
   (constraint 13).

### Phase 4 — selection and bulk

10. `state.selected` in the page (`roundsLog.ts:1164`), a checkbox `<td>` per row, a `[data-select]`
    branch **above** `tr[data-key]` (constraint 5), a header checkbox for *all matched rows*
    (explicitly the filtered set, not the page — `render()` slices to a page at
    `roundsLog.ts:1283-1287`), and a toolbar button reading `Export 41 selected…`, disabled at zero.

    **Pruning, stated exactly, because "stale" was doing two jobs** (plan round, local). A key is
    dropped **only when no row with that key exists in `ROWS` at all** — the round has fallen out of
    the loaded window. A selected row that a filter has merely hidden **stays selected**: the person
    ticked it deliberately and a filter is a view, not a decision. Pruning happens in `render()`,
    where the row set is already in hand, and **not** only on the five-second tick — otherwise an
    export fired between ticks carries keys the page has already forgotten. When the selection
    includes rows the current filter hides, the button says so (`Export 41 selected (3 hidden)…`),
    so nobody exports more than they can see without being told.
11. Server: **`--findings-many --keys-file <path>`** — its own `args[0]`, answering findings for many
    rounds in one spawn, keyed by `sessionId|stage|number`.

    **A separate MODE rather than a flag on `--findings`, and this is the correction the plan round
    produced.** An older binary given `--findings --keys-file …` does not reject it: `Classify` sees
    `--findings`, `FindingsJson` finds no `--session`, and it exits **69 — "no such round … its
    findings were never recorded"** (`Program.cs:288-316`), which the client faithfully renders as a
    round that recorded nothing. A bulk export would have declared every selected round empty. With
    its own `args[0]`, an old `Classify` returns `Startup.Usage` and exits **64**
    (`Program.cs:142-144`), which is the only code the fallback can distinguish.

    **A file rather than argv** because that is this server's established shape for bulk input
    (`--ask-local`'s `--prompt-file`, `Program.cs:166-171`: *"Everything goes to a FILE, in both
    directions"*) and because a thousand keys overflow a Windows command line. It reuses the existing
    grouped read (`RoundsQuery.FindingsByRound`, `RoundsQuery.cs:317-351`) with a join that returns
    the round's public key instead of its row id, and answers a **state per round** so a key it has
    never heard of is `absent` rather than silently missing (constraint 14).
12. `roundsDbRead.ts`: `readManyFindings`, with the `EX_USAGE (64)` fallback — **an older installed
    server falls back to per-round reads**, which is the behaviour of today. The fallback is
    **throttled to a small concurrency pool (4)** rather than firing N spawns at once: a hundred
    concurrent child processes exhaust handles and freeze the extension host (plan round, gemini).
    Each per-round failure is carried as that round's `failed` state, not swallowed.
13. **Bulk is a progress task, not a button label.** `vscode.window.withProgress` on the
    notification, reporting *n of m rounds*, **cancellable** — cancelling stops the reads and writes
    nothing. A selection over a cap (500) asks for confirmation before starting rather than
    discovering the size halfway through (plan round, gemini).
14. `.agents/PROJECT.md`: add `--findings-many` to the sanctioned one-shot list (constraint 10).

## Test plan

Every phase ships its tests in the same task (`.agents/conventions/common/testing.md`).

| What | Where | Asserts |
|---|---|---|
| **an out-of-order resolve** (phase 0) | `src_mcp/tests/RoundsDbTests.cs` | decisions sent as `finding 2` then `finding 0` land on findings 2 and 0 — seen RED first, failing with the marks swapped |
| a PARTIAL resolve (phase 0) | `src_mcp/tests/RoundsDbTests.cs` | resolving only `finding 1` leaves 0 and 2 undecided rather than marking 0 |
| `MAX(resolved_utc)` reaches `LoggedRound` | `src_mcp/tests/RoundsQueryTests.cs` | a resolved round carries the stamp; an unresolved one carries `''`; a partly-resolved one carries the **last** decision |
| `parseLog` tolerates its absence | `src_vs_code/src/test/roundsDb.test.ts` | a payload with no `resolvedUtc` yields `''`, not `undefined` |
| the second time | `src_vs_code/src/test/roundsLog.test.ts` | decide-seconds from the two stamps; `null` for a negative difference, an implausible one, and a round nobody resolved |
| the Took cell | `src_vs_code/src/test/roundsLogPage.test.ts` | one number when there is no decision time, two separated by `·` when there is |
| `csvOf` | new `src_vs_code/src/test/roundsCsv.test.ts` | header; one row per finding; a finding-less round yields one row with empty finding cells; **`null` renders empty, never `0`**; a newline/quote/comma in `why` survives a round trip; a `=cmd` title is neutralised; the BOM is present once |
| the export command | `src_vs_code/src/test/roundsLogPaging.test.ts` (its harness) | clicking a row's Export posts exactly one `export` carrying that round's identity; **clicking a checkbox does not expand the row**; selection survives a `rows` tick; stale keys are pruned |
| the decoder | `src_vs_code/src/test/theLogLosesItsFirstPush.test.ts` | `export` decodes; an empty/garbled key array is ignored |
| the bundled page | `src_vs_code/src/test/bundledPage.test.ts` | the page still runs minified; **`roundsCsv` must not pull `node:` into the bundle** |
| the batch read | `src_mcp/tests/RoundsQueryTests.cs`, `src_vs_code/src/test/roundsDbRead.test.ts` | many keys in one call; an unknown key answers "not known" rather than "found nothing"; **exit 64 falls back, exit 69 does NOT** — the fallback fires only on the code an old binary actually returns for an unknown mode |
| **a round with no finding rows** (phase 1) | `src_mcp/tests/RoundsQueryTests.cs` | `ResolvedUtc` is `''`, not null — the `COALESCE` case |
| **`''` never becomes 0** (phase 1) | `src_vs_code/src/test/roundsLog.test.ts` | an empty `resolvedUtc` yields `decideSeconds === null`; so does a negative difference and an unparseable `completedUtc` |
| **a failed read never becomes an empty round** | `src_vs_code/src/test/roundsCsv.test.ts` | `state: 'failed'` makes `csvOf` refuse and name the round; `state: 'absent'` writes a placeholder reading `not recorded`; `loaded` with no findings writes the ordinary placeholder |
| **the formula guard covers every column** | `src_vs_code/src/test/roundsCsv.test.ts` | a repository named `=HYPERLINK(…)`, a branch named `@x`, a `file` starting `-`, a role and a vendor — each neutralised; a table-driven case over every string column so a new column cannot be forgotten |
| **cancel and write failure** | `src_vs_code/src/test/` (host export test) | a cancelled dialog reports nothing and clears the in-flight state; a throwing write reports an error and reports NO success; both clear state in `finally` |
| **selection survives a filter, dies with the row** | `src_vs_code/src/test/roundsLogPaging.test.ts` | a filtered-out selected row stays selected and the button says how many are hidden; a row that leaves `ROWS` entirely is dropped from the selection in `render()`, not only on a tick |
| **the fallback is throttled** | `src_vs_code/src/test/roundsDbRead.test.ts` | 20 keys against a server that exits 64 never has more than 4 reads in flight |

## The defect found while tracing — in scope, as phase 0

`RoundsDb.RecordDecisions` binds `$ordinal` to the **loop index** of the decisions list
(`src_mcp/src/Store/RoundsDb.cs:135, 146`) and never reads the `Decision`'s own finding.
`PanelService` builds that list in the caller's DTO order (`PanelService.cs:2237-2262`), validating
each index independently. **A caller that sends decisions out of order, or a partial set, has them
written onto the wrong findings in the database** — the session-file state machine is unaffected, so
nothing visibly breaks. The only test for the path passes them in order
(`src_mcp/tests/RoundsDbTests.cs`), which is why it has stayed latent.

This matters here because the accept/decline mark is precisely what the export is being asked to
carry: a CSV built on it would publish the wrong marks. **Decided with the operator 2026-09-13: fixed
in this plan, as phase 0** — a RED test that resolves findings out of order and observes the swap,
then bind the ordinal from the decision. It is a small change, and the export is the first thing that
would spread the error outside this machine.

Two smaller observations, recorded and **not** acted on without being asked:

- `RecordDecisions` takes its stamp from `DateTime.UtcNow` (`RoundsDb.cs:134`) where the family rule
  asks for an injected `TimeProvider` so a test can control it
  (`.agents/conventions/common/utc-timestamps.md`). Phase 1 makes that stamp load-bearing for a
  number on screen, which strengthens the case for injecting it — but it is a change to code this
  task did not otherwise need to touch.
- `reviewers.model` is never persisted (`RoundsDb.RecordReviewers`), although `ReviewerState.Model`
  exists — the model survives only in the session file. Out of scope here; it is the subject of
  [PLAN_the_log_names_the_model.md](PLAN_the_log_names_the_model.md).

## Definition of Done

- [ ] A resolve that arrives out of order, or covers only some findings, writes every mark onto the
      finding it was about — proven by a test seen RED with the marks swapped.
- [ ] Every row has an Export button; it writes a CSV of that round and its findings, to a path the
      person chose, and says what it wrote.
- [ ] Every row has a checkbox; a header checkbox selects every **matched** row; the toolbar button
      names the count and is disabled at zero; ticking a box does not expand a row; the selection
      survives the five-second tick and drops keys that no longer exist.
- [ ] The CSV carries the table's own columns **and** every finding, each with its accept/decline
      mark and, for a rejection, its reason — in the same words the page uses.
- [ ] A `null` measurement is an empty cell, never `0`; `~` and `+` survive as their own columns.
- [ ] Free text survives a round trip through a spreadsheet, and a formula-shaped field cannot execute.
- [ ] The **Took** column shows the analysis time and the deciding time, and shows one number for a
      round nobody has decided.
- [ ] An older installed server degrades to today's behaviour on both new reads — and the batch mode
      is its own `args[0]`, so the degradation is triggered by exit **64** and never mistaken for a
      round that recorded nothing (exit 69).
- [ ] A round whose findings could not be READ is never written as a round that found nothing; the
      export says what failed instead of claiming success.
- [ ] A cancelled save writes nothing and reports nothing; a failed write reports the failure and
      never a success; neither leaves a control on *Exporting…*.
- [ ] A bulk export shows cancellable progress, throttles its fallback, and confirms before starting
      an unusually large selection.
- [ ] `.agents/PROJECT.md`'s one-shot list names `--findings-many`.
- [ ] No new `contributes.command`, no new `coai.*` setting — so no new help article and no
      translations.
- [ ] Tests as tabled above, each seen RED before it went green; `cd src_vs_code && npm test` and
      the MCP test executable both green, with their output quoted in the summary.
- [ ] `research/module_server.md` and the rounds-log section of `research/architecture.md` updated;
      `todo/README.md`'s table carries this plan.
- [ ] The plan round of this repository's own gate reached `proceed`, and the code round after it.
