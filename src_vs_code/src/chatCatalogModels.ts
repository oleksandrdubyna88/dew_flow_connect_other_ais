import type { ModelPreset } from './chatPresets';
import { wasMoved, type MovedPreset } from './chatPresetMove';
import type { Vendor } from './vendors';

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
