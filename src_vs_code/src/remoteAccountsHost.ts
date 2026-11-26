import * as vscode from 'vscode';
import { byCodeUnit } from './codeUnitOrder';
import { textCopier } from './copyText';
import { notifyThen } from './notify';
import { AccountsDriver, CAN_BE_SHOWN, DriverWorld, RoundSummary, answerCanBeShown } from './remoteAccountsDriver';
import { readAccounts } from './remoteAccountsReply';
import { AccountsCall, Memory, RoundResult, STATE_KEY, Trigger, accountsHosts, callFor, logGate } from './remoteAccountsRound';
import { AccountsView, ChooseSeams, Row, accountsRows, choose, copyDecisionFor } from './remoteAccountsRows';
import type { RemoteSessions } from './remoteSessionHost';
import { ACCOUNTS_LIMITS, defaultRemoteSeams, runAccounts } from './remoteSsh';
import { registerCommand } from './shellBridge';
import { liveTerminals } from './terminalHost';

/**
 * The `vscode` seams of the accounts round (E1.S3, contract C4 and C7). Host code: every decision AND every orchestration is
 * `remoteAccountsRound.ts`'s (pure, tested with fake seams and a fake clock); this file holds the window's memory and its one timer, reads
 * the terminals and the Stop, and binds the call.
 *
 * <p>Nothing of a reading is written anywhere: no `globalState`, no file, no log. The state row is written only through the pure
 * `stopTheRound` and `startTheRound` and the one notice's time. No `console` call here: the log goes through the one default seam of `remoteSsh.ts`.</p>
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
  /** What the last round ended with: its end, the places it called and the ones that answered. */
  readonly summary: () => RoundSummary | undefined;
  /** What the list draws now: the round's memory, the standings, the Stop and the two things a press of the list itself carries. */
  readonly view: (extra: ViewExtra) => Promise<AccountsView>;
  /** The list tells the window what it handed to the QuickPick, for the test door. */
  readonly noteShown: (items: readonly ShownItem[]) => void;
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
  /** The labels, descriptions, details and kinds of the items last given to the list. */
  readonly lastItems: () => readonly ShownItem[];
  /** How many times the one notice `accounts-can-be-shown` was raised in this window. */
  readonly noticeShown: () => number;
}

/** One item of the list as it was handed to the QuickPick. */
export interface ShownItem {
  readonly label: string;
  readonly description: string | undefined;
  readonly detail: string | undefined;
  readonly kind: 'separator' | 'item';
}

/** What a press of the list itself carries into a draw. */
export interface ViewExtra {
  readonly floor: boolean;
  readonly startUnwritten: boolean;
}

export const terminalAccountsTestDoorOf = (accounts: TerminalAccounts): TerminalAccountsTestDoor => accounts.door;

/** The terminals of this window, as the round reads them. */
const windowTerminals = (): ReturnType<typeof liveTerminals> => liveTerminals(vscode.window.terminals);

/** The window's one round: the driver (`remoteAccountsDriver.ts`, pure over its seams) bound to `vscode`, the keeper and the clock. */
class AccountsWindow implements TerminalAccounts {
  readonly onLanded: vscode.Event<string | undefined>;
  readonly door: TerminalAccountsTestDoor;
  private readonly landed = new vscode.EventEmitter<string | undefined>();
  private readonly driver: AccountsDriver;
  private replaced: AccountsCall | undefined;
  private shown: readonly ShownItem[] = [];

  constructor(private readonly context: vscode.ExtensionContext, private readonly sessions: RemoteSessions, private readonly test: boolean) {
    this.onLanded = this.landed.event;
    this.driver = new AccountsDriver(this.worldOf());
    this.door = {
      replaceCall: (call) => { this.replaced = call; },
      runRoundNow: (trigger) => this.driver.runNow(trigger),
      armed: () => this.driver.armedNow(),
      memoryHosts: () => [...this.driver.memory().keys()].sort(byCodeUnit),
      lastItems: () => this.shown,
      noticeShown: () => noticesRaised,
    };
  }

  /** The seams of the driver: the ONE call and the ONE parse, the door of the keeper, the clock and the timer. None of them asks a person. */
  private worldOf(): DriverWorld {
    const write = defaultRemoteSeams().log;
    const gate = logGate(write);
    const real: AccountsCall = (host, mode, digest) => runAccounts(host, mode, digest, ACCOUNTS_LIMITS, { ...defaultRemoteSeams(), log: gate.seamFor(host) });

    return {
      test: this.test,
      live: windowTerminals,
      permitted: async () => typeof (await this.sessions.permission()) === 'object',
      may: (host) => this.sessions.may('round', host),
      standings: () => this.sessions.hostStandings(),
      call: async (host, mode, digest) => {
        const run = await callFor(this.test, real, this.replaced)(host, mode, digest);
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

  noteShown(items: readonly ShownItem[]): void {
    this.shown = items;
  }

  async view(extra: ViewExtra): Promise<AccountsView> {
    const hosts = accountsHosts(windowTerminals());

    return {
      live: hosts.length > 0,
      stopped: await this.driver.stoppedNow(),
      busy: this.driver.isRunning(),
      memory: this.driver.memory(),
      standings: await this.sessions.hostStandings(),
      hosts,
      ignored: undefined,
      allowNew: false,
      damagedFile: (await this.sessions.roundStop()) === 'damaged',
      startUnwritten: extra.startUnwritten,
      floor: extra.floor,
    };
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
    registerCommand('coai.terminalAccounts', () => { openList(accounts, sessions); }),
    vscode.window.onDidOpenTerminal(() => { void accounts.rearm(); }),
    vscode.window.onDidCloseTerminal(accounts.closed),
    sessions.onChanged(() => { void accounts.changedList(); }),
  );
  void accounts.changedList();

  return accounts;
}

// ───────────────── the one notice ─────────────────

const LIST_FAILED_LINE = 'ConnectOtherAIs: the accounts list could not finish a press';

/** How many times the one notice was raised in this window; the test door reads it. */
let noticesRaised = 0;

/** A detached piece of work of the list or of the notice: a failure is one fixed line and nothing else. */
function guarded(work: Promise<unknown>): void {
  work.catch(() => { defaultRemoteSeams().log(LIST_FAILED_LINE); });
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

// ───────────────── the list ─────────────────

interface ListItem extends vscode.QuickPickItem {
  readonly row: Row;
}

const BUTTON = {
  refresh: { iconPath: new vscode.ThemeIcon('refresh'), tooltip: 'Refresh now' },
  stop: { iconPath: new vscode.ThemeIcon('debug-stop'), tooltip: 'Stop the accounts round' },
  copy: { iconPath: new vscode.ThemeIcon('copy'), tooltip: 'Copy the command' },
} satisfies Record<string, vscode.QuickInputButton>;

/** How each kind of row is drawn; a host's sentence is the item's second line, where it has room. */
const ITEM_OF: { readonly [K in Row['kind']]: (row: Extract<Row, { kind: K }>) => ListItem } = {
  separator: (row) => ({ row, label: row.label, kind: vscode.QuickPickItemKind.Separator }),
  usage: (row) => ({ row, label: row.label, description: row.description, detail: row.detail }),
  host: (row) => ({ row, label: row.label, detail: row.description, buttons: row.copy === undefined ? [] : [BUTTON.copy] }),
  info: (row) => ({ row, label: row.label }),
};

const itemOf = (row: Row): ListItem => (ITEM_OF[row.kind] as (one: Row) => ListItem)(row);

const shownOf = (item: ListItem): ShownItem =>
  ({ label: item.label, description: item.description, detail: item.detail, kind: item.kind === vscode.QuickPickItemKind.Separator ? 'separator' : 'item' });

/** The doors a press uses: the road's own, bound once. The one call that takes a lock over is here and nowhere else. */
function chooseSeamsOf(sessions: RemoteSessions, accounts: TerminalAccounts): ChooseSeams {
  return {
    change: (host) => sessions.changeHost(host),
    ask: (host) => sessions.changeHost(host),
    save: () => sessions.saveWaiting(),
    start: () => accounts.start(),
    refresh: () => accounts.refresh(),
  };
}

/** The list that is on screen, if any: a second press of the icon replaces it. */
let shownList: AccountsList | undefined;

/**
 * One QuickPick that stays open, redraws when a host's read lands and is disposed when it hides. Everything it shows is `accountsRows`; what it
 * does with a press is `choose`.
 */
class AccountsList {
  private readonly pick = vscode.window.createQuickPick<ListItem>();
  private readonly copier = textCopier({
    writeText: (text) => Promise.resolve(vscode.env.clipboard.writeText(text)),
    say: (message, forMs) => vscode.window.setStatusBarMessage(message, forMs),
  });
  private readonly subscriptions: readonly vscode.Disposable[];
  private floor = false;
  private startUnwritten = false;
  private onScreen = true;
  private drawn = 0;

  constructor(private readonly accounts: TerminalAccounts, private readonly sessions: RemoteSessions) {
    this.pick.title = 'AI accounts and usage';
    this.pick.matchOnDescription = true;
    this.pick.matchOnDetail = true;
    this.pick.keepScrollPosition = true;
    this.pick.ignoreFocusOut = true;
    this.pick.buttons = [BUTTON.refresh, BUTTON.stop];
    this.subscriptions = [
      this.pick.onDidTriggerButton((button) => { guarded(this.titleButton(button)); }),
      this.pick.onDidTriggerItemButton((pressed) => { void this.copier.copy(() => copyDecisionFor(pressed.item.row)); }),
      this.pick.onDidAccept(() => { guarded(this.accept()); }),
      this.pick.onDidHide(() => { this.dispose(); }),
      this.accounts.onLanded(() => { guarded(this.draw()); }),
    ];
  }

  show(): void {
    this.pick.show();
    guarded(this.accounts.openStarts());
    guarded(this.draw());
  }

  hide(): void {
    this.pick.hide();
  }

  /** The items are rebuilt in place, never reopened. A draw that a newer one overtook is dropped. */
  private async draw(): Promise<void> {
    this.drawn += 1;
    const mine = this.drawn;
    const extra = { floor: this.floor, startUnwritten: this.startUnwritten };
    const view = await this.accounts.view(extra);
    if (!this.onScreen || mine !== this.drawn) {
      return;
    }
    this.pick.busy = view.busy;
    this.pick.items = accountsRows(view, Date.now()).map(itemOf);
    this.floor = false;
    this.accounts.noteShown(this.pick.items.map(shownOf));
  }

  private async titleButton(button: vscode.QuickInputButton): Promise<void> {
    if (button === BUTTON.stop) {
      await this.accounts.stop();
    } else {
      this.floor = (await this.accounts.refresh()) === 'floor';
    }
    await this.draw();
  }

  /** Choosing a row: `choose` decides what it means; the list is drawn again whatever came of it. */
  private async accept(): Promise<void> {
    const row = this.pick.selectedItems[0]?.row;
    if (row === undefined) {
      return;
    }
    this.startUnwritten = (await choose(row, chooseSeamsOf(this.sessions, this.accounts))) === 'start-failed';
    await this.draw();
  }

  dispose(): void {
    this.onScreen = false;
    this.subscriptions.forEach((one) => { one.dispose(); });
    this.pick.dispose();
    shownList = shownList === this ? undefined : shownList;
  }
}

/** The command: one list at a time. */
function openList(accounts: TerminalAccounts, sessions: RemoteSessions): void {
  shownList?.hide();
  shownList = new AccountsList(accounts, sessions);
  shownList.show();
}
