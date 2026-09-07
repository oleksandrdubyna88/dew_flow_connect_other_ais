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
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const TIMEOUT_MS = 30_000;

const repo = resolve(import.meta.dirname, '..', '..');
const binary = process.env['COAI_MCP_DLL']
  ?? join(repo, 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', 'coai-mcp.dll');

const { serverSettingsJson } = await import('../out/serverSettingsFile.js');
const { vendorsFrom } = await import('../out/vendors.js');
const { DEFAULTS } = await import('../out/settingsShape.js');
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
  baseUrl: 'PLACEHOLDER',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-'));
const server = catalogServer();
await new Promise((done) => server.listen(0, '127.0.0.1', done));
const address = `http://127.0.0.1:${server.address().port}`;
ROW.baseUrl = address;

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
writeFileSync(join(dataDir, 'settings.json'), serverSettingsJson(DEFAULTS, vendorsFrom([ROW]), '9.9.9'), 'utf8');

let answer;
try {
  answer = await providers();
} catch (e) {
  fail(`could not ask the server — build it first (dotnet build src_mcp/src/CoaiMcp.csproj): ${e.message}`);
}

const row = (answer.providers ?? []).find((p) => p.provider === 'remsoftdev-claude');
if (row === undefined) {
  fail(`the server did not see the row at all. It saw: ${(answer.providers ?? []).map((p) => p.provider).join(', ')}`);
}

// The whole point, and it has been watched failing. With `remoteVendor` removed from `vendorsEnv`
// this run answers: "the Team server at http://127.0.0.1:… does not offer a vendor called
// 'remsoftdev-claude' — it offers claude". That is the bug of 2026-09-07, reproduced end to end by
// the two real implementations rather than described by a fixture.
if (row.note.includes('does not offer a vendor called') || row.note.includes('does not record which vendor')) {
  fail(`remoteVendor did not cross the seam — the server was asked for the ROW ID. It said: ${row.note}`);
}

// The positive half: it was asked about `claude`, found it, and read its accounts. Asserting only
// the absence of a refusal would pass for a row the server never resolved at all.
if (row.auth !== 'server token') {
  fail(`the row did not resolve to a usable Team-server vendor. auth=${row.auth}, note=${row.note}`);
}

// And the row must be a REMOTE one on the far side: a `codex` classification is the other half of
// the same failure, and it reads as "install it with npm install -g @openai/codex".
if (row.note.includes('npm install')) {
  fail(`the server classified a Team-server row as a codex vendor. It said: ${row.note}`);
}

server.close();
rmSync(dataDir, { recursive: true, force: true });
console.log(`seam: ok — the server read the row as a remote vendor and knows it by its server's name.`);
console.log(`seam: its note was "${row.note}"`);
