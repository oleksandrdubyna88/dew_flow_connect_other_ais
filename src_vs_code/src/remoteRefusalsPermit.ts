import type { Claim } from './chatStoreLock';
import {
  ALLOW_REMOVALS_MAX, Against, Allowed, Grant, WITHDRAWN_KEEP_MAX, Withdrawal, Withdrawn, floorOf, grantOf, inForce, nameStamp, stampsOf, STAMP_MAX, withdrawalName, withdrawalOf, withdrawalStamp, withdrawalText,
} from './remotePermission';
import {
  BESIDE_STALE_MS, CLAIM_PAUSE_MS, CLAIM_ROUNDS, RESTARTS_MAX, RefusalsDisk, RefusalsFile, WITHDRAW_ROUNDS, WRITE_ROUNDS, textOf, withChange,
} from './remoteRefusals';

/**
 * The permission's two writes and their two small ones (E2.S1, § P6 and § P7 of its spec): an *Allow* under the claim, a *Withdraw* with NO claim, the
 * copy-in of a withdrawal and a person's `free` of a lock that hangs. The writer's own pieces (the file's read, the claim, its freshness) are
 * borrowed through `Toolkit`, so this module imports nothing of the writer.
 *
 * <p>The grant is written in ONE place, step 8 of `AllowRun`. A withdrawal's file is made with an exclusive create and never waits for the lock: a held
 * lock delays only the word *withdrawn* (the read-back), and nothing here takes a lock over by its age.</p>
 */

/** What the permission's operations need of the writer. */
export interface Toolkit {
  readonly disk: RefusalsDisk;
  readonly clock: () => number;
  readonly sleep: (ms: number) => Promise<void>;
  readonly readFile: () => Promise<RefusalsFile | undefined>;
  readonly claimed: (doing: string, take: 'person' | undefined) => Promise<Claim>;
  readonly released: <T>(claim: Extract<Claim, { kind: 'claimed' }>, step: T) => Promise<T>;
  readonly stale: (claim: Extract<Claim, { kind: 'claimed' }>, began: number) => Promise<boolean>;
  readonly replaced: (text: string) => Promise<boolean>;
}

/** The withdrawal files of the folder as the permission reads them. `names` is every name with the prefix, the unread ones (`rest`) last. */
export interface Listing {
  readonly files: readonly Withdrawal[];
  readonly crowded: boolean;
  readonly rest: readonly string[];
  readonly names: readonly string[];
}

/** The folder's withdrawal files, read now; `failed` when the folder cannot be listed. */
export async function listing(disk: RefusalsDisk): Promise<Listing | 'failed'> {
  const got = await disk.withdrawals();
  if (got === 'failed') {
    return 'failed';
  }
  const files = got.seen.map((one) => withdrawalOf(disk.withdrawnPrefix, one.name, one.text));

  return { files, crowded: got.rest.length > 0, rest: got.rest, names: [...files.map((one) => one.name), ...got.rest] };
}

const againstOf = (list: Listing, copy: Against['copy']): Against => ({ files: list.files, crowded: list.crowded, copy, held: false });

type Copy = Against['copy'];

const unwritten = (why: 'busy' | 'disk' | 'lost'): Extract<Allowed, { kind: 'unwritten' }> => ({ kind: 'unwritten', why });

/** Nothing was kept: the write did not stay, or the claim kept going stale. */
const giveUp = (lost: number): Allowed => unwritten(lost > 0 ? 'lost' : 'busy');

/** What the *Allow* wrote, kept for step 10 even when the claim went stale after the rename. */
interface Wrote {
  readonly grant: Grant;
  readonly floor: number;
  readonly restarted: boolean;
  /** Step 3's listing of the round that wrote. */
  readonly listing: Listing;
}

/** Where an *Allow* goes next: an answer, or another round. `lost`: the grant was not at the read-back. */
type Round = Allowed | 'restart' | 'lost';

interface Plan {
  readonly floor: number;
  readonly stamp: number;
  readonly restart: boolean;
}

export interface AllowInput {
  readonly utc: string;
  readonly copy: () => Copy;
}

/** An *Allow* (§ P6): every step is one small method. */
class AllowRun {
  private first!: Listing;
  private copyFirst: Copy;
  private wrote: Wrote | undefined;
  /** The floor files this *Allow* made itself: they are never a new name. */
  private readonly floors = new Set<string>();

  constructor(private readonly kit: Toolkit, private readonly input: AllowInput) {
    this.copyFirst = undefined;
  }

  /** Step 0: the listing and the copy BEFORE the claim are what was there when the *Allow* began. */
  async run(): Promise<Allowed> {
    const before = await listing(this.kit.disk);
    if (before === 'failed') {
      return { kind: 'damaged' };
    }
    this.first = before;
    this.copyFirst = this.input.copy();

    return this.rounds();
  }

  private async rounds(): Promise<Allowed> {
    const tally = { restart: 0, lost: 0 };
    for (let round = 0; round < RESTARTS_MAX + WRITE_ROUNDS; round += 1) {
      const step = await this.round();
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

  /** Step 1: the ordinary claim, never `take: 'person'`. */
  private async round(): Promise<Round> {
    const claim = await this.kit.claimed('grant', undefined);
    if (claim.kind !== 'claimed') {
      return unwritten(claim.kind === 'held' ? 'busy' : 'disk');
    }

    return this.underClaim(claim, this.kit.clock());
  }

  /** Steps 2 and 3 first half: the file and the listing, read under the claim; `undefined` when either cannot be. */
  private async looked(): Promise<Seen | undefined> {
    const file = await this.kit.readFile();
    const now = await listing(this.kit.disk);

    return file === undefined || now === 'failed' ? undefined : { file, now, copy: this.input.copy() };
  }

  /** Steps 2 and 3. */
  private async underClaim(claim: Held, began: number): Promise<Round> {
    const seen = await this.looked();
    if (seen === undefined) {
      return this.kit.released(claim, { kind: 'damaged' });
    }
    const early = this.early(seen);
    if (early === 'ten') {
      return this.tenth(claim);
    }

    return early === undefined ? this.planned(claim, began, seen) : this.kit.released(claim, early);
  }

  /** This *Allow* wrote and finds its own grant in force: the claim is released and step 10 reads back what step 3 of the writing round listed. */
  private async tenth(claim: Held): Promise<Round> {
    await claim.release();

    return this.readBack(this.wrote as Wrote);
  }

  /** The grant of the file, when it is in force against this listing and this copy. */
  private inForceNow({ file, now, copy }: Seen): Grant | undefined {
    const grant = grantOf(file);

    return grant !== undefined && inForce(grant, againstOf(now, copy)) ? grant : undefined;
  }

  /** Step 3's first two answers: the grant is already in force (`kept`, or step 10 when this *Allow* wrote), or something new came. */
  private early(seen: Seen): Allowed | 'ten' | undefined {
    const grant = this.inForceNow(seen);
    if (grant !== undefined) {
      return this.wrote === undefined ? { kind: 'kept', grant } : 'ten';
    }

    return this.sawNew(seen.now, seen.copy) ? { kind: 'withdrawn' } : undefined;
  }

  private newName(now: Listing): boolean {
    const had = new Set(this.first.names);

    return now.names.some((name) => !had.has(name) && !this.floors.has(name));
  }

  /** A readable withdrawal whose `utc` is later than the *Allow*'s is a later word, whatever listing it was in (F5). */
  private laterWord(now: Listing): boolean {
    return now.files.some((one) => one.kind === 'stamp' && one.utc > this.input.utc && !this.floors.has(one.name));
  }

  private higherCopy(copy: Copy): boolean {
    return typeof copy === 'number' && (typeof this.copyFirst !== 'number' || copy > this.copyFirst);
  }

  private sawNew(now: Listing, copy: Copy): boolean {
    return this.newName(now) || this.laterWord(now) || this.higherCopy(copy);
  }

  /** Step 4: the floor and the stamp; at `STAMP_MAX` the count starts again. */
  private planOf(file: RefusalsFile, now: Listing, copy: Copy): Plan {
    const floor = floorOf(now.files, copy, grantOf(file));

    return floor >= STAMP_MAX ? { floor: 0, stamp: 1, restart: true } : { floor, stamp: floor + 1, restart: false };
  }

  private async planned(claim: Held, began: number, seen: Seen): Promise<Round> {
    const plan = await this.floored(this.planOf(seen.file, seen.now, seen.copy), seen.now);
    if (plan === undefined) {
      return this.kit.released(claim, unwritten('disk'));
    }
    const removed = await this.removed(plan, seen.now);
    if (removed !== undefined) {
      return this.kit.released(claim, removed);
    }

    return this.written(claim, began, { ...seen, plan });
  }

  /** Step 5 is not needed: the restart, a floor of 0, or a floor file that is already in the listing. */
  private hasFloor(plan: Plan, now: Listing): boolean {
    return plan.restart || plan.floor < 1 || now.files.some((one) => one.kind === 'stamp' && one.stamp === plan.floor);
  }

  /** Step 5: the floor file, made BEFORE anything is removed. A name taken by a file that cannot be read makes the floor one higher. */
  private async floored(plan: Plan, now: Listing): Promise<Plan | undefined> {
    return this.hasFloor(plan, now) ? plan : this.floorTries(plan);
  }

  private async floorTries(plan: Plan): Promise<Plan | undefined> {
    let at = plan;
    for (let tries = 0; tries < 3; tries += 1) {
      const made = await this.floorFile(at.floor);
      if (made !== 'taken') {
        return made === 'ok' ? at : undefined;
      }
      at = { ...at, floor: at.floor + 1, stamp: at.stamp + 1 };
    }

    return undefined;
  }

  /** The withdrawal file of `name` as the folder lists it now. */
  private async found(name: string): Promise<Withdrawal | undefined> {
    const back = await listing(this.kit.disk);

    return back === 'failed' ? undefined : back.files.find((one) => one.name === name);
  }

  /** One try of the floor file: `ok`, `taken` (by a file that cannot be read) or `disk`. */
  private async floorFile(floor: number): Promise<'ok' | 'taken' | 'disk'> {
    const { disk } = this.kit;
    const name = withdrawalName(disk.withdrawnPrefix, floor);
    const made = await disk.withdrawn(name, withdrawalText(floor, this.input.utc));
    const seen = made === 'refused' ? undefined : await this.found(name);
    if (seen === undefined) {
      return 'disk';
    }
    this.floors.add(name);

    return seen.kind === 'stamp' ? 'ok' : 'taken';
  }

  /** The names step 6 removes: the files that cannot be read, `rest` when crowded, and every name at the restart. */
  private targets(plan: Plan, now: Listing): readonly string[] {
    const unreadable = now.files.filter((one) => one.kind === 'unreadable').map((one) => one.name);

    return plan.restart ? now.names : [...unreadable, ...now.rest];
  }

  /** A file that cannot be read when step 3 listed it reads now: it is not removed, and the round goes back to step 3. */
  private async readsNow(plan: Plan, targets: readonly string[]): Promise<boolean> {
    if (plan.restart) {
      return false;
    }
    const again = await listing(this.kit.disk);

    return again !== 'failed' && again.files.some((one) => one.kind === 'stamp' && targets.includes(one.name));
  }

  /** Step 6: each file that cannot be read is read once more just before it goes; a file that reads now stays and ends this round. */
  private async removed(plan: Plan, now: Listing): Promise<Round | undefined> {
    const targets = this.targets(plan, now);
    if (targets.length === 0) {
      return undefined;
    }

    return (await this.readsNow(plan, targets)) ? 'lost' : this.removing(targets);
  }

  /** The removals of one press: at most `ALLOW_REMOVALS_MAX`; when more are left the answer is `lost` and the next press takes the next ones. */
  private async removing(targets: readonly string[]): Promise<Round | undefined> {
    if ((await this.removeAll(targets.slice(0, ALLOW_REMOVALS_MAX))) === 'disk') {
      return unwritten('disk');
    }

    return targets.length > ALLOW_REMOVALS_MAX ? unwritten('lost') : undefined;
  }

  private async removeAll(names: readonly string[]): Promise<'ok' | 'disk'> {
    for (const name of names) {
      if (!(await this.kit.disk.unwithdraw(name))) {
        return 'disk';
      }
    }

    return 'ok';
  }

  /** Steps 7 to 9: the claim again, the replace of the file with the grant, the release. */
  private async written(claim: Held, began: number, seen: Seen & { readonly plan: Plan }): Promise<Round> {
    await this.kit.disk.sweep(BESIDE_STALE_MS);
    if (await this.kit.stale(claim, began)) {
      return this.kit.released(claim, 'restart');
    }
    const grant: Grant = { utc: this.input.utc, stamp: seen.plan.stamp };
    const merged = withChange(seen.file, { kind: 'grant', utc: grant.utc, stamp: grant.stamp, floor: seen.plan.floor }, claim.token);
    if (merged.kind !== 'written') {
      return this.kit.released(claim, unwritten('lost'));
    }
    if (!(await this.kit.replaced(textOf(merged.file)))) {
      return this.kit.released(claim, unwritten('disk'));
    }

    return this.afterWrite(claim, { grant, floor: seen.plan.floor, restarted: seen.plan.restart, listing: seen.now });
  }

  /** Step 9: release and note whether the lock was still this claim's; then step 10. */
  private async afterWrite(claim: Held, wrote: Wrote): Promise<Round> {
    this.wrote = wrote;
    const ours = await this.kit.disk.stillOurs(claim);
    await claim.release();

    return ours ? this.readBack(wrote) : 'restart';
  }

  /** Step 10: read back with no claim — the listing, then the file. A read-back that cannot be made never ends as `given`. */
  private async readBack(wrote: Wrote): Promise<Round> {
    const list = await listing(this.kit.disk);
    const file = await this.kit.readFile();

    return list === 'failed' || file === undefined ? this.withdrawnAt(wrote.grant.stamp) : this.checked(wrote, list, grantOf(file));
  }

  /** The grant is not there, or lower than this *Allow*'s: another writer renamed since, and the *Allow* goes again from 1. */
  private checked(wrote: Wrote, list: Listing, grant: Grant | undefined): Promise<Round> | 'lost' {
    return grant === undefined || grant.stamp < wrote.grant.stamp ? 'lost' : this.verdict(wrote, list, grant);
  }

  /** Whether a withdrawal file stands at or above the grant, or cannot be read. */
  private against(list: Listing, grant: Grant): boolean {
    return list.crowded || list.files.some((one) => one.kind === 'unreadable' || one.stamp >= grant.stamp);
  }

  private async verdict(wrote: Wrote, list: Listing, grant: Grant): Promise<Round> {
    const had = new Set(wrote.listing.names);
    const fresh = list.names.some((name) => !had.has(name) && !this.floors.has(name));
    if (!fresh && !this.against(list, grant)) {
      return this.given(wrote);
    }

    return this.against(list, grant) ? { kind: 'withdrawn' } : this.withdrawnAt(grant.stamp);
  }

  /** A withdrawal came while the *Allow* ran and no file stands at or above the grant: the *Allow* makes `withdrawn-<the grant's stamp>.json` itself. */
  private async withdrawnAt(stamp: number): Promise<Round> {
    const { disk } = this.kit;
    const made = await disk.withdrawn(withdrawalName(disk.withdrawnPrefix, stamp), withdrawalText(stamp, this.input.utc));
    const back = made === 'refused' ? 'failed' : await listing(disk);
    const stands = back !== 'failed' && back.files.some((one) => one.name === withdrawalName(disk.withdrawnPrefix, stamp));

    return stands ? { kind: 'withdrawn' } : { kind: 'withdrawn', unsaved: true };
  }

  /** Step 11: the lower files go last — only readable ones of step 3's listing, below the floor; a removal that fails changes nothing. */
  private async given(wrote: Wrote): Promise<Round> {
    for (const one of wrote.listing.files) {
      if (one.kind === 'stamp' && one.stamp < wrote.floor) {
        await this.kit.disk.unwithdraw(one.name);
      }
    }

    return { kind: 'given', grant: wrote.grant, floor: wrote.floor, restarted: wrote.restarted };
  }
}

type Held = Extract<Claim, { kind: 'claimed' }>;

interface Seen {
  readonly file: RefusalsFile;
  readonly now: Listing;
  readonly copy: Copy;
}

// ─────────────────────────────── Withdraw ───────────────────────────────

export interface WithdrawInput {
  readonly utc: string;
  readonly above: number;
}

/** The stamps a withdrawal sees: the grant, every readable file, the stamp spelled by the name of an unreadable one, and `above`. */
function seenStamps(prefix: string, list: Listing, grant: Grant | undefined, above: number): readonly number[] {
  const spelled = list.files.flatMap((one) => (one.kind === 'unreadable' ? [nameStamp(prefix, one.name) ?? 0] : []));

  return [grant?.stamp ?? 0, ...stampsOf(list.files), ...spelled, above];
}

/** A *Withdraw* (§ P7): no claim, an exclusive create, a read-back that looks at the lock FIRST. */
class WithdrawRun {
  constructor(private readonly kit: Toolkit, private readonly input: WithdrawInput) {}

  async run(): Promise<Withdrawn> {
    for (let round = 0; round < WITHDRAW_ROUNDS; round += 1) {
      const step = await this.once();
      if (step !== 'again') {
        return step;
      }
    }

    return { kind: 'unwritten', why: 'lost' };
  }

  /** The grant of the file as it is now; a file that cannot be read is no obstacle (I2). */
  private async grantNow(): Promise<Grant | undefined> {
    return grantOf((await this.kit.readFile()) ?? {});
  }

  /** Steps 4 to 8, then 9. */
  private async once(): Promise<Withdrawn | 'again'> {
    const { disk } = this.kit;
    const list = await listing(disk);
    if (list === 'failed') {
      return { kind: 'unwritten', why: 'disk' };
    }
    const stamp = withdrawalStamp(seenStamps(disk.withdrawnPrefix, list, await this.grantNow(), this.input.above));

    return (await this.pruned(list)) >= WITHDRAWN_KEEP_MAX ? { kind: 'withdrawn', stamp } : this.made(stamp);
  }

  /** Step 8: an exclusive create; then step 9. */
  private async made(stamp: number): Promise<Withdrawn | 'again'> {
    const { disk } = this.kit;
    const made = await disk.withdrawn(withdrawalName(disk.withdrawnPrefix, stamp), withdrawalText(stamp, this.input.utc));

    return made === 'refused' ? { kind: 'unwritten', why: 'disk' } : this.readBack(stamp);
  }

  /** Step 6: past 8 names the readable file with the LOWEST stamp goes, never the highest and never one that cannot be read. Answers how many names are left. */
  private async pruned(list: Listing): Promise<number> {
    const readable = list.files.filter((one): one is Extract<Withdrawal, { kind: 'stamp' }> => one.kind === 'stamp').sort((a, b) => a.stamp - b.stamp);
    let left = list.names.length;
    while (left >= WITHDRAWN_KEEP_MAX && readable.length > 1) {
      const lowest = readable[0] as Extract<Withdrawal, { kind: 'stamp' }>;
      if (!(await this.kit.disk.unwithdraw(lowest.name))) {
        break;
      }
      readable.shift();
      left -= 1;
    }

    return left;
  }

  /** The lock as a claim waits: `free` or `gone` stand; still `held` is `busy`. */
  private async lockLooked(): Promise<'ready' | 'busy'> {
    for (let look = 0; look < CLAIM_ROUNDS; look += 1) {
      if ((await this.kit.disk.lockNow()) !== 'held') {
        return 'ready';
      }
      await this.kit.sleep(CLAIM_PAUSE_MS);
    }

    return 'busy';
  }

  /** Step 9: the lock FIRST, then the listing, then the file. An *Allow* that wrote in between sends the withdrawal again from 4. */
  private async readBack(stamp: number): Promise<Withdrawn | 'again'> {
    if ((await this.lockLooked()) === 'busy') {
      return { kind: 'unwritten', why: 'busy' };
    }
    const list = await listing(this.kit.disk);

    return list === 'failed' ? { kind: 'unwritten', why: 'disk' } : this.stands(list, stamp);
  }

  private async stands(list: Listing, stamp: number): Promise<Withdrawn | 'again'> {
    return inForce(await this.grantNow(), againstOf(list, undefined)) ? 'again' : { kind: 'withdrawn', stamp };
  }
}

// ─────────────────────────────── The store's four operations ───────────────────────────────

/** The permission's operations over a writer. */
export class Permit {
  constructor(private readonly kit: Toolkit) {}

  allow(input: AllowInput): Promise<Allowed> {
    return new AllowRun(this.kit, input).run();
  }

  withdraw(input: WithdrawInput): Promise<Withdrawn> {
    return new WithdrawRun(this.kit, input).run();
  }

  /** The copy-in: one withdrawal file for a copy that is above the folder. An exclusive create with no claim. */
  async copyIn(stamp: number, utc: string): Promise<'written' | 'kept' | 'unwritten'> {
    const { disk } = this.kit;
    const made = await disk.withdrawn(withdrawalName(disk.withdrawnPrefix, stamp), withdrawalText(stamp, utc));

    return made === 'made' ? 'written' : made === 'there' ? 'kept' : 'unwritten';
  }

  /** A person's press: one claim with `take: 'person'`, released at once. Writes nothing. */
  async free(): Promise<void> {
    const claim = await this.kit.claimed('free', 'person');
    if (claim.kind === 'claimed') {
      await claim.release();
    }
  }
}
