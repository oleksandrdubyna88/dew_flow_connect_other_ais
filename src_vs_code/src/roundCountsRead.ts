import { join } from 'node:path';

import { DATABASE_FILE } from './dataDir';
import { readTailSince } from './ledgerTail';
import { NO_SERVER, RoundCountsRead, roundCountsFrom, WINDOW_MS } from './roundCounts';
import { RoundsLoad } from './roundCountsCache';
import { Run, serverRun } from './roundsDbRead';
import { parseUsageLine } from './usage';

/**
 * Asking `coai-mcp --round-counts`, and the ledger's last seven days beside it (story 2.2 of
 * todo/PLAN_every_round_is_counted.md).
 *
 * <p><b>Through `serverRun`</b>, the one door that hands the child this window's data directory, like every other read
 * of the rounds database — a child left to inherit the window's environment would count the DEFAULT directory's rounds
 * while the panel names another. Its own file rather than a function in `roundsDbRead.ts`, which sits at 796 of its
 * 800 lines.</p>
 *
 * <p><b>This side's store only.</b> The other side's `coai.db` is never opened: it is in WAL mode, and WAL's shared
 * memory and locks are not coordinated across the `\\wsl.localhost` / drvfs boundary. {@link otherStores} only asks
 * whether one EXISTS, so the block can say its rounds are not included.</p>
 */

/** Reading counts rounds in SQL over an index; a cap well above that, and below a repaint anybody waits on. */
const CAP_MS = 8_000;

/** The counts, or why there are none — 64, a timeout, an unreadable answer: never 0. */
export async function readRoundCounts(executable: string, run: Run = serverRun(executable)): Promise<RoundCountsRead> {
  return executable.length === 0 ? NO_SERVER : roundCountsFrom(await run(['--round-counts'], CAP_MS));
}

/**
 * The cache's loader: one `--round-counts` spawn and one tail read of `usage.jsonl` back to the oldest window, the two
 * in parallel. `executable` empty is a side with no server — the counts are unavailable and the ledger is still read.
 */
export function roundsLoader(executable: () => string, usagePath: string, run?: Run): RoundsLoad {
  return async (nowMs) => {
    const binary = executable();
    const [read, tail] = await Promise.all([
      readRoundCounts(binary, run ?? serverRun(binary)),
      readTailSince(usagePath, nowMs - WINDOW_MS['7d'], parseUsageLine),
    ]);

    return { read, ledger: tail.records, ledgerFailure: tail.failure };
  };
}

/**
 * The directories, among the other installations this window was told about, that hold a store of their own — asked
 * by EXISTENCE of the file and nothing more, so the block can name them without reading a byte of them.
 */
export function otherStores(directories: readonly string[], exists: (path: string) => boolean): readonly string[] {
  return directories.filter((directory) => exists(join(directory, DATABASE_FILE)));
}
