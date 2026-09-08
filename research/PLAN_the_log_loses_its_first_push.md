# PLAN — one lost message empties the log page for ever

> Status: **IMPLEMENTED, 2026-09-08.** Scope: `src_vs_code/src/pushLedger.ts` (new),
> `src_vs_code/src/roundsLogMessages.ts` (new), `src_vs_code/src/roundsLogPanel.ts`,
> `src_vs_code/src/roundsLog.ts` (the page script).
>
> Related docs: [module_extension.md](module_extension.md).

## The symptom

The operator reported two things on the rounds log, both from today:

- **The *What it keeps missing* tab is completely empty.**
- **The table no longer shows how many findings were accepted and rejected.**

Everything else on the page is right: 57 of 352 rounds listed, verdicts, gating counts, findings
counts, tokens, cost, reviewers.

## What is NOT wrong — measured, at every step

The temptation is to look at the database. It is fine, and so is everything that reads it:

| Step | Checked how | Result |
|---|---|---|
| the installed binary | `coai-mcp.exe --version` | `coai-mcp 0.18.13` |
| its SQLite library | the file beside it | `e_sqlite3.dll` present |
| the log it emits | `--log --limit 300`, as the extension calls it | exit 0, **4.17 MB**, 207 rounds, **24 blind spots**, **205 rounds carrying a decision** |
| the 8-second cap | timed, twice | **143 ms**, 147 ms |
| `parseLog` | run over that exact output | 207 rounds, 24 spots, 205 decided |
| `blindSpotsHtml` | run over the parsed log | **2968 characters** of real HTML |
| `decisionsByRound` | same | 207 keys, e.g. `681a658c\|d:/rsd/…\|feat/chat-trigger\|codereview\|1` |

Every number the page is missing exists, correct, one function call from the page.

## The defect: a push recorded as delivered without being delivered

Three facts, and it is the third that makes it permanent.

1. **The first paint is deliberately database-free.** `showRoundsLog` (`extension.ts:492`) calls
   `rowsFrom(…, undefined, …)` — no log — because reading it spawns a process and
   *"a person who opened a log should not wait on a process to see it: the findings arrive in the
   next push, a moment later"*. So the page is rendered with no decisions and `spotsHtml = ''`.
2. **That next push is sent blind.** `RoundsLogPanel.update` (`roundsLogPanel.ts:82-99`) does
   `void this.panel.webview.postMessage(…)`. `postMessage` answers a `Thenable<boolean>` —
   `false` when the webview did not receive it, which is exactly the state a webview is in for the
   moment between `webview.html = …` and its script running. Nothing here reads that answer.
3. **And the bookkeeping records it as sent anyway.** `this.lastPayload = payload` and
   `this.lastSpots = spotsHtml` are assigned before the post. Every later tick compares against
   them, finds no change, and sends nothing. **One dropped message is permanent** — the page stays
   in its pre-database state until it is closed and reopened, and the same race can drop that one
   too.

There is no `ready` handshake to make the first push safe: the page posts only `command`
(`roundsLog.ts:968`), and `onDidReceiveMessage` handles only that (`roundsLogPanel.ts:63-73`).

## The change

**1. The page asks.** When its script is running and its listeners are attached, it posts
`{ type: 'ready' }`. The panel answers with a full, forced push. A webview that cannot receive is
then not a problem, because nothing is sent until it says it can.

**2. State is recorded when the post SUCCEEDS, not when it is made.** `postMessage` is awaited and
`lastPayload` / `lastUsage` / `lastSpots` are assigned only on `true`. A dropped push then leaves
the panel believing the page is stale — which it is — so the next tick sends it again.

Two halves of one guarantee, and each covers what the other cannot: the handshake fixes the first
paint even on a slow machine; the bookkeeping fixes every later one, including a webview that goes
away mid-push.

**3. `ready` is idempotent.** A page reloaded by VS Code (a tab dragged to another group, a window
reload) sends it again and gets a fresh push, which is the behaviour that makes the whole surface
recoverable rather than a one-shot.

## What this does NOT do

- It does not make the first paint wait for the database. That trade was made deliberately and the
  reason still holds; what changes is that the second paint actually arrives.
- It does not touch the database, the parser or the HTML builders. Every one was measured above and
  every one is correct.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `a push that was not delivered is sent again` | The defect: a `postMessage` answering `false` must not be recorded as sent |
| 2 | `a push that was delivered is not sent twice` | The guard on the other side — this bookkeeping exists to stop a repaint per tick |
| 3 | `the page asks for its data when its listeners are attached` | The page posts `ready` |
| 4 | `a ready message is answered with a full push` | Including the regions the first paint could not carry |
| 5 | `a second ready re-sends everything` | A reloaded webview recovers rather than staying blank |

`RoundsLogPanel` takes the webview through a seam thin enough to fake in a test — a `postMessage`
that answers `false`, then `true` — because the panel imports `vscode` today and cannot be
constructed in this suite otherwise.

## Definition of Done

- [x] The page posts `ready`; the panel answers with a forced push.
- [x] No `lastPayload` / `lastUsage` / `lastSpots` is recorded for a push that was not delivered.
- [x] Tests 1–5 written, watched fail, and passing.
- [x] `research/module_extension.md` records the handshake and why a blind `postMessage` after
      `webview.html = …` is not safe, with a sequence diagram of the flow it changes.
- [x] The plan completion check from
      [planning-docs.md](../.claude/rules/shared/common/planning-docs.md) was run: this plan's status
      line was re-read against the shipped code, and the plan promoted in the same task.

## What shipped differently

**The decision layer became its own module.** The plan proposed a webview seam thin enough to fake.
What was built instead is `pushLedger.ts` — no `vscode` import, so no seam is needed: the panel keeps
the API calls and the ledger keeps every decision about what to send and what to record. It is the
same move `chatMessages.ts` records, and it is the reason the six ledger tests exercise the real
logic rather than a test double of it.

**A generation number, which the plan did not have.** Two reviewers raised the same race from
opposite directions on the plan round: two pushes are in flight whenever an ordinary tick meets the
forced answer to `ready`, and an older one resolving second would record content the page does not
hold — leaving it stale until something else changed, which is the very defect this plan is about.
Every push now carries a generation, `settle` refuses one older than the newest recorded for its
region, and the posts themselves are serialised through one promise chain.

**A 5 s fallback timer, raised as blocking by two reviewers.** Gating every push on `ready` would
leave a page whose script threw before attaching its listener empty for ever, with nothing anywhere
saying why. After `ASSUME_READY_MS` the panel warns and pushes anyway — which is exactly what it did
before this change, so the failure mode is no worse than the one being fixed.

**The message vocabulary moved out too** (`roundsLogMessages.ts`, `logCommandOf`). `ready` is
load-bearing: a typo in that one branch ships with every test green and leaves the page as empty as
the reported defect. Inside a class importing `vscode` no test in this suite could reach it.

**Both tabs open on a hint rather than on nothing.** An empty div made a push that never arrived look
identical to one that had not arrived yet — which is why the report could not say which it was.

## What the code round changed

The first version's own fallback reintroduced the defect. Four findings, from both remote vendors
across three roles, said the same thing: the 5 s timer called `ledger.ready()`, so every push it made
was RECORDED as delivered on the strength of a `postMessage` that answers `true` for a webview that
merely exists — the retry stopped, and the tab read *Reading the log…* for ever with only a
`console.warn` in the extension host, which nobody opens.

- `assumeListening()` is now distinct from `ready()`: the panel pushes at a silent page, and records
  nothing, so every tick sends again until something acknowledges.
- The page carries its own deadline. A section that opened on the placeholder and has still been told
  nothing after 15 s says what went wrong and what to do. A section painted with real data — which
  the spending tab always is — is never overwritten; which sections opened empty is stated by the
  render as a `WAITING` constant rather than sniffed out of the DOM.
- A `page` counter on every push. The generation orders pushes within one page and cannot order them
  across two: a push in flight when the page was replaced would otherwise be recorded against its
  successor.
- The fallback timer got a catch-all (`reliability.md`: the outermost edge of a detached execution),
  the promise chain and the `postMessage` catch now log instead of swallowing (`coding-style.md`),
  and the region list became a `Record<Region, …>` so a new region without a push is a compile error.
- A sequence diagram in `module_extension.md`, and the placeholder lost its region name — the
  template that took one produced *"Reading the log for what it keeps missing…"*.

Six findings were rejected with reasons; the two that claimed a runtime crash (`newest` read before
its field initialiser) and a memory leak (the rolling `inFlight` reference read as a growing chain)
are recorded in the round because both are plausible-sounding and neither survives contact with how
class fields and promise reactions actually work.

## Evidence

Fifteen tests in `src_vs_code/src/test/theLogLosesItsFirstPush.test.ts`; whole suite **856 tests, 855
pass, 0 fail, 1 skipped**; lint clean.

The teeth, proved by putting each half of the defect back:

| reverted | test | failure |
|---|---|---|
| `settle` records regardless of `arrived` | `a push that was not delivered is sent again` | `AssertionError: an undelivered push is made again` |
| the page's `ready` post deleted | `the page says it is listening, and only once it actually is` | `AssertionError: the page never told the panel it was listening` |
| `ready` posted before the message listener | same test | `AssertionError: the page said ready BEFORE attaching its message listener` |
| the fallback calls `ready()` | `a page only ASSUMED to be listening is pushed at, and never recorded as told` | `AssertionError: but a true from postMessage is not evidence it arrived` |
| no `page` on a push | `a push made for a page that has since been replaced is not recorded` | `AssertionError: the new page has been told nothing` |
| no page-side deadline | `a page that is never told anything says so, where the person is looking` | `AssertionError: usage-body still claims to be reading` |
| the `WAITING` guard removed | `a section painted with real data is never replaced by the deadline` | `AssertionError: the spending section was written over` |

The third row is why the page tests RUN the script through a stub DOM rather than matching its source:
what has to hold is an order, and a regexp cannot see one.
