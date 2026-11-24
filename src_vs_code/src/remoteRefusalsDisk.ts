import { randomBytes } from 'node:crypto';
import { open, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { writeFileAtomically, BESIDE_SUFFIX } from './atomicFile';
import { Claim, OwnerSeam, claimFile, lockHolds, ownerGone, takenByPerson } from './chatStoreLock';
import { makeDataDirectory } from './dataDirMaker';
import { REFUSALS_BYTES_MAX, RefusalsDisk, UNSURE_BYTES_MAX, UNSURE_MAX } from './remoteRefusals';

/**
 * The refusals file's disk (E1.S1, § S1.5): the real `RefusalsDisk` over ONE folder. The names — the file and the prefix of a
 * marker — are given by the host half, which is the one place that names them. The lock is the one the chat store has
 * (`chatStoreLock.ts`), with the rule that asks whether its owner is gone and never its age.
 */

const LIST_TRIES = 3;
const LIST_PAUSE_MS = 20;
const LOCK_ID = 'refused';
const WHAT = 'list of refused hosts';

const pause = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

const codeOf = (reason: unknown): string => String((reason as { code?: unknown } | null)?.code ?? '');

/** At most `limit` bytes of a file, as text; `undefined` when it is absent. A read that fails for another reason throws. */
async function headOf(path: string, limit: number): Promise<string | undefined> {
  let handle;
  try {
    handle = await open(path, 'r');
  } catch (reason) {
    if (codeOf(reason) === 'ENOENT') {
      return undefined;
    }
    throw reason;
  }
  try {
    const buffer = Buffer.alloc(limit);
    const { bytesRead } = await handle.read(buffer, 0, limit, 0);

    return buffer.subarray(0, bytesRead).toString('utf8');
  } finally {
    await handle.close();
  }
}

export interface DiskNames {
  /** The file of refusals, e.g. its name inside the folder. */
  readonly file: string;
  /** The prefix of a marker's name. */
  readonly markerPrefix: string;
}

/** The disk of one side's folder. `owner` is the seam of the lock's owner rule; `now` is the clock the claim stamps its note with. */
export function refusalsDiskOver(dir: string, names: DiskNames, owner: OwnerSeam, now: () => number = Date.now): RefusalsDisk {
  const path = join(dir, names.file);
  let counter = 0;
  const markers = async (): Promise<readonly string[]> => {
    try {
      return (await readdir(dir)).filter((name) => name.startsWith(names.markerPrefix));
    } catch (reason) {
      if (codeOf(reason) === 'ENOENT') {
        return [];
      }
      throw reason;
    }
  };

  return {
    read: async () => {
      try {
        const text = await headOf(path, REFUSALS_BYTES_MAX + 1);

        return text === undefined ? { kind: 'absent' } : { kind: 'text', text };
      } catch {
        return { kind: 'failed' };
      }
    },
    claim: (doing, take): Promise<Claim> => claimFile(dir, LOCK_ID, doing, now(), WHAT, take === 'person' ? takenByPerson : ownerGone(owner)),
    stillOurs: (claim) => lockHolds(claim.path, claim.token),
    replace: (text) => writeFileAtomically(path, text),
    sweep: async (ms) => {
      try {
        for (const name of (await readdir(dir)).filter((one) => one.startsWith(`${names.file}.`) && one.endsWith(BESIDE_SUFFIX))) {
          if (now() - (await stat(join(dir, name))).mtimeMs > ms) {
            await rm(join(dir, name), { force: true });
          }
        }
      } catch {
        // A sweep never throws: a beside-file it could not remove stays until the next write.
      }
    },
    mark: async (text) => {
      try {
        if ((await markers()).length >= UNSURE_MAX || !(await makeDataDirectory(dir)).made) {
          return undefined;
        }
        counter += 1;
        const name = `${names.markerPrefix}${process.pid}-${counter}-${randomBytes(4).toString('hex')}.json`;
        await writeFile(join(dir, name), text, { encoding: 'utf8', flag: 'wx' });

        return name;
      } catch {
        return undefined;
      }
    },
    marks: async () => {
      for (let tries = 1; tries <= LIST_TRIES; tries += 1) {
        try {
          const listed = await markers();

          const read = await Promise.all(listed.map(async (name) => ({ name, got: await headOf(join(dir, name), UNSURE_BYTES_MAX + 1).then((text) => ({ text }), () => ({ text: undefined, torn: true })) })));

          // A marker that vanished between the listing and the read was landed by another window: it is gone, not torn.
          return read.filter((one) => one.got.text !== undefined || 'torn' in one.got).map((one) => ({ name: one.name, text: one.got.text }));
        } catch {
          await pause(LIST_PAUSE_MS);
        }
      }

      return 'failed';
    },
    unmark: async (name) => { await rm(join(dir, name), { force: true }).catch(() => undefined); },
  };
}
