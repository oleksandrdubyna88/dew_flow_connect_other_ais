#!/usr/bin/env node
/**
 * What an OLDER `coai-mcp` does with a consultant DEFINITION on the wire — story B4's measurement.
 *
 * <p>`.agents/PROJECT.md`: a wire field added on one side must be measured against the OLD other
 * side before it ships. `COAI_CONSULTANTS` gained three members per entry (runtime, baseUrl,
 * executablePath — `PLAN_the_consultant_has_its_own_vendors`); this drives a server built BEFORE
 * them over stdio, exactly as `run-seam.mjs` drives the current one, and records the `consult` reply
 * verbatim for each cell. Run 2026-09-15 against mcp-v0.22.0 (4fe3cb02), the last released server;
 * the replies are in `research/module_server.md`, and the panel's `consultantSkewNote` says what they
 * mean while such a server is installed.</p>
 *
 * <p><b>The input is the PRODUCT's</b>: every `COAI_VENDORS` / `COAI_CONSULTANTS` value below comes
 * out of the extension's own `envBlock`, over its own readers — never a hand-written literal. The
 * first draft of this harness wrote the definition by hand and, read back against `envBlock`, had the
 * keys in a different order. Same members, same values, and member order means nothing to
 * `System.Text.Json` — but a measurement whose input differs from what the product sends is a
 * measurement of something else, so the recorded run is this one.</p>
 *
 * <p><b>Pinned</b> in every cell, in the harness: a fresh `COAI_DATA_DIR`; the three caller-session
 * variables cleared so the caller kind is `other`; one scratch repository with one uncommitted
 * change; one problem text; the stand-in CLI in vendor mode answering `thread.started` and a fixed
 * advice, recording its argv; a 30 s deadline; everything as ENVIRONMENT, which wins over the settings
 * file on every server so far, so that layer is out of the picture. <b>Varied</b>: the vendor rows
 * and the consultant map, per cell. The prediction for each cell is in {@link CELLS}, written before
 * the first run, and the output prints it beside what was observed.</p>
 *
 * <pre>
 *   COAI_OLD_MCP_DLL=…/coai-mcp.dll COAI_OLD_FAKECLI=…/FakeCli.exe node scripts/measure-consultant-skew.mjs
 * </pre>
 *
 * Build the older half in a throwaway worktree first:
 * <pre>
 *   git worktree add --detach /tmp/wt-old mcp-v0.22.0
 *   dotnet build /tmp/wt-old/src_mcp/src/CoaiMcp.csproj -c Debug
 *   dotnet build /tmp/wt-old/src_mcp/tests_fakecli/FakeCli.csproj -c Debug
 * </pre>
 *
 * <p>Refuses rather than skips when either path is missing: a measurement that silently ran against
 * the current build would report the skew as absent.</p>
 */
import { execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const TIMEOUT_MS = 30_000;

const binary = process.env['COAI_OLD_MCP_DLL'] ?? '';
const fakeCli = process.env['COAI_OLD_FAKECLI'] ?? '';
if (!existsSync(binary) || !existsSync(fakeCli)) {
  console.error('measure: set COAI_OLD_MCP_DLL and COAI_OLD_FAKECLI to an OLDER build’s coai-mcp.dll and FakeCli — see the header of this script');
  process.exit(1);
}

const { envBlock, DEFAULTS } = await import('../out/settingsShape.js');
const { consultSettingsFrom } = await import('../out/consultSettings.js');
const { vendorsFrom } = await import('../out/vendors.js');

/** The consultant settings as the PANEL reads them from a stored map — the reader, never a literal. */
const consultFrom = (consultants) => consultSettingsFrom((section) => (section === 'consultants' ? consultants : undefined));

/** A reviewer row as `coai.vendors` holds one; `vendorsFrom` fills in the rest. */
const codexRow = (over = {}) => ({ id: 'codex', runtime: 'codex', model: 'gpt-5.6-terra', enabled: true, executablePath: fakeCli, ...over });
const remoteRow = {
  id: 'remsoftdev-claude', runtime: 'remote', model: 'haiku', enabled: true, baseUrl: 'http://127.0.0.1:9/', remoteVendor: 'claude',
};

// A CLI path that does not exist: launched, it fails loudly, so a run that answers proves the ROW's
// path was used instead.
const definitionOnlyCli = join(resolve(tmpdir()), 'coai-measure-definition-only', 'codex.exe');

const CELLS = [
  {
    id: 'control',
    title: 'CONTROL — a legacy pair {vendor, model}, the shape every extension before B4 sent; the row on a different model',
    vendors: [codexRow()],
    // The ONE literal here, and it has to be: since B4 `envBlock` resolves a legacy entry and sends
    // the definition, so the product cannot produce the bytes an older extension sent. This is what
    // the arms are compared AGAINST — a definition arm that does not differ from it in effect is a
    // definition the old server ignored.
    wireLiteral: JSON.stringify({ other: { vendor: 'codex', model: 'gpt-5.6-luna' } }),
    consultants: { other: { vendor: 'codex', model: 'gpt-5.6-luna' } },
    prediction: 'RUNS: FakeCli (the row’s CLI path) is launched with -m gpt-5.6-luna, the entry’s model, which the old DTO carries; the reply holds a consultationId and the stand-in’s advice.',
  },
  {
    id: 'i-a',
    title: '(i-a) a DEFINITION; the row carries a different model and a different CLI path; no base URL on either side',
    vendors: [codexRow()],
    consultants: { other: { vendor: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', baseUrl: '', executablePath: definitionOnlyCli } },
    prediction: 'RUNS, identical in effect to the control: the ROW’s CLI path (FakeCli) with -m gpt-5.6-luna. The plan predicted "the ROW’s model"; the entry’s model crosses because `model` was on the wire before B3 — only runtime, baseUrl and executablePath are unknown members, and System.Text.Json skips them. The definition’s CLI path is never launched.',
  },
  {
    id: 'i-b',
    title: '(i-b) the literal shape asked for: a DEFINITION whose row carries a different model AND a custom base URL',
    vendors: [codexRow({ baseUrl: 'http://127.0.0.1:9/row-endpoint' })],
    consultants: { other: { vendor: 'codex', runtime: 'codex', model: 'gpt-5.6-luna', baseUrl: '', executablePath: fakeCli } },
    prediction: 'REFUSED, not run: the old server takes the ROW, and a codex row with a base URL is CannotConsult — "runs on ‘codex’ with a custom endpoint, which cannot hold a consultation in this build". The plan predicted "runs on the ROW’s model and endpoint"; its own fact #1 says a codex custom endpoint cannot consult in this build, so both cannot hold.',
  },
  {
    id: 'ii',
    title: '(ii) a DEFINITION whose id has NO reviewer row at all (a custom id on the claude runtime)',
    vendors: [codexRow()],
    consultants: { other: { vendor: 'anthropic-direct', runtime: 'claude', model: 'claude-opus-4-6', baseUrl: '', executablePath: fakeCli } },
    prediction: 'REFUSED loudly: "the consultant for a ‘other’ caller is the vendor ‘anthropic-direct’, which is not configured — pick an enabled vendor row …". The current server RUNS this definition on the claude CLI.',
  },
  {
    id: 'iii',
    title: '(iii) a DEFINITION with runtime "remote" whose id names a Team-server row',
    vendors: [codexRow(), remoteRow],
    consultants: { other: { vendor: 'remsoftdev-claude', runtime: 'remote', model: 'haiku', baseUrl: 'http://127.0.0.1:9/', executablePath: '' } },
    prediction: 'REFUSED by CannotConsult, from the ROW’s runtime (the definition’s own `runtime` is never read): "the vendor ‘remsoftdev-claude’ runs on ‘remote’ with a custom endpoint, which cannot hold a consultation in this build". Nothing is launched.',
  },
];

function scratchRepo() {
  const dir = mkdtempSync(join(tmpdir(), 'coai-measure-repo-'));
  const git = (...args) => execFileSync('git', args, { cwd: dir, stdio: ['ignore', 'ignore', 'pipe'] });
  git('init', '--initial-branch=main');
  git('config', 'user.email', 'measure@example.invalid');
  git('config', 'user.name', 'measure');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 3; }\n', 'utf8');
  git('add', '-A');
  git('commit', '-m', 'the committed state');
  writeFileSync(join(dir, 'Parser.cs'), 'class Parser { int Count() => 4; }\n', 'utf8');

  return dir;
}

/** One server over stdio, `run-seam.mjs`'s shape; `end` waits for the process to be GONE (Windows holds the log file until then). */
function serverSession(env) {
  const child = spawn('dotnet', [binary], { env, stdio: ['pipe', 'pipe', 'pipe'] });
  const waiting = new Map();
  let next = 1;
  let buffered = '';
  let err = '';
  child.stderr.on('data', (b) => { err += String(b); });
  child.stdout.on('data', (b) => {
    buffered += String(b);
    for (const line of buffered.split('\n').slice(0, -1)) {
      let message;
      try { message = JSON.parse(line); } catch { continue; }
      const settle = waiting.get(message.id);
      if (settle !== undefined) { waiting.delete(message.id); settle(message); }
    }
    buffered = buffered.slice(buffered.lastIndexOf('\n') + 1);
  });
  const say = (message) => child.stdin.write(`${JSON.stringify(message)}\n`);
  const ask = (method, params) => {
    const id = (next += 1);

    return new Promise((done, broke) => {
      const deadline = setTimeout(() => { waiting.delete(id); broke(new Error(`${method} did not answer within ${TIMEOUT_MS} ms\n${err}`)); }, TIMEOUT_MS);
      waiting.set(id, (message) => { clearTimeout(deadline); done(message); });
      child.on('error', (e) => { clearTimeout(deadline); broke(e); });
      say({ jsonrpc: '2.0', id, method, params });
    });
  };
  const closed = new Promise((done) => child.on('close', done));

  return {
    ready: ask('initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'measure-consultant-skew', version: '1' } })
      .then(() => say({ jsonrpc: '2.0', method: 'notifications/initialized' })),
    consult: (repoPath) => ask('tools/call', { name: 'consult', arguments: { repoPath, problem: 'The parser returns 3 where 4 is expected, after two fix attempts.' } }),
    end: async () => { child.kill(); await closed; },
  };
}

/** Best effort: a directory Windows still holds is left behind and named, never a lost result. */
function tidy(dir) {
  try { rmSync(dir, { recursive: true, force: true }); } catch (e) { console.error(`measure: could not remove ${dir}: ${e.message}`); }
}

const repoPath = scratchRepo();
const results = [];
for (const cell of CELLS) {
  const dataDir = mkdtempSync(join(tmpdir(), `coai-measure-${cell.id}-`));
  const recordDir = mkdtempSync(join(tmpdir(), `coai-measure-argv-${cell.id}-`));
  // THE PRODUCT'S BYTES: the rows through `vendorsFrom`, the map through the reader, both through `envBlock`.
  const block = envBlock({ ...DEFAULTS, consult: consultFrom(cell.consultants) }, vendorsFrom(cell.vendors));
  const env = {
    ...process.env,
    COAI_DATA_DIR: dataDir,
    CLAUDE_CODE_SESSION_ID: '', CODEX_SESSION_ID: '', GEMINI_CLI_SESSION_ID: '',
    COAI_VENDORS: block['COAI_VENDORS'] ?? '',
    COAI_CONSULTANTS: cell.wireLiteral ?? block['COAI_CONSULTANTS'] ?? '',
    FAKECLI_MODE: 'vendor',
    FAKECLI_RECORD_DIR: recordDir,
    FAKECLI_STDOUT: `${JSON.stringify({ type: 'thread.started', thread_id: '0199-measure' })}\n`,
    FAKECLI_OUTFILE_TEXT: 'Print the token stream: your loop stops one short.',
  };
  const session = serverSession(env);
  let reply;
  let failure = '';
  try {
    await session.ready;
    const answer = await session.consult(repoPath);
    reply = answer?.result?.content?.[0]?.text ?? JSON.stringify(answer);
  } catch (e) {
    failure = e.message;
  } finally {
    await session.end();
  }
  const launches = readdirSync(recordDir).filter((f) => f.endsWith('.argv')).map((f) => readFileSync(join(recordDir, f), 'utf8').split('\0'));
  const models = launches.map((argv) => { const at = argv.indexOf('-m'); return at >= 0 ? argv[at + 1] : '(no -m)'; });
  results.push({
    cell: cell.id, title: cell.title,
    sent: { COAI_VENDORS: env.COAI_VENDORS, COAI_CONSULTANTS: env.COAI_CONSULTANTS },
    prediction: cell.prediction,
    observed: { reply, failure, fakeCliLaunches: launches.length, modelsLaunched: models },
  });
  tidy(dataDir);
  tidy(recordDir);
}
tidy(repoPath);

const out = { subject: { binary, fakeCli }, harness: import.meta.filename, ranAt: new Date().toISOString(), results };
const outFile = process.env['COAI_MEASURE_OUT'] ?? '';
if (outFile.length > 0) {
  writeFileSync(outFile, JSON.stringify(out, null, 2), 'utf8');
}
for (const r of results) {
  console.log(`\n=== ${r.cell}: ${r.title}`);
  console.log(`SENT COAI_VENDORS=${r.sent.COAI_VENDORS}`);
  console.log(`SENT COAI_CONSULTANTS=${r.sent.COAI_CONSULTANTS}`);
  console.log(`PREDICTED: ${r.prediction}`);
  console.log(`OBSERVED reply: ${r.observed.reply ?? '(none)'}`);
  if (r.observed.failure) {
    console.log(`OBSERVED failure: ${r.observed.failure}`);
  }
  console.log(`OBSERVED FakeCli launches: ${r.observed.fakeCliLaunches}, models: ${JSON.stringify(r.observed.modelsLaunched)}`);
}
console.log(`\nmeasure: asked ${binary}`);
