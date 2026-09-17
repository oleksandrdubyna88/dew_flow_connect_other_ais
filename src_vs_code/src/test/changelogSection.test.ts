import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The notes a release carries are the notes somebody wrote.
 *
 * <p><b>Why this exists:</b> every release body in this repository is a literal in the workflow —
 * `release.yml` hands `draft-release.sh` a fixed sentence, and `draft-release.sh` hands it to
 * `gh release create`. Nothing reads `CHANGELOG.md`. So **65 `mcp-v*` releases carry byte-identical
 * bodies**, and the guard that now refuses a release with no changelog entry was making somebody
 * write prose that the reader of the release never sees.</p>
 *
 * <p>This extractor is the other half: it prints the section a tag's release actually has, and the
 * fixed sentence when there is none. The fallback is load-bearing rather than polite —
 * `extension-v*` and `server-v*` have 51 and 5 releases with no entry and `bugs-v*` has no heading
 * shape at all, and none of those releases may fail over a gap this work is not closing.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'changelog-section.mjs');

const FALLBACK = 'Native AOT builds of the ConnectOtherAIs MCP server.';

interface Ran { code: number; out: string; said: string }

function section(tag: string, changelog: string, extra: string[] = []): Ran {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'changelogsection-'));
  try {
    const file = path.join(dir, 'CHANGELOG.md');
    fs.writeFileSync(file, changelog, 'utf8');
    const ran = spawnSync(process.execPath,
      [script, tag, '--changelog', file, '--fallback', FALLBACK, ...extra],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    assert.equal(ran.error, undefined, `the extractor could not be run: ${ran.error?.message}`);
    assert.notEqual(ran.status, null, `it did not exit on its own (signal ${ran.signal})`);

    return { code: ran.status as number, out: ran.stdout, said: `${ran.stdout}${ran.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

const CHANGELOG = [
  '# Changelog',
  '',
  '## Server 0.28.0 — 2026-09-17',
  '',
  'The newest one. It has **bold**, a `backtick`, a "quote" and a $dollar.',
  '',
  '- a bullet, which argv would read as a flag',
  '',
  '## Extension 0.32.3 · Server 0.18.17 — 2026-09-10',
  '',
  'One release, both halves.',
  '',
  '## 0.31.0 — 2026-09-06 (server 0.18.3)',
  '',
  'The form the early extension releases used.',
  '',
  '## Team server 0.4.0 — 2026-09-12',
  '',
  'A different product that can hold the same number.',
  '',
].join('\n');

test('a release with an entry gets that entry as its body', () => {
  const { code, out } = section('mcp-v0.28.0', CHANGELOG);

  assert.equal(code, 0);
  assert.match(out, /The newest one/, 'the body is the prose somebody wrote');
  assert.doesNotMatch(out, /One release, both halves/,
    'and it stops before the next release, or the body is the whole file');
  assert.doesNotMatch(out, new RegExp(FALLBACK.replace(/\./g, String.raw`\.`)),
    'the fixed sentence is not appended to a real entry');
});

test('the heading travels with it', () => {
  const { out } = section('mcp-v0.28.0', CHANGELOG);

  assert.match(out, /^## Server 0\.28\.0 — 2026-09-17/,
    'a release page that opens with its own version and date reads as a release page');
});

test('everything in the section survives, including what a shell would eat', () => {
  const { out } = section('mcp-v0.28.0', CHANGELOG);

  assert.match(out, /\*\*bold\*\*/);
  assert.match(out, /`backtick`/);
  assert.match(out, /"quote"/);
  assert.match(out, /\$dollar/);
  assert.match(out, /^- a bullet/m,
    'a body beginning a line with a hyphen is why this is written to a file, not an argument');
});

test('a joint heading is used whole, because it describes both halves', () => {
  const { code, out } = section('mcp-v0.18.17', CHANGELOG);

  assert.equal(code, 0);
  assert.match(out, /## Extension 0\.32\.3 · Server 0\.18\.17/);
  assert.match(out, /One release, both halves/);
});

test('the parenthesised form is found too', () => {
  const { code, out } = section('mcp-v0.18.3', CHANGELOG);

  assert.equal(code, 0);
  assert.match(out, /The form the early extension releases used/);
});

test('another line with the same number is not this release', () => {
  // `## Team server 0.4.0` must not answer `mcp-v0.4.0`. One number, two products.
  const { code, out } = section('mcp-v0.4.0', CHANGELOG);

  assert.equal(code, 0, 'a missing entry is not an error — it is the fallback');
  assert.equal(out.trim(), FALLBACK);
  assert.doesNotMatch(out, /different product/);
});

test('a release with no entry falls back to the sentence', () => {
  const { code, out } = section('mcp-v0.99.0', CHANGELOG);

  assert.equal(code, 0,
    '51 extension and 5 server releases have no entry; none of them may fail over it');
  assert.equal(out.trim(), FALLBACK);
});

test('a line that has no heading word at all falls back', () => {
  // `bugs-v*` is a real release line and this changelog documents it nowhere — measured: zero
  // headings mention coai-bugs. A line with no `word` must not be a crash.
  const { code, out } = section('bugs-v0.1.0', CHANGELOG);

  assert.equal(code, 0);
  assert.equal(out.trim(), FALLBACK);
});

test('a tag from no known line is refused rather than guessed', () => {
  const { code, said } = section('wat-v1.0.0', CHANGELOG);

  assert.equal(code, 2, 'a guard that cannot tell which line this is must not invent a body');
  assert.match(said, /wat-v1\.0\.0/);
});

test('the section ends at the next RELEASE heading, not at any "## "', () => {
  // A note may legitimately contain its own `## ` subheading. Cutting there would truncate the body
  // at the first subheading somebody writes.
  const withSubheading = [
    '# Changelog',
    '',
    '## Server 0.28.0 — 2026-09-17',
    '',
    'Opening line.',
    '',
    '## Breaking changes',
    '',
    'Still part of 0.28.0.',
    '',
    '## Server 0.27.1 — 2026-09-16',
    '',
    'The previous one.',
    '',
  ].join('\n');

  const { out } = section('mcp-v0.28.0', withSubheading);

  assert.match(out, /Still part of 0\.28\.0/, 'an internal subheading does not end the section');
  assert.match(out, /## Breaking changes/, 'and it is kept, because somebody wrote it');
  assert.doesNotMatch(out, /The previous one/, 'the next RELEASE does end it');
});

test('--out writes the body to a file instead of stdout', () => {
  // How the workflow uses it: multiline markdown cannot travel as a shell argument, so it is
  // written and the PATH is what gets passed.
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'changelogsection-'));
  try {
    const changelog = path.join(dir, 'CHANGELOG.md');
    const out = path.join(dir, 'notes.md');
    fs.writeFileSync(changelog, CHANGELOG, 'utf8');

    const ran = spawnSync(process.execPath,
      [script, 'mcp-v0.28.0', '--changelog', changelog, '--fallback', FALLBACK, '--out', out],
      { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

    assert.equal(ran.status, 0, ran.stderr);
    assert.ok(fs.existsSync(out), 'the file the workflow will hand to gh exists');
    assert.match(fs.readFileSync(out, 'utf8'), /The newest one/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('it reads the release lines from the guard, not from a second copy of them', () => {
  // A second implementation of "which heading word belongs to this tag" is a defect from the moment
  // it compiles: the two drift and nothing notices. The registry lives in the guard and is reported
  // by `--lines`; every line must carry the `guarded` flag, and the ones this changelog documents
  // must carry their heading word.
  const ran = spawnSync(process.execPath,
    [path.join(repoRoot, '.github', 'scripts', 'changelog-names-the-release.mjs'), '--lines'],
    { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });

  assert.equal(ran.status, 0, ran.stderr);
  const lines = JSON.parse(ran.stdout) as { prefix: string; word?: string; guarded: boolean }[];
  const wordOf = new Map(lines.map((l) => [l.prefix, l.word]));

  assert.equal(wordOf.get('mcp-v'), 'Server');
  assert.equal(wordOf.get('extension-v'), 'Extension');
  assert.equal(wordOf.get('server-v'), 'Team server',
    'measured from the file: one `## Team server` heading exists');
  assert.equal(wordOf.get('bugs-v'), undefined,
    'measured from the file: zero headings mention coai-bugs, so this line has no word');

  assert.deepEqual(lines.filter((l) => l.guarded).map((l) => l.prefix), ['mcp-v'],
    'giving a line a heading word must NOT start guarding it — the two fields are orthogonal');

  const source = fs.readFileSync(script, 'utf8');
  assert.match(source, /from '\.\/changelog-names-the-release\.mjs'/,
    'the extractor imports the registry rather than declaring its own');
});
