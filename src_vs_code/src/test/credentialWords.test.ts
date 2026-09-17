import assert from 'node:assert/strict';
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
