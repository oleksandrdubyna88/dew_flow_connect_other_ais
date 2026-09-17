import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { flushLedgers } from '../jsonlLedger';
import { countSince } from '../notificationsFile';
import {
  SEEN_FILE,
  Span,
  acknowledge,
  alreadyRead,
  merge,
  olderRemain,
  parseSeen,
  parseSeenLine,
  readSeen,
  readSoFar,
  seenPath,
  tailBegins,
} from '../notificationsSeen';

/**
 * What the person has already been shown, and what it costs to ask.
 *
 * <p>Two promises are under test and they pull in opposite directions. The watermark must never
 * claim a record was seen when it was not — which is why it stores INTERVALS and not a maximum —
 * and the panel must be able to ask "how many are new" every five seconds, in every window,
 * against a directory that may be a NAS. A design that got either alone would be easy.</p>
 */

const NEWLINE = String.fromCharCode(10);

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-seen-'));
}

const span = (from: number, to: number): Span => ({ from, to });

test('overlapping and touching ranges become the fewest that cover the same bytes', () => {
  assert.deepEqual(merge([span(0, 100), span(100, 200)]), [span(0, 200)], 'touching is one range');
  assert.deepEqual(merge([span(0, 150), span(100, 200)]), [span(0, 200)], 'overlapping is one range');
  assert.deepEqual(merge([span(500, 600), span(0, 100)]), [span(0, 100), span(500, 600)], 'sorted, and a real gap survives');
  assert.deepEqual(merge([span(0, 900), span(100, 200)]), [span(0, 900)], 'contained is swallowed');
  assert.deepEqual(merge([]), []);
});

test('the tail begins at the highest acknowledged byte, and gaps below it stay gaps', () => {
  // The two readings of one file the plan calls for: the COUNT uses only this number, so it is
  // cheap; the PAGE uses the intervals, so it is exact. Neither is the other's answer.
  const spans = merge([span(0, 100), span(500, 600)]);

  assert.equal(tailBegins(spans), 600, 'everything after 600 is unread for certain');
  assert.equal(olderRemain(spans), true, 'and 100..500 is unread too, but older');
  assert.equal(olderRemain(merge([span(0, 600)])), false, 'a single run from zero leaves nothing behind');
  assert.equal(olderRemain(merge([span(40, 600)])), true, 'a run that does not start at zero does');
  assert.equal(tailBegins([]), 0, 'nothing acknowledged is a tail from the start of the file');
});

test('a row is read when its own line start falls inside an acknowledged range', () => {
  const spans = merge([span(100, 200)]);

  assert.equal(alreadyRead(100, spans), true, 'the first byte of the range');
  assert.equal(alreadyRead(199, spans), true);
  assert.equal(alreadyRead(200, spans), false, 'the range is half-open: 200 begins the next record');
  assert.equal(alreadyRead(99, spans), false);
  assert.equal(alreadyRead(150, []), false, 'nothing acknowledged, nothing read');
});

test('the two ledgers keep their own ranges, or the quieter one is swallowed', () => {
  // `notifications.jsonl` is much the busier file. One set of ranges shared between them would
  // apply its 5000-byte offsets to a `server-notices.jsonl` that is 300 bytes long, permanently
  // marking every server notice read. The ledger's NAME is in the key for that reason.
  const soFar = readSoFar([
    { utc: 'u', ledger: 'notifications.jsonl', from: 0, to: 5000 },
    { utc: 'u', ledger: 'server-notices.jsonl', from: 0, to: 120 },
  ]);

  assert.deepEqual([...(soFar.get('notifications.jsonl') ?? [])], [span(0, 5000)]);
  assert.deepEqual([...(soFar.get('server-notices.jsonl') ?? [])], [span(0, 120)]);
  assert.equal(alreadyRead(300, soFar.get('server-notices.jsonl') ?? []), false, 'and 300 is past its end');
});

test('a line that is torn, foreign or backwards costs itself and nothing else', () => {
  // Errs toward UNREAD, which is the safe direction: a dropped acknowledgement shows something
  // twice, a wrongly kept one hides it for ever.
  assert.equal(parseSeenLine('{"ledger":"n.jsonl","from":0,"to"'), undefined, 'torn');
  assert.equal(parseSeenLine('not json'), undefined);
  assert.equal(parseSeenLine('null'), undefined);
  assert.equal(parseSeenLine('{"from":0,"to":10}'), undefined, 'no ledger named');
  assert.equal(parseSeenLine('{"ledger":"n","from":10,"to":10}'), undefined, 'an empty range says nothing');
  assert.equal(parseSeenLine('{"ledger":"n","from":10,"to":5}'), undefined, 'backwards');
  assert.equal(parseSeenLine('{"ledger":"n","from":-5,"to":50}'), undefined, 'a negative offset is not an offset');

  const good = '{"utc":"2026-09-17T09:00:00.000Z","ledger":"n.jsonl","from":0,"to":50}';
  assert.equal(parseSeen(`${good}${NEWLINE}broken${NEWLINE}${good}`).length, 2, 'the torn one between them is dropped');
});

test('two windows acknowledging at once cannot move the watermark BACKWARDS', async () => {
  // The defect that decided the whole storage shape. With temp+rename and a component-wise
  // maximum, two windows read the old offsets, each computes its own max, and they rename in
  // reverse order: the later rename wins with the SMALLER value and records somebody has read come
  // back as unread. An append has no read-modify-write in it, so both lines survive and the union
  // is the union whichever order they land in.
  const dir = home();
  try {
    const ledger = 'notifications.jsonl';
    await Promise.all([
      acknowledge(dir, { utc: 'a', ledger, from: 0, to: 200 }),
      acknowledge(dir, { utc: 'b', ledger, from: 0, to: 5000 }),
    ]);
    await flushLedgers();

    const spans = readSoFar(await readSeen(dir)).get(ledger) ?? [];

    assert.equal(tailBegins(spans), 5000, 'the larger stands, whichever landed last');
    assert.equal(readFileSync(seenPath(dir), 'utf8').trim().split(NEWLINE).length, 2, 'both lines are there');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a missing acknowledgement file is a first run, and everything is unread', async () => {
  const dir = home();
  try {
    assert.deepEqual(await readSeen(dir), []);
    assert.equal(tailBegins([]), 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the count asks how many are new without parsing one of them', async () => {
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    // Deliberately not valid JSON: the counter must not care, because it counts newline bytes.
    // If this test ever needs real records, the implementation has started parsing.
    const rows = Array.from({ length: 12 }, (_, n) => `line ${n} —`);
    writeFileSync(path, rows.join(NEWLINE) + NEWLINE);
    const upTo = rows.slice(0, 9).reduce((total, row) => total + Buffer.byteLength(row, 'utf8') + 1, 0);

    assert.deepEqual(await countSince(path, upTo, 100), { count: 3, more: false }, 'three since the watermark');
    assert.deepEqual(await countSince(path, 0, 100), { count: 12, more: false }, 'all of them from the start');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a person who is up to date is counted without reading anything', async () => {
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    const text = `one${NEWLINE}two${NEWLINE}`;
    writeFileSync(path, text);

    assert.deepEqual(
      await countSince(path, Buffer.byteLength(text, 'utf8'), 100),
      { count: 0, more: false },
      'the watermark is the end of the file, so there is nothing after it',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('far behind, the count stops at the cap and says there are more', async () => {
  // "3000+" rather than a number it would cost a megabyte to earn - the same honesty as the
  // storm rows, which say "1000+" rather than a figure the ceiling makes untrue.
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    writeFileSync(path, Array.from({ length: 500 }, (_, n) => `row ${n}`).join(NEWLINE) + NEWLINE);

    assert.deepEqual(await countSince(path, 0, 50), { count: 50, more: true });
    assert.deepEqual(await countSince(path, 0, 500), { count: 500, more: true }, 'the cap reached exactly still says more');
    assert.deepEqual(await countSince(path, 0, 501), { count: 500, more: false }, 'one above it does not');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a half-written last line is not a record yet, and is not counted', async () => {
  const dir = home();
  try {
    const path = join(dir, 'n.jsonl');
    writeFileSync(path, `one${NEWLINE}two${NEWLINE}{"utc":"2026-09`);

    assert.deepEqual(await countSince(path, 0, 100), { count: 2, more: false });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a ledger that is not there counts zero rather than throwing', async () => {
  const dir = home();
  try {
    assert.deepEqual(await countSince(join(dir, 'absent.jsonl'), 0, 100), { count: 0, more: false });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the acknowledgement file is named where the data-directory move can find it', () => {
  assert.equal(SEEN_FILE, 'notifications-seen.jsonl');
  assert.ok(seenPath('/data').endsWith(SEEN_FILE));
});
