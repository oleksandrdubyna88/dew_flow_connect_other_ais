# PLAN — the busy mark on every webview, not only the panel's two pages

> Status: **plan only, nothing implemented yet, 2026-10-02.** Scope: the extension's other webviews, each with its
> own message loop. The open tail of [PLAN_model_search_and_busy_marks.md](../research/PLAN_model_search_and_busy_marks.md)
> (its §8), extracted when that plan was promoted.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## 1. The goal

The operator asked on 2026-10-02 for a progress bar or a spinner *everywhere* an action takes longer than half a
second. The sidebar and the Settings tab have it (`busyMark.ts`, `inFlight.ts`, `renderTracker.ts`). The other
webviews post to the host and show nothing until it answers.

## 2. The sites (verified 2026-10-02, at the promotion commit)

Each has its own `onDidReceiveMessage` and its own page script:

| Webview | Handler | What it already shows while working |
|---|---|---|
| `chatPanel.ts` | `:207` | a turn has its own running state |
| `roundsLogPanel.ts` | `:150` | nothing — reads the database through `coai-mcp` |
| `bugzReviewPanel.ts` | `:301` | an in-flight map of its own, per fetch |
| `rolesPanel.ts` | `:157` | nothing |
| `phrasesPanel.ts` | `:177` | nothing |
| `commandsPanel.ts` | `:64` | nothing |
| `chatPresetsPanel.ts` | `:224` | nothing |
| `notificationsPanel.ts` | `:138` | nothing |
| `helpPanel.ts` | `:90` | nothing (navigation only — likely nothing to mark) |
| `bugsKeysPanel.ts` | `:123` | a `Turns.busy` flag (`bugsKeysTurns.ts`) |
| `chatRestorePanel.ts` | `:143` | nothing |

## 3. What must be true when it is done

1. Every webview whose host work can exceed `BUSY_AFTER_MS` numbers its posts through the same `busyMarkScript`
   fragment and settles them through the same `tracked()` — one implementation, included, never copied.
2. A webview whose actions are all instant (navigation, a clipboard copy) is listed as such, with the reason, rather
   than given a mark that never shows.
3. Where a webview already shows its own running state (a chat turn, the bugz fetch map), the plan says whether the
   bar replaces it, sits beside it, or is not added — decided per page, with the reason.

## 4. Build order

1. Measure: for each webview, which messages take longer than 500 ms on this machine (the rounds log's database read
   is the obvious one). The answer decides the scope.
2. Generalise `tracked()`'s `slots` parameter to a webview that is not a `SurfaceSlot` (it already takes any
   `Poster`).
3. One webview at a time, starting with the one the measurement says is slowest.

## 5. Test plan

The same as the panel's: a page RUN on `PageClock` for each page that gains the mark (nothing at 499 ms, the bar at
500, gone on `settled`), a value test for any new host seam, and each line's teeth checked by deleting it.

## 6. Growth surfaces

None new: the `InFlight` record per webview is bounded and settled the same way.

## 7. Definition of Done

- [ ] Every webview is either marked or listed as instant, with the reason.
- [ ] One fragment and one `tracked()` — no copy.
- [ ] Page tests run on the fake clock, with teeth; docs updated; this plan promoted.
