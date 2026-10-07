import { rowsAfterMain, type ChatVendorChoice } from './chatPresets';
import { editedRows, type PresetCommand } from './chatPresetsMessages';
import { reviewsAnything } from './catalogRules';
import { freeChatRowId, takenRowIds } from './chatPresetMove';
import { vendorsFrom } from './vendors';

/**
 * The chat presets page's model edits, routed to ONE store (todo/PLAN_one_model_catalog.md E4.6a). A model the chat
 * lists from a catalog row is edited on that row; a preset the move has not taken — before the move, after a refused
 * one, in a restored layer — keeps the preset's own path, and is the only thing that path ever writes. A moved preset is
 * listed as its row, never as the preset, so it can never be edited in `chatModelPresets` again. Pure: the host saves.
 */

type RawRow = Readonly<Record<string, unknown>>;

/** What the page's model edits read: this side's raw catalog rows, the raw presets, and the model the chat opens on. */
export interface ChatModelStores {
  readonly rows: readonly RawRow[];
  readonly presets: readonly RawRow[];
  readonly chatModel: string;
}

/** One setting to save, in order. */
export interface ChatModelWrite {
  readonly key: 'vendors' | 'chatModelPresets' | 'chatModel' | 'chatModelName';
  readonly value: unknown;
}

type ModelCommand = Extract<PresetCommand, { kind: 'edit' | 'remove' }>;

/** A page field, and the row field it edits. */
const ROW_FIELDS: Readonly<Record<string, string>> = { name: 'name', model: 'model', startingPrompt: 'chatStartingPrompt' };

/** What one edit of a model saves — nothing for an id that names nothing, or a value the store already holds. */
export function chatModelEdit(command: ModelCommand, stores: ChatModelStores): readonly ChatModelWrite[] {
  if (stores.rows.some((row) => row['id'] === command.id)) {
    return onRow(command, stores);
  }

  return stores.presets.some((preset) => preset['id'] === command.id) ? onPreset(command, stores.presets) : [];
}

// ---------------------------------------------------------------- a catalog row

function onRow(command: ModelCommand, stores: ChatModelStores): readonly ChatModelWrite[] {
  if (command.kind === 'remove') {
    return [{ key: 'vendors', value: leftTheChat(stores.rows, command.id) }];
  }

  return command.field === 'main' ? mainWrites(command.id, command.value === true, stores.chatModel) : fieldWrites(stores.rows, command);
}

/**
 * Main is the model a new conversation opens on — `coai.chatModel`, with a stale model name cleared so it opens on this
 * row's own model. The chat model cannot be unticked into nothing: tick another instead, as the presets' rule was.
 */
function mainWrites(id: string, on: boolean, chatModel: string): readonly ChatModelWrite[] {
  return on && id !== chatModel ? [{ key: 'chatModel', value: id }, { key: 'chatModelName', value: '' }] : [];
}

function fieldWrites(rows: readonly RawRow[], command: Extract<ModelCommand, { kind: 'edit' }>): readonly ChatModelWrite[] {
  const key = ROW_FIELDS[command.field];
  const next = key === undefined ? rows : rows.map((row) => (row['id'] === command.id ? withField(row, key, command.value) : row));

  return next.every((row, at) => row === rows[at]) ? [] : [{ key: 'vendors', value: next }];
}

/** The row with one field set — the same row back when it already holds that; an emptied starting text removed. */
function withField(row: RawRow, key: string, value: string | boolean): RawRow {
  return key === 'chatStartingPrompt' && String(value).trim().length === 0 ? withoutField(row, key) : withValue(row, key, value);
}

function withoutField(row: RawRow, key: string): RawRow {
  return key in row ? Object.fromEntries(Object.entries(row).filter(([name]) => name !== key)) : row;
}

function withValue(row: RawRow, key: string, value: string | boolean): RawRow {
  return row[key] === value ? row : { ...row, [key]: value };
}

/** A row out of the chat: a row that exists for the chat alone goes; one that serves anything else only loses its tick. */
function leftTheChat(rows: readonly RawRow[], id: string): readonly RawRow[] {
  return rows.flatMap((row) => (row['id'] === id ? untickedOrGone(row) : [row]));
}

function untickedOrGone(row: RawRow): readonly RawRow[] {
  const uses = Array.isArray(row['uses']) ? (row['uses'] as readonly unknown[]) : [];

  return chatOnly(row, uses) ? [] : [{ ...row, uses: uses.filter((use) => use !== 'chat') }];
}

/** A row that reviews nothing and is used for the chat alone — the chat's own, which leaving the chat removes. */
function chatOnly(row: RawRow, uses: readonly unknown[]): boolean {
  const parsed = vendorsFrom([row])[0];

  return parsed !== undefined && !reviewsAnything(parsed) && uses.every((use) => use === 'chat');
}

// ---------------------------------------------------------------- a preset the move has not taken

function onPreset(command: ModelCommand, presets: readonly RawRow[]): readonly ChatModelWrite[] {
  const next = presetsAfter(command, presets);

  return next === presets ? [] : [{ key: 'chatModelPresets', value: next }];
}

/** The presets after the edit — the SAME list back when nothing changes, which writes nothing. */
function presetsAfter(command: ModelCommand, presets: readonly RawRow[]): readonly RawRow[] {
  if (command.kind === 'remove') {
    return presets.filter((preset) => preset['id'] !== command.id);
  }

  return command.field === 'main' ? rowsAfterMain(presets, command.id, command.value === true) : editedRows(presets, command);
}

// ---------------------------------------------------------------- a new model

/** A model added on the page: a catalog row ticked Chat — never a preset — its id from its name. */
export function chatModelAdd(rows: readonly RawRow[], vendor: ChatVendorChoice, model: string, name: string, startingPrompt: string): readonly RawRow[] {
  return [...rows, {
    id: newChatId(rows, name),
    name: name.trim().length > 0 ? name.trim() : 'New model',
    runtime: vendor.runtime,
    model,
    enabled: true,
    plan: false,
    code: false,
    document: false,
    baseUrl: vendor.baseUrl ?? '',
    executablePath: vendor.executablePath ?? '',
    uses: ['chat'],
    ...optionalOf(startingPrompt, vendor),
  }];
}

/** `chat-<the name, normalised>`, the next free one — the rule a moved preset's row takes too (`freeChatRowId`). */
function newChatId(rows: readonly RawRow[], name: string): string {
  return freeChatRowId(name, takenRowIds(rows));
}

function optionalOf(startingPrompt: string, vendor: ChatVendorChoice): RawRow {
  return {
    ...(startingPrompt.trim().length > 0 ? { chatStartingPrompt: startingPrompt.trim() } : {}),
    ...(vendor.teamServerId === undefined ? {} : { teamServerId: vendor.teamServerId }),
    ...(vendor.remoteVendor === undefined ? {} : { remoteVendor: vendor.remoteVendor }),
  };
}
