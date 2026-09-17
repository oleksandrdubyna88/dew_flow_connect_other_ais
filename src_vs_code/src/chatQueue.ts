import type { WaitingQuestion } from './chatPage';

/**
 * A queue of questions that have been typed and not yet asked — issue #288.
 *
 * <h2>Why there is one at all</h2>
 *
 * <p>A person asked something and Send went dead until the answer arrived. A real explanation was
 * measured at 9.4 s, eight of them silent, and they would rather type the next question and move on.
 * The HOST has queued turns since the chain was built — `ask` chains onto `thread.turns`, so a
 * question asked through the keybinding already waits its turn and lands in order — and the only
 * door that could not reach that queue was the composer, which the page locked.</p>
 *
 * <h2>Why the decisions are HERE and not in `chatCommand.ts`</h2>
 *
 * <p>That file needs a `vscode` host the test suite has none of, so nothing in it can be executed by
 * a test. The standing arrangement for that is this one: the decisions live in a pure module and the
 * wiring stays in the command file, as `answerCopy`, `chatLedger` and `chatUsage` already do. A
 * queue whose ordering, ceiling and withdrawal rules cannot be driven is a queue those rules are
 * hoped at.</p>
 *
 * <h2>The rule that decides the shape: withdrawal must CANCEL</h2>
 *
 * <p>Taking a row off the page does not un-create the callback that will run it — that was made the
 * moment Send was pressed, and a promise chain cannot be un-chained. So a queued question carries a
 * stable ID, and the thing that begins a turn asks {@link isWanted} about that id at the moment it
 * begins. A withdrawn question then begins nothing, sends nothing and appends nothing, rather than
 * running invisibly and billing somebody for a question they took back. Keying on the TEXT would not
 * do: the same words typed twice is an ordinary thing to do, and withdrawing the second would cancel
 * the first. (codex, the plan round.)</p>
 */

/**
 * How many questions may wait.
 *
 * <p>Eight is a judgement and is written down so that changing it is a decision somebody makes on
 * purpose. What is NOT a judgement is that there must be a limit: two reviewers arrived at it
 * independently, and they are right — nothing otherwise stops a person holding Enter until the
 * extension host is carrying a stack of prompts that will all eventually be billed.</p>
 */
export const MOST_WAITING = 8;

/**
 * And how much TEXT may wait across all of them.
 *
 * <p>A count alone is not a ceiling: eight pasted files is not eight questions, and the thing that
 * costs money is the words rather than the rows.</p>
 */
export const MOST_WAITING_CHARS = 64 * 1024;

/**
 * Why a question did not join — a REASON, never a sentence.
 *
 * <p>A pure module that returned English would make every other caller either parse user-facing text
 * or invent its own; and `nothing` is not a capacity problem at all, so reporting it as one would
 * make a metric counting refusals count an empty box as a full queue. The words live at the command
 * boundary, where the rest of this feature's words live. (codex, the code round.)</p>
 */
export type NotJoined = 'nothing' | 'tooMany' | 'tooLong';

/** A question joined the queue, or it did not and here is why. */
export type Joined =
  | { readonly kind: 'queued'; readonly waiting: readonly WaitingQuestion[]; readonly id: string }
  | { readonly kind: 'full'; readonly why: NotJoined };

/** What a withdrawal leaves behind, and the words it hands back. */
export interface Withdrawn {
  readonly waiting: readonly WaitingQuestion[];
  /** The withdrawn question's words, or empty when the id named nothing. */
  readonly returned: string;
}

/**
 * Put a question at the back of the queue, or refuse it with a sentence.
 *
 * <p>The id is supplied rather than minted here so that the caller — which is the only thing that
 * can also extend the promise chain — holds one id for both halves of the same act. A queue that
 * minted its own would leave the caller guessing which row its callback belongs to.</p>
 */
export function join(
  waiting: readonly WaitingQuestion[],
  text: string,
  id: string,
): Joined {
  const said = text.trim();
  if (said.length === 0) {
    // Not a ceiling, and its own reason: there is nothing to queue. An empty question reaching the
    // queue would draw a blank row somebody cannot read and cannot act on.
    return { kind: 'full', why: 'nothing' };
  }
  if (waiting.length >= MOST_WAITING) {
    return { kind: 'full', why: 'tooMany' };
  }
  const held = waiting.reduce((total, one) => total + one.text.length, 0);
  if (held + said.length > MOST_WAITING_CHARS) {
    return { kind: 'full', why: 'tooLong' };
  }

  return { kind: 'queued', waiting: [...waiting, { id, text: said }], id };
}

/** Take one question out by name, and hand its words back. */
export function withdraw(waiting: readonly WaitingQuestion[], id: string): Withdrawn {
  const found = waiting.find((one) => one.id === id);

  return {
    waiting: waiting.filter((one) => one.id !== id),
    // Empty when the id named nothing, which is an ordinary race rather than a fault: a press can
    // land a tick after that question's turn began. It must not put somebody else's words anywhere.
    returned: found?.text ?? '',
  };
}

/**
 * Is this question still wanted?
 *
 * <p>Asked at the moment a turn begins, and the whole reason the id exists. See the module note.</p>
 */
export function isWanted(waiting: readonly WaitingQuestion[], id: string): boolean {
  return waiting.some((one) => one.id === id);
}

/** Take it out because its turn has begun — the same removal, named for the other reason. */
export function began(waiting: readonly WaitingQuestion[], id: string): readonly WaitingQuestion[] {
  return waiting.filter((one) => one.id !== id);
}

/**
 * Everything waiting, as words to hand back — oldest first.
 *
 * <p>What a reset does with a queue. All of them come back, because a count on its own is not a
 * recovery: the first draft of this plan said "three dropped" while restoring one, which was simply
 * false, and three reviewers objected to losing typed words from three directions.</p>
 *
 * <p><b>It returns the words and not a composer.</b> Putting them INTO the box is
 * `pushChatDraft`, which posts a draft the page appends below whatever is already there — a rule
 * this product already owns and `chatPage.test.ts` already drives. Writing that rule a second time
 * here is the duplicate the reuse rule forbids, and it would have been the copy nobody updated. So
 * the queue decides only what it is the authority on: the order.</p>
 */
export function drained(waiting: readonly WaitingQuestion[]): readonly string[] {
  return waiting.map((one) => one.text);
}
