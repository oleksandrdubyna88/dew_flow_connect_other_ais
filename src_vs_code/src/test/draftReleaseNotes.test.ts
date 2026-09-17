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
 * <p>It needs <b>bash</b>, not merely a POSIX shell, and getting that wrong is what CI caught here:
 * `draft-release.sh` opens with `set -euo pipefail`, and `pipefail` is a bash extension that POSIX
 * `sh` does not have. Running it with `sh` passed on a Windows checkout — where Git Bash's `sh` IS
 * bash — and on `ubuntu-latest`, where `sh` is dash, every one of these cases got exit **2** from
 * `Illegal option -o pipefail` before the script did anything. Green where written, red where
 * enforced, again.</p>
 *
 * <p>A checkout without bash skips rather than fails — the shape of
 * `StageRulesTests.RequireTheMount`, and for its reason. CI always has it.</p>
 */

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const script = path.join(repoRoot, '.github', 'scripts', 'draft-release.sh');

const hasBash = spawnSync('bash', ['-c', 'exit 0'], { encoding: 'utf8' }).status === 0;
const needsShell = hasBash ? false : 'no bash on this checkout';

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

    const ran = spawnSync('bash', [script, 'mcp-v0.28.0', 'coai-mcp 0.28.0', notesFile], {
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

const workflowText = () => fs
  .readFileSync(path.join(repoRoot, '.github', 'workflows', 'release.yml'), 'utf8')
  .replace(/\r\n/g, '\n');

/**
 * The jobs that draft a release, derived from the workflow rather than listed here.
 *
 * <p>A test that repeats a list the code also holds will not notice the fourth entry — and this
 * repository grew from two release lines to four.</p>
 */
function draftingJobs(): { name: string; block: string }[] {
  const workflow = workflowText();
  const jobs: { name: string; block: string }[] = [];

  for (const match of workflow.matchAll(/^ {2}([a-z0-9-]+):$/gm)) {
    const start = match.index as number;
    const nextAt = workflow.slice(start + 1).search(/^ {2}[a-z0-9-]+:$/m);
    const block = nextAt === -1 ? workflow.slice(start) : workflow.slice(start, start + 1 + nextAt);
    if (block.includes('draft-release.sh')) {
      jobs.push({ name: match[1], block });
    }
  }

  return jobs;
}

test('every drafting job is found, and there are still the three we know of', () => {
  const found = draftingJobs().map((j) => j.name);

  assert.deepEqual(found.sort(), ['bugs-draft', 'mcp-draft', 'server-draft'],
    'if a fourth release line appears, the cases below should start covering it automatically');
});

test('a fallback sentence never carries shell syntax the env block will not expand', () => {
  // The defect this case exists for was mine. The Team server's fallback is
  // `…/coai-server:${GITHUB_REF_NAME#server-v}` — it worked while it sat inside `run:`, where bash
  // expands it. Moved into `env:`, GitHub sets the value VERBATIM and bash does not re-expand
  // parameter syntax found inside a variable's value, so a Team server release with no changelog
  // entry would publish the literal characters `${GITHUB_REF_NAME#server-v}` to everybody.
  for (const { name, block } of draftingJobs()) {
    const env = block.match(/RELEASE_NOTES_FALLBACK:.*/g) ?? [];

    for (const line of env) {
      assert.doesNotMatch(line, /\$\{[A-Za-z_]/,
        `${name}: a shell expansion in an env VALUE is never expanded — compute it in the run block`);
    }
  }
});

test('the three draft jobs write the notes and pass the file', () => {
  // A script nothing invokes changes nothing. Each job that drafts a release must run the extractor
  // and hand `draft-release.sh` the path it wrote, not a sentence.
  const workflow = workflowText();

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

test('the extractor and the drafter work as one chain, the way the job runs them',
  { skip: needsShell }, () => {
    // Each half is proved on its own above. This is the seam: the file one writes is the file the
    // other reads, and what reaches `gh` is the prose from CHANGELOG.md rather than a sentence.
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'draftchain-'));
    try {
      const changelog = path.join(dir, 'CHANGELOG.md');
      const notes = path.join(dir, 'release-notes.md');
      const log = path.join(dir, 'calls.txt');
      fs.writeFileSync(changelog,
        '# Changelog\n\n## Server 0.28.0 — 2026-09-17\n\n'
        + '- a bullet that argv would read as a flag\n\nAnd a "quoted" $word.\n', 'utf8');

      const extracted = spawnSync(process.execPath, [
        path.join(repoRoot, '.github', 'scripts', 'changelog-section.mjs'), 'mcp-v0.28.0',
        '--changelog', changelog, '--fallback', 'the fixed sentence', '--out', notes,
      ], { encoding: 'utf8', timeout: 30_000, killSignal: 'SIGKILL' });
      assert.equal(extracted.status, 0, extracted.stderr);

      // A `gh` that records the notes it was actually handed, by reading the file it was given.
      fs.writeFileSync(path.join(dir, 'gh'), [
        '#!/bin/sh',
        `echo "$*" >> "${log.replace(/\\/g, '/')}"`,
        'if [ "$2" = "view" ]; then exit 1; fi',
        'while [ $# -gt 0 ]; do',
        '  if [ "$1" = "--notes-file" ]; then',
        `    cat "$2" >> "${log.replace(/\\/g, '/')}"`,
        '  fi',
        '  shift',
        'done',
        'exit 0',
        '',
      ].join('\n'), { encoding: 'utf8', mode: 0o755 });

      const drafted = spawnSync('bash', [script, 'mcp-v0.28.0', 'coai-mcp 0.28.0', notes], {
        encoding: 'utf8',
        timeout: 60_000,
        killSignal: 'SIGKILL',
        env: { ...process.env, PATH: `${dir}${path.delimiter}${process.env.PATH ?? ''}` },
      });
      assert.equal(drafted.status, 0, `${drafted.stdout}${drafted.stderr}`);

      const seen = fs.readFileSync(log, 'utf8');
      assert.match(seen, /## Server 0\.28\.0/, 'gh received the changelog section itself');
      assert.match(seen, /^- a bullet that argv would read as a flag$/m,
        'intact, including the line a shell argument would have eaten');
      assert.match(seen, /"quoted" \$word/);
      assert.doesNotMatch(seen, /the fixed sentence/, 'and not the fallback, since an entry exists');
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

test('a notes path that is a directory is refused, not read', { skip: needsShell }, () => {
  // `[ -r ]` is true for a directory and for some broken symlinks. What the script needs is a FILE.
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'draftrelease-'));
  try {
    const ran = spawnSync('bash', [script, 'mcp-v0.28.0', 'title', dir],
      { encoding: 'utf8', timeout: 60_000, killSignal: 'SIGKILL' });

    assert.notEqual(ran.status, 0, 'a directory is not a notes file');
    assert.match(`${ran.stdout}${ran.stderr}`, /not (a readable file|readable)/);
    assert.match(ran.stderr, /notes file/,
      'and the complaint goes to stderr, where a CI log shows it as an error');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('the extension release line is deliberately NOT wired, and that is recorded', () => {
  // Raised on the second code round: an `extension-v*` tag with a changelog entry still publishes
  // the old fixed sentence. True, and scoped out on purpose — but "scoped out" and "forgotten" look
  // identical in a workflow, so this case is what makes it a decision somebody can find.
  //
  // It also pins what was found while checking: the `extension` job carries its OWN inline
  // create-or-reuse block, a FOURTH copy of the policy `draft-release.sh` exists to keep in one
  // place — and it has already drifted, because the script now refreshes a reused draft's notes and
  // the copy does not. That is the follow-up, written into the plan's open tail.
  const workflow = workflowText();
  const start = workflow.indexOf('\n  extension:') + 1;
  assert.notEqual(start, 0, 'the extension release job still exists');
  // From INSIDE the heading line, or the search finds this job's own heading at offset zero.
  const nextAt = workflow.slice(start + 2).search(/^ {2}[a-z0-9-]+:$/m);
  const job = workflow.slice(start, nextAt === -1 ? undefined : start + 2 + nextAt);

  assert.ok(!job.includes('changelog-section.mjs'),
    'if somebody wires it, this case should be the thing that makes them update the plan too');
  assert.match(job, /gh release create/,
    'it drafts its own release inline rather than through draft-release.sh');

  const plan = fs.readFileSync(
    path.join(repoRoot, 'research', 'PLAN_a_release_says_what_it_shipped.md'), 'utf8');
  assert.match(plan, /extension release line/i,
    'the plan records which line is not covered');
  assert.match(plan, /inline|fourth copy|own create-or-reuse/i,
    'and that the job holds a duplicate of the create-or-reuse policy, which has already drifted');
});

test('no run block carries a literal backslash-n where a line break belongs', () => {
  // This caught a real break in this very change, three times: a `\` line continuation written
  // through a shell heredoc arrives as the two characters `\n`, which bash reads as an argument
  // rather than as "the command continues". YAML still parses, every other assertion here still
  // passes, and the release fails on the day. Cheap to forbid outright.
  const both = ['release.yml', 'ci.yml'].map((name) => ({
    name,
    text: fs.readFileSync(path.join(repoRoot, '.github', 'workflows', name), 'utf8'),
  }));

  for (const { name, text } of both) {
    for (const [at, line] of text.replace(/\r\n/g, '\n').split('\n').entries()) {
      assert.doesNotMatch(line, /\n {2,}/,
        `${name}:${at + 1} has a literal "\n" where a line continuation belongs: ${line.trim()}`);
    }
  }
});

test('the script says which shell it wants, and these cases use that one', () => {
  // The cases above run the script with `bash`. That is only right while the script ASKS for bash —
  // and it does, in its shebang, because `set -euo pipefail` needs it. If somebody changes the
  // shebang to `/bin/sh` while keeping `pipefail`, the tests would keep passing under bash while
  // every release failed under dash, which is the exact split this file already paid for once.
  const first = fs.readFileSync(script, 'utf8').split('\n')[0];

  assert.match(first, /^#!.*\bbash\b/,
    `the tests invoke bash; the script's shebang says ${first}`);
  assert.match(fs.readFileSync(script, 'utf8'), /set -euo pipefail/,
    'and pipefail is the reason it has to be bash rather than any POSIX shell');
});
