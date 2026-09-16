import { open, stat } from 'node:fs/promises';
import { join } from 'node:path';
import { appendLine, readLedger } from './jsonlLedger';
import { NotificationRecord, notificationLine, parseNotifications } from './notifications';

/**
 * The notifications ledgers' world-facing half: two files, appended to and read back.
 *
 * <h2>Two files, because two programs ship on different days</h2>
 *
 * <p>The extension writes `notifications.jsonl`; `coai-mcp` writes `server-notices.jsonl` beside it;
 * the page merges them in memory. The first draft of the plan had one file both halves append to and
 * called that "the established shape here" — it is the opposite. `research/architecture.md` records
 * that chat rows inside the server's `usage.jsonl` were REJECTED on this gate's plan round, *"its two
 * writers release on different days… a format shared by two programs that ship separately is a
 * contract nobody wrote down"*, and `research/module_extension.md` records the same refusal a second
 * time for `chat-doors.jsonl` — *"the gate refused that twice, independently."* The established shape
 * is two files and one merge, and it is what lets either half move alone. On this machine the halves
 * demonstrably do.</p>
 *
 * <h2>What it costs to keep, forever</h2>
 *
 * <p>A notification record is about <b>400 bytes</b> — larger than a door (110 B) or a chat turn
 * (270 B) because three of its fields are prose. At the hand-estimated 20–50 events a day this
 * machine sees, that is <b>3–7 MB a year</b>; at a pessimistic 200 a day, 29 MB. Kept for ever, and
 * that is a decision taken for THIS file rather than inherited: the 2026-09-10 ruling about
 * `chat-usage.jsonl` is the precedent for the shape — keep rather than trim, and its docstring
 * refuses trimming by count by name — but a ruling about spending history does not settle retention
 * for notifications. If a bound is ever wanted it is a roll-up, and the record already carries what
 * one needs. Nothing here rewrites, truncates or compacts: append-only is what keeps the measured
 * `O_APPEND` guarantee applicable and what makes reading by byte offset exact.</p>
 *
 * <p>The rate is NOT measured, which is why `scripts/count-notifications.mjs` exists and why the
 * projection above says "hand-estimated" rather than pretending otherwise.</p>
 */

/** The extension's own ledger. */
export const NOTIFICATIONS_FILE = 'notifications.jsonl';

/** The server's, beside it — written by `coai-mcp`, read-only to this side. */
export const SERVER_NOTICES_FILE = 'server-notices.jsonl';

export function notificationsPath(dataDir: string): string {
  return join(dataDir, NOTIFICATIONS_FILE);
}

export function serverNoticesPath(dataDir: string): string {
  return join(dataDir, SERVER_NOTICES_FILE);
}

/**
 * Write one notification down, on the notifications chain.
 *
 * <p>Its own chain rather than the shared one: a repeating fault queues hundreds of appends, and on
 * a NAS each is tens of milliseconds — behind which a chat turn would wait, and `deactivate` would
 * wait for all of them.</p>
 *
 * <p>Awaited by the funnel BEFORE the toast is shown, for every class. "Records, then shows" is not
 * true of a promise nobody waits for: a host that dies in between showed a message the ledger never
 * had, which is this feature's own definition of the defect.</p>
 */
export function recordNotification(
  dataDir: string,
  record: NotificationRecord,
  onFailure?: (reason: unknown) => void,
): Promise<void> {
  return appendLine(
    notificationsPath(dataDir),
    notificationLine(record),
    'a notification',
    { chain: 'notifications', ...(onFailure === undefined ? {} : { onFailure }) },
  );
}

/** Everything this side ever recorded, or nothing at all. */
export function readNotifications(dataDir: string): Promise<readonly NotificationRecord[]> {
  return readLedger(notificationsPath(dataDir), parseNotifications, 'notifications');
}

/** Everything the SERVER recorded on this side, or nothing at all. */
export function readServerNotices(dataDir: string): Promise<readonly NotificationRecord[]> {
  return readLedger(serverNoticesPath(dataDir), parseNotifications, 'server notices');
}

/**
 * How much of the end of a file one read takes. Bigger than any plausible record, small enough that
 * a page wanting a few hundred records touches a few hundred kilobytes rather than a history.
 */
const WINDOW = 64 * 1024;

/**
 * How many WHOLE records the accumulated windows hold.
 *
 * <p>The first entry is a fragment until the read reaches the start of the file, and the last is
 * the empty string after the final newline. Counting entries instead of these stops one record
 * short — which the backwards-read test caught on the first version of this function, asking for
 * five and getting four.</p>
 */
function complete(lines: readonly string[]): number {
  return lines.slice(1).filter((line) => line.trim().length > 0).length;
}

/**
 * The newest `limit` records of one ledger, without reading the rest of it.
 *
 * <p>"The page loads the newest N, not the file" is not a bound if finding them means parsing
 * everything first — which is what the plan's second draft would have done, and what the rounds log
 * already learned the expensive way when it shipped a 3.78 MB payload for rounds nobody had opened.
 * So this seeks to the END and walks backwards a window at a time, keeping complete lines, until it
 * has enough or reaches the start.</p>
 *
 * <p><b>Append-only is what makes it exact.</b> A file that is rewritten or compacted invalidates
 * every offset; one that is only ever appended to can be read from either end, which is the third
 * thing the no-rewrite rule buys (after the measured atomicity and the byte-offset watermark).</p>
 *
 * <p>A fragment at the front of the buffer is dropped while there is more file behind it — it is
 * half a line by construction, not a torn one — and kept once offset 0 is reached, where it is the
 * genuine first line.</p>
 */
export async function readNewest(
  path: string,
  limit: number,
  windowBytes: number = WINDOW,
): Promise<readonly NotificationRecord[]> {
  if (limit <= 0) {
    return [];
  }

  let handle;
  try {
    handle = await open(path, 'r');
  } catch (reason: unknown) {
    if ((reason as { code?: unknown } | null)?.code !== 'ENOENT') {
      console.error('ConnectOtherAIs: the notifications ledger exists but could not be opened', reason);
    }

    return [];
  }

  try {
    const size = (await stat(path)).size;
    let offset = size;
    // BYTES, not text. Decoding each window on its own would corrupt any character a window
    // boundary happens to split — and this file carries prose, so it carries multi-byte characters.
    // Decoding the whole accumulation at once puts the only possible broken character at the FRONT,
    // which is the fragment dropped below while there is still file behind it.
    let held = Buffer.alloc(0);
    let lines: string[] = [];

    // Stop on COMPLETE lines, not on entries. `split` counts the leading fragment and the trailing
    // empty string left by the final newline, so counting entries stops a whole record early —
    // which is exactly what the first version of this did, and what its test caught.
    while (offset > 0 && complete(lines) < limit) {
      const take = Math.min(windowBytes, offset);
      offset -= take;
      const buffer = Buffer.alloc(take);
      await handle.read(buffer, 0, take, offset);
      held = Buffer.concat([buffer, held]);
      lines = held.toString('utf8').split('\n');
    }

    // Everything but the first entry is a whole line; the first is half of one unless we reached
    // the start of the file, where it is the genuine first line. Empties go BEFORE the slice, not
    // after: the final newline leaves one, and `slice(-limit)` counting it as a record is the same
    // off-by-one a second time, one step further along.
    const whole = (offset > 0 ? lines.slice(1) : lines).filter((line) => line.trim().length > 0);

    return parseNotifications(whole.slice(-limit).join('\n'));
  } finally {
    await handle.close();
  }
}
