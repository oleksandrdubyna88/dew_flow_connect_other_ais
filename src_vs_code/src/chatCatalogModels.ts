import { legacyPick, type ChatProviderList, type LegacyPick } from './chatModels';
import { chatModelPresetsFrom, type ModelPreset } from './chatPresets';
import { movedRecordFrom, wasMoved, type MovedPreset } from './chatPresetMove';
import { vendorsFrom, type Vendor } from './vendors';

/**
 * What the chat lists as its models, once its presets have moved into the catalog (todo/PLAN_one_model_catalog.md
 * E4.6a): the catalog rows ticked Chat — a moved preset's own row, or any model a person ticked Chat on Models — under
 * their row ids, in the shape every chat path already takes. Pure.
 *
 * <p>A preset the record does not hold is listed too: before a layer's first move, after a move the 64-row cap
 * refused, in a layer the person restored. So nothing the chat offered disappears while the move has not happened, and
 * a preset that HAS moved is never listed twice — nor brought back after its row was removed on Models.</p>
 */

function ticked(row: Vendor): boolean {
  return (row.uses ?? []).includes('chat');
}

/** A row's optional fields, and what each is called on a chat model — carried only when the row has it. */
const OPTIONAL: readonly (readonly [keyof Vendor, keyof ModelPreset])[] = [
  ['chatStartingPrompt', 'startingPrompt'], ['teamServerId', 'teamServerId'], ['remoteVendor', 'remoteVendor'], ['vaultKeyName', 'vaultKeyName'],
  // A row chats as it reviews (E4.6c, D8).
  ['effort', 'effort'], ['systemPrompt', 'systemPrompt'],
];

function optionalOf(row: Vendor): Partial<ModelPreset> {
  return Object.fromEntries(OPTIONAL.filter(([from]) => row[from] !== undefined).map(([from, to]) => [to, row[from]]));
}

/** A row as the chat's model: its name, its starting text, its server and the key it is filed under, where it has them. */
function asChatModel(row: Vendor): ModelPreset {
  return {
    id: row.id,
    name: row.name ?? row.id,
    runtime: row.runtime,
    model: row.model,
    // The catalog has no "main": the model the chat opens on is `coai.chatModel`, which the move points at the main row.
    main: false,
    executablePath: row.executablePath,
    baseUrl: row.baseUrl,
    ...optionalOf(row),
  };
}

/**
 * The chat's models.
 *
 * @param presets `coai.chatModelPresets` as the chat reads them
 * @param rows this side's catalog rows
 * @param record this side's record of the presets it moved
 */
export function chatModelsOf(presets: readonly ModelPreset[], rows: readonly Vendor[], record: readonly MovedPreset[]): readonly ModelPreset[] {
  return [...rows.filter(ticked).map(asChatModel), ...presets.filter((preset) => !wasMoved(preset, record))];
}

/**
 * The chat's models as one side reads them — the ONE composition every reader takes, so the chat and the settings'
 * picker cannot list two different things.
 *
 * @param presets `coai.chatModelPresets` as the chat reads them (the user layer's: the presets are not per side)
 * @param read this side's reader, for its catalog rows and its record of the presets it moved
 */
export function chatModelsReading(presets: unknown, read: (key: string) => unknown): readonly ModelPreset[] {
  return chatModelsOf(chatModelPresetsFrom(presets), vendorsFrom(read('vendors')), movedRecordFrom(read('chatPresetsMoved')));
}

/**
 * The row a saved value names now: an old preset id — a conversation's record, or `coai.chatModel` — is followed to
 * the row the move made of it; anything else is returned as it is.
 */
export function movedTo(saved: string, record: readonly MovedPreset[]): string {
  return record.find((one) => one.presetId === saved)?.rowId ?? saved;
}

/**
 * The pair a resumed conversation opens on. The row it recorded, while that row is still offered — so two rows that
 * offer one model name cannot swap its model, starting text or effort; otherwise what the saved model value says, an
 * old preset id followed through the record first.
 *
 * @param providerId the row the conversation recorded, or empty for a record written before it was recorded
 * @param modelId the conversation's saved model value
 */
export function resumedPick(
  list: ChatProviderList,
  specs: readonly Vendor[],
  record: readonly MovedPreset[],
  providerId: string,
  modelId: string,
): LegacyPick {
  const row = list.providers.find((one) => one.id === providerId);

  // A value that names a row that exists is that row — `coai.chatModel` has always held one — never an old preset of the
  // same id.
  const named = list.providers.some((one) => one.id === modelId) ? modelId : movedTo(modelId, record);

  return row === undefined ? legacyPick(list, specs, named) : { providerId, modelId, candidates: [] };
}
