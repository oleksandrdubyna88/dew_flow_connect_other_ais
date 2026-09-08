/**
 * Which Claude Code session a captured passage came from.
 *
 * <p>Pure, over a NARROWED snapshot of `vscode.window.tabGroups` rather than the API itself, so the
 * one decision in it can be tested without a host. The caller in `chatCommand.ts` does the widening;
 * this file does the judging.</p>
 *
 * <p><b>The key is the tab OBJECT, not its label.</b> The first version of the plan used the label
 * and wrote the consequence off as an accepted risk; two of the gate's three reviewers refused it
 * independently, and they were right. Two Claude Code tabs can both be called `main` — people rename
 * them, and people also leave them unnamed — and a label key sends the second tab's follow-up into
 * the first tab's conversation. That failure is silent: the answer arrives, it is simply about
 * somebody else's question.</p>
 *
 * <p><b>The label survives as a fallback, and only as one.</b> While a tab is open its object is the
 * identity the host already maintains. If a future VS Code hands back a NEW object for a tab that is
 * still open — renaming is the case to fear — the old key would vanish from the snapshot while the
 * conversation is still wanted. Then, and only then, a label match re-keys the existing panel onto
 * the new object. A label match while the old object is still on screen means two real tabs, and it
 * must NOT re-key: that is exactly the collapse this module exists to prevent.</p>
 */

/**
 * The view type VS Code reports for Claude Code's chat panel.
 *
 * <p>Measured, not guessed: the phase-0 probe read `mainThreadWebview-claudeVSCodePanel` off a live
 * tab. The `mainThreadWebview-` prefix is the host's, wrapped around the extension's own
 * `claudeVSCodePanel` — which is also the id Claude Code's `webview/context` menu entries match on.
 * If Anthropic renames their view type this constant is what stops matching, and the command falls
 * back to doing nothing rather than guessing at a stranger's tab.</p>
 */
export const CLAUDE_PANEL_VIEW_TYPE = 'mainThreadWebview-claudeVSCodePanel';

/** One tab, narrowed to what the decision needs. `key` is the host's own `vscode.Tab` object. */
export interface TabSnapshot {
  readonly key: object;
  readonly label: string;
  readonly viewType: string;
}

/** A panel the registry already holds, and the tab it was opened for. */
export interface KnownPanel {
  readonly key: object;
  readonly label: string;
}

/**
 * What the caller should do with the active tab.
 *
 * <p>Three outcomes rather than a key, because "re-key the panel I already have" is a different
 * action from "open a new one", and a caller handed only a key would have to re-derive which.</p>
 */
export type SessionMatch =
  | { readonly kind: 'existing'; readonly key: object; readonly label: string }
  | { readonly kind: 'rekey'; readonly from: object; readonly key: object; readonly label: string }
  | { readonly kind: 'new'; readonly key: object; readonly label: string };

/** Whether a known panel's tab is still somewhere on screen. */
function stillOpen(known: KnownPanel, tabs: readonly TabSnapshot[]): boolean {
  return tabs.some((tab) => tab.key === known.key);
}

/**
 * The source session for the active tab, or nothing when the active tab is not a Claude Code panel.
 *
 * @param active the active tab of the active group, or `undefined` when the group has none
 * @param tabs every open tab, used only to ask whether a known panel's tab is still there
 * @param known the panels the registry already holds
 */
export function sourceSession(
  active: TabSnapshot | undefined,
  tabs: readonly TabSnapshot[],
  known: readonly KnownPanel[],
): SessionMatch | undefined {
  if (active === undefined || active.viewType !== CLAUDE_PANEL_VIEW_TYPE) {
    return undefined;
  }

  const byIdentity = known.find((panel) => panel.key === active.key);
  if (byIdentity !== undefined) {
    return { kind: 'existing', key: byIdentity.key, label: active.label };
  }

  // Only a panel whose own tab has GONE may be re-attached by name, and only when the name points at
  // exactly ONE of them. Two conditions, not one: the first version checked only that the panel it
  // found was closed, so with two panels called `main` — one closed, one still open — a new `main`
  // tab was re-keyed onto the closed one while the live namesake sat beside it. That is the same
  // silent wrong-conversation this module exists to prevent, re-entering through the fallback.
  // (codex, the code round on this very file.)
  const sameLabel = known.filter((panel) => panel.label === active.label);
  const orphaned = sameLabel.length === 1 && !stillOpen(sameLabel[0]!, tabs) ? sameLabel[0]! : undefined;
  if (orphaned !== undefined) {
    return { kind: 'rekey', from: orphaned.key, key: active.key, label: active.label };
  }

  return { kind: 'new', key: active.key, label: active.label };
}
