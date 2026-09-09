/**
 * The seam between the conversation's LIFECYCLE and one vendor's wire protocol.
 *
 * <p>`cliChatSession.ts` did two jobs: it owned the process lifecycle — budgets, one turn at a time,
 * the four ways a child can end, what a death costs a conversation — and it spoke `agy`'s NDJSON.
 * The first is vendor-neutral and tested; only the second differs. This file is the line between
 * them, and the three files beside it are what is on the other side of it.</p>
 *
 * <p><b>A gate reviewer asked for exactly this seam on the trigger's code round and it was rejected
 * THEN — correctly.</b> Two of the three vendors had not been measured, and an interface designed
 * against unmeasured shapes is a guess with a type around it. They are measured now, and the shapes
 * turned out to differ in a way no guess would have produced: two of them hold a conversation in one
 * process, and the third holds none at all and resumes a stored session instead.</p>
 *
 * <p><b>The two shapes, and why the difference is not cosmetic.</b> A `persistent` vendor is a pipe:
 * write a line, read events, the context lives in the child, and the child dying takes the
 * conversation with it. A `per-turn` vendor is a process per question: the prompt goes in on stdin
 * and the stream is CLOSED, the answer arrives, the process exits — and the conversation is not lost,
 * because it lives in the vendor's own session store and the next turn resumes it by id. The
 * context-loss rule is therefore inverted between them, which is the single most likely place for
 * this change to go wrong and is why it has a test of its own.</p>
 */

/** What one line of a vendor's output means. Anything else is `nothing` — a CLI may log where it likes. */
export type AdapterEvent =
  /** The process is up and will take turns. Persistent vendors only. */
  | { readonly kind: 'ready' }
  /** The conversation's id in the vendor's own store, for resuming it. Per-turn vendors only. */
  | { readonly kind: 'session'; readonly id: string }
  | { readonly kind: 'answer'; readonly text: string }
  | { readonly kind: 'failure'; readonly failure: string }
  | { readonly kind: 'nothing' };

/** How a vendor is driven. See the note above — the difference reaches the context-loss rule. */
export type ChatShape = 'persistent' | 'per-turn';

export interface ChatAdapter {
  readonly shape: ChatShape;
  /**
   * Whether the process says it is READY before it is asked anything.
   *
   * <p>Measured, and it is not a detail: `agy` prints `init` the moment it starts, so a session can
   * wait for that and know the child is alive before spending a turn on it. `claude` prints
   * NOTHING until a turn arrives — given an empty stdin it simply exits — so a session that waits
   * for readiness first waits out its whole startup budget and reports a CLI that never started,
   * for a CLI that was working perfectly and had not been spoken to.</p>
   *
   * <p>Found by the live check of the three-adapter change, not by any test: both vendors speak
   * NDJSON over a pipe and differ only in who speaks first. A `per-turn` adapter ignores this — it
   * has no start to announce.</p>
   */
  readonly announces: boolean;
  /**
   * The command line for a process that will answer turns.
   *
   * @param resume the session to continue, for a `per-turn` vendor's second and later turns. Empty
   *   means a new conversation, and a `persistent` vendor ignores it entirely.
   */
  argv(resume: string): readonly string[];
  /**
   * One turn, as the bytes to send.
   *
   * <p>A `persistent` vendor gets an NDJSON line written into a pipe that stays open; a `per-turn`
   * vendor gets the prompt itself, written and followed by EOF. The session knows which from
   * `shape`, and the difference is why this returns text rather than performing the write.</p>
   */
  encode(text: string): string;
  classify(line: string): AdapterEvent;
}

/** Nothing was said. Its own constant because three adapters return it on most lines. */
export const NOTHING: AdapterEvent = { kind: 'nothing' };

/**
 * Parse a line of NDJSON, or say it was nothing.
 *
 * <p>Every one of the three vendors logs prose to stdout beside its events — a banner, a warning, a
 * progress note — so an unreadable line is the ordinary case and never a failure.</p>
 */
export function parsed(line: string): Record<string, unknown> | undefined {
  try {
    const value: unknown = JSON.parse(line);

    return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : undefined;
  } catch {
    return undefined;
  }
}

/** A field, when it is a non-empty string. The shape of these payloads is a vendor's promise, not ours. */
export function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}
