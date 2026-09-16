import { ConversationMeta, ConversationRecord, ConversationSource, sameSource, sourceOfSession } from './chatStore';
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
  | { readonly kind: 'unreadable'; readonly reason: string }
  /** The folder ANSWERED, and no session in it is called what this tab is called. */
  | { readonly kind: 'unmatched' };

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
  | {
    readonly kind: 'pick';
    readonly among: readonly ConversationMeta[];
    readonly why: Narrowing;
    readonly offer: string;
    /**
     * Whether choosing a row may BIND that conversation to this tab.
     *
     * <p>False for `cross root`, and that is the whole reason this field exists. Those conversations
     * belong to another project: opening one is right, and re-homing it onto this tab is not — it
     * would move somebody's conversation between projects because they pressed a key looking for it.
     * Such a row is opened the way the picker opens one, under its own key. `bindable` would refuse
     * it anyway, which is how two reviewers found that this answer had no valid completion.</p>
     */
    readonly bind: boolean;
  }
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
  /**
   * Whether MORE THAN ONE session actually answered to this tab's name.
   *
   * <p>A POSITIVE fact, and stated positively on purpose. It began as `unsure` — "the folder could
   * not be read" — which meant the name fallback below was licensed by the ABSENCE of a known
   * problem, so a future walker outcome that set `ambiguous` for some third reason would have
   * enabled it silently. Now the fallback is licensed by the one thing that is evidence about
   * ownership: two sessions really are called this, so a saved conversation of that name plausibly
   * belongs to one of them. Any new reason to be ambiguous arrives as `false` and asks. (codex, the
   * code round — "derive an explicit value at the walker boundary so every new ambiguity case must
   * choose whether name evidence is valid".)</p>
   */
  readonly severalSessions: boolean;
  /**
   * Whether the session walk FAILED, as against answering and matching nothing.
   *
   * <p>`ambiguous` folds the two together — it is true for a directory that would not open AND for
   * one that opened and holds nothing of this name — and with only that flag the decision below had
   * to call every non-namesake case unreadable. So the commonest case there is, a tab whose name
   * belongs to no session at all, was reported as a failure that had not happened, sending the person
   * after a directory problem that was not there. Carried in separately because the two ask
   * different things of them: one is "try again", the other is "pick one".</p>
   */
  readonly walkFailed: boolean;
  /**
   * How many tabs open right now carry this tab's label — at least 1, since this tab is one of them.
   *
   * <p>It is what makes a NAME usable as evidence, and the whole reason the fallback below is
   * allowed to exist. Three reviewers refused an auto-open on a unique title with the same case:
   * two tabs called the same thing, one conversation of that name, and pressing *go to* on the tab
   * that does not own it opens the one that does. They are right, and this is the fact that answers
   * them — when this window has exactly ONE tab of that name, that case cannot arise, and when it
   * has two the question goes back to the person.</p>
   */
  readonly namesakes: number;
  /**
   * Every saved conversation whose SOURCE is this tab's, from every root — `ConversationIndex.bySource`.
   *
   * <p>Across all roots on purpose: a conversation about this very file filed under another project
   * is something to offer, not to hide. Empty for a tab whose source could not be resolved, which is
   * every ambiguous Claude tab — which is why {@link GotoAsked.inRoot} exists beside it.</p>
   */
  readonly candidates: readonly ConversationMeta[];
  /**
   * Every saved conversation of THIS TAB'S ROOT, whatever its source.
   *
   * <p>What an ambiguous tab is offered, and what an unreadable store shows. Two reviewers found that
   * one list could not serve both jobs: an ambiguous Claude tab has no source, so a source-filtered
   * list is empty, and a picker asking which conversation somebody meant would offer none — the bug
   * the previous round fixed, which a caller reading a one-list contract would have rebuilt.</p>
   */
  readonly inRoot: readonly ConversationMeta[];
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

/**
 * What kind of thing a tab is, decided over what a snapshot carries.
 *
 * <p>Here rather than in the host for the reason everything else in this module is: it is a decision,
 * and `chatCommand.ts` is far over the file-length limit. The host reads the two facts; this says
 * what they mean. (gemini, the code round.)</p>
 */
export function tabKindOf(seen: { readonly claude: boolean; readonly document: boolean }): TabKind {
  if (seen.claude) {
    return 'claude';
  }

  return seen.document ? 'document' : 'other';
}

/**
 * The session a Claude tab is named by — or nothing, when its name answers to none or to several.
 *
 * <p>`none` is the honest answer for BOTH, and they are told apart by `ambiguous` rather than by this:
 * one is "there is nothing to match on", the other is "there is too much". A walk that FAILED is a
 * third thing again, and the host reports it rather than folding it in here.</p>
 */
export const sessionSourceOf = (id: string): ConversationSource => sourceOfSession(id);

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
  const here = rootOfTab(asked);
  if (asked.index.kind === 'unavailable') {
    // Its last good rows are a statement about a moment that has passed. They are worth SHOWING —
    // they are probably right — but not worth binding a tab to without a person looking at them. The
    // root's conversations rather than this tab's, because a store that could not be read cannot say
    // which are this tab's, and the root's are the honest superset.
    return { kind: 'pick', among: asked.inRoot, why: { kind: 'unreadable', reason: asked.index.reason }, offer, bind: true };
  }
  if (asked.ambiguous && asked.tab.kind === 'claude') {
    // The TAB's own identity is in doubt, not the conversations'. Only a Claude tab can be ambiguous
    // in this sense — a document is named by its uri, which answers to one thing or to nothing — so
    // the flag is read only where it can mean something rather than trusted wherever it is set.
    // (codex, the plan round.)
    //
    // THE NAME IS THE LAST THING LEFT, and only here. Everywhere else this module matches a source
    // and never a title, because a title is somebody else's word for a thing and two tabs can share
    // one — handing a conversation to a tab that never owned it is this feature's worst failure. But
    // on THIS path there is no source to be wrong about: the tab's own session could not be
    // resolved, so the alternative is not "match by source instead", it is "show forty rows and ask".
    // The first version did exactly that, under a title reading "more than one Claude session is
    // called X" above a list of conversations mostly not called X. (Found by the operator, testing
    // 0.40.0: *"должно брать имя с вкладки… если находит только 1 разговор с таким именем —
    // логично открывать его самому"*.)
    //
    // A unique match opens. It is NOT bound to the tab — `bindable` refuses a source of `none`, and
    // it is right to: what has been guessed from a name is not something to write down as ownership.
    // Several matches narrow the question to them. None leaves the old behaviour, because a list of
    // everything is still better than nothing when the name says nothing either.
    const named = sameName(asked.inRoot, asked.tab.label);
    // A unique match opens ONLY when the name is evidence rather than a coincidence. Two conditions,
    // and each answers a finding: the walk must have ANSWERED — a directory that could not be read
    // says nothing about names either, and a conversation opened on the strength of a temporary
    // failure is the worst kind of wrong (codex) — and this window must hold exactly ONE tab of this
    // name, or the conversation found may be the other tab's (local, gemini and codex, one case
    // each). Where either fails the question goes back to the person, narrowed to the matches.
    const evidence = asked.severalSessions && asked.namesakes === 1;
    const only = evidence && named.length === 1 ? named[0] : undefined;
    if (only !== undefined) {
      return { kind: 'reopen', meta: only };
    }

    return {
      kind: 'pick',
      among: named.length > 0 ? named : asked.inRoot,
      // AND IT SAYS WHICH AMBIGUITY. A walk that could not be done is not two sessions of one name,
      // and telling somebody their sessions share a name when the truth is that the folder would not
      // open sends them looking for a duplicate that is not there. (gemini, the code round.)
      why: whyNarrowed(asked),
      offer,
      bind: true,
    };
  }
  // ONE PASS over the source matches, split by whether they are this tab's project or another's.
  const mine: ConversationMeta[] = [];
  const elsewhere: ConversationMeta[] = [];
  for (const meta of asked.candidates) {
    if (sameSource(meta.source, asked.source)) {
      (sameRoot(meta.workspace, here, asked.caseBlind) ? mine : elsewhere).push(meta);
    }
  }
  if (mine.length > 1) {
    // Impossible by construction — one source belongs to one conversation, and C1 writes it once —
    // and answered honestly anyway, because the alternative is guessing which of two is yours.
    return { kind: 'pick', among: mine, why: { kind: 'several' }, offer, bind: true };
  }
  const only = mine[0];
  if (only !== undefined) {
    return { kind: 'reopen', meta: only };
  }
  if (elsewhere.length > 0) {
    // THIS TAB'S CONVERSATION, filed under another project. Dropping it to silence would offer to
    // start a new one while the old one sits a folder away — the duplicate this whole rule exists to
    // prevent, produced by the rule itself. It is shown, and the person decides. It is NOT bound to
    // this tab: it belongs where it is filed, and moving it because somebody went looking for it
    // would be a decision they did not ask for. (Two vendors, twice.)
    return { kind: 'pick', among: elsewhere, why: { kind: 'cross root' }, offer, bind: false };
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
      // path it has not got — so it is a COMPILE error. At runtime it answers "not eligible", which
      // sends the press to the full list: a tab kind this build has never heard of is exactly the
      // case the operator's decision 7 covers, and throwing here would take the command down instead
      // of showing the list. (codex, then local, the code rounds.)
      const unhandled: never = kind;
      console.warn(`ConnectOtherAIs: a tab kind this build has no arm for: ${JSON.stringify(unhandled)}`);

      return false;
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
 * The conversations in this root that were opened from a tab of this NAME.
 *
 * <p>A record's title is the label of the tab it was opened from, captured then. So this asks "was
 * anything here started from a tab called that", which is a guess — and it is used in exactly one
 * place, where the honest answer to the better question is already "I cannot tell". An empty label
 * matches nothing rather than everything, which is the difference between a fallback and a bug.</p>
 */
const sameName = (rows: readonly ConversationMeta[], label: string): readonly ConversationMeta[] =>
  (label.trim().length === 0 ? [] : rows.filter((meta) => meta.title === label));

/**
 * Which ambiguity this is, in the order the person cares about.
 *
 * <p>Three situations reach one picker and they ask three different things. Two sessions of one name
 * is *"which of these is yours"*. A folder that would not open is *"try again"*. A folder that opened
 * and holds nothing of this name — the commonest of the three — is *"none of them is called that;
 * pick one"*, and it used to be told as the second, sending people after a directory problem that
 * was not there.</p>
 */
function whyNarrowed(asked: GotoAsked): Narrowing {
  if (asked.severalSessions) {
    return { kind: 'ambiguous session' };
  }

  return asked.walkFailed
    ? { kind: 'unreadable', reason: 'this tab’s Claude sessions could not be read' }
    : { kind: 'unmatched' };
}

/**
 * The root this tab belongs to — its own, or the fallback for a tab under none of them.
 *
 * <p>Through {@link filedUnder}, which is the function story C1 FILES a conversation with. Two copies
 * of that rule would drift the day either changed, and the failure would be silent: a record filed
 * one way and looked up the other, answering `start` for a conversation that exists. (gemini, the
 * code round.)</p>
 */
export const rootOfTab = (asked: GotoAsked): string =>
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
