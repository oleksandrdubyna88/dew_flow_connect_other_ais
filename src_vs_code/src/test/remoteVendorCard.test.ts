import * as assert from 'node:assert';
import { test } from 'node:test';
import { DEFAULTS } from '../settingsShape';
import { PanelState, panelHtml } from '../panelView';
import { Vendor } from '../vendors';
import { TeamServerState } from '../teamServerView';

/** A remote reviewer row, as the panel actually draws it. */

const SERVER_URL = 'https://coai.example.com/';

const ROW: Vendor = {
  id: 'remsoft-dev-codex',
  runtime: 'remote',
  remoteVendor: 'codex',
  model: 'gpt-5.6',
  enabled: true,
  plan: true,
  code: true,
  // Stored canonical by the panel; the server entry below holds it as the person typed it.
  baseUrl: 'https://coai.example.com',
  executablePath: '',
  pricePerMillionIn: 0,
  pricePerMillionOut: 0,
};

const TEAM: TeamServerState = {
  server: { id: 'remsoft-dev', name: 'RemSoft Dev', url: SERVER_URL },
  email: 'a@b.c',
  problem: '',
  stale: false,
  catalog: {
    serverVersion: '0.5.1',
    isAdmin: false,
    error: '',
    vendors: [{
      id: 'codex',
      runtime: 'codex',
      models: ['gpt-5.6', 'gpt-5.6-mini'],
      slots: { total: 2, ready: 2, coolingDown: 0, needsSignIn: 0 },
    }],
  },
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
    teamServers: [TEAM],
    ...over,
  } as unknown as PanelState, 'nonce');
}

test('a Team server row is offered the models that server allows', () => {
  const html = page();

  assert.ok(html.includes('gpt-5.6-mini'), 'the dropdown is the server’s allowlist');
});

test('the server is matched CANONICALLY, not by the string somebody typed', () => {
  // The row stores `https://coai.example.com` and the server entry holds it with a trailing slash.
  // Comparing them as written is the exact mistake this feature has a shared fixture to prevent —
  // and here it would show a person an empty model list for a server that is working perfectly.
  assert.ok(page().includes('gpt-5.6-mini'));
});

test('a remote row asks for no endpoint, no CLI path and no price', () => {
  // All three are decided ON the server: its address is the row's endpoint, its CLI runs there, and
  // its price is the company's subscription rather than this person's. Three fields that could only
  // ever be filled in wrongly.
  const html = page();

  assert.ok(!html.includes('data-setting="baseUrl" data-vendor="remsoft-dev-codex"'));
  assert.ok(!html.includes('data-setting="executablePath" data-vendor="remsoft-dev-codex"'));
  assert.ok(!html.includes('data-setting="pricePerMillionIn" data-vendor="remsoft-dev-codex"'));
});

test('a server that has not answered yet offers no models rather than a guess', () => {
  const html = page({ teamServers: [{ ...TEAM, catalog: undefined }] });

  assert.ok(!html.includes('gpt-5.6-mini'));
  // The saved one is still there, marked, so the selection does not silently vanish.
  assert.ok(html.includes('gpt-5.6'));
});

test('an ordinary vendor still gets its endpoint and price fields', () => {
  // The remote branch must not have taken anything away from everybody else.
  const codex: Vendor = { ...ROW, id: 'mistral', runtime: 'codex', remoteVendor: '', baseUrl: 'https://api.mistral.ai/v1' };
  const html = page({ vendors: [codex] });

  assert.ok(html.includes('data-setting="baseUrl" data-vendor="mistral"'));
  assert.ok(html.includes('data-setting="pricePerMillionIn" data-vendor="mistral"'));
});

test('a row follows its server by ID, so correcting a typo in the URL does not orphan it', () => {
  // The id is generated once and never rewritten; the address is correctable. Matching on the
  // address meant that fixing a hostname froze every row's model list and made removal find none of
  // them. Caught on the code round.
  const moved: TeamServerState = {
    ...TEAM,
    server: { ...TEAM.server, url: 'https://coai-corrected.example.com' },
  };
  const row: Vendor = { ...ROW, teamServerId: 'remsoft-dev' };

  const html = page({ vendors: [row], teamServers: [moved] });

  assert.ok(html.includes('gpt-5.6-mini'), 'the row still finds its server after the URL changed');
});

test('a row written before ids existed still finds its server by address', () => {
  // Absent is not wrong — it is what every row saved before this field looks like.
  const html = page({ vendors: [{ ...ROW }], teamServers: [TEAM] });

  assert.ok(html.includes('gpt-5.6-mini'));
});

/**
 * The caption under a Team-server row names the SERVER's catalog, never the Codex CLI.
 *
 * <p>`modelsProvenance` had arms for local, gemini, claude and antigravity and fell through to
 * codex, so a Team-server reviewer was captioned "codex · 8 models the Codex CLI has cached for
 * this machine" — a sentence about a CLI that has nothing to do with it, under a dropdown whose
 * contents came from an HTTP catalog. Reported from a screenshot, 2026-09-07.</p>
 */
test('a Team-server card is captioned with its server, not with the Codex CLI', () => {
  const html = page();

  assert.ok(!html.includes('the Codex CLI has cached'), 'this row runs no CLI on this machine at all');
  assert.match(html, /2 models? this Team server allows/);
  assert.ok(html.includes('codex'), 'the vendor the server knows it by is what the count is about');
});

test('before the catalog has arrived the caption says so, rather than inventing a number', () => {
  const html = page({ teamServers: [{ ...TEAM, catalog: undefined }] });

  // Not "has not been asked yet": nothing here can tell a request in flight from one that failed.
  // The Team servers section owns that, with the server's own message and its stale marker.
  assert.match(html, /catalog has not arrived/);
  assert.ok(!html.includes('not been asked yet'), 'that asserts which of the two states it is');
  assert.ok(!html.includes('the Codex CLI has cached'));
});

test('a server that allows the vendor no models says that, rather than counting to zero', () => {
  const empty = { ...TEAM, catalog: { ...TEAM.catalog!, vendors: [{ ...TEAM.catalog!.vendors[0]!, models: [] }] } };

  assert.match(page({ teamServers: [empty] }), /allows 'codex' no models at all/);
});

test('one allowed model is one model, not "1 models"', () => {
  const one = { ...TEAM, catalog: { ...TEAM.catalog!, vendors: [{ ...TEAM.catalog!.vendors[0]!, models: ['gpt-5.6'] }] } };

  assert.match(page({ teamServers: [one] }), /the one model this Team server allows/);
});

test('a row carrying an empty remoteVendor falls back to its id, as the C# side of the seam does', () => {
  // `??` keeps an empty string. The other half of this contract length-checks the same field
  // (`VendorIdentity.Recorded`), and two halves disagreeing about what counts as absent is how the
  // whole of this plan started.
  // The row id must BE a vendor the catalog offers, or both readings miss and the test proves
  // nothing: with the fallback working this row resolves to the server's `codex` and its two models;
  // with `??` keeping the empty string it searches the catalog for a vendor called nothing.
  const blank: Vendor = { ...ROW, id: 'codex', remoteVendor: '' };

  const html = page({ vendors: [blank] });

  assert.match(html, /the 2 models this Team server allows for 'codex'/);
  assert.ok(!html.includes("allows '' no models"), 'an empty name would be asked of the catalog verbatim');
});

test('a row whose Team server is gone is told THAT, not to go and read the section it left', () => {
  // What somebody is left holding after removing a server and keeping its reviewers. "Its catalog
  // has not arrived — the Team servers section says why" would send them to a section that no
  // longer lists their server. Accepted finding, this story's code round.
  const html = page({ teamServers: [] });

  assert.match(html, /no Team server on this side matches this reviewer/);
  assert.ok(!html.includes('catalog has not arrived'), 'a missing entry is not a pending fetch');
});

/**
 * Every other runtime's caption, in full.
 *
 * <p>Whole strings, not fragments: the promise was that no other caption changes by a character, and
 * a fragment match cannot fail on reworded punctuation or an appended clause. Accepted finding, this
 * story's code round.</p>
 */
test('no other runtime lost its caption to the remote arm', () => {
  const captions: [Vendor['runtime'], string][] = [
    ['codex', 'codex · the Codex CLI has cached no model list yet — type a model, or run codex once.'],
    ['gemini', 'gemini · a curated list — the Gemini CLI publishes none. Any other model can be typed in.'],
    ['claude', 'claude · aliases the Claude CLI resolves to the latest of each family. Any exact id can be typed in.'],
    ['antigravity', 'antigravity · a list from when this was written — `agy` did not answer, so it may be behind. Any id can be typed in.'],
    ['local', 'local · no engine probed yet.'],
  ];

  for (const [runtime, expected] of captions) {
    const row: Vendor = { ...ROW, id: runtime, runtime, remoteVendor: undefined, baseUrl: '', model: '' };
    assert.ok(page({ vendors: [row], teamServers: [] }).includes(expected), `${runtime}: ${expected}`);
  }
});
