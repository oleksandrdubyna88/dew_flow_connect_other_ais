/**
 * What the panel's pages posted and the host has not finished (research/PLAN_model_search_and_busy_marks.md §3.8–§3.10).
 *
 * <p>The pages' busy mark is drawn from this record, and the record — not the page — owns the truth. A page posts a
 * setting, a prompt or a command with a sequence number; the host keeps an entry for it while the work and the render
 * it causes run, removes it in a `finally` however the work ended, and announces every change of the count to every
 * page. A page that was replaced meanwhile owns nothing: its sequence number is simply unheard, the entry still leaves,
 * and the new page learns the count from the paint, its own `ready`, and the next announcement.</p>
 *
 * <p><b>Growth.</b> One entry per operation in flight — bounded by what one person clicks while the host is busy —
 * removed in its own `finally`, and all of them settled by {@link settleEverything} when the panel goes away.</p>
 */

/** What a page needs to draw the host's part of the mark: how much is running, and for how long the oldest has. */
export interface BusySnapshot {
  readonly count: number;
  readonly oldestMs: number;
}

/** Nothing in flight. */
export const IDLE: BusySnapshot = { count: 0, oldestMs: 0 };

/** Somewhere a message can be posted: a page slot, as far as tracking needs one. */
export interface Poster {
  post(message: unknown): void;
}

/**
 * What a page numbered its work under: its sequence number, and the document that sent it. Every document counts
 * from 1 and a settle reaches the slot's CURRENT document, so the number alone could clear another document's mark;
 * the document id is echoed back so the page can tell (own review of E3).
 */
export interface Ask {
  readonly seq: number;
  readonly doc: string;
}

/** One operation in flight: what the page numbered it, the page that posted it, and when it started. */
export interface Operation<S> extends Ask {
  readonly slot: S;
  readonly startedAt: number;
}

/** The record itself. Replaced on every change, never mutated in place. */
export class InFlight<S extends Poster> {
  private next = 0;
  private entries: ReadonlyMap<number, Operation<S>> = new Map();

  constructor(private readonly now: () => number) {}

  /** Records an operation and answers the id that finishes it. */
  start(ask: Ask, slot: S): number {
    this.next += 1;
    this.entries = new Map([...this.entries, [this.next, { seq: ask.seq, doc: ask.doc, slot, startedAt: this.now() }]]);

    return this.next;
  }

  /** Removes an operation and answers it — or nothing, when it was already finished. */
  finish(id: number): Operation<S> | undefined {
    const done = this.entries.get(id);
    this.entries = new Map([...this.entries].filter(([key]) => key !== id));

    return done;
  }

  /** Everything still in flight, removed — what ending the panel settles. */
  drain(): readonly Operation<S>[] {
    const all = [...this.entries.values()];
    this.entries = new Map();

    return all;
  }

  snapshot(): BusySnapshot {
    const starts = [...this.entries.values()].map((one) => one.startedAt);

    return starts.length === 0 ? IDLE : { count: starts.length, oldestMs: this.now() - Math.min(...starts) };
  }
}

/** What a failure to post is reported through when the caller names nothing better. */
const SAY_IT = (error: unknown): void => { console.error('ConnectOtherAIs: a panel action failed', error); };

/**
 * One post, to one page, that cannot stop the next: a page closed mid-action may refuse it synchronously, and the
 * pages still open must hear what it could not (final E3 round, gemini). The refusal is reported, never swallowed.
 */
function postTo(slot: Poster, message: unknown, report: (error: unknown) => void): void {
  try {
    slot.post(message);
  } catch (error) {
    report(error);
  }
}

/** Tells every page what is running now. */
export function announce<S extends Poster>(flight: InFlight<S>, slots: readonly S[], report: (error: unknown) => void = SAY_IT): void {
  const snapshot = flight.snapshot();
  for (const slot of slots) {
    postTo(slot, { type: 'busy', ...snapshot }, report);
  }
}

/**
 * Runs one posted operation under the record: announced as it starts, settled to its poster and announced again in a
 * `finally`, whatever the work did.
 *
 * <p>This is a detached edge — the page's message handler starts it with `void` — so it catches everything, says it
 * through `report`, and settles `ok: false` rather than letting a rejection go unseen.</p>
 */
export async function tracked<S extends Poster>(
  flight: InFlight<S>,
  slots: readonly S[],
  from: S,
  ask: Ask,
  work: () => Promise<void>,
  report: (error: unknown) => void = SAY_IT,
): Promise<void> {
  const id = flight.start(ask, from);
  announce(flight, slots, report);
  let ok = false;
  try {
    await work();
    ok = true;
  } catch (error) {
    report(error);
  } finally {
    flight.finish(id);
    postTo(from, { type: 'settled', seq: ask.seq, doc: ask.doc, ok }, report);
    announce(flight, slots, report);
  }
}

/** Ending the panel: every operation still in flight is settled as not done, and the record is emptied. */
export function settleEverything<S extends Poster>(flight: InFlight<S>, report: (error: unknown) => void = SAY_IT): void {
  for (const one of flight.drain()) {
    postTo(one.slot, { type: 'settled', seq: one.seq, doc: one.doc, ok: false }, report);
  }
}

/**
 * What a write's settle waits for, in order: the write queue to have written it, then a render that STARTED after
 * `mark` — taken in the same turn the write was queued. Extracted from `PanelProvider` so the order is a value a test
 * runs rather than a line it reads (own review of E3: deleting either await left the suite green).
 */
export function settleAfterWrite(
  writes: { settled(): Promise<void> },
  renders: { mark(): number; runAfter(mark: number): Promise<void> },
): () => Promise<void> {
  const mark = renders.mark();

  return async () => {
    await writes.settled();
    await renders.runAfter(mark);
  };
}

/** A page's ask, when the message carries one: a positive whole `seq` and a non-empty `doc`. Anything else is unnumbered. */
export function askOf(message: { readonly seq?: unknown; readonly doc?: unknown }): Ask | undefined {
  const { seq, doc } = message;

  return isSeq(seq) && isDoc(doc) ? { seq, doc } : undefined;
}

function isSeq(value: unknown): value is number {
  return Number.isInteger(value) && (value as number) > 0;
}

function isDoc(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}
