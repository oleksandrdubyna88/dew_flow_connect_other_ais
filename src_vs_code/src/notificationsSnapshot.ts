import { Arrival, ReadPerLedger, group } from './notificationsRead';
import { NOTIFICATIONS_FILE, PlacedRecord, SERVER_NOTICES_FILE } from './notificationsFile';
import { PageState } from './notificationsPage';
import { SeenRange, Span, olderRemain, readSoFarWithin } from './notificationsSeen';

/**
 * What one draw found, turned into what the page is handed.
 *
 * <p>Pure, and that is the point of it existing at all. This is the half-dozen decisions a draw
 * makes — which ledger could be read, which acknowledged ranges are still about the files on disk,
 * whether older records remain, and what window may therefore be acknowledged — and every one of
 * them used to live inside `notificationsPanel.ts`, which imports `vscode` and can therefore never
 * be imported by a test. The same split `notice.ts` / `notify.ts` already uses, for the same
 * reason: what is left in the host is a webview, two messages and one write.</p>
 */

/** What reading one ledger produced, or that it could not be read at all. */
export interface LedgerRead {
  readonly records: readonly PlacedRecord[];
  /** The first byte of the loaded window. Non-zero means older records are still in the file. */
  readonly start: number;
  /** Just past the last complete record. What an acknowledgement of this window ends at. */
  readonly end: number;
  readonly readable: boolean;
}

/** A ledger that did not answer. Never an empty ledger — those are different facts. */
export const UNREADABLE_LEDGER: LedgerRead = {
  records: [], start: 0, end: 0, readable: false,
};

/** The page, and what the host may write down once the page says it rendered it. */
export interface Snapshot {
  readonly state: PageState;
  /** The window this draw is offering, per ledger FILE NAME. Empty when nothing may be claimed. */
  readonly loaded: ReadonlyMap<string, Span>;
  /** What the disk already says is read, so an acknowledgement adding nothing is not written. */
  readonly covered: ReadonlyMap<string, readonly Span[]>;
}

/** Everything one draw read. An options object because seven positional arguments is a trap. */
export interface WhatWasRead {
  readonly dataDir: string;
  readonly mine: LedgerRead;
  readonly theirs: LedgerRead;
  /** The acknowledgements, or nothing when that file could not be read either. */
  readonly seen: readonly SeenRange[] | undefined;
  readonly generation: number;
  /** Something the host has to say that outlived the draw it happened in. Usually empty. */
  readonly notice: string;
  /** How long the reads were given, so the failure sentence can say it. */
  readonly waitedSeconds: number;
}

/**
 * The page and the claim, decided together.
 *
 * <p>They belong in one function because they are one decision: an unreadable draw offers no window
 * at all, and a draw that offers a window must be the one that rendered it. Computing them apart
 * was how a snapshot that failed to render still marked three thousand records read.</p>
 */
export function snapshotOf(found: WhatWasRead): Snapshot {
  const { dataDir, mine, theirs, seen } = found;
  const unreadable = !mine.readable || !theirs.readable || seen === undefined;
  // Kept APART, and each range measured against the file it is an offset INTO — before they are
  // merged, because merging a stale range with a valid one and dropping the pair is how a rotated
  // ledger stops accepting acknowledgements altogether.
  const soFar = readSoFarWithin(seen ?? [], new Map([
    [NOTIFICATIONS_FILE, mine.end],
    [SERVER_NOTICES_FILE, theirs.end],
  ]));
  const read: ReadPerLedger = {
    extension: soFar.get(NOTIFICATIONS_FILE) ?? [],
    server: soFar.get(SERVER_NOTICES_FILE) ?? [],
  };
  const arrivals: readonly Arrival[] = [
    ...mine.records.map((placed) => ({ ...placed, ledger: 'extension' as const })),
    ...theirs.records.map((placed) => ({ ...placed, ledger: 'server' as const })),
  ];

  return {
    state: {
      rows: group(arrivals, read),
      dataDir,
      older: mine.start > 0 || theirs.start > 0
        || olderRemain(read.extension)
        || olderRemain(read.server),
      loaded: arrivals.length,
      generation: found.generation,
      ...(found.notice === '' ? {} : { notice: found.notice }),
      // An unreadable ledger is SAID, never rendered as empty tabs. A person who sees an empty page
      // concludes there is nothing to see, which is the opposite of what happened.
      ...(unreadable
        ? { unreadable: `the ledgers did not answer within ${found.waitedSeconds} seconds` }
        : {}),
    },
    // Nothing may be claimed for a draw that could not read what it is claiming.
    loaded: unreadable
      ? new Map()
      : new Map([
        [NOTIFICATIONS_FILE, { from: mine.start, to: mine.end }],
        [SERVER_NOTICES_FILE, { from: theirs.start, to: theirs.end }],
      ]),
    covered: new Map([
      [NOTIFICATIONS_FILE, read.extension],
      [SERVER_NOTICES_FILE, read.server],
    ]),
  };
}
