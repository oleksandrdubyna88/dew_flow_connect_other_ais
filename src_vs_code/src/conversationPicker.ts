import { ConversationMeta } from './chatStore';
import { IndexState } from './chatStoreCache';

/**
 * What the picker offers, decided without a host.
 *
 * <p>Pure, for the reason every decision in this feature is pure: the rows ARE the feature at the
 * moment a person uses it — which conversation they are looking at, in what order, described well
 * enough to tell two of them apart — and a rule about that buried inside a `createQuickPick` call is
 * a rule no test can reach. What is left on the `vscode` side is the widget.</p>
 *
 * <p>The shape is a list of TAGGED rows rather than a list of items with optional fields, because a
 * separator, a conversation, the offer to start a new one and a notice are four different things that
 * a caller must handle differently — and a union makes the compiler ask. {@link PickerRow} says what
 * choosing each of them means.</p>
 */

/** The heading above conversations that are open in a tab right now. */
export const OPEN_SECTION = 'Open';

/** The heading above conversations that are only on disk. */
export const RECENT_SECTION = 'Recent';

/**
 * The most conversations a picker draws.
 *
 * <p>Ninety days at this operator's rate is hundreds, and a QuickPick that renders all of them is
 * slow to open for a list nobody scrolls to the end of. It is a CUT rather than a filter, and it is
 * stated on a row of its own: a list that silently stops is a list somebody searches in vain.</p>
 *
 * <p><b>It is applied after {@link PickerInput.query}, never before.</b> A cut that came first left
 * the hundred-and-first conversation unreachable by any amount of typing, because QuickPick filters
 * only what it was handed — so the list could not do the one thing it exists for. What is cut now is
 * the newest hundred OF THE MATCHES.</p>
 */
export const MOST_ROWS = 100;

/**
 * One conversation a window currently holds open — described by the same facts as a stored row.
 *
 * <p>Two open conversations sharing a title, a model and a turn count are exactly the case the
 * registry exists to handle, and the last line and the age are what tell them apart; a row that
 * omitted them made the open ones the least distinguishable rows on the list. (gemini, the code
 * round.)</p>
 */
export interface OpenConversation {
  readonly id: string;
  readonly title: string;
  readonly modelId: string;
  readonly turns: number;
  /** The last thing said in it, as the store's `lastLine` carries it; empty when nothing has been said yet. */
  readonly lastLine: string;
  /** When something was last said in it — or when it was opened, for one that has said nothing. */
  readonly updatedAt: number;
}

/**
 * Where a conversation IS — and therefore what choosing its row does.
 *
 * <p>Three states rather than open-or-not, because the third one was drawn as the second and that was
 * the defect three reviewers found independently. `here` is a tab in THIS window: choosing it reveals
 * that tab. `closed` is on disk and nowhere open: choosing it reads it back. `elsewhere` is a tab in
 * ANOTHER window, which this one cannot reveal — VS Code gives no extension a way to raise a window it
 * is not running in — and must not reopen: a second tab for one record is a second writer, and the
 * store's compare-and-swap would then fork the conversation and tell the person about it afterwards.
 * A fork this product caused itself is the worst kind, so the row says where it is and declines.</p>
 */
export type Whereabouts = 'here' | 'elsewhere' | 'closed';

/** What a notice row says instead of a conversation. */
export type PickerNotice = 'building' | 'unreadable' | 'empty' | 'more';

/**
 * A row, tagged by what CHOOSING it means — and only a `conversation` carries an id.
 *
 * <p>Pressing Enter on any non-separator row of a QuickPick fires accept. So the union states, per
 * row, what an accept handler may do: open a `conversation` (reveal it when `live`, reopen it
 * otherwise); start one on `new`; and NOTHING on a `notice`, which has nothing to open — no `id`, so a
 * widget cannot so much as try, and no sentinel id that a crafted record would have to be kept from.
 * A `section` is a separator and cannot be chosen at all. That a notice is not openable used to be a
 * rule the widget had to remember; it is now a field the compiler refuses to find. (The code round.)</p>
 */
export type PickerRow =
  | { readonly kind: 'section'; readonly label: string }
  | {
    readonly kind: 'conversation';
    readonly id: string;
    readonly label: string;
    readonly description: string;
    readonly detail: string;
    /** Where it is — the caller reveals `here`, reopens `closed`, and declines `elsewhere`. */
    readonly where: Whereabouts;
  }
  | { readonly kind: 'new'; readonly label: string; readonly detail: string }
  | { readonly kind: 'notice'; readonly notice: PickerNotice; readonly label: string; readonly detail: string };

export interface PickerInput {
  /** What this window holds open, in the order it wants them shown. */
  readonly open: readonly OpenConversation[];
  /**
   * What the index holds, IN THE ORDER IT PUBLISHES THEM — newest first, sorted once when the rows
   * were published. Nothing here sorts: a second sort was a sort of the whole store, before the cut to
   * a hundred, on every keystroke of a filter.
   */
  readonly stored: readonly ConversationMeta[];
  /**
   * Where the index stands. `building` is the window's first seconds — the index is empty until the
   * sweep and the first refresh finish, and a picker that read that as "no conversations" would say so
   * over a full store. `unavailable` is a directory that would not answer; `stored` then holds the last
   * good rows the index kept, and the notice says they may be behind.
   */
  readonly index: IndexState;
  /**
   * What OTHER windows announce they hold open. Never this window's own tabs — those are `open`
   * above, and revealing one is the cheapest answer the picker has.
   */
  readonly elsewhere: ReadonlySet<string>;
  readonly workspace: string;
  /** Whether the person asked for every workspace rather than this one. */
  readonly everywhere: boolean;
  /**
   * Whether these rows were CHOSEN for this question rather than gathered from the store.
   *
   * <p>A narrowed list is an answer somebody already worked out — the conversations that could
   * belong to this tab — and filtering it again by workspace throws away rows that were selected on
   * purpose. It did: the picker is handed the window's FIRST root as its workspace, so a narrowed
   * pick for a tab under the second root came up empty, and so did the *cross root* case, whose
   * entire subject is a conversation filed under another root. The globe that would have widened it
   * is suppressed for narrowed lists, so there was no way out of the empty list either.</p>
   *
   * <p>The query filter still applies — that is the person typing, not a rule about roots.</p>
   */
  readonly narrowed: boolean;
  /**
   * What the person has typed, matched BEFORE the hundred-row cut.
   *
   * <p>QuickPick filters the items it was given, and this list is capped — so without this the
   * hundred-and-first conversation could not be found by typing its own title, which is the one
   * thing the list exists for. Empty means no filter, and then the cut is simply the newest
   * hundred.</p>
   */
  readonly query: string;
  readonly now: number;
  /** The tab a new conversation would belong to, when the caller wants that offered. */
  readonly offerNew?: string;
}

/**
 * The rows, in the order they are shown.
 *
 * <p>Open first, because what is already in a tab is what a person is most often looking for — this
 * feature exists because ten of them are open at once — and because choosing one of those is a
 * reveal, which is instant, while choosing a closed one starts something. The open ones come from
 * the registry, not the index, so they are shown whatever state the index is in: a disk that will not
 * answer, or an index still being built, takes nothing away from a tab that is already open.</p>
 */
export function pickerRows(input: PickerInput): readonly PickerRow[] {
  const first: PickerRow[] = input.offerNew === undefined ? [] : [newRow(input.offerNew)];
  const open = input.open.map((one) => openRow(one, input.now));
  const opened: PickerRow[] = open.length === 0 ? [] : [section(OPEN_SECTION), ...open];
  if (input.index.kind === 'building') {
    return [...first, ...opened, buildingNotice()];
  }
  const held = new Set(input.open.map((one) => one.id));
  const closed = input.stored.filter((one) => !held.has(one.id)
    && (input.narrowed || input.everywhere || one.workspace === input.workspace)
    && matches(one, input.query));
  const recent = recentRows(closed, input.now, input.elsewhere);
  if (input.index.kind === 'unavailable') {
    // NOT an empty list. They are opposite facts, and only one of them means "you have none" — which
    // is the whole reason the index's state is typed. The offer to start one survives, because
    // starting a conversation needs nothing from a disk that will not answer; so do the last good
    // rows, said to be from before the disk stopped answering.
    return [...first, ...opened, unreadableNotice(input.index.reason, recent.length > 0), ...recent];
  }
  if (open.length === 0 && closed.length === 0) {
    return [...first, emptyNotice(input.everywhere)];
  }

  return [...first, ...opened, ...recent];
}

/** The Recent section: the newest hundred as given, each saying where it is when that could differ, and the cut stated. */
function recentRows(closed: readonly ConversationMeta[], now: number, elsewhere: ReadonlySet<string>): readonly PickerRow[] {
  const shown = closed.slice(0, MOST_ROWS);
  if (shown.length === 0) {
    return [];
  }
  const where = folderLabels(shown.map((one) => one.workspace));
  const cut: PickerRow[] = closed.length > shown.length ? [moreNotice(closed.length - shown.length)] : [];

  return [
    section(RECENT_SECTION),
    ...shown.map((one) => storedRow(one, where.get(one.workspace) ?? '', now, elsewhere.has(one.id))),
    ...cut,
  ];
}

/**
 * Whether a conversation answers what has been typed — the same three fields the widget's own filter
 * reads, so narrowing here can never hide a row the widget would have shown.
 *
 * <p>Title, model and last line, case-blind and by substring. It runs BEFORE the cut to a hundred,
 * which is the whole point: the cut is what made an older conversation unfindable, and a filter
 * applied after it searches only the hundred newest. (codex, the code round.)</p>
 */
const matches = (one: ConversationMeta, query: string): boolean => {
  if (query.length === 0) {
    return true;
  }
  const looking = query.toLocaleLowerCase();

  return [one.title, one.modelId, one.lastLine].some((field) => field.toLocaleLowerCase().includes(looking));
};

const section = (label: string): PickerRow => ({ kind: 'section', label });

const newRow = (tab: string): PickerRow => ({
  kind: 'new',
  label: `New conversation for ${tab}`,
  detail: 'Nothing is created until you choose this.',
});

const buildingNotice = (): PickerRow => ({
  kind: 'notice',
  notice: 'building',
  label: 'Your saved conversations are still being listed',
  detail: 'The list is being built; open this again in a moment.',
});

const unreadableNotice = (reason: string, rowsFollow: boolean): PickerRow => ({
  kind: 'notice',
  notice: 'unreadable',
  label: 'Your saved conversations could not be read',
  detail: `The folder they are kept in did not answer (${reason}). Nothing has been lost; try again in a moment.`
    + `${rowsFollow ? ' The list below is as it was last read, and may be behind.' : ''}`,
});

const emptyNotice = (everywhere: boolean): PickerRow => ({
  kind: 'notice',
  notice: 'empty',
  label: everywhere ? 'No conversations yet' : 'No conversations in this folder yet',
  detail: everywhere
    ? 'Ask another AI about something and it will be here.'
    : 'Nothing here yet — or they belong to another folder. Press the globe to look everywhere.',
});

const moreNotice = (older: number): PickerRow => ({
  kind: 'notice',
  notice: 'more',
  label: `… and ${older} older, not shown`,
  detail: '',
});

const openRow = (one: OpenConversation, now: number): PickerRow => ({
  kind: 'conversation',
  id: one.id,
  label: one.title,
  description: [one.modelId, turnsIn(one.turns), rowAge(now, one.updatedAt), 'open now'].filter((part) => part.length > 0).join(' · '),
  detail: one.lastLine,
  where: 'here',
});

const storedRow = (one: ConversationMeta, folder: string, now: number, elsewhere: boolean): PickerRow => ({
  kind: 'conversation',
  id: one.id,
  label: one.title,
  description: [
    one.modelId,
    turnsIn(one.turns),
    rowAge(now, one.updatedAt),
    folder,
    // Said on the row rather than only in the sentence that follows a press: a person scanning this
    // list should be able to see which conversations this window can actually take them to.
    ...(elsewhere ? ['open in another window'] : []),
  ].filter((part) => part.length > 0).join(' · '),
  detail: one.lastLine,
  where: elsewhere ? 'elsewhere' : 'closed',
});

const turnsIn = (turns: number): string => (turns === 1 ? '1 turn' : `${turns} turns`);

/**
 * WHERE each conversation is — said only when it could be somewhere else, and only as much as tells
 * them apart.
 *
 * <p>Nothing when every row is one folder: a folder name on every row of a list that is all one
 * folder is noise. The folder's name when the names differ. And for two projects whose folders share
 * a name — `coai` under `rsd` and `coai` under `other` — the folder above, and above that, until the
 * labels differ: `rsd\coai` beside `other\coai`, in the path's own separators. A conversation with
 * no folder behind it is `no folder`. Two paths that are one folder spelled twice, with nothing in the
 * spelling to tell them apart, stay alike — which is the truth — and the widening stops there.</p>
 */
export function folderLabels(workspaces: readonly string[]): ReadonlyMap<string, string> {
  const distinct = [...new Set(workspaces)];
  if (distinct.length < 2) {
    return new Map(distinct.map((one) => [one, '']));
  }
  const labels = new Map(distinct.map((one) => [one, folderName(one, 1)]));
  for (let depth = 2; ; depth += 1) {
    const clashing = distinct.filter((one) => distinct.some((other) => other !== one && labels.get(other) === labels.get(one)));
    const widened = clashing.filter((one) => folderName(one, depth) !== labels.get(one));
    if (widened.length === 0) {
      return labels;
    }
    for (const one of widened) {
      labels.set(one, folderName(one, depth));
    }
  }
}

/** The last `depth` segments of a path, in its own separators — what a person calls the project. */
function folderName(workspace: string, depth: number): string {
  const parts = workspace.split(/[\\/]/u).filter((part) => part.length > 0);
  if (parts.length === 0) {
    return 'no folder';
  }

  return parts.slice(-depth).join(workspace.includes('\\') ? '\\' : '/');
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
