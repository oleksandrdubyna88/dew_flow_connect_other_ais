import assert from 'node:assert/strict';
import { test } from 'node:test';

import { cardGaugeLines } from '../cardGauge';
import { modelsTabHtml } from '../modelsTab';
import { parseRoundCounts } from '../roundCounts';
import type { RoundsSnapshot } from '../roundCountsCache';
import type { GaugeReading } from '../teamServerApi';
import type { TeamServerState } from '../teamServerView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';
import { pageTree } from './pageTree';

/**
 * The vendor's own gauge on a model card (story 6.2 of todo/PLAN_every_round_is_counted.md). Local capture is deferred
 * (6.1b), so a card shows a Team server's observation ONLY for a row that runs on that server — the explicit match of
 * an account — naming the server; a local codex row says the gauge is not read on this machine, from the local answer's
 * own reason; every other row says nothing.
 */

const NOW = Date.parse('2026-10-12T12:00:00Z');

const READING: GaugeReading = {
  observedUtc: '2026-10-12T11:48:00+00:00', planType: 'prolite', rateLimitReachedType: '', ordinaryUsageAllowed: true,
  resetCreditsAvailable: null, stale: false,
  windows: [{ limitId: 'codex', kind: 'primary', usedPercent: 37, windowDurationMins: 10080, resetsAtUtc: '2026-10-15T07:00:00Z' }],
};

function acme(gauge?: GaugeReading): TeamServerState {
  return {
    server: { id: 'acme', name: 'Acme', url: 'https://acme.example.com' }, email: 'me@example.com', problem: '', stale: false,
    catalog: {
      serverVersion: '0.12.0', isAdmin: false, error: '',
      vendors: [{ id: 'codex', runtime: 'codex', models: [], slots: { total: 1, ready: 1, coolingDown: 0, needsSignIn: 0, ...(gauge === undefined ? {} : { gauge }) } }],
    },
  };
}

const LOCAL_CODEX: Vendor = { ...DEFAULT_VENDORS.find((row) => row.runtime === 'codex')!, id: 'codex' };
const REMOTE: Vendor = { ...LOCAL_CODEX, id: 'acme-codex', runtime: 'remote', teamServerId: 'acme', remoteVendor: 'codex', baseUrl: 'https://acme.example.com' };

/** A snapshot whose round-count answer came from a `coai-mcp` that says (or does not say) why it reads no gauge. */
function snapshot(gauge: unknown): RoundsSnapshot {
  const answer = parseRoundCounts(JSON.stringify({ nowUtc: '2026-10-12T12:00:00Z', windows: [], modelsKnownSince: null, ...(gauge === undefined ? {} : { gauge }) }))!;

  return { read: { state: 'counted', answer }, ledger: [], ledgerFailure: '', atMs: NOW };
}

test('a row that runs on a Team server shows that server\'s reading of the account, naming the server', () => {
  assert.deepEqual(cardGaugeLines(REMOTE, [acme(READING)], undefined, NOW, 'UTC'),
    ['codex (weekly): 37 % used · resets Thu 07:00 · seen 12 min ago · read on Acme']);
});

test('no reading on the server, an unknown server, or a vendor the server does not serve: nothing, never a 0 %', () => {
  assert.deepEqual(cardGaugeLines(REMOTE, [acme()], undefined, NOW, 'UTC'), []);
  assert.deepEqual(cardGaugeLines({ ...REMOTE, teamServerId: 'gone', baseUrl: 'https://gone.example.com' }, [acme(READING)], undefined, NOW, 'UTC'), []);
  assert.deepEqual(cardGaugeLines({ ...REMOTE, remoteVendor: 'claude' }, [acme(READING)], undefined, NOW, 'UTC'), []);
});

test('a local codex row is never matched to a server\'s account: it says, from the local answer, that this machine does not read the gauge', () => {
  const lines = cardGaugeLines(LOCAL_CODEX, [acme(READING)], snapshot({ ratios: [], unavailable: 'local-capture-deferred' }), NOW, 'UTC');

  assert.deepEqual(lines, ['codex gauge: unavailable — this machine does not read the gauge; a Team server does']);
});

test('a coai-mcp older than the field, or no read yet: the local card says nothing about a gauge', () => {
  assert.deepEqual(cardGaugeLines(LOCAL_CODEX, [], snapshot(undefined), NOW, 'UTC'), []);
  assert.deepEqual(cardGaugeLines(LOCAL_CODEX, [], undefined, NOW, 'UTC'), []);
});

test('a row whose vendor has no gauge says nothing', () => {
  const claude: Vendor = { ...LOCAL_CODEX, id: 'claude', runtime: 'claude' };

  assert.deepEqual(cardGaugeLines(claude, [], snapshot({ ratios: [], unavailable: 'local-capture-deferred' }), NOW, 'UTC'), []);
});

test('the local answer\'s gauge reason is read by name, and an answer without it parses as before', () => {
  const said = parseRoundCounts(JSON.stringify({ nowUtc: 'x', windows: [], modelsKnownSince: null, gauge: { ratios: [], unavailable: 'local-capture-deferred' } }));
  const older = parseRoundCounts(JSON.stringify({ nowUtc: 'x', windows: [], modelsKnownSince: null }));

  assert.equal(said?.gaugeUnavailable, 'local-capture-deferred');
  assert.ok(older !== undefined, 'an older answer no longer parses');
  assert.equal(older.gaugeUnavailable, undefined);
});

test('the Models tab draws a remote row\'s gauge in its card\'s foot, and a local codex row\'s reason in its own', () => {
  const html = modelsTabHtml({
    ...panelState('reviewers'), vendors: [REMOTE, LOCAL_CODEX], teamServers: [acme(READING)], now: NOW,
    roundsSnapshot: snapshot({ ratios: [], unavailable: 'local-capture-deferred' }),
  });
  const cards = pageTree(html).find((node) => node.tagName === 'ARTICLE');
  const linesOf = (id: string): readonly string[] => cards.find((card) => card.dataset.modelCard === id)!
    .find((node) => node.className === 'gauge-line').map((node) => node.text());

  assert.equal(linesOf('acme-codex').length, 1, 'the remote row\'s card draws no gauge line');
  assert.match(linesOf('acme-codex')[0]!, /^codex \(weekly\): 37 % used · resets \w{3} \d{2}:\d{2} · seen 12 min ago · read on Acme$/);
  assert.deepEqual(linesOf('codex'), ['codex gauge: unavailable — this machine does not read the gauge; a Team server does']);
});
