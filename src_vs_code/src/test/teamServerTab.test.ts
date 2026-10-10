import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

import { CATALOG_CSS } from '../catalogCss';
import { CATALOG_BADGES, CATALOG_CHIPS, CATALOG_TOKENS } from '../catalogSheet';
import { NO_TEAM_PUSH, TEAM_SERVER_WITH_PEOPLE, isOlderServer, statusLine, teamParts, teamTabPush } from '../teamServerTab';
import { personCard } from '../teamPeople';
import { TEAM_TAB_CSS, teamMatches, teamOrder } from '../teamServerTabPage';
import { Usage } from '../teamServerApi';
import { TeamServerState } from '../teamServerView';
import {
  DEFAULT_TEAM_WINDOW, NO_SELECTION, selectedServer, usageWants, withPage, withServer, withShown, withWindow,
} from '../teamTabSelection';
import { pageTree } from './pageTree';
import { UsageCell } from '../teamUsageCache';

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

  assert.ok(
    text(older.chart).includes(`needs Team server ≥ ${TEAM_SERVER_WITH_PEOPLE}`),
    'an older server is told which version adds the chart',
  );
  assert.doesNotMatch(text(newer.chart), /needs Team server/, 'a newer server is not told it is too old');
});

test('a figure nobody can know yet is never a zero', () => {
  const parts = teamParts(state(), { usage: OLD_BODY, problem: '', answeredAt: 0 }, false, palette);
  const all = Object.values(parts).map(text).join(' ');

  assert.doesNotMatch(all, /\$0(\.0+)?\b/, 'a price that cannot be known rendered as $0');
  assert.match(text(parts.summary), /~ List price—/);
  assert.match(text(parts.badges), /2 ledger line\(s\) the server could not read/);
});

test('a NEWER server whose people listing is still on its way says it is asking — never "not shown by this version"', () => {
  const asking = teamParts(state(), { usage: NEW_BODY, problem: '', answeredAt: 0 }, false, palette,
    { price: () => undefined, now: 0, peopleAsking: true });
  const said = text(asking.summary) + text(asking.idle);

  assert.doesNotMatch(said, /not shown by this version/, 'a listing in flight was reported as a feature this extension lacks');
  assert.doesNotMatch(said, /needs Team server/, 'a newer server was told it is too old while its listing loaded');
  assert.match(said, /asking Acme…/i);

  const failed = teamParts(state(), { usage: NEW_BODY, problem: '', answeredAt: 0 }, false, palette,
    { price: () => undefined, now: 0, people: { problem: 'it did not answer within 10s', answeredAt: 0 } });
  assert.match(text(failed.idle), /could not be read: it did not answer within 10s/);
});

test('the push says WHY a sort is unavailable: asking, a failure, or an older server', () => {
  const push = (people: UsageCell | undefined, peopleAsking: boolean, usage: Usage) => teamTabPush({
    states: [state()], selection: withShown(NO_SELECTION, true), cell: () => ({ usage, problem: '', answeredAt: 0 }),
    asking: () => false, palette, people: () => people, peopleAsking: () => peopleAsking, price: () => undefined, now: 0,
  });

  assert.match(push(undefined, true, NEW_BODY).sortWhy?.seen ?? '', /asking/i);
  assert.match(push({ problem: 'boom', answeredAt: 0 }, false, NEW_BODY).sortWhy?.seen ?? '', /could not be read/);
  assert.match(push(undefined, false, OLD_BODY).sortWhy?.seen ?? '', /needs Team server ≥/);
  assert.equal(push({ people: [], problem: '', answeredAt: 0 }, false, NEW_BODY).sortWhy?.seen, '');
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
    people: () => undefined,
    peopleAsking: () => false,
    price: () => undefined,
    now: 0,
  });

  assert.deepEqual(pushed, NO_TEAM_PUSH);
});

test('an admin push carries the selected server, its window and when it was read', () => {
  const pushed = teamTabPush({
    states: [state()], selection: withShown(NO_SELECTION, true),
    cell: () => ({ usage: OLD_BODY, problem: '', answeredAt: Date.UTC(2026, 9, 9, 10, 0) }), asking: () => false, palette, people: () => undefined, peopleAsking: () => false, price: () => undefined, now: 0,
  });

  assert.equal(pushed.admin, true);
  assert.equal(pushed.selected, 'acme');
  assert.equal(pushed.window, DEFAULT_TEAM_WINDOW);
  assert.equal(pushed.readUtc, '2026-10-09T10:00:00.000Z');
});

test('a hostile server and person are text', () => {
  const card = personCard({ usage: { email: '"><script>x</script>', vendors: [] }, name: '"><script>y</script>', lastUsedUtc: '' },
    { serverId: 'acme', price: () => undefined, palette, now: 0 });

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
  const showing = withShown(withPage(hidden, 'front'), true);

  assert.deepEqual(usageWants(hidden, 'beta', 'today', admins).map((one) => one.scope), ['me']);
  assert.deepEqual(usageWants(showing, 'acme', 'today', admins).map((one) => one.scope), ['me']);
  assert.deepEqual(usageWants(showing, 'beta', 'today', admins), [
    { scope: 'me', window: 'today' }, { scope: 'company', window: DEFAULT_TEAM_WINDOW }, { scope: 'people', window: '' },
  ]);
  assert.equal(selectedServer(withServer(NO_SELECTION, 'gone'), admins), 'acme', 'a server no longer admin falls back to the first');
});

test('the page helpers: search is case-blind on the server text, sort is highest first and stable by email', () => {
  assert.equal(teamMatches('alice@example.com', ' ALI '), true);
  assert.equal(teamMatches('alice@example.com', 'bob'), false);
  const a = { launches: 2, tokens: 9, cost: 4, seen: 100, search: 'a' };
  const b = { launches: 5, tokens: 1, cost: -1, seen: 200, search: 'b' };
  assert.ok(teamOrder(a, b, 'launches') > 0);
  assert.ok(teamOrder(a, b, 'tokens') < 0);
  assert.ok(teamOrder(a, b, 'cost') < 0, 'a known ~$ sorts before an unknown one');
  assert.ok(teamOrder(a, b, 'seen') > 0, 'the more recently seen first');
  assert.ok(teamOrder(a, { ...a, search: 'c' }, 'launches') < 0, 'equal figures order by email');
  assert.ok(teamOrder(a, b, 'nonsense') > 0, 'an unknown key sorts by launches');
});

test('the Settings sheet is byte-identical after its tokens, chips and badges moved to the shared leaf', () => {
  // Pinned when the three pieces were EXTRACTED from catalogCss.ts (todo/PLAN_team_usage_by_person.md, 1.3): the
  // hash of CATALOG_CSS as main's UNEXTRACTED catalogCss.ts produces it. Re-pinned at the merge of main's #724 (the
  // Settings tabs' two columns, bd11f64a) from main's own file — built against this tree's settingsPage, never from
  // the merged sheet — so it still proves the extraction changed nothing. A deliberate change re-pins it, on purpose.
  assert.equal(createHash('sha256').update(CATALOG_CSS).digest('hex'),
    '9e1a9ad49c7ddecd983b12c40928a4e416afc02d678552ffcdbb9ad1f8d19b14');
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
