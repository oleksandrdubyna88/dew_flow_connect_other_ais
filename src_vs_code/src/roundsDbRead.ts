import { DbFinding, DbLog, EMPTY_LOG, parseFindings, parseLog } from './roundsDb';
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

/** The real spawn. Injectable above it, so every branch of both readers is a unit test. */
function spawn(executable: string): Run {
  return (args, capMs) => capture(executable, [...args], false, capMs);
}
