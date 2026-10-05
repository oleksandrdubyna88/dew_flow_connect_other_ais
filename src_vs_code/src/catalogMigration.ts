import { CatalogUse, usesFrom } from './catalogFields';
import { catalogRefusal, reviewsAnything } from './catalogRules';
import { CALLER_KINDS, consultantChoiceFrom, DEFAULT_CONSULT, sameChoice } from './consultSettings';
import { questionRowFrom } from './qconsultSettings';
import { DEFAULT_VENDORS, freeVendorId, normaliseId, Vendor, vendorsFrom } from './vendors';
import { asRecord, isRecord, Launch, RawRow, rawId, sameLaunch, withLaunch } from './catalogLaunch';

/**
 * The one-time move of every model DEFINITION into the catalog (PLAN_one_model_catalog.md D3, E1.3).
 *
 * <p>A consultant or question-consultant entry that carries its own runtime becomes a row of `coai.vendors`
 * that reviews nothing and lists the feature in `uses`; the entry becomes a reference to that row. Resolving
 * the reference gives back the definition exactly (`resolveConsultant`, `qconsultSettingsFrom`), so the env
 * block coai-mcp reads is byte-identical before and after — the promise of an epic in which nothing changes
 * for the person.</p>
 *
 * <p>Pure: one settings LAYER in (the user layer, or one side's overlay — each migrated on its own), the
 * writes out, in the order that makes an interrupted run safe: the backup first and only once, then the
 * rows, then the references, then the marker. A rerun finds a row already added by its exact fields, so it
 * finishes an interrupted run instead of duplicating it. Chat presets are NOT moved here: chat conversations refer
 * to a preset by id (E4.3). The Bugz model is, from E2.1 — but only when the installed coai-mcp ranks by runtime
 * ({@link MigrationOptions.bugzByRuntime}): an older one matches the row id `local` and would refuse `bugz-local`.</p>
 */

/** The marker a layer carries once migrated, and once restored — after which nothing migrates it by itself. */
export const MIGRATED = 'migrated';
export const RESTORED = 'restored';

/** The keys a migration may rewrite, and so the keys its backup holds. */
export const BACKED_UP: readonly ('vendors' | 'consultants' | 'qconsultRows' | 'bugzModel')[] = ['vendors', 'consultants', 'qconsultRows', 'bugzModel'];

/** One layer's raw values — `undefined` is a key this layer does not hold. */
export interface CatalogLayer {
  readonly vendors?: unknown;
  readonly consultants?: unknown;
  readonly qconsultRows?: unknown;
  /** `bugzModel` — `rowId/model`. */
  readonly bugzModel?: unknown;
  /** `catalogMigration`. */
  readonly marker?: unknown;
  /** `migratedFrom`. */
  readonly backup?: unknown;
  /** What `vendors` reads when this layer holds none: the shared value, or nothing for the shipped rows. */
  readonly sharedVendors?: unknown;
}

export interface LayerWrite {
  readonly key: 'migratedFrom' | 'vendors' | 'consultants' | 'qconsultRows' | 'bugzModel' | 'catalogMigration';
  /** `undefined` removes the key from the layer. */
  readonly value: unknown;
}

export type MigrationOutcome =
  | { readonly kind: 'unchanged' }
  | { readonly kind: 'unchanged-with-skips'; readonly skipped: readonly string[] }
  | { readonly kind: 'restored' }
  | { readonly kind: 'refused'; readonly why: string }
  | { readonly kind: 'migrate'; readonly writes: readonly LayerWrite[]; readonly skipped: readonly string[] };

/** What a definition needs of a row — one {@link Launch}, the shape the old page's save compares with too. */
type Definition = Launch;

/** A definition found in the layer, the row id it would like, and the reference that must not equal its shipped pair. */
interface Found {
  readonly use: CatalogUse;
  readonly desired: string;
  readonly definition: Definition;
  readonly caller?: string;
  readonly question?: number;
  /** The Bugz model's own name — what `bugzModel` names after `<row id>/`. */
  readonly bugz?: string;
}


/** What this layer needs done, or why nothing is. */
export interface MigrationOptions {
  /** Whether the installed coai-mcp ranks Bugz by the row's runtime (`--features` lists `bugzRuntime`). */
  readonly bugzByRuntime?: boolean;
}

export function migrateLayer(layer: CatalogLayer, options: MigrationOptions = {}): MigrationOutcome {
  if (layer.marker === RESTORED) {
    return { kind: 'restored' };
  }
  const { found, skipped } = definitionsIn(layer, options);
  const placed = placeAll(baseRows(layer), found);
  const repaired = repairUses(placed.rows, referencesIn(layer));
  const rows = { rows: repaired.rows, changed: placed.changed || repaired.changed };

  return outcomeOf(writesFor(layer, found, placed.ids, rows), skipped, catalogRefusal(vendorsFrom([...rows.rows])), rows.changed);
}

function outcomeOf(writes: readonly LayerWrite[], skipped: readonly string[], refusal: string, rowsChanged: boolean): MigrationOutcome {
  if (refusal.length > 0 && rowsChanged) {
    return { kind: 'refused', why: refusal };
  }

  return writes.length > 0 ? { kind: 'migrate', writes, skipped } : quiet(skipped);
}

function quiet(skipped: readonly string[]): MigrationOutcome {
  return skipped.length === 0 ? { kind: 'unchanged' } : { kind: 'unchanged-with-skips', skipped };
}

// ---------------------------------------------------------------- what the layer holds

/** The rows this layer would write over: its own, else the shared ones, else the shipped ones — raw. */
function baseRows(layer: CatalogLayer): readonly RawRow[] {
  const raw = layer.vendors ?? layer.sharedVendors;

  return Array.isArray(raw) ? raw.filter(isRecord) : DEFAULT_VENDORS.map((row) => ({ ...row }));
}

/** Every definition in the layer, in caller order then row order; a name that is not a valid id is skipped. */
function definitionsIn(layer: CatalogLayer, options: MigrationOptions): { found: readonly Found[]; skipped: readonly string[] } {
  const bugz = options.bugzByRuntime === true ? bugzDefinition(layer.bugzModel, vendorsFrom([...baseRows(layer)])) : [];
  const all = [...consultantDefinitions(layer.consultants), ...questionDefinitions(layer.qconsultRows), ...bugz];
  const valid = all.filter((one) => normaliseId(one.definition.vault) === one.definition.vault);

  return {
    found: valid,
    skipped: all.filter((one) => !valid.includes(one)).map(skipSentence),
  };
}

function skipSentence(one: Found): string {
  const where = one.caller === undefined ? `question row ${one.question ?? 0}` : `the ${one.caller} consultant`;

  return `${where} names '${one.definition.vault}', which is not a model id (lower-case letters, digits and dashes) — left as it is`;
}

function consultantDefinitions(raw: unknown): readonly Found[] {
  const stored = isRecord(raw) ? raw : {};

  return CALLER_KINDS.flatMap(({ id }) => {
    const choice = consultantChoiceFrom(stored[id]);

    return choice.runtime === '' || choice.vendor === ''
      ? []
      : [{ use: 'consultant' as const, desired: `consult-${id}`, caller: id, definition: definitionOf(choice, choice.vendor) }];
  });
}

function questionDefinitions(raw: unknown): readonly Found[] {
  const rows = Array.isArray(raw) ? raw : [];

  return rows.flatMap((value, index) => questionRowFrom(value)
    .filter((row) => row.runtime !== '' && row.vendor !== '')
    .map((row) => ({ use: 'qconsult' as const, desired: `ask-${normaliseId(row.id)}`, question: index, definition: definitionOf(row, row.vendor) })));
}

/**
 * The Bugz model `<row id>/<model>`, when its model is not the row's own: a row of that row's launch with the Bugz
 * model, wanted as `bugz-<row id>`. Nothing for a model the row already runs, or a row that is not there.
 */
function bugzDefinition(raw: unknown, rows: readonly Vendor[]): readonly Found[] {
  const [rowId, model] = bugzParts(raw);
  const row = rows.find((one) => one.id === rowId);

  return row === undefined || !movesFrom(row, model)
    ? []
    : [{ use: 'bugz' as const, desired: `bugz-${row.id}`, bugz: model, definition: { ...definitionOf(row, vaultOf(row)), model } }];
}

/** The row id (lower case) and the model of `<row id>/<model>`; empty strings when it is not that. */
function bugzParts(raw: unknown): readonly [string, string] {
  const [rowId = '', model = ''] = typeof raw === 'string' ? splitOnce(raw.trim(), '/') : [];

  return [rowId.toLowerCase(), model];
}

/** A model the row does not already run, through a row that is not Bugz's own. */
function movesFrom(row: Vendor, model: string): boolean {
  return model !== '' && model !== row.model && !bugzOwned(row);
}

/** `rowId/model` split at the FIRST slash — a model name may carry one of its own. */
function splitOnce(text: string, at: string): readonly string[] {
  const index = text.indexOf(at);

  return index < 0 ? [text, ''] : [text.slice(0, index), text.slice(index + 1)];
}

function definitionOf(one: { runtime: string; model: string; baseUrl: string; executablePath: string }, vault: string): Definition {
  return { runtime: one.runtime, model: one.model, baseUrl: one.baseUrl, executablePath: one.executablePath, vault };
}

// ---------------------------------------------------------------- the rows

interface Placed {
  readonly rows: readonly RawRow[];
  /** Definition index → the row id its reference names. */
  readonly ids: readonly string[];
  readonly changed: boolean;
}

/** Each definition joins the row that matches it exactly, or gets a new one — one at a time, so two equal definitions share. */
function placeAll(base: readonly RawRow[], found: readonly Found[]): Placed {
  return found.reduce<Placed>((acc, one) => {
    const next = placeOne(acc.rows, one);

    return { rows: next.rows, ids: [...acc.ids, next.id], changed: acc.changed || next.changed };
  }, { rows: base, ids: [], changed: false });
}

function placeOne(rows: readonly RawRow[], one: Found): { rows: readonly RawRow[]; id: string; changed: boolean } {
  const parsed = vendorsFrom([...rows]);
  const own = one.use === 'bugz' ? parsed.find((row) => row.id === one.desired && bugzOwned(row)) : undefined;
  if (own !== undefined) {
    return inPlace(rows, own, one.definition);
  }
  const match = matchingRow(parsed, one);
  if (match !== undefined) {
    return withUse(rows, match, one.use);
  }
  const id = freeVendorId(one.desired, new Set(parsed.map((row) => row.id)));

  return { rows: [...rows, newRow(id, one)], id, changed: true };
}

/**
 * Bugz's own row, rewritten in place to the newly picked model rather than joined by a second one: the picker writes
 * the setting on every pick, and a new `bugz-<row>-N` per pick would orphan the last until the 64-row cap.
 */
function inPlace(rows: readonly RawRow[], own: Vendor, definition: Definition): { rows: readonly RawRow[]; id: string; changed: boolean } {
  return sameLaunch(own, definition)
    ? { rows, id: own.id, changed: false }
    : { rows: rows.map((raw) => (rawId(raw) === own.id ? withLaunch(raw, own.id, definition) : raw)), id: own.id, changed: true };
}

/** A row that exists for Bugz alone — reviews nothing, used for nothing else: the one the move made. */
function bugzOwned(row: Vendor): boolean {
  return !reviewsAnything(row) && row.uses?.length === 1 && row.uses[0] === 'bugz';
}

/** The row this definition may join: every launch field equal and the key filed under the same name — its desired id first. */
function matchingRow(rows: readonly Vendor[], one: Found): Vendor | undefined {
  const candidates = rows.filter((row) => sameLaunch(row, one.definition) && mayRefer(row.id, one));

  return candidates.find((row) => row.id === one.desired) ?? candidates[0];
}

/** A consultant reference equal to its caller's shipped pair is not sent at all, so it may never be written. */
function mayRefer(rowId: string, one: Found): boolean {
  return one.caller === undefined || !sameChoice(consultantChoiceFrom(referenceTo(rowId)), DEFAULT_CONSULT.stored[one.caller]);
}

/** The name a row's key is filed under: its `vaultKeyName`, else its id. */
function vaultOf(row: Vendor): string {
  return row.vaultKeyName ?? row.id;
}

function newRow(id: string, one: Found): RawRow {
  const { definition } = one;

  return {
    id,
    runtime: definition.runtime,
    model: definition.model,
    enabled: true,
    // Reviews nothing, in this build and in an older one: an older build reads these three flags too.
    plan: false,
    code: false,
    document: false,
    baseUrl: definition.baseUrl,
    executablePath: definition.executablePath,
    ...(definition.vault === id ? {} : { vaultKeyName: definition.vault }),
    uses: [one.use],
  };
}

/** The raw rows with `use` added to one row — unchanged when it already lists it. */
function withUse(rows: readonly RawRow[], match: Vendor, use: CatalogUse): { rows: readonly RawRow[]; id: string; changed: boolean } {
  if (match.uses?.includes(use) === true) {
    return { rows, id: match.id, changed: false };
  }

  return { rows: rows.map((raw) => (rawId(raw) === match.id ? addUse(raw, use) : raw)), id: match.id, changed: true };
}

function addUse(raw: RawRow, use: CatalogUse): RawRow {
  return { ...raw, uses: usesFrom([...usesFrom(raw['uses']), use]) };
}

// ---------------------------------------------------------------- an older build's rewrite

/**
 * The `uses` an older build dropped, given back. An older build parses rows with its own `vendorsFrom`, which
 * keeps no `uses`, and writes them back on its first reviewer edit — so a migrated row comes back reviewing
 * nothing, using nothing, and still carrying the `vaultKeyName` the migration gave it (that build knows the
 * field). Only such a row — and only for a feature a reference in this layer still names it for — is repaired.
 */
function repairUses(rows: readonly RawRow[], references: readonly Reference[]): { rows: readonly RawRow[]; changed: boolean } {
  const lost = vendorsFrom([...rows]).filter(lostItsUses);
  const repairs = references.filter((ref) => lost.some((row) => row.id === ref.rowId));

  return repairs.reduce<{ rows: readonly RawRow[]; changed: boolean }>((acc, ref) => {
    const match = vendorsFrom([...acc.rows]).find((row) => row.id === ref.rowId);
    const next = match === undefined ? { rows: acc.rows, changed: false } : withUse(acc.rows, match, ref.use);

    return { rows: next.rows, changed: acc.changed || next.changed };
  }, { rows, changed: false });
}

function lostItsUses(row: Vendor): boolean {
  return !reviewsAnything(row) && row.uses === undefined && carriesAForeignKeyName(row);
}

/** A CLI row whose key is filed under another name — which only the migration gives a CLI row. */
function carriesAForeignKeyName(row: Vendor): boolean {
  return row.runtime !== 'api' && vaultOf(row) !== row.id;
}

interface Reference {
  readonly rowId: string;
  readonly use: CatalogUse;
}

function referencesIn(layer: CatalogLayer): readonly Reference[] {
  const stored = isRecord(layer.consultants) ? layer.consultants : {};
  const consultants = CALLER_KINDS.map(({ id }) => consultantChoiceFrom(stored[id]))
    .filter((choice) => choice.runtime === '' && choice.vendor !== '')
    .map((choice) => ({ rowId: choice.vendor.toLowerCase(), use: 'consultant' as const }));
  const questions = (Array.isArray(layer.qconsultRows) ? layer.qconsultRows : []).flatMap(questionRowFrom)
    .filter((row) => row.runtime === '' && row.vendor !== '')
    .map((row) => ({ rowId: row.vendor.toLowerCase(), use: 'qconsult' as const }));

  return [...consultants, ...questions, ...bugzReference(layer.bugzModel)];
}

function bugzReference(raw: unknown): readonly Reference[] {
  const [rowId] = bugzParts(raw);

  return rowId === '' ? [] : [{ rowId, use: 'bugz' }];
}

// ---------------------------------------------------------------- the writes

/** Backup → rows → references → marker; each only when it changes something. */
function writesFor(layer: CatalogLayer, found: readonly Found[], ids: readonly string[], rows: { rows: readonly RawRow[]; changed: boolean }): readonly LayerWrite[] {
  const references = referenceWrites(layer, found, ids);
  if (!rows.changed && references.length === 0) {
    return [];
  }

  return [...backupWrite(layer, references.map((write) => write.key)), ...rowsWrite(rows), ...references, ...markerWrite(layer)];
}

/**
 * The backup — written the first time, and never overwritten after: a later run only ADDS a key it is about to rewrite
 * that the backup does not hold yet (the Bugz model, moved by E2.1 into a layer epic 1 already migrated), with that
 * key's value from before this run. What an earlier backup saved stays exactly as it was.
 */
function backupWrite(layer: CatalogLayer, rewrites: readonly string[]): readonly LayerWrite[] {
  if (layer.backup === undefined) {
    return [{ key: 'migratedFrom', value: backupOf(layer, rewrites) }];
  }
  const missing = BACKED_UP.filter((key) => !ALWAYS_BACKED_UP.includes(key) && rewrites.includes(key) && !backedUpKeys(layer.backup).includes(key));

  return missing.length === 0 ? [] : [{ key: 'migratedFrom', value: extended(layer, missing) }];
}

function backedUpKeys(backup: unknown): readonly unknown[] {
  const keys = asRecord(backup)['keys'];

  return Array.isArray(keys) ? keys : [];
}

/** The earlier backup with `missing` added — each key's current value, absence recorded as absence. */
function extended(layer: CatalogLayer, missing: readonly (typeof BACKED_UP)[number][]): { keys: readonly unknown[]; values: Record<string, unknown> } {
  const earlier = asRecord(layer.backup);
  const added = Object.fromEntries(missing.filter((key) => layer[key] !== undefined).map((key) => [key, layer[key]]));

  return { keys: [...backedUpKeys(layer.backup), ...missing], values: { ...asRecord(earlier['values']), ...added } };
}

function rowsWrite(rows: { rows: readonly RawRow[]; changed: boolean }): readonly LayerWrite[] {
  return rows.changed ? [{ key: 'vendors', value: rows.rows }] : [];
}

function markerWrite(layer: CatalogLayer): readonly LayerWrite[] {
  return layer.marker === MIGRATED ? [] : [{ key: 'catalogMigration', value: MIGRATED }];
}

/** The keys every migration may rewrite; the Bugz model is backed up only by a run that moves it. */
const ALWAYS_BACKED_UP: readonly string[] = ['vendors', 'consultants', 'qconsultRows'];

/**
 * The first backup: the keys every run may rewrite, and the Bugz model only when THIS run moves it — a backup naming a
 * key it never needed would have a restore remove a Bugz model the migration never touched.
 */
function backupOf(layer: CatalogLayer, rewrites: readonly string[]): { keys: readonly string[]; values: Record<string, unknown> } {
  const keys = BACKED_UP.filter((key) => ALWAYS_BACKED_UP.includes(key) || rewrites.includes(key));
  const values = Object.fromEntries(keys.filter((key) => layer[key] !== undefined).map((key) => [key, layer[key]]));

  return { keys, values };
}

function referenceWrites(layer: CatalogLayer, found: readonly Found[], ids: readonly string[]): readonly LayerWrite[] {
  const callers = found.flatMap((one, index) => (one.caller === undefined ? [] : [[one.caller, ids[index] ?? ''] as const]));
  const questions = found.flatMap((one, index) => (one.question === undefined ? [] : [[one.question, ids[index] ?? ''] as const]));

  return [
    ...bugzWrite(found, ids),
    ...(callers.length === 0 ? [] : [{ key: 'consultants' as const, value: { ...asRecord(layer.consultants), ...Object.fromEntries(callers.map(([caller, id]) => [caller, referenceTo(id)])) } }]),
    ...(questions.length === 0 ? [] : [{ key: 'qconsultRows' as const, value: questionsReferring(layer.qconsultRows, new Map(questions)) }]),
  ];
}

/** The Bugz model naming its new row: `<row id>/<model>`, the shape the collector and the picker read. */
function bugzWrite(found: readonly Found[], ids: readonly string[]): readonly LayerWrite[] {
  return found.flatMap((one, index) => (one.bugz === undefined ? [] : [{ key: 'bugzModel' as const, value: `${ids[index] ?? ''}/${one.bugz}` }]));
}

/** A consultant reference as the file holds it: the row's id and nothing else — the row says the rest. */
function referenceTo(rowId: string): { vendor: string } {
  return { vendor: rowId };
}

/** The stored question rows with each migrated one's launch fields replaced by its row's id; every other field kept. */
function questionsReferring(raw: unknown, ids: ReadonlyMap<number, string>): readonly unknown[] {
  const rows = Array.isArray(raw) ? raw : [];

  return rows.map((value, index) => {
    const id = ids.get(index);
    if (id === undefined || !isRecord(value)) {
      return value;
    }
    const { runtime: _runtime, model: _model, baseUrl: _baseUrl, executablePath: _executablePath, ...kept } = value;

    return { ...kept, vendor: id };
  });
}

// ---------------------------------------------------------------- restore

export type RestoreOutcome =
  | { readonly kind: 'restore'; readonly writes: readonly LayerWrite[] }
  | { readonly kind: 'nothing-to-restore' };

/**
 * The order a restore writes in — the REVERSE of the migration's: the consultant and question entries get their
 * definitions back first, the rows go last. A restore stopped part way then leaves definitions and perhaps rows nobody
 * refers to, never a reference to a row that is gone (CodeRabbit, PR #681).
 */
const RESTORE_ORDER: readonly (typeof BACKED_UP)[number][] = ['consultants', 'qconsultRows', 'bugzModel', 'vendors'];

/** Every backed-up key exactly as it was — removed where it was absent — and the layer marked restored. */
export function restoreLayer(layer: CatalogLayer): RestoreOutcome {
  const backup = asRecord(layer.backup);
  const saved: readonly unknown[] = Array.isArray(backup['keys']) ? backup['keys'] : [];
  const keys = RESTORE_ORDER.filter((key) => saved.includes(key));
  if (keys.length === 0) {
    return { kind: 'nothing-to-restore' };
  }
  const values = asRecord(backup['values']);

  return {
    kind: 'restore',
    writes: [...keys.map((key) => ({ key, value: values[key] })), { key: 'catalogMigration', value: RESTORED }],
  };
}


