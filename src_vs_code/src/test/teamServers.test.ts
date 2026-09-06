import * as assert from 'node:assert';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  applicationIdOf,
  canonicalTeamServerUrl,
  isSafeAdvertisedScope,
  isUsableVendorId,
  newTeamServerId,
  noProviderMessage,
  offerableProviders,
  remoteVendorRowId,
  teamServerEndpoint,
  teamServerFingerprint,
  teamServersFrom,
  tokenFileName,
} from '../teamServers';

/**
 * The vectors live outside both implementations and BOTH suites assert them.
 *
 * The extension writes the Team-server token file and the MCP shim reads it, each deriving the path
 * independently — TypeScript's `URL` here, .NET's `Uri` there. Isolated unit tests on each side
 * cannot catch a divergence, because each side is self-consistent; the failure appears only as a
 * person signing in successfully and being told they are not signed in one second later, signing in
 * again, and it happening again. Raised independently by two reviewers on the plan round of epic 3.
 */
interface Vector {
  readonly why: string;
  readonly inputs: readonly string[];
  readonly canonical: string;
  readonly fingerprint: string;
}

const VECTORS: readonly Vector[] = JSON.parse(
  readFileSync(join(__dirname, '..', '..', '..', 'shared', 'team-server-url-vectors.json'), 'utf8'),
).vectors;

test('the shared fixture actually loaded', () => {
  // Without this, every assertion below would pass vacuously over an empty list.
  assert.ok(VECTORS.length > 5, `expected the shared vectors, got ${VECTORS.length}`);
});

test('every spelling canonicalises to the answer C# also produces', () => {
  for (const vector of VECTORS) {
    for (const input of vector.inputs) {
      assert.strictEqual(
        canonicalTeamServerUrl(input),
        vector.canonical,
        `${JSON.stringify(input)} — ${vector.why}`,
      );
    }
  }
});

test('every spelling fingerprints to the answer C# also produces', () => {
  for (const vector of VECTORS) {
    for (const input of vector.inputs) {
      assert.strictEqual(
        teamServerFingerprint(input),
        vector.fingerprint,
        `${JSON.stringify(input)} — ${vector.why}`,
      );
    }
  }
});

test('the token file is named by that fingerprint', () => {
  // The whole point of the fixture: this is the file the MCP shim will look for.
  assert.strictEqual(tokenFileName('https://coai.example.com/'), '0ac377feb52b33a9.token');
});

test('the request URL is built from the same canonical value as the hash', () => {
  // Normalising only for the hash left the slash in the request, so `{url}/api/…` became
  // `…com//api/…` — a 404 from a server the panel then called unhealthy, with a token that matched
  // perfectly.
  assert.strictEqual(
    teamServerEndpoint('https://coai.example.com/', '/api/catalog'),
    'https://coai.example.com/api/catalog',
  );
});

test('a server id is readable, stable, and never collides', () => {
  assert.strictEqual(newTeamServerId('RemSoft Dev', []), 'remsoft-dev');
  assert.strictEqual(newTeamServerId('RemSoft Dev', ['remsoft-dev']), 'remsoft-dev-2');
  assert.strictEqual(newTeamServerId('RemSoft Dev', ['remsoft-dev', 'remsoft-dev-2']), 'remsoft-dev-3');
  // A name with nothing usable in it still yields something addressable.
  assert.strictEqual(newTeamServerId('***', []), 'team');
});

test('a reviewer row is named by the server id, not by the editable display name', () => {
  // The display name can be changed; the row id cannot follow it without orphaning the row's usage
  // history and its vault key. Raised twice on the plan round.
  assert.strictEqual(remoteVendorRowId('remsoft-dev', 'codex'), 'remsoft-dev-codex');
});

test('a saved list survives a reload, and an entry from before ids gets one', () => {
  const saved = teamServersFrom([
    { id: 'remsoft-dev', name: 'RemSoft Dev', url: 'https://coai.remsoft.dev' },
    { name: 'Older Entry', url: 'https://old.example.com' },
    { name: 'no url here' },
    'not an object',
  ]);

  assert.strictEqual(saved.length, 2, 'an entry naming no server is not a configuration');
  assert.strictEqual(saved[0]?.id, 'remsoft-dev');
  assert.strictEqual(saved[1]?.id, 'older-entry', 'an upgrade must not orphan somebody’s servers');
});

test('two saved entries cannot share an id', () => {
  const saved = teamServersFrom([
    { id: 'dup', name: 'One', url: 'https://one.example.com' },
    { id: 'dup', name: 'Two', url: 'https://two.example.com' },
  ]);

  assert.notStrictEqual(saved[0]?.id, saved[1]?.id);
});

test('nothing saved is no servers, not a default one', () => {
  assert.deepStrictEqual(teamServersFrom(undefined), []);
  assert.deepStrictEqual(teamServersFrom('nonsense'), []);
});

test('a scope must name an application id AND the one permission', () => {
  const good = 'api://3afb5834-1111-2222-3333-444455556666/coai.access';
  assert.ok(isSafeAdvertisedScope(good));
  assert.ok(isSafeAdvertisedScope(good.toUpperCase()), 'a guid is not case-sensitive');
  assert.strictEqual(applicationIdOf(good), '3afb5834-1111-2222-3333-444455556666');
});

test('a hostile server cannot name a scope that reads anybody’s mail', () => {
  // The plan round's most serious finding: a shape check is not a trust check. A person adds a
  // server by typing a URL; without this, a hostile one could advertise any scope and the extension
  // would mint a Microsoft token for it and post it straight back.
  for (const hostile of [
    'https://graph.microsoft.com/Mail.Read',
    'https://graph.microsoft.com/.default',
    'Mail.ReadWrite',
    'api://graph.microsoft.com/coai.access',
    // The right application shape, the WRONG permission — a different scope on the same app.
    'api://3afb5834-1111-2222-3333-444455556666/Files.ReadWrite.All',
    'api://3afb5834-1111-2222-3333-444455556666/coai.access extra',
    'api://not-a-guid/coai.access',
    '',
    undefined,
    42,
  ]) {
    assert.strictEqual(isSafeAdvertisedScope(hostile), false, `accepted ${String(hostile)}`);
  }
});

test('only providers this build can complete are offered', () => {
  // Offering a provider with no implementation is a dead button: the person picks Google and
  // nothing happens. Caught on the plan round.
  assert.deepStrictEqual(offerableProviders(['microsoft', 'google']), ['microsoft']);
  assert.deepStrictEqual(offerableProviders(['google']), []);
  assert.deepStrictEqual(offerableProviders([]), []);
});

test('a server offering nothing we can do says which ones it offered', () => {
  const said = noProviderMessage('https://s', ['google']);

  assert.ok(said.includes('google'), 'naming what it offered is what makes it actionable');
  assert.ok(said.includes('microsoft'));
});

test('a vendor id a SERVER sent is checked before it becomes an id here', () => {
  // A catalog is a stranger's answer, and this id lands in a reviewer row id — which names that
  // row's spending history and its vault key — and on a command line as `--vendor`. Caught on the
  // code round.
  for (const good of ['codex', 'DeepSeek', 'gpt-oss', 'claude.opus', 'a', 'A1_b-c.d']) {
    assert.strictEqual(isUsableVendorId(good), true, `refused ${good}`);
  }

  for (const bad of [
    '../../other',
    '..',
    '/etc/passwd',
    'a/b',
    'a\b',
    'has space',
    '-leading-dash',
    '.leading-dot',
    '',
    'x'.repeat(65),
    undefined,
    42,
    null,
  ]) {
    assert.strictEqual(isUsableVendorId(bad), false, `accepted ${String(bad)}`);
  }
});

test('a server’s own spelling of its vendor is kept, not lower-cased', () => {
  // Both parsers used to lower-case it, so a catalog saying `DeepSeek` produced `--vendor deepseek`
  // and the server answered that it offers no such vendor. Caught on the code round.
  assert.strictEqual(isUsableVendorId('DeepSeek'), true);
});
