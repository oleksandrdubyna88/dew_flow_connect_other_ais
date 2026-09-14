import { DbFinding, DbLog, EMPTY_LOG, ManyFound, parseFindings, parseLog, parseManyFindings } from './roundsDb';
import { inBatches, READS_AT_ONCE } from './roundsExport';
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

/** What one page holds, matching the server's own default. */
export const DEFAULT_LIMIT = 200;

/**
 * The most the server will hand over at once, and what the page asks for.
 *
 * <p>Two hundred is the size of a PAGE, which is a question about what a person reads. This is a
 * different question: the list also carries each round's decision counts, and a row whose round is
 * outside the window loses its ✓/✗ badge. Once the findings left the list a round costs about 220
 * bytes, so a thousand of them is ~220 KB against the <b>3.83 MB</b> this change was written to end
 * — cheap enough to cover far more than anybody pages through, and bounded rather than unbounded.</p>
 */
export const MAX_LIMIT = 1000;

/** What a spawn of the server answers: its exit code and everything it wrote. */
export type Run = (args: readonly string[], capMs: number) => Promise<{ code: number; output: string }>;

/** `unknown argument` — what a server too old for a flag exits with. */
const EX_USAGE = 64;

/** `no such round` — the database has never heard of it, which is not "it found nothing". */
const EX_UNAVAILABLE = 69;

/** Which page to ask for. A cursor is opaque: the server writes it and the page hands it back. */
export interface Page {
  readonly limit?: number;
  readonly before?: string;
}

/**
 * What the server says, or an empty log.
 *
 * <p>Anything at all going wrong is an empty log: a database that is not there, a binary that has
 * been replaced mid-read. The page shows what it can either way.</p>
 *
 * <p><b>`--paged` is the compatibility hinge, and it is one flag.</b> A server that knows it answers
 * a page of rounds with no findings inside them and the totals counted in SQL — measured before this
 * existed, the findings were <b>3.78 MB of a 3.83 MB payload</b>, for rounds nobody had opened. A
 * server that does not know it exits 64, and this asks again without it, which is exactly the answer
 * it gave yesterday. The two halves of this product update separately; the log that comes back says
 * which shape it came from, so the page offers paging only where paging exists.</p>
 */
export async function readLog(executable: string, page: Page = {}, run: Run = spawn(executable)): Promise<DbLog> {
  if (executable.length === 0) {
    return EMPTY_LOG;
  }
  const limit = ['--limit', String(page.limit ?? DEFAULT_LIMIT)];
  const cursor = page.before ?? '';
  const before = cursor.length > 0 ? ['--before', cursor] : [];
  const paged = await run(['--log', '--paged', ...limit, ...before], CAP_MS);
  if (paged.code === 0) {
    return parseLog(paged.output, true);
  }
  if (paged.code !== EX_USAGE) {
    return EMPTY_LOG;
  }

  // Too old to page. It still knows `--log`, and its answer carries the findings inline — which is
  // what the page rendered until today, so nothing is lost but the paging it never had.
  const whole = await run(['--log', ...limit], CAP_MS);

  return whole.code === 0 ? parseLog(whole.output, false) : EMPTY_LOG;
}

/** Which round's findings to fetch. The three fields the database keys a round by. */
export interface RoundKey {
  readonly sessionId: string;
  readonly stage: string;
  readonly number: number;
}

/**
 * Why a round has no findings to show, when it has none.
 *
 * <p>`readLog` turns every failure into an empty log, and that is right for a log: an empty page is
 * legible and says nothing untrue. An empty findings LIST is different — it is a claim that a round
 * was clean, and a timed-out read making that claim is the defect this type exists to prevent. All
 * three reviewers of the plan round raised it independently.</p>
 */
export type FoundState = 'loaded' | 'absent' | 'failed';

export interface Found {
  readonly state: FoundState;
  readonly findings: readonly DbFinding[];
}

/**
 * The findings of exactly one round, asked for when somebody opens its row.
 *
 * <p>`absent` is the server saying it has never heard of the round, so its findings were recorded
 * nowhere — an older server wrote no database at all. `failed` is everything else: a spawn that
 * could not start, a read past its deadline, a binary replaced under us. Neither of them means the
 * round found nothing.</p>
 */
export async function readFindings(
  executable: string,
  key: RoundKey,
  run: Run = spawn(executable),
): Promise<Found> {
  if (executable.length === 0) {
    return { state: 'failed', findings: [] };
  }
  const { code, output } = await run(
    ['--findings', '--session', key.sessionId, '--stage', key.stage, '--number', String(key.number)],
    CAP_MS);

  if (code === EX_UNAVAILABLE) {
    return { state: 'absent', findings: [] };
  }
  if (code !== 0) {
    // Including 74, which is the database itself being unreadable — a third thing again, and one
    // that says nothing about the round.
    return { state: 'failed', findings: [] };
  }
  const findings = parseFindings(output);

  // Exit 0 with an answer nobody can read is a FAILED read. Turning it into an empty list would tell
  // somebody a round was clean because a pipe was truncated. (Code round, codex.)
  return findings === undefined ? { state: 'failed', findings: [] } : { state: 'loaded', findings };
}

/**
 * How long a BATCH read may take, for a batch of this size.
 *
 * <p>A flat eight seconds is the deadline for one round, and a thousand rounds through one process
 * is not one round's work. It is still a ceiling rather than a wait — it only matters when the child
 * has stopped answering — so it is generous, and proportionate rather than flat so that exporting
 * three rounds cannot hang for a minute.</p>
 */
export function manyCapMs(rounds: number): number {
  return Math.min(MANY_BASE_MS + rounds * MANY_PER_ROUND_MS, MANY_CEILING_MS);
}

const MANY_BASE_MS = 8_000;
const MANY_PER_ROUND_MS = 50;
const MANY_CEILING_MS = 120_000;

/**
 * Hand the server a file of keys, run something with its path, and take the file away again.
 *
 * <p>The keys travel in a FILE because a thousand of them do not fit on a Windows command line —
 * 32,767 characters, and a key is a 36-character session id plus a stage and a number. The same
 * reason `--ask-local` takes its prompt from one.</p>
 *
 * <p>It is a port so that every branch below is a unit test, and so the file is removed on the way
 * out of a failure as well as a success.</p>
 */
export type WithKeysFile = <T>(json: string, use: (path: string) => Promise<T>) => Promise<T>;

/**
 * The findings of MANY rounds, in ONE spawn.
 *
 * <p><b>This is the whole point of the story.</b> A bulk export of five hundred rounds used to start
 * five hundred processes, four at a time; it starts one now. What it costs is granularity: a cancel
 * arriving mid-read cannot stop a child that is already reading all of them, so it takes effect when
 * the spawn returns — which {@link manyCapMs} bounds.</p>
 *
 * <p><b>Exit 64 falls back; every other code does not.</b> 64 is `unknown argument`, which is the one
 * answer that means "this server predates the mode" — the extension and the server ship separately,
 * so the older half is a normal Tuesday, not an error. Any other non-zero code came from a server
 * that DOES know the mode and is reporting a real failure, and re-reading the whole selection one
 * round at a time would then be five hundred spawns failing the same way.</p>
 */
export async function readManyFindings(
  executable: string,
  keys: readonly RoundKey[],
  withKeysFile: WithKeysFile,
  run: Run = spawn(executable),
  readOne: typeof readFindings = readFindings,
  stop?: () => boolean,
): Promise<readonly FoundRound[]> {
  if (keys.length === 0) {
    return [];
  }
  if (executable.length === 0) {
    return keys.map((key) => ({ key, found: FAILED }));
  }
  const asked = JSON.stringify(
    keys.map((key) => ({ session: key.sessionId, stage: key.stage, number: key.number })));
  const { code, output } = await withKeysFile(asked, (path) =>
    run(['--findings-many', '--keys-file', path], manyCapMs(keys.length)));

  if (code === EX_USAGE) {
    // The server is older than this mode, so one spawn per round is what it has always answered —
    // and FOUR AT A TIME, which is what it has always answered too. Reading them strictly one after
    // another would have made the old-server path four times slower than the release before it,
    // which is the opposite of "an older server gets last release's behaviour". (Code round, codex.)
    const batched = await inBatches<RoundKey, FoundRound>(
      keys,
      async (key) => ({ key, found: await readOne(executable, key, run) }),
      (key, reason) => {
        console.error('ConnectOtherAIs: a round could not be read on the fallback path', reason);

        return { key, found: FAILED };
      },
      READS_AT_ONCE,
      stop);

    // A cancelled fallback answers about the rounds it never read, rather than leaving them out:
    // every row must get an entry, and one nobody read is failed.
    return batched.done
      ? batched.results
      : keys.map((key, at) => batched.results[at] ?? { key, found: FAILED });
  }
  if (code !== 0) {
    return keys.map((key) => ({ key, found: FAILED }));
  }
  const answered = parseManyFindings(output);

  return answered === undefined
    ? keys.map((key) => ({ key, found: FAILED }))
    : keys.map((key) => ({ key, found: found(answered, key) }));
}

/** Unreadable, and saying nothing about the round. One value, because it carries no state. */
const FAILED: Found = { state: 'failed', findings: [] };

/**
 * One round's answer, still attached to the round it is about.
 *
 * <p>The key travels all the way to the export rather than being dropped here, and that is the whole
 * of this type's reason to exist. Three reviewers of the code round made the same objection: the
 * server echoes each key back precisely so nobody has to pair an answer to a question by position,
 * and handing the caller a bare array put the pairing back — correct today because this function
 * builds it in order, and one refactor away from writing round A's findings onto round B's row.</p>
 */
export interface FoundRound {
  readonly key: RoundKey;
  readonly found: Found;
}

/**
 * What the server said about ONE of the rounds asked about, found BY ITS KEY.
 *
 * <p>A round the answer does not mention is failed, not absent: the server never said it had no
 * record — it never said anything.</p>
 */
function found(answered: readonly ManyFound[], key: RoundKey): Found {
  const mine = answered.find((one) =>
    one.sessionId === key.sessionId && one.stage === key.stage && one.number === key.number);
  if (mine === undefined) {
    return FAILED;
  }

  return mine.known ? { state: 'loaded', findings: mine.findings } : { state: 'absent', findings: [] };
}

/**
 * The real keys file, written in a folder the CALLER names.
 *
 * <p><b>One fixed name per process, deliberately.</b> It began as `mkdtemp`, which is a NEW directory
 * every export — and the plan round was right that a host killed mid-export leaks it. In this
 * product that is not a tidiness point: `%TEMP%` on a working machine here holds a hundred thousand
 * entries, the server enumerates it, and the class of defect is already documented. One name per
 * process id means a leak is ONE file that the next export of that process overwrites, rather than
 * a pile that grows with every cancelled export. Nothing concurrent can collide on it: exports are
 * serialised by `exportQueue`, and a second VS Code window is a second process id.</p>
 *
 * <p>The panel hands it the extension's own storage directory rather than the machine's temp root,
 * so the file lands somewhere VS Code owns and cleans up with the extension.</p>
 */
export function keysFileIn(folder: string): WithKeysFile {
  return async (json, use) => {
    const fs = await import('node:fs/promises');
    const path = await import('node:path');
    const file = path.join(folder, `export-keys-${process.pid}.json`);
    try {
      await fs.mkdir(folder, { recursive: true });
      await fs.writeFile(file, json, 'utf8');

      return await use(file);
    } finally {
      await fs.rm(file, { force: true }).catch(() => undefined);
    }
  };
}

/** The real spawn. Injectable above it, so every branch of both readers is a unit test. */
function spawn(executable: string): Run {
  return (args, capMs) => capture(executable, [...args], false, capMs);
}
