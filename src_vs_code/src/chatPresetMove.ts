import type { ModelPreset } from './chatPresets';
import { serverVendorOf } from './teamServers';
import { freeVendorId, normaliseId } from './vendors';

/**
 * The move of chat model presets into the catalog — its pure core (todo/PLAN_one_model_catalog.md E4.6a, as revised by
 * the design review of 2026-10-06).
 *
 * <p>Each preset the chat sees becomes a catalog row of its OWN, ticked Chat: never joined to an existing row, because
 * the launch match ignores the Team server and two presets' starting texts would collapse into one. What a later run
 * skips by is the RECORD this writes — each moved preset's id with its fingerprint (runtime, model, name) and the row it
 * became — never the row's key name, which a person can change, duplicate or delete. So a deleted row stays deleted,
 * and a positional id (`preset-N`) that shifted onto a different preset is moved as the different preset it is.</p>
 */

/** One moved preset, as the record keeps it. */
export interface MovedPreset {
  readonly presetId: string;
  readonly runtime: string;
  readonly model: string;
  readonly name: string;
  readonly rowId: string;
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

/** The record as stored — every entry that has its five strings; anything else left out. */
export function movedRecordFrom(raw: unknown): readonly MovedPreset[] {
  return (Array.isArray(raw) ? raw : []).filter(isMoved);
}

const FIELDS = ['presetId', 'runtime', 'model', 'name', 'rowId'] as const;

function isMoved(value: unknown): value is MovedPreset {
  const one = typeof value === 'object' && value !== null ? value as Record<string, unknown> : undefined;

  return one !== undefined && FIELDS.every((field) => typeof one[field] === 'string');
}

/** Whether a preset is the one an entry recorded — the same id, and still the same model under it. */
export function wasMoved(preset: ModelPreset, record: readonly MovedPreset[]): boolean {
  return record.some((one) => one.presetId === preset.id && one.runtime === preset.runtime && one.model === preset.model && one.name === preset.name);
}

/** Every preset the record does not hold yet becomes its own row, in the presets' order. */
export function chatMove(input: ChatMoveInput): ChatMove {
  const due = input.presets.filter((preset) => !wasMoved(preset, input.record));

  const preferred = input.preferred ?? [];

  return due.reduce<ChatMove>((acc, preset) => movedOne(acc, preset, preferred), { rows: input.rows, record: input.record, changed: false });
}

/** The row id a preset takes: the one another layer gave it when it is free here, else the next free `chat-<id>`. */
function rowIdFor(preset: ModelPreset, taken: ReadonlySet<string>, preferred: readonly MovedPreset[]): string {
  const there = preferredId(preset, preferred);

  return there.length > 0 && !taken.has(there) ? there : freeChatRowId(preset.id, taken);
}

/** The row id another layer gave this preset — '' when it gave none. */
function preferredId(preset: ModelPreset, preferred: readonly MovedPreset[]): string {
  return preferred.find((one) => wasMoved(preset, [one]))?.rowId ?? '';
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
  const entry: MovedPreset = { presetId: preset.id, runtime: preset.runtime, model: preset.model, name: preset.name, rowId };

  return {
    rows: adopted === undefined ? [...acc.rows, rowOf(preset, rowId)] : acc.rows,
    record: [...acc.record, entry],
    ...mainAfter(acc, preset, rowId),
    changed: true,
  };
}

/**
 * The row an INTERRUPTED run already wrote for this preset — the rows and the record are two settings writes, and a
 * window closed between them left the row with no record (CodeRabbit's architecture note on PR #688). It is the id
 * this preset would take, ticked Chat, holding this preset's runtime, model and name, and no record names it; anything
 * else under that id is somebody else's row and is never adopted.
 */
function adoptedRow(acc: ChatMove, preset: ModelPreset, preferred: readonly MovedPreset[]): string | undefined {
  return [preferredId(preset, preferred), freeChatRowId(preset.id, new Set())]
    .filter((id) => id.length > 0 && !acc.record.some((one) => one.rowId === id))
    .find((id) => acc.rows.some((row) => row['id'] === id && isThisPresetsRow(row, preset)));
}

const FINGERPRINT: readonly ('runtime' | 'model' | 'name')[] = ['runtime', 'model', 'name'];

function isThisPresetsRow(row: Readonly<Record<string, unknown>>, preset: ModelPreset): boolean {
  return FINGERPRINT.every((key) => row[key] === preset[key]) && Array.isArray(row['uses']) && row['uses'].includes('chat');
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
  return {
    id: rowId,
    name: preset.name,
    runtime: preset.runtime,
    model: preset.model,
    enabled: true,
    // Reviews nothing, in this build and in an older one: an older build reads these three flags too.
    plan: false,
    code: false,
    document: false,
    baseUrl: preset.baseUrl,
    executablePath: preset.executablePath,
    uses: ['chat'],
    ...startingText(preset),
    ...keyName(preset),
    ...teamServer(preset),
  };
}

function startingText(preset: ModelPreset): RawRow {
  return (preset.startingPrompt ?? '').length > 0 ? { chatStartingPrompt: preset.startingPrompt } : {};
}

/** The old id as the key name, only when it is a clean id: `vendorsFrom` lower-cases the field, so any other would drift. */
function keyName(preset: ModelPreset): RawRow {
  return normaliseId(preset.id) === preset.id && preset.id.length > 0 ? { vaultKeyName: preset.id } : {};
}

/**
 * A Team-server preset's server and the vendor THAT SERVER knows, written out: it used to be read off the id's
 * `<server>-` prefix, which a `chat-` id no longer carries.
 */
function teamServer(preset: ModelPreset): RawRow {
  const server = preset.teamServerId ?? '';

  return preset.runtime === 'remote' && server.length > 0 ? { teamServerId: server, remoteVendor: serverVendorOf(preset, server) } : {};
}
