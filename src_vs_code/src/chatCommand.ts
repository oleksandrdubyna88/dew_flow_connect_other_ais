import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { randomUUID } from 'node:crypto';
import * as vscode from 'vscode';
import { ChatTabMemory, SavedTab, reloadedNote } from './chatTabs';
import { ChatEntry, ChatPanels } from './chatPanels';
import { ChatSession } from './chatSession';
import { ChatMessage, ChatModelChoice } from './chatPage';
import { CliChatSession, REAL_TIMERS } from './cliChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import { ChatMemory, chatChoice, chatModelsFrom, isRemote, memoryOf } from './chatModels';
import { remoteIsFull } from './remoteAsk';
import { remoteChatFor } from './chatRemote';
import { TeamServer, rowBelongsTo, teamServersFrom } from './teamServers';
import { readToken } from './teamServerAuth';
import { coaiDataDir } from './dataDir';
import { chatSettingsFrom } from './chatSettings';
import { chatUiScale, createChatPanel, pushChatDraft, pushChatState } from './chatPanel';
import { captureSelection, COPY_SCRIPT, argvFor, ran } from './selectionCapture';
import { ChatHome, adapterFor, chatHome, chatRuntimeRefusal, defaultExecutableFor } from './cliChatLaunch';
import { chatProcessFor } from './chatProcess';
import { launch } from './processLauncher';
import { resolvedExecutable } from './versionProbe';
import { CARRY_BUDGET, REMOTE_CARRY_BUDGET, carriedTurn, openingTurn } from './chatPrompt';
import { LanguageCode } from './settingsShape';
import { sourceSession, TabSnapshot } from './sessionKey';
import { triggerPlan } from './chatTrigger';
import { Vendor, vendorsFrom } from './vendors';

/**
 * The one command the person actually presses.
 *
 * <p>The `vscode` half, and the only place the pieces meet: which door the invocation came through
 * (`chatTrigger`), where the passage comes from (`selectionCapture` or the clipboard), which tab it
 * belongs to (`sessionKey`), which conversation that is (`chatPanels`), what to say (`chatPrompt`),
 * and who answers (`chatModels` → `cliChatLaunch` → `cliChatSession`). Every one of those decides
 * without a host and is tested; this file wires them and does nothing clever, which is the same
 * reason `chatPanel.ts` is thin — what cannot be tested should be small.</p>
 */

/**
 * What a conversation is, beyond its process. Keyed by the entry's own id, weak so it dies with it.
 *
 * <p>It EXTENDS `ChatMemory` rather than restating its fields, which is what makes them impossible
 * to update in one of the two places a session starts and forget in the other: `memoryOf` is checked
 * against that type at its source, and both callers take the whole object.</p>
 */
interface Thread extends ChatMemory {
  /**
   * The conversation itself — REPLACED when the person picks another model.
   *
   * <p>Held here rather than widened into `ChatPanels`: the registry was written to need only
   * that a session can be ENDED, which is what makes its tests a dozen lines of fakes. Asking it
   * to know how a turn is sent would buy nothing and cost that.</p>
   */
  session: ChatSession;
  /** The directory that session runs in. Replaced with it, and released with it. */
  home: ChatHome;
  readonly passage: string;
  readonly models: readonly ChatModelChoice[];
  modelId: string;
  /** Whether a turn is in flight. Only so a switch can say out loud that it is waiting for one. */
  running: boolean;
  /**
   * Which turn of this conversation is running, counted from 1. Never reset.
   *
   * <p>It exists so a STOP can name what it means to stop. The page renders its control against the
   * turn it is watching and posts that number back; a stop naming anything else is refused here. A
   * bridge message can land a tick after the answer did, and by then the next question can already
   * be in flight — without the number the second press of a button would end the turn that the first
   * press was too late to reach. (codex and gemini, the plan round.)</p>
   */
  turn: number;
  /**
   * The conversation to hand the NEXT turn, because the process it goes to never heard it.
   *
   * <p>Empty in the ordinary case. Filled when the person switches model: a vendor CLI keeps its
   * context inside its own process, so the replacement starts with nothing, and the only way to
   * carry the questions and the answers across is to say them again in the next turn. Cleared once
   * that turn has been sent — from then on the new process remembers, exactly as the old one did,
   * and nobody pays to re-send a conversation twice.</p>
   */
  carry: readonly ChatMessage[];
  messages: readonly ChatMessage[];
  /**
   * The turns of THIS conversation, one after another.
   *
   * <p>The session already refuses to interleave two turns down one pipe, but the transcript is
   * kept here and two overlapping `ask` calls would write it out of order: pressing the keybinding
   * twice against an open tab put both questions above both answers. The page cannot prevent it —
   * it disables its own composer, and the keybinding does not go through the composer. So the
   * chain is here, mirroring the one inside `cliChatSession`. (gemini, the code round.)</p>
   */
  turns: Promise<unknown>;
  /**
   * This conversation's id in the store, so what it is holding can be written down as it changes.
   *
   * <p>Not the entry's identity object — that one dies with the window. This is the string the page
   * hands back to the serializer after a reload.</p>
   */
  readonly saveId: string;
  /** The tab's heading, kept here because what is written down has to name the conversation. */
  readonly title: string;
  /**
   * A conversation restored from a reload, whose vendor process does not exist yet.
   *
   * <p>Nothing is started when a tab comes back: the process behind it died with the window, a
   * restored tab may never be spoken to again, and starting one per tab at every reload would spawn
   * a CLI for each of them for nothing. The first question opens the session and hands it the whole
   * transcript through `carry`, which is the handover a model switch already ships.</p>
   */
  reopen: boolean;
}

const threads = new WeakMap<object, Thread>();

/**
 * Where conversations are kept so a window reload does not empty them. Set once, in `activate`.
 *
 * <p>A module-level handle rather than a parameter on six signatures: the command has no context and
 * neither do the callbacks a panel is wired with, and threading a store through both to reach two
 * call sites would be a wide change for a narrow need. It is absent only in tests of this file's
 * pure neighbours, and every use is guarded.</p>
 */
let memory: ChatTabMemory | undefined;

export function rememberChatsIn(store: ChatTabMemory): void {
  memory = store;
}

/** The tabs, narrowed to what `sessionKey` judges on. */
function snapshots(): { active: TabSnapshot | undefined; all: TabSnapshot[] } {
  const all: TabSnapshot[] = [];
  for (const group of vscode.window.tabGroups.all) {
    for (const tab of group.tabs) {
      const input = tab.input as { viewType?: unknown } | undefined;
      all.push({
        key: tab,
        label: tab.label,
        viewType: typeof input?.viewType === 'string' ? input.viewType : '',
      });
    }
  }
  const activeTab = vscode.window.tabGroups.activeTabGroup.activeTab;

  return { active: all.find((tab) => tab.key === activeTab), all };
}

/** A directory with nothing in it, and the way to take it away. See `cliChatLaunch` for both. */
function emptyTempDir(): ChatHome {
  return chatHome(
    () => fs.mkdtempSync(path.join(os.tmpdir(), 'coai-chat-')),
    (dir) => fs.rmSync(dir, { recursive: true, force: true }),
    // Not a message box: nothing a person can do about a locked temp directory, and a modal for it
    // would be worse than the leak. The extension host's own log is where this belongs.
    (failure) => console.warn(`[coai] ${failure}`),
  );
}

/** Run the copy helper, and resolve when it has finished however it finished. */
function pressCopy(): Promise<void> {
  const child = launch('powershell.exe', argvFor(COPY_SCRIPT), { cwd: os.tmpdir() });

  return ran(child, (ms, run) => {
    const handle = setTimeout(run, ms);

    return () => clearTimeout(handle);
  });
}

/**
 * Push the page's whole visible state, with the thread's own model list rather than an empty one.
 *
 * <p>A thread that is GONE pushes nothing. That is the guard for a tab closed while a turn was still
 * in flight: the answer can arrive up to a poll later, and posting into a webview VS Code has torn
 * down throws out of a callback nobody is catching. Forgetting the thread on close is what makes
 * every reader of it — this one included — a no-op afterwards.</p>
 *
 * @param queued how many turns are ahead of this one on a Team server; 0 for none, and for a local
 *   model, which has no queue
 */
function show(entry: ChatEntry, running: boolean, failure: string, queued = 0): void {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  pushChatState(entry, {
    messages: thread.messages,
    running,
    // Built in epic 2 and set for the first time here: a remote conversation stops at three turns
    // and the page offers the local model that has a memory instead.
    capped: thread.forgetful && remoteIsFull(thread.asked),
    failure,
    models: thread.models,
    modelId: thread.modelId,
    queued,
  });
  // The one place the transcript reaches a page is the one place it is written down. A record per
  // push is a record that cannot be a turn behind, and the store is small: a title, a passage, a
  // model id and the messages.
  memory?.remember({
    id: thread.saveId,
    title: thread.title,
    passage: thread.passage,
    modelId: thread.modelId,
    messages: thread.messages,
  });
}

/**
 * Ask, once whatever this conversation is already doing has finished.
 *
 * <p>Two things the chain buys, and both were found by the gate. The transcript stays in order when
 * the keybinding is pressed twice in a row. And a `send` that REJECTS — which `CliChatSession`
 * promises never to do, but a `ChatSession` is an interface and the remote one is not written yet —
 * cannot leave the composer locked forever behind a `void ask(...)` nobody is watching.</p>
 */
function ask(entry: ChatEntry, text: string): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return Promise.resolve();
  }
  const mine = thread.turns
    .then(() => oneTurn(entry, text))
    .catch((reason: unknown) => {
      // Including the flag: a turn that threw is not a turn still running, and leaving it set would
      // make every later switch claim to be waiting for an answer that will never arrive.
      const thrown = threads.get(entry.id);
      if (thrown !== undefined) {
        thrown.running = false;
      }
      show(entry, false, `the turn failed unexpectedly: ${asText(reason)}`);
    });
  thread.turns = mine;

  return mine;
}

/** The answer language, read fresh: a follow-up turn is asked long after the command ran. */
function chatLanguage(): LanguageCode {
  return chatSettingsFrom((key) => vscode.workspace.getConfiguration('coai').get(key)).language;
}

/** A thrown thing, as a sentence. */
function asText(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

/**
 * Give a restored conversation the process it has not got, or say why it cannot have one.
 *
 * <p>Returns an empty string when there is nothing to do, which is every ordinary turn. A refusal is
 * the sentence the page shows: the model this conversation was having may no longer be configured,
 * its CLI may be gone, a Team server may have signed out. All three are the same three checks the
 * command makes before opening a tab at all — asked again here, because a reload can be a week and
 * a machine rebuild away from the conversation it is restoring.</p>
 */
async function reopened(thread: Thread): Promise<string> {
  if (!thread.reopen) {
    return '';
  }
  const config = vscode.workspace.getConfiguration('coai');
  const ready = readyToChat(config, thread.modelId);
  if (!ready.ok) {
    return ready.refusal;
  }
  const cli = await cliFor(ready.vendor);
  if (cli.refusal.length > 0) {
    return cli.refusal;
  }
  const remote = isRemote(ready.vendor) ? await remoteFor(ready.vendor) : { session: undefined, refusal: '' };
  if (remote.refusal.length > 0) {
    return remote.refusal;
  }

  const opened = started(ready.vendor, cli.resolved, remote.session);
  thread.session.dispose();
  thread.home.release();
  thread.session = opened.session;
  thread.home = opened.home;
  thread.modelId = ready.modelId;
  // The WHOLE memory object, not one field of it: a switch that set `forgetful` and forgot `asked`
  // is a defect this file has already had once, and the type is what stops it happening twice.
  Object.assign(thread, memoryOf(ready.vendor));
  thread.reopen = false;

  return '';
}

/**
 * One turn, start to finish.
 *
 * <p>The question is appended BEFORE the turn is sent, so the page shows it while the model is
 * thinking — nine measured seconds of silence otherwise look like a tab that ignored a keypress.</p>
 */
async function oneTurn(entry: ChatEntry, text: string): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  // A conversation that came back from a reload has no process yet. It is opened HERE, on the first
  // question and not before, so a window with five restored tabs starts nothing until one of them is
  // spoken to — and the transcript is already in `carry`, so the model that answers is handed the
  // whole thread exactly as it is after a model switch.
  const refused = await reopened(thread);
  if (refused.length > 0) {
    show(entry, false, refused);

    return;
  }
  thread.messages = [...thread.messages, { role: 'you', text }];
  thread.running = true;
  thread.turn += 1;
  show(entry, true, '');

  // A forgetful model is handed the conversation EVERY time, not only after a switch: the server
  // answers one question and forgets it, so turn three without the transcript is turn one wearing
  // a number. This is also why such a conversation is capped — the bill for turn N is the bill for
  // everything before it.
  if (thread.forgetful && thread.messages.length > 1) {
    thread.carry = thread.messages.slice(0, -1);
  }

  // What is SHOWN is what the person typed; what is SENT may carry the whole conversation with it,
  // because the process it is going to never heard any of it. Putting the carried version in the
  // transcript would print the entire history back at them under their own one-line question.
  const carrying = thread.carry;
  // Named, because a budget is the kind of thing that must be readable at a glance: a server takes
  // less than a pipe, and which one this conversation is is the whole difference.
  const budget = thread.forgetful ? REMOTE_CARRY_BUDGET : CARRY_BUDGET;
  const sent = carrying.length > 0 ? carriedTurn(carrying, text, chatLanguage(), budget) : text;

  // The queue position, pushed as it changes. A local session never calls this back; a Team server
  // does on every poll, which is the difference between "the model is thinking" and "somebody else's
  // round has the vendor and you are fourth". Guarded by the flag, because a turn that finished
  // while a poll was in flight must not re-open the thinking line. (gemini, the code round.)
  const result = await thread.session.send(sent, (position) => {
    if (thread.running) {
      show(entry, true, '', position);
    }
  });
  thread.running = false;
  if (!result.ok) {
    // A STOPPED turn is written down before anything is carried, and the order is the whole finding.
    // The question was appended before the turn was sent, so a turn that ends without an answer
    // leaves the transcript ending on a dangling question. Handing THAT to a fresh process gives the
    // next model a question nobody answered with no sign it was abandoned — and the turn after it
    // appends a second `you` directly on top of the first. One line saying what happened makes the
    // transcript true, and it is only then worth carrying. (gemini, the plan round, Blocking.)
    if (result.stopped === true) {
      thread.messages = [...thread.messages, { role: 'model', text: '(you stopped this answer)' }];
    }
    // And only a vendor that LOST the conversation needs it re-sent. `contextLost` on the failure arm
    // is the session saying which of the two it is: a killed process that held the thread says yes, a
    // per-turn vendor resuming by id and a server that never remembered anything say nothing. Set
    // after the line above, so what is carried is the transcript a reader would recognise.
    if (result.contextLost === true) {
      thread.carry = [...thread.messages];
    }
    // The carry is NOT cleared here. A turn that failed carried nothing anywhere, and clearing it
    // would mean the retry — the same question, one keypress later — reaches the new model with no
    // conversation behind it, which is the exact thing the switch existed to prevent. (gemini, the
    // plan round, twice.)
    show(entry, false, result.failure);

    return;
  }
  // Cleared only now, and only once: from here the process remembers, and nobody pays to re-send a
  // conversation twice. `thread.carry` rather than `carrying` — a switch may have queued another.
  if (thread.carry === carrying) {
    thread.carry = [];
  }

  // A restart is said in the transcript, not only in a flag nobody sees: the answer genuinely does
  // not remember the earlier turns, and a reader comparing it with them deserves to know why. A
  // model the person SWITCHED to is not this case — that one was handed the conversation.
  const answer = result.contextLost === true
    ? `(the conversation restarted — this answer does not remember the earlier ones)\n\n${result.answer}`
    : result.answer;
  thread.messages = [...thread.messages, { role: 'model', text: answer }];
  thread.asked += 1;
  show(entry, false, '');
}

/**
 * Everything needed to start one conversation, or the sentence saying why it cannot start.
 *
 * <p>Two arms rather than one shape with an empty field. The single-shape version had to put
 * SOMETHING in `vendor` on the refusal path and reached for `vendors[0] as Vendor` — a cast that is
 * `undefined` whenever the person has no vendors configured at all, and a promise to keep a shape
 * by hand that comes due the first time somebody reads the field before checking the sentence.
 * The compiler keeps that promise instead. (gemini, the code round.)</p>
 */
type Ready =
  | {
    readonly ok: true;
    readonly vendor: Vendor;
    readonly models: readonly ChatModelChoice[];
    readonly modelId: string;
  }
  | { readonly ok: false; readonly refusal: string };

function readyToChat(config: vscode.WorkspaceConfiguration, asked: string): Ready {
  const vendors = vendorsFrom(config.get('vendors'));
  const models = chatModelsFrom(vendors);
  // A model the person NAMED and which cannot answer is refused by that name — never quietly
  // replaced by another vendor's, which is somebody else's model, billed, in a voice nobody chose.
  const choice = chatChoice(models, asked);
  if (choice.refusal.length > 0) {
    return { ok: false, refusal: choice.refusal };
  }

  const vendor = vendors.find((row) => row.id === choice.modelId);
  if (vendor === undefined) {
    return { ok: false, refusal: `The model ${choice.modelId} is no longer configured.` };
  }

  const refusal = chatRuntimeRefusal(vendor);

  return refusal.length > 0
    ? { ok: false, refusal }
    : { ok: true, vendor, models: models.offered, modelId: choice.modelId };
}

/** The clipboard, as `selectionCapture` wants it. VS Code answers a Thenable, not a Promise. */
const hostClipboard = {
  read: async (): Promise<string> => vscode.env.clipboard.readText(),
  write: async (value: string): Promise<void> => {
    await vscode.env.clipboard.writeText(value);
  },
};

/**
 * Where the passage comes from, and a status line while it is being fetched.
 *
 * <p>The keybinding path takes about 1.7 seconds — PowerShell's own startup, mostly — and until the
 * tab appears there is nothing at all to see. A person who presses a shortcut and watches nothing
 * happen presses it again, which is how one question becomes two. The menu path is instant and says
 * nothing.</p>
 */
function passageFor(path: 'menu' | 'keyboard'): Promise<{ text: string; failure: string }> {
  if (path === 'menu') {
    return hostClipboard.read().then((text) => ({ text, failure: '' }));
  }

  return Promise.resolve(
    vscode.window.withProgress(
      { location: vscode.ProgressLocation.Window, title: 'Copying the selection…' },
      () => captureSelection(pressCopy, hostClipboard),
    ),
  );
}

/** Which Claude Code session this belongs to, or nothing when it was not invoked from one. */
function matchedSession(panels: ChatPanels): ReturnType<typeof sourceSession> {
  const { active, all } = snapshots();

  return sourceSession(active, all, panels.known());
}

/**
 * A vendor process in an empty directory of its own, and the directory, held together.
 *
 * <p>The launch is built per TURN rather than once, because a vendor that keeps no process needs a
 * different command line for its second question than for its first: `codex` resumes a thread by
 * id, and the id is not known until the first turn has been answered. A persistent vendor ignores
 * the argument entirely and gets the same argv every time.</p>
 */
function started(vendor: Vendor, resolved: string, remote?: ChatSession): { session: ChatSession; home: ChatHome } {
  if (remote !== undefined) {
    // A Team server needs no process and no directory: the home is a stub whose release does
    // nothing, so the rest of this file does not have to know which kind it holds.
    return { session: remote, home: { dir: '', release: () => undefined } };
  }
  const home: ChatHome = emptyTempDir();
  const adapter = adapterFor(vendor.runtime);

  return {
    session: new CliChatSession(
      chatProcessFor(vendor, home.dir, resolved),
      DEFAULT_BUDGETS,
      REAL_TIMERS,
      adapter,
    ),
    home,
  };
}

/**
 * The FILE this vendor's CLI is, or the sentence saying it could not be found.
 *
 * <p>`spawn` searches neither PATHEXT nor the shell's own rules, so a bare name that every terminal
 * resolves fails here with `ENOENT`. Asked once, before anything is created, so a missing CLI is a
 * message about a missing CLI rather than a conversation that dies at its first turn.</p>
 */
/**
 * The conversation for a REMOTE row, or the sentence saying why there is none.
 *
 * <p>Two things can be missing and they are different sentences: the server the row belongs to may
 * have been removed from the settings, and this side may not be signed in to it. A person can act
 * on either, and neither is "the model did not answer".</p>
 */
async function remoteFor(vendor: Vendor): Promise<{ session: ChatSession | undefined; refusal: string }> {
  const servers = teamServersFrom(vscode.workspace.getConfiguration('coai').get('teamServers'));
  const server: TeamServer | undefined = servers.find((one) => rowBelongsTo(vendor, one));
  if (server === undefined) {
    return { session: undefined, refusal: `${vendor.id} belongs to a Team server this side no longer has.` };
  }
  const token = await readToken(coaiDataDir(), server.url);
  // The whole server, not just its URL: its id is half of this row's own name, so it is what tells
  // the row's id apart from the vendor name the server actually knows. See `serverVendorOf`.
  const session = remoteChatFor(vendor, server, token);

  return session === undefined
    ? { session: undefined, refusal: `Sign in to ${server.name} to chat with ${vendor.id}.` }
    : { session, refusal: '' };
}

async function cliFor(vendor: Vendor): Promise<{ resolved: string; refusal: string }> {
  if (isRemote(vendor)) {
    // A Team server has no executable to find. Saying so here keeps the caller's shape: one
    // question, one refusal, before anything is created.
    return { resolved: '', refusal: '' };
  }
  const asked = vendor.executablePath.length > 0 ? vendor.executablePath : defaultExecutableFor(vendor.runtime);
  const resolved = await resolvedExecutable(asked);

  return resolved.length > 0
    ? { resolved, refusal: '' }
    : {
      resolved: '',
      refusal: `${asked} could not be found. Install the ${vendor.runtime} CLI, or put its full path in that reviewer's settings.`,
    };
}

/** The vendor row behind a model id, read fresh — the person may have edited settings since. */
function vendorFor(modelId: string): Vendor | undefined {
  return vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors')).find((row) => row.id === modelId);
}

/**
 * The person chose a different model in the open tab.
 *
 * <p>A conversation is a process, so this is a new process — but not a new conversation. The whole
 * transcript, questions and answers both, is handed to the next turn (`carriedTurn`), because that
 * is the only way context crosses a process boundary here. Asked for directly by the owner, and the
 * alternative shipped for about an hour: a switch that started again and said so.</p>
 *
 * <p>It waits for a turn in flight instead of killing it. The page disables its composer while the
 * model is thinking but not its picker, and disposing the session under a running turn would fail
 * that turn with "the conversation was closed" — an error about something the person did on
 * purpose. The switch simply joins the queue the turns already run in.</p>
 */
function switchModel(entry: ChatEntry, modelId: string): void {
  const thread = threads.get(entry.id);
  if (thread === undefined || thread.modelId === modelId) {
    return;
  }
  if (thread.running) {
    void vscode.window.showInformationMessage(
      `Switching to ${modelId} as soon as the current answer arrives.`,
    );
  }
  thread.turns = thread.turns
    .then(() => switchNow(entry, modelId))
    .catch((reason: unknown) => {
      // Not swallowed: a switch that failed leaves the thread on the OLD model, and a person who
      // believes otherwise reads the next answer as the new model's. (codex, the code round.)
      const failure = `The chat could not switch to ${modelId}: ${asText(reason)}`;
      void vscode.window.showWarningMessage(failure);
      show(entry, false, failure);
    });
}

async function switchNow(entry: ChatEntry, modelId: string): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined || thread.modelId === modelId) {
    return;
  }
  const vendor = vendorFor(modelId);
  const refusal = vendor === undefined
    ? `The model ${modelId} is no longer configured.`
    : chatRuntimeRefusal(vendor);
  if (vendor === undefined || refusal.length > 0) {
    void vscode.window.showWarningMessage(refusal);
    // The page has already moved its own select; put the state back so it stops claiming otherwise.
    show(entry, false, refusal);

    return;
  }

  // The same resolution the first launch did, because the model being switched TO may be a vendor
  // whose CLI is not installed — and finding that out by spawning it would kill a conversation that
  // was working a moment ago.
  const cli = await cliFor(vendor);
  const remote = isRemote(vendor) ? await remoteFor(vendor) : { session: undefined, refusal: '' };
  const cannot = cli.refusal.length > 0 ? cli.refusal : remote.refusal;
  if (cannot.length > 0) {
    void vscode.window.showWarningMessage(cannot);
    show(entry, false, cannot);

    return;
  }

  thread.session.dispose();
  thread.home.release();
  const replacement = started(vendor, cli.resolved, remote.session);
  thread.session = replacement.session;
  thread.home = replacement.home;
  thread.modelId = modelId;
  // The memory rules move WITH the model. Left behind, they described the one just thrown away:
  // switching to a Team server kept `forgetful` false, so the server — which remembers nothing —
  // was asked turn two with no transcript behind it and the three-turn cap never applied; switching
  // back kept it true, so a CLI that HAS a memory was refused a fourth question. Spread from the one
  // function both callers use, so a field added there cannot be applied here and forgotten there.
  // (codex, the code round.)
  Object.assign(thread, memoryOf(vendor));
  // Everything said so far travels with the next question. Not sent now: nobody should be billed
  // for a conversation they moved and then never continued. Taken HERE rather than when the switch
  // was asked for, because this runs after the turn queue has drained — so an answer that was still
  // arriving when the person changed their mind is in the transcript by now. (codex, the plan round.)
  thread.carry = [...thread.messages];
  show(entry, false, '');
  // Said out loud: the next question costs more than the last one, because it carries everything
  // above it. A person who is not told reads the first answer as a model that mysteriously knows.
  void vscode.window.showInformationMessage(
    `Now asking ${modelId}. Your next question carries this conversation across to it.`,
  );
}

/** Everything one new conversation is made of. Called ONLY when a tab has no panel yet. */
function newConversation(
  panels: ChatPanels,
  ready: Extract<Ready, { ok: true }>,
  state: { readonly title: string; readonly passage: string; readonly draft: string },
  resolved: string,
  remote: ChatSession | undefined,
  extensionUri: vscode.Uri,
): ChatEntry {
  const first = started(ready.vendor, resolved, remote);
  const session = first.session;
  // Minted here, once, and never seen by anybody: it goes into the page, comes back from the page
  // after a reload, and names this conversation in the store. A title could not — two Claude Code
  // sessions can share one, which is the defect `chatPanels.ts` was keyed by identity to avoid.
  const saveId = randomUUID();
  const entry = createChatPanel(
    {
      id: saveId,
      title: state.title,
      passage: state.passage,
      messages: [],
      models: ready.models,
      modelId: ready.modelId,
      running: false,
      capped: false,
      failure: '',
      draft: state.draft,
      uiScale: chatUiScale(),
    },
    session,
    conversationHooks(panels),
    extensionUri,
  );
  // Recorded against the entry's OWN id, which `createChatPanel` made — not against the tab key,
  // which can move under a live conversation. That distinction cost a whole code round.
  threads.set(entry.id, {
    session,
    home: first.home,
    passage: state.passage,
    models: ready.models,
    modelId: ready.modelId,
    running: false,
    // Counted from 1 by the first turn, so 0 is "this conversation has not asked anything yet" and
    // can never be mistaken for a turn a stop could name.
    turn: 0,
    ...memoryOf(ready.vendor),
    carry: [],
    messages: [],
    turns: Promise.resolve(),
    saveId,
    title: state.title,
    reopen: false,
  });

  return entry;
}

/**
 * Everything a chat page can ask of the host, for a conversation that is opened OR restored.
 *
 * <p>One object built in one place. Both paths create a panel, and a second copy of these six
 * callbacks would be six chances for a restored tab to stop stopping turns, or to leak a session on
 * close, the day one of them changes.</p>
 */
function conversationHooks(panels: ChatPanels): Parameters<typeof createChatPanel>[2] {
  return {
      onSend: (id, text) => {
        const found = panels.entryOf(id);
        if (found !== undefined) {
          void ask(found, text);
        }
      },
      onPick: (id, modelId) => {
        const found = panels.entryOf(id);
        if (found !== undefined) {
          switchModel(found, modelId);
        }
      },
      onStop: (id, turn) => {
        const found = panels.entryOf(id);
        const thread = threads.get(id);
        if (found === undefined || thread?.running !== true) {
          // Nothing is running, or the tab is already gone. A stop is a message about a turn, and
          // there is no turn — killing the process for it would cost the conversation for a keypress
          // that arrived too late to mean anything.
          return;
        }
        if (turn !== thread.turn) {
          // The page named a turn that is no longer the running one, which is what a late or a
          // repeated press looks like from here. Refused rather than applied to whatever happens to
          // be in flight now — that turn is a different question the person has not asked to stop.
          // There is no wildcard to fall back to: the bridge already refuses a stop that names no
          // turn, so `turn` here is always a real number a page chose. (codex and gemini, the code
          // round, on both halves of this rule.)
          return;
        }
        thread.session.stop();
      },
      onClosed: (id) => {
        // The registry disposes the session the ENTRY was created with, which after a model switch
        // is no longer the one that is running. So the thread's own current session is ended here
        // too — disposal is idempotent, and the alternative is an authenticated child nobody owns.
        const thread = threads.get(id);
        panels.closeById(id);
        // Forgotten, not merely disposed. A remote turn can be answered a poll after the tab went
        // away, and `show` would then post state into a webview VS Code has torn down — a throw out
        // of a callback nobody catches. Every reader of a thread starts by looking it up, so
        // removing it turns all of them into no-ops at once. (codex and gemini, the code round.)
        threads.delete(id);
        thread?.session.dispose();
        thread?.home.release();
      },
      onRestart: () => undefined,
      onUseLocal: () => undefined,
      onPageError: (_id, message) => {
        void vscode.window.showWarningMessage(`The chat page reported: ${message}`);
      },
  };
}

/**
 * Bring one conversation back into a panel VS Code has just restored.
 *
 * <p>No process is started. The transcript is rendered, the composer works, and the session is opened
 * by the first question — see `reopened`. The panel is built by `createChatPanel` like every other
 * one, so the icon, the message wiring, the zoom hook and the disposal are the same code and cannot
 * drift apart.</p>
 */
export function restoreConversation(
  panels: ChatPanels,
  panel: vscode.WebviewPanel,
  saved: SavedTab,
  extensionUri: vscode.Uri,
): void {
  const config = vscode.workspace.getConfiguration('coai');
  const ready = readyToChat(config, saved.modelId);
  // A dead session, and the page can never reach it: `reopened` replaces it before the first turn is
  // sent. It answers rather than throws, because a `ChatSession` that rejects is a contract this
  // codebase does not have — every failure here is a sentence.
  const closed: ChatSession = {
    send: () => Promise.resolve({ ok: false, failure: reloadedNote(saved.modelId) }),
    stop: () => undefined,
    dispose: () => undefined,
  };
  const entry = createChatPanel(
    {
      id: saved.id,
      title: saved.title,
      passage: saved.passage,
      messages: saved.messages,
      models: ready.ok ? ready.models : [],
      modelId: saved.modelId,
      running: false,
      capped: false,
      failure: ready.ok ? reloadedNote(saved.modelId) : ready.refusal,
      draft: '',
      uiScale: chatUiScale(),
    },
    closed,
    conversationHooks(panels),
    extensionUri,
    panel,
  );
  threads.set(entry.id, {
    session: closed,
    home: { dir: '', release: () => undefined },
    passage: saved.passage,
    models: ready.ok ? ready.models : [],
    modelId: saved.modelId,
    running: false,
    turn: 0,
    // Replaced by `reopened` with the rules of whatever model actually answers — this conversation
    // asks nothing until then, so neither field is consulted before it is right. Written out rather
    // than taken from `memoryOf`, which needs a vendor, and a restored tab has none yet.
    forgetful: false,
    asked: 0,
    // The whole transcript, ready to travel with the first question — the same handover a model
    // switch performs, and the reason nothing has to resume a vendor thread.
    carry: [...saved.messages],
    messages: [...saved.messages],
    turns: Promise.resolve(),
    saveId: saved.id,
    title: saved.title,
    reopen: true,
  });
  // Its own key: the tab this conversation was opened FROM may be gone, may be a different object,
  // or may already hold a live conversation of its own. A restored tab is its own thing until
  // somebody closes it.
  panels.open({}, saved.title, () => entry);
}

/**
 * The command.
 *
 * @param panels the registry of open conversations
 * @param args what VS Code handed it — a menu item passes the webview, a keybinding passes nothing
 */
export async function chatWithOtherAi(
  panels: ChatPanels,
  extensionUri: vscode.Uri,
  args: readonly unknown[],
): Promise<void> {
  const config = vscode.workspace.getConfiguration('coai');
  const settings = chatSettingsFrom((key) => config.get(key));
  const ready = readyToChat(config, settings.model);
  if (!ready.ok) {
    void vscode.window.showWarningMessage(ready.refusal);

    return;
  }

  // ASKED FIRST, before anything expensive or anything that touches what belongs to the person.
  // The keybinding is scoped to the assistant panel, but the command palette is not: invoked from
  // the wrong tab this used to spend 1.7 seconds, borrow the clipboard and synthesise a keystroke,
  // and only then say it was the wrong tab. (gemini, the second code round.)
  const match = matchedSession(panels);
  if (match === undefined) {
    void vscode.window.showWarningMessage(
      'Open this from a Claude Code session tab — the conversation is named after it.',
    );

    return;
  }
  if (match.kind === 'rekey') {
    panels.rekey(match.from, match.key);
  }

  const plan = triggerPlan(args, settings.autoSend);
  const passage = await passageFor(plan.path);
  if (passage.text.trim().length === 0) {
    void vscode.window.showWarningMessage(
      passage.failure.length > 0 ? passage.failure : 'Nothing to explain — copy the text first.',
    );

    return;
  }

  // Resolved BEFORE a tab exists, because `spawn` does not search PATHEXT: a bare `codex` on
  // Windows means `codex.cmd`, and spawning the bare name fails with ENOENT at the first turn —
  // deep inside a conversation, where it reads as the model refusing rather than as a CLI that is
  // not installed. Said here instead, in a sentence somebody can act on.
  const cli = await cliFor(ready.vendor);
  if (cli.refusal.length > 0) {
    void vscode.window.showWarningMessage(cli.refusal);

    return;
  }
  const remote = isRemote(ready.vendor) ? await remoteFor(ready.vendor) : { session: undefined, refusal: '' };
  if (remote.refusal.length > 0) {
    void vscode.window.showWarningMessage(remote.refusal);

    return;
  }

  const turn = openingTurn(settings.prompt, settings.language, passage.text);
  // A factory, not a value: nothing is built — no process, no temp directory — for a tab that
  // already holds a conversation.
  const opened = panels.open(match.key, match.label, () => newConversation(panels, ready, {
    title: match.label,
    passage: passage.text,
    draft: plan.send ? '' : turn,
  }, cli.resolved, remote.session, extensionUri));

  opened.entry.panel.reveal();
  if (plan.send) {
    await ask(opened.entry, turn);

    return;
  }
  if (opened.outcome === 'revealed') {
    // A new panel opened with the draft already in its composer; an open one has to be told.
    pushChatDraft(opened.entry, turn);
  }
}
