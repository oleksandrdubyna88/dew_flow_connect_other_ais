import { ConfigReader, overlaidReader, seedOverlay, SettingsOverlay } from './settingsShape';
import { overlayKey, Side } from './coaiInstall';

/**
 * The little of `vscode.Memento` this needs, declared here so a test can implement it.
 *
 * <p>Structurally compatible with the real thing, which is what lets the overlay be tested at all:
 * the suite cannot import `vscode`, and the whole point of this module is that its decisions are
 * checkable.</p>
 */
export interface KeyValueStore {
  get<T>(key: string): T | undefined;
  update(key: string, value: unknown): Thenable<void>;
}

/**
 * This side's own settings, or nothing when it has none.
 *
 * <p>Kept in the client's `globalState` — one database shared by every window of the profile, which
 * is exactly why the key names the SIDE. There is no alternative: VS Code resolves `window`-scoped
 * settings from the client's `settings.json` and hands the same values to every extension host, and
 * no API writes a remote host's machine settings.</p>
 */
export function readOverlay(store: KeyValueStore, side: Side): SettingsOverlay {
  const stored = store.get<Record<string, unknown>>(overlayKey(side));

  return isRecord(stored) ? stored : {};
}

/**
 * Writes one setting into this side's overlay, leaving every other side untouched.
 */
export async function writeOverlay(
  store: KeyValueStore,
  side: Side,
  section: string,
  value: unknown,
): Promise<void> {
  await store.update(overlayKey(side), { ...readOverlay(store, side), [section]: value });
}

/**
 * How ANY caller reads a `coai.*` setting: this side's own value first, the shared one otherwise.
 *
 * <p>This decision is not new — the panel has computed it correctly since the switch shipped, and its
 * own doc already said why there must be only one of it: <i>"a read that goes around it is a setting
 * that silently stays shared"</i>. It said that about a PRIVATE method, so three reads went around it
 * anyway: the chat's two vendor lookups, and the settings file handed to `coai-mcp`. That last one is
 * the expensive one — it is what the GATE reads, so a shared read there runs somebody's reviewers on
 * another side's CLI paths.</p>
 *
 * <p>The side is never worked out here and never defaulted. Callers pass
 * `thisSide(context.globalStorageUri)`, which is the one derivation, so a caller cannot quietly read
 * a different side's settings while still satisfying the type.</p>
 */
export function sideConfigReader(
  shared: ConfigReader,
  perSide: boolean,
  store: KeyValueStore,
  side: Side,
): ConfigReader {
  return perSide ? overlaidReader(shared, readOverlay(store, side)) : shared;
}

/**
 * Turning the switch on: this side keeps what it reads today.
 *
 * <p>Seeded rather than left empty, so enabling changes nothing until something is edited. An empty
 * overlay that falls through to the shared values looks identical — until the first shared edit on
 * another side silently changes this one, which is the surprise the whole feature exists to remove.</p>
 *
 * <p>Idempotent: a side that already has an overlay keeps it. Turning the switch off and on again
 * must not discard what this side had configured.</p>
 */
export async function seedIfEmpty(
  store: KeyValueStore,
  side: Side,
  shared: ConfigReader,
): Promise<SettingsOverlay> {
  const existing = readOverlay(store, side);
  if (Object.keys(existing).length > 0) {
    return existing;
  }

  const seeded = seedOverlay(shared);
  await store.update(overlayKey(side), seeded);

  return seeded;
}

/** Anything else in that slot — a string, an array, a null left by an older build — is no overlay. */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
