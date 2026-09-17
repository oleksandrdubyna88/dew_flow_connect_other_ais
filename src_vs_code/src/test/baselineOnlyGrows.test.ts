import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The ratchet closes in both directions.
 *
 * <p>`changelog-baseline.json` is what makes the release guard able to say a note has gone missing.
 * But the baseline lives in the repository it guards, so <b>one commit that deletes a note AND its
 * baseline row passes everything</b> — both sides of the comparison move together. That is the
 * residual hole in any in-repo ratchet, and closing it is what makes "the line cannot lose its
 * notes" exact rather than nearly exact.</p>
 *
 * <p>The base revision is the thing to get right, and two gate reviewers said so independently: a
 * shallow CI checkout has no `origin/main`, and after a deletion lands on main, main compares equal
 * to itself. So the comparison is against the pull request's own recorded base SHA, and a base that
 * carries no baseline at all — the commit that introduces one — is a pass, not a crash.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'baseline-only-grows.mjs');

const KEPT = 0;
const LOST = 1;
const REFUSED = 2;

function compare(current: unknown, base: unknown): { code: number; said: string } {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'baselinegrows-'));
  try {
    const now = path.join(dir, 'now.json');
    const before = path.join(dir, 'before.json');
    fs.writeFileSync(now, JSON.stringify(current), 'utf8');
    fs.writeFileSync(before, JSON.stringify(base), 'utf8');

    const ran = spawnSync(process.execPath, [script, now, before],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    assert.equal(ran.error, undefined, `could not run: ${ran.error?.message}`);
    assert.notEqual(ran.status, null, `did not exit on its own (signal ${ran.signal})`);

    return { code: ran.status as number, said: `${ran.stdout}${ran.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('removing a recorded release is refused, and named', () => {
  const { code, said } = compare(
    { Server: ['0.27.1', '0.28.0'] },
    { Server: ['0.27.1', '0.27.0', '0.28.0'] });

  assert.equal(code, LOST, 'deleting the row is how the note becomes deletable');
  assert.match(said, /0\.27\.0/, 'the refusal names what would stop being protected');
});

test('adding releases is the normal case and passes', () => {
  const { code } = compare({ Server: ['0.27.1', '0.28.0', '0.29.0'] }, { Server: ['0.27.1', '0.28.0'] });

  assert.equal(code, KEPT, 'every release adds a row; that is the whole workflow');
});

test('an unchanged baseline passes', () => {
  const { code } = compare({ Server: ['0.28.0'] }, { Server: ['0.28.0'] });

  assert.equal(code, KEPT);
});

test('reordering is not removing', () => {
  const { code } = compare({ Server: ['0.28.0', '0.27.1'] }, { Server: ['0.27.1', '0.28.0'] });

  assert.equal(code, KEPT, 'the baseline is a set of protected versions, not a sequence');
});

test('dropping a whole release line is refused too', () => {
  const { code, said } = compare({ Server: ['0.28.0'] }, { Server: ['0.28.0'], Extension: ['0.50.0'] });

  assert.equal(code, LOST);
  assert.match(said, /Extension/, 'a line that disappears takes every row with it');
});

test('a base with no baseline at all is a pass, not a crash', () => {
  // The commit that INTRODUCES the baseline has nothing to compare against, and so does any branch
  // taken from before it existed. Refusing there would make the check impossible to merge.
  const { code, said } = compare({ Server: ['0.28.0'] }, {});

  assert.equal(code, KEPT, 'nothing was recorded before, so nothing can have been lost');
  assert.match(said, /nothing|no baseline|first/i, 'and it says why it had nothing to compare');
});

test('a file it cannot read is refused rather than waved through', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'baselinegrows-'));
  try {
    const ran = spawnSync(process.execPath,
      [script, path.join(dir, 'missing.json'), path.join(dir, 'also-missing.json')],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    assert.equal(ran.status, REFUSED, 'a check that cannot check must not report a pass');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('the real baseline has not lost anything against itself', () => {
  const real = JSON.parse(fs.readFileSync(
    path.join(repoRoot, '.github', 'changelog-baseline.json'), 'utf8'));
  const { code } = compare(real, real);

  assert.equal(code, KEPT, 'the shipped file is a valid input to its own check');
});

test('CI runs it against the pull request\'s own base commit', () => {
  // The base revision is the half two reviewers called Blocking. `origin/main` does not exist in a
  // shallow checkout, and once a deletion has landed on main, main compares equal to itself. The
  // event's recorded base SHA is stable and is the commit this change is actually proposed against.
  const ci = fs.readFileSync(path.join(repoRoot, '.github', 'workflows', 'ci.yml'), 'utf8')
    .replace(/\r\n/g, '\n');

  const at = ci.indexOf('baseline-only-grows.mjs');
  assert.notEqual(at, -1, 'a check nothing invokes guards nothing');

  const step = ci.slice(ci.lastIndexOf('- name:', at), at + 400);
  assert.match(step, /pull_request\.base\.sha/,
    'pinned to the event\'s base commit, not to a ref a shallow checkout may not have');
  assert.match(step, /if:\s*github\.event_name == 'pull_request'/,
    'only on a pull request — on main it would compare a commit with itself');
});
