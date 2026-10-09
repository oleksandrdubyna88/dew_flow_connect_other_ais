import { chatModelPresetsFrom, type ModelPreset } from './chatPresets';
import {
  COPIED_FIELDS, copiedOf, entryOf, movedRecordFrom, newestEntries, rowsById, snapshotted, type CopiedField, type MovedCopy, type MovedPreset,
} from './chatPresetMove';
import type { RevisionChoice } from './chatPresetsMessages';

/**
 * A chat preset an OLDER build edited after the move into the catalog — a conflicting revision of the same preset — and
 * the two ways to settle it (research/PLAN_one_model_catalog.md, epic 5 prerequisite (a), R7). Pure.
 *
 * <p><b>Why it is a conflict and not a move.</b> An older build still reads and writes `coai.chatModelPresets`, and the
 * presets stay in the settings until epic 5 for exactly that build. The move used to match a preset to its record entry by
 * a fingerprint of id, runtime, model and name, so an older build's edit of the name made a SECOND row (`chat-<id>-2`) and
 * an edit of the CLI path, the endpoint, the starting text or the Team server was not seen at all: the chat went on with
 * the old values and nobody was told. Neither is the person's intent, and only the person knows which values are right —
 * the row may have been edited on Models since. So the edit is SHOWN, beside the row as it is, and the person chooses.</p>
 *
 * <p><b>What it compares.</b> The preset with the record entry's snapshot — everything the move copied, or what the last
 * choice settled on — never with the live row, so a choice ends the conflict whatever the row says, and a later,
 * DIFFERENT edit raises it again. An entry written before the snapshot existed takes one from its row first
 * (`chatPresetMove.snapshotted`).</p>
 *
 * <p><b>Where the answer lives.</b> In the record, not in memory: both choices give the entry the preset's whole state
 * (and its id-runtime-model-name fingerprint, so an older build reading the record does not move the preset again), and
 * *Use the edited values* writes the row FIRST, so a choice stopped between its two writes is asked again rather than
 * lost. Nothing here writes the presets: they are not this build's to change (`noSecondPresetStore.test.ts`).</p>
 */

/** A raw catalog row, as the settings file holds it. */
type RawRow = Readonly<Record<string, unknown>>;

/**
 * What a conflict is read from: the presets as the move reads them, this side's raw rows, its record — and the row the
 * chat opens on (`coai.chatModel`), whose model name a choice may have to follow.
 */
export interface RevisionStores {
  readonly presets: readonly ModelPreset[];
  readonly rows: readonly RawRow[];
  readonly record: readonly MovedPreset[];
  readonly chatModel: string;
}

/** One field an older build changed: what the row holds now, and what the edited preset says. */
export interface ConflictField {
  readonly field: CopiedField;
  readonly row: string;
  readonly edited: string;
}

/** A preset edited after its move — the row it became, by id and name, and every field the edit changed. */
export interface PresetConflict {
  readonly presetId: string;
  readonly rowId: string;
  readonly rowName: string;
  readonly fields: readonly ConflictField[];
}

/** One write a choice makes, in the order it must land. */
export interface RevisionWrite {
  readonly key: 'vendors' | 'chatModelName' | 'chatPresetsMoved';
  readonly value: unknown;
}

/**
 * Every preset that differs from its entry's snapshot, in the presets' order — none for a preset the record does not
 * hold, an entry whose row is gone (a deleted row stays deleted), or an entry with no snapshot it could take. The record
 * and the rows are each read once for the whole pass, never once per preset (R7's code round, finding 8).
 */
export function presetConflicts(stores: RevisionStores): readonly PresetConflict[] {
  const newest = newestEntries(snapshotted(stores.record, stores.presets, stores.rows));
  const rows = rowsById(stores.rows);

  return stores.presets.flatMap((preset) => conflictOf(preset, newest.get(preset.id), rows));
}

function conflictOf(preset: ModelPreset, entry: MovedPreset | undefined, rows: ReadonlyMap<unknown, RawRow>): readonly PresetConflict[] {
  return entry?.copied === undefined ? [] : conflictOn(preset, entry, entry.copied, rows.get(entry.rowId));
}

/** @param row the entry's row on disk — none once the person deleted it, which raises nothing */
function conflictOn(preset: ModelPreset, entry: MovedPreset, copied: MovedCopy, row: RawRow | undefined): readonly PresetConflict[] {
  const fields = row === undefined ? [] : editedFields(preset, copied);

  return row === undefined || fields.length === 0
    ? []
    : [{ presetId: preset.id, rowId: entry.rowId, rowName: nameOf(row, entry.rowId), fields: fields.map((field) => fieldOf(field, row, preset)) }];
}

/** The copied fields where the preset now says something other than the snapshot — absent and empty being the same. */
function editedFields(preset: ModelPreset, copied: MovedCopy): readonly CopiedField[] {
  const now = copiedOf(preset);

  return COPIED_FIELDS.filter((field) => (now[field] ?? '') !== (copied[field] ?? ''));
}

function fieldOf(field: CopiedField, row: RawRow, preset: ModelPreset): ConflictField {
  return { field, row: textOf(row[field]), edited: copiedOf(preset)[field] ?? '' };
}

function nameOf(row: RawRow, rowId: string): string {
  const name = textOf(row['name']);

  return name.length > 0 ? name : rowId;
}

function textOf(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * What one choice writes — nothing when that preset has no conflict any more (a stale page, or a choice already made in
 * another window), so a second press is harmless.
 *
 * @param choice `use` — the row takes the edited values; `keep` — the row stays as it is
 * @param presetId the preset the conflict is about, as the page names it
 */
export function revisionWrites(choice: RevisionChoice, presetId: string, stores: RevisionStores): readonly RevisionWrite[] {
  const preset = stores.presets.find((one) => one.id === presetId);
  const conflict = presetConflicts(stores).find((one) => one.presetId === presetId);

  return preset === undefined || conflict === undefined ? [] : writesOf(choice, preset, conflict, stores);
}

function writesOf(choice: RevisionChoice, preset: ModelPreset, conflict: PresetConflict, stores: RevisionStores): readonly RevisionWrite[] {
  const record: RevisionWrite = { key: 'chatPresetsMoved', value: settledRecord(snapshotted(stores.record, stores.presets, stores.rows), preset) };

  // The row first: stopped before the record, the conflict is still there to answer; the other way round, it would be
  // gone with the row never changed.
  return choice === 'use' ? [{ key: 'vendors', value: rowsTaking(stores.rows, conflict, preset) }, ...modelNameWrites(conflict, preset, stores), record] : [record];
}

/**
 * The model name the chat opens with follows the row it opens on (R7's code round, finding 5): when that row is the one
 * this choice changes and its model changed, a stale `coai.chatModelName` would open the OLD model on the edited row —
 * the move itself writes both for the same reason. Written where the other writes go, before the record.
 */
function modelNameWrites(conflict: PresetConflict, preset: ModelPreset, stores: RevisionStores): readonly RevisionWrite[] {
  const opensHere = conflict.rowId === stores.chatModel;

  return opensHere && conflict.fields.some((one) => one.field === 'model') ? [{ key: 'chatModelName', value: preset.model }] : [];
}

/** The record with the preset's entry in force holding its whole state — the snapshot and the fingerprint. */
function settledRecord(record: readonly MovedPreset[], preset: ModelPreset): readonly MovedPreset[] {
  const entry = entryOf(preset.id, record);

  return record.map((one) => (one === entry ? { ...one, runtime: preset.runtime, model: preset.model, name: preset.name, copied: copiedOf(preset) } : one));
}

/** The rows with the conflict's row taking the edited value of every field the edit changed — a cleared field removed. */
function rowsTaking(rows: readonly RawRow[], conflict: PresetConflict, preset: ModelPreset): readonly RawRow[] {
  const edited = copiedOf(preset);
  const fields = new Set<string>(conflict.fields.map((one) => one.field));

  return rows.map((row) => (row['id'] === conflict.rowId ? { ...withoutFields(row, fields), ...pick(edited, fields) } : row));
}

function withoutFields(row: RawRow, fields: ReadonlySet<string>): RawRow {
  return Object.fromEntries(Object.entries(row).filter(([key]) => !fields.has(key)));
}

function pick(copied: MovedCopy, fields: ReadonlySet<string>): RawRow {
  return Object.fromEntries(Object.entries(copied).filter(([key]) => fields.has(key)));
}

/**
 * The stores as one side reads them.
 *
 * @param presets `coai.chatModelPresets` as the chat reads them (the user layer's: the presets are not per side)
 * @param read this side's reader, for its catalog rows and its record of the presets it moved
 */
export function revisionStoresReading(presets: unknown, read: (key: string) => unknown): RevisionStores {
  const rows = read('vendors');

  return {
    presets: chatModelPresetsFrom(presets),
    rows: Array.isArray(rows) ? rows.filter((row): row is RawRow => typeof row === 'object' && row !== null) : [],
    record: movedRecordFrom(read('chatPresetsMoved')),
    chatModel: textOf(read('chatModel')),
  };
}

/** The conflicts one side shows — the ONE composition the Chat tab and a choice's host both read. */
export function chatConflictsReading(presets: unknown, read: (key: string) => unknown): readonly PresetConflict[] {
  return presetConflicts(revisionStoresReading(presets, read));
}

/**
 * What a choice needs of the window it is made in: its reads, its writes, its refusal notice and the catalog's turn.
 * The host hands the real ones (`chatPresetsHost.revisionPorts`); a test or a real-editor scenario hands its own — so
 * the whole of a choice, the order and the stop at a refusal included, runs outside a webview.
 */
export interface RevisionPorts {
  /** The presets as the move reads them — the user layer's (`modelKeys.userChatPresets`). */
  readonly presets: () => unknown;
  /** This side's reader, for its rows, its record and the chat model it opens on. */
  readonly reader: () => (key: string) => unknown;
  readonly save: (key: RevisionWrite['key'], value: unknown) => Promise<void>;
  readonly refused: (key: RevisionWrite['key'], error: unknown) => void;
  /** The catalog's turn: no migration reads between the read and the last write. */
  readonly turn: <T>(work: () => Promise<T>) => Promise<T>;
}

/**
 * One choice, carried out: read, decided and written inside ONE catalog turn, in the order {@link revisionWrites} gives,
 * stopping at the first refusal — which is said — so a choice stopped before its record is still on Chat to answer.
 *
 * <p>Answers that the page must be redrawn, ALWAYS (R7's code round, finding 7): a choice on a conflict already settled —
 * in another window, or by a press a moment ago — writes nothing, and answering "no redraw" left its card on Chat with
 * both buttons disabled; a refused write leaves the card to answer again, which only a redraw shows.</p>
 */
export async function applyRevisionChoice(command: { readonly presetId: string; readonly choice: RevisionChoice }, ports: RevisionPorts): Promise<true> {
  await ports.turn(async () => {
    await savedInOrder(revisionWrites(command.choice, command.presetId, revisionStoresReading(ports.presets(), ports.reader())), ports);
  });

  return true;
}

async function savedInOrder(writes: readonly RevisionWrite[], ports: RevisionPorts): Promise<void> {
  for (const one of writes) {
    try {
      await ports.save(one.key, one.value);
    } catch (error: unknown) {
      ports.refused(one.key, error);

      return;
    }
  }
}
