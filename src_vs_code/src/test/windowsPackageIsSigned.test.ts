import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';

/**
 * The shape of the Windows signing in `release.yml`, held as a reading of the YAML.
 *
 * <p>A reading is the weakest kind of test, so each assertion names the DEFECT it stands against.
 * The signing itself is not here and cannot be: it runs on a GitHub runner against Azure, in the
 * shared workflow `dew_flow_release_signing/.github/workflows/sign-windows.yml`, and is exercised by
 * a dispatch trial (`target: mcp`) on the pull request branch. What this file holds is the part this
 * repository owns: that an unsigned Windows package is never attached to a release, that the
 * signing job is called the way the shared contract says, that the product check runs on the FINAL
 * zip before it is uploaded, and that the release is published only after all of it.</p>
 */

const workflow = fs
  .readFileSync(path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'), 'utf8')
  .split('\r\n')
  .join('\n');

/** One job's block: from its own key to the next job key. */
function jobBlock(job: string): string {
  const start = workflow.indexOf(`\n  ${job}:\n`);
  assert.notEqual(start, -1, `the workflow has no ${job} job`);
  const rest = workflow.slice(start + 1);
  const next = rest.search(/\n {2}[a-z][a-z0-9-]*:\n/);
  return next === -1 ? rest : rest.slice(0, next);
}

/** Every job key under `jobs:`. */
function jobNames(): string[] {
  const jobs = workflow.slice(workflow.indexOf('\njobs:\n'));
  return [...jobs.matchAll(/^ {2}([a-z][a-z0-9-]*):$/gm)].map((m) => m[1]!);
}

/** The block without its comment lines, so prose that explains a thing cannot satisfy a test of the thing. */
const withoutComments = (text: string) =>
  text
    .split('\n')
    .filter((line) => !/^\s*#/.test(line))
    .join('\n');

/** A job's steps, each as its own text; the step list is the part of the block after `steps:`. */
function stepsOf(job: string): string[] {
  const block = jobBlock(job);
  const after = block.slice(block.indexOf('\n    steps:\n') + '\n    steps:\n'.length);
  return after.split(/\n(?= {6}- )/);
}

/** The step with this name. */
function step(job: string, name: string): string {
  const found = stepsOf(job).find((s) => new RegExp(`^ {6}- name: ${name}\\s*$`, 'm').test(s));
  assert.ok(found, `${job} has no step named "${name}"`);
  return found;
}

/** A job's `if:`, folded or not, as one line. */
function condition(job: string): string {
  const block = jobBlock(job);
  const folded = /\n {4}if: >-\n((?: {6}.*\n)+)/.exec(block);
  if (folded) {
    return folded[1]!.split('\n').map((l) => l.trim()).join(' ').trim();
  }
  const single = /\n {4}if: (.+)\n/.exec(block);
  assert.ok(single, `${job} states a condition`);
  return single[1]!.trim();
}

/** What the job lists under `needs:`, inline or as a list. */
function needsOf(job: string): string[] {
  const inline = /\n {4}needs: \[([^\]]*)\]/.exec(jobBlock(job));
  if (inline) {
    return inline[1]!.split(',').map((n) => n.trim()).filter((n) => n.length > 0);
  }
  const single = /\n {4}needs: ([a-z][a-z0-9-]*)\s*\n/.exec(jobBlock(job));
  assert.ok(single, `${job} declares what it needs`);
  return [single[1]!];
}

/** The `rid:` values of a job's matrix. */
const ridsOf = (job: string) =>
  [...jobBlock(job).matchAll(/^\s+- rid:\s*(\S+)\s*$/gm)].map((m) => m[1]!);

const windowsRids = () => ridsOf('mcp-binaries').filter((rid) => rid.startsWith('win-'));

/** Every `run:` body of a job, so a test can ask whether an expression reached a shell. */
function runBodies(job: string): string[] {
  const bodies: string[] = [];
  const lines = withoutComments(jobBlock(job)).split('\n');
  for (let i = 0; i < lines.length; i++) {
    const match = /^(\s*)(?:- )?run:\s*(.*)$/.exec(lines[i]!);
    if (!match) {
      continue;
    }
    const indent = match[1]!.length;
    let body = match[2]!.replace(/^[|>][-+]?$/, '');
    for (let j = i + 1; j < lines.length; j++) {
      const next = lines[j]!;
      if (next.trim() !== '' && next.length - next.trimStart().length <= indent) {
        break;
      }
      body += `\n${next}`;
    }
    bodies.push(body);
  }
  return bodies;
}

test('a Windows leg uploads its package UNSIGNED, under a name that no mcp-* pattern matches', () => {
  // The defect: the leg zips and attaches as it always did, and an unsigned `coai-mcp.exe` reaches a
  // release that Smart App Control then blocks. The signed artifact is `mcp-<rid>`; the leg's own is
  // `unsigned-mcp-<rid>`, so a download of `mcp-win-*` can never pick the unsigned one up.
  const upload = step('mcp-binaries', 'Upload the unsigned Windows package');

  assert.match(upload, /uses: actions\/upload-artifact@[0-9a-f]{40} # v\d/, 'a pinned upload');
  assert.match(upload, /\n\s+name: unsigned-mcp-\$\{\{ matrix\.rid \}\}\s*\n/, 'named unsigned-mcp-<rid>');
  assert.match(upload, /\n\s+retention-days: 7\s*\n/, 'kept a week, not the default ninety days');
  assert.match(upload, /\n\s+if-no-files-found: error\s*\n/, 'an empty upload is red');
  assert.match(upload, /if: startsWith\(matrix\.rid, 'win-'\)/, 'and only the Windows legs make one');
});

test('a Windows leg builds no zip and attaches nothing', () => {
  // The defect: a zip made BEFORE signing is the artifact the next step would attach.
  const attach = step('mcp-binaries', 'Attach to the draft release');
  const checksum = step('mcp-binaries', 'Checksum');
  const pack = step('mcp-binaries', 'Package');

  assert.match(attach, /if: .*!startsWith\(matrix\.rid, 'win-'\)/, 'the attach is for the other RIDs only');
  assert.match(attach, /startsWith\(github\.ref, 'refs\/tags\/mcp-v'\)/, 'and still tag-only');
  assert.match(checksum, /if: \$\{\{ !startsWith\(matrix\.rid, 'win-'\) \}\}/, 'the checksum has nothing to sum on Windows');
  assert.doesNotMatch(withoutComments(pack), /7z a/, 'no zip is made before signing');
});

test('the Windows package folder is listed, with its prefix, by BOTH carry checks', () => {
  // The defect: the carry checks used to read the zip's listing; with no zip they would read nothing
  // and the first one would fail every Windows leg — or, worse, be skipped. The folder's listing has
  // the same `NAME/file` shape a tar listing has, so the existing patterns apply unchanged.
  const pack = withoutComments(step('mcp-binaries', 'Package'));

  assert.match(pack, /LISTING=\$\(cd "\$PARENT" && find "\$NAME" -type f\)/, 'the folder, from its parent');
  assert.match(pack, /grep -Eq "\(\^\|\[\[:space:\]\/\]\)\$NAME\/\[\^\/\]\*e_sqlite3"/, 'the library check is kept');
  assert.match(pack, /kept-grammars\.txt/, 'and so is the grammar count');
});

test('mcp-sign calls the shared workflow, pinned to a commit, with the contract it expects', () => {
  const block = jobBlock('mcp-sign');
  const code = withoutComments(block);

  assert.match(
    code,
    /\n {4}uses: oleksandrdubyna88\/dew_flow_release_signing\/\.github\/workflows\/sign-windows\.yml@[0-9a-f]{40}\s/,
    'a 40-hex SHA, never a branch or a tag that can move',
  );
  // The artifact names are DERIVED from the matrix: a Windows RID added to it and not here would
  // ship an unsigned package, and a list retyped in two places is how that happens.
  const artifacts = /\n {6}artifacts: '(\[[^']*\])'/.exec(code);
  assert.ok(artifacts, 'it names its artifacts');
  assert.deepEqual(
    (JSON.parse(artifacts[1]!) as string[]).sort(),
    windowsRids().map((rid) => `mcp-${rid}`).sort(),
    'one per Windows RID of the build matrix, without the unsigned- prefix',
  );
  assert.match(code, /\n {6}own-files: '\["coai-mcp\.exe"\]'/, 'the one binary this repository builds');
  assert.match(
    code,
    /\n {6}required: \$\{\{ startsWith\(github\.ref, 'refs\/tags\/'\) \}\}/,
    'a tag that is not signed is red; a trial may fall back',
  );
});

test('the libraries mcp-sign declares are the ones the Windows package ships', () => {
  // The defect: the shared workflow signs only what is DECLARED and turns an undeclared unsigned PE
  // red. A glob that matches nothing the package ships leaves e_sqlite3.dll unsigned and red; one
  // broader than the package signs files nobody meant to. The names are the ones the publish
  // produces: e_sqlite3.dll (SQLitePCLRaw's bundle) and tree-sitter.dll plus tree-sitter-<lang>.dll
  // (the kept grammars, `shared/kept-grammars.txt`).
  const extra = /\n {6}extra-files: '(\[[^']*\])'/.exec(withoutComments(jobBlock('mcp-sign')));
  assert.ok(extra, 'it declares its libraries');
  const globs = JSON.parse(extra[1]!) as string[];
  const toRegex = (glob: string) => new RegExp(`^${glob.replace(/[.]/g, '\\.').replace(/\*/g, '.*')}$`);

  const kept = fs
    .readFileSync(path.join(__dirname, '..', '..', '..', 'shared', 'kept-grammars.txt'), 'utf8')
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l !== '' && !l.startsWith('#'));
  const shipped = ['e_sqlite3.dll', ...kept.map((name) => `${name}.dll`)];

  for (const file of shipped) {
    assert.ok(globs.some((g) => toRegex(g).test(file)), `${file} ships and no glob covers it`);
  }
  for (const glob of globs) {
    assert.ok(shipped.some((file) => toRegex(glob).test(file)), `the glob ${glob} matches nothing that ships`);
  }
  assert.ok(!globs.some((g) => toRegex(g).test('coai-mcp.exe')), 'the own file is declared once, as own');
});

test('mcp-sign runs exactly when the build succeeded, and may hold the federation permission', () => {
  const block = jobBlock('mcp-sign');

  assert.deepEqual(needsOf('mcp-sign'), ['mcp-binaries']);
  // The defects this stands against: copying mcp-binaries' own `if:` word for word (it reads
  // `needs.mcp-draft.result`, which mcp-sign does not need, so on a tag it is null == 'success'); and
  // the bare `needs.mcp-binaries.result == 'success'`, whose implicit success() is skipped on a
  // dispatch by the skipped draft. What the job wants is the result of what it NEEDS, behind a
  // status function that does not look at the rest — `!cancelled()`, so a cancel still stops it.
  // And a status function is REQUIRED. A job `if:` with none gets an implicit `success()`, and that one
  // looks at EVERY upstream job, not only the `needs` — including `mcp-draft`, which a dispatch skips
  // (actions/runner#491). So the bare comparison is skipped on a dispatch and V1 can never run.
  assert.equal(condition('mcp-sign'), "${{ !cancelled() && needs.mcp-binaries.result == 'success' }}");
  assert.doesNotMatch(condition('mcp-sign'), /mcp-draft/, 'a job not in `needs` is not in the context');
  assert.match(
    block,
    /\n {4}permissions:\n {6}contents: read\n {6}id-token: write\n(?! {6}\S)/,
    'exactly contents: read and id-token: write, and nothing else',
  );
});

test('no other job can mint a federated token', () => {
  // The defect: a job that may write `id-token` can ask Azure for a signing token if the trust names
  // its environment. The trust is the caller's environment; the permission must stay in one job.
  const topLevel = withoutComments(workflow.slice(0, workflow.indexOf('\njobs:\n')));
  assert.doesNotMatch(topLevel, /id-token/, 'not at the top of the file, where every job would inherit it');

  const holders = jobNames().filter((job) => /id-token:/.test(withoutComments(jobBlock(job))));
  assert.deepEqual(holders, ['mcp-sign']);
});

test('mcp-sign-attach checks the FINAL zip with the product check, then uploads it on a tag only', () => {
  const block = jobBlock('mcp-sign-attach');
  const code = withoutComments(block);

  assert.deepEqual(needsOf('mcp-sign-attach'), ['mcp-sign']);
  assert.equal(condition('mcp-sign-attach'), "${{ !cancelled() && needs.mcp-sign.result == 'success' }}");
  assert.match(code, /\n {4}permissions:\n {6}contents: write\n(?! {6}\S)/, 'it uploads, and only that');
  assert.doesNotMatch(code, /id-token/);

  const download = step('mcp-sign-attach', 'Download the signed packages');
  assert.match(download, /uses: actions\/download-artifact@[0-9a-f]{40} # v\d/, 'a pinned download');
  assert.match(download, /\n\s+pattern: mcp-win-\*\s*\n/, 'the signed ones, never unsigned-mcp-*');

  const check = withoutComments(step('mcp-sign-attach', 'The product check on each final zip'));
  const attach = withoutComments(step('mcp-sign-attach', 'Attach to the draft release'));
  assert.match(check, /archive-carries\.sh "\$ZIP" coai-mcp\.exe '\*e_sqlite3\*'/, 'the binary and the SQLite library');
  assert.doesNotMatch(check, /^\s+if:/m, 'it runs on a dispatch trial too, which is what V1 exercises');
  assert.match(attach, /if: startsWith\(github\.ref, 'refs\/tags\/'\)/, 'only a tag uploads');
  assert.match(attach, /gh release upload "\$GITHUB_REF_NAME" "\$ZIP" --clobber/, 'the zip');
  assert.match(attach, /gh release upload "\$GITHUB_REF_NAME" "\$ZIP\.sha256" --clobber/, 'and its checksum');

  const names = stepsOf('mcp-sign-attach').map((s) => /- name: (.*)/.exec(s)?.[1]);
  assert.ok(
    names.indexOf('The product check on each final zip') < names.indexOf('Attach to the draft release'),
    'the check runs BEFORE the upload',
  );
});

test('the new jobs hand no expression to a shell', () => {
  // The rule for every new run block here: a `${{ }}` inside `run:` is text a ref name or an input
  // can shape into a command. Values arrive through `env:`.
  for (const job of ['mcp-sign', 'mcp-sign-attach']) {
    for (const body of runBodies(job)) {
      assert.doesNotMatch(body, /\$\{\{/, `${job} has an expression inside a run block`);
    }
  }
});

test('the release is published only after the signed packages are attached', () => {
  // The defect: a completeness job that needs only the build would publish a draft that holds no
  // Windows package, or hold the signed one back until after the publish.
  assert.deepEqual(needsOf('mcp-release-complete').sort(), ['mcp-binaries', 'mcp-sign-attach']);
  assert.match(condition('mcp-release-complete'), /startsWith\(github\.ref, 'refs\/tags\/mcp-v'\)/);
});
