import { ConversationRecord } from './chatStore';
import { IndexScope } from './chatStoreCache';
import { ForgetOutcome, ReadOutcome } from './chatStoreFile';
import { Narrowing } from './chatGoto';
import { OpenConversation, PickerRow } from './conversationPicker';

/**
 * What CHOOSING something in the picker means — the other half of `conversationPicker.ts`, and pure
 * for the same reason.
 *
 * <p>That module decides what a person is shown; this one decides what happens when they press one
 * of it. Between the two there is nothing left for the widget but a `createQuickPick` and its
 * events — which matters because an event handler is the one place in this extension no test can
 * reach, and "the conversation you picked is no longer there" is exactly the sentence somebody will
 * only ever read once, at the worst moment, when it has to be right.</p>
 *
 * <h2>The same question, asked for a picker instead of for a tab</h2>
 *
 * <p>`chatRestore.ts` already decides what each {@link ReadOutcome} means — for the reload
 * serializer, which holds a panel VS Code has handed it and must decide whether to keep it. Its
 * sentences are written for that panel ("this tab keeps its place until it can be read") and its
 * `absent` arm DISPOSES. Neither fits here: a picker holds no tab, so absent means a row to take off
 * a list and a sentence to show, and a notice tab is not among the answers. So this is a second
 * decision over the same union rather than a call to that one — and both are exhaustive by name, so
 * a fifth answer added to the store is a compile error in two places rather than a silent arm in
 * either.</p>
 */

/** What choosing a stored row came to, once the store has been asked what is actually there. */
export type Opening =
  /** The record as the store holds it NOW — never the metadata the row was drawn from. */
  | { readonly kind: 'reopen'; readonly record: ConversationRecord }
  /** It is not there any more: say so, and take the row off the list. */
  | { readonly kind: 'gone'; readonly message: string }
  /** It is there and could not be opened. The row STAYS — nothing about it has been lost. */
  | { readonly kind: 'refused'; readonly message: string };

/**
 * What forgetting one came to.
 *
 * <p>Success carries no sentence, deliberately. The row disappearing from a picker that stayed open
 * is the answer, and a notification for every trash press is the interruption the operator refused a
 * dialog to avoid — *«Модальные окна на каждое базовое действие превращают софт в пытку»* — wearing a
 * smaller hat. A failure speaks, because then the row is still there and nothing else would explain
 * why.</p>
 */
export type Forgetting =
  | { readonly kind: 'forgotten' }
  | { readonly kind: 'failed'; readonly message: string };

/** Whether the row under the cursor may be forgotten at all. */
export type MayForget =
  | { readonly kind: 'forget'; readonly id: string; readonly title: string }
  | { readonly kind: 'refuse'; readonly message: string };

/**
 * What to say when somebody chooses a conversation another window is holding.
 *
 * <p>The one row the picker will not act on, and therefore the one that must explain itself. VS Code
 * gives an extension no way to raise a window it is not running in, so "switch to it for them" is not
 * among the things that could have been built here; what is left is to say where it is and why this
 * window is not going to make a second copy. The alternative — reopening it — gives one record two
 * writers, and the store would fork the conversation under a new id and report it afterwards, which is
 * the product causing the exact accident its compare-and-swap exists to catch.</p>
 *
 * <p>It names the tab rather than the window, because a pid is not something anybody can act on.</p>
 */
export const openElsewhere = (title: string): string =>
  `“${title}” is already open in another VS Code window. Switch to that window to carry on with it —`
  + ' reopening it here would split it in two.';

/** What the picker's title says, which is the only place the scope is visible. */
export const pickerTitle = (everywhere: boolean): string =>
  everywhere ? 'Conversations in every folder' : 'Conversations in this folder';

/**
 * What the title says when *go to* could not answer on its own and is asking.
 *
 * <p>The picker is the same list; the title is what makes it an ANSWER rather than a shrug. Somebody
 * who pressed *go to* expecting to arrive somewhere is owed the reason they did not — and each of
 * the three reasons wants a different sentence, because what they should do about it differs: choose
 * between two conversations, tell two Claude sessions apart, or try again in a moment.</p>
 *
 * <p>It names the TAB, not the conversations: the tab is what they were looking at, and it is the
 * thing all the candidates have in common.</p>
 */
export function narrowedTitle(why: Narrowing, tab: string, among: number): string {
  switch (why.kind) {
    case 'several':
      return `${among} conversations could belong to “${tab}” — which one?`;
    case 'ambiguous session':
      return `More than one Claude session is called “${tab}” — which conversation did you mean?`;
    case 'unreadable':
      return `Conversations for “${tab}”, as they were last read — the folder did not answer just now`;
    default: {
      // EXHAUSTIVE BY NAME, like every other answer in this file: a fourth reason must be a compile
      // error rather than a picker with no title.
      const unhandled: never = why;

      throw new Error(
        `a narrowing this build has no sentence for: ${JSON.stringify(unhandled)}`
        + ' — the reasons it may give are several, ambiguous session and unreadable',
      );
    }
  }
}

/**
 * What *go to* says when the list is not built yet.
 *
 * <p>It opens nothing and creates nothing: an index that is still being read is empty, and a store
 * that is empty and a store nobody has listed yet are different facts. Offering to start a
 * conversation here would offer a SECOND one for a tab that already has one.</p>
 */
export const stillListing = (tab: string): string =>
  `The list of your conversations is still being built, so this cannot say yet whether “${tab}” has one.`
  + ' Try again in a moment.';

/**
 * What the title button offers — what pressing it WILL do, never what is on screen.
 *
 * <p>A toggle labelled with its current state is the oldest way to make somebody press it twice.</p>
 */
export const scopeTooltip = (everywhere: boolean): string =>
  everywhere ? 'Show only this folder’s conversations' : 'Show conversations from every folder';

/** What the index is asked for. This window's folder is named in it; nothing else knows which that is. */
export const scopeOf = (everywhere: boolean, workspace: string): IndexScope =>
  everywhere ? { kind: 'everywhere' } : { kind: 'workspace', workspace };

/**
 * The open conversations, newest use first — a NEW array, because the registry's is not ours to sort.
 *
 * <p>The registry hands them back in the order its map iterates, which is the order tabs were opened,
 * and that is not the order anybody looks for one in. Ties are broken by id so that two tabs opened
 * by one keypress have an order a person can build a habit on rather than one that changes between
 * two openings of the same picker. The same rule the index publishes its own rows by.</p>
 */
export const byLastUsed = (open: readonly OpenConversation[]): readonly OpenConversation[] =>
  [...open].sort((left, right) => right.updatedAt - left.updatedAt || left.id.localeCompare(right.id));

/**
 * What the store's answer means for a row somebody has just pressed.
 *
 * <p><b>Why the store is asked at all</b>, when the row already carries a title, a model and a turn
 * count: those came from an index built in the background, and the row can be pressed an hour later.
 * In between, another window can have written into that conversation or forgotten it entirely. So the
 * transcript that goes on screen is the one read at the moment of the press, and the three ways that
 * read can fail are three different sentences.</p>
 *
 * @param title the conversation's name as the row showed it — what the person pressed, so what the
 *   sentence must name, even when the record it belonged to can no longer be read
 */
export function opening(title: string, seen: ReadOutcome): Opening {
  switch (seen.kind) {
    case 'record':
      return { kind: 'reopen', record: seen.record };
    case 'absent':
      // The one arm that takes the row away. It is the honest answer to a store that no longer has
      // it, and the alternative — opening a tab for a conversation that is nowhere — is what the
      // reload serializer already refuses for the same reason.
      return {
        kind: 'gone',
        message: `“${title}” is no longer in the store — another window may have forgotten it.`
          + ' It has been taken off this list.',
      };
    case 'incompatible':
      // NOT gone. A record a newer build wrote is perfectly safe, and taking its row off the list
      // would hide a conversation that a build one version along opens without trouble.
      return { kind: 'refused', message: `“${title}” could not be opened: ${seen.reason}. Nothing has been deleted.` };
    case 'unavailable':
      return { kind: 'refused', message: `“${title}” could not be read just now: ${seen.reason}. Try again in a moment.` };
    default: {
      // EXHAUSTIVE BY NAME, the shape `restoreDecision` and `nextAfterSave` already use here: a fifth
      // answer added to `ReadOutcome` is a compile error rather than an arm that quietly opens
      // nothing. What arrives anyway is a defect, and it says which values were legal.
      const unhandled: never = seen;

      throw new Error(
        `a conversation store answer this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the answers it may give are record, absent, incompatible and unavailable',
      );
    }
  }
}

/** What removing one came to. See {@link Forgetting} for why success says nothing. */
export function forgetting(title: string, done: ForgetOutcome): Forgetting {
  switch (done.kind) {
    case 'ok':
      return { kind: 'forgotten' };
    case 'failed':
      return { kind: 'failed', message: `“${title}” could not be forgotten: ${done.reason}.` };
    default: {
      // EXHAUSTIVE BY NAME, like every other answer read in this file. A ternary treated everything
      // that was not `ok` as a failure, so a third outcome added to the store would have been
      // reported to the person as one — silently, and with the wrong sentence. (codex, the code
      // round; the constraint was my own.)
      const unhandled: never = done;

      throw new Error(
        `a conversation store forget answer this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the answers it may give are ok and failed',
      );
    }
  }
}

/**
 * Whether the list must be rebuilt for what has just been typed.
 *
 * <p>The picker draws at most a hundred conversations, and QuickPick filters what it was
 * GIVEN — so the hundred-and-first conversation could not be found by typing its own title, which is
 * the one thing this list exists to do. The fix is to filter before the cut; this is the rule for
 * when that has to happen again.</p>
 *
 * <p>A set drawn for `"pay"` already contains every match for `"paym"`, because matching is a
 * substring test and extending a query can only narrow it — so while somebody types forward, the
 * widget's own filter is enough and nothing is rebuilt. Deleting a character widens the question and
 * the set must be read again. And a draw that HIT the cap is incomplete for anything, so the next
 * keystroke rebuilds whatever it was.</p>
 */
export const mustRedraw = (drawn: string, next: string, cut: boolean): boolean =>
  cut || !next.startsWith(drawn);

/**
 * Whether this row may be forgotten — and the sentence when it may not.
 *
 * <p>TOTAL over the row union by construction rather than by care: only a `conversation` carries an
 * id, so there is nothing for the other three kinds to act on and nothing a caller could pass by
 * mistake. A conversation open in a tab is refused although it has one: that tab is still writing the
 * record, so deleting the files under it would have the next push create them again — the row would
 * come back and the person would have been told a lie. That holds wherever the tab is, which is why
 * `elsewhere` is refused by the same rule and not by a different one: the window that would recreate
 * the files is simply not this one, and this window cannot even close the tab to stop it. That is also
 * why no trash button is drawn on either; this is the keybinding's half of the one rule, and
 * `Alt+Delete` is pressed wherever the cursor happens to be.</p>
 */
export function mayForget(row: PickerRow | undefined): MayForget {
  if (row === undefined || row.kind !== 'conversation') {
    return { kind: 'refuse', message: 'There is no conversation under the cursor — move to one, then press it again.' };
  }
  if (row.where === 'here') {
    return { kind: 'refuse', message: `“${row.label}” is open in a tab. Close the tab first, and then it can be forgotten.` };
  }
  if (row.where === 'elsewhere') {
    return {
      kind: 'refuse',
      message: `“${row.label}” is open in another VS Code window. Close it there, and then it can be forgotten.`,
    };
  }

  return { kind: 'forget', id: row.id, title: row.label };
}
