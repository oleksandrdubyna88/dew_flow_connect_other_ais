import * as vscode from 'vscode';
import { Bound, Goto, GotoAsked, bindTarget, goto, terminalStillThere } from './chatGoto';
import {
  askFromTerminal,
  chatWithOtherAi,
} from './chatCommand';
import { restoreConversation } from './chatConversationRestore';
import { askedForGoto, askedForTerminal } from './chatSessionJoin';
import { liveTerminals, confirmReattach } from './terminalHost';
import { revealBound, revealConversation, tabStillOpen, whereConversationSits } from './chatRegistry';
import { ChatPanels } from './chatPanels';
import { ConversationIndex } from './chatStoreCache';
import { ChatStoreFile } from './chatStoreFile';
import { ConversationRecord } from './chatStore';
import { Narrowed, switchConversations } from './conversationPickerCommand';
import { notify } from './notify';
import { narrowedTitle, stillListing } from './conversationChoice';

/**
 * *CoAI: go to conversation* — the door, and nothing else.
 *
 * <p>`chatGoto.ts` decides; this carries the decision out. The split is the same one every command in
 * this feature has, and here it is load-bearing: six answers, each of which touches the registry or
 * the disk, over a question with five inputs. A branch of that inside an event handler is a branch no
 * test in this repository can reach.</p>
 *
 * <h2>Binding is the thing that can go wrong, and it is decided AT THE PRESS</h2>
 *
 * <p>Everything else this command does is a reveal or a list. BINDING — registering a saved
 * conversation under a live tab — is the one act that changes what a tab owns. Three reviewers found
 * the same defect in the first draft: the target tab was worked out BEFORE a picker that a person may
 * browse for a minute, so a row chosen at the end could be bound to a tab that had since closed or
 * gained a conversation of its own. Nothing is decided in advance now. {@link bindingTo} is called
 * when a row is accepted, with the record the store has just answered with, and it says no unless
 * every one of these still holds:</p>
 *
 * <ul>
 *   <li>the record is still this tab's — source AND workspace, through `bindable`;</li>
 *   <li>the tab is still open;</li>
 *   <li>the tab still holds no conversation of its own.</li>
 * </ul>
 *
 * <p>And one press at a time, because the awaits are long enough for a second chord and what a
 * second would build is a second panel for one conversation.</p>
 *
 * <h2>Nothing is created on a guess</h2>
 *
 * <p>`start` does not open a conversation. It opens the picker with *New conversation for <tab>*
 * under the cursor, and nothing exists until that row is chosen — because starting one would resolve
 * a CLI and launch a vendor process for a conversation nobody has typed into, and this chord is one
 * a person can press by accident. (Two vendors, the plan round; it is also what the plan said before
 * my own brief for this story drifted from it.)</p>
 */

/**
 * One way into go-to: what the press asked, and the three things that differ between a TAB and a TERMINAL. `arrive` takes it whole, so
 * the tab road and the terminal road share every arm of the exhaustive switch.
 *
 * <p>`key` is what a conversation would be bound to — the tab, or the terminal; `stillThere` is the press-time question of whether it
 * still is; `start` is the door *New conversation* runs: the tab road keeps the file door, the terminal road runs the terminal door
 * for THAT terminal, never the tab door, which would read a file tab.</p>
 */
interface Road {
  readonly asked: GotoAsked;
  readonly key: object | undefined;
  readonly stillThere: (key: object) => boolean;
  readonly start: () => void;
}

/**
 * One press at a time.
 *
 * <p>Module-level, like the picker's own registration, because an extension host has one of this
 * command for its whole life. It is set INSIDE the try: outside it, a synchronous throw anywhere in
 * the work would never reach the `finally` and the command would be dead until the window was
 * reloaded. (gemini, the code round.)</p>
 */
let running = false;

/** Run one press under the latch that both commands share: a second press while one is working does nothing. */
async function pressed(work: () => Promise<void>): Promise<void> {
  if (running) {
    return;
  }
  try {
    running = true;
    await work();
  } finally {
    running = false;
  }
}

/** The command. It reads the active tab; the menus it appears in are the editor's and the panel's. */
export async function goToConversation(
  panels: ChatPanels,
  index: ConversationIndex,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
): Promise<void> {
  await pressed(async () => {
    const { asked, key } = await askedForGoto(panels, index);
    await arrive(panels, store, extensionUri, index, { asked, key, stillThere: tabStillOpen, start: startingFor(panels, extensionUri) });
  });
}

/**
 * *CoAI: go to this terminal's conversation* — go-to pressed IN a terminal, for THAT terminal (E3.S2).
 *
 * <p>A terminal is not a tab, so this never goes through the tab match: it reads the terminal it is handed (the window's active one),
 * asks `askedForTerminal`, and shares every arm of `arrive` with the tab road under the SAME `running` latch. It runs no re-attach of
 * its own — it keeps its explicit-choice path: the person pressed go-to in THIS terminal, and two live namesakes or several records
 * give a list. A bind it makes is final. When it SHOWS this terminal's own conversation (`reveal`), that is a use, and a bind
 * re-attach made becomes final (`confirmReattach`, only while the plan for the name still answers `bound`).</p>
 */
export async function goToFromTerminal(
  panels: ChatPanels,
  index: ConversationIndex,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
  terminal: vscode.Terminal | undefined,
): Promise<void> {
  if (terminal === undefined) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'goToConversation',
      code: 'no-terminal-for-goto',
      title: 'There is no terminal here — open one, then press go to.',
    });

    return;
  }
  await pressed(async () => {
    // The name is read ONCE at the press: the question is built from it and the confirmation judges the plan under it.
    const name = terminal.name;
    const asked = askedForTerminal(panels, index, terminal, name);
    const road: Road = {
      asked,
      key: terminal,
      stillThere: (key) => terminalStillThere(liveTerminals(vscode.window.terminals), key, asked.source),
      start: () => {
        void askFromTerminal(panels, extensionUri, terminal);
      },
    };
    if (await arrive(panels, store, extensionUri, index, road) === 'reveal') {
      confirmReattach(panels, terminal, name);
    }
  });
}

/** What to do about the tab or terminal somebody is looking at, and then the doing of it. Says which answer it carried out. */
async function arrive(
  panels: ChatPanels,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
  index: ConversationIndex,
  road: Road,
): Promise<Goto['kind']> {
  const { asked } = road;
  const answer = goto(asked);
  const show = (narrowed?: Narrowed): void =>
    switchConversations(panels, { index, store, extensionUri, workspace: () => asked.fallback }, narrowed);
  const offer = 'offer' in answer ? answer.offer : asked.tab.label;
  switch (answer.kind) {
    case 'reveal':
      // The commonest answer and the cheapest: it is already here.
      revealConversation(panels, answer.id);

      return answer.kind;
    case 'reopen':
      await bind(panels, store, extensionUri, answer, road, show);

      return answer.kind;
    case 'pick':
      // Every ambiguity this feature meets ends here: the person sees the candidates and chooses. A
      // `cross root` row carries no binding at all — that conversation belongs to another project,
      // and moving it because somebody went looking for it is a decision they did not ask for.
      show({
        rows: answer.among,
        title: narrowedTitle(answer.why, offer, answer.among.length),
        offer,
        startOnNew: false,
        onNew: road.start,
        ...(answer.bind ? { bindTo: bindingTo(panels, road) } : {}),
      });

      return answer.kind;
    case 'start':
      // NOT a new conversation. The picker, with the offer under the cursor, and nothing created
      // until it is chosen.
      show({
        rows: [],
        title: `No conversation for “${offer}” yet`,
        offer,
        startOnNew: true,
        onNew: road.start,
        bindTo: bindingTo(panels, road),
      });

      return answer.kind;
    case 'everything':
      // The operator's decision: a tab with nothing behind it gets the LIST, never an error and
      // never silence.
      show();

      return answer.kind;
    case 'building':
      // The list is not read yet, so this cannot say whether the tab has a conversation — and
      // guessing would offer to create a second one for a tab that already has one.
      void notify({
        as: 'information',
        class: 'refusal',
        source: 'goToConversation',
        code: 'conversation-index-still-building',
        title: stillListing(offer),
      });

      return answer.kind;
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
 * <p>A failure here does not stop at a sentence. Somebody who asked to go to a conversation and is
 * told it moved needs somewhere to go: they are told, and then given the list, so what they were
 * after is one search away rather than gone. (Two vendors, the code round — my own plan had said the
 * same and my implementation had quietly dropped it.)</p>
 */
async function bind(
  panels: ChatPanels,
  store: ChatStoreFile,
  extensionUri: vscode.Uri,
  answer: Extract<Goto, { kind: 'reopen' }>,
  road: Road,
  show: (narrowed?: Narrowed) => void,
): Promise<void> {
  const seen = await store.read(answer.meta.id);
  const record = seen.kind === 'record' ? seen.record : undefined;
  const tab = record === undefined ? undefined : bindingTo(panels, road)(record);
  if (record === undefined) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'goToConversation',
      code: 'conversation-moved-under-us',
      subject: answer.meta.id,
      title: `“${answer.meta.title}” is not where it was a moment ago — another window may have changed it.`,
    });
    show();

    return;
  }
  // ALREADY LIVE, under another key: the picker or a reload opened it without a tab. Move the
  // registration onto this tab rather than leaving the two halves disagreeing about what it owns —
  // this is the gap epic B recorded and named this story as the place to close. (Two vendors.)
  const where = whereConversationSits(panels, record.id);
  if (where !== undefined) {
    // Through the key already in hand rather than a second walk of the registry, and through the
    // same helper the picker's own accept uses, so the two cannot disagree about when a live
    // conversation is moved onto a tab.
    revealBound(panels, where, tab?.key);

    return;
  }
  restoreConversation(panels, undefined, record, extensionUri, tab).panel.reveal();
}

/**
 * What choosing *New conversation for <tab>* does.
 *
 * <p>The ordinary door, on the tab the row names. A conversation is born from a PASSAGE here — there
 * is no other way to begin one and there must not be a second — so this hands over to
 * `chatWithOtherAi` rather than building an empty one, and somebody with nothing selected is told to
 * copy something first in the same sentence every other door says it with. Opened, nothing sent: the
 * row promised that nothing is created until it is chosen, not that anything would be asked.</p>
 *
 * <p>REFUSED when that tab is no longer the one in front. The door reads whatever is active when it
 * runs, and a person who moved away while the picker was up would otherwise get a conversation for
 * the tab they moved TO — created on a guess, which is the single thing this answer exists to
 * avoid. (gemini, the second code round: the offer could not be completed at all.)</p>
 */
function startingFor(panels: ChatPanels, extensionUri: vscode.Uri): () => void {
  // THERE IS NO GUARD HERE ANY MORE, and that is the finding rather than an omission.
  //
  // It began as "do not start a conversation for the wrong tab" and went through two shapes, each
  // refusing far more than it caught. By OBJECT: a `vscode.Tab` is replaced whenever a tab changes
  // and a Claude Code tab renames itself as the assistant works, so it refused every press — which
  // is how the operator met it. By LABEL: three reviewers found three more false refusals between
  // them, and each is ordinary rather than exotic. Two tabs called `README.md` make the offer
  // impossible for BOTH of them, for ever. An untitled buffer has no label, so it can never start
  // one. And a Claude tab renamed while the picker is open refuses the press that follows, which is
  // the original bug wearing the fix.
  //
  // What the guard was protecting against is a person moving to another tab while the picker is up
  // and getting a conversation for the tab they moved TO. That is what the ordinary chord does from
  // wherever it is pressed, the chat is titled after the tab it was actually started from, and
  // nothing is bound or overwritten — so it is visible and undoable. Weighed against three certain
  // refusals it is not worth having, and a guard that fails toward "no" is not safer than none when
  // "no" is the thing the person cannot get past.
  return () => {
    void chatWithOtherAi(panels, extensionUri, [], false);
  };
}

/**
 * The question "may this conversation be bound to that tab or terminal", asked at the moment of the press.
 *
 * <p>Everything it checks can change while a record is read or a picker is on screen, which is why it is a function handed forward
 * rather than an answer worked out in advance. The decision is `bindTarget` (pure, `chatGoto.ts`): the record is still this one's,
 * the road still has it (`stillThere`: the tab is open, or the terminal is in the window, alive and still the same name), and it holds no
 * conversation of its own. (Three findings, the code round.)</p>
 */
function bindingTo(panels: ChatPanels, road: Road): (record: ConversationRecord) => Bound | undefined {
  return (record) => bindTarget(record, road.asked, road.key, {
    stillThere: road.stillThere,
    holdsOne: (key) => panels.known().some((one) => one.key === key),
  });
}
