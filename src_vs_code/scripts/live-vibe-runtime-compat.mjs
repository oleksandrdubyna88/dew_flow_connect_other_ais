/**
 * The released-half check for S2 of todo/PLAN_vibe_and_kimi_runtimes.md (D13, path 1): a `vibe` row, written by this
 * extension's REAL send path — the settings file `coai-mcp` reads — against the last RELEASED `coai-mcp`, which does
 * not know the runtime.
 *
 * <p>No unit test can answer this: the suite holds one build of each half, and the question is what a server built
 * BEFORE the runtime does with the file this build writes. Three seams:</p>
 *
 * <ol>
 *   <li><b>The gate.</b> Told the installed server is the released one, the writer holds the vibe row back, and the
 *       released server serves its other rows exactly as it serves a file that never had the row.</li>
 *   <li><b>Why the gate is there.</b> The same rows with the gate bypassed (an unknown version): the released server
 *       does not review with the vibe row — since mcp 0.44.0 it refuses it by name, before that it ran it as codex. A
 *       row that would only ever be unavailable is the one the card explains instead.</li>
 *   <li><b>This server over the same file</b>, when this checkout has one built: it knows the runtime, so the row is
 *       listed and not refused as unknown.</li>
 * </ol>
 *
 * <h2>How to run it</h2>
 *
 * <pre>cd src_vs_code &amp;&amp; npm run test:vibe-runtime-compat</pre>
 *
 * <p>It downloads the newest published `mcp-v*` with `gh` (`--server-tag=` for another, or `COAI_OLD_SERVER=<path>`).
 * Seam 3 uses this checkout's Debug build when one exists (`dotnet build dew_flow_connect_other_ais.slnx -c Debug`) and
 * says plainly when it does not. NOT in CI: it needs the network and a released artefact, and a check that silently
 * skips in CI reports success for having done nothing. `--providers` asks each configured CLI its `--version`; no
 * round runs and nothing is sent to a model.</p>
 *
 * <p>Exit codes: <b>0</b> the boundary holds · <b>1</b> it does not, and says which assertion · <b>2</b> the released
 * server or this checkout's compiled extension could not be obtained — an environment answer, not a contract one.</p>
 */
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { flag, HERE, newServer, releasedServer, run } from './releasedHalves.mjs';

const broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken.push(what);
  }
};

const ours = path.join(HERE, 'src_vs_code');
if (!existsSync(path.join(ours, 'out', 'runtimeSince.js'))) {
  console.log('this checkout\'s extension is not compiled — run `npm run compile` in src_vs_code first');
  process.exit(2);
}
const load = async (name) => await import(pathToFileURL(path.join(ours, 'out', `${name}.js`)).href);
const { serverSettingsJson } = await load('serverSettingsFile');
const { DEFAULTS } = await load('settingsShape');
const { DEFAULT_VENDORS } = await load('vendors');
const { VIBE_RUNTIME_SINCE, runtimeOnServer } = await load('runtimeSince');

const work = mkdtempSync(path.join(tmpdir(), 'coai-vibe-runtime-compat-'));
mkdirSync(path.join(work, 'server'));
const old = releasedServer(path.join(work, 'server'), flag('server-tag', ''));
if (old.length === 0) {
  console.log('the released server could not be obtained; nothing here is a contract result');
  process.exit(2);
}
const oldVersion = (await run(old, ['--version'], work)).out.trim().split(/\s+/u).pop() ?? '';
// The settings file is the only source the servers below may read: an inherited variable would win.
const noEnv = { COAI_VENDORS: '', COAI_PROVIDERS: '' };

/** A data directory holding one settings file. */
function dataDir(name, settingsJson) {
  const dir = path.join(work, name);
  mkdirSync(dir);
  writeFileSync(path.join(dir, 'settings.json'), settingsJson);

  return dir;
}

/** The rows `--providers` answered with. */
const rowsOf = (text) => JSON.parse(text || '{}').providers ?? [];

const VIBE = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};
const withVibe = [...DEFAULT_VENDORS, VIBE];

say(!runtimeOnServer('vibe', oldVersion),
  `the released server (${oldVersion}) is older than VIBE_RUNTIME_SINCE (${VIBE_RUNTIME_SINCE}) — the case this checks`,
  'pass --server-tag= for a release without the runtime');

// ---------- 1. The gate: this build's REAL send path, told the installed server is the released one ----------
const gated = serverSettingsJson(DEFAULTS, withVibe, 'compat-new', oldVersion);
const gatedRows = JSON.parse(JSON.parse(gated).COAI_VENDORS ?? '[]');
say(gatedRows.every((one) => one.id !== 'vibe') && gatedRows.length === DEFAULT_VENDORS.length,
  'the settings file holds the vibe row back and keeps every other row', gatedRows.map((one) => one.id).join(', '));
const served = await run(old, ['--providers'], dataDir('gated', gated), noEnv);
const plain = await run(old, ['--providers'], dataDir('plain', serverSettingsJson(DEFAULTS, [...DEFAULT_VENDORS], 'compat-new', oldVersion)), noEnv);
say(served.code === 0, `the released server answers --providers over that file`, `exit ${served.code}`);
say(JSON.stringify(rowsOf(served.out)) === JSON.stringify(rowsOf(plain.out)),
  'and serves its other rows exactly as it serves a file that never had the vibe row',
  rowsOf(served.out).map((one) => `${one.provider}:${one.auth}`).join(', '));

// ---------- 2. Why: the same rows with the gate bypassed ----------
const ungated = serverSettingsJson(DEFAULTS, withVibe, 'compat-new', '');
const told = await run(old, ['--providers'], dataDir('ungated', ungated), noEnv);
const vibeThere = rowsOf(told.out).find((one) => one.provider === 'vibe');
say(told.code === 0 && vibeThere !== undefined, 'with the gate bypassed the released server is handed the vibe row', `exit ${told.code}`);
say(vibeThere?.auth === 'unavailable' && /vibe/u.test(vibeThere?.note ?? ''),
  'and does not review with it — it refuses the runtime by name', vibeThere?.note ?? '(no row)');

// ---------- 3. This server over the same file ----------
const built = newServer();
if (built.length === 0) {
  console.log('SKIP  seam 3 — this checkout has no built server (`dotnet build dew_flow_connect_other_ais.slnx -c Debug`)');
} else {
  const ours3 = await run(built, ['--providers'], dataDir('ours', ungated), noEnv);
  const vibeHere = rowsOf(ours3.out).find((one) => one.provider === 'vibe');
  say(ours3.code === 0 && vibeHere !== undefined, 'this checkout\'s server lists the vibe row', `exit ${ours3.code}`);
  say(!/not one this build of coai-mcp runs/u.test(vibeHere?.note ?? ''),
    'and knows the runtime — it is not refused as unknown', `${vibeHere?.auth ?? ''}: ${vibeHere?.note ?? '(no row)'}`);
}

rmSync(work, { recursive: true, force: true });
console.log('');
if (broken.length === 0) {
  console.log('The boundary holds.');
  process.exit(0);
}
console.log(`The boundary is BROKEN: ${broken.length} assertion(s) failed.`);
process.exit(1);
