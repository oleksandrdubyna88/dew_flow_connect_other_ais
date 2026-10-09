/**
 * Which operating system an absolute path is SPELLED for — and so whether a folder stored in settings this window
 * shares with another is the other side's.
 *
 * <p><b>Why it exists</b> (operator, 2026-10-09). VS Code's user settings are shared by a WSL window and a plain Windows
 * window on one machine, and each side runs its own `coai-mcp`. A question-consultant root written from WSL
 * (`/home/jinx/git`) therefore reaches the Windows side, whose server refused it as "not a directory on this machine"
 * with a failure toast on every start. The folder is not missing: it is the other side's, and the server THERE reads it.
 * The server now skips it (`QuestionRoots.OtherSideHere`), and the Settings page says so beside the root.</p>
 *
 * <p>The spelling is lexical, so a test decides both directions on any machine. `//server/share` is neither side's
 * alone — a UNC share on Windows, a path from `/` on POSIX — and a relative or drive-relative path is this side's,
 * refused there by name. The DECISION also asks the disk ({@link otherSideHere}): on Windows `/work` is the folder
 * `work` on the current drive, and one that exists is this side's. `shared/path-family-vectors.json` is answered by this
 * and by the C#, so the page names exactly the roots the server skips. Pure and `vscode`-free; the disk is handed in.</p>
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

/**
 * The DECISION, as the server's `QuestionRoots.OtherSideHere` makes it: spelled for the other OS AND no directory here.
 * The spelling alone is not enough (the plan round, 2026-10-09): on Windows `/work` is a legal root-relative path to the
 * folder `work` on the current drive, and such a folder is this side's. Answered by the `existence` vectors.
 */
export function otherSideHere(path: string, windows: boolean, existsHere: boolean): boolean {
  return !existsHere && spelledForTheOtherOs(path, windows);
}

/**
 * Which of `roots` are spelled for the other OS yet ARE directories on this machine — the disk is asked about those
 * alone (a root of this OS's own spelling is this side's whatever the disk says), and a root it cannot answer for is
 * not claimed as this side's. `isDirectory` is handed in, so a test is the same on any machine.
 *
 * <p>The residual, stated where it is decided: a root-relative Windows path names a folder on the CURRENT drive, and
 * the extension host's current drive and the server's need not be the same one.</p>
 */
export async function existingHere(
  roots: readonly string[],
  windows: boolean,
  isDirectory: (path: string) => Promise<boolean>,
): Promise<readonly string[]> {
  const asked = roots.filter((root) => spelledForTheOtherOs(root, windows));
  const answers = await Promise.all(asked.map((root) => isDirectory(root.trim()).catch(() => false)));

  return asked.filter((_root, at) => answers[at] === true);
}

/** The sentence beside a stored root of the other side — said, never drawn as a refusal. */
export function otherSideNote(windows: boolean): string {
  return windows
    ? 'a Linux, WSL or macOS path and no folder on this machine — the other side\'s folder: its server reads it there, and this Windows side skips it'
    : 'a Windows path and no folder on this machine — the other side\'s folder: its server reads it there, and this side skips it';
}
