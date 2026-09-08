import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CliChatSession, Timers } from '../cliChatSession';
import { ProcessHandle } from '../processLauncher';
import { TurnBudgets } from '../chatSession';

/**
 * The four ways a long-lived vendor process ends, and the one way it answers.
 *
 * <p>Nothing here starts a real process. What is being asserted is the STATE MACHINE — what happens
 * when `init` never comes, when a turn goes unanswered, when the child dies mid-sentence — and every
 * one of those would otherwise need a real failure to reproduce, or a suite that waits three minutes
 * for a budget. The launcher itself is tested against real children in `processLauncher.test.ts`;
 * this file tests what is built on top of it.</p>
 *
 * <p><b>The fake replays, and that is not a convenience.</b> The first version of it appended
 * listeners and delivered nothing that had arrived before them — and every test in this file timed
 * out, because a session subscribes on a microtask while a real child can have spoken already. The
 * REAL launcher buffers until its first subscriber for exactly that reason (it was a finding on its
 * own code round). A fake weaker than the thing it stands for does not simplify a test; it tests a
 * program that does not exist.</p>
 */

const BUDGETS: TurnBudgets = { startupMs: 1000, turnMs: 2000 };

/** Let every queued microtask and immediate run, so the session gets as far as it can. */
const flush = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));

interface FakeChild {
  readonly handle: ProcessHandle;
  readonly written: readonly string[];
  say(line: string): void;
  exit(): void;
  fail(reason: string): void;
  killed(): number;
  setStderr(text: string): void;
}

/** A launcher's child that says what the test tells it to — with the real one's replay contract. */
function fakeChild(): FakeChild {
  const written: string[] = [];
  let lineListeners: ((line: string) => void)[] = [];
  const held: string[] = [];
  let opened = false;
  let exitListeners: ((code: number) => void)[] = [];
  let errorListeners: ((reason: string) => void)[] = [];
  let ended: { kind: 'exit'; code: number } | { kind: 'error'; reason: string } | undefined;
  let kills = 0;
  let stderr = '';
  let alive = true;

  const deliver = (line: string): void => {
    if (!opened) {
      held.push(line);

      return;
    }
    for (const listener of lineListeners) {
      listener(line);
    }
  };

  return {
    handle: {
      writeLine: (line: string) => {
        if (!alive) {
          return false;
        }
        written.push(line);

        return true;
      },
      onStdout: () => () => undefined,
      onLine: (listener) => {
        opened = true;
        lineListeners = [...lineListeners, listener];
        const pending = held.splice(0, held.length);
        for (const line of pending) {
          listener(line);
        }

        return () => {
          lineListeners = lineListeners.filter((known) => known !== listener);
        };
      },
      onExit: (listener) => {
        if (ended?.kind === 'exit') {
          listener(ended.code);

          return;
        }
        exitListeners = [...exitListeners, listener];
      },
      onError: (listener) => {
        if (ended?.kind === 'error') {
          listener(ended.reason);

          return;
        }
        errorListeners = [...errorListeners, listener];
      },
      stderrTail: () => stderr,
      kill: () => {
        kills += 1;
        alive = false;
      },
    },
    written,
    say: deliver,
    exit: () => {
      alive = false;
      ended = { kind: 'exit', code: 0 };
      for (const listener of exitListeners) {
        listener(0);
      }
    },
    fail: (reason) => {
      alive = false;
      ended = { kind: 'error', reason };
      for (const listener of errorListeners) {
        listener(reason);
      }
    },
    killed: () => kills,
    setStderr: (text) => {
      stderr = text;
    },
  };
}

/** Timers the test drives by hand: nothing here ever waits for a real millisecond. */
function fakeTimers(): Timers & { expire(): void } {
  let pending: (() => void)[] = [];

  return {
    after: (_ms, run) => {
      pending = [...pending, run];

      return () => {
        pending = pending.filter((known) => known !== run);
      };
    },
    expire: () => {
      const due = pending;
      pending = [];
      for (const run of due) {
        run();
      }
    },
  };
}

const INIT = JSON.stringify({ event: 'init', conversation_id: 'c1' });
const STEP = JSON.stringify({ event: 'step_update', step_update: { step_index: 0 } });
const ok = (answer: string): string =>
  JSON.stringify({ event: 'result', result: { status: 'SUCCESS', response: answer } });

/**
 * Start a turn, let the session get as far as writing it, and hand the promise back UNAWAITED.
 *
 * <p>Wrapped in an object on purpose. An `async` function that returns a promise ADOPTS it, so a
 * helper declared `Promise<Promise<T>>` silently waits for the turn it was supposed to hand over -
 * and every test here deadlocked on an answer that could not be said until the helper returned. The
 * wrapper is what stops the flattening.</p>
 */
async function asking(
  session: CliChatSession,
  child: FakeChild,
  text: string,
): Promise<{ answering: Promise<unknown> }> {
  const answering = session.send(text);
  child.say(INIT);
  await flush();

  return { answering };
}

test('a turn is exactly one NDJSON line, in the schema the CLI actually accepts', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const { answering } = await asking(session, child, 'why is it pinned?');
  child.say(STEP);
  child.say(ok('because a worktree is cheap'));

  assert.deepStrictEqual(await answering, { ok: true, answer: 'because a worktree is cheap' });
  assert.strictEqual(child.written.length, 1, 'a turn wrote more than one line');
  // The shape came out of the binary's own refusal: `stream input message is missing the "event"
  // field`. `type:` was the first guess and it was wrong.
  assert.deepStrictEqual(JSON.parse(child.written[0]!), {
    event: 'user',
    message: { role: 'user', content: 'why is it pinned?' },
  });
});

test('a second turn is answered by the same process', async () => {
  // The regression test a REJECTED finding earned. A reviewer called `--mode plan` Blocking on the
  // claim that a planning mode is a single-prompt batch which exits after one turn; three measured
  // turns down one pipe said otherwise. A CLI upgrade could still make the reviewer right, and this
  // is what would notice.
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const { answering: first } = await asking(session, child, 'one');
  child.say(ok('answer one'));
  await first;

  const second = session.send('two');
  await flush();
  child.say(ok('answer two'));

  assert.deepStrictEqual(await second, { ok: true, answer: 'answer two' });
  assert.strictEqual(child.written.length, 2);
  assert.strictEqual(session.running, true, 'the process did not survive its first turn');
});

test('one launch, however many turns', async () => {
  let launches = 0;
  const child = fakeChild();
  const session = new CliChatSession(
    () => {
      launches += 1;

      return child.handle;
    },
    BUDGETS,
    fakeTimers(),
  );

  const { answering: first } = await asking(session, child, 'one');
  child.say(ok('a'));
  await first;
  const second = session.send('two');
  await flush();
  child.say(ok('b'));
  await second;

  assert.strictEqual(launches, 1, 'the startup cost was paid twice');
});

test('a turn sent while one is in flight waits, and never interleaves', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const { answering: first } = await asking(session, child, 'one');
  const second = session.send('two');
  await flush();

  // Nothing of the second turn may be on the wire while the first is unanswered.
  assert.strictEqual(child.written.length, 1, 'two turns went down one pipe at once');

  child.say(ok('answer one'));
  assert.deepStrictEqual(await first, { ok: true, answer: 'answer one' });

  await flush();
  assert.strictEqual(child.written.length, 2, 'the queued turn never went out');
  child.say(ok('answer two'));
  assert.deepStrictEqual(await second, { ok: true, answer: 'answer two' });
});

test('an ERROR result is an error, not an empty answer', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const { answering } = await asking(session, child, 'hello');
  child.say(JSON.stringify({ event: 'result', result: { status: 'ERROR', response: '', error: 'the model refused' } }));

  // A page showing nothing would look like a model with nothing to say.
  assert.deepStrictEqual(await answering, { ok: false, failure: 'the model refused' });
});

test('a line that is not JSON is skipped rather than fatal', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  child.say('a CLI may log wherever it pleases');
  const { answering } = await asking(session, child, 'hello');
  child.say('[warn] and it does');
  child.say(ok('still fine'));

  assert.deepStrictEqual(await answering, { ok: true, answer: 'still fine' });
});

test('a process that never says init fails with its own sentence, and is killed', async () => {
  const child = fakeChild();
  const timers = fakeTimers();
  const session = new CliChatSession(() => child.handle, BUDGETS, timers);

  const answering = session.send('hello');
  await flush();
  timers.expire();

  const result = await answering as { ok: boolean; failure?: string };
  assert.strictEqual(result.ok, false);
  // "It would not start" and "it will not answer" send a person to different places.
  assert.match(result.failure ?? '', /did not start/);
  assert.strictEqual(child.killed(), 1, 'a process that would not start was left running');
});

test('a turn that is never answered fails at its budget, and the process is killed', async () => {
  const child = fakeChild();
  const timers = fakeTimers();
  const session = new CliChatSession(() => child.handle, BUDGETS, timers);

  const { answering } = await asking(session, child, 'hello');
  timers.expire();

  const result = await answering as { ok: boolean; failure?: string };
  assert.strictEqual(result.ok, false);
  assert.match(result.failure ?? '', /did not answer in time/);
  assert.strictEqual(child.killed(), 1, 'a process that stopped answering was handed the next turn');
  assert.strictEqual(session.running, false);
});

test('a process that exits mid-turn fails that turn and says what it left behind', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const { answering } = await asking(session, child, 'hello');
  child.setStderr('out of tokens');
  child.exit();

  const result = await answering as { ok: boolean; failure?: string };
  assert.strictEqual(result.ok, false);
  assert.match(result.failure ?? '', /out of tokens/);
});

test('the next turn after a death starts a new process rather than writing into a closed pipe', async () => {
  let launches = 0;
  let current = fakeChild();
  const session = new CliChatSession(
    () => {
      launches += 1;

      return current.handle;
    },
    BUDGETS,
    fakeTimers(),
  );

  const { answering: first } = await asking(session, current, 'one');
  current.exit();
  await first;

  current = fakeChild();
  const second = session.send('two');
  current.say(INIT);
  await flush();
  current.say(ok('back again'));

  assert.deepStrictEqual(await second, { ok: true, answer: 'back again' });
  assert.strictEqual(launches, 2, 'the dead process was asked a second question');
});

test('a launch that fails at all is a sentence, not a rejection', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const answering = session.send('hello');
  await flush();
  child.fail('spawn agy ENOENT');

  const result = await answering as { ok: boolean; failure?: string };
  assert.strictEqual(result.ok, false);
  assert.match(result.failure ?? '', /ENOENT/);
});

test('disposal kills the process and ends the turn that was waiting', async () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  const { answering } = await asking(session, child, 'hello');
  session.dispose();

  const result = await answering as { ok: boolean; failure?: string };
  assert.strictEqual(result.ok, false);
  assert.match(result.failure ?? '', /closed/);
  assert.strictEqual(child.killed(), 1, 'a vendor process outlived its tab');
});

test('a disposed conversation refuses politely rather than starting a process', async () => {
  let launches = 0;
  const child = fakeChild();
  const session = new CliChatSession(
    () => {
      launches += 1;

      return child.handle;
    },
    BUDGETS,
    fakeTimers(),
  );
  session.dispose();

  const result = await session.send('anybody there?');

  assert.strictEqual(result.ok, false);
  assert.strictEqual(launches, 0, 'a closed conversation started a vendor process');
});

test('disposing twice is not an error', () => {
  const child = fakeChild();
  const session = new CliChatSession(() => child.handle, BUDGETS, fakeTimers());

  session.dispose();

  assert.doesNotThrow(() => session.dispose());
});
