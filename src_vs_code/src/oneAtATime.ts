/**
 * A door that is already open does not open twice.
 *
 * <p><b>What it is for.</b> A command a person reaches through a keyboard shortcut can be pressed
 * again before the first press has produced anything to look at. Showing progress makes that wait
 * legible; it does not make the path single-entry, because both presses arrive while the first is
 * awaiting and both pass the entry point. This refuses the second one.</p>
 *
 * <p><b>REFUSED, not queued.</b> Queueing is the wrong answer for a gesture: a person who presses a
 * shortcut twice wants one result, not two in a row. The refusal is a value the caller chooses, so
 * the same door can answer with an empty passage, a sentence, or anything else that reads as "that
 * press did nothing" in its own vocabulary.</p>
 *
 * <p><b>The latch is taken SYNCHRONOUSLY</b>, before the first `await`. Taken after one — even an
 * already-resolved one — two presses in a single tick would both find the door open, which is the
 * exact race this exists to close and the one its test pins.</p>
 *
 * <p><b>And released in a `finally`.</b> That is the half a hand-rolled latch gets wrong, and the
 * symptom is one nobody reports: a shortcut that silently stops working after something failed once,
 * with no connection between the two events. This repository has three latches written in place —
 * `chatGotoCommand`, `chatStoreCache` and `bugzReviewPanel` — and this is the first one that is a
 * value, so the rule is asserted rather than described. Converting those three is named in
 * `todo/PLAN_the_tail_of_the_command_split.md` rather than done in passing.</p>
 *
 * <p><b>`PromiseLike`, not `Promise`.</b> The editor API answers in `Thenable`, and a door that
 * accepted only native promises would push every caller into wrapping one — a line of ceremony per
 * call site, and one more place to get the latch wrong.</p>
 *
 * @param refusal what a press that arrived while the door was shut is answered with
 * @returns a door. Every call with work to do goes through the same one, or it is not a latch.
 */
export function oneAtATime<T>(refusal: T): (work: () => PromiseLike<T>) => Promise<T> {
  let open = true;

  return async (work: () => PromiseLike<T>): Promise<T> => {
    if (!open) {
      return refusal;
    }
    open = false;
    try {
      return await work();
    } finally {
      open = true;
    }
  };
}
