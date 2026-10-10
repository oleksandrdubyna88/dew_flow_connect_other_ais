import { open } from 'node:fs/promises';

import { missingRatherThanBroken } from './notificationsFile';

/**
 * The END of an append-only ledger, read back to an instant and no further (story 2.2 of
 * todo/PLAN_every_round_is_counted.md).
 *
 * <p>The Rounds block needs the last seven days of `usage.jsonl` and nothing before them, and refreshes at most once a
 * minute. Parsing a year of history for a week's tokens on every refresh is what the code round refused, so the file
 * is read from its end in chunks, and the read stops at the first chunk whose oldest whole line is older than the
 * instant asked for. Lines are appended in time order; two processes appending at once can interleave by seconds, which
 * a seven-day window does not notice.</p>
 *
 * <p>`node:fs`, no `vscode`: the data directory is a path, and a test hands in a temp file. A file that does not exist
 * is no lines — nothing has run yet. Any other failure is no lines AND a `failure` that says why, so the block draws its
 * tokens and money as unavailable rather than as nothing spent; the counts beside it come from another source and
 * still show.</p>
 */

/** How much is read per step back. A week of this machine's ledger is ~0.5 MB; 64 KiB keeps the overshoot small. */
export const TAIL_CHUNK_BYTES = 64 * 1024;

/** What a tail read found, and how many bytes it cost — the second is what the bound is tested by. */
export interface Tail<T> {
  readonly records: readonly T[];
  readonly bytesRead: number;
  /** Why a ledger that exists did not read — empty when it read, or when there is no ledger yet. */
  readonly failure: string;
}

/**
 * Every record whose `utc` is at or after `sinceMs`, oldest first, reading the file from its end.
 *
 * @param parse one line to a record, or nothing for a torn or foreign line — the ledger's own line parser.
 */
export async function readTailSince<T extends { readonly utc: string }>(
  path: string,
  sinceMs: number,
  parse: (line: string) => T | undefined,
  chunkBytes: number = TAIL_CHUNK_BYTES,
): Promise<Tail<T>> {
  try {
    const handle = await open(path, 'r');
    try {
      const info = await handle.stat();
      // On Windows a directory in the ledger's place OPENS cleanly with size 0, so nothing would ever fail to read.
      return info.isFile() ? await tailOf(handle, info.size, sinceMs, parse, chunkBytes) : notAFile(path);
    } finally {
      await handle.close();
    }
  } catch (reason: unknown) {
    return { records: [], bytesRead: 0, failure: failureOf(reason) };
  }
}

function notAFile<T>(path: string): Tail<T> {
  return { records: [], bytesRead: 0, failure: `${path} is not a file` };
}

/** No ledger yet is nothing run, not a failure; anything else that stopped the read is said, never read as "nothing spent". */
function failureOf(reason: unknown): string {
  return missingRatherThanBroken(reason) ? '' : String(reason);
}

interface Reader {
  read(buffer: Buffer, offset: number, length: number, position: number): Promise<{ bytesRead: number }>;
}

async function tailOf<T extends { readonly utc: string }>(
  handle: Reader, size: number, sinceMs: number, parse: (line: string) => T | undefined, chunkBytes: number,
): Promise<Tail<T>> {
  let end = size;
  let carry: Buffer = Buffer.alloc(0);
  let found: T[] = [];
  let bytesRead = 0;
  while (end > 0) {
    const start = Math.max(0, end - chunkBytes);
    const chunk = Buffer.alloc(end - start);
    bytesRead += (await handle.read(chunk, 0, chunk.length, start)).bytesRead;
    const step = splitStep(Buffer.concat([chunk, carry]), start === 0);
    carry = step.carry;
    const records = wholeLines(step.text).flatMap((line) => parsedOrNone(line, parse));
    found = [...records.filter((one) => at(one) >= sinceMs), ...found];
    if (records.some((one) => at(one) < sinceMs)) {
      break;
    }
    end = start;
  }

  return { records: found, bytesRead, failure: '' };
}

/**
 * A chunk's whole lines, and the part before its first line break — which belongs to the line that STARTED in the
 * chunk before it and is carried back to be joined to it. At the file's start nothing is carried.
 */
function splitStep(bytes: Buffer, atStart: boolean): { readonly text: string; readonly carry: Buffer } {
  if (atStart) {
    return { text: bytes.toString('utf8'), carry: Buffer.alloc(0) };
  }
  const firstBreak = bytes.indexOf(0x0a);

  return firstBreak < 0
    ? { text: '', carry: bytes }
    : { text: bytes.subarray(firstBreak + 1).toString('utf8'), carry: bytes.subarray(0, firstBreak + 1) };
}

function wholeLines(text: string): readonly string[] {
  return text.split('\n').map((line) => line.trim()).filter((line) => line.length > 0);
}

function parsedOrNone<T>(line: string, parse: (line: string) => T | undefined): readonly T[] {
  const record = parse(line);

  return record === undefined ? [] : [record];
}

/** A record's instant — NaN for one that cannot be read, which is neither inside the window nor a reason to stop. */
function at(record: { readonly utc: string }): number {
  return Date.parse(record.utc);
}
