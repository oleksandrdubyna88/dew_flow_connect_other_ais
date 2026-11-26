import {
  Allowed, Asker, Against, DoorAnswer, Grant, Permission, Withdrew, grantOf, inForce, stampsOf,
} from './remotePermission';
import type { RefusalsRead, RefusalsStore } from './remoteRefusals';

/**
 * The one door and the permission's operations of the keeper (E2.S1, § P8, § P6 and § P7): `may`, `allow`, `withdraw`, `permission`, and the copy of a
 * withdrawal's stamp between the state row and the folder. The keeper (`remoteKeeper.ts`) owns the queue and the E1 sync and hands this class a narrow
 * view of itself, so no rule moves out of the one function that answers: `may` reads everything afresh and answers from no value it kept.
 *
 * <p>The grant is never copied and never held: only the file of the side has it. What this class keeps is one counter of the presses of *Withdraw* and
 * the stamp of the last one that landed, so a withdrawal that the disk did not take is held in this window (the parent's residual 9).</p>
 */

/** What the door needs of the row of the state: the copy of the newest withdrawal's stamp and the Stop's copy. */
export interface PermissionRow {
  /** The row, read NOW from this window's `globalState`. */
  readonly read: () => { readonly withdrawn: number | 'unreadable' | undefined; readonly stopped: boolean };
  /** One commit of the row. The write is called in the step of the call. It never rejects. */
  readonly change: (change: RowChange) => PromiseLike<void>;
}

/** The three changes of the row that the permission makes (`StateChange` of `remoteAccountsRound.ts`, its permission half). */
export type RowChange =
  | { readonly withdrawn: number; readonly utc: string; readonly by: 'press' | 'read' }
  | { readonly floor: number; readonly utc: string; readonly restart?: true };

/** What the door needs of the keeper. */
export interface DoorSide {
  readonly refusals: RefusalsStore;
  readonly row: PermissionRow;
  readonly now: () => string;
  readonly changed: () => void;
  /** The notice of a withdrawal that the disk did not take (W9's third title). */
  readonly unkeptWithdrawal: (why: 'busy' | 'disk' | 'lost') => void;
  readonly queued: <T>(work: () => Promise<T>) => Promise<T>;
  /** The side as the keeper's sync leaves it: what waits is tried, "no" rows are copied in. No permission part. */
  readonly synced: () => Promise<RefusalsRead>;
  /** The file, the markers and the withdrawal files, read now, remembered by the keeper. */
  readonly look: () => Promise<RefusalsRead>;
  /** Whether the host is refused, or its refusal waits unsaved; `damaged` when the list or the file cannot be read. */
  readonly standing: (read: RefusalsRead, host: string) => 'no' | 'damaged' | 'open';
  /** Whether a refusal or the Stop is not saved on this side. */
  readonly anyWaiting: (read: RefusalsRead) => boolean;
  /** The Stop is in the file, in this window's memory or in a marker. */
  readonly stoppedIn: (read: Extract<RefusalsRead, { readonly kind: 'file' }>) => boolean;
}

type Seen = Extract<RefusalsRead, { readonly kind: 'file' }>;

interface Context {
  readonly who: Asker;
  readonly standing: 'no' | 'damaged' | 'open';
  readonly waiting: boolean;
  readonly inForce: boolean;
  readonly stopped: boolean;
}

/** The door's table, in the order of § P8: the first row that holds is the answer. */
const RULES: readonly (readonly [(context: Context) => boolean, DoorAnswer])[] = [
  [(c) => c.standing === 'no', 'no'],
  [(c) => c.standing === 'damaged', 'damaged'],
  [(c) => c.waiting, 'waiting'],
  [(c) => !c.inForce, 'no-permission'],
  [(c) => c.who === 'round' && c.stopped, 'stopped'],
];

const answerOf = (context: Context): DoorAnswer => RULES.find(([holds]) => holds(context))?.[1] ?? 'yes';

const isFile = (read: RefusalsRead): read is Seen => read.kind === 'file';

/** The withdrawal files of a read; nothing for a read that is damage. */
const filesOf = (read: RefusalsRead): Pick<Against, 'files' | 'crowded'> => (isFile(read) ? { files: read.withdrawn.files, crowded: read.withdrawn.crowded } : { files: [], crowded: false });

const grantIn = (read: RefusalsRead): Grant | undefined => (isFile(read) ? grantOf(read.file) : undefined);

/** The copy-in is due: the copy is a stamp above every readable withdrawal of the folder and nothing there cannot be read. */
const copyInDue = (copy: Against['copy'], folder: { readonly top: number; readonly bad: boolean }): copy is number => typeof copy === 'number' && copy > folder.top && !folder.bad;

/** The copy-out is due: the folder is above the copy, or the copy is absent. A copy that is not a stamp is never replaced by a read. */
const copyOutDue = (copy: Against['copy'], top: number): boolean => top > 0 && (copy === undefined || (typeof copy === 'number' && copy < top));

const isKept = (result: Allowed): result is Extract<Allowed, { readonly kind: 'given' | 'kept' }> => result.kind === 'given' || result.kind === 'kept';

/** The highest readable stamp of the folder, and whether something there cannot be read (`bad`). */
function folderOf(read: Seen): { readonly top: number; readonly bad: boolean } {
  const stamps = stampsOf(read.withdrawn.files);

  return { top: stamps.reduce((a, b) => Math.max(a, b), 0), bad: read.withdrawn.crowded || read.withdrawn.files.some((one) => one.kind === 'unreadable') };
}

export class Door {
  /** The presses of *Withdraw* in this window, and the newest whose withdrawal landed. A press above `landed` is held unsaved. */
  private presses = 0;
  private landed = 0;
  /** The copy is above the folder and the copy-in did not land. */
  private copyWaits = false;
  /** The highest stamp this window has seen: a grant, a withdrawal file, the copy. */
  private known = 0;

  constructor(private readonly side: DoorSide) {}

  /** A withdrawal waits on this side: this window holds one unsaved, or the copy-in did not land. */
  waits = (): boolean => this.presses > this.landed || this.copyWaits;

  forget(): void {
    this.presses = 0;
    this.landed = 0;
    this.copyWaits = false;
    this.known = 0;
  }

  // ───────────── the door ─────────────

  private contextOf(who: Asker, host: string, read: RefusalsRead, held: boolean): Context {
    const row = this.side.row.read();

    return {
      who,
      standing: this.side.standing(read, host),
      waiting: this.side.anyWaiting(read) || this.waits(),
      inForce: inForce(grantIn(read), { ...filesOf(read), copy: row.withdrawn, held }),
      stopped: row.stopped || (isFile(read) && this.side.stoppedIn(read)),
    };
  }

  /** The ONE door (§ P8): every read is fresh, in the keeper's queue. */
  may(who: Asker, host: string): Promise<DoorAnswer> {
    return this.side.queued(async () => answerOf(this.contextOf(who, host, await this.read(), false)));
  }

  /** The permission as the tab and the hosts list show it, from a fresh read. */
  permission(): Promise<Permission> {
    return this.side.queued(async () => this.permissionOf(await this.read()));
  }

  /** `waiting` when row 3 holds; the grant's time when the door says `yes`; else `none` — a file that cannot be read is `none` here, and each host's door says `damaged`. */
  private permissionOf(read: RefusalsRead): Permission {
    const answer = answerOf(this.contextOf('session', '', read, false));
    const grant = grantIn(read);

    return answer === 'waiting' ? 'waiting' : answer === 'yes' && grant !== undefined ? { given: grant.utc } : 'none';
  }

  /** The side read afresh, with the permission's own part: a withdrawal that waits is tried, the copy goes in and out. */
  async read(): Promise<RefusalsRead> {
    const read = await this.side.synced();
    if (!isFile(read)) {
      return read;
    }
    this.learn(read);
    const landed = await this.landUnsaved(false);
    const seen = landed ? await this.side.look() : read;

    return isFile(seen) ? this.copied(seen) : seen;
  }

  private learn(read: Seen): void {
    const copy = this.side.row.read().withdrawn;
    this.known = Math.max(this.known, ...stampsOf(read.withdrawn.files), grantOf(read.file)?.stamp ?? 0, typeof copy === 'number' ? copy : 0);
  }

  // ───────────── the copy (§ P5) ─────────────

  /** The copy-in and the copy-out. A copy that is not a stamp is never replaced by a read. */
  private async copied(read: Seen): Promise<RefusalsRead> {
    const copy = this.side.row.read().withdrawn;
    const folder = folderOf(read);
    this.copyWaits = false;
    if (copyInDue(copy, folder)) {
      return this.copyIn(copy, read);
    }
    if (copyOutDue(copy, folder.top)) {
      await this.side.row.change({ withdrawn: folder.top, utc: this.side.now(), by: 'read' });
    }

    return read;
  }

  private async copyIn(copy: number, read: Seen): Promise<RefusalsRead> {
    const done = await this.side.refusals.copyIn(copy, this.side.now());
    this.copyWaits = done === 'unwritten';

    return done === 'unwritten' ? read : this.side.look();
  }

  // ───────────── *Allow* ─────────────

  /** A person's *Allow* (§ P6). It runs in the keeper's queue; the grant is written by the store, nowhere else. */
  allow(): Promise<Allowed> {
    const utc = this.side.now();
    const press = this.presses;

    return this.side.queued(async () => this.afterAllow(await this.side.refusals.allow({ utc, copy: () => this.side.row.read().withdrawn }), utc, press));
  }

  /** On `given` or `kept` the *Allow* is the person's later word: a withdrawal held unsaved before it is dropped. It answers `given` only over its own `inForce`. */
  private async afterAllow(result: Allowed, utc: string, press: number): Promise<Allowed> {
    if (!isKept(result)) {
      return result;
    }
    this.landed = Math.max(this.landed, press);
    await this.floorWritten(result, utc);
    const read = await this.read();
    this.side.changed();

    // a Withdraw of this window pressed after the Allow and not saved yet is a later word: it stands against the grant too
    return this.contextOf('session', '', read, this.waits()).inForce ? result : { kind: 'withdrawn' };
  }

  /** The row's copy is raised to the floor of an *Allow* that was given (`restart`: removed). */
  private async floorWritten(result: Extract<Allowed, { readonly kind: 'given' | 'kept' }>, utc: string): Promise<void> {
    if (result.kind === 'given') {
      await this.side.row.change({ floor: result.floor, utc, ...(result.restarted ? { restart: true as const } : {}) });
    }
  }

  // ───────────── *Withdraw* ─────────────

  /** A person's *Withdraw* (§ P7): this window holds it, the row's copy is written, and the signal fires — all before anything is awaited. */
  withdraw(): Promise<Withdrew> {
    const utc = this.side.now();
    this.presses += 1;
    const press = this.presses;
    const above = this.known;
    const copy = this.side.row.read().withdrawn;
    const first = Math.max(above, typeof copy === 'number' ? copy : 0) + 1;
    const rowDone = Promise.resolve(this.side.row.change({ withdrawn: first, utc, by: 'press' }));
    this.side.changed();

    return this.side.queued(async () => {
      await rowDone;

      return this.landed_(await this.side.refusals.withdraw({ utc, above: Math.max(above, first - 1) }), press, true);
    });
  }

  /** What a withdrawal came to: on `withdrawn` the row is raised to its stamp and this window holds nothing; else it stays held, and the person is told once. */
  private async landed_(result: Awaited<ReturnType<RefusalsStore['withdraw']>>, press: number, tell: boolean): Promise<Withdrew> {
    if (result.kind === 'unwritten') {
      if (tell) {
        this.side.unkeptWithdrawal(result.why);
      }

      return result;
    }
    this.landed = Math.max(this.landed, press);
    this.known = Math.max(this.known, result.stamp);
    await this.side.row.change({ withdrawn: result.stamp, utc: this.side.now(), by: 'press' });
    this.side.changed();

    return 'withdrawn';
  }

  /** A withdrawal held unsaved is tried again, with a stamp taken THEN. `tell`: a person's *Save now* is told again when it still fails. */
  async landUnsaved(tell: boolean): Promise<boolean> {
    if (this.presses <= this.landed) {
      return false;
    }
    const press = this.presses;
    const result = await this.side.refusals.withdraw({ utc: this.side.now(), above: this.known });
    const said = await this.landed_(result, press, tell);

    return said === 'withdrawn';
  }
}
