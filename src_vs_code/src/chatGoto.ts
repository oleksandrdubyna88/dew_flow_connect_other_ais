import { ConversationMeta, ConversationRecord, ConversationSource, sameSource } from './chatStore';
import { IndexState } from './chatStoreCache';
import { rootOf } from './chatSource';

/**
 * *CoAI: go to conversation* — which conversation belongs to the tab somebody is looking at.
 *
 * <p>Pure, and the whole of the decision. Epic B's picker is a list a person searches BY HAND; this
 * is the other half of what the operator asked for — press once on the tab you are working in and
 * arrive in the conversation about it, two days later, without hunting. Story C1 gave every
 * conversation a durable source to make that possible; this is what reads it.</p>
 *
 * <h2>Five answers, and a title is never one of them</h2>
 *
 * <p>{@link Goto} is closed: reveal what is open, reopen the one that matches, offer the candidates
 * when there is more than one, offer to start one when there is none, and show everything when the
 * tab is not something a conversation can belong to. A NAME is never a key — `chatPanels.ts` records
 * why, and two Claude sessions can share a title — so nothing here matches on a label.</p>
 *
 * <h2>What it will not do</h2>
 *
 * <p><b>It never guesses.</b> Two candidates is the picker, not the newest of the two: the operator
 * ruled on this in as many words — *«Эвристика и угадывание в профессиональном инструменте — табу»* —
 * and a machine choosing which of your conversations you meant, then telling you afterwards, is the
 * failure this plan exists to avoid.</p>
 *
 * <p><b>It never answers from an index that could not be read.</b> An `unavailable` index is serving
 * its last good rows, and binding a tab to a record that may since have been forgotten is the same
 * guess wearing a disk. Such a store gets the picker, narrowed to what was last known, carrying the
 * reason — the person sees the candidates and chooses, and the press re-reads the record. (The plan
 * round, three vendors.)</p>
 *
 * <p><b>It never reads an empty index as an empty store.</b> The index is empty for a window's first
 * seconds, and `start` there would offer to create a second conversation for a tab that already has
 * one — precisely the duplicate this feature exists to prevent. {@link Goto} has a member for it.</p>
 */

/** What a tab IS, as far as this decision is concerned. */
export type TabKind =
  /** One of Claude Code's own chat panels. Its conversation is named by a session id. */
  | 'claude'
  /** A document a person is reading: `file` or `untitled`. Named by its uri. */
  | 'document'
  /** A terminal, the Output pane, a settings tab, our own chat tab — anything a conversation cannot belong to. */
  | 'other';

/** Why a narrowed picker is being shown, so the title can say it in the person's own terms. */
export type Narrowing =
  /** More than one saved conversation carries this tab's source. */
  | { readonly kind: 'several' }
  /** More than one Claude session answers to this tab's name, so its own identity is in doubt. */
  | { readonly kind: 'ambiguous session' }
  /** The store could not be read; these are the rows as they were last known. */
  | { readonly kind: 'unreadable'; readonly reason: string };

/**
 * What *go to* should do.
 *
 * <p>Exhaustive by name at every reader, like every other decision in this feature: a sixth answer
 * added here must be a compile error in the command rather than a press that does nothing.</p>
 */
export type Goto =
  /** This window already holds it. The commonest answer, and the cheapest. */
  | { readonly kind: 'reveal'; readonly id: string }
  /** Exactly one saved conversation is this tab's. Bind it to this tab. */
  | { readonly kind: 'reopen'; readonly meta: ConversationMeta }
  /** More than one answer, or a store that could not be read: let the person choose. */
  | { readonly kind: 'pick'; readonly among: readonly ConversationMeta[]; readonly why: Narrowing; readonly offer: string }
  /** Nothing is saved for this tab, and one could be started for it. */
  | { readonly kind: 'start'; readonly offer: string }
  /** The tab is not one a conversation belongs to: the whole list, which is never an error. */
  | { readonly kind: 'everything' }
  /** The list is not built yet. Nothing is opened and nothing is created on a guess. */
  | { readonly kind: 'building' };

export interface GotoAsked {
  readonly tab: {
    readonly kind: TabKind;
    /** What the tab is called. Used for the offer's wording only — never to match anything. */
    readonly label: string;
    /**
     * The document's own FILESYSTEM PATH, for a `document`; empty otherwise.
     *
     * <p>A path and not a uri, because what is done with it is compared against the workspace
     * roots, which are paths. The host converts — it is the half that knows how a uri is spelled —
     * exactly as story C1 does when it files a conversation, so the two agree by construction.
     * Passing the uri here quietly matched nothing: `file:///D:/...` is not inside `D:\...`, so
     * every tab fell back to the first root and the whole rule was a no-op.</p>
     */
    readonly path: string;
  };
  /**
   * The conversation this window already holds for this tab, if any — matched on the tab OBJECT by
   * the registry, never on a source. Empty when there is none.
   */
  readonly live: string;
  /**
   * This tab's own source, as the host resolved it: a file's uri, or the session id a Claude tab
   * pinned. `none` when the tab has one but it could not be resolved — an unpinned Claude tab whose
   * name answers to nothing, or to more than one.
   */
  readonly source: ConversationSource;
  /**
   * Whether a Claude tab's session is in DOUBT rather than simply unresolved: more than one session
   * answers to this name. `pinnable` decides it; this is its answer carried in.
   */
  readonly ambiguous: boolean;
  /** Every saved conversation whose source is this tab's — the index's answer, unfiltered by root. */
  readonly candidates: readonly ConversationMeta[];
  /** The window's workspace roots, to work out which one this tab belongs to. */
  readonly roots: readonly string[];
  /** Where a conversation goes when its tab is under no root — the same fallback story C1 files by. */
  readonly fallback: string;
  /**
   * Whether this filesystem treats two spellings of one name as the same name.
   *
   * <p>The host says, because only it knows the platform. Story C1 files a conversation by the same
   * rule, so the two halves agree about which root a path is in rather than disagreeing on a Linux
   * machine where `/work/App` and `/work/app` are two folders.</p>
   */
  readonly caseBlind: boolean;
  readonly index: IndexState;
}

/** What the offer to start one is called, for a tab whose own name is empty. */
export const UNNAMED_TAB = 'this tab';

/**
 * What *go to* should do about the tab somebody is looking at.
 *
 * <p>In order, and the order is the point: what is already open costs one lookup and is by far the
 * commonest answer, so it is asked first and asked about the TAB rather than about a source. Only
 * then does anything read the store.</p>
 */
export function goto(asked: GotoAsked): Goto {
  if (asked.live.length > 0) {
    // Already here. Nothing is read, nothing is decided — this is the ten-tabs case the whole
    // feature was asked for, and it must be instant.
    return { kind: 'reveal', id: asked.live };
  }
  if (asked.tab.kind === 'other') {
    // A terminal, the Output pane, our own chat tab. The operator's decision: a tab with nothing
    // behind it gets the LIST, never an error and never silence.
    return { kind: 'everything' };
  }
  const offer = offerFor(asked.tab.label);
  if (asked.index.kind === 'building') {
    // NOT an empty store. The index is empty for a window's first seconds, and `start` here would
    // offer to create a second conversation for a tab that already has one. (Three vendors.)
    return { kind: 'building' };
  }
  const mine = ours(asked);
  if (asked.index.kind === 'unavailable') {
    // Its last good rows are a statement about a moment that has passed. They are worth SHOWING —
    // they are probably right — but not worth binding a tab to without a person looking at them.
    return { kind: 'pick', among: mine, why: { kind: 'unreadable', reason: asked.index.reason }, offer };
  }
  if (asked.ambiguous) {
    // The tab's own identity is in doubt: more than one Claude session answers to this name, so
    // there is no source to match on and the candidates are whatever was passed in.
    return { kind: 'pick', among: mine, why: { kind: 'ambiguous session' }, offer };
  }
  if (mine.length > 1) {
    // Impossible by construction — one source belongs to one conversation, and C1 writes it once —
    // and answered honestly anyway, because the alternative is guessing which of two is yours.
    return { kind: 'pick', among: mine, why: { kind: 'several' }, offer };
  }
  const only = mine[0];

  return only === undefined ? { kind: 'start', offer } : { kind: 'reopen', meta: only };
}

/**
 * The saved conversations that are THIS tab's: its source, and its own workspace root.
 *
 * <p><b>The tab's root, not the window's roots.</b> Membership in the window's list is too weak: with
 * two roots open, a conversation filed under the first would attach to a tab under the second. The
 * root is worked out from the tab's own uri, by the same rule story C1 files a conversation with, so
 * the two halves agree by construction rather than by coincidence — and a tab under no root falls
 * back to the same answer C1 gives such a conversation. (Three vendors, the plan round.)</p>
 *
 * <p>A source of `none` matches NOTHING, including another `none`. That is `sameSource`'s rule and it
 * is the line this whole story rests on: every conversation written before C1 has no source, and
 * handing one of them to a tab that never owned it would be this feature's worst failure.</p>
 */
function ours(asked: GotoAsked): readonly ConversationMeta[] {
  if (asked.source.kind === 'none') {
    return [];
  }
  const here = belongsTo(asked);

  return asked.candidates.filter((meta) => sameSource(meta.source, asked.source) && meta.workspace === here);
}

/** The root this tab belongs to — its own, or the fallback for a tab under none of them. */
function belongsTo(asked: GotoAsked): string {
  const root = asked.tab.path.length === 0 ? '' : rootOf(asked.tab.path, asked.roots, asked.caseBlind);

  return root.length > 0 ? root : asked.fallback;
}

/**
 * What the offer to start a conversation is called.
 *
 * <p>An untitled document is eligible by kind and can have no name at all, which would have produced
 * an offer ending in nothing. (codex, the plan round.)</p>
 */
const offerFor = (label: string): string => (label.trim().length > 0 ? label : UNNAMED_TAB);

/**
 * The record the host should bind, once it has re-read it — or why it cannot.
 *
 * <p>A decision is a statement about a moment that has passed. Between `goto` answering `reopen` and
 * the host reading the record, another window can have forgotten it. This is the same division B3
 * made for the picker: decide on what is known, verify at the act.</p>
 */
export const bindable = (record: ConversationRecord | undefined, source: ConversationSource): boolean =>
  record !== undefined && sameSource(record.source, source);
