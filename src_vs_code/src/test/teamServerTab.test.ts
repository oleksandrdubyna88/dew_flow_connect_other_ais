import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

import { CATALOG_CSS } from '../catalogCss';
import { CATALOG_BADGES, CATALOG_CHIPS, CATALOG_TOKENS } from '../catalogSheet';
import { NO_TEAM_PUSH, TEAM_SERVER_WITH_PEOPLE, isOlderServer, personCard, statusLine, teamParts, teamTabPush } from '../teamServerTab';
import { TEAM_TAB_CSS, teamMatches, teamOrder } from '../teamServerTabPage';
import { Usage } from '../teamServerApi';
import { TeamServerState } from '../teamServerView';
import {
  DEFAULT_TEAM_WINDOW, NO_SELECTION, selectedServer, usageWants, withServer, withShown, withWindow,
} from '../teamTabSelection';
import { pageTree } from './pageTree';

/**
 * The Team server tab's renderers and its selection, pure (todo/PLAN_team_usage_by_person.md, stories 1.2 and 1.3).
 * What the PAGE does with them is `teamServerTabPage.test.ts`, which runs it.
 */

const palette = (): string => 'var(--c)';

function state(over: Partial<TeamServerState> = {}): TeamServerState {
  return {
    server: { id: 'acme', name: 'Acme', url: 'https://acme.example.com' },
    email: 'admin@example.com',
    problem: '',
    stale: false,
    catalog: { serverVersion: '0.10.0', isAdmin: true, vendors: [], error: '' },
    ...over,
  };
}

/** A body exactly as server 0.10.0 answers `GET /api/usage?window=week&scope=company` — no models, no daily. */
const OLD_BODY = JSON.parse(`{
  "fromUtc": "2026-10-02T10:00:00+00:00", "toUtc": "2026-10-09T10:00:00+00:00", "scope": "company",
  "vendors": [{ "vendor": "codex", "runs": 3, "failed": 1, "tokensIn": 3000, "tokensOut": 300, "seconds": 30,
    "costUsd": null, "costIsFloor": false, "unpricedRuns": 3 }],
  "people": [{ "email": "Alice@Example.com", "vendors": [{ "vendor": "codex", "runs": 3, "failed": 1, "tokensIn": 3000,
    "tokensOut": 300, "seconds": 30, "costUsd": null, "costIsFloor": false, "unpricedRuns": 3 }] }],
  "unreadableLines": 2, "kinds": []
}`) as Usage;

const NEW_BODY: Usage = { ...OLD_BODY, daily: { fromUtc: '', toUtc: '', days: [] } };

function text(html: string): string {
  return pageTree(html).text();
}

test('two casings of one email are ONE person — the server grouped them, and the client never regroups (D2)', () => {
  const html = teamParts(state(), { usage: OLD_BODY, problem: '', answeredAt: 0 }, false, palette).people;
  const cards = pageTree(html).find((node) => node.tagName === 'DETAILS');

  assert.equal(cards.length, 1);
  assert.equal(cards[0]!.attrs['data-fold'], 'acme|alice@example.com', 'the fold key is the server and the lower-cased email');
  assert.match(text(html), /Alice@Example\.com/, 'and the email is shown as the server wrote it');
});

test('an older server is recognised by the absence of daily, never by an empty models list (D8)', () => {
  assert.equal(isOlderServer(OLD_BODY), true);
  assert.equal(isOlderServer(NEW_BODY), false);
  const older = teamParts(state(), { usage: OLD_BODY, problem: '', answeredAt: 0 }, false, palette);
  const newer = teamParts(state(), { usage: NEW_BODY, problem: '', answeredAt: 0 }, false, palette);

  assert.match(text(older.chart), new RegExp(`needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE.replace(/\./g, '\\.')}`));
  assert.doesNotMatch(text(newer.chart), /needs Team server/, 'a newer server is not told it is too old');
});

test('a figure nobody can know yet is never a zero', () => {
  const parts = teamParts(state(), { usage: OLD_BODY, problem: '', answeredAt: 0 }, false, palette);
  const all = Object.values(parts).map(text).join(' ');

  assert.doesNotMatch(all, /\$0(\.0+)?\b/, 'a price that cannot be known rendered as $0');
  assert.match(text(parts.summary), /~ List price—/);
  assert.match(text(parts.badges), /2 ledger line\(s\) the server could not read/);
});

test('a refused company view says the caller is no longer an admin here', () => {
  const html = statusLine(state(), { problem: 'scope=company is for admins.', refused: true, answeredAt: 0 }, false);

  assert.match(text(html), /You are no longer an admin here/);
});

test('nothing answered yet says it is asking, rather than showing an empty tab', () => {
  assert.match(text(statusLine(state(), undefined, false)), /Asking Acme…/);
  assert.match(text(statusLine(state(), { usage: OLD_BODY, problem: '', answeredAt: 0 }, true)), /Asking Acme…/);
  assert.equal(statusLine(state(), { usage: OLD_BODY, problem: '', answeredAt: 0 }, false), '');
});

test('a non-admin is pushed nothing but the flag — not a single email', () => {
  const pushed = teamTabPush({
    states: [state({ catalog: { serverVersion: '0.10.0', isAdmin: false, vendors: [], error: '' } })],
    selection: withShown(NO_SELECTION, true),
    cell: () => ({ usage: OLD_BODY, problem: '', answeredAt: 0 }),
    asking: () => false,
    palette,
  });

  assert.deepEqual(pushed, NO_TEAM_PUSH);
});

test('an admin push carries the selected server, its window and when it was read', () => {
  const pushed = teamTabPush({
    states: [state()], selection: withShown(NO_SELECTION, true),
    cell: () => ({ usage: OLD_BODY, problem: '', answeredAt: Date.UTC(2026, 9, 9, 10, 0) }), asking: () => false, palette,
  });

  assert.equal(pushed.admin, true);
  assert.equal(pushed.selected, 'acme');
  assert.equal(pushed.window, DEFAULT_TEAM_WINDOW);
  assert.equal(pushed.readUtc, '2026-10-09T10:00:00.000Z');
});

test('a hostile server and person are text', () => {
  const card = personCard('acme', { email: '"><script>x</script>', vendors: [] }, palette);

  assert.ok(!card.includes('<script>'), 'an email became a script element');
  assert.equal(pageTree(card).find((node) => node.tagName === 'DETAILS').length, 1, 'the card closed early');
});

test('a window chip names its server and a window the server knows; anything else is ignored', () => {
  const chosen = withWindow(NO_SELECTION, 'acme|year');

  assert.deepEqual(chosen.windows, { acme: 'year' });
  assert.deepEqual(withWindow(chosen, 'acme|decade'), chosen);
  assert.deepEqual(withWindow(chosen, '|year'), chosen);
  assert.deepEqual(withWindow(chosen, 'acme-year'), chosen);
});

test('company figures are wanted only while the tab shows THAT server', () => {
  const admins = ['acme', 'beta'];
  const hidden = withServer(NO_SELECTION, 'beta');
  const showing = withShown(hidden, true);

  assert.deepEqual(usageWants(hidden, 'beta', 'today', admins).map((one) => one.scope), ['me']);
  assert.deepEqual(usageWants(showing, 'acme', 'today', admins).map((one) => one.scope), ['me']);
  assert.deepEqual(usageWants(showing, 'beta', 'today', admins), [
    { scope: 'me', window: 'today' }, { scope: 'company', window: DEFAULT_TEAM_WINDOW },
  ]);
  assert.equal(selectedServer(withServer(NO_SELECTION, 'gone'), admins), 'acme', 'a server no longer admin falls back to the first');
});

test('the page helpers: search is case-blind on the server text, sort is highest first and stable by email', () => {
  assert.equal(teamMatches('alice@example.com', ' ALI '), true);
  assert.equal(teamMatches('alice@example.com', 'bob'), false);
  const a = { launches: 2, tokens: 9, search: 'a' };
  const b = { launches: 5, tokens: 1, search: 'b' };
  assert.ok(teamOrder(a, b, 'launches') > 0);
  assert.ok(teamOrder(a, b, 'tokens') < 0);
  assert.ok(teamOrder(a, { ...a, search: 'c' }, 'launches') < 0, 'equal figures order by email');
});

test('the Settings sheet is byte-identical after its tokens, chips and badges moved to the shared leaf', () => {
  // Pinned when the three pieces were EXTRACTED from catalogCss.ts (todo/PLAN_team_usage_by_person.md, 1.3): the
  // hash of CATALOG_CSS as it was before the move. A deliberate change to the Settings sheet re-pins it, on purpose.
  assert.equal(createHash('sha256').update(CATALOG_CSS).digest('hex'),
    '09f5493ba751296ed0d0b20dfb44b1a193e87fb934e2b46a85b60dfd7a662783');
  for (const piece of [CATALOG_TOKENS, CATALOG_CHIPS, CATALOG_BADGES]) {
    assert.ok(CATALOG_CSS.includes(piece) && TEAM_TAB_CSS.includes(piece), 'both sheets wear the one shared piece');
  }
});

test('the shared sheet is a LEAF — it imports nothing, so the rounds page never drags the Settings sheet in', () => {
  const source = fs.readFileSync(path.join(process.cwd(), 'src', 'catalogSheet.ts'), 'utf8');

  assert.doesNotMatch(source, /^\s*import\b/m);
  assert.match(fs.readFileSync(path.join(process.cwd(), 'src', 'catalogCss.ts'), 'utf8'), /from '\.\/catalogSheet'/,
    'and the Settings sheet still takes its pieces from it — the scan above is looking at the right file');
});
