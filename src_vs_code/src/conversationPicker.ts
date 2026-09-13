import { ConversationMeta } from './chatStore';

/**
 * What the picker offers, decided without a host.
 *
 * <p>Pure, for the reason every decision in this feature is pure: the rows ARE the feature at the
 * moment a person uses it — which conversation they are looking at, in what order, described well
 * enough to tell two of them apart — and a rule about that buried inside a `createQuickPick` call is
 * a rule no test can reach. What is left on the `vscode` side is the widget.</p>
 *
 * <p>The shape is a list of TAGGED rows rather than a list of items with optional fields, because a
 * separator, a conversation, the offer to start a new one and the sentence shown when the store
 * could not be read are four different things that a caller must handle differently — and a union
 * makes the compiler ask.</p>
 */

/** The heading above conversations that are open in a tab right now. */
export const OPEN_SECTION = 'Open';

/** The heading above conversations that are only on disk. */
export const RECENT_SECTION = 'Recent';

/** The id of the row that starts a conversation rather than opening one. */
// A COLON, which `isSafeId` refuses: these two rows are not conversations, and an id that can never
// be a real one is what stops a crafted record ever being mistaken for the action row beside it.
export const NEW_ROW_ID = ':new';

/** The id of the row shown when the store could not be read at all. */
export const UNREADABLE_ROW_ID = ':unreadable';

/**
 * The most conversations a picker draws.
 *
 * <p>Ninety days at this operator's rate is hundreds, and a QuickPick that renders all of them is
 * slow to open for a list nobody scrolls to the end of. It is a CUT rather than a filter, and it is
 * stated on a row of its own: a list that silently stops is a list somebody searches in vain.</p>
 */
export const MOST_ROWS = 100;

/** One conversation a window currently holds open. */
export interface OpenConversation {
  readonly id: string;
  readonly title: string;
  readonly modelId: string;
  readonly turns: number;
}

export type PickerRow =
  | { readonly kind: 'section'; readonly label: string }
  | {
    readonly kind: 'conversation';
    readonly id: string;
    readonly label: string;
    readonly description: string;
    readonly detail: string;
    /** Whether it is open in a tab — the caller reveals one and reopens the other. */
    readonly live: boolean;
  }
  | { readonly kind: 'new'; readonly id: string; readonly label: string; readonly detail: string }
  | { readonly kind: 'unreadable'; readonly id: string; readonly label: string; readonly detail: string }
  | { readonly kind: 'empty'; readonly label: string; readonly detail: string }
  | { readonly kind: 'more'; readonly label: string };

export interface PickerInput {
  /** What this window holds open, in the order it wants them shown. */
  readonly open: readonly OpenConversation[];
  /** What the index holds, or that it could not be read — the distinction A2's typed listing exists for. */
  readonly stored: readonly ConversationMeta[] | 'unavailable';
  readonly workspace: string;
  /** Whether the person asked for every workspace rather than this one. */
  readonly everywhere: boolean;
  readonly now: number;
  /** The tab a new conversation would belong to, when the caller wants that offered. */
  readonly offerNew?: string;
}

/**
 * The rows, in the order they are shown.
 *
 * <p>Open first, because what is already in a tab is what a person is most often looking for — this
 * feature exists because ten of them are open at once — and because choosing one of those is a
 * reveal, which is instant, while choosing a closed one starts something.</p>
 */
export function pickerRows(input: PickerInput): readonly PickerRow[] {
  const first: PickerRow[] = input.offerNew === undefined ? [] : [newRow(input.offerNew)];
  if (input.stored === 'unavailable') {
    // NOT an empty list. They are opposite facts, and only one of them means "you have none" — which
    // is the whole reason the store's listing is typed. The offer to start one survives, because
    // starting a conversation needs nothing from a disk that will not answer.
    return [...first, {
      kind: 'unreadable',
      id: UNREADABLE_ROW_ID,
      label: 'Your saved conversations could not be read',
      detail: 'The folder they are kept in did not answer. Nothing has been lost; try again in a moment.',
    }];
  }

  const open = input.open.map(openRow);
  const held = new Set(input.open.map((one) => one.id));
  const closed = [...input.stored]
    .filter((one) => !held.has(one.id))
    .filter((one) => input.everywhere || one.workspace === input.workspace)
    .sort((left, right) => right.updatedAt - left.updatedAt);
  if (open.length === 0 && closed.length === 0) {
    return [...first, {
      kind: 'empty',
      label: input.everywhere ? 'No conversations yet' : 'No conversations in this folder yet',
      detail: input.everywhere
        ? 'Ask another AI about something and it will be here.'
        : 'Nothing here yet — or they belong to another folder. Press the globe to look everywhere.',
    }];
  }

  const shown = closed.slice(0, MOST_ROWS).map((one) => storedRow(one, input));
  const cut: PickerRow[] = closed.length > shown.length
    ? [{ kind: 'more', label: `… and ${closed.length - shown.length} older, not shown` }]
    : [];

  return [
    ...first,
    ...(open.length === 0 ? [] : [{ kind: 'section' as const, label: OPEN_SECTION }, ...open]),
    ...(shown.length === 0 ? [] : [{ kind: 'section' as const, label: RECENT_SECTION }, ...shown, ...cut]),
  ];
}

const newRow = (tab: string): PickerRow => ({
  kind: 'new',
  id: NEW_ROW_ID,
  label: `New conversation for ${tab}`,
  detail: 'Nothing is created until you choose this.',
});

const openRow = (one: OpenConversation): PickerRow => ({
  kind: 'conversation',
  id: one.id,
  label: one.title,
  description: [one.modelId, turnsIn(one.turns), 'open now'].filter((part) => part.length > 0).join(' · '),
  detail: '',
  live: true,
});

function storedRow(one: ConversationMeta, input: PickerInput): PickerRow {
  // WHERE, but only when it could be somewhere else. A folder name on every row of a list that is
  // all one folder is noise; on a list spanning several it is the thing telling two apart.
  const where = input.everywhere ? folderName(one.workspace) : '';

  return {
    kind: 'conversation',
    id: one.id,
    label: one.title,
    description: [one.modelId, turnsIn(one.turns), rowAge(input.now, one.updatedAt), where]
      .filter((part) => part.length > 0)
      .join(' · '),
    detail: one.lastLine,
    live: false,
  };
}

const turnsIn = (turns: number): string => (turns === 1 ? '1 turn' : `${turns} turns`);

/** The last segment of a path, which is what a person calls the project. */
function folderName(workspace: string): string {
  const parts = workspace.split(/[\\/]/u).filter((part) => part.length > 0);

  return parts[parts.length - 1] ?? 'no folder';
}

/**
 * How long ago, the way a person would say it.
 *
 * <p>A clock that has stepped backwards reads as `just now` rather than as a conversation from the
 * future: two machines writing one directory is the ordinary case here, and a row that says "in 3
 * hours" is a row nobody can act on.</p>
 */
export function rowAge(now: number, at: number): string {
  const ago = Math.max(0, now - at);
  const minutes = Math.floor(ago / 60_000);
  if (minutes < 1) {
    return 'just now';
  }
  if (minutes === 1) {
    return 'a minute ago';
  }
  if (minutes < 60) {
    return `${minutes} minutes ago`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return hours === 1 ? 'an hour ago' : `${hours} hours ago`;
  }
  const days = Math.floor(hours / 24);

  return days === 1 ? 'yesterday' : `${days} days ago`;
}
