import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, test } from 'node:test';

import { writeFileAtomically, writeFileAtomicallySync } from '../atomicFile';
import { ChatStoreFile } from '../chatStoreFile';
import { ChatStoreKeeper } from '../chatStoreKeeper';
import { claimConversation } from '../chatStoreLock';
import { NOTHING_CHOSEN, useStorageSettings } from '../dataDir';
import { makeDataDirectory, makeDataDirectorySync } from '../dataDirMaker';
import { forgetFindingsBoard } from '../findingsBoard';
import { appendLine } from '../jsonlLedger';
import { ensureOverride } from '../securityPromptOverride';
import { writeToken } from '../teamServerAuth';
import { UNDER_THE_DATA_DIRECTORY, WRITERS, codeOf } from './dataDirWriters';

/**
 * Every writer that can make a folder under the data directory, RUN with a named root that is not there — and the root
 * is still not there afterwards (research/PLAN_paths_per_side.md E5.7; the gate's plan-round item B: "text in a file proves
 * nothing about call order or effect"). The inventory scan (`everyWriterOfTheDataDirectory.test.ts`) says which files
 * those are; this runs each one.
 *
 * <p><b>The board knows nothing here</b> — no finding published, no absence kept. That is the worst case: the moment
 * after a settings change, or the root removed after the last probe. What holds the root absent is the one maker
 * (`dataDirMaker.ts`), which never makes a named root, so each writer must be proven on the disk, not on its guard.</p>
 *
 * <p>Two writers need an editor to run and are held otherwise, each said: the settings file (`extension.ts`), which the
 * host's second launch runs against a missing root (`missingDataRootScenario.ts`), and the import's backup
 * (`configTransferCommands.ts`), whose one directory call is the maker (the scan holds it to that).</p>
 */

type Run = (root: string) => Promise<void>;

/** Held without a run — a file that imports `vscode` cannot run here; the reason says what holds it instead. */
interface HeldElsewhere {
  readonly needsAnEditor: string;
}

const quietly = async (work: () => Promise<unknown>): Promise<void> => {
  await work().catch(() => undefined);
};

const syncQuietly = (work: () => void): void => {
  try {
    work();
  } catch {
    // Refused — which is the point; the assertion is on the disk.
  }
};

const RUNS: Readonly<Record<string, Run | HeldElsewhere>> = {
  'atomicFile.ts': async (root) => {
    await quietly(() => writeFileAtomically(join(root, 'window-1.json'), '{}'));
    syncQuietly(() => { writeFileAtomicallySync(join(root, 'orphans.json'), '{}'); });
  },
  'chatStoreFile.ts': async (root) => { await quietly(() => new ChatStoreFile(join(root, 'conversations')).forget('a1b2c3')); },
  'chatStoreKeeper.ts': async (root) => { await quietly(() => new ChatStoreKeeper(join(root, 'conversations')).quarantineOrphanRecord('a1b2c3', Date.now())); },
  'chatStoreLock.ts': async (root) => { await quietly(() => claimConversation(join(root, 'conversations'), 'a1b2c3', 'a test')); },
  'configTransferCommands.ts': { needsAnEditor: 'the import\'s backup: its one directory call is makeDataDirectory, which this file runs below' },
  'dataDirMaker.ts': async (root) => {
    await quietly(() => makeDataDirectory(root));
    await quietly(() => makeDataDirectory(join(root, 'windows', 'consultations')));
    syncQuietly(() => { makeDataDirectorySync(join(root, 'windows')); });
  },
  'extension.ts': { needsAnEditor: 'the settings file and its lock: run against a missing named root by the host\'s second launch (missingDataRootScenario.ts)' },
  'jsonlLedger.ts': async (root) => { await appendLine(join(root, 'notifications.jsonl'), '{}\n', 'a test line', { chain: 'notifications' }); },
  'securityPromptOverride.ts': async (root) => { await quietly(() => ensureOverride(join(root, 'prompts', 'security', 'lane.md'))); },
  'teamServerAuth.ts': async (root) => { await quietly(() => writeToken(root, 'https://team.example', 'a-token')); },
};

let parent = '';
let root = '';

beforeEach(() => {
  forgetFindingsBoard();
  parent = mkdtempSync(join(tmpdir(), 'coai-writers-'));
  root = join(parent, 'named-but-missing');
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);
});

afterEach(() => {
  useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
  forgetFindingsBoard();
  rmSync(parent, { recursive: true, force: true });
});

test('the runs cover exactly the writers the scan classifies as writing under the data directory', () => {
  const underIt = Object.entries(WRITERS).filter(([, one]) => UNDER_THE_DATA_DIRECTORY.includes(one.guard)).map(([file]) => file).sort();

  assert.deepEqual(Object.keys(RUNS).sort(), underIt, 'a writer under the data directory has no run here, or a run names a writer the scan no longer classifies so');
});

test('a writer held without a run really needs an editor — the exemption cannot be taken by a file that could run here', () => {
  for (const [file] of Object.entries(RUNS).filter(([, run]) => typeof run !== 'function')) {
    assert.match(codeOf(file), /from 'vscode'/u, `${file} is held without a run but does not import vscode — run it`);
  }
});

for (const [file, run] of Object.entries(RUNS)) {
  if (typeof run !== 'function') {
    continue;
  }
  test(`${file}: run under a missing named root, with the board knowing nothing, leaves the root absent`, async () => {
    await run(root);

    assert.equal(existsSync(root), false, `${file} created the missing named data root`);
  });
}
