import * as vscode from 'vscode';

import { textControlFrom, type TextControl } from './textControls';
import { applyToneDelta, pushTextToneTo } from './textToneHost';
import { applyZoomDelta, pushUiScaleTo } from './uiScaleHost';

/**
 * The host half of the two text controls (`textControls.ts`): push both settings to a page, and apply a
 * press on either. ONE disposable for both pushes, so a page that is closed cannot keep one of them.
 */
export function pushTextControlsTo(webview: vscode.Webview): vscode.Disposable {
  return vscode.Disposable.from(pushUiScaleTo(webview), pushTextToneTo(webview));
}

/** A press, applied — the setting changes, and every open page is pushed the new value. */
export async function applyTextControl(control: TextControl): Promise<void> {
  await (control.kind === 'zoom' ? applyZoomDelta(control.delta) : applyToneDelta(control.delta));
}

/**
 * A page's raw message, applied when it is a press on a text control — `true` when it was one, so the
 * host's own handling of the page's messages never sees it.
 *
 * <p>For the hosts whose pages post raw messages into a queue or a flow of their own; a failed save is
 * logged by the page's name rather than dropped.</p>
 *
 * @param page the page's name, for the log line
 */
export function appliedTextControl(message: unknown, page: string): boolean {
  const control = textControlFrom(message);
  if (control === undefined) {
    return false;
  }
  void applyTextControl(control).catch((error: unknown) => {
    console.error(`[coai] ${page}: the text size or tone could not be saved`, error);
  });

  return true;
}
