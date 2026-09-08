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

Coercing is not the whole answer, because a silent `[object Object]` is a defect with no reporter.
So `parseEscalation` validates what its interface promises, and a question whose file is malformed
is SKIPPED with a line in the log — the shape it already uses for a file that will not parse.

## Build order

1. `escapeHtml` and `escapeHtmlForHighlighting` coerce, matching their in-page twin. One line each,
   and it removes the whole class from every caller at once.
2. `repoNameOf` coerces the same way.
3. `parseEscalation` checks every field the `Escalation` interface declares, and returns `undefined`
   for a file that does not match — with the reason logged, so a skipped question is not a silent one.
4. `panelProvider` subscribes to `onDidDispose`, clears its handle, and stops posting into a promise
   nobody reads.

## Test plan

Every one fails first.

| # | Test | Holds |
|---|---|---|
| 1 | `TheSidebarStopsPaintingOnceItsViewIsDisposed` | A disposed view is cleared, and a render afterwards is a no-op rather than a throw |
| 2 | `APostToADisposedViewIsNotAnUnhandledRejection` | The `void postMessage` that VS Code surfaces as a notification |
| 3 | `EscapingSurvivesAValueThatIsNotAString` | The exact failure: a number, an object and `undefined` through `escapeHtml` |
| 4 | `ARepoPathThatIsNotAStringDoesNotStopTheLog` | The same for `repoNameOf` |
| 5 | `AQuestionFileMissingAFieldIsSkipped_NotRendered` | `parseEscalation` validates what its type claims |
| 6 | `ASkippedQuestionSaysWhyInTheLog` | Skipping silently would trade a crash for a question nobody sees |

`src_vs_code/src/test` already tests both halves; these go beside them.

## Definition of Done

- [ ] The sidebar cannot write to a disposed view, and a post to one is not an unhandled rejection.
- [ ] No escaper or path helper throws on a non-string.
- [ ] `parseEscalation` validates every field it declares, and says why it skipped a file.
- [ ] Tests 1–6 written, watched fail, and passing.
- [ ] `research/module_extension.md` records the disposal rule for the sidebar view beside the two
      panels that already had it.

## What this does NOT do

- It does not determine which field was the non-string on that person's machine. If their
  `escalations/` file can be captured, it is worth one look — but the fix does not wait on it.
- It does not add a schema validator library. Four field checks in a function that already exists
  are cheaper than a dependency, and the interface is the schema.
