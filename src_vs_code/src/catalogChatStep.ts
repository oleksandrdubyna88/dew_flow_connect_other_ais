import type { CatalogLayer, LayerWrite } from './catalogMigration';
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

/** What the step changes in a layer. */
export interface ChatStep {
  readonly rows: readonly RawRow[];
  readonly writes: readonly LayerWrite[];
  readonly changed: boolean;
}

const NOTHING = (rows: readonly RawRow[]): ChatStep => ({ rows, writes: [], changed: false });

export function chatStep(layer: CatalogLayer, rows: readonly RawRow[]): ChatStep {
  const moved = keepsOwnRows(layer) ? movedIn(layer, rows) : undefined;

  return moved === undefined || !moved.changed ? NOTHING(rows) : stepOf(layer, moved);
}

/** The user layer, or a side that keeps rows of its own — never a side that reads the shared rows. */
function keepsOwnRows(layer: CatalogLayer): boolean {
  return layer.side !== true || layer.vendors !== undefined;
}

function movedIn(layer: CatalogLayer, rows: readonly RawRow[]): ChatMove {
  return chatMove({ presets: chatModelPresetsFrom(layer.chatPresets), rows, record: movedRecordFrom(layer.chatPresetsMoved), preferred: preferredOf(layer) });
}

/** For a side, the user layer's record — whose row ids it keeps where they are free. */
function preferredOf(layer: CatalogLayer): readonly MovedPreset[] {
  return layer.side === true ? movedRecordFrom(layer.userChatRecord) : [];
}

/** The rows, the record, and — in the user layer — the model the chat opens on. */
function stepOf(layer: CatalogLayer, moved: ChatMove): ChatStep {
  const added = moved.record.slice(movedRecordFrom(layer.chatPresetsMoved).length);
  const chatKeys = layer.side === true ? [] : opensOn(layer, added, moved.main);

  return { rows: moved.rows, writes: [{ key: 'chatPresetsMoved', value: moved.record }, ...chatKeys], changed: true };
}

/**
 * The model the chat opens on, after this run: the main preset's row AND its model — a stale model name would open
 * another model on that row — else the row the saved choice named, when this run moved it; else nothing is written.
 */
function opensOn(layer: CatalogLayer, added: readonly MovedPreset[], main: ChatMove['main']): readonly LayerWrite[] {
  if (main !== undefined) {
    return [{ key: 'chatModel', value: main.rowId }, { key: 'chatModelName', value: main.model }];
  }
  const row = savedRowOf(layer, added);

  return row.length > 0 ? [{ key: 'chatModel', value: row }] : [];
}

/** The row the saved chat model became in this run — '' when this run did not move it. */
function savedRowOf(layer: CatalogLayer, added: readonly MovedPreset[]): string {
  const saved = typeof layer.chatModel === 'string' ? layer.chatModel : '';

  return added.find((one) => one.presetId === saved)?.rowId ?? '';
}

/** The rows the record names, for the uses repair: a chat row an older build wrote back without its `uses`. */
export function chatReferences(layer: CatalogLayer): readonly { readonly rowId: string; readonly use: 'chat' }[] {
  return movedRecordFrom(layer.chatPresetsMoved).map((one) => ({ rowId: one.rowId, use: 'chat' as const }));
}
