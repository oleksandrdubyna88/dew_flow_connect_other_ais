# PLAN — an error you can see, and answer

> Status: **partially implemented, 2026-09-15.** Story A (an empty turn is a failure) has shipped;
> stories C and D are open. Scope: the chat tab's failure surface —
> `src_vs_code/src/chatPage.ts`, `chatPanel.ts`, `chatMessages.ts`, `chatCommand.ts`, `agyAdapter.ts`,
> `claudeAdapter.ts`, `codexAdapter.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [architecture.md](../research/architecture.md).

## The symptom

Four reports from the operator on 2026-09-15, all on the same surface, all about a turn whose outcome
the page does not show.

1. **A turn ended with nothing at all.** "оно подумало и все" — the thinking line ran, then stopped;
   no answer, no error, the composer unlocked. Nothing was written anywhere: not the transcript, not
   the failure line, not a log.
2. **An error that did appear was off screen.** The vendor's `API error (attempt 1): UNAVAILABLE (code
   503): No capacity available for model gemini-3.8-flash-medium on the server` rendered above the
   transcript, under the passage, while the same push scrolled the reader to the bottom.
3. **An error offers nothing to do.** The turn is lost; re-asking means finding the question, copying
   it back into the composer and pressing Send.
4. **A long question buries the conversation.** A pasted prompt of several hundred lines pushes every
   answer off the screen, and there is no way to fold it.

These are one defect with four faces: **a turn's outcome is not reliably visible, and when it is
visible it is not actionable.**

## What the code does today — verified

Line references are against `origin/main` at `a28a0142`, extension `0.43.1`.

### 1. An empty answer renders as silence

`src_vs_code/src/agyAdapter.ts:50-52`:

```ts
if (status === 'SUCCESS') {
  return { kind: 'answer', text: text(result['response']).trim(), ...spent(usageOf(result)) };
}
```

`text()` (`chatAdapter.ts:91`) returns `''` for a missing or non-string field. So a `SUCCESS` result
with no `response` yields `{kind: 'answer', text: ''}` → `cliChatSession.ts` settles `{ok: true,
answer: ''}` → `chatCommand.ts` appends `{role: 'model', text: ''}` → `chatMessagesHtml` renders a
`.msg.model` block whose `.what` is empty. The reader sees the words "The other AI" at `opacity: .7`
and nothing under them.

The branch four lines below it is guarded against exactly this, and says so:

> An ERROR result is an error, not an empty answer: the CLI reports a refused input this way — it is
> how the schema was discovered — and a page showing nothing would look like a model with nothing to
> say.

The `SUCCESS` branch never got the same treatment. `claudeAdapter.ts` and `codexAdapter.ts` have the
same shape and the same gap.

Nothing logs the raw line either, so the case is invisible from both ends. A thinking-tier model that
spends its budget reasoning and returns empty content is a plausible producer, and the 503s the
operator is seeing on `gemini-3.8-flash-medium` put that model under exactly that pressure.

### 2. The failure line is pinned above the transcript

`chatPage.ts:848-854`:

```
<main id="scroll">
<div class="passage" id="passage">…</div>
<div id="failure">…</div>      ← 850
<div id="messages">…</div>     ← 851
<div id="thinking">…</div>     ← 852
<div id="capped">…</div>       ← 853
</main>
```

The failure is a single-slot region at the TOP of the scrolling area. Writing it sets `wrote = true`
(`chatPage.ts:1696`), and the handler then follows the reader to the bottom
(`if (wrote) { follow ? scheduleFollow() : offerJump(true) }`). So the page reliably scrolls away from
the thing it just wrote. `#thinking` at 852 is where the reader is already looking.

### 3. The failure markup is authored twice, and its region is never re-wired

The same expression exists in both render paths, with no shared builder:

- `chatPage.ts:827` (first render, via `regionsOf`)
- `chatPanel.ts:417` (every push, via `pushChatState`)

Every other pushed region has an exported builder (`chatMessagesHtml`, `chatPickerHtml`,
`chatCappedHtml`, `chatStatusHtml`, `chatPresetRowsHtml` — `chatPage.ts:325/430/477/528/796`). The
failure is the only one that does not. Adding a button to one copy would ship it in one render path.

And `#capped` calls `wireCapped()` after its write (`chatPage.ts:1675`) while `#failure` does not
(`chatPage.ts:1692-1697`). A directly-bound button in the failure region is dead after the first push.

### 4. Nothing is collapsible in the chat page

`#messages` is replaced wholesale by `innerHTML` on every push (`chatPage.ts` message handler). Two
techniques are ruled out before the work starts:

- **`<details>` whose state round-trips to the host** is the documented toggle feedback loop —
  `research/module_extension.md`, and the review instruction in `.coderabbit.yaml`: *"a webview's own
  state must never round-trip through the extension host and be re-applied (that produced a toggle
  feedback loop once)"*. Guarded by a regression test in `src_vs_code/src/test/activeRounds.test.ts`.
- **State that lives only in the DOM** snaps shut mid-read on the next push, which is the failure
  `panelView.ts`'s `openSections` doc comment names.

## What this plan does

### The sanctioned technique, for both new controls

A **delegated listener on the container**, which is never replaced — only its `innerHTML` is. The
precedent is the preset rows, `chatPage.ts:1497-1523`, whose comment states the rule:

> Delegated on the row, because a push replaces both rows whenever the saved lists change.

and `chatPage.ts:1683`:

> The container stays, so the delegated click listener on it survives the swap.

`#failure` and `#messages` are both in the first-render markup and both persist for the tab's life.
Binding to them by id, once, needs no re-wire hook and is visible to the page test harness, which
fabricates elements only for ids the initial document renders.

### A. An empty answer becomes a failure — `agyAdapter.ts`, `claudeAdapter.ts`, `codexAdapter.ts`

A `SUCCESS`/`success` result whose answer text is empty after trimming returns
`{kind: 'failure', failure: 'the model returned an empty answer'}` instead of an empty answer. The
existing ERROR-branch comment already argues this case; the change makes the SUCCESS branch obey it.

The turn then flows down the path everything else in this plan improves: it is a failure, so it lands
in the failure line, at the bottom, with a Retry button.

### B. One failure builder — `chatPage.ts`, `chatPanel.ts`

Extract `export function chatFailureHtml(failure: string): string` beside `chatCappedHtml`, and call
it from `regionsOf` (`chatPage.ts:827`) and `pushChatState` (`chatPanel.ts:417`). Reuse-first: the
button added in C must exist in both render paths, and one builder is the only way that is true by
construction rather than by remembering.

### C. The failure moves below the transcript, and carries Retry

- `chatBody` region order becomes `passage → messages → thinking → failure → capped`. The push handler
  addresses regions by id and `lastWritten` is keyed, so nothing else changes.
- `chatFailureHtml` emits `<button type="button" id="retry" data-retry>Try again</button>` inside
  `.failure`.
- A delegated `click` listener on `#failure` posts `{type: 'command', command: 'retry'}`.
- The command needs all four parts or it is a silently dead button: a `kind` in `ChatCommand`, a branch
  in `chatCommandOf` (`chatMessages.ts`), a hook on `ChatPanelHooks`, a `case` in `chatPanel.handle`.
- Host side: a retry re-sends the last question. **The trap:** `oneTurn` appends `{role: 'you', text}`
  before sending and the failure path leaves it there, so calling `ask()` again would print the
  question twice. The retry hook must drop the trailing `you` message before re-asking, or take a path
  that does not re-append.
- `thread.carry` must NOT be cleared — `chatCommand.ts` already preserves it across a failure
  deliberately, commented *"the retry — the same question, one keypress later"*. This plan is that
  keypress.
- `#failure` is also written by the `noteHtml` branch (`chatPage.ts:1580-1589`) and by `window.onerror`
  (`chatPage.ts:944`, via `textContent`). Both replace the region's content and therefore remove the
  button; the delegated listener survives, so the next failure restores it. Stated deliberately here so
  a reviewer sees it was decided rather than missed.

### D. A long question folds — `chatPage.ts`

- A pure exported predicate decides length host-side, so it is testable:
  `export const COLLAPSE_AFTER_LINES = 5` and a helper returning true when the text has more than
  `COLLAPSE_AFTER_LINES` newlines **or** exceeds `COLLAPSE_AFTER_CHARS` (400). The character arm is not
  decoration: the operator's pasted prompts are frequently one long wrapped paragraph with almost no
  newlines, which a newline-only rule would never fold.
- Applies to `role: 'you'` messages only — the operator asked for their own messages.
- `chatMessagesHtml` marks such a message `class="msg you long"`, gives it `data-i="<index>"`, and
  emits a `<button type="button" class="more" data-more="<index>">Show all N lines</button>`. The count
  is named rather than an ellipsis, per the house rule that a list which was cut and looks whole is
  worse than no list.
- CSS clamps `.msg.long .what` with `max-height` in `lh`/`em` and `overflow: hidden`; `.msg.long.open
  .what` lifts it. No `@media`/`@supports` — the stylesheet test's rule reader cannot see inside one.
- A page-local `expandedMessages` map, re-applied by `data-i` after each `#messages` write, keyed by
  index. Nothing crosses to the host.
- Expanding changes height under the reader, so it goes through the existing
  `repinIfTheyWereAtTheBottom()` / `landOnNewest()`, never a raw `scrollTop`.

## Build order

1. **A** — the three adapters. Smallest, independent, and it is the bug that produces silence.
2. **B** — extract `chatFailureHtml`. Pure refactor, no behaviour change, green before and after.
3. **C** — region order, then the button, then the command wiring, then the host hook.
4. **D** — the predicate, the markup, the CSS, the delegated toggle.

Each step is a commit. A and B are independently shippable if C or D stall.

## Test plan

`node:test` + `node:assert/strict`, run with `cd src_vs_code && npm test` (which cleans `out/` first —
a stale `out/` runs both old and new names and inflates the count).

RED first for every behavioural item, with the failure message naming the real symptom.

| # | Test | Guarantee |
|---|---|---|
| A1 | `chatAdapters.test.ts` | a SUCCESS result with no `response` is a failure, not an empty answer — one case per vendor |
| A2 | `chatAdapters.test.ts` | a SUCCESS result WITH a response is still an answer (the guard did not eat the happy path) |
| B1 | `chatPage.test.ts` | `chatFailureHtml('')` is empty; `chatFailureHtml('x')` escapes and wraps |
| B2 | `chatPage.test.ts` | the pushed failure and the first-render failure are byte-identical for the same input |
| C1 | `chatPage.test.ts` | source order: `id="messages"` < `id="thinking"` < `id="failure"` (the `indexOf` idiom from `panelView.test.ts`) |
| C2 | `chatPage.test.ts` | a failure renders a Retry button; an empty failure renders none |
| C3 | `chatPage.test.ts` | `runChatPage()`: deliver a state with a failure, fire a click on the retry control, assert `command: 'retry'` was posted |
| C4 | `chatPage.test.ts` | the retry control still posts after a SECOND failure push — the delegated listener survives the swap (this is the dead-button regression) |
| C5 | `chatMessages.test.ts` | `chatCommandOf({type:'command', command:'retry'})` is `{kind:'retry'}` |
| C6 | `chatCommand` tests | a retry re-asks the last question exactly once — the transcript does not gain a duplicate `you` message |
| D1 | `chatPage.test.ts` | the boundary: 5 newlines is not long, 6 is; 400 chars is not long, 401 is |
| D2 | `chatPage.test.ts` | a long `you` message gets `class="msg you long"` and a control naming the line count |
| D3 | `chatPage.test.ts` | a long `model` message does NOT fold |
| D4 | `chatPage.test.ts` | the stylesheet has `.msg.long .what` and `.msg.long.open .what`, and `rules()` still parses |

Plus the suites any page change must keep green: `bundledPage.test.ts` (the minified page — embedded
functions bound by assignment, never declaration) and `chatWiring.test.ts`.

## Definition of Done

- [ ] RED observed and reported for every behavioural test, with the real symptom in the message
- [ ] `cd src_vs_code && npm test` green, whole suite, count reported
- [ ] `npm run typecheck` clean
- [ ] The coai gate: `review_plan` → `resolve` → implement → `review_code` → `resolve`, rebased on
      `origin/main` immediately before the code round
- [ ] `src_vs_code/CHANGELOG.md` in the prose the file already uses
- [ ] `research/module_extension.md` updated — the region order and the failure surface
- [ ] `src_vs_code/package.json` version bumped, and the release TAGGED, not merely bumped
- [ ] This plan promoted to `research/` with `IMPLEMENTED 2026-09-15` and its deviations recorded
- [ ] `todo/README.md` table matches the folder
- [ ] `node .claude/rules/shared/tools/plan-lifecycle.mjs` green

## Not in this plan

**The WSL escalation split**, reported the same day: a Claude Code session running inside WSL writes
its escalations to `/home/<user>/.local/share/coai-mcp/escalations`, while a Windows-hosted extension
host watches `%LOCALAPPDATA%\coai-mcp\escalations` (`src_vs_code/src/dataDir.ts:16-21`). Two live
stores were confirmed on 2026-09-15 — a 25.8 MB `coai.db` on the Windows side and an 8.3 MB one in
WSL, both written to that day. The questions are captured; nothing is watching the store they land in.
`COAI_DATA_DIR` is the existing override and was unset on both sides.

It is a different subsystem, a different fix, and quite possibly a configuration answer rather than a
code change. It gets its own plan.
