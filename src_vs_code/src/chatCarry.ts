import { ChatMessage } from './chatPage';

/**
 * Where a conversation starts when it is handed to a model that has not heard it.
 *
 * <p>Pure, and its own file, because this is a DECISION and the two files that would otherwise hold
 * it both import `vscode`. Everything here is reachable from a test with nothing mocked.</p>
 *
 * <p><b>What the mark means.</b> A person presses *Carry nothing above* on the last answer, and from
 * then on what is handed to ANOTHER model — on a switch, on a re-ask, on the first turn after a
 * reload — and to a Team server, which is handed the conversation every single turn, begins below
 * the rule that press drew. The conversation itself is untouched: it stays on screen, whole.</p>
 *
 * <p><b>What it is NOT.</b> The local CLI holding this conversation in its own process never sees
 * `carry` at all — an ordinary local turn sends the question and nothing else — so the model being
 * spoken to right now keeps every word of it. This is "do not carry this onward", never "forget
 * this". Confirmed with the operator in those words before it was built.</p>
 */

/** The mark a conversation with no mark has: carry everything, which is what every tab did before. */
export const CARRY_EVERYTHING = 0;

/**
 * A mark that can be used as an index, whatever arrived.
 *
 * <p>It arrives from two untrusted places — a webview's message and a JSON file somebody could have
 * edited — and one trusted-but-moving one: the transcript shrinks from the end when a re-ask drops
 * the answer nobody wanted, which can leave a mark past the last message.</p>
 *
 * <p>Past the end is clamped to the end, and carries nothing: the safe direction, since the mark said
 * "not the conversation above" and there is no conversation below. NEGATIVE is the one that must
 * never reach `slice`, which reads it as an offset FROM THE END and would carry the last message
 * instead of the suffix — the exact opposite of what was asked for. (codex, the plan round.)</p>
 */
export function carryMark(at: unknown, upTo: number): number {
  if (typeof at !== 'number' || !Number.isInteger(at) || at < 0) {
    return CARRY_EVERYTHING;
  }

  return Math.min(at, Math.max(upTo, 0));
}

/**
 * The conversation to hand over, from the mark down.
 *
 * <p>One function, called everywhere `carry` is built, so the Team server's every-turn handover and
 * the model switch's one-off cannot disagree about where a conversation begins.</p>
 */
export function carriedFrom(messages: readonly ChatMessage[], at: number): readonly ChatMessage[] {
  return messages.slice(carryMark(at, messages.length));
}
