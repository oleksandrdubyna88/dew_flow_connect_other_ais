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

/** A family as the pages name it (D1): **Windows**, and **WSL / Linux** for every POSIX side — a distro, an SSH remote, a Mac. */
export function familyLabel(family: PathFamily): string {
  return family === 'windows' ? 'Windows' : 'WSL / Linux';
}

/**
 * A row's CLI path PER SIDE (todo/PLAN_paths_per_side.md E3.1, design (b) O2): one value per family, each PRESENT only when
 * a save wrote it — an explicitly empty one is the PATH lookup — and `mirrored`, the legacy `executablePath` as the last
 * save by an extension that knows this field left it. Additive on the row: an older extension ignores it (and drops it
 * when it saves the list), and coai-mcp never sees it — the extension resolves this side's value before it writes the
 * server's settings file.
 */
export interface ExecutableBySide {
  readonly windows?: string;
  readonly posix?: string;
  readonly mirrored?: string;
}

/** The members of {@link ExecutableBySide}, each read by name — a hand edit of another shape is nothing. */
const BY_SIDE_MEMBERS = ['windows', 'posix', 'mirrored'] as const;

/** The stored per-side field as a record of its string members, trimmed, or nothing when it says nothing. */
export function bySideOf(raw: unknown): ExecutableBySide | undefined {
  if (!isPlainRecord(raw)) {
    return undefined;
  }
  const fields = raw;
  const said = BY_SIDE_MEMBERS.filter((name) => typeof fields[name] === 'string').map((name) => [name, String(fields[name]).trim()] as const);

  return said.length === 0 ? undefined : Object.fromEntries(said);
}

/** An object that is not an array — the only shape a per-side field can have. */
function isPlainRecord(raw: unknown): raw is Readonly<Record<string, unknown>> {
  return typeof raw === 'object' && raw !== null && !Array.isArray(raw);
}

/**
 * The per-side value THIS side's field holds — rule 1 of design (c): `executablePathBySide[family]` when the member is
 * PRESENT (an empty one included: it is the PATH lookup and final) — or nothing, when the legacy `executablePath` is the
 * value this side has: there is no member, or an older extension saved the legacy value since ({@link olderSaveFor}).
 */
function ownSidePath(stored: StoredExecutable, family: PathFamily): string | undefined {
  const own = stored.executablePathBySide?.[family];

  return own === undefined || olderSaveFor(stored, family) ? undefined : own.trim();
}

/**
 * Whether an extension that does not know `executablePathBySide` saved the legacy value since a newer one last wrote it
 * — the legacy value is not what the newer one recorded as `mirrored` (todo/PLAN_paths_per_side.md design (c), the
 * cadence consultation for epics 4-6) — AND that save is this family's: spelled for it, or for neither OS. Decided by
 * the SPELLING alone, so the page (which asks no disk) and the host draw and run the same value. A row no newer
 * extension saved has no `mirrored` and nothing to compare.
 */
function olderSaveFor(stored: StoredExecutable, family: PathFamily): boolean {
  const mirrored = stored.executablePathBySide?.mirrored;
  if (mirrored === undefined || mirrored.trim() === stored.executablePath.trim()) {
    return false;
  }

  return !spelledForTheOtherOs(stored.executablePath, family === 'windows');
}

/** A stored CLI path as a row holds it — the legacy single value every extension version reads, and the per-side ones. */
export interface StoredExecutable {
  readonly executablePath: string;
  readonly executablePathBySide?: ExecutableBySide | undefined;
}

/** What this side does with a stored CLI path: the path it runs (empty = PATH lookup), and the value it skipped. */
export interface ThisSidePath {
  readonly path: string;
  /** The stored value, trimmed, when it was spelled for the other OS and so skipped — empty otherwise. */
  readonly otherSide: string;
}

/** What the disk said about a FILE: there, confirmed absent (none, a directory, an unspellable name), or it could not tell. */
export type FilePresence = 'here' | 'absent' | 'unknown';

/**
 * The side a CLI path is judged on: its family, the drive a root-relative Windows path is qualified with, and the disk,
 * asked about a FILE synchronously — the wire writers are synchronous, and the only paths ever asked are this machine's
 * own spellings of the other OS's (`C:\usr\local\bin\codex` on Windows, a relative name in WSL), never a share.
 */
export interface ExecutableSide {
  readonly family: PathFamily;
  readonly systemDrive: string;
  readonly fileAt: (path: string) => FilePresence;
}

/**
 * The CLI path THIS side runs (todo/PLAN_paths_per_side.md, design (c), rules 1-3) — the value this side's field holds
 * (rule 1, {@link storedForThisSide}: the per-side member when it is present, else the legacy value), judged by ONE rule
 * wherever it is stored. A member is judged exactly as the legacy value is, so a save that pins a family's value into its
 * member never moves what that family runs (the risk consultant, 2026-10-11, finding c: `/tools/codex.exe` saved
 * unchanged on Windows ran unqualified, and a WSL save took away the `C:\tools\codex.exe` the Windows disk had found).
 *
 * <p>The rule — the question-consultant roots' (`otherSideHere`) applied to a FILE: the value trimmed, unless it is spelled for the other operating
 * system AND the disk confirms there is no such file here — then nothing, so the runtime's own name is looked up on
 * PATH, and the skipped value is named so a page can say whose it is. On Windows `/Program Files/nodejs/node.exe` is a
 * legal root-relative path; one that exists — qualified with the SYSTEM drive (`qualified`), the base both halves use
 * — is this side's and runs qualified, and one the disk could not answer for is kept too: never skipped on a guess (the
 * cadence consultant, E1's code round). Never refused. Answered by the `executable` vectors, which coai-mcp's
 * `ExecutablePaths.Here` answers too.</p>
 */
export function pathForThisSide(stored: StoredExecutable, side: ExecutableSide): ThisSidePath {
  return valueForThisSide(storedForThisSide(stored, side.family), side);
}

/** Rules 2-3 of design (c) over one stored value — what every side ran before E3, and what it runs of a member now. */
function valueForThisSide(stored: string, side: ExecutableSide): ThisSidePath {
  const value = stored.trim();
  if (!spelledForTheOtherOs(value, side.family === 'windows')) {
    return { path: value, otherSide: '' };
  }
  const here = qualified(value, side.family === 'windows', side.systemDrive);

  return side.fileAt(here) === 'absent' ? { path: '', otherSide: value } : { path: here, otherSide: '' };
}

/**
 * The ids of the rows whose CLI path this side skips as the other side's — decided by the HOST, which can ask the disk,
 * and handed to the page (`PanelState.otherSideClis`), which must not: the Settings page's modules bundle without the
 * host and reach no `node:fs`.
 */
export function otherSideClis(rows: readonly (StoredExecutable & { readonly id: string })[], side: ExecutableSide): readonly string[] {
  return rows.filter((row) => pathForThisSide(row, side).otherSide.length > 0).map((row) => row.id);
}

/** {@link pathForThisSide}'s path alone, for a LEGACY definition — a consultant's, a question row's, a chat preset's — which holds one value. */
export function executableHere(path: string, side: ExecutableSide): string {
  return pathForThisSide({ executablePath: path }, side).path;
}

/** {@link pathForThisSide}'s path alone, for a stored ROW — the per-side value when this side has one. What a launch runs. */
export function executableOf(stored: StoredExecutable, side: ExecutableSide): string {
  return pathForThisSide(stored, side).path;
}

/**
 * The STORED value of this side's CLI-path field, unresolved — what the one field draws, what a copy of a row into a
 * definition (a consultant's, a question row's, a chat model's) takes, so the copy is resolved later exactly as the row
 * would be: the per-side value when this side has one, else the legacy value, trimmed.
 */
export function storedForThisSide(stored: StoredExecutable, family: PathFamily): string {
  return ownSidePath(stored, family) ?? stored.executablePath.trim();
}

/**
 * The disk probe {@link ExecutableSide.fileAt} uses on THIS host — installed by activation (`useFileProbe`), the
 * `useStorageSettings` pattern: the Settings page's modules bundle without the host, so no module they reach may import
 * `node:fs`. Before it is installed every path is `unknown` — and an unknown path is KEPT, never skipped on a guess.
 */
let fileProbe: (path: string) => FilePresence = () => 'unknown';

/** Install how this host asks the disk about a file — `fileAt(statSync)` at activation; a test installs its own. */
export function useFileProbe(probe: (path: string) => FilePresence): void {
  fileProbe = probe;
}

/** The installed probe's answer for one path. */
export function installedFileProbe(path: string): FilePresence {
  return fileProbe(path);
}

/**
 * Whether a path is a FILE, by a synchronous `stat`: `here` for a file, `absent` when the disk SAID there is none — not
 * found, not a directory on the way, a directory where the file should be, or a name no file system can spell — and
 * `unknown` for anything it could not tell (EACCES, EBUSY, EIO), which is never read as absent. {@link directoryAt}'s
 * contract, for a file.
 */
export function fileAt(statSync: (path: string) => { isFile(): boolean }): (path: string) => FilePresence {
  return (path) => {
    try {
      return statSync(path).isFile() ? 'here' : 'absent';
    } catch (failure) {
      return presenceOnFailure(failure);
    }
  };
}

/** A failed `stat`: absent when the disk SAID there is nothing there, unknown for anything else. The probes (`pathProbes.ts`) map theirs through it too. */
export function presenceOnFailure(failure: unknown): FilePresence {
  return ABSENT.has(String((failure as { code?: unknown }).code ?? '')) ? 'absent' : 'unknown';
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
