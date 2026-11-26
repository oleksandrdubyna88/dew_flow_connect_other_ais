import { oneAtATime } from './oneAtATime';
import {
  AccountsCall, AccountsState, Memory, ROUND_GAP_MS, RoundResult, RoundSeams, STATE_KEY, StateChange, StateCommitted, StopDoor, TimerAction, Trigger, accountsHosts, accountsStateOf, armFor, callableHosts,
  droppedLine, eligibleOf, isStopped, liveOnly, memoryAfter, nextAfterRound, openStarts, permissionEdge, refreshAnswer, refusedHosts, runRound, settleRound, standingsBump, startTheRound,
  startsAfterAllow, stateMerge, stopTheRound, timerFor, unreadHost, withRefusals,
} from './remoteAccountsRound';
import type { HostStanding } from './remoteHosts';
import type { DoorAnswer } from './remotePermission';
import type { WslPort } from './remoteWslRound';
import type { LiveTerminal } from './terminalSource';

/**
 * The window's round under the permission (E2.S4, contract C6). Pure over its seams, so a unit test drives a window — its memory, its ONE timer, its
 * latch — with a fake clock, fake timers, a fake door and a fake store; `remoteAccountsHost.ts` binds the seams to `vscode` and to the keeper.
 *
 * <p><b>No timer and no call without the permission.</b> Every road to a round (a tick, *Refresh now*, opening the tab, *Start*, an *Allow*) reads the
 * permission first and does nothing when it is not in force; the round itself asks the door before every call and before every result, so a
 * withdrawal that no event told this window of ends the round at its next question. The driver is handed NO seam that asks a person: the permission
 * is asked for by a person's press, in `remoteSessionHost.ts`, and nowhere here.</p>
 *
 * <p>A withdrawal bumps the generation, clears the timer and the memory at once; an *Allow* starts a round at once, unless one runs, the round is
 * stopped or no terminal is eligible. At activation the first round is 30 s later (the window restores its terminals first); after each round, 10
 * minutes from its END.</p>
 */

/** The ONE notice of E2 (W15): once for each installation, when a `Creds:` terminal is first seen without the permission. It decides nothing. */
export const CAN_BE_SHOWN = {
  code: 'accounts-can-be-shown',
  title: 'CoAI can show the AI accounts and usage of the hosts of your Creds terminals.',
  action: 'Open',
} as const;

/** The button of the notice opens the tab and does nothing else: no grant, no row. */
export function answerCanBeShown(button: string | undefined, open: () => void): void {
  if (button === CAN_BE_SHOWN.action) {
    open();
  }
}

export const ROUND_FAILED_LINE = 'ConnectOtherAIs: an accounts round could not finish';

/** What the window gives the driver. Nothing in it asks a person. */
export interface DriverWorld {
  /** `ExtensionMode.Test`: no timer is ever started. */
  readonly test: boolean;
  readonly live: () => readonly LiveTerminal[];
  /** The permission is in force on this side, from a fresh read: `sessions.permission()` is `{ given }`. */
  readonly permitted: () => Promise<boolean>;
  /** C2's door for the round. */
  readonly may: (host: string) => Promise<DoorAnswer>;
  readonly standings: () => Promise<readonly HostStanding[]>;
  /** The call, with the test-mode lock already applied. */
  readonly call: AccountsCall;
  readonly parse: RoundSeams['parse'];
  readonly now: () => number;
  readonly setTimer: (ms: number, run: () => void) => unknown;
  readonly clearTimer: (handle: unknown) => void;
  /** A place's block changed (a host), or the whole thing did (`undefined`). */
  readonly landed: (host: string | undefined) => void;
  /** One fixed line. */
  readonly log: (line: string) => void;
  /** The state row as the window holds it. */
  readonly readState: () => unknown;
  readonly door: StopDoor;
  /** The one notice is raised. */
  readonly canBeShown: () => void;
  /** The WSL port of a Windows window (E3.S3); nothing in any other window, and then the scope host `wsl` is never called. */
  readonly wsl?: (() => WslPort | undefined) | undefined;
}

/** What the last round ended with: its end, the places it called and the ones that answered with a reading. */
export interface RoundSummary {
  readonly endedMs: number;
  readonly asked: number;
  readonly read: number;
}

export class AccountsDriver {
  private readonly latch = oneAtATime<RoundResult | undefined>(undefined);
  private readonly seams: RoundSeams;
  private memoryNow: Memory = new Map();
  private round = 0;
  /** The generation the running round started at: the news of a host is applied only while it is still this one. */
  private roundGeneration = 0;
  private lastEndMs: number | undefined;
  private lastSummary: RoundSummary | undefined;
  private timer: unknown;
  private armed = false;
  private running = false;
  /** A Start came while a round ran: its refresh runs when that round ends. */
  private startPending = false;
  private disposed = false;
  /** Bumped on a Stop, on a refusal, on a Forget and on a withdrawal: a result of a round that started before it is dropped. */
  private generation = 0;
  /** What the last read of the permission saw: nothing before the first read. */
  private was: boolean | undefined;
  private reads: Promise<unknown> = Promise.resolve();
  private known: ReadonlySet<string> = new Set();
  private refused: ReadonlySet<string> = new Set();
  private noticing = false;

  constructor(private readonly world: DriverWorld, private readonly policy?: Parameters<typeof runRound>[4]) {
    this.seams = this.seamsOf();
  }

  private seamsOf(): RoundSeams {
    const w = this.world;
    const wslNow = (): WslPort | undefined => this.wslNow();

    return {
      live: w.live,
      stopped: () => this.stoppedNow(),
      may: (host) => w.may(host),
      call: (host, mode) => w.call(host, mode),
      parse: w.parse,
      now: w.now,
      landed: (memory, host) => { this.landedFrom(memory, host); },
      generation: () => this.generation,
      dropped: (host) => { w.log(droppedLine(host)); },
      get wsl() { return wslNow(); },
    };
  }

  /** The WSL port as the window gives it now (a test may replace it between two rounds). */
  private wslNow(): WslPort | undefined {
    return this.world.wsl?.();
  }

  private wslOn(): boolean {
    return this.wslNow() !== undefined;
  }

  // ───────────── what the window asks of it ─────────────

  memory(): Memory {
    return this.memoryNow;
  }

  isRunning(): boolean {
    return this.running;
  }

  summary(): RoundSummary | undefined {
    return this.lastSummary;
  }

  /** Whether the timer would be armed outside test mode, from what `armFor` says now. */
  async armedNow(): Promise<boolean> {
    return timerFor(await this.decision(false), false).kind === 'start';
  }

  stoppedNow(): Promise<boolean> {
    return isStopped(this.world.door, () => this.world.readState());
  }

  // ───────────── the permission ─────────────

  /** One read of the permission at a time, so a stale answer never overtakes a newer one. A withdrawal clears at once. */
  private sync(): Promise<{ readonly permitted: boolean; readonly edge: ReturnType<typeof permissionEdge> }> {
    const next = this.reads.then(async () => {
      const permitted = await this.world.permitted();
      const edge = permissionEdge(this.was, permitted);
      this.was = permitted;
      if (edge === 'withdrawn') {
        this.withdrawnNow();
      }

      return { permitted, edge };
    });
    this.reads = next.catch(() => undefined);

    return next;
  }

  /** A withdrawal: the generation moves, the timer and the memory go, at once. */
  private withdrawnNow(): void {
    this.generation += 1;
    this.startPending = false;
    this.clear();
    this.memoryNow = new Map();
    this.world.landed(undefined);
  }

  private clear(): void {
    if (this.timer !== undefined) {
      this.world.clearTimer(this.timer);
    }
    this.timer = undefined;
    this.armed = false;
  }

  // ───────────── the timer ─────────────

  private async eligibleNow(): Promise<boolean> {
    return eligibleOf(callableHosts(this.world.live(), this.wslOn()), await this.world.standings()).length > 0;
  }

  private async decision(armed: boolean): Promise<ReturnType<typeof armFor>> {
    return armFor({ eligible: await this.eligibleNow(), permitted: await this.world.permitted(), armed, stopped: await this.stoppedNow(), running: this.running });
  }

  /** The ONE place a timer is set: only what `timerFor` answers is applied. */
  private apply(action: TimerAction): void {
    if (this.disposed || action.kind === 'none') {
      return;
    }
    this.clear();
    if (action.kind === 'start') {
      this.startAfter(action.ms);
    }
  }

  private startAfter(ms: number): void {
    this.timer = this.world.setTimer(ms, () => { this.timer = undefined; this.armed = false; void this.runNow('timer'); });
    this.armed = true;
  }

  rearm = async (): Promise<void> => {
    const { permitted } = await this.sync();
    this.apply(timerFor(await this.decision(this.armed), this.world.test));
    await this.observe(permitted);
  };

  // ───────────── the rounds ─────────────

  /** A host's news landed: the round's memory so far is the window's, unless a Stop, a refusal, a Forget or a withdrawal came since the round started. */
  private landedFrom(memory: Memory, host: string): void {
    if (this.running && this.generation === this.roundGeneration) {
      this.memoryNow = liveOnly(memory, accountsHosts(this.world.live()));
      this.world.landed(host);
    }
  }

  /**
   * One round, if none runs and the permission is in force. With no permission nothing happens: no round, no door question, no timer, no notice
   * beyond the one a terminal brings.
   */
  async runNow(trigger: Trigger): Promise<RoundResult | undefined> {
    if (this.disposed || !(await this.sync()).permitted || this.disposed) {
      return undefined;
    }
    const mine = { ran: false };
    const result = await this.latch(() => this.oneRound(trigger, mine));
    this.runPending(mine.ran);

    return result;
  }

  private async oneRound(trigger: Trigger, mine: { ran: boolean }): Promise<RoundResult | undefined> {
    mine.ran = true;
    this.running = true;
    this.roundGeneration = this.generation;
    this.world.landed(undefined);
    try {
      return await settleRound(
        () => runRound(this.seams, this.memoryNow, this.round, trigger, this.policy),
        (done) => this.afterRound(done),
        () => { this.apply(timerFor({ kind: 'at', atMs: this.world.now() + ROUND_GAP_MS }, this.world.test, this.world.now())); },
        () => { this.world.log(ROUND_FAILED_LINE); },
      );
    } finally {
      this.running = false;
      this.world.landed(undefined);
    }
  }

  private runPending(ran: boolean): void {
    if (ran && this.startPending && !this.disposed) {
      this.startPending = false;
      void this.runNow('refresh');
    }
  }

  /** After a round, or one that failed (`done` is nothing): a withdrawal seen only now clears first; then the memory it left and the next timer. */
  private async afterRound(done: RoundResult | undefined): Promise<void> {
    const { permitted } = await this.sync();
    this.keep(done);
    const next = nextAfterRound({ stopped: await this.stoppedNow(), eligible: await this.eligibleNow(), permitted, endedMs: done?.endedMs ?? this.world.now() });
    this.apply(timerFor(next, this.world.test, this.world.now()));
  }

  /** What a round that ended leaves in the window: its memory (an abandoned round's is not kept), its count, its end and its summary. */
  private keep(done: RoundResult | undefined): void {
    if (done === undefined) {
      return;
    }
    this.memoryNow = memoryAfter(this.memoryNow, done);
    this.round += 1;
    this.lastEndMs = done.endedMs;
    this.lastSummary = done.abandoned ? this.lastSummary : { endedMs: done.endedMs, asked: done.asked, read: done.read };
  }

  /** A terminal closed: a host with none left is dropped from memory. */
  closed = (): void => {
    this.memoryNow = liveOnly(this.memoryNow, accountsHosts(this.world.live()));
    void this.rearm();
  };

  /**
   * The change signal of the keeper: a refusal or a Forget bumps the generation; a refused host keeps an entry with no reading and the kind `refused`;
   * a withdrawal clears at once; an *Allow* starts a round at once.
   */
  changed = async (): Promise<void> => {
    const standings = await this.world.standings();
    if (standingsBump(this.known, this.refused, standings)) {
      this.generation += 1;
    }
    this.known = new Set(standings.map((one) => one.host));
    this.refused = refusedHosts(standings);
    this.memoryNow = withRefusals(this.memoryNow, this.refused);
    this.world.landed(undefined);
    const { permitted, edge } = await this.sync();
    if (edge === 'allowed' && startsAfterAllow({ running: this.running, stopped: await this.stoppedNow(), eligible: await this.eligibleNow() })) {
      void this.runNow('refresh');

      return;
    }
    this.apply(timerFor(await this.decision(this.armed), this.world.test));
    await this.observe(permitted);
  };

  /** A `Creds:` terminal is open, the permission is not in force, and no notice is on its way. */
  private noticeDue(permitted: boolean): boolean {
    return !permitted && !this.noticing && !this.disposed && accountsHosts(this.world.live()).length > 0;
  }

  /** The one notice: a `Creds:` terminal is open and the permission is not in force. */
  private async observe(permitted: boolean): Promise<void> {
    if (!this.noticeDue(permitted)) {
      return;
    }
    this.noticing = true;
    try {
      await tellCanBeShown({ state: () => this.state(), nowMs: this.world.now, commit: (change) => this.world.door.commitKey(STATE_KEY, change, stateMerge), show: this.world.canBeShown });
    } finally {
      this.noticing = false;
    }
  }

  private state(): AccountsState | undefined {
    const read = accountsStateOf(this.world.readState());

    return read.kind === 'state' ? read.state : undefined;
  }

  // ───────────── the person's presses ─────────────

  async stop(): Promise<void> {
    this.generation += 1;
    this.startPending = false;
    this.clear();
    this.memoryNow = new Map();
    await stopTheRound(this.world.door, new Date(this.world.now()).toISOString());
    this.world.landed(undefined);
  }

  async start(): Promise<boolean> {
    const started = await startTheRound(this.world.door);
    if (started && this.running) {
      this.startPending = true;
    } else if (started) {
      void this.runNow('refresh');
    }

    return started;
  }

  async refresh(): Promise<'start' | 'busy' | 'floor'> {
    const answer = refreshAnswer({ running: this.running, lastEndMs: this.lastEndMs, now: this.world.now() });
    if (answer === 'start') {
      void this.runNow('refresh');
    }

    return answer;
  }

  async openStarts(): Promise<boolean> {
    const unread = unreadHost(callableHosts(this.world.live(), this.wslOn()), this.memoryNow, await this.world.standings());
    const starts = openStarts({ running: this.running, lastEndMs: this.lastEndMs, now: this.world.now(), unreadHost: unread });
    if (starts) {
      void this.runNow('open');
    }

    return starts;
  }

  dispose(): void {
    this.disposed = true;
    this.generation += 1;
    this.startPending = false;
    if (this.timer !== undefined) {
      this.world.clearTimer(this.timer);
    }
  }
}

// ───────────────── the one notice ─────────────────

/** What the notice needs: the state row as it can be read (`undefined` over a row that cannot be: nobody is told), the clock, one commit and the toast. */
export interface CanBeShownSeams {
  readonly state: () => unknown;
  readonly nowMs: () => number;
  readonly commit: (change: StateChange) => Promise<StateCommitted>;
  readonly show: () => void;
}

/**
 * Once for each installation: the time is written FIRST and the toast is raised only when this write was the one that took it, so two windows show
 * it once between them. A row that cannot be read tells nobody.
 */
export async function tellCanBeShown(seams: CanBeShownSeams): Promise<void> {
  const state = seams.state() as { readonly canBeShownUtc?: string } | undefined;
  if (state === undefined || state.canBeShownUtc !== undefined) {
    return;
  }
  const written = await seams.commit({ canBeShown: new Date(seams.nowMs()).toISOString() });
  if (written.kind === 'written') {
    seams.show();
  }
}
