import { CALLER_KINDS } from './consultSettings';
import { type FileRead, type SideFiles, readSide, sideSignature, withBeatsSeen } from './consultantHealthRead';
import type { HealthSide } from './consultantSides';

/**
 * The health files of every side, kept fresh — E5.4 of `research/PLAN_the_consultant_works_on_every_vendor.md`.
 *
 * <p>The `ConsultationWatcher` shape: THIS side's `consultations/health/*.json` under a file watcher, and EVERY side
 * on a five-second poll — a watcher on a path outside the workspace is not guaranteed to fire, and on a
 * `\\wsl.localhost` share there is none to be had, so the poll is what makes "you will see it" true. Ports rather than
 * `vscode`, so the rules are tests: a side that could not be read keeps its last snapshot; the view is told only when
 * what a person would see changed (`sideSignature` leaves the heartbeat out and judges the other side's checks by the
 * clock); and a check state — or an outcome file — that moved on THIS side asks the server again, because only it can
 * settle a check by its lock and decide whether a failure is current. One refresh at a time: a share that answers slowly
 * is not read twice over itself.</p>
 *
 * <p><b>Whose clock</b> (the whole-branch review, O): every poll notes when THIS window first saw each heartbeat value
 * (`withBeatsSeen`), and another side's `checking` is judged by how long that value has stood still on this window's own
 * clock — a WSL distribution's clock drifts from its Windows host's, so its stamp is never subtracted from ours.</p>
 *
 * <p><b>The first read only takes note</b> (the review, N3): the render that started the watcher already asked the
 * server, so this side's first signature is recorded without asking again — opening the tab runs `--consultants` once.</p>
 */

/** The same five seconds the consultation and escalation watchers poll at, for the same reason. */
export const HEALTH_POLL_MS = 5000;

/** The Settings tab's id for the Consultant tab — `settingsSections()` in `panelView.ts`. */
export const CONSULTANT_TAB = 'consultant';

/**
 * Whether the Consultant tab is SHOWING — the Settings tab is open and visible, and the tab it holds is the Consultant
 * tab — which is the only time its health is computed and its stores are watched (the whole-branch review, N2): a hidden
 * Settings tab, or one showing Reviewers, would otherwise spawn `--consultants` and poll a WSL share for a
 * block nobody can see.
 */
export function consultantTabShowing(settingsVisible: boolean, heldTab: string): boolean {
  // On the new page the Consultant tab is a place, and the Models tab shows each row's ✓ Check (PLAN_one_model_catalog.md E3.3).
  return settingsVisible && HEALTH_SHOWN_ON.includes(heldTab);
}

const HEALTH_SHOWN_ON: readonly string[] = [CONSULTANT_TAB, 'consultants/consultant', 'models'];

export interface HealthWatchPorts {
  /** The sides to read, asked on every refresh — the setting that names them can change while the window is open. */
  readonly sides: () => readonly HealthSide[];
  readonly read: (path: string) => Promise<FileRead>;
  readonly now: () => number;
  /** Watches `<dir>/consultations/health/*.json`; the function returned stops it. */
  readonly watch: (dir: string, changed: () => void) => () => void;
  readonly every: (ms: number, tick: () => void) => () => void;
  /** The checks to read beyond the caller kinds' — each catalog row's `model-<id>` (PLAN_one_model_catalog.md E3.3). */
  readonly moreKinds?: () => readonly string[];
}

const KINDS: readonly string[] = CALLER_KINDS.map((one) => one.id);

export class ConsultantHealthWatcher {
  /** Called when what any side shows changed. */
  public onChanged: () => void = () => undefined;

  /** Called when THIS side's check states moved — the server is asked again, because only it can settle them. */
  public onOwnChecksChanged: () => void = () => undefined;

  private snapshots: ReadonlyMap<string, SideFiles> = new Map();

  private shown = '';

  private ownChecks = '';

  /** Whether this side's signature has been noted once — the first read records it, every later change re-asks. */
  private primed = false;

  private refreshing = false;

  private stops: readonly (() => void)[] = [];

  constructor(private readonly ports: HealthWatchPorts) {}

  /** Starts watching and polling — or does nothing when it already is: every render that asks may call it. */
  start(): void {
    if (this.stops.length > 0) {
      return;
    }
    const own = this.ports.sides().find((side) => side.kind === 'this');
    const kick = (): void => { void this.refresh(); };
    this.stops = [
      ...(own === undefined ? [] : [this.ports.watch(own.dir, kick)]),
      this.ports.every(HEALTH_POLL_MS, kick),
    ];
    kick();
  }

  /** What a side last read as — undefined while it has never been read. */
  side(dir: string): SideFiles | undefined {
    return this.snapshots.get(dir);
  }

  /** Re-reads every side once, and tells the view only what changed. */
  async refresh(): Promise<void> {
    if (this.refreshing) {
      return;
    }
    this.refreshing = true;
    try {
      await this.readAll(this.ports.sides());
    } finally {
      this.refreshing = false;
    }
  }

  /**
   * Stops watching and polling, and KEEPS what each side last read — the Settings tab closed, and polling a
   * `\\wsl.localhost` share for a tab nobody sees can wake a stopped distribution (epic 5's review). The next
   * {@link start} resumes, with the last snapshots on screen until the first refresh lands.
   */
  pause(): void {
    for (const stop of this.stops) {
      stop();
    }
    this.stops = [];
  }

  dispose(): void {
    this.pause();
  }

  private async readAll(sides: readonly HealthSide[]): Promise<void> {
    const read = await Promise.all(sides.map(async (side) => [side.dir, await readSide(side.dir, [...KINDS, ...(this.ports.moreKinds?.() ?? [])], this.ports, side.kind === 'this')] as const));
    const now = this.ports.now();
    // A side that failed keeps what it had: replaced, never mutated, with only the sides that answered this time — each
    // with when this window first saw its heartbeats, carried over from the read before.
    this.snapshots = new Map([
      ...this.snapshots,
      ...read.flatMap(([dir, files]) => (files === undefined ? [] : [[dir, withBeatsSeen(files, this.snapshots.get(dir), now)] as const])),
    ]);
    this.tell(sides);
  }

  /** This side moved: the server is asked again — except on the very first read, which only records what is there. */
  private askAgainUnlessFirst(): void {
    if (this.primed) {
      this.onOwnChecksChanged();
    }
    this.primed = true;
  }

  private tell(sides: readonly HealthSide[]): void {
    const now = this.ports.now();
    const own = sides.filter((side) => side.kind === 'this').map((side) => sideSignature(this.snapshots.get(side.dir), 'this', now)).join('#');
    const shown = sides.map((side) => `${side.dir}=${sideSignature(this.snapshots.get(side.dir), side.kind, now)}`).join('#');
    if (own !== this.ownChecks) {
      this.ownChecks = own;
      this.askAgainUnlessFirst();
    }
    if (shown !== this.shown) {
      this.shown = shown;
      this.onChanged();
    }
  }
}
