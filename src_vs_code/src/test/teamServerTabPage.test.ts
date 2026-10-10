import assert from 'node:assert/strict';
import { before, test } from 'node:test';

import { NO_TEAM_PUSH, TeamPush, teamTabPush } from '../teamServerTab';
import { TeamServerState } from '../teamServerView';
import { NO_SELECTION, TeamSelection, withShown } from '../teamTabSelection';
import { Usage } from '../teamServerApi';
import { UsageCell } from '../teamUsageCache';
import { BundledLog, bundledLogModule, DomPage, runningDomPage } from './roundsLogPageHarness';

/**
 * The Team server tab, RUN on the shipped page (todo/PLAN_team_usage_by_person.md, story 1.4).
 *
 * <p>Bundled and minified as it ships, executed over a document built from its own markup (`pageDom.ts`), and fed
 * pushes the host's real renderer built (`teamTabPush`). So what is asserted is what an admin would see — a tab that
 * is there or is not, cards a search hides, a card that stays open — and never a substring of the page's source.</p>
 */

let bundle: BundledLog;
before(() => { bundle = bundledLogModule(); });

const palette = (vendor: string): string => (vendor === 'codex' ? 'var(--codex)' : 'var(--other)');

function server(id: string, name: string, admin = true): TeamServerState {
  return {
    server: { id, name, url: `https://${id}.example.com` },
    email: 'admin@example.com',
    problem: '',
    stale: false,
    catalog: { serverVersion: '0.10.0', isAdmin: admin, vendors: [], error: '' },
  };
}

/** A company answer shaped like today's (0.10.0) server's: no models, no daily. */
function company(people: NonNullable<Usage['people']>): Usage {
  return {
    window: 'week',
    vendors: [
      { vendor: 'codex', runs: 7, failed: 1, tokensIn: 7000, tokensOut: 700, seconds: 70 },
      { vendor: 'gemini', runs: 2, failed: 0, tokensIn: 200, tokensOut: 20, seconds: 9 },
    ],
    people,
    unreadableLines: 0,
  };
}

const ALICE = { email: 'alice@example.com', vendors: [{ vendor: 'codex', runs: 2, failed: 0, tokensIn: 5000, tokensOut: 500, seconds: 20 }] };
const BOB = { email: 'bob@example.com', vendors: [{ vendor: 'codex', runs: 5, failed: 1, tokensIn: 2000, tokensOut: 200, seconds: 50 }] };

function push(
  states: readonly TeamServerState[],
  usage: Usage | undefined,
  selection: TeamSelection = withShown(NO_SELECTION, true),
): TeamPush {
  const cell: UsageCell | undefined = usage === undefined ? undefined : { usage, problem: '', answeredAt: Date.UTC(2026, 9, 9, 10, 0) };

  return teamTabPush({ states, selection, cell: () => cell, asking: () => false, palette, people: () => undefined, peopleAsking: () => false, price: () => undefined, now: 0 });
}

function send(page: DomPage, team: TeamPush): void {
  page.message({ type: 'team', ...team });
}

function cards(page: DomPage): ReturnType<DomPage['element']>[] {
  return page.element('team-people').querySelectorAll('details[data-fold]');
}

function shownEmails(page: DomPage): string[] {
  return cards(page).filter((card) => !card.hidden).map((card) => card.getAttribute('data-search') ?? '');
}

function openTeamTab(page: DomPage): void {
  page.element('team-tab').click();
}

function typeSearch(page: DomPage, text: string): void {
  const box = page.element('team-q');
  box.value = text;
  page.document.dispatch('input', box);
}

test('a non-admin page has no Team server tab and no section — before any push and after one', () => {
  const page = runningDomPage(bundle);

  assert.equal(page.element('team-tab').hidden, true, 'the tab button showed before anything said this is an admin');
  assert.equal(page.element('tab-team').hidden, true);
  send(page, push([server('acme', 'Acme', false)], undefined));

  assert.equal(page.element('team-tab').hidden, true, 'a person who is no admin anywhere was shown the tab');
  assert.equal(page.element('tab-team').hidden, true);
});

test('an admin sees the tab, and pressing it shows the people the server counted', () => {
  const page = runningDomPage(bundle);

  send(page, push([server('acme', 'Acme')], company([ALICE, BOB])));
  assert.equal(page.element('team-tab').hidden, false, 'the admin flag did not reveal the tab');
  openTeamTab(page);

  assert.equal(page.element('tab-team').hidden, false, 'the tab was pressed and its section stayed hidden');
  assert.equal(page.element('tab-rounds').hidden, true);
  assert.deepEqual(shownEmails(page), ['bob@example.com', 'alice@example.com'], 'the cards, by launches, highest first');
  assert.match(page.element('team-summary').textContent, /Launches9/);
  assert.equal(page.element('team-tab').textContent, 'Team serveradmin', 'the admin mark is a child the tab handler keeps');
  assert.deepEqual(page.sent.filter((one) => one['command'] === 'teamTab').map((one) => one['id']), ['shown'],
    'the host was not told the tab is showing, so it would never ask for company figures');
});

test('search narrows the people, by email, and says how many match', () => {
  const page = runningDomPage(bundle);
  send(page, push([server('acme', 'Acme')], company([ALICE, BOB])));

  typeSearch(page, 'ALI');

  assert.deepEqual(shownEmails(page), ['alice@example.com']);
  assert.equal(page.element('team-count').textContent, '1 of 2');
  typeSearch(page, 'nobody');
  assert.equal(page.element('team-none').hidden, false, 'a search that matches nobody says so');
});

test('the sort is the page\'s own, and moves the cards themselves', () => {
  const page = runningDomPage(bundle);
  send(page, push([server('acme', 'Acme')], company([ALICE, BOB])));
  const sort = page.element('team-sort');

  sort.value = 'tokens';
  page.document.dispatch('change', sort);

  assert.deepEqual(cards(page).map((card) => card.getAttribute('data-search')), ['alice@example.com', 'bob@example.com'],
    'Alice spent the most tokens and is not first');
  assert.ok(page.sent.every((one) => one['command'] !== 'teamSort'), 'a sort is no round trip');
});

test('a hostile email renders as text, never as markup', () => {
  const page = runningDomPage(bundle);
  // Quoted, so that — unescaped — the page's reader WOULD make an element of it, and the test could see one.
  const hostile = { ...ALICE, email: '<img src="x" onerror="alert(1)">@example.com' };

  send(page, push([server('acme', '<b>Acme</b>')], company([hostile])));

  assert.equal(page.element('team-people').querySelectorAll('img').length, 0, 'an email from the server became an element');
  assert.ok(page.element('team-people').textContent.includes(hostile.email), 'the email is not shown as the text it is');
  assert.equal(page.element('team-server').querySelectorAll('b').length, 0, 'a server name became markup');
});

test('a push keeps the search, its focus, the order and an opened card', () => {
  const page = runningDomPage(bundle);
  send(page, push([server('acme', 'Acme')], company([ALICE, BOB])));
  const sort = page.element('team-sort');
  sort.value = 'tokens';
  page.document.dispatch('change', sort);
  typeSearch(page, 'example');
  page.element('team-q').focus();
  cards(page).find((card) => card.getAttribute('data-search') === 'bob@example.com')!.open = true;

  send(page, push([server('acme', 'Acme')], company([ALICE, { ...BOB, vendors: [{ ...BOB.vendors[0]!, runs: 6 }] }])));

  assert.equal(page.element('team-q').value, 'example', 'the search text was wiped by a push');
  assert.equal(page.document.activeElement, page.element('team-q'), 'the search box lost the caret to a push');
  assert.deepEqual(cards(page).map((card) => card.getAttribute('data-search')), ['alice@example.com', 'bob@example.com'],
    'the order went back to the default');
  const bob = cards(page).find((card) => card.getAttribute('data-search') === 'bob@example.com')!;
  assert.equal(bob.open, true, 'the card the admin opened snapped shut under them');
  assert.match(bob.textContent, /6 launches/, 'and it is the NEW figure that is shown');
});

test('the same email on another server is another card — an opened one does not follow the switch', () => {
  const page = runningDomPage(bundle);
  const both = [server('acme', 'Acme'), server('beta', 'Beta')];
  send(page, push(both, company([ALICE])));
  cards(page)[0]!.open = true;

  send(page, push(both, company([ALICE]), { page: 'front', shown: true, server: 'beta', windows: {} }));

  assert.equal(page.element('team-server-pick').hidden, false, 'two admin servers and no picker');
  assert.equal(page.element('team-server').value, 'beta');
  assert.equal(cards(page)[0]!.open, false, 'Alice on Beta opened because Alice on Acme was open');
});

test('what today\'s server cannot say says which server it needs — never a zero', () => {
  const page = runningDomPage(bundle);

  send(page, push([server('acme', 'Acme')], company([ALICE])));

  const said = ['team-summary', 'team-vendors', 'team-chart', 'team-idle'].map((id) => page.element(id).textContent);
  for (const text of said) {
    assert.match(text, /needs Team server ≥ 0\.11\.0/, `a missing figure said nothing: ${text}`);
  }
  assert.ok(said.every((text) => !/\$0(\.0+)?\b/.test(text)), 'a price nobody knows rendered as $0');
  assert.match(page.element('team-summary').textContent, /~ List price—/);
});

test('the chips, Refresh, the picker and the tab each tell the host, naming the server', () => {
  const page = runningDomPage(bundle);
  send(page, push([server('acme', 'Acme'), server('beta', 'Beta')], company([ALICE])));
  openTeamTab(page);

  page.element('tab-team').querySelector('[data-team-window="year"]')!.click();
  page.element('team-refresh').click();
  const picker = page.element('team-server');
  picker.value = 'beta';
  page.document.dispatch('change', picker);
  page.document.querySelector('[data-tab="rounds"]')!.click();

  const told = page.sent.filter((one) => String(one['command'] ?? '').startsWith('team')).map((one) => `${String(one['command'])} ${String(one['id'])}`);
  assert.deepEqual(told, ['teamTab shown', 'teamWindow acme|year', 'teamRefresh acme', 'teamServer beta', 'teamTab hidden']);
  assert.equal(page.element('tab-team').querySelector('[data-team-window="year"]')!.getAttribute('aria-pressed'), 'true');
  assert.equal(page.element('tab-team').querySelector('[data-team-window="week"]')!.getAttribute('aria-pressed'), 'false');
});

test('an admin demoted while looking is taken back to Rounds, and the tab goes', () => {
  const page = runningDomPage(bundle);
  send(page, push([server('acme', 'Acme')], company([ALICE])));
  openTeamTab(page);

  send(page, NO_TEAM_PUSH);

  assert.equal(page.element('team-tab').hidden, true);
  assert.equal(page.element('tab-team').hidden, true, 'the company figures stayed on screen for somebody no longer an admin');
  assert.equal(page.element('tab-rounds').hidden, false);
  // Hidden is not gone: the section is still in the document, and anything left in it is one style change from view.
  assert.ok(!page.element('tab-team').textContent.includes('alice@example.com'),
    'the demoted admin\'s page still holds a colleague\'s email, merely hidden');
  assert.equal(page.element('team-people').querySelectorAll('details').length, 0);
  assert.equal(page.element('team-server').querySelectorAll('option').length, 0, 'the server list stayed behind');
});

test('signing out pushes no tab, and the page then holds no email at all', () => {
  const page = runningDomPage(bundle);
  send(page, push([server('acme', 'Acme')], company([ALICE, BOB])));

  // After a sign-out the host no longer holds a catalog for the server, so the push it builds says: no admin anywhere.
  const { catalog: _gone, ...signedOut } = { ...server('acme', 'Acme'), email: '' };
  send(page, push([signedOut], company([ALICE, BOB])));

  assert.equal(page.element('team-tab').hidden, true);
  assert.ok(!/@example\.com/.test(page.element('tab-team').textContent), 'an email survived the sign-out on the page');
});

test('the fifteen-second watchdog leaves a tab alone once it was pushed, and speaks when it never was', () => {
  const told = runningDomPage(bundle);
  send(told, push([server('acme', 'Acme')], undefined));
  told.runTimers();
  assert.match(told.element('team-status').textContent, /Asking Acme…/, 'a pushed tab was replaced by the watchdog');

  const never = runningDomPage(bundle);
  never.runTimers();
  assert.match(never.element('team-status').textContent, /never received its data/);
});

// ---------- epic 3: the same page against a NEW server's body (models, daily, the people listing) ----------

const NOW = Date.UTC(2026, 9, 10, 12, 0);

/** A company answer as a server ≥ 0.11.0 sends it: models under each vendor, 30 dense days, people. */
function newCompany(): Usage {
  const model = (name: string, runs: number, tokensIn: number, tokensOut: number) => ({ model: name, runs, failed: 0, tokensIn, tokensOut });
  const vendor = (name: string, runs: number, models: ReturnType<typeof model>[]) => ({
    vendor: name, runs, failed: 0, seconds: 1,
    tokensIn: models.reduce((s, m) => s + m.tokensIn, 0), tokensOut: models.reduce((s, m) => s + m.tokensOut, 0), models,
  });
  const days = Array.from({ length: 30 }, (_, i) => ({
    day: `2026-09-${String(11 + i).padStart(2, '0')}`.replace(/2026-09-(3[1-9]|4\d)/, (_m, d: string) => `2026-10-${String(Number(d) - 30).padStart(2, '0')}`),
    vendors: [{ vendor: 'codex', runs: i % 3 }, { vendor: 'gemini', runs: i === 29 ? 4 : 0 }],
  }));

  return {
    window: 'week',
    vendors: [vendor('codex', 7, [model('gpt-5.6-sol', 7, 3_000_000, 300_000)]), vendor('gemini', 2, [model('', 2, 100, 10)])],
    people: [
      { email: 'alice@example.com', vendors: [vendor('codex', 2, [model('gpt-5.6-sol', 2, 2_000_000, 200_000)])] },
      { email: 'bob@example.com', vendors: [vendor('codex', 5, [model('gpt-5.6-sol', 5, 1_000_000, 100_000)]), vendor('gemini', 2, [model('', 2, 100, 10)])] },
    ],
    unreadableLines: 0,
    daily: { fromUtc: '2026-09-11T00:00:00Z', toUtc: '2026-10-11T00:00:00Z', days },
  };
}

const LISTED = [
  { email: 'Alice@Example.com', displayName: 'Alice Smith', lastUsedUtc: new Date(NOW - 30 * 60_000).toISOString() },
  { email: 'bob@example.com', displayName: 'Bob <b>Jones</b>', lastUsedUtc: new Date(NOW - 5 * 3_600_000).toISOString() },
  { email: 'carol@example.com', displayName: 'Carol Idle', lastUsedUtc: new Date(NOW - 50 * 3_600_000).toISOString() },
];

function pushNew(usage: Usage = newCompany()): TeamPush {
  const cell: UsageCell = { usage, problem: '', answeredAt: NOW };
  const people: UsageCell = { people: LISTED, problem: '', answeredAt: NOW };

  return teamTabPush({
    states: [server('acme', 'Acme')], selection: withShown(NO_SELECTION, true), cell: () => cell, asking: () => false, palette,
    people: () => people,
    peopleAsking: () => false,
    price: (name) => (name === 'gpt-5.6-sol' ? { inPerMillion: 2, outPerMillion: 10, source: 'openrouter' } : undefined),
    now: NOW,
  });
}

test('a NEW server: names, last seen, ~$, the idle list and the chart — and nothing says a newer server is needed', () => {
  const page = runningDomPage(bundle);
  send(page, pushNew());

  const text = page.element('tab-team').textContent;
  assert.match(text, /Alice Smith/);
  assert.match(text, /last seen within the hour/);
  assert.match(text, /last seen 5 h ago/);
  assert.match(page.element('team-summary').textContent, /≥ ~\$9\.00/, 'codex at list price is ~$9.00; gemini\'s unknown model makes it a floor');
  assert.match(page.element('team-idle').textContent, /Signed in, no recorded runs in this window: 1/);
  assert.match(page.element('team-idle').textContent, /Carol Idle/);
  assert.ok(page.element('team-chart').querySelectorAll('rect').length > 0, 'the chart drew no bars');
  assert.match(page.element('team-chart').textContent, /codex/);
  assert.doesNotMatch(text, /needs Team server/, 'a newer server was told it is too old');
  assert.equal(page.element('team-people').querySelectorAll('b').filter((one) => one.textContent === 'Jones').length, 0,
    'a display name became markup on the page');
});

test('a NEW server: the search matches names, says so, and ~$ and last seen become sorts', () => {
  const page = runningDomPage(bundle);
  send(page, pushNew());

  assert.equal(page.element('team-q').getAttribute('placeholder'), 'Search name or email');
  typeSearch(page, 'smith');
  assert.deepEqual(shownEmails(page), ['alice@example.com alice smith']);
  typeSearch(page, '');
  const sort = page.element('team-sort');
  const cost = sort.querySelector('option[value="cost"]')!;
  assert.equal(cost.disabled, false, 'a new server sends per-model figures, and the ~$ sort is still disabled');
  assert.equal(cost.textContent, '~$');
  sort.value = 'cost';
  page.document.dispatch('change', sort);
  assert.deepEqual(cards(page).map((card) => card.getAttribute('data-fold')), ['acme|alice@example.com', 'acme|bob@example.com'],
    'Alice cost ~$6, Bob ~$3 and a floor — the order by ~$ is wrong');
  sort.value = 'seen';
  page.document.dispatch('change', sort);
  assert.equal(cards(page)[0]!.getAttribute('data-fold'), 'acme|alice@example.com', 'the most recently seen first');
});

test('an OLD server after a new one: the sorts it cannot serve go back to saying why, and the search to email', () => {
  const page = runningDomPage(bundle);
  send(page, pushNew());
  const sort = page.element('team-sort');
  sort.value = 'cost';
  page.document.dispatch('change', sort);

  send(page, push([server('acme', 'Acme')], company([ALICE, BOB])));

  const cost = sort.querySelector('option[value="cost"]')!;
  assert.equal(cost.disabled, true);
  assert.match(cost.textContent, /needs Team server ≥ 0\.11\.0/);
  assert.equal(sort.value, 'launches', 'a sort the answer cannot serve stayed chosen');
  assert.equal(page.element('team-q').getAttribute('placeholder'), 'Search email');
  assert.doesNotMatch(page.element('tab-team').textContent, /\$0(\.0+)?\b/, 'an old server rendered a zero price');
  // The cards too: an older server cannot say when anybody was last seen, so a card does not pretend to with a dash.
  assert.doesNotMatch(page.element('team-people').textContent, /last seen/, 'an old server\'s card shows a last-seen it cannot know');
});

test('a push keeps the chart\'s numbers open — they are its text alternative, not decoration', () => {
  const page = runningDomPage(bundle);
  send(page, pushNew());
  const fold = page.element('team-chart').querySelector('details[data-fold]');
  assert.ok(fold !== null, 'the numbers fold has no fold key, so nothing can keep it open');
  fold.open = true;

  send(page, pushNew());

  assert.equal(page.element('team-chart').querySelector('details[data-fold]')?.open, true,
    'a push snapped the chart\'s numbers shut under the person reading them');
});
