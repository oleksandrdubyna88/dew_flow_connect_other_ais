import assert from 'node:assert/strict';
import { test } from 'node:test';
import { installedKey, serverStatus, sideLabel } from '../coaiInstall';
import { serverSentence } from '../panelView';
import { parseCliVersion } from '../cliVersions';

/**
 * The Server section is about ONE side of a machine — the side the panel is running on.
 *
 * <p>Measured 2026-09-03: one machine, a Windows window and a `WSL: Ubuntu` window of the same
 * profile. `globalState` is the client's storage and is shared by both; `globalStorageUri`, where
 * the binary goes, is per extension host. So an install pressed in Windows left `0.12.2` in the one
 * record, and the WSL panel — whose disk held the published 0.12.1, hash-verified — read it, said
 * "you are up to date", and rendered no button at all. There was no way left, from inside the
 * product, to update the server WSL actually runs.</p>
 */

test('a record made on the other side does not make this side claim installed', () => {
  // The WSL press at 10:55 wrote 0.12.1; the Windows press at 11:52 overwrote it with 0.12.2. One
  // record, two disks — so here is the Windows record, read on a side whose disk is empty.
  const status = serverStatus({
    fileExists: false,
    reported: '',
    remembered: '0.12.2',
    published: '0.12.2',
  });

  assert.equal(status.kind, 'absent', 'no file on this side is not "installed", whatever is remembered');
  assert.equal(status.version, '', 'a version belonging to another disk must not be shown as this one');
});

test('the binary\'s own answer wins over the record', () => {
  const status = serverStatus({
    fileExists: true,
    reported: '0.12.1',
    remembered: '0.12.2',
    published: '0.12.2',
  });

  assert.equal(status.kind, 'known');
  assert.equal(status.version, '0.12.1', 'the file is the only source that cannot belong to another machine');
  assert.equal(status.remembered, false);
  assert.equal(status.updateOffered, true, '0.12.2 is published over the 0.12.1 that is here');
});

test('an up-to-date binary offers nothing', () => {
  const status = serverStatus({
    fileExists: true,
    reported: '0.12.3',
    remembered: '',
    published: '0.12.3',
  });

  assert.equal(status.kind, 'known');
  assert.equal(status.updateOffered, false);
});

test('an unknown version offers nothing while the published one cannot be read', () => {
  // Offline. `offerUpdate` runs at activation and would otherwise announce "a newer coai-mcp is
  // published" on the strength of nothing, and the install it offers would fail on the same
  // network. (gemini, the code round.)
  const status = serverStatus({
    fileExists: true,
    reported: '',
    remembered: '',
    published: '',
  });

  assert.equal(status.kind, 'unknown');
  assert.equal(status.updateOffered, false);
});

test('a binary that cannot report its version reads unknown and offers the update', () => {
  // Every release up to and including 0.12.2 exits 64 on `--version`, printing nothing on stdout.
  const status = serverStatus({
    fileExists: true,
    reported: '',
    remembered: '',
    published: '0.12.3',
  });

  assert.equal(status.kind, 'unknown', 'a file that cannot answer is not a file that is up to date');
  assert.equal(status.version, '');
  assert.equal(status.updateOffered, true);
});

test('the record is the fallback when the file is there but cannot be asked', () => {
  // Smart App Control refuses to spawn a freshly written executable; the install that just wrote it
  // knows what it was.
  const status = serverStatus({
    fileExists: true,
    reported: '',
    remembered: '0.12.3',
    published: '0.12.3',
  });

  assert.equal(status.kind, 'known');
  assert.equal(status.version, '0.12.3');
  assert.equal(status.remembered, true, 'the panel must be able to say where the number came from');
  assert.equal(status.updateOffered, false);
});

test('nothing installed is never an update prompt', () => {
  const status = serverStatus({
    fileExists: false,
    reported: '',
    remembered: '',
    published: '0.12.3',
  });

  assert.equal(status.kind, 'absent');
  assert.equal(
    status.updateOffered,
    false,
    'offerUpdate runs at activation: a machine with nothing must not be told to update it',
  );
});

test('an unstamped local build is older than every release', () => {
  const status = serverStatus({
    fileExists: true,
    reported: '0.0.0',
    remembered: '',
    published: '0.12.3',
  });

  assert.equal(status.version, '0.0.0');
  assert.equal(status.updateOffered, true, 'a default 1.0.0 would have suppressed this button for ever');
});

test('an unreadable published version never lights the button', () => {
  const status = serverStatus({
    fileExists: true,
    reported: '0.12.1',
    remembered: '',
    published: '',
  });

  assert.equal(status.updateOffered, false, 'offline is not an update');
});

test('the two sides of one machine cannot share a record', () => {
  // The measured pair: one Windows profile directory, one ~/.vscode-server in a distro.
  const windows = installedKey({
    hostname: 'jinx',
    storagePath: 'c:\\Users\\strug\\AppData\\Roaming\\Code\\User\\globalStorage\\remsoftdev.connect-other-ais',
  });
  const ubuntu = installedKey({
    remoteName: 'wsl',
    distro: 'Ubuntu',
    hostname: 'jinx',
    storagePath: '/home/jinx/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais',
  });

  assert.notEqual(ubuntu, windows, 'the record a Windows press leaves must not satisfy the WSL side');
});

test('two distros of one machine cannot share a record either', () => {
  // The storage path is IDENTICAL in both: same user name, same ~/.vscode-server. Keying on the
  // path alone — the first fix attempted here — would have collided exactly as Gemini predicted.
  const storagePath = '/home/jinx/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';
  const ubuntu = installedKey({ remoteName: 'wsl', distro: 'Ubuntu', hostname: 'jinx', storagePath });
  const debian = installedKey({ remoteName: 'wsl', distro: 'Debian', hostname: 'jinx', storagePath });

  assert.notEqual(ubuntu, debian);
});

test('two SSH hosts with the same home are two sides', () => {
  const storagePath = '/home/ada/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';
  const one = installedKey({ remoteName: 'ssh-remote', hostname: 'build-1', storagePath });
  const two = installedKey({ remoteName: 'ssh-remote', hostname: 'build-2', storagePath });

  assert.notEqual(one, two, 'a remote with no distro is told apart by its hostname');
});

test('a local window is not identified by a name the machine can be renamed with', () => {
  const storagePath = 'c:\\Users\\strug\\AppData\\Roaming\\Code\\User\\globalStorage\\remsoftdev.connect-other-ais';

  assert.equal(
    installedKey({ hostname: 'before', storagePath }),
    installedKey({ hostname: 'after', storagePath }),
    'renaming the machine must not throw away what is installed on it',
  );
  assert.equal(
    installedKey({ remoteName: '', hostname: 'x', storagePath }),
    installedKey({ hostname: 'x', storagePath }),
    'an empty remoteName IS a local window',
  );
});

test('the legacy key is not one of ours', () => {
  // It cannot be attributed to a side — that is the defect — so it is left in place, unread.
  assert.notEqual(installedKey({ storagePath: 'c:\\x' }), 'coai.installedVersion');
  assert.notEqual(
    installedKey({ remoteName: 'wsl', distro: 'Ubuntu', storagePath: '/home/j' }),
    'coai.installedVersion',
  );
});

test('the side is named the way the remote indicator names it', () => {
  assert.equal(sideLabel('wsl', 'Ubuntu'), 'WSL: Ubuntu');
  assert.equal(sideLabel('wsl'), 'WSL', 'a distro name we do not have is not invented');
  assert.equal(sideLabel('ssh-remote', ''), 'SSH');
  assert.equal(sideLabel(undefined), '', 'one side needs no word for it');
  assert.equal(sideLabel(''), '');
  assert.equal(
    sideLabel('attached-container', ''),
    'attached-container',
    'a remote kind this build does not know is printed as it is, never prettified into a guess',
  );
});

test('the sentence never names an action the section cannot offer', () => {
  // It used to end "— press Update"; offline there is no button under it, and a sentence naming an
  // action nobody can take is worse than a plain statement of what is there. (codex + gemini.)
  const unknown = serverSentence(
    { kind: 'unknown', version: '', remembered: false, updateOffered: false },
    'WSL: Ubuntu',
  );

  assert.ok(unknown.includes('cannot report its version'));
  assert.ok(!/press Update/i.test(unknown));
});

test('the sentence names the side, and says when the number is only remembered', () => {
  assert.equal(
    serverSentence({ kind: 'known', version: '0.12.1', remembered: false, updateOffered: true }, 'WSL: Ubuntu'),
    'coai-mcp 0.12.1 is installed in WSL: Ubuntu.',
  );
  assert.equal(
    serverSentence({ kind: 'absent', version: '', remembered: false, updateOffered: false }, 'WSL: Ubuntu'),
    'coai-mcp is not installed in WSL: Ubuntu.',
  );
  assert.equal(
    serverSentence({ kind: 'absent', version: '', remembered: false, updateOffered: false }, ''),
    'coai-mcp is not installed yet.',
    'a local window has one side and no need for a word for it',
  );
  assert.ok(
    serverSentence({ kind: 'known', version: '0.12.3', remembered: true, updateOffered: false }, '').includes(
      "this side's own record",
    ),
    'a number that came from the record must not read as the binary having answered',
  );
});

test('two identities that differ only in punctuation are two records', () => {
  // The first attempt slugified, so these pairs shared a key and one side could adopt the other's
  // remembered version as its fallback. Found by codex on the implementation, not on the plan.
  const path = '/home/j/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais';
  assert.notEqual(
    installedKey({ remoteName: 'wsl', distro: 'Ubuntu-Dev', hostname: 'j', storagePath: path }),
    installedKey({ remoteName: 'wsl', distro: 'Ubuntu Dev', hostname: 'j', storagePath: path }),
  );
  assert.notEqual(
    installedKey({ remoteName: 'wsl', distro: 'U', hostname: 'j', storagePath: '/x/a-b' }),
    installedKey({ remoteName: 'wsl', distro: 'U', hostname: 'j', storagePath: '/x/a/b' }),
  );
  assert.notEqual(
    installedKey({ remoteName: 'wsl', distro: 'a|b', hostname: 'j', storagePath: '/x' }),
    installedKey({ remoteName: 'wsl', distro: 'a', hostname: 'j', storagePath: 'b|/x' }),
    'the separator itself cannot be smuggled into a component',
  );
});

test('a key stays legible for whoever has to read the state later', () => {
  const key = installedKey({
    remoteName: 'wsl',
    distro: 'Ubuntu',
    hostname: 'jinx',
    storagePath: '/home/jinx/.vscode-server/data/User/globalStorage/remsoftdev.connect-other-ais',
  });

  assert.ok(key.startsWith('coai.installedVersion@wsl|Ubuntu|'), key);
  assert.ok(key.includes('.vscode-server'), 'the path a person would recognise survives the escape');
});

test('the version banner the server will print parses', () => {
  assert.equal(parseCliVersion('coai-mcp 0.12.3'), '0.12.3');
  assert.equal(parseCliVersion(''), '', 'a binary that printed nothing reports nothing');
  assert.equal(
    parseCliVersion('[coai-mcp] unknown argument \'--version\''),
    '',
    'the refusal an older release writes must not parse as a version',
  );
});
