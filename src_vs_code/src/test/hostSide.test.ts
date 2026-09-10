import assert from 'node:assert/strict';
import { test } from 'node:test';
import { classifyWindowsReach, hostPlatform, windowsReach } from '../hostSide';

/**
 * Which side this extension host is on, and what it can reach from there.
 *
 * <p>The whole module exists because `process.platform` answers a different question from the one
 * three call sites were asking it. These tests pin the difference: a WSL host reports `linux` and
 * still has a Windows session one hop away, and a native Linux box reports the same thing and has
 * nothing of the kind.</p>
 */

test('a Windows host can always reach itself directly, without asking whether it is WSL', () => {
  assert.deepStrictEqual(classifyWindowsReach('win32', false), { kind: 'direct' });
  // True even if something claimed it were WSL: a Windows host runs the helper itself, and the
  // question of a hop does not arise.
  assert.deepStrictEqual(classifyWindowsReach('win32', true), { kind: 'direct' });
});

test('a WSL host is a candidate for interop, whether or not it turns out to work', () => {
  assert.deepStrictEqual(classifyWindowsReach('linux', true), { kind: 'interop' });
});

test('a plain Linux host with no WSL has no Windows side to reach', () => {
  assert.deepStrictEqual(classifyWindowsReach('linux', false), { kind: 'none' });
});

test('darwin never has a Windows side to reach', () => {
  assert.deepStrictEqual(classifyWindowsReach('darwin', false), { kind: 'none' });
  assert.deepStrictEqual(classifyWindowsReach('darwin', true), { kind: 'none' });
});

test('hostPlatform narrows anything that is neither win32 nor darwin to linux', () => {
  assert.strictEqual(hostPlatform('win32'), 'win32');
  assert.strictEqual(hostPlatform('darwin'), 'darwin');
  assert.strictEqual(hostPlatform('linux'), 'linux');
  // The safe default is the POSIX branch, never the Windows-only one: a platform nobody planned for
  // must not be handed a `taskkill` or a synthetic keystroke.
  assert.strictEqual(hostPlatform('freebsd'), 'linux');
  assert.strictEqual(hostPlatform(''), 'linux');
});

test('windowsReach never asks whether it is WSL when the host is already Windows', async () => {
  let asked = 0;
  const reach = await windowsReach('win32', async () => {
    asked += 1;

    return true;
  });

  assert.deepStrictEqual(reach, { kind: 'direct' });
  assert.strictEqual(asked, 0, 'a Windows host paid for a /proc read that cannot change its answer');
});

test('a mac never pays for a file read that cannot change its answer either', async () => {
  let asked = 0;
  const reach = await windowsReach('darwin', async () => {
    asked += 1;

    return true;
  });

  assert.deepStrictEqual(reach, { kind: 'none' });
  assert.strictEqual(asked, 0, 'darwin read /proc/version, which it does not have, to learn nothing');
});

test('a host that cannot say whether it is WSL refuses in words rather than throwing', async () => {
  // The command handler awaits this with no catch of its own: a rejection here would take the
  // keybinding down with nothing on screen. Not knowing is `none`, which names the menu instead.
  const reach = await windowsReach('linux', async () => {
    throw new Error('EACCES: permission denied, open /proc/version');
  });

  assert.deepStrictEqual(reach, { kind: 'none' });
});

test('windowsReach asks once on Linux, and carries the answer through', async () => {
  let asked = 0;
  const underWsl = async (): Promise<boolean> => {
    asked += 1;

    return true;
  };

  assert.deepStrictEqual(await windowsReach('linux', underWsl), { kind: 'interop' });
  assert.strictEqual(asked, 1);
  assert.deepStrictEqual(await windowsReach('linux', async () => false), { kind: 'none' });
});
