import { join } from 'node:path';
import { ChatDoorRecord, chatDoorLine, parseChatDoors } from './chatDoors';
import { appendLine, readLedger } from './jsonlLedger';

/**
 * The door ledger's world-facing half: one file, appended to and read back.
 *
 * <p>Beside `chat-usage.jsonl` rather than inside it. The reason is measured against that file's own
 * parser and written down in `chatDoors.ts`: `parseChatUsageLine` requires a non-empty `utc` and
 * nothing else, so a door line living there is read as a turn that cost nothing by every caller that
 * already reads it — including an older build somebody rolls back to. A second file costs one reader
 * and one stamped cache; it cannot change what a file four call sites already read means.</p>
 *
 * <h2>What it costs to keep, forever</h2>
 *
 * <p>The family rule asks anything that GROWS to name its budget before the first write. A door
 * record is about <b>110 bytes</b> — four short fields, no UUID and no title. So 50 invocations a day
 * is <b>2 MB a year</b>, and 1000 a day, which nobody does, is 40 MB. It is kept for the same reason
 * the turn ledger is kept and by the same ruling: the file IS the history, and a count that quietly
 * forgets last month is worse than a file that is large.</p>
 *
 * <p>There is no flush of its own — the queue in `jsonlLedger.ts` is shared, so `flushChatUsage` in
 * `deactivate` already drains these writes too. One drain, not one per ledger, is the whole point of
 * sharing the chain.</p>
 */

/** What the door ledger is called, beside the chat ledger and the server's own `usage.jsonl`. */
export const CHAT_DOORS_FILE = 'chat-doors.jsonl';

/** Where this installation's door ledger lives, given the coai data directory. */
export function chatDoorsPath(dataDir: string): string {
  return join(dataDir, CHAT_DOORS_FILE);
}

/**
 * Write one invocation down. Never rejects, and never delays the caller.
 *
 * <p>Called BEFORE the door does anything that can refuse — no CLI, nothing captured, a dismissed
 * dialog — because those are invocations too, and "how often was this reached for" is the question
 * the count answers. (gemini, the plan round, as the blocking finding on the first draft, which
 * recorded this downstream where only the doors that succeeded would have arrived.)</p>
 */
export function recordChatDoor(dataDir: string, record: ChatDoorRecord): Promise<void> {
  return appendLine(chatDoorsPath(dataDir), chatDoorLine(record), 'a chat door');
}

/** Every invocation ever recorded, or nothing at all. */
export function readChatDoors(dataDir: string): Promise<readonly ChatDoorRecord[]> {
  return readLedger(chatDoorsPath(dataDir), parseChatDoors, 'chat doors');
}
