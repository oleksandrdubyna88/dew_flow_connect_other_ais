import assert from 'node:assert/strict';
import { test } from 'node:test';

import { HELP_ARTICLES, HELP_LANGUAGES, bodyFor } from '../helpContent';
import { NO_NOTES, parseProviders, type ProviderHealth } from '../providers';
import { roundsBody, type PanelState } from '../panelView';
import { parseSession, reviewerLines, reviewerRows, type RoundRecord, type SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import type { Vendor } from '../vendors';
import { everyPageHtml } from './panelPages';
import { pageTree } from './pageTree';

/**
 * A launch the server ALLOWED and still warned about is said where the person looks (the owner's decision of
 * 2026-10-13: an unmeasured Vibe version warns and runs instead of being refused).
 *
 * <p>The server carries one runtime-neutral string in two places (research/module_server.md, *A launch the control
 * WARNED about*): `reviewerStates[].warning` in the session file, for ANY status, and `providers[].warning` on the
 * providers wire. Both were invisible: the round's line showed a reviewer's note only when it failed, queued or stood
 * down, and the card showed `note` only for an unavailable row — while a warned Vibe is DONE and AVAILABLE.</p>
 */

const WARNING = 'Vibe 2.27.0 is not the measured 2.26.1: the confinement measurements and the plugin hashes are 2.26.1’s '
  + '— re-run scripts/vibe-home-manifest.mjs and the confinement probe.';

const round = (over: Partial<RoundRecord> = {}): RoundRecord => ({
  stage: 'PlanReview', number: 1, verdict: '', gatingCount: 0, reviewers: '', completedUtc: '', ...over,
});

const doneVibe = { provider: 'vibe', role: 'PlanCritique', status: 'done', findings: 2, note: '' };

// ---------------------------------------------------------------- the round's reviewer line

test('a DONE reviewer the server warned about carries the warning on its line', () => {
  const [row] = reviewerRows(round({ reviewerStates: [{ ...doneVibe, warning: WARNING }] }));

  assert.ok(row!.said.includes(WARNING), `the warning is not on the line: ${row!.said}`);
  assert.match(row!.said, /^done \(2 findings, warned: Vibe 2\.27\.0/u);
});

test('a reviewer with no warning — or a blank or foreign one — reads exactly as before', () => {
  const plain = reviewerRows(round({ reviewerStates: [doneVibe] }))[0]!.said;

  assert.equal(plain, 'done (2 findings)');
  for (const warning of ['', '   ', 42, null]) {
    const state = { ...doneVibe, warning } as unknown as typeof doneVibe;
    assert.equal(reviewerRows(round({ reviewerStates: [state] }))[0]!.said, plain, `warning ${JSON.stringify(warning)}`);
  }
});

test('the warning reaches the rounds log line, read from a session file as the server writes it', () => {
  const file = parseSession(JSON.stringify({
    state: { sessionId: 's', repoPath: 'D:/r', branch: 'main', stage: 'PlanReview', awaitingResolve: false },
    rounds: [round({ reviewerStates: [{ ...doneVibe, warning: WARNING }] })],
  }));

  assert.ok(reviewerLines(file!.rounds[0]!)[0]!.includes(`warned: ${WARNING}`));
});

test('the sidebar’s running round shows the warning under the reviewer it is about', () => {
  const session: SessionFile = {
    state: { sessionId: 's', repoPath: 'D:/r', branch: 'main', stage: 'PlanReview', awaitingResolve: false },
    rounds: [round({ status: 'running', startedUtc: '2026-10-13T10:00:00Z',
      reviewerStates: [{ ...doneVibe, warning: WARNING }, { provider: 'codex', role: 'PlanCritique', status: 'running', findings: 0, note: '' }] })],
  };
  const reviewers = pageTree(roundsBody([session], Date.parse('2026-10-13T10:05:00Z'), ['vibe', 'codex']))
    .find((node) => node.className === 'reviewer');

  assert.equal(reviewers.length, 2);
  assert.ok(reviewers[0]!.text().includes(WARNING), 'the warned reviewer does not show it');
  assert.ok(!reviewers[1]!.text().includes('warned'), 'the warning leaks onto another reviewer');
});

// ---------------------------------------------------------------- the row's card

test('providers[].warning is read off the wire, and an absent or empty one is no field at all', () => {
  const reported = parseProviders(JSON.stringify({ providers: [
    { provider: 'vibe', auth: 'own auth', note: 'the CLI’s own sign-in is used', warning: WARNING },
    { provider: 'codex', auth: 'own auth', note: '' },
    { provider: 'antigravity', auth: 'own auth', note: '', warning: '  ' },
  ] }))!;

  assert.equal(reported['vibe']!.warning, WARNING);
  assert.ok(!('warning' in reported['codex']!), 'an older server sends none, and none is read');
  assert.ok(!('warning' in reported['antigravity']!), 'a blank warning says nothing');
});

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

function pages(reported: Readonly<Record<string, ProviderHealth>>): string {
  const state: PanelState = {
    settings: DEFAULTS, vendors: [VIBE], codexModels: [], agyModels: [], localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '', perSide: false, questions: [], sessions: [], openSections: ['reviewers'], usage: [], usageWindow: 'day',
    latestServerVersion: '', cliStatus: {}, cardPrices: {}, snippetStatus: { kind: 'absent', current: 0 }, teamServers: [],
    providers: { reported, asked: true, answered: true, notes: NO_NOTES },
  };

  return everyPageHtml(state, 'nonce');
}

test('an AVAILABLE row the server warned about says so on its card, beside its note', () => {
  const html = pages({ vibe: { provider: 'vibe', auth: 'own auth', note: 'the CLI’s own sign-in is used', warning: WARNING } });
  const card = pageTree(html).one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card');
  const said = card.find((node) => node.className.split(' ').includes('warned'));

  assert.equal(said.length, 1, 'the card draws no warning');
  assert.ok(said[0]!.text().includes(WARNING), said[0]!.text());
  assert.ok(!html.includes('cannot review'), 'a warned row is not an unavailable one');
});

test('a card whose row carries no warning draws none — the page an older server gets is unchanged', () => {
  const card = pageTree(pages({ vibe: { provider: 'vibe', auth: 'own auth', note: '' } }))
    .one((node) => node.dataset['modelCard'] === 'vibe', 'the Vibe card');

  assert.equal(card.find((node) => node.className.split(' ').includes('warned')).length, 0);
});

// ---------------------------------------------------------------- the help, in five languages

test('every language’s choose-reviewers article says what a warned row looks like, in the words the page uses', () => {
  const article = HELP_ARTICLES.find((one) => one.id === 'choose-reviewers');
  assert.ok(article !== undefined, 'the reviewers article has been renamed, and this test is now asserting nothing');

  for (const language of HELP_LANGUAGES) {
    const { body, fallback } = bodyFor(article, language);
    const text = Object.values(body).join('\n');
    assert.equal(fallback, false, `the ${language} article is missing`);
    assert.ok(text.includes('**Runs with a warning:**'), `the ${language} article does not name the card's line`);
    assert.ok(text.includes('*warned:*'), `the ${language} article does not name the round line's word`);
  }
});
