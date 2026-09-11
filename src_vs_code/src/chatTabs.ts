import { ChatMessage } from './chatPage';

/**
 * What is kept of a chat tab so a window reload does not take the conversation with it.
 *
 * <p>There is no `WebviewPanelSerializer` in this extension before this module, so a reload emptied
 * every chat tab: `retainContextWhenHidden` keeps a panel alive while it is HIDDEN, which is a
 * different thing entirely.</p>
 *
 * <p>No `vscode` import, on purpose. Every rule here — what is written, what a corrupt record does,
 * what is pruned and when — is a decision, and a decision inside the host is a decision no test can
 * reach. The host half is thirty lines that call this.</p>
 */

/** One conversation, as much of it as is worth carrying across a reload. */
export interface SavedTab {
  /**
   * The conversation's own id, minted when its tab is created and echoed back by the page.
   *
   * <p>Not the title. Two Claude Code sessions can both be called `main`, and `chatPanels.ts` exists
   * because of it — *"a registry keyed by name hands the second tab the first tab's conversation, an
   * answer that arrives and is simply about somebody else's question."* A title-keyed store would
   * have reproduced that defect at reload, and worse: the two records would have overwritten each
   * other on the way in, so the collision could not even be detected on the way out.</p>
   */
  readonly id: string;
  /** When this record was last written, for the pruning below. */
  readonly savedAt: number;
  readonly title: string;
  readonly passage: string;
  readonly modelId: string;
  readonly messages: readonly ChatMessage[];
  /**
   * Whether this conversation came from a Claude Code session rather than from a file.
   *
   * <p>OPTIONAL, so a record written before this field existed is still read rather than dropped —
   * the alternative was bumping {@link TAB_VERSION}, which discards every stored conversation to
   * carry one boolean. Absent reads as `true`, which every tab was when it was written.</p>
   */
  readonly fromSession?: boolean;
}

/** The `globalState`/`workspaceState` key, in the dotted form `coai.usageForgottenBefore` set. */
export const TAB_STORE_KEY = 'coai.chatTabs';

/**
 * The shape this module writes. Bumping it discards every older record rather than guessing at it.
 *
 * <p>A stored object is read back by a build that may be newer or older than the one that wrote it,
 * and a half-understood transcript is worse than none: it would be rendered as a conversation the
 * person recognises, with pieces missing.</p>
 */
export const TAB_VERSION = 1;

/** Records older than this are dropped: a week-old conversation is not one anybody is resuming. */
export const KEEP_FOR_MS = 7 * 24 * 60 * 60 * 1000;

/**
 * And no more than this many, newest first.
 *
 * <p>Age alone does not bound the store: somebody who opens thirty tabs in an afternoon has thirty
 * records within the week, and `workspaceState` is a bounded thing whose overflow is silent.</p>
 */
export const KEEP_TABS = 20;

const isMessage = (value: unknown): value is ChatMessage => {
  const row = value as { role?: unknown; text?: unknown } | null;

  return row !== null && typeof row === 'object'
    && (row.role === 'you' || row.role === 'model') && typeof row.text === 'string';
};

const isTab = (value: unknown): value is SavedTab => {
  const tab = value as Partial<Record<keyof SavedTab, unknown>> | null;

  return tab !== null && typeof tab === 'object'
    && typeof tab.id === 'string' && tab.id.length > 0
    && typeof tab.savedAt === 'number' && Number.isFinite(tab.savedAt)
    && typeof tab.title === 'string' && typeof tab.passage === 'string'
    && typeof tab.modelId === 'string'
    && Array.isArray(tab.messages) && tab.messages.every(isMessage)
    // Absent is legitimate — an older record. Present and not a boolean is a record this build
    // cannot trust, and it is dropped with the rest of them rather than half-read.
    && (tab.fromSession === undefined || typeof tab.fromSession === 'boolean');
};

/**
 * The records in a stored value, and nothing else.
 *
 * <p>Every field is checked because every field is rendered, and what is stored can be edited by
 * hand, written by another version, or half-written by a host that was killed. A record that does
 * not survive this is DROPPED rather than repaired: the alternative is a transcript with a hole in
 * it that reads as if the model said nothing.</p>
 */
export function tabsFrom(raw: unknown): readonly SavedTab[] {
  const stored = raw as { version?: unknown; tabs?: unknown } | null;
  if (stored === null || typeof stored !== 'object' || stored.version !== TAB_VERSION) {
    return [];
  }

  return Array.isArray(stored.tabs) ? stored.tabs.filter(isTab) : [];
}

/** What `tabsFrom` reads back. */
export function stored(tabs: readonly SavedTab[]): { readonly version: number; readonly tabs: readonly SavedTab[] } {
  return { version: TAB_VERSION, tabs };
}

/** Newest first, then cut by age and by count. */
export function pruned(tabs: readonly SavedTab[], now: number): readonly SavedTab[] {
  return [...tabs]
    .sort((left, right) => right.savedAt - left.savedAt)
    .filter((tab) => now - tab.savedAt < KEEP_FOR_MS)
    .slice(0, KEEP_TABS);
}

/** This conversation, replacing whatever was held for it, with the store pruned on the way. */
export function remembered(tabs: readonly SavedTab[], tab: SavedTab, now: number): readonly SavedTab[] {
  return pruned([tab, ...tabs.filter((held) => held.id !== tab.id)], now);
}

/** This conversation forgotten — for a tab the person closed, not one a reload took away. */
export function forgotten(tabs: readonly SavedTab[], id: string): readonly SavedTab[] {
  return tabs.filter((tab) => tab.id !== id);
}

export const savedTab = (tabs: readonly SavedTab[], id: string): SavedTab | undefined =>
  tabs.find((tab) => tab.id === id);

/**
 * The sentence a restored tab opens with. Shown in the page, where the person is looking.
 *
 * <p>It has to be honest about two different things at once: the process that was answering is gone
 * and is not coming back, and the conversation itself is not — the next question opens a new session
 * and hands it everything above. A tab that simply reappeared full of text would be a tab that looks
 * alive, and the first question would then quietly cost a whole transcript with nothing having said
 * so.</p>
 */
export function reloadedNote(modelId: string): string {
  return 'This conversation was closed by a window reload. Ask again to continue it — the next '
    + `question opens a new ${modelId} session and carries everything above across to it.`;
}

/** The two methods of a VS Code memento this module needs, and no more of it than that. */
export interface TabStore {
  get(key: string): unknown;
  update(key: string, value: unknown): Thenable<void>;
}

/**
 * The store, written in order.
 *
 * <p>Every write is read-modify-write of one key, so two tabs saving at the same moment would both
 * read the same old value and the later write would drop the earlier one's conversation. They queue,
 * and the queue recovers from a rejection instead of staying poisoned — a storage failure that
 * silently stopped every later write would be indistinguishable from this bug.</p>
 */
export class ChatTabMemory {
  private queued: Promise<void> = Promise.resolve();

  constructor(private readonly store: TabStore, private readonly clock: () => number = Date.now) {}

  /** What is held right now, validated. */
  held(): readonly SavedTab[] {
    return tabsFrom(this.store.get(TAB_STORE_KEY));
  }

  saved(id: string): SavedTab | undefined {
    return savedTab(this.held(), id);
  }

  remember(tab: Omit<SavedTab, 'savedAt'>): void {
    this.write((tabs) => remembered(tabs, { ...tab, savedAt: this.clock() }, this.clock()));
  }

  forget(id: string): void {
    this.write((tabs) => forgotten(tabs, id));
  }

  /** On activation: a week of closed conversations is not something to carry forever. */
  prune(): void {
    this.write((tabs) => pruned(tabs, this.clock()));
  }

  /** Resolves when everything asked for so far has been written — the tests' way in, and only that. */
  settled(): Promise<void> {
    return this.queued;
  }

  private write(change: (tabs: readonly SavedTab[]) => readonly SavedTab[]): void {
    const step = async (): Promise<void> => {
      await this.store.update(TAB_STORE_KEY, stored(change(this.held())));
    };
    this.queued = this.queued.then(step, step).catch((reason: unknown) => {
      // Swallowed so the queue survives — a chain left rejected would stop every later write, which
      // is the same silence as the bug this module exists to end. Said out loud in the host's own
      // log rather than in a notification: nobody can act on a memento that would not take a write,
      // and one per turn would be a wall of them.
      console.error('ConnectOtherAIs: a chat tab could not be saved for a reload', reason);
    });
  }
}
