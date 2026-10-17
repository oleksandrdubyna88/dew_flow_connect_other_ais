// node --test scripts/lib/*.test.mjs — DL2's names grammar (todo/PLAN_kimi_and_vibe_on_linux.md) as a reference, held against the
// vectors python-dotenv itself produced (shared/dotenv-name-vectors.json, L-V6b, written by scripts/dotenv-name-vectors.mjs with
// Vibe 2.26.1's own Python). The grammar reads NAMES only — the value after `=` is never parsed — so these tests assert the
// IMPLICATION the plan's soundness argument rests on: whatever the reference ADMITS binds no name but MISTRAL_API_KEY in
// python-dotenv. Runs nothing; reads one committed JSON file.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';
import { ADMITTED_NAME, PYTHON_WHITESPACE, coaiVerdict, physicalLines } from './dotenv-names.mjs';

const repo = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const VECTORS = JSON.parse(readFileSync(join(repo, 'shared', 'dotenv-name-vectors.json'), 'utf8'));

test('a physical line ends at CRLF, LF or a bare CR — and at nothing else (DL2: python-dotenv\'s _newline)', () => {
  assert.deepEqual(physicalLines('a\r\nb\nc\rd'), ['a', 'b', 'c', 'd']);
  assert.deepEqual(physicalLines('a\u000bb\u000cc\u0085d e'), ['a\u000bb\u000cc\u0085d e'], 'VT, FF, NEL and LS are not line breaks');
});

const ADMIT = { verdict: 'admitted', refusals: [] };
const refused = (...refusals) => ({ verdict: 'refused', line: refusals[0].line, why: refusals[0].why, refusals });
const verdictOf = (text) => {
  const { names, ...verdict } = coaiVerdict(text);
  return verdict;
};

test('the forms python-dotenv binds MISTRAL_API_KEY from are admitted (S-V13\'s admitted rows)', () => {
  for (const line of ['MISTRAL_API_KEY=v', 'MISTRAL_API_KEY = "v"', "export MISTRAL_API_KEY='v'", '  MISTRAL_API_KEY=v  # note', "'MISTRAL_API_KEY'=v"]) {
    assert.deepEqual(coaiVerdict(`${line}\n`), { ...ADMIT, names: ['MISTRAL_API_KEY'] }, line);
  }
});

test('a bare key, a line beginning with "=", and a continuation that binds nothing are refused as NoBinding at their line', () => {
  assert.deepEqual(verdictOf('sk-test-abcdef\n'), refused({ line: 1, why: 'NoBinding' }));
  assert.deepEqual(verdictOf('=v\n'), refused({ line: 1, why: 'NoBinding' }));
  assert.deepEqual(verdictOf('MISTRAL_API_KEY="dummy\nending"\n'), refused({ line: 2, why: 'NoBinding' }));
});

test('the three multi-line shapes of DL2, and a bare CR, refuse or admit by the grammar — the FIRST refused line is the verdict\'s', () => {
  assert.deepEqual(verdictOf('MISTRAL_API_KEY="dummy\nLEAK_CANARY=ending"\n'), refused({ line: 2, why: 'OtherName' }));
  assert.deepEqual(verdictOf('MISTRAL_API_KEY="dummy\n#ending"\n'), ADMIT);
  // DL2 and S-V13 say "refused at line 3" here; by DL2's own grammar line 2 (a lone `"`) is already a bare word, so the first
  // refusal is line 2 — and line 3, where python-dotenv binds VIBE_X, is refused too. Both are recorded.
  assert.deepEqual(verdictOf('MISTRAL_API_KEY="a\n"\nVIBE_X=1\n'), refused({ line: 2, why: 'NoBinding' }, { line: 3, why: 'OtherName' }));
  assert.deepEqual(verdictOf('MISTRAL_API_KEY=x\rVIBE_X=1\n'), refused({ line: 2, why: 'OtherName' }));
});

test('the BYTES decide two kinds (DL2, the A1 spec): a UTF-8 byte-order mark is ByteOrderMark at line 1; invalid UTF-8 or a NUL is NotText at its line', () => {
  const bytes = (...parts) => Buffer.concat(parts.map((p) => (typeof p === 'string' ? Buffer.from(p, 'utf8') : Buffer.from(p))));
  assert.deepEqual(verdictOf(bytes([0xef, 0xbb, 0xbf], 'MISTRAL_API_KEY=sk-test-bom\n')), refused({ line: 1, why: 'ByteOrderMark' }));
  assert.deepEqual(verdictOf(bytes('MISTRAL_API_KEY=sk-test-ok\n', 'MISTRAL_API_KEY=sk-test-', [0xff], '\n')), refused({ line: 2, why: 'NotText' }));
  assert.deepEqual(verdictOf(bytes('MISTRAL_API_KEY=sk-test-a', [0x00], 'b\n')), refused({ line: 1, why: 'NotText' }));
  assert.deepEqual(verdictOf(bytes('MISTRAL_API_KEY=sk-test-ok\n')), ADMIT, 'a Buffer and a string are read alike');
});

test('every vector: the reference gives the verdict the file records, and an ADMITTED file binds no name but MISTRAL_API_KEY in python-dotenv', () => {
  assert.ok(VECTORS.vectors.length >= 30, 'the loop below would check almost nothing');
  assert.ok(VECTORS.vectors.some((v) => v.coai.verdict === 'admitted') && VECTORS.vectors.some((v) => v.coai.verdict === 'refused'),
    'the file must hold both verdicts, or the implication is checked vacuously');
  for (const v of VECTORS.vectors) {
    assert.deepEqual(verdictOf(Buffer.from(v.inputBase64, 'base64')), v.coai, `${v.id}: the reference disagrees with the recorded verdict`);
    if (v.input !== null) assert.equal(Buffer.from(v.input, 'utf8').toString('base64'), v.inputBase64, `${v.id}: input and inputBase64 disagree`);
    if (v.coai.verdict === 'admitted') {
      assert.deepEqual(v.binds.filter((n) => n !== ADMITTED_NAME), [], `${v.id}: admitted, yet python-dotenv binds ${JSON.stringify(v.binds)}`);
    }
  }
});

test('a refused vector names what its sentence must never echo: every name but the admitted one, and every value word', () => {
  const leak = VECTORS.vectors.find((v) => v.id === 'multiline-binding-shaped-continuation');
  assert.ok(leak, 'the LEAK_CANARY vector of DL2 is missing');
  assert.ok(leak.mustNotEcho.includes('LEAK_CANARY') && leak.mustNotEcho.includes('ending') && leak.mustNotEcho.includes('dummy'), JSON.stringify(leak.mustNotEcho));
  assert.equal(leak.mustNotEcho.includes(ADMITTED_NAME), false);
});

test('the whitespace the reference skips is the set python-dotenv\'s \\s matched in the oracle run', () => {
  assert.deepEqual([...PYTHON_WHITESPACE].map((c) => c.codePointAt(0)).sort((a, b) => a - b), VECTORS.oracle.whitespace);
});

test('no vector input holds a real-looking secret: every value is synthetic (sk-test-… or a short word)', () => {
  for (const v of VECTORS.vectors) assert.doesNotMatch(Buffer.from(v.inputBase64, 'base64').toString('latin1'), /sk-(?!test-)[A-Za-z0-9]{8,}/u, v.id);
});

test('every refusal kind of DL2 has at least one vector, so E2\'s test meets each', () => {
  const kinds = new Set(VECTORS.vectors.flatMap((v) => v.coai.refusals.map((r) => r.why)));
  assert.deepEqual([...kinds].sort(), ['ByteOrderMark', 'NoBinding', 'NotText', 'OtherName']);
});
