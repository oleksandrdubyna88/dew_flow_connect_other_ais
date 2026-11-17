import type { ChatPanels } from './chatPanels';

/**
 * Bringing a conversation to the front, and moving its registration onto a tab or terminal — the decision, with no `vscode` in it.
 *
 * <p>Split out of `chatRegistry.ts` (E3.S2) so a unit test can load it: `chatRegistry.ts` reads the tab snapshots, which is
 * `vscode`. The host road is still `revealBound` there; it supplies the one fact only the window knows (is this key a terminal
 * of the window) and calls this.</p>
 */

/** Bring the panel under this key to the front, for a caller that already knows where it sits. */
export function revealUnder(panels: Pick<ChatPanels, 'get'>, key: object): void {
  panels.get(key)?.panel.reveal();
}

/**
 * Reveal a conversation this window already holds — moving its registration onto `onto` first, when that is where it belongs.
 *
 * <p>ONE rule for the two paths that reach a live conversation: *go to*'s reopen arm and the picker's own accept. They were two
 * copies of it and only one of them had it, so a conversation chosen from a narrowed picker was revealed and the tab it belongs to
 * left unbound for ever. (Two vendors, the second code round.)</p>
 *
 * <p><b>A live terminal's conversation is never moved</b> (E3.S2, J4): when `where` is a terminal of the window and is not
 * `onto`, the conversation is revealed where it is. Two live terminals of one name carry equal sources, and moving one's
 * conversation onto its namesake would make the answer arrive in the other terminal's chat with nothing looking wrong. The guard is
 * HERE, on the road both callers take, and not at the two callers. A conversation under a key that is not a terminal (restored by a
 * reload, or reopened by the picker: `{}`) is still moved onto the terminal — that is the person's own re-attach.</p>
 *
 * @param isLiveTerminal whether a key is a terminal of the window (the host reads `window.terminals`)
 */
export function revealBoundWith(
  panels: Pick<ChatPanels, 'get' | 'rekey'>,
  where: object,
  onto: object | undefined,
  isLiveTerminal: (key: object) => boolean,
): void {
  if (onto !== undefined && onto !== where && !isLiveTerminal(where)) {
    panels.rekey(where, onto);
    revealUnder(panels, onto);

    return;
  }
  revealUnder(panels, where);
}
