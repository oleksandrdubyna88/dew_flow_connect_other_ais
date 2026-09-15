import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  adoptionSentence,
  defaultSideName,
  sideRefusal,
  sidesIn,
} from '../dataChoice';
import { usableSideName } from '../dataDir';

/**
 * What the install flow asks, and what it says about the answer.
 *
 * <p>The dialogs themselves live in `extension.ts` behind `vscode`; every DECISION they make is
 * here, where it can be asserted. That split is the house rule for a panel command, and this flow
 * has more decisions than most: what a chosen folder already holds decides whether the sentence a
 * person reads says "this starts empty" or "this continues your history", and getting that pair the
 * wrong way round is how somebody walks past the one screen that would have told them their four
 * years of rounds were about to be ignored.</p>
 *
 * <p><b>A non-empty folder is the GOOD case here</b>, and that is the opposite of the rule the move
 * flow lives by. Adopting means reading what is already there; moving means writing where nothing
 * is. One sentence copied into the wrong one of those two destroys a history.</p>
 */

const WINDOWS = { remoteName: '', distro: '', hostname: 'Desktop01', platform: 'win32' };
const WSL = { remoteName: 'wsl', distro: 'Ubuntu-24.04', hostname: 'Desktop01', platform: 'linux' };

// ---------- the name this side is offered ----------

test('a local Windows window is offered its platform and its machine', () => {
  // Both halves matter: two machines sharing one NAS would collide on `windows` alone, and a
  // collision here is two installations writing one SQLite file — the thing a side exists to stop.
  assert.equal(defaultSideName(WINDOWS), 'windows-desktop01');
});

test('a WSL window is offered its distro, which is what distinguishes it', () => {
  assert.equal(defaultSideName(WSL), 'wsl-ubuntu-24.04');
});

test('another kind of remote falls back to its own name and host', () => {
  assert.equal(
    defaultSideName({ remoteName: 'ssh-remote', distro: '', hostname: 'build-box', platform: 'linux' }),
    'ssh-remote-build-box');
});

test('every name this offers is one the server would accept', () => {
  // The offered name goes straight into a path both halves compute, so an offer the grammar refuses
  // would be the flow handing somebody a value it then rejects.
  const windows = [
    WINDOWS,
    WSL,
    { remoteName: '', distro: '', hostname: 'Ada’s MacBook Pro', platform: 'darwin' },
    { remoteName: '', distro: '', hostname: '', platform: 'freebsd' },
    { remoteName: 'wsl', distro: 'openSUSE Leap 15.6', hostname: '', platform: 'linux' },
  ];

  for (const window of windows) {
    const offered = defaultSideName(window);
    assert.ok(
      usableSideName(offered),
      `${JSON.stringify(window)} was offered "${offered}", which the server refuses to start on`);
  }
});

// ---------- a name somebody typed ----------

test('a side name with a separator is refused, and the sentence says what is allowed', () => {
  const refusal = sideRefusal('wsl/node1');

  assert.match(refusal, /lower-case letters, digits, dot, dash and underscore/);
});

test('a usable name is refused by nothing', () => {
  assert.equal(sideRefusal('windows-desktop01'), '');
  assert.equal(sideRefusal(''), '', 'empty is a choice: the folder is not divided');
});

test('a name is judged after trimming and lower-casing, as the server judges it', () => {
  assert.equal(sideRefusal('  Windows  '), '');
});

// ---------- what a folder already holds ----------

test('a folder with a database of its own is adopted, and the sentence says so', () => {
  const said = adoptionSentence('Z:\\coai', { hasDatabase: true, sides: [] });

  assert.match(said, /already/u);
  assert.match(said, /Z:\\coai/u);
  assert.ok(!/empty|starts with no history/u.test(said), 'this folder has a history and is not empty');
});

test('a folder holding side directories offers them by name', () => {
  const said = adoptionSentence('Z:\\coai', { hasDatabase: false, sides: ['windows-desktop01', 'wsl-ubuntu-24.04'] });

  assert.match(said, /windows-desktop01/u);
  assert.match(said, /wsl-ubuntu-24\.04/u);
});

test('a folder with nothing in it says the history starts here', () => {
  const said = adoptionSentence('Z:\\coai', { hasDatabase: false, sides: [] });

  assert.match(said, /no history/u);
});

test('the sentence never claims a history a folder does not have', () => {
  // The failure this pair exists to prevent, asserted as the pair: a person who is told their
  // history is there walks past the screen that was their last chance to notice it is not.
  const empty = adoptionSentence('Z:\\coai', { hasDatabase: false, sides: [] });

  assert.ok(!/already holds/u.test(empty));
});

// ---------- reading a folder ----------

test('a side is a subdirectory that holds a database, and nothing else is', () => {
  // `worktrees/` and `servers/` are directories in exactly the same place and are not sides. Listing
  // them would offer somebody a scratch folder as their history.
  const found = sidesIn([
    { name: 'windows-desktop01', isDirectory: true, hasDatabase: true },
    { name: 'wsl-ubuntu-24.04', isDirectory: true, hasDatabase: true },
    { name: 'worktrees', isDirectory: true, hasDatabase: false },
    { name: 'servers', isDirectory: true, hasDatabase: false },
    { name: 'coai.db', isDirectory: false, hasDatabase: false },
  ]);

  assert.deepEqual(found, ['windows-desktop01', 'wsl-ubuntu-24.04']);
});

test('a subdirectory whose name no side could have is not offered', () => {
  // It cannot be reached: `COAI_DATA_SIDE` would be refused for it, so offering it would produce a
  // configuration the server will not start on.
  const found = sidesIn([{ name: 'Windows Desktop', isDirectory: true, hasDatabase: true }]);

  assert.deepEqual(found, []);
});
