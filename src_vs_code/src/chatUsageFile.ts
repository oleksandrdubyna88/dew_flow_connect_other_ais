import { appendFile, mkdir, readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { ChatTurnRecord, chatUsageLine, parseChatUsage } from './chatUsage';

/**
 * The chat ledger's world-facing half: one file, appended to and read back.
 *
 * <p>Every rule about what a record MEANS lives in `chatUsage.ts` and is tested there. This file
 * does the thing that needs a disk, and holds no judgement of its own — the same split
 * `chatOrphans.ts` has against `chatLedger.ts`, and for the same reason.</p>
 *
 * <h2>Why one shared file, when the orphan ledger needed one per host</h2>
 *
 * <p>`chatOrphans.ts` puts the owner's pid in its file NAME because a shared file meant one window
 * reading another window's live children and killing them. That reasoning does not reach here — a
 * usage record is inert, and every window's records are wanted by every window's log page. What DOES
 * reach here is the writing: the coai data directory is shared by every VS Code window, so a single
 * ledger has as many writers as the person has windows open, and both reviewers on the plan round
 * said independently that concurrent appenders tear each other's lines.</p>
 *
 * <p><b>Measured, and they are wrong about this particular write.</b> `npm run measure:append` forks
 * real processes that append records of deliberately awkward sizes to one file and then checks every
 * line back. Eight processes × 1000 records — 116 MB, including 60 KB lines, far past any buffer a
 * tear would happen at — produced <b>8000 whole records of 8000 and zero torn lines</b> (2026-09-09,
 * Windows 11, NTFS, node 22). Four × 500 was clean first. That is what `O_APPEND` /
 * `FILE_APPEND_DATA` is documented to guarantee and it is what this machine does; `appendFile` opens
 * with `'a'`, which is that mode.</p>
 *
 * <p>The claim the reviewers were right about is the one that made this a separate file at all: the
 * SERVER's `usage.jsonl` is written by a program this side does not control and does not ship in
 * step with, so the extension keeps its own ledger and the log page merges the two in memory.</p>
 *
 * <p><b>Nothing here throws into a turn.</b> A ledger that cannot be written must cost the record and
 * nothing else — a person's answer does not fail because a disk was full — so every path here says
 * what went wrong on the console and returns.</p>
 */

/** What the chat ledger is called, beside the server's own `usage.jsonl`. */
export const CHAT_USAGE_FILE = 'chat-usage.jsonl';

/** Where this installation's chat ledger lives, given the coai data directory. */
export function chatUsagePath(dataDir: string): string {
  return join(dataDir, CHAT_USAGE_FILE);
}

/**
 * Appends, in order, one turn at a time.
 *
 * <p>Two `await`ed appends from one process can be issued in either order — a promise chain is the
 * cheapest thing that makes the file's order the conversation's order. It is not a lock and it does
 * not need to be: the measurement above is what makes several PROCESSES safe, and this is only about
 * this one.</p>
 */
let queue: Promise<void> = Promise.resolve();

/**
 * Write one turn down. Never rejects, and never delays the caller.
 *
 * <p>Returns the promise so a test can wait for the write it just asked for; the turn path ignores
 * it, because a person's answer must not wait on a disk.</p>
 */
export function recordChatTurn(dataDir: string, record: ChatTurnRecord): Promise<void> {
  const path = chatUsagePath(dataDir);
  queue = queue.then(async () => {
    try {
      // The directory may genuinely not exist: on a machine where nobody has run a review round, the
      // extension's chat is the FIRST thing to write anything under the coai data directory.
      await mkdir(dirname(path), { recursive: true });
      await appendFile(path, chatUsageLine(record), 'utf8');
    } catch (reason: unknown) {
      // Said out loud rather than swallowed, per `coding-style.md`. What is lost is one line of
      // accounting; what would be lost by throwing is the answer the person is reading.
      console.error('ConnectOtherAIs: a chat turn could not be written to the usage ledger', reason);
    }
  });

  return queue;
}

/**
 * Every chat turn ever recorded, or nothing at all.
 *
 * <p>A missing file is the ordinary case — it means nobody has held a conversation yet — and is not
 * an error. A file being appended to WHILE it is read can end in half a line, which
 * {@link parseChatUsage} drops; that costs the newest turn a place on the page until the next tick,
 * which is the same bargain `usage.ts` already makes for the server's ledger.</p>
 */
export async function readChatUsage(dataDir: string): Promise<readonly ChatTurnRecord[]> {
  try {
    return parseChatUsage(await readFile(chatUsagePath(dataDir), 'utf8'));
  } catch {
    return [];
  }
}
