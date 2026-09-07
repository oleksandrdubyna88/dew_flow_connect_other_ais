import { ProviderHealth, parseProviders } from './providers';
import { capture } from './versionProbe';

/**
 * Asking the server what it makes of the configured reviewers.
 *
 * <p>Apart from {@link providers} on purpose, and it is the same split `roundsDbRead` has for the
 * same reason: that module is types and pure functions and the PAGE imports it, so a spawn beside
 * them would drag `node:child_process` into a webview bundle. Caught here too, by the same test.</p>
 */

/** Long enough for a cold binary on a slow disk; short enough that a repaint never waits on it. */
const CAP_MS = 8_000;

/**
 * Ask the server, or answer nothing.
 *
 * <p>Every failure is an empty map rather than an exception: a missing binary, a build too old for
 * the flag, a JSON body that changed shape. The panel then shows what it always showed — and an
 * empty map reads as UNKNOWN, never as "everything is fine".</p>
 */
export async function readProviders(executable: string): Promise<Record<string, ProviderHealth>> {
  if (executable.length === 0) {
    return {};
  }
  const { code, output } = await capture(executable, ['--providers'], false, CAP_MS);

  return code === 0 ? parseProviders(output) : {};
}
