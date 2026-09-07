import assert from 'node:assert';
import { test } from 'node:test';
import { availabilityOf, parseProviders } from '../providers';
import { PanelState, panelHtml } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { Vendor } from '../vendors';

/**
 * A reviewer the server cannot run is named on its own card, before a round is asked for.
 *
 * <p>The whole of this plan in one surface. On 2026-09-07 a Team-server row sat in this panel,
 * enabled and ticked for both stages, while every round quietly ran without it — the panel said the
 * reviewer was configured, which was true, and nothing anywhere said it could not review.</p>
 *
 * <p>The verdict comes from the SERVER, through `coai-mcp --providers`. The panel displays it. The
 * alternative — deciding availability again in TypeScript — is the second copy of a decision this
 * repository has twice paid for, and `RuntimeResolution` carries the docstring about it.</p>
 */

const ROW: Vendor = {
  id: 'remsoftdev-claude',
  runtime: 'remote',
  remoteVendor: 'claude',
  model: 'haiku',
  enabled: true,
  plan: true,
  code: true,
  baseUrl: 'https://coai.example.com',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

function page(over: Partial<PanelState> = {}): string {
  return panelHtml({
    settings: DEFAULTS,
    vendors: [ROW],
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '', perSide: false,
    questions: [], sessions: [], openSections: ['reviewers'],
    usage: [], usageWindow: 'day', latestServerVersion: '',
    cliStatus: {}, modelPrices: {},
    snippetStatus: { kind: 'absent', version: 0 },
    teamServers: [],
    ...over,
  } as unknown as PanelState, 'nonce');
}

const UNAVAILABLE = {
  'remsoftdev-claude': {
    provider: 'remsoftdev-claude',
    auth: 'unavailable',
    note: 'not signed in to the Team server at https://coai.example.com — sign in from the panel',
  },
};

test('a reviewer the server cannot run is badged, with the reason', () => {
  const html = page({ providerHealth: UNAVAILABLE });

  assert.match(html, /cannot review/);
  assert.ok(html.includes('not signed in to the Team server'), 'the count is not actionable; the reason is');
});

test('a reviewer the server CAN run is badged with nothing', () => {
  const fine = {
    'remsoftdev-claude': { provider: 'remsoftdev-claude', auth: 'server token', note: '1 of 1 account(s) ready' },
  };

  assert.ok(!page({ providerHealth: fine }).includes('cannot review'));
});

test('a probe that answered nothing badges nothing — unknown is not unavailable', () => {
  // A missing binary, a build too old for the flag, a timeout, a body that changed shape. A badge
  // that lights up because a probe failed is a badge that lies, and the ⤓ buttons on this same card
  // already refuse to guess for exactly this reason.
  assert.ok(!page().includes('cannot review'), 'no answer at all');
  assert.ok(!page({ providerHealth: {} }).includes('cannot review'), 'an empty answer');
});

test('a row the server did not mention is unknown, not fine', () => {
  // The two halves disagree about what is configured, which is a thing to stay quiet about rather
  // than to reassure somebody over.
  assert.equal(availabilityOf('remsoftdev-claude', {}), 'unknown');
  assert.equal(availabilityOf('remsoftdev-claude', UNAVAILABLE), 'unavailable');
  assert.equal(
    availabilityOf('x', { x: { provider: 'x', auth: 'own auth', note: '' } }),
    'fine',
  );
});

test('an answer with no auth at all is unknown, because it says nothing', () => {
  assert.equal(availabilityOf('x', { x: { provider: 'x', auth: '', note: 'something' } }), 'unknown');
});

test('the parser survives everything a different build could hand it', () => {
  assert.deepEqual(parseProviders('not json'), {});
  assert.deepEqual(parseProviders('null'), {});
  assert.deepEqual(parseProviders('{}'), {}, 'a body with no providers array');
  assert.deepEqual(parseProviders('{"providers":"nope"}'), {});
  assert.deepEqual(parseProviders('{"providers":[null,{"provider":""},{"nope":1}]}'), {}, 'rows with no id');

  const one = parseProviders('{"providers":[{"provider":"codex","auth":"own auth"}]}');
  assert.equal(one['codex']?.auth, 'own auth');
  assert.equal(one['codex']?.note, '', 'a missing note is empty, not undefined');
});
