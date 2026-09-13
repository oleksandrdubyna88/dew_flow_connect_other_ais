import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { restoreConversation } from './chatCommand';
import { chatTabIcon } from './chatIcon';
import { ChatPanels } from './chatPanels';
import {
  MIGRATION_WAIT_MS,
  RESTORE_RETRY,
  RestoreDecision,
  noticeHtml,
  persistedId,
  restoreDecision,
  restoringHtml,
} from './chatRestore';
import { ChatStoreFile } from './chatStoreFile';
import { ChatTabMemory } from './chatTabs';

/**
 * The `vscode` half of bringing a chat tab back: validate what the reload handed over, put something
 * on screen, wait for the migration under a ceiling, read the store, ask `chatRestore.ts` what to do,
 * and do it. Its own module because `extension.ts` is close to the file-size limit and
 * `chatCommand.ts` is far past it, and because a retry needs a message listener the serializer's
 * closure is the wrong place to own.
 *
 * <p>A restored conversation goes through `restoreConversation` and therefore through
 * `createChatPanel`, the one place a chat panel is built — the icon, the wiring and the disposal come
 * from there. A NOTICE tab is not a conversation: it is one page, one button and one listener, and
 * it wears the same icon so a tab kept over a disk fault does not look like a stranger's.</p>
 *
 * <p><b>Two things dispose a panel here, and both mean "nowhere".</b> A state that carries no id this
 * build could file — nothing is on disk under it, and the migration quarantines rather than carries
 * such a memento entry — and a conversation the store AND the memento both say is absent. Nothing
 * else: not a permissions error, not a newer build's record, not a defect (the outer catch in
 * `extension.ts` logs and leaves the tab).</p>
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
 * The serializer's whole body, for one panel.
 *
 * <p>Order is the point. The id is validated FIRST, at the boundary, and refused with the legal shape
 * named. Then the tab is DRAWN — *Restoring…*, with the id already `setState`d — before anything is
 * awaited, so a person reloading with ten tabs sees ten tabs saying what they are doing rather than
 * ten blank panels. Then the migration is waited for, but only up to {@link MIGRATION_WAIT_MS}: past
 * that the tab proceeds with what is readable, which the memento fallback in `restoreDecision` makes
 * safe. Rejects only on a defect — the store answers in outcomes — and the caller's catch is the
 * outer edge.</p>
 */
export async function restoreAfterReload(
  deps: RestoreDeps,
  panel: vscode.WebviewPanel,
  state: unknown,
  migration: Promise<unknown>,
): Promise<void> {
  const id = persistedId(state);
  if (id.length === 0) {
    console.warn(
      'ConnectOtherAIs: a restored chat tab carried no usable conversation id — an id is a string of letters, '
      + `digits, dash and underscore — so nothing can be filed under it and the tab is closed: ${JSON.stringify(state)}`,
    );
    panel.dispose();

    return;
  }
  // WHETHER THE TAB IS STILL THERE, recorded before anything is awaited. The wait below is up to five
  // seconds and a person can close the tab inside it; the restore then went on into a panel that no
  // longer existed, and both arms of it assign `panel.webview.html`, which throws on a disposed panel
  // — into the serializer's handler. Nothing leaks on this path (`restoreConversation` registers the
  // panel only after `createChatPanel` returns); it is the throw. So the decision is applied only to
  // a panel that is still open: checked after the wait, and again after the read that follows it.
  // (CodeRabbit, PR #223.)
  let disposed = false;
  const closing = panel.onDidDispose(() => { disposed = true; });
  const open = (): boolean => !disposed;
  try {
    draw(deps, panel, restoringHtml(id, nonce()));
    await withinCeiling(migration, MIGRATION_WAIT_MS);
    if (!open()) {
      return;
    }
    await restoreChatTab(deps, panel, id, open);
  } finally {
    closing.dispose();
  }
}

/**
 * One tab, decided and done — the half a retry runs again.
 *
 * @param open whether the panel is still there once the store has answered; the decision is applied
 *   only then. The retry leaves it at its default — a press is proof enough that the tab is open.
 */
export async function restoreChatTab(
  deps: RestoreDeps,
  panel: vscode.WebviewPanel,
  id: string,
  open: () => boolean = () => true,
): Promise<void> {
  const decision = restoreDecision(await deps.store.read(id), deps.memento.saved(id), deps.workspace());
  if (!open()) {
    return;
  }
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
 * has not redraws the notice with what it said this time, which also gives the button back.
 */
function showNotice(
  deps: RestoreDeps,
  panel: vscode.WebviewPanel,
  id: string,
  notice: Extract<RestoreDecision, { kind: 'notice' }>,
): void {
  draw(deps, panel, noticeHtml(id, notice, nonce()));
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

/**
 * A page into the panel, configured the way `createChatPanel` configures a restored one: the
 * webview half of the options is settable after a reload and the page has a script, and the icon
 * is workbench chrome the reload does not bring back.
 */
function draw(deps: RestoreDeps, panel: vscode.WebviewPanel, html: string): void {
  panel.webview.options = { enableScripts: true, localResourceRoots: [] };
  panel.iconPath = chatTabIcon((...segments) => vscode.Uri.joinPath(deps.extensionUri, ...segments));
  panel.webview.html = html;
}

/**
 * Wait for `work`, but not past `ms`. Neither outcome is an error: the migration never rejects (its
 * caller catches), and a ceiling reached is the tab proceeding with what is readable. The timer is
 * cleared when the work wins, so a fast migration leaves nothing ticking.
 */
async function withinCeiling(work: Promise<unknown>, ms: number): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const ceiling = new Promise<void>((resolve) => {
    timer = setTimeout(resolve, ms);
  });
  try {
    await Promise.race([work.then(() => undefined, () => undefined), ceiling]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

const nonce = (): string => randomBytes(16).toString('hex');
