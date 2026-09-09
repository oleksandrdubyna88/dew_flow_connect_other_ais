# PLAN — a chat process that dies could carry the conversation too, exactly as a model switch does

> Status: **deliberately not implemented, and gated on a MEASUREMENT rather than on a decision.**
> The owner's ruling, 2026-09-08: *"если будет часто сделаем. если раз в 5 лет и хер с ним."* So the
> question this plan exists to answer is **how often a chat process actually dies under somebody**,
> not what to do about it — that part is already written and shipped for the neighbouring case.
>
> Related docs: [research/module_extension.md](../research/module_extension.md),
> [research/PLAN_chat_with_other_ais.md](../research/PLAN_chat_with_other_ais.md).

## The symptom, and why it is only half a symptom

A chat tab holds one long-lived vendor process. When it dies — token exhaustion, a crash, an OS
kill, a sign-in that expired mid-conversation — the next question starts a REPLACEMENT process that
never heard anything, and the answer after it carries a sentence saying so:

> *(the conversation restarted — this answer does not remember the earlier ones)*

That is honest and it is the current behaviour (`chatCommand.ts`, `oneTurn`, on
`result.contextLost`). What it is not is helpful: the person still has the conversation on screen,
and the model answering has none of it.

Since 2026-09-08 the machinery to fix that exists and is tested. `carriedTurn`
(`src_vs_code/src/chatPrompt.ts`) hands a whole conversation — questions and answers both,
attributed, fenced with a one-time delimiter, bounded at 60 000 characters — to a model that never
heard it. `switchModel` already uses it for the case the owner asked for: choosing another model in
an open tab. Applying it to a DEATH is roughly six lines.

## Why it is not just switched on

**Because it spends somebody's money for a reason nobody chose.** A model switch is a decision: the
person clicked a picker, and re-sending the conversation is what they asked for. A crashed CLI is an
accident, and silently re-sending an entire conversation because a third-party process fell over is
a bill they did not agree to — repeatedly, if the thing crashes in a loop.

The alternatives, if it turns out to be worth doing:

| Shape | What it costs | What it risks |
|---|---|---|
| Carry silently, exactly like a switch | one full transcript per death | a crash loop bills a conversation per crash |
| Carry, and SAY so in the transcript | the same | nothing much — but the sentence is a second thing to read |
| Offer it: *"the conversation was lost — send it again?"* | nothing until pressed | one more click in a moment that is already annoying |
| Leave it as it is | nothing | the person re-explains by hand, or starts a new tab |

The third is what this plan would build if it is built. The second is the cheap version.

## The measurement this plan is really about

**Nothing gets built until the frequency is known.** Two ways to know it, in order of cost:

1. **The person notices.** The restart sentence is already in the transcript, in words, every time.
   If it is never seen, this plan expires. That is the whole test, and it costs nothing.
2. **A line in the extension host log** when a chat process dies with a conversation open, naming
   the vendor and how many turns were lost. `cliChatSession.ts:onGone` is the one place it happens
   and it already has the two facts. About four lines, no UI, no new setting — and it turns "does
   this ever happen" from a memory into something greppable.

Do (2) only if (1) is ambiguous. Do neither if the answer is already obvious.

## If it IS built — the build order

1. **The signal.** `TurnResult` already carries `contextLost?: true`. `oneTurn` reads it. Nothing
   new is needed to KNOW; what is missing is only the offer.
2. **The offer.** On a `contextLost` answer, the page shows one button — *send the conversation
   again* — beside the restart sentence. Pressing it sets `thread.carry = [...thread.messages]`, the
   same field the switch sets, and the next question carries everything. The page already has the
   `capped` region with two buttons (`chatCappedHtml`), which is the shape to copy rather than
   invent.
3. **The bound is already there.** 60 000 characters, newest first, an oversized single turn cut
   with a marker. Nothing about a death changes any of it.

## What must NOT change

- **The sentence stays.** Whatever else happens, an answer that does not remember the earlier ones
  says so. The carry is an offer on top of the truth, never a replacement for telling it.
- **Nothing is re-sent without a press.** That is the entire reason this is a plan and not a commit.

## Test plan

Nothing new for `carriedTurn` — it is tested (nine tests in `chatPrompt.test.ts`, including the
budget, the oversized single turn, the forged role line and the one-time fence). What a build would
add is the page half: that the offer appears only with a `contextLost` answer, that pressing it arms
the carry, and that it disappears once used.

## Definition of Done

- [ ] The frequency is known, by (1) or (2) above.
- [ ] If it is rare, this plan is closed with the observation recorded — a plan that expires because
      the problem was measured away is a good outcome, not a failure.
- [ ] If it is common, the offer ships with its tests and `research/module_extension.md` records it.
