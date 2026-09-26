import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * Markdown alone never opens a release — the operator's rule, 2026-09-26
 * (research/PLAN_the_release_guards_contradict.md). Run as the workflow runs it, over the same facts.
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'docs-only-title.mjs');

const FINE = 0;
const REFUSED = 1;
const UNREADABLE = 2;

interface Commit { sha: string; message: string; files: string[] }

function check(title: string, files: string[], commits: Commit[] = []): { code: number; said: string } {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'docsonly-'));
  try {
    const facts = path.join(dir, 'facts.json');
    fs.writeFileSync(facts, JSON.stringify({ title, files, commits }), 'utf8');
    const ran = spawnSync(process.execPath, [script, '--facts', facts],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });
    assert.equal(ran.error, undefined, `could not run: ${ran.error?.message}`);
    assert.notEqual(ran.status, null, `did not exit on its own (signal ${ran.signal})`);

    return { code: ran.status as number, said: `${ran.stdout}${ran.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('a releasing title over Markdown alone in a package is refused, for every releasing type', () => {
  for (const title of ['feat: a new page', 'fix(docs): a typo', 'perf: faster prose', 'revert: the old words', 'docs!: renamed']) {
    const { code, said } = check(title, ['src_mcp/README.md']);
    assert.equal(code, REFUSED, `"${title}" would open an mcp release for a README`);
    assert.match(said, /src_mcp/, 'and the refusal names the package it would release');
  }
});

test('the same change said as docs: passes', () => {
  assert.equal(check('docs: a new page', ['src_mcp/README.md', 'src_vs_code/CHANGELOG.md']).code, FINE);
});

test('a releasing title with code in the package passes, Markdown beside it or not', () => {
  assert.equal(check('feat: the thing', ['src_mcp/src/Thing.cs', 'src_mcp/README.md']).code, FINE);
});

test('Markdown under one package riding on code for another is refused — it would release both', () => {
  // A `feat:` for the extension that also touches src_mcp/README.md puts that commit on the mcp line.
  const { code, said } = check('feat(panel): a switch', ['src_vs_code/src/panel.ts', 'src_mcp/README.md']);
  assert.equal(code, REFUSED, 'the mcp line would release for its README');
  assert.match(said, /src_mcp\/README\.md/);
  assert.doesNotMatch(said, /would release src_vs_code/, 'the extension has code, so it is not the problem');
});

test('Markdown outside every package opens no release, so it is not refused', () => {
  assert.equal(check('feat: the plan', ['todo/PLAN_x.md', 'research/module_x.md']).code, FINE);
});

test('a Markdown-only COMMIT of a releasing type is refused even under a docs: title — a rebase merge keeps it', () => {
  const { code, said } = check('docs: the notes', ['src_mcp/README.md'],
    [{ sha: 'a'.repeat(40), message: 'fix: the readme\n\nbody', files: ['src_mcp/README.md'] }]);
  assert.equal(code, REFUSED, 'release-please reads this commit under a rebase merge');
  assert.match(said, /aaaaaaaa/, 'and the refusal names the commit');
});

test('facts it cannot read are refused, not passed', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'docsonly-'));
  try {
    const facts = path.join(dir, 'facts.json');
    fs.writeFileSync(facts, JSON.stringify({ title: 'feat: x' }), 'utf8');
    const ran = spawnSync(process.execPath, [script, '--facts', facts], { encoding: 'utf8', timeout: 30_000 });
    assert.equal(ran.status, UNREADABLE, 'facts without files or commits cannot be judged');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('pr-title.yml runs it on every pull request, with the title passed as data', () => {
  const workflow = fs.readFileSync(path.join(repoRoot, '.github', 'workflows', 'pr-title.yml'), 'utf8');

  assert.match(workflow, /node \.github\/scripts\/docs-only-title\.mjs --pr "\$PR_NUMBER"/,
    'a rule nobody runs holds nothing');
  assert.match(workflow, /PR_TITLE: \$\{\{ github\.event\.pull_request\.title \}\}/,
    'the title reaches the script through the environment, never pasted into the shell');
  assert.doesNotMatch(workflow, /ref: \$\{\{ github\.event\.pull_request\.head/,
    'pull_request_target must not check out the pull request\'s own code');
});
