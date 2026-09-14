import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';

import { rowsAfter, rowsOf, viewOf } from './phrasesEdit';
import { phraseEdit, phraseRepaints, phrasesHtml, type PhraseCommand } from './phrasesPage';
import { settledWrites } from './settledWrites';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';

/**
 * The phrases tab: one webview, reused while open.
 *
 * <p>Thin on purpose — `phrasesPage.ts` decides what a message MEANS, `phrasesEdit.ts` decides what
 * it does to the rows, and `settledWrites.ts` decides when and in what order it is stored. What is
 * left here is what only a host can do: read a setting, write one, repaint, and tell somebody when a
 * write did not land.</p>
 *
 * <p><b>A save that fails is shown WITHOUT a repaint.</b> That is not a detail: a repaint draws the
 * rows the setting still holds, which — when the write was rejected — are exactly the rows without
 * the words the person just typed. So the failure travels to the page as a message and lands in a
 * banner, and the box keeps what they wrote. (Gate findings from both stages, codex and local.)</p>
 */

const SECTION = 'coai';
const KEY = 'phrases';

let panel: vscode.WebviewPanel | undefined;

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

/** Read fresh on every command, so a write is never based on a list read minutes ago. */
function stored(): ReturnType<typeof rowsOf> {
  return rowsOf(config().get(KEY));
}

async function apply(command: PhraseCommand): Promise<boolean> {
  if (command.kind === 'zoom') {
    await applyZoomDelta(command.delta);

    return false;
  }
  const outcome = rowsAfter(stored(), command);
  if (outcome.kind === 'unchanged') {
    return false;
  }
  await config().update(KEY, outcome.rows, vscode.ConfigurationTarget.Global);
  // A write that lands takes the failure line away. Without this the banner had no path back: once
  // the settings file became writable again, the page went on saying nothing was being saved, which
  // is worse than never having said it.
  void panel?.webview.postMessage({ type: 'saveOk' });

  return phraseRepaints(command);
}

function render(): void {
  if (panel === undefined) {
    return;
  }
  panel.webview.html = phrasesHtml(
    { rows: viewOf(stored()), uiScale: currentUiScale() },
    randomBytes(16).toString('hex'),
  );
}

/**
 * A write that did not land, said to the person who made it.
 *
 * <p>To the PAGE rather than as a notification, because the banner sits beside the box that still
 * holds the words: a toast names the problem somewhere else on the screen and is gone in seconds.
 * The console line is for the log; the sentence is for them.</p>
 */
function saveFailed(error: unknown): void {
  console.error('[coai] phrases tab: a phrase could not be saved', error);
  const said = 'That change could not be saved — your settings file may be read-only or held by another program.';
  if (panel === undefined) {
    // The tab has already gone: this is the flush on dispose, and the banner it would have written
    // to went with it. A notification is the only surface left, and silence here would mean a phrase
    // somebody typed and then closed the tab on vanished without a word. (Code round, codex.)
    void vscode.window.showErrorMessage(`${said} The phrase you were writing was not stored.`);

    return;
  }
  void panel.webview.postMessage({
    type: 'saveFailed',
    text: `${said} What you typed is still here; try again once it is writable.`,
  });
}

const writes = settledWrites<PhraseCommand>({
  apply,
  render,
  report: saveFailed,
  // Typing settles; a click does not, because there is no caret for a redraw to disturb and making
  // somebody wait to see a row appear would be a delay bought for nothing.
  fieldOf: (command) => (command.kind === 'edit' ? `${command.id}/${command.field}` : undefined),
});

export function openPhrases(): void {
  if (panel !== undefined) {
    panel.reveal();

    return;
  }
  panel = vscode.window.createWebviewPanel(
    'coaiPhrases',
    'Phrases',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const scale = pushUiScaleTo(panel.webview);
  panel.webview.onDidReceiveMessage((message: unknown) => { writes.queue(phraseEdit(message)); });
  panel.onDidDispose(() => {
    scale.dispose();
    panel = undefined;
    // Whatever is still settling belongs to somebody who typed it. Losing it because they closed the
    // tab would be the one data loss this page can cause.
    void writes.flush();
  });
  render();
}
