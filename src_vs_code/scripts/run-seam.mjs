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
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
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
const { DEFAULT_CONSULT } = await import('../out/consultSettings.js');
const { tokenFileName } = await import('../out/teamServers.js');

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
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: 'ignore' });
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
 * One `consult` call against the real binary, over the transport a real client uses.
 *
 * <p>The three session variables are CLEARED rather than inherited: this process is itself running
 * under an assistant, so the caller kind would otherwise be whatever happens to be driving the
 * script, and the leg would assert about a different row of the map on somebody else's machine.</p>
 */
async function consult(repoPath, kindVariable) {
  const child = spawn('dotnet', [binary], {
    env: {
      ...process.env,
      COAI_DATA_DIR: dataDir,
      CLAUDE_CODE_SESSION_ID: '',
      CODEX_SESSION_ID: '',
      GEMINI_CLI_SESSION_ID: '',
      ...(kindVariable === '' ? {} : { [kindVariable]: 'seam-session' }),
    },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  const say = (message) => child.stdin.write(`${JSON.stringify(message)}\n`);

  return await new Promise((done, broke) => {
    let buffered = '';
    let err = '';
    const deadline = setTimeout(() => {
      child.kill('SIGKILL');
      broke(new Error(`consult did not answer within ${TIMEOUT_MS} ms\n${err}`));
    }, TIMEOUT_MS);

    child.stderr.on('data', (b) => {
      err += String(b);
    });
    child.stdout.on('data', (b) => {
      buffered += String(b);
      for (const line of buffered.split('\n').slice(0, -1)) {
        const message = JSON.parse(line);
        if (message.id === 1) {
          say({ jsonrpc: '2.0', method: 'notifications/initialized' });
          say({
            jsonrpc: '2.0',
            id: 2,
            method: 'tools/call',
            params: {
              name: 'consult',
              arguments: { repoPath, problem: 'The parser returns 3 where 4 is expected, after two fix attempts.' },
            },
          });
        }
        if (message.id === 2) {
          clearTimeout(deadline);
          child.kill();
          done(message);
        }
      }
      buffered = buffered.slice(buffered.lastIndexOf('\n') + 1);
    });
    child.on('error', (e) => {
      clearTimeout(deadline);
      broke(e);
    });

    say({
      jsonrpc: '2.0',
      id: 1,
      method: 'initialize',
      params: { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'seam', version: '1' } },
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

const repoPath = scratchRepo();
const consultFail = (why) => {
  rmSync(repoPath, { recursive: true, force: true });
  fail(why);
};

// A consultant nobody configured, for the `other` caller kind — the one a plain client gets.
const CHOSEN = 'a-vendor-nobody-configured';
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(
  {
    ...DEFAULTS,
    consult: { ...DEFAULT_CONSULT, byCaller: { ...DEFAULT_CONSULT.byCaller, other: { vendor: CHOSEN, model: '' } } },
  },
  vendorsFrom([row]),
  '9.9.9',
), 'utf8');

let routed;
try {
  routed = answerOf(await consult(repoPath, ''));
} catch (e) {
  consultFail(`the binary could not answer a consult call: ${e.message}`);
}

if (!String(routed.error ?? '').includes(CHOSEN)) {
  consultFail(`the consultant map did not cross the seam. The server answered: ${JSON.stringify(routed).slice(0, 400)}`);
}

// And the switch, which is the other key with no second reader: off must refuse BY NAME rather than
// leave a stuck agent to guess why nothing came back.
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(
  { ...DEFAULTS, consult: { ...DEFAULT_CONSULT, enabled: false } },
  vendorsFrom([row]),
  '9.9.9',
), 'utf8');

let switched;
try {
  switched = answerOf(await consult(repoPath, 'CLAUDE_CODE_SESSION_ID'));
} catch (e) {
  consultFail(`the binary could not answer a consult call with the feature off: ${e.message}`);
}

if (!String(switched.error ?? '').includes('switched off')) {
  consultFail(`COAI_CONSULT_ENABLED did not cross the seam. The server answered: ${JSON.stringify(switched).slice(0, 400)}`);
}

rmSync(repoPath, { recursive: true, force: true });
rmSync(dataDir, { recursive: true, force: true });
console.log(`seam: ok — the server read the row as a remote vendor and knows it by its server's name.`);
console.log(`seam: its note was "${reported.note}"`);
console.log(`seam: the consultant map crossed too — "${String(routed.error).slice(0, 120)}…"`);
console.log(`seam: and the switch — "${String(switched.error).slice(0, 120)}…"`);
console.log(`seam: asked ${binary}`);
