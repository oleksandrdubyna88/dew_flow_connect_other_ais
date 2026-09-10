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
  exitWith(code: number): void;
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
    let exits: ((code: number) => void)[] = [];
    let exitCode = 0;
    const held: string[] = [];
    let opened = false;
    let exited = false;
    const turn: { resume: string; prompt: string } = { resume, prompt: '' };

    const handle: ProcessHandle = {
      pid: 4242,
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
          listener(exitCode);

          return;
        }
        exits = [...exits, (code: number) => listener(code)];
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
          fire(0);
        }
      },
      exitWith: (code: number) => {
        exited = true;
        exitCode = code;
        for (const fire of exits.splice(0, exits.length)) {
          fire(code);
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
/** A real `turn.completed` block, whose counts are CUMULATIVE for the thread. */
const spent = (tokensIn: number, tokensOut: number): string =>
  JSON.stringify({ type: 'turn.completed', usage: { input_tokens: tokensIn, output_tokens: tokensOut } });

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
  assert.match(result.failure, /ended without answering/);
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
  cumulative: false,
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

test('a thread that was never named IS a lost conversation, and the next answer says so', async () => {
  // The inversion is not "never lost": it is "lost only when the THREAD is". A first turn that dies
  // before `thread.started` leaves nothing to resume, so the next question opens a brand new
  // conversation — and saying nothing about that is exactly the silence the persistent rule exists
  // to prevent. (gemini, the plan round.)
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('first');
  await flush();
  launcher.turns[0]!.exit();
  assert.ok(!(await first).ok, 'a turn that said nothing was reported as an answer');

  const second = session.send('second');
  await flush();
  launcher.turns[1]!.say(started('thread-2'));
  launcher.turns[1]!.say(answered('a fresh start'));
  launcher.turns[1]!.exit();

  const result = await second;
  assert.ok(result.ok);
  assert.strictEqual(result.contextLost, true, 'a conversation that started again did not say so');
  assert.strictEqual(launcher.turns[1]!.resume, '', 'it tried to resume a thread nobody named');
});

test('a thread that WAS named survives a failed turn, and the next one resumes it', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('first');
  await flush();
  launcher.turns[0]!.say(started('thread-1'));
  launcher.turns[0]!.say(JSON.stringify({ type: 'turn.failed', message: 'the model gave up' }));
  launcher.turns[0]!.exit();
  assert.ok(!(await first).ok);

  const second = session.send('second');
  await flush();
  launcher.turns[1]!.say(answered('carrying on'));
  launcher.turns[1]!.exit();

  const result = await second;
  assert.ok(result.ok);
  assert.strictEqual(result.contextLost, undefined, 'a thread that was still there was called lost');
  assert.strictEqual(launcher.turns[1]!.resume, 'thread-1');
});

test('a process that dies with a code and no answer says which code', async () => {
  // "ended unexpectedly" is true and unhelpful. A non-zero exit is the one fact the operating
  // system gives away for free, and it is the difference between "it crashed" and "it refused".
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const turn = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.exitWith(9);

  const result = await turn;
  assert.ok(!result.ok);
  assert.match(result.failure, /9/, `the exit code is nowhere in: ${result.failure}`);
});

test('a thread that cannot be resumed is dropped, rather than failing every turn after it', async () => {
  // A stored session can go: the vendor prunes it, a machine is re-imaged, a version changes its
  // format. Retrying the same dead id forever turns one bad turn into a conversation that can never
  // answer again. One turn is lost, the thread with it, and the next question starts a new one and
  // says so. (gemini, the code round.)
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('first');
  await flush();
  launcher.turns[0]!.say(started('thread-1'));
  launcher.turns[0]!.say(answered('one'));
  launcher.turns[0]!.exit();
  await first;

  // The resume fails: no thread, no answer, a non-zero exit — the shape of a session that is gone.
  const second = session.send('second');
  await flush();
  assert.strictEqual(launcher.turns[1]!.resume, 'thread-1', 'it did not even try to resume');
  launcher.turns[1]!.exitWith(1);
  assert.ok(!(await second).ok);

  const third = session.send('third');
  await flush();
  assert.strictEqual(launcher.turns[2]!.resume, '', 'it tried the dead thread again');
  launcher.turns[2]!.say(started('thread-2'));
  launcher.turns[2]!.say(answered('a fresh start'));
  launcher.turns[2]!.exit();

  const result = await third;
  assert.ok(result.ok);
  assert.strictEqual(result.contextLost, true, 'the new conversation did not say it was new');
});

test('stopping a per-turn turn keeps the thread, so the next question resumes the same conversation', async () => {
  // The inversion, at the one moment it is most likely to be got wrong. For `claude` and `agy` a
  // stop kills the conversation and the caller must carry the transcript; here the conversation is
  // in the vendor's own store, the thread id survives the killed process, and carrying anything
  // would re-send a conversation the model already has. A reviewer asked for this to be PROVEN
  // rather than asserted in a comment. (codex, the plan round.)
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  launcher.turns[0]!.say(answered('it means this'));
  launcher.turns[0]!.exit();
  await first;

  const stopped = session.send('and in the north?');
  await flush();
  launcher.turns[1]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  session.stop();

  const result = await stopped;
  assert.strictEqual(result.ok, false, 'a stopped turn is not an answer');
  assert.strictEqual(result.ok === false ? result.stopped : undefined, true);
  assert.strictEqual(
    result.ok === false ? result.contextLost : undefined,
    undefined,
    'a per-turn vendor keeps the conversation in its own store; a stop loses nothing to report',
  );

  const third = session.send('try again');
  await flush();
  assert.strictEqual(
    launcher.turns[2]!.resume,
    '01a0851c-488c-7be0-9e51-e35c5fb2ce5c',
    'the turn after a stop must resume the SAME thread, not open a new conversation',
  );
  launcher.turns[2]!.say(answered('there it is different'));
  launcher.turns[2]!.exit();
  assert.deepStrictEqual(await third, { ok: true, answer: 'there it is different' });
});


test('a cumulative vendor is differenced BY THE SESSION, so a result is always one turn’s cost', async () => {
  // `codex` counts up across the thread: 1000, then 1200. Recording what it says would bill the
  // second turn for the first as well, and every conversation on that vendor would inflate the
  // longer it ran. The baseline lives HERE because a session’s life is exactly the vendor
  // thread’s life — it is minted with the id this session resumes by and dies with it.
  // (gemini, the code round: the rule used to reach out of the adapter layer into the command that
  // orchestrates the page.)
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  launcher.turns[0]!.say(answered('it means this'));
  launcher.turns[0]!.say(spent(1000, 200));
  launcher.turns[0]!.exit();
  assert.deepStrictEqual(
    await first,
    { ok: true, answer: 'it means this', usage: { tokensIn: 1000, tokensOut: 200, costUsd: null } },
  );

  const second = session.send('and in the north?');
  await flush();
  launcher.turns[1]!.say(answered('there it is different'));
  launcher.turns[1]!.say(spent(1200, 300));
  launcher.turns[1]!.exit();
  assert.deepStrictEqual(
    await second,
    { ok: true, answer: 'there it is different', usage: { tokensIn: 200, tokensOut: 100, costUsd: null } },
    'the second turn was billed for the first as well',
  );
});

test('a STOPPED turn carries what the vendor had already charged for it', async () => {
  // The accounting hole four reviewers across both remote vendors raised on the code round: `codex`
  // emits its usage on a line of its own, so a turn can be priced and then stopped a moment later.
  // Carrying the numbers only on the answer recorded such a turn as zero tokens at no cost — and
  // a stopped turn is precisely the one somebody hunting for waste is looking for.
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const stopped = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  launcher.turns[0]!.say(spent(800, 40));
  session.stop();

  const result = await stopped;
  assert.strictEqual(result.ok, false, 'a stopped turn is not an answer');
  assert.deepStrictEqual(
    result.ok === false ? result.usage : undefined,
    { tokensIn: 800, tokensOut: 40, costUsd: null },
    'the tokens the vendor had already charged for were thrown away',
  );
});

test('a turn stopped after a priced one is differenced against it, not against nothing', async () => {
  // The compounding half of the same defect: if a stopped turn moved no baseline, the NEXT turn
  // would be differenced against the turn before the stop and would absorb the stopped turn’s
  // tokens on top of its own. Because the stop settles through the same funnel as an answer, the
  // baseline moves and each turn is billed once. (gemini, the code round.)
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const first = session.send('one');
  await flush();
  launcher.turns[0]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  launcher.turns[0]!.say(answered('first'));
  launcher.turns[0]!.say(spent(1000, 100));
  launcher.turns[0]!.exit();
  await first;

  const stopped = session.send('two');
  await flush();
  launcher.turns[1]!.say(spent(1500, 150));
  session.stop();
  const second = await stopped;
  assert.deepStrictEqual(
    second.ok === false ? second.usage : undefined,
    { tokensIn: 500, tokensOut: 50, costUsd: null },
  );

  const third = session.send('three');
  await flush();
  launcher.turns[2]!.say(answered('third'));
  launcher.turns[2]!.say(spent(1800, 180));
  launcher.turns[2]!.exit();
  assert.deepStrictEqual(
    await third,
    { ok: true, answer: 'third', usage: { tokensIn: 300, tokensOut: 30, costUsd: null } },
    'the turn after a stop absorbed the stopped turn’s tokens',
  );
});

test('a turn nobody priced carries NO usage key, so an old result looks exactly as it did', async () => {
  const launcher = perTurnLauncher();
  const session = new CliChatSession(launcher.start, BUDGETS, NEVER, codexAdapter);

  const only = session.send('what does this mean?');
  await flush();
  launcher.turns[0]!.say(started('01a0851c-488c-7be0-9e51-e35c5fb2ce5c'));
  launcher.turns[0]!.say(answered('it means this'));
  launcher.turns[0]!.exit();

  const result = await only;
  assert.deepStrictEqual(result, { ok: true, answer: 'it means this' });
  assert.ok(!Object.keys(result).includes('usage'), 'an unpriced turn grew a key it never had');
});
