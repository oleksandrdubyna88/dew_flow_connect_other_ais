import { ConversationMeta, ConversationRecord, ConversationSource, sameSource } from './chatStore';
import { IndexState } from './chatStoreCache';
import { filedUnder, sameRoot } from './chatSource';

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
  /** This tab's conversation exists, filed under ANOTHER project of this window. */
  | { readonly kind: 'cross root' }
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
  if (!eligible(asked.tab.kind)) {
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
  const here = belongsTo(asked);
  // EVERY candidate of this root, whatever its source. The ambiguous branch needs these and only
  // these: when a Claude title answers to more than one session there IS no source to match on, and
  // filtering by one leaves nothing — a picker asking which conversation somebody meant, with no
  // conversations in it. Four reviewers found that, and it is the one defect in this story that
  // would have shipped looking like a working feature. (The code round.)
  const around = asked.candidates.filter((meta) => sameRoot(meta.workspace, here, asked.caseBlind));
  if (asked.index.kind === 'unavailable') {
    // Its last good rows are a statement about a moment that has passed. They are worth SHOWING —
    // they are probably right — but not worth binding a tab to without a person looking at them.
    return { kind: 'pick', among: around, why: { kind: 'unreadable', reason: asked.index.reason }, offer };
  }
  if (asked.ambiguous) {
    // The TAB's own identity is in doubt, not the conversations'. So the candidates are shown as they
    // came, narrowed only by the root they belong to.
    return { kind: 'pick', among: around, why: { kind: 'ambiguous session' }, offer };
  }
  const mine = around.filter((meta) => sameSource(meta.source, asked.source));
  if (mine.length > 1) {
    // Impossible by construction — one source belongs to one conversation, and C1 writes it once —
    // and answered honestly anyway, because the alternative is guessing which of two is yours.
    return { kind: 'pick', among: mine, why: { kind: 'several' }, offer };
  }
  const only = mine[0];
  if (only !== undefined) {
    return { kind: 'reopen', meta: only };
  }
  const elsewhere = asked.candidates.filter((meta) => sameSource(meta.source, asked.source));
  if (elsewhere.length > 0) {
    // THIS TAB'S CONVERSATION, filed under another project. Dropping it to silence would offer to
    // start a new one while the old one sits a folder away — the duplicate this whole rule exists to
    // prevent, produced by the rule itself. It is shown, and the person decides. (Two vendors.)
    return { kind: 'pick', among: elsewhere, why: { kind: 'cross root' }, offer };
  }

  return { kind: 'start', offer };
}

/** Whether a conversation can belong to a tab of this kind — exhaustive, so a fourth kind must decide. */
function eligible(kind: TabKind): boolean {
  switch (kind) {
    case 'claude':
    case 'document':
      return true;
    case 'other':
      return false;
    default: {
      // A new tab kind must not fall silently into the document path, where it would be matched by a
      // path it has not got. (codex, the code round; the constraint was my own.)
      const unhandled: never = kind;

      throw new Error(
        `a tab kind this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the kinds it may be are claude, document and other',
      );
    }
  }
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
/**
 * The root this tab belongs to — its own, or the fallback for a tab under none of them.
 *
 * <p>Through {@link filedUnder}, which is the function story C1 FILES a conversation with. Two copies
 * of that rule would drift the day either changed, and the failure would be silent: a record filed
 * one way and looked up the other, answering `start` for a conversation that exists. (gemini, the
 * code round.)</p>
 */
const belongsTo = (asked: GotoAsked): string =>
  filedUnder(asked.tab.path, asked.roots, asked.fallback, asked.caseBlind);

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
 *
 * <p><b>BOTH halves of the ownership key</b>, not just the source. `goto` decides on source AND
 * workspace, so checking only the source here would let a record another window re-filed into a
 * different project bind to this tab anyway — which is precisely what the workspace rule was added to
 * prevent, undone at the last step. (Three findings, the code round.)</p>
 *
 * <p>It has no caller yet: C3 is the story that re-reads and binds. The contract is fixed and pinned
 * by its test now, while the reasoning that produced it is still to hand.</p>
 */
export const bindable = (
  record: ConversationRecord | undefined,
  source: ConversationSource,
  workspace: string,
  caseBlind: boolean,
): boolean =>
  record !== undefined && sameSource(record.source, source) && sameRoot(record.workspace, workspace, caseBlind);
