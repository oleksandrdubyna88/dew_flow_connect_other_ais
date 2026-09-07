import * as assert from 'node:assert';
import { test } from 'node:test';
import { Catalog } from '../teamServerApi';
import { TeamServer, canonicalTeamServerUrl } from '../teamServers';
import {
  TeamServerState,
  hereSentence,
  sideSentence,
  statusSentence,
  teamServerHere,
} from '../teamServerView';

/**
 * The *Server* section answers "what is this side talking to" — both halves of it.
 *
 * <p>The address is shown and NOT editable: pointing a live session somewhere else is a sign-out,
 * not a text edit. The version is the other half, and it only appears once something has actually
 * answered — a number that goes silently stale is what makes a dead connection look healthy.</p>
 */

const SERVER: TeamServer = { id: 'remsoft-dev', name: 'RemSoft Dev', url: 'https://coai.remsoft.dev/' };
const OTHER: TeamServer = { id: 'staging', name: 'Staging', url: 'https://coai.staging.dev' };

function catalog(serverVersion = '0.5.2'): Catalog {
  return { serverVersion, isAdmin: false, vendors: [], error: '' };
}

function state(over: Partial<TeamServerState> = {}): TeamServerState {
  return { server: SERVER, email: '', problem: '', stale: false, ...over };
}

/**
 * The address one block actually carries, read off the input's `value`.
 *
 * <p>Read out and COMPARED rather than asserted with `includes`, which was both the weaker test —
 * an address anywhere in the markup would have satisfied it, including inside a sentence — and a
 * `js/incomplete-url-substring-sanitization` alert from CodeQL, whose heuristic cannot tell a test
 * assertion from a security check written that way. This says the exact thing the test means.</p>
 */
function addressIn(html: string, serverId: string): string {
  return new RegExp(`id="ts-here-${serverId}"[^>]*value="([^"]*)"`).exec(html)?.[1] ?? '';
}

test('a person with no Team server sees nothing new in the Server section', () => {
  assert.strictEqual(teamServerHere([], false), '');
});

test('the address is there, and it is read-only', () => {
  const html = teamServerHere([state({ email: 'a@remsoft.dev', catalog: catalog() })], false);

  assert.strictEqual(addressIn(html, 'remsoft-dev'), canonicalTeamServerUrl(SERVER.url));
  assert.ok(html.includes('readonly'), 'changing where a live session points is a sign-out');
  assert.ok(!html.includes('data-setting='), 'nothing here writes a setting');
});

test('every configured server gets its own block', () => {
  // The panel has always allowed several. Picking one to display would be right only by luck.
  const html = teamServerHere(
    [state({ email: 'a@remsoft.dev' }), state({ server: OTHER, email: 'a@remsoft.dev' })],
    false,
  );

  assert.strictEqual(addressIn(html, 'remsoft-dev'), canonicalTeamServerUrl(SERVER.url));
  assert.strictEqual(addressIn(html, 'staging'), canonicalTeamServerUrl(OTHER.url));
});

test('the coai-server version appears once the server has answered', () => {
  assert.strictEqual(
    hereSentence(state({ email: 'a@remsoft.dev', catalog: catalog('0.5.2') })),
    'coai-server 0.5.2 — signed in as a@remsoft.dev.',
  );
});

test('before it answers, it says it is connecting rather than showing no version', () => {
  assert.strictEqual(
    hereSentence(state({ email: 'a@remsoft.dev' })),
    'Signed in as a@remsoft.dev — connecting…',
  );
});

test('a version that is only the last known one says so', () => {
  // Otherwise the number sits there looking healthy while nothing is answering.
  const said = hereSentence(state({
    email: 'a@remsoft.dev',
    catalog: catalog('0.5.2'),
    problem: 'the server did not answer',
    stale: true,
  }));

  assert.ok(said.includes('0.5.2'));
  assert.ok(said.includes('last known'), said);
});

test('a side that holds no token says so, even while another side is signed in', () => {
  // The defect this whole change is about: the record said "signed in", the distro had no token,
  // and the panel reported a session no review could use.
  assert.strictEqual(hereSentence(state()), 'Not signed in on this side.');
  assert.strictEqual(
    hereSentence(state({ elsewhere: 'a@remsoft.dev' })),
    'Not signed in on this side — a@remsoft.dev is signed in on another one.',
  );
});

test('this section never tells anybody to press a button it does not have', () => {
  // Every control is in *Team servers*. The first wording here ended in "press Sign in to use it
  // here", under a block whose only element is a read-only input. Caught on the code round.
  const out = teamServerHere([state({ elsewhere: 'a@remsoft.dev' })], false);

  assert.ok(!out.includes('press Sign in'), out);
  assert.ok(out.includes('Team servers'), 'it names where the buttons actually are');
  assert.ok(!out.includes('sign out under'), 'and does not ask somebody signed out to sign out');
});

test('a failure is never swallowed, whoever else is signed in', () => {
  // It used to be reachable only when another side was signed in, so with the sides separated a
  // refused sign-in and a server that is down both rendered as a bare "Not signed in."
  assert.strictEqual(
    hereSentence(state({ problem: 'the server did not answer' })),
    'Not signed in — the server did not answer',
  );
});

test('a side that could not sign itself in says why, not just that it is signed out', () => {
  const said = statusSentence(state({
    elsewhere: 'a@remsoft.dev',
    problem: 'your Microsoft session has expired — sign in again.',
  }));

  assert.ok(said.includes('a@remsoft.dev'));
  assert.ok(said.includes('Microsoft session has expired'), said);
});

test('which arrangement the machine is in is said, not implied', () => {
  assert.ok(sideSentence(false).includes('shares this sign-in'));
  assert.ok(sideSentence(true).includes('different accounts'));
});

test('the block points at where a change is actually made', () => {
  const html = teamServerHere([state({ email: 'a@remsoft.dev', catalog: catalog() })], false);

  assert.ok(html.includes('sign out'), 'the URL is fixed while a session is open — say where to go');
  assert.ok(html.includes('Team servers'));
});
