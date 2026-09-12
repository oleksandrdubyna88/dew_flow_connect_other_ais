# PLAN — Carry nothing above

> Status: **plan only, nothing implemented yet.** Scope: the chat page's transcript and the thread's
> `carry` — `src_vs_code/src/chatPage.ts`, `chatCommand.ts`, `chatPanel.ts`, `chatMessages.ts`,
> `chatTabs.ts`, and the five help catalogs.
>
> Related docs: [PLAN_what_you_asked_is_on_disk.md](../research/PLAN_what_you_asked_is_on_disk.md),
> [module_extension.md](../research/module_extension.md).

## The symptom

A long conversation is expensive and muddled where it is re-sent. Two places re-send it: a **Team
server**, which holds nothing and is handed the whole transcript every turn, and a **model switch**,
which hands the next model everything said so far. Ten turns about one subject followed by a question
about another means the second question arrives wrapped in the first subject — and, on a Team server,
is paid for on every turn after that.

The operator asked for a way to say *from here on, none of that goes with it* — without losing the
conversation on screen.

## What it will do

A button on the LAST model answer, reading **Carry nothing above**.

- Pressed, a dash-dot orange rule is drawn on that answer's separator.
- The conversation above it stays exactly where it is: visible, scrollable, copyable. Nothing is
  deleted, and the tab reads as one conversation because it is one.
- From then on, what is handed to **another model** on a switch, and to a **Team server** on every
  turn, starts BELOW that rule.

### The five decisions, as the operator made them

1. **The last answer only.** Not every answer. Breaking the thread retroactively in the middle is a
   different gesture and it would have to argue with the marks already below it.
2. **It is never removed, only MOVED.** Pressing it again further down does not clear the first mark —
   it becomes the point. There is no "undo", because there is nothing to undo: the mark is a position,
   and setting it again is setting it.
3. **Only the current one is drawn.** With several presses behind it, the rule appears at the newest
   and nowhere else. The earlier positions are not history anybody needs to see.
4. **It survives a window reload**, with the transcript. Without that, a reload would silently restore
   the full carry and the next Team-server turn would quietly cost what it used to.
5. **Nothing above goes with it — including the passage and the instruction.** The captured passage
   lives in the transcript as the first turn's material, so the slice excludes it with everything
   else; no special handling. The next model brings its OWN role and prompt, which is what it would
   do anyway.

### What it deliberately does NOT do

**The local CLI still remembers.** It holds the conversation in its own process, so the model you are
talking to right now is unaffected — confirmed with the operator in those words. This button is
"do not carry this onward", not "forget this". Saying otherwise would mean restarting the vendor
process, which is a different feature and loses things this one keeps.

## The seam

`thread.carry` is already the single funnel: `oneTurn` sends it ahead of the question, a model switch
fills it from the transcript, and a Team server is handed it every turn. So the whole change is a
POSITION and a slice.

- `Thread.carryFrom: number` — the index in `thread.messages` the carry starts at. `0` by default,
  which is today's behaviour exactly.
- Every place that builds `carry` slices from it instead of from the start.
- `SavedTab.carryFrom?: number` — optional, so a record written before the field exists still reads,
  and absent means `0`. (The same shape `fromSession` took, and for the same reason: bumping
  `TAB_VERSION` discards every stored conversation.)
- The page posts `{ type: 'carryFrom', at }` when the button is pressed; the host records it and
  pushes the transcript back with the mark on it.

## Build order

1. `carryFrom` on the thread and the slice at every `carry` site — the behaviour, with no UI.
2. The page: the button on the last model answer, the dash-dot rule, and the message.
3. The store: the optional field, absent reading as `0`.
4. The help, in all five languages, in the same commit.

## Test plan

- The slice: `carryFrom` at 0 carries everything (today's behaviour, unchanged); at the end carries
  nothing; in the middle carries exactly what is below it.
- Pressing it again MOVES the mark rather than adding a second.
- The rule is drawn once, at the newest mark only.
- A stored record with no `carryFrom` reads as 0; one with a number reads it back; one with a
  non-number is dropped with the rest of the malformed records.
- The passage is excluded by the slice without being named anywhere — the test that proves there is
  no special case.
- The shipped bundle, pressed: the button posts, the rule appears, a second press moves it.

## Definition of Done

- [ ] The button sits on the last model answer and reads **Carry nothing above**.
- [ ] The rule is dash-dot and orange, and only the newest one is drawn.
- [ ] A switch and a Team-server turn both carry only what is below it.
- [ ] The local CLI is untouched, and the help says so in as many words.
- [ ] It survives a reload.
- [ ] Help updated in English, Russian, Ukrainian, German and Spanish in the same commit.
- [ ] `npm run typecheck` clean, the whole suite green, the bundled-page test watched red first.
