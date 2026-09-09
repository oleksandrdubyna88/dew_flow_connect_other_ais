import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { PageMessage, chatCommandOf } from './chatMessages';
import { ChatEntry, DisposableSession, RevealablePanel } from './chatPanels';
import {
  ChatMessage,
  ChatModelChoice,
  ChatPageState,
  chatCappedHtml,
  chatMessagesHtml,
  chatPageHtml,
  chatPickerHtml,
  chatThinkingHtml,
} from './chatPage';
import { escapeHtml } from './webviewHtml';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';

/**
 * The `vscode` half of a conversation tab, and deliberately the thin one.
 *
 * <p>Everything that can be decided without a host lives elsewhere: which tab a conversation belongs
 * to is `sessionKey.ts`, which conversations exist is `chatPanels.ts`, what a page message means is
 * `chatMessages.ts`, and what the page looks like is `chatPage.ts`. What is left here is the part
 * that can only be done against the real API — creating the panel, carrying its messages, and
 * pushing state into it — which is also the part no unit test can reach. Keeping it small is how the
 * untestable surface stays small.</p>
 *
 * <p><b>Hooks are called with the conversation's `id`, never with the tab key.</b> The first version
 * closed over the key it was created with, and a `rekey` — which happens when the host replaces a
 * live tab's object — left every message from that page looking itself up under a key nobody held:
 * sends silently dropped, and a close that could not find its entry leaving the vendor process
 * running. The id is created here, once, and outlives every key the tab wears. (gemini and codex,
 * the code round, independently.)</p>
 *
 * <p><b>Everything assigned to `innerHTML` in the page is escaped HERE, where it is built.</b> The
 * page never escapes anything it is handed; it renders what the host produced. That is the invariant
 * a reader should carry into `chatPage.ts`.</p>
 */

/** What the panel asks the extension to do. Every hook is passed the conversation's stable id. */
export interface ChatPanelHooks {
  /** The person pressed send. */
  readonly onSend: (id: object, text: string) => void;
  /** The person chose a different model — already checked against the offered list. */
  readonly onPick: (id: object, modelId: string) => void;
  /** VS Code closed the tab — the registry must forget it and end its session. */
  readonly onClosed: (id: object) => void;
  /** Start again with the same passage. */
  readonly onRestart: (id: object) => void;
  /** Move the thread to a model that keeps a conversation. */
  readonly onUseLocal: (id: object) => void;
  /** The page trapped an error, or the host failed to handle one of its messages. */
  readonly onPageError: (id: object, message: string) => void;
}

/** Everything the page shows that can change after it is open. */
export interface ChatPushState {
  readonly messages: readonly ChatMessage[];
  readonly running: boolean;
  readonly capped: boolean;
  readonly failure: string;
  readonly models: readonly ChatModelChoice[];
  readonly modelId: string;
  /**
   * How many turns are ahead of this one on a Team server, or 0 for none and for a local model.
   *
   * <p>Not optional. A push that says nothing about the queue would leave the last number on screen
   * while the turn is being answered — and this state is deduplicated by its serialisation, so a
   * stale position would be pushed exactly once and then stick.</p>
   */
  readonly queued: number;
}

/**
 * Open a tab for one conversation.
 *
 * <p>Returns the registry's entry rather than the panel: the caller's next act is to put it in the
 * map, and handing back a `WebviewPanel` would invite a second place that knows how to dispose one.</p>
 *
 * @param known whether a model id the page asks for is one this conversation was actually offered
 */
export function createChatPanel(
  state: ChatPageState,
  session: DisposableSession,
  hooks: ChatPanelHooks,
): ChatEntry {
  // The conversation's own identity, created once and never replaced. See the module comment.
  const id = { conversation: state.title };
  // What this conversation is allowed to be answered by, kept from the state it was opened with and
  // replaced by every push. A separate predicate handed in at creation was the alternative, and it
  // would have drifted the moment a Team server's catalog arrived: two places deciding what a valid
  // model is, one of them frozen. (gemini, the second code round.)
  offered.set(id, new Set(state.models.map((model) => model.id)));

  const panel = vscode.window.createWebviewPanel(
    'coaiChat',
    state.title,
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] },
  );
  panel.webview.html = chatPageHtml(state, crypto.randomBytes(16).toString('hex'));

  const scale = pushUiScaleTo(panel.webview);
  panel.webview.onDidReceiveMessage((message: PageMessage) => {
    // A detached boundary: nothing awaits this, so a rejection here would have no owner and the
    // extension host would report it as unhandled instead of the tab saying anything. (codex.)
    void handle(id, message, hooks).catch((reason: unknown) => {
      hooks.onPageError(id, reason instanceof Error ? reason.message : String(reason));
    });
  });
  panel.onDidDispose(() => {
    scale.dispose();
    try {
      hooks.onClosed(id);
    } catch {
      // A throw here escapes into VS Code's disposal loop, which is not ours to break. The session
      // it failed to close is the caller's problem to notice; the editor's is not. (local.)
    }
  });

  const revealable: RevealablePanel = {
    reveal: () => panel.reveal(),
    dispose: () => panel.dispose(),
    post: (message) => {
      void panel.webview.postMessage(message);
    },
  };

  return { id, panel: revealable, session };
}

/**
 * One message from the page.
 *
 * <p>The READING of the message is `chatMessages.ts`, which needs no host and is tested; what is
 * left here is the dispatch. That split exists because a wrong message type would otherwise have
 * shipped with every test green — a person's send quietly ignored. (codex, the plan round.)</p>
 */
async function handle(id: object, message: PageMessage, hooks: ChatPanelHooks): Promise<void> {
  const command = chatCommandOf(message);
  switch (command.kind) {
    case 'zoom':
      await applyZoomDelta(command.delta);

      return;
    case 'send':
      hooks.onSend(id, command.text);

      return;
    case 'pick':
      // A page can post any id it likes — a stale retained webview certainly will, and a tampered
      // one might. Choosing a model is choosing who gets paid, so the host checks the name against
      // what this conversation was actually offered rather than trusting the page. (codex.)
      if (offered.get(id)?.has(command.id) === true) {
        hooks.onPick(id, command.id);
      } else {
        hooks.onPageError(id, `that model is not one this conversation offers: ${command.id}`);
      }

      return;
    case 'restart':
      hooks.onRestart(id);

      return;
    case 'useLocal':
      hooks.onUseLocal(id);

      return;
    case 'pageError':
      hooks.onPageError(id, command.message);

      return;
    default:
      // Ignored on purpose — see the exception documented in `chatMessages.ts`.
      return;
  }
}

/**
 * What was last pushed to a conversation, so an unchanged state is not pushed again.
 *
 * <p>Keyed by the conversation's id and weak, so a closed tab's record goes with it. The page
 * replaces its whole message region on every push, and pushing an identical one costs a re-render
 * that drops the reader's text selection for nothing. The same shape `roundsLogPanel.ts` uses.</p>
 */
const lastPushed = new WeakMap<object, string>();

/** Which models each conversation currently offers — the one source the pick check reads. */
const offered = new WeakMap<object, Set<string>>();

/**
 * Push what changed into an open page.
 *
 * <p>Regions, not a re-render. Re-assigning `webview.html` would rebuild the page and throw away the
 * half-typed follow-up in the composer — which is exactly what a person does while waiting nine
 * seconds for an answer.</p>
 *
 * <p>It carries the MODEL as well as the messages. After a capped thread takes "continue with a
 * local model" the host switches models, and a page that was never told would keep showing the
 * remote one as selected while the answers came from somewhere else — misleading about behaviour,
 * latency and cost, all three. (gemini and codex, the code round.)</p>
 *
 * @returns whether anything was actually sent
 */
export function pushChatState(entry: ChatEntry, state: ChatPushState): boolean {
  const payload = {
    type: 'state',
    messagesHtml: chatMessagesHtml(state.messages),
    running: state.running,
    capped: state.capped,
    thinkingHtml: chatThinkingHtml(state.running, state.queued),
    cappedHtml: chatCappedHtml(state.capped),
    failureHtml: state.failure.length === 0 ? '' : `<div class="failure">${escapeHtml(state.failure)}</div>`,
    pickerHtml: chatPickerHtml(state.models, state.modelId),
    modelId: state.modelId,
  };

  offered.set(entry.id, new Set(state.models.map((model) => model.id)));

  const serialised = JSON.stringify(payload);
  if (lastPushed.get(entry.id) === serialised) {
    return false;
  }
  lastPushed.set(entry.id, serialised);
  entry.panel.post(payload);

  return true;
}

/**
 * Put text into an open tab’s composer, unsent.
 *
 * <p>The menu path’s whole shape: the passage arrives, the person reads it, and one keypress
 * sends it. A separate message from the state push because it must NOT be idempotent — pushing
 * the same state twice should change nothing, while inviting a second passage into the composer
 * is exactly what a second invocation means.</p>
 */
export function pushChatDraft(entry: ChatEntry, draft: string): void {
  entry.panel.post({ type: 'state', draft });
}

/** The scale the page opens at, so a new tab matches the ones already open. */
export function chatUiScale(): number {
  return currentUiScale();
}
