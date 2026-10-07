import * as vscode from 'vscode';
import {
  flushChatPresetEdits,
  onChatPresetsRedraw,
  presetModels,
  presetProviders,
  pruneDeadModelRows,
  queueChatPresetEdit,
  savedPromptPresets,
  unreadablePresetModels,
} from './chatPresetsHost';
import { chatPresetsHtml } from './chatPresetsPage';
import { presetEdit } from './chatPresetsMessages';
import { isTextControl } from './textControls';
import { applyTextControl, pushTextControlsTo } from './textControlsHost';
import { currentTextTone } from './textToneHost';
import { currentUiScale } from './uiScaleHost';
import { webviewNonce } from './webviewNonce';

/**
 * The presets tab: one webview, reused while open.
 *
 * <p>Thin on purpose — the page decides what a message MEANS (`presetEdit`), the editing core both pages call
 * (`chatPresetsHost.ts`, todo/PLAN_one_model_catalog.md E4.6b) stores it, and this draws the tab. That is the
 * arrangement the rounds log and the help page already use, and the reason it is worth keeping is that everything
 * interesting stays reachable from a unit test.</p>
 *
 * <p><b>Saved as it is typed, and re-rendered only when the shape changes.</b> A typed edit settles in the host's queue
 * and leaves the page alone: re-rendering on every keystroke would move the caret to the end of the box somebody is
 * typing in the middle of, which is the defect the sidebar's prompt box had and which its own plan fixed. Adding and
 * removing a row DO re-render, because the list's shape is what changed.</p>
 */

let panel: vscode.WebviewPanel | undefined;

function render(): void {
  if (panel === undefined) {
    return;
  }
  panel.webview.html = chatPresetsHtml(
    {
      prompts: savedPromptPresets(),
      models: presetModels(),
      providers: presetProviders(),
      unreadable: unreadablePresetModels(),
      uiScale: currentUiScale(),
      textTone: currentTextTone(),
    },
    webviewNonce(),
  );
}

/** Open the tab, or bring back the one that is already open. */
export function openChatPresets(): void {
  if (panel !== undefined) {
    panel.reveal();

    return;
  }
  // AWAITED before anything is drawn, which the code round was right about: the prune writes, the
  // render reads, and started side by side the page paints the very rows the prune is removing. The
  // tab opens after it either way — a cleanup that cannot run is not a reason to withhold the tab,
  // and it is said out loud rather than swallowed.
  void pruneDeadModelRows()
    .catch((error: unknown) => { console.error('coai: the unusable model presets could not be cleared', error); })
    .then(openPanel);
}

function openPanel(): void {
  if (panel !== undefined) {
    panel.reveal();

    return;
  }
  panel = vscode.window.createWebviewPanel(
    'coaiChatPresets',
    'Chat presets',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const scale = pushTextControlsTo(panel.webview);
  const redraw = onChatPresetsRedraw(render);
  panel.webview.onDidReceiveMessage((message: unknown) => {
    const command = presetEdit(message);
    void (isTextControl(command) ? applyTextControl(command) : queueChatPresetEdit(command));
  });
  panel.onDidDispose(() => {
    // Whatever is still settling is written before the page that typed it goes — the one data loss a page like this
    // can cause is text somebody typed and then closed the tab on.
    void flushChatPresetEdits();
    redraw.dispose();
    scale.dispose();
    panel = undefined;
  });
  render();
}
