import { readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import * as vscode from 'vscode';
import { byCodeUnit } from './codeUnitOrder';
import { thisMachine } from './chatStoreLock';
import type { ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { notify, notifyAndAsk, notifyThen } from './notify';
import { askPerson } from './personWait';
import { thisSideLabel } from './consultantSides';
import { credsSession } from './credsSession';
import { STATE_KEY, permissionRowOf, stateMerge } from './remoteAccountsRound';
import { Committed, HostButton, commit as commitOne, HostChange, HostStanding, buttonsOf, changeOfButton, choicesOf, listedHosts, readableOf } from './remoteHosts';
import { HOSTS_SAID, UnkeptWhy, notKeptSaid } from './remoteHostsSaid';
import { HostKeeper, failedHostOf, hostKeeper } from './remoteKeeper';
import { Allowed, Permission, PermissionAsked, SessionAnswer, permissionAsker } from './remotePermission';
import { PermissionRow } from './remoteKeeperDoor';
import { PickRow, Picked, Purpose, RemoteAnswer, RoadSeams, onePerTerminal, reachOf, readsKey, remoteRoad } from './remoteRead';
import { refusalsDiskOver } from './remoteRefusalsDisk';
import { refusalsStore } from './remoteRefusalsStore';
import { SETTLE, foundQuiet, putBackUntilHeld } from './storeSettle';
import { runRemote } from './remoteSsh';
import { liveTerminals } from './terminalHost';
import { registerCommand } from './shellBridge';

/**
 * The `vscode` seams of the road to a terminal's remote Claude session (E2.S3, contract C4). Host code: every decision is
 * `remoteRead.ts`'s (pure, tested by its own unit tests); this file only supplies the editor — the modal question, the
 * progress line, the pick list, the answers in `globalState` — and holds the two per-terminal maps.
 *
 * <p>Nothing else in the extension spawns ssh or reads the consent. No `console` call here. The three messages this file raises
 * (the one permission question, the change question for a host, and the short messages of the hosts list) go through the one door, `notifyAndAsk`
 * and `notify`, with fixed text and a host name; every sentence of the road itself reaches the door that asked as a value, and that door says it.</p>
 *
 * <p>Unit tests cannot load this module (it imports `vscode`); `terminalHostsScenario.ts` runs it in a real editor, in CI.</p>
 */

/** The `globalState` key of the answers: at most 100 rows, one per host. */
export const TERMINAL_HOSTS_KEY = 'coai.terminalHosts';

const ALLOW_READING = 'Allow reading';
const NOT_NOW = 'Not now';
const SAVE_NOW = 'Save now';

/** The buttons of the change question, by what each does (§ P10). */
const BUTTON_LABEL: Readonly<Record<HostButton, string>> = { 'not-this-host': 'Not this host', 'read-again': 'Read this host again', 'forget': 'Forget' };

/** The file of refusals, the prefix of a marker and the prefix of a withdrawal file, inside the folder the extension hands in: the ONE place that names them. */
const REFUSED_FILE = 'refused.json';
const MARKER_PREFIX = 'unsure-';
const WITHDRAWN_PREFIX = 'withdrawn-';

/**
 * The ONE question (W14), through the one door every message goes through: raised ONLY by the permission's asker, never by a timer, the keeper or
 * the round. Escape and *Not now* are `false`, and write nothing.
 */
const raisePermission = async (): Promise<boolean> =>
  (await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'remoteSessions',
    code: 'read-accounts-permission',
    modal: true,
    title: HOSTS_SAID.permissionTitle,
    detail: HOSTS_SAID.permissionDetail(sideLabel()),
    actions: [ALLOW_READING, NOT_NOW],
  })) === ALLOW_READING;

interface Choice extends vscode.QuickPickItem {
  readonly sessionId: string;
}

const choiceOf = (row: PickRow): Choice => ({ label: row.label, description: row.description, detail: row.detail, sessionId: row.sessionId });

/** The pick list: one row for each candidate, in the order the road decided (newest first), nothing preselected; it says so when not every session was read. */
async function pickOne(rows: readonly PickRow[], partial: boolean): Promise<string | undefined> {
  const chosen = await askPerson(() => vscode.window.showQuickPick(rows.map(choiceOf), {
    canPickMany: false,
    title: 'Which Claude session is this terminal’s?',
    placeHolder: partial ? 'Not every session on the host was read, so the right one may be missing. Choose the session to read' : 'Choose the session to read',
    matchOnDescription: true,
    matchOnDetail: true,
  }));

  return chosen?.sessionId;
}

/** The progress line around one ssh call. */
const underProgress = <T>(host: string, work: () => PromiseLike<T>): PromiseLike<T> =>
  vscode.window.withProgress({ location: vscode.ProgressLocation.Window, title: `Reading the Claude session on “${host}”…` }, () => Promise.resolve(work()));

/** The terminal's LIVE name, or nothing when it is no longer in the window or its process has ended. */
const liveNameOf = (terminal: vscode.Terminal): string | undefined =>
  (vscode.window.terminals.includes(terminal) && terminal.exitStatus === undefined ? terminal.name : undefined);

/** The side of this machine this window runs on, as the MCP server tab names it. */
const sideLabel = (): string => thisSideLabel(vscode.env.remoteName, process.env['WSL_DISTRO_NAME'] ?? '', process.platform);

/** What the rest of the extension asks of the road. */
export interface RemoteSessions {
  /** One read of the terminal's remote session: one at a time for a terminal, the LIVE `terminal.name`, the picked id of that terminal. */
  readonly read: (terminal: vscode.Terminal, purpose: Purpose) => Promise<RemoteAnswer>;
  /** The terminal is closed: its picked id goes. */
  readonly forget: (terminal: vscode.Terminal) => void;
  /** The picked id this terminal keeps, if any. */
  readonly pickedOf: (terminal: vscode.Terminal) => Picked | undefined;
  /** The keeper's commit of ONE host's change — the only write of the list. */
  readonly commitHost: HostKeeper['commit'];
  readonly commitKey: HostKeeper['commitKey'];
  /** The ONE door (E2.S1): asked by the round before every call and by the session road before EVERY spawn. It never asks a person. */
  readonly may: HostKeeper['may'];
  readonly hostStandings: HostKeeper['standings'];
  readonly stopRound: HostKeeper['stopRound'];
  readonly startRound: HostKeeper['startRound'];
  readonly roundStop: HostKeeper['roundStop'];
  /** A PERSON'S PRESS ONLY: saves what the file did not take. Called from the action of the notice and from a press on a row of the list. */
  readonly saveWaiting: HostKeeper['saveWaiting'];
  /** The choices for one host (each only where it does something); over a damaged list or file the answer is a `mend`. */
  readonly changeHost: (host: string) => Promise<void>;
  /** The ONE question (W14). No modal when the permission is in force; *Not now* writes nothing. Called by a person's press only. */
  readonly askPermission: () => Promise<PermissionAsked>;
  /** The icon's press: asks only when the state row holds no `askedUtc` and the permission is not in force, and notes it BEFORE the modal. */
  readonly askPermissionFirst: () => Promise<PermissionAsked | 'asked-before'>;
  /** A person's *Withdraw*: narrows at once. */
  readonly withdraw: HostKeeper['withdraw'];
  /** The permission as the tab and the hosts list show it, from a fresh read of the disk. */
  readonly permission: HostKeeper['permission'];
  /** Fires after every commit that wrote (the keeper's change signal): the accounts round learns of a refusal or a Forget from it. */
  readonly onChanged: vscode.Event<void>;
  /** *CoAI: hosts read over ssh*: the permission's row first, then the hosts. */
  readonly changeHosts: () => Promise<void>;
  /** The keeper, for the test door only. */
  readonly keeper: HostKeeper;
}

interface HostChoice extends vscode.QuickPickItem {
  /** The host this row changes; the first row has none. */
  readonly host?: string;
  /** The first row: the permission. */
  readonly permission?: 'none' | 'waiting' | 'damaged' | 'given';
}

/** How the host stands, as a row of the hosts list (W4, W4b, W4c). */
function hostChoice(host: string, standing: HostStanding | undefined, state: { readonly permitted: boolean; readonly damaged: boolean }): HostChoice {
  if (standing === undefined) {
    return { label: host, description: state.damaged ? HOSTS_SAID.noAnswerReadable : HOSTS_SAID.rowDescription(false, state.permitted), host };
  }

  return { label: host, description: HOSTS_SAID.rowDescription(standing.answer === 'no', state.permitted), detail: HOSTS_SAID.rowDetail(standing), host };
}

/** One short message about the list of hosts: an answer that could not be kept, or the permission withdrawn. */
const tell = (title: string): Promise<void> => notify({
  as: 'information',
  class: 'outcome',
  source: 'remoteSessions',
  code: 'no-remote-host-asked',
  title,
});

/** What the person chose, whatever the kind of change that carried it. */
const chosenOf = (change: HostChange): 'no' | 'forget' => (change.kind === 'forget' ? 'forget' : change.kind === 'mend' ? change.answer : 'no');

/** What is wrong with the list or the file, in W7's and W7b's words; nothing when both can be read. */
function damageSaid(list: ReturnType<typeof readableOf>, fileDamaged: boolean, standings: readonly HostStanding[]): readonly string[] {
  return [
    list.damaged ? HOSTS_SAID.damagedList(list.rows.length, list.rows.filter((row) => row.answer === 'no').length, list.unreadable) : '',
    fileDamaged ? HOSTS_SAID.damagedFile(sideLabel(), standings.filter((one) => one.answer === 'no').length) : '',
  ];
}

/** The change question's detail: how the host stands, over damage what is wrong, then the words that tell what each offered button does. */
async function changeDetail(keeper: HostKeeper, host: string): Promise<{ readonly detail: string; readonly damaged: boolean; readonly buttons: readonly HostButton[] }> {
  const list = readableOf(keeper.stored());
  const fileDamaged = (await keeper.roundStop()) === 'damaged';
  const standings = await keeper.standings();
  const standing = standings.find((one) => one.host === host);
  const damaged = list.damaged || fileDamaged;
  const buttons = buttonsOf(standing, standing?.answer === 'no', damaged);
  const stands = { standing, permitted: typeof (await keeper.permission()) === 'object' };
  const parts = [...damageSaid(list, fileDamaged, standings), HOSTS_SAID.changeDetail(stands, buttons, sideLabel())];

  return { detail: parts.filter((part) => part.length > 0).join(' '), damaged, buttons };
}

const askChange = (host: string, detail: string, buttons: readonly HostButton[]): Promise<string | undefined> => notifyAndAsk({
  as: 'warning',
  class: 'confirmation',
  source: 'remoteSessions',
  code: 'change-remote-host-answer',
  subject: host,
  modal: true,
  title: HOSTS_SAID.title(host),
  detail,
  actions: buttons.map((one) => BUTTON_LABEL[one]),
});

/** The change a button means: over damage a `mend`, else a plain change. `undefined` for Escape. */
function changeFor(label: string | undefined, host: string, damaged: boolean): HostChange | undefined {
  const button = (Object.keys(BUTTON_LABEL) as HostButton[]).find((one) => BUTTON_LABEL[one] === label);

  return button === undefined ? undefined : changeOfButton(button, host, damaged);
}

/** Commits a person's answer for a host and tells the person when the store did not keep it. */
async function committedAndTold(keeper: HostKeeper, host: string, change: HostChange): Promise<void> {
  const said = notKeptSaid(host, chosenOf(change), await keeper.commit(change));
  if (said !== undefined) {
    await tell(said);
  }
}

/** The change question for one host, and the chosen change committed. A change the store did not keep is told. */
async function changeHostOf(keeper: HostKeeper, host: string): Promise<void> {
  const { detail, damaged, buttons } = await changeDetail(keeper, host);
  const change = changeFor(await askChange(host, detail, buttons), host, damaged);
  if (change !== undefined) {
    await committedAndTold(keeper, host, change);
  }
}

/** The row or host the person chose from the list, or nothing (Escape). */
async function chosenRow(rows: readonly HostChoice[]): Promise<HostChoice | undefined> {
  return askPerson(() => vscode.window.showQuickPick(rows, { title: 'Hosts read over ssh', placeHolder: 'Choose the permission or a host' }));
}

const liveHostsOf = (): readonly string[] =>
  liveTerminals(vscode.window.terminals).flatMap((one) => { const name = credsSession(one.name); return name.kind === 'session' ? [name.host] : []; });

/** The permission as the first row shows it: damage first (a list or a file that cannot be read), then what waits, then what is given. */
async function permissionState(keeper: HostKeeper): Promise<'none' | 'waiting' | 'damaged' | { readonly given: string }> {
  if (readableOf(keeper.stored()).damaged || (await keeper.roundStop()) === 'damaged') {
    return 'damaged';
  }
  const now: Permission = await keeper.permission();

  return now;
}

/** The first row of the list: the permission, in its four states (W18). */
function permissionChoice(state: Awaited<ReturnType<typeof permissionState>>): HostChoice {
  const said = HOSTS_SAID.permissionRow(state);

  return { label: said.label, description: said.description, permission: typeof state === 'string' ? state : 'given' };
}

/** What a press on the permission's row needs: the keeper, the question's asker and whether a notice was raised by it. */
interface Press {
  readonly keeper: HostKeeper;
  readonly asker: ReturnType<typeof permissionAsker>;
  readonly raised: { notice: boolean };
}

/** *Allow reading…*: the one question; an answer that was not kept is told here when no notice told it. */
async function pressedAsk(press: Press): Promise<void> {
  press.raised.notice = false;
  const asked = await press.asker.ask();
  if (typeof asked === 'object' && !press.raised.notice) {
    await tell(asked.said);
  }
}

/** *Withdraw the permission*: at once; W17 when it landed (a withdrawal that did not has its own notice). */
async function pressedWithdraw(press: Press): Promise<void> {
  if ((await press.keeper.withdraw()) === 'withdrawn') {
    await tell(HOSTS_SAID.withdrew);
  }
}

/** What a press on the permission's row does, by its state; over damage it is information only. */
const PRESSED: Readonly<Record<NonNullable<HostChoice['permission']>, (press: Press) => Promise<void>>> = {
  none: pressedAsk,
  given: pressedWithdraw,
  waiting: async (press) => { afterPress(press.keeper, await press.keeper.saveWaiting()); },
  damaged: async () => undefined,
};

/** The hosts' rows of the list: refused ∪ live ∪ stored, in code-unit order, each saying how it stands. */
async function hostRows(keeper: HostKeeper, state: Awaited<ReturnType<typeof permissionState>>): Promise<readonly HostChoice[]> {
  const standings = await keeper.standings();
  const listed = choicesOf(listedHosts(keeper.stored(), liveHostsOf()), standings);

  return listed.map((one) => hostChoice(one.host, one.standing, { permitted: typeof state === 'object', damaged: state === 'damaged' }));
}

/** The panel's list: the permission's row first, then the hosts; choosing a host asks again and commits the answer; Escape changes nothing. */
async function changeHostsOf(keeper: HostKeeper, asker: ReturnType<typeof permissionAsker>, raised: { notice: boolean }): Promise<void> {
  const state = await permissionState(keeper);
  const chosen = await chosenRow([permissionChoice(state), ...(await hostRows(keeper, state))]);
  if (chosen !== undefined) {
    await pressedRow(chosen, { keeper, asker, raised });
  }
}

/** The permission's row does what its state says; a host's row asks the change question for that host. */
async function pressedRow(chosen: HostChoice, press: Press): Promise<void> {
  if (chosen.permission !== undefined) {
    await PRESSED[chosen.permission](press);
  } else if (chosen.host !== undefined) {
    await changeHostOf(press.keeper, chosen.host);
  }
}

/**
 * What a press of *Save now* that came to `full` or `damaged` says, in W6's words. An `unwritten` press is told by the keeper, as W9, with its new
 * reason. `host` is the host of the item that failed (`failedHostOf`): a Stop has none, and W6's "full" sentence names a host.
 */
const pressSaid = (host: string | undefined, result: Committed): string | undefined =>
  (result.kind === 'full' && host === undefined ? undefined : notKeptSaid(host ?? '', 'no', result));

/** W9: a refusal, a Stop or a withdrawal the file did not take. One call site, ONE action: *Save now*; `after` is given what the press came to. */
function raiseUnkept(keeper: HostKeeper, subject: string, title: string, cure: string, after: (result: Committed) => void): void {
  void notifyThen({
    as: 'warning',
    class: 'outcome',
    source: 'remoteSessions',
    code: 'remote-host-refusal-not-kept',
    subject,
    title,
    cure,
    action: SAVE_NOW,
  }, (button) => {
    if (button === SAVE_NOW) {
      void keeper.saveWaiting().then((result) => after(result));
    }
  });
}

/** W16 for an *Allow* the store did not keep; for `unwritten` also the notice with *Save now*, which tells the person what waits and takes a lock over that hangs. */
function allowNotKept(keeper: HostKeeper, raised: { notice: boolean }, result: Allowed): string {
  const said = HOSTS_SAID.allowNotKept(result);
  if (result.kind === 'unwritten') {
    raised.notice = true;
    void notifyThen({
      as: 'warning',
      class: 'outcome',
      source: 'remoteSessions',
      code: 'accounts-permission-not-kept',
      title: said,
      action: SAVE_NOW,
    }, (button) => {
      if (button === SAVE_NOW) {
        void keeper.saveWaiting().then((done) => afterPress(keeper, done));
      }
    });
  }

  return said;
}

/** The change event of a road that has no keeper to listen to: nobody is ever told. */
const noChange: vscode.Event<void> = () => ({ dispose: () => undefined });

/** The modal of the permission's question, until the test door replaces it (a modal has nobody to answer it in a real editor under test). */
let boundQuestion: (() => Promise<boolean>) | undefined;

/** The permission's one decision (`permissionAsker`) over this window's keeper and state row. Nothing but a person's press reaches it. */
function askerOf(keeper: HostKeeper, state: vscode.Memento, raised: { notice: boolean }): ReturnType<typeof permissionAsker> {
  return permissionAsker({
    raise: () => (boundQuestion ?? raisePermission)(),
    permission: () => keeper.permission(),
    allow: () => keeper.allow(),
    askedBefore: () => permissionRowOf(state.get(STATE_KEY)).askedUtc !== undefined,
    noteAsked: () => keeper.commitKey(STATE_KEY, { asked: new Date().toISOString() }, stateMerge).then(() => undefined),
    notKept: (result) => allowNotKept(keeper, raised, result),
  });
}

/**
 * The road over a keeper and a run seam. `run` is `runRemote` unless a test says otherwise. The per-terminal state — the picked id
 * and the latch — lives here, in `WeakMap`s keyed by the terminal object, so it goes with it.
 */
export function remoteSessionsOf(keeper: HostKeeper, state: vscode.Memento, run: RoadSeams['run'] = runRemote, onChanged: vscode.Event<void> = noChange): RemoteSessions {
  const picked = new WeakMap<vscode.Terminal, Picked>();
  const latch = onePerTerminal<vscode.Terminal>();
  const raised = { notice: false };
  const asker = askerOf(keeper, state, raised);
  const seamsOf = (terminal: vscode.Terminal): RoadSeams => ({
    store: { may: (host) => keeper.may('session', host) as Promise<SessionAnswer> },
    askPermission: () => asker.ask(),
    liveName: () => liveNameOf(terminal),
    run,
    pick: pickOne,
    progress: underProgress,
    keep: (next) => {
      if (next === undefined) {
        picked.delete(terminal);
      } else {
        picked.set(terminal, next);
      }
    },
  });

  return {
    read: (terminal, purpose) => latch(terminal, () => remoteRoad(seamsOf(terminal), terminal.name, purpose, picked.get(terminal))),
    forget: (terminal) => { picked.delete(terminal); },
    pickedOf(terminal) {
      return picked.get(terminal);
    },
    commitHost: (change) => keeper.commit(change),
    commitKey: keeper.commitKey,
    may: (who, host) => keeper.may(who, host),
    hostStandings: () => keeper.standings(),
    stopRound: () => keeper.stopRound(),
    startRound: () => keeper.startRound(),
    roundStop: () => keeper.roundStop(),
    saveWaiting: () => keeper.saveWaiting(),
    changeHost: (host) => changeHostOf(keeper, host),
    askPermission: () => asker.ask(),
    askPermissionFirst: () => asker.askFirst(),
    withdraw: () => keeper.withdraw(),
    permission: () => keeper.permission(),
    onChanged,
    changeHosts: () => changeHostsOf(keeper, asker, raised),
    keeper,
  };
}

let bound: RemoteSessions | undefined;

/** The ssh call of the bound road: `runRemote`, until the test door replaces it. */
let boundRun: RoadSeams['run'] = runRemote;

/** The context key the terminal panel's two overflow items follow: the active terminal reads a session and its host is not answered "no" (`readsKey`). */
const READS_KEY = 'coai.terminalReadsSession';

/** The change signal of the bound road, for the test door's `restore` alone: nothing else calls it. */
let signalChange: (() => void) | undefined;

/** The value last given to the key: a context key cannot be read back from the host, so a host scenario reads it here (test door only). */
let lastKey: boolean | undefined;

async function setReadsKey(sessions: RemoteSessions): Promise<void> {
  lastKey = readsKey(vscode.window.activeTerminal?.name ?? '', await sessions.hostStandings());
  void vscode.commands.executeCommand('setContext', READS_KEY, lastKey);
}

/** After a press of *Save now*: W9's torn cure when a note that could not be read was removed, and W6's words when the file or the list is not whole. */
function afterPress(live: HostKeeper, result: Committed): void {
  if (live.savedTorn()) {
    raiseUnkept(live, 'accounts-round', HOSTS_SAID.unkeptStop('elsewhere', sideLabel()), HOSTS_SAID.cureTorn, () => undefined);
  }
  const said = pressSaid(failedHostOf(result), result);
  if (said !== undefined) {
    void tell(said);
  }
}

/** The state row's two reads of the permission and its one write, over the profile's `globalState`: the keeper imports nothing of the round. */
const rowOf = (state: vscode.Memento): PermissionRow => ({
  read: () => {
    const row = permissionRowOf(state.get(STATE_KEY));

    return { withdrawn: row.withdrawn, stopped: row.stopped };
  },
  change: (change) => commitOne(state, STATE_KEY, change, stateMerge).then(() => undefined, () => undefined),
});

/** The keeper of this window over the profile's state and the folder of this side's refusals file. */
function keeperOf(context: vscode.ExtensionContext, folder: string, changed: () => void): HostKeeper {
  const store = refusalsStore(refusalsDiskOver(folder, { file: REFUSED_FILE, markerPrefix: MARKER_PREFIX, withdrawnPrefix: WITHDRAWN_PREFIX }, thisMachine), Date.now);
  const keeperRef: { current?: HostKeeper } = {};
  const keeper = hostKeeper({
    state: context.globalState,
    key: TERMINAL_HOSTS_KEY,
    refusals: store,
    now: () => new Date().toISOString(),
    row: rowOf(context.globalState),
    changed,
    unkept: (host, why, marked) => {
      const live = keeperRef.current as HostKeeper;
      const title = host === undefined ? HOSTS_SAID.unkeptStop(why, sideLabel(), marked) : HOSTS_SAID.unkeptHost(host, why as UnkeptWhy, sideLabel(), marked);
      raiseUnkept(live, host ?? 'accounts-round', title, marked ? HOSTS_SAID.cureMarked : HOSTS_SAID.cureUnmarked, (result) => afterPress(live, result));
    },
    unkeptWithdrawal: (why) => {
      const live = keeperRef.current as HostKeeper;
      raiseUnkept(live, 'permission', HOSTS_SAID.unkeptWithdrawal(why), why === 'busy' ? HOSTS_SAID.cureWithdrawalBusy : HOSTS_SAID.cureWithdrawal, (result) => afterPress(live, result));
    },
  });
  keeperRef.current = keeper;

  return keeper;
}

/**
 * Activation: the one road of this window, its terminal listener, the context key of the terminal items and the panel's command.
 * `folder` is where this side keeps the refusals file (`globalStorageUri/terminal-hosts`): no other module names it.
 */
export function bindRemoteSessions(context: vscode.ExtensionContext, folder: string): RemoteSessions {
  // The key is set again whenever a change was written — by the first read of a host, or from the panel's list.
  const changes = new vscode.EventEmitter<void>();
  const sessions = remoteSessionsOf(keeperOf(context, folder, () => { void setReadsKey(sessions); changes.fire(); }), context.globalState, (host, session, operands, script) => boundRun(host, session, operands, script), changes.event);
  bound = sessions;
  signalChange = () => { changes.fire(); };
  void setReadsKey(sessions);
  context.subscriptions.push(
    changes,
    vscode.window.onDidChangeActiveTerminal(() => { void setReadsKey(sessions); }),
    vscode.window.onDidCloseTerminal((closed) => { sessions.forget(closed); }),
    registerCommand('coai.terminalHosts', () => sessions.changeHosts()),
  );

  return sessions;
}

/**
 * What a host scenario may reach of the BOUND road (S3-H1). The extension hands it out only when it runs in test mode
 * (`ExtensionMode.Test`): in production the field is absent, so no other extension can write a consent or replace the ssh call.
 */
export interface RemoteSessionsTestDoor {
  /** Replace the bound road's ssh call; `undefined` puts `runRemote` back. */
  readonly replaceRun: (run: RoadSeams['run'] | undefined) => void;
  /** TEST ONLY. Replace the modal of the permission's question by an answer (`true`: *Allow reading*; `false`: *Not now*), no dialog is shown; `undefined` puts the modal back. */
  readonly replaceQuestion: (raise: (() => Promise<boolean>) | undefined) => void;
  readonly read: RemoteSessions['read'];
  readonly pickedOf: RemoteSessions['pickedOf'];
  /** What the context key `coai.terminalReadsSession` was last set to, or nothing before the first set. */
  readonly readsKey: () => boolean | undefined;
  /** The product's own commit: a scenario gives a host its answer the way a press does. */
  readonly commit: RemoteSessions['commitHost'];
  /** The one door. */
  readonly may: RemoteSessions['may'];
  /** The keeper's own *Allow* and *Withdraw*: a scenario gives and withdraws the way a press does, with no modal. */
  readonly allow: HostKeeper['allow'];
  readonly withdraw: RemoteSessions['withdraw'];
  readonly permission: RemoteSessions['permission'];
  /** The names of this side's withdrawal files, read now. */
  readonly withdrawnFiles: () => Promise<readonly string[]>;
  readonly standings: RemoteSessions['hostStandings'];
  /** What is stored, as it is, for the `finally`: the list, the state row, the TEXT of the file, and every withdrawal file with its text. */
  readonly stored: () => Promise<StoredBefore>;
  /**
   * What is stored as it is FOUND: `stored`, read again a step later until two reads agree. A scenario takes its `before` from this, so what an earlier
   * scenario's last writes are still doing in the editor is not taken for the profile it must put back (`storeSettle.ts`).
   */
  readonly found: () => Promise<StoredBefore>;
  /**
   * TEST ONLY. Puts all four back by raw writes, removes every other withdrawal file, every marker and the lock, empties what this window holds as
   * not saved and what it remembers of the file, and sends the change signal a commit sends. Then it reads, and puts back again whatever differs,
   * until the profile has stood as found for a quiet time: the editor can take a `globalState` write away with the late news of an older one.
   */
  readonly restore: (before: StoredBefore) => Promise<void>;
  /** The Stop, through the product's own road. */
  readonly stopRound: RemoteSessions['stopRound'];
  readonly startRound: RemoteSessions['startRound'];
  readonly roundStop: RemoteSessions['roundStop'];
}

export interface StoredBefore {
  readonly list: unknown;
  readonly row: unknown;
  readonly file: string | undefined;
  readonly withdrawn: readonly { readonly name: string; readonly text: string }[];
}

/** The file's text; `undefined` only when there is no file. Any other error is thrown, so a scenario does not take a file it could not read for a missing one. */
const textOrNothing = (path: string): Promise<string | undefined> =>
  readFile(path, 'utf8').catch((error: unknown) => {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') {
      return undefined;
    }
    throw error;
  });

const namesOf = async (folder: string, prefix: string): Promise<readonly string[]> =>
  (await readdir(folder).catch(() => [] as string[])).filter((one) => one.startsWith(prefix)).sort(byCodeUnit);

/** The withdrawal files of the folder with their text: raw reads, for the `finally` of a scenario only. */
async function withdrawnOf(folder: string): Promise<StoredBefore['withdrawn']> {
  const names = await namesOf(folder, WITHDRAWN_PREFIX);

  return Promise.all(names.map(async (name) => ({ name, text: (await textOrNothing(join(folder, name))) ?? '' })));
}

async function restoreFiles(folder: string, before: StoredBefore): Promise<void> {
  if (before.file === undefined) {
    await rm(join(folder, REFUSED_FILE), { force: true });
  } else {
    await writeFile(join(folder, REFUSED_FILE), before.file, 'utf8');
  }
  for (const name of (await readdir(folder).catch(() => [] as string[])).filter((one) => one.startsWith(MARKER_PREFIX) || one.startsWith(WITHDRAWN_PREFIX) || one.endsWith('.lock'))) {
    await rm(join(folder, name), { force: true });
  }
  for (const one of before.withdrawn) {
    await writeFile(join(folder, one.name), one.text, 'utf8');
  }
}

/** What a scenario put back, as text, so two reads compare: the list, the row, the file's text and the withdrawal files. */
const sampleText = (stored: StoredBefore): string => JSON.stringify(stored);

const pauseFor = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

/** The raw writes of a restore, in the order the wiring test holds: the list, the row, then the folder. */
async function putBack(state: vscode.Memento, folder: string, before: StoredBefore): Promise<void> {
  await state.update(TERMINAL_HOSTS_KEY, before.list);
  await state.update(STATE_KEY, before.row);
  await restoreFiles(folder, before);
}

/** What is stored, as it is: the list, the state row, the TEXT of the file, and every withdrawal file with its text. */
const storedOf = async (state: vscode.Memento, folder: string): Promise<StoredBefore> =>
  ({ list: state.get(TERMINAL_HOSTS_KEY), row: state.get(STATE_KEY), file: await textOrNothing(join(folder, REFUSED_FILE)), withdrawn: await withdrawnOf(folder) });

export const testDoorOf = (sessions: RemoteSessions, state: vscode.Memento, folder: string): RemoteSessionsTestDoor => ({
  replaceRun: (run) => { boundRun = run ?? runRemote; },
  replaceQuestion: (raise) => { boundQuestion = raise; },
  read: sessions.read,
  pickedOf: sessions.pickedOf,
  readsKey: () => lastKey,
  commit: sessions.commitHost,
  may: sessions.may,
  allow: () => sessions.keeper.allow(),
  withdraw: sessions.withdraw,
  permission: sessions.permission,
  withdrawnFiles: () => namesOf(folder, WITHDRAWN_PREFIX),
  standings: sessions.hostStandings,
  stored: () => storedOf(state, folder),
  found: () => foundQuiet({ read: () => storedOf(state, folder), same: (a, b) => sampleText(a) === sampleText(b), pause: pauseFor, now: Date.now }),
  restore: async (before) => {
    // What the keeper has queued is let finish first, so no operation of this window runs in the middle of the raw writes below.
    await sessions.keeper.held();
    await putBack(state, folder, before);
    sessions.keeper.forgetHeld();
    void setReadsKey(sessions);
    // The raw writes above sent no change signal: tell the listeners (the accounts window's known hosts and generation) what the store now holds, as a commit does.
    signalChange?.();
    // The editor may take a write away with the late news of an older one, and the keeper may put a refusal back from what it read before: read, and put back, until it stands.
    await putBackUntilHeld(sampleText(before), {
      read: async () => sampleText(await storedOf(state, folder)),
      putBack: async () => {
        await sessions.keeper.held();
        await putBack(state, folder, before);
        sessions.keeper.forgetHeld();
      },
      pause: pauseFor,
      now: Date.now,
    }, SETTLE);
  },
  stopRound: sessions.stopRound,
  startRound: sessions.startRound,
  roundStop: sessions.roundStop,
});

/** The ONE entry the doors and *Asked* call: read this terminal's remote Claude session for this purpose. */
export function readRemoteSession(terminal: vscode.Terminal, purpose: Purpose): Promise<RemoteAnswer> {
  if (bound === undefined) {
    throw new Error('The remote session road is not bound: bindRemoteSessions has not run.');
  }

  return bound.read(terminal, purpose);
}

/** The terminal a conversation reads, or the sentence that says why there is none. */
export type ReachedTerminal =
  | { readonly kind: 'terminal'; readonly terminal: vscode.Terminal }
  | { readonly kind: 'refused'; readonly refusal: string };

const recordedNameOf = (id: object): string => {
  const source = threads.get(id)?.source;

  return source?.kind === 'terminal' ? source.name : '';
};

/** The window's terminal that is this key. The key came from the window's own list a moment ago; if it is gone, the answer says so. */
function terminalFor(key: object): ReachedTerminal {
  const terminal = vscode.window.terminals.find((one) => one === key);

  return terminal === undefined ? { kind: 'refused', refusal: 'The terminal is no longer open.' } : { kind: 'terminal', terminal };
}

/**
 * The terminal of a conversation (for *Asked*): the live terminal it is keyed by, else the ONE live terminal with the RECORDED
 * name — decided by `reachOf` over the live terminals; this function only reads `window.terminals`. `id` is the conversation's
 * entry id (the key of `threads`), and its source must be a terminal's.
 */
export function terminalOf(id: object, panels: Pick<ChatPanels, 'keyOf'>): ReachedTerminal {
  const reach = reachOf(liveTerminals(vscode.window.terminals), panels.keyOf(id), recordedNameOf(id));

  return reach.kind === 'refused' ? reach : terminalFor(reach.key);
}
