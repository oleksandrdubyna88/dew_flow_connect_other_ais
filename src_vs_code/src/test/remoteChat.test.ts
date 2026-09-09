import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  CHAT_ROLE,
  REMOTE_TURNS,
  acceptedId,
  backoffMs,
  readStep,
  remoteIsFull,
  requestBody,
  waitSecondsFor,
} from '../remoteAsk';
import { RemoteChatSession, RemoteTransport } from '../remoteChatSession';
import { REQUEST_TIMEOUT_MS } from '../teamServerApi';
import { Timers } from '../cliChatSession';
import { TurnBudgets } from '../chatSession';

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
function fakeServer(replies: readonly { body?: unknown; failure?: string; status?: number }[]): {
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

test('a finished review is its answer; a finished review with nothing in it is a failure', () => {
  assert.deepStrictEqual(readStep(answered('  it means this  ')), {
    kind: 'answer',
    text: 'it means this',
    tokensIn: 10,
    tokensOut: 3,
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
