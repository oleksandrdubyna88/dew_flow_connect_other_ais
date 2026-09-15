import { SIDE_GRAMMAR, usableSideName } from './dataDir';

/**
 * What the install flow asks about the data directory, decided without `vscode`.
 *
 * <p>The dialogs are `extension.ts`'s — a quick pick, a folder dialog, an input box. Everything they
 * DECIDE is here, because the panel and its commands cannot be instantiated under the test runner
 * and a decision behind a dialog is a decision nobody checks.</p>
 *
 * <p><b>The load-bearing asymmetry.</b> Adopting a folder and moving into one are opposite rules
 * about the same question. A folder that already holds a database is what adoption is FOR — it is a
 * reinstalled machine finding its own history — and it is exactly what a move must refuse, because
 * copying over it destroys that history. Each rule is stated where it belongs and neither is
 * expressed as "the destination is checked".</p>
 */

/** The window doing the asking, in the four facts that decide what it is called. */
export interface WindowIdentity {
  /** `vscode.env.remoteName` — `wsl`, `ssh-remote`, … or empty in a local window. */
  readonly remoteName: string;
  /** `WSL_DISTRO_NAME`, which the subsystem sets inside every distro. */
  readonly distro: string;
  /** `os.hostname()`. */
  readonly hostname: string;
  /** `process.platform`. */
  readonly platform: string;
}

/** What one entry of a chosen folder is, as far as this decision cares. */
export interface FolderEntry {
  readonly name: string;
  readonly isDirectory: boolean;
  /** Whether a `coai.db` sits inside it. */
  readonly hasDatabase: boolean;
}

/** What a chosen folder already holds. */
export interface FolderReport {
  /** A `coai.db` directly in the folder — the layout from before sides existed. */
  readonly hasDatabase: boolean;
  /** Subdirectories that hold a database of their own, so each is somebody's side. */
  readonly sides: readonly string[];
}

/**
 * A name for this installation inside a shared folder, offered rather than imposed.
 *
 * <p><b>Two parts, and both are needed.</b> The kind of window says what it is — a Windows desktop,
 * a WSL distro — and the machine says which one. `windows` alone collides the moment a second
 * machine is pointed at the same NAS, and a collision here is two installations writing one SQLite
 * file, which is the whole thing a side exists to prevent.</p>
 *
 * <p>A WSL window uses its DISTRO rather than its host, because two distros on one machine are two
 * installations and share a hostname.</p>
 *
 * <p>Whatever comes out is put through the server's own grammar: a machine called `Ada's MacBook`
 * must not be offered as a side the server then refuses to start on.</p>
 */
export function defaultSideName(window: WindowIdentity): string {
  const remote = window.remoteName.trim();
  const distro = window.distro.trim();
  const host = window.hostname.trim();

  const parts = remote.length === 0
    ? [platformName(window.platform), host]
    : [remote, distro.length > 0 ? distro : host];

  const offered = pathSafe(parts.filter((part) => part.length > 0).join('-'));

  // A machine whose every character the grammar refuses still needs a name, and an empty one would
  // mean "no side" — which is not what was asked for.
  return offered.length > 0 ? offered : 'this-side';
}

/** What a platform is called in a side name. Anything unknown keeps its own id, sanitised. */
function platformName(platform: string): string {
  const known: Readonly<Record<string, string>> = { win32: 'windows', darwin: 'macos', linux: 'linux' };

  return known[platform] ?? platform;
}

/**
 * The grammar's own transformation: lower-case, and every character it refuses becomes a dash.
 *
 * <p>Runs of dashes collapse and the ends are trimmed, so `Ada’s MacBook Pro` is `ada-s-macbook-pro`
 * rather than `ada-s-macbook-pro---`. A name is read by people as well as by `Path.Combine`.</p>
 */
function pathSafe(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9._-]+/gu, '-')
    .replace(/-{2,}/gu, '-')
    .replace(/^[-.]+|[-.]+$/gu, '');
}

/**
 * Why a typed side name cannot be used, or empty when it can.
 *
 * <p>Empty INPUT is not a refusal: no side is a legitimate answer, and it means the folder is not
 * divided. The check is made on the trimmed, lower-cased value, because that is the form the server
 * compares — somebody who types `  Windows  ` has typed a usable name.</p>
 */
export function sideRefusal(side: string): string {
  const asked = side.trim().toLowerCase();
  if (asked.length === 0 || usableSideName(asked)) {
    return '';
  }

  return `A side may contain ${SIDE_GRAMMAR}, and "${side.trim()}" does not — `
    + 'the server refuses to start on a name it cannot use rather than quietly sharing one folder.';
}

/**
 * Which subdirectories of a chosen folder are somebody's side.
 *
 * <p>A directory holding a `coai.db` — and nothing else. `worktrees/` and `servers/` sit in exactly
 * the same place and are not sides; offering the first would hand somebody a scratch checkout as
 * their history. A directory whose NAME the grammar refuses is not offered either: it could not be
 * asked for, so an offer of it produces a configuration the server will not start on.</p>
 */
export function sidesIn(entries: readonly FolderEntry[]): string[] {
  return entries
    .filter((entry) => entry.isDirectory && entry.hasDatabase && usableSideName(entry.name))
    .map((entry) => entry.name);
}

/**
 * What a person is told about the folder they just chose, BEFORE anything is saved.
 *
 * <p>This is the screen that decides whether somebody notices. Two sentences, and which one they
 * read has to follow what is actually on the disk: "this already holds a history" is the reinstall
 * case working exactly as intended, and "this starts with no history" is the moment to stop and
 * check the path. Saying the first about an empty folder is how a person walks past the last chance
 * to find out their rounds are somewhere else.</p>
 */
export function adoptionSentence(folder: string, found: FolderReport): string {
  if (found.hasDatabase) {
    return `${folder} already holds a database, and this installation will go on writing to it — `
      + 'which is what continues a history across a reinstalled machine. Nothing in it is moved or '
      + 'overwritten by choosing it.';
  }

  if (found.sides.length > 0) {
    return `${folder} is already shared by ${found.sides.length === 1 ? 'one installation' : `${found.sides.length} installations`}: `
      + `${found.sides.join(', ')}. Name this one the same as the side whose history you are `
      + 'continuing, or give it a name of its own to start beside them.';
  }

  return `${folder} has no history in it yet, so this installation starts with none. If that is a `
    + 'surprise, check the path before recording into it — a history you are looking for is in some '
    + 'other folder.';
}
