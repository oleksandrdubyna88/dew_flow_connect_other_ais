import * as vscode from 'vscode';
import type { ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { notify, notifyAndAsk } from './notify';
import { askPerson } from './personWait';
import { HostAnswer, PickRow, Picked, Purpose, RemoteAnswer, RoadSeams, answersOf, onePerTerminal, reachOf, remoteRoad, withAnswer, withoutHost } from './remoteRead';
import { runRemote } from './remoteSsh';

/**
 * The `vscode` seams of the road to a terminal's remote Claude session (E2.S3, contract C4). Host code: every decision is
 * `remoteRead.ts`'s (pure, tested by its own unit tests); this file only supplies the editor — the modal question, the
 * progress line, the pick list, the answers in `globalState` — and holds the two per-terminal maps.
 *
 * <p>Nothing else in the extension spawns ssh or reads the consent. No `console` and no `notify` here: a sentence reaches the
 * door that asked as a value, and the door says it.</p>
 *
 * <p>Unit tests cannot load this module (it imports `vscode`); `terminalHostsScenario.ts` runs it in a real editor, in CI.</p>
 */

/** The `globalState` key of the answers: at most 100 rows, one per host. */
export const TERMINAL_HOSTS_KEY = 'coai.terminalHosts';

const READ = 'Read';
const NOT_THIS_HOST = 'Not this host';
const FORGET = 'Forget';

const answerOf = (button: string | undefined): 'yes' | 'no' | undefined => (button === READ ? 'yes' : button === NOT_THIS_HOST ? 'no' : undefined);

/**
 * The modal question for a host, through the one door every message goes through (it is recorded, and the wait is the person's, not
 * the panel's). Escape gives `undefined`, and the road writes nothing for it.
 */
const askHost = async (host: string): Promise<'yes' | 'no' | undefined> =>
  answerOf(await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'remoteSessions',
    code: 'read-remote-session',
    subject: host,
    modal: true,
    title: `Read the Claude session on “${host}” over ssh?`,
    actions: [READ, NOT_THIS_HOST],
  }));

interface Choice extends vscode.QuickPickItem {
  readonly sessionId: string;
}

const choiceOf = (row: PickRow): Choice => ({ label: row.label, description: row.description, detail: row.detail, sessionId: row.sessionId });

/** The pick list: one row for each candidate, in the order the road decided (newest first), nothing preselected. */
async function pickOne(rows: readonly PickRow[]): Promise<string | undefined> {
  const chosen = await askPerson(() => vscode.window.showQuickPick(rows.map(choiceOf), {
    canPickMany: false,
    title: 'Which Claude session is this terminal’s?',
    placeHolder: 'Choose the session to read',
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

/** The answers of one `Memento`. */
export interface AnswerStore {
  readonly read: () => unknown;
  readonly write: (rows: readonly HostAnswer[]) => PromiseLike<void>;
}

/** What the rest of the extension asks of the road. */
export interface RemoteSessions {
  /** One read of the terminal's remote session: one at a time for a terminal, the LIVE `terminal.name`, the picked id of that terminal. */
  readonly read: (terminal: vscode.Terminal, purpose: Purpose) => Promise<RemoteAnswer>;
  /** The terminal is closed: its picked id goes. */
  readonly forget: (terminal: vscode.Terminal) => void;
  /** The answers, in `globalState`. */
  readonly answers: AnswerStore;
  /** *CoAI: hosts read over ssh*: list the remembered hosts, change one answer or forget it. */
  readonly changeHosts: () => Promise<void>;
}

const CHANGES: ReadonlyMap<string | undefined, (list: unknown, host: string, now: string) => readonly HostAnswer[]> = new Map([
  [READ, (list, host, now) => withAnswer(list, host, 'yes', now)],
  [NOT_THIS_HOST, (list, host, now) => withAnswer(list, host, 'no', now)],
  [FORGET, (list, host) => withoutHost(list, host)],
]);

interface HostChoice extends vscode.QuickPickItem {
  readonly host: string;
}

const hostChoice = (row: HostAnswer): HostChoice =>
  ({ label: row.host, description: row.answer === 'yes' ? 'the Claude session is read' : 'not read', detail: `asked ${row.askedUtc}`, host: row.host });

/** Nothing to change yet: the first read of a host asks once. */
const saidNoHost = (): Promise<void> => notify({
  as: 'information',
  class: 'outcome',
  source: 'remoteSessions',
  code: 'no-remote-host-asked',
  title: 'No host has been asked yet. The first read of a Claude session in a “Creds: <host> / <session>” terminal asks once.',
});

/** The host the person chose from the list of remembered ones, or nothing (Escape). */
async function chosenHost(rows: readonly HostAnswer[]): Promise<string | undefined> {
  const chosen = await askPerson(() => vscode.window.showQuickPick(rows.map(hostChoice), { title: 'Hosts read over ssh', placeHolder: 'Choose a host to change its answer' }));

  return chosen?.host;
}

/** The question again for one host, with *Forget* beside the two answers. */
const askChange = (host: string): Promise<string | undefined> => notifyAndAsk({
  as: 'warning',
  class: 'confirmation',
  source: 'remoteSessions',
  code: 'change-remote-host-answer',
  subject: host,
  modal: true,
  title: `Read the Claude session on “${host}” over ssh?`,
  actions: [READ, NOT_THIS_HOST, FORGET],
});

/** The chosen change applied to the stored list; a button that is none of the three (Escape) changes nothing. */
async function changed(store: AnswerStore, host: string): Promise<void> {
  const change = CHANGES.get(await askChange(host));
  if (change !== undefined) {
    await store.write(change(store.read(), host, new Date().toISOString()));
  }
}

/** The panel's list: choosing a host asks again and writes the new answer; *Forget* removes the row; Escape changes nothing. */
async function changeHostsOf(store: AnswerStore): Promise<void> {
  const rows = answersOf(store.read());
  const host = rows.length === 0 ? undefined : await chosenHost(rows);
  if (rows.length === 0) {
    await saidNoHost();
  } else if (host !== undefined) {
    await changed(store, host);
  }
}

/**
 * The road over a `Memento` and a run seam. `run` is `runRemote` unless a test says otherwise (S3-H1 passes a fake that counts).
 * The per-terminal state — the picked id and the latch — lives here, in `WeakMap`s keyed by the terminal object, so it goes with it.
 */
export function remoteSessionsOf(state: vscode.Memento, run: RoadSeams['run'] = runRemote): RemoteSessions {
  const picked = new WeakMap<vscode.Terminal, Picked>();
  const latch = onePerTerminal<vscode.Terminal>();
  const answers: AnswerStore = { read: () => state.get(TERMINAL_HOSTS_KEY), write: (rows) => state.update(TERMINAL_HOSTS_KEY, rows) };
  const seamsOf = (terminal: vscode.Terminal): RoadSeams => ({
    store: answers,
    now: () => new Date().toISOString(),
    ask: askHost,
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
    answers,
    changeHosts: () => changeHostsOf(answers),
  };
}

let bound: RemoteSessions | undefined;

/** Activation: the one road of this window, its terminal listener and the panel's command. */
export function bindRemoteSessions(context: vscode.ExtensionContext): RemoteSessions {
  const sessions = remoteSessionsOf(context.globalState);
  bound = sessions;
  context.subscriptions.push(
    vscode.window.onDidCloseTerminal((closed) => { sessions.forget(closed); }),
    vscode.commands.registerCommand('coai.terminalHosts', () => sessions.changeHosts()),
  );

  return sessions;
}

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
  const live = vscode.window.terminals.map((terminal) => ({ key: terminal, name: terminal.name, exited: terminal.exitStatus !== undefined }));
  const reach = reachOf(live, panels.keyOf(id), recordedNameOf(id));

  return reach.kind === 'refused' ? reach : terminalFor(reach.key);
}
