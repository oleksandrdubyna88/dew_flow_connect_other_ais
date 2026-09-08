import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { ChatSession } from './chatSession';
import { ChatMessage, ChatModelChoice } from './chatPage';
import { CliChatSession } from './cliChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import { chatModelsFrom, chosenModel } from './chatModels';
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
   * The conversation itself.
   *
   * <p>Held here rather than widened into `ChatPanels`: the registry was written to need only
   * that a session can be ENDED, which is what makes its tests a dozen lines of fakes. Asking it
   * to know how a turn is sent would buy nothing and cost that.</p>
   */
  readonly session: ChatSession;
  readonly passage: string;
  readonly models: readonly ChatModelChoice[];
  readonly modelId: string;
  messages: readonly ChatMessage[];
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
 * Ask, and put the answer on the page.
 *
 * <p>The question is appended BEFORE the turn is sent, so the page shows it while the model is
 * thinking — nine measured seconds of silence otherwise look like a tab that ignored a keypress.</p>
 */
async function ask(entry: ChatEntry, text: string): Promise<void> {
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
  // not remember the earlier turns, and a reader comparing it with them deserves to know why.
  const answer = result.contextLost === true
    ? `(the conversation restarted — this answer does not remember the earlier ones)\n\n${result.answer}`
    : result.answer;
  thread.messages = [...thread.messages, { role: 'model', text: answer }];
  show(entry, false, '');
}

/** Everything needed to start one conversation, or the sentence saying why it cannot start. */
interface Ready {
  readonly vendor: Vendor;
  readonly models: readonly ChatModelChoice[];
  readonly modelId: string;
  readonly refusal: string;
}

function readyToChat(config: vscode.WorkspaceConfiguration, asked: string): Ready {
  const vendors = vendorsFrom(config.get('vendors'));
  const models = chatModelsFrom(vendors);
  if (models.offered.length === 0) {
    const why = models.refused.map((row) => row.reason).join('; ');

    return {
      vendor: vendors[0] as Vendor,
      models: [],
      modelId: '',
      refusal: why.length > 0
        ? `No model can answer a chat yet: ${why}`
        : 'Enable a reviewer on the antigravity runtime to chat with it.',
    };
  }

  const modelId = chosenModel(models, asked);
  const vendor = vendors.find((row) => row.id === modelId);

  return vendor === undefined
    ? { vendor: vendors[0] as Vendor, models: [], modelId: '', refusal: `The model ${modelId} is no longer configured.` }
    : { vendor, models: models.offered, modelId, refusal: '' };
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
  if (ready.refusal.length > 0) {
    void vscode.window.showWarningMessage(ready.refusal);

    return;
  }

  const plan = triggerPlan(args, settings.autoSend);
  const passage = plan.path === 'menu'
    ? { text: await vscode.env.clipboard.readText(), failure: '' }
    : await captureSelection(pressCopy, {
      // VS Code answers a Thenable, not a Promise; `async` is the cheapest honest bridge.
      read: async () => vscode.env.clipboard.readText(),
      write: async (value: string) => {
        await vscode.env.clipboard.writeText(value);
      },
    });
  if (passage.text.trim().length === 0) {
    void vscode.window.showWarningMessage(
      passage.failure.length > 0 ? passage.failure : 'Nothing to explain — copy the text first.',
    );

    return;
  }

  const { active, all } = snapshots();
  const match = sourceSession(active, all, panels.known());
  if (match === undefined) {
    void vscode.window.showWarningMessage(
      'Open this from a Claude Code session tab — the conversation is named after it.',
    );

    return;
  }
  if (match.kind === 'rekey') {
    panels.rekey(match.from, match.key);
  }

  // Asked BEFORE anything is created, and the directory is made only where a process will actually
  // run in it: pressing this against an already-open tab starts nothing, and used to leave an empty
  // directory in %TEMP% behind every single time.
  const refusal = chatRuntimeRefusal(ready.vendor);
  if (refusal.length > 0) {
    void vscode.window.showWarningMessage(refusal);

    return;
  }

  const turn = openingTurn(settings.prompt, settings.language, passage.text);
  const opened = panels.open(match.key, match.label, () => {
    const home: ChatHome = emptyTempDir();
    const spec = launchSpecFor(ready.vendor, home.dir);
    const session = new CliChatSession(
      () => launch(spec.executable, spec.args, { cwd: spec.cwd }),
      DEFAULT_BUDGETS,
    );
    const entry = createChatPanel(
    {
      title: match.label,
      passage: passage.text,
      messages: [],
      models: ready.models,
      modelId: ready.modelId,
      running: false,
      capped: false,
      failure: '',
      draft: plan.send ? '' : turn,
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
      onPick: () => undefined,
      onClosed: (id) => {
        panels.closeById(id);
        home.release();
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
      passage: passage.text,
      models: ready.models,
      modelId: ready.modelId,
      messages: [],
    });

    return entry;
  });

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
