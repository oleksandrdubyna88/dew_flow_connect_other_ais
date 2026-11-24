import * as vscode from 'vscode';
import { oneAtATime } from './oneAtATime';
import { readAccounts } from './remoteAccountsReply';
import {
  AccountsCall, Memory, RoundResult, RoundSeams, STATE_KEY, TimerAction, Trigger, accountsHosts, armFor, callFor, droppedLine, eligibleOf, isStopped, liveOnly, logGate,
  ROUND_GAP_MS, memoryAfter, nextAfterRound, openStarts, refreshAnswer, refusedHosts, runRound, settleRound, standingsBump, startTheRound, stopTheRound, timerFor, unreadHost, withRefusals,
} from './remoteAccountsRound';
import type { RemoteSessions } from './remoteSessionHost';
import { ACCOUNTS_LIMITS, defaultRemoteSeams, runAccounts } from './remoteSsh';
import { liveTerminals } from './terminalHost';

/**
 * The `vscode` seams of the accounts round (E1.S3, contract C4 and C7). Host code: every decision AND every orchestration is
 * `remoteAccountsRound.ts`'s (pure, tested with fake seams and a fake clock); this file holds the window's memory and its one timer, reads
 * the terminals and the Stop, and binds the call.
 *
 * <p>Nothing of a reading is written anywhere: no `globalState`, no file, no log. The state row is written only through the pure
 * `stopTheRound` and `startTheRound`. No `console` call here: the log goes through the one default seam of `remoteSsh.ts`.</p>
 *
 * <p><b>Test mode: two locks.</b> In `ExtensionMode.Test` the round never arms itself (`timerFor`) and the default call is a stub that spawns
 * nothing (`callFor`), so the three older host scenarios that open `Creds: h / s` start no `ssh`. A scenario drives rounds with `runRoundNow`.</p>
 *
 * <p>Unit tests cannot load this module (it imports `vscode`); `terminalAccountsScenario.ts` runs it in a real editor, in CI.</p>
 */

/** The window's accounts round, for the list (S4): *Refresh now*, opening the list, Stop and Start, what memory holds, and an event when a result landed. */
export interface TerminalAccounts {
  readonly refresh: () => Promise<'start' | 'busy' | 'floor'>;
  readonly openStarts: () => Promise<boolean>;
  readonly stop: () => Promise<void>;
  readonly start: () => Promise<boolean>;
  readonly memory: () => Memory;
  readonly onLanded: vscode.Event<void>;
  /** For the test door only. */
  readonly door: TerminalAccountsTestDoor;
}

/** What a host scenario may reach: present only in test mode (`remoteExportGuard.test.ts` holds that). */
export interface TerminalAccountsTestDoor {
  /** Replace the call; `undefined` puts the stub back (in test mode the default call is a stub). */
  readonly replaceCall: (call: AccountsCall | undefined) => void;
  readonly runRoundNow: (trigger: Trigger) => Promise<RoundResult | undefined>;
  /** Whether the timer would be armed: what `armFor` answers now. In test mode no timer ever runs. */
  readonly armed: () => Promise<boolean>;
  readonly memoryHosts: () => readonly string[];
}

export const terminalAccountsTestDoorOf = (accounts: TerminalAccounts): TerminalAccountsTestDoor => accounts.door;

const ROUND_FAILED_LINE = 'ConnectOtherAIs: an accounts round could not finish';

/** The terminals of this window, as the round reads them. */
const windowTerminals = (): ReturnType<typeof liveTerminals> => liveTerminals(vscode.window.terminals);

/** The window's one round: its memory, its one timer, and the latch that keeps two rounds from running at once. */
class AccountsWindow implements TerminalAccounts {
  readonly onLanded: vscode.Event<void>;
  readonly door: TerminalAccountsTestDoor;
  private readonly landed = new vscode.EventEmitter<void>();
  private readonly latch = oneAtATime<RoundResult | undefined>(undefined);
  private readonly seams: RoundSeams;
  private memoryNow: Memory = new Map();
  private round = 0;
  private lastEndMs: number | undefined;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private armed = false;
  private running = false;
  /** Bumped on a Stop, on a refusal and on a Forget: a result of a round that started before it is dropped. */
  private generation = 0;
  private replaced: AccountsCall | undefined;
  /** The hosts the list held at the last change, and which of them were refused. */
  private known: ReadonlySet<string> = new Set();
  private refused: ReadonlySet<string> = new Set();

  constructor(private readonly context: vscode.ExtensionContext, private readonly sessions: RemoteSessions, private readonly test: boolean) {
    this.onLanded = this.landed.event;
    this.seams = this.seamsOf();
    this.door = {
      replaceCall: (call) => { this.replaced = call; },
      runRoundNow: (trigger) => this.runNow(trigger),
      armed: async () => (await this.decision(false)) === 'arm-first',
      memoryHosts: () => [...this.memoryNow.keys()].sort(),
    };
  }

  /** The Stop, the door, the ONE call and the ONE parse. */
  private seamsOf(): RoundSeams {
    const write = defaultRemoteSeams().log;
    const gate = logGate(write);
    const real: AccountsCall = (host, mode) => runAccounts(host, mode, ACCOUNTS_LIMITS, { ...defaultRemoteSeams(), log: gate.seamFor(host) });

    return {
      live: windowTerminals,
      stopped: () => this.stoppedNow(),
      admit: (host) => this.sessions.admitHost(host),
      call: async (host, mode) => {
        const run = await callFor(this.test, real, this.replaced)(host, mode);
        if (run.kind === 'answered') {
          gate.answered(host);
        }

        return run;
      },
      parse: (run, mode) => readAccounts(run, mode, Date.now()),
      now: () => Date.now(),
      landed: () => { this.landed.fire(); },
      generation: () => this.generation,
      dropped: (host) => { write(droppedLine(host)); },
    };
  }

  private stoppedNow(): Promise<boolean> {
    return isStopped(this.sessions, () => this.context.globalState.get(STATE_KEY));
  }

  private async eligibleNow(): Promise<boolean> {
    return eligibleOf(accountsHosts(windowTerminals()), await this.sessions.hostStandings()).length > 0;
  }

  /** What `armFor` says now; `armed` is whether this window's timer is set. */
  private async decision(armed: boolean): Promise<ReturnType<typeof armFor>> {
    return armFor({ eligible: await this.eligibleNow(), armed, stopped: await this.stoppedNow(), running: this.running });
  }

  /** The ONE place a timer is set: only what `timerFor` answers is applied. */
  private apply(action: TimerAction): void {
    if (action.kind === 'none') {
      return;
    }
    clearTimeout(this.timer);
    this.timer = action.kind === 'start' ? setTimeout(() => { this.timer = undefined; this.armed = false; void this.runNow('timer'); }, action.ms) : undefined;
    this.armed = action.kind === 'start';
  }

  rearm = async (): Promise<void> => {
    this.apply(timerFor(await this.decision(this.armed), this.test));
  };

  /**
   * One round, if none runs. A round that fails is one fixed line and nothing else, and the timer is still set from now (`settleRound`):
   * rounds never stop silently.
   */
  runNow(trigger: Trigger): Promise<RoundResult | undefined> {
    return this.latch(async () => {
      this.running = true;
      try {
        return await settleRound(
          () => runRound(this.seams, this.memoryNow, this.round, trigger),
          (done) => this.afterRound(done),
          () => { this.apply(timerFor({ kind: 'at', atMs: Date.now() + ROUND_GAP_MS }, this.test, Date.now())); },
          () => { defaultRemoteSeams().log(ROUND_FAILED_LINE); },
        );
      } finally {
        this.running = false;
      }
    });
  }

  /** After a round, or after one that failed (`done` is nothing): the memory it left, and the next timer, counted from its end or from now. */
  private async afterRound(done: RoundResult | undefined): Promise<void> {
    if (done !== undefined) {
      this.memoryNow = memoryAfter(this.memoryNow, done);
      this.round += 1;
      this.lastEndMs = done.endedMs;
    }
    const next = nextAfterRound({ stopped: await this.stoppedNow(), eligible: await this.eligibleNow(), endedMs: done?.endedMs ?? Date.now() });
    this.apply(timerFor(next, this.test, Date.now()));
  }

  /** A terminal closed: a host with none left is dropped from memory. */
  closed = (): void => {
    this.memoryNow = liveOnly(this.memoryNow, accountsHosts(windowTerminals()));
    void this.rearm();
  };

  /** The change signal of the list: a refusal or a Forget bumps the generation; a refused host keeps an entry with no reading and the kind `refused`. */
  changedList = async (): Promise<void> => {
    const standings = await this.sessions.hostStandings();
    if (standingsBump(this.known, this.refused, standings)) {
      this.generation += 1;
    }
    this.known = new Set(standings.map((one) => one.host));
    this.refused = refusedHosts(standings);
    this.memoryNow = withRefusals(this.memoryNow, this.refused);
    this.landed.fire();
    await this.rearm();
  };

  async stop(): Promise<void> {
    this.generation += 1;
    clearTimeout(this.timer);
    this.timer = undefined;
    this.armed = false;
    this.memoryNow = new Map();
    this.landed.fire();
    await stopTheRound(this.sessions, new Date().toISOString());
  }

  async start(): Promise<boolean> {
    const started = await startTheRound(this.sessions);
    if (started) {
      void this.runNow('refresh');
    }

    return started;
  }

  async refresh(): Promise<'start' | 'busy' | 'floor'> {
    const answer = refreshAnswer({ running: this.running, lastEndMs: this.lastEndMs, now: Date.now() });
    if (answer === 'start') {
      void this.runNow('refresh');
    }

    return answer;
  }

  async openStarts(): Promise<boolean> {
    const unread = unreadHost(accountsHosts(windowTerminals()), this.memoryNow, await this.sessions.hostStandings());
    const starts = openStarts({ running: this.running, lastEndMs: this.lastEndMs, now: Date.now(), unreadHost: unread });
    if (starts) {
      void this.runNow('open');
    }

    return starts;
  }

  memory(): Memory {
    return this.memoryNow;
  }

  dispose(): void {
    clearTimeout(this.timer);
    this.landed.dispose();
  }
}

/**
 * Activation: the window's round, armed at once when an eligible terminal is open (30 s later), and again when a terminal opens or closes.
 * `test` is `context.extensionMode === vscode.ExtensionMode.Test` and nothing else.
 */
export function bindTerminalAccounts(context: vscode.ExtensionContext, sessions: RemoteSessions, test: boolean): TerminalAccounts {
  const accounts = new AccountsWindow(context, sessions, test);
  context.subscriptions.push(
    accounts,
    vscode.window.onDidOpenTerminal(() => { void accounts.rearm(); }),
    vscode.window.onDidCloseTerminal(accounts.closed),
    sessions.onChanged(() => { void accounts.changedList(); }),
  );
  void accounts.changedList();

  return accounts;
}
