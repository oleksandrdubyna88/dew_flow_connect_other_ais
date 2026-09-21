import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { namesACredential } from '../credentialWords';

/**
 * One list, and it has to be broad without being silly.
 *
 * <p>Two callers depend on this judgement and they pay different prices for a wrong answer.
 * `consultantWrite` REFUSES a URL it thinks carries a key, so a false positive is a person unable to
 * save a legitimate endpoint. The notifications ledger REDACTS, so a false positive is a diagnostic
 * parameter replaced by `[redacted]` in the one record somebody needed — and, worse over time, a
 * mechanism people learn to ignore.</p>
 */

/** One row of the corpus both halves answer. A real type, so nothing below is an `as`. */
interface CredentialCase {
  readonly name: string;
  readonly credential: boolean;
}

/**
 * The shared corpus, VALIDATED rather than asserted into shape.
 *
 * <p>`JSON.parse(...) as { cases: ... }` is a cast standing in for a real type, which this
 * repository's testing rule names: the compiler then checks nothing, and a fixture that lost its
 * `credential` field would read as `undefined` and quietly compare unequal to `false`. This walks
 * the rows and refuses anything that is not the shape it claims.</p>
 *
 * <p>out/test at run time, so three levels reach the repository root.</p>
 */
function sharedCorpus(): readonly CredentialCase[] {
  const parsed: unknown = JSON.parse(readFileSync(
    join(__dirname, '..', '..', '..', 'shared', 'credential-words.json'), 'utf8',
  ));
  const rows: unknown = (parsed as Record<string, unknown>)['cases'];

  assert.ok(Array.isArray(rows), 'shared/credential-words.json carries no `cases` array');

  return rows.map((row: unknown, at: number) => {
    const one = row as Record<string, unknown>;

    assert.equal(typeof one['name'], 'string', `case ${at} has no name`);
    assert.equal(typeof one['credential'], 'boolean', `case ${at} has no credential verdict`);

    return { name: one['name'] as string, credential: one['credential'] as boolean };
  });
}

test('a name that merely CONTAINS a short word is not a credential', () => {
  // The defect gemini found on the code round: matching 'key', 'auth' and 'sig' as substrings made
  // every one of these a credential, so `?author=octocat` was refused and redacted.
  for (const ordinary of ['author', 'authors', 'design', 'assignee', 'signal', 'monkey', 'keyboard-layout']) {
    assert.equal(namesACredential(ordinary), false, ordinary);
  }
});

test('the same short words ARE credentials when they are a whole part of the name', () => {
  for (const named of ['key', 'api_key', 'apiKey', 'X-Api-Key', 'API-KEY', 'auth', 'x-auth', 'sig', 'Sig']) {
    assert.ok(namesACredential(named), named);
  }
});

test('a run-together spelling is still caught, which is why the long list has compounds in it', () => {
  // `apikey` has no boundary for 'key' to sit on, so the whole-part rule cannot see it. That is the
  // stated cost of the rule, and it is paid by naming the compound rather than by loosening the
  // rule until `monkey` comes back.
  for (const named of ['apikey', 'myapikey', 'accesskey', 'privatekey']) {
    assert.ok(namesACredential(named), named);
  }
});

test('the unambiguous words match anywhere, because there is no ordinary word around them', () => {
  for (const named of ['token', 'refresh_token', 'client_secret', 'password', 'passwd', 'Authorization', 'signature', 'credential']) {
    assert.ok(namesACredential(named), named);
  }
});

test('an ordinary endpoint parameter stays ordinary', () => {
  // The sentence both callers' docstrings make the same promise about: `?api-version=2024-02-01` is
  // how Azure spells a version, and treating it as a secret sends people back to pasting keys
  // somewhere worse.
  for (const ordinary of ['api-version', 'deployment', 'region', 'model', 'count', 'page']) {
    assert.equal(namesACredential(ordinary), false, ordinary);
  }
});

test('the shared corpus is answered the same way here as on the server', () => {
  // THE FINDING OF STORY 1.1's PLAN ROUND, and the one that mattered most. Until this, each half
  // was checked against its OWN hand-written table — so a TypeScript splitter that disagreed with
  // the C# one about `requestSig`, `auth-key` or `token2` would leave both suites green while the
  // extension and the server redacted different notices. That is a secret on disk on one path and
  // not the other, and nothing anywhere would have said so.
  //
  // `shared/credential-words.json` carries the corpus and `CredentialWordsTests.cs` asserts the
  // same rows. Neither side owns it, and a case added to it has to be answered twice.
  //
  // out/test at run time, so three levels reach the repository root.
  const cases = sharedCorpus();

  // A COMPANION to the scan, because a scan that matches nothing passes for ever. Not merely "the
  // list is non-empty" — a named case that must be in it, so a corpus reduced to the easy rows
  // cannot quietly stop covering the boundary the C# and TypeScript splitters disagreed on first.
  assert.ok(cases.length >= 40, `the shared corpus has shrunk to ${cases.length} cases`);
  assert.ok(
    cases.some((one) => one.name === 'xAuth' && one.credential),
    'the shared corpus no longer carries xAuth, which is the case that only the whole-part rule on '
    + 'the ORIGINAL casing can answer — the one both halves got wrong first',
  );

  for (const one of cases) {
    assert.equal(
      namesACredential(one.name),
      one.credential,
      `shared/credential-words.json says "${one.name}" is ${one.credential ? '' : 'not '}`
      + 'a credential, and the server is held to the same row',
    );
  }
});
