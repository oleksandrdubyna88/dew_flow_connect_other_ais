import type { ChatPanels } from './chatPanels';
import type { ConversationSource } from './chatStore';
import { threadUnder } from './chatThread';
import { Borrowed, Capture, Clipboard, borrowClipboard } from './selectionCapture';
import type { KnownPanel, SessionMatch } from './sessionKey';

/**
 * The terminal as a conversation source: copying its selection, naming its conversation, finding its panel — with no `vscode` in it.
 *
 * <p>The rules are pure values and take their seams as parameters. The one exception is `tabPanels`, which reads the chat registry
 * and the thread table once to say which panels a tab may match; it is the only function here that touches shared state.</p>
 *
 * <p><b>No keystroke, ever.</b> A synthetic Ctrl+C reaches a terminal as SIGINT: in `ssh`, `tmux` or
 * the Claude CLI that interrupts the program instead of copying anything. The copy is VS Code's own
 * `copySelection` command, which the host runs through the `execute` seam below, inside the same
 * clipboard borrow the keyboard capture uses. There is no PowerShell side, no launch and no reach
 * check here: nothing about a terminal copy needs the Windows half of the machine.</p>
 */

/** The command that copies the active terminal's selection to the clipboard. */
export const TERMINAL_COPY = 'workbench.action.terminal.copySelection';

/** What a person is told when nothing was selected — the owner's own words. */
export const NO_TERMINAL_SELECTION = 'Select text in the terminal first.';

/**
 * What a finished borrow means for a terminal.
 *
 * <p>Nothing landed, or only white space landed: no selection. The text is NOT trimmed — the
 * selection is the passage, and indentation in a terminal is content.</p>
 */
export function terminalPassage(borrowed: Borrowed<unknown>): Capture {
  if (!borrowed.landed || borrowed.text.trim().length === 0) {
    return { text: '', failure: NO_TERMINAL_SELECTION };
  }

  return { text: borrowed.text, failure: '' };
}

/**
 * Copy the active terminal's selection through the clipboard, and give the clipboard back.
 *
 * <p>A copy command that REJECTS is not "select text first": the person did select, and a sentence
 * that blames them hides the real cause. It says what the command said. The borrow has restored the
 * clipboard by then, whichever way the command ended.</p>
 *
 * @param execute runs one VS Code command by id — `vscode.commands.executeCommand` in the host
 * @param clipboard the clipboard, injected
 */
export async function captureTerminalSelection(
  execute: (command: string) => PromiseLike<unknown>,
  clipboard: Clipboard,
): Promise<Capture> {
  try {
    return terminalPassage(await borrowClipboard(clipboard, () => execute(TERMINAL_COPY)));
  } catch (reason) {
    const said = reason instanceof Error ? reason.message : String(reason);

    return { text: '', failure: `The terminal selection could not be copied: ${said}` };
  }
}

const CREDS = 'Creds: ';

/** The chat's title: the terminal's name without a leading `Creds: ` — at the start only, and only when something is left. */
export function terminalLabel(name: string): string {
  return name.startsWith(CREDS) && name.length > CREDS.length ? name.slice(CREDS.length) : name;
}

/** The conversation of THIS terminal object, or a new one. It never matches by name and never answers `rekey`. */
export function terminalMatch(known: readonly KnownPanel[], terminal: object, name: string): SessionMatch {
  const label = terminalLabel(name);

  return known.some((panel) => panel.key === terminal)
    ? { kind: 'existing', key: terminal, label }
    : { kind: 'new', key: terminal, label };
}

/** One terminal of the window, narrowed to what the rules read. `key` is the host's `vscode.Terminal` object. */
export interface LiveTerminal {
  readonly key: object;
  readonly name: string;
  /** The terminal's process has ended (`exitStatus` is set). Such a terminal is never shown. */
  readonly exited: boolean;
}

export type NamedTerminal =
  | { readonly kind: 'one'; readonly key: object }
  | { readonly kind: 'none' }
  | { readonly kind: 'several'; readonly count: number };

/** The ONE live terminal of this exact name — or that there is none, or how many there are. It never picks. */
export function oneLiveNamed(live: readonly LiveTerminal[], name: string): NamedTerminal {
  const named = live.filter((one) => !one.exited && one.name === name);
  if (named.length === 1) {
    return { kind: 'one', key: named[0]!.key };
  }

  return named.length === 0 ? { kind: 'none' } : { kind: 'several', count: named.length };
}

/**
 * The panels a TAB may be matched against: every panel but the ones opened from a terminal.
 * Judged by the conversation's durable source, never by its key — a restored terminal conversation is keyed by a plain object.
 */
export function tabSourced(
  known: readonly KnownPanel[],
  sourceOf: (key: object) => ConversationSource | undefined,
): readonly KnownPanel[] {
  return known.filter((panel) => sourceOf(panel.key)?.kind !== 'terminal');
}

/** The registry's panels a tab may be matched against — `tabSourced`, read from each panel's own thread. */
export function tabPanels(panels: Pick<ChatPanels, 'known' | 'get'>): readonly KnownPanel[] {
  return tabSourced(panels.known(), (key) => threadUnder(panels, key)?.source);
}
