import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * A release that tells nobody what is in it does not ship.
 *
 * <p><b>Measured, which is why this exists:</b> 67 `mcp-v*` tags against 10 `## Server` entries, and
 * thirteen consecutive releases carrying custom roles, `review_document`, the whole consultant and
 * `coai-bugs` with not one word written about any of them. The GitHub release body is the same fixed
 * sentence every time, so the only record was the commit log.</p>
 *
 * <p>The guard runs in `mcp-draft`, the first job an `mcp-v*` tag reaches, and a refusal there costs
 * nothing: no draft exists yet and no asset has been uploaded. <b>If it does refuse, the repair is to
 * delete the tag and push it again</b> once the entry is written — this repository has burned a tag
 * before (`mcp-v0.16.0`) and it is the supported move, because the alternative is a published release
 * whose notes never arrive.</p>
 *
 * <p>These cases SPAWN the script rather than importing it, because the thing that has to be true is
 * its exit code: a guard that prints a complaint and exits 0 stops nothing in a workflow.</p>
 */

const script = path.resolve(__dirname, '..', '..', '..', '.github', 'scripts',
  'changelog-names-the-release.mjs');

/** Run the guard against a changelog we wrote, and report what a workflow would see. */
function guard(tag: string, changelog: string): { code: number; said: string } {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'changelogguard-'));
  try {
    const file = path.join(dir, 'CHANGELOG.md');
    fs.writeFileSync(file, changelog, 'utf8');
    const ran = spawnSync(process.execPath, [script, tag, file], { encoding: 'utf8' });

    return { code: ran.status ?? -1, said: `${ran.stdout ?? ''}${ran.stderr ?? ''}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

const entry = (line: string, version: string) =>
  `# Changelog\n\n## ${line} ${version} — 2026-09-17\n\nSomething a person can read.\n`;

test('a release with no entry does not ship', () => {
  const { code, said } = guard('mcp-v0.29.0', entry('Server', '0.28.0'));

  assert.notEqual(code, 0, 'a guard that complains and exits 0 stops nothing in a workflow');
  assert.match(said, /0\.29\.0/, 'the refusal names the version whose entry is missing');
  assert.match(said, /## Server 0\.29\.0/, 'and the heading somebody has to write');
});

test('a release whose entry is there ships', () => {
  const { code } = guard('mcp-v0.28.0', entry('Server', '0.28.0'));

  assert.equal(code, 0, 'the guard has to be seen to open as well as to close');
});

test('another release line does not satisfy it', () => {
  // Three lines share this file and one number can exist in two of them. `Team server 0.28.0` is a
  // different product from `Server 0.28.0`.
  const { code, said } = guard('mcp-v0.28.0', entry('Team server', '0.28.0'));

  assert.notEqual(code, 0, 'the Team server line is not the MCP line');
  assert.match(said, /0\.28\.0/);
});

test('a version this one is a prefix of does not satisfy it', () => {
  // The trap a naive `includes` walks into: "0.2.0" is a substring of "0.28.0".
  const { code } = guard('mcp-v0.2.0', entry('Server', '0.28.0'));

  assert.notEqual(code, 0, '0.2.0 is not 0.28.0, however the substring reads');
});

test('a heading that only starts with the version does not satisfy it either', () => {
  const { code } = guard('mcp-v0.2.0', entry('Server', '0.2.0.1'));

  assert.notEqual(code, 0, 'a version is a whole number, not a prefix of a longer one');
});

test('the date after the version does not stop it matching', () => {
  // Every heading in this file carries ` — <date>`, so an equality check on the line would refuse
  // every real entry.
  const { code } = guard('mcp-v0.28.0', '# Changelog\n\n## Server 0.28.0 — 2026-09-17\n\nText.\n');

  assert.equal(code, 0, 'the heading format this file actually uses has to pass');
});

test('a tag from another line is not this guard\'s business', () => {
  const { code } = guard('extension-v0.50.0', entry('Server', '0.28.0'));

  assert.equal(code, 0,
    'only the MCP line is guarded today; the extension line has its own gaps and its own decision');
});

test('a malformed argument is refused rather than assumed', () => {
  const { code, said } = guard('not-a-tag', entry('Server', '0.28.0'));

  assert.notEqual(code, 0, 'a tag it cannot read must not be read as "nothing to check"');
  assert.match(said, /not-a-tag/, 'and it says what it was given');
});

/**
 * Every release of this line that HAS a note keeps it.
 *
 * <p>The guard above examines only the tag being released, which a code round pointed out is not the
 * same promise as "this line cannot lose its notes": a 0.29.0 release that deletes the 0.28.0 heading
 * passes it. So the versions are recorded here, and deleting one is a red test.</p>
 *
 * <p>It is also what verifies the backfill itself. Thirteen entries were reconstructed on 2026-09-17
 * from the commit record, and an omitted one would otherwise leave every check green while the
 * record stayed incomplete.</p>
 *
 * <p><b>The list starts at 0.18.15 and that is deliberate.</b> 0.1.0 through 0.18.14 carry 57 tags
 * and nine entries between them; those releases pre-date anyone installing this component on
 * purpose, and reconstructing fifty of them from months-old commits would produce prose nobody can
 * check. The gap is accepted, not forgotten — a new entry for one of them is welcome and this list
 * simply does not require it.</p>
 */
test('no release of this line that has a note loses it', () => {
  // Run the REAL guard against the REAL changelog, once per recorded version — the same path a
  // release takes. Importing the predicate instead was tried and is not available: these tests
  // compile to CommonJS, which cannot load an ESM `.mjs` by file URL, and spawning exercises more
  // of the thing anyway.
  const changelog = fs.readFileSync(path.resolve(__dirname, '..', '..', 'CHANGELOG.md'), 'utf8');

  const recorded = [
    '0.18.15', '0.18.16', '0.18.17', '0.19.0', '0.20.0', '0.21.0', '0.22.0',
    '0.23.0', '0.24.0', '0.25.0', '0.26.0', '0.27.0', '0.27.1', '0.28.0',
  ];

  for (const version of recorded) {
    const { code } = guard(`mcp-v${version}`, changelog);

    assert.equal(code, 0,
      `"## Server ${version}" is gone from the changelog. Thirteen of these were reconstructed from `
      + 'the commit record because nobody wrote them at the time; losing one again is the thing this '
      + 'case exists to make loud.');
  }
});

test('and that list is the one the backfill actually wrote', () => {
  // Teeth for the case above: if the recorded list drifted to versions nobody released, it would
  // pass while proving nothing. Every entry in it must be a real heading, and the count must match
  // the run this repository can see — 0.18.15 upward, with no hole in it.
  const changelog = fs.readFileSync(
    path.resolve(__dirname, '..', '..', 'CHANGELOG.md'), 'utf8');
  const found = [...changelog.matchAll(/^## Server ([0-9.]+) —/gm)].map((m) => m[1]);
  const fromBackfill = found.filter((v) => {
    const [major, minor, patch] = v.split('.').map(Number);

    return major > 0 || minor > 18 || (minor === 18 && patch >= 15);
  });

  assert.equal(fromBackfill.length, 14,
    `expected the thirteen reconstructed entries plus 0.28.0, found ${fromBackfill.length}: `
    + fromBackfill.join(', '));
});

/**
 * A script nothing invokes guards nothing, and order is half of what this one promises.
 */
test('the mcp release job runs it, after the checkout and before the draft', () => {
  const workflow = fs
    .readFileSync(path.resolve(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'),
      'utf8')
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
  assert.match(job, /changelog-names-the-release\.mjs "\$\{\{ github\.ref_name \}\}"/,
    'with the tag passed to it, since the script cannot guess which release is being made');
});
