import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import type { Catalog } from '../teamServerApi';
import { roleOnServers, serverRolesFrom, serverRuns, whyNotOnServer } from '../serverRoles';

/**
 * Which of a person's roles a configured Team server will run, said before a round rather than after.
 *
 * <p>The interesting half is the ABSENT field. A server older than plan 3 sends no `roles` property,
 * and that means the five this product ships — the behaviour that predates the field. Reading it as
 * "none" would silently empty every round; reading it as "any" would send custom roles to a server
 * certain to refuse them. This family lost `remoteVendor` twice for want of exactly that rule.</p>
 */

const shipped = BUILTIN_ROLES[0]!.id;

const catalog = (over: Partial<Catalog> = {}): Catalog => ({
  serverVersion: '0.5.1',
  isAdmin: false,
  vendors: [],
  error: '',
  ...over,
});

// ---------- reading a catalog ----------

test('a catalog with no roles field is a server that runs the five this product ships', () => {
  assert.deepStrictEqual(serverRolesFrom(catalog()), { kind: 'shipped' });
});

test('a catalog with an EMPTY roles list means the same as one with no field at all', () => {
  // A server that HAS the field always accepts at least five, so [] can only be a bug — and reading
  // it as "no roles" would take every reviewer out of every round against that server.
  assert.deepStrictEqual(serverRolesFrom(catalog({ roles: [] })), { kind: 'shipped' });
});

test('no catalog at all is UNKNOWN, which is not the same as an old server', () => {
  // A fetch that failed has no opinion about roles. Reading it as "the five it ships" would be
  // inventing an answer nobody gave, and then being unable to say why a role did not run.
  assert.deepStrictEqual(serverRolesFrom(undefined), { kind: 'unknown' });
});

test('a catalog that names roles is answered, and carries the flag it sent', () => {
  assert.deepStrictEqual(
    serverRolesFrom(catalog({ roles: ['Architecture', 'Requirements'], allowAnyRole: true })),
    { kind: 'answered', names: ['Architecture', 'Requirements'], allowAny: true },
  );
});

test('allowAnyRole absent is false rather than undefined', () => {
  const read = serverRolesFrom(catalog({ roles: ['Architecture'] }));

  assert.strictEqual(read.kind === 'answered' && read.allowAny, false);
});

// ---------- what a server will run ----------

test('a shipped role runs on every server, whatever its catalog said', () => {
  for (const roles of [serverRolesFrom(undefined), serverRolesFrom(catalog())]) {
    assert.strictEqual(serverRuns(roles, shipped), true);
  }
});

test('a role you added runs only where a server SAID it does', () => {
  assert.strictEqual(serverRuns(serverRolesFrom(catalog()), 'Requirements'), false);
  assert.strictEqual(serverRuns(serverRolesFrom(undefined), 'Requirements'), false);
  assert.strictEqual(
    serverRuns(serverRolesFrom(catalog({ roles: ['Requirements'] })), 'Requirements'), true);
});

test('the list is matched without case, because the server matches without case', () => {
  const roles = serverRolesFrom(catalog({ roles: ['Requirements'] }));

  assert.strictEqual(serverRuns(roles, 'requirements'), true);
  assert.strictEqual(serverRuns(roles, 'REQUIREMENTS'), true);
});

test('a server that runs anything runs a role it never named', () => {
  const roles = serverRolesFrom(catalog({ roles: ['Architecture'], allowAnyRole: true }));

  assert.strictEqual(serverRuns(roles, 'Invented'), true);
});

test('a server that NAMED its roles can refuse one this product ships', () => {
  // If it said which roles it runs, that list is the whole truth about it. Sending a shipped role
  // it left out would spend a round on a 400.
  const roles = serverRolesFrom(catalog({ roles: ['Requirements'] }));

  assert.strictEqual(serverRuns(roles, shipped), false);
});

// ---------- the sentence ----------

test('a role that will run says nothing at all', () => {
  assert.strictEqual(
    whyNotOnServer(serverRolesFrom(catalog({ roles: ['Requirements'] })), 'Requirements', 'Requirements we wrote', 'work'),
    '');
});

test('the sentence names the role the way the PERSON named it', () => {
  const line = whyNotOnServer(serverRolesFrom(catalog()), 'Requirements', 'Requirements we wrote', 'work');

  assert.ok(line.includes('Requirements we wrote'));
  assert.ok(line.includes('work'), 'and which server it is about');
});

test('an old server and an unreachable one give different sentences', () => {
  const old = whyNotOnServer(serverRolesFrom(catalog()), 'Requirements', 'Requirements', 'work');
  const gone = whyNotOnServer(serverRolesFrom(undefined), 'Requirements', 'Requirements', 'work');

  assert.ok(old.includes('older'));
  assert.ok(gone.includes('could not be asked'));
  assert.notStrictEqual(old, gone, 'one is fixed by upgrading a server, the other by looking at a network');
});

test('a server that answered says what it DOES run', () => {
  const line = whyNotOnServer(
    serverRolesFrom(catalog({ roles: ['Architecture', 'Conventions'] })), 'Requirements', 'Requirements', 'work');

  assert.ok(line.includes('Architecture'));
  assert.ok(line.includes('Conventions'));
});

// ---------- across every configured server ----------

test('no Team servers at all is silence', () => {
  assert.deepStrictEqual(roleOnServers([], 'Requirements', 'Requirements'), []);
});

test('a role every server runs is silence too', () => {
  const servers = [
    { name: 'work', catalog: catalog({ roles: ['Requirements'] }) },
    { name: 'other', catalog: catalog({ allowAnyRole: true, roles: ['Architecture'] }) },
  ];

  assert.deepStrictEqual(roleOnServers(servers, 'Requirements', 'Requirements'), []);
});

test('one sentence per server that will not run it', () => {
  const servers = [
    { name: 'work', catalog: catalog({ roles: ['Requirements'] }) },
    { name: 'old', catalog: catalog() },
    { name: 'gone', catalog: undefined },
  ];

  const lines = roleOnServers(servers, 'Requirements', 'Requirements');

  assert.strictEqual(lines.length, 2, 'work runs it; the other two do not');
  assert.ok(lines[0]!.includes('old'));
  assert.ok(lines[1]!.includes('gone'));
});

test('a shipped role is silent even against a server nobody could reach', () => {
  const servers = [{ name: 'gone', catalog: undefined }];

  assert.deepStrictEqual(roleOnServers(servers, shipped, shipped), [],
    'the five have always run everywhere, and a failed fetch does not change that');
});

// ---------- the catalog is a promise, not a fact ----------

test('a roles array carrying rubbish does not take the panel down with it', () => {
  // `Catalog` is a TypeScript interface — a promise about a value that came over HTTP from a server
  // somebody else configured. `{"roles":[null]}` reached `name.toLowerCase()` while the Prompts
  // section was being built, so one malformed response aborted the whole render.
  // (codex, story 4's code round.)
  for (const rubbish of [[null], [undefined], [1, 2], [{}], ['']]) {
    const read = serverRolesFrom(catalog({ roles: rubbish as unknown as string[] }));

    assert.deepStrictEqual(read, { kind: 'shipped' }, JSON.stringify(rubbish));
  }
});

test('roles that is not a list at all is read as an answer nobody could use', () => {
  for (const rubbish of ['Architecture', 42, {}, null]) {
    assert.deepStrictEqual(
      serverRolesFrom(catalog({ roles: rubbish as unknown as string[] })),
      { kind: 'shipped' },
      JSON.stringify(rubbish));
  }
});

test('the usable names survive a list that is only partly rubbish', () => {
  const read = serverRolesFrom(
    catalog({ roles: ['Requirements', null, '', 'Brief'] as unknown as string[] }));

  assert.deepStrictEqual(read, { kind: 'answered', names: ['Requirements', 'Brief'], allowAny: false });
});

test('a hostile catalog still carries the five this product ships', () => {
  // The floor holds whatever arrives: a round does not get smaller because a server sent nonsense.
  assert.strictEqual(
    serverRuns(serverRolesFrom(catalog({ roles: [null] as unknown as string[] })), shipped), true);
});

test('a name that is not a role ID does not make a catalog look answered', () => {
  // `[' ']` used to pass the "non-empty string" filter, so the catalog read as ANSWERED — and an
  // answered catalog is the whole truth about its server, so one space reported every shipped role
  // as unsupported. It is the ID RULE that decides what is usable. (CodeRabbit, plan 3.)
  for (const rubbish of [[' '], ['My-Role'], ['123Role'], ['role with spaces'], ['a'.repeat(49)]]) {
    assert.deepStrictEqual(
      serverRolesFrom(catalog({ roles: rubbish })), { kind: 'shipped' }, JSON.stringify(rubbish));
  }
});

test('a name with space around it is the same name', () => {
  // The server trims what it is sent, so a catalog naming ' Requirements ' names Requirements.
  const read = serverRolesFrom(catalog({ roles: ['  Requirements  '] }));

  assert.deepStrictEqual(read, { kind: 'answered', names: ['Requirements'], allowAny: false });
});

test('a role exactly at the id bound is still a role', () => {
  const longest = 'R'.repeat(48);

  assert.strictEqual(serverRuns(serverRolesFrom(catalog({ roles: [longest] })), longest), true);
});
