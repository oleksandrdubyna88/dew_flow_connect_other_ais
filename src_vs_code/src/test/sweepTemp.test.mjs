import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { readRule, sweep, toSweep } from '../../scripts/sweepTemp.mjs';

/**
 * A run sweeps what earlier runs left in temp, before it starts — and never anything that is not
 * a leftover.
 *
 * <p>`todo/PLAN_the_tests_take_their_temp_directories_with_them.md`. The one outcome this must never
 * have is removing something in use: another session's run in flight, or the product's own working
 * directories, which share the `coai-` prefix — a live chat's directory can sit for an hour with
 * nothing written into it. So every "takes" case below is paired with the "keeps" case that a
 * build removing everything would fail.</p>
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.join(here, '..', '..', '..');
const rule = readRule(repoRoot);
const minute = 60_000;
const now = Date.UTC(2026, 8, 23, 12, 0, 0);

function dir(name, minutesAgo) {
  return { name, isDirectory: true, lastWriteMs: now - minutesAgo * minute };
}

test('the rule is the shared one: coai-, ten minutes, and the product\'s own directories named', () => {
  assert.equal(rule.prefix, 'coai-');
  assert.equal(rule.keepMinutes, 10, 'the operator\'s ruling of 2026-09-18');
  // The extension's own runtime directory, made by chatLaunch.ts. The server's are held by its suite.
  assert.ok(rule.neverSwept.includes('coai-chat-'), 'a live chat\'s working directory is not a leftover');
});

test('older than the window is taken, younger is kept — both directions', () => {
  const taken = toSweep([dir('coai-old-1', 11), dir('coai-new-1', 9), dir('coai-edge-1', 10)], now, rule);

  assert.deepEqual(taken, ['coai-old-1'],
    'eleven minutes old is a leftover; nine may be another session\'s run; ten is not yet OLDER than ten');
});

test('only the prefix is considered, whatever the age', () => {
  const taken = toSweep([dir('somebody-elses-dir', 60 * 24), dir('coai', 60 * 24), dir('coai-x', 60 * 24)], now, rule);

  assert.deepEqual(taken, ['coai-x'], 'the system temp directory is shared with other software');
});

test('the product\'s own working directories and a downloaded server are never swept', () => {
  const owned = rule.neverSwept.map((prefix) => dir(`${prefix}abc123`, 60 * 24 * 7));
  const cache = dir('coai-0.31.0', 60 * 24 * 7);

  assert.deepEqual(toSweep([...owned, cache, dir('coai-panel-abc', 60 * 24 * 7)], now, rule), ['coai-panel-abc'],
    'a week old, and still somebody\'s: the product sweeps its own on its own clock');
});

test('a FILE with the prefix is not a directory to sweep', () => {
  assert.deepEqual(toSweep([{ name: 'coai-3.png', isDirectory: false, lastWriteMs: 0 }], now, rule), []);
});

test('on a real disk: the old one goes — read-only file and all — and the fresh one stays', (t) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'sweeptest-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const old = path.join(root, 'coai-leftover');
  const fresh = path.join(root, 'coai-in-use');
  fs.mkdirSync(path.join(old, '.git', 'objects'), { recursive: true });
  const blob = path.join(old, '.git', 'objects', 'e9');
  fs.writeFileSync(blob, 'git marks its objects read-only');
  fs.chmodSync(blob, 0o444);
  fs.mkdirSync(fresh);
  const anHourAgo = new Date(Date.now() - 60 * minute);
  fs.utimesSync(old, anHourAgo, anHourAgo);

  const said = sweep(root, Date.now(), rule);

  assert.deepEqual(said, { removed: 1, failed: 0 });
  assert.equal(fs.existsSync(old), false, 'an hour-old leftover is nobody\'s working state');
  assert.equal(fs.existsSync(fresh), true, 'and one made a moment ago may be another run\'s');
});

test('a directory that cannot be removed is counted and stepped over, never thrown', () => {
  const io = {
    readdirSync: () => ['coai-held', 'coai-free'].map((name) => ({ name, isDirectory: () => true })),
    statSync: () => ({ mtimeMs: now - 60 * minute }),
    rmSync: (target) => {
      if (target.endsWith('coai-held')) {
        throw Object.assign(new Error('EBUSY: a handle is open'), { code: 'EBUSY' });
      }
    },
  };

  assert.deepEqual(sweep('/tmp', now, rule, io), { removed: 1, failed: 1 },
    'a suite that refuses to start over housekeeping has made things worse');
});

test('the runner sweeps FIRST, before it discovers or runs anything', () => {
  const runner = fs.readFileSync(path.join(here, '..', '..', 'scripts', 'run-tests.mjs'), 'utf8');
  const main = runner.slice(runner.indexOf('function main() {'));

  assert.match(main, /^function main\(\) \{\s*sweepFirst\(\);\s*\n/,
    'a sweep after the run starts would race the run\'s own directories');
});
