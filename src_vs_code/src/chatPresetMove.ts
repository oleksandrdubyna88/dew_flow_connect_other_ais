import type { ModelPreset } from './chatPresets';
import { serverVendorOf } from './teamServers';
import { freeVendorId, normaliseId } from './vendors';

/**
 * The move of chat model presets into the catalog — its pure core (todo/PLAN_one_model_catalog.md E4.6a, as revised by
 * the design review of 2026-10-06).
 *
 * <p>Each preset the chat sees becomes a catalog row of its OWN, ticked Chat: never joined to an existing row, because
 * the launch match ignores the Team server and two presets' starting texts would collapse into one. What a later run
 * skips by is the RECORD this writes — each moved preset's id, the row it became, and a snapshot of everything the move
 * copied ({@link COPIED_FIELDS}) — never the row's key name, which a person can change, duplicate or delete. So a deleted
 * row stays deleted.</p>
 *
 * <p>A preset is matched to its entry by its id alone (R7, epic 5 prerequisite (a)): an older build that edits it after
 * the move makes a REVISION of the same preset, which `chatPresetRevision.ts` raises on Chat, never a second row. So a
 * positional id (`preset-N`) that shifted onto a different preset is raised the same way — the person sees both and
 * chooses — where it used to be moved into a row of its own.</p>
 */

/**
 * Every field a moved row takes from its preset — what {@link rowOf} copies, under the row's own names — and so every
 * field an entry's snapshot holds (todo/PLAN_one_model_catalog.md, epic 5 prerequisite (a), R7).
 *
 * <p>ONE list, which `rowOf` is built from ({@link copiedOf}), so a field the move learns to copy is a field the snapshot
 * compares the day it is added. The record used to fingerprint on three of these and nothing else, and an older build's
 * edit of a CLI path, an endpoint, a starting text or a Team server after the move was then silently `unchanged`: the
 * chat kept the old values and nobody was told (the plan round, finding 2).</p>
 */
export const COPIED_FIELDS = ['name', 'runtime', 'model', 'baseUrl', 'executablePath', 'chatStartingPrompt', 'vaultKeyName', 'teamServerId', 'remoteVendor'] as const;

export type CopiedField = (typeof COPIED_FIELDS)[number];

/** What the move copied from a preset, by the row's field names — a field it did not write is absent. */
export type MovedCopy = Readonly<Partial<Record<CopiedField, string>>>;

/** One moved preset, as the record keeps it. */
export interface MovedPreset {
  readonly presetId: string;
  readonly runtime: string;
  readonly model: string;
  readonly name: string;
  readonly rowId: string;
  /**
   * The preset as it was moved — or as it was last resolved — field by field ({@link COPIED_FIELDS}). Optional: an
   * entry written before R7 has none, and takes one from its row on disk the first time this build reads it
   * ({@link snapshotted}). A preset that differs from it is a conflicting revision (`chatPresetRevision.ts`).
   */
  readonly copied?: MovedCopy;
}

/** A raw catalog row, as the settings file holds it. */
type RawRow = Readonly<Record<string, unknown>>;

/** What a run starts from: the presets as the chat reads them, the layer's rows, and its record. */
export interface ChatMoveInput {
  readonly presets: readonly ModelPreset[];
  readonly rows: readonly RawRow[];
  readonly record: readonly MovedPreset[];
  /**
   * Another layer's record — the user layer's, for a side that keeps its own rows: a preset it moved keeps the SAME row
   * id here when that id is free, so the one `coai.chatModel` names the same row on every side.
   */
  readonly preferred?: readonly MovedPreset[];
}

/** What a run ends with — and the row and model the chat opens on, when it moved the main preset. */
export interface ChatMove {
  readonly rows: readonly RawRow[];
  readonly record: readonly MovedPreset[];
  readonly main?: { readonly rowId: string; readonly model: string };
  readonly changed: boolean;
}

/**
 * The record as stored — every entry that has its five strings; anything else left out. A snapshot that is not an
 * object is dropped rather than the entry: an entry without one is still a preset that moved, and takes its snapshot from
 * its row again ({@link snapshotted}) — dropping the ENTRY would move the preset a second time.
 */
export function movedRecordFrom(raw: unknown): readonly MovedPreset[] {
  return (Array.isArray(raw) ? raw : []).filter(isMoved).map(withReadSnapshot);
}

const FIELDS = ['presetId', 'runtime', 'model', 'name', 'rowId'] as const;

function isMoved(value: unknown): value is MovedPreset {
  const one = typeof value === 'object' && value !== null ? value as Record<string, unknown> : undefined;

  return one !== undefined && FIELDS.every((field) => typeof one[field] === 'string');
}

/** The entry with its snapshot as it can be read — its known string fields only — or without one. */
function withReadSnapshot(one: MovedPreset): MovedPreset {
  const { copied, ...entry } = one;
  const raw: unknown = copied;

  return isObject(raw) ? { ...entry, copied: copyFrom(raw) } : entry;
}

function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** The copied fields a stored value holds as strings — a snapshot as written, or a row on disk. */
function copyFrom(value: Readonly<Record<string, unknown>>): MovedCopy {
  return Object.fromEntries(COPIED_FIELDS.flatMap((field) => {
    const held = value[field];

    return typeof held === 'string' ? [[field, held]] : [];
  }));
}

/**
 * Whether the record holds a preset — by its id, whatever the preset says now (R7). It used to be a fingerprint of id,
 * runtime, model and name, so an older build's edit of any of the three after the move was a preset the record did not
 * hold, and was moved again into a second row (`chat-<id>-2`); an edit of anything else was not seen at all. An edit
 * after the move is a revision of the SAME preset, raised as a conflict (`chatPresetRevision.ts`), never a new row.
 */
export function wasMoved(preset: ModelPreset, record: readonly MovedPreset[]): boolean {
  return record.some((one) => one.presetId === preset.id);
}

/**
 * The entry a preset answers to: the NEWEST of its id. Two exist only where epic 4's build moved an older build's edit
 * into a second row, and the newest is the row that edit went to — the one `catalogChatStep.recordedEntryOf` and
 * `chatCatalogModels.movedTo` read too.
 */
export function entryOf(presetId: string, record: readonly MovedPreset[]): MovedPreset | undefined {
  return [...record].reverse().find((one) => one.presetId === presetId);
}

/**
 * The record with a snapshot for every entry in force that has none — an entry written before R7 — taken from its ROW on
 * disk, so an edit an older build already made to the preset differs from it and is raised rather than silently lost (the
 * plan round, finding 4). Where the row is gone (a deleted row stays deleted) or the preset is (finding 3), the entry is
 * left as it is: nothing is read from a value that is not there. The SAME array when nothing was taken, so a caller can
 * tell a run that has something to write.
 */
export function snapshotted(record: readonly MovedPreset[], presets: readonly ModelPreset[], rows: readonly RawRow[]): readonly MovedPreset[] {
  const taken = record.map((one) => (owesSnapshot(one, record, presets) ? withRowSnapshot(one, rows) : one));

  return taken.some((one, index) => one !== record[index]) ? taken : record;
}

function owesSnapshot(one: MovedPreset, record: readonly MovedPreset[], presets: readonly ModelPreset[]): boolean {
  return one.copied === undefined && entryOf(one.presetId, record) === one && presets.some((preset) => preset.id === one.presetId);
}

function withRowSnapshot(one: MovedPreset, rows: readonly RawRow[]): MovedPreset {
  const row = rows.find((candidate) => candidate['id'] === one.rowId);

  return row === undefined ? one : { ...one, copied: copyFrom(row) };
}

/**
 * Every preset the record does not hold yet becomes its own row, in the presets' order; an entry written before R7 takes
 * its snapshot first. A preset the record holds is never moved again, edited or not.
 */
export function chatMove(input: ChatMoveInput): ChatMove {
  const record = snapshotted(input.record, input.presets, input.rows);
  const due = input.presets.filter((preset) => !wasMoved(preset, record));

  const preferred = input.preferred ?? [];

  return due.reduce<ChatMove>((acc, preset) => movedOne(acc, preset, preferred), { rows: input.rows, record, changed: record !== input.record });
}

/** The row id a preset takes: the one another layer gave it when it is free here, else the next free `chat-<id>`. */
function rowIdFor(preset: ModelPreset, taken: ReadonlySet<string>, preferred: readonly MovedPreset[]): string {
  const there = preferredId(preset, preferred);

  return there.length > 0 && !taken.has(there) ? there : freeChatRowId(preset.id, taken);
}

/** The row id another layer gave this preset — '' when it gave none. */
function preferredId(preset: ModelPreset, preferred: readonly MovedPreset[]): string {
  return entryOf(preset.id, preferred)?.rowId ?? '';
}

/**
 * The next free id for a chat row: `chat-<seed, normalised>` — `chat-model` for a seed with nothing left once normalised.
 * The ONE rule for both ways a chat row is made: a preset moved (its id is the seed) and a model added on the Chat
 * presets tab (its name is) — `chatModelEdits.chatModelAdd`.
 */
export function freeChatRowId(seed: string, taken: ReadonlySet<string>): string {
  const id = normaliseId(seed);

  return freeVendorId(id.length > 0 ? `chat-${id}` : 'chat-model', taken);
}

/** The ids the rows already use, lower-cased — what `freeVendorId` compares against. */
export function takenRowIds(rows: readonly Readonly<Record<string, unknown>>[]): ReadonlySet<string> {
  return new Set(rows.map((row) => String(row['id'] ?? '').toLowerCase()));
}

function movedOne(acc: ChatMove, preset: ModelPreset, preferred: readonly MovedPreset[]): ChatMove {
  const adopted = adoptedRow(acc, preset, preferred);
  const rowId = adopted ?? rowIdFor(preset, takenRowIds(acc.rows), preferred);
  const entry: MovedPreset = { presetId: preset.id, runtime: preset.runtime, model: preset.model, name: preset.name, rowId, copied: copiedOf(preset) };

  return {
    rows: adopted === undefined ? [...acc.rows, rowOf(preset, rowId)] : acc.rows,
    record: [...acc.record, entry],
    ...mainAfter(acc, preset, rowId),
    changed: true,
  };
}

/**
 * The row an INTERRUPTED run already wrote for this preset — the rows and the record are two settings writes, and a
 * window closed between them left the row with no record (CodeRabbit's architecture note on PR #688). It is an id this
 * preset could have taken — the preferred one, `chat-<id>`, or a `chat-<id>-N` a collision pushed it to — ticked Chat,
 * no record naming it, and launched EXACTLY as this run would write it: the same runtime, model, name, endpoint,
 * executable and server. Anything else is somebody else's row and is never adopted (the risk consultation, R3).
 */
function adoptedRow(acc: ChatMove, preset: ModelPreset, preferred: readonly MovedPreset[]): string | undefined {
  return candidateIds(acc.rows, preset, preferred)
    .filter((id) => !acc.record.some((one) => one.rowId === id))
    .find((id) => acc.rows.some((row) => row['id'] === id && isThisPresetsRow(row, preset, id)));
}

/** Every id this preset's row may be under: the preferred one, its own base, and the base's numbered family. */
function candidateIds(rows: readonly RawRow[], preset: ModelPreset, preferred: readonly MovedPreset[]): readonly string[] {
  const base = freeChatRowId(preset.id, new Set());
  const family = rows.map((row) => String(row['id'] ?? '')).filter((id) => id.startsWith(`${base}-`) && /^\d+$/u.test(id.slice(base.length + 1)));

  return [preferredId(preset, preferred), base, ...family].filter((id) => id.length > 0);
}

/** The fields that say where a model is launched — a row adopted must hold every one as this run would write it. */
const LAUNCH: readonly string[] = ['runtime', 'model', 'name', 'baseUrl', 'executablePath', 'teamServerId', 'remoteVendor'];

function isThisPresetsRow(row: Readonly<Record<string, unknown>>, preset: ModelPreset, id: string): boolean {
  const ours = rowOf(preset, id);

  return LAUNCH.every((key) => (row[key] ?? '') === (ours[key] ?? '')) && Array.isArray(row['uses']) && row['uses'].includes('chat');
}

/** The main preset the run moved — the first one that claims it, as the presets list itself decides. */
function mainAfter(acc: ChatMove, preset: ModelPreset, rowId: string): Pick<ChatMove, 'main'> {
  if (acc.main !== undefined) {
    return { main: acc.main };
  }

  return preset.main ? { main: { rowId, model: preset.model } } : {};
}

/**
 * The row a preset becomes: reviews nothing, ticked Chat, with the preset's name and starting text, and its vault key
 * filed under the old id when that id is one a key name can hold.
 */
function rowOf(preset: ModelPreset, rowId: string): RawRow {
  const { name, runtime, model, baseUrl, executablePath, ...rest } = copiedOf(preset);

  return {
    id: rowId,
    name,
    runtime,
    model,
    enabled: true,
    // Reviews nothing, in this build and in an older one: an older build reads these three flags too.
    plan: false,
    code: false,
    document: false,
    baseUrl,
    executablePath,
    uses: ['chat'],
    ...rest,
  };
}

/**
 * Everything a preset's row takes from it ({@link COPIED_FIELDS}) — what {@link rowOf} writes, and what the record's
 * snapshot keeps, so the two cannot list different fields.
 */
export function copiedOf(preset: ModelPreset): MovedCopy {
  return {
    name: preset.name,
    runtime: preset.runtime,
    model: preset.model,
    baseUrl: preset.baseUrl,
    executablePath: preset.executablePath,
    ...startingText(preset),
    ...keyName(preset),
    ...teamServer(preset),
  };
}

function startingText(preset: ModelPreset): MovedCopy {
  const words = preset.startingPrompt ?? '';

  return words.length > 0 ? { chatStartingPrompt: words } : {};
}

/** The old id as the key name, only when it is a clean id: `vendorsFrom` lower-cases the field, so any other would drift. */
function keyName(preset: ModelPreset): MovedCopy {
  return normaliseId(preset.id) === preset.id && preset.id.length > 0 ? { vaultKeyName: preset.id } : {};
}

/**
 * A Team-server preset's server and the vendor THAT SERVER knows, written out: it used to be read off the id's
 * `<server>-` prefix, which a `chat-` id no longer carries.
 */
function teamServer(preset: ModelPreset): MovedCopy {
  const server = preset.teamServerId ?? '';

  return preset.runtime === 'remote' && server.length > 0 ? { teamServerId: server, remoteVendor: serverVendorOf(preset, server) } : {};
}
