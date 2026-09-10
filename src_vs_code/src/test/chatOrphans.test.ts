import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ProcEntry, endIfOursPosix } from '../chatOrphans';
import { ChildRecord } from '../chatLedger';

/**
 * Ending a vendor CLI a force-kill left behind, on the side that is not Windows.
 *
 * <p>The Windows half asks its three facts and kills inside one PowerShell command and is not tested,
 * because faking a PowerShell round trip costs more than it proves. This half had no such excuse: it
 * reads two files and sends one signal, so both are injected and every branch is a test — including
 * the ones a real machine would only produce by accident, like a pid that dies between the read and
 * the signal.</p>
 *
 * <p>`'unknown'` is the outcome that KEEPS a row and asks again. Everything here turns on which
 * failures are allowed to be it: too many and an orphan is immortal, too few and a row is struck out
 * over a process still running.</p>
 */

const RECORD: ChildRecord = { pid: 4242, image: 'codex', startedMs: 1_789_000_000_000 };

const found = (names: readonly string[], startedMs = RECORD.startedMs): ProcEntry =>
  ({ kind: 'found', names, startedMs });

/** A `process.kill` that refuses the way the operating system would. */
function refuses(code: string): (pid: number) => void {
  return () => {
    const error = new Error(code) as NodeJS.ErrnoException;
    error.code = code;
    throw error;
  };
}

test('a Linux child that still matches is ended', async () => {
  const killed: number[] = [];

  const outcome = await endIfOursPosix(RECORD, async () => found(['codex']), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'killed');
  assert.deepStrictEqual(killed, [4242]);
});

test('a shebang CLI whose argv[0] is the interpreter is still recognised by its own name', async () => {
  // Measured on the operator's machine: codex and gemini in WSL are `#!/bin/sh` scripts in the
  // WINDOWS npm directory, so the kernel runs the interpreter and argv[0] is /bin/sh. A check that
  // reads argv[0] — or `comm`, which is `sh` here — answers "not ours" and keeps the row for ever.
  const killed: number[] = [];

  const outcome = await endIfOursPosix(RECORD, async () => found(['sh', 'sh', 'codex']), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'killed');
  assert.deepStrictEqual(killed, [4242]);
});

test('a stranger that merely mentions a vendor’s name in its arguments is never ours', async () => {
  // The looser rule this plan started with — "any cmdline entry" — would have killed this. Two
  // reviewers refused it independently, and `python job.py codex` is the case they meant.
  const killed: number[] = [];

  const outcome = await endIfOursPosix(RECORD, async () => found(['python3', 'python3', 'job.py']), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'not ours');
  assert.deepStrictEqual(killed, [], 'a stranger was killed');
});

test('a Linux child whose pid now belongs to something else is left running', async () => {
  const killed: number[] = [];

  const outcome = await endIfOursPosix(RECORD, async () => found(['node']), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'not ours');
  assert.deepStrictEqual(killed, []);
});

test('a namesake started at another time is not ours, however well the name matches', async () => {
  const killed: number[] = [];
  const longAfter = RECORD.startedMs + 60_000;

  const outcome = await endIfOursPosix(RECORD, async () => found(['codex'], longAfter), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'not ours');
  assert.deepStrictEqual(killed, [], 'a pid the kernel had recycled onto a namesake was killed');
});

test('a start time nobody could read is not treated as a match', async () => {
  // `startedAtMs` answers -1 when either half is missing, and -1 must never fall inside the slack.
  const killed: number[] = [];

  const outcome = await endIfOursPosix(RECORD, async () => found(['codex'], -1), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'not ours');
  assert.deepStrictEqual(killed, []);
});

test('a Linux child that is simply gone is not asked to die twice', async () => {
  const killed: number[] = [];

  const outcome = await endIfOursPosix(RECORD, async () => ({ kind: 'absent' }), (pid) => {
    killed.push(pid);
  });

  assert.strictEqual(outcome, 'gone', 'a vanished process kept its row instead of settling it');
  assert.deepStrictEqual(killed, []);
});

test('a Linux child /proc could not be asked about is kept and retried, never assumed dead', async () => {
  const outcome = await endIfOursPosix(RECORD, async () => ({ kind: 'unknown' }), () => undefined);

  assert.strictEqual(outcome, 'unknown');
});

test('a kill that races the process’s own exit is reported gone, not unknown', async () => {
  const outcome = await endIfOursPosix(RECORD, async () => found(['codex']), refuses('ESRCH'));

  assert.strictEqual(outcome, 'gone');
});

test('a kill we are not allowed to make keeps the row', async () => {
  // EPERM is a process that EXISTS and will not be touched by us. Striking the row out here is how
  // a running orphan becomes one nobody ever looks for again.
  const outcome = await endIfOursPosix(RECORD, async () => found(['codex']), refuses('EPERM'));

  assert.strictEqual(outcome, 'unknown');
});
