import { CoaiSettings, envBlock } from './settingsShape';
import { Vendor } from './vendors';

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
 */
export function serverSettingsJson(settings: CoaiSettings, vendors: readonly Vendor[]): string {
  return JSON.stringify(envBlock(settings, vendors), null, 2);
}
