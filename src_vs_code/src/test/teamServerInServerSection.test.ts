import * as assert from 'node:assert';
import { test } from 'node:test';
import { Catalog } from '../teamServerApi';
import { TeamServer } from '../teamServers';
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

test('a person with no Team server sees nothing new in the Server section', () => {
  assert.strictEqual(teamServerHere([], false), '');
});

test('the address is there, and it is read-only', () => {
  const html = teamServerHere([state({ email: 'a@remsoft.dev', catalog: catalog() })], false);

  assert.ok(html.includes('https://coai.remsoft.dev'), html);
  assert.ok(html.includes('readonly'), 'changing where a live session points is a sign-out');
  assert.ok(!html.includes('data-setting='), 'nothing here writes a setting');
});

test('every configured server gets its own block', () => {
  // The panel has always allowed several. Picking one to display would be right only by luck.
  const html = teamServerHere(
    [state({ email: 'a@remsoft.dev' }), state({ server: OTHER, email: 'a@remsoft.dev' })],
    false,
  );

  assert.ok(html.includes('https://coai.remsoft.dev'));
  assert.ok(html.includes('https://coai.staging.dev'));
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
  assert.ok(hereSentence(state()).startsWith('Not signed in'));
  assert.strictEqual(
    hereSentence(state({ elsewhere: 'a@remsoft.dev' })),
    'a@remsoft.dev is signed in on another side of this machine — press Sign in to use it here.',
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
