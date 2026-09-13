import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { restoreConversation } from './chatCommand';
import { chatTabIcon } from './chatIcon';
import { ChatPanels } from './chatPanels';
import { RESTORE_RETRY, RestoreDecision, noticeHtml, restoreDecision } from './chatRestore';
import { ChatStoreFile } from './chatStoreFile';
import { ChatTabMemory } from './chatTabs';

/**
 * The `vscode` half of bringing a chat tab back: read the store, ask `chatRestore.ts` what to do, and
 * do it. Its own module because `extension.ts` is close to the file-size limit and `chatCommand.ts`
 * is far past it, and because a retry needs a message listener the serializer's closure is the wrong
 * place to own.
 *
 * <p>A restored conversation goes through `restoreConversation` and therefore through
 * `createChatPanel`, the one place a chat panel is built — the icon, the wiring and the disposal come
 * from there. A NOTICE tab is not a conversation: it is one page, one button and one listener, and
 * it wears the same icon so a tab kept over a disk fault does not look like a stranger's.</p>
 */

/** What restoring a tab needs of the host, and no more of it. */
export interface RestoreDeps {
  readonly panels: ChatPanels;
  readonly store: ChatStoreFile;
  /** The memento, asked as a fallback while its key still holds anything. */
  readonly memento: ChatTabMemory;
  readonly extensionUri: vscode.Uri;
  /** Read per call rather than once: a folderless window has none, and the answer is the migration's. */
  readonly workspace: () => string;
}

/**
 * One tab, decided and done. Rejects only on a defect — the store answers in outcomes — so the
 * serializer's own catch is the outer edge.
 */
export async function restoreChatTab(deps: RestoreDeps, panel: vscode.WebviewPanel, id: string): Promise<void> {
  const decision = restoreDecision(await deps.store.read(id), deps.memento.saved(id), deps.workspace());
  if (decision.kind === 'restore') {
    restoreConversation(deps.panels, panel, decision.record, deps.extensionUri);

    return;
  }
  if (decision.kind === 'dispose') {
    // As before this story: a panel with no conversation anywhere is not left as an empty tab
    // pretending to be one.
    panel.dispose();

    return;
  }
  showNotice(deps, panel, id, decision);
}

/**
 * The defined tab for a record that could not be read. The retry, when there is one, runs the whole
 * decision again — a disk that has come back restores the conversation into THIS panel, a disk that
 * has not redraws the notice with what it said this time.
 */
function showNotice(
  deps: RestoreDeps,
  panel: vscode.WebviewPanel,
  id: string,
  notice: Extract<RestoreDecision, { kind: 'notice' }>,
): void {
  // The webview half of the options is settable after a reload and the page has a script; the same
  // two lines `createChatPanel` runs for a restored panel, for the same reason.
  panel.webview.options = { enableScripts: true, localResourceRoots: [] };
  panel.iconPath = chatTabIcon((...segments) => vscode.Uri.joinPath(deps.extensionUri, ...segments));
  panel.webview.html = noticeHtml(id, notice, randomBytes(16).toString('hex'));
  if (!notice.retry) {
    return;
  }
  const listener = panel.webview.onDidReceiveMessage((message: unknown) => {
    if ((message as { type?: unknown } | null)?.type !== RESTORE_RETRY) {
      return;
    }
    // Disposed BEFORE the retry runs: a restore that succeeds wires the page's own listener through
    // `createChatPanel`, and a second listener on the same panel would answer the chat page's
    // messages with a retry nobody asked for.
    listener.dispose();
    restoreChatTab(deps, panel, id).catch((reason: unknown) => {
      // The outer edge of a detached call: the detail on the console, a sentence with no path in
      // it to the page — which is left showing a notice with a live button rather than a dead one.
      console.error('ConnectOtherAIs: a chat tab could not be restored on retry', reason);
      showNotice(deps, panel, id, {
        ...notice,
        sentence: 'The retry did not complete; the reason is on the extension host console. Nothing has been deleted.',
      });
    });
  });
  panel.onDidDispose(() => listener.dispose());
}
