/**
 * The live counterpart check for issue #309: does the PREVIOUS released server still work with this
 * build's reader, and does the new close fail the way the boundary table says against it?
 *
 * <p>No unit test can answer either question. The suite runs one binary against one reader, both
 * from this checkout; the claim in the plan's two-sided table is about two DIFFERENT builds meeting
 * on one data directory, which is the situation a person is in for as long as they have updated one
 * half and not the other. `module_server.md` records what that has already cost once — a wire field
 * dropped by every component written before it existed, for three releases.</p>
 *
 * <h2>What it asserts</h2>
 *
 * <ol>
 *   <li><b>A record the OLD server wrote is read with its STATUS intact and no outcome.</b> The
 *       dangerous direction is the one the first draft of the plan got wrong: an outcome-less record
 *       must not be reported as finished when the server says it is still open.</li>
 *   <li><b>`--close-consult` against the OLD binary fails as an unknown argument</b>, with a non-zero
 *       exit and a sentence — not a silent success and not a crash. That is exactly what the panel's
 *       close control meets on a machine whose server has not been updated.</li>
 *   <li><b>The NEW binary closes the same record in the same directory</b>, so the upgrade path is
 *       demonstrated rather than assumed.</li>
 * </ol>
 *
 * <h2>How to run it</h2>
 *
 * <pre>cd src_vs_code &amp;&amp; npm run test:close-compat</pre>
 *
 * <p>It downloads the previous release with `gh` the first time, and reuses `COAI_OLD_SERVER` when
 * that variable already names a binary on disk. It is NOT in CI: it needs the network and a released
 * artefact, and a check that silently skips in CI is a check that reports success for having done
 * nothing.</p>
 *
 * <p>Exit codes: <b>0</b> the boundary holds · <b>1</b> it does not, and says which assertion · <b>2</b>
 * the old binary could not be obtained, which is an environment answer rather than a contract failure.</p>
 */
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, mkdirSync, existsSync, readdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

/**
 * Every child here gets a deadline, and none of them had one.
 *
 * <p>This script is run by a person at a terminal, which is exactly why it matters: `gh` waiting on
 * an auth prompt, a download stalled behind a proxy, or a server build that hangs on a lock all
 * looked identical to "still working", for ever. A check whose failure mode is silence is a check
 * nobody trusts the green of. The two numbers are different because they are different waits: a
 * release download crosses the network, a one-shot mode is one small local write.</p>
 */
const NETWORK_MS = 180_000;
const LOCAL_MS = 60_000;

const REPO = 'oleksandrdubyna88/dew_flow_connect_other_ais';
const HERE = path.join(import.meta.dirname, '..', '..');

const broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken.push(what);
  }
};

/** A one-shot mode: stdout is the answer, the exit code is what happened. */
function run(exe, args, dataDir) {
  return new Promise((resolve) => {
    const child = spawn(exe, args, { shell: false, env: { ...process.env, COAI_DATA_DIR: dataDir } });
    let out = '';
    let err = '';
    // KILLED at the deadline rather than waited on: a one-shot mode that has not answered in a
    // minute is not going to, and the whole tree goes because a released build may have spawned a
    // child of its own before it stopped.
    const deadline = setTimeout(() => {
      err += `\n(no answer within ${LOCAL_MS / 1000}s — killed)`;
      child.kill('SIGKILL');
    }, LOCAL_MS);
    deadline.unref();
    child.stdin.end();
    child.stdout.on('data', (chunk) => { out += String(chunk); });
    child.stderr.on('data', (chunk) => { err += String(chunk); });
    child.on('error', (reason) => { clearTimeout(deadline); resolve({ code: -1, out, err: String(reason) }); });
    child.on('close', (code) => { clearTimeout(deadline); resolve({ code: code ?? -1, out, err }); });
  });
}

/** Where this checkout's freshly built server is. */
function newServer() {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';
  const built = path.join(HERE, 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', exe);

  return existsSync(built) ? built : '';
}

/**
 * The previous released server, downloaded once.
 *
 * <p>The release BEFORE the newest, deliberately: the newest may already be this change. What the
 * boundary table is about is the build a person has not updated yet.</p>
 */
function oldServer(into) {
  if (process.env['COAI_OLD_SERVER'] !== undefined && existsSync(process.env['COAI_OLD_SERVER'])) {
    return process.env['COAI_OLD_SERVER'];
  }
  const listed = spawnSync('gh', ['release', 'list', '--repo', REPO, '--limit', '40', '--json', 'tagName'], {
    encoding: 'utf8', shell: false, timeout: NETWORK_MS,
  });
  if (listed.status !== 0) {
    return '';
  }
  const tags = JSON.parse(listed.stdout).map((one) => one.tagName).filter((one) => one.startsWith('mcp-v'));
  // The release BEFORE the newest. Taking the newest would, the moment this change ships,
  // compare the build against ITSELF and report a boundary that was never tested — the docblock
  // said "before" from the start and the code did not. (The review pass, 2026-09-17.)
  const tag = tags.length > 1 ? tags[1] : tags[0];
  if (tag === undefined) {
    return '';
  }
  const asset = process.platform === 'win32'
    ? `*win-${process.arch === 'arm64' ? 'arm64' : 'x64'}.zip`
    : `*${process.platform === 'darwin' ? 'osx' : 'linux'}-${process.arch === 'arm64' ? 'arm64' : 'x64'}.tar.gz`;
  const got = spawnSync('gh', ['release', 'download', tag, '--repo', REPO, '--pattern', asset, '--dir', into], {
    encoding: 'utf8', shell: false, timeout: NETWORK_MS,
  });
  if (got.status !== 0) {
    console.log(`could not download ${tag}: ${got.stderr}`);

    return '';
  }
  const archive = readdirSync(into).find((one) => one.endsWith('.zip') || one.endsWith('.tar.gz'));
  if (archive === undefined) {
    return '';
  }
  const unpacked = path.join(into, 'old');
  mkdirSync(unpacked, { recursive: true });
  const opened = unpack(path.join(into, archive), unpacked);
  if (opened.status !== 0) {
    console.log(`could not unpack ${archive}: ${opened.stderr}`);

    return '';
  }
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';
  const found = readdirSync(unpacked, { recursive: true }).find((one) => String(one).endsWith(exe));

  return found === undefined ? '' : path.join(unpacked, String(found));
}

/**
 * Opening the archive, with the unpacker named by its ABSOLUTE path on Windows.
 *
 * <p><b>`tar` is two different programs and the difference decides whether this works.</b> Windows
 * has shipped <b>bsdtar</b> as <code>%SystemRoot%\System32\tar.exe</code> since 2018, and bsdtar
 * reads a zip and takes a drive-lettered path. Git Bash puts <b>GNU tar</b> earlier on PATH, and
 * that one answers "This does not look like a tar archive" for a zip — and reads the leading
 * <code>C:</code> of a Windows path as a REMOTE HOST before it gets that far. So the bare word
 * `tar` is a coin toss decided by whichever shell a person happens to be in, and the absolute path
 * is not. (codex, the code round; the first draft routed zips through PowerShell to dodge this,
 * which worked and left the coin toss in place for the tarball.)</p>
 *
 * <p>PowerShell remains the fallback for a Windows old enough to lack bsdtar, and every unpacker
 * gets the network deadline: an archive is arriving from a temp directory, but a wedged child is
 * still a terminal that never comes back.</p>
 */
function unpack(archive, into) {
  const bsdtar = process.platform === 'win32'
    ? path.join(process.env['SystemRoot'] ?? 'C:\\Windows', 'System32', 'tar.exe')
    : 'tar';
  if (process.platform !== 'win32' || existsSync(bsdtar)) {
    // `-xf`, not `-xzf`: bsdtar reads the format off the file, and a zip is not gzip.
    return spawnSync(bsdtar, ['-xf', archive, '-C', into], { encoding: 'utf8', timeout: NETWORK_MS });
  }

  return spawnSync(
    'powershell',
    ['-NoProfile', '-NonInteractive', '-Command',
      `Expand-Archive -LiteralPath '${archive}' -DestinationPath '${into}' -Force`],
    { encoding: 'utf8', timeout: NETWORK_MS },
  );
}

// ---------------------------------------------------------------------------------------------

const work = mkdtempSync(path.join(tmpdir(), 'coai-close-compat-'));
const data = path.join(work, 'data');
const repo = path.join(work, 'repo');
mkdirSync(path.join(data, 'consultations'), { recursive: true });
mkdirSync(repo, { recursive: true });

const now = () => new Date().toISOString().replace('Z', '0000Z');
const id = 'e'.repeat(32);

// EXACTLY the members a server before this change wrote — no `outcome`, and still OPEN. That pair is
// the whole point: a reader must not turn "nobody said anything" into "it is finished".
writeFileSync(path.join(data, 'consultations', `${id}.json`), `${JSON.stringify({
  id,
  caller: `repo:${repo}`,
  callerKind: 'claude',
  sessionId: 's1',
  repoPath: repo,
  branch: 'main',
  headSha: 'sha',
  vendor: 'codex',
  model: 'gpt-6',
  runtime: 'codex',
  memory: 'vendorRemembers',
  maxTurns: 5,
  startedUtc: now(),
  updatedUtc: now(),
  status: 'open',
  handle: 'h1',
  turns: [{ utc: now(), problem: 'stuck', advice: 'try this', seconds: 10, tokensIn: 100, tokensOut: 20 }],
}, null, 2)}\n`);

const built = newServer();
if (built.length === 0) {
  console.log('this checkout has no built server — run `dotnet build dew_flow_connect_other_ais.slnx -c Debug` first');
  process.exit(2);
}

const old = oldServer(work);
if (old.length === 0) {
  console.log('the previous released server could not be obtained; nothing here is a contract result');
  process.exit(2);
}
console.log(`old: ${old}`);
console.log(`new: ${built}\n`);

// 1. The NEW close against the OLD binary. This is the row of the boundary table that decides
//    what the panel shows a person whose server has not been updated.
const refused = await run(old, ['--close-consult', '--repo', repo, '--id', id, '--outcome', 'solved'], data);
say(refused.code !== 0,
  'the previous release REFUSES --close-consult rather than appearing to succeed', `exit ${refused.code}`);
say(`${refused.out}${refused.err}`.includes('unknown argument'),
  'and says so in a sentence the panel can show', `${refused.out}${refused.err}`.trim().slice(0, 140));

// 2. The NEW binary reads the OLD record — written with no `outcome` at all — and closes it. The
//    close is also what PROJECTS it, which is what makes step 3 possible.
const closed = await run(built, ['--close-consult', '--repo', repo, '--id', id, '--outcome', 'solved'], data);
say(closed.code === 0, 'this build reads a record written without the field, and closes it',
  `exit ${closed.code}: ${closed.out.trim().slice(0, 120)}`);

// 3. THE MIGRATION, from the other side. This build adds a column to `consultations`; the question
//    a schema change always raises is whether the binary a person has NOT updated can still read
//    the file afterwards. A migration is one-way, so getting this wrong strands them until they do.
const oldAfter = await run(old, ['--log'], data);
say(oldAfter.code === 0, 'the previous release still reads a database this build has migrated',
  `exit ${oldAfter.code}`);
const oldRow = JSON.parse(oldAfter.out || '{}').consultations?.find((one) => one.id === id);
say(oldRow !== undefined, 'and still answers the consultation, column it has never heard of and all');
say(oldRow?.status === 'closed', 'with the status this build wrote', `status: ${oldRow?.status}`);

// 4. And this build agrees with itself about the same row.
const newAfter = await run(built, ['--log'], data);
const newRow = JSON.parse(newAfter.out || '{}').consultations?.find((one) => one.id === id);
say(newRow?.outcome === 'solved', 'this build reads the outcome back', `outcome: ${newRow?.outcome}`);
// The AUTHOR crosses the same boundary and is its own column, so it is its own assertion: a
// migration that added one of the two would pass every check above.
say(newRow?.outcomeBy === 'person', 'and who recorded it', `outcomeBy: ${newRow?.outcomeBy}`);
console.log('');
if (broken.length === 0) {
  console.log('The boundary holds, both ways.');
  process.exit(0);
}
console.log(`The boundary is BROKEN: ${broken.length} assertion(s) failed.`);
console.log('Read the two-sided table in the plan before changing either half.');
process.exit(1);
