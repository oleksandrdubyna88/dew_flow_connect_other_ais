import assert from 'node:assert/strict';
import { test } from 'node:test';
import { POSIX_ON_WINDOWS, answerPaths, dirKey, usableDirs, watchedDirs } from '../escalationDirs';

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

test('a WSL path named from a Windows window is refused with the shape that works', () => {
  // THE trap this whole feature would otherwise walk into. `/home/...` resolved by a Windows host is
  // `C:\home\...`, which does not exist — and an absent directory contributes nothing and says
  // nothing, which is the original symptom with extra steps. (gemini, the plan round.)
  const dirs = watchedDirs('C:\\Own', ['/home/user/.local/share/coai-mcp'], 'win32');

  assert.strictEqual(dirs[1]?.refusal, POSIX_ON_WINDOWS, 'a POSIX path was accepted on Windows');
  assert.strictEqual(dirs[1]?.asked, '/home/user/.local/share/coai-mcp', 'the panel cannot name what to correct');
  assert.strictEqual(dirs[1]?.path, '', 'a refused directory was still handed to the watcher');
  assert.deepStrictEqual(usableDirs(dirs), ['C:\\Own'], 'the refused directory was watched anyway');

  // And NO distribution is invented: the refusal names the shape, it does not guess a path.
  assert.doesNotMatch(dirs[1]?.refusal ?? '', /Ubuntu|Debian|wsl\.localhost\\[A-Za-z]+\\home\\user/,
    'a distribution was guessed rather than asked for');
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

test('a root written with a trailing separator does not compose a mixed-separator path', () => {
  // Callers pass native filesystem paths, so `C:\coai\` trimmed of forward slashes only would give
  // `C:\coai\/escalations` — the same mixed-separator string the first round removed elsewhere.
  for (const root of ['C:\\coai\\', 'C:\\coai', 'C:\\coai//', '/srv/coai/', '/srv/coai']) {
    const paths = answerPaths('q1', root);

    assert.ok(paths, `a plain id was refused for root ${root}`);
    assert.doesNotMatch(paths.dir, /[\\/]{2,}escalations|\\\/|\/\\/u, `mixed or doubled separators for ${root}: ${paths.dir}`);
    assert.match(paths.dir, /escalations$/u, `the escalations directory was lost for ${root}`);
  }
  assert.strictEqual(answerPaths('q1', 'C:\\coai\\')?.dir, answerPaths('q1', 'C:\\coai')?.dir,
    'a trailing separator made a second directory');
});

test('an answer is written beside the question it answers, not in this window’s own directory', () => {
  // The server that asked polls the directory it wrote in and nowhere else. An answer written here
  // would leave that round blocked for ever, HAVING BEEN ANSWERED — which is worse than never
  // answering, because nothing on either side says anything is wrong.
  const elsewhere = answerPaths('q1', 'file:///c:/Own', 'file:///c:/Wsl');

  assert.strictEqual(elsewhere?.dir, 'file:///c:/Wsl/escalations');
  assert.strictEqual(elsewhere?.target, 'file:///c:/Wsl/escalations/q1.answer.json');

  // A question with no `from` is one of this window's own — every question there was before the
  // setting existed, and the behaviour that must not change.
  const own = answerPaths('q1', 'file:///c:/Own');
  assert.strictEqual(own?.target, 'file:///c:/Own/escalations/q1.answer.json');
  assert.strictEqual(answerPaths('q1', 'file:///c:/Own', '   ')?.target, own?.target, 'a blank from moved the answer');
});

test('the temporary file is in the same directory as the answer — the EXDEV rule', () => {
  // The write is atomic: temp, then rename. A rename across two filesystems throws EXDEV, so a temp
  // written in this window's directory and renamed into a WSL or NAS one fails EVERY time and the
  // answer never lands. Asserted rather than commented. (gemini, the plan round, Blocking.)
  for (const from of [undefined, 'file:///c:/Wsl', '\\\\wsl.localhost\\Ubuntu\\home\\user']) {
    const paths = answerPaths('q1', 'file:///c:/Own', from);
    assert.ok(paths, 'a plain id was refused');
    const dirOf = (path: string): string => path.slice(0, path.lastIndexOf('/'));

    assert.strictEqual(
      dirOf(paths.temp),
      dirOf(paths.target),
      `the temp and the answer are on different paths for from=${String(from)} — rename would throw EXDEV`,
    );
    assert.strictEqual(paths.temp, `${paths.target}.tmp`, 'the temp is not the answer plus a suffix');
  }
});

test('an id that is not a name gets no answer path at all', () => {
  // THE ID IS NOT OURS: it is read out of a JSON file another process wrote, and it is about to
  // become part of a path this extension writes to. A question asking to be answered into
  // `../../.ssh/` is one this window declines. Refused rather than sanitised — rewriting somebody's
  // id would answer a different question. (codex, the code round.)
  for (const id of ['../escape', '..', '.', 'a/b', 'a\\b', '', 'c:\\x', 'q 1']) {
    assert.strictEqual(
      answerPaths(id, 'C:\\Own'),
      undefined,
      `an id of ${JSON.stringify(id)} was turned into a path`,
    );
  }

  // And the ids the server actually mints still work — a guard that refused everything would be a
  // feature that never answers anything.
  assert.ok(answerPaths('e1f7ed251598', 'C:\\Own'), 'a real question id was refused');
  assert.ok(answerPaths('a-b_c.1', 'C:\\Own'), 'a name-shaped id was refused');
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
