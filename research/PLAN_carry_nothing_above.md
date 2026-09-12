# PLAN — Carry nothing above

> Status: **IMPLEMENTED, 2026-09-12.** Scope: the chat page's transcript and the thread's
> `carry` — `src_vs_code/src/chatPage.ts`, `chatCommand.ts`, `chatPanel.ts`, `chatMessages.ts`,
> `chatTabs.ts`, and the five help catalogs.
>
> Related docs: [PLAN_what_you_asked_is_on_disk.md](PLAN_what_you_asked_is_on_disk.md),
> [module_extension.md](module_extension.md).

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

### What the mark IS, exactly

`carryFrom` is **the index of the first message that IS carried** — not the index of the marked
answer. The button on an answer at index *n* posts *n + 1*. Stated because the plan round found the
plan did not say, and both readings look right in prose: a rule drawn under an answer means that
answer is above the line, so it is not carried.

**A raw index is safe here, and that was measured rather than assumed.** Three reviewers asked for a
stable message id instead. `thread.messages` is only ever appended to (three sites) or truncated
FROM THE END (one site: the re-ask, which drops the rejected answer and its question). Nothing
mutates the middle, so an index keeps naming the same message as the conversation grows. What a raw
index does need is a BOUND, which is the next paragraph.

**Clamped everywhere, and validated on the way in.** `carryFrom` is used as
`messages.slice(clamp(carryFrom, 0, messages.length))`. A re-ask that truncates past the mark leaves
it beyond the end; `slice` would then carry nothing, which is the safe direction, but a NEGATIVE value
would carry the last message instead of the suffix — so only a finite non-negative integer is
accepted, from the page and from the store alike, and anything else is dropped with the rest of the
malformed record.

### The host owns the mark

The page does not draw the rule because it was pressed. It ASKS; the host records the mark, and the
page draws what the host pushes back. So a press that fails to record draws nothing, rather than
showing a line that is not durable while the next Team turn quietly re-sends everything.

### Why the local CLI is untouched — the mechanism, not the promise

`thread.carry` is EMPTY on an ordinary local turn: `oneTurn` sends `carrying.length > 0 ?
carriedTurn(...) : asked`, and nothing fills `carry` for a persistent local session. It is filled
only where a conversation is HANDED OVER:

| filled by | when |
|---|---|
| `thread.carry = messages.slice(0, -1)` | every turn of a `forgetful` Team server |
| `switchNow` | a model switch |
| `thread.carry = [...again.said]` | a re-ask, which is a switch by another name |
| `reopen` | the first turn after a window reload |

So the slice narrows handovers and never the ordinary local turn. The promise holds by construction.

**The reload is a handover too, and this plan treats it as one.** The operator named two cases — a
Team server and a model switch. A reload is a third, and the process behind the tab died with the
window: what comes back is a NEW model instance being handed a conversation it never heard, which is
the same event as a switch. Assumed rather than asked, and said here so it can be contradicted.

## Build order

1. `carryFrom` on the thread, the clamp, and the slice at every `carry` site — the behaviour, with
   no UI — **together with** the store's optional field and its validation. The two were separate
   steps until the plan round pointed out that a page which can write the mark before the store can
   read it is a window where a saved tab deserialises into a value nothing validates.
2. The page: the button on the last model answer, the dash-dot rule with a line saying what it means,
   and the message that asks for it.
3. The help, in all five languages, in the same commit.

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
- **The index the page posts is the index the carry starts at** — the button on answer *n* produces a
  carry beginning at *n + 1*, asserted against the exact payload rather than against the mark alone.
- **Both consumers actually use it**: one model switch and one Team-server turn after a mark, with
  the exact outbound arrays asserted. Every other test here can pass while one consumer still builds
  its own `slice(0, -1)` — and that consumer would re-send the earlier subject and cost exactly what
  this was built to stop.
- A negative, a fractional and an out-of-range mark, from the page and from the store.

## Definition of Done

- [ ] The button sits on the last model answer and reads **Carry nothing above**.
- [ ] The rule is dash-dot and orange, and only the newest one is drawn.
- [ ] A switch and a Team-server turn both carry only what is below it.
- [ ] The local CLI is untouched, and the help says so in as many words.
- [ ] It survives a reload.
- [ ] Help updated in English, Russian, Ukrainian, German and Spanish in the same commit.
- [ ] `npm run typecheck` clean, the whole suite green, the bundled-page test watched red first.

## What shipped differently

Everything above shipped, and the plan round changed four things before a line of it was built —
each recorded in the sections above rather than here, because they are part of the design now:
the mark is the first index CARRIED and not the marked answer's; it is clamped and validated at both
ends; the store's field arrives WITH the model change rather than after it; and the host, not the
page, decides when the rule is drawn.

Two things the building itself taught.

**The store's dedupe had to learn about the mark.** `show` skips the write when neither the
transcript nor the model changed — which is exactly what pressing this button does. The rule would
have been drawn and never saved, and a reload would have put the whole conversation back on the wire
without a word. Caught by writing the test for "it survives a reload" before believing it did.

**A bundled-page test can be hollow, and this one was.** The DOM stub replaced `closest` with a
function that handed back the button whatever selector was asked for — so it never exercised the
page's own selector, which had NOT been widened to include the new control. The test passed against
a button that could not have been pressed. Found by breaking it on purpose and watching nothing go
red; the stub honours the selector now.

## The open tail

- The mark only travels FORWARD. The operator chose that ("не снимается, обновляется точка откуда
  брать историю"), and the consequence is that including something above it again means pressing the
  button on an earlier answer — which the button is not offered on. Re-opens if anybody asks to move
  it back.
- A reload is treated as a handover, so a marked conversation restored after one hands the new local
  process only the suffix. Assumed rather than asked: the operator named a Team server and a model
  switch, and the process behind the tab genuinely died with the window.
