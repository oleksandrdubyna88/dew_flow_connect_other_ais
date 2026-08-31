# PLAN — the escalation loopback: reaching a human from the server

> Status: **IMPLEMENTED, 2026-08-31.** Both stories shipped. Verified live on this machine: the
> installed `coai-mcp` asked a question, the installed extension showed it, the operator answered
> "да" in the modal, and the server returned `{"status":"answered","answer":"да"}` to the caller.
>
> **Deviations from the plan:**
> - The file shape won, as the plan argued — and the UI grew beyond the modal on the operator's
>   call: a **status-bar item** and an **open-questions section at the top of the rounds view**, so
>   a dismissed modal never loses a question that is blocking a round.
> - A **5-second poll backs the file watcher**. A `FileSystemWatcher` on a path outside the
>   workspace is not guaranteed on every platform, and "you will see the question" has to be true
>   rather than likely.
> - The budget is **30 minutes** (`COAI_ESCALATION_MINUTES`), plus `COAI_ESCALATION_SECONDS`
>   which wins when set: minutes are what a person configures, seconds are what a test needs. One
>   knob would have had to lie about one of the two.
> - The timeout answers `no_answer_yet` with the instruction to ask in the chat — the family's
>   `remote-ask` fallback — rather than the plan's unspecified "named timeout".
> - An **empty** answer is treated exactly like a malformed one: silence with a file around it is
>   still silence. The plan named only the malformed case.
> - Story 2's "already answered raises no modal" is enforced by prompt-once-per-id, which also
>   covers the case the plan did not name: a poll must not re-raise a modal a person just closed.

## The symptom

`ask_human` cannot reach a human. When a stage exhausts its rounds with findings still gating, the
verdict says `call_human` and the tool returns a refusal telling the main AI to surface the
question itself. That works — the model does surface it — but it means the escalation lands in a
terminal the person may not be looking at, and there is nowhere to answer it *from*.

Everything else in the product shipped without a loopback and is better for it: settings ride one
way in the `mcpServers` env block, and the rounds view reads the server's own session files. The
escalation is the ONE case where the server needs to reach the editor, so it is the only reason to
open a port at all — and that is worth deciding deliberately rather than inheriting.

## Two shapes, and the one worth building

| | A person answers in VS Code (loopback) | A person answers wherever they are (file poll) |
|---|---|---|
| Mechanism | extension listens on 127.0.0.1; server posts and blocks | server writes `escalations/<id>.json`, watches for `<id>.answer.json` |
| Cost | a port, a token, a lifetime, a health story | a directory watch on both sides |
| Reaches | the machine's own VS Code | VS Code, a phone through a synced folder, another agent |
| Precedent | CredsForDevs' broker (proven, and its complexity is why it is proven) | none here |

**Recommendation: the file shape.** The server already owns a data dir the extension already
reads; an escalation is a file in it, and an answer is a file beside it. No port, no token, no
"is the window up" question — the same reason the rounds view needed no protocol. If it turns out
a person wants a real modal, the extension can raise one *from the watch* without the server ever
learning about it.

## Stories

### 1 — The escalation file protocol

Server: `ask_human` writes `escalations/<id>.json` (question, session, findings, asked-at) and
waits for `<id>.answer.json` up to a configurable timeout (default 30 min), then returns either the
answer or a named timeout. Extension: watches the directory, raises a modal, writes the answer file.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `AskHuman_WritesTheQuestionFile` | file appears with the question and session id |
| 2 | `AnswerFileAppears_ToolReturnsIt` | the tool's reply carries the person's text |
| 3 | `NoAnswerBeforeTheTimeout_IsANamedOutcome` | not an exception, and not a silent proceed |
| 4 | `MalformedAnswerFile_IsIgnored_TheWaitContinues` | a half-written file never resolves a question |
| 5 | `TwoConcurrentEscalations_DoNotCrossAnswers` | ids keep them apart |
| 6 | `ServerKilledMidWait_LeavesNoLock` | the next run is unaffected |

### 2 — The modal, and the person's answer

Extension: on a new escalation file, a modal with the question and the open findings; the answer is
written back. Answering from another editor window must not double-answer.

**Test cases**

| # | Test | Expected |
|---|---|---|
| 1 | `NewEscalationFile_RaisesTheModal` | against a fake watcher, pure |
| 2 | `AnswerIsWrittenAtomically` | temp + rename; no half file for the server to read |
| 3 | `AlreadyAnsweredEscalation_RaisesNoModal` | reopening VS Code does not re-ask |
| 4 | `DismissedModal_LeavesTheQuestionOpen` | dismissing is not answering |

## Definition of Done

- [ ] `ask_human` reaches a person and returns their answer, or a named timeout.
- [ ] No port is opened by either half.
- [ ] The static refusal in `Tools.cs` is replaced, and `module_server.md` updated to match.
