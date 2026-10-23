import assert from 'node:assert/strict';
import { test } from 'node:test';

import { hostKind, hostKindOf } from '../hostSide';
import { runtimeSkewNote } from '../runtimeSince';
import { vibeNoteFor } from '../vendorTerminal';
import type { Vendor } from '../vendors';
import { readProcVersion } from '../wslNetwork';

/**
 * An unreadable /proc/version is said, once (E3 of research/PLAN_kimi_and_vibe_on_linux.md, code gate; E2.S3 of
 * research/PLAN_kimi_and_vibe_on_native_linux_E2.md).
 *
 * <p>`hostKind` reads the kernel banner to tell WSL2 from WSL1 and from a native Linux host. When the read fails the host used
 * to be treated as native Linux — a refusal, never a promise — and that used to happen in silence, so a person on a WSL2 window
 * whose banner could not be read saw "refused on a native Linux host" with nothing anywhere saying why. Since E2 a native Linux
 * host RUNS Vibe, so "treated as native Linux" would be a promise the server refuses (its `ProcVersion.ReadFrom` calls a blank or
 * missing banner unread and the admission refuses on it): the host is its own kind, `linux-unread`, and it is still SAID, once,
 * where the extension's other host-side warnings go: `console.warn` with the `ConnectOtherAIs:` prefix (the extension has no
 * output channel of its own; its diagnostics land in the extension-host log).</p>
 */

const WSL2_BANNER = 'Linux version 6.18.33.2-microsoft-standard-WSL2 (root@build) (gcc) #1 SMP';
const NATIVE_BANNER = 'Linux version 5.15.0-91-generic (buildd@lcy02) (gcc) #101-Ubuntu SMP';

const VIBE: Vendor = {
  id: 'vibe', runtime: 'vibe', model: 'mistral-medium-3.5', enabled: true, plan: true, code: true,
  baseUrl: '', executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

test('a banner that cannot be read is logged once, naming the cause and what the host is treated as', async () => {
  const said: string[] = [];
  const kind = await hostKind('linux', async () => { throw new Error('EACCES: permission denied, open /proc/version'); }, (line) => said.push(line));

  assert.equal(kind, 'linux-unread', 'an unreadable banner promised a Vibe row: it is neither native Linux nor WSL, it is a refusal');
  assert.equal(said.length, 1, `one line, not ${said.length}: ${said.join(' | ')}`);
  assert.match(said[0]!, /^ConnectOtherAIs: /u, 'the prefix every host-side warning of this extension carries');
  assert.match(said[0]!, /\/proc\/version could not be read/u);
  assert.match(said[0]!, /EACCES/u, 'the cause travels with it');
  assert.match(said[0]!, /treated as a Linux host whose banner could not be read, so a Vibe row is shown as refused here/u);
});

test('a banner that reads, and a host that never reads one, log nothing', async () => {
  const said: string[] = [];
  const push = (line: string): void => { said.push(line); };

  assert.equal(await hostKind('linux', async () => WSL2_BANNER, push), 'wsl2');
  assert.equal(await hostKind('linux', async () => NATIVE_BANNER, push), 'linux');
  assert.equal(await hostKind('linux', async () => '', push), 'linux-unread', 'an empty banner that READ is not a failed read, and still not a banner');
  assert.equal(await hostKind('linux', async () => '   \n', push), 'linux-unread', 'whitespace is not a read banner either (the server’s ProcVersion.ReadFrom)');
  assert.equal(await hostKind('win32', async () => { throw new Error('never asked'); }, push), 'windows');
  assert.equal(await hostKind('darwin', async () => { throw new Error('never asked'); }, push), 'darwin');
  assert.deepEqual(said, []);
});

test('the pure rule agrees with the server: a blank banner on Linux is unread, a platform alone is not asked', () => {
  assert.equal(hostKindOf('linux', ''), 'linux-unread', 'the server calls a blank banner unread and refuses; the extension may not promise a row it refuses');
  assert.equal(hostKindOf('linux', ' \t\n'), 'linux-unread');
  assert.equal(hostKindOf('linux', NATIVE_BANNER), 'linux');
  assert.equal(hostKindOf('win32', ''), 'windows');
  assert.equal(hostKindOf('darwin', ''), 'darwin');
});

test('a host whose banner was not read is told it is refused and why, at every server version — never to update', () => {
  for (const version of ['0.59.0', '0.63.0', '0.64.0', '0.70.0', '']) {
    const note = runtimeSkewNote(version, [VIBE], 'linux-unread');

    assert.match(note, /could not read .* kernel banner/u, `server "${version}": an unreadable banner promised a Vibe row`);
    assert.match(note, /WSL1/u, `server "${version}"`);
    assert.ok(note.includes('refuses a vibe row here'), `server "${version}": ${note}`);
    assert.doesNotMatch(note, /Update coai-mcp/u, `server "${version}": an update would not change it`);
  }
  assert.match(vibeNoteFor('linux-unread'), /could not read this host's kernel banner/u);
});

test('the real reader reports a failed read as a failure, not as an empty banner',
  { skip: process.platform !== 'win32' && 'only a host with no /proc/version can show it' },
  async () => {
    await assert.rejects(readProcVersion(), 'a Windows host has no /proc/version: the read must reject so hostKind can say so');
  });
