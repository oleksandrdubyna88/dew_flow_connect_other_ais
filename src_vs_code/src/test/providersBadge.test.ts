import assert from 'node:assert';
import { test } from 'node:test';
import { availabilityOf, NO_NOTES, parseProviderNotes, parseProviders, ProviderHealth } from '../providers';
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

/**
 * A `PanelState` the COMPILER checks, rather than one cast past it.
 *
 * <p>Every field spelled out and no trailing `as`. The fixture beside this one is written with
 * `as unknown as PanelState`, which is the shape the TypeScript doctrine names: a promise to
 * maintain a type by hand that comes due silently the day `PanelState` gains a required field. Two
 * reviewers caught this file copying it, which is the specific thing reuse-first forbids — do not
 * imitate a pattern you can see is wrong because the file next door does it that way.</p>
 *
 * <p>The neighbour is left alone deliberately: rewriting a fixture I was not asked to change turns a
 * small diff into one nobody can review. It is named here instead.</p>
 */
function page(over: Partial<PanelState> = {}): string {
  const state: PanelState = {
    settings: DEFAULTS,
    vendors: [ROW],
    codexModels: [],
    agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    questions: [],
    sessions: [],
    openSections: ['reviewers'],
    usage: [],
    usageWindow: 'day',
    latestServerVersion: '',
    cliStatus: {},
    modelPrices: {},
    // `current`, not `version` — which the cast in the neighbouring fixture has been hiding, and
    // which the compiler said the moment this one stopped casting.
    snippetStatus: { kind: 'absent', current: 0 },
    teamServers: [],
    ...over,
  };

  return panelHtml(state, 'nonce');
}

const UNAVAILABLE: Record<string, ProviderHealth> = {
  'remsoftdev-claude': {
    provider: 'remsoftdev-claude',
    auth: 'unavailable',
    note: 'not signed in to the Team server at https://coai.example.com — sign in from the panel',
  },
};

test('a reviewer the server cannot run is badged, with the reason', () => {
  const html = page({ providers: { reported: UNAVAILABLE, asked: true, answered: true, notes: NO_NOTES } });

  assert.match(html, /cannot review/);
  assert.ok(html.includes('not signed in to the Team server'), 'the count is not actionable; the reason is');
});

test('a reviewer the server CAN run is badged with nothing', () => {
  const fine: Record<string, ProviderHealth> = {
    'remsoftdev-claude': { provider: 'remsoftdev-claude', auth: 'server token', note: '1 of 1 account(s) ready' },
  };

  assert.ok(!page({ providers: { reported: fine, asked: true, answered: true, notes: NO_NOTES } }).includes('cannot review'));
});

test('a probe that answered nothing badges nothing — unknown is not unavailable', () => {
  // A missing binary, a build too old for the flag, a timeout, a body that changed shape. A badge
  // that lights up because a probe failed is a badge that lies, and the ⤓ buttons on this same card
  // already refuse to guess for exactly this reason.
  assert.ok(!page().includes('cannot review'), 'no answer at all');
  assert.ok(
    !page({ providers: { reported: {}, asked: true, answered: false, notes: NO_NOTES } }).includes('cannot review'),
    'an answer that could not be read',
  );
});

test('a row the server did not mention is unknown, not fine', () => {
  // The two halves disagree about what is configured, which is a thing to stay quiet about rather
  // than to reassure somebody over.
  assert.equal(availabilityOf('remsoftdev-claude', {}), 'unknown');
  assert.equal(availabilityOf('remsoftdev-claude', UNAVAILABLE), 'unavailable');
  assert.equal(availabilityOf('x', { x: { provider: 'x', auth: 'own auth', note: '' } }), 'fine');
});

test('an answer with no auth at all is unknown, because it says nothing', () => {
  assert.equal(availabilityOf('x', { x: { provider: 'x', auth: '', note: 'something' } }), 'unknown');
});

test('a body that is not a providers answer is undefined, not an empty one', () => {
  // The distinction the Server section's sentence rests on. Collapsing the two made "the binary
  // could not answer" derivable from an empty map, so a build that legitimately reported zero
  // reviewers would have shown "could not report its reviewers" forever. Two reviewers, one round.
  assert.equal(parseProviders('not json'), undefined);
  assert.equal(parseProviders('null'), undefined);
  assert.equal(parseProviders('{}'), undefined, 'a body with no providers array');
  assert.equal(parseProviders('{"providers":"nope"}'), undefined);

  assert.deepEqual(parseProviders('{"providers":[]}'), {}, 'an EMPTY array is an answer');
  assert.deepEqual(
    parseProviders('{"providers":[null,{"provider":""},{"nope":1}]}'),
    {},
    'rows with no id are dropped, and the answer is still an answer',
  );
});

test('what the server says about ITSELF is read, and used to be thrown away', () => {
  // `--providers` has always answered these two and the panel's parser took {provider, auth, note}
  // off each row and discarded the rest — so a COAI_ROLES the server could not read was visible
  // only in a log nobody opens. Three lines of parser closed it.
  const notes = parseProviderNotes(JSON.stringify({
    providers: [],
    vaultNote: 'no COAI_CREDS_KEY configured — keyless vendors still work on their own auth',
    unrecognised: ['Role2: named more than once — the first row wins', 'COAI_ROUNDS_X is not a number'],
  }));

  assert.equal(notes.unrecognised.length, 2);
  assert.match(notes.unrecognised[0]!, /named more than once/u);
  assert.match(notes.vaultNote, /COAI_CREDS_KEY/u);
});

test('a body with no notes in it, or no body at all, is two empties rather than a throw', () => {
  // Advisory, so their absence is not an error: a build too old to send them and a body that is
  // not JSON at all must both leave the panel saying nothing rather than saying something wrong.
  assert.deepEqual(parseProviderNotes('{"providers":[]}'), NO_NOTES);
  assert.deepEqual(parseProviderNotes('not json'), NO_NOTES);
  assert.deepEqual(parseProviderNotes('null'), NO_NOTES);
});

test('a complaint that is not a sentence is dropped rather than rendered', () => {
  const notes = parseProviderNotes(JSON.stringify({
    unrecognised: ['a real one', '', 7, null, { nope: 1 }],
    vaultNote: 42,
  }));

  assert.deepEqual([...notes.unrecognised], ['a real one']);
  assert.equal(notes.vaultNote, '', 'a number is not a note, and "42" on screen would be worse than nothing');
});

test('the parser keeps what it can read and defaults what it cannot', () => {
  const one = parseProviders('{"providers":[{"provider":"codex","auth":"own auth"}]}');

  assert.equal(one?.['codex']?.auth, 'own auth');
  assert.equal(one?.['codex']?.note, '', 'a missing note is empty, not undefined');
});

test('the Server section says when the installed binary could not report at all', () => {
  // Not a badge on a card: a failed probe says nothing about any one reviewer, and a badge fed by
  // one would be a badge that lies. It IS a fact about this binary, and silence about a failed CHECK
  // is the class of defect this whole plan is about. Four reviewers raised it on the plan round.
  const html = page({
    providers: { reported: {}, asked: true, answered: false, notes: NO_NOTES },
    openSections: ['reviewers', 'server'],
  });

  assert.match(html, /could not report its reviewers/);
  assert.ok(!html.includes('cannot review'), 'and still badges no reviewer');
});

test('a binary that answered with no reviewers is not a binary that failed', () => {
  const html = page({
    providers: { reported: {}, asked: true, answered: true, notes: NO_NOTES },
    openSections: ['reviewers', 'server'],
  });

  assert.ok(!html.includes('could not report its reviewers'));
});

test('a binary that was never asked says nothing — that section already says the server is absent', () => {
  const html = page({
    providers: { reported: {}, asked: false, answered: false, notes: NO_NOTES },
    openSections: ['reviewers', 'server'],
  });

  assert.ok(!html.includes('could not report its reviewers'));
});
