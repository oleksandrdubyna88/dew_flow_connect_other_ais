import { parseProviders, ProvidersAnswer } from './providers';
import { capture } from './versionProbe';

/**
 * Asking the server what it makes of the configured reviewers.
 *
 * <p>Apart from {@link providers} on purpose, and it is the same split `roundsDbRead` has for the
 * same reason: that module is types and pure functions and the PAGE imports it, so a spawn beside
 * them would drag `node:child_process` into a webview bundle. Caught here too, by the same test.</p>
 */

/**
 * Long enough for a cold binary on a slow disk; short enough that a repaint never waits on it.
 *
 * <p>Enforced by `capture`, which kills the child at the cap — a probe that hangs must not park a
 * repaint, and a child left running is the next repaint's mystery.</p>
 */
const CAP_MS = 8_000;

/**
 * Ask the server, and say whether it answered.
 *
 * <p>Three outcomes, and the third is the one worth having. **No binary** — nothing was asked, and
 * the Server section already says the server is absent, so nothing more is said. **Asked and it
 * failed** — a non-zero exit (a build too old for the flag exits 64 saying so), a timeout, or a body
 * whose shape moved: nothing is known about any reviewer, no card may claim otherwise, and the
 * Server section says the check itself could not be made. **Answered** — the map, which the cards
 * read.</p>
 *
 * <p>An empty parse from a zero exit counts as NOT answered: a real configuration always has at
 * least the two default vendors, so an empty map means the shape moved rather than that somebody
 * configured nobody.</p>
 */
export async function readProviders(executable: string): Promise<ProvidersAnswer> {
  if (executable.length === 0) {
    return { reported: {}, asked: false, answered: false };
  }

  const { code, output } = await capture(executable, ['--providers'], false, CAP_MS);
  if (code !== 0) {
    return { reported: {}, asked: true, answered: false };
  }

  const reported = parseProviders(output);

  return { reported, asked: true, answered: Object.keys(reported).length > 0 };
}
