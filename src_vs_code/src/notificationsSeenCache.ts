import { FileHandle, open, stat } from 'node:fs/promises';
import { NEWLINE_BYTE, missingRatherThanBroken } from './notificationsFile';
import { Span, merge, parseSeen, readSoFar, seenPath } from './notificationsSeen';

/**
 * The acknowledgement ledger, read ONCE and then only where it grew.
 *
 * <h2>Why this exists at all</h2>
 *
 * <p>The panel's count runs in every window every five seconds, whatever happened, and it needs the
 * watermark to know what "new" means. Reading the whole of `notifications-seen.jsonl` for that was
 * the one part of the cheap path that was not cheap: the file gains a line every time somebody
 * opens the page, so on a machine that has been in use for a year it is hundreds of kilobytes, and
 * re-reading and re-parsing all of it twelve times a minute in every window — from a directory that
 * may be a NAS — is the exact cost `countSince` was written to avoid. (gemini, the S5 code round.)</p>
 *
 * <p>The file is <b>append-only</b>, which is what makes this safe: bytes below `readTo` cannot
 * change, so the merged ranges computed from them cannot change either. Merging is associative, so
 * yesterday's merged spans plus today's appended lines are the same answer as parsing the lot.</p>
 *
 * <h2>The three ways it refuses to trust itself</h2>
 *
 * <p>A file SHORTER than what was already consumed is not this file any more — it was rotated,
 * restored, or replaced — so the cache is thrown away and it is read from the start.</p>
 *
 * <p>A file that is NOT shorter is still checked rather than believed: the last bytes consumed are
 * re-read and compared, because a file replaced in place keeps its path, its inode and its birth
 * time, and keeps its length too if the replacement happens to be the same size. That check is what
 * turns "assumes append-only" into "verifies append-only", and it costs one seek — on a tick that
 * already opens both ledgers to count them.</p>
 *
 * <p>And only complete lines are consumed: `readTo` always lands just past a newline, so a line that
 * was half written when this read happened is read whole on the next tick rather than parsed in two
 * halves.</p>
 */

/** What is remembered about one acknowledgement file between ticks. */
interface Remembered {
  /** How many bytes have been consumed. Always just past a newline. */
  readonly readTo: number;
  /** The merged ranges those bytes amount to, per ledger. */
  readonly spans: ReadonlyMap<string, readonly Span[]>;
  /** The last bytes consumed, re-read whenever the file changed to prove it is still the same file. */
  readonly seam: Buffer;
}

/**
 * How many bytes before the watermark are checked before the cache is believed.
 *
 * <p>Nothing about a RESTORED file says it changed. Copying a backup over
 * `notifications-seen.jsonl` truncates it in place, so the path, the inode and the birth time are
 * all the ones that were cached, and if the backup is a similar age the length is too — the size
 * check catches a file that got shorter and nothing catches one that did not. So the cache re-reads
 * the last 128 bytes it consumed and compares them: bounded, one seek, and it turns "assumes
 * append-only" into "verifies append-only". (local, the second S5 code round.)</p>
 */
export const SEAM = 128;

/**
 * How much of the tail one call will take.
 *
 * <p>A bound, not a budget: without it a file that grew by fifty megabytes between ticks would be
 * one fifty-megabyte allocation on the five-second path. Whatever is left is taken on the next
 * tick, so the only effect of the bound is that a ledger which grew enormously is caught up over a
 * few ticks — and until it is, fewer records are known to be read, which shows MORE unread rather
 * than fewer. That is the safe direction.</p>
 */
export const TAIL_CAP = 1024 * 1024;

const remembered = new Map<string, Remembered>();

/** Forget everything. For tests, and for the moment the data directory moves. */
export function forgetSeen(): void {
  remembered.clear();
}

const NOTHING: ReadonlyMap<string, readonly Span[]> = new Map();
const EMPTY = Buffer.alloc(0);

/** The last `SEAM` bytes before `readTo`, which is what proves the prefix is still the prefix. */
async function seamAt(handle: FileHandle, readTo: number): Promise<Buffer> {
  const want = Math.min(SEAM, readTo);
  if (want <= 0) {
    return EMPTY;
  }
  const buffer = Buffer.alloc(want);
  await handle.read(buffer, 0, want, readTo - want);

  return buffer;
}

/** Yesterday's ranges and today's, per ledger, merged. */
function joined(
  held: ReadonlyMap<string, readonly Span[]>,
  added: ReadonlyMap<string, readonly Span[]>,
): ReadonlyMap<string, readonly Span[]> {
  const all = new Map<string, readonly Span[]>(held);
  for (const [ledger, spans] of added) {
    all.set(ledger, merge([...(all.get(ledger) ?? []), ...spans]));
  }

  return all;
}

/** What has been acknowledged of each ledger, or nothing at all when the file cannot be read. */
export async function readSoFarCheaply(
  dataDir: string,
): Promise<ReadonlyMap<string, readonly Span[]> | undefined> {
  const path = seenPath(dataDir);
  let size: number;
  try {
    size = (await stat(path)).size;
  } catch (reason: unknown) {
    if (missingRatherThanBroken(reason)) {
      // A first run. Nothing acknowledged, and no cache to keep — the file may be created between
      // this tick and the next.
      remembered.delete(path);

      return NOTHING;
    }
    console.error('ConnectOtherAIs: the acknowledgements exist but could not be measured', reason);

    return undefined;
  }

  const held = remembered.get(path);
  let handle;
  try {
    handle = await open(path, 'r');
  } catch (reason: unknown) {
    console.error('ConnectOtherAIs: the acknowledgements could not be opened', reason);

    return undefined;
  }
  try {
    // Something changed, so the cache is checked rather than trusted: are the bytes we consumed
    // still the bytes that are there? A file replaced in place keeps its path, its inode and its
    // birth time, so this is the only question that distinguishes it from one that was appended to.
    const sameFile = held !== undefined
      && size >= held.readTo
      && (await seamAt(handle, held.readTo)).equals(held.seam);
    const base: Remembered = sameFile && held !== undefined
      ? held
      : { readTo: 0, spans: NOTHING, seam: EMPTY };
    if (size === base.readTo) {
      // Nothing new, and this is the common case by far: twelve ticks a minute against a file
      // somebody appends to a few times a day. Nothing is parsed and nothing is allocated.
      remembered.set(path, base);

      return base.spans;
    }

    const length = Math.min(size - base.readTo, TAIL_CAP);
    const buffer = length > 0 ? Buffer.alloc(length) : EMPTY;
    if (length > 0) {
      await handle.read(buffer, 0, length, base.readTo);
    }
    const lastNewline = buffer.lastIndexOf(NEWLINE_BYTE);
    if (lastNewline < 0) {
      // Nothing complete beyond the watermark. The bytes stay unconsumed and are read whole next
      // time; the stamps are recorded so a half-written line is not re-read on every tick.
      remembered.set(path, base);

      return base.spans;
    }
    const complete = buffer.subarray(0, lastNewline + 1);
    const spans = joined(base.spans, readSoFar(parseSeen(complete.toString('utf8'))));
    const readTo = base.readTo + complete.length;
    remembered.set(path, { readTo, spans, seam: await seamAt(handle, readTo) });

    return spans;
  } catch (reason: unknown) {
    console.error('ConnectOtherAIs: the acknowledgements could not be read', reason);

    return undefined;
  } finally {
    await handle.close();
  }
}
