import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

/**
 * The inventory of every writer that MAKES a directory (todo/PLAN_paths_per_side.md E5.0 (b)), derived by a scan rather
 * than remembered: each one is guarded by the storage-write policy (`findingsBoard.refuseUnderMissingRoot`, asked before
 * its `mkdir`), or carries the reason it can never create a missing NAMED data root — it makes a folder inside a store
 * that already exists, the folder a person just chose, the extension's own storage, or a temp folder of its own.
 *
 * <p>Why a scan: the policy was verified on E1 as "the notice about a missing data directory creates it" — one writer
 * found by reading. A guard applied at the sites somebody remembered is the defect `security.md` counts four of; the
 * list below is the one the scan produces, and a new `mkdir` is red here until it is classified.</p>
 */

type Guard =
  /** Asks `refuseUnderMissingRoot` before it makes the directory. */
  | 'board'
  /** Makes a folder inside a store that already exists — the store's own root is somebody else's to make. */
  | 'inside an existing store'
  /** Makes the folder a person just picked in the system dialog — the one writer ALLOWED to make a named root. */
  | 'the folder a person chose'
  /** Writes into VS Code's own storage for this extension, never the data directory. */
  | 'extension storage'
  /** Writes into a temp folder of its own. */
  | 'a temp folder';

interface Writer {
  readonly sites: number;
  readonly guard: Guard;
  readonly why: string;
}

/** Every shipped file that makes a directory, its sites and how each cannot create a missing named root. */
const WRITERS: Readonly<Record<string, Writer>> = {
  'atomicFile.ts': { sites: 2, guard: 'board', why: 'the shared atomic write: the chat store, the probe cache, the roles and commands pages, the orphan ledger and a backup go through it' },
  'chatFastSettings.ts': { sites: 1, guard: 'a temp folder', why: 'the fast-mode marker files live in a mkdtemp folder of this process, never under the data directory' },
  'chatStoreFile.ts': { sites: 1, guard: 'inside an existing store', why: 'the quarantine folder is made inside the store holding the record being quarantined' },
  'chatStoreKeeper.ts': { sites: 1, guard: 'inside an existing store', why: 'the same quarantine folder, from the keeper' },
  'chatStoreLock.ts': { sites: 1, guard: 'board', why: 'a claim makes the store directory before its lock file; the store sits under the data directory' },
  'claudeProbeCache.ts': { sites: 1, guard: 'board', why: 'the probe cache is written under the data directory on every probe, with nobody acting' },
  'configTransferCommands.ts': { sites: 1, guard: 'board', why: 'an import backs the setup up under the data directory first' },
  'consultPromptFile.ts': { sites: 1, guard: 'board', why: 'a prompt override is written under the data directory from a keystroke pause' },
  'dataCommands.ts': { sites: 1, guard: 'the folder a person chose', why: 'the move flow creates the landing folder a person picked in the system dialog — the destination exists; its side folder is made inside it' },
  'extension.ts': { sites: 1, guard: 'board', why: 'the settings file and its lock — one road, createDataDirectory, for both' },
  'installer.ts': { sites: 2, guard: 'extension storage', why: 'the binary and its download scratch live in globalStorageUri, never in the data directory' },
  'jsonlLedger.ts': { sites: 1, guard: 'board', why: 'the notifications, seen, chat-usage and door ledgers all append through it' },
  'roundsDbRead.ts': { sites: 1, guard: 'extension storage', why: 'the --findings-many keys file is written to the folder the caller names: the extension\'s own storage' },
  'securityPromptEditor.ts': { sites: 1, guard: 'board', why: 'opening a prompt override for editing creates the empty file under the data directory' },
  'teamServerAuth.ts': { sites: 1, guard: 'board', why: 'a sign-in writes the token file under the data directory' },
};

const SOURCE = path.join(__dirname, '..', '..', 'src');

/** A line of code, not of prose: docstrings and line comments are dropped, as `aCliPathOfTheOtherSideIsSkipped.test.ts` drops them. */
function codeOf(file: string): string {
  return fs.readFileSync(path.join(SOURCE, file), 'utf8').split('\n').filter((line) => !/^\s*(?:\*|\/\/|\/\*\*)/.test(line)).join('\n');
}

/** The calls that make a directory: Node's two, and VS Code's. */
const MAKES_A_DIRECTORY = /\b(?:mkdir|mkdirSync|createDirectory)\(/g;

function sitesIn(file: string): number {
  return (codeOf(file).match(MAKES_A_DIRECTORY) ?? []).length;
}

/** Every shipped source file, as the census of CLI-path reads walks them. */
function shippedSources(): readonly string[] {
  return fs.readdirSync(SOURCE).filter((name) => name.endsWith('.ts') && !name.endsWith('.generated.ts')).sort();
}

test('every writer that makes a directory is in the inventory with its sites — a new mkdir is red until it is classified', () => {
  const found = Object.fromEntries(shippedSources().map((file) => [file, sitesIn(file)] as const).filter(([, sites]) => sites > 0));
  const expected = Object.fromEntries(Object.entries(WRITERS).map(([file, one]) => [file, one.sites]));

  assert.deepEqual(found, expected, 'a directory-making call site appeared, moved or went: classify it in WRITERS, with its guard or its reason');
});

test('every writer guarded by the board actually asks it before it makes the directory', () => {
  for (const [file, one] of Object.entries(WRITERS).filter(([, writer]) => writer.guard === 'board')) {
    assert.ok(codeOf(file).includes('refuseUnderMissingRoot('), `${file} is classified as guarded by the board and never calls refuseUnderMissingRoot — ${one.why}`);
  }
});

test('a writer classified as making no named root does NOT carry the guard — a guard there would say the folder can be missing when it cannot', () => {
  for (const [file] of Object.entries(WRITERS).filter(([, writer]) => writer.guard !== 'board')) {
    assert.equal(codeOf(file).includes('refuseUnderMissingRoot('), false, `${file} carries the guard but is classified as never creating a named root`);
  }
});

test('the scan still finds the ledger\'s mkdir and the atomic write\'s two — a scan that matched nothing would pass forever', () => {
  assert.equal(sitesIn('jsonlLedger.ts'), 1);
  assert.equal(sitesIn('atomicFile.ts'), 2);
  assert.ok(Object.values(WRITERS).some((one) => one.guard === 'board'), 'nothing is guarded, so the policy guards nothing');
});
