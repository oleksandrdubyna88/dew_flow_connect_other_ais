import * as assert from 'node:assert';
import { mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  AuthHost,
  RENEW_WITHIN_MS,
  SignedIn,
  StateStore,
  deleteToken,
  TokenFact,
  needsRenewal,
  reconcile,
  signIn,
  signOut,
  signedInKey,
  tokenFactKey,
  trustedKey,
  writeToken,
} from '../teamServerAuth';
import { TeamServer, tokenFileName } from '../teamServers';

const SERVER: TeamServer = { id: 'remsoft-dev', name: 'RemSoft Dev', url: 'https://coai.example.com/' };

function store(seed: Record<string, unknown> = {}): StateStore & { readonly all: Record<string, unknown> } {
  const all: Record<string, unknown> = { ...seed };

  return {
    all,
    get<T>(key: string): T | undefined {
      return all[key] as T | undefined;
    },
    async update(key: string, value: unknown): Promise<void> {
      if (value === undefined) {
        delete all[key];
      } else {
        all[key] = value;
      }
    },
  };
}

const GOOD_SCOPE = 'api://3afb5834-1111-2222-3333-444455556666/coai.access';

/** A server that says the right things: an acceptable scope, then a session. */
function serverThatWorks(): typeof fetch & { seen: string[] } {
  const seen: string[] = [];
  const impl = (async (input: unknown, init: RequestInit = {}) => {
    const url = String(input);
    seen.push(`${init.method ?? 'GET'} ${url}`);
    const body = url.endsWith('/api/client-config')
      ? JSON.stringify({ microsoftScope: GOOD_SCOPE, providers: ['microsoft'] })
      : JSON.stringify({ token: 'server-token', expiresUtc: '2027-01-01T00:00:00Z', email: 'a@b.c' });

    return { ok: true, status: 200, text: async () => body } as Response;
  }) as typeof fetch & { seen: string[] };
  impl.seen = seen;

  return impl;
}

function host(
  dataDir: string,
  state: StateStore,
  said: string[] = [],
  fetchImpl: typeof fetch = serverThatWorks(),
): AuthHost {
  return {
    dataDir,
    state,
    fetchImpl,
    getSession: async () => 'idp-token',
    confirmApplication: async () => true,
    say: (m) => said.push(m),
  };
}

test('a token lands exactly where the MCP shim will look for it', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    await writeToken(dir, SERVER.url, 'the-token');

    // The path both sides derive independently. `shared/team-server-url-vectors.json` is what keeps
    // them agreeing; this asserts the writer actually uses it.
    const path = join(dir, 'servers', tokenFileName(SERVER.url));
    assert.strictEqual(readFileSync(path, 'utf8'), 'the-token');
    assert.strictEqual(tokenFileName(SERVER.url), '0ac377feb52b33a9.token');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the token file is readable by its owner and nobody else', async (t) => {
  if (process.platform === 'win32') {
    // The Unix mode API does not apply; the profile directory is already ACL-protected.
    t.skip('POSIX modes only');

    return;
  }

  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    await writeToken(dir, SERVER.url, 'first');
    // Written TWICE on purpose: `mode` on writeFile only applies when the file is created, so a
    // token replacing an older one would otherwise keep whatever mode that one had.
    await writeToken(dir, SERVER.url, 'second');

    const mode = statSync(join(dir, 'servers', tokenFileName(SERVER.url))).mode & 0o777;
    assert.strictEqual(mode, 0o600, `a bearer token any local user can read is a login they can take`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('deleting a token that is not there is not an error', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    await deleteToken(dir, SERVER.url);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a session with plenty of time left is not renewed', () => {
  const now = Date.parse('2026-09-06T12:00:00Z');
  assert.strictEqual(needsRenewal('2026-09-30T12:00:00Z', now), false);
});

test('a session inside the renewal window is due', () => {
  const now = Date.parse('2026-09-06T12:00:00Z');
  assert.strictEqual(needsRenewal(new Date(now + RENEW_WITHIN_MS - 1000).toISOString(), now), true);
  assert.strictEqual(needsRenewal(new Date(now + RENEW_WITHIN_MS + 1000).toISOString(), now), false);
});

test('an already-expired session is due', () => {
  assert.strictEqual(needsRenewal('2020-01-01T00:00:00Z', Date.parse('2026-09-06T12:00:00Z')), true);
});

test('an unreadable expiry is treated as due rather than as never', () => {
  // Renewing early costs one silent request; being wrong the other way costs somebody their session
  // in the middle of something.
  assert.strictEqual(needsRenewal('not a date', Date.now()), true);
  assert.strictEqual(needsRenewal('', Date.now()), true);
});

test('reconciling does nothing at all when nobody is signed in', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const done = await reconcile(SERVER, host(dir, store()), Date.now());

    assert.deepStrictEqual(done, { problem: '', changed: false });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('reconciling does nothing while THIS SIDE holds a comfortable session', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const signedIn: SignedIn = { email: 'a@b.c', expiresUtc: '2027-01-01T00:00:00Z' };
    const state = store({
      [signedInKey(SERVER.id)]: signedIn,
      [tokenFactKey(SERVER.id, '')]: { ...signedIn, mintedAtMs: 1 } satisfies TokenFact,
    });
    await writeToken(dir, SERVER.url, 'the-token');

    const done = await reconcile(SERVER, host(dir, state), Date.parse('2026-09-06T12:00:00Z'));

    assert.deepStrictEqual(done, { problem: '', changed: false });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a renewal that is due is attempted WITHOUT asking the person anything', async () => {
  // The finding that made this test worth writing: the plan reused the interactive options for
  // renewal, and `clearSessionPreference: true` forces an account prompt while `createIfNone: false`
  // forbids showing one — so the pair can only return nothing, the extension would conclude the
  // identity session was gone, and it would sign the person out. Weekly. Which is the exact thing
  // silent renewal exists to prevent.
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store({
      [signedInKey(SERVER.id)]: { email: 'a@b.c', expiresUtc: '2026-09-06T13:00:00Z' } satisfies SignedIn,
      // Approved earlier, by a person. A renewal for an application nobody approved is refused
      // before it reaches the identity provider — see the test below.
      [trustedKey(SERVER.id)]: '3afb5834-1111-2222-3333-444455556666',
      [tokenFactKey(SERVER.id, '')]: {
        email: 'a@b.c', expiresUtc: '2026-09-06T13:00:00Z', mintedAtMs: 1,
      } satisfies TokenFact,
    });
    await writeToken(dir, SERVER.url, 'the-old-token');
    const asked: boolean[] = [];
    const spy: AuthHost = {
      ...host(dir, state),
      getSession: async (_scope, interactive) => {
        asked.push(interactive);

        return undefined;
      },
    };

    await reconcile(SERVER, spy, Date.parse('2026-09-06T12:00:00Z'));

    assert.deepStrictEqual(asked, [false], 'a renewal must never be able to raise a prompt');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a server this build cannot sign into never reaches the identity provider', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store({
      [signedInKey(SERVER.id)]: { email: 'a@b.c', expiresUtc: '2026-09-06T13:00:00Z' } satisfies SignedIn,
    });
    let minted = 0;
    const refuses = (async () => {
      throw new Error('connection refused');
    }) as unknown as typeof fetch;
    const spy: AuthHost = {
      ...host(dir, state, [], refuses),
      getSession: async () => {
        minted += 1;

        return 'idp-token';
      },
    };

    // The server cannot be reached, so it never says what it wants — and a token must not be minted
    // for a server that has not said.
    const done = await reconcile(SERVER, spy, Date.parse('2026-09-06T12:00:00Z'));

    assert.ok(done.problem.length > 0, 'a refusal is a sentence the row can show');
    assert.strictEqual(minted, 0, 'nothing is minted before the server is trusted');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a good sign-in writes the token and remembers who it is', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store();
    const result = await signIn(SERVER, host(dir, state));

    assert.strictEqual(result.ok, true);
    assert.strictEqual(result.ok === true && result.signedIn.email, 'a@b.c');
    assert.strictEqual(
      readFileSync(join(dir, 'servers', tokenFileName(SERVER.url)), 'utf8'),
      'server-token',
    );
    assert.ok(state.all[signedInKey(SERVER.id)] !== undefined);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the person is asked to confirm the application ONCE, then never again', async () => {
  // The application id is the server's to choose and the scope check cannot say whether it belongs
  // to the company — only a person can. Asking on every sign-in would train them to click through.
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store();
    const shown: string[] = [];
    const asking: AuthHost = {
      ...host(dir, state),
      confirmApplication: async (_s, application) => {
        shown.push(application);

        return true;
      },
    };

    await signIn(SERVER, asking);
    await signIn(SERVER, asking);

    assert.deepStrictEqual(shown, ['3afb5834-1111-2222-3333-444455556666']);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('refusing the application mints nothing at all', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    let minted = 0;
    const refusing: AuthHost = {
      ...host(dir, store()),
      confirmApplication: async () => false,
      getSession: async () => {
        minted += 1;

        return 'idp-token';
      },
    };

    const result = await signIn(SERVER, refusing);

    assert.strictEqual(result.ok, false);
    assert.strictEqual(minted, 0, 'a refused application must never reach the identity provider');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a session that cannot be SAVED is ended again rather than left open', async () => {
  // Otherwise a live session exists on the server that this machine can neither use nor revoke — the
  // half-completed state the plan round asked for a compensation for.
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const calls: string[] = [];
    const watching = (async (input: unknown, init: RequestInit = {}) => {
      calls.push(`${init.method ?? 'GET'} ${String(input)}`);
      const body = String(input).endsWith('/api/client-config')
        ? JSON.stringify({ microsoftScope: GOOD_SCOPE, providers: ['microsoft'] })
        : JSON.stringify({ token: 'server-token', expiresUtc: '2027-01-01T00:00:00Z', email: 'a@b.c' });

      return { ok: true, status: 200, text: async () => body } as Response;
    }) as typeof fetch;

    // A data directory that cannot hold a `servers` folder, because a file of that name is there.
    const blocked = join(dir, 'blocked');
    await writeToken(dir, SERVER.url, 'x');
    const state = store();
    const result = await signIn(SERVER, {
      ...host(blocked, state, [], watching),
      dataDir: join(dir, 'servers', tokenFileName(SERVER.url)),
    });

    assert.strictEqual(result.ok, false);
    assert.ok(result.ok === false && result.message.includes('ended again'));
    assert.ok(calls.some((c) => c.startsWith('DELETE ')), 'the session must be revoked');
    assert.strictEqual(state.all[signedInKey(SERVER.id)], undefined, 'nobody is signed in');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('an account outside the company domain is told that, not told to retry', async () => {
  // Signing in again cannot fix being outside the allowed domain, and telling somebody to try would
  // waste their afternoon.
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const forbidding = (async (input: unknown) => {
      const url = String(input);
      if (url.endsWith('/api/client-config')) {
        return {
          ok: true,
          status: 200,
          text: async () => JSON.stringify({ microsoftScope: GOOD_SCOPE, providers: ['microsoft'] }),
        } as Response;
      }

      return { ok: false, status: 403, text: async () => '{"error":"domain"}' } as Response;
    }) as typeof fetch;

    const result = await signIn(SERVER, host(dir, store(), [], forbidding));

    assert.strictEqual(result.ok, false);
    assert.ok(result.ok === false && result.message.includes('company domain'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('signing out clears the token, the file and the record', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store();
    const h = host(dir, state);
    await signIn(SERVER, h);

    const result = await signOut(SERVER, h, 'server-token');

    assert.strictEqual(result.ok, true);
    assert.throws(() => readFileSync(join(dir, 'servers', tokenFileName(SERVER.url)), 'utf8'));
    assert.strictEqual(state.all[signedInKey(SERVER.id)], undefined);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a server that is down cannot keep somebody signed in on this machine', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store();
    await writeToken(dir, SERVER.url, 'server-token');
    await state.update(signedInKey(SERVER.id), { email: 'a@b.c', expiresUtc: '2027-01-01T00:00:00Z' });
    const down = (async () => {
      throw new Error('connection refused');
    }) as unknown as typeof fetch;

    const result = await signOut(SERVER, host(dir, state, [], down), 'server-token');

    // The LOCAL half succeeds regardless — that is the point — and the record is gone, so nothing
    // on this machine can use the session. What changed on the code round is that the caller is
    // told the server was never reached, instead of being handed a plain success.
    assert.strictEqual(state.all[signedInKey(SERVER.id)], undefined);
    assert.strictEqual(result.ok, false);
    assert.ok(result.message.includes('expire on its own'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a renewal for an application nobody approved is REFUSED, not minted silently', async () => {
  // The hole this closes: the confirmation was guarded on `interactive`, so a background renewal
  // skipped it entirely. A server that changed its advertised scope would have had a Microsoft
  // token minted for the new application and posted to it, with nobody ever seeing it. Caught on
  // the code round.
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    const state = store({
      [signedInKey(SERVER.id)]: { email: 'a@b.c', expiresUtc: '2026-09-06T13:00:00Z' } satisfies SignedIn,
      // Approved a DIFFERENT application than the server now advertises.
      [trustedKey(SERVER.id)]: '00000000-0000-0000-0000-000000000000',
    });
    let minted = 0;
    const spy: AuthHost = {
      ...host(dir, state),
      getSession: async () => {
        minted += 1;

        return 'idp-token';
      },
    };

    const done = await reconcile(SERVER, spy, Date.parse('2026-09-06T12:00:00Z'));

    assert.ok(done.problem.length > 0, 'a refusal is a sentence the row can show');
    assert.strictEqual(minted, 0, 'nothing may be minted for an application nobody has seen');
    assert.ok(done.problem.includes('different Microsoft application'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a bearer token cannot travel over plain http, whatever the call', async () => {
  // The check used to live only in the client-config call, so a server URL edited to plain http
  // after signing in would have had the catalog and usage calls carry the token in the clear. Two
  // reviewers found it; it now lives on the one road in.
  const dir = mkdtempSync(join(tmpdir(), 'coai-auth-'));
  try {
    let sent = 0;
    const counting = (async () => {
      sent += 1;

      return { ok: true, status: 200, text: async () => '{}' } as Response;
    }) as typeof fetch;
    const insecure = { ...SERVER, url: 'http://coai.example.com' };

    const result = await signOut(insecure, host(dir, store(), [], counting), 'a-token');

    assert.strictEqual(sent, 0, 'nothing may be sent to a plain-http host');
    assert.ok(result.ok === false || result.ok === true, 'and it must not throw');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
