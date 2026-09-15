import assert from 'node:assert/strict';
import { test } from 'node:test';
import { POSIX_ON_WINDOWS, dirKey, usableDirs, watchedDirs } from '../escalationDirs';

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
  const dirs = watchedDirs('C:\\Own', ['\\\\wsl.localhost\\Ubuntu\\home\\jinx\\.local\\share\\coai-mcp'], 'win32');

  assert.strictEqual(dirs.length, 2);
  assert.strictEqual(dirs[1]?.refusal, '', 'a reachable directory was refused');
  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own', '\\\\wsl.localhost\\Ubuntu\\home\\jinx\\.local\\share\\coai-mcp']);
});

test('a WSL path named from a Windows window is refused with the shape that works', () => {
  // THE trap this whole feature would otherwise walk into. `/home/...` resolved by a Windows host is
  // `C:\home\...`, which does not exist — and an absent directory contributes nothing and says
  // nothing, which is the original symptom with extra steps. (gemini, the plan round.)
  const dirs = watchedDirs('C:\\Own', ['/home/jinx/.local/share/coai-mcp'], 'win32');

  assert.strictEqual(dirs[1]?.refusal, POSIX_ON_WINDOWS, 'a POSIX path was accepted on Windows');
  assert.strictEqual(dirs[1]?.asked, '/home/jinx/.local/share/coai-mcp', 'the panel cannot name what to correct');
  assert.strictEqual(dirs[1]?.path, '', 'a refused directory was still handed to the watcher');
  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own'], 'the refused directory was watched anyway');

  // And NO distribution is invented: the refusal names the shape, it does not guess a path.
  assert.doesNotMatch(dirs[1]?.refusal ?? '', /Ubuntu|Debian|wsl\.localhost\\[A-Za-z]+\\home\\jinx/,
    'a distribution was guessed rather than asked for');
});

test('the same POSIX path is fine on the platform it belongs to', () => {
  const dirs = watchedDirs('/home/jinx/.local/share/coai-mcp', ['/mnt/c/Users/strug/AppData/Local/coai-mcp'], 'linux');

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

test('case is folded only where the platform folds it', () => {
  // On Linux /home/A and /home/a are two directories, and treating them as one silently drops a
  // watch somebody asked for.
  assert.strictEqual(dirKey('C:\\Data\\', 'win32'), dirKey('c:/data', 'win32'));
  assert.notStrictEqual(dirKey('/home/A', 'linux'), dirKey('/home/a', 'linux'));
  assert.strictEqual(dirKey('/home/a/', 'linux'), dirKey('/home/a', 'linux'), 'a trailing slash made a second place');
});
