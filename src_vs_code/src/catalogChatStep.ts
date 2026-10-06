import { chatModelPresetsFrom } from './chatPresets';
import { chatMove, movedRecordFrom, type ChatMove, type MovedPreset } from './chatPresetMove';

/**
 * The chat presets' step of the catalog migration (todo/PLAN_one_model_catalog.md E4.6a): which rows a layer gains, and
 * which of the chat's own keys it writes — run inside `migrateLayer`, so it shares its turn, its backup and its order.
 * Pure.
 *
 * <p>A side that keeps no rows of its own reads the user layer's, and is left alone: writing rows into it would fork
 * its catalog from the shared one. The chat's own keys — the model it opens on and that model's name — are the user
 * layer's alone, written once, by the run that moves the preset they name.</p>
 */

type RawRow = Readonly<Record<string, unknown>>;

/**
 * What the step reads of a layer — the chat's part of `CatalogLayer`, declared here rather than imported, so the
 * migration that calls this step is not also a module this step depends on (an import cycle bundles and fails).
 */
export interface ChatLayer {
  readonly vendors?: unknown;
  readonly chatPresets?: unknown;
  readonly chatPresetsMoved?: unknown;
  readonly chatModel?: unknown;
  readonly chatModelName?: unknown;
  readonly side?: boolean;
  readonly userChatRecord?: unknown;
  /** For a side: the user layer's `coai.chatModel`, which a side without its own inherits (the risk consultation, R2). */
  readonly userChatModel?: unknown;
}

/** One of the chat's keys the step writes — a `LayerWrite` of the migration's. */
export interface ChatWrite {
  readonly key: 'chatPresetsMoved' | 'chatModel' | 'chatModelName';
  readonly value: unknown;
}

/** What the step changes in a layer. */
export interface ChatStep {
  readonly rows: readonly RawRow[];
  readonly writes: readonly ChatWrite[];
  readonly changed: boolean;
}

const NOTHING = (rows: readonly RawRow[]): ChatStep => ({ rows, writes: [], changed: false });

export function chatStep(layer: ChatLayer, rows: readonly RawRow[]): ChatStep {
  const moved = keepsOwnRows(layer) ? movedIn(layer, rows) : undefined;
  if (moved === undefined) {
    return NOTHING(rows);
  }

  return moved.changed ? stepOf(layer, moved) : remapped(layer, rows);
}

/**
 * A run interrupted after its record was written left `coai.chatModel` naming a preset it recorded (CodeRabbit's
 * architecture note on PR #688); nothing moves this time, so the remap is all that is owed. Never over a chat model that
 * names a row that exists: `coai.chatModel` has always held a row id, so that is the person's own pick.
 */
function remapped(layer: ChatLayer, rows: readonly RawRow[]): ChatStep {
  const writes = ownsChatModel(layer) ? remapOf(layer, rows) : inheritedFix(layer, movedRecordFrom(layer.chatPresetsMoved));

  return writes.length === 0 ? NOTHING(rows) : { rows, writes, changed: true };
}

function remapOf(layer: ChatLayer, rows: readonly RawRow[]): readonly ChatWrite[] {
  const entry = recordedEntryOf(layer, rows);

  return entry === undefined ? [] : remapWrites(layer, entry);
}

/**
 * A side that inherits the user layer's chat model, when that model is a moved preset's row and the side's own move put
 * the SAME preset at another id (its id was taken there): the inherited id names somebody else's row on this side, so
 * the side writes its own — its row for that preset (the risk consultation of epic 4, R2). Nothing otherwise: a side
 * whose row kept the id still inherits.
 */
function inheritedFix(layer: ChatLayer, record: readonly MovedPreset[]): readonly ChatWrite[] {
  const theirs = inheritedEntry(layer);

  return theirs === undefined ? [] : ownRowFor(theirs, record);
}

/** The user layer's record entry for the chat model a side inherits — none for the user layer, or a model no move made. */
function inheritedEntry(layer: ChatLayer): MovedPreset | undefined {
  return layer.side === true ? movedRecordFrom(layer.userChatRecord).find((one) => one.rowId === layer.userChatModel) : undefined;
}

function ownRowFor(theirs: MovedPreset, record: readonly MovedPreset[]): readonly ChatWrite[] {
  const mine = record.find((one) => sameMove(one, theirs));

  return mine === undefined || mine.rowId === theirs.rowId ? [] : [{ key: 'chatModel', value: mine.rowId }];
}

/** The same preset moved in two layers: the same id and the same fingerprint. */
function sameMove(one: MovedPreset, other: MovedPreset): boolean {
  return one.presetId === other.presetId && one.runtime === other.runtime && one.model === other.model && one.name === other.name;
}

/** The record's entry for the preset the chat model still names — none when it names a row that exists, or nothing. */
function recordedEntryOf(layer: ChatLayer, rows: readonly RawRow[]): MovedPreset | undefined {
  const saved = typeof layer.chatModel === 'string' ? layer.chatModel : '';

  return rows.some((row) => row['id'] === saved) ? undefined : movedRecordFrom(layer.chatPresetsMoved).find((one) => one.presetId === saved);
}

/**
 * What the uninterrupted run would have written: the row — and, for the preset ticked MAIN, its model as well, because
 * that run writes both and a stale model name opens another model on the right row (the risk consultation, R4).
 */
function remapWrites(layer: ChatLayer, entry: MovedPreset): readonly ChatWrite[] {
  const main = chatModelPresetsFrom(layer.chatPresets).some((preset) => preset.main && preset.id === entry.presetId);

  return main
    ? [{ key: 'chatModel', value: entry.rowId }, { key: 'chatModelName', value: entry.model }]
    : [{ key: 'chatModel', value: entry.rowId }];
}

/** The user layer, or a side that keeps rows of its own — never a side that reads the shared rows. */
function keepsOwnRows(layer: ChatLayer): boolean {
  return layer.side !== true || layer.vendors !== undefined;
}

function movedIn(layer: ChatLayer, rows: readonly RawRow[]): ChatMove {
  return chatMove({ presets: chatModelPresetsFrom(layer.chatPresets), rows, record: movedRecordFrom(layer.chatPresetsMoved), preferred: preferredOf(layer) });
}

/** For a side, the user layer's record — whose row ids it keeps where they are free. */
function preferredOf(layer: ChatLayer): readonly MovedPreset[] {
  return layer.side === true ? movedRecordFrom(layer.userChatRecord) : [];
}

/** The rows, the record, and — in the user layer — the model the chat opens on. */
function stepOf(layer: ChatLayer, moved: ChatMove): ChatStep {
  const added = moved.record.slice(movedRecordFrom(layer.chatPresetsMoved).length);
  const chatKeys = ownsChatModel(layer) ? opensOn(layer, added, moved.main) : inheritedFix(layer, moved.record);

  return { rows: moved.rows, writes: [{ key: 'chatPresetsMoved', value: moved.record }, ...chatKeys], changed: true };
}

/**
 * Whether this layer holds the chat model it opens on: the user layer always; a side only when its overlay keeps one of
 * its own (`chatModel` is per side, D8) — a side that reads the user layer's is never handed one.
 */
function ownsChatModel(layer: ChatLayer): boolean {
  return layer.side !== true || layer.chatModel !== undefined;
}

/**
 * The model the chat opens on, after this run: the main preset's row AND its model — a stale model name would open
 * another model on that row — else the row the saved choice named, when this run moved it; else nothing is written.
 */
function opensOn(layer: ChatLayer, added: readonly MovedPreset[], main: ChatMove['main']): readonly ChatWrite[] {
  if (main !== undefined) {
    return [{ key: 'chatModel', value: main.rowId }, { key: 'chatModelName', value: main.model }];
  }
  const row = savedRowOf(layer, added);

  return row.length > 0 ? [{ key: 'chatModel', value: row }] : [];
}

/** The row the saved chat model became in this run — '' when this run did not move it. */
function savedRowOf(layer: ChatLayer, added: readonly MovedPreset[]): string {
  const saved = typeof layer.chatModel === 'string' ? layer.chatModel : '';

  return added.find((one) => one.presetId === saved)?.rowId ?? '';
}

/** The rows the record names, for the uses repair: a chat row an older build wrote back without its `uses`. */
export function chatReferences(layer: ChatLayer): readonly { readonly rowId: string; readonly use: 'chat'; readonly proven: true }[] {
  // Proven: the record names exactly the rows the move wrote (R6).
  return movedRecordFrom(layer.chatPresetsMoved).map((one) => ({ rowId: one.rowId, use: 'chat' as const, proven: true as const }));
}
