import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CliChatSession, Timers } from '../cliChatSession';
import { ProcessHandle } from '../processLauncher';
import { TurnBudgets } from '../chatSession';
import { codexAdapter } from '../codexAdapter';
import { ChatAdapter } from '../chatAdapter';

/**
 * A conversation held by a vendor that keeps no process.
 *
 * <p>`codex` answers one question per process: the prompt goes in on stdin, the stream is closed,
 * the answer arrives, the process exits. The conversation is not in the pipe — it is in the
 * vendor's own store, and the next turn resumes it by id.</p>
 *
 * <p><b>Which inverts the rule this session was built on.</b> For `agy` and `claude`, a child that
 * exits has taken the conversation with it and the next answer must say so. For `codex`, a child
 * that exits has finished a turn, and saying so under every single answer would be a lie repeated
 * once per question. That inversion is the most likely place for the adapter seam to go wrong,
 * which is why it has a file of its own.</p>
 */

const BUDGETS: TurnBudgets = { startupMs: 30_000, turnMs: 180_000 };

/**
 * Let the queue start the turn.
 *
 * <p>`send` chains onto the previous turn's settlement, so the process is launched a microtask
 * later — the same reason `cliChatSession.test.ts` has a `flush` of its own. A test that pokes the
 * child before that tick is poking a child that does not exist yet.</p>
 */
const flush = (): Promise<void> => new Promise((resolve) => setImmediate(resolve));

/** Timers that never fire: these tests are about what the process says, not about running out. */
const NEVER: Timers = { after: () => () => undefined };

interface FakeTurn {
  readonly handle: ProcessHandle;
  readonly resume: string;
  readonly prompt: string;
  say(line: string): void;
  exit(): void;
}

/**
 * One process per turn, recorded.
 *
 * <p>The real launcher replays what a child said before anybody subscribed, and so does this: a
 * per-turn child can print its whole answer and exit before the session's next line runs.</p>
 */
function perTurnLauncher(): { start: (resume: string) => ProcessHandle; turns: FakeTurn[] } {
  const turns: FakeTurn[] = [];

  const start = (resume: string): ProcessHandle => {
    let lines: ((line: string) => void)[] = [];
    let exits: (() => void)[] = [];
    const held: string[] = [];
    let opened = false;
    let exited = false;
    const turn: { resume: string; prompt: string } = { resume, prompt: '' };

    const handle: ProcessHandle = {
      // Both, because this launcher drives both shapes: a per-turn child is written to and CLOSED,
      // a persistent one is written a line at a time down a pipe that stays open.
      writeLine: (line: string) => {
        turn.prompt = line;

        return true;
      },
      writeAndEnd: (text: string) => {
        turn.prompt = text;

        return true;
      },
      onStdout: () => () => undefined,
      onLine: (listener) => {
        opened = true;
        lines = [...lines, listener];
        for (const line of held.splice(0, held.length)) {
          listener(line);
        }

        return () => {
          lines = lines.filter((known) => known !== listener);
        };
      },
      onExit: (listener) => {
        if (exited) {
          listener(0);

          return;
        }
        exits = [...exits, () => listener(0)];
      },
      onError: () => undefined,
      stderrTail: () => '',
      kill: () => undefined,
    };

    turns.push({
      handle,
      get resume() {
        return turn.resume;
      },
      get prompt() {
        return turn.prompt;
      },
      say: (line: string) => {
        if (opened) {
          for (const listener of lines) {
            listener(line);
          }
        } else {
          held.push(line);
        }
      },
      exit: () => {
        exited = true;
        for (const fire of exits.splice(0, exits.length)) {
          fire();
        }
      },
    });

    return handle;
  };

  return { start, turns };
}

const started = (id: string): string => JSON.stringify({ type: 'thread.started', thread_id: id });
const answered = (said: string): string =>
  JSON.stringify({ type: 'item.completed', item: { id: 'item_0', type: 'agent_message', text: said } });

test('the first turn opens a thread and the second RESUMES the one it was told', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  launcher.turns[0]!.say(answered('it means this'));
  launcher.turns[0]!.exit();
  assert.deepStrictEqual(await first, { ok: true, answer: 'it means this' });

  const second = session.send('and in the north?');
  await flush();
  launcher.turns[1]!.say(answered('there it is different'));
  launcher.turns[1]!.exit();
  assert.deepStrictEqual(await second, { ok: true, answer: 'there it is different' });

  assert.strictEqual(launcher.turns[0]!.resume, '', 'the first turn tried to resume something');
  assert.strictEqual(
    launcher.turns[1]!.resume,
    '01a0851c-488c-7be0-9e51-e35c5fb2ce5c',
    'the second turn did not carry the thread the first was given',
  );
});

test('an exit between turns is NOT a lost conversation, and nothing says it was', async () => {
  // The whole inversion, in one assertion. For a persistent vendor this exact sequence — a process
  // that ends and a new one for the next question — sets `contextLost` and prints a sentence under
  // the answer. Here the vendor kept the conversation, and the sentence would be a lie.
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('first');
  await flush();
  launcher.turns[0]!.say(started('thread-1'));
  launcher.turns[0]!.say(answered('one'));
  launcher.turns[0]!.exit();
  await first;

  const second = session.send('second');
  await flush();
  launcher.turns[1]!.say(answered('two'));
  launcher.turns[1]!.exit();
  const result = await second;

  assert.ok(result.ok);
  assert.strictEqual(result.contextLost, undefined, 'a finished turn was reported as a lost conversation');
});

test('the prompt travels whole on stdin, in no envelope at all', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);
  const passage = `explain this\n\n--- the text ---\n${'о'.repeat(5_000)}`;

  const turn = session.send(passage);
  await flush();
  launcher.turns[0]!.say(answered('done'));
  launcher.turns[0]!.exit();
  await turn;

  assert.strictEqual(launcher.turns[0]!.prompt, passage, 'the prompt was wrapped, truncated or lost');
});

test('a turn that ends without saying anything is a failure, not an empty answer', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const turn = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.exit();

  const result = await turn;
  assert.ok(!result.ok);
  assert.match(result.failure, /ended unexpectedly/);
});

test('a failure event wins over an answer that never came', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const turn = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.say(JSON.stringify({ type: 'turn.failed', message: 'the model gave up' }));
  launcher.turns[0]!.exit();

  const result = await turn;
  assert.ok(!result.ok);
  assert.strictEqual(result.failure, 'the model gave up');
});

test('nothing waits for a ready event, because a per-turn vendor never sends one', async () => {
  // A persistent session waits out its whole startup budget for `init`. This one must not: there is
  // no init, and the budget would fire on every question. The timers here never fire, so a session
  // that waited for anything would simply never resolve — which is what this asserts.
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const turn = session.send('go');
  await flush();
  launcher.turns[0]!.say(answered('at once'));
  launcher.turns[0]!.exit();

  assert.deepStrictEqual(await turn, { ok: true, answer: 'at once' });
});

test('two questions asked at once are still two turns, one after the other', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('one');
  const second = session.send('two');
  await flush();
  assert.strictEqual(launcher.turns.length, 1, 'the second turn started before the first had finished');

  launcher.turns[0]!.say(started('thread-1'));
  launcher.turns[0]!.say(answered('first answer'));
  launcher.turns[0]!.exit();
  await first;
  await flush();

  launcher.turns[1]!.say(answered('second answer'));
  launcher.turns[1]!.exit();
  await second;

  assert.strictEqual(launcher.turns.length, 2);
  assert.strictEqual(launcher.turns[1]!.resume, 'thread-1', 'the queued turn lost the thread');
});

/**
 * The other shape difference, and the one no unit test would have found.
 *
 * <p>`agy` prints `init` the moment it starts. `claude` prints NOTHING until a turn arrives — given
 * an empty stdin it exits without a word. Both speak NDJSON down a pipe and differ only in who
 * speaks first, so a session that waits for readiness before sending anything waits out its whole
 * startup budget against `claude` and then reports a CLI that never started, for a CLI that was
 * working perfectly and had not been spoken to. Measured by the live check; pinned here.
 */

const SILENT_UNTIL_ASKED: ChatAdapter = {
  shape: 'persistent',
  announces: false,
  argv: () => [],
  encode: (turn) => turn,
  classify: (line) => (line.length > 0 ? { kind: 'answer', text: line } : { kind: 'nothing' }),
};

test('a vendor that announces nothing is asked at once, not waited out', async () => {
  // The timers here NEVER fire, so a session that waited for a ready event would never resolve —
  // which is exactly what a sixty-second startup budget looked like against the real CLI.
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, SILENT_UNTIL_ASKED);

  const turn = session.send('go');
  await flush();
  launcher.turns[0]!.say('the answer, with no init before it');

  assert.deepStrictEqual(await turn, { ok: true, answer: 'the answer, with no init before it' });
});
