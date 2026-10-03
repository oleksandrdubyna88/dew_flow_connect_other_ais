# PLAN — the busy mark does not count the time a person spends answering VS Code

> Status: **IMPLEMENTED, 2026-10-02.** One epic, one pull request. Plan gate proceed (1 of 1, gemini; 4 accepted,
> 1 rejected); code gate proceed (4 of 4; 1 accepted — this promotion). Deviations in §7. The release (extension
> 0.61.1, its CHANGELOG section on the release-please PR) follows the merge.
> Scope: `src_vs_code/src/inFlight.ts`, `busyMark.ts`, two new modules (`personWait.ts`, `busySnapshot.ts`), every VS
> Code prompt site in `src_vs_code/src` (wrapped, not changed), `notify.ts` (`notifyAndAsk`), tests,
> `research/module_extension.md`, `research/module_tests.md`.
>
> Related docs: [PLAN_model_search_and_busy_marks.md](PLAN_model_search_and_busy_marks.md) (the busy mark
> this corrects, Epic 3), [module_extension.md](module_extension.md).

## 1. Symptom

Extension 0.61.0 draws a thin bar (and `aria-busy` on the pressed control) for any panel action the host has not
finished 500 ms after the press. Several actions open a VS Code input box, quick pick, file dialog or modal question
before they do their work — *Add a reviewer*, *Add a Team server*, a custom model, the consultant's own endpoint,
*Close consultation*, the bugs key, the data directory moves. The action is "in flight" for the whole time the box is
open, so the bar runs while the person is typing. The operator's ruling (2026-10-02): **"пока печатаю не считаем"** —
time spent waiting on the person is not work, and must not be shown or counted as work.

## 2. Goal

- While an operation is waiting for the person, neither the page's own half of the mark nor the host's half shows
  it, and its clock is stopped.
- When the person answers, the clock restarts from where it stopped: the bar appears once the operation's **working**
  time passes 500 ms. Work done before the box opened still counts.
- An operation already showing the bar when a box opens loses the bar and the control's `aria-busy` while the box is
  open (unless another operation on the same control is still due).
- Nothing changes for an action that opens no box, nor for a prompt opened outside a tracked panel operation.

## 3. Design

### 3.1 Which operation is waiting — `AsyncLocalStorage`, not a parameter

The prompts sit 1–4 calls below `run()` (`panelProvider.ts:2021`), in helpers shared with commands that are not panel
operations at all (`dataCommands.ts`, `notify.ts`). Threading an operation id through every helper would touch every
signature on the way. Node's `AsyncLocalStorage` (`node:async_hooks`, available in the extension host; esbuild
bundles with `--platform=node`, so the builtin stays external) carries the current operation through every `await`
below the point where `tracked` (`inFlight.ts:112`) starts the work.

New module `src_vs_code/src/personWait.ts`:

```ts
export interface Waiting { pause(): void; resume(): void }
export function whileWorking<T>(waiting: Waiting, work: () => T): T;     // AsyncLocalStorage.run
export async function askPerson<T>(prompt: () => Thenable<T>): Promise<T>; // pause → await → resume in finally
```

`askPerson` reads the store synchronously before opening the prompt; with no store (a command from the palette, a
startup question) it simply runs the prompt. `resume` is in a `finally`, so a rejected or cancelled prompt still
resumes.

### 3.2 The host's record — `InFlight.pause` / `resume`

Each `Operation` gains `waits` (how many prompts it has open — two at once is legal, a nested helper) and `pausedAt`.

- `pause(id)` → `true` only on the 0 → 1 transition; records `pausedAt`.
- `resume(id)` → the working time so far (`spentMs`) only on the 1 → 0 transition, `undefined` otherwise; moves
  `startedAt` forward by `now - pausedAt`, so `oldestMs` is working time only — across any number of prompts in turn
  (plan round, gemini: a second prompt must not count the first one's typing).
- Both are no-ops on an id already finished (a prompt that outlives its operation must not throw or resurrect it).
- `snapshot()` counts only entries with `waits === 0`.

Entries stay immutable: each change replaces the map, as today.

### 3.3 Telling the page — two new messages

`tracked` runs `work` inside `whileWorking(...)`. Its `Waiting`:

- `pause` → when `flight.pause(id)` transitions: post `{type: 'waiting', seq, doc}` to the poster, then `announce`.
- `resume` → when `flight.resume(id)` transitions: post `{type: 'working', seq, doc, spentMs}` to the poster, then
  `announce`. `spentMs` is the operation's working time so far, by the host's clock — the page keeps no clock of its
  own for the pause, so there is one measurement of it, and the one a test can drive.

Both posts go through `postTo` (a closed page must not stop the others hearing the count).

### 3.4 The page — `busyMark.ts`

Each local entry gains `waiting`. The timer callback stops mutating its closure's entry and
calls `markDue(seq)`, which replaces the entry (consistent with the rest of the fragment).

- `waiting(seq, doc)` for this document: clear the timer, replace the entry with
  `{due: false, waiting: true}`, remove the control's `aria-busy` unless another due entry holds the same control,
  `drawBusy()`.
- `working(seq, doc, spentMs)` for this document: `left = max(0, busyAfter - spentMs)`; if `left` is 0 mark due
  now, else start the timer for `left`.
- `settled` while waiting clears the entry exactly as today.

### 3.5 Where `askPerson` goes

Every site that waits for the person, in `src_vs_code/src`:

- `panelProvider.ts` — the 15 `showInputBox` / `showQuickPick` sites (lines 2400, 2421, 2751, 2759, 3098, 3113, 3149,
  3159, 3536, 3582, 3716, 3734, 3789, 3816, 3826).
- `notify.ts:319` `notifyAndAsk` — its `await show(notice)`. This covers every modal confirmation the panel awaits
  (`panelProvider.ts` 1322, 1378, 1566, 2281, 2332, 2380, 2528, 3193, 3296, 3897 and the helpers'). `notify`,
  `notifyOnce` and `notifyThen` do not wait for the person and are not wrapped.
- The prompt sites in `bugsKeysPanel.ts`, `chatPresetsPanel.ts`, `configTransferCommands.ts`, `dataCommands.ts`,
  `escalationWatcher.ts`, `extension.ts` (`showInputBox`, `showQuickPick`, `showOpenDialog`, `showSaveDialog`). Outside
  a panel operation the wrapper is a plain call; wrapping them all is what lets one structural rule hold (§5).
- NOT `conversationPickerCommand.ts:197` (`createQuickPick`): `switchConversations` is synchronous, shows the picker
  and returns, so no operation ever waits on it — wrapping it would pause nothing. The guard names it as the one
  exception, with this reason.

A prompt an operation does NOT await (started with `void`) would pause an operation that keeps working; there are
none today (checked: no `void` before a prompt or `notifyAndAsk`), and the guard in §5 keeps it so.

### 3.6 Not in scope

- The other webviews' busy marks — still [PLAN_busy_marks_on_every_webview.md](PLAN_busy_marks_on_every_webview.md).
- A VS Code progress notification during work — separate, not asked for.

## 4. Build order

1. RED: `inFlight.test.ts` — pause/resume semantics, snapshot, no-op after finish, nested waits, `tracked` posts
   `waiting`/`working` and announces 0 while waiting, a rejected prompt still resumes. New `personWait.test.ts` —
   store propagates through awaits, `askPerson` outside an operation is a plain call.
2. RED: `busyMarkPage.test.ts` — page half pauses and resumes on the messages, counts working time only, clears and
   restores `aria-busy` per control, ignores another document's numbers.
3. RED: `promptsWaitForThePerson.test.ts` — structural: every prompt call in `src_vs_code/src` sits inside
   `askPerson(`, `notifyAndAsk` wraps its `show`, and no prompt is preceded by `void`.
4. GREEN: `personWait.ts`, `InFlight`, `tracked`, `busyMark.ts`, then the site wrapping; regenerate the notification
   inventory if `count-notifications.mjs` moves lines.
5. Teeth: revert each half (host pause, page pause, one wrapped site) and watch the matching test go red.
6. Docs, CHANGELOG (extension 0.61.1), full `npm test`, lint (complexity 4, ≤ 50 lines per function).

## 5. Test plan

| Test | Proves |
|---|---|
| `inFlight.test.ts` (extended) | a waiting operation leaves the count; its clock skips the wait; a late resume is harmless; `tracked` tells the poster and every page |
| `personWait.test.ts` (new) | the operation is found below several awaits; no operation → plain call; a rejecting prompt still resumes |
| `busyMarkPage.test.ts` (extended) | run the real page: press → box opens at 200 ms → typed for 5 s → no bar; answer → bar at 300 ms more of work; bar already up → box opens → bar and `aria-busy` gone |
| `promptsWaitForThePerson.test.ts` (new) | a new prompt site added without `askPerson` is red — and its companion feeds the scan an unwrapped prompt, a wrapped one and a comment naming one, so a scan that matches nothing cannot pass (plan round, gemini) |

## 6. Definition of Done

- [x] Plan gate passed; code gate passed (per epic: this is one epic).
- [x] Every test above RED first, then GREEN, with teeth checks recorded (`research/module_tests.md`).
- [x] `npm test` green; lint clean; suppression file shrank by one (`dataCommands.ts` complexity 4 → 3).
- [x] `research/module_extension.md`, `research/module_tests.md` updated; this plan promoted to `research/`.
- [ ] CHANGELOG section for extension 0.61.1; released and verified on the Marketplace — after the merge, on the
      release-please PR, as every extension release here is.

## 7. Deviations — what shipped differently

- **The page keeps no clock for the pause.** The plan had the page measure its own `spent`. The page harness runs on
  the real `Date.now`, and two clocks measuring one pause can disagree. So `working` carries `spentMs`, measured by
  the host's clock, and the page restarts its timer from it. The plan round's first finding (gemini: a second prompt
  counted the first one's typing) applied to the old shape. It is covered by a host test of two prompts in turn.
- **The conversation picker is not wrapped.** Its `createQuickPick` is shown by a synchronous `switchConversations`
  that nothing awaits, so there is nothing to pause; the guard names it as the one exception.
- **A second new module, `busySnapshot.ts`.** The full suite found it, not the plan. `panelView.ts` imported `IDLE`
  from `inFlight.ts`, so the webview bundles reached `personWait.ts`, and its module-level `AsyncLocalStorage` kept
  `require('node:async_hooks')` in a bundle that runs with no `require`. Nine bundle tests went red. The snapshot
  type and `IDLE` now live in a module with no imports.
- **The structural guard has a companion** (plan round, gemini). It feeds the scan an unwrapped prompt, a wrapped
  one and a comment, so a scan that matches nothing cannot pass.
- **Rejected at the plan round**, with the reason recorded: patching `vscode.window` globally instead of one wrapper.
  Modal questions already have a single road (`notify.ts` is the only caller of `show*Message`), and `askPerson` plus
  its guard is the road for prompts.
