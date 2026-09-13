import { ConversationRecord, fromLegacy, isPrefixOf, isSafeId, saidIn } from './chatStore';
import { ChatStoreFile, SaveOutcome } from './chatStoreFile';
import { SavedTab, TAB_STORE_KEY, TAB_VERSION, TabStore, isTab } from './chatTabs';

/**
 * The migration: every conversation the MEMENTO holds is carried into the store on disk, once, and
 * the memento is emptied only when every one of them has been confirmed there.
 *
 * <p>This is the one step in the whole feature that can destroy a person's history: everything
 * before it added a second copy, and this is what makes the new copy authoritative and takes the
 * old one away. Every rule below exists because a reviewer found a way to lose a conversation
 * silently with all tests green, and each is written down with the loss it prevents.</p>
 *
 * <h2>The store holding an id is NOT proof its copy is newer</h2>
 *
 * <p>The dual write of story A3 was best-effort: a store save can come back `busy`, `failed` or
 * `refused`, and the memento carried on being the source of truth regardless. So the memento can
 * hold eight turns of a conversation while the store holds the same id at five, and a migration that
 * skipped on "the id exists" and then emptied the key would delete three turns, permanently, with
 * every test green. (codex and gemini, A4's plan round, independently.) The migration therefore
 * compares TRANSCRIPTS, never ids: {@link newerOf} says which copy is the beginning of the other.
 * The store's copy is a prefix of the memento's, or the store has none → the memento is newer and is
 * written. They are equal, or the store's is the longer → the store is newer and stands. Neither is
 * the beginning of the other → the two have DIVERGED, and the memento's copy is filed under a fork
 * of its id ({@link forkId}) rather than either being thrown away: an extra row in the picker is
 * recoverable, a lost turn is not. The comparison is `isPrefixOf` in `chatStore.ts`, the same one
 * the write decision uses to tell its own earlier session from a rival — moved there rather than
 * copied here.</p>
 *
 * <p>When the memento's copy is written OVER the store's, it is the store's record with the
 * memento's words and settings laid over it ({@link overDisk}): everything the store knew and the
 * memento never did — when the conversation began, what it was opened from, whether a new chat closed
 * it, and any field a later build adds to the record — survives without this module having to name
 * it. (gemini, A4's code round.)</p>
 *
 * <h2>Only a save that landed WHOLE counts</h2>
 *
 * <p>A `partial` save committed the record and not its index, so the conversation exists and
 * nothing can list it; emptying the memento on the strength of that hides a conversation. So the
 * partial ones have their index repaired (a read regenerates it) and then EVERY record filed by this
 * run must be confirmed in the store's own index — per id, through {@link ChatStoreFile.listed},
 * rather than by listing a store of thousands to confirm twenty — before the key may go. `busy`,
 * `refused`, `failed` and `incompatible` never count: the last of those is a file this build cannot
 * read, which is not proof the person's conversation is safe — it may be a newer build's, and the
 * memento's copy is then the only one this build can read. The key stays, the report says so, and
 * the next activation tries again — nothing here is remembered between runs, so a re-run can neither
 * duplicate nor overwrite: the id is the record's own, the comparison is by words, and the fork id is
 * derived.</p>
 *
 * <h2>A damaged record neither vanishes nor wedges</h2>
 *
 * <p>If a record `tabsFrom` would drop counted as missing, the key would never be emptied and this
 * would re-run for ever; if it were ignored, it would be deleted with no trace the moment the key
 * went. So it is QUARANTINED — the raw value written, as it was, by {@link ChatStoreFile.quarantine}
 * under the store's own subdirectory — and only then counted as settled. Nothing this module cannot
 * file as a conversation is ever discarded; the same for a whole memento value of a shape no build
 * wrote. (A re-run that follows a failed clear quarantines the same value again under a new name;
 * two copies of a thing that was never lost is the cheaper side of that trade.)</p>
 *
 * <h2>The memento is SEALED, then re-read, then emptied — in that order</h2>
 *
 * <p>Three things can put a record into the key after this run has read it. Another window's dual
 * write, which goes on in every window until ITS migration has succeeded. This window's own dual
 * write, which is still running while the pass runs. And this window's memento QUEUE: `ChatTabMemory`
 * queues its writes, so a write issued a moment before the clear executes its `update` AFTER it and
 * fills the key again — and the migration would re-run on every activation for ever (gemini, A4's
 * code round). So once every record is confirmed the memento is sealed through `deps.seal`: the host
 * unbinds its writer (nothing new can queue) and drains the queue (what was queued has landed). Only
 * then is the key read again; if it changed, what is there now is migrated too, and it is read a
 * third time; if it changed again — that can only be another window now — the key is left for the
 * next activation. The store's health is checked once more immediately before the `update`, because
 * a disk can go away between the pass and the clear. If the clear does not happen after a seal, for
 * any reason, `deps.unseal` re-binds the writer: a sealed memento with the key still populated would
 * be a window whose next words are written to one store only while the other is still in charge. The
 * residual is the interval between the last read and the `update`, and it is stated here rather than
 * pretended away: it is another window's write, carried by that window's own store write (the dual
 * write), and lost from the memento only if that store write also failed.</p>
 *
 * <h2>A store that cannot be READ migrates nothing</h2>
 *
 * <p>`unavailable` from the store is the one state in which touching the memento is unrecoverable:
 * emptying the source of truth because the disk would not answer. Nothing is written, the key is
 * untouched, and the report names the reason. The dual write stays on in that window.</p>
 *
 * <p>No `vscode` here. The rules are pure and tested as values; the I/O below them takes a memento
 * (two methods of it), a store and two hooks, and is tested against a real temporary directory with a
 * memento fake. Records are carried {@link IMPORT_WIDTH} abreast — the same worker-pool shape as the
 * store's listing — so twenty records are not forty serial round trips on the activation path.
 * Nothing throws, a memento whose `get` throws included: every failure is a typed fate or an
 * `incomplete` report with a sentence, and every one is said on the console — never swallowed, per
 * `coding-style.md`.</p>
 */

/**
 * The suffix a memento copy is filed under when its own id holds DIFFERENT words on disk.
 *
 * <p>Derived rather than minted: a random id would make a re-run after a crash between the fork
 * and the clear file a second copy, and idempotency by id is the property every other branch here
 * has. A dash and a letter keep it a safe filename component, and a fork that is itself diverged is
 * not forked again — that is the one branch that reports `unconfirmed` instead, so nothing here can
 * chain. No `randomUUID()` produces this shape (a uuid is 36 characters with four dashes in fixed
 * places), so the fork id cannot collide with a conversation minted the ordinary way.</p>
 */
export const FORK_SUFFIX = '-m';

/** The sentence a fork that would itself diverge gets, because nothing forks twice. */
export const DIVERGED_TWICE =
  'the copy on disk and the copy in the old store hold different words, and so does the copy already set aside for that';

/**
 * How many records are carried at once.
 *
 * <p>Serially, twenty records are forty round trips (a read and a save each) one after another on the
 * activation path, while the serializer waits. All at once is a thundering herd against a directory
 * another window may be writing. Four is the store's own listing width halved — a save is two writes
 * and a lock where a listing entry is one read — and a constant rather than a setting because nothing
 * a person can observe would tell them which value to pick. Two memento entries with ONE id (a hand
 * edit; `remembered` keeps one per id) can meet in the pool: the second finds the lock held and is
 * `unconfirmed`, and the next activation settles it.</p>
 */
export const IMPORT_WIDTH = 4;

// ---------------------------------------------------------------------------------------------
// The rules — pure, and tested as values.
// ---------------------------------------------------------------------------------------------

/** What the memento held, sorted into what can be filed and what cannot. */
export type MementoContents =
  /** No key, or nothing under it. */
  | { readonly kind: 'empty' }
  /** A value of a shape no build of this extension wrote. Quarantined whole. */
  | { readonly kind: 'foreign' }
  /** The records this build can render, and the raw entries it cannot. Either list may be empty. */
  | { readonly kind: 'records'; readonly tabs: readonly SavedTab[]; readonly damaged: readonly unknown[] };

/**
 * Whether one stored entry can be FILED — a record this build renders, under an id that can be a
 * filename. `isTab` alone is not enough: an id `randomUUID()` never minted (a hand edit with a dot or
 * a slash) would fail every save with the same sentence for ever, and wedge the key open.
 */
const filable = (value: unknown): value is SavedTab => isTab(value) && isSafeId(value.id);

/** The memento's value, sorted. `null` counts as empty because a memento round-trips `undefined` to it. */
export function contentsOf(raw: unknown): MementoContents {
  if (raw === undefined || raw === null) {
    return { kind: 'empty' };
  }
  const stored = raw as { version?: unknown; tabs?: unknown };
  if (typeof raw !== 'object' || stored.version !== TAB_VERSION || !Array.isArray(stored.tabs)) {
    return { kind: 'foreign' };
  }

  return {
    kind: 'records',
    tabs: stored.tabs.filter(filable),
    damaged: stored.tabs.filter((entry) => !filable(entry)),
  };
}

/** Which of two copies of one conversation is the newer, judged by its words alone. */
export type Newer = 'same' | 'memento' | 'store' | 'diverged';

/**
 * The comparison the whole migration rests on. See the header for why it is by transcript.
 *
 * <p>`same` includes two empty transcripts, which is right: a conversation nobody has spoken in has
 * nothing to lose in either direction, and the store's copy stands.</p>
 */
export function newerOf(memento: readonly string[], store: readonly string[]): Newer {
  if (isPrefixOf(store, memento)) {
    return store.length === memento.length ? 'same' : 'memento';
  }

  return isPrefixOf(memento, store) ? 'store' : 'diverged';
}

/**
 * The id a diverged memento copy is filed under, or empty when there is no safe one.
 *
 * <p>Empty rather than a throw for an id long enough that the suffix takes it past the limit — a
 * hand-made id no build minted; such a record reports `unconfirmed` and is not spent code on.</p>
 */
export function forkId(id: string): string {
  const forked = `${id}${FORK_SUFFIX}`;

  return isSafeId(forked) ? forked : '';
}

/** What one memento record came to. */
export type Fate =
  /** The memento's copy is now on disk under `id`. `indexed` false is a `partial` save, repaired before the confirmation. */
  | { readonly kind: 'written'; readonly id: string; readonly indexed: boolean; readonly forkedFrom?: string }
  /** The store's copy stood — the same words, or more of them. */
  | { readonly kind: 'kept'; readonly id: string; readonly forkedFrom?: string }
  /** A value this build cannot file, written as it was to `at`. */
  | { readonly kind: 'quarantined'; readonly at: string }
  /** Nothing can be said about where this record is. The key stays. */
  | { readonly kind: 'unconfirmed'; readonly id: string; readonly reason: string };

const NOT_LANDED: Readonly<Record<'refused' | 'busy', string>> = {
  refused: 'another window changed this conversation while it was being carried across',
  busy: 'another window is writing this conversation',
};

/**
 * A save's answer as a fate. Only `ok` and `partial` put the copy on disk; everything else says
 * nothing about whether the person's words are safe there, and is `unconfirmed`.
 */
export function fateOfSave(id: string, outcome: SaveOutcome): Fate {
  if (outcome.kind === 'ok' || outcome.kind === 'partial') {
    return { kind: 'written', id, indexed: outcome.kind === 'ok' };
  }
  if (outcome.kind === 'refused' || outcome.kind === 'busy') {
    return { kind: 'unconfirmed', id, reason: NOT_LANDED[outcome.kind] };
  }

  return { kind: 'unconfirmed', id, reason: outcome.reason };
}

/** The ids a run says are on disk — every one of which the store's index must carry before the key may go. */
export const filedIds = (fates: readonly Fate[]): readonly string[] =>
  fates.flatMap((fate) => (fate.kind === 'written' || fate.kind === 'kept' ? [fate.id] : []));

/** Whether every record came to rest — on disk under an id, or in quarantine as it was. */
export const allSettled = (fates: readonly Fate[]): boolean => fates.every((fate) => fate.kind !== 'unconfirmed');

/**
 * The memento's copy, written OVER the store's older one.
 *
 * <p>The DISK RECORD FIRST, then the memento's words and settings over it. Everything the store knew
 * and the memento never did — when the conversation began, what it was opened from, whether a new
 * chat closed it, and any field a later build adds to the record — survives without being named
 * here; the first version rebuilt the record from the legacy shape and would have erased every such
 * field the day one existed. (gemini, A4's code round.)</p>
 *
 * <p>Origin stays a PAIR. `recordFrom` refuses a record whose `source` names an identity its
 * `fromSession` disagrees with, so a disk source that names one keeps the flag it was written with;
 * only a disk record with no source takes the memento's. Where the record is filed stays the disk's
 * unless the disk filed it nowhere.</p>
 */
export function overDisk(tab: SavedTab, disk: ConversationRecord, workspace: string): ConversationRecord {
  const ours = fromLegacy(tab, disk.workspace.length > 0 ? disk.workspace : workspace);

  return {
    ...disk,
    title: ours.title,
    passage: ours.passage,
    modelId: ours.modelId,
    messages: ours.messages,
    carryFrom: ours.carryFrom,
    updatedAt: ours.updatedAt,
    workspace: ours.workspace,
    // WHICH DOOR IT CAME THROUGH stays the disk's, always. It used to be taken from the memento
    // whenever the disk record named no source — which today is every record, since the durable
    // source is story C1 — so the condition was a branch with one live arm, and the moment C1 lands
    // it would silently start behaving differently for records written before and after it. A
    // migration is not the place to learn that. The memento's value is the older statement of the
    // same fact and the disk's is the newer one, which is the rule everywhere else here.
    // (gemini, A4's second code round.)
    fromSession: disk.fromSession,
  };
}

/** What the migration came to, and — for the two answers that leave the key in place — why. */
export type ImportReport =
  /** The key was empty. Nothing to carry; the store is the source of truth. */
  | { readonly kind: 'nothing' }
  /** The store would not answer. Nothing was written, nothing was touched. */
  | { readonly kind: 'unavailable'; readonly reason: string }
  /** Every record confirmed on disk or set aside, and the key emptied. */
  | { readonly kind: 'migrated'; readonly fates: readonly Fate[] }
  /** The key stays for the next activation. `reason` says why, `fates` say what did land. */
  | { readonly kind: 'incomplete'; readonly fates: readonly Fate[]; readonly reason: string };

/** Whether the store may be trusted as the ONLY store from here on in this window. */
export const importSucceeded = (report: ImportReport): boolean =>
  report.kind === 'nothing' || report.kind === 'migrated';

/** One line for the console — what happened, counted, and the ids nothing could be said about. */
export function describe(report: ImportReport): string {
  if (report.kind === 'nothing') {
    return 'ConnectOtherAIs: no chat conversations to carry into the store';
  }
  if (report.kind === 'unavailable') {
    return `ConnectOtherAIs: chat conversations were NOT carried into the store, which could not be read (${report.reason}); the old store stays in charge`;
  }
  const counts = countsOf(report.fates);
  if (report.kind === 'migrated') {
    return `ConnectOtherAIs: chat conversations carried into the store — ${counts}; the old store is emptied`;
  }
  const left = report.fates.flatMap((fate) => (fate.kind === 'unconfirmed' ? [`${fate.id}: ${fate.reason}`] : []));

  return `ConnectOtherAIs: chat conversations NOT fully carried into the store — ${report.reason}; ${counts}`
    + (left.length === 0 ? '' : `; unconfirmed: ${left.join('; ')}`)
    + '; the old store stays in charge until the next activation';
}

function countsOf(fates: readonly Fate[]): string {
  const count = (kind: Fate['kind']): number => fates.filter((fate) => fate.kind === kind).length;

  return `${count('written')} written, ${count('kept')} already newer on disk, ${count('quarantined')} set aside, ${count('unconfirmed')} unconfirmed`;
}

// ---------------------------------------------------------------------------------------------
// The I/O — thin, and every decision above it.
// ---------------------------------------------------------------------------------------------

/** What the migration needs, and no more of the host than that. */
export interface ImportDeps {
  /** The memento — `get` and `update` of one key. */
  readonly memento: TabStore;
  readonly store: ChatStoreFile;
  /** The workspace a migrated record is filed under: what the dual write filed its neighbours under. */
  readonly workspace: string;
  /**
   * The clock, pinned by tests. It names the quarantine files — and nothing else.
   *
   * <p>It used to date the store's locks too, through every read and save, and that stamped each lock
   * a migration took with the migration's START time: a run of many records on a slow disk outlives
   * the lock's thirty-second window, after which a lock it was actively holding read as abandoned to
   * another extension host — and a rival's genuinely stale lock read as fresh by the same frozen
   * clock. The store's own clock dates its locks now ({@link migrateOne}); this instant survives where
   * a NAME is derived from it, which a re-run must reproduce. (CodeRabbit, PR #223.)</p>
   */
  readonly at?: number;
  /**
   * End this window's writes to the memento: nothing new may be queued, and what is queued has landed.
   * Called once every record is confirmed — or the key was found empty — immediately before the key
   * is read for the last time and emptied. See the header for the race it closes.
   */
  readonly seal?: () => Promise<void>;
  /** Resume this window's writes to the memento: the key was NOT emptied after a seal, so the memento is still in charge. */
  readonly unseal?: () => void;
}

/** A pass over one value of the key: what landed, and whether the key may go on its account. */
type Pass =
  | { readonly kind: 'settled'; readonly fates: readonly Fate[] }
  | { readonly kind: 'incomplete'; readonly fates: readonly Fate[]; readonly reason: string };

/** One read of the key, or the fact that the memento would not answer — never a throw out of here. */
type KeyRead =
  | { readonly kind: 'value'; readonly value: unknown }
  | { readonly kind: 'failed'; readonly reason: string };

const codeOf = (reason: unknown): string => {
  const code = (reason as { code?: unknown } | null)?.code;

  return typeof code === 'string' ? ` (${code})` : '';
};

const incomplete = (fates: readonly Fate[], reason: string): Extract<ImportReport, { kind: 'incomplete' }> =>
  ({ kind: 'incomplete', fates, reason });

/** The key, read without letting a memento that throws escape: the reason on the console, a sentence back. */
function readKey(memento: TabStore): KeyRead {
  try {
    return { kind: 'value', value: memento.get(TAB_STORE_KEY) };
  } catch (reason) {
    console.error('ConnectOtherAIs: the old chat store could not be read', reason);

    return { kind: 'failed', reason: 'the old store could not be read' };
  }
}

/**
 * Carry every memento record into the store, and empty the key once all of them are confirmed.
 *
 * <p>Never throws. Runs once per activation; the serializer waits for it (under a ceiling) before
 * it answers, so a tab whose conversation has not been carried yet does not read as absent.</p>
 */
export async function importLegacyTabs(deps: ImportDeps): Promise<ImportReport> {
  const at = deps.at ?? Date.now();
  const first = readKey(deps.memento);
  if (first.kind === 'failed') {
    return incomplete([], first.reason);
  }
  if (contentsOf(first.value).kind !== 'empty') {
    return carry(deps, first.value, at);
  }
  // Empty — but that is not on its own a reason to retire the old store. ASK THE NEW ONE FIRST: a
  // fresh window whose store cannot be reached would otherwise unbind the memento, and the first
  // conversation of that session would be written to a disk that refuses it and to nothing else.
  // (codex, A4's second code round.)
  const ready = await deps.store.state();
  if (ready.kind === 'unavailable') {
    return { kind: 'unavailable', reason: ready.reason };
  }
  // Now seal — nothing new may be written from this window — and look once more: a write the seal
  // drained may have filled the key between the read above and now.
  await deps.seal?.();
  const again = readKey(deps.memento);
  if (again.kind === 'failed') {
    deps.unseal?.();

    return incomplete([], again.reason);
  }

  if (contentsOf(again.value).kind === 'empty') {
    return { kind: 'nothing' };
  }
  // A write the seal drained filled the key after all. It is carried like any other — and the seal
  // taken above is given BACK first, because the carry takes its own at the one moment it is safe
  // to, and a writer left unbound over a key that is still populated is a window whose next words
  // go to one store while the other is in charge. (gemini, A4's second code round.)
  deps.unseal?.();

  return carry(deps, again.value, at);
}

/** A non-empty key, carried; and the memento's writer resumed whenever the key did NOT go. */
async function carry(deps: ImportDeps, raw: unknown, at: number): Promise<ImportReport> {
  const outcome = await carrying(deps, raw, at);
  if (outcome.kind !== 'migrated') {
    // Re-binding a writer that was never unbound is harmless; leaving one unbound over a key that
    // is still populated is a window whose next words go to one store while the other is in charge.
    deps.unseal?.();
  }

  return outcome;
}

async function carrying(deps: ImportDeps, raw: unknown, at: number): Promise<ImportReport> {
  const state = await deps.store.state();
  if (state.kind === 'unavailable') {
    return { kind: 'unavailable', reason: state.reason };
  }
  const first = await pass(deps, raw, at);
  if (first.kind === 'incomplete') {
    return first;
  }
  // Every record confirmed. The seal is NOT taken here: it lives one statement from the `update`
  // itself, in `clear`. Sealed here, an edit made during the re-read and the health check below
  // would be written to neither store if its own disk write then failed and the clear then failed
  // too — and both of those are outcomes this function has. (codex, A4's second code round.)
  return clearIfUnchanged(deps, raw, first.fates, at);
}

/** Migrate whatever the key holds NOW, then prove it: every filed id in the store's own index. */
async function pass(deps: ImportDeps, raw: unknown, at: number): Promise<Pass> {
  const contents = contentsOf(raw);
  if (contents.kind === 'empty') {
    return { kind: 'settled', fates: [] };
  }
  const fates = contents.kind === 'foreign'
    ? [await quarantine(deps.store, raw, 'value', at)]
    : await migrateRecords(deps, contents, at);
  const open = fates.filter((fate) => fate.kind === 'unconfirmed').length;
  if (open > 0) {
    return incomplete(fates, `${open} conversation(s) could not be confirmed on disk`);
  }
  await repairIndexes(deps.store, fates);
  const missing = await unlisted(deps.store, fates);
  if (missing.length > 0) {
    return incomplete(fates, `${missing.length} conversation(s) are on disk but not in the list that finds them`);
  }

  return { kind: 'settled', fates };
}

/** Every damaged entry set aside, every record carried — {@link IMPORT_WIDTH} abreast, fates in memento order. */
async function migrateRecords(
  deps: ImportDeps,
  contents: Extract<MementoContents, { kind: 'records' }>,
  at: number,
): Promise<readonly Fate[]> {
  const jobs: readonly (() => Promise<Fate>)[] = [
    ...contents.damaged.map((entry, index) => () => quarantine(deps.store, entry, `entry-${index}`, at)),
    ...contents.tabs.map((tab) => () => migrateOne(deps, tab, true)),
  ];

  return abreast(jobs, IMPORT_WIDTH);
}

/** The listing's worker pool, over jobs: `width` at a time, results in the jobs' order. */
async function abreast<T>(jobs: readonly (() => Promise<T>)[], width: number): Promise<readonly T[]> {
  const out = new Array<T>(jobs.length);
  let next = 0;
  const worker = async (): Promise<void> => {
    while (next < jobs.length) {
      const here = next;
      next += 1;
      out[here] = await (jobs[here] as () => Promise<T>)();
    }
  };
  await Promise.all(Array.from({ length: Math.min(width, jobs.length) }, worker));

  return out;
}

/**
 * One record: read what the store holds under its id, and let {@link newerOf} decide.
 *
 * <p>The read is NOT a check-then-act on absence: a save at baseline 0 is refused by the store's own
 * swap if anything has appeared in between, and a save at the disk's revision is refused if it moved
 * — either way the fate is `unconfirmed` and the next activation compares again.</p>
 *
 * <p><b>The read and the save take the store's own clock</b>, never the migration's `at`. Both may
 * claim the conversation's lock, and a lock is dated by what it is handed: dated from the moment
 * the migration BEGAN, a lock taken twenty records into a slow run was already old when it was
 * written, and read as abandoned to any other window once the run passed the thirty-second window —
 * while a rival's stale lock, aged against the same frozen instant, read as fresh and left the
 * record `unconfirmed`. The migration has no clock of its own to offer here; the store's default is
 * the right one. (CodeRabbit, PR #223.)</p>
 */
async function migrateOne(deps: ImportDeps, tab: SavedTab, mayFork: boolean): Promise<Fate> {
  const seen = await deps.store.read(tab.id);
  if (seen.kind === 'absent') {
    return fateOfSave(tab.id, await deps.store.save(fromLegacy(tab, deps.workspace), 0));
  }
  if (seen.kind !== 'record') {
    return { kind: 'unconfirmed', id: tab.id, reason: seen.reason };
  }

  return reconcile(deps, tab, seen.record, mayFork);
}

/** Both copies exist. Which is the beginning of the other decides everything. */
async function reconcile(deps: ImportDeps, tab: SavedTab, disk: ConversationRecord, mayFork: boolean): Promise<Fate> {
  const newer = newerOf(saidIn(tab.messages), saidIn(disk.messages));
  if (newer === 'same' || newer === 'store') {
    return { kind: 'kept', id: tab.id };
  }
  if (newer === 'memento') {
    return fateOfSave(tab.id, await deps.store.save(overDisk(tab, disk, deps.workspace), disk.rev));
  }

  return fork(deps, tab, mayFork);
}

/**
 * The diverged case: the memento's words under the fork of their id, by the same rules, once.
 *
 * <p>It goes back through {@link migrateOne}, so the fork id is READ before anything is written: a
 * fork already there from an earlier run is compared like any record — the same words stand, a
 * memento copy that grew since is written over it through {@link overDisk} and so keeps the fork's
 * own `createdAt`, and a fork that has itself diverged is `unconfirmed` rather than forked again.
 * Whatever the fork comes to is attributed to the MEMENTO record, which is what the person has and
 * what the report names.</p>
 */
async function fork(deps: ImportDeps, tab: SavedTab, mayFork: boolean): Promise<Fate> {
  const as = forkId(tab.id);
  if (!mayFork || as.length === 0) {
    return { kind: 'unconfirmed', id: tab.id, reason: DIVERGED_TWICE };
  }
  const fate = await migrateOne(deps, { ...tab, id: as }, false);
  if (fate.kind === 'unconfirmed') {
    return { ...fate, id: tab.id };
  }

  return fate.kind === 'quarantined' ? fate : { ...fate, forkedFrom: tab.id };
}

/**
 * A `partial` save's index, regenerated by the store's own read. Best-effort: the confirmation judges
 * the result. The read takes the store's clock, for the reason {@link migrateOne} gives — it claims
 * the lock to write the index.
 */
async function repairIndexes(store: ChatStoreFile, fates: readonly Fate[]): Promise<void> {
  for (const fate of fates) {
    if (fate.kind === 'written' && !fate.indexed) {
      await store.read(fate.id);
    }
  }
}

/** The filed ids the store's index does not carry: conversations that exist and cannot be found. */
async function unlisted(store: ChatStoreFile, fates: readonly Fate[]): Promise<readonly string[]> {
  const ids = filedIds(fates);
  const found = await abreast(ids.map((id) => () => store.listed(id)), IMPORT_WIDTH);

  return ids.filter((_, index) => found[index] !== true);
}

/** The raw value, set aside by the store as it was. The store says where, on the console. */
async function quarantine(store: ChatStoreFile, raw: unknown, label: string, at: number): Promise<Fate> {
  const out = await store.quarantine(raw, `${TAB_STORE_KEY}-${label}`, at);

  return out.kind === 'ok'
    ? { kind: 'quarantined', at: out.at }
    : { kind: 'unconfirmed', id: label, reason: out.reason };
}

/** Two memento values, the same by value. What the re-read before the clear compares. */
const sameValue = (left: unknown, right: unknown): boolean => JSON.stringify(left) === JSON.stringify(right);

/**
 * Re-read the key — after the seal — and clear only what was seen.
 *
 * <p>`undefined` on the re-read is another window having emptied the key already — every record
 * this run saw is confirmed, so that is `migrated`, and the clear below is harmless.</p>
 */
async function clearIfUnchanged(deps: ImportDeps, seen: unknown, fates: readonly Fate[], at: number): Promise<ImportReport> {
  const again = readKey(deps.memento);
  if (again.kind === 'failed') {
    return incomplete(fates, again.reason);
  }

  return sameValue(again.value, seen) ? clear(deps, fates) : afterChange(deps, again.value, fates, at);
}

/** The key changed under the pass: migrate what is there now, once, and look a third time. */
async function afterChange(deps: ImportDeps, changed: unknown, fates: readonly Fate[], at: number): Promise<ImportReport> {
  const second = await pass(deps, changed, at);
  const all = [...fates, ...second.fates];
  if (second.kind === 'incomplete') {
    return incomplete(all, second.reason);
  }
  const third = readKey(deps.memento);
  if (third.kind === 'failed') {
    return incomplete(all, third.reason);
  }
  if (!sameValue(third.value, changed)) {
    return incomplete(all, 'the old store changed twice while it was being carried across');
  }

  return clear(deps, all);
}

/**
 * The key, gone — once the store has answered one more time that it is still there.
 *
 * <p>A disk can go away between the pass and this line, and emptying the memento on the strength of
 * records a store that has stopped answering holds is the one unrecoverable mistake here. A memento
 * that would not take the write leaves the key and says so.</p>
 */
async function clear(deps: ImportDeps, fates: readonly Fate[]): Promise<ImportReport> {
  const state = await deps.store.state();
  if (state.kind === 'unavailable') {
    return incomplete(fates, `the store stopped answering before the old one could be emptied (${state.reason})`);
  }
  // THE SEAL, one statement from the update it protects. Unbind so nothing new can queue, then
  // drain what already has — in that order, or a write issued a moment ago lands after the clear and
  // fills the key again, which makes the migration re-run on every activation for ever.
  //
  // And here rather than earlier: everything above this line can still refuse, and an edit made
  // while a refusal was being decided would have been written to neither store. The window between
  // this line and the update is one `await` wide, and nothing of the person's can enter it.
  await deps.seal?.();
  try {
    await deps.memento.update(TAB_STORE_KEY, undefined);

    return { kind: 'migrated', fates };
  } catch (reason) {
    console.error('ConnectOtherAIs: every chat conversation is on disk, but the old store could not be emptied', reason);

    return incomplete(fates, `every conversation is on disk, but the old store could not be emptied${codeOf(reason)}`);
  }
}
