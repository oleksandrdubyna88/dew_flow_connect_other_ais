import { ReportedUsage } from './chatUsage';

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
  /**
   * The answer, and what the vendor said it cost.
   *
   * <p>`usage` is optional because a vendor may say nothing, and because it is only ever read for a
   * ledger line — a turn whose numbers are missing is still an answer. What each vendor reports and
   * where was MEASURED rather than read out of a manual
   * (`research/PLAN_an_answer_as_it_arrives.md`), and the three differ enough that the numbers are
   * not comparable: `claude` bills a real `total_cost_usd` and counts cache reads, `agy` reports
   * tokens and no money at all, and `codex` reports a CUMULATIVE total for the thread rather than
   * the cost of one turn. Normalising that is `chatUsage.turnTokens`'s job, not an adapter's — an
   * adapter reports what its vendor said.</p>
   */
  | { readonly kind: 'answer'; readonly text: string; readonly usage?: ReportedUsage | undefined }
  /**
   * What the turn cost, arriving on an event of its own.
   *
   * <p>It exists because `codex` does not put its numbers on the answer: the answer is an
   * `item.completed` and the usage is on `turn.completed`, a separate line arriving afterwards. An
   * adapter cannot attach one to the other without holding state between calls, which `classify` is
   * deliberately unable to do — it reads one line and says what that line meant.</p>
   *
   * <p><b>Which shape may send it, and when — the rule, because the loose version of this sentence
   * was a trap.</b> A `per-turn` vendor may send it at any point up to the process exiting: that
   * shape's turn ends at the EXIT, so a usage line printed after the answer is still read (the
   * launcher fires its exit event on `close`, once stdout has been flushed, and replays lines held
   * before anybody subscribed). A `persistent` vendor may send it only BEFORE the answer, because
   * that shape's turn ends at the ANSWER — the session settles there and moves on, and a usage line
   * printed afterwards would belong to a turn that is already over.</p>
   *
   * <p>Both vendors of that shape today put their numbers ON the answer event, which is the simplest
   * way to satisfy this and the way a new persistent adapter should follow;
   * `chatAdapters.test.ts` pins it. A future persistent CLI that insists on a trailing usage line
   * would need the seam to gain a turn-complete event rather than an adapter to work around it —
   * which is the shape of change this comment exists to make visible before somebody spends a day
   * discovering it. (codex, the second code round.)</p>
   */
  | { readonly kind: 'usage'; readonly usage: ReportedUsage }
  | { readonly kind: 'failure'; readonly failure: string }
  | { readonly kind: 'nothing' };

/** How a vendor is driven. See the note above — the difference reaches the context-loss rule. */
export type ChatShape = 'persistent' | 'per-turn';

/**
 * Everything a command line is built from, as one value.
 *
 * <p>Two fields today and they are not the same kind of thing, which is exactly why they travel
 * together: `resume` is the vendor's own bookkeeping and only a `per-turn` vendor has any use for
 * it, while `model` is the PERSON's choice and every vendor must honour it. A third thing a launch
 * needs — permissions, an effort level, a thinking budget — arrives as a field here, and the
 * compiler then asks every adapter what it does about it.</p>
 */
export interface ChatLaunch {
  /**
   * The session to continue, for a `per-turn` vendor's second and later turns. Empty means a new
   * conversation, and a `persistent` vendor ignores it entirely.
   */
  readonly resume: string;
  /**
   * The model the person picked, or empty for the CLI's own default.
   *
   * <p>Empty means send NO model flag rather than an empty one: asking a CLI for a model called ""
   * is a different request from not asking, and the first is an error the person did not make.</p>
   */
  readonly model: string;
}

/** A launch that asks for nothing in particular — a first turn on whatever the row is set to. */
export const NEW_CONVERSATION: ChatLaunch = { resume: '', model: '' };

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
   * Whether this vendor's token counts are a RUNNING TOTAL for the thread rather than one turn's.
   *
   * <p>`codex` counts up: turn one reports 1000, turn two reports 1200, and 1200 is not what turn two
   * cost. Recording what it says would over-bill every conversation on that vendor increasingly, the
   * longer it ran. The session subtracts what the thread last reported — see `perTurnUsage` in
   * `cliChatSession.ts` — so a `TurnResult` always carries the cost of ONE turn whatever the vendor
   * counts in.</p>
   *
   * <p><b>It governs the TOKENS. A cumulative vendor's money is refused, not differenced</b> —
   * `turnCost` answers `null` for one, because no vendor has ever been seen billing a running total
   * and inventing the arithmetic for it was dead code the operator ruled out on 2026-09-10. A `null`
   * bill means "nobody told us what this turn cost", and the log page prices such a row from a public
   * list instead and marks it as an estimate. Today the one cumulative vendor reports no money at
   * all, so nothing observable turns on this.</p>
   *
   * <p><b>It lives on the adapter because it is a fact about the wire protocol</b>, and the adapter is
   * where this codebase keeps those. It was first a list of runtime NAMES in `chatUsage.ts`, and a
   * gate reviewer named the flaw: implementing `ChatAdapter` was then not enough to be billed
   * correctly, because an unlisted runtime silently defaulted to per-turn and its conversations would
   * inflate without anything saying so. A required field on the interface cannot be forgotten — the
   * compiler asks.</p>
   */
  readonly cumulative: boolean;
  /**
   * The command line for a process that will answer turns.
   *
   * <p>It takes a CONTEXT rather than a bare resume id, and that is a shipped defect rather than
   * taste. The parameter used to be `resume: string`, which left nowhere for the chosen model to
   * travel — so all three adapters omitted it, each of them correctly, and the model picker decided
   * nothing at all for a local CLI while a Team-server chat honoured it. The audit of 2026-09-09
   * (finding 7) compiled the launch for every runtime and swapped one model name for another:
   * byte-identical command lines. A parameter that does not exist cannot be forgotten by one adapter
   * and remembered by another; this one makes the compiler ask all of them.</p>
   */
  argv(launch: ChatLaunch): readonly string[];
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

/** A field, when it is a finite non-negative number. Vendors omit, misspell and stringify these. */
export function count(value: unknown): number {
  const asNumber = typeof value === 'number' ? value : Number(text(value));

  return Number.isFinite(asNumber) && asNumber > 0 ? asNumber : 0;
}

/** A money field, kept as `null` when the vendor said nothing — a zero would claim the turn was free. */
export function money(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** The nested object at `key`, or nothing. Every vendor buries its usage one level down. */
export function inside(event: Record<string, unknown>, key: string): Record<string, unknown> | undefined {
  const value = event[key];

  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : undefined;
}

// `spent` used to be here as well as in `cliChatSession.ts`, with the same body and two copies of
// the same paragraph explaining it. It lives in `chatUsage.ts` now, beside the type it is about, and
// both adapters import it from there — see the note on it.
