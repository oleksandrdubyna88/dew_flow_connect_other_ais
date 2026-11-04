import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { test } from 'node:test';

/**
 * The shape of the Windows signing in `release.yml`, held as a reading of the YAML.
 *
 * <p>A reading is the weakest kind of test, so each assertion names the DEFECT it stands against.
 * The signing itself is not here and cannot be: it runs on a GitHub runner against Azure, in the
 * shared workflow `dew_flow_release_signing/.github/workflows/sign-windows.yml`, and is exercised by
 * a dispatch trial (`target: mcp`) on the pull request branch. What this file holds is the part this
 * repository owns: that the `unsigned-mcp-*` build artifact is never attached to a release (the
 * attached zip is the shared workflow's: signed once a provider is active, unsigned with a warning
 * until then), that the signing job is called the way the shared contract says, that the product check runs on the FINAL
 * zip before it is uploaded, and that the release is published only after all of it.</p>
 */

const workflow = fs
  .readFileSync(path.join(__dirname, '..', '..', '..', '.github', 'workflows', 'release.yml'), 'utf8')
  .split('\r\n')
  .join('\n');

/** Every regex metacharacter escaped, backslash included: text that is to be matched as itself. */
function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/** A file-name glob (`*` and nothing else is a wildcard) as an anchored regex. */
function globToRegex(glob: string): RegExp {
  return new RegExp(`^${escapeRegExp(glob).replace(/\\\*/g, '.*')}$`);
}

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
  const found = stepsOf(job).find((s) => new RegExp(`^ {6}- name: ${escapeRegExp(name)}\\s*$`, 'm').test(s));
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
  // release that Smart App Control then blocks. The shared workflow's artifact is `mcp-<rid>` (signed
  // once a provider is active, unsigned with a warning until then); the leg's own is
  // `unsigned-mcp-<rid>`, so a download of `mcp-win-*` can never pick the build artifact up.
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

test('the folder the Package step stages for signing is the folder the upload sends', () => {
  // The defect: the upload sends a path that is not where the Windows package was made. With
  // `if-no-files-found: error` that is red on every leg; with a parent that is the workspace instead
  // (`.`) it is worse — the upload would carry the whole checkout, and the shared workflow's "exactly
  // one top-level directory" check would be the only thing that noticed. So both names are pinned,
  // and pinned to each other.
  const pack = withoutComments(step('mcp-binaries', 'Package'));
  const upload = step('mcp-binaries', 'Upload the unsigned Windows package');

  const parent = /if \[\[ "\$RID" == win-\* \]\]; then PARENT=([^;\s]+); else PARENT=\.; fi/.exec(pack);
  assert.ok(parent, 'the Package step sets PARENT for the Windows RIDs, and the workspace for the others');
  assert.equal(parent[1], 'unsigned', 'a parent of its own, never the workspace');
  assert.match(pack, /mkdir -p "\$PARENT\/\$NAME"/, 'the package folder is made under it');
  assert.match(upload, /\n\s+path: unsigned\/\s*\n/, 'the upload sends that parent, with its trailing slash');
  assert.match(upload, new RegExp(`\\n\\s+path: ${escapeRegExp(parent[1]!)}/\\s*\\n`), 'and it is the one the step made');
});

/** The v1.1.0 merge commit of the shared workflow; the tag is only the comment beside it. */
const SHARED_PIN = 'a9a7eccdf76306d8fdbc67fc917cf8d5a362d851';

test('mcp-sign calls the shared workflow, pinned to the v1.1.0 commit, with the contract it expects', () => {
  const block = jobBlock('mcp-sign');
  const code = withoutComments(block);

  assert.match(
    code,
    /\n {4}uses: oleksandrdubyna88\/dew_flow_release_signing\/\.github\/workflows\/sign-windows\.yml@[0-9a-f]{40}\s/,
    'a 40-hex SHA, never a branch or a tag that can move',
  );
  // The defect: a pin left at v1.0.0 has no `allow-unsigned-release` input, so the switch line below
  // would be an input the called workflow does not declare and the call would be refused.
  assert.match(
    block,
    new RegExp(
      `\\n {4}uses: oleksandrdubyna88/dew_flow_release_signing/\\.github/workflows/sign-windows\\.yml@${escapeRegExp(SHARED_PIN)} # v1\\.1\\.0\\n`,
    ),
    'the v1.1.0 merge commit, with its tag as the comment',
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
  // The defect: a bare `startsWith(github.ref, 'refs/tags/')` leaves a dispatch trial no way to take the
  // tag path, so the switch could be proven only by cutting a real tag. `inputs.required` is the force.
  assert.match(
    code,
    /\n {6}required: \$\{\{ startsWith\(github\.ref, 'refs\/tags\/'\) \|\| inputs\.required \}\}\n/,
    'a tag, or a dispatch that forces it, is a release decision; a plain trial may fall back',
  );
});

test('the unsigned-release switch is exactly the repository variable compared with the string true', () => {
  // The defects, each of them quiet. A bare `${{ vars.ALLOW_UNSIGNED_RELEASES }}` is the STRING 'false'
  // or 'banana' where the input is a boolean, and a non-empty string is not a clean refusal; a comparison
  // with another spelling ('True', a number) is a switch nobody can read; and a trailing comment is
  // text the next edit moves. Absent or misspelled must mean ENFORCEMENT, which `== 'true'` gives.
  const code = withoutComments(jobBlock('mcp-sign'));
  assert.match(
    code,
    /\n {6}allow-unsigned-release: \$\{\{ vars\.ALLOW_UNSIGNED_RELEASES == 'true' \}\}\n/,
    'the variable compared with the string true, nothing after it',
  );
  assert.equal(code.match(/allow-unsigned-release:/g)?.length, 1, 'passed once');
});

test('the dispatch can force the signing decision as a tag does, and the force defaults off', () => {
  // The defect: without the input, the caller's `required` expression has nothing to read on a
  // dispatch (`inputs.required` is null), and the switch could not be proven before a real tag.
  const head = workflow.slice(workflow.indexOf('\n  workflow_dispatch:\n'), workflow.indexOf('\npermissions:\n'));
  const input = /\n {6}required:\n((?: {8}.*\n)+)/.exec(head);
  assert.ok(input, 'workflow_dispatch has an input named required');
  assert.match(input[1]!, /\n? {8}description: 'force the signing decision as on a release tag'\n/);
  assert.match(input[1]!, /\n {8}type: boolean\n/, 'a boolean');
  assert.match(input[1]!, /\n {8}default: false\n/, 'off unless asked');
  assert.match(input[1]!, /\n {8}required: false\n/, 'and not itself required of the person who dispatches');
});

test('no job that calls the shared signing workflow passes `environment`', () => {
  // The defect: the shared workflow's `environment` input defaults to `release-signing`, the
  // environment the federated trust names. A caller that passes its own can send the signing job into
  // an environment the trust does not cover, or — a typo — into one GitHub creates empty, where a
  // trial takes skip and a tag, with the switch on, ships unsigned without a word. The default is the
  // contract; no caller restates it.
  const callers = jobNames().filter((job) =>
    /\n {4}uses: oleksandrdubyna88\/dew_flow_release_signing\//.test(withoutComments(jobBlock(job))),
  );
  // The scan must still find its subject, or it passes forever over nothing.
  assert.deepEqual(callers, ['mcp-sign'], 'the scan finds the one caller');
  for (const job of callers) {
    const code = withoutComments(jobBlock(job));
    assert.doesNotMatch(code, /^\s+environment:/m, `${job} passes or sets an environment`);
  }
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
  const toRegex = globToRegex;

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

  assert.match(block, /\n {4}name: attach the final Windows coai-mcp\n/, 'named for what it attaches, not for a signature it may not carry');
  const download = step('mcp-sign-attach', 'Download the final packages');
  assert.match(download, /uses: actions\/download-artifact@[0-9a-f]{40} # v\d/, 'a pinned download');
  assert.match(download, /\n\s+pattern: mcp-win-\*\s*\n/, 'the shared workflow\'s zips, never unsigned-mcp-*');

  const check = withoutComments(step('mcp-sign-attach', 'The product check on each final zip'));
  const attach = withoutComments(step('mcp-sign-attach', 'Attach to the draft release'));
  assert.match(check, /archive-carries\.sh "\$ZIP" coai-mcp\.exe '\*e_sqlite3\*'/, 'the binary and the SQLite library');
  assert.doesNotMatch(check, /^\s+if:/m, 'it runs on a dispatch trial too, which is what V1 exercises');
  assert.match(
    attach,
    /if: startsWith\(github\.ref, 'refs\/tags\/mcp-v'\)\s*\n/,
    'only an mcp tag uploads — the same tag line as mcp-binaries and mcp-release-complete, not any tag',
  );
  assert.match(attach, /gh release upload "\$GITHUB_REF_NAME" "\$ZIP" --clobber/, 'the zip');
  assert.match(attach, /gh release upload "\$GITHUB_REF_NAME" "\$ZIP\.sha256" --clobber/, 'and its checksum');

  // The defect (CodeRabbit): the text of the check is matched, and a loop narrowed to `win-x64` would
  // still contain it, so the arm64 zip would ship unchecked and the suite stay green. The platforms the
  // loop visits are DERIVED from the build matrix, the way the artifact list above is, and the layout
  // check has to sit inside the loop, after the basename check.
  const loop = /for RID in ([^;\n]+); do\n([\s\S]*?)\n\s+done\b/.exec(check);
  assert.ok(loop, 'the check is a loop over the platforms');
  assert.deepEqual(
    loop[1]!.trim().split(/\s+/).sort(),
    windowsRids().sort(),
    'exactly the Windows RIDs of the build matrix',
  );
  const body = loop[2]!;
  const carries = body.indexOf('archive-carries.sh "$ZIP"');
  const layout = body.indexOf('final-zip-layout.sh "$ZIP"');
  assert.ok(carries !== -1, 'the basename check is inside the loop');
  assert.ok(layout > carries, 'and the layout check is inside it too, after it, on the same zip');

  const names = stepsOf('mcp-sign-attach').map((s) => /- name: (.*)/.exec(s)?.[1]);
  assert.ok(
    names.indexOf('The product check on each final zip') < names.indexOf('Attach to the draft release'),
    'the check runs BEFORE the upload',
  );
});

// ---------------------------------------------------------------------------------------------------
// THE FINAL-ZIP STEP, RUN. Release-only shell is shell nothing executes until the day it matters
// (common/testing.md), and the loop that picks one zip per Windows platform and hands it to the two
// checks is exactly that: the tests above match its TEXT, which cannot see a glob that selects the
// wrong file or a loop that tolerates two. This one takes the step's own `run:` body out of the
// workflow and executes it under bash, in a folder of fixtures, with the two scripts replaced by stubs
// that record what they were given. (CodeRabbit, #944.)
//
// A checkout without bash skips rather than fails, as `draftReleaseNotes.test.ts` does; CI has it.
// ---------------------------------------------------------------------------------------------------

const hasBash = spawnSync('bash', ['-c', 'exit 0'], { encoding: 'utf8' }).status === 0;
const needsBash = hasBash ? false : 'no bash on this checkout; ubuntu-latest has it and runs this';

interface StepRun {
  readonly code: number | null;
  readonly said: string;
  /** The stubs' calls, in order, one `<script> <zip>` per line. */
  readonly calls: string[];
}

/**
 * Run the product-check step's `run:` text in a temp folder.
 *
 * @param zips the final zips to create, as paths under `final/`
 * @param exits the exit code each stub answers with (0 unless named)
 */
function runFinalZipStep(zips: readonly string[], exits: { carries?: number; layout?: number } = {}): StepRun {
  const body = runBodies('mcp-sign-attach').find((b) => b.includes('for RID in'));
  assert.ok(body, 'the step has a run body with the platform loop');

  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'finalzipstep-'));
  try {
    const log = path.join(dir, 'calls.txt').replace(/\\/g, '/');
    const scripts = path.join(dir, '.github', 'scripts');
    fs.mkdirSync(scripts, { recursive: true });
    for (const [name, code] of [['archive-carries', exits.carries ?? 0], ['final-zip-layout', exits.layout ?? 0]] as const) {
      fs.writeFileSync(
        path.join(scripts, `${name}.sh`),
        `#!/bin/sh\necho "${name} $*" >> "${log}"\nexit ${code}\n`,
        { encoding: 'utf8', mode: 0o755 },
      );
    }
    for (const zip of zips) {
      const file = path.join(dir, 'final', zip);
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, '');
    }

    const stepScript = path.join(dir, 'final-zip-step.sh');
    fs.writeFileSync(stepScript, body, 'utf8');
    const ran = spawnSync('bash', [stepScript], { cwd: dir, encoding: 'utf8', timeout: 30_000 });
    const calls = fs.existsSync(log) ? fs.readFileSync(log, 'utf8').split('\n').filter((l) => l !== '') : [];
    return { code: ran.status, said: `${ran.stdout}${ran.stderr}`, calls };
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

const bothZips = ['mcp-win-x64/coai-mcp-0.1.0-win-x64.zip', 'mcp-win-arm64/coai-mcp-0.1.0-win-arm64.zip'];

test('the final-zip step hands each Windows platform its own zip to BOTH checks, basename first', { skip: needsBash }, () => {
  const ran = runFinalZipStep(bothZips);

  assert.equal(ran.code, 0, ran.said);
  const asks = ' coai-mcp.exe *e_sqlite3*';
  // Each platform's zip, never the other's; the layout check on the same file, right after.
  assert.deepEqual(
    ran.calls.map((c) => (c.endsWith(asks) ? c.slice(0, -asks.length) : c)),
    [
      'archive-carries final/mcp-win-x64/coai-mcp-0.1.0-win-x64.zip',
      'final-zip-layout final/mcp-win-x64/coai-mcp-0.1.0-win-x64.zip',
      'archive-carries final/mcp-win-arm64/coai-mcp-0.1.0-win-arm64.zip',
      'final-zip-layout final/mcp-win-arm64/coai-mcp-0.1.0-win-arm64.zip',
    ],
  );
  // The basename check still asks for the binary and the library, whatever the zip.
  assert.ok(ran.calls.filter((c) => c.startsWith('archive-carries')).every((c) => c.endsWith(asks)), 'the binary and the library');
});

test('the final-zip step is red when a platform has no zip, and the other platform\'s zip does not stand in', { skip: needsBash }, () => {
  const ran = runFinalZipStep([bothZips[0]!]);

  assert.notEqual(ran.code, 0, 'a missing arm64 zip is not a pass over the x64 one');
  assert.match(ran.said, /expected exactly one final zip for win-arm64/);
});

test('the final-zip step is red when one platform has two zips', { skip: needsBash }, () => {
  const ran = runFinalZipStep([...bothZips, 'mcp-win-x64/coai-mcp-0.2.0-win-x64.zip']);

  assert.notEqual(ran.code, 0, 'two candidates is a step that cannot say which one ships');
  assert.match(ran.said, /expected exactly one final zip for win-x64/);
});

test('the final-zip step is red when either check refuses the zip', { skip: needsBash }, () => {
  assert.notEqual(runFinalZipStep(bothZips, { carries: 1 }).code, 0, 'the basename check failing fails the step');
  const layout = runFinalZipStep(bothZips, { layout: 1 });
  assert.notEqual(layout.code, 0, 'the layout check failing fails the step');
  assert.ok(layout.calls.some((c) => c.startsWith('final-zip-layout')), 'and it was reached');
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

test('the release is published only after the final Windows packages are attached', () => {
  // The defect: a completeness job that needs only the build would publish a draft that holds no
  // Windows package, or hold the final one back until after the publish.
  assert.deepEqual(needsOf('mcp-release-complete').sort(), ['mcp-binaries', 'mcp-sign-attach']);
  assert.match(condition('mcp-release-complete'), /startsWith\(github\.ref, 'refs\/tags\/mcp-v'\)/);
});

test('a glob\'s regex metacharacters are literal, and only `*` is a wildcard', () => {
  // The defect (CodeQL js/incomplete-sanitization): escaping the dot alone leaves `+ ? ( ) [ ] { } | ^ $ \\`
  // live, so a declared name that carries one would match something else, or throw, and the
  // library-list test above would pass or fail for a reason that is not about the package.
  const glob = globToRegex('a+b(1)[x].dll');
  assert.ok(glob.test('a+b(1)[x].dll'), 'the name itself matches');
  assert.ok(!glob.test('aab1x.dll'), 'and the same text read as a pattern does not');
  assert.ok(!globToRegex('tree-sitter.dll').test('tree-sitterXdll'), 'a dot is a dot');
  assert.ok(globToRegex('tree-sitter*.dll').test('tree-sitter-rust.dll'), 'a star is a wildcard');
});
