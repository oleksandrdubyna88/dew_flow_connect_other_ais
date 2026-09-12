import { join } from 'node:path';
import { ChatTurnRecord, chatUsageLine, parseChatUsage } from './chatUsage';
import { appendLine, flushLedgers, readLedger } from './jsonlLedger';

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
 *
 * <h2>What it costs to keep, forever</h2>
 *
 * <p>The family rule asks anything that GROWS to name its budget before the first write, and a
 * reviewer asked for it by name. One record is <b>270 bytes</b> — measured, with a real model name, a
 * UUID and a plan filename in it, not estimated. So:</p>
 *
 * <ul>
 *   <li>50 turns a day — a busy person — is <b>4.7 MB a year</b>.</li>
 *   <li>200 a day is 18.8 MB a year.</li>
 *   <li>1000 a day, which nobody types, is 94 MB a year.</li>
 * </ul>
 *
 * <p><b>Nothing retires it, and the operator ruled that on 2026-09-10 — asked with these numbers in
 * front of them, the answer was "keep it for ever".</b> The file IS the history: `conversationTotal`
 * sums it, and deleting old lines would silently change what a past conversation is recorded to have
 * cost. A ledger that quietly forgets is worse than one that is large, and at the sizes above it is
 * not large. The two alternatives were put and refused: trimming by age or count bounds the file but
 * answers "what did I spend last year" WRONGLY rather than not at all, and rolling trimmed lines up
 * into a summary per conversation keeps the truth at the price of a second record type. If a bound is
 * ever wanted, the roll-up is the only version of it that does not lie.</p>
 *
 * <p>What DID need fixing is the cost of holding it: the log page used to re-read and re-parse the
 * whole file every five seconds, so `PanelProvider` now caches the parse and re-reads only when the
 * file's size or mtime moves.</p>
 *
 * <p>It is a plain file in the person's own data directory, named in the help; deleting it is theirs
 * to do, and costs them only the history.</p>
 */

/** What the chat ledger is called, beside the server's own `usage.jsonl`. */
export const CHAT_USAGE_FILE = 'chat-usage.jsonl';

/** Where this installation's chat ledger lives, given the coai data directory. */
export function chatUsagePath(dataDir: string): string {
  return join(dataDir, CHAT_USAGE_FILE);
}

/**
 * Write one turn down. Never rejects, and never delays the caller.
 *
 * <p>Returns the promise so a test can wait for the write it just asked for; the turn path ignores
 * it, because a person's answer must not wait on a disk. The ordering, the directory and the
 * swallowed failure all live in `jsonlLedger.ts` now, shared with the door ledger — the promise
 * chain that makes the file's order the conversation's order was the first thing a second ledger
 * wanted to copy.</p>
 */
export function recordChatTurn(dataDir: string, record: ChatTurnRecord): Promise<void> {
  return appendLine(chatUsagePath(dataDir), chatUsageLine(record), 'a chat turn');
}

/**
 * Wait for every queued write to reach the disk.
 *
 * <p>Called from `deactivate`. The turn path does not wait for a write — a person's answer must not
 * sit behind a disk — which means a host closed the instant a turn ends can take the record with it.
 * VS Code awaits what `deactivate` returns, so this is the one moment the queue can be drained
 * without making anybody wait for it. (codex and the local reviewer, the code round.)</p>
 */
export function flushChatUsage(): Promise<void> {
  return flushLedgers();
}

/**
 * Every chat turn ever recorded, or nothing at all.
 *
 * <p>A file being appended to WHILE it is read can end in half a line, which {@link parseChatUsage}
 * drops; that costs the newest turn a place on the page until the next tick, which is the same
 * bargain `usage.ts` already makes for the server's ledger.</p>
 *
 * <p><b>A missing file and an unreadable one are different facts, and only one of them is
 * ordinary.</b> No file means nobody has held a conversation yet. A permission error, a directory
 * where the file should be, a disk that stopped answering — those mean the history EXISTS and could
 * not be read, and returning an empty list for them shows a person "no conversations" when the truth
 * is "not readable". It still returns empty, because a log page must not fail to open over its own
 * second data source; what it no longer does is stay quiet about it. (codex, the code round.)</p>
 */
export function readChatUsage(dataDir: string): Promise<readonly ChatTurnRecord[]> {
  return readLedger(chatUsagePath(dataDir), parseChatUsage, 'chat usage');
}
