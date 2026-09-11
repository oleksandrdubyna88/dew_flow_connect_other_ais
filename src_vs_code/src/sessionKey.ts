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
 *
 * <p><b>`Tab` objects are STABLE — measured, 2026-09-08.</b> A gate reviewer called this design
 * Blocking on the claim that VS Code recreates them whenever tab-group state changes. The probe
 * extension was taught to log object identity across invocations, and three calls answered it: a
 * tab invoked on, left for another tab, and returned to reported <b>SAME object</b> both times
 * (19:19:49 and 19:20:22 against a first sighting at 19:19:28; a second tab likewise SAME against
 * 19:18:15). Switching the active tab does not replace the object, so the identity branch is the
 * one that runs and the label is genuinely a fallback rather than the load-bearing path.</p>
 *
 * <p>The fallback is kept anyway, and that is not superstition: the measurement covers activation
 * changes, which is what the reviewer claimed, not every future version of the host. If identity
 * is ever lost, a uniquely named tab re-keys and keeps its conversation and an ambiguous one opens
 * a second tab — an extra tab, never a follow-up delivered to somebody else.</p>
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
  /**
   * The URI scheme of the document behind the tab, or empty for a tab that has no document.
   *
   * <p>What separates a FILE from everything else that can be the active tab. `file` and `untitled`
   * are documents a person is reading and can send; `output`, `git`, `vscode-settings` and a webview
   * are not, and a scheme test excludes them structurally rather than by a list of names to keep up
   * to date. Optional, so every snapshot written before the second door compiles unchanged.</p>
   */
  readonly scheme?: string;
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

/** A tab that is one of Claude Code's own chat panels. */
export function isClaudeSessionTab(tab: TabSnapshot): boolean {
  return tab.viewType === CLAUDE_PANEL_VIEW_TYPE;
}

/**
 * Whether a source of this kind may be re-keyed by its LABEL when its own tab has gone.
 *
 * <p>Claude's panels may: a label there is a session name a person chose, and re-attaching a
 * conversation to a renamed tab is the case the fallback exists for. FILES may not: `/a/README.md`
 * and `/b/README.md` share a label and are not the same document, so a capture from the second
 * would continue the first's conversation — the silent wrong-conversation this module exists to
 * prevent, arriving through the door that was just added. A file whose tab has gone starts a new
 * conversation instead, which costs a tab and lies about nothing. (codex, the code round.)</p>
 */
export function rekeysByLabel(tab: TabSnapshot): boolean {
  return isClaudeSessionTab(tab);
}

/**
 * A tab that is an ordinary document — a file, or an unsaved buffer.
 *
 * <p>By SCHEME, not by "it has no viewType": an Output pane, a settings editor and a diff of a git
 * revision all have documents of their own, and none of them is a thing a person means when they
 * select a paragraph and press the chord. `file` and `untitled` are, and a new scheme arriving in a
 * future VS Code is excluded until somebody decides it should not be.</p>
 */
export function isOrdinaryEditorTab(tab: TabSnapshot): boolean {
  return tab.scheme === 'file' || tab.scheme === 'untitled';
}

/**
 * The source session for the active tab, or nothing when the active tab is not an eligible source.
 *
 * <p><b>The eligibility is a parameter, and the rest of this function is why.</b> Identity first, a
 * label re-key only for a panel whose own tab has gone, and a refusal to guess when a name is
 * ambiguous — all of that is about TABS, not about Claude Code. Two files called `README.md` in two
 * folders collide exactly the way two tabs called `main` do, and a second copy of this algorithm
 * for the second door would be a second place for that collision to be handled differently.</p>
 *
 * @param active the active tab of the active group, or `undefined` when the group has none
 * @param tabs every open tab, used only to ask whether a known panel's tab is still there
 * @param known the panels the registry already holds
 * @param eligible what may be a source at all; Claude Code's own panel by default, so every caller
 *   written before the second door keeps the behaviour it had
 */
export function sourceSession(
  active: TabSnapshot | undefined,
  tabs: readonly TabSnapshot[],
  known: readonly KnownPanel[],
  eligible: (tab: TabSnapshot) => boolean = isClaudeSessionTab,
): SessionMatch | undefined {
  if (active === undefined || !eligible(active)) {
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
  if (!rekeysByLabel(active)) {
    return { kind: 'new', key: active.key, label: active.label };
  }
  const sameLabel = known.filter((panel) => panel.label === active.label);
  const orphaned = sameLabel.length === 1 && !stillOpen(sameLabel[0]!, tabs) ? sameLabel[0]! : undefined;
  if (orphaned !== undefined) {
    return { kind: 'rekey', from: orphaned.key, key: active.key, label: active.label };
  }

  return { kind: 'new', key: active.key, label: active.label };
}
