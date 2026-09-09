import * as assert from 'node:assert';
import { test } from 'node:test';
import {
  CONTRACT_HEADER,
  ask,
  createSession,
  deleteSession,
  fetchCatalog,
  fetchClientConfig,
  fetchUsage,
  isHttpsOrLoopback,
} from '../teamServerApi';

/** A `fetch` that answers what the test says, and records what it was asked. */
function stub(
  answer: (url: string, init: RequestInit) => { status: number; body: string; headers?: Record<string, string> } | Promise<never>,
): typeof fetch & { seen: { url: string; init: RequestInit }[] } {
  const seen: { url: string; init: RequestInit }[] = [];
  const impl = (async (input: unknown, init: RequestInit = {}) => {
    const url = String(input);
    seen.push({ url, init });
    const result = await answer(url, init);

    return {
      ok: result.status >= 200 && result.status < 300,
      status: result.status,
      // A real `Response` always carries these; the stub did not, which is why nothing could test
      // what the server says back about itself.
      headers: new Headers(result.headers ?? {}),
      text: async () => result.body,
    } as Response;
  }) as typeof fetch & { seen: typeof seen };
  impl.seen = seen;

  return impl;
}

const GOOD_SCOPE = 'api://3afb5834-1111-2222-3333-444455556666/coai.access';

test('a request carries the contract version, and the token when there is one', async () => {
  const fetchImpl = stub(() => ({ status: 200, body: '{"ok":true}' }));

  await ask('https://s/', 'api/catalog', { token: 'the-token', fetchImpl });

  const headers = fetchImpl.seen[0]?.init.headers as Record<string, string>;
  assert.strictEqual(headers[CONTRACT_HEADER], '1');
  assert.strictEqual(headers['Authorization'], 'Bearer the-token');
  // The one canonical spelling, so a saved trailing slash cannot produce `…//api/catalog` — a 404
  // from a server the panel would then call unhealthy, with a token that matched perfectly.
  assert.strictEqual(fetchImpl.seen[0]?.url, 'https://s/api/catalog');
});

test('no token means no Authorization header at all', async () => {
  const fetchImpl = stub(() => ({ status: 200, body: '{}' }));

  await ask('https://s', 'api/client-config', { fetchImpl });

  const headers = fetchImpl.seen[0]?.init.headers as Record<string, string>;
  assert.ok(!('Authorization' in headers));
});

test('a refusal is an answer, never an exception', async () => {
  const result = await ask('https://s', 'api/catalog', {
    fetchImpl: stub(() => ({ status: 401, body: '{"error":"token expired"}' })),
  });

  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.ok === false && result.status, 401);
  // The SERVER's own sentence, which is the one that says what to do.
  assert.strictEqual(result.ok === false && result.message, 'token expired');
});

test('a body that is not JSON is still reported as something readable', async () => {
  // Something in front of the server can answer with an HTML error page.
  const result = await ask('https://s', 'api/catalog', {
    fetchImpl: stub(() => ({ status: 502, body: '<html>bad gateway</html>' })),
  });

  assert.strictEqual(result.ok === false && result.message.includes('bad gateway'), true);
});

test('a very long server sentence is cut rather than printed whole', async () => {
  const result = await ask('https://s', 'api/catalog', {
    fetchImpl: stub(() => ({ status: 500, body: 'x'.repeat(5000) })),
  });

  assert.ok(result.ok === false && result.message.length < 260);
});

test('a network failure is an answer too, and says the server could not be reached', async () => {
  const result = await ask('https://s', 'api/catalog', {
    fetchImpl: stub(() => Promise.reject(new Error('connection refused'))),
  });

  assert.strictEqual(result.ok, false);
  assert.strictEqual(result.ok === false && result.status, 0);
  assert.ok(result.ok === false && result.message.includes('connection refused'));
});

test('an aborted request says it did not answer in time, not that it refused', async () => {
  const abort = new Error('aborted');
  abort.name = 'AbortError';
  const result = await ask('https://s', 'api/catalog', {
    fetchImpl: stub(() => Promise.reject(abort)),
  });

  assert.ok(result.ok === false && result.message.includes('did not answer'));
});

test('a token may only travel over https, or http on this machine', () => {
  // A bearer token in the clear over a network is a token anybody on that network can take.
  assert.strictEqual(isHttpsOrLoopback('https://coai.example.com'), true);
  assert.strictEqual(isHttpsOrLoopback('http://localhost:5310'), true);
  assert.strictEqual(isHttpsOrLoopback('http://127.0.0.1:5310'), true);
  assert.strictEqual(isHttpsOrLoopback('http://coai.example.com'), false);
  assert.strictEqual(isHttpsOrLoopback('not a url'), false);
});

test('a plain-http server is refused before it is even asked', async () => {
  const fetchImpl = stub(() => ({ status: 200, body: '{}' }));

  const result = await fetchClientConfig('http://coai.example.com', fetchImpl);

  assert.strictEqual(result.ok, false);
  assert.strictEqual(fetchImpl.seen.length, 0, 'nothing should have been sent');
});

test('a server advertising an acceptable scope is accepted', async () => {
  const result = await fetchClientConfig(
    'https://s',
    stub(() => ({ status: 200, body: JSON.stringify({ microsoftScope: GOOD_SCOPE, providers: ['microsoft'] }) })),
  );

  assert.strictEqual(result.ok, true);
  assert.strictEqual(result.ok === true && result.value.microsoftScope, GOOD_SCOPE);
});

test('a server asking for a Graph scope is refused, and told why', async () => {
  // The plan round's most serious finding: a person adds a server by typing a URL, and without this
  // a hostile one could name any resource and have the extension mint a token for it.
  const result = await fetchClientConfig(
    'https://s',
    stub(() => ({
      status: 200,
      body: JSON.stringify({ microsoftScope: 'https://graph.microsoft.com/Mail.Read', providers: ['microsoft'] }),
    })),
  );

  assert.strictEqual(result.ok, false);
  assert.ok(result.ok === false && result.message.includes('coai.access'));
});

test('the catalog is fetched with the bearer, on the route the server serves', async () => {
  // The call two whole stories depend on, which the plan forgot to specify at all.
  const fetchImpl = stub(() => ({
    status: 200,
    body: JSON.stringify({ serverVersion: '0.5.1', isAdmin: false, error: '', vendors: [] }),
  }));

  const result = await fetchCatalog('https://s/', 'tok', fetchImpl);

  assert.strictEqual(result.ok, true);
  assert.strictEqual(fetchImpl.seen[0]?.url, 'https://s/api/catalog');
  assert.strictEqual((fetchImpl.seen[0]?.init.headers as Record<string, string>)['Authorization'], 'Bearer tok');
});

test('a session is created by POST and given up by DELETE', async () => {
  const fetchImpl = stub(() => ({
    status: 200,
    body: JSON.stringify({ token: 't', expiresUtc: '2026-10-01T00:00:00Z', email: 'a@b.c' }),
  }));

  await createSession('https://s', 'idp-token', fetchImpl);
  await deleteSession('https://s', 't', fetchImpl);

  assert.strictEqual(fetchImpl.seen[0]?.init.method, 'POST');
  assert.strictEqual(fetchImpl.seen[1]?.init.method, 'DELETE');
});

/**
 * The seam that shipped broken in 0.31.1, and the reason this assertion is here rather than one
 * about the body.
 *
 * <p>The extension posted the identity token as `{ token }` in the BODY. The server authorises
 * `/api/session` from the Authorization header ALONE — `Auth.Bearer` reads
 * `Request.Headers.Authorization` and nothing on that server ever reads the body — so every
 * sign-in was answered `401` with an empty body in half a millisecond, without the JWT handler
 * running at all. Observed on `coai.remsoft.dev`: a 2114-byte JSON body, no
 * `JwtBearerHandler` line, `401 0 null 0.6838ms`.</p>
 *
 * <p>Both halves had tests. The server's posts a header and a null body; this file's asserted the
 * token was in the body. Two green suites, contradictory expectations, and nothing crossing
 * between them.</p>
 */
test('the identity token is minted through the Authorization header, which is the only place the server reads it', async () => {
  const fetchImpl = stub(() => ({
    status: 200,
    body: JSON.stringify({ token: 't', expiresUtc: '2026-10-01T00:00:00Z', email: 'a@b.c' }),
  }));

  await createSession('https://s', 'idp-token', fetchImpl);

  // `new Headers(...)` rather than `init.headers as Record<string, string>`: the doctrine's DoD
  // forbids an `as` cast standing in for a real type, and `HeadersInit` is a union of three shapes
  // — a cast to one of them is a promise about `ask`'s internals that would come due silently the
  // day it passes a `Headers` instead. This reads whichever shape it is. (The neighbouring catalog
  // test still casts; left as found rather than rewritten in a hotfix.)
  const sent = fetchImpl.seen[0];
  assert.strictEqual(new Headers(sent?.init.headers).get('Authorization'), 'Bearer idp-token');
  assert.strictEqual(
    sent?.init.body,
    undefined,
    'the server reads no body on this route, and a bearer token in one is a bearer token in a log',
  );
});

test('usage names its window and its scope on the wire', async () => {
  const fetchImpl = stub(() => ({ status: 200, body: JSON.stringify({ window: 'today', vendors: [] }) }));

  await fetchUsage('https://s', 'tok', 'today', 'company', fetchImpl);

  assert.ok(fetchImpl.seen[0]?.url.includes('window=today'));
  assert.ok(fetchImpl.seen[0]?.url.includes('scope=company'));
});

// ---------- what the SERVER says about itself ----------

/**
 * The contract rides both ways, and until now the panel read only its own half.
 *
 * <p>`ContractVersion` in the server puts `X-Coai-Contract` on every response for exactly this, and
 * says so: "a newer client knows what it is doing better than an older server does, and its own
 * check against the response header is the right place to decide". The panel sent its number and
 * threw away the answer.</p>
 */
test('a server that says what it speaks is heard', async () => {
  const fetchImpl = stub(() => ({ status: 200, body: '{}', headers: { [CONTRACT_HEADER]: '2' } }));

  const answer = await ask('https://s', 'api/catalog', { fetchImpl });

  assert.strictEqual(answer.contract, 2);
});

test('a server that says nothing is legacy, not unknown', async () => {
  // It ANSWERED — it simply predates the header. That is a fact about the server, and a different
  // one from never having been reached.
  const fetchImpl = stub(() => ({ status: 200, body: '{}' }));

  const answer = await ask('https://s', 'api/catalog', { fetchImpl });

  assert.strictEqual(answer.contract, 0);
});

test('the number rides the failure arm too, because that is where it matters most', async () => {
  // A 426 is the server refusing this client as too old. The one thing a caller wants at that
  // moment is what the other side speaks, and a result that carried it only on success would drop
  // it exactly then.
  const fetchImpl = stub(() => ({ status: 426, body: '{"error":"too old"}', headers: { [CONTRACT_HEADER]: '3' } }));

  const answer = await ask('https://s', 'api/catalog', { fetchImpl });

  assert.strictEqual(answer.ok, false);
  assert.strictEqual(answer.contract, 3);
});

test('no response at all leaves the contract unknown rather than legacy', async () => {
  // The distinction the whole feature turns on. A dropped connection must not be recorded as "this
  // server is old" — the panel would flash a skew warning on every network blip.
  const fetchImpl = stub(() => Promise.reject(new Error('socket hang up')) as Promise<never>);

  const answer = await ask('https://s', 'api/catalog', { fetchImpl });

  assert.strictEqual(answer.ok, false);
  assert.strictEqual(answer.contract, undefined);
});

test('a header that is not a number is unknown, never legacy', async () => {
  // `v2` is something else answering on that URL, or a proxy inventing a value. Reading it as 0
  // would report a modern server as ancient.
  const fetchImpl = stub(() => ({ status: 200, body: '{}', headers: { [CONTRACT_HEADER]: 'v2' } }));

  const answer = await ask('https://s', 'api/catalog', { fetchImpl });

  assert.strictEqual(answer.contract, undefined);
});
