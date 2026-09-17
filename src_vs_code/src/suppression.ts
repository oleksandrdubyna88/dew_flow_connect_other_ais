import { Notice, buttonsOf } from './notice';
import { NotificationRecord } from './notifications';

/**
 * How much of one repeating fault the ledger keeps, and when it says so out loud.
 *
 * <p>109 call sites and one loop is a million lines. This is the bound — and it is keyed on the
 * SITUATION, not on a clock. The first draft of the plan collapsed repeats inside a 60-second
 * window, which folds a fault recurring in forty seconds together and separates one recurring in an
 * hour; backwards, for the question anybody actually asks. The mechanism already in this repository
 * keys the other way: `serverSettingsSync.ts:247` reports once per version and `:171` clears that on
 * a successful write, *"the situation is over; a stand-down after this is news, not a repeat."*</p>
 *
 * <h2>It bounds the LEDGER, never the toast</h2>
 *
 * <p>Nothing here can stop a message being shown. The funnel's promise is that recording is
 * additive — no message stops being a toast and none is added — so a suppressed record is a record
 * the file does not keep, not a message the person does not get. That matters for reading this
 * module: at the ceiling the person is still being told a hundred times over, which is why the storm
 * record is a row and not a hundred-and-first toast on a screen already full of them.</p>
 *
 * <h2>Exact below the bound, explicitly truncated above it, never ambiguous between</h2>
 *
 * <p>No sampling. The plan's first draft wrote the 1st, 10th, 100th and so on, and the consultation
 * killed it with one sentence that cannot be worked around: <b>10 occurrences and 99 occurrences
 * produce an identical ledger.</b> A sampled ledger can only answer "at least N" while the design
 * claimed a reader could see exactly how far it went. 1000 records is 400 KB — the whole cost of not
 * lying.</p>
 *
 * <p>Plan: `todo/PLAN_every_message_is_written_down.md`, section E.</p>
 */

/** When the meta-alert fires: a fault that has repeated this many times is news in itself. */
export const STORM_AT = 100;

/** The most records one `(code, subject)` may write in one run. Beyond it, one row says so. */
export const CEILING = 1000;

/**
 * The most records a run keeps beyond each code's first.
 *
 * <p>~2 MB at 400 B a record, and the number that makes this file's growth a bound rather than a
 * hope.</p>
 */
export const RUN_BUDGET = 5000;

/**
 * What `failure` and `stand-down` keep beyond the budget, and nothing else may spend.
 *
 * <p>A budget that could starve the settings refusal arriving after a churn loop would protect
 * storage at the cost of the one record the ledger exists for. Operator ruling, 2026-09-16.</p>
 */
export const FAULT_RESERVE = 1000;

/**
 * The most `(code, subject)` pairs one run can ever be counting, and it is not a guess.
 *
 * <p><b>This replaced an LRU of 512, which the code round was right to call a defect.</b> An LRU
 * evicts, an evicted key returns with its count at one, and two things that must not restart
 * restarted with it: `notifyOnce` showed a toast it had promised to show once, and a repeat
 * threshold that had climbed to ninety went back to zero — so a condition recurring slowly among
 * many others could never reach its storm at all. Two reviewers found it from two directions.</p>
 *
 * <p>Nothing is evicted now. That is affordable because the run budget already bounds it: a key
 * enters this map only when a record for it is WRITTEN, and writes are capped at one per code plus
 * the budget plus the fault reserve. The ceiling below is therefore arithmetic rather than a guess,
 * and `tracked()` exists so a test can assert it instead of trusting this paragraph.</p>
 */
export const KEYS_REMEMBERED = 512;

/** Which bound a storm record marks, so a reader can group the same crossing from two windows. */
export type StormBound = typeof STORM_AT | typeof CEILING | typeof RUN_BUDGET;

/** What `admit` decided about one occurrence. */
export interface Verdict {
  /** Whether the record goes on disk. The toast is shown either way. */
  readonly write: boolean;
  /** This occurrence's ordinal for its `(code, subject)` in this run. Diagnostic only. */
  readonly seq: number;
  /**
   * Whether this run has never seen this `(code, subject)` before.
   *
   * <p>What `notifyOnce` shows on, and it is NOT `seq === 1` — that was the bug. A key evicted from
   * a bounded cache comes back with `seq` at one while this stays false, because the question "have
   * I told them about this already" is about the run and not about a cache.</p>
   */
  readonly firstEver: boolean;
  /** A meta-alert this occurrence crossed a bound into. At most two per key, one per run. */
  readonly storm?: NotificationRecord;
}

/** The bounded state of one run. */
export interface Suppressor {
  readonly admit: (notice: Notice, at: Date) => Verdict;
  /** How many keys this run is counting. A seam for the test that pins the bound. */
  readonly tracked: () => number;
  /**
   * The condition this names has ended, so the next occurrence is a first occurrence again.
   *
   * <p>This is what makes "it came back" visible, and what a clock window destroys.</p>
   */
  readonly resolved: (code: string, subject?: string) => void;
}

interface Seen {
  readonly firstMs: number;
  readonly count: number;
}

/**
 * One key, unambiguously.
 *
 * <p>A JSON pair rather than two strings joined by a separator: a subject is free text — a path, a
 * server name, a sentence the server sent — and any separator picked here is one a subject may
 * contain. `["a-b", ""]` and `["a", "b"]` cannot collide.</p>
 */
function keyOf(code: string, subject: string | undefined): string {
  return JSON.stringify([code, subject ?? '']);
}

/** Records per minute, or nothing at all when no window was observed. */
function ratePerMinute(count: number, firstMs: number, atMs: number): number | undefined {
  const minutes = (atMs - firstMs) / 60_000;

  return minutes > 0 ? count / minutes : undefined;
}

/** The rate as a clause, or an empty one — a measurement not taken must not render as `0`. */
function rateClause(count: number, firstMs: number, atMs: number): string {
  const rate = ratePerMinute(count, firstMs, atMs);

  return rate === undefined ? '' : `, about ${rate.toFixed(1)} a minute`;
}

/**
 * The meta-alert for one key crossing one bound.
 *
 * <p><b>No number in the title.</b> A title carrying the count mints a fresh key per occurrence and
 * would be uncollapsible itself — the exact defect that made the plan stop keying repeats on titles.
 * The count and the rate live in `detail`, which nothing groups on.</p>
 */
function keyStorm(
  notice: Notice,
  seen: Seen,
  bound: StormBound,
  run: string,
  pid: number,
  at: Date,
): NotificationRecord {
  const since = new Date(seen.firstMs).toISOString();
  const reached = bound === CEILING
    ? `${notice.code} has filled its share of the ledger`
    : `${notice.code} keeps happening`;
  const cure = bound === CEILING
    ? 'Further repeats of this are no longer written down in this run. The count is exact up to the '
      + 'ceiling; past it the ledger says only that it continued. Fix what it names, or restart the '
      + 'window to start a fresh run.'
    : 'Something is retrying and not succeeding. The notifications page sorts on this code; the '
      + 'count resets when the condition clears.';

  return {
    utc: at.toISOString(),
    class: 'storm',
    source: notice.source,
    code: notice.code,
    ...(notice.subject === undefined ? {} : { subject: notice.subject }),
    title: reached,
    detail: `${seen.count} since ${since}${rateClause(seen.count, seen.firstMs, at.getTime())}`,
    cure,
    run,
    pid,
    bound,
  };
}

/** The meta-alert for the run itself. Written once, whatever spends the last of the budget. */
function budgetStorm(run: string, pid: number, at: Date): NotificationRecord {
  return {
    utc: at.toISOString(),
    class: 'storm',
    source: 'notify',
    code: 'notifications-run-budget-spent',
    title: 'This window has written down as many notifications as it keeps',
    detail: `${RUN_BUDGET} records beyond each code's first, in one run.`,
    cure: 'Failures and stand-downs are still recorded, out of a reserve kept for them. Ordinary '
      + 'repeats are not, until this window is restarted. Something is looping — the notifications '
      + 'page, sorted by count, names it.',
    run,
    pid,
    bound: RUN_BUDGET,
  };
}

/**
 * The bounded state of one run.
 *
 * <p>A factory rather than module state, on `textCopier`'s shape: a test gets its own, and no test
 * has to remember to reset a global before the next one reads it.</p>
 *
 * <p><b>Why `heard` is a Set of CODES and the budget charges against it.</b> The plan bounded the
 * run with a budget on repeats of a `(code, subject)`, to stop "a loop churning 512 distinct
 * subjects" evicting its own counter and writing for ever — and that bound does not close that hole.
 * An evicted key returns looking like a first occurrence, a first occurrence is never charged, and
 * the loop writes for ever anyway. So the budget is charged against the first time each CODE speaks
 * instead. Codes are string literals at call sites, so that set is finite, is never evicted, and
 * cannot be reset by the churn it exists to stop. The cost is stated rather than hidden: once the
 * budget is spent, a genuinely new `(code, subject)` of an ordinary class is not written either —
 * which is what the `failure`/`stand-down` reserve above is for, and why it is not optional.</p>
 *
 * <p><b>A QUESTION is never suppressed, by any bound.</b> A notice carrying buttons or a modal is
 * asking a person something, and a destructive confirmation that is asked, answered and obeyed with
 * neither side on disk is precisely the audit this ledger exists to be. It is safe because a
 * question cannot storm: every one of them waits for a hand, so a loop of them is serialised by the
 * person answering it. Found by codex on the code round, and it was the most valuable finding in
 * the round — the budget had a path that silently disabled the auditing of destructive actions in
 * exactly the conditions where somebody would later need it.</p>
 */
export function suppressor(run: string, pid: number): Suppressor {
  const seen = new Map<string, Seen>();
  const heard = new Set<string>();
  let spent = 0;
  let budgetSaid = false;

  const affordable = (notice: Notice): boolean => {
    if (notice.modal === true || buttonsOf(notice).length > 0) {
      return true;
    }
    const kind = notice.class;

    return spent < (kind === 'failure' || kind === 'stand-down' ? RUN_BUDGET + FAULT_RESERVE : RUN_BUDGET);
  };

  return {
    tracked: () => seen.size,
    admit: (notice, at) => {
      const key = keyOf(notice.code, notice.subject);
      const before = seen.get(key);
      const firstEver = before === undefined;
      const now: Seen = {
        firstMs: before?.firstMs ?? at.getTime(),
        count: (before?.count ?? 0) + 1,
      };

      const firstOfThisCode = !heard.has(notice.code);
      heard.add(notice.code);
      if (firstOfThisCode) {
        seen.set(key, now);

        return { write: true, seq: now.count, firstEver };
      }
      if (now.count > CEILING) {
        seen.set(key, now);

        return { write: false, seq: now.count, firstEver };
      }
      if (!affordable(notice)) {
        // NOT remembered. The map grows only with what is written, which is what makes its size
        // arithmetic rather than a guess: one key per code, plus the budget, plus the reserve. A
        // refused key left in it would let a churn loop grow memory without bound after the ledger
        // had already stopped growing, which is the wrong way round.
        if (budgetSaid) {
          return { write: false, seq: now.count, firstEver };
        }
        budgetSaid = true;

        return { write: false, seq: now.count, firstEver, storm: budgetStorm(run, pid, at) };
      }
      spent += 1;
      seen.set(key, now);
      if (now.count !== STORM_AT && now.count !== CEILING) {
        return { write: true, seq: now.count, firstEver };
      }

      return {
        write: true,
        seq: now.count,
        firstEver,
        storm: keyStorm(notice, now, now.count === CEILING ? CEILING : STORM_AT, run, pid, at),
      };
    },
    resolved: (code, subject) => {
      seen.delete(keyOf(code, subject));
    },
  };
}
