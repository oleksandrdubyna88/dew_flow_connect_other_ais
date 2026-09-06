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
  answer: (url: string, init: RequestInit) => { status: number; body: string } | Promise<never>,
): typeof fetch & { seen: { url: string; init: RequestInit }[] } {
  const seen: { url: string; init: RequestInit }[] = [];
  const impl = (async (input: unknown, init: RequestInit = {}) => {
    const url = String(input);
    seen.push({ url, init });
    const result = await answer(url, init);

    return {
      ok: result.status >= 200 && result.status < 300,
      status: result.status,
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
  assert.strictEqual(String(fetchImpl.seen[0]?.init.body).includes('idp-token'), true);
  assert.strictEqual(fetchImpl.seen[1]?.init.method, 'DELETE');
});

test('usage names its window and its scope on the wire', async () => {
  const fetchImpl = stub(() => ({ status: 200, body: JSON.stringify({ window: 'today', vendors: [] }) }));

  await fetchUsage('https://s', 'tok', 'today', 'company', fetchImpl);

  assert.ok(fetchImpl.seen[0]?.url.includes('window=today'));
  assert.ok(fetchImpl.seen[0]?.url.includes('scope=company'));
});
