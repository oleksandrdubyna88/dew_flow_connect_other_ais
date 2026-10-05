import { CATALOG_USES, type CatalogUse } from './catalogFields';
import { addRefusal, lastStageMessage, lastStagesOf } from './catalogWriteRules';
import { useRefusal } from './modelCardFields';
import { freeVendorId, type Vendor } from './vendors';

/**
 * The Models tab's own edits (todo/PLAN_one_model_catalog.md E3.2) — what the catalog becomes when a use is ticked or a
 * row duplicated. Pure: the host saves `rows` (and `bugzModel` when it moved) through the panel's one save, and shows
 * `refused` or `said` when there is one.
 */
export interface RowsChange {
  readonly rows: readonly Vendor[];
  /** The ranking model to store, when ticking or unticking Bugz moved it (D7); absent when it did not move. */
  readonly bugzModel?: string;
  /** Why nothing changed — '' when it did. */
  readonly refused: string;
  /** What the person is told when something else moved with it — '' for nothing. */
  readonly said: string;
}

function unchanged(rows: readonly Vendor[], refused: string): RowsChange {
  return { rows, refused, said: '' };
}

/** Why a use cannot be toggled on this row at all — '' when it can. */
function toggleRefusal(row: Vendor | undefined, use: string): string {
  if (row === undefined || !(CATALOG_USES as readonly string[]).includes(use)) {
    return `There is no ${use} on a row called that here.`;
  }

  return useRefusal(use as CatalogUse, row.runtime);
}

/** The row's uses with one ticked or unticked, in the catalog's own order. */
function flipped(row: Vendor, use: CatalogUse): readonly CatalogUse[] {
  const held = new Set(row.uses ?? []);
  const on = !held.has(use);

  return CATALOG_USES.filter((one) => (one === use ? on : held.has(one)));
}

/**
 * One use ticked or unticked on a row. Bugz has ONE ranking model (D7): ticking it takes it off every other row and
 * names this row as the model; unticking the row that held it leaves Bugz with none.
 *
 * @param bugzModel what `coai.bugzModel` holds now — `<row>/<model>`
 */
export function toggledUse(rows: readonly Vendor[], id: string, use: string, bugzModel: string): RowsChange {
  const row = rows.find((one) => one.id === id);
  const refused = toggleRefusal(row, use);
  if (refused.length > 0 || row === undefined) {
    return unchanged(rows, refused);
  }
  const uses = flipped(row, use as CatalogUse);

  return use === 'bugz' ? bugzMoved(rows, row, uses, bugzModel) : { rows: withUses(rows, id, uses), refused: '', said: '' };
}

function withUses(rows: readonly Vendor[], id: string, uses: readonly CatalogUse[]): readonly Vendor[] {
  return rows.map((one) => (one.id === id ? { ...one, uses: [...uses] } : one));
}

function bugzMoved(rows: readonly Vendor[], row: Vendor, uses: readonly CatalogUse[], bugzModel: string): RowsChange {
  const on = uses.includes('bugz');
  const others = rows.filter((one) => one.id !== row.id && (one.uses ?? []).includes('bugz')).map((one) => one.id);
  const moved = rows.map((one) => (one.id === row.id ? { ...one, uses: [...uses] } : withoutBugz(one)));

  return {
    rows: moved,
    bugzModel: on ? `${row.id}/${row.model}` : heldElsewhere(bugzModel, row.id),
    refused: '',
    said: on && others.length > 0 ? `Bugz ranks with ${row.id} now — moved from ${others.join(', ')}.` : '',
  };
}

function withoutBugz(row: Vendor): Vendor {
  return (row.uses ?? []).includes('bugz') ? { ...row, uses: (row.uses ?? []).filter((one) => one !== 'bugz') } : row;
}

/** The ranking model after a row unticked Bugz: none when it was that row's, else as it was. */
function heldElsewhere(bugzModel: string, id: string): string {
  return bugzModel.split('/')[0] === id ? '' : bugzModel;
}

/**
 * A row removed — after the page's one confirm, so no second question here — and refused while it is the only
 * switched-on model for a review stage (the card locks the same, from the same rule).
 */
export function removedRow(rows: readonly Vendor[], id: string, bugzModel = ''): RowsChange {
  const row = rows.find((one) => one.id === id);
  const refused = row === undefined ? `There is no model ${id} to remove.` : lockRefusal(rows, row);

  return refused.length > 0 ? unchanged(rows, refused) : { ...withoutRow(rows, id, bugzModel), refused: '', said: '' };
}

/** The catalog without the row — and, when Bugz ranked with it, no Bugz model rather than one naming a row that is gone. */
function withoutRow(rows: readonly Vendor[], id: string, bugzModel: string): Pick<RowsChange, 'rows' | 'bugzModel'> {
  const kept = rows.filter((one) => one.id !== id);

  return bugzModel.split('/')[0] === id ? { rows: kept, bugzModel: '' } : { rows: kept };
}

function lockRefusal(rows: readonly Vendor[], row: Vendor): string {
  const last = lastStagesOf(rows, row);

  return last.length === 0 ? '' : lastStageMessage(row.id, last);
}

/**
 * A copy of a row with its own id — every setting copied, `vaultKeyName` included (two rows may share one key), named
 * "<name> (copy)", right after the source. Refused past the catalog's cap.
 */
export function duplicated(rows: readonly Vendor[], id: string): RowsChange {
  const at = rows.findIndex((one) => one.id === id);
  const refused = at < 0 ? `There is no model ${id} to copy.` : addRefusal(rows);
  if (refused.length > 0) {
    return unchanged(rows, refused);
  }
  const source = rows[at]!;
  const copy: Vendor = {
    ...structuredClone(source),
    id: freeVendorId(source.id.replace(/-\d+$/u, ''), new Set(rows.map((one) => one.id))),
    name: `${source.name ?? source.id} (copy)`,
  };

  return { rows: [...rows.slice(0, at + 1), copy, ...rows.slice(at + 1)], refused: '', said: '' };
}
