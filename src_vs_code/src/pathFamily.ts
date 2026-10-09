/**
 * Which operating system an absolute path is SPELLED for — and so whether a folder stored in settings this window
 * shares with another is the other side's.
 *
 * <p><b>Why it exists</b> (operator, 2026-10-09). VS Code's user settings are shared by a WSL window and a plain Windows
 * window on one machine, and each side runs its own `coai-mcp`. A question-consultant root written from WSL
 * (`/home/jinx/git`) therefore reaches the Windows side, whose server refused it as "not a directory on this machine"
 * with a failure toast on every start. The folder is not missing: it is the other side's, and the server THERE reads it.
 * The server now skips it (`QuestionRoots.OtherSide`), and the Settings page says so beside the root.</p>
 *
 * <p>Lexical, so a test decides both directions on any machine. `//server/share` is neither side's alone — a UNC share on
 * Windows, a path from `/` on POSIX — and a relative or drive-relative path is this side's, refused there by name.
 * `shared/path-family-vectors.json` is answered by this and by the C#, so the page names exactly the roots the server
 * skips. Pure and `vscode`-free.</p>
 */

/** A POSIX absolute path — one leading slash. Two are a UNC share on Windows, so they are not this shape. */
export function isPosixAbsolute(path: string): boolean {
  const text = path.trim();

  return text.startsWith('/') && !text.startsWith('//');
}

/** A Windows absolute path — a drive letter with either slash, or a backslash-rooted one (`\\server\share`). */
export function isWindowsAbsolute(path: string): boolean {
  const text = path.trim();

  return /^[A-Za-z]:[\\/]/.test(text) || text.startsWith('\\');
}

/** Whether `path` is spelled for the OTHER operating system than the one this host — `windows` or not — runs on. */
export function spelledForTheOtherOs(path: string, windows: boolean): boolean {
  return windows ? isPosixAbsolute(path) : isWindowsAbsolute(path);
}

/** The sentence beside a stored root of the other side — said, never drawn as a refusal. */
export function otherSideNote(windows: boolean): string {
  return windows
    ? 'a Linux, WSL or macOS path — the other side\'s folder: its server reads it there, and this Windows side skips it'
    : 'a Windows path — the other side\'s folder: its server reads it there, and this side skips it';
}
