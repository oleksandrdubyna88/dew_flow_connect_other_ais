import * as assert from 'node:assert';
import { test } from 'node:test';
import { Catalog, CatalogVendor } from '../teamServerApi';
import { TeamServer, canonicalTeamServerUrl } from '../teamServers';
import {
  TeamServerState,
  disclosure,
  slotSentence,
  statusSentence,
  teamServerRow,
  teamServersBody,
} from '../teamServerView';

const SERVER: TeamServer = { id: 'remsoft-dev', name: 'RemSoft Dev', url: 'https://coai.example.com/' };

function vendor(slots: Partial<CatalogVendor['slots']>, id = 'codex'): CatalogVendor {
  return {
    id,
    runtime: 'codex',
    models: ['gpt-5.6'],
    slots: { total: 0, ready: 0, coolingDown: 0, needsSignIn: 0, ...slots },
  };
}

function catalog(vendors: CatalogVendor[]): Catalog {
  return { serverVersion: '0.5.1', isAdmin: false, vendors, error: '' };
}

function state(over: Partial<TeamServerState> = {}): TeamServerState {
  return { server: SERVER, email: '', problem: '', stale: false, ...over };
}

test('a ready vendor says how many of how many', () => {
  assert.strictEqual(slotSentence(vendor({ total: 3, ready: 2 })), 'codex: 2 of 3 ready');
});

test('a ready vendor that is partly cooling still says so', () => {
  assert.strictEqual(
    slotSentence(vendor({ total: 3, ready: 2, coolingDown: 1 })),
    'codex: 2 of 3 ready, 1 cooling down',
  );
});

test('accounts that are signed out name the person who must act', () => {
  // The distinction that decides who does something: signed-out accounts never come back on their
  // own, and rate-limited ones always do.
  const said = slotSentence(vendor({ total: 2, needsSignIn: 2 }));

  assert.ok(said.includes('signed out'));
  assert.ok(said.includes('operator'));
  assert.ok(!said.includes('come back'));
});

test('accounts that are merely rate-limited say they return by themselves', () => {
  const said = slotSentence(vendor({ total: 2, coolingDown: 2 }));

  assert.ok(said.includes('rate-limited'));
  assert.ok(said.includes('by themselves'));
});

test('a vendor with no accounts at all points at the operator', () => {
  assert.ok(slotSentence(vendor({})).includes('no accounts'));
});

test('the disclosure names the host, because that is the part people read', () => {
  // "An external endpoint is configured" is a sentence somebody skims. Asserted WHOLE rather than by
  // looking for the host inside it: the wording is the thing under test, and a substring check for a
  // hostname is the shape CodeQL flags as an incomplete URL check — correctly, since that pattern is
  // a real bypass wherever it decides anything.
  assert.strictEqual(
    disclosure('https://coai.example.com/'),
    'Every review sent here — your plan, your diffs and the file contents around them — goes to '
      + 'coai.example.com.',
  );
});

test('the disclosure is shown even for the company’s own server', () => {
  // Being the company's own server makes it no less true that the code leaves this machine. The row
  // must carry exactly what `disclosure` produces, which is a stronger claim than "the host appears
  // somewhere in the markup".
  const row = teamServerRow(state({ email: 'a@b.c' }));

  assert.ok(row.includes(disclosure(SERVER.url)), 'the row carries the disclosure verbatim');
});

test('a server nobody signed into says exactly that', () => {
  assert.strictEqual(statusSentence(state()), 'Not signed in.');
});

test('a signed-in server names the version it is running', () => {
  const said = statusSentence(state({ email: 'a@b.c', catalog: catalog([]) }));

  assert.ok(said.includes('0.5.1'));
});

test('a server that would not answer says so, and says the numbers are old', () => {
  // A stale catalog is more useful than a blank one, and saying it is stale is what keeps it honest.
  const said = statusSentence(state({
    email: 'a@b.c',
    catalog: catalog([]),
    problem: 'it did not answer within 10s',
    stale: true,
  }));

  assert.ok(said.includes('did not answer'));
  assert.ok(said.includes('last said'));
});

test('a first failure with nothing cached does not pretend to have numbers', () => {
  const said = statusSentence(state({ email: 'a@b.c', problem: 'connection refused', stale: false }));

  assert.strictEqual(said, 'connection refused');
});

/**
 * The fifth state, and the one that had no test until this section became the only place it shows.
 *
 * <p>Signed in and the catalog has not answered yet — what the *Server* section used to spell as
 * *connecting…* before that block was removed in 0.31.4. Three reviewers asked the same question
 * about that removal: does this section really carry every state the other one did? Four of the
 * five were covered above; this is the one that was not.</p>
 */
test('a server that has not answered yet says it is asking, not that it is broken', () => {
  const said = statusSentence(state({ email: 'a@b.c' }));

  assert.ok(said.includes('Signed in'), said);
  assert.ok(said.includes('Asking what it offers'), said);
});

/**
 * The address and the account, which now appear in this section and nowhere else.
 *
 * <p>Asserted here for the same reason: until 0.31.4 they were also in the *Server* section, so a
 * regression that dropped them from the row would still have shown them somewhere. Nothing is
 * behind this one now.</p>
 */
test('a row names the server it is, the address it points at and who is signed in', () => {
  const row = teamServerRow(state({ email: 'someone@company.example' }));

  assert.ok(row.includes('RemSoft Dev'), row);
  // Read out of its span and COMPARED, not `includes`-d. The weaker form is a
  // `js/incomplete-url-substring-sanitization` alert from CodeQL — which cannot tell a test
  // assertion from a host check written that way — and it caught this exact mistake in 0.31.2, one
  // file over. It is also the stronger assertion: an address anywhere in the row would satisfy it.
  assert.strictEqual(
    /<span class="ts-url">([^<]*)<\/span>/.exec(row)?.[1] ?? '',
    canonicalTeamServerUrl(SERVER.url),
  );
  assert.ok(row.includes('someone@company.example'), row);
});

test('the row offers Sign in when nobody is, and Sign out when somebody is', () => {
  assert.ok(teamServerRow(state()).includes('data-command="signInTeamServer"'));
  assert.ok(!teamServerRow(state()).includes('data-command="signOutTeamServer"'));

  const on = teamServerRow(state({ email: 'a@b.c' }));
  assert.ok(on.includes('data-command="signOutTeamServer"'));
  assert.ok(!on.includes('data-command="signInTeamServer"'));
});

test('every button carries the server it is about', () => {
  const row = teamServerRow(state({ email: 'a@b.c' }));

  // The id, not the display name: the name is editable and would stop matching.
  assert.ok(row.includes('data-id="remsoft-dev"'));
});

test('the slot line is the server’s own, vendor by vendor', () => {
  const row = teamServerRow(state({
    email: 'a@b.c',
    catalog: catalog([vendor({ total: 3, ready: 2 }), vendor({ total: 1, needsSignIn: 1 }, 'claude')]),
  }));

  assert.ok(row.includes('codex: 2 of 3 ready'));
  assert.ok(row.includes('claude: all 1 signed out'));
});

test('an empty section explains what a Team server is FOR', () => {
  const body = teamServersBody([]);

  assert.ok(body.includes('one company subscription') || body.includes('company subscription'));
  assert.ok(body.includes('data-command="addTeamServer"'));
});

test('a name or an email with markup in it cannot become markup', () => {
  const hostile = teamServerRow(state({
    server: { id: 'x', name: '<img src=x onerror=alert(1)>', url: 'https://s' },
    email: '"><script>alert(1)</script>',
  }));

  assert.ok(!hostile.includes('<img'));
  assert.ok(!hostile.includes('<script>'));
  assert.ok(hostile.includes('&lt;img'));
});
