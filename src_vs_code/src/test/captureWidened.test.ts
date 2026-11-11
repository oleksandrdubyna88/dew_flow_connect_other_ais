import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFileSync, realpathSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { setTimeout as after } from 'node:timers/promises';
import { launch, system32 } from '../processLauncher';
import { capture } from '../versionProbe';

/**
 * The launcher and `capture`, widened for the remote reader (E2.S2, X8) — against REAL children, as `captureStops.test.ts`
 * does, because what is asserted is that a process goes away and not that we called `kill`.
 *
 * <p>One new file on purpose: the existing tests of `capture`, `launch` and `roundsDbRead` stay byte for byte, which is
 * the proof that the widening changed nothing for the callers of today.</p>
 */

const NODE = process.execPath;

/** A child that will outlive any wait this file is willing to make. */
const NEVER_ENDS = ['-e', 'setInterval(() => {}, 1000);'];

test('S2-T22 (launch half): a handle that was asked for the tree kill ends its child on kill()', async () => {
  const child = launch(NODE, NEVER_ENDS, { killTree: true });
  const ended = new Promise<number>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('a killTree handle was killed and the child never ended')), 10_000);
    child.onExit((code) => {
      clearTimeout(timer);
      resolve(code);
    });
  });

  child.kill();

  await ended;
});

test('S2-T23: the system folder is one rule — SystemRoot, then WINDIR, then C:\\Windows', () => {
  assert.equal(system32({ SystemRoot: 'D:\\W' }), 'D:\\W\\System32');
  assert.equal(system32({ WINDIR: 'E:\\Win' }), 'E:\\Win\\System32');
  assert.equal(system32({ SystemRoot: 'D:\\W', WINDIR: 'E:\\Win' }), 'D:\\W\\System32', 'SystemRoot wins over WINDIR');
  assert.equal(system32({}), 'C:\\Windows\\System32');
});

/** The run folder this test run owns (`inRunFolder.cjs` has checked it is set). */
function runFolder(): string {
  const folder = process.env['COAI_TEST_RUN_FOLDER'];
  assert.ok(folder !== undefined && folder.length > 0, 'the run folder is not set');

  return folder;
}

/** Whether a process with this pid is still there (signal 0 only asks). */
function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);

    return true;
  } catch {
    return false;
  }
}

test('S2-T8: the stdout cap counts BYTES, not characters', async () => {
  // 300 characters are 600 bytes. A cap of 400 is under the bytes and over the characters, so only a count of bytes ends it.
  // The cap of the call is 5 s, not the 60 s of the spec's sketch, so the break-it (a count of characters) fails at once.
  const writesThenWaits = ['-e', 'process.stdout.write("é".repeat(300)); setInterval(() => {}, 1000);'];
  const began = Date.now();

  const answer = await capture(NODE, writesThenWaits, false, 5_000, undefined, undefined, '', { maxStdoutBytes: 400 });

  assert.equal(answer.ended, 'capped', '8 MiB of characters is not 8 MiB: the cap counted characters, not bytes');
  assert.equal(answer.code, -1);
  assert.equal(answer.output, '', 'the stdout read so far is NOT in a capped answer');
  assert.ok(Date.now() - began < 4_500, 'the child was killed at the cap, not at the time limit');
});

test('S2-T8 companion: a child under the cap is answered whole', async () => {
  const writes = ['-e', 'process.stdout.write("é".repeat(300));'];

  const answer = await capture(NODE, writes, false, 10_000, undefined, undefined, '', { maxStdoutBytes: 700 });

  assert.equal(answer.code, 0);
  assert.equal(answer.output, 'é'.repeat(300));
  assert.equal(answer.ended, undefined);
});

test('S2-T11: capture is widened, not changed', async () => {
  // The parameters before `input` (which has a default) are the six of today: the eighth parameter did not move any of them.
  assert.equal(capture.length, 6, 'the first parameters of capture moved — the options object must be the EIGHTH');
  const prints = ['-e', 'process.stdout.write("x");'];

  assert.deepEqual(await capture(NODE, prints, false, 10_000), { code: 0, output: 'x' },
    'a call with no options answers exactly what it answered before — no stderrTail key');
  const withOptions = await capture(NODE, prints, false, 10_000, undefined, undefined, '', {});
  assert.ok('stderrTail' in withOptions, 'a call WITH the options object answers its stderr tail');
  assert.equal(withOptions.stderrTail, '');

  const where = runFolder();
  const printsCwd = ['-e', 'process.stdout.write(process.cwd());'];
  const here = await capture(NODE, printsCwd, false, 10_000, undefined, undefined, '', { cwd: where });
  assert.equal(realpathSync(here.output), realpathSync(where), 'the child did not run in the folder it was given');
});

test('S2-T11 companion: the stderr tail of a failed call is there', async () => {
  const failing = ['-e', 'process.stderr.write("boom\\n"); process.exit(3);'];

  const answer = await capture(NODE, failing, false, 10_000, undefined, undefined, '', {});

  assert.equal(answer.code, 3);
  assert.equal(answer.stderrTail, 'boom\n');
});

test('S2-T22: a child past the limit is gone', async () => {
  const pidFile = join(runFolder(), `pid-${randomUUID()}`);
  const writesItsPid = ['-e', 'require("node:fs").writeFileSync(process.argv[1], String(process.pid)); setInterval(() => {}, 1000);', pidFile];

  // 1 s rather than the spec's 150 ms: a slow worker could expire the cap before node had written the file.
  const answer = await capture(NODE, writesItsPid, false, 1_000, undefined, undefined, '', { killTree: true });

  assert.equal(answer.ended, 'timeout');
  const pid = Number(readFileSync(pidFile, 'utf8'));
  assert.ok(pid > 0, 'the child did not write its pid');
  try {
    const deadline = Date.now() + 5_000;
    while (alive(pid) && Date.now() < deadline) {
      await after(50);
    }
    assert.equal(alive(pid), false, 'a timed-out ssh was left running');
  } finally {
    // Whatever the verdict, no child of this test outlives it (a break-it that removes the kill must not hang the run).
    if (alive(pid)) {
      process.kill(pid, 'SIGKILL');
    }
    rmSync(pidFile, { force: true });
  }
});
