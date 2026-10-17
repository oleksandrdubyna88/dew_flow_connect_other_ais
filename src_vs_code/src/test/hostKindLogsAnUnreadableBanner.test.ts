import assert from 'node:assert/strict';
import { test } from 'node:test';

import { hostKind } from '../hostSide';
import { readProcVersion } from '../wslNetwork';

/**
 * An unreadable /proc/version is said, once (E3 of todo/PLAN_kimi_and_vibe_on_linux.md, code gate).
 *
 * <p>`hostKind` reads the kernel banner to tell WSL2 from WSL1 and from a native Linux host. When the read fails the host is
 * treated as native Linux — a refusal, never a promise — and that used to happen in silence, so a person on a WSL2 window
 * whose banner could not be read saw "refused on a native Linux host" with nothing anywhere saying why. The line goes where
 * the extension's other host-side warnings go: `console.warn` with the `ConnectOtherAIs:` prefix (the extension has no
 * output channel of its own; its diagnostics land in the extension-host log).</p>
 */

const WSL2_BANNER = 'Linux version 6.18.33.2-microsoft-standard-WSL2 (root@build) (gcc) #1 SMP';

test('a banner that cannot be read is logged once, naming the cause and the native-Linux fallback', async () => {
  const said: string[] = [];
  const kind = await hostKind('linux', async () => { throw new Error('EACCES: permission denied, open /proc/version'); }, (line) => said.push(line));

  assert.equal(kind, 'linux', 'an unreadable banner is a native host: it only ever refuses');
  assert.equal(said.length, 1, `one line, not ${said.length}: ${said.join(' | ')}`);
  assert.match(said[0]!, /^ConnectOtherAIs: /u, 'the prefix every host-side warning of this extension carries');
  assert.match(said[0]!, /\/proc\/version could not be read/u);
  assert.match(said[0]!, /EACCES/u, 'the cause travels with it');
  assert.match(said[0]!, /treated as a native Linux host/u);
});

test('a banner that reads, and a host that never reads one, log nothing', async () => {
  const said: string[] = [];
  const push = (line: string): void => { said.push(line); };

  assert.equal(await hostKind('linux', async () => WSL2_BANNER, push), 'wsl2');
  assert.equal(await hostKind('linux', async () => '', push), 'linux', 'an empty banner that READ is not a failed read');
  assert.equal(await hostKind('win32', async () => { throw new Error('never asked'); }, push), 'windows');
  assert.equal(await hostKind('darwin', async () => { throw new Error('never asked'); }, push), 'darwin');
  assert.deepEqual(said, []);
});

test('the real reader reports a failed read as a failure, not as an empty banner',
  { skip: process.platform !== 'win32' && 'only a host with no /proc/version can show it' },
  async () => {
    await assert.rejects(readProcVersion(), 'a Windows host has no /proc/version: the read must reject so hostKind can say so');
  });
