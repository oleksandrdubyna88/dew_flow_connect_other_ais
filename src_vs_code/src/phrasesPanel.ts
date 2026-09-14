import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';

import { rowsAfter, rowsOf, viewOf } from './phrasesEdit';
import { phraseEdit, phraseRepaints, phrasesHtml, type PhraseCommand } from './phrasesPage';
import { settledWrites } from './settledWrites';
import { refusalFor, reportRefusal, saveSetting } from './sideConfig';
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
let context: vscode.ExtensionContext | undefined;

function config(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration(SECTION);
}

/**
 * The extension context, which is what {@link saveSetting} needs to know WHICH SIDE this window is.
 *
 * <p>The roles page's rule, verbatim, for the reason recorded there: every write below happens while
 * the panel is open, the panel can only be opened by {@link openPhrases}, which takes the context,
 * and reaching this with none is a wiring mistake rather than a state worth handling quietly.</p>
 *
 * <p><b>`phrases` is deliberately NOT in `OVERLAID_SETTINGS`</b> — a person's saved sentences belong
 * to the person, not to a side of the machine, which is the decision `phrases.ts` records. So this
 * write goes to `settings.json` on every side today. It goes through `saveSetting` anyway, because
 * the alternative is a second copy of "which layer does this belong in", and that copy is the one
 * that will not be updated the day this answer changes.</p>
 */
function side(): vscode.ExtensionContext {
  if (context === undefined) {
    throw new Error('the phrases page was asked to save before it was opened');
  }

  return context;
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
  await saveSetting(side(), config(), KEY, outcome.rows);
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
 * A write that did not land, said to the person who made it — in the words of whatever refused it.
 *
 * <p>To the PAGE rather than as a notification, because the banner sits beside the box that still
 * holds the words: a toast names the problem somewhere else on the screen and is gone in seconds.
 * The console line is for the log; the sentence is for them.</p>
 *
 * <p><b>The sentence is no longer this function's guess.</b> It used to assert that the settings file
 * was "read-only or held by another program" whatever had actually happened, and on 2026-09-14 that
 * sentence was read by somebody whose settings file was neither: the extension had been updated under
 * a window that had not caught up, so VS Code would not store a key it had not registered yet. The
 * real reason was in the exception, on the line above, going to the log. `settingRefused.ts` decides
 * what to say now, and for that one refusal it names the cure.</p>
 *
 * <p><b>Nothing here may throw.</b> It is `settledWrites`' `report`, which runs inside the `.catch`
 * at the end of the write chain — an exception raised in it has no handler left and becomes an
 * unhandled rejection, which is the silence this whole change was about, arriving by another door.
 * That is why the context is read defensively rather than through `side()`. (Code round, local.)</p>
 */
function saveFailed(error: unknown): void {
  if (context === undefined) {
    // Unreachable through the panel — it cannot exist before `openPhrases` set this — but this
    // function is the last handler in the chain, so it copes rather than asserts.
    console.error('[coai] phrases tab: a phrase could not be saved, before the tab was opened', error);

    return;
  }

  const refusal = refusalFor(context, KEY, error);
  if (panel === undefined) {
    // The tab has already gone: this is the flush on dispose, and the banner it would have written
    // to went with it. A notification is the only surface left, and silence here would mean a phrase
    // somebody typed and then closed the tab on vanished without a word. (Code round, codex.)
    reportRefusal(context, KEY, error, `${refusal.text} The phrase you were writing was not stored.`);

    return;
  }

  void panel.webview.postMessage({
    type: 'saveFailed',
    text: `${refusal.text}${refusal.reloadCures ? '' : ' What you typed is still here.'}`,
  });

  if (refusal.reloadCures) {
    // The banner beside the box carries the reasoning; this carries the BUTTON, and says only what
    // the banner cannot — that one click is available. Deliberately not the same three lines again:
    // two reviewers called the duplicate surfaces a collision, and they were right. `reportRefusal`
    // owns "at most once", per WINDOW, because that is what is stale.
    reportRefusal(
      context,
      KEY,
      error,
      'ConnectOtherAIs cannot save settings in this window until it is reloaded — the Phrases tab '
      + 'says why. Reloading closes that tab, so copy anything you have typed first.',
    );

    return;
  }

  console.error('[coai] phrases tab: a phrase could not be saved', error);
}

const writes = settledWrites<PhraseCommand>({
  apply,
  render,
  report: saveFailed,
  // Typing settles; a click does not, because there is no caret for a redraw to disturb and making
  // somebody wait to see a row appear would be a delay bought for nothing.
  fieldOf: (command) => (command.kind === 'edit' ? `${command.id}/${command.field}` : undefined),
});

export function openPhrases(extension: vscode.ExtensionContext): void {
  context = extension;
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
