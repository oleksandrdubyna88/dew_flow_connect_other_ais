import { ChatAutoSend, sendsImmediately } from './chatSettings';

/**
 * Which door the command came through, and what that means for the passage.
 *
 * <p>Pure. The `vscode` half of the trigger does the capturing and the opening; this file decides,
 * and it is separate for the reason story 1.3 learned the hard way — a decision inside a module that
 * imports `vscode` is a decision no unit test can reach, and the gate said so with a finding.</p>
 *
 * <p><b>The two doors are not the same door.</b> Measured with the phase-0 probe: from a KEYBINDING
 * the webview still holds the keyboard and a synthetic `Ctrl+C` captures the selection (638
 * characters); from the CONTEXT MENU it captures nothing at all — the clipboard is not even touched,
 * because closing the menu takes the focus or the selection out of the webview. So the menu path
 * cannot copy for the person and must take what they copied themselves, which is also why it does
 * not send by default: it cannot know how old that is.</p>
 */

/** How the passage is to be obtained. */
export type ChatPath = 'keyboard' | 'menu';

export interface TriggerPlan {
  readonly path: ChatPath;
  /** Whether the captured passage is asked immediately, or left in the composer. */
  readonly send: boolean;
}

/**
 * Whether VS Code invoked this command from the webview's context menu.
 *
 * <p>It hands a menu item `[{ webview: 'claudeVSCodePanel' }]` and a keybinding nothing at all —
 * measured with the probe, and the only signal there is. Anything unrecognised is treated as the
 * keyboard path, because that is the one that captures for itself: guessing "menu" for an unknown
 * caller would silently stop capturing and look like a broken key.</p>
 */
export function fromMenuArgs(args: readonly unknown[]): boolean {
  const first = args[0];

  return typeof first === 'object' && first !== null && 'webview' in first;
}

/**
 * What to do, given the door, the person's setting — and, for the two menu items that say so, the
 * answer they have already given.
 *
 * <p>`asked` is how *Chat with other AI: default* and *: choose* differ from each other and from the
 * chord: each of them NAMES what it will do, so it does not consult `coai.chatAutoSend` at all. A
 * menu item whose behaviour depends on a setting somewhere else is an item nobody can predict from
 * its own label, which is what the operator was working around by having two.</p>
 */
export function triggerPlan(
  args: readonly unknown[],
  autoSend: ChatAutoSend,
  asked?: boolean,
): TriggerPlan {
  const menu = fromMenuArgs(args);

  return { path: menu ? 'menu' : 'keyboard', send: asked ?? sendsImmediately(autoSend, menu) };
}
