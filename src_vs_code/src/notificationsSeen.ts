/**
 * What of the notification ledgers this person has already been shown.
 *
 * <h2>Append-only, because a watermark that is rewritten moves backwards</h2>
 *
 * <p>One line per "this window read bytes [from, to) of ledger Y", and the read set is the UNION of
 * those intervals per ledger. Nothing here rewrites a file, and that is the whole design rather
 * than a preference: the plan's earlier drafts had a whole-file write, then a temp+rename holding a
 * component-wise maximum, and both lose updates. The second is the subtler mistake — <b>temp+rename
 * is atomic but it is not mutual exclusion</b>. Two windows read the old offsets, each computes its
 * own maximum, and they rename in reverse order; the later rename wins with the smaller value, the
 * watermark moves backwards, and records somebody has already read come back as unread. An append
 * has no read-modify-write in it, so two windows append two lines and the union is the union.</p>
 *
 * <h2>Intervals, not a maximum — the plan promised both and they are not the same</h2>
 *
 * <p>The page shows the newest N, not the file. Storing only `max(to)` would mark every byte below
 * it read, including the thousands that were never rendered, which is exactly the claim the range
 * rule exists to prevent. Two reviewers found the contradiction independently on the S5 plan
 * round.</p>
 *
 * <h2>Offsets, not timestamps</h2>
 *
 * <p>`utc` comes from each writer's own clock. A WSL distro after a resume, or a server started
 * before an NTP step, writes a record stamped EARLIER than a watermark another process has already
 * set — and that record is then never counted, ever, while sitting visibly on the page. A byte
 * offset into an append-only file is monotone by construction.</p>
 */

import { appendLine, readLedger } from './jsonlLedger';
import { join } from 'node:path';

/** Beside the ledgers it describes, and in `DATA_TO_MOVE` with them. */
export const SEEN_FILE = 'notifications-seen.jsonl';

export function seenPath(dataDir: string): string {
  return join(dataDir, SEEN_FILE);
}

/** A half-open byte range of one ledger: `from` inclusive, `to` exclusive. */
export interface Span {
  readonly from: number;
  readonly to: number;
}

/** One acknowledgement, as it is written down. */
export interface SeenRange extends Span {
  readonly utc: string;
  /** The ledger's FILE NAME, so the two ledgers cannot be confused for one another. */
  readonly ledger: string;
}

/** The line an acknowledgement becomes. */
export function seenLine(range: SeenRange): string {
  return `${JSON.stringify(range)}\n`;
}

/**
 * A number that is a real, non-negative byte offset, or nothing.
 *
 * <p>SAFE integer, not merely finite. `1e21` and `3.5` are both finite and neither is a byte offset:
 * a fractional bound makes `alreadyRead` answer differently for two records on the same byte, and a
 * value past 2^53 compares greater than every offset the file will ever have, so one corrupt line
 * would mark a ledger read for ever. Errs toward UNREAD, which is the safe direction.
 * (codex, the S5 code round.)</p>
 */
function offsetOf(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : undefined;
}

/**
 * One line back, or nothing when it is torn, foreign or nonsense.
 *
 * <p>Dropped rather than thrown, and the previous ranges stand — which errs toward UNREAD, the safe
 * direction. A file being appended to by another window can end in half a line, and one torn line
 * must not cost a year of acknowledgements.</p>
 */
export function parseSeenLine(line: string): SeenRange | undefined {
  let value: unknown;
  try {
    value = JSON.parse(line);
  } catch {
    return undefined;
  }
  if (value === null || typeof value !== 'object') {
    return undefined;
  }
  const row = value as Record<string, unknown>;
  const ledger = typeof row['ledger'] === 'string' ? row['ledger'] : '';
  const from = offsetOf(row['from']);
  const to = offsetOf(row['to']);
  const utc = typeof row['utc'] === 'string' ? row['utc'] : '';
  if (ledger.length === 0 || from === undefined || to === undefined || to <= from) {
    return undefined;
  }

  return { utc, ledger, from, to };
}

/** Every acknowledgement in one file's text. A torn last line costs itself and nothing else. */
export function parseSeen(text: string): SeenRange[] {
  const kept: SeenRange[] = [];
  for (const line of text.split('\n')) {
    const trimmed = line.trim();
    if (trimmed.length === 0) {
      continue;
    }
    const range = parseSeenLine(trimmed);
    if (range !== undefined) {
      kept.push(range);
    }
  }

  return kept;
}

/**
 * Overlapping and touching ranges merged into the fewest that cover the same bytes.
 *
 * <p>Touching counts: `[0,100)` and `[100,200)` are one range, because the byte at 100 begins the
 * second and nothing lies between them. Leaving them apart would be harmless for correctness and
 * would let the list grow without bound on a page opened repeatedly, which is the thing the
 * compaction in the growth budget relies on being able to do.</p>
 */
export function merge(spans: readonly Span[]): readonly Span[] {
  const sorted = [...spans].sort((a, b) => a.from - b.from);
  const merged: Span[] = [];
  for (const span of sorted) {
    const last = merged[merged.length - 1];
    if (last !== undefined && span.from <= last.to) {
      merged[merged.length - 1] = { from: last.from, to: Math.max(last.to, span.to) };
    } else {
      merged.push({ from: span.from, to: span.to });
    }
  }

  return merged;
}

/** What has been acknowledged of each ledger, merged. */
export function readSoFar(ranges: readonly SeenRange[]): ReadonlyMap<string, readonly Span[]> {
  const byLedger = new Map<string, Span[]>();
  for (const range of ranges) {
    const held = byLedger.get(range.ledger) ?? [];
    held.push({ from: range.from, to: range.to });
    byLedger.set(range.ledger, held);
  }

  return new Map([...byLedger].map(([ledger, spans]) => [ledger, merge(spans)]));
}

/**
 * Where the unacknowledged TAIL of a ledger begins.
 *
 * <p>The highest byte anything has been acknowledged up to. Everything after it is unread for
 * certain; anything before it that no span covers is unread too, but it is OLDER, and the count
 * deliberately does not walk down to find out how much. That is what keeps the panel cheap: the
 * watcher re-renders every window every five seconds, and a count that meant "parse the newest
 * three thousand records" would be a megabyte of parsing per window per tick, from a directory
 * that may be a NAS.</p>
 */
export function tailBegins(spans: readonly Span[]): number {
  return spans.reduce((highest, span) => Math.max(highest, span.to), 0);
}

/**
 * Only the ranges that are about the ledger on disk NOW.
 *
 * <p>A ledger can get SHORTER — rotated, restored from a backup, replaced by hand — and the
 * acknowledgements written against the old one then reach past the end of the new file. Left alone,
 * `[0, 400000)` against a 45-byte file starts the count beyond the file, finds nothing, and renders
 * as "Nothing new" while the replacement is full of records nobody has seen.</p>
 *
 * <p>A range that does not fit is DROPPED, not cut down. Cutting was the first fix and it was the
 * same bug wearing a hat: `[0, 400000)` cut to `[0, 45)` claims every record in the new file has
 * been read, which is exactly the claim the whole watermark exists to prevent — quieter, and
 * therefore worse. After a rotation there is no correspondence between the old offsets and the new
 * file's bytes, so the only honest answer is that nothing is known to have been read. That errs
 * toward UNREAD: a record may be shown twice, which is the harmless direction.
 * (Found by making the rotation test pass for the wrong reason.)</p>
 *
 * <p>No LEGITIMATE range can exceed the size, which is what makes the rule safe: an acknowledgement
 * is written with `to` at the end of the last COMPLETE line, and a file only grows past that.</p>
 */
export function onlyWithin(spans: readonly Span[], size: number): readonly Span[] {
  return spans.filter((span) => span.to <= size);
}

/**
 * Whether a range is ALREADY acknowledged, so appending it again would say nothing new.
 *
 * <p>An acknowledgement is a line on an append-only file, so a redundant one is not wrong — the
 * union is the same union — it is WASTE, and it is waste that accumulates in the one file the cheap
 * path has to read. *Mark everything read* writes `[0, end)` and then redraws, and the redrawn page
 * dutifully offers the window it was given; every reopen of an unchanged page did the same. That is
 * one line a time for an answer already on the disk. (codex, the second S5 code round.)</p>
 */
export function covers(spans: readonly Span[], span: Span): boolean {
  return merge(spans).some((held) => held.from <= span.from && held.to >= span.to);
}

/** Whether one record's line start has already been acknowledged. What the PAGE marks rows with. */
export function alreadyRead(at: number, spans: readonly Span[]): boolean {
  return spans.some((span) => at >= span.from && at < span.to);
}

/** Whether anything before the tail is still unacknowledged — the "+ older" the panel says. */
export function olderRemain(spans: readonly Span[]): boolean {
  const merged = merge(spans);
  const first = merged[0];

  return merged.length > 1 || (first !== undefined && first.from > 0);
}

/**
 * Write one acknowledgement down.
 *
 * <p>On the notifications chain, beside the ledger it describes, so an acknowledgement cannot
 * overtake the records it acknowledges.</p>
 */
export function acknowledge(
  dataDir: string,
  range: SeenRange,
  onFailure?: (reason: unknown) => void,
): Promise<void> {
  return appendLine(
    seenPath(dataDir),
    seenLine(range),
    'an acknowledgement',
    { chain: 'notifications', ...(onFailure === undefined ? {} : { onFailure }) },
  );
}

/**
 * Everything acknowledged so far, or nothing at all.
 *
 * <p><b>Nothing at all is not the same as "everything is read".</b> A missing file is a first run
 * and everything is unread, which is correct. A file that exists and cannot be READ is a different
 * fact, and the caller is told which by `readLedger`'s own contract — the panel says "not read yet"
 * rather than 0, on the Bugz precedent, because a broken read rendering as a clean zero is how a
 * person stops trusting a count.</p>
 */
export function readSeen(dataDir: string): Promise<readonly SeenRange[]> {
  return readLedger(seenPath(dataDir), parseSeen, 'acknowledgements');
}
