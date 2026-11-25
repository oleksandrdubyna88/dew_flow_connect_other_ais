import * as vscode from 'vscode';
import { byCodeUnit } from './codeUnitOrder';
import { textCopier } from './copyText';
import { ALLOW_NEW_HOSTS_KEY } from './modelKeys';
import { notify, notifyThen } from './notify';
import { oneAtATime } from './oneAtATime';
import { readAccounts } from './remoteAccountsReply';
import {
  AccountsCall, Memory, RoundResult, RoundSeams, STATE_KEY, TimerAction, Trigger, accountsHosts, accountsStateOf, armFor, callFor, droppedLine, eligibleOf, isStopped, liveOnly, logGate,
  ROUND_GAP_MS, memoryAfter, nextAfterRound, openStarts, refreshAnswer, refusedHosts, runRound, settleRound, standingsBump, startTheRound, stateMerge, stopTheRound, timerFor, unreadHost, withRefusals,
} from './remoteAccountsRound';
import { AccountsView, ChooseSeams, NoticeSeams, Row, accountsRows, choose, copyDecisionFor, tellAfterRound } from './remoteAccountsRows';
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
  /** What the list draws now: the round's memory, the standings, the Stop and the two things a press of the list itself carries. */
  readonly view: (extra: ViewExtra) => Promise<AccountsView>;
  /** The list tells the window what it handed to the QuickPick, for the test door. */
  readonly noteShown: (items: readonly ShownItem[]) => void;
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
  /** The labels, descriptions, details and kinds of the items last given to the list. */
  readonly lastItems: () => readonly ShownItem[];
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
  /** The generation the running round started at: the news of a host is applied only while it is still this one. */
  private roundGeneration = 0;
  private lastEndMs: number | undefined;
  /** When the round that runs, or ran last, started: the first-round notice names only the "yes" rows older than it. */
  private roundStartedMs = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private armed = false;
  private running = false;
  /** A Start came while a round ran: its refresh runs when that round ends (the door refuses a second round, it does not queue). */
  private startPending = false;
  /** Set by `dispose`: nothing starts again, and the round in flight publishes nothing. */
  private disposed = false;
  /** Bumped on a Stop, on a refusal and on a Forget: a result of a round that started before it is dropped. */
  private generation = 0;
  private replaced: AccountsCall | undefined;
  private shown: readonly ShownItem[] = [];
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
      memoryHosts: () => [...this.memoryNow.keys()].sort(byCodeUnit),
      lastItems: () => this.shown,
    };
  }

  /** A host's news landed (step 8): the round's memory so far is the window's, unless a Stop, a refusal or a Forget came since the round started. */
  private landedFrom(memory: Memory): void {
    if (this.running && this.generation === this.roundGeneration) {
      this.memoryNow = liveOnly(memory, accountsHosts(windowTerminals()));
    }
    this.landed.fire();
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
      landed: (memory) => { this.landedFrom(memory); },
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
    if (this.disposed) {
      return;
    }
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
  async runNow(trigger: Trigger): Promise<RoundResult | undefined> {
    if (this.disposed) {
      return undefined;
    }
    let ran = false;
    const result = await this.latch(async () => {
      ran = true;
      this.running = true;
      this.roundStartedMs = Date.now();
      this.roundGeneration = this.generation;
      this.landed.fire();
      try {
        return await settleRound(
          () => runRound(this.seams, this.memoryNow, this.round, trigger),
          (done) => this.afterRound(done),
          () => { this.apply(timerFor({ kind: 'at', atMs: Date.now() + ROUND_GAP_MS }, this.test, Date.now())); },
          () => { defaultRemoteSeams().log(ROUND_FAILED_LINE); },
        );
      } finally {
        this.running = false;
        this.landed.fire();
      }
    });
    if (ran && this.startPending && !this.disposed) {
      this.startPending = false;
      void this.runNow('refresh');
    }

    return result;
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
    this.noticeAfter(done);
  }

  /** The notices after a round that ended: they never hold the round, and a failure of theirs is one fixed line. */
  private noticeAfter(done: RoundResult | undefined): void {
    if (done !== undefined && !done.abandoned) {
      guarded(tellAfterRound(this.noticeSeams(), true));
    }
  }

  /** What the notices after a round need: the state row's copy that can be read, the standings, the clock and the two toasts. */
  private noticeSeams(): NoticeSeams {
    return {
      state: () => { const read = accountsStateOf(this.context.globalState.get(STATE_KEY)); return read.kind === 'state' ? read.state : undefined; },
      standings: () => this.sessions.hostStandings(),
      nowMs: () => Date.now(),
      roundStartMs: () => this.roundStartedMs,
      allowed: allowedNotice,
      everyTen: everyTenNotice,
      commit: (change) => this.sessions.commitKey(STATE_KEY, change, stateMerge),
    };
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
    this.startPending = false;
    clearTimeout(this.timer);
    this.timer = undefined;
    this.armed = false;
    this.memoryNow = new Map();
    await stopTheRound(this.sessions, new Date().toISOString());
    this.landed.fire();
  }

  async start(): Promise<boolean> {
    const started = await startTheRound(this.sessions);
    if (started && this.running) {
      this.startPending = true;
    } else if (started) {
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

  noteShown(items: readonly ShownItem[]): void {
    this.shown = items;
  }

  async view(extra: ViewExtra): Promise<AccountsView> {
    const hosts = accountsHosts(windowTerminals());

    return {
      live: hosts.length > 0,
      stopped: await this.stoppedNow(),
      busy: this.running,
      memory: this.memoryNow,
      standings: await this.sessions.hostStandings(),
      hosts,
      ignored: this.sessions.ignoredLayer(),
      allowNew: this.sessions.allowsNewHosts(),
      damagedFile: (await this.sessions.roundStop()) === 'damaged',
      startUnwritten: extra.startUnwritten,
      floor: extra.floor,
    };
  }

  dispose(): void {
    this.disposed = true;
    this.generation += 1;
    this.startPending = false;
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
    registerCommand('coai.terminalAccounts', () => { openList(accounts, sessions); }),
    vscode.window.onDidOpenTerminal(() => { void accounts.rearm(); }),
    vscode.window.onDidCloseTerminal(accounts.closed),
    sessions.onChanged(() => { void accounts.changedList(); }),
  );
  void accounts.changedList();

  return accounts;
}

// ───────────────── the notices ─────────────────

const SETTING = `coai.${ALLOW_NEW_HOSTS_KEY}`;
const OPEN_SETTINGS = 'Open settings';
const LIST_FAILED_LINE = 'ConnectOtherAIs: the accounts list could not finish a press';

/** A detached piece of work of the list or of the notices: a failure is one fixed line and nothing else. */
function guarded(work: Promise<unknown>): void {
  work.catch(() => { defaultRemoteSeams().log(LIST_FAILED_LINE); });
}

/** The daily notice: the hosts the round allowed by itself, where to forbid one, the setting and that it is on. One action, which decides nothing. */
function allowedNotice(hosts: readonly string[]): void {
  void notifyThen({
    as: 'information',
    class: 'outcome',
    source: 'terminalAccounts',
    code: 'accounts-hosts-allowed',
    title: `CoAI allowed ${hosts.join(', ')} by itself, and now reads the AI accounts there over ssh.`,
    detail: `To forbid one, choose it in CoAI: hosts read over ssh and pick Not this host. The setting ${SETTING} is on; turn it off to be asked about each new host.`,
    action: OPEN_SETTINGS,
  }, (button) => {
    if (button === OPEN_SETTINGS) {
      void vscode.commands.executeCommand('workbench.action.openSettings', SETTING);
    }
  });
}

/** The first-round notice: the hosts the person allowed before are now read every 10 minutes, and where to stop it. */
function everyTenNotice(hosts: readonly string[]): void {
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'terminalAccounts',
    code: 'accounts-read-every-ten-minutes',
    title: `CoAI now reads the AI accounts of ${hosts.join(', ')} over ssh every 10 minutes, as you allowed before. To stop it, pick Not this host in CoAI: hosts read over ssh, or stop the round in AI accounts and usage.`,
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
    ask: (host) => sessions.askHost(host),
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
