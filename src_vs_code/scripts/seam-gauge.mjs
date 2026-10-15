/**
 * The vendor-gauge leg of the seam (story 6.2 of todo/PLAN_every_round_is_counted.md — E6's plan round: a real-product
 * scenario from the stored reading to what an admin reads).
 *
 * <p>The Team server WRITES the gauge in two places — the slot's `state.json` (served as the catalog's `slots.gauge`,
 * story 6.1a) and the ledger lines (from which `/api/usage` derives `gauges`, rounds per 1 %, through the shared runners
 * library) — and the extension READS both through its own client and parsers and DRAWS them on the Team server tab
 * (`teamGauge.ts`). Each side has its own suite; a field serialised under a name the other does not read would leave both
 * green and show every admin an empty block after the deploy. So this starts the REAL server built from this tree over a
 * seeded slot and ledger, asks it the way the tab does, and renders the block from what came back — asserting the
 * sentences, not the JSON.</p>
 *
 * <p>The ledger holds codex lines on one slot with consecutive readings of the same weekly window: the derivation has
 * intervals to judge, and codex's figure must still come back HIDDEN for the consumer coai cannot count (direct codex
 * use), with the gauge itself shown. A second vendor whose limit has no uncounted consumer proves the figure itself
 * crosses as a number. Nothing is launched and nothing is billed: the server only reads its files.</p>
 *
 * <p>Its limits, said once: the reading is planted on the slot rather than captured by a fake `codex app-server` (the
 * capture itself is the server suite's `GaugeCaptureTests` and `GaugeReaderCrashHarnessTests`); the page is the pure
 * renderer the tab calls, not the bundled webview (that is `teamServerTabPage.test.ts`).</p>
 */
import { createHmac } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

import { startTeamServer } from './team-server-child.mjs';

const { createSession, fetchCatalog, fetchUsage } = await import('../out/teamServerApi.js');
const { serverGauges, teamGaugeBlock } = await import('../out/teamGauge.js');

const DOMAIN = 'seam.test';
const ADMIN = `admin@${DOMAIN}`;
const HOUR = 3_600_000;
const RESETS = new Date(Date.now() + 3 * 24 * HOUR).toISOString();

/** One Team-server review line on slot `a`, written a second after the reading taken right after its launch. */
function line(hoursAgo, vendor, limit, round, used) {
  const at = Date.now() - hoursAgo * HOUR;

  return JSON.stringify({
    utc: new Date(at).toISOString(), provider: vendor, model: 'm', role: 'Architecture', stage: 'TeamServer', seconds: 3,
    tokensIn: 100, tokensOut: 10, costUsd: null, outcome: 'ok', email: ADMIN, kind: 'review', attempt: 'first',
    job: round, turn: round, slot: 'a', round, clientAttempt: 'first',
    gaugeObservedUtc: new Date(at - 1000).toISOString(), gaugePlan: 'prolite',
    gaugeWindows: [{ limitId: limit, kind: 'primary', mins: 10080, usedPercent: used, resetsUtc: RESETS }],
  });
}

const R = (n) => String(n).padStart(32, '0');

const LEDGER = [
  line(5, 'codex', 'codex', R(1), 30), line(4, 'codex', 'codex', R(2), 31), line(3, 'codex', 'codex', R(3), 33),
  // A vendor whose limit has no uncounted consumer: one round over 1 %, then one over 2 % — weighted by rounds, 0.75.
  line(5, 'acme', 'acme', R(4), 10), line(4, 'acme', 'acme', R(5), 11), line(3, 'acme', 'acme', R(6), 13),
];

/** The slot state the server serves the catalog's reading from — written as `SlotRegistry.SaveGauge` writes it. */
const STATE = JSON.stringify({
  cooldownUntilUtc: null, needsSignIn: false, note: '', lastUsedUtc: new Date().toISOString(), consecutiveCooldowns: 0,
  gauge: {
    observedUtc: new Date(Date.now() - 12 * 60_000).toISOString(), planType: 'prolite', rateLimitReachedType: '', ordinaryUsageAllowed: true,
    resetCreditsAvailable: null, windows: [{ limitId: 'codex', kind: 'primary', usedPercent: 33, windowDurationMins: 10080, resetsAtUtc: RESETS }],
    stale: false,
  },
});

function localToken(key, email) {
  const part = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({ iss: 'coai-local', email, exp: Math.floor(Date.now() / 1000) + 600 });

  return `${head}.${body}.${createHmac('sha256', key).update(`${head}.${body}`).digest('base64url')}`;
}

function seeded() {
  const dataDir = mkdtempSync(join(tmpdir(), 'coai-seam-gauge-'));
  writeFileSync(join(dataDir, 'vendors.json'), JSON.stringify([{ id: 'codex', runtime: 'codex', models: ['m'], slots: ['a'] }]));
  mkdirSync(join(dataDir, 'accounts', 'codex', 'a'), { recursive: true });
  writeFileSync(join(dataDir, 'accounts', 'codex', 'a', 'state.json'), STATE);
  writeFileSync(join(dataDir, 'usage.jsonl'), `${LEDGER.join('\n')}\n`, 'utf8');

  return dataDir;
}

function must(condition, what) {
  if (!condition) {
    throw new Error(what);
  }
}

/** What crossed: the catalog's reading, the derived figures, and the block an admin reads. */
async function crossed(server) {
  const made = await createSession(server.url, localToken(server.key, ADMIN));
  must(made.ok, `the admin was refused a session: ${made.status} — ${made.message}`);
  const catalog = await fetchCatalog(server.url, made.value.token);
  must(catalog.ok, `the catalog was refused: ${catalog.status} — ${catalog.message}`);
  const usage = await fetchUsage(server.url, made.value.token, 'week', 'company');
  must(usage.ok, `the company usage was refused: ${usage.status} — ${usage.message}`);

  return { vendors: catalog.value.vendors, usage: usage.value };
}

function check({ vendors, usage }) {
  const figures = serverGauges(usage);
  must(figures !== undefined, `the extension read no \`gauges\` from the server's answer: ${JSON.stringify(usage).slice(0, 400)}`);
  const codex = figures.find((one) => one.vendor === 'codex');
  must(codex?.roundsPerPercent === null && codex.missingConsumer === 'direct-codex-use', `codex's figure did not come back hidden for direct use: ${JSON.stringify(codex)}`);
  const acme = figures.find((one) => one.vendor === 'acme');
  must(Math.abs((acme?.roundsPerPercent ?? 0) - 0.75) < 1e-9 && acme.intervals === 2, `the uncounted-consumer-free figure did not cross as 0.75 over 2 intervals: ${JSON.stringify(acme)}`);

  const text = teamGaugeBlock({ serverId: 'seam', vendors, usage, prices: { 'seam/acme': 200 }, now: Date.now(), palette: () => '#000' })
    .replace(/<[^>]+>/g, '\n');
  must(/codex \(weekly\): 33 % used · resets \w{3} \d{2}:\d{2} · seen 12 min ago/.test(text), `the block did not draw the catalog's reading:\n${text}`);
  must(/rounds per 1 % of codex \(weekly\): hidden — direct codex use/.test(text), `the block did not say why codex's figure is hidden:\n${text}`);
  must(!/of your plan per round/.test(text), 'a plan share was drawn beside a hidden figure');

  return { figures: figures.length };
}

/** @returns {Promise<{ figures: number }>} */
export async function gaugeSeam({ fail, extensionRoot = resolve(import.meta.dirname, '..') }) {
  const dataDir = seeded();
  let server;
  let outcome;
  try {
    server = await startTeamServer({ extensionRoot, dataDir, domain: DOMAIN, admins: ADMIN, stdio: 'ignore', env: { PATH: '' } });
    outcome = check(await crossed(server));
  } catch (error) {
    outcome = error instanceof Error ? error.message : String(error);
  } finally {
    server?.stop();
    try {
      rmSync(dataDir, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
    } catch {
      // A leftover temp folder is not worth a red run.
    }
  }
  if (typeof outcome === 'string') {
    fail(`the vendor gauge did not cross from the server to the Team server tab: ${outcome}`);
  }

  return outcome;
}
