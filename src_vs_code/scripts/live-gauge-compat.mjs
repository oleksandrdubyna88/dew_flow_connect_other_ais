/**
 * An older client against the new server (E6's plan round, todo/PLAN_every_round_is_counted.md): the previously RELEASED
 * extension's own parsers against the answers this tree's server gives once they carry the gauge fields (story 6.1a).
 *
 * <p>What a shipped extension reads from a Team server is the catalog (`fetchCatalog`, with its per-vendor slot counts)
 * and the usage ledger's lines (`parseUsage`, which prices the Spending tab). The new server ADDS to both: a `gauge`
 * object under `slots` when a reading exists, and nine trailing `gauge*` fields on a Team-server ledger line. A client
 * older than the fields must load both unchanged — the catalog with every vendor and count it read before, the lines
 * with every token count and outcome — and must not see a number it cannot name. No unit test holds two builds of one
 * half; this one does.</p>
 *
 * <h2>How to run it</h2>
 *
 * <pre>dotnet build src_server/src/CoaiServer.csproj &amp;&amp; cd src_vs_code &amp;&amp; npm run test:gauge-compat</pre>
 *
 * <p>It builds the newest published `extension-v*` from its tag (`releasedHalves.mjs`); pass `--extension-tag=` for
 * another release (`extension-v0.69.0` is the last one released before the gauge fields). NOT in CI: it needs the
 * network and a released tag.</p>
 *
 * <p>Exit codes: <b>0</b> the older client loads both unchanged · <b>1</b> it does not, and says which assertion ·
 * <b>2</b> the released extension or this tree's server could not be obtained — an environment answer, not a contract
 * one.</p>
 */
import { createHmac } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { flag, HERE, releasedExtension } from './releasedHalves.mjs';
import { startTeamServer } from './team-server-child.mjs';

let broken = [];
const say = (ok, what, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'}  ${what}${detail === '' ? '' : ` — ${detail}`}`);
  if (!ok) {
    broken = [...broken, what];
  }
};

const DOMAIN = 'compat.test';
const ADMIN = `admin@${DOMAIN}`;

/** One Team-server ledger line as `JobRunner.Ledger` writes it since 6.1a — the gauge read after the launch on it. */
const LINE_WITH_GAUGE = JSON.stringify({
  utc: new Date(Date.now() - 3_600_000).toISOString(), provider: 'codex', model: 'gpt-6-luna', role: 'Architecture', stage: 'TeamServer',
  seconds: 3, tokensIn: 100, tokensOut: 10, costUsd: null, outcome: 'ok', email: ADMIN, kind: 'review', tokensCached: 80,
  turn: 'j1', attempt: 'first', job: 'j1', slot: 'a',
  gaugeObservedUtc: '2026-10-11T10:00:05.0000000Z', gaugePlan: 'prolite', gaugeLimit: 'codex',
  gaugePrimaryMins: 10080, gaugePrimaryUsedPercent: 40, gaugePrimaryResetsUtc: '2026-10-14T07:48:11.0000000Z',
  gaugeSecondaryResetsUtc: '',
});

/** The slot state the new server serves a reading from — written as `SlotRegistry.SaveGauge` writes it. */
const STATE_WITH_GAUGE = JSON.stringify({
  cooldownUntilUtc: null, needsSignIn: false, note: '', lastUsedUtc: '2026-10-11T10:00:00+00:00', consecutiveCooldowns: 0,
  gauge: {
    observedUtc: '2026-10-11T10:00:05+00:00', planType: 'prolite', rateLimitReachedType: '', ordinaryUsageAllowed: true,
    resetCreditsAvailable: 1, windows: [{ limitId: 'codex', kind: 'primary', usedPercent: 40, windowDurationMins: 10080, resetsAtUtc: '2026-10-14T07:48:11.0000000Z' }],
    stale: false,
  },
});

function localToken(key, email) {
  const part = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({ iss: 'coai-local', email, exp: Math.floor(Date.now() / 1000) + 600 });

  return `${head}.${body}.${createHmac('sha256', key).update(`${head}.${body}`).digest('base64url')}`;
}

const scratch = mkdtempSync(path.join(tmpdir(), 'coai-gauge-compat-'));
const released = releasedExtension(scratch, flag('extension-tag', ''));
if (released === undefined) {
  console.log('the released extension could not be built; nothing to compare against');
  rmSync(scratch, { recursive: true, force: true });
  process.exit(2);
}

const load = async (name) => await import(pathToFileURL(path.join(released.root, 'out', `${name}.js`)).href);
const oldApi = await load('teamServerApi');
const oldUsage = await load('usage');

const dataDir = mkdtempSync(path.join(tmpdir(), 'coai-gauge-compat-data-'));
writeFileSync(path.join(dataDir, 'vendors.json'), JSON.stringify([{ id: 'codex', runtime: 'codex', models: ['gpt-6-luna'], slots: ['a'] }]));
mkdirSync(path.join(dataDir, 'accounts', 'codex', 'a'), { recursive: true });
writeFileSync(path.join(dataDir, 'accounts', 'codex', 'a', 'state.json'), STATE_WITH_GAUGE);
writeFileSync(path.join(dataDir, 'usage.jsonl'), `${LINE_WITH_GAUGE}\n`);

let server;
try {
  server = await startTeamServer({ extensionRoot: path.join(HERE, 'src_vs_code'), dataDir, domain: DOMAIN, admins: ADMIN, stdio: 'ignore', env: { PATH: '' } });
  const made = await oldApi.createSession(server.url, localToken(server.key, ADMIN));
  say(made.ok, `the released extension (${released.tag}) signs in to the new server`, made.ok ? '' : `${made.status} — ${made.message}`);
  const token = made.value?.token ?? '';

  const catalog = await oldApi.fetchCatalog(server.url, token);
  say(catalog.ok, 'the released catalog client loads a catalog whose slots carry a gauge', catalog.ok ? '' : `${catalog.status} — ${catalog.message}`);
  const codex = catalog.value?.vendors?.find((v) => v.id === 'codex');
  say(codex !== undefined, 'the vendor is there with its models and slot counts', JSON.stringify(codex ?? null));
  say(codex?.slots?.total === 1 && codex?.slots?.ready === 1, 'the counts it read before are unchanged beside the new key', JSON.stringify(codex?.slots ?? null));
  say('gauge' in (codex?.slots ?? {}), 'the new server really sent the gauge (the control: the wire carries it)', '');

  // The ledger line through the released parser: the raw bytes the new server appends, read by the old reader.
  const lines = oldUsage.parseUsage(readFileSync(path.join(dataDir, 'usage.jsonl'), 'utf8'));
  say(lines.length === 1, 'the released ledger parser reads a line carrying the gauge fields', `${lines.length} line(s)`);
  const line = lines[0] ?? {};
  say(line.tokensIn === 100 && line.tokensOut === 10 && line.outcome === 'ok' && line.provider === 'codex', 'its token counts, outcome and vendor are what they were', JSON.stringify(line));
  const totals = oldUsage.totalsByVendor?.(lines) ?? [];
  say(Array.isArray(totals), 'the released totals still compute over such lines', JSON.stringify(totals).slice(0, 200));
} catch (error) {
  say(false, 'the comparison ran to its end', error instanceof Error ? error.message : String(error));
} finally {
  server?.stop();
  rmSync(dataDir, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
  rmSync(scratch, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
}

if (broken.length === 0) {
  console.log('the older client loads the catalog and the ledger unchanged beside the gauge fields');
  process.exit(0);
}
console.log(`${broken.length} assertion(s) failed:\n  ${broken.join('\n  ')}`);
process.exit(1);
