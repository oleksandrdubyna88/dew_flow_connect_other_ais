import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * A release that tells nobody what is in it does not ship.
 *
 * <p><b>Measured across all three heading shapes this file uses</b>, which is the correction that
 * matters: 65 `mcp-v*` tags, 24 of them documented — 10 as `## Server X`, 3 inside a joint
 * `## Extension A · Server X`, and 15 in the parenthesised `## A — date (server X)` form the early
 * extension releases used — and 41 with nothing at all. Counting only `^## Server` says 10, which is
 * how a first pass at this came to reconstruct three releases that were already documented.</p>
 *
 * <p>The guard runs in `mcp-draft`, the first job an `mcp-v*` tag reaches, and a refusal there costs
 * nothing: no draft exists yet and no asset has been uploaded. <b>If it does refuse, the repair is to
 * delete the tag and push it again</b> once the entry is written — this repository has burned a tag
 * before (`mcp-v0.16.0`) and it is the supported move, because the alternative is a published release
 * whose notes never arrive.</p>
 *
 * <p>These cases SPAWN the script rather than importing it, because the thing that has to be true is
 * its exit code: a guard that prints a complaint and exits 0 stops nothing in a workflow. They assert
 * the EXACT code — a code round pointed out that `notEqual(code, 0)` is also satisfied by a crash, a
 * bad argument, or the script not being there at all, so it proves none of the refusals it claims.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'changelog-names-the-release.mjs');
const realChangelog = path.join(repoRoot, 'src_vs_code', 'CHANGELOG.md');
const realBaseline = path.join(repoRoot, '.github', 'changelog-baseline.json');

/** Exit codes the guard promises. Named, because a bare 1 and a bare 2 read the same in an assert. */
const NAMED = 0;
const MISSING = 1;
const REFUSED = 2;

interface Ran { code: number; said: string }

/**
 * Run the guard the way a workflow runs it, and report what the workflow would see.
 *
 * <p>Bounded and killed as a tree: the shared rule is "exe + argv, never a shell string, always with
 * a timeout — and the timeout kills the entire process tree". A hung guard must fail this suite, not
 * hold it open.</p>
 */
function guard(tag: string, changelog: string, baseline?: string): Ran {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'changelogguard-'));
  try {
    const changelogPath = path.join(dir, 'CHANGELOG.md');
    fs.writeFileSync(changelogPath, changelog, 'utf8');

    const argv = [script, tag, '--changelog', changelogPath];
    if (baseline !== undefined) {
      const baselinePath = path.join(dir, 'baseline.json');
      fs.writeFileSync(baselinePath, baseline, 'utf8');
      argv.push('--baseline', baselinePath);
    }

    const ran = spawnSync(process.execPath, argv,
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    // A spawn that never ran, or one the timeout killed, must not be read as a refusal: both leave a
    // null status, and `notEqual(code, 0)` would call either of them the guard working.
    assert.equal(ran.error, undefined, `the guard could not be run at all: ${ran.error?.message}`);
    assert.notEqual(ran.status, null,
      `the guard did not exit on its own (signal ${ran.signal}) — that is a hang, not a refusal`);

    return { code: ran.status as number, said: `${ran.stdout}${ran.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

/** A changelog holding one heading, in whichever of the three shapes the case is about. */
const entry = (heading: string) => `# Changelog\n\n${heading}\n\nSomething a person can read.\n`;

/** The baseline a case wants, as the file the guard reads. */
const baselineOf = (...versions: string[]) => JSON.stringify({ Server: versions }, undefined, 2);

test('a release with no entry does not ship', () => {
  const { code, said } = guard('mcp-v0.29.0', entry('## Server 0.28.0 — 2026-09-17'),
    baselineOf('0.28.0', '0.29.0'));

  assert.equal(code, MISSING, 'a guard that complains and exits 0 stops nothing in a workflow');
  assert.match(said, /0\.29\.0/, 'the refusal names the version whose entry is missing');
  assert.match(said, /## Server 0\.29\.0/, 'and the heading somebody has to write');
});

test('a release whose entry is there ships', () => {
  const { code } = guard('mcp-v0.28.0', entry('## Server 0.28.0 — 2026-09-17'),
    baselineOf('0.28.0'));

  assert.equal(code, NAMED, 'the guard has to be seen to open as well as to close');
});

test('another release line does not satisfy it', () => {
  // Three lines share this file and one number can exist in two of them. `Team server 0.28.0` is a
  // different product from `Server 0.28.0`.
  const { code, said } = guard('mcp-v0.28.0', entry('## Team server 0.28.0 — 2026-09-17'),
    baselineOf('0.28.0'));

  assert.equal(code, MISSING, 'the Team server line is not the MCP line');
  assert.match(said, /0\.28\.0/);
});

test('a version this one is a prefix of does not satisfy it', () => {
  // The trap a naive `includes` walks into: "0.2.0" is a substring of "0.28.0".
  const { code } = guard('mcp-v0.2.0', entry('## Server 0.28.0 — 2026-09-17'), baselineOf('0.2.0'));

  assert.equal(code, MISSING, '0.2.0 is not 0.28.0, however the substring reads');
});

test('a heading that only starts with the version does not satisfy it either', () => {
  const { code } = guard('mcp-v0.2.0', entry('## Server 0.2.0.1 — 2026-09-17'), baselineOf('0.2.0'));

  assert.equal(code, MISSING, 'a version is a whole number, not a prefix of a longer one');
});

test('a version carrying a suffix is not the version', () => {
  // `(?![\d.])` was the first guard here and it passes every one of these: the character after the
  // number is a letter or a hyphen, which is neither a digit nor a dot. The heading has to END the
  // version, not merely stop being numeric.
  for (const typo of ['## Server 0.28.0rc1 — 2026-09-17',
    '## Server 0.28.0-beta.1 — 2026-09-17',
    '## Server 0.28.0x — 2026-09-17']) {
    const { code } = guard('mcp-v0.28.0', entry(typo), baselineOf('0.28.0'));

    assert.equal(code, MISSING, `"${typo}" is not an entry for 0.28.0`);
  }
});

test('the date after the version does not stop it matching', () => {
  // Every heading in this file carries ` — <date>`, so an equality check on the line would refuse
  // every real entry.
  const { code } = guard('mcp-v0.28.0', entry('## Server 0.28.0 — 2026-09-17'), baselineOf('0.28.0'));

  assert.equal(code, NAMED, 'the heading format this file actually uses has to pass');
});

test('a joint heading is an entry for both halves', () => {
  // What this file writes when one release shipped the extension and the server together. Three of
  // them exist, and missing them is what made a first pass reconstruct 0.18.15 to 0.18.17 twice.
  const { code } = guard('mcp-v0.18.17',
    entry('## Extension 0.32.3 · Server 0.18.17 — 2026-09-10'), baselineOf('0.18.17'));

  assert.equal(code, NAMED, 'a joint heading names the Server release as surely as a bare one');
});

test('the parenthesised form is an entry too', () => {
  // Fifteen releases are documented this way, by the extension entries that carried them. A guard
  // that cannot see them would call every one of those releases undocumented.
  const { code } = guard('mcp-v0.18.3', entry('## 0.31.0 — 2026-09-06 (server 0.18.3)'),
    baselineOf('0.18.3'));

  assert.equal(code, NAMED, 'the form the early extension releases used still documents the server');
});

test('a tag from another line is not this guard\'s business', () => {
  const { code } = guard('extension-v0.50.0', entry('## Server 0.28.0 — 2026-09-17'),
    baselineOf('0.28.0'));

  assert.equal(code, NAMED,
    'only the MCP line is guarded today; the extension line has its own gaps and its own decision');
});

test('a malformed argument is refused rather than assumed', () => {
  const { code, said } = guard('not-a-tag', entry('## Server 0.28.0 — 2026-09-17'),
    baselineOf('0.28.0'));

  assert.equal(code, REFUSED, 'a tag it cannot read must not be read as "nothing to check"');
  assert.match(said, /not-a-tag/, 'and it says what it was given');
});

test('a changelog it cannot read is refused, never passed', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'changelogguard-'));
  try {
    const ran = spawnSync(process.execPath,
      [script, 'mcp-v0.28.0', '--changelog', path.join(dir, 'nothing-here.md')],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    assert.equal(ran.status, REFUSED, 'a guard that cannot check must not report a pass');
    assert.match(`${ran.stdout}${ran.stderr}`, /nothing-here\.md|could not be read/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

/**
 * The line cannot lose a note it already has.
 *
 * <p>A guard that examines only the tag being released is not that promise, and a code round said so:
 * a 0.29.0 release that deletes the 0.28.0 heading passes it. So the versions live in a baseline FILE
 * that the release gate itself reads — not in a list a test keeps, which no release ever consults.</p>
 */
test('a release that deletes an older note does not ship', () => {
  const { code, said } = guard('mcp-v0.29.0',
    entry('## Server 0.29.0 — 2026-09-18'), baselineOf('0.28.0', '0.29.0'));

  assert.equal(code, MISSING, 'the new entry is there, but 0.28.0 has lost its note');
  assert.match(said, /0\.28\.0/, 'and the refusal names the release whose note went missing');
});

test('a release absent from the baseline does not ship either', () => {
  // The ratchet has to close behind each release or it protects only what it was born with: writing
  // the entry and adding the version are one act, and the refusal says so.
  const { code, said } = guard('mcp-v0.29.0',
    entry('## Server 0.29.0 — 2026-09-18'), baselineOf('0.28.0'));

  assert.equal(code, MISSING, '0.29.0 has an entry but nothing would notice it being deleted');
  assert.match(said, /baseline/i, 'and the refusal names the file to add it to');
});

/**
 * A line cannot be both guarded and bypassed.
 *
 * <p>The first version of this script kept two structures — a `LINES` map and an `UNGUARDED` array
 * checked BEFORE it — and its own header told the next person that guarding another line was "one row
 * in LINES". It was not: the bypass ran first, so the new row would have been silently dead. One
 * registry, one field, and this case reads it back.</p>
 */
test('every release line is declared exactly once, guarded or not', () => {
  const ran = spawnSync(process.execPath, [script, '--lines'],
    { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

  assert.equal(ran.status, NAMED, `--lines should report the registry: ${ran.stderr}`);
  const lines = JSON.parse(ran.stdout) as { prefix: string; word?: string; guarded: boolean }[];

  const prefixes = lines.map((l) => l.prefix);
  assert.equal(new Set(prefixes).size, prefixes.length,
    `a prefix declared twice is a line that can be guarded and bypassed at once: ${prefixes}`);
  assert.ok(prefixes.includes('mcp-v'), 'the MCP line is the one guarded today');

  const guarded = lines.filter((l) => l.guarded);
  assert.deepEqual(guarded.map((l) => l.prefix), ['mcp-v'],
    'guarding another line is a change to this registry, and nothing else');
  assert.ok(guarded.every((l) => typeof l.word === 'string' && l.word.length > 0),
    'a guarded line has to say which heading word its entries carry');
});

/**
 * The baseline is real, and the changelog still honours it.
 *
 * <p>This replaces a hand-kept list of versions and a hardcoded total. The total was worse than
 * useless: a code round pointed out it would go RED on the next legitimate release, so the next
 * person would have edited the test to restore green — which is the opposite of a ratchet.</p>
 */
test('every version in the baseline still has its note', () => {
  const baseline = JSON.parse(fs.readFileSync(realBaseline, 'utf8')) as Record<string, string[]>;
  const changelog = fs.readFileSync(realChangelog, 'utf8');

  assert.ok((baseline.Server ?? []).length > 0, 'the Server line has a recorded baseline');

  for (const version of baseline.Server) {
    const { code } = guard(`mcp-v${version}`, changelog, JSON.stringify(baseline));

    assert.equal(code, NAMED,
      `the changelog no longer documents Server ${version}, which the baseline says it does. `
      + 'Either the note was deleted — put it back — or it was never there and the baseline is wrong.');
  }
});

test('the baseline names no release that was never tagged', () => {
  // The defect this case exists for was mine: entries were written for `0.26.0` and `0.27.0`, whose
  // versions live in the manifest and whose tags were never pushed. A changelog entry for a release
  // nobody can install is worse than a gap, because it reads as installable.
  const baseline = JSON.parse(fs.readFileSync(realBaseline, 'utf8')) as Record<string, string[]>;
  const tags = spawnSync('git', ['tag', '--list', 'mcp-v*'],
    { cwd: repoRoot, encoding: 'utf8', timeout: 60_000, killSignal: 'SIGKILL' });

  if (tags.status !== 0) {
    assert.fail(`git could not list the tags, so this case proved nothing: ${tags.stderr}`);
  }

  const released = new Set(tags.stdout.split('\n')
    .map((t) => t.trim()).filter(Boolean).map((t) => t.slice('mcp-v'.length)));

  // A checkout with NO tags cannot answer this question, and it does not fail loudly on its own:
  // `git tag --list` exits 0 with empty output, so every recorded version reads as a phantom and the
  // case fails naming the wrong cause. That is not hypothetical — `actions/checkout` fetches no tags
  // by default, which is what this repository's checkouts did when the case was written, so it was
  // green here and would have been red in CI against a perfectly correct tree.
  assert.notEqual(released.size, 0,
    'this checkout carries no tags, so this case cannot tell a phantom version from a real one. '
    + 'In CI that means the job\'s `actions/checkout` needs `fetch-tags: true`; locally it means '
    + 'the clone was made with --no-tags.');

  const phantom = baseline.Server.filter((v) => !released.has(v));

  assert.deepEqual(phantom, [],
    `these versions have a changelog entry and no tag: ${phantom.join(', ')}`);
});

test('the job that runs this suite fetches the tags it needs', () => {
  // The case above is only as good as the checkout it runs in, and a test that can only pass on a
  // developer's machine is worse than no test: it reports green where it was written and red where
  // it is enforced. `actions/checkout` brings no tags unless asked.
  const ci = fs.readFileSync(path.join(repoRoot, '.github', 'workflows', 'ci.yml'), 'utf8')
    .replace(/\r\n/g, '\n');

  const start = ci.indexOf('extension · typecheck · test · package');
  assert.notEqual(start, -1, 'the extension job is still the one that runs npm test');
  const job = ci.slice(start, ci.indexOf('\n  ', ci.indexOf('- name: Package', start)) + 1
    || undefined);

  const checkout = job.indexOf('actions/checkout@');
  assert.notEqual(checkout, -1, 'the job checks the repository out');
  assert.match(job.slice(checkout, checkout + 200), /fetch-tags:\s*true/,
    'without fetch-tags the tag list is empty, `git tag --list` still exits 0, and the '
    + 'phantom-version case fails naming every release a phantom');
});

/**
 * A script nothing invokes guards nothing, and order is half of what this one promises.
 */
test('the mcp release job runs it, after the checkout and before the draft', () => {
  const workflow = fs
    .readFileSync(path.join(repoRoot, '.github', 'workflows', 'release.yml'), 'utf8')
    .replace(/\r\n/g, '\n');

  const start = workflow.indexOf('\n  mcp-draft:');
  assert.notEqual(start, -1, 'the mcp-draft job is still the first thing an mcp-v tag reaches');
  const next = workflow.indexOf('\n  server-draft:', start);
  const job = workflow.slice(start, next === -1 ? undefined : next);

  const checkout = job.indexOf('actions/checkout@');
  const guardStep = job.indexOf('changelog-names-the-release.mjs');
  const draft = job.indexOf('draft-release.sh');

  assert.notEqual(guardStep, -1, 'the job calls the guard at all');
  assert.ok(checkout !== -1 && checkout < guardStep,
    'after the checkout — before it there is no CHANGELOG.md on the runner to read');
  assert.ok(draft !== -1 && guardStep < draft,
    'and before the draft, so a refusal leaves nothing created to clean up');
});

test('the tag reaches the guard as an environment variable, never as shell text', () => {
  // `node script.mjs "${{ github.ref_name }}"` pastes the ref INTO the shell source before bash
  // parses it. A git ref may legitimately contain `"`, `$`, `;` and a backtick — none of them are on
  // git's forbidden list — so a tag like `mcp-v1.0.0";id;#` runs `id` on the runner holding the
  // release credentials. The env indirection is what makes the value data instead of code.
  const workflow = fs
    .readFileSync(path.join(repoRoot, '.github', 'workflows', 'release.yml'), 'utf8')
    .replace(/\r\n/g, '\n');

  const start = workflow.indexOf('\n  mcp-draft:');
  const next = workflow.indexOf('\n  server-draft:', start);
  const job = workflow.slice(start, next === -1 ? undefined : next);
  const at = job.indexOf('changelog-names-the-release');
  const guardStep = job.slice(job.lastIndexOf('- name:', at), at + 400);

  assert.doesNotMatch(guardStep, /changelog-names-the-release\.mjs\s+"?\$\{\{/,
    'the ref must not be interpolated into the command line');
  assert.match(guardStep, /env:[\s\S]*?\$\{\{\s*github\.ref_name\s*\}\}/,
    'it arrives through the step env');
  assert.match(guardStep, /changelog-names-the-release\.mjs\s+"\$[A-Z_]+"/,
    'and the command expands that variable, quoted');
});
