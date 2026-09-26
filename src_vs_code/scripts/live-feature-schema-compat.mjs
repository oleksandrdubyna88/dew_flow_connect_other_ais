/**
 * The released-half check for S2.1 of the feature-review plan (§4.13): does the PREVIOUS released
 * coai-mcp still read what this build writes, and does this build take over what the released one
 * wrote?
 *
 * <p>No unit test can answer either question — the suite runs one binary against one reader, both
 * from this checkout — and a schema step is one-way: getting this wrong strands a person until they
 * update. Three seams this story introduces, each measured against the real released artefact:</p>
 *
 * <ol>
 *   <li><b>A database this build migrated to step 16 (`rounds.note`)</b>, holding a `FeatureReview`
 *       round with verdict `skipped` and a note: the OLD binary's `--log` must still answer, exit 0,
 *       and list the round under its raw stage name — it has no `note` member and no phrase for the
 *       stage, and neither is a failure.</li>
 *   <li><b>A database the OLD binary created</b> (at its own step count): this build opens it,
 *       migrates it FORWARD and reads the note back. Old → new is the supported direction.</li>
 *   <li><b>A session file with `"stage": "FeatureReview"` and a `feature` key</b>, holding a round a
 *       dead process left running: the OLD binary's startup sweep must leave it alone (it cannot
 *       deserialise the stage, and a file it cannot read is skipped, never rewritten), and THIS
 *       build's sweep must mark the round interrupted and save it back under the same file — its own
 *       key — leaving one session file, not two.</li>
 * </ol>
 *
 * <h2>How to run it</h2>
 *
 * <pre>dotnet build dew_flow_connect_other_ais.slnx -c Debug &amp;&amp; cd src_vs_code &amp;&amp; npm run test:feature-compat</pre>
 *
 * <p>It downloads the NEWEST `mcp-v*` release with `gh` — the release a person has not updated
 * from is the newest one until this change ships; once it has, pass the one before it as
 * `COAI_OLD_SERVER=<path>` or `--tag=mcp-vX.Y.Z`. It is NOT in CI: it needs the network and a
 * released artefact, and a check that silently skips in CI is a check that reports success for
 * having done nothing.</p>
 *
 * <p>Exit codes: <b>0</b> the boundary holds · <b>1</b> it does not, and says which assertion ·
 * <b>2</b> the old binary could not be obtained, which is an environment answer rather than a
 * contract failure.</p>
 */
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtempSync, writeFileSync, mkdirSync, existsSync, readdirSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';

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

const flag = (name, fallback) => {
  const found = process.argv.find((a) => a.startsWith(`--${name}=`));

  return found === undefined ? fallback : found.slice(name.length + 3);
};

/** A one-shot mode, or a serve that ends on EOF: stdout is the answer, the exit code is what happened. */
function run(exe, args, dataDir) {
  return new Promise((resolve) => {
    const child = spawn(exe, args, { shell: false, env: { ...process.env, COAI_DATA_DIR: dataDir } });
    let out = '';
    let err = '';
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

function newServer() {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';
  const built = path.join(HERE, 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', exe);

  return existsSync(built) ? built : '';
}

/** The released server, downloaded once — the newest, or the tag asked for. */
function oldServer(into) {
  if (process.env['COAI_OLD_SERVER'] !== undefined && existsSync(process.env['COAI_OLD_SERVER'])) {
    return process.env['COAI_OLD_SERVER'];
  }
  let tag = flag('tag', '');
  if (tag === '') {
    // Published releases only: a DRAFT is somebody's release in progress, with no assets a person
    // could have installed — measured 2026-09-26, when the newest tag was a draft carrying three of
    // its six platforms.
    const listed = spawnSync('gh', ['release', 'list', '--repo', REPO, '--limit', '40', '--exclude-drafts', '--json', 'tagName'], {
      encoding: 'utf8', shell: false, timeout: NETWORK_MS,
    });
    if (listed.status !== 0) {
      return '';
    }
    tag = JSON.parse(listed.stdout).map((one) => one.tagName).find((one) => one.startsWith('mcp-v')) ?? '';
  }
  if (tag === '') {
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
  console.log(`old: ${tag}`);

  return found === undefined ? '' : path.join(unpacked, String(found));
}

/** bsdtar by its absolute path on Windows — `tar` is two different programs there (see live-close-consult-compat.mjs). */
function unpack(archive, into) {
  const bsdtar = process.platform === 'win32'
    ? path.join(process.env['SystemRoot'] ?? 'C:\\Windows', 'System32', 'tar.exe')
    : 'tar';
  if (process.platform !== 'win32' || existsSync(bsdtar)) {
    return spawnSync(bsdtar, ['-xf', archive, '-C', into], { encoding: 'utf8', timeout: NETWORK_MS });
  }

  return spawnSync(
    'powershell',
    ['-NoProfile', '-NonInteractive', '-Command',
      `Expand-Archive -LiteralPath '${archive}' -DestinationPath '${into}' -Force`],
    { encoding: 'utf8', timeout: NETWORK_MS },
  );
}

const now = () => new Date().toISOString().replace('Z', '0000Z');

/** A consultation record EXACTLY as the released build writes one — the one write both binaries have that opens the database. */
function plantConsultation(data, repo, id) {
  mkdirSync(path.join(data, 'consultations'), { recursive: true });
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
}

/** The session file name, as SessionStore derives it: sha256 of the key, first sixteen hex characters. */
function sessionFile(data, repo, plan) {
  const repoKey = repo.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
  const planKey = process.platform === 'win32' ? plan.toLowerCase() : plan;
  const key = `${repoKey}#:feature#feature:${planKey}`;
  const hash = createHash('sha256').update(key, 'utf8').digest('hex').slice(0, 16);

  return path.join(data, 'sessions', `session-${hash}.json`);
}

/** A feature session holding a round a dead process left running — what the startup sweep is for. */
function plantFeatureSession(data, repo, plan) {
  mkdirSync(path.join(data, 'sessions'), { recursive: true });
  const file = sessionFile(data, repo, plan);
  writeFileSync(file, JSON.stringify({
    state: {
      sessionId: 'f1', repoPath: repo, branch: ':feature',
      config: { roles: {}, onExhausted: 'Human' },
      stage: 'FeatureReview', roundsRunThisStage: 0, escalationsUsed: 0, awaitingResolve: false,
      planProceeded: false, advanceOnResolve: false, humanGate: false, rejections: [],
      document: '', feature: plan,
    },
    rounds: [{
      stage: 'FeatureReview', number: 1, verdict: 'running', gatingCount: 0, reviewers: '0 of 1 answered',
      completedUtc: now(), status: 'running', startedUtc: now(), runnerPid: 999999, sha: '',
      reviewerStates: [], subject: 'PLAN_x.md', tokensIn: 0, tokensOut: 0,
    }],
    openedUtc: now(), pending: [], planText: '# the plan', usedPrompts: [],
  }, null, 2));

  return file;
}

const sessionsIn = (data) => existsSync(path.join(data, 'sessions'))
  ? readdirSync(path.join(data, 'sessions')).filter((one) => one.startsWith('session-') && one.endsWith('.json'))
  : [];

// ---------------------------------------------------------------------------------------------

const work = mkdtempSync(path.join(tmpdir(), 'coai-feature-compat-'));
const repo = path.join(work, 'repo');
mkdirSync(repo, { recursive: true });

const built = newServer();
if (built.length === 0) {
  console.log('this checkout has no built server — run `dotnet build dew_flow_connect_other_ais.slnx -c Debug` first');
  process.exit(2);
}
const old = oldServer(work);
if (old.length === 0) {
  console.log('the released server could not be obtained; nothing here is a contract result');
  process.exit(2);
}
console.log(`new: ${built}\n`);

// ---------- 1. NEW writes step 16 and a skipped feature round; OLD reads the log ----------
const dataA = path.join(work, 'a');
plantConsultation(dataA, repo, 'a'.repeat(32));
const migrated = await run(built, ['--close-consult', '--repo', repo, '--id', 'a'.repeat(32), '--outcome', 'solved'], dataA);
say(migrated.code === 0, 'this build opens a fresh data directory and migrates it', `exit ${migrated.code}`);
{
  const db = new DatabaseSync(path.join(dataA, 'coai.db'));
  const version = db.prepare('PRAGMA user_version').get();
  say(version.user_version === 16, 'and the file records sixteen steps', `user_version ${version.user_version}`);
  const started = now();
  db.exec(`INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('f1', '${repo.replace(/'/g, "''")}', ':feature', '${started}')`);
  db.exec(`INSERT INTO rounds (session_id, stage, number, subject, status, verdict, gating, started_utc, completed_utc, head_sha, note)
           VALUES ('f1', 'FeatureReview', 1, 'PLAN_x.md', 'done', 'skipped', 0, '${started}', '${started}', 'abc123', 'no vendor is ticked for the feature review ×3')`);
  db.close();
}
const oldLog = await run(old, ['--log', '--paged'], dataA);
say(oldLog.code === 0, 'the released binary still answers --log over a step-16 database', `exit ${oldLog.code}`);
const oldRound = JSON.parse(oldLog.out || '{}').rounds?.find((one) => one.stage === 'FeatureReview');
say(oldRound !== undefined, 'and lists the skipped feature round under its raw stage name');
say(oldRound !== undefined && !('note' in oldRound), 'with no note member, which it has never heard of');
const newLog = await run(built, ['--log', '--paged'], dataA);
const newRound = JSON.parse(newLog.out || '{}').rounds?.find((one) => one.stage === 'FeatureReview');
say(newRound?.note === 'no vendor is ticked for the feature review ×3', 'this build reads the note back', `note: ${newRound?.note}`);

// ---------- 2. OLD creates the database; NEW migrates it forward ----------
const dataB = path.join(work, 'b');
plantConsultation(dataB, repo, 'b'.repeat(32));
const oldMade = await run(old, ['--close-consult', '--repo', repo, '--id', 'b'.repeat(32), '--outcome', 'solved'], dataB);
say(oldMade.code === 0, 'the released binary creates a database at its own step count', `exit ${oldMade.code}`);
let before = -1;
{
  const db = new DatabaseSync(path.join(dataB, 'coai.db'));
  before = db.prepare('PRAGMA user_version').get().user_version;
  db.close();
}
say(before > 0 && before < 16, 'which is below sixteen', `user_version ${before}`);
plantConsultation(dataB, repo, 'c'.repeat(32));
const forward = await run(built, ['--close-consult', '--repo', repo, '--id', 'c'.repeat(32), '--outcome', 'solved'], dataB);
say(forward.code === 0, 'this build opens the released binary\'s database', `exit ${forward.code}`);
{
  const db = new DatabaseSync(path.join(dataB, 'coai.db'));
  const after = db.prepare('PRAGMA user_version').get().user_version;
  const hasNote = db.prepare("SELECT COUNT(*) AS n FROM pragma_table_info('rounds') WHERE name = 'note'").get().n;
  db.close();
  say(after === 16, 'and migrates it forward to sixteen', `user_version ${after}`);
  say(hasNote === 1, 'with the note column present');
}
const oldAfter = await run(old, ['--log'], dataB);
say(oldAfter.code === 0, 'and the released binary still reads it afterwards', `exit ${oldAfter.code}`);

// ---------- 3. A feature session file: the OLD sweep leaves it alone, the NEW sweep takes it over ----------
const dataC = path.join(work, 'c');
const planted = plantFeatureSession(dataC, repo, 'todo/PLAN_x.md');
const plantedBytes = readFileSync(planted, 'utf8');
const oldServe = await run(old, [], dataC);
say(oldServe.code === 0, 'the released binary starts over a data directory holding a feature session and exits on EOF', `exit ${oldServe.code}`);
say(readFileSync(planted, 'utf8') === plantedBytes, 'and its startup sweep left the file it cannot read untouched');
say(sessionsIn(dataC).length === 1, 'without writing a second session file', `${sessionsIn(dataC).length} file(s)`);
const newServe = await run(built, [], dataC);
say(newServe.code === 0, 'this build starts over the same directory and exits on EOF', `exit ${newServe.code}`);
const files = sessionsIn(dataC);
say(files.length === 1, 'its sweep saved the session back under its own key — one file, not two', `${files.length} file(s)`);
const swept = JSON.parse(readFileSync(path.join(dataC, 'sessions', files[0] ?? path.basename(planted)), 'utf8'));
say(swept.rounds?.[0]?.status === 'interrupted', 'and the dead round is marked interrupted', `status: ${swept.rounds?.[0]?.status}`);
say(swept.state?.feature === 'todo/PLAN_x.md' && swept.state?.stage === 'FeatureReview', 'with the feature and the stage intact');

console.log('');
if (broken.length === 0) {
  console.log('The boundary holds, both ways.');
  process.exit(0);
}
console.log(`The boundary is BROKEN: ${broken.length} assertion(s) failed.`);
console.log('Read §4.13 of todo/PLAN_feature_review.md before changing either half.');
process.exit(1);
