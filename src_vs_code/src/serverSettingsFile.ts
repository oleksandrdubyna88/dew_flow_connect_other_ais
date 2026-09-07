import { CoaiSettings, envBlock } from './settingsShape';
import { Vendor } from './vendors';

/** The key that records which build wrote the file. Read by the next writer, ignored by the server. */
export const WRITTEN_BY = 'COAI_WRITTEN_BY';

/**
 * The settings file the server reads out of its own data directory.
 *
 * <p><b>Why this exists.</b> Settings used to reach the server only through the `env` of the
 * pasted `mcpServers` block, which made every change to a threshold or a language a chore: copy
 * the block again, find the client's config, paste, restart. The two halves already share a
 * directory — sessions and escalations live there — so the settings live there too, and the
 * pasted block goes back to being what it should be: a path to a binary, pasted once.</p>
 *
 * <p>The content is deliberately the SAME shape as the env block. One writer, one reader, one
 * parser on the server: a second encoding would be a second thing to keep in step.</p>
 *
 * <p><b>Plus one key the env block does not carry.</b> `COAI_WRITTEN_BY` is provenance, and it
 * belongs to the FILE rather than to `envBlock`, because `envBlock` also builds the block a person
 * pastes into an MCP client — a stamp there is noise in something a human reads. The server is
 * unaffected: `PanelSettings.UnknownValues` reports unknown VALUES of known keys, never unknown
 * keys, so nothing is raised by its presence.</p>
 */
export function serverSettingsJson(
  settings: CoaiSettings,
  vendors: readonly Vendor[],
  writtenBy = '',
): string {
  const block = envBlock(settings, vendors);

  return JSON.stringify(
    writtenBy.length === 0 ? block : { ...block, [WRITTEN_BY]: writtenBy },
    null,
    2,
  );
}

/**
 * Which build wrote the file that is there now, or an empty string.
 *
 * <p>Anything unreadable is "no stamp", and that is the migration path rather than a fallback worth
 * apologising for: every file written before this key existed reads exactly like a file written by
 * a build too old to have an opinion, which is what it is.</p>
 */
export function writtenBy(existing: string): string {
  try {
    const parsed: unknown = JSON.parse(existing);
    if (typeof parsed !== 'object' || parsed === null) {
      return '';
    }
    const stamp = (parsed as Record<string, unknown>)[WRITTEN_BY];

    return typeof stamp === 'string' ? stamp.trim() : '';
  } catch {
    return '';
  }
}
