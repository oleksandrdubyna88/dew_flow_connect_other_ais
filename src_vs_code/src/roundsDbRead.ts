import { DbLog, EMPTY_LOG, parseLog } from './roundsDb';
import { capture } from './versionProbe';

/**
 * Asking the server for its rounds database.
 *
 * <p>Apart from {@link roundsDb} on purpose: that module is types and pure functions, and the page
 * module imports it. A spawn in the same file would drag `node:child_process` into the bundle the
 * webview page is built from — which is exactly what the bundled-page test caught, with
 * `require is not defined`, the first time this was written as one file.</p>
 */

/** Reading is slower than a version probe and still must not hold up a repaint. */
const CAP_MS = 8_000;

/**
 * What the server says, or an empty log.
 *
 * <p>Anything at all going wrong is an empty log: a server too old for the flag, a database that is
 * not there, a binary that has been replaced mid-read. The page shows what it can either way.</p>
 */
export async function readLog(executable: string, limit = 300): Promise<DbLog> {
  if (executable.length === 0) {
    return EMPTY_LOG;
  }
  const { code, output } = await capture(executable, ['--log', '--limit', String(limit)], false, CAP_MS);

  return code === 0 ? parseLog(output) : EMPTY_LOG;
}

