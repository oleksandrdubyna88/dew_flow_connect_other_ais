# PLAN — two errors that stop a person answering a question

> Status: **plan only, nothing implemented yet.** Scope:
> `src_vs_code/src/panelProvider.ts`, `src_vs_code/src/escapeHtml.ts`,
> `src_vs_code/src/escalations.ts`, `src_vs_code/src/roundsLog.ts`.
>
> Related docs: [module_extension.md](../research/module_extension.md).

## The symptom

2026-09-08. A person ran a plan review, the gate asked them a question, and they opened the rounds
log to answer it. Two error notifications:

```
Webview is disposed
e.replace is not a function
```

The round was still running — the sidebar showed `plan review 1`, `running · 0 gating`, one reviewer
done and one in flight. **The one moment a question is waiting on a person is the one moment the
surface for answering it threw twice.**

## 1. `Webview is disposed` — established

`panelProvider.ts` holds `private view?: vscode.WebviewView` (`:141`), assigns it in
`resolveWebviewView` (`:227`), and **never subscribes to `onDidDispose`**. The two other webview
owners in this extension both do — `helpPanel.ts:70` and `roundsLogPanel.ts:74` — and both null
their handle there, which is why neither of them produces this.

VS Code disposes a webview VIEW when it is hidden. The handle survives, and the next `render()`
writes into it:

- `:466` assigns `this.view.webview.html = …`, which throws synchronously; or
- `:476` does `void this.view.webview.postMessage(…)`, whose rejection nobody handles — and an
  unhandled rejection is what VS Code shows as a notification.

The conditions are exactly the screenshot's: the escalation watcher ticks every five seconds, a
round in flight rewrites its session file constantly, and opening the log hides the sidebar.

**The cure**: subscribe on resolve, clear the handle, and treat "no view" as the ordinary state it
is. `postMessage` also stops being fire-and-forget — a rejection is caught and dropped, because a
disposed view is not an error a person can act on.

## 2. `e.replace is not a function` — the class is established, the instance is not

`escapeHtml(text: string)` calls `text.replace(…)` directly (`escapeHtml.ts:18`), and so does
`repoNameOf(repoPath: string)` (`roundsLog.ts:326`). Their parameter types say `string`; TypeScript
erases types at run time, and **every value they are given comes from JSON on disk**:

- `parseEscalation` (`escalations.ts:31`) validates **two of ten fields** — `id` and `question` —
  and passes the rest through `{ openFindings: [], ...parsed } as Escalation`. `branch`, `repoPath`,
  `askedUtc`, and every finding's `severity`, `category`, `file` and `title` are taken on trust, and
  `questionsHtml` escapes all of them.
- Session rounds are read the same way, and `repoNameOf(session.state.repoPath)` calls `.replace`
  with no guard at all.

Any one of them holding a number or an object produces this message verbatim. **Which one it was
cannot be determined from here** — that needs the person's `escalations/*.json` or an extension-host
stack, and inventing a field would be a guess dressed as a diagnosis.

**So the fix is not to find the field; it is to make the failure impossible.** An HTML escaper is the
last thing before text reaches a document, and it already has a twin that gets this right: the
in-page `esc` in `roundsLog.ts:836` does `String(value)` first. A page that renders `[object Object]`
is strictly better than a log that will not open — the person can still answer their question, and
the wrong-looking value is visible rather than fatal.

Coercing is not the whole answer on its own, and the plan round inverted what the rest of it should
be. **The first draft said `parseEscalation` should validate every field its interface declares and
SKIP a file that does not match.** Both gemini and codex refused that, and they are right: the file
being validated is the question a person is waiting to answer. Dropping it because `repoPath` is a
number leaves the round gated with nothing on screen to answer — a crash traded for a hang, which is
worse, because a crash at least says something happened.

**So the existing two-field check is correct and stays.** `id` and `question` are what make a
question answerable; everything else is metadata around it. Metadata is SANITISED rather than
trusted or rejected — a missing `branch` renders as empty, not as `undefined`, and never as a reason
to hide the question.

## Build order

1. `escapeHtml` and `escapeHtmlForHighlighting` coerce — `value == null ? '' : String(value)`, not a
   bare `String(value)`, because the bare form renders a missing field as the word `undefined`.
   One line each, and it removes the whole class from every caller at once.
2. `repoNameOf` coerces the same way.
3. `parseEscalation` keeps its two-field check and SANITISES the rest: every declared string field
   becomes a string, `openFindings` keeps only entries that are objects, and a question is skipped
   only when `id` or `question` is unusable — which is what it already did. A field that had to be
   sanitised is named in the log, so a `[object Object]` on screen has a reporter.
4. `panelProvider` subscribes to `onDidDispose` and clears its handle **only when the disposed view
   is still the current one** — a delayed callback from view A must not blank view B after a
   re-resolve. The render is additionally wrapped, because `onDidDispose` can fire between the null
   check and the write, and a null check cannot close that window on its own.
5. A rejected `postMessage` is dropped ONLY when it is the disposal we expect; anything else is
   logged, because a live view refusing a message is a different problem with no other reporter.

## Test plan

Every one fails first.

| # | Test | Holds |
|---|---|---|
| 1 | `TheSidebarStopsPaintingOnceItsViewIsDisposed` | A disposed view is cleared, and a render afterwards is a no-op rather than a throw |
| 2 | `ADisposalFromAnOldViewDoesNotBlankTheNewOne` | The re-resolve race: view A's late callback must not clear view B |
| 3 | `ARenderThatLosesItsViewMidWayIsStillNotAThrow` | The window a null check cannot close |
| 4 | `EscapingSurvivesAValueThatIsNotAString` | The exact failure: a number, an object, `null` and `undefined` through `escapeHtml` |
| 5 | `AMissingValueEscapesToNothing_NotToTheWordUndefined` | `String(undefined)` is a defect of its own |
| 6 | `ARepoPathThatIsNotAStringDoesNotStopTheLog` | The same for `repoNameOf` |
| 7 | `AQuestionWithBadMetadataIsStillAnswerable` | **The inversion.** A question whose `branch` is a number renders and keeps its Answer button |
| 8 | `AQuestionWithNoUsableIdIsSkipped` | The one case that still skips, and the guard that it is the only one |

`src_vs_code/src/test` already tests both halves; these go beside them.

## Definition of Done

- [ ] The sidebar cannot write to a disposed view, and a post to one is not an unhandled rejection.
- [ ] A late disposal from a replaced view does not blank the live one.
- [ ] No escaper or path helper throws on a non-string, and none renders `undefined` as a word.
- [ ] A question with bad metadata is still ANSWERABLE; only an unusable id or question is skipped.
- [ ] Tests 1–8 written, watched fail, and passing.
- [ ] `research/module_extension.md` records the disposal rule for the sidebar view beside the two
      panels that already had it.

## What this does NOT do

- It does not determine which field was the non-string on that person's machine. If their
  `escalations/` file can be captured, it is worth one look — but the fix does not wait on it.
- It does not add a schema validator library. Four field checks in a function that already exists
  are cheaper than a dependency, and the interface is the schema.
