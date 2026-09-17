import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * `draft-release.sh`, run for real against a `gh` that records what it was asked to do.
 *
 * <p>This is shell that only runs on a release day, which is the class of code
 * `TheArchiveCheckTests` exists for: the archive check lived inline in `release.yml`, nothing
 * executed it until the day it mattered, and it was wrong. So this runs the REAL script with a stub
 * `gh` first on PATH.</p>
 *
 * <p><b>Two things are being proved, and the second was found by a gate reviewer.</b> First, that a
 * multiline body reaches `gh` through a FILE — a release note is markdown that can begin a line with
 * `-`, and as a shell argument it is word-split, truncated at the first newline, or read as a flag.
 * Second, that a RE-RUN over an existing draft updates that draft's notes. `draft-release.sh`
 * deliberately reuses a draft it already made; without this, a first run that drafted with the
 * fallback and then failed would leave the fallback on the release for ever, however many times it
 * was retried.</p>
 *
 * <p>It needs a POSIX shell. CI runs on `ubuntu-latest` where that is always true, so a missing `sh`
 * fails there and skips on a Windows checkout without one — the shape of
 * `StageRulesTests.RequireTheMount`, and for its reason.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'draft-release.sh');

const hasShell = spawnSync('sh', ['-c', 'exit 0'], { encoding: 'utf8' }).status === 0;
const needsShell = hasShell ? false : 'no POSIX shell on this checkout';

/** What the stub `gh` was asked to do, one call per line. */
interface Run { code: number; calls: string[]; said: string }

/**
 * Run the real script with a `gh` that answers `state` and records every invocation.
 *
 * @param state what `gh release view` should report: a draft, a published release, or nothing.
 */
function draft(state: 'draft' | 'published' | 'absent', notes: string): Run {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'draftrelease-'));
  try {
    const log = path.join(dir, 'calls.txt');
    const notesFile = path.join(dir, 'notes.md');
    fs.writeFileSync(notesFile, notes, 'utf8');

    // The stub. `$*` is recorded verbatim so an assertion can see exactly which flags were used.
    fs.writeFileSync(path.join(dir, 'gh'), [
      '#!/bin/sh',
      `echo "$*" >> "${log.replace(/\\/g, '/')}"`,
      'if [ "$2" = "view" ]; then',
      `  case "${state}" in`,
      '    draft) echo true ;;',
      '    published) echo false ;;',
      '    *) exit 1 ;;',
      '  esac',
      '  exit 0',
      'fi',
      'exit 0',
      '',
    ].join('\n'), { encoding: 'utf8', mode: 0o755 });

    const ran = spawnSync('sh', [script, 'mcp-v0.28.0', 'coai-mcp 0.28.0', notesFile], {
      encoding: 'utf8',
      timeout: 60_000,
      killSignal: 'SIGKILL',
      env: { ...process.env, PATH: `${dir}${path.delimiter}${process.env.PATH ?? ''}` },
    });

    const calls = fs.existsSync(log)
      ? fs.readFileSync(log, 'utf8').split('\n').filter(Boolean)
      : [];

    return { code: ran.status ?? -1, calls, said: `${ran.stdout}${ran.stderr}` };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

const BODY = '## Server 0.28.0 — 2026-09-17\n\n- a bullet, which argv would read as a flag\n';

test('a new release is drafted with the notes FILE, never a notes string',
  { skip: needsShell }, () => {
    const { code, calls } = draft('absent', BODY);

    assert.equal(code, 0, calls.join(' | '));
    const create = calls.find((c) => c.startsWith('release create'));
    assert.ok(create, `nothing created the release: ${calls.join(' | ')}`);
    assert.match(create, /--notes-file/,
      'multiline markdown cannot survive as a shell argument');
    assert.doesNotMatch(create, /--notes\s/,
      'the string form is what truncated the body at the first newline');
  });

test('a re-run over an existing draft UPDATES its notes', { skip: needsShell }, () => {
  // The reviewer's case: run one creates the draft with the fallback and then fails; run two reuses
  // the draft. Without an update the published release keeps the wrong body for ever.
  const { code, calls } = draft('draft', BODY);

  assert.equal(code, 0, calls.join(' | '));
  assert.ok(!calls.some((c) => c.startsWith('release create')),
    'it must still not create a second draft — that was the defect this script was written for');
  const edit = calls.find((c) => c.startsWith('release edit'));
  assert.ok(edit, `a reused draft kept whatever body it was born with: ${calls.join(' | ')}`);
  assert.match(edit, /--notes-file/, 'and the update carries the body the same way');
});

test('a PUBLISHED release is still refused, and nothing is written to it',
  { skip: needsShell }, () => {
    const { code, calls, said } = draft('published', BODY);

    assert.equal(code, 1, 'uploading into a release clients can already see is the whole hazard');
    assert.ok(!calls.some((c) => c.startsWith('release create') || c.startsWith('release edit')),
      `it must not touch a published release: ${calls.join(' | ')}`);
    assert.match(said, /PUBLISHED/);
  });

test('the three draft jobs write the notes and pass the file', () => {
  // A script nothing invokes changes nothing. Each job that drafts a release must run the extractor
  // and hand `draft-release.sh` the path it wrote, not a sentence.
  const workflow = fs
    .readFileSync(path.join(repoRoot, '.github', 'workflows', 'release.yml'), 'utf8')
    .replace(/\r\n/g, '\n');

  for (const job of ['mcp-draft', 'server-draft', 'bugs-draft']) {
    const start = workflow.indexOf(`\n  ${job}:`);
    assert.notEqual(start, -1, `${job} still exists`);
    const next = workflow.indexOf('\n  ', workflow.indexOf('draft-release.sh', start) + 1);
    const block = workflow.slice(start, next === -1 ? undefined : next);

    assert.match(block, /changelog-section\.mjs/,
      `${job} does not extract its notes, so its releases still carry one fixed sentence`);
    assert.match(block, /--out\s+"?\$?\{?[A-Za-z_]/,
      `${job} must write the notes to a file — markdown does not survive a shell argument`);
    assert.match(block, /draft-release\.sh[\s\S]{0,200}\$/,
      `${job} must pass that file's path to draft-release.sh`);
  }
});

test('every job that runs node says which node it wants', () => {
  // Raised as Blocking on the plan round: these jobs call `node` with no `setup-node` step, so they
  // depend on whatever the runner image happens to ship. That is an undeclared dependency on a job
  // whose failure mode is a release with no notes.
  const workflow = fs
    .readFileSync(path.join(repoRoot, '.github', 'workflows', 'release.yml'), 'utf8')
    .replace(/\r\n/g, '\n');

  for (const job of ['mcp-draft', 'server-draft', 'bugs-draft']) {
    const start = workflow.indexOf(`\n  ${job}:`);
    const next = workflow.indexOf('\n  ', workflow.indexOf('draft-release.sh', start) + 1);
    const block = workflow.slice(start, next === -1 ? undefined : next);

    assert.match(block, /actions\/setup-node/,
      `${job} runs node and never says which one`);
  }
});
