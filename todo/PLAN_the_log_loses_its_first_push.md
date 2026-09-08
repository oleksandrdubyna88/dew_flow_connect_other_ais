# PLAN — one lost message empties the log page for ever

> Status: **plan only, nothing implemented yet, 2026-09-08.** Scope:
> `src_vs_code/src/roundsLogPanel.ts`, `src_vs_code/src/roundsLog.ts` (the page script),
> `src_vs_code/src/extension.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md).

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

- [ ] The page posts `ready`; the panel answers with a forced push.
- [ ] No `lastPayload` / `lastUsage` / `lastSpots` is recorded for a push that was not delivered.
- [ ] Tests 1–5 written, watched fail, and passing.
- [ ] `research/module_extension.md` records the handshake and why a blind `postMessage` after
      `webview.html = …` is not safe.
