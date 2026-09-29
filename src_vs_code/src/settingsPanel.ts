import * as vscode from 'vscode';

import { settingsSections } from './panelView';
import { nextSettingsTab, SETTINGS_LOADING } from './settingsPage';
import { openedFrom } from './tabStrip';
import { pushTextControlsTo } from './textControlsHost';

/**
 * The Settings editor tab — a thin host. Everything it SHOWS is the panel's (`panelProvider.ts` paints
 * it as a second surface beside the sidebar, from the same state and through the same write path); what
 * is left here is only what a host must do: create the tab or bring it back, and hold which tab of it is
 * open (`research/PLAN_settings_page.md`).
 */

/** What the panel offers this host — its own surface to attach, and a way to move the open tab. */
export interface SettingsHost {
  attachSettings(panel: vscode.WebviewPanel): void;
  showSettingsTab(tab: string): void;
}

let panel: vscode.WebviewPanel | undefined;

/**
 * Which tab is open. Held HERE rather than on the page, because a repaint replaces the whole document;
 * a module variable, not a setting, so closing and reopening the tab in this window keeps it and nothing
 * on disk needs validating — the arrangement `rolesPanel.ts` uses. Empty means the first tab.
 */
let tab = '';

function tabIds(): readonly string[] {
  return settingsSections().map((section) => section.id);
}

/** The tab the page should show — always one that exists: the strip's own fallback, not a second copy of it. */
export function heldSettingsTab(): string {
  return openedFrom(tabIds().map((key) => ({ key, label: key })), tab);
}

/** A tab was chosen, on the page or by the command's argument; anything unknown changes nothing. */
export function chooseSettingsTab(requested: unknown): string {
  tab = nextSettingsTab(tab, requested, tabIds());

  return heldSettingsTab();
}

/**
 * Opens the one Settings tab of this window, or brings it back — on `requested`, when that names a tab.
 *
 * <p>ONE per window: a second press reveals the tab that is there rather than opening another. The panel
 * variable is assigned before anything can run in between, so two presses in quick succession still
 * find it on the second.</p>
 */
export function openSettings(host: SettingsHost, requested?: unknown): void {
  const shown = chooseSettingsTab(requested);
  if (panel !== undefined) {
    panel.reveal();
    host.showSettingsTab(shown);

    return;
  }

  panel = vscode.window.createWebviewPanel(
    'coaiSettings',
    'ConnectOtherAIs — Settings',
    vscode.ViewColumn.Active,
    { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [], enableFindWidget: true },
  );
  // Until the first paint: gathering the state runs CLI and network probes, and a blank tab would read as
  // a broken one.
  panel.webview.html = SETTINGS_LOADING;
  const opened = panel;
  const text = pushTextControlsTo(opened.webview);
  opened.onDidDispose(() => {
    text.dispose();
    if (panel === opened) {
      panel = undefined;
    }
  });
  host.attachSettings(opened);
}
