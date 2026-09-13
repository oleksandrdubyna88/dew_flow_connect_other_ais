import { join } from 'node:path';
import { writeFileAtomically } from './atomicFile';
import { ConversationRecord, fromLegacy, isPrefixOf, isSafeId, saidIn } from './chatStore';
import { ChatStoreFile, Listing, SaveOutcome } from './chatStoreFile';
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
 * <h2>Only a save that landed WHOLE counts</h2>
 *
 * <p>A `partial` save committed the record and not its index, so the conversation exists and
 * nothing can list it; emptying the memento on the strength of that hides a conversation. So the
 * partial ones have their index repaired (a read regenerates it) and then EVERY record filed by this
 * run must appear in the store's own listing before the key may go. `busy`, `refused`, `failed` and
 * `incompatible` never count: the last of those is a file this build cannot read, which is not proof
 * the person's conversation is safe — it may be a newer build's, and the memento's copy is then the
 * only one this build can read. The key stays, the report says so, and the next activation tries
 * again — nothing here is remembered between runs, so a re-run can neither duplicate nor overwrite:
 * the id is the record's own, the comparison is by words, and the fork id is derived.</p>
 *
 * <h2>A damaged record neither vanishes nor wedges</h2>
 *
 * <p>If a record `tabsFrom` would drop counted as missing, the key would never be emptied and this
 * would re-run for ever; if it were ignored, it would be deleted with no trace the moment the key
 * went. So it is QUARANTINED — the raw value written, as it was, under {@link QUARANTINE_DIR} beside
 * the records — and only then counted as settled. Nothing this module cannot file as a conversation
 * is ever discarded; the same for a whole memento value of a shape no build wrote. (A re-run that
 * follows a failed clear quarantines the same value again under a new name; two copies of a thing
 * that was never lost is the cheaper side of that trade.)</p>
 *
 * <h2>The memento is re-read before it is emptied</h2>
 *
 * <p>Another window can add or update a record while this one migrates — the dual write goes on in
 * every window until ITS migration has succeeded — and an unconditional clear would erase it. So the
 * key is read again immediately before the clear; if it changed, what is there now is migrated too,
 * and it is read a third time; if it changed again, the key is left for the next activation. The
 * residual is the interval between the last read and the `update`, and it is stated here rather
 * than pretended away: a write landing inside it is carried by the other window's own store write
 * (the dual write), and lost from the memento only if that store write also failed.</p>
 *
 * <h2>A store that cannot be READ migrates nothing</h2>
 *
 * <p>`unavailable` from the store is the one state in which touching the memento is unrecoverable:
 * emptying the source of truth because the disk would not answer. Nothing is written, the key is
 * untouched, and the report names the reason. The dual write stays on in that window.</p>
 *
 * <p>No `vscode` here. The rules are pure and tested as values; the I/O below them takes a memento
 * (two methods of it), a store and a directory, and is tested against a real temporary directory
 * with a memento fake. Nothing throws: every failure is a typed fate with a sentence, and every
 * one is said on the console — never swallowed, per `coding-style.md`.</p>
 */

/** Where what could not be filed as a conversation is kept, as it was. A subdirectory, so the store's listing never reads it. */
export const QUARANTINE_DIR = 'quarantine';

/**
 * The suffix a memento copy is filed under when its own id holds DIFFERENT words on disk.
 *
 * <p>Derived rather than minted: a random id would make a re-run after a crash between the fork
 * and the clear file a second copy, and idempotency by id is the property every other branch here
 * has. A dash and a letter keep it a safe filename component, and a fork that is itself diverged is
 * not forked again — that is the one branch that reports `unconfirmed` instead, so nothing here can
 * chain.</p>
 */
export const FORK_SUFFIX = '-m';

/** The sentence a fork that would itself diverge gets, because nothing forks twice. */
export const DIVERGED_TWICE =
  'the copy on disk and the copy in the old store hold different words, and so does the copy already set aside for that';

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
  /** The memento's copy is now on disk under `id`. `indexed` false is a `partial` save, repaired before the listing check. */
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

/** The ids a run says are on disk — every one of which the store's listing must contain before the key may go. */
export const filedIds = (fates: readonly Fate[]): readonly string[] =>
  fates.flatMap((fate) => (fate.kind === 'written' || fate.kind === 'kept' ? [fate.id] : []));

/** Whether every record came to rest — on disk under an id, or in quarantine as it was. */
export const allSettled = (fates: readonly Fate[]): boolean => fates.every((fate) => fate.kind !== 'unconfirmed');

/** The filed ids the listing does not carry: conversations that exist and cannot be found. */
export function unlisted(fates: readonly Fate[], listing: Listing): readonly string[] {
  if (listing.kind === 'unavailable') {
    return filedIds(fates);
  }
  const listed = new Set(listing.metas.map((meta) => meta.id));

  return filedIds(fates).filter((id) => !listed.has(id));
}

/**
 * The memento's copy, written OVER the store's older one: the memento's words, and what the store
 * already knew better — when the conversation began, what it was opened from, where it is filed,
 * and whether a new chat closed it. The memento has none of those; the dual write did.
 */
export function overDisk(tab: SavedTab, disk: ConversationRecord, workspace: string): ConversationRecord {
  return {
    ...fromLegacy(tab, disk.workspace.length > 0 ? disk.workspace : workspace),
    createdAt: disk.createdAt,
    source: disk.source,
    ...(disk.closedAt === undefined ? {} : { closedAt: disk.closedAt }),
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
  /** The clock, pinned by tests. It dates the locks and names the quarantine files. */
  readonly at?: number;
}

/** A pass over one value of the key: what landed, and whether the key may go on its account. */
type Pass =
  | { readonly kind: 'settled'; readonly fates: readonly Fate[] }
  | { readonly kind: 'incomplete'; readonly fates: readonly Fate[]; readonly reason: string };

const codeOf = (reason: unknown): string => {
  const code = (reason as { code?: unknown } | null)?.code;

  return typeof code === 'string' ? ` (${code})` : '';
};

/**
 * Carry every memento record into the store, and empty the key once all of them are confirmed.
 *
 * <p>Never throws. Runs once per activation; the serializer awaits it before it answers, so a tab
 * whose conversation has not been carried yet cannot read as absent and be disposed.</p>
 */
export async function importLegacyTabs(deps: ImportDeps): Promise<ImportReport> {
  const at = deps.at ?? Date.now();
  const raw = deps.memento.get(TAB_STORE_KEY);
  if (contentsOf(raw).kind === 'empty') {
    return { kind: 'nothing' };
  }
  const state = await deps.store.state();
  if (state.kind === 'unavailable') {
    return { kind: 'unavailable', reason: state.reason };
  }
  const first = await pass(deps, raw, at);
  if (first.kind === 'incomplete') {
    return first;
  }

  return clearIfUnchanged(deps, raw, first.fates, at);
}

/** Migrate whatever the key holds NOW, then prove it: every filed id in the store's own listing. */
async function pass(deps: ImportDeps, raw: unknown, at: number): Promise<Pass> {
  const contents = contentsOf(raw);
  if (contents.kind === 'empty') {
    return { kind: 'settled', fates: [] };
  }
  const fates = contents.kind === 'foreign'
    ? [await quarantine(deps.store.dir, raw, 'value', at)]
    : await migrateRecords(deps, contents, at);
  const open = fates.filter((fate) => fate.kind === 'unconfirmed').length;
  if (open > 0) {
    return { kind: 'incomplete', fates, reason: `${open} conversation(s) could not be confirmed on disk` };
  }
  await repairIndexes(deps.store, fates, at);
  const missing = unlisted(fates, await deps.store.listMeta());
  if (missing.length > 0) {
    return { kind: 'incomplete', fates, reason: `${missing.length} conversation(s) are on disk but not in the list that finds them` };
  }

  return { kind: 'settled', fates };
}

/** Every damaged entry set aside, every record carried — one after another, so the console reads in order. */
async function migrateRecords(
  deps: ImportDeps,
  contents: Extract<MementoContents, { kind: 'records' }>,
  at: number,
): Promise<readonly Fate[]> {
  const fates: Fate[] = [];
  for (const [index, entry] of contents.damaged.entries()) {
    fates.push(await quarantine(deps.store.dir, entry, `entry-${index}`, at));
  }
  for (const tab of contents.tabs) {
    fates.push(await migrateOne(deps, tab, at, true));
  }

  return fates;
}

/**
 * One record: read what the store holds under its id, and let {@link newerOf} decide.
 *
 * <p>The read is NOT a check-then-act on absence: a save at baseline 0 is refused by the store's own
 * swap if anything has appeared in between, and a save at the disk's revision is refused if it moved
 * — either way the fate is `unconfirmed` and the next activation compares again.</p>
 */
async function migrateOne(deps: ImportDeps, tab: SavedTab, at: number, mayFork: boolean): Promise<Fate> {
  const seen = await deps.store.read(tab.id, at);
  if (seen.kind === 'absent') {
    return fateOfSave(tab.id, await deps.store.save(fromLegacy(tab, deps.workspace), 0, at));
  }
  if (seen.kind !== 'record') {
    return { kind: 'unconfirmed', id: tab.id, reason: seen.reason };
  }

  return reconcile(deps, tab, seen.record, at, mayFork);
}

/** Both copies exist. Which is the beginning of the other decides everything. */
async function reconcile(
  deps: ImportDeps,
  tab: SavedTab,
  disk: ConversationRecord,
  at: number,
  mayFork: boolean,
): Promise<Fate> {
  const newer = newerOf(saidIn(tab.messages), saidIn(disk.messages));
  if (newer === 'same' || newer === 'store') {
    return { kind: 'kept', id: tab.id };
  }
  if (newer === 'memento') {
    return fateOfSave(tab.id, await deps.store.save(overDisk(tab, disk, deps.workspace), disk.rev, at));
  }

  return fork(deps, tab, at, mayFork);
}

/** The diverged case: the memento's words under the fork of their id, by the same rules, once. */
async function fork(deps: ImportDeps, tab: SavedTab, at: number, mayFork: boolean): Promise<Fate> {
  const as = forkId(tab.id);
  if (!mayFork || as.length === 0) {
    return { kind: 'unconfirmed', id: tab.id, reason: DIVERGED_TWICE };
  }
  const fate = await migrateOne(deps, { ...tab, id: as }, at, false);
  if (fate.kind === 'unconfirmed') {
    // Attributed to the MEMENTO record, which is what the person has and what the report names:
    // whatever stopped the fork landing, it is `tab.id` that is not on disk.
    return { ...fate, id: tab.id };
  }

  return fate.kind === 'quarantined' ? fate : { ...fate, forkedFrom: tab.id };
}

/** A `partial` save's index, regenerated by the store's own read. Best-effort: the listing check judges the result. */
async function repairIndexes(store: ChatStoreFile, fates: readonly Fate[], at: number): Promise<void> {
  for (const fate of fates) {
    if (fate.kind === 'written' && !fate.indexed) {
      await store.read(fate.id, at);
    }
  }
}

/** The raw value, as it was, under the quarantine directory. Said on the console with its path. */
async function quarantine(dir: string, raw: unknown, label: string, at: number): Promise<Fate> {
  const path = join(dir, QUARANTINE_DIR, `${TAB_STORE_KEY}-${at}-${label}.json`);
  try {
    await writeFileAtomically(path, JSON.stringify(raw) ?? 'undefined');
    console.warn(`ConnectOtherAIs: a stored chat record this build cannot read was set aside, unchanged, at ${path}`);

    return { kind: 'quarantined', at: path };
  } catch (reason) {
    console.error(`ConnectOtherAIs: a stored chat record this build cannot read could not be set aside at ${path}`, reason);

    return { kind: 'unconfirmed', id: label, reason: `a damaged record could not be set aside${codeOf(reason)}` };
  }
}

/** Two memento values, the same by value. What the re-read before the clear compares. */
const sameValue = (left: unknown, right: unknown): boolean => JSON.stringify(left) === JSON.stringify(right);

/**
 * Re-read the key; migrate what changed under us, once; clear only what was seen.
 *
 * <p>`undefined` on the re-read is another window having emptied the key already — every record
 * this run saw is confirmed, so that is `migrated`, and the clear below is harmless.</p>
 */
async function clearIfUnchanged(deps: ImportDeps, seen: unknown, fates: readonly Fate[], at: number): Promise<ImportReport> {
  const again = deps.memento.get(TAB_STORE_KEY);
  if (sameValue(again, seen)) {
    return clear(deps, fates);
  }
  const second = await pass(deps, again, at);
  const all = [...fates, ...second.fates];
  if (second.kind === 'incomplete') {
    return { kind: 'incomplete', fates: all, reason: second.reason };
  }
  if (!sameValue(deps.memento.get(TAB_STORE_KEY), again)) {
    return { kind: 'incomplete', fates: all, reason: 'the old store changed twice while it was being carried across' };
  }

  return clear(deps, all);
}

/** The key, gone. A memento that would not take the write leaves the key and says so. */
async function clear(deps: ImportDeps, fates: readonly Fate[]): Promise<ImportReport> {
  try {
    await deps.memento.update(TAB_STORE_KEY, undefined);

    return { kind: 'migrated', fates };
  } catch (reason) {
    console.error('ConnectOtherAIs: every chat conversation is on disk, but the old store could not be emptied', reason);

    return { kind: 'incomplete', fates, reason: `every conversation is on disk, but the old store could not be emptied${codeOf(reason)}` };
  }
}
