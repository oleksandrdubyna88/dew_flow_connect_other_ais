import fs from 'node:fs';
import path from 'node:path';

/**
 * The inventory of every shipped site that can MAKE a directory (todo/PLAN_paths_per_side.md E5.0 (b), E5.7; the gate's
 * plan-round item B), derived by a scan rather than remembered, and shared by the two tests that hold it:
 * `everyWriterOfTheDataDirectory.test.ts` (the scan finds exactly these, each guarded as classified) and
 * `everyWriterLeavesAMissingRootAbsent.test.ts` (each writer RUN under a missing named root leaves it absent — text in a
 * file proves nothing about call order or effect).
 *
 * <p>A site is a call that makes a directory: Node's `mkdir`/`mkdirSync`, VS Code's `createDirectory`, the one maker
 * (`makeDataDirectory`/`makeDataDirectorySync`), and VS Code's `writeFile`/`rename`/`copy` — which make missing parent
 * folders, so a write through them recreated a root removed after the guard (the risk consultation's item F).</p>
 */

export type Guard =
  /** The one maker: the only raw `mkdir` of a path that may lie under the data directory — never a named root. */
  | 'the one maker'
  /** Waits for the board's first data-root answer, asks the guard, and makes through the one maker. */
  | 'board'
  /** Makes through the one maker without the board's wait: a folder inside a store that exists (the quarantine). */
  | 'through the maker'
  /** Makes the folder a person just picked in the system dialog — the move flow; consent, not a side effect. */
  | 'the folder a person chose'
  /** Writes into VS Code's own storage for this extension, never the data directory. */
  | 'extension storage'
  /** Writes into a temp folder of its own. */
  | 'a temp folder';

export interface Writer {
  readonly sites: number;
  readonly guard: Guard;
  readonly why: string;
}

/** Every shipped file that makes a directory, its sites and how each cannot create a missing named root. */
export const WRITERS: Readonly<Record<string, Writer>> = {
  'atomicFile.ts': { sites: 2, guard: 'board', why: 'the shared atomic write and its sync form: the chat store and its heartbeat, the probe cache, the prompt overrides, the roles and commands pages, the orphan ledger and a backup go through it' },
  'chatFastSettings.ts': { sites: 1, guard: 'a temp folder', why: 'the fast-mode marker files live in a mkdtemp folder of this process, never under the data directory' },
  'chatStoreFile.ts': { sites: 1, guard: 'through the maker', why: 'the quarantine folder inside the store holding the record being set aside — under a claim that asked the board' },
  'chatStoreKeeper.ts': { sites: 1, guard: 'through the maker', why: 'the same quarantine folder, from the keeper' },
  'chatStoreLock.ts': { sites: 1, guard: 'board', why: 'a claim makes the store directory before its lock file; the store sits under the data directory' },
  'configTransferCommands.ts': { sites: 1, guard: 'board', why: 'an import backs the setup up under the data directory first' },
  'dataCommands.ts': { sites: 2, guard: 'the folder a person chose', why: 'the move flow creates and fills <destination>/<side> inside the folder a person picked in the system dialog — the one intended exception' },
  'dataDirMaker.ts': { sites: 6, guard: 'the one maker', why: 'below a named root one non-recursive mkdir per level from the root down, the root itself never made; the default location and paths outside a named root recursively' },
  'extension.ts': { sites: 1, guard: 'board', why: 'the settings file and its lock — one road, createDataDirectory; the file itself is written and renamed with Node, never VS Code' },
  'installer.ts': { sites: 5, guard: 'extension storage', why: 'the binary and its download scratch live in globalStorageUri, never in the data directory' },
  'jsonlLedger.ts': { sites: 1, guard: 'board', why: 'the notifications, seen, chat-usage and door ledgers all append through it' },
  'roundsDbRead.ts': { sites: 1, guard: 'extension storage', why: 'the --findings-many keys file is written to the folder the caller names: the extension\'s own storage' },
  'securityPromptOverride.ts': { sites: 1, guard: 'board', why: 'opening a security prompt override for editing creates the empty file under the data directory' },
  'teamServerAuth.ts': { sites: 1, guard: 'board', why: 'a sign-in writes the token file under the data directory' },
};

/** The guards whose writers may make a folder under the data directory — each must be proven by a run, not by its text. */
export const UNDER_THE_DATA_DIRECTORY: readonly Guard[] = ['the one maker', 'board', 'through the maker'];

export const SOURCE = path.join(__dirname, '..', '..', 'src');

/** A line of code, not of prose: docstrings and line comments are dropped, as `aCliPathOfTheOtherSideIsSkipped.test.ts` drops them. */
export function codeOf(file: string): string {
  return fs.readFileSync(path.join(SOURCE, file), 'utf8').split('\n').filter((line) => !/^\s*(?:\*|\/\/|\/\*\*)/.test(line)).join('\n');
}

/** The calls that make a directory: Node's two, VS Code's, the one maker's two, and VS Code's writes that make parents. */
export const MAKES_A_DIRECTORY = /\b(?:mkdir|mkdirSync|createDirectory|makeDataDirectory|makeDataDirectorySync)\(|\bworkspace\.fs\.(?:writeFile|rename|copy)\(/g;

/** Only the one maker's calls — what a writer under the data directory may use. */
export const THROUGH_THE_MAKER = /\bmakeDataDirectory(?:Sync)?\(/g;

export function sitesIn(file: string): number {
  return (codeOf(file).match(MAKES_A_DIRECTORY) ?? []).length;
}

/** Every shipped source file, as the census of CLI-path reads walks them. */
export function shippedSources(): readonly string[] {
  return fs.readdirSync(SOURCE).filter((name) => name.endsWith('.ts') && !name.endsWith('.generated.ts')).sort((a, b) => a.localeCompare(b));
}
