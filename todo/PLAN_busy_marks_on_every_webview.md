# PLAN — the busy mark on every webview, not only the panel's two pages

> Status: **plan only, nothing implemented yet, 2026-10-03.** Plan gate proceed (1 of 1, gemini; 4 accepted, 1 rejected). Scope: the extension's other webviews, each with its
> own message loop. The open tail of [PLAN_model_search_and_busy_marks.md](../research/PLAN_model_search_and_busy_marks.md)
> (its §8), extracted when that plan was promoted. Three epics, one branch, one plan round, one code round per epic.
>
> Related docs: [module_extension.md](../research/module_extension.md),
> [PLAN_busy_mark_pauses_while_you_type.md](../research/PLAN_busy_mark_pauses_while_you_type.md).

## 1. The goal

The operator asked on 2026-10-02 for a progress bar or a spinner *everywhere* an action takes longer than half a
second, and on 2026-10-03 for this plan to be built. The sidebar and the Settings tab have it (`busyMark.ts`,
`inFlight.ts`, `renderTracker.ts`, and `personWait.ts` so that waiting on the person is not counted). The other
webviews post to the host and, for most of their actions, show nothing until it answers.

## 2. What was measured (2026-10-03, this machine)

The dominant slow path is a read of the rounds database. It spawns `coai-mcp`
(`roundsDbRead.ts:77`, `--log --paged`):

| What | Time |
|---|---|
| A rounds-log page read (`coai-mcp --log --paged --limit 50`) | 694, 930, 818 ms |
| A bare spawn (`coai-mcp --version`) | 354, 324 ms |

So every action that reads or writes through `coai-mcp` crosses 500 ms on every press: the rounds log's commands,
and the bugz review's `decide`, `comment` and `fetchReal`. The bugs-keys page's actions end in a network `fetch`
(`bugsAdminApi.ts:181`), and the roles and commands pages read every prompt or command file on a structural change.
Settings writes (`config.update`) are short and were not timed; they are classed by what they await.

## 3. Every webview, decided

The survey behind this table is a read of each page's `onDidReceiveMessage` and its page script (2026-10-03).

| Webview | Slow actions | Decision | Why |
|---|---|---|---|
| Rounds log (`roundsLogPanel.ts:152`) | every `command`: answer, usageWindow, spotsPeriod, forgetUsage, forgetChat, closeConsultation, export, findings | **marked** (E1) | Every one spawns `coai-mcp` (0.7–0.9 s measured). Its own "Reading what this round found…" stays beside the bar. |
| Bugz review (`bugzReviewPanel.ts:301`) | decide, comment, fetchReal, openAt, openCurrent, openTree, calls, choose, openCall | **marked** (E2) | `decide` is a server write plus a re-read, with no indicator at all. The per-row pending map and "asking…" stay beside the bar. |
| Bugs keys (`bugsKeysPanel.ts:124`) | setkey, refresh, next, back, issue, revoke, discard (`copy` and `dismiss` are local: not numbered) | **marked** (E2) | A network round trip. `Turns` already disables every button at once. That says "wait", not "working", so the bar sits beside it. |
| Roles (`rolesPanel.ts:157`) | add, addPrompt, removePrompt, restorePrompt, remove, finishDeletion, and a role switched on | **marked** (E3) | File reads of every prompt and a redraw. Typed fields are NOT numbered: typing is settled for 300 ms by design, and a bar over the person's own typing is the 0.61.0 defect again. |
| Commands (`commandsPanel.ts:64`) | add, retitle, switch, restage, remove, restore | **marked** (E3) | Each re-reads every command file (`texts()`). The `text` field is typing and is not numbered. |
| Chat (`chatPanel.ts:207`) | send, retry, restart, pick, showAsked… | **not marked** | It already shows its own running state for each: "Thinking…" with Stop, the composer lock, "Stopping…", "Starting…", "Reading the session…". A second bar would say the same thing twice. |
| Notifications (`notificationsPanel.ts:138`) | markAll | **not marked** | `markAll` already disables itself and reads "Marking everything read…" at once. `shown` is posted by the page itself after each render, not by a press. |
| Chat restore (`chatRestorePanel.ts:143`) | retry | **not marked** | It already reads "Retrying…" and disables the button. |
| Chat presets (`chatPresetsPanel.ts:225`) | add a model (two pickers, two boxes) | **not marked** | The only slow part is the person answering VS Code, which `personWait.ts` already excludes. What is left is a `config.update`. |
| Help (`helpPanel.ts:90`), phrases (`phrasesPanel.ts:177`) | none | **instant** | Settings writes only (`config.update` / `saveSetting`). |

**Found on the way, not fixed here.** The chat presets page posts `edit` on every keystroke
(`chatPresetsPage.ts:216`), and its host writes each one straight through (`chatPresetsPanel.ts:225`). It does not
use the 300 ms settling that roles, phrases and commands use. That defect goes into a new todo plan of its own.

## 4. Design

### 4.1 One fragment, told which posts are work

`busyMarkScript(painted, tracked = PANEL_TRACKED)` gains its second argument: the message `type`s this page
numbers. The panel keeps `['setting', 'prompt', 'command']`. Each other page passes its own list, from the table
above. The rounds log passes `['command']`: every one of its slow actions is a `command` with a sub-command
(plan round, gemini). `send(message, control)` stays the one door. A page that must post a tracked type WITHOUT a number posts it
straight through `vscode.postMessage`, as today. That is what a typed field does.

The other pages build their own HTML (no page uses `pageDocument`). Each one includes `BUSY_BAR` in its body,
`BUSY_CSS` in its stylesheet, and `busyMarkScript(...)` in its script, and ends its script with
`vscode.postMessage({ type: 'ready' })`. One page names its webview API `api`, not `vscode`: the notifications page,
which is not marked, so none of these pages needs a renaming.

### 4.2 One host seam, not a copy per panel

A new `busyHost.ts` exports `BusyHost`, one per webview panel. It holds an `InFlight` and the webview as its one
`Poster`:

- `track(message, work)` runs `tracked()` when `askOf(message)` finds a number, and runs `work` plainly otherwise.
- `heard(message)` answers a `ready` with `{type: 'busy', ...snapshot}` and says it consumed it.
- `snapshot()` is painted into pages that replace their whole HTML.
- `dispose()` settles everything still running (`settleEverything`).

`tracked()` and `askOf()` are reused unchanged. Through `whileWorking`, so is the rule that a VS Code prompt
pauses the mark.

### 4.3 Pages that replace their whole HTML

Bugz review, bugs keys, roles and commands repaint by replacing the document. The page's own numbers die with it,
just as when the sidebar repaints. What survives is the host half. The builder takes the host's snapshot and passes
it to `busyMarkScript(painted, …)`, and the new document's `ready` is answered. So a repaint in the middle of an
action keeps the bar for what is still running.

### 4.4 When a write through `settledWrites` is done

The roles and commands pages queue every write through `settledWrites`, whose `queue()` returns nothing. It is
widened: `queue(command)` returns a `Promise<void>` that settles when THAT command has been applied and the host's
render function has returned (its awaited file reads and the `webview.html` assignment — never Chromium's paint;
plan round, gemini), whether it succeeded or failed. A typed field's promise settles when its settled write lands. That
widens the existing module rather than adding a second queue (reuse-first, step 2.1). The host tracks a structural
command with `busy.track(message, () => writes.queue(command))`.

## 5. Epics and build order

- **E1 — the seam, and the rounds log.**
  1. RED: a `BusyHost` value test; the panel's page tests still pass with the default list; the rounds log RUN on a
     fake clock shows nothing at 499 ms, the bar at 500 ms, and nothing after `settled`, for a `command`.
  2. GREEN: `busyMarkScript`'s list, `busyHost.ts`, the rounds log page and its host.
- **E2 — bugz review and bugs keys.** The same RED page tests on each page's runner; painted snapshots on a repaint;
  each existing indicator still drawn.
- **E3 — roles and commands.** The `settledWrites` promise (RED value tests: settles after apply and redraw, on
  failure, and for a typed field after it settles); then the two pages, with typing never numbered.
- After each epic: teeth (delete the new line, watch the named test go red), docs, `npm test`, lint, one code round
  (`again: true` after the first).

## 6. Test plan

| Test | Proves |
|---|---|
| `busyHost.test.ts` | track numbers and settles through `tracked`; an unnumbered post runs plainly; `ready` is answered with the snapshot; dispose settles all |
| `busyMarkPage.test.ts` (extended) | a page with its own list numbers only those types; the panel's default is unchanged |
| a page test per marked page, on a fake clock | nothing at 499 ms, the bar at 500 ms, gone on `settled`; a painted snapshot draws the bar after what is left of the delay; a typed field is never numbered (roles, commands) |
| `settledWrites.test.ts` (extended) | the queue's promise settles after apply and redraw, on failure, and for a typed field after it settles |

The runners, budgeted per epic (plan round, gemini). The rounds log's runner in `roundsLogPaging.test.ts` is
extracted to a shared `roundsLogPageHarness.ts` (E1). `bugzReviewPage.test.ts`, `bugsKeysPage.test.ts` (E2),
`rolesPageHarness.ts` and `commandsPage.test.ts` (E3) already run their scripts. Each gains the panel's exported
`PageClock` as its `setTimeout`/`clearTimeout`, which is reused, not copied.

## 7. Growth surfaces

None new: one `InFlight` per open webview, bounded by what one person presses while it works, settled on dispose.

## 8. Definition of Done

- [ ] Every webview is marked or listed with its reason (§3).
- [ ] One fragment, one `tracked()`, one `BusyHost`. No copies.
- [ ] Page tests run on a fake clock, with teeth; `npm test` green; lint clean; suppression file did not grow.
- [ ] The chat-presets per-keystroke write is filed as its own todo plan.
- [ ] Docs updated (`research/module_extension.md`, `research/module_tests.md`), this plan promoted, extension
      released and verified on the Marketplace.
