import type { RoundCountsRead, RoundStore } from './roundCounts';
import type { RoundsSnapshot, StoredLine } from './roundCountsCache';
import type { LedgerLine } from './roundMoney';

/**
 * The ledger lines of both stores, as the Rounds block reads them (E6.S2, "Stores are not accounts" of
 * todo/PLAN_the_round_counter_tail_E6.md). Pure.
 *
 * <p><b>Activity is summed; status is not.</b> Tokens, money and launches add up across stores. A status — a stated reset — is
 * per (provider, store), so one store's success never hides another's refusal.</p>
 *
 * <p><b>Keyed money follows the round.</b> A line that carries a round key counts only from the store the merged answer's
 * `keyStore` attributes that key to — so a copied store is one round, one launch, one dollar. A key with NO entry (its round
 * began before the window, is still running, or a snapshot is stale) is counted from the store it was read in: ownership never
 * drops spend, since rounds are windowed by their start and money by its line's time.</p>
 */

/** The stores the merged answer named — none from a single-store answer or a read that failed. */
export function storesOfRead(read: RoundCountsRead): readonly RoundStore[] {
  return read.state === 'counted' ? (read.answer.stores ?? []) : [];
}

/** This side's own store id, when the merged answer named one. */
export function liveStoreId(read: RoundCountsRead): string | undefined {
  return storesOfRead(read).find((one) => one.live)?.id;
}

/** Which store owns each round key the answer counted, over every row of every window. */
export function keyOwners(read: RoundCountsRead): ReadonlyMap<string, string> {
  const rows = read.state === 'counted' ? read.answer.windows.flatMap((window) => window.rows) : [];

  return new Map(rows.flatMap((row) => Object.entries(row.keyStore ?? {})));
}

/** A line with its store resolved (a line of this side carries this side's id) and whether it came from ANOTHER store. */
export type ViewLine = StoredLine & { readonly foreign: boolean };

/**
 * Every line, store resolved: this side's lines carry this side's id once the answer named one, and a line is foreign when it
 * was read from a store that is not this side's. With no store named (a single-store answer) only a line the loader tagged
 * with a directory is foreign.
 */
export function ledgerView(snapshot: Pick<RoundsSnapshot, 'read' | 'ledger'>): readonly ViewLine[] {
  const live = liveStoreId(snapshot.read);

  return snapshot.ledger.map((line) => {
    const store = line.store ?? live;

    return { ...line, ...(store === undefined ? {} : { store }), foreign: store !== undefined && store !== live };
  });
}

/** The lines that count: a keyed line only from the store its key was attributed to. */
export function ownedLines(snapshot: Pick<RoundsSnapshot, 'read' | 'ledger'>): readonly ViewLine[] {
  const owners = keyOwners(snapshot.read);

  return ledgerView(snapshot).filter((line) => followsItsRound(owners, line));
}

/** A line with no key, a key nobody owns, or no store to compare is kept; otherwise it must come from the owner. */
function followsItsRound(owners: ReadonlyMap<string, string>, line: LedgerLine): boolean {
  const owner = owners.get(line.round ?? '');

  return owner === undefined || line.store === undefined || owner === line.store;
}
