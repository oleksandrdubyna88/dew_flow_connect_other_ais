import { ConversationRecord, fromLegacy } from './chatStore';
import { ReadOutcome } from './chatStoreFile';
import { SavedTab } from './chatTabs';
import { escapeHtml, jsonForScript } from './webviewHtml';

/**
 * What a reload does about each answer the store gives for a tab VS Code has handed back.
 *
 * <p>The serializer used to look the id up in the memento and dispose the panel when nothing came
 * back. The store answers FOUR ways, which is exactly why story A2 made its read typed: `absent` is
 * the only one that means "no such conversation". `incompatible` is a record this build cannot read
 * — most likely a newer build's, after a downgrade — and `unavailable` is a disk that would not
 * answer. Disposing over either throws a person's tab away over a permissions error or a version
 * skew; leaving the panel undisposed and untouched is a blank tab with no message handlers and no
 * explanation, which is the same silence wearing a different hat. So those two get a DEFINED tab:
 * one that says what happened, keeps the conversation's id so the next reload finds the same record,
 * and offers the one action that makes sense — a retry when the disk did not answer, and for a
 * record this build cannot read, the sentence that names it.</p>
 *
 * <p><b>The memento is still asked, for as long as it holds anything.</b> `deserializeWebviewPanel`
 * can run before the migration has finished — the host awaits the migration first, but a migration
 * that could not clear the key (a store that would not take a write) leaves records there, and a
 * conversation not yet carried across must not read as `absent` and be disposed. A record found only
 * in the memento is restored with baseline 0, which is the state the write decision's adopt rule
 * exists for: its first save meets whatever the store holds under that id and either takes it over
 * or forks. Only when the store says absent AND the memento holds nothing is the panel disposed —
 * as before, because an empty tab pretending to be a conversation is the thing this replaces.</p>
 *
 * <p>Pure: no `vscode`, no disk. The host half (`chatRestorePanel.ts`) reads the store, hands the
 * answer here, and does what it is told.</p>
 */

/** What the serializer does with one tab. */
export type RestoreDecision =
  | { readonly kind: 'restore'; readonly record: ConversationRecord }
  | { readonly kind: 'dispose' }
  | { readonly kind: 'notice'; readonly sentence: string; readonly retry: boolean };

/** What the page posts when the person presses the one button a notice tab has. */
export const RESTORE_RETRY = 'retry';

export const RETRY_LABEL = 'Try again';

/** For a record on disk that this build cannot read. Exported so the test reads it, once. */
export const INCOMPATIBLE_NOTICE =
  'This conversation is on disk, but it was written in a form this build cannot read — most likely by a newer '
  + 'version of ConnectOtherAIs. Nothing has been deleted: the file is where it was, and a build that can read it '
  + 'will open it in this tab.';

/** For a disk that would not answer. The reason is the store's person-facing sentence, never a path. */
export const unavailableNotice = (reason: string): string =>
  `This conversation could not be read from disk just now: ${reason}. Nothing has been deleted — the file is where `
  + 'it was, and this tab keeps its place until it can be read.';

/**
 * The decision, over what the store said and what the memento still holds.
 *
 * @param fallback the memento's record for this id, if the key still carries one
 * @param workspace what a memento-only record is filed under — the same answer the migration gives
 */
export function restoreDecision(seen: ReadOutcome, fallback: SavedTab | undefined, workspace: string): RestoreDecision {
  switch (seen.kind) {
    case 'record':
      return { kind: 'restore', record: seen.record };
    case 'absent':
      // Baseline 0: this record has no disk revision. The first save meets whatever is there and
      // the write decision adopts it or forks — the rule kept for exactly this case.
      return fallback === undefined
        ? { kind: 'dispose' }
        : { kind: 'restore', record: { ...fromLegacy(fallback, workspace), rev: 0 } };
    case 'incompatible':
      return { kind: 'notice', sentence: INCOMPATIBLE_NOTICE, retry: false };
    default:
      return { kind: 'notice', sentence: unavailableNotice(seen.reason), retry: true };
  }
}

/**
 * The notice tab's page.
 *
 * <p>Same CSP shape as the chat page — no inline handlers, one nonce'd script — and the script's
 * first act is `setState({ id })`: the id is what the serializer receives after the NEXT reload, so
 * a tab kept over a disk fault comes back asking for the same conversation once the disk answers.
 * The sentence goes through `escapeHtml`, the id through `jsonForScript`; both come from files a
 * person can edit.</p>
 */
export function noticeHtml(id: string, notice: Extract<RestoreDecision, { kind: 'notice' }>, nonce: string): string {
  const button = notice.retry
    ? `<p><button id="retry" type="button">${escapeHtml(RETRY_LABEL)}</button></p>`
    : '';

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Conversation not opened</title>
<style>
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 24px; max-width: 640px; line-height: 1.5; }
h1 { font-size: 1.2em; font-weight: 600; margin: 0 0 12px; }
button { font: inherit; padding: 4px 14px; border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; background: var(--vscode-button-background); color: var(--vscode-button-foreground); cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); }
button[disabled] { opacity: 0.6; cursor: default; }
</style>
</head>
<body>
<h1>This conversation could not be opened</h1>
<p>${escapeHtml(notice.sentence)}</p>
${button}
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
vscode.setState(${jsonForScript({ id })});
const retry = document.getElementById('retry');
if (retry) {
  retry.addEventListener('click', () => {
    retry.disabled = true;
    vscode.postMessage({ type: ${jsonForScript(RESTORE_RETRY)} });
  });
}
</script>
</body>
</html>`;
}
