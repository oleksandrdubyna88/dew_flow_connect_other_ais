import { readdir, readFile, rename, rm } from 'node:fs/promises';
import { writeFileAtomically } from './atomicFile';
import { Tombstone, TombstoneStore } from './roleDeletion';
import { ROLE_ID } from './roles';
import { RESERVED_FILE_NAMES } from './rolesPrompts';

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
 * file that fails to parse strands ONE deletion rather than all of them.</p>
 *
 * <p><b>Nothing here is swallowed.</b> A directory that is not there yet is absence — the first run
 * of every installation — and every other failure is REPORTED while the readable tombstones carry
 * on. An unreadable entry disappearing quietly is how an id gets released to a new role that then
 * inherits the old one's prompt files. (codex, the code round, twice.)</p>
 */

/** The claim: one window renames the tombstone to this and the others find it gone. */
const CLAIMED = '.claiming';

export function deletionsDir(dataDir: string): string {
  return `${dataDir}/deletions`;
}

/**
 * The file this deletion belongs in — or nothing, for an id that may not become a path.
 *
 * <p>Nothing rather than a sanitised path, the same answer `promptFile` gives and for the same
 * reason: a caller holding an id this refuses is a caller whose id came from somewhere it should
 * not have, and quietly writing `....escaped.json` would hide that. Rewriting would be worse still,
 * because the tombstone would then be about a different role than the one being deleted.</p>
 *
 * <p>The pattern and the reserved names are IMPORTED rather than copied — `roles.ts` decides what a
 * role id is and `rolesPrompts.ts` decides what Windows refuses, and a second copy of either drifts
 * silently in the worst direction: a prompt file accepted whose tombstone is not.</p>
 */
export function deletionFile(dataDir: string, roleId: string): string | undefined {
  if (!ROLE_ID.test(roleId) || RESERVED_FILE_NAMES.has(roleId.toLowerCase())) {
    return undefined;
  }

  return `${deletionsDir(dataDir)}/${roleId}.json`;
}

/** The store the coordinator is handed. `dataDir` is read per call, because it can MOVE. */
export function tombstonesIn(dataDir: () => string): TombstoneStore {
  const pathOf = (roleId: string, suffix = '.json'): string | undefined => {
    const file = deletionFile(dataDir(), roleId);

    return file === undefined ? undefined : file.replace(/\.json$/u, suffix);
  };

  return {
    put: async (tombstone: Tombstone): Promise<void> => {
      const file = pathOf(tombstone.roleId);
      if (file === undefined) {
        throw new Error(`a role id that cannot become a path: ${tombstone.roleId}`);
      }
      // Beside and renamed over, per `atomicFile`: a tombstone that parses to nothing is a deletion
      // nobody can finish, which is the state it exists to prevent.
      await writeFileAtomically(file, JSON.stringify(tombstone, null, 2));
    },

    read: async (roleId: string): Promise<Tombstone | undefined> => {
      const file = pathOf(roleId);

      return file === undefined ? undefined : await parsed(file);
    },

    all: async (): Promise<readonly Tombstone[]> => {
      const dir = deletionsDir(dataDir());
      const found = await Promise.all(
        (await names(dir)).map(async (name) => await parsed(`${dir}/${name}`)),
      );

      return found.filter((one): one is Tombstone => one !== undefined);
    },

    reservedIds: async (): Promise<ReadonlySet<string>> => {
      // From the NAMES. A body that cannot be read is still a deletion that has not finished, and
      // releasing its id hands a new role the old one's prompt files.
      const ids = (await names(deletionsDir(dataDir())))
        .map((name) => name.replace(/\.(json|claiming)$/u, ''))
        .filter((id) => ROLE_ID.test(id))
        .map((id) => id.toLowerCase());

      return new Set(ids);
    },

    claim: async (roleId: string, nonce: string): Promise<boolean> => {
      const file = pathOf(roleId);
      const mine = pathOf(roleId, CLAIMED);
      if (file === undefined || mine === undefined) {
        return false;
      }
      // Checked INSIDE the claim: an id that has come back to life under a new tombstone must be
      // refused, not renamed away from the role that now holds it.
      const held = await parsed(file);
      if (held?.nonce !== nonce) {
        return false;
      }
      try {
        await rename(file, mine);

        return true;
      } catch (reason) {
        // Somebody else won it, or it finished between the read and here. Either way it is not
        // ours, and the one thing that must not happen is deleting text on a claim we do not hold.
        if (!missing(reason)) {
          console.error(`[coai] the deletion of ${roleId} could not be claimed`, reason);
        }

        return false;
      }
    },

    drop: async (roleId: string): Promise<void> => {
      for (const suffix of ['.json', CLAIMED]) {
        const file = pathOf(roleId, suffix);
        if (file !== undefined) {
          await rm(file, { force: true });
        }
      }
    },
  };
}

/**
 * Every tombstone file in the directory — including the CLAIMED ones, because a window that died
 * holding a claim left a deletion that still has to finish.
 *
 * <p>A missing directory is absence: that is the first run of every installation, and throwing
 * there would fail activation over a folder that is empty by definition. Anything else is said out
 * loud, and answers empty so the caller is not handed a half-list it cannot tell from a whole
 * one.</p>
 */
async function names(dir: string): Promise<readonly string[]> {
  try {
    return (await readdir(dir)).filter((one) => one.endsWith('.json') || one.endsWith(CLAIMED));
  } catch (reason) {
    if (!missing(reason)) {
      console.error(`[coai] the outstanding role deletions could not be listed in ${dir}`, reason);
    }

    return [];
  }
}

/** A tombstone, or nothing at all — and a failure that is not simple absence is reported. */
async function parsed(file: string): Promise<Tombstone | undefined> {
  const claimed = file.replace(/\.json$/u, CLAIMED);
  const text = await body(file) ?? await body(claimed);
  if (text === undefined) {
    return undefined;
  }
  try {
    return shaped(JSON.parse(text) as Partial<Tombstone>);
  } catch (reason) {
    console.error(`[coai] a role deletion record could not be read: ${file}`, reason);

    return undefined;
  }
}

async function body(file: string): Promise<string | undefined> {
  try {
    return await readFile(file, 'utf8');
  } catch (reason) {
    if (!missing(reason)) {
      console.error(`[coai] a role deletion record could not be opened: ${file}`, reason);
    }

    return undefined;
  }
}

function shaped(held: Partial<Tombstone>): Tombstone | undefined {
  if (typeof held.roleId !== 'string' || !ROLE_ID.test(held.roleId) || typeof held.nonce !== 'string') {
    return undefined;
  }

  return {
    roleId: held.roleId,
    name: typeof held.name === 'string' ? held.name : held.roleId,
    promptIds: Array.isArray(held.promptIds)
      ? held.promptIds.filter((one) => typeof one === 'string')
      : [],
    askedAt: typeof held.askedAt === 'string' ? held.askedAt : '',
    nonce: held.nonce,
    reason: typeof held.reason === 'string' ? held.reason : '',
    failedAt: typeof held.failedAt === 'string' ? held.failedAt : '',
  };
}

/** The one failure that is not a failure: nothing is there yet. */
function missing(reason: unknown): boolean {
  return typeof reason === 'object' && reason !== null
    && (reason as { code?: unknown }).code === 'ENOENT';
}
