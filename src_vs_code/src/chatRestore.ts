import { ConversationRecord, fromLegacy, isSafeId } from './chatStore';
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
 * can run before the migration has finished — the host waits for it, up to {@link MIGRATION_WAIT_MS}
 * — and a migration that could not clear the key (a store that would not take a write) leaves
 * records there; a conversation not yet carried across must not read as `absent` and be disposed. A
 * record found only in the memento is restored with baseline 0, which is the state the write
 * decision's adopt rule exists for: its first save meets whatever the store holds under that id and
 * either takes it over or forks. Only when the store says absent AND the memento holds nothing is
 * the panel disposed — as before, because an empty tab pretending to be a conversation is the thing
 * this replaces.</p>
 *
 * <p><b>And a tab is never blank while it waits.</b> Four findings from three reviewers, one defect:
 * a person reloading with ten tabs must never see ten empty panels with no explanation for as long
 * as a migration takes. {@link restoringHtml} is drawn BEFORE anything is awaited, and the wait has
 * a ceiling; past it the host proceeds with what is readable, which the memento fallback makes safe
 * — a record the migration has not written yet is found in the memento, and its first save adopts
 * the store's copy when the migration lands it.</p>
 *
 * <p>Pure: no `vscode`, no disk. The host half (`chatRestorePanel.ts`) reads the store, hands the
 * answer here, and does what it is told.</p>
 */

/** What the serializer does with one tab. */
export type RestoreDecision =
  | { readonly kind: 'restore'; readonly record: ConversationRecord }
  | { readonly kind: 'dispose' }
  | { readonly kind: 'notice'; readonly sentence: string; readonly retry: boolean };

/**
 * How long a restored tab waits for the migration before it proceeds with what is readable.
 *
 * <p>Twenty records on an ordinary disk are a fraction of a second; twenty records each meeting a
 * lock another window holds are twenty waits of the lock's own bound (~120 ms), under three seconds.
 * Five is past both with room, and short enough that a tab which shows *Restoring…* for that long is
 * a tab a person is still willing to wait for. Past it, nothing is lost: the memento fallback finds
 * what the migration has not yet written.</p>
 */
export const MIGRATION_WAIT_MS = 5_000;

/** What the page posts when the person presses the one button a notice tab has. */
export const RESTORE_RETRY = 'retry';

export const RETRY_LABEL = 'Try again';

/** What the button says while a retry is in flight — a disabled button that says nothing reads as broken. */
export const RETRYING_LABEL = 'Retrying…';

/** For a record on disk that this build cannot read. Exported so the test reads it, once. */
export const INCOMPATIBLE_NOTICE =
  'This conversation is on disk, but it was written in a form this build cannot read — most likely by a newer '
  + 'version of ConnectOtherAIs. Nothing has been deleted: the file is where it was, and a build that can read it '
  + 'will open it in this tab.';

/** For a disk that would not answer. The reason is the store's person-facing sentence, never a path. */
export const unavailableNotice = (reason: string): string =>
  `This conversation could not be read from disk just now: ${reason}. Nothing has been deleted — the file is where `
  + 'it was, and this tab keeps its place until it can be read.';

/** What a tab says while the migration is still running. */
export const RESTORING_NOTICE =
  'The conversation is being read from disk. After an update this takes a moment longer, while the conversations '
  + 'of earlier versions are carried into the new store.';

/**
 * The conversation id a reload handed back, or empty when there is nothing this build can look up.
 *
 * <p>The state is what the page saved with `setState`, and it is persisted by the workbench — a
 * value this build did not write a moment ago and does not get to trust. An id must be a string of
 * letters, digits, dash and underscore ({@link isSafeId}): that is what `randomUUID()` mints and what
 * the store can file. Anything else names nothing on disk and nothing the migration would carry
 * (an unfilable id is quarantined, not migrated), so the caller closes the tab rather than opening
 * a conversation that could never be saved.</p>
 */
export function persistedId(state: unknown): string {
  const id = (state as { id?: unknown } | null)?.id;

  return typeof id === 'string' && isSafeId(id) ? id : '';
}

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
    case 'unavailable':
      return { kind: 'notice', sentence: unavailableNotice(seen.reason), retry: true };
    default: {
      // EXHAUSTIVE, by name, for the same reason `nextAfterSave` is: a fifth answer added to
      // ReadOutcome used to land here and be drawn as a disk that would not answer, with a retry
      // button — silently. Now it is a compile error, and a value the types forbid that arrives
      // anyway is a defect named out loud. (CodeRabbit, PR #223.)
      const unhandled: never = seen;

      throw new Error(`a store answer this build has no arm for: ${JSON.stringify(unhandled)}`);
    }
  }
}

/** The notice tab's page: what happened, and the one button when one makes sense. */
export function noticeHtml(id: string, notice: Extract<RestoreDecision, { kind: 'notice' }>, nonce: string): string {
  const button = notice.retry
    ? `<p><button id="retry" type="button">${escapeHtml(RETRY_LABEL)}</button></p>`
    : '';

  return page(id, 'This conversation could not be opened', notice.sentence, button, nonce);
}

/** The page a tab shows while the migration is still running — drawn before anything is awaited. */
export function restoringHtml(id: string, nonce: string): string {
  return page(id, 'Restoring this conversation…', RESTORING_NOTICE, '', nonce);
}

/**
 * One page shape for every tab that is not (yet) a conversation.
 *
 * <p>Same CSP shape as the chat page — no inline handlers, one nonce'd script — and the script's
 * first act is `setState({ id })`: the id is what the serializer receives after the NEXT reload, so
 * a tab kept over a disk fault, or reloaded again while restoring, comes back asking for the same
 * conversation. The heading and sentence go through `escapeHtml`, the id and the labels through
 * `jsonForScript`; a sentence carries what a file said.</p>
 */
function page(id: string, heading: string, sentence: string, button: string, nonce: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(heading)}</title>
<style>
body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); padding: 24px; max-width: 640px; line-height: 1.5; }
h1 { font-size: 1.2em; font-weight: 600; margin: 0 0 12px; }
button { font: inherit; padding: 4px 14px; border: 1px solid var(--vscode-button-border, transparent); border-radius: 2px; background: var(--vscode-button-background); color: var(--vscode-button-foreground); cursor: pointer; }
button:hover { background: var(--vscode-button-hoverBackground); }
button[disabled] { opacity: 0.6; cursor: default; }
</style>
</head>
<body>
<h1>${escapeHtml(heading)}</h1>
<p>${escapeHtml(sentence)}</p>
${button}
<script nonce="${nonce}">
const vscode = acquireVsCodeApi();
vscode.setState(${jsonForScript({ id })});
const retry = document.getElementById('retry');
if (retry) {
  retry.addEventListener('click', () => {
    retry.disabled = true;
    retry.textContent = ${jsonForScript(RETRYING_LABEL)};
    vscode.postMessage({ type: ${jsonForScript(RESTORE_RETRY)} });
  });
}
</script>
</body>
</html>`;
}
