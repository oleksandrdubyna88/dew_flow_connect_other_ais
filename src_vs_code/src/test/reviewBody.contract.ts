/**
 * The zero-spend probe of the review body a NEW client sends (story 4.1 of todo/PLAN_every_round_is_counted.md, decided at
 * epic 4's plan round): `round`, `attempt`, `remainingSeconds` and one deliberately unknown member, POSTed to a REAL Team
 * server with `"kind":"probe-no-such-kind"`.
 *
 * <p>Why this shape. The client ships before the server reads the new fields, and contract 2 cannot tell the client whether
 * the server does. The deployed server's `ServerJsonContext` sets no `UnmappedMemberHandling`
 * (src_server/src/ServerJsonContext.cs:252), so binding SKIPS members it does not know — and the server binds the JSON
 * before it judges the kind (`ReviewEndpoints.MapReviewEndpoints` → `Refusal` → `JobKind.Refusal`). So a 400 naming
 * <i>"'probe-no-such-kind' is not a kind of job"</i> proves the new members were skipped by binding and no job was ever
 * created: nothing claimed, nothing launched, nothing spent. "POST then DELETE" was refused for exactly that reason — the
 * runner can claim and launch between the two.</p>
 *
 * <p>The control in the same run: the same body with a malformed KNOWN member (`timeoutSeconds: "x"`) is answered by the
 * binding error instead — never the kind refusal — which is what proves the first answer came from the kind check and not
 * from a server that refuses everything the same way. It never posts a valid job.</p>
 *
 * <p>Like its siblings, it reads COAI_CONTRACT_URL from the runner (scripts/run-contract.mjs) and throws rather than
 * skipping when it is missing: a contract test that quietly does nothing is a green tick over an assertion nobody made.</p>
 */
import * as assert from 'node:assert';
import { createHmac } from 'node:crypto';
import { test } from 'node:test';

import { ask, createSession } from '../teamServerApi';

function required(name: string): string {
  const value = process.env[name] ?? '';
  if (value.length === 0) {
    throw new Error(`${name} is not set. Run this through "npm run test:contract", which starts a server and fills it in, `
      + 'or set it yourself to point at one.');
  }

  return value;
}

const SERVER = required('COAI_CONTRACT_URL');
const KEY = required('COAI_CONTRACT_KEY');
const DOMAIN = required('COAI_CONTRACT_DOMAIN');

/** An identity token the server's local scheme accepts — the same minting as teamServerSession.contract.ts. */
function localToken(email: string): string {
  const part = (o: object) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const head = part({ alg: 'HS256', typ: 'JWT' });
  const body = part({ iss: 'coai-local', email, exp: Math.floor(Date.now() / 1000) + 600 });

  return `${head}.${body}.${createHmac('sha256', KEY).update(`${head}.${body}`).digest('base64url')}`;
}

async function sessionFor(email: string): Promise<string> {
  const result = await createSession(SERVER, localToken(email));
  if (!result.ok) {
    assert.fail(`${email} was refused a session: ${result.status} — ${result.message}`);
  }

  return result.value.token;
}

/** The kind no server has, now or later: the probe is refused BY NAME, before anything is queued. */
const PROBE_KIND = 'probe-no-such-kind';

/**
 * What `RemoteAsk.RequestBody` sends for a launch of a round (src_mcp/runners/Reviewers/RemoteAsk.cs), member for member,
 * plus one member no server knows — so the probe proves skipping of UNKNOWN members, not only of the three this client
 * will send.
 */
function newBody(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    vendor: 'codex',
    model: 'gpt-5.6-luna',
    prompt: 'p',
    role: 'Architecture',
    timeoutSeconds: 60,
    kind: PROBE_KIND,
    round: '0123456789abcdef0123456789abcdef',
    attempt: 'first',
    remainingSeconds: 50,
    memberNoServerKnows: 'skipped by binding',
    ...overrides,
  };
}

test('TheNewBody_IsAcceptedByTheLiveServer — the probe is refused by its kind, so the new members were bound past, and no job was made', async () => {
  const token = await sessionFor(`contract@${DOMAIN}`);

  const result = await ask<unknown>(SERVER, 'api/reviews', { method: 'POST', token, body: newBody() });

  assert.strictEqual(result.ok, false, 'a kind no server has must never be accepted');
  if (result.ok) {
    return;
  }
  assert.strictEqual(result.status, 400);
  assert.match(result.message, /not a kind of job/,
    `the refusal names the kind — which the server reaches only after binding the body, new members and all: ${result.message}`);
  assert.match(result.message, new RegExp(PROBE_KIND));
});

test('the control: a malformed KNOWN member is the binding error, never the kind refusal', async () => {
  const token = await sessionFor(`contract@${DOMAIN}`);

  const result = await ask<unknown>(SERVER, 'api/reviews', { method: 'POST', token, body: newBody({ timeoutSeconds: 'x' }) });

  assert.strictEqual(result.ok, false);
  if (result.ok) {
    return;
  }
  assert.strictEqual(result.status, 400, `a body the server cannot bind is a 400 of its own: ${result.message}`);
  assert.doesNotMatch(result.message, /not a kind of job/,
    'the kind is judged AFTER binding, so a body that does not bind never reaches it — which is what makes the probe above a measurement');
});
