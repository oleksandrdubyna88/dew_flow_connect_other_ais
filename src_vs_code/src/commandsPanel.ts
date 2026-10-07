import * as vscode from 'vscode';
import { bindCommands, commandRows, commandTexts, flushCommandEdits, onCommandsRedraw, queueCommandEdit, reportCommandsFailure, commandsSide } from './commandsHost';
import { commandsHtml } from './commandsPage';
import { commandEdit } from './commandsMessages';
import { serverOnThisSide } from './installer';
import { appliedTextControl, pushTextControlsTo } from './textControlsHost';
import { currentTextTone } from './textToneHost';
import { currentUiScale } from './uiScaleHost';
import { BusyHost } from './busyHost';
import { type BusySnapshot, IDLE } from './busySnapshot';
import { webviewNonce } from './webviewNonce';

/**
 * The tab that edits the gate's commands — issue #467, Epic B. A thin host over pure modules and the shared editing core
 * (`commandsHost.ts`, which the new Settings page's Commands calls too: todo/PLAN_one_model_catalog.md E4.4). What is
 * left here is this tab's own: its panel, its busy marks and its redraw.
 */

let panel: vscode.WebviewPanel | undefined;

/**
 * What the tab asked for and the host has not finished — its busy mark (research/PLAN_busy_marks_on_every_webview.md, E3).
 * One per open tab: made with it, painted into every redraw, settled when it closes.
 */
let busy: BusyHost | undefined;
let serverVersion = '';

/**
 * Hears the tab, and holds each structural change under its busy mark (research/PLAN_busy_marks_on_every_webview.md, E3).
 * One `BusyHost` per open tab, settled by the caller when it closes.
 */
function listen(opened: vscode.WebviewPanel): BusyHost {
  const marks = new BusyHost({ post: (message) => { void opened.webview.postMessage(message); } });
  busy = marks;
  opened.webview.onDidReceiveMessage((message: unknown) => {
    // A press on a text control is a setting of the person's, not an edit of the commands: it never enters the queue.
    if (!appliedTextControl(message, 'gate commands page')) {
      // A fresh tab's `ready` is told what is running; a numbered change is held until it has been written and the tab
      // redrawn. Typing posts unnumbered and settles on its own.
      if (marks.heard(message as object)) {
        return;
      }
      void marks.track(message as object, () => queueCommandEdit(commandEdit(message)));
    }
  });

  return marks;
}

/** What a redraw paints as still running: nothing when no tab is open to hold any. */
function stillRunning(): BusySnapshot {
  return busy?.snapshot() ?? IDLE;
}

export function openCommands(extension: vscode.ExtensionContext): void {
  bindCommands(extension);
  if (panel !== undefined) {
    panel.reveal();

    return;
  }

  panel = vscode.window.createWebviewPanel(
    'coaiCommands',
    'Gate commands',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const text = pushTextControlsTo(panel.webview);
  const marks = listen(panel);
  const redraws = onCommandsRedraw(render);
  panel.onDidDispose(() => {
    marks.dispose();
    busy = undefined;
    text.dispose();
    redraws.dispose();
    panel = undefined;
    // Whatever is still settling is somebody's typing; closing the tab must not lose it.
    void flushCommandEdits();
  });
  void render().catch((error: unknown) => { reportCommandsFailure('ConnectOtherAIs could not draw the commands page.', error); });
  void askTheServer().catch((error: unknown) => {
    console.error('[coai] commands page: the installed server could not be identified', error);
  });
}

/** Once per opening: the page says when the installed server is too old to read any of this. */
async function askTheServer(): Promise<void> {
  const status = await serverOnThisSide(commandsSide().globalStorageUri, commandsSide().globalState, '');
  serverVersion = status.kind === 'absent' ? '' : status.version;
  await render();
}

async function render(): Promise<void> {
  if (panel === undefined) {
    return;
  }
  const html = commandsHtml(
    {
      rows: commandRows(), texts: await commandTexts(), serverVersion,
      perSide: vscode.workspace.getConfiguration('coai').get('perSideSettings') === true,
      uiScale: currentUiScale(), textTone: currentTextTone(), busy: stillRunning(),
    },
    webviewNonce(),
  );
  // Re-checked: `await commandTexts()` is a suspension point, and the tab can close across it.
  if (panel !== undefined) {
    panel.webview.html = html;
  }
}
