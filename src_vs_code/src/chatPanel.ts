import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { PageMessage, chatCommandOf } from './chatMessages';
import { ChatEntry, DisposableSession, RevealablePanel } from './chatPanels';
import { ChatMessage, ChatPageState, chatCappedHtml, chatMessagesHtml, chatPageHtml } from './chatPage';
import { escapeHtml } from './webviewHtml';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';

/**
 * The `vscode` half of a conversation tab, and deliberately the thin one.
 *
 * <p>Everything that can be decided without a host lives elsewhere: which tab a conversation belongs
 * to is `sessionKey.ts`, which conversations exist is `chatPanels.ts`, and what the page looks like
 * is `chatPage.ts`. What is left here is the part that can only be done against the real API —
 * creating the panel, carrying its messages, and pushing state into it — which is also the part no
 * unit test can reach. Keeping it small is how the untestable surface stays small.</p>
 *
 * <p>The panel is created with `retainContextWhenHidden` for the same reason the rounds log is: what
 * has been typed into the composer and how far the conversation is scrolled are page state, and a
 * page torn down behind another tab loses both every time somebody looks away.</p>
 */

/** What the panel asks the extension to do. Everything else is the page's own business. */
export interface ChatPanelHooks {
  /** The person pressed send. */
  readonly onSend: (key: object, text: string) => void;
  /** The person chose a different model. */
  readonly onPick: (key: object, modelId: string) => void;
  /** VS Code closed the tab — the registry must forget it and end its session. */
  readonly onClosed: (key: object) => void;
  /** Start again with the same passage. */
  readonly onRestart: (key: object) => void;
  /** Move the thread to a model that keeps a conversation. */
  readonly onUseLocal: (key: object) => void;
  /** The page trapped an error and said so. */
  readonly onPageError: (key: object, message: string) => void;
}

/**
 * Open a tab for one conversation.
 *
 * <p>Returns the registry's entry rather than the panel: the caller's next act is to put it in the
 * map, and handing back a `WebviewPanel` would invite a second place that knows how to dispose one.</p>
 */
export function createChatPanel(
  key: object,
  state: ChatPageState,
  session: DisposableSession,
  hooks: ChatPanelHooks,
): ChatEntry {
  const panel = vscode.window.createWebviewPanel(
    'coaiChat',
    state.title,
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] },
  );
  panel.webview.html = chatPageHtml(state, crypto.randomBytes(16).toString('hex'));

  const scale = pushUiScaleTo(panel.webview);
  panel.webview.onDidReceiveMessage((message: PageMessage) => {
    void handle(key, message, hooks);
  });
  panel.onDidDispose(() => {
    scale.dispose();
    hooks.onClosed(key);
  });

  const revealable: RevealablePanel = {
    reveal: () => panel.reveal(),
    dispose: () => panel.dispose(),
    post: (message) => {
      void panel.webview.postMessage(message);
    },
  };

  return { panel: revealable, session, label: state.title };
}

/**
 * One message from the page.
 *
 * <p>The READING of the message is `chatMessages.ts`, which needs no host and is tested; what is
 * left here is the dispatch. That split exists because a wrong message type would otherwise have
 * shipped with every test green — a person’s send quietly ignored. (codex, the plan round.)</p>
 */
async function handle(key: object, message: PageMessage, hooks: ChatPanelHooks): Promise<void> {
  const command = chatCommandOf(message);
  switch (command.kind) {
    case 'zoom':
      await applyZoomDelta(command.delta);

      return;
    case 'send':
      hooks.onSend(key, command.text);

      return;
    case 'pick':
      hooks.onPick(key, command.id);

      return;
    case 'restart':
      hooks.onRestart(key);

      return;
    case 'useLocal':
      hooks.onUseLocal(key);

      return;
    case 'pageError':
      hooks.onPageError(key, command.message);

      return;
    default:
      // Ignored on purpose: a webview retained across a reload can be older than the extension that
      // talks to it, and the older half must not break because the newer one learned a word.
      return;
  }
}

/**
 * Push what changed into an open page.
 *
 * <p>Regions, not a re-render. Re-assigning `webview.html` would rebuild the page and throw away the
 * half-typed follow-up in the composer — which is exactly what a person does while waiting nine
 * seconds for an answer.</p>
 */
export function pushChatState(
  entry: ChatEntry,
  messages: readonly ChatMessage[],
  running: boolean,
  failure: string,
  capped = false,
): void {
  entry.panel.post({
    type: 'state',
    messagesHtml: chatMessagesHtml(messages),
    running,
    capped,
    cappedHtml: chatCappedHtml(capped),
    failureHtml: failure.length === 0 ? '' : `<div class="failure">${escapeHtml(failure)}</div>`,
  });
}

/** The scale the page opens at, so a new tab matches the ones already open. */
export function chatUiScale(): number {
  return currentUiScale();
}
