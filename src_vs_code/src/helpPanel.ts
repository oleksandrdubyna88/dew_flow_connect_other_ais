import * as vscode from 'vscode';
import { HELP_LANGUAGES, HelpLanguage } from './helpContent';
import { renderHelpHtml } from './helpPage';
import { settingWritten } from './settingWrite';
import { applyZoomDelta, currentUiScale, pushUiScaleTo } from './uiScaleHost';
import { applyToneDelta, currentTextTone, pushTextToneTo } from './textToneHost';

/**
 * The help panel : one webview, reused while open. The language lives in the
 * setting `coai.helpLanguage` — a real setting so it syncs — and nothing else reads
 * it, which is what scopes the choice to the help pages on purpose.
 */

const SECTION = 'coai';
const LANGUAGE_KEY = 'helpLanguage';

let panel: vscode.WebviewPanel | undefined;

interface HelpMessage {
  type: string;
  language?: string;
  delta?: number;
}

/**
 * A settings write that failed is said out loud, never dropped into a discarded promise.
 *
 * <p>The body moved to `settingWrite.ts` when the review panel adopted the same two controls and
 * grew a second copy of it; a code round named the duplication and this is the extracted half. The
 * behaviour changed in one way, deliberately: it goes through `notify` rather than
 * `showWarningMessage`, which was the last DIRECT notification call in this extension — the census
 * ratchet in `notificationSites.test.mjs` only ever falls, and this takes it to zero.</p>
 */
const said = (writing: Promise<void>): Promise<void> => settingWritten(writing, 'help');

async function onHelpMessage(message: HelpMessage): Promise<void> {
  const handlers: Record<string, () => Promise<void>> = {
    zoom: () => said(applyZoomDelta(message.delta ?? 0)),
    tone: () => said(applyToneDelta(message.delta ?? 0)),
    language: () => said(setHelpLanguage(message.language ?? '')),
  };
  await handlers[message.type]?.();
}

/** Writes the setting only for a language the catalog knows — the value comes off a select. */
async function setHelpLanguage(language: string): Promise<void> {
  if (!(HELP_LANGUAGES as readonly string[]).includes(language)) {
    return;
  }
  await vscode.workspace
    .getConfiguration(SECTION)
    .update(LANGUAGE_KEY, language, vscode.ConfigurationTarget.Global);
}

export function helpLanguage(): HelpLanguage {
  const value = vscode.workspace.getConfiguration(SECTION).get<string>(LANGUAGE_KEY, 'en');
  return (HELP_LANGUAGES as readonly string[]).includes(value) ? (value as HelpLanguage) : 'en';
}

export function showHelp(): void {
  if (panel !== undefined) {
    panel.reveal();
    return;
  }
  panel = vscode.window.createWebviewPanel(
    'coaiHelp',
    'ConnectOtherAIs — Help',
    vscode.ViewColumn.Active,
    // The page has a search box of its own; the find bar is chrome ABOVE the webview and displaces
    // nothing, and one behaviour everywhere beats a page where Ctrl+F is dead for no visible reason.
    { enableScripts: true, enableFindWidget: true, localResourceRoots: [] },
  );
  const render = (): void => {
    if (panel !== undefined) {
      panel.webview.html = renderHelpHtml({
        language: helpLanguage(),
        uiScale: currentUiScale(),
        textTone: currentTextTone(),
      });
    }
  };
  render();
  const zoomHook = pushUiScaleTo(panel.webview);
  const toneHook = pushTextToneTo(panel.webview);
  const languageHook = vscode.workspace.onDidChangeConfiguration((change) => {
    if (change.affectsConfiguration(`${SECTION}.${LANGUAGE_KEY}`)) {
      render();
    }
  });
  panel.webview.onDidReceiveMessage((message: HelpMessage) => void onHelpMessage(message));
  panel.onDidDispose(() => {
    zoomHook.dispose();
    toneHook.dispose();
    languageHook.dispose();
    panel = undefined;
  });
}
