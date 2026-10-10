/**
 * Every setting that holds a PATH on this machine — the registry `todo/PLAN_paths_per_side.md` E1.1 asks for.
 *
 * <p><b>Why a registry.</b> VS Code's user settings are shared by a WSL window and a plain Windows window on one machine,
 * and each side runs its own `coai-mcp` (operator, 2026-10-09). A path written on one side is read on the other, so each
 * path setting has to answer the same questions: what kind of path it is, whether it must already exist, which half of
 * the product says something about it, and what this side does with a value spelled for the OTHER operating system
 * (`pathFamily.ts`). Before this file those answers lived in six modules and nobody could list them; the census test
 * (`pathSettings.test.ts`) walks the manifest's `contributes.configuration` and fails on a `*Path` / `*Directory` /
 * `*Roots` / `*Directories` setting or nested field that is not here, so the next one cannot arrive unseen.</p>
 *
 * <p>Pure data and `vscode`-free: the page, the wire writers and the tests all read it.</p>
 */

/** What a path names: one folder, one file (a CLI), or a list of folders. */
export type PathKind = 'folder' | 'file' | 'folderList';

/**
 * Which half of the product says something about the value — the table in the plan's design (e). `server`: coai-mcp
 * judges it and the extension relays its answer; `extension`: the extension reads it or stats it itself; `nobody`: a
 * legacy field no page draws, only skipped at launch when it is the other side's.
 */
export type SaidBy = 'server' | 'extension' | 'nobody';

/** One path field, and what this product does with it. */
export interface PathSetting {
  /**
   * The field as the census spells it: the setting itself (`coai.qconsultRoots`), a field of every row of a list
   * (`coai.vendors[].executablePath`), or a field of every entry of a map (`coai.consultants.*.executablePath`).
   */
  readonly id: string;
  /** The `coai.*` setting the field lives in — declared by the manifest, whatever its item schema says. */
  readonly setting: string;
  readonly kind: PathKind;
  /**
   * Whether the value must already exist to be of any use. A CLI path or a watched folder that is not there does
   * nothing; a data directory's NAMED root must exist (decided with the operator, D3); the default location is created
   * lazily and is never in this registry.
   */
  readonly mustExist: boolean;
  readonly saidBy: SaidBy;
  /** Where on the Settings page the value is drawn, or empty when no page draws it. */
  readonly place: string;
  /** How the value is named in a sentence — `folder`, `CLI`, `data folder`. */
  readonly noun: string;
  /** What the other side does with it, said in the "the other side's" note — `its server reads it there`. */
  readonly there: string;
  /** What THIS side does instead of using it, appended after "skips it" — empty when it simply goes without. */
  readonly instead: string;
  /**
   * Whether the other-side decision also asks the disk: a question-consultant root spelled like the other OS that IS a
   * folder here is this side's (`otherSideHere`). Every other path is decided by its spelling alone.
   */
  readonly asksDisk: boolean;
}

/** A CLI path in a legacy definition: no page draws it, and the catalog migration retires it. */
function legacyCli(id: string, setting: string): PathSetting {
  return {
    id, setting, kind: 'file', mustExist: true, saidBy: 'nobody', place: '', noun: 'CLI',
    there: 'the server on that side runs it', instead: ' and looks the CLI up on PATH', asksDisk: false,
  };
}

/** The question consultant's disk roots — the setting the rule was first written for. */
export const QCONSULT_ROOTS: PathSetting = {
  id: 'coai.qconsultRoots', setting: 'coai.qconsultRoots', kind: 'folderList', mustExist: true, saidBy: 'server',
  place: 'Consultants › Question consultant', noun: 'folder', there: 'its server reads it there', instead: '', asksDisk: true,
};

/** A reviewer row's CLI — the one scalar path still drawn, on its Models card. */
export const VENDOR_EXECUTABLE: PathSetting = {
  id: 'coai.vendors[].executablePath', setting: 'coai.vendors', kind: 'file', mustExist: true, saidBy: 'extension',
  place: 'Models', noun: 'CLI', there: 'the server on that side runs it', instead: ' and looks the CLI up on PATH', asksDisk: false,
};

/** Another installation's data folder, whose questions this window also answers. */
export const WATCHED_DIRECTORIES: PathSetting = {
  id: 'coai.alsoWatchDataDirectories', setting: 'coai.alsoWatchDataDirectories', kind: 'folderList', mustExist: true,
  saidBy: 'extension', place: 'Setup › MCP server', noun: 'data folder', there: 'a window on that side answers its questions',
  instead: '', asksDisk: false,
};

/** Where this window keeps its data — the SHARED layer is the one another side's value can reach. */
export const DATA_DIRECTORY: PathSetting = {
  id: 'coai.dataDirectory', setting: 'coai.dataDirectory', kind: 'folder', mustExist: true, saidBy: 'extension',
  place: 'Setup › MCP server', noun: 'data folder', there: 'a window on that side keeps its data there',
  instead: ' and uses the next choice', asksDisk: false,
};

/** Every path field this product reads — seven fields in seven settings (2026-10-09). */
export const PATH_SETTINGS: readonly PathSetting[] = [
  QCONSULT_ROOTS,
  VENDOR_EXECUTABLE,
  legacyCli('coai.consultants.*.executablePath', 'coai.consultants'),
  legacyCli('coai.qconsultRows[].executablePath', 'coai.qconsultRows'),
  legacyCli('coai.chatModelPresets[].executablePath', 'coai.chatModelPresets'),
  WATCHED_DIRECTORIES,
  DATA_DIRECTORY,
];

/** The name a path field is recognised by — what the census looks for in the manifest. */
export const PATH_FIELD_NAME = /(?:Path|Directory|Roots|Directories)$/;
