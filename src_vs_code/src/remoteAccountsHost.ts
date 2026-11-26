import * as vscode from 'vscode';
import { byCodeUnit } from './codeUnitOrder';
import { textCopier } from './copyText';
import { notifyThen } from './notify';
import { AccountsDriver, CAN_BE_SHOWN, DriverWorld, RoundSummary, answerCanBeShown } from './remoteAccountsDriver';
import { AccountsPanel } from './remoteAccountsPanel';
import { readAccounts } from './remoteAccountsReply';
import { AccountsCall, Memory, RoundResult, STATE_KEY, Trigger, accountsHosts, callFor, logGate } from './remoteAccountsRound';
import { runAccountsCommand } from './remoteAccountsRun';
import { TableModel, tableModel } from './remoteAccountsTable';
import { readableOf } from './remoteHosts';
import type { RemoteSessions } from './remoteSessionHost';
import { ACCOUNTS_LIMITS, defaultRemoteSeams, runAccounts } from './remoteSsh';
import { registerCommand } from './shellBridge';
import { liveTerminals } from './terminalHost';

/**
 * The `vscode` seams of the accounts round (E1.S3, contract C4 and C7) and the accounts tab's wiring (E2.S3). Host code: every decision AND every
 * orchestration is `remoteAccountsRound.ts`'s and `remoteAccountsDriver.ts`'s (pure, tested with fake seams and a fake clock); this file holds the
 * window's memory and its one timer, reads the terminals and the Stop, binds the call, and hands the tab (`remoteAccountsPanel.ts`) the model.
 *
 * <p>Nothing of a reading is written anywhere: no `globalState`, no file, no log. The state row is written only through the pure
 * `stopTheRound` and `startTheRound` and the one notice's time. No `console` call here: the log goes through the one default seam of `remoteSsh.ts`.</p>
 *
 * <p><b>Test mode: two locks.</b> In `ExtensionMode.Test` the round never arms itself (`timerFor`) and the default call is a stub that spawns
 * nothing (`callFor`), so the three older host scenarios that open `Creds: h / s` start no `ssh`. A scenario drives rounds with `runRoundNow`.</p>
 *
 * <p>Unit tests cannot load this module (it imports `vscode`); `terminalAccountsScenario.ts` runs it in a real editor, in CI.</p>
 */

/** The window's accounts round, for the tab (S3): *Refresh now*, opening the tab, Stop and Start, what memory holds, and an event when a result landed. */
export interface TerminalAccounts {
  readonly refresh: () => Promise<'start' | 'busy' | 'floor'>;
  readonly openStarts: () => Promise<boolean>;
  readonly stop: () => Promise<void>;
  readonly start: () => Promise<boolean>;
  readonly memory: () => Memory;
  /** What the last round ended with: its end, the places it called and the ones that answered. */
  readonly summary: () => RoundSummary | undefined;
  /** What the tab draws now: the window's memory, the standings, the Stop and the permission as ONE model. */
  readonly model: () => Promise<TableModel>;
  /** A place's block changed (its host), or the whole thing did (nothing). */
  readonly onLanded: vscode.Event<string | undefined>;
  /** For the test door only. */
  readonly door: TerminalAccountsTestDoor;
}

/** What a host scenario may reach: present only in test mode (`remoteExportGuard.test.ts` holds that). */
export interface TerminalAccountsTestDoor {
  /** Replace the call; `undefined` puts the stub back (in test mode the default call is a stub). */
  readonly replaceCall: (call: AccountsCall | undefined) => void;
  readonly runRoundNow: (trigger: Trigger) => Promise<RoundResult | undefined>;
  /** Whether the timer would be armed outside test mode: what `timerFor` makes of `armFor`'s answer now (never without the permission). In test mode no timer ever runs. */
  readonly armed: () => Promise<boolean>;
  readonly memoryHosts: () => readonly string[];
  /** The model the open tab holds now, or `undefined` while no tab is open. */
  readonly lastModel: () => TableModel | undefined;
  /** Whether the tab is open. */
  readonly tabOpen: () => boolean;
  /** How many times the one notice `accounts-can-be-shown` was raised in this window. */
  readonly noticeShown: () => number;
}

export const terminalAccountsTestDoorOf = (accounts: TerminalAccounts): TerminalAccountsTestDoor => accounts.door;

/** The terminals of this window, as the round reads them. */
const windowTerminals = (): ReturnType<typeof liveTerminals> => liveTerminals(vscode.window.terminals);

/** The window's one round: the driver (`remoteAccountsDriver.ts`, pure over its seams) bound to `vscode`, the keeper and the clock. */
class AccountsWindow implements TerminalAccounts {
  readonly onLanded: vscode.Event<string | undefined>;
  readonly door: TerminalAccountsTestDoor;
  /** The tab: ONE for this window. Its commands run through `runAccountsCommand` over this window's doors. */
  readonly tab: AccountsPanel;
  private readonly landed = new vscode.EventEmitter<string | undefined>();
  private readonly driver: AccountsDriver;
  private readonly copier = textCopier({
    writeText: (text) => Promise.resolve(vscode.env.clipboard.writeText(text)),
    say: (message, forMs) => vscode.window.setStatusBarMessage(message, forMs),
  });
  private replaced: AccountsCall | undefined;

  constructor(private readonly context: vscode.ExtensionContext, private readonly sessions: RemoteSessions, private readonly test: boolean) {
    this.onLanded = this.landed.event;
    this.driver = new AccountsDriver(this.worldOf());
    this.tab = new AccountsPanel({ run: (command) => { guarded(runAccountsCommand(command, this.seams()).then(() => this.redraw())); } });
    this.door = {
      replaceCall: (call) => { this.replaced = call; },
      runRoundNow: (trigger) => this.driver.runNow(trigger),
      armed: () => this.driver.armedNow(),
      memoryHosts: () => [...this.driver.memory().keys()].sort(byCodeUnit),
      lastModel: () => this.tab.current,
      tabOpen: () => this.tab.isOpen,
      noticeShown: () => noticesRaised,
    };
  }

  /** The doors a command of the tab uses: the road's own and the driver's, bound once. A press that widens only raises a question. */
  private seams(): Parameters<typeof runAccountsCommand>[1] {
    return {
      refresh: () => this.driver.refresh(),
      stop: () => this.driver.stop(),
      start: () => this.driver.start(),
      askPermission: () => this.sessions.askPermission(),
      withdraw: () => this.sessions.withdraw(),
      saveWaiting: () => this.sessions.saveWaiting(),
      refuse: (host) => this.sessions.commitHost({ kind: 'person', host, answer: 'no' }),
      askChange: (host) => this.sessions.changeHost(host),
      copy: (decision) => this.copier.copy(() => decision),
    };
  }

  /** The seams of the driver: the ONE call and the ONE parse, the door of the keeper, the clock and the timer. None of them asks a person. */
  private worldOf(): DriverWorld {
    const write = defaultRemoteSeams().log;
    const gate = logGate(write);
    const real: AccountsCall = (host, mode) => runAccounts(host, mode, ACCOUNTS_LIMITS, { ...defaultRemoteSeams(), log: gate.seamFor(host) });

    return {
      test: this.test,
      live: windowTerminals,
      permitted: async () => typeof (await this.sessions.permission()) === 'object',
      may: (host) => this.sessions.may('round', host),
      standings: () => this.sessions.hostStandings(),
      call: async (host, mode) => {
        const run = await callFor(this.test, real, this.replaced)(host, mode);
        if (run.kind === 'answered') {
          gate.answered(host);
        }

        return run;
      },
      parse: (run, mode) => readAccounts(run, mode, Date.now()),
      now: () => Date.now(),
      setTimer: (ms, run) => setTimeout(run, ms),
      clearTimer: (handle) => { clearTimeout(handle as ReturnType<typeof setTimeout>); },
      landed: (host) => { this.landed.fire(host); },
      log: write,
      readState: () => this.context.globalState.get(STATE_KEY),
      door: this.sessions,
      canBeShown: () => { canBeShownNotice(); },
    };
  }

  rearm = (): Promise<void> => this.driver.rearm();

  closed = (): void => { this.driver.closed(); };

  changedList = (): Promise<void> => this.driver.changed();

  stop(): Promise<void> {
    return this.driver.stop();
  }

  start(): Promise<boolean> {
    return this.driver.start();
  }

  refresh(): Promise<'start' | 'busy' | 'floor'> {
    return this.driver.refresh();
  }

  openStarts(): Promise<boolean> {
    return this.driver.openStarts();
  }

  memory(): Memory {
    return this.driver.memory();
  }

  summary(): RoundSummary | undefined {
    return this.driver.summary();
  }

  /** The model, from what the window holds now (`tableModel`, pure). */
  async model(): Promise<TableModel> {
    const summary = this.driver.summary();
    const file = (await this.sessions.roundStop()) === 'damaged';
    const list = readableOf(this.sessions.keeper.stored()).damaged;

    return tableModel({
      hosts: accountsHosts(windowTerminals()),
      memory: this.driver.memory(),
      standings: await this.sessions.hostStandings(),
      stopped: await this.driver.stoppedNow(),
      permission: await this.sessions.permission(),
      damaged: file ? 'file' : list ? 'list' : undefined,
      round: summary === undefined ? { kind: 'idle' } : { kind: 'ended', endedMs: summary.endedMs, read: summary.read, asked: summary.asked },
      windows: process.platform === 'win32',
    });
  }

  /** The tab is opened (or revealed) with the model of now. */
  async openTab(): Promise<void> {
    this.tab.show(await this.model());
  }

  /** After a press, a refusal or a permission change: the whole model again, when a tab is open. */
  async redraw(): Promise<void> {
    if (this.tab.isOpen) {
      this.tab.replaceModel(await this.model());
    }
  }

  /** A place landed: its block (and the blocks that share its account), or the whole model after a Stop. */
  async afterLanded(host: string | undefined): Promise<void> {
    if (!this.tab.isOpen) {
      return;
    }
    const next = await this.model();
    if (host === undefined) {
      this.tab.replaceModel(next);
    } else {
      this.tab.landed(next);
    }
  }

  dispose(): void {
    this.driver.dispose();
    this.landed.dispose();
  }
}

/**
 * Activation: the window's round, armed 30 s after an eligible terminal is seen when the permission is in force, and again when a terminal opens or
 * closes, or the keeper's list or permission changes. `test` is `context.extensionMode === vscode.ExtensionMode.Test` and nothing else.
 */
export function bindTerminalAccounts(context: vscode.ExtensionContext, sessions: RemoteSessions, test: boolean): TerminalAccounts {
  const accounts = new AccountsWindow(context, sessions, test);
  context.subscriptions.push(
    accounts,
    registerCommand('coai.terminalAccounts', () => { guarded(openTab(accounts, sessions)); }),
    vscode.window.onDidOpenTerminal(() => { void accounts.rearm(); }),
    vscode.window.onDidCloseTerminal(accounts.closed),
    sessions.onChanged(() => { void accounts.changedList(); guarded(accounts.redraw()); }),
    accounts.onLanded((host) => { guarded(accounts.afterLanded(host)); }),
  );
  void accounts.changedList();

  return accounts;
}

// ───────────────── the one notice ─────────────────

const TAB_FAILED_LINE = 'ConnectOtherAIs: the accounts tab could not finish a press';

/** How many times the one notice was raised in this window; the test door reads it. */
let noticesRaised = 0;

/** A detached piece of work of the tab or of the notice: a failure is one fixed line and nothing else. */
function guarded(work: Promise<unknown>): void {
  work.catch(() => { defaultRemoteSeams().log(TAB_FAILED_LINE); });
}

/** The one notice (W15): the tab can show the accounts. One button, *Open*, which opens the tab and decides nothing. */
function canBeShownNotice(): void {
  noticesRaised += 1;
  void notifyThen({
    as: 'information',
    class: 'outcome',
    source: 'terminalAccounts',
    code: 'accounts-can-be-shown',
    title: CAN_BE_SHOWN.title,
    action: CAN_BE_SHOWN.action,
  }, (button) => {
    answerCanBeShown(button, () => { void vscode.commands.executeCommand('coai.terminalAccounts'); });
  });
}

// ───────────────── the tab ─────────────────

/**
 * The icon's press: ONE tab (a second press reveals it), opened with the model of now. Without a permission, and only the first time (W14), the
 * ONE question is asked by this press and by nothing else; the tab is already open behind it. The round is nudged as the old list nudged it.
 */
async function openTab(accounts: AccountsWindow, sessions: RemoteSessions): Promise<void> {
  await accounts.openTab();
  guarded(accounts.openStarts());
  await sessions.askPermissionFirst();
  await accounts.redraw();
}
