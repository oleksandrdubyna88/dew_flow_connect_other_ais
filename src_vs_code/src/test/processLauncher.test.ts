import assert from 'node:assert/strict';
import { test } from 'node:test';
import { tmpdir } from 'node:os';
import { ProcessHandle, launch, workingDirectory } from '../processLauncher';

/**
 * The launcher, against REAL children.
 *
 * <p>A fake would pass every one of these and prove nothing: what is being asserted is how node's
 * own streams behave — a line arriving in two chunks, a `close` that carries the last unterminated
 * line, a spawn that fails asynchronously with `ENOENT`. Those are the behaviours the old inline
 * spawn was written against, and a stub would encode today's belief about them rather than the
 * fact.</p>
 *
 * <p>Children are `process.execPath -e …`: node is certainly present, since it is running this.</p>
 */

const NODE = process.execPath;

/** Wait for a handle to end, however it ends. Rejects on a timeout so a hang names itself. */
function ended(child: ProcessHandle, whatFor: string, ms = 10_000): Promise<{ code?: number; error?: string }> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`${whatFor}: nothing arrived within ${ms} ms`)), ms);
    child.onExit((code) => {
      clearTimeout(timer);
      resolve({ code });
    });
    child.onError((error) => {
      clearTimeout(timer);
      resolve({ error });
    });
  });
}

test('a line split across two chunks arrives whole, and once', async () => {
  const child = launch(NODE, [
    '-e',
    'process.stdout.write("first half, "); setTimeout(() => process.stdout.write("second half\\n"), 60);',
  ]);
  const lines: string[] = [];
  child.onLine((line) => lines.push(line));

  await ended(child, 'the split-line child');

  assert.deepStrictEqual(lines, ['first half, second half']);
});

test('a last line with no newline is still delivered, on close', async () => {
  const child = launch(NODE, ['-e', 'process.stdout.write("no terminator")']);
  const lines: string[] = [];
  child.onLine((line) => lines.push(line));

  await ended(child, 'the unterminated child');

  assert.deepStrictEqual(lines, ['no terminator']);
});

test('several lines in one chunk are delivered separately', async () => {
  const child = launch(NODE, ['-e', 'process.stdout.write("one\\ntwo\\nthree\\n")']);
  const lines: string[] = [];
  child.onLine((line) => lines.push(line));

  await ended(child, 'the three-line child');

  assert.deepStrictEqual(lines, ['one', 'two', 'three']);
});

test('the exit code is reported', async () => {
  const child = launch(NODE, ['-e', 'process.exit(3)']);

  const end = await ended(child, 'the exiting child');

  assert.strictEqual(end.code, 3);
  assert.strictEqual(end.error, undefined);
});

test('a child answers what is written to its stdin, line by line', async () => {
  const child = launch(NODE, [
    '-e',
    'let buf = ""; process.stdin.on("data", (d) => { buf += d; const parts = buf.split("\\n"); buf = parts.pop();'
      + ' for (const p of parts) process.stdout.write("echo:" + p + "\\n"); });',
  ]);
  const lines: string[] = [];
  child.onLine((line) => lines.push(line));

  assert.strictEqual(child.writeLine('hello'), true, 'the pipe refused a write to a live child');
  await new Promise((r) => setTimeout(r, 300));
  child.kill();
  await ended(child, 'the echo child');

  assert.deepStrictEqual(lines, ['echo:hello']);
});

test('a child that would never exit is gone after kill', async () => {
  const child = launch(NODE, ['-e', 'setInterval(() => {}, 1000)']);

  child.kill();
  const end = await ended(child, 'the killed child');

  // Killed children have no code of their own; what matters is that the end arrived at all — the
  // process is not still sitting there holding a vendor session open.
  assert.ok(end.code !== undefined || end.error !== undefined, 'the child never ended');
});

test('an unlaunchable target is an error state, never a throw', async () => {
  const child = launch('coai-no-such-binary-9f3a', ['--version']);

  const end = await ended(child, 'the missing binary');

  assert.ok(end.error !== undefined, `expected an error state, got exit ${String(end.code)}`);
  assert.ok(end.error.length > 0, 'the error state carries no reason');
});

test('a listener registered after the event still hears it', async () => {
  const child = launch('coai-no-such-binary-9f3a', []);
  await ended(child, 'the missing binary');

  // The whole point: a caller that subscribes on the next line must not miss what already happened.
  const late = await new Promise<string>((resolve) => child.onError(resolve));

  assert.ok(late.length > 0, 'a late subscriber heard nothing');
});

test('the stderr tail is capped rather than grown for ever', async () => {
  const child = launch(NODE, ['-e', 'process.stderr.write("x".repeat(40000))']);

  await ended(child, 'the noisy child');

  const tail = child.stderrTail();
  assert.ok(tail.length > 0, 'nothing was kept');
  assert.ok(tail.length <= 8000, `the tail grew to ${tail.length} chars`);
});

test('writing to a child that has gone is refused rather than thrown', async () => {
  const child = launch(NODE, ['-e', 'process.exit(0)']);
  await ended(child, 'the short-lived child');

  assert.strictEqual(child.writeLine('anybody there?'), false);
});

/* ------------------------------------------------------------------------------------------------
 * What the code round of 2026-09-08 added. Each test below is one accepted finding, and each one
 * fails against the launcher as it was first written — which is the only reason to keep them.
 * ---------------------------------------------------------------------------------------------- */

test('a late onLine subscriber is replayed the lines that already arrived', async () => {
  // A child can print its whole answer and close before the caller's next statement runs. The first
  // version appended the listener and lost the answer for ever. (codex and gemini, independently.)
  const child = launch(NODE, ['-e', 'process.stdout.write("answer\\n")']);
  await ended(child, 'the fast child');

  const seen: string[] = [];
  child.onLine((line) => seen.push(line));

  assert.deepStrictEqual(seen, ['answer'], 'the line that arrived before the subscriber was lost');
});

test('unsubscribing stops delivery, so a per-turn listener does not outlive its turn', async () => {
  const child = launch(NODE, [
    '-e',
    'process.stdout.write("one\\n"); setTimeout(() => process.stdout.write("two\\n"), 120);',
  ]);
  const seen: string[] = [];
  const stop = child.onLine((line) => {
    seen.push(line);
    stop();
  });

  await ended(child, 'the two-line child');

  // Without an unsubscribe, a chat session that registers per turn delivers turn 100's answer to
  // ninety-nine stale handlers. (codex and gemini, independently.)
  assert.deepStrictEqual(seen, ['one'], 'the listener kept receiving after it unsubscribed');
});

test('a multi-byte character split across two chunks survives', async () => {
  // The passages this launcher exists to carry are routinely Russian. Decoding each chunk on its own
  // turns a Cyrillic character straddling the boundary into two replacement characters, silently.
  // (gemini, the code round.)
  const child = launch(NODE, [
    '-e',
    'const b = Buffer.from("Проверяющие\\n", "utf8");'
      + ' process.stdout.write(b.subarray(0, 5)); setTimeout(() => process.stdout.write(b.subarray(5)), 80);',
  ]);
  const seen: string[] = [];
  child.onLine((line) => seen.push(line));

  await ended(child, 'the Cyrillic child');

  assert.deepStrictEqual(seen, ['Проверяющие']);
  assert.ok(!seen.join('').includes('\uFFFD'), 'the text came back with replacement characters in it');
});

test('writing to a child whose stdin has gone does not take the host down', async () => {
  // EPIPE arrives asynchronously, so no try/catch around `write` can see it; unhandled, node makes
  // it fatal and the extension host dies. The guard is an error listener on stdin. (gemini, Blocking.)
  const child = launch(NODE, ['-e', 'process.stdin.destroy(); setTimeout(() => {}, 400);']);
  await new Promise((r) => setTimeout(r, 150));

  for (let at = 0; at < 50; at += 1) {
    child.writeLine('are you there');
  }
  child.kill();
  await ended(child, 'the deaf child');

  // Reaching this line at all is the assertion: an unhandled EPIPE would have killed the runner.
  assert.ok(true);
});

test('a child that never started still says why', async () => {
  const child = launch('coai-no-such-binary-9f3a', []);
  await ended(child, 'the missing binary');

  const why = child.stderrTail();

  // An empty tail leaves somebody guessing between a missing binary, a denied permission and a
  // rejected argument — three different things to do next. (local and gemini.)
  assert.ok(why.length > 0, 'the failure carries no reason at all');
});

test('a shell is never pointed at the opened workspace by omission', () => {
  const workspace = process.cwd();

  assert.strictEqual(workingDirectory(true, undefined), tmpdir(), 'a shell inherited the workspace');
  assert.notStrictEqual(workingDirectory(true, undefined), workspace);
  assert.strictEqual(workingDirectory(true, 'C:/chosen'), 'C:/chosen', 'an explicit choice was overridden');
  assert.strictEqual(workingDirectory(false, undefined), undefined, 'a non-shell child was moved for no reason');
});
