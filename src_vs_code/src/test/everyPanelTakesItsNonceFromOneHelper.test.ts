import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { webviewNonce } from '../webviewNonce';

/**
 * Every webview's script nonce comes from ONE helper, and it is unguessable (todo/PLAN_one_model_catalog.md, E3.1).
 *
 * <p>The nonce is the whole of a page's content security policy: a predictable one is a policy an injected script can
 * satisfy. The Settings panel built its own from `Math.random()` — seeded per process, its sequence recoverable from a
 * few outputs — while nine other panels each built theirs from `crypto` in four different encodings. A second copy of
 * the decision is how the first one stayed wrong: `bugzReviewPanel.ts` copied the panel's, a scanner refused the copy,
 * and the original was left as it was. So the rule is structural: no source file but the helper makes one.</p>
 */

const SRC = join(__dirname, '..', '..', 'src');

/**
 * What building a webview nonce looks like in this code base: a `nonce` function or binding, or nonce-sized random bytes
 * (16, 24 or 32) turned into text. A short id — the notifications run id, a command's 4-byte token — is not one.
 */
const MAKES_A_NONCE = /function nonce\s*\(|const nonce\s*=\s*\(|randomBytes\((?:16|24|32)\)\.toString\(|Math\.random\(\)\s*\*\s*alphabet/;

/** Not a webview nonce: a role deletion's claim token (`roleDeletion.ts`), which only has to be unique between windows. */
const OTHER_USES: readonly string[] = ['roleDeletion.ts', 'webviewNonce.ts'];

test('no panel builds its own nonce — every one comes from webviewNonce', () => {
  const builders = readdirSync(SRC)
    .filter((name) => name.endsWith('.ts') && !OTHER_USES.includes(name))
    .filter((name) => MAKES_A_NONCE.test(readFileSync(join(SRC, name), 'utf8')));

  assert.deepEqual(builders, [], `these build a nonce of their own: ${builders.join(', ')}`);
});

test('a nonce is 128 bits, fresh each time, and safe inside a policy string', () => {
  const nonces = new Set(Array.from({ length: 64 }, () => webviewNonce()));

  assert.equal(nonces.size, 64, 'every call is a new value');
  for (const nonce of nonces) {
    // base64url: what CSP's nonce-source admits, and no quote, space or semicolon that could end the policy.
    assert.match(nonce, /^[A-Za-z0-9_-]{22}$/);
  }
});
