import type { ChatPanels } from './chatPanels';
import type { ConversationSource } from './chatStore';
import { threadUnder } from './chatThread';
import type { Capture } from './selectionCapture';
import type { KnownPanel, SessionMatch } from './sessionKey';

/**
 * The terminal as a conversation source: the text the person copied, naming its conversation, finding its panel — with no `vscode` in it.
 *
 * <p>The rules are pure values and take their seams as parameters. The one exception is `tabPanels`, which reads the chat registry
 * and the thread table once to say which panels a tab may match; it is the only function here that touches shared state.</p>
 *
 * <p><b>The door reads what the person copied.</b> A drag in the Claude CLI (or in `tmux`) is that program's own selection, already
 * copied by it, and is not a VS Code selection: a copy command then copies nothing (change 1.2, the first real-window
 * run). So the person copies, and the door reads the clipboard once. It is given a READ function only, so it cannot write the
 * clipboard or run a command.</p>
 *
 * <p><b>No keystroke, ever.</b> A synthetic Ctrl+C reaches a terminal as SIGINT: in `ssh`, `tmux` or the Claude CLI that
 * interrupts the program instead of copying anything.</p>
 */

/** What a person is told when the clipboard holds no text — the owner's own words. */
export const NOTHING_COPIED = 'Copy the text first, then press Ask other AI.';

/**
 * What the clipboard's text means for a terminal.
 *
 * <p>Empty or white space only (an image reads as empty): nothing was copied. The text is NOT trimmed — what the person copied
 * is the passage, and indentation in a terminal is content.</p>
 */
export function terminalPassage(text: string): Capture {
  if (text.trim().length === 0) {
    return { text: '', failure: NOTHING_COPIED };
  }

  return { text, failure: '' };
}

/**
 * Read the text the person copied, once. It never writes, and it never rejects.
 *
 * <p>A clipboard that cannot be read is not "copy the text first": the person may have copied, and a sentence that blames them
 * hides the real cause. It says what the read said.</p>
 *
 * @param read reads the clipboard's text — `vscode.env.clipboard.readText` in the host
 */
export async function readCopiedText(read: () => PromiseLike<string>): Promise<Capture> {
  try {
    return terminalPassage(await read());
  } catch (reason) {
    const said = reason instanceof Error ? reason.message : String(reason);

    return { text: '', failure: `The clipboard could not be read: ${said}` };
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
