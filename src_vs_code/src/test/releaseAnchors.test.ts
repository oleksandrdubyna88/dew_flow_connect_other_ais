import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * Every release line's tag sits on a commit release-please can count from
 * (research/PLAN_the_release_guards_contradict.md, 2026-09-26).
 *
 * <p>release-please looks for a line's release commit inside the commits that touch the line's
 * package. `mcp-v0.38.0` had been moved to a commit touching only `.github/`, so it was not there, and
 * an empty `mcp 0.39.0` opened three times. Run here against a real, throwaway git repository, so the
 * part of the script that asks git is run too.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'release-anchors.mjs');

const ANCHORED = 0;
const ADRIFT = 1;
const REFUSED = 2;

function git(dir: string, ...args: string[]): string {
  const ran = spawnSync('git', args, { cwd: dir, encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });
  assert.equal(ran.status, 0, `git ${args.join(' ')} failed: ${ran.stderr}`);

  return ran.stdout.trim();
}

/** Writes files and commits them, answering the new commit. */
function commit(dir: string, message: string, files: Record<string, string>): string {
  for (const [file, text] of Object.entries(files)) {
    fs.mkdirSync(path.dirname(path.join(dir, file)), { recursive: true });
    fs.writeFileSync(path.join(dir, file), text, 'utf8');
  }
  git(dir, 'add', '-A');
  git(dir, '-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', message);

  return git(dir, 'rev-parse', 'HEAD');
}

/** A repository with one line, `pkg` released as `pkg-v1.0.0` on its release commit. */
function released(): { dir: string; release: string } {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'anchors-'));
  git(dir, 'init', '-q');
  commit(dir, 'feat: the first code', { 'pkg/a.ts': 'one' });
  const release = commit(dir, 'chore(main): release pkg 1.0.0', {
    'pkg/version.txt': '1.0.0',
    'release-please-config.json': JSON.stringify({ packages: { pkg: { component: 'pkg' } } }),
    '.release-please-manifest.json': JSON.stringify({ pkg: '1.0.0' }),
  });

  return { dir, release };
}

function run(dir: string): { code: number; said: string } {
  const ran = spawnSync(process.execPath, [script, '--repo', dir],
    { encoding: 'utf8', timeout: 60_000, killSignal: 'SIGKILL', env: { ...process.env, GITHUB_REPOSITORY: 'o/r' } });
  assert.equal(ran.error, undefined, `could not run: ${ran.error?.message}`);
  assert.notEqual(ran.status, null, `did not exit on its own (signal ${ran.signal})`);

  return { code: ran.status as number, said: `${ran.stdout}${ran.stderr}` };
}

function cleaned<T>(dir: string, body: () => T): T {
  try {
    return body();
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('a tag on the release commit — which touches only the package version file — is anchored', () => {
  const { dir, release } = released();
  cleaned(dir, () => {
    git(dir, 'tag', 'pkg-v1.0.0', release);

    const { code, said } = run(dir);
    assert.equal(code, ANCHORED, `a version file is under the package, so this is findable: ${said}`);
  });
});

test('a tag moved onto a commit outside its package is refused, and the way back is printed', () => {
  // The 2026-09-26 shape: the recovery moved the tag onto the baseline commit.
  const { dir, release } = released();
  cleaned(dir, () => {
    const baseline = commit(dir, 'chore(release): pkg 1.0.0 in the changelog baseline',
      { '.github/changelog-baseline.json': '{"Server":["1.0.0"]}' });
    git(dir, 'tag', 'pkg-v1.0.0', baseline);

    const { code, said } = run(dir);
    assert.equal(code, ADRIFT, 'release-please would count every pkg commit in its window as new');
    assert.match(said, /pkg-v1\.0\.0/, 'the refusal names the tag');
    assert.match(said, /\.github\/changelog-baseline\.json/, 'and what its commit touches instead');
    assert.ok(said.includes(`refs/tags/pkg-v1.0.0 -f sha=${release}`),
      'and the command that puts it back on the release commit, found rather than guessed');
    assert.match(said, /identical/, 'and that the package tree is the same there, so the release is unchanged');
  });
});

test('a rebase merge whose last commit is outside the package is refused the same way', () => {
  // How #560 merged: the release commit, then a CHANGELOG commit on top, and the tag on the top one.
  const { dir, release } = released();
  cleaned(dir, () => {
    const top = commit(dir, 'docs(changelog): Extension 1.0.0 · Server 1.0.0', { 'other/CHANGELOG.md': '# 1.0.0' });
    git(dir, 'tag', 'pkg-v1.0.0', top);

    const { code, said } = run(dir);
    assert.equal(code, ADRIFT, `a tag on the trailing commit is the same break: ${said}`);
    assert.ok(said.includes(release), 'and the way back names the release commit under it');
  });
});

test('a version whose tag has not been cut yet passes — that is a merged release waiting for its tag', () => {
  // The run that CUTS the tag starts with exactly this state; refusing it would block every release.
  const { dir } = released();
  cleaned(dir, () => {
    const { code, said } = run(dir);
    assert.equal(code, ANCHORED, `the tagging run itself would be blocked: ${said}`);
    assert.match(said, /not cut yet/);
  });
});

test('a configuration it cannot read is refused, not passed', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'anchors-'));
  cleaned(dir, () => {
    git(dir, 'init', '-q');

    assert.equal(run(dir).code, REFUSED, 'a check that cannot look must not say all is well');
  });
});

test('release-please.yml runs the anchor check first, over a checkout that has the tags', () => {
  // A script nobody runs guards nothing; the order matters because the action is what opens the PR.
  const workflow = fs.readFileSync(path.join(repoRoot, '.github', 'workflows', 'release-please.yml'), 'utf8');
  const check = workflow.indexOf('node .github/scripts/release-anchors.mjs');
  const action = workflow.indexOf('googleapis/release-please-action@');

  assert.ok(check > 0 && action > check, 'the anchor check does not run before release-please does');
  const before = workflow.slice(0, check);
  assert.match(before, /fetch-depth: 0/, 'a shallow checkout has no history to find the release commit in');
  assert.match(before, /fetch-tags: true/, 'a checkout without tags reads every line as not cut yet');
});
