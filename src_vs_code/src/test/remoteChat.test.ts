import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CHAT_KIND,
  CHAT_ROLE,
  REMOTE_TURNS,
  acceptedId,
  backoffMs,
  readStep,
  remoteIsFull,
  requestBody,
  turnKey,
  waitSecondsFor,
} from '../remoteAsk';
import { RemoteChatSession, RemoteTransport } from '../remoteChatSession';
import { REQUEST_TIMEOUT_MS, ServerResult } from '../teamServerApi';
import { remoteChatFor, transportFor } from '../chatRemote';
import { Timers } from '../cliChatSession';
import { TurnBudgets } from '../chatSession';
import { Vendor } from '../vendors';

/**
 * A conversation held by a server that holds no conversation.
 *
 * <p>The wire is the one `coai-mcp --ask-remote` has spoken since the Team server existed, and these
 * tests pin the shapes rather than describe them — a second dialect of one protocol is how two
 * halves become green about opposite things, which this repository has paid for once.</p>
 */

const BUDGETS: TurnBudgets = { startupMs: 30_000, turnMs: 60_000 };

/** Timers that fire at once: these tests are about what the server says, not about waiting. */
const AT_ONCE: Timers = {
  after: (_ms, run) => {
    const handle = setTimeout(run, 0);

    return () => clearTimeout(handle);
  },
};

const answered = (text: string): unknown => ({ status: 'done', answer: text, tokensIn: 10, tokensOut: 3 });

/** A server that says what the test tells it to, in order, and records what it was asked. */
function fakeServer(
  replies: readonly { body?: unknown; failure?: string; status?: number }[],
  /** Held open, so a test can close the tab while the submit is still in flight. */
  holdSubmit?: Promise<void>,
): {
  transport: RemoteTransport;
  submitted: Record<string, unknown>[];
  polls: { id: string; waitSeconds: number }[];
  cancelled: string[];
} {
  const submitted: Record<string, unknown>[] = [];
  const polls: { id: string; waitSeconds: number }[] = [];
  const cancelled: string[] = [];
  let next = 0;

  return {
    submitted,
    polls,
    cancelled,
    transport: {
      submit: async (body) => {
        submitted.push(body);
        await holdSubmit;

        return { body: { id: 'review-1' }, failure: '', status: 200 };
      },
      poll: async (id, waitSeconds) => {
        polls.push({ id, waitSeconds });
        const reply = replies[Math.min(next, replies.length - 1)] ?? {};
        next += 1;

        return { body: reply.body, failure: reply.failure ?? '', status: reply.status ?? 200 };
      },
      cancel: async (id) => {
        cancelled.push(id);
      },
    },
  };
}

const vendor = { vendor: 'claude', model: 'claude-opus-5' };

test('a chat turn carries NO review role, which is what tells it from a review', () => {
  // Measured against the live server: `Chat` is refused outright — "'Chat' is not a review role" —
  // and an empty one is accepted. The server is right to refuse it: a role carries a prompt, a
  // threshold and a round budget, and a chat has none of those. A usage row with no role is a
  // conversation, because every review has one.
  const body = requestBody('claude', 'claude-opus-5', 'explain this', 60);

  assert.strictEqual(body['role'], CHAT_ROLE);
  assert.strictEqual(CHAT_ROLE, '', 'a chat is being sent as a review role again');
  // And it says what it IS, rather than letting an absence say it. The follow-up plan makes `kind`
  // part of the contract with `review` as the default, so a client sending neither is read as a
  // review — and the day the server requires a role for one, every chat sending a blank role and no
  // kind stops working with a message about roles. Sending it now is what makes this client the old
  // client that KEEPS working, and it costs nothing: measured against the live server on 2026-09-09,
  // a body carrying `kind` was accepted in 56 ms, exactly as one without it. (codex, the code round.)
  assert.strictEqual(body['kind'], CHAT_KIND);
  assert.strictEqual(CHAT_KIND, 'chat');
  assert.strictEqual(body['vendor'], 'claude');
  assert.strictEqual(body['model'], 'claude-opus-5');
  assert.strictEqual(body['prompt'], 'explain this');
  assert.strictEqual(body['timeoutSeconds'], 60);
});

test('an accepted submit gives its review id, and anything else gives none', () => {
  assert.strictEqual(acceptedId({ id: 'review-1', position: 2 }), 'review-1');
  for (const notOne of [{}, { id: 7 }, null, undefined, 'a login page', []]) {
    assert.strictEqual(acceptedId(notOne), '', `it read an id out of: ${JSON.stringify(notOne)}`);
  }
});

test('an id the server invents is checked before it becomes an authenticated URL', () => {
  // The id is a stranger's answer and it is interpolated into `api/reviews/<id>` on a request that
  // carries the bearer token. A compromised or merely misconfigured server answering `../../x` or
  // one carrying query syntax would send that token somewhere this side never meant to reach. The
  // same guard the catalog's vendor ids get, and the thread ids in the local adapters. (codex.)
  for (const hostile of [
    { id: '../../api/servers' },
    { id: 'review-1?wait=0&x=' },
    { id: 'review 1' },
    { id: 'review/1' },
    { id: '' },
    { id: `r${'e'.repeat(200)}` },
  ]) {
    assert.strictEqual(acceptedId(hostile), '', `it accepted the id: ${hostile.id}`);
  }

  // What a real server sends still passes — a GUID, and the shapes around it.
  assert.strictEqual(
    acceptedId({ id: '3afb5834-0c1e-4a9b-9f2d-5c7e8a1b2c3d' }),
    '3afb5834-0c1e-4a9b-9f2d-5c7e8a1b2c3d',
  );
  assert.strictEqual(acceptedId({ id: 'review_1.2-3' }), 'review_1.2-3');
});

test('a finished review is its answer; a finished review with nothing in it is a failure', () => {
  assert.deepStrictEqual(readStep(answered('  it means this  ')), {
    kind: 'answer',
    text: 'it means this',
  });
  // The same rule the local adapters follow: a page showing nothing looks like a model with nothing
  // to say, which is a different thing from a turn that produced none.
  assert.strictEqual(readStep({ status: 'done', answer: '   ' }).kind, 'failure');
});

test('a failed review says what the server said, and an unknown status is still waiting', () => {
  const failed = readStep({ status: 'failed', failure: 'the vendor refused the prompt' });
  assert.strictEqual(failed.kind, 'failure');
  assert.match((failed as { failure: string }).failure, /the vendor refused the prompt/);

  // A body with no status at all is a poll that answered nothing yet, which IS waiting.
  assert.deepStrictEqual(readStep({}), { kind: 'waiting', position: 0 });
});

test('a poll never asks for longer than the turn has left', () => {
  // Asking for the full window when four seconds remain means the answer arrives after this side
  // has given up, which reads as a server that never answered.
  assert.strictEqual(waitSecondsFor(4_000), 4);
  // Eight, not twenty-five: `ask` aborts the request at ten seconds — see the test below.
  assert.strictEqual(waitSecondsFor(600_000), 8);
  assert.strictEqual(waitSecondsFor(10), 1, 'a poll asked for zero seconds is a busy loop');
});

test('a refusal is waited out, and the wait grows', () => {
  assert.strictEqual(backoffMs(0), 1_000);
  assert.strictEqual(backoffMs(1), 2_000);
  assert.strictEqual(backoffMs(2), 4_000);
  assert.strictEqual(backoffMs(20), 10_000, 'the wait grew without a ceiling');
});

test('a remote conversation is full after three turns, and says so before the fourth', () => {
  // The owner's ruling: a remote model keeps no conversation, so every turn re-sends the whole
  // transcript and the cost of turn N is the cost of everything before it.
  assert.strictEqual(REMOTE_TURNS, 3);
  assert.strictEqual(remoteIsFull(0), false);
  assert.strictEqual(remoteIsFull(2), false);
  assert.strictEqual(remoteIsFull(3), true);
});

test('a turn is submitted, polled and answered', async () => {
  const server = fakeServer([{ body: answered('it means this') }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  assert.deepStrictEqual(await session.send('explain this'), { ok: true, answer: 'it means this' });
  assert.strictEqual(server.submitted.length, 1);
  assert.strictEqual(server.polls[0]?.id, 'review-1');
});

test('a queue is waited through, not given up on', async () => {
  const server = fakeServer([
    { body: { status: 'queued', position: 3 } },
    { body: { status: 'running', position: 0 } },
    { body: answered('at last') },
  ]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  assert.deepStrictEqual(await session.send('explain this'), { ok: true, answer: 'at last' });
  assert.strictEqual(server.polls.length, 3, 'it stopped polling before the answer arrived');
});

test('a 429 is waited out and then the answer is taken', async () => {
  // The shared-account ceiling doing its job: somebody else's round has the vendor.
  const server = fakeServer([
    { status: 429, failure: 'too many requests' },
    { status: 429, failure: 'too many requests' },
    { body: answered('after the queue cleared') },
  ]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  assert.deepStrictEqual(await session.send('explain this'), { ok: true, answer: 'after the queue cleared' });
});

test('a server that stops answering fails the turn rather than hanging on it', async () => {
  const server = fakeServer([{ failure: 'the server could not be reached' }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const result = await session.send('explain this');
  assert.ok(!result.ok);
  assert.match(result.failure, /could not be reached/);
});

test('a 2xx that is not a review is refused rather than shown as an answer', async () => {
  // Something in front of the server can answer 200 with a login page, and reporting that as an
  // answer would put a login page in somebody's conversation.
  const server = fakeServer([]);
  const notAReview: RemoteTransport = {
    ...server.transport,
    submit: async () => ({ body: { hello: 'sign in please' }, failure: '', status: 200 }),
  };
  const session = new RemoteChatSession(notAReview, vendor, BUDGETS, AT_ONCE);

  const result = await session.send('explain this');
  assert.ok(!result.ok);
  assert.match(result.failure, /did not say which review/);
});

test('closing the tab CANCELS a review still in the queue', async () => {
  // A queued review nobody is waiting for is a vendor slot somebody else could have had, and on a
  // shared account that is the scarcest thing the server has.
  const server = fakeServer([{ body: { status: 'queued', position: 9 } }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const turn = session.send('explain this');
  await new Promise((resolve) => setImmediate(resolve));
  session.dispose();

  const result = await turn;
  assert.ok(!result.ok);
  assert.match(result.failure, /closed/);
  assert.deepStrictEqual(server.cancelled, ['review-1']);
});

test('two questions at once are two turns, one after the other', async () => {
  const server = fakeServer([{ body: answered('one') }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const first = session.send('first');
  const second = session.send('second');
  await Promise.all([first, second]);

  assert.strictEqual(server.submitted.length, 2, 'the second turn was submitted before the first finished');
});

test('a poll never asks the server to hold longer than this side will wait', () => {
  // Two constants that had to be read together: `ask` aborts every request at REQUEST_TIMEOUT_MS,
  // and a long poll asking for longer than that is a request this side kills before the server
  // answers — every poll failing with "it did not answer within 10s", for a server behaving
  // perfectly. (codex, the plan round, from the other end.)
  assert.ok(
    waitSecondsFor(600_000) * 1000 < REQUEST_TIMEOUT_MS,
    `a poll asks for ${waitSecondsFor(600_000)}s and the request is aborted at ${REQUEST_TIMEOUT_MS / 1000}s`,
  );
});

test('a status this client does not know is named, not waited out', () => {
  // The server has exactly four: Queued, Running, Done, Failed. A fifth value means a server much
  // newer than this build, or something in front of it answering for it — and three minutes of
  // "Thinking…" is the worst way to say either. (gemini and local, one finding from two sides.)
  const odd = readStep({ status: 'abandoned' });

  assert.strictEqual(odd.kind, 'failure');
  assert.match((odd as { failure: string }).failure, /abandoned/);

  // The two that ARE running stay running.
  assert.strictEqual(readStep({ status: 'queued', position: 3 }).kind, 'waiting');
  assert.strictEqual(readStep({ status: 'running' }).kind, 'waiting');
});

test('a tab closed while the question is still being SUBMITTED cancels what the server accepted', async () => {
  // The window between the POST leaving and its answer arriving is the one the cancellation misses:
  // `dispose` has nothing to cancel yet, and by the time the id exists the session is already
  // closed. The job then runs, holds a vendor slot on a shared account, and answers into nothing —
  // which is the exact thing closing a tab was supposed to prevent. (codex.)
  let release = (): void => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  const server = fakeServer([{ body: answered('nobody will read this') }], held);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const turn = session.send('explain this');
  await new Promise((resolve) => setImmediate(resolve));
  assert.strictEqual(server.submitted.length, 1, 'the submit had not left yet');
  session.dispose();
  release();

  const result = await turn;
  assert.ok(!result.ok);
  assert.match(result.failure, /closed/);
  assert.deepStrictEqual(server.cancelled, ['review-1'], 'the accepted job was left running');
  assert.strictEqual(server.polls.length, 0, 'a closed session went on polling');
});

test('a queue tells the person where they are, rather than leaving them at "Thinking…"', async () => {
  // A shared Team server queues twenty deep per person by design, and a poll already parses the
  // position — it was read and thrown away. Minutes of an unchanging spinner is the one shape that
  // cannot be told from a broken tab. (gemini.)
  const server = fakeServer([
    { body: { status: 'queued', position: 4 } },
    { body: { status: 'queued', position: 2 } },
    { body: { status: 'running', position: 0 } },
    { body: answered('at last') },
  ]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const seen: number[] = [];
  const result = await session.send('explain this', (position) => seen.push(position));

  assert.deepStrictEqual(result, { ok: true, answer: 'at last' });
  assert.deepStrictEqual(seen, [4, 2, 0], 'the queue position never reached the caller');
});

test('a caller that wants no progress is not required to take any', async () => {
  // `ChatSession` is one interface with two implementations and the local one has no queue at all.
  const server = fakeServer([{ body: { status: 'queued', position: 1 } }, { body: answered('done') }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  assert.deepStrictEqual(await session.send('explain this'), { ok: true, answer: 'done' });
});

test('the wire carries the vendor the SERVER knows, and the status the server actually sent', async () => {
  // Two things one seam answers. The vendor: a row saved before `remoteVendor` existed is called
  // `<server>-<vendor>`, and sending that id produces a 400 reading "not a vendor here" — a defect
  // this repository shipped in three releases. The status: a success arm reporting a literal 200 is
  // a fact invented at the one place the real one was in hand, and the poll loop decides on exactly
  // that field. (codex and gemini, the code round.)
  const routes: string[] = [];
  const bodies: unknown[] = [];
  const asked = async <T>(
    _url: string,
    route: string,
    attempt: { body?: unknown } = {},
  ): Promise<ServerResult<T>> => {
    routes.push(route);
    if (attempt.body !== undefined) {
      bodies.push(attempt.body);

      return { ok: true, status: 202, contract: 1, value: { id: 'review-9' } as T };
    }

    return { ok: true, status: 203, contract: 1, value: { status: 'done', answer: 'yes' } as T };
  };

  const legacy: Vendor = {
    id: 'remsoftdev-codex',
    runtime: 'remote',
    model: 'gpt-5-codex',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: 'https://coai.remsoft.dev',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  };
  const server = { id: 'remsoftdev', name: 'RemSoft Dev', url: 'https://coai.remsoft.dev' };

  const session = remoteChatFor(legacy, server, 'a-token', asked);
  assert.ok(session !== undefined, 'a signed-in row got no session');
  assert.deepStrictEqual(await session.send('explain this'), { ok: true, answer: 'yes' });

  assert.strictEqual(
    (bodies[0] as Record<string, unknown>)['vendor'],
    'codex',
    'the row id went to the server as a vendor name',
  );
  assert.deepStrictEqual(routes, ['api/reviews', 'api/reviews/review-9?wait=8']);

  // A transport that reports what it was told. 202 and 203 are not shapes this server sends today —
  // they are here because a literal would have swallowed either.
  const transport = transportFor('https://coai.remsoft.dev', 'a-token', asked);
  assert.strictEqual((await transport.submit({ a: 1 })).status, 202);
  assert.strictEqual((await transport.poll('review-9', 8)).status, 203);
});

test('a row belonging to a server this side cannot reach gets no session at all', () => {
  const server = { id: 'remsoftdev', name: 'RemSoft Dev', url: 'https://coai.remsoft.dev' };
  const row: Vendor = {
    id: 'remsoftdev-codex',
    runtime: 'remote',
    model: 'gpt-5-codex',
    enabled: true,
    plan: true,
    code: true,
    baseUrl: 'https://coai.remsoft.dev',
    executablePath: '',
    pricePerMillionIn: 0,
    pricePerMillionOut: 0,
  };

  assert.strictEqual(remoteChatFor(row, server, ''), undefined, 'a signed-out row built a session');
  assert.strictEqual(remoteChatFor(row, { ...server, url: '' }, 'a-token'), undefined);
});

test('a turn carries one idempotency key, and two turns carry two', async () => {
  // The failure this answers: the POST reaches the server, the job is accepted, and the response is
  // lost on the way back. Pressing send again without a key makes a SECOND job — two slots on a
  // shared account for one question. The key is per TURN, not per attempt; per attempt would be no
  // key at all, and per question would collapse two deliberate asks into one.
  const server = fakeServer([{ body: answered('one') }]);
  const keys = ['turn-a', 'turn-b'];
  let next = 0;
  const session = new RemoteChatSession(
    server.transport, vendor, BUDGETS, AT_ONCE, Date.now, () => keys[next++] as string,
  );

  await session.send('first');
  await session.send('second');

  assert.strictEqual(server.submitted[0]?.['idempotencyKey'], 'turn-a');
  assert.strictEqual(server.submitted[1]?.['idempotencyKey'], 'turn-b');
});

test('a key is a shape the server will accept, and never the same twice', () => {
  // The server checks it before it becomes part of a lookup: letters, digits and a few punctuation
  // marks, at most 128 of them. A UUID is what it was written to accept.
  const seen = new Set<string>();
  for (let i = 0; i < 50; i += 1) {
    const key = turnKey();
    assert.match(key, /^[A-Za-z0-9._:-]{1,128}$/, key);
    assert.ok(!seen.has(key), 'a key repeated across turns would merge two questions into one');
    seen.add(key);
  }
});

test('a client that sends no key sends no field, rather than an empty one', () => {
  // An empty string is a value; the absence is what says "I did not choose one". A server reading a
  // blank key as a key would match every keyless submit against every other.
  const without = requestBody('claude', 'm', 'p', 60);
  assert.ok(!('idempotencyKey' in without));

  const with_ = requestBody('claude', 'm', 'p', 60, 'turn-1');
  assert.strictEqual(with_['idempotencyKey'], 'turn-1');
});

test('the person retrying a failed turn carries the SAME key, which is the whole point', async () => {
  // A key minted per `send` is a key minted per ATTEMPT, and the attempt this exists for is the
  // second one. The POST arrives, the job is accepted, its answer is lost; the person presses send
  // again on the same question — and with a fresh key the server has no way to know it is the same
  // question and makes a second paid job. (codex, the code round, twice.)
  const submitted: Record<string, unknown>[] = [];
  let refuse = true;
  const transport: RemoteTransport = {
    submit: async (body) => {
      submitted.push(body);
      const failing = refuse;
      refuse = false;

      return failing
        ? { body: undefined, failure: 'the connection went away', status: 0 }
        : { body: { id: 'review-1' }, failure: '', status: 202 };
    },
    poll: async () => ({ body: answered('at last'), failure: '', status: 200 }),
    cancel: async () => undefined,
  };
  let minted = 0;
  const session = new RemoteChatSession(
    transport, vendor, BUDGETS, AT_ONCE, Date.now, () => `turn-${(minted += 1)}`,
  );

  const failed = await session.send('explain this');
  assert.ok(!failed.ok, 'the first attempt was supposed to fail');
  const answeredTurn = await session.send('explain this');
  assert.ok(answeredTurn.ok);

  assert.strictEqual(submitted.length, 2);
  assert.strictEqual(
    submitted[1]?.['idempotencyKey'],
    submitted[0]?.['idempotencyKey'],
    'the retry minted a new key, so the server would have made a second paid job',
  );
  assert.strictEqual(minted, 1, 'only the first attempt needed a key');
});

test('a DIFFERENT question after a failure is a new turn, with a new key', async () => {
  // The kept key belongs to that question. A new one is a new turn, and the server would refuse the
  // old key for it anyway — it binds a key to a fingerprint of what was asked.
  const submitted: Record<string, unknown>[] = [];
  let refuse = true;
  const transport: RemoteTransport = {
    submit: async (body) => {
      submitted.push(body);
      const failing = refuse;
      refuse = false;

      return failing
        ? { body: undefined, failure: 'gone', status: 0 }
        : { body: { id: 'review-1' }, failure: '', status: 202 };
    },
    poll: async () => ({ body: answered('ok'), failure: '', status: 200 }),
    cancel: async () => undefined,
  };
  let minted = 0;
  const session = new RemoteChatSession(
    transport, vendor, BUDGETS, AT_ONCE, Date.now, () => `turn-${(minted += 1)}`,
  );

  await session.send('the first question');
  await session.send('a different question');

  assert.notStrictEqual(submitted[1]?.['idempotencyKey'], submitted[0]?.['idempotencyKey']);
  assert.strictEqual(minted, 2);
});

test('a turn that was ACCEPTED leaves no key to reuse', async () => {
  // From there the id identifies the turn, and the next question is a new one. Keeping the key would
  // make an identical follow-up return the earlier answer instead of asking again.
  const submitted: Record<string, unknown>[] = [];
  const server = fakeServer([{ body: answered('one') }]);
  const transport: RemoteTransport = {
    ...server.transport,
    submit: async (body) => {
      submitted.push(body);

      return server.transport.submit(body);
    },
  };
  let minted = 0;
  const session = new RemoteChatSession(
    transport, vendor, BUDGETS, AT_ONCE, Date.now, () => `turn-${(minted += 1)}`,
  );

  await session.send('same words');
  await session.send('same words');

  assert.notStrictEqual(submitted[1]?.['idempotencyKey'], submitted[0]?.['idempotencyKey']);
  assert.strictEqual(minted, 2);
});

/**
 * Stopping a turn a SERVER is running.
 *
 * <p>The remote half has no process to kill and no conversation to lose. What it has is a job on
 * somebody else's machine, holding a slot on a shared vendor account, and a poll that may be holding
 * a connection open for another eight seconds. So a stop here is two obligations that pull apart: the
 * person must see it took AT ONCE, and the server must be told so the slot is freed rather than left
 * to the drop-on-no-poll sweep three minutes later.</p>
 */

test('stopping a remote turn settles it at once, without waiting for the poll in flight', async () => {
  // The finding two reviewers raised independently: a poll holds the connection for up to eight
  // seconds, and a person who pressed stop is not waiting eight seconds to learn it worked.
  let releasePoll = (): void => undefined;
  const held = new Promise<void>((resolve) => {
    releasePoll = resolve;
  });
  const server = fakeServer([{ body: { status: 'queued', position: 2 } }]);
  const slowPoll: RemoteTransport = {
    ...server.transport,
    poll: async (id, waitSeconds) => {
      server.polls.push({ id, waitSeconds });
      await held;

      return { body: answered('too late to matter'), failure: '', status: 200 };
    },
  };
  const session = new RemoteChatSession(slowPoll, vendor, BUDGETS, AT_ONCE);

  const answering = session.send('explain this');
  await new Promise((resolve) => setImmediate(resolve));
  session.stop();

  const result = await answering;
  assert.strictEqual(result.ok, false, 'a stopped turn is not an answer');
  assert.strictEqual(result.ok === false ? result.stopped : undefined, true);
  assert.deepStrictEqual(server.cancelled, ['review-1'], 'the server must be told, not left to its sweep');

  // And the answer that arrives afterwards is dropped rather than resolving the turn a second time.
  releasePoll();
  await new Promise((resolve) => setImmediate(resolve));
  assert.strictEqual(
    (await answering).ok,
    false,
    'a poll landing after a stop must not turn a stopped turn into an answered one',
  );
});

test('a remote conversation still works after a turn is stopped', async () => {
  // `stop` is not `dispose`: the session stays open. A server holds no conversation anyway, so the
  // only thing that must survive is the session's own ability to submit again.
  const server = fakeServer([{ body: answered('the second answer') }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const first = session.send('one');
  await new Promise((resolve) => setImmediate(resolve));
  session.stop();
  await first;

  const second = await session.send('two');
  assert.deepStrictEqual(second, { ok: true, answer: 'the second answer' });
  assert.strictEqual(server.submitted.length, 2, 'the session must still be able to ask');
});

test('a stop with no remote turn running tells the server nothing', async () => {
  const server = fakeServer([{ body: answered('done') }]);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  session.stop();
  assert.deepStrictEqual(server.cancelled, [], 'nothing was running, so nothing should be cancelled');

  await session.send('one');
  session.stop();
  assert.deepStrictEqual(server.cancelled, [], 'the turn had finished; a late stop must be a no-op');
});

test('a stop while the question is still being submitted settles the turn at once', async () => {
  // The stop signal used to be armed only after `submit` returned an id, so a stop pressed while the
  // POST was in flight had no resolver to call — and a submit can take twenty seconds. (codex, the
  // code round, twice.)
  let releaseSubmit = (): void => undefined;
  const held = new Promise<void>((resolve) => {
    releaseSubmit = resolve;
  });
  const server = fakeServer([{ body: answered('too late') }], held);
  const session = new RemoteChatSession(server.transport, vendor, BUDGETS, AT_ONCE);

  const answering = session.send('explain this');
  await new Promise((resolve) => setImmediate(resolve));
  session.stop();

  const result = await answering;
  assert.strictEqual(result.ok, false, 'a stop during submission must end the turn, not wait it out');
  assert.strictEqual(result.ok === false ? result.stopped : undefined, true);

  // And when the submission finally lands, the job it created is cancelled rather than left running.
  releaseSubmit();
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepStrictEqual(
    server.cancelled,
    ['review-1'],
    'a job whose submission finished after the stop must still be dropped',
  );
});

test('a poll belonging to a stopped turn cannot come back to life during the next one', async () => {
  // The defect three reviewers found in the first version. It used a `stopped` flag that the NEXT
  // turn reset — so turn A's poll, sitting in an await when A was stopped, woke up after turn B had
  // cleared the flag, decided it was not stopped after all, and carried on polling A's cancelled job
  // and pushing A's queue positions into B's tab. A generation cannot be un-stopped.
  let releasePoll = (): void => undefined;
  const held = new Promise<void>((resolve) => {
    releasePoll = resolve;
  });
  let firstPoll = true;
  const positions: number[] = [];
  const server = fakeServer([{ body: answered('the second answer') }]);
  const racy: RemoteTransport = {
    ...server.transport,
    poll: async (id, waitSeconds) => {
      server.polls.push({ id, waitSeconds });
      if (firstPoll) {
        firstPoll = false;
        await held;

        return { body: { status: 'queued', position: 9 }, failure: '', status: 200 };
      }

      return { body: answered('the second answer'), failure: '', status: 200 };
    },
  };
  const session = new RemoteChatSession(racy, vendor, BUDGETS, AT_ONCE);

  const first = session.send('one', (position) => positions.push(position));
  await new Promise((resolve) => setImmediate(resolve));
  session.stop();
  await first;

  // Turn two starts and finishes while turn one's poll is still held.
  const second = await session.send('two');
  assert.deepStrictEqual(second, { ok: true, answer: 'the second answer' });

  // Now turn one's poll finally comes back. It must find itself stale and stop, not resume.
  const pollsBefore = server.polls.length;
  releasePoll();
  await new Promise((resolve) => setImmediate(resolve));
  await new Promise((resolve) => setImmediate(resolve));

  assert.deepStrictEqual(positions, [], 'a stopped turn must not push a queue position afterwards');
  assert.strictEqual(
    server.polls.length,
    pollsBefore,
    'the abandoned poll loop must not issue another poll once its turn is stale',
  );
});
