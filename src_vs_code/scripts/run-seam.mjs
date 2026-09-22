#!/usr/bin/env node
/**
 * The ONE live check across the settings seam.
 *
 * <p>The extension WRITES `<dataDir>/settings.json` and `coai-mcp` READS it, and neither is the
 * other's caller — they meet only at a path. Both suites can be green while the contract is broken,
 * because each compares its own list against itself, and that is not a hypothetical here: for the
 * whole life of the `remote` runtime the extension never wrote `remoteVendor` into `COAI_VENDORS`
 * while the server parsed it faithfully, so every Team-server reviewer was dropped from every round
 * and 1600 tests said nothing. `.claude/rules/shared/common/testing.md` names exactly that failure
 * and asks for exactly this: one check that exercises the two implementations against each other.</p>
 *
 * <p>So this produces the file with the EXTENSION's own `serverSettingsJson` — no fixture, no
 * hand-written JSON, no second copy of the shape — and hands it to the REAL binary through
 * `--providers`, which answers with what the server made of it.</p>
 *
 * <p><b>It refuses rather than skips when the binary is missing.</b> A cross-side check that quietly
 * passes when it did not run is worse than no check: it is the green suite the rule is about.</p>
 */
import { execFileSync, spawn } from 'node:child_process';
import {
  existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync,
} from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const TIMEOUT_MS = 30_000;

const repo = resolve(import.meta.dirname, '..', '..');

/**
 * The binary this repository just built, in whichever configuration built it.
 *
 * <p>RELEASE first, because that is what CI builds — `dotnet build … -c Release` — and a script that
 * only looked under `Debug` would have refused on its very first run there. Debug second, because
 * that is what a person has locally. `COAI_MCP_DLL` overrides both, for a published binary.</p>
 */
function findBinary() {
  const named = process.env['COAI_MCP_DLL'];
  if (named !== undefined && named.length > 0) {
    return named;
  }
  for (const configuration of ['Release', 'Debug']) {
    const candidate = join(repo, 'src_mcp', 'src', 'bin', configuration, 'net10.0', 'coai-mcp.dll');
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return '';
}

const binary = findBinary();
if (binary === '') {
  console.error('seam: no coai-mcp build found. Run: dotnet build src_mcp/src/CoaiMcp.csproj');
  process.exit(1);
}

const { serverSettingsJson } = await import('../out/serverSettingsFile.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { DEFAULTS } = await import('../out/settingsShape.js');
// The extension's OWN resolver, which is half of what the fifth leg compares. Importing it
// rather than re-deriving the path is the whole point: a second spelling here would be a third
// implementation of the thing two implementations already disagreed about.
const { coaiDataDir } = await import('../out/dataDir.js');
const { DEFAULT_CONSULT, consultSettingsFrom } = await import('../out/consultSettings.js');
const { tokenFileName } = await import('../out/teamServers.js');
// The sixth leg reads the server's notices with the EXTENSION's own path, reader, parser and
// writer — every one of them imported, none re-derived here.
const { serverNoticesPath, readServerNotices } = await import('../out/notificationsFile.js');
const { notificationLine, parseNotificationLine } = await import('../out/notifications.js');

/**
 * The consultant settings as the PANEL reads them, from a stored map — never built by hand here.
 *
 * <p>That is the fix this helper came from. This script is `.mjs` and nothing typechecks it, so when
 * `ConsultSettings` gained a second map on 2026-09-15 — `stored`, the unresolved side `envBlock`
 * compares against the shipped pairs — a literal that overrode only `byCaller` went on running, went
 * on writing a settings file, and quietly stopped putting `COAI_CONSULTANTS` on the wire. The seam is
 * what caught it: the real binary answered about `codex`, the shipped default, instead of the vendor
 * this script had chosen. Reading through the product's own reader means the next field added to that
 * type costs this script nothing.</p>
 */
const consultFrom = (consultants) => consultSettingsFrom(
  (section) => (section === 'consultants' ? consultants : undefined),
);

/**
 * A catalog, from a server that exists for eight seconds.
 *
 * <p>Without it this check is toothless, and the first version was: an unreachable server answers
 * "not signed in" before the vendor NAME is used at all, so removing `remoteVendor` from the writer
 * changed nothing it could see. What discriminates is the refusal a real catalog produces — a row
 * that sent its own id is told the server does not offer a vendor by that name, and a row that sent
 * the server's name is told about its accounts.</p>
 */
function catalogServer() {
  const server = createServer((req, res) => {
    if (!(req.url ?? '').startsWith('/api/catalog')) {
      res.writeHead(404).end();
      return;
    }
    res.writeHead(200, { 'content-type': 'application/json' }).end(JSON.stringify({
      serverVersion: '0.5.2',
      isAdmin: false,
      error: '',
      vendors: [{
        id: 'claude',
        runtime: 'claude',
        models: ['haiku'],
        health: { enabled: true, cliFound: true, version: '1', auth: 'own auth', note: '' },
        slots: { total: 1, ready: 1, coolingDown: 0, needsSignIn: 0 },
      }],
    }));
  });
  return server;
}

/**
 * A Team-server row exactly as the panel writes one: the id is `<server>-<vendor>` and the name the
 * SERVER knows is recorded separately. Sending the id is the defect this whole check is about.
 */
const ROW = {
  id: 'remsoftdev-claude',
  runtime: 'remote',
  teamServerId: 'remsoftdev',
  remoteVendor: 'claude',
  model: 'haiku',
  enabled: true,
  plan: true,
  code: true,
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-'));
const server = catalogServer();
// The callback fires when the socket is LISTENING, which is why nothing here polls for readiness.
await new Promise((done) => server.listen(0, '127.0.0.1', done));
const address = `http://127.0.0.1:${server.address().port}`;

// Derived, not assigned into: the address is only knowable after the server is up, and a fixture
// that is mutated after creation is a fixture whose value depends on when you read it.
const row = { ...ROW, baseUrl: address };

// Signed in, at the path BOTH sides derive independently — the extension's `tokenFileName` here,
// .NET's `TeamServerAuth.TokenPath` there. Without a token the probe answers "not signed in" before
// the vendor name is used at all, which is exactly how the first draft of this check proved nothing.
mkdirSync(join(dataDir, 'servers'), { recursive: true });
writeFileSync(join(dataDir, 'servers', tokenFileName(address)), 'a-token', 'utf8');

function fail(why) {
  console.error(`seam: ${why}`);
  server.close();
  rmSync(dataDir, { recursive: true, force: true });
  process.exit(1);
}

async function providers() {
  return await new Promise((done, broke) => {
    const child = spawn('dotnet', [binary, '--providers'], {
      env: { ...process.env, COAI_DATA_DIR: dataDir },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      broke(new Error(`--providers did not answer within ${TIMEOUT_MS} ms`));
    }, TIMEOUT_MS);

    child.stdout.on('data', (b) => {
      out += String(b);
    });
    child.stderr.on('data', (b) => {
      err += String(b);
    });
    child.on('error', (e) => {
      clearTimeout(deadline);
      broke(e);
    });
    child.on('close', (code) => {
      clearTimeout(deadline);
      if (code !== 0) {
        broke(new Error(`--providers exited ${code}\n${err}`));
        return;
      }
      done(JSON.parse(out));
    });
  });
}

// The extension's own writer, over the extension's own parser: the path a real panel takes.
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(DEFAULTS, vendorsFrom([row]), '9.9.9'), 'utf8');

let answer;
try {
  answer = await providers();
} catch (e) {
  fail(`${binary} could not answer --providers. If it is a stale build, rebuild it:
  dotnet build src_mcp/src/CoaiMcp.csproj -c Release
${e.message}`);
}

const reported = (answer.providers ?? []).find((p) => p.provider === 'remsoftdev-claude');
if (reported === undefined) {
  fail(`the server did not see the row at all. It saw: ${(answer.providers ?? []).map((p) => p.provider).join(', ')}`);
}

// The whole point, and it has been watched failing. With `remoteVendor` removed from `vendorsEnv`
// this run answers: "the Team server at http://127.0.0.1:… does not offer a vendor called
// 'remsoftdev-claude' — it offers claude". That is the bug of 2026-09-07, reproduced end to end by
// the two real implementations rather than described by a fixture.
if (reported.note.includes('does not offer a vendor called') || reported.note.includes('does not record which vendor')) {
  fail(`remoteVendor did not cross the seam — the server was asked for the ROW ID. It said: ${reported.note}`);
}

// The positive half: it was asked about `claude`, found it, and read its accounts. Asserting only
// the absence of a refusal would pass for a row the server never resolved at all.
if (reported.auth !== 'server token') {
  fail(`the row did not resolve to a usable Team-server vendor. auth=${reported.auth}, note=${reported.note}`);
}

// And the row must be a REMOTE one on the far side: a `codex` classification is the other half of
// the same failure, and it reads as "install it with npm install -g @openai/codex".
if (reported.note.includes('npm install')) {
  fail(`the server classified a Team-server row as a codex vendor. It said: ${reported.note}`);
}

server.close();

// ----------------------------------------------------------------------------------------------
// The FIFTH leg, and the first thing to write down about it is what a fixture cannot do. Both halves
// resolve `COAI_DATA_DIR` + `COAI_DATA_SIDE` independently, and `shared/data-side-vectors.json`
// holds them to the same ANSWERS — but two implementations can agree with a JSON file and still
// disagree with each other about a physical path: separators, normalisation and how each runtime
// reads an environment variable are exactly the differences a static vector cannot see.
//
// So this proves agreement by CONSEQUENCE rather than by comparing two strings. The extension's own
// `coaiDataDir()` decides where the settings file goes; the real binary is started with the same two
// variables and asked what it made of it. If the two resolvers disagree by so much as a directory,
// the server reads nothing and reports the shipped defaults - which is precisely the silent failure
// this whole plan is about, reproduced live.
/**
 * What the EXTENSION's own resolver answers for a root and a side — and the environment put back.
 *
 * <p>Set and restored by hand: this is a script rather than a test file, and reaching for the suite's
 * `withEnv` would drag a test helper into the one check that has to run against the real binary.</p>
 *
 * <p><b>Restored by DELETING what was absent, not by assigning it back.</b> `process.env.X = undefined`
 * writes the STRING 'undefined', so the first version of this poisoned every later leg: the consultant
 * leg then resolved a data directory literally named `undefined`, found no consultant settings, and
 * the server reached a real vendor instead of the stand-in CLI. A variable the parent DID have is
 * reassigned, so a run that inherited one gets it back. Caught by this suite, which is the suite for
 * catching exactly that.</p>
 *
 * <p>One helper for the fifth leg and the sixth: the sixth first carried its own copy of this block,
 * which is a second implementation of the thing whose first one already had a bug. (Story 2.4.)</p>
 */
function resolvedFor(root, side) {
  const before = { COAI_DATA_DIR: process.env['COAI_DATA_DIR'], COAI_DATA_SIDE: process.env['COAI_DATA_SIDE'] };
  process.env['COAI_DATA_DIR'] = root;
  process.env['COAI_DATA_SIDE'] = side;
  try {
    return coaiDataDir();
  } finally {
    for (const [name, value] of Object.entries(before)) {
      if (value === undefined) {
        delete process.env[name];
      } else {
        process.env[name] = value;
      }
    }
  }
}

async function sideSeam() {
  const root = mkdtempSync(join(tmpdir(), 'coai-seam-side-'));
  const side = 'wsl';
  const written = join(resolvedFor(root, side), 'settings.json');

  if (!written.startsWith(join(root, side))) {
    fail(`the extension resolved ${written}, which is not under the side it was given. `
      + 'Nothing below would mean anything: the two halves cannot be compared through a path one of '
      + 'them never chose.');
  }
  mkdirSync(join(root, side), { recursive: true });

  // A row the shipped defaults do NOT contain, so finding it can only mean the file was read.
  const row = { id: 'remsoftdev-seam-side', runtime: 'claude', label: 'the side seam', accounts: 1 };
  writeFileSync(written, serverSettingsJson(DEFAULTS, vendorsFrom([row]), '9.9.9'), 'utf8');

  const answer = await providersIn({ COAI_DATA_DIR: root, COAI_DATA_SIDE: side });
  const seen = (answer.providers ?? []).map((p) => p.provider);
  if (!seen.includes(row.id)) {
    fail(`the extension wrote ${written} and the server did not read it. It saw: ${seen.join(', ') || '(nothing)'}.
This is the two halves disagreeing about where a side's settings live - the defect
PLAN_the_settings_file_ignores_the_side.md exists for, caught live rather than by a fixture.`);
  }

  // And the other direction, which is what makes the first half mean something: the ROOT file must
  // NOT be what the server read. A server still reading `<root>/settings.json` would pass the check
  // above on an installation that happened to have the row in both places.
  const other = mkdtempSync(join(tmpdir(), 'coai-seam-side-'));
  const decoy = { id: 'remsoftdev-seam-root', runtime: 'claude', label: 'the root decoy', accounts: 1 };
  writeFileSync(join(other, 'settings.json'), serverSettingsJson(DEFAULTS, vendorsFrom([decoy]), '9.9.9'), 'utf8');
  mkdirSync(join(other, side), { recursive: true });

  const adopted = await providersIn({ COAI_DATA_DIR: other, COAI_DATA_SIDE: side });
  const afterwards = (adopted.providers ?? []).map((p) => p.provider);
  if (!afterwards.includes(decoy.id)) {
    fail(`a side with no settings of its own did not adopt <root>/settings.json. It saw: ${afterwards.join(', ') || '(nothing)'}.
An installation that was partitioned before the settings file knew about sides would
start on defaults here, with a person's vendors and keys silently gone.`);
  }
  if (!existsSync(join(other, 'settings.json'))) {
    fail('the adoption REMOVED <root>/settings.json, so the second side to start finds nothing.');
  }
  if (!existsSync(join(other, side, 'settings.json'))) {
    fail('the adoption reported success and published nothing.');
  }

  rmSync(root, { recursive: true, force: true });
  rmSync(other, { recursive: true, force: true });
}

async function providersIn(extra) {
  return await new Promise((done, broke) => {
    const child = spawn('dotnet', [binary, '--providers'], {
      env: { ...process.env, ...extra },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '';
    let err = '';
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      broke(new Error(`--providers did not answer within ${TIMEOUT_MS} ms`));
    }, TIMEOUT_MS);

    child.stdout.on('data', (b) => { out += String(b); });
    child.stderr.on('data', (b) => { err += String(b); });
    child.on('error', (e) => { clearTimeout(deadline); broke(e); });
    child.on('close', (code) => {
      clearTimeout(deadline);
      if (code !== 0) {
        broke(new Error(`--providers exited ${code}\n${err}`));
        return;
      }
      done(JSON.parse(out));
    });
  });
}

await sideSeam();

// The SIXTH leg — story 2.4 of PLAN_the_server_says_what_it_did.md, owed since 1.1.
//
// `server-notices.jsonl` is written by C# and read by TypeScript, and BOTH halves redact before
// anything reaches disk. The parity harness (`test:parity`) proves the two REDACTORS agree, by
// driving both through `NoticeTool`, a third executable built for the check. What it cannot prove
// is that the PRODUCT does what they agree on: nothing there starts the shipped server, provokes a
// refusal down the road a real client takes, and reads what landed with the extension's own reader.
// A product that serialised past `ServerNoticeLine.Of`, wrote somewhere the extension does not look,
// or wrote a line the extension's parser rejects would leave the harness green, because the harness
// never runs `coai-mcp`.
//
// So the trigger is a REAL refusal, not a test mode: `open` on a `repoPath` that is not a directory,
// which `PanelService.OpenAsync` refuses with the path QUOTED — `'<path>' is not a directory on this
// machine`. That quoting is the whole reason this refusal was chosen: a secret placed in the path
// genuinely reaches the writer. The secret is a GitHub token shape.
async function refusalSeam() {
  const root = mkdtempSync(join(tmpdir(), 'coai-seam-refusal-'));
  const side = 'seam';
  mkdirSync(join(root, side), { recursive: true });

  // Assembled at run time, so no token-shaped literal sits in this file for a scanner to report.
  // `ghp_` and 36 of [A-Za-z0-9] is what the server's vendor-prefix pattern takes out
  // (`Redaction.cs`: `(?<![A-Za-z0-9_])(sk-|ghp_|…)[A-Za-z0-9._-]{8,512}`), and a path separator
  // before it satisfies the lookbehind on both platforms.
  const secret = ['ghp', '_', 'S34mLeg2p4Refusal', 'CrossesTheWire', 'Now12'].join('');
  const absent = join(root, 'no-such-checkout', secret);

  // Where the EXTENSION looks, by its own resolver.
  const sideDir = resolvedFor(root, side);
  const noticesFile = serverNoticesPath(sideDir);

  const session = serverSession({ COAI_DATA_DIR: root, COAI_DATA_SIDE: side });

  /**
   * Remove this leg's directory, and never let that removal be what the run reports.
   *
   * <p>A failing leg must end on ITS sentence. The directory is a temporary one, so a removal that
   * still fails after the retries is SAID and then left behind — not thrown past the reason the
   * leg is failing, which is what the first version did.</p>
   */
  const forget = () => {
    try {
      rmSync(root, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    } catch (e) {
      console.error(`seam: could not remove ${root} (${e.code ?? e.message}); it is left behind`);
    }
  };

  /** A failure BEFORE the clean close: the child is still alive and still holds files in `root`. */
  const leave = async (why) => {
    await session.stop();
    forget();
    fail(why);
  };

  let refused;
  try {
    await session.ready;
    refused = answerOf(await session.call('open', { repoPath: absent, branch: 'main' }));
  } catch (e) {
    await leave(`the binary could not be asked to open a missing checkout: ${e.message}`);
  }

  // 1. THE TEETH. The answer to the calling AI is not redacted — it is the AI's own argument, echoed
  //    — so finding the secret here is what proves the writer was HANDED it. Without this, every
  //    assertion below would pass on a server that wrote nothing sensitive because it was never
  //    given anything sensitive.
  const said = String(refused?.error ?? '');
  if (!said.includes('is not a directory on this machine') || !said.includes(secret)) {
    await leave('the refusal did not quote the path it refused, so nothing below could tell a '
      + `redacted secret from one that never reached the writer. It answered: ${said.slice(0, 200)}`);
  }

  // 2. A CLEAN end, by EOF — the only road on which the writer is drained (story 2.3.1).
  const ended = await session.close();
  if (!ended.exited) {
    forget();
    fail(`the server did not exit within ${TIMEOUT_MS} ms of its stdin closing, and was killed. A `
      + 'client that ends its session this way would leave the notices undrained.');
  }

  // 3-6, each a claim that answers the reason it fails, or '' when it holds.
  const sink = noSinkCarries(root, ended.stderr, secret);
  if (sink.why !== '') {
    forget();
    fail(sink.why);
  }
  const crossed = await theRecordCrosses(root, sideDir, noticesFile);
  if (crossed.why !== '') {
    forget();
    fail(crossed.why);
  }

  forget();

  return { title: crossed.title, logs: sink.logs };
}

/**
 * Claim 3: no sink of this run carries the secret — not stderr, and not ONE file under its data root.
 *
 * <p>The notices file is not the only place a refusal's sentence could land, and the operator's
 * standing rule is that no secret reaches a log line at all. (codex, on the plan round.) The companion
 * is what stops "no file carries it" passing on a run that wrote nothing: the run must have left a
 * log file for the check to have covered one.</p>
 */
function noSinkCarries(root, stderr, secret) {
  const shown = (files) => files.map((f) => f.slice(root.length)).join(', ') || '(nothing)';
  if (stderr.includes(secret)) {
    return { why: 'the secret reached the server\'s STDERR in clear, which is where a stdio host sends its console log.', logs: 0 };
  }
  const everything = filesUnder(root);
  const leaking = everything.filter((file) => readFileSync(file).includes(secret));
  if (leaking.length > 0) {
    return { why: `the secret reached the disk in clear, in: ${shown(leaking)}`, logs: 0 };
  }
  const logs = everything.filter((file) => file.endsWith('.log'));
  if (logs.length === 0) {
    return { why: `the run left no log file under ${root}, so "no log carries the secret" was vacuous. It left: ${shown(everything)}`, logs: 0 };
  }

  return { why: '', logs: logs.length };
}

/**
 * Claims 4-6: the record is where the EXTENSION looks, its own reader returns it, and its own parser
 * gives the line back unchanged.
 *
 * <p>The raw bytes come first, so an empty or missing file cannot pass claim 3 by having nothing in
 * it (gemini, on the plan round). The fixed point is the bar `ServerNoticeLine.cs` sets itself —
 * <i>"a line the extension would have WRITTEN from the same record"</i> — checked for the first time
 * against the shipped binary rather than `NoticeTool`.</p>
 */
async function theRecordCrosses(root, sideDir, noticesFile) {
  const failed = (why) => ({ why, title: '' });
  if (!existsSync(noticesFile)) {
    return failed(`the extension looks for the server's notices at ${noticesFile.slice(root.length)} and there is nothing there. `
      + `The run left: ${filesUnder(root).map((f) => f.slice(root.length)).join(', ')}`);
  }
  const bytes = readFileSync(noticesFile, 'utf8');
  const refusedLine = bytes.split('\n').find((line) => line.includes('"code":"refused"'));
  if (refusedLine === undefined || !bytes.endsWith('\n')) {
    return failed(`the notices file has no refused line, or its last line is not terminated: ${bytes.slice(0, 300)}`);
  }
  const record = (await readServerNotices(sideDir)).find((one) => one.code === 'refused' && one.subject === 'OpenAsync');
  if (record === undefined || record.class !== 'refusal' || record.source !== 'coai-mcp'
    || !String(record.title).includes('is not a directory on this machine')) {
    return failed(`the extension's reader did not return the refusal the server wrote. It returned: ${JSON.stringify(record ?? null).slice(0, 400)}`);
  }
  const again = notificationLine(parseNotificationLine(refusedLine));
  if (again !== `${refusedLine}\n`) {
    return failed(`the server's line is not a fixed point of the extension's parser.\n  server:    ${refusedLine}\n  extension: ${again.trimEnd()}`);
  }

  return { why: '', title: String(record.title) };
}

/** Every file under `dir`, recursively. */
function filesUnder(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);

    return statSync(path).isDirectory() ? filesUnder(path) : [path];
  });
}

const refusal = await refusalSeam();
console.log('  ok  a side\'s settings are written and read at the same path, and the root is adopted');

// The SECOND leg: the consultant settings, which cross the same seam and have the same failure.
//
// Five keys travel here and the panel writes each one only when it DIFFERS from its own default, so
// a key the writer forgets is a key nobody misses: the server falls back, a consultation goes to a
// vendor nobody picked, and both suites stay green. What discriminates is a refusal that NAMES the
// vendor — asked for one that is deliberately not configured, the server can only produce that name
// if the map reached it. Drop `COAI_CONSULTANTS` from the writer and this run says `codex` instead.
//
// Nothing is launched and nothing is billed: both answers below are refusals, by design.

/** A checkout with something uncommitted in it, which is what a consultation is about. */
function scratchRepo() {
  const dir = mkdtempSync(join(tmpdir(), 'coai-seam-repo-'));
  // git's OWN stderr is kept, because the guard around this reports what it says: 'ignore' threw
  // away the one sentence that explains which git call refused and why.
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: ['ignore', 'ignore', 'pipe'] });
  git('init', '--initial-branch=main');
  git('config', 'user.email', 'seam@example.invalid');
  git('config', 'user.name', 'seam');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 3; }\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'the committed state');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 4; }\n', 'utf8');

  return dir;
}

/**
 * ONE live server, driven over the transport a real client uses.
 *
 * <p><b>One process for the whole leg, deliberately.</b> A fresh server per call would read the file
 * at startup and prove nothing about the case that actually happens: the panel writes while an MCP
 * client is already holding a server, and the NEXT call has to see it. `PanelServiceHost` re-stamps
 * the settings file on every tool call for exactly that reason, and this is what holds it — raised on
 * this story's plan round, where the first version of this leg spawned twice and could not tell.</p>
 *
 * <p>The three session variables are CLEARED rather than inherited: this script is itself running
 * under an assistant, so the caller kind would otherwise be whatever happens to be driving it, and
 * the leg would assert about a different row of the map on somebody else's machine. Cleared, the kind
 * is `other` — which is also the row a plain MCP client gets.</p>
 */
function serverSession(extraEnv = {}) {
  const child = spawn('dotnet', [binary], {
    env: {
      ...process.env,
      COAI_DATA_DIR: dataDir,
      CLAUDE_CODE_SESSION_ID: '',
      CODEX_SESSION_ID: '',
      GEMINI_CLI_SESSION_ID: '',
      ...extraEnv,
    },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  const waiting = new Map();
  let next = 1;
  let buffered = '';
  let err = '';

  child.stderr.on('data', (b) => {
    err += String(b);
  });
  child.stdout.on('data', (b) => {
    buffered += String(b);
    for (const line of buffered.split('\n').slice(0, -1)) {
      // A throw HERE runs inside a 'data' listener, outside every surrounding try — so it escapes
      // as an uncaught exception: the dotnet child is never killed, the temporary directories stay,
      // and the run prints a stack where its own `seam:` line belongs. A line that is not a frame is
      // not this reader's business. (CodeRabbit, on the pull request.)
      let message;
      try {
        message = JSON.parse(line);
      } catch {
        continue;
      }
      const settle = waiting.get(message.id);
      if (settle !== undefined) {
        waiting.delete(message.id);
        settle(message);
      }
    }
    buffered = buffered.slice(buffered.lastIndexOf('\n') + 1);
  });

  const say = (message) => child.stdin.write(`${JSON.stringify(message)}\n`);
  const ask = async (method, params) => {
    const id = (next += 1);

    return await new Promise((done, broke) => {
      const deadline = setTimeout(() => {
        waiting.delete(id);
        broke(new Error(`${method} did not answer within ${TIMEOUT_MS} ms\n${err}`));
      }, TIMEOUT_MS);
      waiting.set(id, (message) => {
        clearTimeout(deadline);
        done(message);
      });
      child.on('error', (e) => {
        clearTimeout(deadline);
        broke(e);
      });
      say({ jsonrpc: '2.0', id, method, params });
    });
  };

  const call = async (name, args) => await ask('tools/call', { name, arguments: args });

  /**
   * End the session the way a client does — by closing stdin — and wait for the process to leave.
   *
   * <p>NOT `kill()`. A kill is a terminate, which skips the `finally` in `ServeAsync` that drains the
   * notice writer (story 2.3.1), so a leg that killed and then read the notices file would be reading
   * whatever happened to be flushed. A clean end of stdin is what a real client's exit looks like,
   * and it is the only road on which the file is complete.</p>
   *
   * <p><b>Bounded, and it kills on the way out.</b> A server that does not exit on EOF is a finding
   * in its own right, and the answer to it must not be a CI job that hangs until the runner's limit
   * with a dotnet child holding the data directory open. (codex and a local reviewer, on story 2.4's
   * plan round.)</p>
   */
  const close = async () => await new Promise((done) => {
    if (child.exitCode !== null) {
      done({ exited: true, code: child.exitCode, stderr: err });
      return;
    }
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      done({ exited: false, code: null, stderr: err });
    }, TIMEOUT_MS);
    child.on('exit', (code) => {
      clearTimeout(deadline);
      done({ exited: true, code, stderr: err });
    });
    child.stdin.end();
  });

  return {
    ready: ask('initialize', {
      protocolVersion: '2024-11-05',
      capabilities: {},
      clientInfo: { name: 'seam', version: '1' },
    }).then(() => say({ jsonrpc: '2.0', method: 'notifications/initialized' })),
    tools: async () => await ask('tools/list', {}),
    consult: async (repoPath) => await call('consult', {
      repoPath,
      problem: 'The parser returns 3 where 4 is expected, after two fix attempts.',
    }),
    call,
    close,
    /**
     * Kill, and WAIT for the process to be gone — for a leg that is failing.
     *
     * <p>`end()` returns the instant the signal is sent. On Windows the dotnet child still holds
     * its log file and the notices file open for a moment after that, so a leg that removed its
     * directory straight after `end()` got `EPERM` from `rmSync` — and that throw escaped and
     * printed a stack where the leg's own `seam:` sentence belonged. Found by planting a defect the
     * sixth leg exists to catch: it failed on the right condition and reported the wrong one.</p>
     */
    stop: async () => await new Promise((done) => {
      if (child.exitCode !== null || child.signalCode !== null) {
        done();
        return;
      }
      const deadline = setTimeout(done, TIMEOUT_MS);
      child.on('exit', () => {
        clearTimeout(deadline);
        done();
      });
      child.kill('SIGKILL');
    }),
    end: () => child.kill(),
  };
}

/**
 * The stand-in vendor CLI the server's own tests use, so a consultation can ANSWER here.
 *
 * <p>Nothing reaches a model and nothing is billed — but the server is real, the record it writes is
 * real, and so is the row it projects. That is what this leg is about: the database is written by one
 * process and read by another, through `--log`, and the extension's own parser is what reads it.</p>
 */
function fakeCli() {
  for (const configuration of ['Release', 'Debug']) {
    const candidate = join(
      repo, 'src_mcp', 'tests_fakecli', 'bin', configuration, 'net10.0',
      process.platform === 'win32' ? 'FakeCli.exe' : 'FakeCli');
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return '';
}

/** `--log`, as a second process: what the extension actually runs, rather than a read of our own. */
async function readLog() {
  return await new Promise((done, broke) => {
    const child = spawn('dotnet', [binary, '--log', '--paged', '--limit', '50'], {
      env: { ...process.env, COAI_DATA_DIR: dataDir },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '';
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      broke(new Error(`--log did not answer within ${TIMEOUT_MS} ms`));
    }, TIMEOUT_MS);
    child.stdout.on('data', (b) => {
      out += String(b);
    });
    child.on('error', (e) => {
      clearTimeout(deadline);
      broke(e);
    });
    child.on('close', (code) => {
      clearTimeout(deadline);
      if (code !== 0) {
        broke(new Error(`--log exited ${code}`));
        return;
      }
      done(out);
    });
  });
}

/** The tool's own answer, which is a JSON object in the text content of the result. */
function answerOf(reply) {
  const text = reply?.result?.content?.[0]?.text ?? '';
  try {
    return JSON.parse(text);
  } catch {
    return { error: text };
  }
}

// The scratch repository is made INSIDE a guard, for the same reason the handshake below is: git
// can refuse (`--initial-branch` wants git 2.28), and an escaping throw skipped every cleanup this
// leg has and printed a stack where a `seam:` line belongs. (CodeRabbit, on the pull request.)
let repoPath;
try {
  repoPath = scratchRepo();
} catch (e) {
  fail(`the scratch repository could not be made (git 2.28 or later is needed for --initial-branch): ${e.message}`);
}
const session = serverSession();
const consultFail = (why) => {
  session.end();
  rmSync(repoPath, { recursive: true, force: true });
  fail(why);
};

// The handshake is INSIDE the guard, not before it: a `ready` that never answers used to skip the
// only cleanup this leg had, leaving a dotnet child and two temporary directories behind on exactly
// the runs somebody would re-run. (codex, this story's code round.)
try {
  await session.ready;
} catch (e) {
  consultFail(`the binary never finished the MCP handshake: ${e.message}`);
}

// A consultant nobody configured, for the `other` caller kind — the one a plain client gets.
const CHOSEN = 'a-vendor-nobody-configured';
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(
  {
    ...DEFAULTS,
    consult: consultFrom({ other: { vendor: CHOSEN, model: '' } }),
  },
  vendorsFrom([row]),
  '9.9.9',
), 'utf8');

let routed;
try {
  routed = answerOf(await session.consult(repoPath));
} catch (e) {
  consultFail(`the binary could not answer a consult call: ${e.message}`);
}

if (!String(routed.error ?? '').includes(CHOSEN)) {
  consultFail(`the consultant map did not cross the seam. The server answered: ${JSON.stringify(routed).slice(0, 400)}`);
}

// And the switch, which is the other key with no second reader — written under the SAME server, so
// what this proves is the live reload as well as the key.
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(
  { ...DEFAULTS, consult: { ...DEFAULT_CONSULT, enabled: false } },
  vendorsFrom([row]),
  '9.9.9',
), 'utf8');

let switched;
let listed;
try {
  switched = answerOf(await session.consult(repoPath));
  listed = await session.tools();
} catch (e) {
  consultFail(`the binary could not answer a consult call with the feature off: ${e.message}`);
}

if (!String(switched.error ?? '').includes('switched off')) {
  consultFail(`COAI_CONSULT_ENABLED did not cross the seam, or a live server does not re-read it. The server answered: ${JSON.stringify(switched).slice(0, 400)}`);
}

// The tool must still be THERE while it is off. A caller that cannot see a tool cannot be told why
// it is not there, and the documentation promises this in those words.
if (!(listed?.result?.tools ?? []).some((tool) => tool.name === 'consult')) {
  consultFail(`the consult tool vanished from tools/list while the feature was off: ${
    (listed?.result?.tools ?? []).map((tool) => tool.name).join(', ')}`);
}

session.end();

// ----------------------------------------------------------------------------------------------
// The THIRD leg: a consultation that answers, projected by one process and read by another.
//
// Story 4's seam is the database. The server writes a row as the consultation advances and the
// extension never opens SQLite — it runs `--log` and parses the answer — so the two halves can each
// be green while nothing crosses. Here the real binary runs a consultation against the stand-in CLI
// its own tests use (no model, no money), and then the extension's own `parseLog` reads it back out
// of a second process.

const cli = fakeCli();
if (cli === '') {
  consultFail('no FakeCli build found. Run: dotnet build src_mcp/tests_fakecli/FakeCli.csproj');
}

writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(
  DEFAULTS,
  vendorsFrom([{
    id: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', enabled: true, plan: true, code: true,
    baseUrl: '', executablePath: cli, pricePerMillionIn: 0, pricePerMillionOut: 0,
  }]),
  '9.9.9',
), 'utf8');

const answering = serverSession({
  FAKECLI_MODE: 'vendor',
  FAKECLI_STDOUT: `${JSON.stringify({ type: 'thread.started', thread_id: '0199-seam' })}\n`,
  FAKECLI_OUTFILE_TEXT: 'Print the token stream: your loop stops one short.',
});
try {
  await answering.ready;
} catch (e) {
  answering.end();
  consultFail(`the binary never finished the MCP handshake for the answering leg: ${e.message}`);
}

let answered;
try {
  answered = answerOf(await answering.consult(repoPath));
} catch (e) {
  answering.end();
  consultFail(`the binary could not run a consultation against the stand-in CLI: ${e.message}`);
}
answering.end();

if (typeof answered.consultationId !== 'string' || answered.consultationId.length === 0) {
  consultFail(`the consultation did not open. The server answered: ${JSON.stringify(answered).slice(0, 400)}`);
}

// And now the other side of the seam: a SECOND process, `--log`, and the extension's own parser.
const { parseLog } = await import('../out/roundsDb.js');
let logged;
try {
  logged = parseLog(await readLog(), true);
} catch (e) {
  consultFail(`--log could not be read: ${e.message}`);
}

const consulted = (logged.consultations ?? []).find((one) => one.id === answered.consultationId);
if (consulted === undefined) {
  consultFail(`the consultation never reached the log. It holds: ${
    JSON.stringify((logged.consultations ?? []).map((one) => one.id)).slice(0, 200)}`);
}
if (!String(consulted.advice).includes('token stream')) {
  consultFail(`the row reached the log without its advice: ${JSON.stringify(consulted).slice(0, 400)}`);
}

// ----------------------------------------------------------------------------------------------
// The FOURTH leg: the DEFINITION crosses — a consultant with NO reviewer row answers.
//
// Story B4 of PLAN_the_consultant_has_its_own_vendors. Before it `COAI_CONSULTANTS` carried
// `{vendor, model}` and the server looked the vendor up among the REVIEWER rows, so a consultant
// nobody had a row for was refused "not configured" — the second leg above leans on exactly that
// refusal. A DEFINITION carries its own runtime and CLI path, and a server that reads them needs no
// row at all. So: no reviewer row under this id, a definition naming the stand-in CLI, and a
// consultation that ANSWERS. Three ways this fails, and each is a sentence the refusal names: the
// writer drops the three fields (the server says "not configured"); the server does not read them
// (the same sentence — which is what the released mcp-v0.22.0 answers, measured 2026-09-15, and what
// this leg says with `COAI_MCP_DLL` pointed at that build); or the two halves spell a key differently
// (System.Text.Json skips a member it does not declare, silently, and the launch goes to PATH).
//
// The advice text is this leg's own, not the third leg's: an assertion on "token stream" here would
// pass on the third leg's answer if the two consultations were ever confused.

const DEFINED = 'a-consultant-with-no-reviewer-row';
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(
  {
    ...DEFAULTS,
    consult: consultFrom({ other: { vendor: DEFINED, runtime: 'codex', model: 'gpt-5.6-luna', baseUrl: '', executablePath: cli } }),
  },
  vendorsFrom([row]),
  '9.9.9',
), 'utf8');

const defined = serverSession({
  FAKECLI_MODE: 'vendor',
  FAKECLI_STDOUT: `${JSON.stringify({ type: 'thread.started', thread_id: '0199-seam-defined' })}\n`,
  FAKECLI_OUTFILE_TEXT: 'The definition crossed: this answer came through the CLI path the entry itself named.',
});
try {
  await defined.ready;
} catch (e) {
  defined.end();
  consultFail(`the binary never finished the MCP handshake for the definition leg: ${e.message}`);
}

let throughDefinition;
try {
  throughDefinition = answerOf(await defined.consult(repoPath));
} catch (e) {
  defined.end();
  consultFail(`the binary could not answer a consult call on a definition: ${e.message}`);
}
defined.end();

if (typeof throughDefinition.consultationId !== 'string' || throughDefinition.consultationId.length === 0) {
  consultFail(`the definition did not cross the seam — the server could not consult through a consultant with no reviewer row. It answered: ${
    JSON.stringify(throughDefinition).slice(0, 400)}`);
}
// The positive half: the answer came through the CLI path the DEFINITION named. A consultationId
// alone would pass for a server that resolved the id some other way.
if (!String(throughDefinition.advice).includes('The definition crossed')) {
  consultFail(`the consultation opened, but not through the definition's own CLI path: ${JSON.stringify(throughDefinition).slice(0, 400)}`);
}

rmSync(repoPath, { recursive: true, force: true });
rmSync(dataDir, { recursive: true, force: true });
console.log(`seam: ok — the server read the row as a remote vendor and knows it by its server's name.`);
console.log(`seam: its note was "${reported.note}"`);
console.log(`seam: the consultant map crossed too — "${String(routed.error).slice(0, 120)}…"`);
console.log(`seam: and the switch — "${String(switched.error).slice(0, 120)}…"`);
console.log(`seam: and a consultation the binary RAN was read back out of --log — "${String(consulted.advice).slice(0, 60)}…"`);
console.log(`seam: and a consultant DEFINED with no reviewer row answered through its own CLI path — "${String(throughDefinition.advice).slice(0, 60)}…"`);
console.log(`seam: and a REAL refusal carrying a secret was written with it taken out, read back by the extension's own reader, and survived its parser byte for byte — "${refusal.title.slice(0, 90)}…" (${refusal.logs} log file(s) checked too)`);
console.log(`seam: asked ${binary}`);
