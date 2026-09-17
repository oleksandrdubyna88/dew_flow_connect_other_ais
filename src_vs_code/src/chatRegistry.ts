import { ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { recordOf } from './chatPersist';
import { metaOf } from './chatStore';
import { snapshots } from './chatCapture';
import { OpenConversation } from './conversationPicker';

/**
 * What this window holds open, and how a conversation is brought to the front.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. The registry itself is `chatPanels.ts`; this is what
 * the rest of the product asks it — which ids are open, so a heartbeat can say none of them is
 * debris; what the picker should show as a live row; where a conversation sits when the only name
 * anybody has for it is the one the STORE knows; and the four ways a tab is revealed or rebound.</p>
 *
 * <p>Read from the registry every time, never cached: a tab restored a moment ago must count.</p>
 */

/**
 * Which conversations this window holds open, by their store ids — what its heartbeat announces so
 * that no other window sweeps one of them by its age. Read from the registry each time, never cached:
 * a tab restored a moment ago must count.
 */
export function heldConversationIds(panels: ChatPanels): readonly string[] {
  return panels.known().flatMap(({ key }) => {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);

    return thread === undefined ? [] : [thread.saveId];
  });
}

/**
 * What this window holds open, described the way a STORED row is — for the picker's *Open* section.
 *
 * <p>A list of ids is what the heartbeat above needs and is not a list a person can choose from. Two
 * open conversations sharing a title are exactly the case the registry exists to handle, and the
 * model, the turn count and the last line are what tell them apart; a row that omitted them made the
 * open ones the least distinguishable rows on the list.</p>
 *
 * <p><b>Through `metaOf`, not by counting here.</b> A stored row is derived from a record by that
 * function, and a second derivation of "how many turns" and "the last line" would be two ways for an
 * open conversation and a closed one to describe the same thing differently — a conversation would
 * change its description the moment its tab closed. So a record is built for the thread and the same
 * derivation is applied to it; nothing is written.</p>
 *
 * <p>It lives here, with the accessor above, because the `Thread` map is private to this file: the
 * registry knows keys and labels, and every fact a row carries is on the thread. No decision is taken
 * here — the ORDER is `conversationChoice.ts`'s and the rows are `conversationPicker.ts`'s.</p>
 */
export function openConversations(panels: ChatPanels): readonly OpenConversation[] {
  return panels.known().flatMap(({ key }) => {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (thread === undefined) {
      return [];
    }
    // The record this conversation WOULD be saved as, stamped with when it was last used rather than
    // with now — `recordOf` takes that instant precisely so a caller that is not saving can say what
    // it means.
    const meta = metaOf(recordOf(thread, thread.usedAt));

    return [{
      id: meta.id,
      title: meta.title,
      modelId: meta.modelId,
      turns: meta.turns,
      lastLine: meta.lastLine,
      updatedAt: meta.updatedAt,
    }];
  });
}

/**
 * Where a conversation sits in the registry, by the id the STORE knows it by.
 *
 * <p>`ChatPanels.keyOf` answers for the entry's own id, which is an object a page carries; this
 * answers for the `saveId`, which is the only name a saved record has. *Go to* needs it to move a
 * conversation the picker or a reload opened without a tab onto the tab it belongs to.</p>
 */
export function whereConversationSits(panels: ChatPanels, saveId: string): object | undefined {
  for (const { key } of panels.known()) {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (thread?.saveId === saveId) {
      return key;
    }
  }

  return undefined;
}

/** Bring the panel under this key to the front, for a caller that already knows where it sits. */
export function revealUnder(panels: ChatPanels, key: object): void {
  panels.get(key)?.panel.reveal();
}

/**
 * Reveal a conversation this window already holds — moving its registration onto `onto` first, when
 * that is where it belongs.
 *
 * <p>ONE rule for the two paths that reach a live conversation: *go to*'s reopen arm and the picker's
 * own accept. They were two copies of it and only one of them had it, so a conversation chosen from a
 * narrowed picker was revealed and the tab it belongs to left unbound for ever — the next press asked
 * the same question again. (Two vendors, the second code round.)</p>
 */
export function revealBound(panels: ChatPanels, where: object, onto: object | undefined): void {
  if (onto !== undefined && onto !== where) {
    panels.rekey(where, onto);
    revealUnder(panels, onto);

    return;
  }
  revealUnder(panels, where);
}

/**
 * Is this tab still on screen?
 *
 * <p>Asked immediately before a conversation is bound to it. A person can close a tab, or switch
 * away and open a chat in it, while a record is being read or a picker is on screen — and binding a
 * conversation to a tab that has gone registers it against something nobody is looking at.
 * (codex, the plan round.)</p>
 */
export function tabStillOpen(key: object): boolean {
  return snapshots().all.some((tab) => tab.key === key);
}

/**
 * Bring the tab holding this conversation to the front, and say whether there was one.
 *
 * <p>By the STORE id, because that is the only name the picker has for a conversation: its rows come
 * from metadata files, and the live key is a `vscode.Tab` object that no row can carry. A caller that
 * gets `false` has asked for a conversation this window does not hold — the registry moved under the
 * picker between the list being drawn and the row being pressed — and must fall back to reopening it
 * rather than doing nothing.</p>
 */
export function revealConversation(panels: ChatPanels, id: string): boolean {
  for (const { key } of panels.known()) {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (entry !== undefined && thread?.saveId === id) {
      entry.panel.reveal();

      return true;
    }
  }

  return false;
}
