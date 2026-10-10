import { QCONSULT_ROOTS, type PathSetting } from './pathSettings';

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

/**
 * The stat errors that mean there is no folder at that path — every other one means the disk did not say. ERR_INVALID_ARG_VALUE
 * is Node refusing a name no file system can spell (a NUL): such a root can never be this machine's folder, which is what the
 * server's probe answers too (the fourth code round). A name Windows alone cannot spell (`<`) already comes back ENOENT.
 */
const ABSENT: ReadonlySet<string> = new Set(['ENOENT', 'ENOTDIR', 'ERR_INVALID_ARG_VALUE']);

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
 * How long a DEFINITIVE answer about the roots is kept: a folder created or removed after a paint is seen within this,
 * as the server sees it on its next start (the fourth code round). An answer holding an unknown root is never kept.
 */
export const ROOT_ANSWER_LIFETIME_MS = 60_000;

/**
 * What the disk said about the stored roots, KEPT: a panel repaints often, and a repaint must not stat. The answer is
 * keyed by the platform, the system drive and the root list — any of them changing asks again — lives
 * {@link ROOT_ANSWER_LIFETIME_MS}, and {@link forget} drops it when the roots setting changes, the settings are mirrored
 * to the server, or the server is (re)installed.
 */
export class RootExistence {
  private kept: { readonly key: string; readonly at: number; readonly answer: Promise<Existence> } | undefined;

  constructor(private readonly isDirectory: (path: string) => Promise<boolean>, private readonly now: () => number = Date.now) {}

  of(roots: readonly string[], windows: boolean, systemDrive: string): Promise<Existence> {
    const key = JSON.stringify([windows, systemDrive, roots]);
    const kept = this.kept;

    return kept !== undefined && kept.key === key && this.now() - kept.at < ROOT_ANSWER_LIFETIME_MS
      ? kept.answer
      : this.asked(key, roots, windows, systemDrive);
  }

  forget(): void {
    this.kept = undefined;
  }

  /** The disk asked again, and the answer kept — unless it holds an UNKNOWN root: the next render then asks again. */
  private asked(key: string, roots: readonly string[], windows: boolean, systemDrive: string): Promise<Existence> {
    const kept = { key, at: this.now(), answer: existingHere(roots, windows, systemDrive, this.isDirectory) };
    this.kept = kept;
    void kept.answer.then((answer) => {
      if (answer.unknown.length > 0 && this.kept === kept) {
        this.kept = undefined;
      }
    });

    return kept.answer;
  }
}

/**
 * The two families a side's paths are spelled in (decided with the operator, D1): `windows` for a host on Windows,
 * `posix` for every other — WSL, an SSH remote, Linux and macOS alike.
 */
export type PathFamily = 'windows' | 'posix';

/** The family of a host by its `process.platform` — the caller hands it in, so this module stays pure. */
export function familyOf(platform: string): PathFamily {
  return platform === 'win32' ? 'windows' : 'posix';
}

/** A stored CLI path as a row holds it — the legacy single value every extension version reads. */
export interface StoredExecutable {
  readonly executablePath: string;
}

/** What this side does with a stored CLI path: the path it runs (empty = PATH lookup), and the value it skipped. */
export interface ThisSidePath {
  readonly path: string;
  /** The stored value, trimmed, when it was spelled for the other OS and so skipped — empty otherwise. */
  readonly otherSide: string;
}

/**
 * The CLI path THIS side runs (todo/PLAN_paths_per_side.md, design (c), rules 2-3): the stored `executablePath`
 * trimmed, unless it is spelled for the other operating system — then nothing, so the runtime's own name is looked up
 * on PATH, and the skipped value is named so a page can say whose it is. Never refused: a Windows `codex.cmd` read in a
 * WSL window is the Windows side's CLI, not a broken setting. The spelling alone decides — a CLI path is not asked of
 * the disk. Answered by the `executable` vectors, which coai-mcp's `ExecutablePaths.Here` answers too.
 */
export function pathForThisSide(stored: StoredExecutable, family: PathFamily): ThisSidePath {
  const value = stored.executablePath.trim();

  return spelledForTheOtherOs(value, family === 'windows') ? { path: '', otherSide: value } : { path: value, otherSide: '' };
}

/** {@link pathForThisSide}'s path alone — for a launch or a wire field that needs only what to run. */
export function executableHere(path: string, family: PathFamily): string {
  return pathForThisSide({ executablePath: path }, family).path;
}

/**
 * The sentence beside a stored value of the other side — said in the quiet hint tone, never drawn as a refusal
 * (todo/PLAN_paths_per_side.md E1.5). One sentence for every path setting, its words taken from the registry
 * (`pathSettings.ts`): what the value is, what the other side does with it, and what this side does instead. A setting
 * whose decision also asked the disk (a question-consultant root) says there is no such folder here.
 */
export function otherSideNote(windows: boolean, setting: PathSetting = QCONSULT_ROOTS): string {
  const side = windows ? ON_WINDOWS : ELSEWHERE;
  const absent = setting.asksDisk ? ` and no ${setting.noun} on this machine` : '';

  return `${side.spelling}${absent} — the other side's ${setting.noun}: ${setting.there}, and ${side.thisSide} skips it${setting.instead}`;
}

/** How each side names the other's spelling, and itself, in {@link otherSideNote}. */
const ON_WINDOWS = { spelling: 'a Linux, WSL or macOS path', thisSide: 'this Windows side' } as const;
const ELSEWHERE = { spelling: 'a Windows path', thisSide: 'this side' } as const;
