import * as assert from 'node:assert';
import { mkdtempSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  AuthHost,
  SignedIn,
  StateStore,
  TokenFact,
  intentScopeOf,
  plannedAction,
  reconcile,
  revokedKey,
  sessionAction,
  signOut,
  signedInKey,
  tokenFactKey,
  trustedKey,
  writeToken,
} from '../teamServerAuth';
import { TeamServer, tokenFileName } from '../teamServers';

/**
 * A sign-in belongs to a SIDE of the machine, not to the machine.
 *
 * <p>The token file is per side already — `coaiDataDir()` is a path on whichever extension host is
 * running, so a WSL window and a Windows one hold different files. What was shared was the RECORD,
 * which is how the panel came to say "Signed in" in a distro where no token existed. These tests
 * pin the rule that replaced it, in both of the arrangements the panel offers.</p>
 */

const SERVER: TeamServer = { id: 'remsoft-dev', name: 'RemSoft Dev', url: 'https://coai.example.com/' };
const APPLICATION = '3afb5834-1111-2222-3333-444455556666';
const GOOD_SCOPE = `api://${APPLICATION}/coai.access`;
const WINDOWS = 'local|C%3A/Users/x/AppData/Roaming/Code';
const WSL = 'wsl|Ubuntu-24.04|/home/x/.vscode-server';

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

/** A server that answers with a session for whichever account the identity provider handed over. */
function serverSaying(email: string): typeof fetch {
  return (async (input: unknown) => {
    const url = String(input);
    const body = url.endsWith('/api/client-config')
      ? JSON.stringify({ microsoftScope: GOOD_SCOPE, providers: ['microsoft'] })
      : JSON.stringify({ token: `token-for-${email}`, expiresUtc: '2027-01-01T00:00:00Z', email });

    return { ok: true, status: 200, text: async () => body } as Response;
  }) as typeof fetch;
}

function host(dir: string, state: StateStore, extra: Partial<AuthHost> = {}): AuthHost {
  return {
    dataDir: dir,
    state,
    fetchImpl: serverSaying('a@b.c'),
    getSession: async () => 'idp-token',
    confirmApplication: async () => true,
    say: () => undefined,
    ...extra,
  };
}

function temp(): string {
  return mkdtempSync(join(tmpdir(), 'coai-side-'));
}

// ---------------------------------------------------------------------------------------------
// The keys
// ---------------------------------------------------------------------------------------------

test('the shared record and each side’s own are three different keys', () => {
  const shared = signedInKey(SERVER.id);
  const windows = signedInKey(SERVER.id, WINDOWS);
  const wsl = signedInKey(SERVER.id, WSL);

  assert.notStrictEqual(shared, windows);
  assert.notStrictEqual(windows, wsl);
  assert.strictEqual(shared, signedInKey(SERVER.id, ''), 'no side means the shared record');
});

test('the token record is per side even when the sign-in is shared', () => {
  // The whole point: the FILE it describes is per side in every mode, so the record has to be too.
  assert.notStrictEqual(tokenFactKey(SERVER.id, WINDOWS), tokenFactKey(SERVER.id, WSL));
  assert.notStrictEqual(tokenFactKey(SERVER.id, WINDOWS), signedInKey(SERVER.id, WINDOWS));
});

test('the sign-out stamp follows the record it revokes', () => {
  assert.strictEqual(revokedKey(SERVER.id), `${signedInKey(SERVER.id)}:revoked`);
  assert.strictEqual(revokedKey(SERVER.id, WSL), `${signedInKey(SERVER.id, WSL)}:revoked`);
});

// ---------------------------------------------------------------------------------------------
// The rule, as a table
// ---------------------------------------------------------------------------------------------

const INTENT: SignedIn = { email: 'a@b.c', expiresUtc: '2027-01-01T00:00:00Z' };
const FACT: TokenFact = { email: 'a@b.c', expiresUtc: '2027-01-01T00:00:00Z', mintedAtMs: 1_000 };
const NOW = Date.parse('2026-09-06T12:00:00Z');

test('a side that holds the intended, unexpired token does nothing', () => {
  assert.strictEqual(sessionAction(INTENT, FACT, 0, NOW), 'nothing');
});

test('a side with no token of its own mints one — this is the WSL window', () => {
  assert.strictEqual(sessionAction(INTENT, undefined, 0, NOW), 'mint');
});

test('a token for a DIFFERENT account than the intent is REPLACED, not minted over', () => {
  // Reachable by turning the per-side switch off while two sides hold two accounts. Without this
  // the WSL window would go on reviewing as somebody the panel no longer names — and minting over
  // it rather than replacing it would leave that person's session live on the server.
  const other: TokenFact = { ...FACT, email: 'someone.else@b.c' };

  assert.strictEqual(sessionAction(INTENT, other, 0, NOW), 'replace');
});

test('a token older than the last sign-out is replaced even though somebody signed back in', () => {
  // The hole the code round found. Sign out on Windows (stamped), sign in again there, and a WSL
  // window that had not refreshed in between saw an intent, a matching account and an unexpired
  // token — so it kept a session the person had ended. The stamp has to mean the same thing whether
  // or not somebody signed back in.
  assert.strictEqual(sessionAction(INTENT, FACT, FACT.mintedAtMs + 1, NOW), 'replace');
});

test('a token minted after the sign-out, for the intended account, is left alone', () => {
  assert.strictEqual(sessionAction(INTENT, FACT, FACT.mintedAtMs - 1, NOW), 'nothing');
});

test('a token that is nearly out is renewed — the old silent renewal, unchanged', () => {
  const soon: TokenFact = { ...FACT, expiresUtc: '2026-09-06T13:00:00Z' };

  assert.strictEqual(sessionAction(INTENT, soon, 0, NOW), 'mint');
});

test('a sign-out elsewhere signs this side out too', () => {
  assert.strictEqual(sessionAction(undefined, FACT, FACT.mintedAtMs + 1, NOW), 'signOut');
});

test('a token minted AFTER the last sign-out is left alone', () => {
  // Sign out, sign in again, then another side refreshes: the stamp is older than what this side
  // now holds, so it is not a revocation of it.
  assert.strictEqual(sessionAction(undefined, FACT, FACT.mintedAtMs - 1, NOW), 'nothing');
});

test('a side with nothing at all, and no intent, does nothing', () => {
  assert.strictEqual(sessionAction(undefined, undefined, Date.now(), NOW), 'nothing');
});

// ---------------------------------------------------------------------------------------------
// What the rule does when it runs
// ---------------------------------------------------------------------------------------------

test('the WSL window signs ITSELF in from the shared record, without a prompt', async () => {
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
    });
    const asked: boolean[] = [];
    const done = await reconcile(SERVER, host(dir, state, {
      side: WSL,
      getSession: async (_scope, interactive) => {
        asked.push(interactive);

        return 'idp-token';
      },
    }), NOW);

    assert.deepStrictEqual(asked, [false], 'nobody may be prompted for this');
    assert.strictEqual(done.problem, '');
    assert.ok(done.changed);
    assert.ok(existsSync(join(dir, 'servers', tokenFileName(SERVER.url))), 'this side has a token now');
    assert.ok(state.all[tokenFactKey(SERVER.id, WSL)] !== undefined, 'and a record of whose it is');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a side whose Microsoft session has gone says so instead of claiming one', async () => {
  // The state the plan round called a phantom session: the shared record says signed in, the mint
  // cannot happen here, and the row used to read "Signed in as ..." over a distro with no token.
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
    });

    const done = await reconcile(SERVER, host(dir, state, {
      side: WSL,
      getSession: async () => undefined,
    }), NOW);

    assert.ok(done.problem.includes('Microsoft'), done.problem);
    assert.strictEqual(done.changed, false);
    assert.strictEqual(state.all[tokenFactKey(SERVER.id, WSL)], undefined, 'no token, no record');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a silent mint that comes back as the WRONG account writes nothing', async () => {
  // A machine signed in to a personal and a work Microsoft account hands back whichever is active
  // on this side. Minting that one would put somebody else's identity on the company's server.
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
    });
    const ended: string[] = [];
    const impl = (async (input: unknown, init: RequestInit = {}) => {
      const url = String(input);
      if ((init.method ?? 'GET') === 'DELETE') {
        ended.push(url);

        return { ok: true, status: 204, text: async () => '' } as Response;
      }

      return {
        ok: true,
        status: 200,
        text: async () => (url.endsWith('/api/client-config')
          ? JSON.stringify({ microsoftScope: GOOD_SCOPE, providers: ['microsoft'] })
          : JSON.stringify({
            token: 'personal-token',
            expiresUtc: '2027-01-01T00:00:00Z',
            email: 'personal@example.com',
          })),
      } as Response;
    }) as typeof fetch;

    const done = await reconcile(SERVER, host(dir, state, { side: WSL, fetchImpl: impl }), NOW);

    assert.ok(done.problem.includes('personal@example.com'), done.problem);
    assert.ok(done.problem.includes('a@b.c'), 'it names the account that was expected');
    assert.strictEqual(ended.length, 1, 'the session it created is ended again');
    assert.ok(!existsSync(join(dir, 'servers', tokenFileName(SERVER.url))), 'and no token is written');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('signing out on one side takes the other side with it', async () => {
  const dirWindows = temp();
  const dirWsl = temp();
  try {
    const shared = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
      [tokenFactKey(SERVER.id, WINDOWS)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
      [tokenFactKey(SERVER.id, WSL)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
    });
    await writeToken(dirWindows, SERVER.url, 'windows-token');
    await writeToken(dirWsl, SERVER.url, 'wsl-token');

    // Windows signs out. It can delete its own token and end its own session; the WSL distro is a
    // filesystem it cannot touch, so all it leaves is the stamp.
    await signOut(SERVER, host(dirWindows, shared, { side: WINDOWS }), 'windows-token');
    assert.ok(!existsSync(join(dirWindows, 'servers', tokenFileName(SERVER.url))));
    assert.ok(existsSync(join(dirWsl, 'servers', tokenFileName(SERVER.url))), 'still there, for now');

    // The WSL window refreshes.
    const done = await reconcile(SERVER, host(dirWsl, shared, { side: WSL }), NOW);

    assert.ok(done.changed, 'the WSL side must act on a sign-out it did not press');
    assert.ok(
      !existsSync(join(dirWsl, 'servers', tokenFileName(SERVER.url))),
      'a sign-out that left a usable token in the distro is the defect this closes',
    );
    assert.strictEqual(shared.all[tokenFactKey(SERVER.id, WSL)], undefined);
  } finally {
    rmSync(dirWindows, { recursive: true, force: true });
    rmSync(dirWsl, { recursive: true, force: true });
  }
});

test('with the sides separated, a sign-out on one leaves the other alone', async () => {
  const dirWindows = temp();
  const dirWsl = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id, WINDOWS)]: INTENT,
      [signedInKey(SERVER.id, WSL)]: { email: 'other@b.c', expiresUtc: '2027-01-01T00:00:00Z' },
      [trustedKey(SERVER.id)]: APPLICATION,
      [tokenFactKey(SERVER.id, WINDOWS)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
      [tokenFactKey(SERVER.id, WSL)]: {
        email: 'other@b.c', expiresUtc: '2027-01-01T00:00:00Z', mintedAtMs: 1_000,
      } satisfies TokenFact,
    });
    await writeToken(dirWindows, SERVER.url, 'windows-token');
    await writeToken(dirWsl, SERVER.url, 'wsl-token');

    await signOut(SERVER, host(dirWindows, state, { side: WINDOWS, perSide: true }), 'windows-token');
    const done = await reconcile(SERVER, host(dirWsl, state, { side: WSL, perSide: true }), NOW);

    assert.deepStrictEqual(done, { problem: '', changed: false });
    assert.ok(
      existsSync(join(dirWsl, 'servers', tokenFileName(SERVER.url))),
      'separate accounts means a sign-out is one side’s own business',
    );
  } finally {
    rmSync(dirWindows, { recursive: true, force: true });
    rmSync(dirWsl, { recursive: true, force: true });
  }
});

test('a token file deleted by hand is not believed for a moment longer', async () => {
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
      [tokenFactKey(SERVER.id, WINDOWS)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
    });
    // No token was ever written: the record describes a file that is not there.
    const done = await reconcile(SERVER, host(dir, state, { side: WINDOWS }), NOW);

    assert.strictEqual(done.problem, '', 'it re-mints rather than complaining');
    assert.ok(existsSync(join(dir, 'servers', tokenFileName(SERVER.url))));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------------------------
// What the CODE round found — one test each
// ---------------------------------------------------------------------------------------------

test('a stale token is ENDED on the server before a new one is asked for', async () => {
  // Minting over it would leave the other account's session running on the server, reachable by
  // whoever still holds that token. Ending it first is the difference between `replace` and `mint`.
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
      [tokenFactKey(SERVER.id, WINDOWS)]: {
        email: 'someone.else@b.c', expiresUtc: '2027-01-01T00:00:00Z', mintedAtMs: 1_000,
      } satisfies TokenFact,
    });
    await writeToken(dir, SERVER.url, 'the-other-account-token');

    const ended: string[] = [];
    const impl = (async (input: unknown, init: RequestInit = {}) => {
      if ((init.method ?? 'GET') === 'DELETE') {
        ended.push(String(input));

        return { ok: true, status: 204, text: async () => '' } as Response;
      }

      return (await (serverSaying('a@b.c') as (i: unknown, o: unknown) => Promise<Response>)(input, init));
    }) as typeof fetch;

    const done = await reconcile(SERVER, host(dir, state, { side: WINDOWS, fetchImpl: impl }), NOW);

    assert.strictEqual(done.problem, '');
    assert.strictEqual(ended.length, 1, 'the account that is being replaced is signed out first');
    assert.strictEqual(
      (state.all[tokenFactKey(SERVER.id, WINDOWS)] as TokenFact).email,
      'a@b.c',
      'and the side ends up on the intended account',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a sign-out this side did not press does not re-stamp the revocation', async () => {
  // Windows signs out at t1 and signs in again at t2. If the WSL window's own sign-out then stamped
  // t3, Windows' NEW session would look older than the revocation and be thrown away on its next
  // refresh — a sign-out invalidating the sign-in that followed it.
  const dirWsl = temp();
  try {
    const state = store({
      [revokedKey(SERVER.id)]: 1_500,
      [tokenFactKey(SERVER.id, WSL)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
    });
    await writeToken(dirWsl, SERVER.url, 'wsl-token');

    await reconcile(SERVER, host(dirWsl, state, { side: WSL, now: () => 9_999 }), NOW);

    assert.strictEqual(state.all[revokedKey(SERVER.id)], 1_500, 'the stamp is the one that was set');
  } finally {
    rmSync(dirWsl, { recursive: true, force: true });
  }
});

test('a token file that will not delete keeps its record, so the next refresh tries again', async () => {
  // Clearing the record first read as tidier and was a hole: with no record nothing ever came back
  // here, so a token that failed to delete stayed on disk as a usable credential under a panel
  // reporting "signed out".
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [tokenFactKey(SERVER.id, WINDOWS)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
    });
    await writeToken(dir, SERVER.url, 'windows-token');
    // The directory is what `rm` is given; making it unreadable is not portable, so the failure is
    // injected where the code actually branches — a dataDir that cannot be written.
    const wedged: AuthHost = {
      ...host(dir, state, { side: WINDOWS }),
      dataDir: join(dir, 'no', 'such', '\0invalid'),
    };

    const out = await signOut(SERVER, wedged, 'windows-token');

    assert.strictEqual(out.ok, false);
    assert.ok(
      state.all[tokenFactKey(SERVER.id, WINDOWS)] !== undefined,
      'the record survives, because it is the only thing that will bring anybody back here',
    );
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the mint stamp comes from the injected clock, not the machine', async () => {
  // `.claude/rules/shared/common/utc-timestamps.md` rule 2: it is persisted and then COMPARED
  // against another side's sign-out, which is exactly the case that rule names.
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [trustedKey(SERVER.id)]: APPLICATION,
    });

    await reconcile(SERVER, host(dir, state, { side: WSL, now: () => 4_242 }), NOW);

    assert.strictEqual((state.all[tokenFactKey(SERVER.id, WSL)] as TokenFact).mintedAtMs, 4_242);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('nothing to do is answered without touching the network', async () => {
  // What `plannedAction` exists for: the panel asks BEFORE it says "Signing in…", so the ordinary
  // sixty-second refresh neither flashes a spinner nor makes a request.
  const dir = temp();
  try {
    const state = store({
      [signedInKey(SERVER.id)]: INTENT,
      [tokenFactKey(SERVER.id, WINDOWS)]: { ...INTENT, mintedAtMs: 1_000 } satisfies TokenFact,
    });
    await writeToken(dir, SERVER.url, 'windows-token');
    let called = 0;
    const counting = (async () => {
      called += 1;

      return { ok: true, status: 200, text: async () => '{}' } as Response;
    }) as typeof fetch;

    const action = await plannedAction(
      SERVER,
      host(dir, state, { side: WINDOWS, fetchImpl: counting }),
      NOW,
    );

    assert.strictEqual(action, 'nothing');
    assert.strictEqual(called, 0);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the scope rule has ONE answer, and both modules ask for it', () => {
  // It was computed in the panel and again in the auth module. Two places to change when the rule
  // changes is two places that can disagree about which record a sign-in is in. Raised on the second
  // code round.
  assert.strictEqual(intentScopeOf(WSL, false), '', 'sharing on means the shared record');
  assert.strictEqual(intentScopeOf(WSL, true), WSL);
  assert.strictEqual(signedInKey(SERVER.id, intentScopeOf(WSL, false)), signedInKey(SERVER.id));
});

test('a sign-out that has not reached a side yet survives the sides being separated', () => {
  // Sign out with the sides shared, separate them before the other side refreshes, and that side
  // would find a token, no intent — and no revocation in its NEW scope. So it kept a session the
  // person had ended. The stamp has to travel with the intent. Raised on the second code round.
  const shared = 2_000;
  const state = store({ [revokedKey(SERVER.id)]: shared });

  // What `carryTeamLogins` does when the switch goes ON, for the revocation half.
  const carried = state.get<number>(revokedKey(SERVER.id));
  assert.strictEqual(carried, shared);

  const fact: TokenFact = { ...INTENT, mintedAtMs: 1_000 };
  assert.strictEqual(
    sessionAction(undefined, fact, carried ?? 0, NOW),
    'signOut',
    'carried across, the stamp still says this token is one the person signed out of',
  );
  assert.strictEqual(
    sessionAction(undefined, fact, 0, NOW),
    'nothing',
    'and without it — the defect — the side keeps a session that was ended',
  );
});
