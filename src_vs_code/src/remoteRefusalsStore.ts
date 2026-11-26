import type { Claim } from './chatStoreLock';
import type { Committed } from './remoteHosts';
import {
  BESIDE_STALE_MS, CLAIM_PAUSE_MS, CLAIM_ROUNDS, CLAIM_FRESH_MS, Changed, EMPTY_FILE, PlainChange, READ_PAUSE_MS, READ_TRIES, RESTARTS_MAX, RefusalsDisk, RefusalsFile, RefusalsRead, RefusalsStore,
  WRITE_ROUNDS, holds, refusalsOf, textOf, waitingOf, withChange,
} from './remoteRefusals';
import { Permit, listing } from './remoteRefusalsPermit';

/**
 * The writer of the refusals file and the store over it (E1.S1, § S1.5; E2.S1 adds the permission's four operations, which live in
 * `remoteRefusalsPermit.ts`). The writer claims, writes and reads back. It never says "kept" for a change it cannot see in the file. No path and no
 * disk error text leaves this module.
 */

const realSleep = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

/** `restart`: the claim went stale or was taken over, so nothing was written. `lost`: the write is not in the file at the read-back. */
type Step = 'restart' | 'lost' | Committed;

const KEPT_STEP: Step = { kind: 'kept', row: undefined };
const WROTE_STEP: Step = { kind: 'written', row: undefined, evicted: [] };
const unwritten = (why: 'busy' | 'disk' | 'lost'): Step => ({ kind: 'unwritten', why });

/** Nothing was kept: the write did not stay, or the claim kept going stale. */
const giveUp = (lost: number): Committed => unwritten(lost > 0 ? 'lost' : 'busy') as Committed;

/** The step a merge that wrote nothing is. `low` cannot come from a plain change: it is the grant's, and a grant is not sent through `commit`. */
const notWritten = (merged: Exclude<Changed, { readonly kind: 'written' }>): Step =>
  (merged.kind === 'kept' ? KEPT_STEP : merged.kind === 'low' ? unwritten('lost') : merged);

const sameWrite = (now: RefusalsFile, wrote: RefusalsFile): boolean => now.gen === wrote.gen && now.by === wrote.by;

/** The writer over a disk. It is also what the permission's operations borrow (`Permit`): the file's read, the claim and the claim's freshness. */
class Writer {
  constructor(readonly disk: RefusalsDisk, readonly clock: () => number, readonly sleep: (ms: number) => Promise<void>) {}

  /** One try of the file's read: the file, `undefined` when it cannot be read, or `retry` when the read failed. */
  private async readOnce(): Promise<RefusalsFile | undefined | 'retry'> {
    const got = await this.disk.read();

    return got.kind === 'absent' ? EMPTY_FILE : got.kind === 'text' ? refusalsOf(got.text) : 'retry';
  }

  /** The file, or `undefined` when it cannot be read; `absent` is the empty file. A failing read is asked again twice. */
  async readFile(): Promise<RefusalsFile | undefined> {
    for (let tries = 1; tries <= READ_TRIES; tries += 1) {
      const one = await this.readOnce();
      if (one !== 'retry') {
        return one;
      }
      await this.sleep(READ_PAUSE_MS);
    }

    return undefined;
  }

  async read(): Promise<RefusalsRead> {
    // The withdrawal files first, then the markers, then the file: a marker that is removed after its change landed is then covered by the file read
    // that comes after it, and from step 5 of an *Allow* on, a readable withdrawal stands at or above the OLD grant before a new one is written.
    const withdrawn = await listing(this.disk);
    const marks = await this.disk.marks();
    const file = await this.readFile();
    if (file === undefined || marks === 'failed' || withdrawn === 'failed') {
      return { kind: 'damaged' };
    }

    return { kind: 'file', file, waiting: marks.map((one) => waitingOf(one.name, one.text)), withdrawn: { files: withdrawn.files, crowded: withdrawn.crowded } };
  }

  async claimed(doing: string, take: 'person' | undefined): Promise<Claim> {
    let got: Claim = { kind: 'held' };
    for (let round = 1; round <= CLAIM_ROUNDS; round += 1) {
      got = await this.disk.claim(doing, take);
      if (got.kind !== 'held') {
        break;
      }
      await this.sleep(CLAIM_PAUSE_MS);
    }

    return got;
  }

  async released<T>(claim: Extract<Claim, { kind: 'claimed' }>, step: T): Promise<T> {
    await claim.release();

    return step;
  }

  /** Step 5: the claim is younger than `CLAIM_FRESH_MS` by this window's clock and still on the disk. */
  async stale(claim: Extract<Claim, { kind: 'claimed' }>, began: number): Promise<boolean> {
    return this.clock() - began >= CLAIM_FRESH_MS || !(await this.disk.stillOurs(claim));
  }

  /** Step 6: the file replaced; `false` when the disk refused. */
  async replaced(text: string): Promise<boolean> {
    try {
      await this.disk.replace(text);

      return true;
    } catch {
      return false;
    }
  }

  /** Steps 2 and 3 under a claim that is ours; then the rest. */
  private async underClaim(change: PlainChange, claim: Extract<Claim, { kind: 'claimed' }>, began: number): Promise<Step> {
    const before = (await this.readFile()) ?? 'damaged';
    const merged = withChange(before, change, claim.token);
    if (merged.kind !== 'written') {
      return this.released(claim, notWritten(merged));
    }

    return this.writeIt(change, claim, { began, before, file: merged.file });
  }

  /** Steps 4 to 8. */
  private async writeIt(change: PlainChange, claim: Extract<Claim, { kind: 'claimed' }>, at: { readonly began: number; readonly before: RefusalsFile | 'damaged'; readonly file: RefusalsFile }): Promise<Step> {
    await this.disk.sweep(BESIDE_STALE_MS);
    if (await this.stale(claim, at.began)) {
      return this.released(claim, 'restart');
    }
    if (!(await this.replaced(textOf(at.file)))) {
      return this.released(claim, unwritten('disk'));
    }
    const ours = await this.disk.stillOurs(claim);
    await claim.release();

    return ours ? this.readBack(change, at.before, at.file) : 'restart';
  }

  /** Step 8: the file as it now is shows this write, or this change. */
  private async readBack(change: PlainChange, before: RefusalsFile | 'damaged', wrote: RefusalsFile): Promise<Step> {
    const now = await this.readFile();
    if (now === undefined) {
      return 'lost';
    }

    return sameWrite(now, wrote) || holds(change, before, now) ? WROTE_STEP : 'lost';
  }

  private async attempt(change: PlainChange, take: 'person' | undefined): Promise<Step> {
    const claim = await this.claimed(change.kind, take);
    if (claim.kind !== 'claimed') {
      return unwritten(claim.kind === 'held' ? 'busy' : 'disk');
    }

    return this.underClaim(change, claim, this.clock());
  }

  async commit(change: PlainChange, take?: 'person'): Promise<Committed> {
    const tally = { restart: 0, lost: 0 };
    for (let round = 0; round < RESTARTS_MAX + WRITE_ROUNDS; round += 1) {
      const step = await this.attempt(change, take);
      if (typeof step !== 'string') {
        return step;
      }
      tally[step] += 1;
      if (tally.lost >= WRITE_ROUNDS) {
        break;
      }
    }

    return giveUp(tally.lost);
  }
}

/** The writer over a disk. `sleep` is the pause between tries; a test hands in one that does not wait. */
export function refusalsStore(disk: RefusalsDisk, clock: () => number, sleep: (ms: number) => Promise<void> = realSleep): RefusalsStore {
  const writer = new Writer(disk, clock, sleep);
  const permit = new Permit(writer);

  return {
    read: () => writer.read(),
    commit: (change, take) => writer.commit(change, take),
    mark: async (waiting) => (await disk.mark(JSON.stringify({ v: 1, ...waiting }))) !== undefined,
    unmark: (name) => disk.unmark(name),
    allow: (input) => permit.allow(input),
    withdraw: (input) => permit.withdraw(input),
    copyIn: (stamp, utc) => permit.copyIn(stamp, utc),
    free: () => permit.free(),
  };
}
