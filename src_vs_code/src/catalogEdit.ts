import { CatalogUse } from './catalogFields';
import { reviewsAnything } from './catalogRules';
import { CALLER_KINDS, ConsultantChoice, consultantChoiceFrom } from './consultSettings';
import { questionRowFrom, QuestionRowSetting } from './qconsultSettings';
import { normaliseId, Vendor, vendorsFrom } from './vendors';
import { asRecord, isRecord, Launch, listOf, RawRow, rawId, sameLaunch, withLaunch } from './catalogLaunch';

/**
 * The old Settings page writes through the catalog (PLAN_one_model_catalog.md E1.4, "one write road").
 *
 * <p>The old page edits a consultant or a question row as a DEFINITION: it resolves the reference, changes one
 * field and writes the whole entry back (`consultantRecordUpdate`; the question tab writes every row resolved).
 * Left to the migration, every such edit would fork a new catalog row and orphan the one before. So, before the
 * write, the change is folded into the catalog: an entry that referred to a catalog row NOTHING ELSE uses has
 * that row rewritten in place and stays a reference; an unchanged row written back resolved becomes the
 * reference again; a reference the edit removes takes its row with it. Anything else passes through, and the
 * migration gives a new definition its own row as it gives every other.</p>
 *
 * <p>Pure. The caller saves `vendors` FIRST and the key second — the order the migration writes in, so a write
 * refused half way leaves a row nobody refers to yet, never a reference to a row that is not there.</p>
 */

/** What this side currently stores, as the reader sees it. */
export interface CatalogState {
  readonly vendors?: unknown;
  readonly consultants?: unknown;
  readonly qconsultRows?: unknown;
  readonly securityLane?: unknown;
}

export interface CatalogFold {
  readonly value: unknown;
  /** The rows to save first — absent when no row changed. */
  readonly vendors?: readonly RawRow[];
}

/**
 * Any write of a setting, folded through the catalog when it is one of the two that refer to rows — what the old
 * page's one save funnel calls, so no write of `consultants` or `qconsultRows` goes around it.
 */
export function foldedWrite(key: string, value: unknown, read: (key: string) => unknown): CatalogFold {
  return key === 'consultants' || key === 'qconsultRows'
    ? editThroughCatalog(key, value, {
      vendors: read('vendors'),
      consultants: read('consultants'),
      qconsultRows: read('qconsultRows'),
      securityLane: read('securityLane'),
    })
    : { value };
}

export function editThroughCatalog(key: 'consultants' | 'qconsultRows', after: unknown, state: CatalogState): CatalogFold {
  const rows = rawRows(state.vendors);
  const fold = key === 'consultants' ? consultantsFold(after, state, rows) : questionsFold(after, state, rows);

  return fold.rows === rows ? { value: fold.value } : { value: fold.value, vendors: fold.rows };
}

interface Working {
  readonly value: unknown;
  readonly rows: readonly RawRow[];
}

// ---------------------------------------------------------------- the consultants

function consultantsFold(after: unknown, state: CatalogState, rows: readonly RawRow[]): Working {
  const before = asRecord(state.consultants);
  const next = asRecord(after);
  const owned = exclusiveRows(state, rows, 'consultant');

  return CALLER_KINDS.reduce<Working>((acc, { id }) => {
    const row = owned.get(referenceOf(consultantChoiceFrom(before[id])));
    if (row === undefined) {
      return acc;
    }

    return consultantOnRow(acc, id, row, consultantChoiceFrom(asRecord(acc.value)[id]));
  }, { value: next, rows });
}

/** One caller whose entry referred to `row`, which only it uses: rewrite the row, keep the reference — or drop the row. */
function consultantOnRow(acc: Working, caller: string, row: Vendor, entry: ConsultantChoice): Working {
  const launch = consultantLaunch(entry);
  if (entry.runtime === '' && entry.vendor.toLowerCase() === row.id) {
    return acc;
  }

  return launch === undefined
    ? { value: acc.value, rows: acc.rows.filter((raw) => rawId(raw) !== row.id) }
    : { value: { ...asRecord(acc.value), [caller]: { vendor: row.id } }, rows: rewritten(acc.rows, row.id, launch) };
}

function consultantLaunch(entry: ConsultantChoice): Launch | undefined {
  return entry.runtime === '' || normaliseId(entry.vendor) !== entry.vendor
    ? undefined
    : { runtime: entry.runtime, model: entry.model, baseUrl: entry.baseUrl, executablePath: entry.executablePath, vault: entry.vendor };
}

/** A legacy-shaped reference's row id, or `''` for a definition. */
function referenceOf(choice: ConsultantChoice): string {
  return choice.runtime === '' ? choice.vendor.toLowerCase() : '';
}

// ---------------------------------------------------------------- the question rows

function questionsFold(after: unknown, state: CatalogState, rows: readonly RawRow[]): Working {
  const owned = exclusiveRows(state, rows, 'qconsult');
  const before = new Map(listOf(state.qconsultRows).flatMap(questionRowFrom).map((row) => [row.id, row] as const));
  const next = listOf(after);
  const kept = new Set(next.flatMap(questionRowFrom).map((row) => row.id));
  const edited = next.reduce<Working>((acc, value, index) => questionOnRow(acc, index, value, before, owned), { value: next, rows });
  const removed = [...before.values()].filter((row) => !kept.has(row.id)).map((row) => owned.get(row.vendor.toLowerCase()));

  return { value: edited.value, rows: edited.rows.filter((raw) => !removed.some((row) => row?.id === rawId(raw))) };
}

function questionOnRow(acc: Working, index: number, value: unknown, before: ReadonlyMap<string, QuestionRowSetting>, owned: ReadonlyMap<string, Vendor>): Working {
  const edit = questionRowFrom(value)
    .filter((now) => now.runtime !== '')
    .flatMap((now) => ownedRowOf(now, before, owned).map((row) => ({ now, row })))[0];
  if (edit === undefined) {
    return acc;
  }
  const { now, row } = edit;
  const launch: Launch = { runtime: now.runtime, model: now.model, baseUrl: now.baseUrl, executablePath: now.executablePath, vault: now.vendor };

  return {
    value: listOf(acc.value).map((one, at) => (at === index ? asReference(one, row.id) : one)),
    rows: sameLaunch(row, launch) ? acc.rows : rewritten(acc.rows, row.id, launch),
  };
}

/** The catalog row this question row referred to before the edit, when only it uses that row. */
function ownedRowOf(now: QuestionRowSetting, before: ReadonlyMap<string, QuestionRowSetting>, owned: ReadonlyMap<string, Vendor>): readonly Vendor[] {
  const was = before.get(now.id);
  const row = was === undefined || was.runtime !== '' ? undefined : owned.get(was.vendor.toLowerCase());

  return row === undefined ? [] : [row];
}

/** The stored question row with its launch fields replaced by the row's id; its own id, prompt, switch and key kept. */
function asReference(value: unknown, rowId: string): unknown {
  const { runtime: _runtime, model: _model, baseUrl: _baseUrl, executablePath: _executablePath, ...kept } = asRecord(value);

  return { ...kept, vendor: rowId };
}

// ---------------------------------------------------------------- the rows

/**
 * The catalog rows serving `use` that exactly ONE reference names — the only rows an edit may rewrite or drop.
 * A row that reviews a stage, serves another feature, is named twice, or is named by a Security lane run is
 * someone else's too, and an edit of one entry must not change it under them.
 */
function exclusiveRows(state: CatalogState, rows: readonly RawRow[], use: CatalogUse): ReadonlyMap<string, Vendor> {
  const counts = referenceCounts(state);

  return new Map(vendorsFrom([...rows])
    .filter((row) => onlyFor(row, use) && counts.get(row.id) === 1)
    .map((row) => [row.id, row] as const));
}

function onlyFor(row: Vendor, use: CatalogUse): boolean {
  return !reviewsAnything(row) && row.uses?.length === 1 && row.uses[0] === use;
}

/** How many references name each row id: consultants, question rows and Security lane runs together. */
function referenceCounts(state: CatalogState): ReadonlyMap<string, number> {
  const stored = asRecord(state.consultants);
  const names = [
    ...CALLER_KINDS.map(({ id }) => referenceOf(consultantChoiceFrom(stored[id]))),
    ...listOf(state.qconsultRows).flatMap(questionRowFrom).filter((row) => row.runtime === '').map((row) => row.vendor.toLowerCase()),
    ...listOf(asRecord(state.securityLane)['runs']).map((run) => String(asRecord(run)['vendor'] ?? '').toLowerCase()),
  ].filter((name) => name.length > 0);

  return names.reduce((counts, name) => counts.set(name, (counts.get(name) ?? 0) + 1), new Map<string, number>());
}

/** The raw row with its launch fields and key name replaced; everything else it holds kept. */
function rewritten(rows: readonly RawRow[], id: string, launch: Launch): readonly RawRow[] {
  return rows.map((raw) => (rawId(raw) === id ? withLaunch(raw, id, launch) : raw));
}

function rawRows(raw: unknown): readonly RawRow[] {
  return listOf(raw).filter((row): row is RawRow => isRecord(row));
}

