import { randomBytes } from 'node:crypto';
import { open, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { writeFileAtomically, BESIDE_SUFFIX } from './atomicFile';
import { Claim, OwnerSeam, claimFile, lockAsItIs, lockHolds, ownerGone, takenByPerson } from './chatStoreLock';
import { makeDataDirectory } from './dataDirMaker';
import { WITHDRAWN_BYTES_MAX, WITHDRAWN_READ_MAX, nameStamp } from './remotePermission';
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

/** The folder is made when it is not there: the ONE call of the data directory's maker in this file, for a marker and for a withdrawal alike. */
const folderMade = async (dir: string): Promise<boolean> => (await makeDataDirectory(dir)).made;

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
  /** The prefix of a withdrawal file's name (E2.S1): named once, by the host half. */
  readonly withdrawnPrefix: string;
}

const markerList = async (dir: string, prefix: string): Promise<readonly string[]> => {
  try {
    return (await readdir(dir)).filter((name) => name.startsWith(prefix));
  } catch (reason) {
    if (codeOf(reason) === 'ENOENT') {
      return [];
    }
    throw reason;
  }
};

/** The file's half: read at most the cap plus one byte, replace atomically, sweep the beside-files a crash left. */
function fileHalf(dir: string, file: string, now: () => number): Pick<RefusalsDisk, 'read' | 'replace' | 'sweep'> {
  const path = join(dir, file);

  return {
    read: async () => {
      try {
        const text = await headOf(path, REFUSALS_BYTES_MAX + 1);

        return text === undefined ? { kind: 'absent' } : { kind: 'text', text };
      } catch {
        return { kind: 'failed' };
      }
    },
    replace: (text) => writeFileAtomically(path, text),
    sweep: async (ms) => {
      try {
        for (const name of (await readdir(dir)).filter((one) => one.startsWith(`${file}.`) && one.endsWith(BESIDE_SUFFIX))) {
          if (now() - (await stat(join(dir, name))).mtimeMs > ms) {
            await rm(join(dir, name), { force: true });
          }
        }
      } catch {
        // A sweep never throws: a beside-file it could not remove stays until the next write.
      }
    },
  };
}

/** The lock's half: the chat store's claim, with the rule that asks whether the owner is gone, and a person's take-over; and the lock as it is, read with no claim. */
function lockHalf(dir: string, owner: OwnerSeam, now: () => number): Pick<RefusalsDisk, 'claim' | 'stillOurs' | 'lockNow'> {
  const gone = ownerGone(owner);

  return {
    claim: (doing, take): Promise<Claim> => claimFile(dir, LOCK_ID, doing, now(), WHAT, take === 'person' ? takenByPerson : gone),
    stillOurs: (claim) => lockHolds(claim.path, claim.token),
    lockNow: async () => {
      const lock = await lockAsItIs(dir, LOCK_ID);

      return lock === 'absent' ? 'free' : lock !== 'unreadable' && gone(lock, now()) !== undefined ? 'gone' : 'held';
    },
  };
}

/** The markers' half: made by an exclusive create with no claim, listed with their text, removed by name. */
function markerHalf(dir: string, prefix: string): Pick<RefusalsDisk, 'mark' | 'marks' | 'unmark'> {
  let counter = 0;

  return {
    mark: async (text) => {
      try {
        if ((await markerList(dir, prefix)).length >= UNSURE_MAX || !(await folderMade(dir))) {
          return undefined;
        }
        counter += 1;
        const name = `${prefix}${process.pid}-${counter}-${randomBytes(4).toString('hex')}.json`;
        await writeFile(join(dir, name), text, { encoding: 'utf8', flag: 'wx' });

        return name;
      } catch {
        return undefined;
      }
    },
    marks: () => listedMarkers(dir, prefix),
    unmark: async (name) => { await rm(join(dir, name), { force: true }).catch(() => undefined); },
  };
}

type Marks = Awaited<ReturnType<RefusalsDisk['marks']>>;

async function listedMarkers(dir: string, prefix: string): Promise<Marks> {
  for (let tries = 1; tries <= LIST_TRIES; tries += 1) {
    try {
      const read = await Promise.all((await markerList(dir, prefix)).map(async (name) => ({ name, got: await headOf(join(dir, name), UNSURE_BYTES_MAX + 1).then((text) => ({ text, torn: false }), () => ({ text: undefined, torn: true })) })));

      // A marker that vanished between the listing and the read was landed by another window: it is gone, not torn.
      return read.filter((one) => one.got.text !== undefined || one.got.torn).map((one) => ({ name: one.name, text: one.got.text }));
    } catch {
      await pause(LIST_PAUSE_MS);
    }
  }

  return 'failed';
}

/** What a name of the folder is, for the listing: a stamp (the grammar's) sorts first, the highest first; every other name after it in code-unit order. */
const rankOf = (prefix: string, name: string): number => -(nameStamp(prefix, name) ?? -Number.MAX_SAFE_INTEGER);

const byStamp = (prefix: string) => (a: string, b: string): number => rankOf(prefix, a) - rankOf(prefix, b) || Number(a > b) - Number(a < b);

type Listed = Awaited<ReturnType<RefusalsDisk['withdrawals']>>;

/** One try of the listing: the first `WITHDRAWN_READ_MAX` names are read; a file that cannot be read is asked again, a name that is gone is left out. */
async function listedWithdrawals(dir: string, prefix: string): Promise<Exclude<Listed, 'failed'>> {
  const names = [...(await markerList(dir, prefix))].sort(byStamp(prefix));
  const seen: { readonly name: string; readonly text: string | undefined }[] = [];
  for (const name of names.slice(0, WITHDRAWN_READ_MAX)) {
    const one = await withdrawalRead(join(dir, name));
    if (one !== 'gone') {
      seen.push({ name, text: one });
    }
  }

  return { seen, rest: names.slice(WITHDRAWN_READ_MAX) };
}

/** A text that is settled: not empty, or the last try. */
const settled = (text: string, tries: number): { readonly value: string } | undefined => (text.length > 0 || tries === LIST_TRIES ? { value: text } : undefined);

/** One try of a withdrawal file's read: its answer when it is settled, `undefined` when it is to be asked again. */
async function triedRead(path: string, tries: number): Promise<{ readonly value: string | 'gone' } | undefined> {
  try {
    const text = await headOf(path, WITHDRAWN_BYTES_MAX + 1);

    return text === undefined ? { value: 'gone' } : settled(text, tries);
  } catch {
    return undefined;
  }
}

/** A withdrawal file's text, read three times, 20 ms apart, before it counts as unreadable (V12). `gone`: removed by another window. */
async function withdrawalRead(path: string): Promise<string | undefined | 'gone'> {
  for (let tries = 1; tries <= LIST_TRIES; tries += 1) {
    const got = await triedRead(path, tries);
    if (got !== undefined) {
      return got.value;
    }
    await pause(LIST_PAUSE_MS);
  }

  return undefined;
}

/** The withdrawal files' half: made by an exclusive create with no claim, listed with their text, removed by name. */
function withdrawalHalf(dir: string, prefix: string): Pick<RefusalsDisk, 'withdrawals' | 'withdrawn' | 'unwithdraw' | 'withdrawnPrefix'> {
  return {
    withdrawnPrefix: prefix,
    withdrawals: async () => {
      for (let tries = 1; tries <= LIST_TRIES; tries += 1) {
        try {
          return await listedWithdrawals(dir, prefix);
        } catch {
          await pause(LIST_PAUSE_MS);
        }
      }

      return 'failed';
    },
    withdrawn: async (name, text) => {
      try {
        if (!(await folderMade(dir))) {
          return 'refused';
        }
        await writeFile(join(dir, name), text, { encoding: 'utf8', flag: 'wx' });

        return 'made';
      } catch (reason) {
        return codeOf(reason) === 'EEXIST' ? 'there' : 'refused';
      }
    },
    unwithdraw: async (name) => rm(join(dir, name), { force: true }).then(() => true, () => false),
  };
}

/** The disk of one side's folder. `owner` is the seam of the lock's owner rule; `now` is the clock the claim stamps its note with. */
export function refusalsDiskOver(dir: string, names: DiskNames, owner: OwnerSeam, now: () => number = Date.now): RefusalsDisk {
  return { ...fileHalf(dir, names.file, now), ...lockHalf(dir, owner, now), ...markerHalf(dir, names.markerPrefix), ...withdrawalHalf(dir, names.withdrawnPrefix) };
}
