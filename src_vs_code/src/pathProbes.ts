import { ROOT_ANSWER_LIFETIME_MS, presenceOnFailure, type FilePresence } from './pathFamily';
import type { Presence, ProbeKind } from './pathFindings';

/**
 * The disk, asked about one path at a time and never waited for by a render (research/PLAN_paths_per_side.md E5.1, as amended
 * by the gate's cadence consultant).
 *
 * <p><b>What the amendment found.</b> `existingHere` awaited each root's `stat` in turn with no deadline, so one stalled
 * share stopped every later path from being asked, while the 60 s expiry of `RootExistence` let a SECOND probe start
 * beside the stalled one. So, here: every probe has a <b>deadline</b> — past it the path is answered `unknown`, which is
 * never counted as missing and never raises the notice — there is <b>at most one outstanding probe per path</b> (a path
 * still being asked is not asked again, and an expiry starts no second one), and an `unknown` answer is <b>never kept as
 * fresh</b>: the next read asks again, bounded by that same one-outstanding rule. A definitive answer lives
 * {@link ROOT_ANSWER_LIFETIME_MS}, as the roots' do, so a folder created or removed later is seen within a minute.</p>
 *
 * <p>The disk, the clock and the timer are handed in, so a test runs a never-settling share beside a missing local
 * folder without sleeping. {@link PathProbes.presence} is SYNCHRONOUS — what the host knows now, `pending` before the
 * first answer — because a repaint must never wait for a share; {@link PathProbes.settled} is for the one caller that
 * must know before it acts (the settings-file write, E5.7), and it waits the deadline at most.</p>
 */

/** How the disk is asked: the three answers of `fileAt`/`directoryAt`, or a rejection, which is `unknown`. */
export type Stat = (path: string, kind: ProbeKind) => Promise<FilePresence>;

/** A timer: run `work` after `ms`. The host's `setTimeout`; a test's clock. */
export type Later = (work: () => void, ms: number) => void;

/** How long a probe may take before it is answered `unknown` — a share that does not answer is not a missing folder. */
export const PROBE_DEADLINE_MS = 5_000;

/** The clock and the bounds the probes run on — the host's by default, a test's by hand. */
export interface ProbeOptions {
  readonly now: () => number;
  readonly later: Later;
  readonly deadlineMs: number;
  readonly lifetimeMs: number;
}

const HOST_OPTIONS: ProbeOptions = {
  now: Date.now,
  later: (work, ms) => { setTimeout(work, ms).unref(); },
  deadlineMs: PROBE_DEADLINE_MS,
  lifetimeMs: ROOT_ANSWER_LIFETIME_MS,
};

/** The last thing the disk said about a path, and when. */
interface Said {
  readonly last: FilePresence;
  readonly at: number;
}

export class PathProbes {
  private said = new Map<string, Said>();
  private readonly outstanding = new Map<string, Promise<FilePresence>>();
  private readonly listeners: (() => void)[] = [];
  private readonly options: ProbeOptions;

  constructor(private readonly stat: Stat, options: Partial<ProbeOptions> = {}) {
    this.options = { ...HOST_OPTIONS, ...options };
  }

  /**
   * What is known about `path` now, without waiting: a fresh definitive answer as it stands; otherwise the last answer
   * (or `pending`) — and a probe is started, unless one is already outstanding for this path.
   */
  presence(path: string, kind: ProbeKind): Presence {
    const fresh = this.freshAnswer(path);
    if (fresh !== undefined) {
      return fresh;
    }
    void this.ask(path, kind);

    return this.lastAnswer(path);
  }

  /**
   * The disk's answer about `path`, waited for — the deadline at most: a probe that has not answered by then is `unknown`,
   * recorded as such, and stays the one outstanding probe for its path until it really settles.
   */
  async settled(path: string, kind: ProbeKind): Promise<FilePresence> {
    return this.freshAnswer(path) ?? this.bounded(path, this.ask(path, kind));
  }

  /**
   * Asks the disk about `path` again NOW, however fresh the last answer — the watch's recheck of a path it found absent,
   * so a folder made again is seen without a settings change (research/PLAN_paths_per_side.md E5.7, risk item F). Still one
   * outstanding probe per path; a changed answer tells the listeners, as every answer does.
   */
  recheck(path: string, kind: ProbeKind): void {
    void this.ask(path, kind);
  }

  /** A setting changed: what the disk said is dropped, so the next read asks again. Outstanding probes stay outstanding. */
  forget(): void {
    this.said = new Map();
  }

  /** Told whenever an answer lands that differs from the last one about its path. */
  onChange(listener: () => void): void {
    this.listeners.push(listener);
  }

  /** How many probes are running — a test's seam for "one per path, however many renders". */
  outstandingProbes(): number {
    return this.outstanding.size;
  }

  /** A definitive answer younger than the lifetime — never an unknown one, which is asked again. */
  private freshAnswer(path: string): FilePresence | undefined {
    const known = this.said.get(path);
    const fresh = known !== undefined && known.last !== 'unknown' && this.options.now() - known.at < this.options.lifetimeMs;

    return fresh ? known.last : undefined;
  }

  private lastAnswer(path: string): Presence {
    return this.said.get(path)?.last ?? 'pending';
  }

  /** The one probe of a path: the running one, or a new one whose answer — or rejection — is recorded when it lands. */
  private ask(path: string, kind: ProbeKind): Promise<FilePresence> {
    const running = this.outstanding.get(path);
    if (running !== undefined) {
      return running;
    }
    const probe = this.stat(path, kind)
      .then((answer) => answer, (): FilePresence => 'unknown')
      .then((answer) => {
        this.outstanding.delete(path);
        this.record(path, answer);

        return answer;
      });
    this.outstanding.set(path, probe);

    return probe;
  }

  /** The probe, or `unknown` at the deadline — recorded, so the page says the disk did not answer. */
  private bounded(path: string, probe: Promise<FilePresence>): Promise<FilePresence> {
    return new Promise((resolve) => {
      let answered = false;
      const once = (answer: FilePresence): void => {
        if (!answered) {
          answered = true;
          resolve(answer);
        }
      };
      this.options.later(() => {
        if (!answered) {
          this.record(path, 'unknown');
        }
        once('unknown');
      }, this.options.deadlineMs);
      void probe.then(once);
    });
  }

  private record(path: string, answer: FilePresence): void {
    const before = this.said.get(path)?.last;
    this.said = new Map([...this.said, [path, { last: answer, at: this.options.now() }]]);
    if (before !== answer) {
      for (const listener of this.listeners) {
        listener();
      }
    }
  }
}

/**
 * The host's disk, asked as the probes ask it: a folder or a file, by one `stat` whose failure is mapped as `directoryAt`
 * and `fileAt` map it (`presenceOnFailure`): the disk SAID nothing is there, or it could not tell.
 */
export function statProbe(stat: (path: string) => Promise<{ isDirectory(): boolean; isFile(): boolean }>): Stat {
  return (path, kind) => stat(path).then((found) => saidPresence(found, kind), presenceOnFailure);
}

/** What a stat that answered says about the kind asked for — the other kind at that path is an absence. */
function saidPresence(found: { isDirectory(): boolean; isFile(): boolean }, kind: ProbeKind): FilePresence {
  const wanted = kind === 'folder' ? found.isDirectory() : found.isFile();

  return wanted ? 'here' : 'absent';
}
