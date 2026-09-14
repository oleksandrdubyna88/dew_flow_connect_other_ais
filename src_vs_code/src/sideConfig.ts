import * as vscode from 'vscode';
import { ConfigReader, OVERLAID_SETTINGS } from './settingsShape';
import { declaresSetting, settingRefusal, type SettingRefusal } from './settingRefused';
import { sideConfigReader, writeOverlay } from './sideSettings';
import { thisSide } from './installer';

/**
 * The ONE construction of this side's settings reader.
 *
 * <p><b>Why a module of four lines.</b> `sideSettings.ts` holds the decision and is deliberately free
 * of `vscode` so it can be tested at all; but WHICH side is running can only be answered with
 * `vscode.env.remoteName`, so the side and the store have to be assembled somewhere that imports the
 * host. That assembly was written out twice — once in the chat and once beside the server settings
 * file — and a reviewer named the obvious consequence: two copies of a four-argument call drift, and
 * the argument that drifts is the one nobody can see is wrong.</p>
 *
 * <p><b>It is also what makes "this side" an invariant rather than a habit.</b> `sideConfigReader`
 * takes any `Side` at all, so a caller holding a cached or unrelated one would read another side's
 * `vendors` — the wrong `executablePath`, the wrong CLI, and no type error anywhere. Nobody outside
 * this function passes a `Side` now: the only door takes the CONTEXT and derives the side from it.
 * (codex and gemini, the code round.)</p>
 */
export function readerFor(
  context: vscode.ExtensionContext,
  config: vscode.WorkspaceConfiguration,
): ConfigReader {
  return sideConfigReader(
    (section) => config.get(section),
    config.get<boolean>('perSideSettings') === true,
    context.globalState,
    thisSide(context.globalStorageUri),
  );
}

/**
 * The ONE write of a `coai.*` setting, and the mirror of {@link readerFor}.
 *
 * <p>Here for the same reason the reader is: a write that decides for itself which layer it belongs
 * in is a setting that silently goes to the wrong one. A side that keeps its own settings never
 * writes to `settings.json` — that file is the CLIENT's, and VS Code hands it to every extension
 * host, which is the whole reason the per-side switch exists.</p>
 *
 * <p>The roles page is why this is a function rather than a private method of the panel. It wrote
 * `roles` — a setting that IS in `OVERLAID_SETTINGS` — straight to the global target, so a role
 * added on one side appeared on every side and ran in reviews it was never meant for, while the page
 * itself said "saved for this side of the machine". Two copies of a rule is one copy of the rule.</p>
 *
 * <p><b>A refusal PROPAGATES; it is no longer caught here.</b> Swallowing it left every caller
 * believing the write had landed, and for a page driven by `settledWrites` that belief has a cost:
 * it repaints, and a repaint draws the rows the setting still holds — which are exactly the rows
 * without the words the person just typed. The roles page did that. Reporting is the CALLER's, which
 * is also the only place that knows whether the sentence belongs in a notification or in a banner
 * beside the box that still holds the text. {@link reportRefusal} is the notification half.</p>
 *
 * <p><b>That makes the rejection part of this function's contract, so here is every caller and what
 * each does with it.</b> A gate reviewer asked for the enumeration rather than the intention, and was
 * right to: an uncaught rejection out of a webview message handler is an unhandled rejection, which
 * is silence again by another route.</p>
 *
 * <ul>
 *   <li><b>`panelProvider.save`</b> — catches locally and reports. The panel has no banner, so the
 *       surface is a notification, and it continues rather than throwing out of a click handler.</li>
 *   <li><b>`rolesPanel.write`</b> — does NOT catch; it is called inside `settledWrites.apply`, whose
 *       chain has a `.catch` that calls its `report`. The repaint is in a `.then` BEFORE that catch,
 *       never a `finally`, so a rejected write skips the repaint — which is the whole repair.</li>
 *   <li><b>`phrasesPanel.apply`</b> — the same, reporting into the page's banner instead.</li>
 * </ul>
 *
 * <p>A fourth caller must pick one of those two shapes. There is no third.</p>
 */
export async function saveSetting(
  context: vscode.ExtensionContext,
  config: vscode.WorkspaceConfiguration,
  key: string,
  value: unknown,
): Promise<void> {
  if (config.get<boolean>('perSideSettings') === true && OVERLAID_SETTINGS.includes(key)) {
    await writeOverlay(context.globalState, thisSide(context.globalStorageUri), key, value);

    return;
  }

  await config.update(key, value, vscode.ConfigurationTarget.Global);
}

/** The label of the one action that cures a stale window, and the words VS Code knows it by. */
const RELOAD = 'Reload Window';

/**
 * Why a write was refused, judged against the manifest of the build that is actually running.
 *
 * <p>The ONE place that pairs the pure rule with the host fact it needs, so that no caller can reach
 * the first without the second. `context.extension.packageJSON` is this build's own manifest — the
 * question "does the extension running right now declare this key" is exactly what separates a window
 * that has not caught up from a key this build never shipped, and only one of those is cured by
 * reloading.</p>
 */
export function refusalFor(
  context: vscode.ExtensionContext,
  key: string,
  error: unknown,
): SettingRefusal {
  return settingRefusal(key, error, declaresSetting(context.extension.packageJSON, key));
}

/**
 * A refused write, said to the person — with the action that fixes it when there is one.
 *
 * <p>The sentence itself is decided in `settingRefused.ts`, which is pure and tested. What is here
 * is the part only a host can do: show it, and — for the one refusal that HAS a one-click cure —
 * offer the click. The same shape `reportStandDown` uses for the other way a window falls behind the
 * build it is running.</p>
 *
 * <p><b>`instead` is for a caller that already has a better sentence.</b> The roles page argues, on
 * the record, that an errno and a path belong in the log rather than in front of somebody who just
 * renamed a role — and it is right about ordinary failures. It is not right about this one: a stale
 * window is not a failure of theirs to interpret, it has a cure, and no caller's sentence can offer
 * the button. So the recognised case always wins, and `instead` covers the rest.</p>
 */
export function reportRefusal(
  context: vscode.ExtensionContext,
  key: string,
  error: unknown,
  instead = '',
): void {
  const refusal = refusalFor(context, key, error);
  console.error(`[coai] coai.${key} was not saved`, error);
  if (!refusal.reloadCures) {
    void vscode.window.showErrorMessage(instead.length > 0 ? instead : refusal.text);

    return;
  }

  void vscode.window.showErrorMessage(refusal.text, RELOAD).then((choice) => {
    if (choice === RELOAD) {
      void vscode.commands.executeCommand('workbench.action.reloadWindow');
    }
  });
}
