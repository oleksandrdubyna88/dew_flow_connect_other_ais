import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The lint ratchet turns only one way.
 *
 * <p>`eslint-suppressions.json` is what lets `complexity` and `max-lines-per-function` be ON for
 * `src/**` while the 615 violations that predate the decision stay RECORDED rather than forgiven.
 * Without a guard it is simply a place to put a new violation: write the code, run the generator,
 * commit both, and lint is green.</p>
 *
 * <p><b>A total is not enough, and a gate reviewer showed exactly how it fails</b> — fix one old
 * violation, add one new, regenerate, and the total FALLS while new code is suppressed. So the
 * comparison is per file and per rule. That is the case this suite exists for and the one a
 * count-only check would pass.</p>
 *
 * <p>The script is SPAWNED rather than imported, because what has to be true of a guard is that it
 * REFUSES — a guard that prints a complaint and exits 0 stops nothing in a workflow.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'suppressions-only-shrink.mjs');

const SHRANK = 0;
const GREW = 1;
const REFUSED = 2;

const one = (count: number) => ({ count });

function compare(current: unknown, base: unknown, extra: string[] = []):
{ code: number; said: string } {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'suppressions-'));
  try {
    const now = path.join(dir, 'now.json');
    const before = path.join(dir, 'before.json');
    fs.writeFileSync(now, JSON.stringify(current), 'utf8');
    fs.writeFileSync(before, JSON.stringify(base), 'utf8');

    const ran = spawnSync(process.execPath, [script, ...extra, now, before],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    return { code: ran.status ?? -1, said: `${ran.stdout}${ran.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('an unchanged file passes, and so does one that suppresses less', () => {
  const base = { 'src/a.ts': { complexity: one(3) }, 'src/b.ts': { complexity: one(1) } };

  assert.equal(compare(base, base).code, SHRANK);
  assert.equal(compare({ 'src/a.ts': { complexity: one(1) } }, base).code, SHRANK,
    'fixing violations and pruning is the whole point of the file');
  assert.equal(compare({}, base).code, SHRANK, 'and fixing all of them is allowed too');
});

test('a file/rule pair that was not suppressed before is REFUSED', () => {
  const base = { 'src/a.ts': { complexity: one(3) } };
  const now = { 'src/a.ts': { complexity: one(3) }, 'src/new.ts': { complexity: one(1) } };

  const { code, said } = compare(now, base);

  assert.equal(code, GREW);
  assert.match(said, /src\/new\.ts/u, 'the refusal must name the file');
  assert.match(said, /complexity: 1 newly suppressed/u);
});

test('a pair that suppresses MORE than it did is refused, count and all', () => {
  const { code, said } = compare(
    { 'src/a.ts': { complexity: one(4) } },
    { 'src/a.ts': { complexity: one(3) } });

  assert.equal(code, GREW);
  assert.match(said, /complexity: 3 -> 4/u);
});

test('a SECOND rule on an already-suppressed file is new, not a continuation', () => {
  // The quiet one: the file is already in the list, so a check that compared file names would pass.
  const { code, said } = compare(
    { 'src/a.ts': { complexity: one(3), 'max-lines-per-function': one(1) } },
    { 'src/a.ts': { complexity: one(3) } });

  assert.equal(code, GREW);
  assert.match(said, /max-lines-per-function: 1 newly suppressed/u);
});

test('the case a TOTAL would pass: one fixed, one added', () => {
  // This is the finding, run. Old file loses three, new file gains one — the total falls from 3 to 1
  // and a monotonic-count check is green while new code is suppressed.
  const base = { 'src/old.ts': { complexity: one(3) } };
  const now = { 'src/new.ts': { complexity: one(1) } };

  const { code, said } = compare(now, base);

  assert.equal(code, GREW, 'the total FELL, and this must still refuse');
  assert.match(said, /src\/new\.ts/u);
});

test('a base that has no suppressions file at all is a pass, not a crash', () => {
  // The commit that introduces the file has nothing to compare against, and so does any branch taken
  // from before it existed. Refusing there would make the check impossible to merge.
  const { code } = compare({ 'src/a.ts': { complexity: one(3) } }, {}, ['--base-absent']);

  assert.equal(code, SHRANK);
});

test('valid JSON that is not a suppressions file is REFUSED rather than read as a verdict', () => {
  for (const bad of [null, [], 42, 'text', { 'src/a.ts': 1 }, { 'src/a.ts': { complexity: 3 } }]) {
    const { code } = compare(bad, { 'src/a.ts': { complexity: one(1) } });

    assert.equal(code, REFUSED, `${JSON.stringify(bad)} must not be read as a suppressions file`);
  }
});

test('the repository OWN suppressions file is a valid one, and it is committed', () => {
  const held = path.join(repoRoot, 'src_vs_code', 'eslint-suppressions.json');

  assert.ok(fs.existsSync(held), 'the file the rules depend on must be in the repository');
  const parsed = JSON.parse(fs.readFileSync(held, 'utf8')) as Record<string, Record<string, { count: number }>>;

  assert.equal(compare(parsed, parsed).code, SHRANK, 'and it must be a shape the guard can read');
  assert.ok(Object.keys(parsed).every((file) => !file.includes('src/test/')),
    'tests are exempt from both rules, so a suppression for one means the config drifted');
});
