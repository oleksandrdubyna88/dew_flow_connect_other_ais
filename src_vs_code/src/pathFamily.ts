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
 * `work` on the system drive (`qualified`), and one that exists is this side's. `shared/path-family-vectors.json` is answered by this
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
 * folder `work` (on the system drive, `qualified`), and such a folder is this side's. Answered by the `existence` vectors.
 *
 * <p>`unknownHere` is the page's alone: a root the disk could not answer for (EACCES, EBUSY) is never classified as the
 * other side's on a guess (the code round, 2026-10-09).</p>
 */
export function otherSideHere(path: string, windows: boolean, existsHere: boolean, unknownHere = false): boolean {
  return !existsHere && !unknownHere && spelledForTheOtherOs(path, windows);
}

/**
 * Which of `roots` are spelled for the other OS yet ARE directories on this machine — the disk is asked about those
 * alone (a root of this OS's own spelling is this side's whatever the disk says), at the place the server looks for
 * them ({@link qualified}: a root-relative Windows root on the system drive), ONE root at a time (the list is short, and
 * a slow share must not be asked six times at once), and a root it cannot answer for is not claimed as this side's.
 * The roots come back as they are stored, so the page can match them. `isDirectory` is handed in, so a test is the
 * same on any machine.
 */
export async function existingHere(
  roots: readonly string[],
  windows: boolean,
  systemDrive: string,
  isDirectory: (path: string) => Promise<boolean>,
): Promise<Existence> {
  const existing: string[] = [];
  const unknown: string[] = [];
  for (const root of roots.filter((one) => spelledForTheOtherOs(one, windows))) {
    const answer = await isDirectory(qualified(root, windows, systemDrive)).then((is) => (is ? 'here' : 'absent'), () => 'unknown');
    if (answer === 'here') {
      existing.push(root);
    } else if (answer === 'unknown') {
      unknown.push(root);
    }
  }

  return { existing, unknown };
}

/** What the disk said about the roots it was asked about: folders here, and roots it could not answer for. */
export interface Existence {
  readonly existing: readonly string[];
  readonly unknown: readonly string[];
}

/**
 * Whether a path is a directory, by a `stat`: true or false when the disk SAID so — a file, or ENOENT / ENOTDIR, is
 * "no folder here" — and a rejection for anything it could not tell (EACCES, EBUSY, EPERM, EIO…), which
 * {@link existingHere} keeps apart as unknown rather than reading as absent (the code round, 2026-10-09).
 */
export function directoryAt(stat: (path: string) => Promise<{ isDirectory(): boolean }>): (path: string) => Promise<boolean> {
  return async (path) => {
    try {
      return (await stat(path)).isDirectory();
    } catch (failure) {
      if (ABSENT.has(String((failure as { code?: unknown }).code ?? ''))) {
        return false;
      }
      throw failure;
    }
  };
}

/** The two stat errors that mean there is no folder at that path — every other one means the disk did not say. */
const ABSENT: ReadonlySet<string> = new Set(['ENOENT', 'ENOTDIR']);

/**
 * The root as this side looks for it — the server's `QuestionRoots.Qualified`: on Windows a root-relative root (one
 * leading `/`, or a `\` not followed by another) is qualified with the SYSTEM drive (`/work` on `D:` → `D:\work`);
 * everything else, and every root elsewhere, is the root trimmed. Never the CURRENT drive: this window's host and the
 * server need not stand on the same one, and one explicit base keeps the page and the server deciding alike (the code
 * round, 2026-10-09). Answered by the `resolution` vectors.
 */
export function qualified(root: string, windows: boolean, systemDrive: string): string {
  const text = root.trim();

  return windows && /^[\\/](?![\\/])/.test(text) ? systemDrive + text.replace(/\//g, '\\') : text;
}

/** The environment's system drive, or `C:` when it says none — the server's `SystemPlaces.SystemDriveOf`. */
export function systemDriveOf(value: string | undefined): string {
  return value === undefined || value.trim().length === 0 ? 'C:' : value.trim();
}

/**
 * What the disk said about the stored roots, KEPT: a panel repaints often, and a repaint must not stat. The answer is
 * keyed by the platform, the system drive and the root list — any of them changing asks again — and {@link forget}
 * drops it when the roots setting changes, so a folder created since is found on the next paint.
 */
export class RootExistence {
  private kept: { readonly key: string; readonly answer: Promise<Existence> } | undefined;

  constructor(private readonly isDirectory: (path: string) => Promise<boolean>) {}

  of(roots: readonly string[], windows: boolean, systemDrive: string): Promise<Existence> {
    const key = JSON.stringify([windows, systemDrive, roots]);
    if (this.kept?.key !== key) {
      const kept = { key, answer: existingHere(roots, windows, systemDrive, this.isDirectory) };
      this.kept = kept;
      // An answer holding an UNKNOWN root is not kept: the next render asks the disk again.
      void kept.answer.then((answer) => {
        if (answer.unknown.length > 0 && this.kept === kept) {
          this.kept = undefined;
        }
      });
    }

    return this.kept.answer;
  }

  forget(): void {
    this.kept = undefined;
  }
}

/** The sentence beside a stored root of the other side — said, never drawn as a refusal. */
export function otherSideNote(windows: boolean): string {
  return windows
    ? 'a Linux, WSL or macOS path and no folder on this machine — the other side\'s folder: its server reads it there, and this Windows side skips it'
    : 'a Windows path and no folder on this machine — the other side\'s folder: its server reads it there, and this side skips it';
}
