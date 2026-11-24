import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { WSL_FROM_WINDOWS, dirKey, usableDirs, watchedDirs } from '../escalationDirs';

/**
 * Which directories are watched for questions.
 *
 * <p>The bug behind all of it: a question asked by a Claude Code session inside WSL is written into
 * the WSL store, and a Windows window watches the Windows one. Two filesystems, both live, and the
 * round blocks on a modal that never appears.</p>
 */

test('the window always watches its own directory, whatever the setting says', () => {
  // A setting that could silence this would be a setting that breaks the feature it extends.
  for (const extras of [[], [''], ['   '], ['C:\\Other']]) {
    const dirs = watchedDirs('C:\\Own', extras, 'win32');

    assert.strictEqual(dirs[0]?.path, 'C:\\Own', `the own directory was lost for ${JSON.stringify(extras)}`);
    assert.strictEqual(dirs[0]?.refusal, '', 'the own directory was refused');
  }
});

test('a named directory is watched beside the window’s own', () => {
  const dirs = watchedDirs('C:\\Own', ['\\\\wsl.localhost\\Ubuntu\\home\\user\\.local\\share\\coai-mcp'], 'win32');

  assert.strictEqual(dirs.length, 2);
  assert.strictEqual(dirs[1]?.refusal, '', 'a reachable directory was refused');
  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own', '\\\\wsl.localhost\\Ubuntu\\home\\user\\.local\\share\\coai-mcp']);
});

test('a WSL path in a Windows window is the other side\'s — never refused, never watched, said with the shape that works here', () => {
  // research/PLAN_paths_per_side.md E1.3. The list is shared with the WSL window, where `/home/...` is right; a refusal here
  // made one list unable to hold both sides' values. Still never handed to the watcher: `/home/...` resolved by a
  // Windows host is `C:\home\...`, which is not that folder.
  const dirs = watchedDirs('C:\\Own', ['/home/user/.local/share/coai-mcp'], 'win32');

  assert.strictEqual(dirs[1]?.refusal, '', 'the WSL window\'s folder was refused in the Windows window');
  assert.match(dirs[1]?.otherSide ?? '', /the other side's data folder/, 'it is not named the other side\'s');
  assert.ok((dirs[1]?.otherSide ?? '').endsWith(WSL_FROM_WINDOWS), 'the shape a Windows window names it by is not said');
  assert.strictEqual(dirs[1]?.asked, '/home/user/.local/share/coai-mcp', 'the panel cannot name it');
  assert.strictEqual(dirs[1]?.path, '', 'the other side\'s directory was handed to the watcher');
  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own'], 'the other side\'s directory was watched anyway');

  // And NO distribution is invented: the sentence names the shape, it does not guess a path.
  assert.doesNotMatch(dirs[1]?.otherSide ?? '', /Ubuntu|Debian|wsl\.localhost\\[A-Za-z]+\\home\\user/,
    'a distribution was guessed rather than asked for');
});

test('a Windows window\'s name for a WSL folder, read in WSL, is the other side\'s — not a relative name that "could not be read"', () => {
  // The other direction (E1.3): `\\wsl.localhost\Ubuntu\x` written from Windows was handed to the WSL watcher as a
  // relative path and failed as "could not be read".
  const dirs = watchedDirs('/home/user/.local/share/coai-mcp', ['\\\\wsl.localhost\\Ubuntu\\x', 'C:\\Users\\user\\AppData\\Local\\coai-mcp'], 'linux');

  assert.deepStrictEqual(usableDirs(dirs), ['/home/user/.local/share/coai-mcp'], 'a Windows spelling was watched in WSL');
  for (const dir of dirs.slice(1)) {
    assert.strictEqual(dir.refusal, '', `${dir.asked} was refused rather than called the other side's`);
    assert.match(dir.otherSide, /^a Windows path — the other side's data folder: .*, and this side skips it$/, dir.asked);
  }
});

test('the same POSIX path is fine on the platform it belongs to', () => {
  const dirs = watchedDirs('/home/user/.local/share/coai-mcp', ['/mnt/c/Users/user/AppData/Local/coai-mcp'], 'linux');

  assert.strictEqual(dirs[1]?.refusal, '', 'a POSIX path was refused on Linux');
  assert.strictEqual(usableDirs(dirs).length, 2);
});

test('two spellings of one directory are watched once', () => {
  // Watching it twice offers the same question twice, from one physical place.
  const dirs = watchedDirs('C:\\Own', ['C:\\Data', 'c:\\data\\', 'C:/Data'], 'win32');

  assert.strictEqual(dirs.length, 2, 'one directory was watched more than once');
  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own', 'C:\\Data']);
});

test('the window’s own directory named again as an extra is not watched twice', () => {
  const dirs = watchedDirs('C:\\Own', ['c:\\own\\'], 'win32');

  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own'], 'the own directory was watched twice');
});

test('a UNC path written with forward slashes is not mistaken for a POSIX one', () => {
  // `//server/share` is reachable from Windows; only a SINGLE leading slash is the shape that
  // resolves to C:\… here and quietly watches nothing.
  const dirs = watchedDirs('C:\\Own', ['//wsl.localhost/Ubuntu/home/user/.local/share/coai-mcp'], 'win32');

  assert.strictEqual(dirs[1]?.refusal, '', 'a forward-slash UNC path was refused as if it were POSIX');
});

test('case is folded only where the platform folds it', () => {
  // On Linux /home/A and /home/a are two directories, and treating them as one silently drops a
  // watch somebody asked for.
  assert.strictEqual(dirKey('C:\\Data\\', 'win32'), dirKey('c:/data', 'win32'));
  assert.notStrictEqual(dirKey('/home/A', 'linux'), dirKey('/home/a', 'linux'));
  assert.strictEqual(dirKey('/home/a/', 'linux'), dirKey('/home/a', 'linux'), 'a trailing slash made a second place');
});
