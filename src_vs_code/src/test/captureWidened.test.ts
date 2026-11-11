import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { launch, system32 } from '../processLauncher';

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
