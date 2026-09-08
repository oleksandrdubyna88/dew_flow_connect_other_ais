import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { ChatSession } from './chatSession';
import { ChatMessage, ChatModelChoice } from './chatPage';
import { CliChatSession } from './cliChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import { chatChoice, chatModelsFrom } from './chatModels';
import { chatSettingsFrom } from './chatSettings';
import { chatUiScale, createChatPanel, pushChatDraft, pushChatState } from './chatPanel';
import { captureSelection, COPY_SCRIPT, argvFor, ran } from './selectionCapture';
import { ChatHome, chatHome, chatRuntimeRefusal, launchSpecFor } from './cliChatLaunch';
import { launch } from './processLauncher';
import { openingTurn } from './chatPrompt';
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

/** What a conversation is, beyond its process. Keyed by the entry's own id, weak so it dies with it. */
interface Thread {
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
  /** The next answer comes from a process that never heard the earlier turns. Said once, then off. */
  restarted: boolean;
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
}

const threads = new WeakMap<object, Thread>();

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

/** Push the page's whole visible state, with the thread's own model list rather than an empty one. */
function show(entry: ChatEntry, running: boolean, failure: string): void {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  pushChatState(entry, {
    messages: thread.messages,
    running,
    capped: false,
    failure,
    models: thread.models,
    modelId: thread.modelId,
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
      show(entry, false, `the turn failed unexpectedly: ${asText(reason)}`);
    });
  thread.turns = mine;

  return mine;
}

/** A thrown thing, as a sentence. */
function asText(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
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
  thread.messages = [...thread.messages, { role: 'you', text }];
  show(entry, true, '');

  const result = await thread.session.send(text);
  if (!result.ok) {
    show(entry, false, result.failure);

    return;
  }

  // A restart is said in the transcript, not only in a flag nobody sees: the answer genuinely does
  // not remember the earlier turns, and a reader comparing it with them deserves to know why. A
  // model the PERSON switched to is the same situation arrived at deliberately, and says the same.
  const restarted = result.contextLost === true || thread.restarted;
  thread.restarted = false;
  const answer = restarted
    ? `(the conversation restarted — this answer does not remember the earlier ones)\n\n${result.answer}`
    : result.answer;
  thread.messages = [...thread.messages, { role: 'model', text: answer }];
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

/** A vendor process in an empty directory of its own, and the directory, held together. */
function started(vendor: Vendor): { session: ChatSession; home: ChatHome } {
  const home: ChatHome = emptyTempDir();
  const spec = launchSpecFor(vendor, home.dir);

  return {
    session: new CliChatSession(() => launch(spec.executable, spec.args, { cwd: spec.cwd }), DEFAULT_BUDGETS),
    home,
  };
}

/** The vendor row behind a model id, read fresh — the person may have edited settings since. */
function vendorFor(modelId: string): Vendor | undefined {
  return vendorsFrom(vscode.workspace.getConfiguration('coai').get('vendors')).find((row) => row.id === modelId);
}

/**
 * The person chose a different model in the open tab.
 *
 * <p>A conversation is a process, so this is a new process: the old one is ended, its directory
 * goes with it, and the next answer says the conversation restarted — the same sentence a death
 * gets, because it is the same fact. The alternative was a picker that changed a caption and
 * nothing else, which is a control that lies. (gemini, the second code round.)</p>
 */
function switchModel(entry: ChatEntry, modelId: string): void {
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

  thread.session.dispose();
  thread.home.release();
  const replacement = started(vendor);
  thread.session = replacement.session;
  thread.home = replacement.home;
  thread.modelId = modelId;
  thread.restarted = true;
  show(entry, false, '');
}

/** Everything one new conversation is made of. Called ONLY when a tab has no panel yet. */
function newConversation(
  panels: ChatPanels,
  ready: Extract<Ready, { ok: true }>,
  state: { readonly title: string; readonly passage: string; readonly draft: string },
): ChatEntry {
  const first = started(ready.vendor);
  const session = first.session;
  const entry = createChatPanel(
    {
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
    {
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
      onClosed: (id) => {
        // The registry disposes the session the ENTRY was created with, which after a model switch
        // is no longer the one that is running. So the thread's own current session is ended here
        // too — disposal is idempotent, and the alternative is an authenticated child nobody owns.
        const thread = threads.get(id);
        panels.closeById(id);
        thread?.session.dispose();
        thread?.home.release();
      },
      onRestart: () => undefined,
      onUseLocal: () => undefined,
      onPageError: (_id, message) => {
        void vscode.window.showWarningMessage(`The chat page reported: ${message}`);
      },
    },
  );
  // Recorded against the entry's OWN id, which `createChatPanel` made — not against the tab key,
  // which can move under a live conversation. That distinction cost a whole code round.
  threads.set(entry.id, {
    session,
    home: first.home,
    passage: state.passage,
    models: ready.models,
    modelId: ready.modelId,
    restarted: false,
    messages: [],
    turns: Promise.resolve(),
  });

  return entry;
}

/**
 * The command.
 *
 * @param panels the registry of open conversations
 * @param args what VS Code handed it — a menu item passes the webview, a keybinding passes nothing
 */
export async function chatWithOtherAi(panels: ChatPanels, args: readonly unknown[]): Promise<void> {
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

  const turn = openingTurn(settings.prompt, settings.language, passage.text);
  // A factory, not a value: nothing is built — no process, no temp directory — for a tab that
  // already holds a conversation.
  const opened = panels.open(match.key, match.label, () => newConversation(panels, ready, {
    title: match.label,
    passage: passage.text,
    draft: plan.send ? '' : turn,
  }));

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
