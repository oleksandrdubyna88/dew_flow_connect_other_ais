import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { WRITE_GAP } from '../notice';

/**
 * The two rules about WHERE the gap record is written from, pinned where they can be checked.
 *
 * <p>`notify.ts` imports `vscode`, so no test in this repository can import it — which is why the
 * arithmetic lives in `writeGap.ts` and is RUN in `writeGap.test.ts`, and why the two rules left
 * here are read out of the source instead. `testing.md` is clear about the price of that: a
 * structural assertion must pin the WHOLE condition, because one matching a fragment survives its
 * own break. So each test below asserts a specific pairing, and each names what it does not
 * prove.</p>
 *
 * <p><b>What these do not prove.</b> Not that a gap is ever written, and not that it appears on the
 * page. Only that the code is still wired the way the fix wired it. The record's own shape is
 * asserted by running it, in `notice.test.ts`.</p>
 */

const NOTIFY = join(__dirname, '..', '..', 'src', 'notify.ts');

function notifySource(): string {
  return readFileSync(NOTIFY, 'utf8').replace(/\r\n/gu, '\n');
}

/** One function's body, from its signature to the closing brace in the first column. */
function bodyOf(text: string, signature: string): string {
  const from = text.indexOf(signature);
  assert.notEqual(from, -1, `${signature} is no longer in this file — the test is reading nothing`);
  const to = text.indexOf('\n}', from);
  assert.notEqual(to, -1, 'no closing brace found, so the slice would run to the end of the file');

  return text.slice(from, to);
}

test('the gap is written only by an append that LANDED', () => {
  // A gap record written after a failed append would be a second write to a disk that has just
  // refused one — and its own failure would be counted as another lost notice, so the number it is
  // trying to report grows every time it tries to report it.
  const write = bodyOf(notifySource(), 'async function write(record: NotificationRecord)');

  assert.match(
    write,
    /if \(landed\) \{[^}]*await flushTheGap\(at\);/u,
    'the flush is not inside the branch that knows the disk answered',
  );
  assert.equal([...write.matchAll(/flushTheGap\(/gu)].length, 1, 'once, from one place');
});

test('the gap record does not go through the counter it is reporting', () => {
  // `write` counts a refusal as a lost NOTICE. A gap record sent through it would count its own
  // failure, inflating the hole it exists to describe; and on success it would recurse into the
  // flush it is already inside. So this one calls the ledger directly and leaves the counter to
  // `settled`, which subtracts exactly what reached the disk.
  const flush = bodyOf(notifySource(), 'async function flushTheGap(at: Date)');

  assert.match(flush, /await recordNotification\(/u, 'it appends for itself');
  assert.doesNotMatch(flush, /\bawait write\(/u, 'never through the funnel write');
  assert.doesNotMatch(flush, /\bnoteLoss\(/u, 'and never counts its own failure as a lost notice');
  assert.match(flush, /gap = settled\(gap, held\)/u, 'the counter moves only by what was written');
  assert.match(flush, /if \(gap\.lost === 0 \|\| flushing\)/u, 'and two landings cannot write it twice');
});

test('the code a gap is written under is one literal, shared with every reader', () => {
  assert.equal(WRITE_GAP, 'notifications-write-gap');
  assert.match(notifySource(), /gapRecord\(/u, 'and the funnel builds the record from it');
});
