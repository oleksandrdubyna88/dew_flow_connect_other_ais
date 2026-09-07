/**
 * The one test that can see BOTH halves of the client/server contract.
 *
 * <p>Every other test here stubs `fetch`, which proves what the client SENDS and can never prove
 * that the server ACCEPTS it. On 2026-09-07 that gap shipped: the extension posted the identity
 * token as an object in the body of POST /api/session while the server authorises that route from
 * the Authorization header alone. Both suites were green — the server's posting a header and a
 * null body, the extension's asserting the token was in the body — and sign-in was impossible for
 * everybody. See research/PLAN_contract_across_the_seam.md.</p>
 *
 * <p>So this file calls the REAL createSession and ask against a REAL server. It does not start
 * one: the address arrives in COAI_CONTRACT_URL, so the same test covers a dotnet process started
 * by scripts/run-contract.mjs in CI and a docker compose stack on a developer's machine.</p>
 *
 * <p>It lives beside the unit tests and is still not one of them: the runner they share lists
 * *.test.js, and this compiles to *.contract.js. So "npm test" stays hermetic and needs nothing
 * running, while "npm run test:contract" picks this up.</p>
 *
 * <p><b>Why no Microsoft.</b> The server carries a symmetric-key Local scheme built for exactly
 * this (src_server/src/Auth.cs): issuer coai-local, no audience check, HMAC-SHA256 over a key of
 * at least 32 bytes. Minting one here is fifteen lines and no dependency, which is what makes this
 * test cheap enough to run on every pull request.</p>
 */
import * as assert from 'node:assert';
import { createHmac } from 'node:crypto';
import { test } from 'node:test';
import { Session, ask, createSession } from '../teamServerApi';

/** The issuer Auth.AddSchemes pins for the local scheme. Not a guess — its ValidIssuer. */
const LOCAL_ISSUER = 'coai-local';

/**
 * What the runner passed us.
 *
 * <p>Throws rather than skipping. A contract test that quietly does nothing when its server is
 * missing is the exact failure this file exists to prevent, one level up: a green tick over an
 * assertion nobody made.</p>
 */
function required(name: string): string {
  const value = process.env[name] ?? '';
  if (value.length === 0) {
    throw new Error(
      name + ' is not set. Run this through "npm run test:contract", which starts a server and '
      + 'fills it in, or set it yourself to point at one (a docker compose stack, say).',
    );
  }

  return value;
}

const SERVER = required('COAI_CONTRACT_URL');
const KEY = required('COAI_CONTRACT_KEY');
const DOMAIN = required('COAI_CONTRACT_DOMAIN');

/** An identity-provider token the server's local scheme accepts, minted the way its own tests do. */
function localToken(email: string): string {
  const part = (o: object) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({
    iss: LOCAL_ISSUER,
    email,
    exp: Math.floor(Date.now() / 1000) + 600,
  });
  const signature = createHmac('sha256', KEY).update(head + '.' + body).digest('base64url');

  return head + '.' + body + '.' + signature;
}

/**
 * The session, or a failure that says what the server answered.
 *
 * <p>`assert.fail` returns `never`, so this narrows the union without a cast — the doctrine
 * forbids one standing in for a real shape, and here the compiler can do the work instead.</p>
 */
async function signIn(email: string): Promise<Session> {
  const result = await createSession(SERVER, localToken(email));
  if (!result.ok) {
    assert.fail(`${email} was refused: status ${result.status} — ${result.message}`);
  }

  // 201, not merely 2xx: `SessionTests.ASession_IsMintedFromATokenAndThenCarriesTheCaller` asserts
  // that exact code on the server side, so this is where the two halves agree about it.
  assert.strictEqual(result.status, 201, 'minting a session is a Created, and the server says so');

  return result.value;
}

test('the real client mints a session against the real server', async () => {
  const email = `contract@${DOMAIN}`;

  // The whole point of the file: this fails whenever the two halves disagree about where the
  // credential travels, whatever each half's own suite believes.
  const session = await signIn(email);

  assert.strictEqual(session.email, email);
  assert.ok(session.token.length > 0, 'a session with no token is not a session');
});

test('the session the server issued is accepted on a later call', async () => {
  const email = `contract@${DOMAIN}`;
  const session = await signIn(email);

  const who = await ask<{ email: string }>(SERVER, 'api/whoami', { token: session.token });

  if (!who.ok) {
    assert.fail(`whoami refused the session: status ${who.status} — ${who.message}`);
  }
  assert.strictEqual(who.value.email, email);
});

/**
 * The teeth of the other two.
 *
 * <p>A refusal proves the server is really authorising rather than waving everything through — so
 * that "the session was minted" above means what it says. It also pins the codes: an account
 * outside the company answers 403, and only a caller the server could not identify at all answers
 * 401. That distinction is what made the 2026-09-07 defect readable once anyone looked.</p>
 */
test('an account outside the company domain is refused 403, not 401', async () => {
  const result = await createSession(SERVER, localToken('outsider@elsewhere.invalid'));

  if (result.ok) {
    assert.fail('an account outside the company domain was signed in');
  }
  assert.strictEqual(result.status, 403);
});
