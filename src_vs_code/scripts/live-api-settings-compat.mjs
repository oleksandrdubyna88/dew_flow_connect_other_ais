/**
 * The released-half check for S3.8 of the feature-review plan (the extension half): an `api` row's per-model
 * settings — `effort`, `thinking`, `reviewMinutes` on the row, `api` on `providers` — measured against the
 * RELEASED artefact a person may still be running on the other side.
 *
 * <p>No unit test can answer these: the suite holds one build of each half, and the question is about two
 * DIFFERENT builds meeting on one data directory. Four seams:</p>
 *
 * <ol>
 *   <li><b>This extension's settings → the released server.</b> Against a server it cannot version the panel
 *       writes the three fields (unknown is not old). The released `coai-mcp` has no such fields: it must
 *       read the whole vendor list exactly as it reads it without them — not refuse it and fall back to its
 *       defaults — and its `providers` must carry no `api`, which is what hides the controls on the card.</li>
 *   <li><b>The version gate.</b> Told the server is the released one, the writer holds the three fields back
 *       and every row still crosses.</li>
 *   <li><b>A rollback to the released extension.</b> It reads the rows this build stored, keeps every row, and
 *       drops the three fields it has never heard of — so what it writes carries none.</li>
 *   <li><b>This server over what this extension wrote</b> — the live check of the contract with two
 *       implementations: the row's own values come back as the module's EFFECTIVE settings, a value the
 *       module does not take comes back as its refusal, and this extension's parser reads the report the
 *       card is drawn from.</li>
 * </ol>
 *
 * <h2>How to run it</h2>
 *
 * <pre>dotnet build dew_flow_connect_other_ais.slnx -c Debug &amp;&amp; cd src_vs_code &amp;&amp; npm run test:api-settings-compat</pre>
 *
 * <p>It downloads the newest published `mcp-v*` with `gh` and builds the newest published `extension-v*` from
 * its tag (`releasedHalves.mjs` says why built rather than downloaded); pass `--server-tag=` /
 * `--extension-tag=` for another release, or `COAI_OLD_SERVER=<path>`. NOT in CI: it needs the network and
 * released artefacts, and a check that silently skips in CI reports success for having done nothing.</p>
 *
 * <p>Exit codes: <b>0</b> the boundary holds · <b>1</b> it does not, and says which assertion · <b>2</b> a
 * released half or this checkout's build could not be obtained — an environment answer, not a contract one.</p>
 */
import { existsSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { flag, HERE, newServer, releasedExtension, releasedServer, run } from './releasedHalves.mjs';

const broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken.push(what);
  }
};

/** The compiled modules of one extension tree — this checkout's, or a released one's. */
async function modulesOf(root) {
  const load = async (name) => await import(pathToFileURL(path.join(root, 'out', `${name}.js`)).href);

  return {
    vendors: await load('vendors'),
    settings: await load('settingsShape'),
    file: await load('serverSettingsFile'),
    providers: await load('providers'),
  };
}

/** The rows `--providers` answered with. */
const rowsOf = (text) => JSON.parse(text || '{}').providers ?? [];

/** What a providers answer says, minus the clock. */
const withoutClock = (text) => JSON.stringify(rowsOf(text));

/** A data directory holding one settings file, the only source the servers below may read. */
function dataDir(work, name, settingsJson) {
  const dir = path.join(work, name);
  mkdirSync(dir);
  writeFileSync(path.join(dir, 'settings.json'), settingsJson);

  return dir;
}

// ---------------------------------------------------------------------------------------------

const ours = path.join(HERE, 'src_vs_code');
if (!existsSync(path.join(ours, 'out', 'apiSettings.js'))) {
  console.log('this checkout\'s extension is not compiled — run `npm run compile` in src_vs_code first');
  process.exit(2);
}
const built = newServer();
if (built.length === 0) {
  console.log('this checkout has no built server — run `dotnet build dew_flow_connect_other_ais.slnx -c Debug` first');
  process.exit(2);
}
const work = mkdtempSync(path.join(tmpdir(), 'coai-api-settings-compat-'));
mkdirSync(path.join(work, 'server'));
mkdirSync(path.join(work, 'extension'));
const old = releasedServer(path.join(work, 'server'), flag('server-tag', ''));
const oldExtension = releasedExtension(path.join(work, 'extension'), flag('extension-tag', ''));
if (old.length === 0 || oldExtension === undefined) {
  console.log('a released half could not be obtained; nothing here is a contract result');
  process.exit(2);
}
console.log(`new: ${built}\n`);
const fresh = await modulesOf(ours);
const released = await modulesOf(oldExtension.root);
const oldVersion = (await run(old, ['--version'], work)).out.trim().split(/\s+/u).pop() ?? '';
// The settings file is the only source the servers below may read: an inherited variable would win.
const noEnv = { COAI_VENDORS: '', COAI_PROVIDERS: '', COAI_LOCAL_REASONING_EFFORT: '', COAI_FEATURE_API_REVIEW_MINUTES: '' };

const QWEN = {
  id: 'qwen', runtime: 'api', model: 'qwen3.8-max', enabled: true, plan: true, code: true,
  baseUrl: 'https://dashscope.example.invalid/compatible-mode/v1', executablePath: '',
  pricePerMillionIn: 0, pricePerMillionOut: 0, dialect: 'dashscope',
};
const GROK = { ...QWEN, id: 'grok', model: 'grok-4.7', baseUrl: 'https://api.x.ai/v1', dialect: 'xai' };
const plain = fresh.vendors.vendorsFrom([...fresh.vendors.DEFAULT_VENDORS, QWEN, GROK]);
const set = fresh.vendors.vendorsFrom([
  ...fresh.vendors.DEFAULT_VENDORS,
  { ...QWEN, effort: 'xhigh', thinking: false, reviewMinutes: 35 },
  { ...GROK, effort: 'ultra' },
]);
say(set.find((one) => one.id === 'qwen')?.effort === 'xhigh' && set.find((one) => one.id === 'qwen')?.thinking === false,
  'this build stores the settings on the row');

// ---------- 1. THIS extension's settings → the RELEASED server ----------
const newFile = fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, set, 'compat-new', '');
const wire = JSON.parse(JSON.parse(newFile).COAI_VENDORS ?? '[]');
say(wire.find((one) => one.id === 'qwen')?.reviewMinutes === 35,
  'against a server it cannot version, the settings cross into COAI_VENDORS (unknown is not old)', JSON.stringify(wire.find((one) => one.id === 'qwen')));
const withSettings = await run(old, ['--providers'], dataDir(work, 'a', newFile), noEnv);
const without = await run(old, ['--providers'], dataDir(work, 'a2', fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, plain, 'compat-new', '')), noEnv);
say(withSettings.code === 0, `the released server (${oldVersion}) answers --providers over a file carrying the settings`, `exit ${withSettings.code}`);
say(JSON.stringify(rowsOf(withSettings.out).map((one) => one.provider)) === '["codex","antigravity","qwen","grok"]',
  'and lists every vendor the file names — it did not refuse the list and fall back to its defaults',
  rowsOf(withSettings.out).map((one) => one.provider).join(', '));
say(without.code === 0 && withoutClock(without.out) === withoutClock(withSettings.out),
  'it answers the same with and without the settings — the fields change nothing there');
say(rowsOf(withSettings.out).every((one) => !('api' in one)), 'and reports no api on any row, which is what hides the controls on the card');
const heard = fresh.providers.parseProviders(withSettings.out) ?? {};
say(heard['qwen'] !== undefined && heard['qwen'].api === undefined, 'this build reads that answer as a row with no report');

// ---------- 2. The version gate ----------
const held = JSON.parse(JSON.parse(fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, set, 'compat-new', oldVersion)).COAI_VENDORS ?? '[]');
say(held.length === 4 && held.every((one) => !('effort' in one) && !('thinking' in one) && !('reviewMinutes' in one)),
  `against a known ${oldVersion} the settings are held back and every row still crosses`, `${held.length} row(s)`);

// ---------- 3. A rollback: the RELEASED extension reads what this build stored ----------
const stored = JSON.parse(JSON.stringify(set));
const rolledBack = released.vendors.vendorsFrom(stored);
say(JSON.stringify(rolledBack.map((one) => one.id)) === JSON.stringify(stored.map((one) => one.id)),
  `${oldExtension.tag} keeps every vendor row this build stored`, rolledBack.map((one) => one.id).join(', '));
say(rolledBack.every((one) => !('effort' in one) && !('thinking' in one) && !('reviewMinutes' in one)),
  'and drops the three settings it has never heard of, so its next save forgets them — a release-note row, not a fix');
const oldWire = JSON.parse(JSON.parse(released.file.serverSettingsJson(released.settings.DEFAULTS, rolledBack, 'compat-old', '')).COAI_VENDORS ?? '[]');
say(oldWire.every((one) => !('effort' in one) && !('thinking' in one) && !('reviewMinutes' in one)), 'what it writes carries none of them');

// ---------- 4. THIS server over what THIS extension wrote ----------
const live = await run(built, ['--providers'], dataDir(work, 'b', newFile), noEnv);
const report = fresh.providers.parseProviders(live.out) ?? {};
const qwen = report['qwen']?.api;
const grok = report['grok']?.api;
say(live.code === 0 && qwen !== undefined && grok !== undefined, 'this server reports api for both rows, and this build parses it', `exit ${live.code}`);
say(qwen?.effective.effort === 'xhigh' && qwen?.effective.thinkingOn === false && qwen?.effective.reviewMinutes === 35,
  'the row’s own settings come back as what it runs with', JSON.stringify(qwen?.effective));
say(JSON.stringify(qwen?.capabilities.effortLevels) === '["low","medium","xhigh"]' && qwen?.defaults.effort === 'medium',
  'the levels and the calibrated default the dropdown is drawn from', JSON.stringify(qwen?.capabilities));
say(/ultra/u.test(grok?.refusal ?? ''), 'an effort the module does not take comes back as its refusal', grok?.refusal ?? '');
const defaults = await run(built, ['--providers'], dataDir(work, 'c', fresh.file.serverSettingsJson(fresh.settings.DEFAULTS, plain, 'compat-new', '')), noEnv);
const unset = (fresh.providers.parseProviders(defaults.out) ?? {})['qwen']?.api;
say(unset !== undefined && JSON.stringify(unset.effective) === JSON.stringify(unset.defaults),
  'a row that sets nothing runs on the calibrated default', JSON.stringify(unset?.effective));

console.log('');
if (broken.length === 0) {
  console.log('The boundary holds, both ways.');
  process.exit(0);
}
console.log(`The boundary is BROKEN: ${broken.length} assertion(s) failed.`);
process.exit(1);
