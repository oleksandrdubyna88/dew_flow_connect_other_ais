import { stat } from 'node:fs/promises';
import { COUNT_CAP, LedgerGlance, UNREADABLE_GLANCE } from './notificationsCount';
import {
  NOTIFICATIONS_FILE,
  SERVER_NOTICES_FILE,
  countSince,
  missingRatherThanBroken,
  notificationsPath,
  serverNoticesPath,
} from './notificationsFile';
import { olderRemain, tailBegins } from './notificationsSeen';
import { readSoFarCheaply } from './notificationsSeenCache';

/**
 * One cheap look at both ledgers: how many are new, and whether older ones are still unopened.
 *
 * <p><b>Cheap is the requirement, not a nicety.</b> `EscalationWatcher` calls `onChanged`
 * unconditionally every 5000 ms, so this runs in every window every five seconds whatever
 * happened. A count that meant "parse the newest three thousand records" would be ~1.2 MB of JSON
 * per window per tick, from a directory that may be a NAS. So nothing here parses a record: it
 * stats two files and counts newline bytes between the watermark and the end.</p>
 *
 * <p>Its own module so that `notificationsFile.ts` keeps to reading records and
 * `notificationsSeen.ts` to the watermark; this is the one place that needs both, and a function
 * that needs two modules belongs beside neither.</p>
 */

/** How many bytes a file holds, or nothing when it exists and cannot be asked. */
async function sizeOf(path: string): Promise<number | undefined> {
  try {
    return (await stat(path)).size;
  } catch (reason: unknown) {
    // The same decision `countSince` makes, from the same function: a missing file is an ordinary
    // first run and answers zero, anything else is a fact about the disk. Two copies of this test
    // would be two chances to answer "nothing new" for a share that stopped responding.
    return missingRatherThanBroken(reason) ? 0 : undefined;
  }
}

/** What the panel says, gathered without reading a record. */
export async function glanceAtLedgers(dataDir: string): Promise<LedgerGlance> {
  const [mine, theirs] = await Promise.all([
    sizeOf(notificationsPath(dataDir)),
    sizeOf(serverNoticesPath(dataDir)),
  ]);
  if (mine === undefined || theirs === undefined) {
    return UNREADABLE_GLANCE;
  }
  // Only where it GREW, and each range measured against the file it indexes BEFORE anything is
  // merged. A rotated ledger leaves ranges reaching past the end of its replacement; one of those
  // passed to `countSince` starts the walk beyond the file, finds nothing, and renders as "Nothing
  // new" while the new ledger is full of records nobody has seen. The page had that check and this
  // path did not, which was the worse half: this is the one that runs every five seconds.
  // (gemini and codex, the S5 code rounds; the ORDER of filter and merge, CodeRabbit on the PR.)
  const soFar = await readSoFarCheaply(dataDir, new Map([
    [NOTIFICATIONS_FILE, mine],
    [SERVER_NOTICES_FILE, theirs],
  ]));
  if (soFar === undefined) {
    return UNREADABLE_GLANCE;
  }

  const each = [
    { path: notificationsPath(dataDir), spans: soFar.get(NOTIFICATIONS_FILE) ?? [] },
    { path: serverNoticesPath(dataDir), spans: soFar.get(SERVER_NOTICES_FILE) ?? [] },
  ];

  let unread = 0;
  let more = false;
  // ONE budget across both ledgers, not one each. Two caps of 3000 could walk 6000 newlines and
  // report "6000 new" — a number above the cap the sentence documents, earned by twice the work the
  // cap exists to bound. What is left after the first ledger is what the second may spend.
  // (codex, the S5 code round.)
  let left = COUNT_CAP;
  for (const ledger of each) {
    if (left <= 0) {
      break;
    }
    const counted = await countSince(ledger.path, tailBegins(ledger.spans), left);
    if (!counted.readable) {
      // `stat` succeeding says nothing about a read: on a share the failure arrives at the first
      // window, and a count that swallowed it would render as "Nothing new".
      return UNREADABLE_GLANCE;
    }
    unread += counted.count;
    more = more || counted.more;
    left -= counted.count;
  }

  return {
    readable: true,
    anyRecords: mine > 0 || theirs > 0,
    unread,
    more,
    older: each.some((ledger) => olderRemain(ledger.spans)),
  };
}
