import * as vscode from 'vscode';
import { webviewNonce } from './webviewNonce';

import { DEFAULT_ROLE_TAB, nextTab, rolesHtml } from './rolesPage';
import { roleEdit } from './rolesMessages';
import { serverOnThisSide } from './installer';
import { bindRoles, flushRoleEdits, onRolesRedraw, queueRoleEdit, reportRolesFailure, roleRows, roleTexts, rolesSide } from './rolesHost';
import { roleDeletions, whenDeletionsChange } from './roleDeletionsHost';
import { appliedTextControl, pushTextControlsTo } from './textControlsHost';
import { currentTextTone } from './textToneHost';
import { currentUiScale } from './uiScaleHost';
import { BusyHost } from './busyHost';
import { type BusySnapshot, IDLE } from './busySnapshot';

/**
 * The Review roles tab — a thin host over pure modules and the shared editing core (`rolesHost.ts`, which the new
 * Settings page's Roles & prompts calls too: todo/PLAN_one_model_catalog.md E4.3). What is left here is this tab's own:
 * its panel, which of its sections is open, its busy marks, and its redraw.
 */

let panel: vscode.WebviewPanel | undefined;

/**
 * What the tab asked for and the host has not finished — its busy mark (research/PLAN_busy_marks_on_every_webview.md, E3).
 * One per open tab: made with it, painted into every redraw, settled when it closes.
 */
let busy: BusyHost | undefined;

/**
 * Which of the sections is open — held HERE rather than on the page, because a redraw replaces `panel.webview.html`
 * wholesale, and every shape-changing action redraws. A module variable, not a stored setting: it outlives the panel,
 * and which tab somebody was last looking at is not worth a key in their settings.
 */
let tab: string = DEFAULT_ROLE_TAB;

/**
 * Hears the tab, and holds each structural change under its busy mark. The open section moves on the press (`nextTab`
 * — adding a role opens the code section it lands in); the edit itself goes to the one queue both pages share.
 */
function listen(opened: vscode.WebviewPanel): BusyHost {
  const marks = new BusyHost({ post: (message) => { void opened.webview.postMessage(message); } });
  busy = marks;
  opened.webview.onDidReceiveMessage((message: unknown) => {
    // A text press is the person's setting, not an edit of the roles, and never waits behind one.
    if (!appliedTextControl(message, 'roles page')) {
      // A fresh tab's `ready` is told what is running; a numbered change is held under the mark until it has been
      // written and the tab redrawn. Typing posts unnumbered and settles on its own.
      if (marks.heard(message as object)) {
        return;
      }
      const command = roleEdit(message);
      tab = nextTab(tab, command);
      void marks.track(message as object, () => queueRoleEdit(command));
    }
  });

  return marks;
}

/** What a redraw paints as still running: nothing when no tab is open to hold any. */
function stillRunning(): BusySnapshot {
  return busy?.snapshot() ?? IDLE;
}

export function openRoles(extension: vscode.ExtensionContext): void {
  bindRoles(extension);
  if (panel !== undefined) {
    panel.reveal();

    return;
  }

  panel = vscode.window.createWebviewPanel(
    'coaiRoles',
    'Review roles',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  const scale = pushTextControlsTo(panel.webview);
  // A deletion that becomes stranded while this page is OPEN has to appear on it: `inMs` says when to come back, so the
  // page waits rather than polls. (codex, the code round.)
  const deletionsChanged = whenDeletionsChange((inMs) => {
    if (inMs <= 0) {
      redrawSoon();

      return;
    }
    // `unref` so a pending redraw cannot keep the host alive; the tab may close first, and `redraw` is a no-op then.
    setTimeout(redrawSoon, inMs).unref?.();
  });
  const marks = listen(panel);
  const redraws = onRolesRedraw(render);
  panel.onDidDispose(() => {
    marks.dispose();
    busy = undefined;
    scale.dispose();
    deletionsChanged.dispose();
    redraws.dispose();
    panel = undefined;
    // Whatever is still settling belongs to a person who typed it. Losing it because they closed the tab would be the
    // one data loss this page can cause.
    void flushRoleEdits();
  });
  void render().catch((error: unknown) => reportRolesFailure('ConnectOtherAIs could not draw the roles page.', error));
  // Not awaited, and deliberately not fatal: the page is useful without knowing the server, and it says as much.
  void askTheServer().catch((error: unknown) => {
    console.error('[coai] roles page: the installed server could not be identified', error);
  });
}

/** The installed server, so the page can warn about one too old to read any of this. */
let serverVersion = '';

/**
 * What this page knows about the installed server: it ASKS, rather than waiting to be told — `coai.editRoles` is on the
 * command palette and does not need the sidebar to have been looked at. Once per opening, not once per repaint.
 */
async function askTheServer(): Promise<void> {
  const context = rolesSide();
  // The published version is what an UPDATE offer is measured against, and this page offers none.
  const status = await serverOnThisSide(context.globalStorageUri, context.globalState, '');
  rolesKnowTheServer(status.kind === 'absent' ? '' : status.version);
}

/**
 * The sidebar telling this page what IT found — a second, cheaper source of the same answer. It REPAINTS when the answer
 * changes; it used to only assign, which is how the version could arrive into a variable nothing read again.
 */
export function rolesKnowTheServer(version: string): void {
  if (version === serverVersion) {
    return;
  }

  serverVersion = version;
  void render().catch((error: unknown) => reportRolesFailure('ConnectOtherAIs could not draw the roles page.', error));
}

/** A redraw that is safe to schedule: the tab may be gone by the time it runs. */
function redrawSoon(): void {
  if (panel === undefined) {
    return;
  }
  void render().catch((error: unknown) => {
    console.error('[coai] the roles page could not be redrawn for a deletion', error);
  });
}

async function render(): Promise<void> {
  if (panel === undefined) {
    return;
  }

  const html = rolesHtml(
    {
      rows: roleRows(),
      texts: await roleTexts(),
      serverVersion,
      perSide: vscode.workspace.getConfiguration('coai').get('perSideSettings') === true,
      tab,
      stranded: await roleDeletions(rolesSide()).stranded(),
      // After the awaits: a press made while they ran is the one this page must be drawn in.
      uiScale: currentUiScale(),
      // What is still running, so a redraw in the middle of a change keeps its bar.
      busy: stillRunning(),
      textTone: currentTextTone(),
    },
    webviewNonce(),
  );
  // Re-checked: `await roleTexts()` is a suspension point, and the panel can be disposed across it.
  if (panel !== undefined) {
    panel.webview.html = html;
  }
}
