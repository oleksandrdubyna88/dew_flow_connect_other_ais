import * as vscode from 'vscode';
import { clampTone, toneColours, toneLabel } from './textTone';

/**
 * The host half of the ± text tone: read the setting, apply a press, keep every open page in step.
 *
 * <p>`uiScaleHost.ts`'s shape, for the control beside it and for the same reasons. The value is
 * `coai.textTone` at global scope, so it syncs — a person who has decided the white is too bright
 * has decided that about their eyes, not about this machine.</p>
 *
 * <p>A press from any page lands here, is CLAMPED here, and is written; the write raises
 * `onDidChangeConfiguration` and every panel registered through {@link pushTextToneTo} repaints
 * from the one stored value. The page reports which way it was pressed and never the result — a
 * page that computed its own next value would be a second place the two could disagree.</p>
 */

const SECTION = 'coai';
const KEY = 'textTone';

export function currentTextTone(): number {
  return clampTone(vscode.workspace.getConfiguration(SECTION).get(KEY));
}

/** Apply one press. Only the DIRECTION is taken from the page; the size of a step is ours. */
export async function applyToneDelta(delta: number): Promise<void> {
  const next = clampTone(currentTextTone() + Math.sign(delta));
  await vscode.workspace
    .getConfiguration(SECTION)
    .update(KEY, next, vscode.ConfigurationTarget.Global);
}

/**
 * Keep one webview's text tone in step with the setting for as long as it lives.
 *
 * <p>Posts immediately and on every change; the returned disposable unhooks the listener. The
 * immediate post is what makes a page opened AFTER a change start at the changed value rather than
 * at the theme's own colour.</p>
 */
export function pushTextToneTo(webview: vscode.Webview): vscode.Disposable {
  const push = (): void => {
    const offset = currentTextTone();
    const { text, read } = toneColours(offset);
    // An empty colour is the theme's own, and assigning '' is how the page gives the property back
    // rather than painting over it with something that looks the same.
    void webview.postMessage({ type: 'textTone', color: text, read, label: toneLabel(offset) });
  };
  push();

  return vscode.workspace.onDidChangeConfiguration((change) => {
    if (change.affectsConfiguration(`${SECTION}.${KEY}`)) {
      push();
    }
  });
}
