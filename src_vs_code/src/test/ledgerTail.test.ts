import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

import { readTailSince } from '../ledgerTail';
import { roundsLoader } from '../roundCountsRead';
import { parseUsageLine } from '../usage';

/**
 * The ledger read for the Rounds block stops at the oldest window (story 2.2 of todo/PLAN_every_round_is_counted.md):
 * a year of history before the 7 d window costs one chunk, not a year's parse per refresh.
 */

const DAY = 24 * 3_600_000;
const NOW = Date.parse('2026-10-10T12:00:00Z');

function lineAt(ms: number, n: number): string {
  return JSON.stringify({ utc: new Date(ms).toISOString(), provider: 'codex', model: 'gpt-6-luna', tokensIn: 1000 + n, tokensOut: 10, outcome: 'ok' });
}

function ledgerFile(lines: readonly string[]): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-tail-'));
  const file = path.join(dir, 'usage.jsonl');
  fs.writeFileSync(file, lines.map((one) => `${one}\n`).join(''));

  return file;
}

test('TheLedgerRead_StopsAtTheOldestWindow — a year before the window costs at most one chunk beyond the window', async () => {
  // A year of history, one line every 15 minutes (~35 000 lines, ~4 MB), then 7 days of 200 lines.
  const old = Array.from({ length: 365 * 96 }, (_, i) => lineAt(NOW - 372 * DAY + i * 15 * 60_000, i));
  const recent = Array.from({ length: 200 }, (_, i) => lineAt(NOW - 7 * DAY + (i + 1) * 3_000_000, i));
  const file = ledgerFile([...old, ...recent]);
  const windowBytes = Buffer.byteLength(recent.map((one) => `${one}\n`).join(''));
  const chunk = 16 * 1024;

  const tail = await readTailSince(file, NOW - 7 * DAY, parseUsageLine, chunk);

  assert.equal(tail.records.length, 200, 'the window\'s lines were not all found, or older ones came with them');
  assert.ok(tail.bytesRead <= windowBytes + chunk,
    `read ${tail.bytesRead} bytes of a ${fs.statSync(file).size}-byte ledger for a ${windowBytes}-byte window — the read did not stop`);
  assert.equal(tail.records[0]?.tokensIn, 1000, 'oldest first');
  fs.rmSync(path.dirname(file), { recursive: true, force: true });
});

test('a line torn across a chunk boundary is joined, not lost or misread', async () => {
  const lines = Array.from({ length: 50 }, (_, i) => lineAt(NOW - 60_000 * (50 - i), i));
  const file = ledgerFile(lines);

  const tail = await readTailSince(file, 0, parseUsageLine, 37); // a chunk smaller than one line: every line is torn

  assert.deepEqual(tail.records.map((one) => one.tokensIn), lines.map((_, i) => 1000 + i));
  fs.rmSync(path.dirname(file), { recursive: true, force: true });
});

test('AnUnreadableLedger_IsAFailureThatSaysWhy_NeverNoLines — a ledger that exists and will not read is not "nothing spent"', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-tail-'));

  const tail = await readTailSince(dir, 0, parseUsageLine); // a directory where the file should be: it exists, it will not read

  assert.deepEqual(tail.records, []);
  assert.ok(tail.failure.length > 0, 'a ledger that would not read came back as an empty one — the money would read as nothing spent');
  fs.rmSync(dir, { recursive: true, force: true });
});

test('the loader hands the ledger\'s failure to the snapshot beside the counts', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-tail-'));
  const load = roundsLoader(() => 'coai-mcp', dir, async () => ({ code: 64, output: '' }));

  const loaded = await load(NOW);

  assert.ok(loaded.ledgerFailure.length > 0, 'the snapshot lost the reason the ledger did not read');
  assert.deepEqual(loaded.ledger, []);
  fs.rmSync(dir, { recursive: true, force: true });
});

test('a ledger that does not exist is no lines; a torn last line costs itself alone', async () => {
  const missing = await readTailSince(path.join(os.tmpdir(), 'no-such-dir-coai', 'usage.jsonl'), 0, parseUsageLine);
  assert.deepEqual(missing.records, []);
  assert.equal(missing.failure, '', 'a ledger never written is nothing run yet, not a failure');
  const file = ledgerFile([lineAt(NOW - 1000, 1)]);
  fs.appendFileSync(file, '{"utc":"2026-10-10T11:59:59Z","provi');

  assert.equal((await readTailSince(file, 0, parseUsageLine)).records.length, 1);
  fs.rmSync(path.dirname(file), { recursive: true, force: true });
});
