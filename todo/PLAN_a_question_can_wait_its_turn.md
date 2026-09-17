# PLAN — a question can wait its turn

> Status: **plan only, nothing implemented yet, 2026-09-17.** Scope: `src_vs_code/src/chatPage.ts`,
> `src_vs_code/src/chatCommand.ts`, `src_vs_code/src/chatMessages.ts`, `src_vs_code/src/chatPanel.ts`
> — the composer while an answer is running.
>
> Issue: [#288](https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais/issues/288).
> Related docs: [module_extension.md](../research/module_extension.md).

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
const mine = thread.turns.then(() => oneTurn(entry, text, began))
```

Its own docblock says so — *"Ask, once whatever this conversation is already doing has finished"* —
and the chain's (`chatCommand.ts:293-300`) says why it exists:

> *"The session already refuses to interleave two turns down one pipe, but the transcript is kept
> here and two overlapping `ask` calls would write it out of order: pressing the keybinding twice
> against an open tab put both questions above both answers. **The page cannot prevent it — it
> disables its own composer, and the keybinding does not go through the composer.** So the chain is
> here, mirroring the one inside `cliChatSession`."*

So a second question asked through the **keybinding** already waits its turn and lands in order. The
only door that cannot reach the queue is the one the person is actually looking at.

**And the stated reason for locking the composer no longer holds.** `chatPage.ts:36-38` says:

> *"The composer is disabled while a turn runs, and that is a feature. … Two turns down one NDJSON
> pipe would interleave; the page makes that impossible rather than the session refusing it late."*

That was true when it was written. It is now belt-and-braces over two other guarantees — the session
refuses to interleave, and the chain orders the transcript — and the belt costs the person the
feature this issue asks for. **The lock is removed; the reasons it existed are kept by the code that
actually enforces them.**

So this is not "build a queue". It is: let the composer join the queue that is already there, and
make what is waiting VISIBLE, because a question that vanishes from the box and appears nowhere is
worse than a locked button.

## What ships

### 1. The composer is live while a turn runs — but not while the conversation is capped

`chatPage.ts:1083` is `const locked = state.running || state.capped;`, and the two halves are
different facts wearing one name:

| | today | after |
|---|---|---|
| `running` — an answer is on its way | locked | **live**, and Send queues |
| `capped` — a remote conversation is full | locked | still locked, unchanged |

`capped` stays because a capped conversation cannot take another turn **at all** — queueing one
would promise something that will never run. The page's existing two honest actions (start again, or
move the thread to a local model) remain the only ones offered there.

`resetting` also stays a refusal, and it already has its own behaviour: a question typed while the
slate is being wiped goes back to the composer (`chatCommand.ts:1811-1818`).

### 2. What is waiting is on the page, and can be withdrawn

A queued question must be visible or the feature is indistinguishable from a bug. It is **not**
appended to `thread.messages` when it is queued, and that is deliberate: `messages` is the
transcript — what was actually said — and it is what a forgetful model is handed as `carry`
(`chatCommand.ts:2047-2052`). A question appended before it is asked would be re-sent to the model
answering the turn in front of it, as though the person had said two things at once.

So a new field beside the transcript:

```ts
/** Questions typed while a turn was running, oldest first. Not transcript: nobody has asked them. */
waiting: readonly WaitingQuestion[];
```

rendered under the thinking line, dimmed, each with a ✕ that withdraws it and puts the words back in
the composer if the composer is empty. The page already has the vocabulary and the class for this —
`.queued` and *"waiting in the queue, N ahead"* (`chatPage.ts:934`, `chatPage.ts:1041`) — written for
a **Team server's** queue position. The same words are used, because to the person the two are the
same fact: something is ahead of this.

### 3. A reset discards the queue, and says how many

`thread.generation` (`chatCommand.ts:258-270`) already drops a queued question after a reset, and
returns it to the composer with `pushChatDraft`. That was correct when at most ONE question could be
queued — through the keybinding. With a real queue, N questions cannot all go back into one box, and
three `pushChatDraft` calls in a row would clobber each other silently.

So: a reset drops the whole queue and the page says how many were dropped. One question goes back to
the composer when the composer is empty, which is the common case and the one worth keeping.

### 4. A stop ends the running turn and leaves the queue alone

`stop` names a turn number (`chatMessages.ts:89`, `chatCommand.ts:3308`) and ends that turn. It does
not touch the queue: *stop this answer* and *forget everything I queued* are two different wishes,
and the ✕ on each waiting question is the control for the second. This is written down because it is
a decision, not an accident — a reviewer will ask.

## What this does NOT do

- **It does not persist the queue across a reload**, and the reason is that the queue is exactly as
  durable as the turn it is waiting behind. A reload kills the extension host: the running turn dies
  with it, and a restored tab deliberately starts no process until it is spoken to again
  (`chatCommand.ts:2007-2011`). Persisting the queue would mean bumping `CONVERSATION_VERSION` and
  its migration for state whose lifetime is a couple of minutes, and then deciding whether a window
  that just came back should start running questions nobody is watching. The record is a
  TRANSCRIPT — what was said — and nothing here has been said yet.
- **It does not run two turns at once.** The whole point of the chain is that it does not, and the
  NDJSON pipe is why.
- **It does not queue across conversations.** The chain is per thread, which is what the person
  means: they are switching away from THIS conversation.
- **It does not change the cap, the switch, or the retry.** A model switch queued behind an answer
  already works (`model-switch-queued`); a question queued behind that switch goes to the new model,
  which is what the chain gives for free.

## Build order

1. **RED** on the pure half: the page state with `running: true` and `capped: false` renders a LIVE
   composer; with `capped: true` it renders a locked one. Asserted by RUNNING the page, per the
   operator ruling — no new behavioural assertion over page source text.
2. `chatPage.ts` — split `locked` into the two facts it is, and draw the waiting list.
3. **RED** on the host: two `send`s in a row against one thread produce two turns in order, with the
   second appended only when it runs; the second is visible as waiting in between.
4. `chatCommand.ts` — `waiting` on the thread, pushed through `show`, cleared as each turn begins.
5. **RED** then GREEN on withdrawal: ✕ on a waiting question removes that one and no other, and puts
   its words back only when the composer is empty.
6. **RED** then GREEN on reset: three queued questions, *New chat*, none of them runs, the page says
   three were dropped, and the transcript of the new conversation is empty.
7. **Teeth**: remove the `capped` half of the lock and watch the capped test go red; remove the
   generation guard and watch the reset test go red.
8. `cd src_vs_code && npm run clean && npm run compile && npm test`, then the server suite, then
   `node .agents/conventions/tools/plan-lifecycle.mjs`.

## Test plan

- The composer's two states are asserted by RUNNING the page (`chatPage.test.ts`'s harness, or
  `forgetAChatRow.test.ts`'s if the control is a `[data-command]` one).
- The ORDER of two queued turns is asserted on the host with a fake session, as the existing chain
  tests do.
- **Every new flow is catalogued in `research/module_tests.md`** with what it does and does not
  prove.
- Unchanged and must stay green: `chatPage.test.ts`, `chatMessages.test.ts`, `chatPanel.test.ts`,
  `chatRestore.test.ts`, `chatFresh.test.ts`.

## Definition of Done

- [ ] A RED test observed failing before each half, naming the real symptom.
- [ ] Send is live while an answer is running, and the question goes into the queue rather than down
      the pipe — asserted on the page as the product builds it.
- [ ] A capped conversation still locks, and still offers its two ways out — proved by breaking it.
- [ ] What is waiting is on the page, in order, and each one can be withdrawn on its own.
- [ ] A queued question is NOT in `thread.messages` until it runs, so a forgetful model is never
      handed a question nobody has asked — asserted, because this is the one that would bill somebody.
- [ ] Two questions queued in a row land in the transcript in the order they were typed.
- [ ] *New chat* runs none of them and says how many it dropped.
- [ ] A stop ends the running turn and leaves the queue — asserted, because it is a decision.
- [ ] Whole extension suite green from a cleaned `out/`; the server suite green; `plan-lifecycle` clean.
- [ ] `research/module_extension.md` updated, including the correction to the "the composer is
      disabled while a turn runs, and that is a feature" paragraph, which this change makes false.
- [ ] Promoted to `research/` with `IMPLEMENTED <date>` and its deviations; both READMEs updated.

## Open questions for the operator

Neither blocks the build; both are recorded so the answer is theirs rather than mine.

1. **Should the queue survive a window reload?** The plan says no, with the reasoning above. If the
   real working pattern is "queue three and close the laptop", that answer is wrong and it becomes a
   `CONVERSATION_VERSION` bump.
2. **Is there a sensible limit on how many can wait?** The plan sets none. A person who holds Enter
   down could queue fifty questions at a vendor that bills per turn, and nothing here would stop
   them.
