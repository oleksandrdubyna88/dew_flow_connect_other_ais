import { join } from 'node:path';

import { DATABASE_FILE } from './dataDir';
import { ForeignLedgers, foreignTargets, type ForeignReads, SNAPSHOT_FILE } from './foreignLedgers';
import { readTailSince } from './ledgerTail';
import { NO_SERVER, RoundCountsRead, roundCountsFrom, type RoundStore, WINDOW_MS } from './roundCounts';
import { RoundsLoad, type StoredLine } from './roundCountsCache';
import { Run, serverRun } from './roundsDbRead';
import { parseUsageLine, type UsageEntry } from './usage';

/**
 * Asking `coai-mcp --round-counts`, and the ledger's last seven days beside it (story 2.2 of
 * research/PLAN_every_round_is_counted.md).
 *
 * <p><b>Through `serverRun`</b>, the one door that hands the child this window's data directory, like every other read
 * of the rounds database — a child left to inherit the window's environment would count the DEFAULT directory's rounds
 * while the panel names another. Its own file rather than a function in `roundsDbRead.ts`, which sits at 796 of its
 * 800 lines.</p>
 *
 * <p><b>The other side's `coai.db` is never opened</b>: it is in WAL mode, and WAL's shared memory and locks are not
 * coordinated across the `\\wsl.localhost` / drvfs boundary. What crosses is a plain JSON snapshot the owning side's
 * coai-mcp writes after every round (read by `--round-counts-merged`, which this module asks FIRST — E6.S1) and that
 * side's append-only `usage.jsonl`, read from its end ({@link roundsLoader}, `foreignLedgers.ts` — E6.S2).
 * {@link otherStores} only asks whether a `coai.db` EXISTS, for the sentence that names the stores.</p>
 */

/** Reading counts rounds in SQL over an index; a cap well above that, and below a repaint anybody waits on. */
const CAP_MS = 8_000;

/** The counts, or why there are none — 64, a timeout, an unreadable answer: never 0. */
export async function readRoundCounts(executable: string, snapshotPaths: readonly string[] = [], run: Run = serverRun(executable)): Promise<RoundCountsRead> {
  return executable.length === 0 ? NO_SERVER : mergedOrPlain(snapshotPaths, run);
}

/** The exit of a binary older than a mode (64), and of a call that did not answer in time (-1, D7). */
const OLDER_BINARY = 64;
const TIMED_OUT = -1;

/**
 * `--round-counts-merged` first, with each other store's snapshot path — also with none, because launches per round needs
 * this side's keys. Exit 64 (a binary older than the mode) or a timeout falls back to `--round-counts`, today's single-store
 * answer; any other exit is the answer's own fault and is NOT hidden by a fallback.
 */
async function mergedOrPlain(snapshotPaths: readonly string[], run: Run): Promise<RoundCountsRead> {
  const merged = await run(['--round-counts-merged', ...snapshotPaths], CAP_MS);
  if (merged.code !== OLDER_BINARY && merged.code !== TIMED_OUT) {
    return roundCountsFrom(merged);
  }
  const plain = roundCountsFrom(await run(['--round-counts'], CAP_MS));

  return merged.code === TIMED_OUT ? withStoresNotAnswering(plain, snapshotPaths) : plain;
}

/** Every snapshot store of a merged call that timed out is *did not answer in time* — never a number from a call that did not answer. */
function withStoresNotAnswering(read: RoundCountsRead, snapshotPaths: readonly string[]): RoundCountsRead {
  if (read.state !== 'counted' || snapshotPaths.length === 0) {
    return read;
  }
  const storesNotIncluded = snapshotPaths.map((path) => ({ path, reason: 'timeout', detail: 'did not answer in time' }));

  return { state: 'counted', answer: { ...read.answer, storesNotIncluded } };
}

/** The other installations' data directories — a list, or a function asked at every load (the setting can change under a live window). */
export type Directories = readonly string[] | (() => readonly string[]);

/**
 * The cache's loader: one count spawn ({@link readRoundCounts}) and one tail read of `usage.jsonl` back to the oldest window,
 * the two in parallel; then, when other stores are known, each one's ledger tail beside them. `executable` empty is a side
 * with no server — the counts are unavailable and the ledger is still read.
 *
 * @param besidePaths ledgers of the same `UsageEntry` shape read beside `usage.jsonl` — the extension's probe ledger
 *   (`probeUsageFile.ts`). A failure to read one is said like the main ledger's. THIS side's only.
 * @param otherStoreDirs the other installations' data directories: their `round-counts-snapshot.json` goes to the merged
 *   call and their `usage.jsonl` is read from its end (E6.S2). A store is read once whatever the spellings that reach it, every
 *   read is bounded by `foreign.deadlineMs` and a foreign failure lands in `otherLedgers`, never in `ledgerFailure`.
 * @param foreign the tail read and its deadline — a seam; the default is the real read and the 8 s cap.
 */
export function roundsLoader(
  executable: () => string, usagePath: string, run?: Run, besidePaths: readonly string[] = [], otherStoreDirs: Directories = [],
  foreign: ForeignReads = { deadlineMs: CAP_MS, read: (path, since) => readTailSince(path, since, parseUsageLine) },
): RoundsLoad {
  const others = new ForeignLedgers(foreign);

  return async (nowMs) => {
    const binary = executable();
    const since = nowMs - WINDOW_MS['7d'];
    const directories = directoriesOf(otherStoreDirs);
    const [read, tails] = await Promise.all([
      readRoundCounts(binary, directories.map((one) => join(one, SNAPSHOT_FILE)), run ?? serverRun(binary)),
      Promise.all([usagePath, ...besidePaths].map((path) => readTailSince(path, since, parseUsageLine))),
    ]);
    const stores = storesOf(read);
    const beside = await others.load(foreignTargets(directories, stores), since);

    return {
      read,
      ledger: [...taggedWith(stores.find((one) => one.live)?.id, tails.flatMap((tail) => tail.records)), ...beside.lines],
      ledgerFailure: tails.map((tail) => tail.failure).filter((failure) => failure.length > 0).join('; '),
      otherLedgers: beside.ledgers,
    };
  };
}

function directoriesOf(directories: Directories): readonly string[] {
  return typeof directories === 'function' ? directories() : directories;
}

/** The stores the merged answer named — none from a single-store answer or a read that failed. */
function storesOf(read: RoundCountsRead): readonly RoundStore[] {
  return read.state === 'counted' ? (read.answer.stores ?? []) : [];
}

/** This side's own lines carry this side's store id once the merged answer has named it — and none before, so a single-store read is as it was. */
function taggedWith(store: string | undefined, records: readonly UsageEntry[]): readonly StoredLine[] {
  return store === undefined ? records : records.map((record) => ({ ...record, store }));
}

/**
 * The directories, among the other installations this window was told about, that hold a store of their own — asked
 * by EXISTENCE of the file and nothing more, so the block can name them without reading a byte of them.
 */
export function otherStores(directories: readonly string[], exists: (path: string) => boolean): readonly string[] {
  return directories.filter((directory) => exists(join(directory, DATABASE_FILE)));
}
