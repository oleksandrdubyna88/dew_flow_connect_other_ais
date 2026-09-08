# PLAN — a progress note is never a reason: the LOCAL shim's half

> Status: **plan only, nothing implemented yet.** Scope:
> `src_mcp/runners/Reviewers/BoundedScheduler.cs` (`IsScaffolding`), `LocalAsk`, and the tail the
> executor keeps in `ReviewerExecutor.cs`.
>
> Related docs: [module_runners.md](../research/module_runners.md), *A progress note is never a
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

- [ ] A queued-out local reviewer reports `LocalAsk.QueuedOutMessage`, whole.
- [ ] The local progress note is owned beside its recogniser, as the remote pair are.
- [ ] A truncated tail never yields a partial first line as the reason.
- [ ] Tests 1–4 written, watched fail, and passing.
- [ ] `research/module_runners.md`'s *A progress note is never a reason* section records the local
      half beside the remote one.
