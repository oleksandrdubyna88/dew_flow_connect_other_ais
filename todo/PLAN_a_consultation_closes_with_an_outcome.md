# PLAN — a consultation closes with an outcome, and its tokens have a price

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: `src_mcp/src/Tools.cs`,
> `src_mcp/src/Server/Consultation/*`, `src_vs_code/src/roundsLog.ts`, `src_vs_code/src/consultations.ts`
> — the consultation's ending and its cost column.
>
> Issue: [#309](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/309).
> Related docs: [module_server.md](../research/module_server.md),
> [module_extension.md](../research/module_extension.md),
> [PLAN_consultant.md](../research/PLAN_consultant.md).

## The two symptoms, and what is measured about each

The issue is two sentences and one screenshot of the **Consultations** tab:

> *"Cost accounting. And closing must exist (with a status — problem solved, not solved; something
> must be recorded)."*

The screenshot shows one row: `Claude Code → codex · gpt-6-astra`, `Turns 1`, **`How it ended: open`**,
`Tokens 245.7k`, **`Cost —`**. The second screenshot is a session explaining, correctly, that there is
no verb to call: *"the coai surface has `open`, `consult`, `review_*`, `resolve`, `status`,
`providers`, `ask_human`, and nothing that ends a consultation… it's already finished. It stands at 1
of 5 turns, status `open`, and it only continues if someone calls `consult` again with that id."*

### Nothing ends a consultation — verified against the tool list

`src_mcp/src/Tools.cs` declares every tool the server publishes. There are nine, and none of them
ends a consultation. `resolve` records accept/reject for a review ROUND's findings and cannot touch
one.

A consultation therefore lapses. `ConsultationStatuses` (`ConsultationRecord.cs:8-24`) already has
the state it would move to — `Closed`, *"Over — the budget was spent, or it sat idle past its budget"*
— but **only those two paths reach it**, and `Reason` beside it answers *why it ended*, never
*whether the problem was solved*. A consultation that did its job and one that was useless end
identically: `open`, for ever, and then a sweep.

**So the thing missing is not a status. It is an OUTCOME, and a verb that records one.** The gate's
own vocabulary already separates these two questions for a review round — a verdict and a reason —
and the consultation has only the reason.

### The cost is `—` because the vendor did not report money, and the server refuses to guess

Measured, not assumed. `Usage.CostUsd` is filled **only when the CLI itself printed a price**, and
the refusal is deliberate and written down (`src_mcp/core/Findings/UsageParser.cs:6-7`):

> *"Only when the CLI itself reported money (claude does); estimating prices for vendors that do not
> would mean shipping a price table that is wrong within a month."*

`ConsultationRows.cs:36-40` is equally deliberate: it returns `null` when no turn priced itself,
*"rather than a zero that reads as 'this was free'"*. The row in the screenshot ran on **codex**,
which reports no money — so `costUsd` is genuinely null, and `roundsLog.ts:1017` renders
`money(null)`, which is the dash.

**Every piece of the fix already exists on the extension side, and the reviewers' half already uses
it.** `usage.ts` has:

| | |
|---|---|
| `priceOf(provider, vendors, listed)` (`usage.ts:214`) | the vendor row's TYPED rate, falling back per field to a published list price |
| `priceOfLine(provider, model, vendors, listed)` (`usage.ts:249`) | the same, but priced by the MODEL when the row has been renamed away — *"the tokens stayed and the cost became a dash, retroactively, for work that really had been paid for"* |
| `estimated(usd)` (`usage.ts:275`) | `~$0.0123` — *"the tilde is the whole point: this is not what anybody billed"* |

So the consultation cost is a dash **only because that column never asks**. The panel prices the
reviewers' tokens and the chat's tokens this way already; the Consultations tab is the one table that
does not. Nothing needs to be added to the server, and in particular no price table — the server's
refusal stands and this plan does not touch it.

## What ships

| | |
|---|---|
| `close_consult` — a tenth tool | the verb the surface has never had: the caller that opened a consultation records how it ended |
| `ConsultationRecord.Outcome` | `solved` / `not_solved` / `abandoned` / `lapsed` — the closed set below — beside the existing `Reason`, which keeps answering *why it stopped* |
| the **How it ended** column | says the outcome when there is one, and stays honest when there is not |
| the **Cost** column | `~$…` from `priceOfLine`, marked as an estimate, when the vendor reported no money |
| a human close | the *Consultant* section's running-consultation card gets a close control, because an AI that has crashed or moved on will never call the verb |

**The outcome is recorded by whoever knows it.** The AI that asked is the one that verified the
advice — `consult`'s own instructions already say *"verify it, then report back on the same
consultationId"* — so the verb is the primary path. The human control exists for the case the verb
cannot cover: a session that ended without calling it, which is exactly the state the issue's own
screenshot is in.

## The outcome — the closed set, who may write it, and what every path leaves

Four values and nothing else, validated at the tool boundary before anything is written:

| outcome | written by | means |
|---|---|---|
| `solved` | the caller, or a person | the advice was verified and it worked |
| `not_solved` | the caller, or a person | it was verified and it did not |
| `abandoned` | the caller, or a person | nobody is going to act on it |
| `lapsed` | **the server only** | the budget ran out or it sat idle — nobody said anything about it |

`lapsed` is the answer to *"automatic closures still produce no outcome"*. Leaving those empty was
the first draft, and it fails the issue's own words — *something must be recorded*. But a spent
budget is not a verdict, so it gets its own value rather than being flattened into `abandoned`: the
column can then say *it ran out* rather than implying somebody decided. **A record from before this
field existed still carries nothing, and nothing is not `lapsed` either** — see the boundary table.

**Ownership reuses the rule that already exists.** `ConsultationService` identifies a caller with
`CallerOf(repoPath)` and `ConsultationRules` already *"refuses a follow-up whose caller differs"*
(`ConsultationService.cs:92`). `close_consult` refuses on exactly that comparison — a second AI
cannot close the first one's consultation — and the test names the case.

**The person is a different door, deliberately.** The panel does not speak MCP; it drives the server
through one-shot CLI modes (below), which run on the person's own machine against their own data
directory. That is a stronger trust position than another AI's, not a weaker one, so the human close
is not subject to the caller check — and the record says which door was used, so *the AI said it
worked* is never confused with *a person marked it closed*.

**An outcome is immutable once written.** A retry carrying the SAME outcome succeeds and rewrites
nothing (the answer to a lost response); a different one is refused as a conflict and says so. The
one permitted transition is filling an ABSENT outcome on a record the server closed — that is the
human close of a lapsed consultation, which is exactly the state the issue's screenshot is in.

## The panel cannot call an MCP tool — the bridge, named

The extension never speaks MCP to this server. It drives one-shot CLI modes selected from `args[0]`
before any transport opens — `--log`, `--findings`, `--findings-many`, `--providers`, `--bugs-json`
(`src_mcp/src/Program.cs:162-178`), through `serverRun` in `roundsDbRead.ts:313`. So the human close
needs its **own one-shot mode**, `--close-consult`, taking the id and the outcome and answering by
exit code, exactly as `--findings-many` does. Without this the close control is a button that cannot
run; the first draft promised the control and never named the door. (gemini, plan round.)

## The boundary, both ways (MANDATORY — the halves ship on their own clocks)

The extension and the server are separate artefacts and the person updates them separately; this is
the table [development-workflow.md](../.agents/conventions/common/development-workflow.md) requires,
and the lesson [module_server.md] records from the `remoteVendor` field that three releases dropped.

| | new extension | old extension |
|---|---|---|
| **new server** | the outcome is written and shown beside the status | the extra field is ignored by a parser that rebuilds every entry; the column reads as it does today |
| **old server** | **the STATUS is preserved and the outcome is simply absent** — an `open` record still reads `open`, a `closed` one still reads `closed`, and neither gains a verdict | unchanged |
| **old server, `close_consult` called** | the tool is absent; the caller gets the MCP "unknown tool" error, which names what to update | — |
| **old server, the human close pressed** | `--close-consult` is not a mode that binary has; it answers its own "unknown argument" and exits non-zero, and the panel says the server is too old rather than failing silently | — |

Two rules the table exists to fix in advance, and the first is the one the first draft got wrong:

1. **An absent outcome never changes the STATUS.** The draft said an outcome-less record "shows
   `closed`", which would have reported a still-running consultation as finished. The status is the
   server's and it is shown as given; the outcome is a second, separate column.
2. **Absent is not `not_solved`, and it is not `lapsed` either.** A record with no outcome is one
   nobody said anything about — including one this build's own sweep closed before the field existed.
   It renders as an em dash, never as a verdict, and there is NO migration that invents one.

**This is local to `src_mcp`, and that was checked rather than assumed.** A consultation is a file in
`<dataDir>/consultations/<id>.json`; `src_server` — the Team server — contains **zero** references to
`Consultation` (measured 2026-09-17). No Team-server schema, wire contract or deploy is involved.

## Build order

1. **RED** on the pure half: `outcomeOf(record)` answers the three outcomes and `''` for a record
   that carries none; a `closed` record with no outcome does NOT become `not_solved`.
2. **RED** on the cost: given a consultation row with tokens and `costUsd: null`, plus a vendor row
   with a typed rate, the Consultations tab shows `~$…`; with a real `costUsd` it shows the money
   unmarked; with neither rate nor cost it still shows the dash.
3. The extension half — `consultations.ts` gains `outcome`, `roundsLog.ts`'s two columns. The page
   test RUNS the page, per the operator ruling: no new behavioural assertion over page source text.
4. **RED** on the server: `CloseAsync` refuses an id that is not this caller's, refuses an outcome
   outside the closed set, is idempotent for an already-closed consultation, and writes `EndedUtc`.
5. The server half — the record field, the service method, the tool declaration, the row.
6. The human close in the *Consultant* section.
7. **Teeth**: delete the absent-is-not-not_solved guard and watch the boundary test go red; remove
   the estimate's tilde and watch the cost test go red.
8. The `--close-consult` one-shot mode, and the panel's outcome picker: the control opens a choice
   of the three human outcomes with a confirm and a cancel, and reports the server's refusal — a
   conflict, a stale record, a binary too old — rather than closing optimistically.
9. **The live counterpart check**, because no unit test can prove the two-sided claim: a script that
   runs the PREVIOUS released server binary and the new extension's reader against one data
   directory, asserting an old record parses with no outcome and its status intact, and that
   `--close-consult` against the old binary fails the way the table says. `npm run test:claude-models`
   is the precedent — hand-run, catalogued, not in CI.
10. `cd src_vs_code && npm run clean && npm run compile && npm test` — the repository's own
   platform-safe scripts, rather than a shell `rm -rf`; then the server suite; then
   `node .agents/conventions/tools/plan-lifecycle.mjs`.

## Test plan

- No behavioural assertion over page source text (operator ruling, 2026-09-14). The columns are
  asserted by running the page; the decisions are asserted by calling them.
- The server's refusals are unit-tested against a real record on disk, as `ConsultationService`'s
  existing suites are.
- **Every new flow is catalogued in `research/module_tests.md`** with what it does and does not
  prove — the AI close, the human close, a retry, and the old-server refusal. A unit test and a page
  test cannot see broken tool wiring or a dead button, which is the gap that catalogue exists to name.
- Unchanged and must stay green: `consultationsLive.test.ts`, `consultant.test.ts`,
  `roundsLogPaging.test.ts`, the server's consultation suites.

## What this does NOT do

- **It does not add a price table to the server.** `UsageParser.cs` refuses one on the record and the
  refusal is right; the estimate is the extension's, made where the rates a person typed already live.
- **It does not change what `Reason` means.** *Why it stopped* and *whether it worked* are two
  questions and this plan adds the second rather than overloading the first.
- **It does not freeze the estimate.** A consultation priced from a rate typed today shows a
  different number if that rate is edited next year, because the estimate is RECALCULATED from the
  rows and lists as they stand — which is what `priceOfLine` already does for every reviewer round and
  every conversation in the ledger. Storing a rate snapshot per consultation would make this table
  disagree with the two beside it; the tilde is what says the number is an estimate rather than a
  bill. Stated here because the plan round asked, and the answer is "deliberately current".
- **It does not sweep or expire anything differently.** The existing budget and idle paths still
  close a consultation; they simply leave the outcome empty, which the column will say.

## Definition of Done

- [ ] A RED test observed failing before each half, naming the real symptom.
- [ ] A `closed` record with no outcome never reads as `not_solved` — asserted, and proved by breaking it.
- [ ] A consultation whose vendor reported no money shows a marked estimate when a rate exists, the
      real money unmarked when the vendor reported one, and the dash when there is neither.
- [ ] An absent outcome renders as a dash and leaves the STATUS untouched — asserted for an `open`
      record and a `closed` one, and proved by breaking it.
- [ ] A repeat of the same outcome succeeds; a different one is refused as a conflict; a person may
      fill an absent outcome on a lapsed record.
- [ ] The human close reaches the server through `--close-consult` and surfaces its refusal.
- [ ] The live counterpart check exists, was RUN against the previous released binary, and its result
      is recorded here.
- [ ] `close_consult` refuses another caller's id and an unknown outcome, and is idempotent.
- [ ] The two-sided table above is true of the code, both rows.
- [ ] Whole extension suite green from a cleaned `out/`; the server suite green; `plan-lifecycle.mjs` clean.
- [ ] `research/module_server.md` and `research/module_extension.md` updated.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations; both READMEs updated.
