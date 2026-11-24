import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { Framed, Sentinels, framed } from '../remoteFraming';
import { sourceOf } from './sourceReading';

/**
 * The ONE frame finder (E1.S2, spec § A2.6): the session parser and the accounts parser both call `framed`, so a banner, a
 * banner that holds the begin line, or a second end line is read the same way by both. It answers with a value and throws
 * nothing, and it names no parser's fault: each parser says its own words for the two misses.
 */

const SESSION: Sentinels = { begin: 'COAI-BEGIN 1', family: 'COAI-BEGIN ', end: 'COAI-END' };
const ACCOUNTS: Sentinels = { begin: 'COAI-ACCOUNTS 1', family: 'COAI-ACCOUNTS ', end: 'COAI-ACCOUNTS-END' };

const both: readonly (readonly [string, Sentinels])[] = [['session', SESSION], ['accounts', ACCOUNTS]];

const text = (...lines: readonly string[]): string => lines.join('\n');
const inner = (result: Framed): readonly string[] => {
  assert.equal(result.kind, 'framed', `no frame: ${JSON.stringify(result)}`);

  return result.kind === 'framed' ? result.lines : [];
};

for (const [label, s] of both) {
  test(`A2-T14: the frame is found by one function — ${label} sentinels`, () => {
    const body = ['a', ':b', 'c d'];
    const plain = text(s.begin, ...body, s.end, '');
    assert.deepEqual(inner(framed(plain, s)), body, 'the lines between the begin and the end');

    assert.deepEqual(inner(framed(text('a banner', 'Welcome', plain), s)), body, 'a banner before the begin line is skipped');
    assert.deepEqual(inner(framed(text(s.begin, 'x', s.end, 'old', s.begin, ...body, s.end), s)), body, 'a banner that HOLDS the begin line: the frame starts at the LAST one');
    assert.deepEqual(inner(framed(text(s.begin, ...body, s.end, 'more', s.end), s)), body, 'two end lines: it ends at the FIRST after the begin');
    assert.deepEqual(inner(framed(text(s.begin, ...body, s.end, 'after', 'the end'), s)), body, 'text after the end is ignored');
    assert.deepEqual(framed(text(s.end, s.begin, 'a'), s), { kind: 'no-end' }, 'an end line before the begin and none after');
    assert.deepEqual(framed(text('a', 'b', s.end), s), { kind: 'no-begin', otherVersion: false }, 'no begin line');
    assert.deepEqual(framed('', s), { kind: 'no-begin', otherVersion: false }, 'nothing at all');
  });
}

test('A2-T14: a begin line of another version is told apart from none', () => {
  assert.deepEqual(framed(text('COAI-ACCOUNTS 2', 'a', 'COAI-ACCOUNTS-END'), ACCOUNTS), { kind: 'no-begin', otherVersion: true }, 'only COAI-ACCOUNTS 2');
  assert.deepEqual(framed(text('COAI-BEGIN 2', 'a', 'COAI-END'), SESSION), { kind: 'no-begin', otherVersion: true }, 'only COAI-BEGIN 2');
  assert.deepEqual(framed(text('COAI-ACCOUNTS 1', 'COAI-ACCOUNTS 2', 'a', 'COAI-ACCOUNTS-END'), ACCOUNTS), { kind: 'framed', lines: ['COAI-ACCOUNTS 2', 'a'] }, 'a begin line of this version wins over another one');
  const whole = framed(text('COAI-ACCOUNTS 1', 'x', 'COAI-ACCOUNTS-END'), ACCOUNTS);
  assert.equal(whole.kind, 'framed', 'a whole accounts reply is never taken for another version');
  assert.deepEqual(framed(text('COAI-ACCOUNTS-END'), ACCOUNTS), { kind: 'no-begin', otherVersion: false }, 'the end line does not start with the family');
});

test('A2-T14: the empty frame, and a begin line with its end right after', () => {
  assert.deepEqual(inner(framed(text(ACCOUNTS.begin, ACCOUNTS.end), ACCOUNTS)), []);
  assert.deepEqual(framed(text(ACCOUNTS.begin, 'a'), ACCOUNTS), { kind: 'no-end' });
});

// ───────────────── A2-T24: the finder was MOVED, not copied ─────────────────

const occurrences = (haystack: string, needle: string): number => haystack.split(needle).length - 1;

test('A2-T24: lastIndexOf( is in remoteFraming.ts once and not in remoteFrame.ts; remoteFrame.ts calls framed( once', () => {
  const framing = sourceOf('remoteFraming.ts');
  const frame = sourceOf('remoteFrame.ts');
  assert.equal(occurrences(framing, 'lastIndexOf('), 1, 'the shared finder holds lastIndexOf( exactly once');
  assert.equal(occurrences(frame, 'lastIndexOf('), 0, 'two frame finders: remoteFrame.ts still holds its own lastIndexOf(');
  assert.equal(occurrences(frame, ' framed('), 1, 'remoteFrame.ts must call the shared framed( exactly once');
});

test('A2-T24 companion: the scan finds what it scans for, in a sample', () => {
  assert.equal(occurrences('const a = lines.lastIndexOf(x);', 'lastIndexOf('), 1);
  assert.equal(occurrences('const r = framed(stdout, S);', ' framed('), 1);
  assert.equal(occurrences('const r = reframed(stdout, S);', ' framed('), 0, 'a longer name is not the call');
});
