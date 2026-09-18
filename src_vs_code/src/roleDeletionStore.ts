import { readdir, readFile, rm } from 'node:fs/promises';
import { writeFileAtomically } from './atomicFile';
import { Tombstone, TombstoneStore } from './roleDeletion';

/**
 * Where a deletion in progress is written down: one file per role id, under the data directory.
 *
 * <p><b>Not a VS Code setting.</b> A setting is mirrored to the server, so the tombstone would
 * itself be waiting on the mirror it exists to survive — the regress, exactly.</p>
 *
 * <p><b>Not the append-only ledger.</b> A tombstone must CLEAR, and nothing in that design is ever
 * rewritten. A directory whose entries are created and removed matches the lifetime.</p>
 *
 * <p><b>One file per id, not one index.</b> Two windows deleting two roles do not contend, and a
 * file that fails to parse strands ONE deletion rather than all of them — which is also why a
 * corrupt entry is skipped rather than thrown: a deletion nobody can read is a deletion the page
 * cannot show, and the other four must still finish.</p>
 */

/** A role id, as `roles.ts` generates them: a letter, then letters, digits and underscores. */
const ROLE_ID = /^[A-Za-z][A-Za-z0-9_]*$/;

/** Names Windows will not give a file, whatever the extension. Mirrors `rolesPrompts.ts`. */
const RESERVED = new Set([
  'con', 'prn', 'aux', 'nul',
  'com1', 'com2', 'com3', 'com4', 'com5', 'com6', 'com7', 'com8', 'com9',
  'lpt1', 'lpt2', 'lpt3', 'lpt4', 'lpt5', 'lpt6', 'lpt7', 'lpt8', 'lpt9',
]);

export function deletionsDir(dataDir: string): string {
  return `${dataDir}/deletions`;
}

/**
 * The file this deletion belongs in — or nothing, for an id that may not become a path.
 *
 * <p>Nothing rather than a sanitised path, the same answer `promptFile` gives and for the same
 * reason: a caller holding an id this refuses is a caller whose id came from somewhere it should
 * not have, and quietly writing `....escaped.json` would hide that. Rewriting it would be worse
 * than refusing, because the tombstone would then be about a different role than the one deleted.</p>
 *
 * <p>Role ids cannot contain a separator by construction — `ROLE_ID` in `roles.ts` allows letters,
 * digits and underscores — and this is the place that actually opens the file, so it asks anyway. A
 * rule held up only by another rule is one rename from neither. (antigravity, the plan round.)</p>
 */
export function deletionFile(dataDir: string, roleId: string): string | undefined {
  if (!ROLE_ID.test(roleId) || RESERVED.has(roleId.toLowerCase())) {
    return undefined;
  }

  return `${deletionsDir(dataDir)}/${roleId}.json`;
}

/** The store the coordinator is handed. `dataDir` is read per call, because it can MOVE. */
export function tombstonesIn(dataDir: () => string): TombstoneStore {
  return {
    put: async (tombstone: Tombstone): Promise<void> => {
      const file = deletionFile(dataDir(), tombstone.roleId);
      if (file === undefined) {
        throw new Error(`a role id that cannot become a path: ${tombstone.roleId}`);
      }
      // Beside and renamed over, per `atomicFile`: a tombstone that parses to nothing is a deletion
      // nobody can finish, which is the state it exists to prevent.
      await writeFileAtomically(file, JSON.stringify(tombstone, null, 2));
    },

    read: async (roleId: string): Promise<Tombstone | undefined> => {
      const file = deletionFile(dataDir(), roleId);

      return file === undefined ? undefined : await parsed(file);
    },

    all: async (): Promise<readonly Tombstone[]> => {
      const names = await readdir(deletionsDir(dataDir())).catch((): readonly string[] => []);
      const found: Tombstone[] = [];
      for (const name of names.filter((one) => one.endsWith('.json'))) {
        const one = await parsed(`${deletionsDir(dataDir())}/${name}`);
        if (one !== undefined) {
          found.push(one);
        }
      }

      return found;
    },

    drop: async (roleId: string): Promise<void> => {
      const file = deletionFile(dataDir(), roleId);
      if (file !== undefined) {
        await rm(file, { force: true });
      }
    },
  };
}

/** A tombstone, or nothing at all — a file that is missing, unreadable or not one. */
async function parsed(file: string): Promise<Tombstone | undefined> {
  const text = await readFile(file, 'utf8').catch(() => '');
  if (text === '') {
    return undefined;
  }
  try {
    const held = JSON.parse(text) as Partial<Tombstone>;

    return typeof held.roleId === 'string' && ROLE_ID.test(held.roleId) && typeof held.nonce === 'string'
      ? {
        roleId: held.roleId,
        name: typeof held.name === 'string' ? held.name : held.roleId,
        promptIds: Array.isArray(held.promptIds) ? held.promptIds.filter((one) => typeof one === 'string') : [],
        askedAt: typeof held.askedAt === 'string' ? held.askedAt : '',
        nonce: held.nonce,
        reason: typeof held.reason === 'string' ? held.reason : '',
        failedAt: typeof held.failedAt === 'string' ? held.failedAt : '',
      }
      : undefined;
  } catch {
    return undefined;
  }
}
