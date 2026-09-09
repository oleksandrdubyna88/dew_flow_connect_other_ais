import assert from 'node:assert/strict';
import { test } from 'node:test';
import { measureOptionsFrom } from '../measureStreamArgs';

/**
 * The arguments that decide how much money a measurement spends.
 *
 * <p>Every one of these runs real turns on real signed-in accounts, so reading the command line
 * wrongly is not a cosmetic bug — it is three accounts billed instead of one, silently, with the
 * harness reporting exactly what it was asked for.</p>
 */

const KNOWN = ['claude', 'codex', 'agy'];

test('a vendor named on its own is the vendor measured', () => {
  // THE REGRESSION. With no `--lines`, `indexOf` returns -1, and the filter that drops the flag's
  // VALUE at `at + 1` dropped index 0 instead — the vendor — so this parsed as `all` and spent turns
  // on all three accounts. Verified against the unfixed parser before the fix landed.
  // (CodeRabbit, on the pull request.)
  assert.deepStrictEqual(
    measureOptionsFrom(['agy'], KNOWN),
    { kind: 'run', vendor: 'agy', lines: 40 },
  );
  assert.deepStrictEqual(
    measureOptionsFrom(['codex'], KNOWN),
    { kind: 'run', vendor: 'codex', lines: 40 },
  );
});

test('no arguments at all measures everything, which is the only safe default to have', () => {
  assert.deepStrictEqual(measureOptionsFrom([], KNOWN), { kind: 'run', vendor: 'all', lines: 40 });
});

test('the vendor is read whichever side of the flag it is written on', () => {
  assert.deepStrictEqual(
    measureOptionsFrom(['agy', '--lines', '400'], KNOWN),
    { kind: 'run', vendor: 'agy', lines: 400 },
  );
  assert.deepStrictEqual(
    measureOptionsFrom(['--lines', '400', 'agy'], KNOWN),
    { kind: 'run', vendor: 'agy', lines: 400 },
  );
});

test('the flag value is never mistaken for the vendor', () => {
  // `400` is not a vendor, and reading it as one would refuse a command that is perfectly good.
  assert.deepStrictEqual(
    measureOptionsFrom(['--lines', '400'], KNOWN),
    { kind: 'run', vendor: 'all', lines: 400 },
  );
});

test('an unknown vendor is refused BY NAME rather than quietly measured as all', () => {
  const seen = measureOptionsFrom(['gemini'], KNOWN);

  assert.strictEqual(seen.kind, 'error');
  assert.match(seen.kind === 'error' ? seen.message : '', /gemini/);
});

test('a line count outside the bound is refused, because it is a prompt sent to a paid account', () => {
  for (const given of ['0', '9999', 'lots', '-5', '']) {
    const seen = measureOptionsFrom(['--lines', given], KNOWN);
    assert.strictEqual(seen.kind, 'error', `--lines ${given} should have been refused`);
  }
});

test('help is help, whichever way it is asked for, and measures nothing', () => {
  assert.deepStrictEqual(measureOptionsFrom(['--help'], KNOWN), { kind: 'help' });
  assert.deepStrictEqual(measureOptionsFrom(['-h'], KNOWN), { kind: 'help' });
  assert.deepStrictEqual(measureOptionsFrom(['agy', '--help'], KNOWN), { kind: 'help' });
});
