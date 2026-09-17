# PLAN — a question can wait its turn

> Status: **IMPLEMENTED, 2026-09-17.** Scope: `src_vs_code/src/chatPage.ts`,
> `src_vs_code/src/chatCommand.ts`, `src_vs_code/src/chatMessages.ts`, `src_vs_code/src/chatPanel.ts`
> — the composer while an answer is running.
>
> Issue: [#288](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/288).
> Related docs: [module_extension.md](module_extension.md).
>
> **What shipped differently from this document**, which is the part of a record worth keeping:
>
> 1. **The queue decisions are their own module, `chatQueue.ts`.** The plan put them in
>    `chatCommand.ts`, which needs a `vscode` host the suite has none of — so every rule in it
>    would have been hoped at rather than driven. The decisions moved out beside `answerCopy` and
>    `chatLedger`; only the wiring stayed.
> 2. **All FOUR doors go through one transition, not two.** The plan noticed the composer and the
>    keybinding. It missed that a re-ask and a retry called `oneTurn` directly, so either could
>    overtake a question already waiting — harmless while the composer locked, because then
>    nothing could be waiting, and not harmless afterwards. `enqueue` is the one door now.
> 3. **The queue does NOT own the composer-appending rule.** A first draft wrote
>    `returnedToComposer`, which is a second implementation of what `pushChatDraft` and the page
>    already do and `chatPage.test.ts` already drives. It was deleted before it shipped;
>    `drained` hands back words in order and nothing else.
> 4. **Eleven existing tests asserted the old lock**, not the two or three the build order
>    implied — including one in the BUNDLED page suite, and a source-structure assertion pinning
>    what a turn is told. Each was rewritten to the new truth rather than deleted, and the one
>    that asserted a second Enter could not reach the host now asserts that it does, because that
>    was the bug.
> 5. **The page stopped locking itself on send.** The plan did not mention it and it is half the
>    fix: the page disabled its own composer the instant it posted, to close a window "the width
>    of a second Enter". That window is the feature.
> **Through the plan gate on 2026-09-17** — `good_enough`, 16 findings, 14 accepted and 2 rejected
> with reasons. The accepted ones changed the design rather than decorating it: withdrawal has to
> CANCEL and not merely un-draw, nothing typed may be silently dropped, the queue needs a ceiling,
> and the three-turn cap turns out not to be enforced anywhere at all. What changed is marked
> **(gate)** below.

## The symptom, in the person's own words

> *"я задал вопрос, и не могу нажать сенд, пока не ответит. но это долго ждать, мне нужно уже
> переключаться дальше. я хочу нажать сенд, и оно должно поставить в очередь, как пред выполнится —
> запустить след."*

They asked something, and Send is dead until the answer arrives. A real explanation was measured at
**9.4 s** and eight of those seconds are silent. They do not want to sit through it: they want to
type the next question, press Send, and have it go when the current one is done.

## What is actually in the way — the queue already exists

This is the finding that decides the whole shape of the change, and it is not what the issue
implies. **The host already queues.** `ask` (`chatCommand.ts:1800`) does not send anything directly;
it chains onto `thread.turns`:

```ts
const mine = thread.turns
  .then(() => oneTurn(entry, text, began))
  .catch((reason: unknown) => { /* clears running, shows the failure */ });
thread.turns = mine;
```

Its own docblock says so — *"Ask, once whatever this conversation is already doing has finished"* —
and the chain's (`chatCommand.ts:293-300`) says why it exists:

> *"The session already refuses to interleave two turns down one pipe, but the transcript is kept
> here and two overlapping `ask` calls would write it out of order… **The page cannot prevent it —
> it disables its own composer, and the keybinding does not go through the composer.** So the chain
> is here, mirroring the one inside `cliChatSession`."*

So a second question asked through the **keybinding** already waits its turn and lands in order. The
only door that cannot reach the queue is the one the person is actually looking at.

**And the stated reason for locking the composer no longer holds.** `chatPage.ts:36-38` says the
lock is a feature because *"two turns down one NDJSON pipe would interleave; the page makes that
impossible rather than the session refusing it late."* That was true when written. It is now
belt-and-braces over two other guarantees — the session refuses to interleave, and the chain orders
the transcript — and the belt costs the person the feature this issue asks for.

So this is not "build a queue". It is: let the composer join the queue that is already there, make
what is waiting VISIBLE and CANCELLABLE, and close the two holes that only matter once a second door
exists.

### The chain's failure contract, written down **(gate)**

Two reviewers read the chain as able to strand later questions when a turn fails or is stopped. It
cannot, and the reason is one character of placement: the `.catch` is **inside** the chain that gets
assigned, so what `thread.turns` holds always RESOLVES, whatever the turn did. A provider failure, a
killed process or a stop leaves the next `.then` free to run, and the catch clears `running` for
exactly that reason.

That was true and undocumented, which is why two reviewers asked. It is now stated here and DRIVEN
by the test plan: fail a turn, then send another, and watch the second one run.

## What ships

### 1. The composer is live while a turn runs — but not while the conversation is capped

`chatPage.ts:1083` is `const locked = state.running || state.capped;`, and the two halves are
different facts wearing one name:

| | today | after |
|---|---|---|
| `running` — an answer is on its way | locked | **live**, and Send queues |
| `capped` — a remote conversation is full | locked | still locked, unchanged |

`capped` stays because a capped conversation cannot take another turn **at all** — queueing one
would promise something that will never run.

### 1b. And the cap becomes a boundary instead of a picture **(gate)**

A reviewer asked what happens if a question reaches a capped conversation through a door that is not
the composer. The answer, verified: **nothing stops it.** `thread.asked` is incremented at
`chatCommand.ts:2174` and read at exactly one place — `chatCommand.ts:1246`, to compute the `capped`
flag the PAGE draws. There is no refusal anywhere. So the keybinding can already ask a capped Team
conversation a fourth time, past the three-turn cap that exists because the bill for turn N is the
bill for everything before it.

That hole predates this change, and this change makes it reachable in a new way: a question queued
while the conversation had room can arrive after the cap has been hit. So the cap is enforced where
the turn BEGINS, in `oneTurn`, beside the generation guard — the words come back to the composer with
the sentence the page already uses. *A picker is not an enforcement; the boundary refuses.*

### 2. What is waiting is on the page, and withdrawing it actually cancels it **(gate)**

A queued question must be visible or the feature is indistinguishable from a bug. It is **not**
appended to `thread.messages` when it is queued, and that is deliberate: `messages` is the transcript
— what was actually said — and it is what a forgetful model is handed as `carry`
(`chatCommand.ts:2047-2052`). A question appended before it is asked would be re-sent to the model
answering the turn in front of it, as though the person had said two things at once.

```ts
/** Questions typed while a turn was running, oldest first. Not transcript: nobody has asked them. */
waiting: readonly WaitingQuestion[];   // { id: string; text: string }
```

**The id is not decoration.** Removing a row from `waiting` does not remove the
`thread.turns.then(() => oneTurn(...))` callback that was created when the question was queued —
that callback exists from the moment Send is pressed, and un-drawing a row would leave it to run and
bill somebody for a question they withdrew. So every queued question carries a stable id, and
`oneTurn` checks — at the moment it begins, not before — whether that id is still in `waiting`. A
withdrawn question begins nothing, sends nothing and appends nothing. This is the same shape as the
generation guard beside it, for the same reason.

Rendered under the thinking line, dimmed, each with a ✕. The page already has the vocabulary and the
class — `.queued` and *"waiting in the queue, N ahead"* (`chatPage.ts:934`, `chatPage.ts:1041`) —
written for a **Team server's** queue position. The same words are used, because to the person the
two are the same fact: something is ahead of this.

**Every string is escaped through the page's existing `escapeHtml` path (gate)**, and the test plan
puts an `<img src=x onerror=…>` through it: a waiting row is text a person pasted, the webview holds
a privileged message bridge, and `.claude/rules/shared/common/security.md` names this exact
anti-pattern by example.

### 3. Nothing typed is ever silently dropped **(gate)**

Three reviewers arrived at this from three directions — withdrawal with a non-empty composer, reset
with several queued, and a dropped-count that contradicted itself. They have one answer, and this
codebase already states it: *"The words are not lost — they go back to the composer, which is where
they were typed"* (`chatCommand.ts:2002`).

So **a question that leaves the queue without being asked goes back into the composer**, appended
below whatever is there with a blank line between, never replacing it. That covers all three cases at
once:

| | what happens |
|---|---|
| ✕ on a waiting row | that row only, returned to the composer |
| *New chat* while several wait | none of them runs; all of them return, oldest first |
| a question that reaches a capped or reset conversation | returns, with the reason on the page |

And the count contradiction goes with it: the page says what was **returned**, not what was
"dropped", because nothing is dropped. My first draft said three were dropped while also restoring
one, which was simply false.

### 4. A stop ends the running turn and leaves the queue alone — and the moment is named **(gate)**

`stop` names a turn number (`chatMessages.ts:89`, `chatCommand.ts:3308`) and ends that turn. It does
not touch the queue: *stop this answer* and *forget everything I queued* are two different wishes,
and the ✕ on each waiting row is the control for the second.

When `waiting` changes is now explicit, because a reviewer asked and the answer was implicit:

- **queued** — synchronously inside `ask`, before the chain is extended, so the page never shows a
  gap between the press and the row.
- **removed** — as the turn BEGINS in `oneTurn`, after the withdrawal and generation checks pass and
  before anything is sent. So a row on screen means "not yet started", exactly.
- **emptied** — by a reset, which returns every entry to the composer per §3.
- **untouched** — by a stop, by a failure, and by a model switch.

### 5. One door, so both doors behave the same **(gate)**

`ask` is the single place that enqueues, and the keybinding already goes through it. So a question
asked by keybinding while a turn runs appears in `waiting` too, rather than being invisibly queued
the way it is today. No second path, nothing to keep in step.

### 6. A ceiling **(gate)**

Two reviewers, independently: nothing stops a person holding Enter, or pasting, until the host is
holding thousands of long prompts that will all eventually be billed. So the queue has a ceiling —
**8 questions, and 64 KB of text across them** — and a Send past it is refused with a sentence
naming which limit was hit, leaving the words in the composer. The numbers are a judgement, not a
measurement; they are written here so that changing them is a decision somebody makes on purpose.

## What this does NOT do

- **It does not persist the queue across a reload, and it is EXPLICITLY excluded (gate).** So
  "nothing typed is ever silently dropped" is a promise about a WINDOW THAT LIVES, and the code
  round was right to say the unqualified version overclaims: a reload takes the queue with it and
  nothing can hand the words back, because the host that held them is gone. What a reload does NOT
  do is lose anything that was asked — the transcript is on disk — and it already killed the running
  turn before this feature existed. Whether that is good enough is question 1 below, and it is the
  operator's. The queue
  is exactly as durable as the turn it waits behind: a reload kills the extension host, the running
  turn dies with it, and a restored tab deliberately starts no process until it is spoken to again
  (`chatCommand.ts:2007-2011`). `waiting` is therefore runtime state on `Thread` and is deliberately
  NOT added to `ConversationRecord` — a reviewer's point, and a sharp one: were it serialised, a
  reload could restore a row whose promise chain died, showing a question that will never run. The
  test plan asserts a reload comes back with none.
- **It does not run two turns at once.** The whole point of the chain is that it does not.
- **It does not queue across conversations.** The chain is per thread, which is what the person
  means: they are switching away from THIS conversation.
- **It does not change the switch or the retry.** A model switch queued behind an answer already
  works (`model-switch-queued`); a question queued behind that switch goes to the new model, which is
  what the chain gives for free.

## Build order

1. **RED** on the page: `running: true, capped: false` renders a LIVE composer; `capped: true`
   renders a locked one. Asserted by RUNNING the page, per the operator ruling.
2. `chatPage.ts` — split `locked` into the two facts it is; draw the waiting rows with their ✕.
3. **RED** on the host: two `send`s in a row produce two turns in order, the second appended only
   when it runs, and visible as waiting in between.
4. `chatCommand.ts` — `WaitingQuestion`, the `waiting` field, `ask` enqueuing synchronously, `oneTurn`
   dequeuing as it begins.
5. **RED** then GREEN on withdrawal: ✕ removes that row and no other, **no model call is made and no
   transcript line appears**, and the words are in the composer.
6. **RED** then GREEN on the cap as a boundary: a question that arrives at a capped conversation is
   refused in `oneTurn`, returns to the composer, and `asked` does not move.
7. **RED** then GREEN on reset: three queued, *New chat*, none runs, all three return to the composer
   oldest first, and the new transcript is empty.
8. **RED** then GREEN on the ceiling: the ninth question is refused with its sentence and stays in
   the composer.
9. **RED** then GREEN on the failure contract: fail a turn, send another, and watch the second run.
10. **The seam (gate)**: a test through the real page→host command boundary — send twice, observe
    waiting, ordered execution, withdrawal and reset — because a page test and a host test can both
    be green while the command name or payload between them is wrong. `forgetAChatRow.test.ts` is
    this repository's scar for exactly that.
11. **XSS (gate)**: run the page with a payload in a waiting row and assert it is text.
12. **Teeth**: remove the `capped` half of the lock and watch its test go red; remove the withdrawal
    check in `oneTurn` and watch the withdrawn question run.
13. `cd src_vs_code && npm run clean && npm run compile && npm test`, then the server suite, then
    `node .agents/conventions/tools/plan-lifecycle.mjs`.

## Test plan

- The composer's two states and the waiting rows are asserted by RUNNING the page.
- The ORDER of two queued turns, withdrawal, the cap, the ceiling and the failure contract are
  asserted on the host with a fake session, as the existing chain tests do.
- One test crosses the seam end to end (step 10).
- **Every new flow is catalogued in `research/module_tests.md`** with what it does and does not prove.
- Unchanged and must stay green: `chatPage.test.ts`, `chatMessages.test.ts`, `chatPanel.test.ts`,
  `chatRestore.test.ts`, `chatFresh.test.ts`.

## Definition of Done

- [x] A RED test observed failing before each half, naming the real symptom.
- [x] Send is live while an answer is running, and the question goes into the queue rather than down
      the pipe — asserted on the page as the product builds it.
- [x] A capped conversation still locks, and a question reaching one through ANY door is refused at
      the boundary with `asked` unmoved — proved by breaking it.
- [x] A withdrawn question makes no model call and leaves no transcript line — asserted, because
      un-drawing a row while its callback still runs is the defect this design exists to avoid.
- [x] A queued question is NOT in `thread.messages` until it runs, so a forgetful model is never
      handed a question nobody has asked.
- [x] Two questions queued in a row land in the transcript in the order they were typed.
- [x] Nothing typed is ever silently dropped: withdrawal, reset and a refusal all return the words.
- [x] The ceiling refuses the ninth question with a sentence and keeps the words.
- [x] A failed turn does not strand the questions behind it — asserted.
- [x] A reload comes back with an empty queue, and `ConversationRecord` never carried it.
- [x] A waiting row renders hostile text as text.
- [x] One test crosses the page→host boundary.
- [x] Whole extension suite green from a cleaned `out/`; the server suite green; `plan-lifecycle` clean.
- [x] `research/module_extension.md` updated, including the correction to the "the composer is
      disabled while a turn runs, and that is a feature" paragraph, which this change makes false.
- [x] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations; both READMEs updated.

## Open questions for the operator

Neither blocks the build.

1. **Should the queue survive a window reload?** The plan says no, for the reason above. If the real
   working pattern is "queue three and close the laptop", that answer is wrong and it becomes a
   `CONVERSATION_VERSION` bump.
2. **Are 8 questions and 64 KB the right ceiling?** They are a judgement. The only thing measured is
   that no ceiling at all is wrong.

### The suites, as run

- `cd src_vs_code && npm test` — from a cleaned `out/`: **3365 tests, 0 failed**, 1 skipped.
- `node .agents/conventions/tools/plan-lifecycle.mjs` — clean.
### The RED observations, with what they said

The testing rule asks for the step-2 failure message and not only the fact of a failure, and the
first version of this record gave only the count. What each guard said when it was broken:

| guard, broken | the test that went red | what it said |
|---|---|---|
| `locked = running \|\| capped` put back | *Send is live while an answer is on its way* | `the composer is dead while an answer is on its way` |
| the page locking itself on send put back | *two questions typed in a row both reach the host* | `the second question never reached the host, which is the whole of issue #288` |
| `chatWaitingHtml` returning nothing | *a question waiting its turn is shown* | `the words are not on the page at all` |
| the same | *the whole page carries the waiting rows* | `the page does not render its own waiting list` |
| the delegated withdraw handler unwired | *PRESSING the cross on a waiting row crosses the seam* | `pressing the cross posted nothing at all` |

And before any of it, the two that named the defect itself: `Send is live while an answer is on its
way` and the four waiting-row cases failed against the unchanged page, which is the symptom the
issue describes. All green after the change; all red again when each guard was broken and green once
more when it was restored.
