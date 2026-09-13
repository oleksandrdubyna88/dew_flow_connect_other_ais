import * as vscode from 'vscode';
import { Goto, GotoAsked, bindable, goto } from './chatGoto';
import { askedForGoto, restoreConversation, revealConversation, tabStillOpen, whereConversationSits } from './chatCommand';
import { ChatPanels } from './chatPanels';
import { ConversationIndex } from './chatStoreCache';
import { ChatStoreFile } from './chatStoreFile';
import { switchConversations } from './conversationPickerCommand';
import { narrowedTitle, stillListing } from './conversationChoice';

/**
 * *CoAI: go to conversation* — the door, and nothing else.
 *
 * <p>`chatGoto.ts` decides; this carries the decision out. The split is the same one every command in
 * this feature has, and here it is load-bearing: six answers, each of which touches the registry or
 * the disk, over a question with five inputs. A branch of that inside an event handler is a branch no
 * test in this repository can reach.</p>
 *
 * <h2>Binding is the thing that can go wrong</h2>
 *
 * <p>Everything else this command does is a reveal or a list. BINDING — registering a saved
 * conversation under a live tab — is the one act that changes what a tab owns, and it is guarded
 * three ways, each for a failure the plan round named:</p>
 *
 * <ul>
 *   <li><b>The record is re-read at the press</b> and checked with `bindable`, which compares the
 *   source AND the workspace. A decision is a statement about a moment that has passed; another
 *   window can have forgotten the conversation or re-filed it into another project in between.</li>
 *   <li><b>The tab must still be there, and still hold nothing.</b> A person can close it or open a
 *   chat in it while the record is being read or a picker is on screen.</li>
 *   <li><b>One press at a time.</b> The awaits above are long enough for a second chord, and what a
 *   second would build is a second panel for one conversation.</li>
 * </ul>
 *
 * <h2>Nothing is created on a guess</h2>
 *
 * <p>`start` does not open a conversation. It opens the picker with *New conversation for <tab>*
 * under the cursor, and nothing exists until that row is chosen — because starting one would resolve
 * a CLI and launch a vendor process for a conversation nobody has typed into, and this chord is one
 * a person can press by accident. (Two vendors, the plan round; it is also what the plan said before
 * my own brief for this story drifted from it.)</p>
 */

/** One press at a time. See the header — the awaits are long enough for a second chord. */
let running = false;

/** The command. `args` is ignored: the menus this appears in act on the active editor. */
export async function goToConversation(
  panels: ChatPanels,
  index: ConversationIndex,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
): Promise<void> {
  if (running) {
    return;
  }
  running = true;
  try {
    await arrive(panels, index, store, extensionUri);
  } finally {
    running = false;
  }
}

/** What to do about the tab somebody is looking at, and then the doing of it. */
async function arrive(
  panels: ChatPanels,
  index: ConversationIndex,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
): Promise<void> {
  const { asked, key } = await askedForGoto(panels, index);
  const answer = goto(asked);
  const picker = { index, store, extensionUri, workspace: () => asked.fallback };
  const offer = 'offer' in answer ? answer.offer : asked.tab.label;
  const tab = (): Bound | undefined => bindableTab(panels, key, asked.tab.label);
  switch (answer.kind) {
    case 'reveal':
      // The commonest answer and the cheapest: it is already here.
      revealConversation(panels, answer.id);

      return;
    case 'reopen':
      await bind(panels, store, extensionUri, answer, asked, key);

      return;
    case 'pick':
      // Every ambiguity this feature meets ends here: the person sees the candidates and chooses.
      // A `cross root` row is opened under its own key — that conversation belongs to another
      // project, and moving it because somebody went looking for it is a decision they did not ask
      // for.
      switchConversations(panels, picker, {
        rows: answer.among,
        title: narrowedTitle(answer.why, offer, answer.among.length),
        offer,
        startOnNew: false,
        ...bound(answer.bind ? tab() : undefined),
      });

      return;
    case 'start':
      // NOT a new conversation. The picker, with the offer under the cursor, and nothing created
      // until it is chosen.
      switchConversations(panels, picker, {
        rows: [],
        title: `No conversation for “${offer}” yet`,
        offer,
        startOnNew: true,
        ...bound(tab()),
      });

      return;
    case 'everything':
      // The operator's decision: a tab with nothing behind it gets the LIST, never an error and
      // never silence.
      switchConversations(panels, picker);

      return;
    case 'building':
      // The list is not read yet, so this cannot say whether the tab has a conversation — and
      // guessing would offer to create a second one for a tab that already has one.
      void vscode.window.showInformationMessage(stillListing(offer));

      return;
    default: {
      // EXHAUSTIVE BY NAME: a seventh answer must be a compile error here rather than a press that
      // does nothing.
      const unhandled: never = answer;

      throw new Error(
        `a go-to answer this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the answers it may give are reveal, reopen, pick, start, everything and building',
      );
    }
  }
}

/**
 * Bind the one conversation this tab's, having checked that it still is.
 *
 * <p>Three things can have changed since the decision: the record, the tab, and what the tab holds.
 * Each is re-checked, and a failure falls back to the PICKER rather than to starting one — the
 * conversation exists, something moved, and offering to create another would be the duplicate this
 * whole feature is here to prevent.</p>
 */
async function bind(
  panels: ChatPanels,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
  answer: Extract<Goto, { kind: 'reopen' }>,
  asked: GotoAsked,
  key: object | undefined,
): Promise<void> {
  const seen = await store.read(answer.meta.id);
  const record = seen.kind === 'record' ? seen.record : undefined;
  if (!bindable(record, asked.source, answer.meta.workspace, asked.caseBlind) || record === undefined) {
    // It moved, or it went. Say so and show what there is rather than inventing a second one.
    void vscode.window.showWarningMessage(
      `“${answer.meta.title}” is not where it was a moment ago — another window may have changed it.`,
    );

    return;
  }
  // ALREADY LIVE, under another key: the picker or a reload opened it without a tab. Move the
  // registration onto this tab rather than leaving the two halves disagreeing about what it owns —
  // this is the gap epic B recorded and named this story as the place to close. (Two vendors.)
  const where = whereConversationSits(panels, record.id);
  const tab = bindableTab(panels, key, asked.tab.label);
  if (where !== undefined) {
    if (tab !== undefined && where !== tab.key) {
      panels.rekey(where, tab.key);
    }
    revealConversation(panels, record.id);

    return;
  }
  restoreConversation(panels, undefined, record, extensionUri, tab).panel.reveal();
}

/** A tab a conversation may be bound to. */
interface Bound {
  readonly key: object;
  readonly label: string;
}

/** The optional field, spelled so `exactOptionalPropertyTypes` can tell absent from undefined. */
const bound = (tab: Bound | undefined): { bindTo?: Bound } => (tab === undefined ? {} : { bindTo: tab });

/**
 * The tab a conversation may be bound TO — or nothing, when it may not be.
 *
 * <p>Checked HERE rather than when the decision was made, because a person can close a tab or start
 * a chat in it while a record is read or a picker is on screen. A tab that has gone, or that now
 * holds a conversation of its own, takes no binding: what would otherwise happen is a conversation
 * registered against a tab nobody is looking at, or a live tab's contents swapped underneath
 * somebody. (codex and gemini, the plan round.)</p>
 */
function bindableTab(panels: ChatPanels, key: object | undefined, label: string): Bound | undefined {
  if (key === undefined || !tabStillOpen(key) || panels.get(key) !== undefined) {
    return undefined;
  }

  return { key, label };
}
