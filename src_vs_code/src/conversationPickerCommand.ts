import * as vscode from 'vscode';
import { openConversations, restoreConversation, revealConversation } from './chatCommand';
import { ChatPanels } from './chatPanels';
import { ConversationIndex } from './chatStoreCache';
import { ChatStoreFile } from './chatStoreFile';
import { PickerRow, pickerRows } from './conversationPicker';
import {
  byLastUsed,
  forgetting,
  mayForget,
  openElsewhere,
  opening,
  pickerTitle,
  scopeOf,
  scopeTooltip,
} from './conversationChoice';

/**
 * *CoAI: switch conversations…* — the widget, and nothing else.
 *
 * <p>Every decision this command takes has already been taken somewhere a test can reach:
 * `conversationPicker.ts` says what the rows ARE and in what order, `conversationChoice.ts` says what
 * choosing one means, what the title says, and whether a row may be forgotten. What is left here is a
 * QuickPick, five event handlers and the two accessors on `chatCommand.ts` that know which
 * conversations this window holds open. That division is the reason the feature can be tested at all:
 * an event handler is the one place in this extension no unit test can enter.</p>
 *
 * <h2>`createQuickPick`, which this repository does not otherwise use</h2>
 *
 * <p>There are six `showQuickPick` sites here and this is the first `createQuickPick`, so the
 * deviation is stated where a reader meets it rather than left to look like an oversight. The simple
 * form takes a list and gives back a choice; it has no instance, and three things this picker does
 * need one:</p>
 *
 * <ul>
 *   <li><b>Item buttons.</b> A trash on every closed row — `QuickPickItem.buttons` is delivered
 *   through `onDidTriggerItemButton`, which only an instance has.</li>
 *   <li><b>A title button.</b> The globe that widens the list from this folder to every folder.</li>
 *   <li><b>Rebuilding the list WITHOUT closing it.</b> This is the load-bearing one. Forgetting three
 *   conversations must be three presses, not three openings of the picker, and `showQuickPick`
 *   resolves — and disappears — the moment anything is chosen.</li>
 * </ul>
 *
 * <h2>It opens on what the index already holds</h2>
 *
 * <p>Ninety days of conversations is thousands of files, and a picker that lists a directory before it
 * draws anything is a picker nobody uses — which is why `chatStoreCache.ts` exists and is built in the
 * background at activation. So the rows go up immediately from what is in memory, and the refresh runs
 * BEHIND them, with the widget already on screen; when it lands the list is redrawn in place. A window
 * left open for a day would otherwise show a list a day old, with another window's conversations
 * missing from it.</p>
 *
 * <h2>One picker at a time, and one context key that says so</h2>
 *
 * <p>`coai.conversationsPickerOpen` is set while this list is on screen and cleared when it hides. It
 * is what scopes `Alt+Delete` to OUR picker rather than to every QuickPick the editor opens, and it is
 * why `forgetPickedConversation` below can be a command with no arguments: the row it acts on is the
 * one under the cursor of the picker that key is true for.</p>
 */

/** What the picker needs of the world. Everything else it asks `chatCommand.ts` for. */
export interface PickerDeps {
  /** The rows, in memory — built at activation, refreshed behind the widget. */
  readonly index: ConversationIndex;
  /** Where a chosen row's transcript is read from, at the moment it is chosen. */
  readonly store: ChatStoreFile;
  readonly extensionUri: vscode.Uri;
  /** This window's folder, the same answer every writer of the store gives. */
  readonly workspace: () => string;
  /** The clock. A parameter because every instant this draws — "3 hours ago" — is measured against it. */
  readonly clock?: () => number;
}

/** A row, and the item drawn for it. The row travels ON the item so no lookup can put them out of step. */
interface Item extends vscode.QuickPickItem {
  readonly row: PickerRow;
}

/** The trash on a closed row. Never drawn on an open one — see {@link mayForget} for why. */
const TRASH: vscode.QuickInputButton = {
  iconPath: new vscode.ThemeIcon('trash'),
  tooltip: 'Forget this conversation',
};

/**
 * The picker on screen right now, if there is one — so the `Alt+Delete` command has something to act
 * on, and so a second invocation replaces the first instead of stacking two lists.
 */
let live: { readonly hide: () => void; readonly forget: () => void } | undefined;

/** One row as VS Code wants it. The labels are the pure module's; nothing is composed here. */
const itemFor = (row: PickerRow): Item => {
  if (row.kind === 'section') {
    return { row, label: row.label, kind: vscode.QuickPickItemKind.Separator };
  }
  if (row.kind === 'conversation') {
    return {
      row,
      label: row.label,
      description: row.description,
      detail: row.detail,
      // A conversation open in a tab carries none — here or in another window: that tab is still
      // writing the record, so deleting the files under it would have the next push create them again.
      buttons: row.where === 'closed' ? [TRASH] : [],
    };
  }

  return { row, label: row.label, detail: row.detail };
};

const scopeButton = (everywhere: boolean): vscode.QuickInputButton => ({
  iconPath: new vscode.ThemeIcon('globe'),
  tooltip: scopeTooltip(everywhere),
});

/** Open the list of conversations. */
export function switchConversations(panels: ChatPanels, deps: PickerDeps): void {
  const now = deps.clock ?? Date.now;
  // A second invocation replaces the first: two of these on screen would both hold the context key,
  // and `Alt+Delete` would act on whichever was bound last rather than on the one being looked at.
  live?.hide();
  let everywhere = false;
  let onScreen = true;
  // One accept per picker. The widget is hidden by the first one and VS Code dispatches its events in
  // order, so a second cannot arrive — but what a second WOULD do is open the same conversation twice,
  // which is the one accident this command is written to prevent, and a latch is cheaper than the
  // argument that it cannot happen. (The plan round.)
  let chosen = false;
  const pick = vscode.window.createQuickPick<Item>();
  // Typing a model name or a word from the last answer filters — which is most of why a person opens
  // this rather than hunting through their tabs.
  pick.matchOnDescription = true;
  pick.matchOnDetail = true;
  // The list is rebuilt in place — by the scope button, by a forget, by the refresh landing — and
  // without this every rebuild would throw a person back to the top of their own list.
  pick.keepScrollPosition = true;
  // A warning message (a forget that failed, a conversation that has gone) can take the focus, and a
  // picker that vanished because it told you something would be a picker that cannot say anything.
  pick.ignoreFocusOut = true;
  pick.placeholder = 'Type to find a conversation by its title, its model, or the last thing said in it';

  /** Rebuild the list in place, keeping the row under the cursor where it can be kept. */
  const draw = (): void => {
    if (!onScreen) {
      // The picker was dismissed while something was being awaited — a forget in flight, the refresh
      // landing. Assigning to a disposed QuickPick throws, into a handler nobody is above; the honest
      // answer to "redraw a list that is not there" is to do nothing.
      return;
    }
    const held = pick.activeItems[0]?.row;
    const keep = held !== undefined && held.kind === 'conversation' ? held.id : '';
    const rows = pickerRows({
      open: byLastUsed(openConversations(panels)),
      stored: deps.index.entries(scopeOf(everywhere, deps.workspace())),
      index: deps.index.state(),
      // Asked at every draw, not once at opening: judged against this instant, so a window that has
      // gone quiet while the list was up stops holding its conversations hostage.
      elsewhere: deps.index.elsewhere(now()),
      workspace: deps.workspace(),
      everywhere,
      now: now(),
    });
    pick.title = pickerTitle(everywhere);
    pick.buttons = [scopeButton(everywhere)];
    pick.items = rows.map(itemFor);
    const again = pick.items.find((item) => item.row.kind === 'conversation' && item.row.id === keep);
    if (again !== undefined) {
      // The cursor stays on the conversation it was on. Without it, forgetting three in a row means
      // pressing Alt+Delete, looking, moving, pressing — for a list the person has not moved in.
      pick.activeItems = [again];
    }
  };

  /** Reveal it if this window has it, otherwise read it back off disk and reopen it. */
  const choose = async (row: Extract<PickerRow, { kind: 'conversation' }>): Promise<void> => {
    // REVEALED FIRST, whatever the row said. A live row is one this window holds and revealing it is
    // the whole answer. A closed one should not be open at all — the rows exclude what is held — but
    // the registry can move between the list being drawn and the row being pressed, and a second tab
    // for one record would give it two writers: the fork the store's swap exists to catch, caused by
    // us. Asking costs one walk of the registry.
    if (revealConversation(panels, row.id)) {
      return;
    }
    if (row.where === 'elsewhere') {
      // ASKED THIS WINDOW FIRST, and only then declined: between the list being drawn and the row
      // being pressed the other window may have closed and this one adopted it, and refusing on the
      // strength of a heartbeat read seconds ago would refuse a tab that is right here.
      void vscode.window.showWarningMessage(openElsewhere(row.label));

      return;
    }
    // NOT the metadata the row was drawn from. The index is built in the background and a row can be
    // pressed an hour later; what goes on screen is what is on disk at the moment of the press.
    const answer = opening(row.label, await deps.store.read(row.id));
    switch (answer.kind) {
      case 'reopen':
        // Its own key, and no panel: this is `restoreConversation`'s first caller without one. The
        // conversation comes back closed, with nothing running, exactly as it does after a reload.
        restoreConversation(panels, undefined, answer.record, deps.extensionUri).panel.reveal();

        return;
      case 'gone':
        // Off the list as well as said out loud, or the row is still there on the next keystroke and
        // the person presses it again.
        deps.index.drop(row.id);
        void vscode.window.showWarningMessage(answer.message);

        return;
      case 'refused':
        // It is there and could not be read. The row STAYS: nothing has been lost, and a permissions
        // error or a record a newer build wrote must not hide a conversation from the list.
        void vscode.window.showWarningMessage(answer.message);

        return;
      default: {
        // Exhaustive by name, as every decision in this feature is: a fourth answer added to
        // `Opening` must be a compile error here rather than a press that does nothing.
        const unhandled: never = answer;

        throw new Error(`a picker choice this build has no arm for: ${JSON.stringify(unhandled)}`);
      }
    }
  };

  pick.onDidTriggerButton(() => {
    everywhere = !everywhere;
    // Rebuilt, not reopened: the filter somebody has typed survives, which is the whole point of
    // widening the scope while looking for something.
    draw();
  });

  pick.onDidAccept(() => {
    const row = pick.selectedItems[0]?.row;
    if (chosen || row === undefined || row.kind !== 'conversation') {
      // TOTAL by construction rather than by care: a separator cannot be chosen at all, and a notice
      // and the offer to start one carry no id — there is nothing here for them to open and no
      // sentinel a crafted row could aim at. (Starting one is story C2's; this command never offers
      // that row.)
      return;
    }
    chosen = true;
    // Hidden BEFORE the work: what happens next is a tab appearing, and a list still covering it
    // would be a list the person has to dismiss to see what they asked for.
    pick.hide();
    void choose(row).catch((reason: unknown) => {
      // The outer edge of a detached call, and therefore a catch that says something: the store
      // answers in outcomes and never rejects, so anything arriving here is a defect.
      console.error('ConnectOtherAIs: a conversation could not be opened from the picker', reason);
      void vscode.window.showWarningMessage('That conversation could not be opened.');
    });
  });

  const forget = async (row: PickerRow | undefined): Promise<void> => {
    const decided = mayForget(row);
    if (decided.kind === 'refuse') {
      void vscode.window.showWarningMessage(decided.message);

      return;
    }
    const done = forgetting(decided.title, await deps.store.forget(decided.id));
    if (done.kind === 'failed') {
      void vscode.window.showWarningMessage(done.message);
      // Redrawn anyway, because the row is still there and the list must go on saying so.
      draw();

      return;
    }
    // Dropped from the index by hand: a refresh would find it gone eventually, and "eventually" is
    // after the person has looked at the row they just deleted.
    deps.index.drop(decided.id);
    draw();
  };

  /**
   * The two doors into forgetting, sharing one body — the trash button and the keybinding differ in
   * WHICH row they mean and in nothing else, and two copies of the catch would be two places for one
   * of them to start swallowing its failures.
   */
  const forgetRow = (row: PickerRow | undefined): void => {
    void forget(row).catch((reason: unknown) => {
      console.error('ConnectOtherAIs: a conversation could not be forgotten from the picker', reason);
      void vscode.window.showWarningMessage('That conversation could not be forgotten.');
    });
  };

  pick.onDidTriggerItemButton((pressed) => {
    // The row the button was drawn ON, not the one under the cursor: a mouse can press a button on a
    // row it never highlighted, and deleting the highlighted one instead would be the worst possible
    // reading of that press.
    forgetRow(pressed.item.row);
  });

  pick.onDidHide(() => {
    onScreen = false;
    live = undefined;
    // CLEARED. A key left true outlives the picker, and Alt+Delete would then belong to us in every
    // quick pick the editor opens afterwards — a delete keybinding pointed at somebody else's list.
    void vscode.commands.executeCommand('setContext', 'coai.conversationsPickerOpen', false);
    pick.dispose();
  });

  draw();
  live = { hide: () => pick.hide(), forget: () => forgetRow(pick.activeItems[0]?.row) };
  void vscode.commands.executeCommand('setContext', 'coai.conversationsPickerOpen', true);
  pick.show();

  // AND ONLY THEN the refresh, behind the rows that are already up. It surveys the directory, which
  // is the one thing that must never be on the way to the first frame.
  pick.busy = true;
  deps.index.refresh(now())
    .then((outcome) => {
      if (outcome.kind === 'unavailable') {
        // The rows on screen are the last good ones and the notice above them says so; this is for
        // whoever reads the log afterwards.
        console.warn(`ConnectOtherAIs: the conversation list could not be brought up to date — ${outcome.reason}`);
      }
      if (onScreen) {
        pick.busy = false;
        draw();
      }
    })
    .catch((reason: unknown) => {
      console.error('ConnectOtherAIs: the conversation index threw while the picker was open', reason);
      if (onScreen) {
        pick.busy = false;
      }
    });
}

/**
 * Forget the conversation under the cursor of the picker that is open.
 *
 * <p>No arguments, because a keybinding passes none and the row it means is the one being looked at.
 * It is bound to `Alt+Delete` scoped by `inQuickOpen && coai.conversationsPickerOpen`, so it can only
 * fire while this list is on screen — and if it somehow fires when it is not, there is nothing to act
 * on and nothing happens, which is the honest answer rather than a guess at which list was meant.</p>
 */
export function forgetPickedConversation(): void {
  live?.forget();
}
