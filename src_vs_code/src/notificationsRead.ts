import { CEILING as RUN_CEILING } from './suppression';
import { NotificationRecord, isKnownClass } from './notifications';
import { PlacedRecord } from './notificationsFile';
import { Span, alreadyRead } from './notificationsSeen';

/**
 * What the notifications page shows: two ledgers merged, and repeats collapsed into rows.
 *
 * <p>Pure. It touches no disk and no `vscode`, which is what lets every rule below be asserted by
 * a test — the split `notice.ts` / `notify.ts` already uses, for the same reason.</p>
 *
 * <h2>A row is a GROUP, and that is a decision</h2>
 *
 * <p>One row per `(class, code, subject)`, carrying how many times it happened and how fast.
 * Operator, 2026-09-17. A page of a thousand identical rows is the thing the ledger already
 * refuses to WRITE — the suppression in `suppression.ts` exists precisely so that a repeating
 * fault does not fill a file — and it would be perverse to render what we declined to store.</p>
 *
 * <p><b>The CLASS is in the key.</b> Tabs are per class, so a row must belong to exactly one tab,
 * and `code` alone does not guarantee that: nothing in the funnel stops one code being raised as a
 * `refusal` at one call site and a `failure` at another. With the class in the key, tab membership
 * is true by construction; a code that changed class shows as two rows, which is honest, rather
 * than as one row that moves between tabs as records arrive.</p>
 *
 * <h2>Counting is counting ROWS</h2>
 *
 * <p>Never `seq`, never an in-memory counter. A map can be evicted and a process can die, and a
 * number on screen that a reader cannot re-derive from the file is a number that will be wrong one
 * day. Within a row, records are grouped by `run` FIRST and the runs are presented together: three
 * windows that each hit the ceiling are not "3000 repeats" of one incident, they are three runs
 * that each hit their ceiling, and the row says so.</p>
 */

/** Which ledger a record came out of. The page says so; the watermark is keyed on it. */
export type Ledger = 'extension' | 'server';

/**
 * What has been read of EACH ledger, kept apart.
 *
 * <p><b>A byte offset means nothing without the file it is an offset into</b>, and flattening the
 * two into one list was a blocking defect: a 10 KB range acknowledged in `notifications.jsonl`
 * marked every `server-notices.jsonl` record below 10 KB read — and the server's file is the small
 * one, so in practice it marked nearly all of them. `notificationsSeen.ts` keys its ranges by
 * ledger for exactly this reason, and the caller threw that away. A type with one field per ledger
 * makes flattening them impossible rather than merely wrong. (gemini, the S5 code round.)</p>
 */
export interface ReadPerLedger {
  readonly extension: readonly Span[];
  readonly server: readonly Span[];
}

/** Nothing acknowledged, in either. */
export const NOTHING_READ: ReadPerLedger = { extension: [], server: [] };

/** One record, with where it came from and where it sits. */
export interface Arrival extends PlacedRecord {
  readonly ledger: Ledger;
}

/** What one run of one fault did. */
export interface RunTally {
  /** The run id, or an empty string for a record written before the field existed. */
  readonly run: string;
  readonly count: number;
  /** Whether this run reached its ceiling, so the count is a floor and not a total. */
  readonly ceilinged: boolean;
}

/** One row of the page. */
export interface Grouped {
  readonly key: string;
  readonly class: string;
  readonly code: string;
  readonly subject: string;
  readonly source: string;
  /** The LATEST occurrence: "when did this last happen" is what the page is opened to answer. */
  readonly when: string;
  /** The first, which the rate needs and the row says "since" with. */
  readonly first: string;
  readonly title: string;
  readonly cure: string;
  readonly ledger: Ledger;
  readonly repeats: number;
  /** Per run, newest run first. One entry is the ordinary case. */
  readonly runs: readonly RunTally[];
  /** Occurrences a minute, or undefined when no window worth measuring was observed. */
  readonly ratePerMin: number | undefined;
  /** Whether every occurrence in this row falls inside an acknowledged range. */
  readonly read: boolean;
}

/**
 * How many occurrences of one `(code, subject)` one run may write before the ledger stops.
 *
 * <p><b>The writer's own constant, imported.</b> The first version copied the number here with a
 * rationale about keeping the reader free of the writing half, and the code round was right that
 * the rationale was not worth the drift: lower the writer's ceiling and this module would render a
 * capped run as an exact total, or call a capped 1500-record run uncapped. Silently, and only on
 * the day somebody changed the bound.</p>
 */
export { CEILING as RUN_CEILING } from './suppression';


/** A row's identity. JSON so that a subject containing any separator cannot forge another key. */
export function keyOf(kind: string, code: string, subject: string): string {
  return JSON.stringify([kind, code, subject]);
}

/** The shortest window a rate may be measured over. */
const A_SECOND = 1000;

/**
 * Occurrences a minute, or nothing at all.
 *
 * <p>Absent for a single occurrence and for any span under a second — not merely for a zero-length
 * one. Forty occurrences in 40 ms divides into tens of thousands a minute and sorts straight to the
 * top of the one column the storm feature exists for, which is the same defect as rendering an
 * unmeasured rate as `0`, in the other direction. (gemini, the S5 plan round.)</p>
 */
export function ratePerMinute(count: number, firstMs: number, lastMs: number): number | undefined {
  const span = lastMs - firstMs;
  if (count < 2 || span < A_SECOND) {
    return undefined;
  }

  return count / (span / 60_000);
}

function msOf(record: NotificationRecord): number {
  const at = new Date(record.utc).getTime();

  return Number.isFinite(at) ? at : 0;
}

/**
 * Every run this row saw, newest first.
 *
 * <p><b>A record with no `run` is its own run, named as unknown.</b> The field arrived with S1 and
 * the ledger is kept for ever, so a file will hold records from before it. Folding them all into
 * one pseudo-run would invent a session that never existed and produce a "1000+ in 1 run" that is
 * the sum of a year.</p>
 */
function runsOf(arrivals: readonly Arrival[]): readonly RunTally[] {
  const perRun = new Map<string, number>();
  for (const arrival of arrivals) {
    const run = arrival.record.run ?? '';
    perRun.set(run, (perRun.get(run) ?? 0) + 1);
  }

  return [...perRun].map(([run, count]) => ({ run, count, ceilinged: count >= RUN_CEILING }));
}

/**
 * Two ledgers' records, grouped into the rows the page shows.
 *
 * <p>The order inside a row is by time; the order OF the rows is the caller's, because sorting is
 * a column the person clicks and belongs to the page.</p>
 */
export function group(arrivals: readonly Arrival[], read: ReadPerLedger): readonly Grouped[] {
  const byKey = new Map<string, Arrival[]>();
  for (const arrival of arrivals) {
    const key = keyOf(arrival.record.class, arrival.record.code, arrival.record.subject ?? '');
    const held = byKey.get(key) ?? [];
    held.push(arrival);
    byKey.set(key, held);
  }

  return [...byKey].map(([key, held]) => {
    const inOrder = [...held].sort((a, b) => msOf(a.record) - msOf(b.record));
    const oldest = inOrder[0] as Arrival;
    const newest = inOrder[inOrder.length - 1] as Arrival;

    return {
      key,
      class: newest.record.class,
      code: newest.record.code,
      subject: newest.record.subject ?? '',
      source: newest.record.source,
      when: newest.record.utc,
      first: oldest.record.utc,
      // The NEWEST wording, because a sentence can be reworded between releases and the one a
      // person is about to act on is the current one.
      title: newest.record.title ?? '',
      cure: newest.record.cure ?? '',
      ledger: newest.ledger,
      repeats: inOrder.length,
      runs: runsOf(inOrder),
      ratePerMin: ratePerMinute(inOrder.length, msOf(oldest.record), msOf(newest.record)),
      // A row counts as read only when EVERY occurrence in it does. One unread repeat of a fault
      // somebody acknowledged last week is news, and hiding it under "read" is how the acknowledged
      // watermark would start lying.
      read: inOrder.every((arrival) => alreadyRead(arrival.at, read[arrival.ledger])),
    };
  });
}

/**
 * What a row says in its Repeats column.
 *
 * <p>A run that reached its ceiling is shown as "1000+", never as a number it is not: past the
 * ceiling the ledger stops writing, so the total is unknowable and a bare figure would be a lie
 * with a decimal point. Several runs are named separately rather than summed — three windows that
 * each hit the ceiling are not one incident of 3000.</p>
 */
export function repeatsSaid(row: Grouped): string {
  const ceilinged = row.runs.filter((run) => run.ceilinged);
  if (ceilinged.length === row.runs.length && ceilinged.length > 1) {
    return `${RUN_CEILING}+ in each of ${ceilinged.length} runs`;
  }
  if (ceilinged.length === 1 && row.runs.length === 1) {
    return `${RUN_CEILING}+`;
  }
  if (row.runs.length > 1) {
    return `${row.repeats} in ${row.runs.length} runs`;
  }

  return String(row.repeats);
}

/** The tabs the page offers: every class this build knows, plus one for anything it does not. */
export const UNKNOWN_TAB = 'other';

/** Which tab a row belongs under. An unknown class is kept and shown, never dropped. */
export function tabOf(row: Grouped): string {
  return isKnownClass(row.class) ? row.class : UNKNOWN_TAB;
}
