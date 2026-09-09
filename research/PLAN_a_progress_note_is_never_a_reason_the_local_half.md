# PLAN — a progress note is never a reason: the LOCAL shim's half

> Status: **IMPLEMENTED, 2026-09-08.** Scope:
> `src_mcp/runners/Reviewers/BoundedScheduler.cs` (`IsScaffolding`, `Because`), `LocalAsk`,
> `src_mcp/src/Program.cs`, and the tail the executor keeps in `ReviewerExecutor.cs`.
>
> Related docs: [module_runners.md](module_runners.md), *A progress note is never a
> reason (2026-09-07)* — which fixed exactly this for the REMOTE shim and did not cover this one.

## The symptom, measured

2026-09-08 17:15 UTC, branch `feat/pin-drawn-by-default`. The panel and the round both reported:

```
local/Conventions FAILED after 290.0s: exit 69: l Qwen3.5-35B-A3B-Q5_vk128:latest, pid 52068)
```

The sentence begins mid-word. What actually happened is not a mystery and not a crash: the reviewer
waited its entire five-minute deadline for the local engine and never got the card — three sibling
local reviewers in the same round answered in 1.9 min, 34 s and 35 s. Exit 69 is `EX_UNAVAILABLE`,
which the shim writes for precisely that, and `LocalAsk.QueuedOutMessage` says what to do about it:
give the reviewers more time, run fewer local roles per round, or point the vendor at a second
engine.

**None of that reached the person.** The cure was written, the shim printed it, and the round
displayed forty-five characters of an unrelated line.

## The chain, all three links

1. The shim writes PROGRESS to the same stderr as its verdict:
   `waiting for the local engine at <endpoint>: N ahead, Xs so far (model …, pid …)`, repeatedly
   (`Program.cs:370`), and then the verdict (`QueuedOutMessage`) before exiting 69.
2. `ReviewerExecutor` keeps the **last 400 characters** of stderr (`StdErrTail`, `:535`). The cut
   lands in the middle of a line.
3. `Because` (`BoundedScheduler.cs:502-523`) picks the FIRST non-scaffolding line of that tail —
   which is now the truncated progress note. The verdict is in the tail, directly underneath, and is
   never selected.

**This is the same defect, one shim over.** On 2026-09-07 the remote shim produced
`exit 70: [coai-mcp] claude: running on the Team server at …` — a reviewer described as RUNNING in
the sentence announcing that it had stopped. The fix then was deliberately not a bigger announcement
vocabulary but a named set: `RemoteAsk.IsProgress` owns its two sentences and `IsScaffolding`
excludes them. The local shim's waiting note was never added to that set, and its verdict competes
with a partial line rather than a whole one.

## The change

Three parts, and the third is what makes the first two hold:

1. **`LocalAsk` owns its progress sentence** the way `RemoteAsk` owns its two — one `IsProgress`
   beside the message it recognises, so the recognition cannot drift from the text. `AskLocal`
   builds the note inline today, which is how the remote pair drifted before them.
2. **`IsScaffolding` excludes it**, so `Because` skips it and reaches the verdict underneath.
3. **A truncated leading line is never a reason.** The tail is cut by byte count, so its first line
   may be a fragment of any line, ours or a vendor's. When the stderr was longer than the kept tail,
   the first line of that tail is dropped — it is, by construction, half of something. Without this,
   part 2 fixes today's sentence and the next long vendor error produces the same shape.

## What this does NOT do

- It does not make the reviewer succeed. Waiting out the deadline for a busy card is correct
  behaviour and the message says so; this is about the message arriving whole.
- It does not raise `StdErrTail`. 400 characters is a size, and the defect is a boundary — a bigger
  buffer moves the cut rather than removing it.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `AQueuedOutLocalReviewer_ReportsWhatToDoAboutIt` | The verdict, not a progress note: the reported reason contains the cure and not `pid` |
| 2 | `AProgressNoteIsNeverTheReason_ForTheLocalShimToo` | The named-set exclusion, beside the remote one that already exists |
| 3 | `AReasonIsNeverHalfALine` | Given a stderr longer than the kept tail, the reported reason begins at a line boundary |
| 4 | `AShortStderrKeepsItsFirstLine` | The guard on the other side — dropping the first line unconditionally would lose the reason on every SHORT failure |

## Definition of Done

- [x] A queued-out local reviewer reports `LocalAsk.QueuedOutMessage`, whole.
- [x] The local progress note is owned beside its recogniser, as the remote pair are.
- [x] A truncated tail never yields a partial first line as the reason.
- [x] Tests 1–4 written, watched fail, and passing — plus four the plan round asked for.
- [x] `research/module_runners.md`'s *A progress note is never a reason* section records the local
      half beside the remote one.
- [x] The plan completion check ran and the plan is promoted in the same task.

## What shipped differently

**The truncation is handled where the truncation happens.** The plan left the contract open —
"either by marking the tail or by passing whether it truncated" — and all three plan reviewers found
the same two holes in the flag, from three directions:

- A 400-character cut can land **exactly on a newline**. Dropping the first line then discards a
  complete diagnostic, and a boolean saying only "it was truncated" cannot tell that case from a cut
  mid-word.
- A stderr with **no newline inside the budget** has no first line to drop; dropping it leaves an
  empty tail, so a real failure would be reported as blank.

So `ReviewerExecutor.TailOf` builds the tail out of WHOLE LINES: as many complete trailing lines as
the budget holds, never half of one. `Because` keeps its signature, gains no parameter, and has no
case left to get wrong. A line longer than the entire budget — the one thing this cannot fit — keeps
its OPENING, which is the half `Because` shows anyway.

**A second fallback moved, which the plan did not name.** `Because` ended at "the last line" when
every line was scaffolding, and for a reviewer killed while it was still queuing that is a progress
note again — the same defect wearing the other shoe. A tail that is nothing but progress notes now
says *it was still waiting for its engine when it stopped*; a tail that is nothing but stack frames
keeps the old behaviour, because its last line is at least something a person can search for.
(codex, the plan round.)

**Measured, since three findings turned on it:** `QueuedOutMessage` is 340–359 characters against the
400-character budget, so it fits today, and an endpoint longer than about 85 characters would not.
`StdErrTail` was not raised: the defect was a boundary, and a bigger buffer moves the cut rather than
removing it.

Two findings were rejected with reasons: that a progress note could follow the verdict (the callback
is gone once `AcquireAsync` has returned, and progress is excluded from selection either way), and
that inline construction creates a flush race (both sentences are one `Note()` call each on the same
stream; a completed write cannot land after one that started later).

## What the CODE round changed

It found the plan's own Definition of Done unmet. *"Reports `LocalAsk.QueuedOutMessage`, whole"* — it
did not, and neither the plan nor its first tests noticed, because the test asserted the opening of
the sentence and the cure is its last clause. Measured: the reported reason is capped at 160
characters, `QueuedOutMessage` is 340–359, so what a person actually read ended
`…One caller uses t.` and named no cure at all. (codex, three findings across two roles.)

- **`ShimNotes`** (new) holds the name this program calls itself, the `[coai-mcp] ` prefix `Note` puts
  in front of every line, and the two questions that prefix answers — *did we write this*, and *what
  is the sentence without the tag*. It was a private constant in the shim's `Program`, unreachable
  from the code that reads the stream, and both real findings needed it.
- **The cap is for a vendor's line.** Ours are bounded by the 400-character tail already, so `Quote`
  lets them through whole.
- **`LocalAsk.IsProgress` is anchored** rather than a substring search: `error: waiting for the local
  engine at …: connection refused` would otherwise have been called progress and hidden — the picker
  suppressing the only line that said anything. Three findings, two vendors.
- **`NothingButScaffolding` triggers on ANY progress note**, not every line being one: a reviewer that
  waited and then crashed was sent back to quoting a stack frame. (gemini.)

Rejected with reasons, and worth naming because both sounded right: that the CRLF arithmetic breaks
the boundary (measured — a cut between the CR and the LF still leaves the next line whole; a test now
covers every offset around the pair), and that `meaningful[0]` returns the first progress note rather
than the verdict (progress notes are filtered out before that index is taken, which is what the
passing test shows).

## Evidence

| reverted | test | failure |
|---|---|---|
| `LocalAsk.IsProgress` returning false | `AQueuedOutLocalReviewer_ReportsWhatToDoAboutIt` | `Expected sentence "exit 69: [coai-mcp] waiting for the local engine at …: 2 ahead, 60s so far (model Qwen3.5-35B-A3B-Q5_vk128:latest, pid 52068)" to contain "was busy for the whole"` — the reported defect, reproduced |
| the sentence built inline | `AProgressNoteIsNeverTheReason_ForTheLocalShimToo` | `Expected LocalAsk.IsProgress(…) to be True … but found False` |
| the tail cut by characters | `AReasonIsNeverHalfALine` | `Expected tail to start with "[coai-mcp] line " … but "xxxxxxxxxxxx…"` |
| the same | `ATailCutExactlyAtALineBoundaryKeepsEveryWholeLine` | `Expected tail to start with "Error: the whole first line of the tail", but "st line of the tail…"` |
| the same | `AStderrOfOneOverlongLineStillYieldsAReason` | `Expected tail to start with "Error: yyy", but "yyyy…" differs near "yyy" (index 0)` |
| the last-line fallback | `WhenEveryLineIsAProgressNote_TheSentenceSaysSo` | `Expected sentence "exit 69: [coai-mcp] waiting for the local engine at …" to contain "still waiting"` |
| the 160-character cap applied to our own sentence | `AQueuedOutLocalReviewer_CarriesItsCureAndNotJustItsOpening` | `Expected sentence "…so its question was never asked. One caller uses t." to contain "Give the reviewers more time"` |
| `IsProgress` as a substring search | `AnErrorThatMerelyMentionsWaitingIsStillTheReason` | `Expected LocalAsk.IsProgress(vendor) to be False because we did not write this line, but found True` |
| `NothingButScaffolding` requiring EVERY line | `AProgressNoteFollowedByACrashStillSaysItWasWaiting` | `Expected sentence "exit 69: Node.js v20.20.2" to contain "still waiting"` |

Whole suite: **1110 tests, 1109 pass, 0 fail, 1 skipped.**

One test was strengthened after it passed against the unfixed code: `AReasonIsNeverHalfALine` first
used forty lines of one width, and the 400-character cut landed exactly on a newline by arithmetic
accident. Its lines are ragged now. The boundary case it accidentally covered became a test of its
own.
