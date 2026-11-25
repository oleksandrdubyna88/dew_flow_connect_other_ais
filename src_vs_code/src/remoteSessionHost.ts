import { readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import * as vscode from 'vscode';
import { thisMachine } from './chatStoreLock';
import type { ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { notify, notifyAndAsk, notifyThen } from './notify';
import { askPerson } from './personWait';
import { thisSideLabel } from './consultantSides';
import { credsSession } from './credsSession';
import { Committed, HostChange, HostStanding, choicesOf, listedHosts, readableOf } from './remoteHosts';
import { HOSTS_SAID, UnkeptWhy, notKeptSaid } from './remoteHostsSaid';
import { HostKeeper, failedHostOf, hostKeeper } from './remoteKeeper';
import { PickRow, Picked, Purpose, RemoteAnswer, RoadSeams, onePerTerminal, reachOf, readsKey, remoteRoad } from './remoteRead';
import { refusalsStore } from './remoteRefusals';
import { refusalsDiskOver } from './remoteRefusalsDisk';
import { allowsNewHosts, ignoredLayerFor } from './sideConfig';
import { ALLOW_NEW_HOSTS_KEY } from './modelKeys';
import { runRemote } from './remoteSsh';
import { liveTerminals } from './terminalHost';
import { registerCommand } from './shellBridge';

/**
 * The `vscode` seams of the road to a terminal's remote Claude session (E2.S3, contract C4). Host code: every decision is
 * `remoteRead.ts`'s (pure, tested by its own unit tests); this file only supplies the editor — the modal question, the
 * progress line, the pick list, the answers in `globalState` — and holds the two per-terminal maps.
 *
 * <p>Nothing else in the extension spawns ssh or reads the consent. No `console` call here. The three messages this file raises
 * (the consent question, the same question from the hosts list, and "no host has been asked yet") go through the one door, `notifyAndAsk`
 * and `notify`, with fixed text and a host name; every sentence of the road itself reaches the door that asked as a value, and that door says it.</p>
 *
 * <p>Unit tests cannot load this module (it imports `vscode`); `terminalHostsScenario.ts` runs it in a real editor, in CI.</p>
 */

/** The `globalState` key of the answers: at most 100 rows, one per host. */
export const TERMINAL_HOSTS_KEY = 'coai.terminalHosts';

const READ = 'Read';
const NOT_THIS_HOST = 'Not this host';
const FORGET = 'Forget';
const SAVE_NOW = 'Save now';

/** The file of refusals and the prefix of a marker, inside the folder the extension hands in: the ONE place that names them. */
const REFUSED_FILE = 'refused.json';
const MARKER_PREFIX = 'unsure-';

const answerOf = (button: string | undefined): 'yes' | 'no' | undefined => (button === READ ? 'yes' : button === NOT_THIS_HOST ? 'no' : undefined);

/**
 * The modal question for a host, through the one door every message goes through (it is recorded, and the wait is the person's, not
 * the panel's). Escape gives `undefined`, and the road writes nothing for it.
 */
const askQuestion = async (host: string): Promise<'yes' | 'no' | undefined> =>
  answerOf(await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'remoteSessions',
    code: 'read-remote-session',
    subject: host,
    modal: true,
    title: HOSTS_SAID.title(host),
    detail: HOSTS_SAID.askedDetail,
    actions: [READ, NOT_THIS_HOST],
  }));

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
  /** The round's door: asked just before EVERY accounts call. It never asks a person. */
  readonly admitHost: HostKeeper['admit'];
  readonly hostStandings: HostKeeper['standings'];
  readonly stopRound: HostKeeper['stopRound'];
  readonly startRound: HostKeeper['startRound'];
  readonly roundStop: HostKeeper['roundStop'];
  /** A PERSON'S PRESS ONLY: saves what the file did not take. Called from the action of the notice and from a press on a row of the list. */
  readonly saveWaiting: HostKeeper['saveWaiting'];
  /** The three choices for one host; over a damaged list or file the answer is a `mend`. */
  readonly changeHost: (host: string) => Promise<void>;
  /** The question for a host that has no answer, committed; over damage it is `changeHost`. */
  readonly askHost: (host: string) => Promise<void>;
  readonly allowsNewHosts: () => boolean;
  readonly ignoredLayer: () => 'workspace' | 'folder' | undefined;
  /** Fires after every commit that wrote (the keeper's change signal): the accounts round learns of a refusal or a Forget from it. */
  readonly onChanged: vscode.Event<void>;
  /** *CoAI: hosts read over ssh*: list the hosts, change one answer or forget it. */
  readonly changeHosts: () => Promise<void>;
  /** The keeper, for the test door only. */
  readonly keeper: HostKeeper;
}

const configNow = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');

interface HostChoice extends vscode.QuickPickItem {
  readonly host: string;
}

const hostChoice = (host: string, standing: HostStanding | undefined, allowNew: boolean): HostChoice =>
  (standing === undefined
    ? { label: host, description: HOSTS_SAID.noAnswerReadable, host }
    : { label: host, description: HOSTS_SAID.rowDescription(standing, allowNew), detail: HOSTS_SAID.rowDetail(standing), host });

/** One short message about the list of hosts: the empty list, or an answer that could not be kept. */
const tell = (title: string): Promise<void> => notify({
  as: 'information',
  class: 'outcome',
  source: 'remoteSessions',
  code: 'no-remote-host-asked',
  title,
});

/** The empty list: which of the three wordings of W5 says why. */
const emptySaid = (damaged: boolean, allowNew: boolean): string => (damaged ? HOSTS_SAID.emptyDamaged : allowNew ? HOSTS_SAID.emptyOn : HOSTS_SAID.emptyOff);

/** What the person chose, whatever the kind of change that carried it. `auto` is never a person's choice. */
const chosenOf = (change: HostChange): 'yes' | 'no' | 'forget' => (change.kind === 'forget' ? 'forget' : change.kind === 'auto' ? 'yes' : change.answer);

/** The change question's detail: how the host stands, over damage what is wrong, then the words that tell what each button does. */
async function changeDetail(keeper: HostKeeper, host: string, allowNew: boolean): Promise<{ readonly detail: string; readonly damaged: boolean }> {
  const value = keeper.stored();
  const list = readableOf(value);
  const fileDamaged = (await keeper.roundStop()) === 'damaged';
  const standings = await keeper.standings();
  const refusals = standings.filter((one) => one.answer === 'no').length;
  const parts = [
    list.damaged ? HOSTS_SAID.damagedList(list.rows.length, list.rows.filter((row) => row.answer === 'no').length, list.unreadable) : '',
    fileDamaged ? HOSTS_SAID.damagedFile(sideLabel(), refusals) : '',
    HOSTS_SAID.changeDetail(standings.find((one) => one.host === host), allowNew, sideLabel()),
  ];

  return { detail: parts.filter((part) => part.length > 0).join(' '), damaged: list.damaged || fileDamaged };
}

const askChange = (host: string, detail: string): Promise<string | undefined> => notifyAndAsk({
  as: 'warning',
  class: 'confirmation',
  source: 'remoteSessions',
  code: 'change-remote-host-answer',
  subject: host,
  modal: true,
  title: HOSTS_SAID.title(host),
  detail,
  actions: [READ, NOT_THIS_HOST, FORGET],
});

/** The change a button means: over damage a `mend`, else a plain change. `undefined` for Escape. */
const BUTTON_ANSWER: ReadonlyMap<string | undefined, 'yes' | 'no' | 'forget'> = new Map([[READ, 'yes'], [NOT_THIS_HOST, 'no'], [FORGET, 'forget']]);

function changeFor(button: string | undefined, host: string, damaged: boolean): HostChange | undefined {
  const answer = BUTTON_ANSWER.get(button);
  if (answer === undefined) {
    return undefined;
  }

  return damaged ? { kind: 'mend', host, answer } : plainChange(host, answer);
}

const plainChange = (host: string, answer: 'yes' | 'no' | 'forget'): HostChange => (answer === 'forget' ? { kind: 'forget', host } : { kind: 'person', host, answer });

/** Commits a person's answer for a host and tells the person when the store did not keep it. */
async function committedAndTold(keeper: HostKeeper, host: string, change: HostChange): Promise<void> {
  const said = notKeptSaid(host, chosenOf(change), await keeper.commit(change));
  if (said !== undefined) {
    await tell(said);
  }
}

/** The change question for one host, and the chosen change committed. A change the store did not keep is told. */
async function changeHostOf(keeper: HostKeeper, host: string): Promise<void> {
  const allowNew = allowsNewHosts(configNow());
  const { detail, damaged } = await changeDetail(keeper, host, allowNew);
  const change = changeFor(await askChange(host, detail), host, damaged);
  if (change !== undefined) {
    await committedAndTold(keeper, host, change);
  }
}

/** The host the person chose from the list, or nothing (Escape). */
async function chosenHost(rows: readonly HostChoice[]): Promise<string | undefined> {
  const chosen = await askPerson(() => vscode.window.showQuickPick(rows, { title: 'Hosts read over ssh', placeHolder: 'Choose a host to change its answer' }));

  return chosen?.host;
}

const liveHostsOf = (): readonly string[] =>
  liveTerminals(vscode.window.terminals).flatMap((one) => { const name = credsSession(one.name); return name.kind === 'session' ? [name.host] : []; });

/** The panel's list: choosing a host asks again and commits the answer; Escape changes nothing. */
async function changeHostsOf(keeper: HostKeeper): Promise<void> {
  const allowNew = allowsNewHosts(configNow());
  const standings = await keeper.standings();
  const listed = choicesOf(listedHosts(keeper.stored(), liveHostsOf()), standings);
  const rows = listed.map((one) => hostChoice(one.host, one.standing, allowNew));
  if (rows.length === 0) {
    await tell(emptySaid(readableOf(keeper.stored()).damaged, allowNew));

    return;
  }
  const host = await chosenHost(rows);
  if (host !== undefined) {
    await changeHostOf(keeper, host);
  }
}

/** The question for a host with no answer: committed as a person's answer; over damage it is the change question. */
async function askHostOf(keeper: HostKeeper, host: string): Promise<void> {
  if (readableOf(keeper.stored()).damaged || (await keeper.roundStop()) === 'damaged') {
    return changeHostOf(keeper, host);
  }
  const answer = await askQuestion(host);
  if (answer !== undefined) {
    await committedAndTold(keeper, host, { kind: 'person', host, answer });
  }
}

/**
 * What a press of *Save now* that came to `full` or `damaged` says, in W6's words. An `unwritten` press is told by the keeper, as W9, with its new
 * reason. `host` is the host of the item that failed (`failedHostOf`): a Stop has none, and W6's "full" sentence names a host.
 */
const pressSaid = (host: string | undefined, result: Committed): string | undefined =>
  (result.kind === 'full' && host === undefined ? undefined : notKeptSaid(host ?? '', 'no', result));

/** W9: a refusal or a Stop the file did not take. One call site, ONE action: *Save now*; `after` is given what the press came to. */
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

/** The change event of a road that has no keeper to listen to: nobody is ever told. */
const noChange: vscode.Event<void> = () => ({ dispose: () => undefined });

/**
 * The road over a keeper and a run seam. `run` is `runRemote` unless a test says otherwise. The per-terminal state — the picked id
 * and the latch — lives here, in `WeakMap`s keyed by the terminal object, so it goes with it.
 */
export function remoteSessionsOf(keeper: HostKeeper, run: RoadSeams['run'] = runRemote, onChanged: vscode.Event<void> = noChange): RemoteSessions {
  const picked = new WeakMap<vscode.Terminal, Picked>();
  const latch = onePerTerminal<vscode.Terminal>();
  const seamsOf = (terminal: vscode.Terminal): RoadSeams => ({
    store: { consent: (host) => keeper.consent(host), answer: (host, answer) => keeper.commit({ kind: 'person', host, answer }) },
    ask: askQuestion,
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
    admitHost: (host) => keeper.admit(host),
    hostStandings: () => keeper.standings(),
    stopRound: () => keeper.stopRound(),
    startRound: () => keeper.startRound(),
    roundStop: () => keeper.roundStop(),
    saveWaiting: () => keeper.saveWaiting(),
    changeHost: (host) => changeHostOf(keeper, host),
    askHost: (host) => askHostOf(keeper, host),
    allowsNewHosts: () => allowsNewHosts(configNow()),
    ignoredLayer: () => ignoredLayerFor(configNow(), ALLOW_NEW_HOSTS_KEY),
    onChanged,
    changeHosts: () => changeHostsOf(keeper),
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

/** The keeper of this window over the profile's state and the folder of this side's refusals file. */
function keeperOf(context: vscode.ExtensionContext, folder: string, changed: () => void): HostKeeper {
  const store = refusalsStore(refusalsDiskOver(folder, { file: REFUSED_FILE, markerPrefix: MARKER_PREFIX }, thisMachine), Date.now);
  const keeperRef: { current?: HostKeeper } = {};
  const keeper = hostKeeper({
    state: context.globalState,
    key: TERMINAL_HOSTS_KEY,
    refusals: store,
    now: () => new Date().toISOString(),
    allowNew: () => allowsNewHosts(vscode.workspace.getConfiguration('coai')),
    changed,
    unkept: (host, why, marked) => {
      const live = keeperRef.current as HostKeeper;
      const title = host === undefined ? HOSTS_SAID.unkeptStop(why, sideLabel(), marked) : HOSTS_SAID.unkeptHost(host, why as UnkeptWhy, sideLabel(), marked);
      raiseUnkept(live, host ?? 'accounts-round', title, marked ? HOSTS_SAID.cureMarked : HOSTS_SAID.cureUnmarked, (result) => afterPress(live, result));
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
  const sessions = remoteSessionsOf(keeperOf(context, folder, () => { void setReadsKey(sessions); changes.fire(); }), (host, session, operands, script) => boundRun(host, session, operands, script), changes.event);
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
  readonly read: RemoteSessions['read'];
  readonly pickedOf: RemoteSessions['pickedOf'];
  /** What the context key `coai.terminalReadsSession` was last set to, or nothing before the first set. */
  readonly readsKey: () => boolean | undefined;
  /** The product's own commit: a scenario gives a host its answer the way a press does. */
  readonly commit: RemoteSessions['commitHost'];
  readonly admit: RemoteSessions['admitHost'];
  readonly standings: RemoteSessions['hostStandings'];
  /** What is stored, as it is, for the `finally`: the list, and the TEXT of this side's refusals file (`undefined` when there is none). */
  readonly stored: () => Promise<{ readonly list: unknown; readonly file: string | undefined }>;
  /**
   * TEST ONLY. Puts both back whole — the list by one raw write, the file by one raw write or a removal — removes every marker and
   * the lock, empties what this window holds as not saved and what it remembers of the file, and sends the change signal a commit sends.
   */
  readonly restore: (before: { readonly list: unknown; readonly file: string | undefined }) => Promise<void>;
  /** The Stop, through the product's own road. */
  readonly stopRound: RemoteSessions['stopRound'];
  readonly startRound: RemoteSessions['startRound'];
  readonly roundStop: RemoteSessions['roundStop'];
}

/** The file's text; `undefined` only when there is no file. Any other error is thrown, so a scenario does not take a file it could not read for a missing one. */
const textOrNothing = (path: string): Promise<string | undefined> =>
  readFile(path, 'utf8').catch((error: unknown) => {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') {
      return undefined;
    }
    throw error;
  });

export const testDoorOf = (sessions: RemoteSessions, state: vscode.Memento, folder: string): RemoteSessionsTestDoor => ({
  replaceRun: (run) => { boundRun = run ?? runRemote; },
  read: sessions.read,
  pickedOf: sessions.pickedOf,
  readsKey: () => lastKey,
  commit: sessions.commitHost,
  admit: sessions.admitHost,
  standings: sessions.hostStandings,
  stored: async () => ({ list: state.get(TERMINAL_HOSTS_KEY), file: await textOrNothing(join(folder, REFUSED_FILE)) }),
  restore: async (before) => {
    await state.update(TERMINAL_HOSTS_KEY, before.list);
    if (before.file === undefined) {
      await rm(join(folder, REFUSED_FILE), { force: true });
    } else {
      await writeFile(join(folder, REFUSED_FILE), before.file, 'utf8');
    }
    for (const name of (await readdir(folder).catch(() => [] as string[])).filter((one) => one.startsWith(MARKER_PREFIX) || one.endsWith('.lock'))) {
      await rm(join(folder, name), { force: true });
    }
    sessions.keeper.forgetHeld();
    void setReadsKey(sessions);
    // The raw writes above sent no change signal: tell the listeners (the accounts window's known hosts and generation) what the store now holds, as a commit does.
    signalChange?.();
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
