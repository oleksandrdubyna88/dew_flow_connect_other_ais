import { BugCorpus, DbFinding, DbLog, EMPTY_CORPUS, EMPTY_LOG, ManyFound, parseBugs, parseFindings, parseLog, parseOrders, RoundOrders, parseManyFindings } from './roundsDb';
import { Decision, ReviewPair } from './reviewPair';
import { FileAtRead, FileAtRevision, TOO_OLD_FOR_THE_REVISION } from './openAtRevision';
import { MethodSide, RealMethod, RealRead, TOO_OLD_FOR_THE_REAL_METHOD } from './realMethodView';
import { TOO_OLD_FOR_COMMENTS } from './commentContract';
import { inBatches, READS_AT_ONCE } from './roundsExport';
import { capture } from './versionProbe';
import { serverEnv } from './dataDir';

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
export async function readLog(executable: string, page: Page = {}, run: Run = serverRun(executable)): Promise<DbLog> {
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
  /** What the round ordered (issue #131) — absent when it recorded none, or a server sent none. */
  readonly orders?: RoundOrders;
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
  run: Run = serverRun(executable),
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
  if (findings === undefined) {
    return { state: 'failed', findings: [] };
  }
  const orders = parseOrders(output);

  return orders === undefined ? { state: 'loaded', findings } : { state: 'loaded', findings, orders };
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
 * five hundred processes, four at a time; it starts one now.</p>
 *
 * <p><b>And a cancel still reaches it.</b> `stop` is polled while the child runs and KILLS it, so
 * giving up on an export ends the read rather than merely stopping the wait for it — the code round
 * refused the trade this paragraph used to describe, and was right to. {@link manyCapMs} is the
 * separate bound on a child that has stopped answering altogether.</p>
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
  run: Run = serverRun(executable),
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
    keys.map((key) => ({ sessionId: key.sessionId, stage: key.stage, number: key.number })));
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

/**
 * The real spawn, and the ONE way the server binary is started in order to be read from.
 *
 * <p>Injectable everywhere above it, so every branch of both readers is a unit test — and the
 * DEFAULT of every reader, so a caller cannot forget the part that matters.</p>
 *
 * <p><b>`serverEnv()` is that part (issue #115).</b> The binary resolves its own data directory from
 * its own environment, and a directory a person chose lives in a SETTING, because a VS Code window
 * has no `COAI_DATA_DIR` to inherit. A child left to inherit this window's environment therefore
 * asks the DEFAULT directory about a history that is on a NAS, and is told — truthfully and
 * uselessly — that there is none. It is not a parameter: a parameter is a thing three call sites
 * remember and the fourth does not, and what that fourth one produces is an empty list rather than
 * an error.</p>
 */
export function serverRun(executable: string, stop?: () => boolean): Run {
  return (args, capMs) => capture(executable, [...args], false, capMs, stop, serverEnv());
}

/**
 * The spawn a SEND goes through, and the only one that carries a credential.
 *
 * <p><b>Named rather than a parameter on {@link serverRun}.</b> A generic "extra environment" would
 * be a door any caller could put anything through, and the thing being carried here is a contributor
 * key: the plan round's objection was that the specific context is lost in a generic signature, and
 * it was right. This function exists so that the answer to "what can put a credential into a child
 * process" is one grep with one result.</p>
 *
 * <p><b>The key is never an argument.</b> An argument is in process listings, in `/proc`, and in a
 * shell history; `coai-mcp` refuses `--key` outright for that reason and reads `COAI_BUGS_KEY`
 * instead. It is never written to a file either — `--key-file` exists, and it would mean writing a
 * credential to disk to avoid memory it is already in, with a cleanup a crash skips.</p>
 *
 * <p>{@link serverEnv} still decides the data directory, so a send reads the same database the
 * section shows.</p>
 */
export function uploadRun(executable: string, key: string): Run {
  return (args, capMs) =>
    capture(executable, [...args], false, capMs, undefined, { ...serverEnv(), COAI_BUGS_KEY: key });
}

/**
 * The same spawn, asked about a directory this window is NOT pointed at.
 *
 * <p>The one legitimate reason to want that, and it has one caller: verifying a move. The copy has
 * to be read back from where it landed BEFORE this window is pointed there, because pointing first
 * would show an empty history for as long as the copy took and for ever if it failed.</p>
 *
 * <p>It is named differently from {@link serverRun} rather than being a parameter on it, so that the
 * default door cannot quietly become the wrong-directory one. `theMoveReadsTheRightDirectory.test.ts`
 * asserts this function's only caller in `src/` — the same guard `writeWslconfig` carries, for the
 * same reason: a second caller must be a deliberate decision.</p>
 *
 * <p>The directory is passed RESOLVED and with no side, which is what makes it unambiguous: a path
 * that already includes its side resolves to itself when no side is named, so there is no way to
 * apply one twice.</p>
 */
export function serverRunAt(executable: string, resolvedDirectory: string): Run {
  return (args, capMs) =>
    capture(executable, [...args], false, capMs, undefined, { COAI_DATA_DIR: resolvedDirectory });
}

/**
 * What the corpus has to offer, and what the last collector run made of it.
 *
 * <p>One spawn for both, because the panel owns no SQLite and the server already has the file open
 * to answer the first question. A second mode would be a second process for an answer it was
 * holding.</p>
 *
 * <p><b>An old server is not a failure.</b> `--bugs-json` arrived before the run table did, so a
 * server that predates it answers a corpus with no `lastRun`; {@link parseBugs} reads that as "no
 * run has ever started", which is exactly what it means. A server too old for the FLAG exits
 * `EX_USAGE`, and that is nothing known rather than nothing there.</p>
 */
export async function readBugs(
  executable: string,
  run: Run = serverRun(executable),
): Promise<BugCorpus> {
  const { code, output } = await run(['--bugs-json'], CAP_MS);

  return code === EX_USAGE ? EMPTY_CORPUS : parseBugs(output);
}

/**
 * What a pair read came to.
 *
 * <p><b>"Nothing was collected" and "the read failed" are different sentences</b>, and the first
 * version of this returned `[]` for both. Four reviewers said so, and they were right to: the page
 * would then say "Nothing has been collected yet" over a corpus of two hundred pairs whose server
 * had merely timed out, and the person would go and collect again for nothing. `BugCorpus` already
 * carries exactly this distinction as its `read` flag; this did not, in the same change.</p>
 */
export type PairsRead =
  | { readonly ok: true; readonly pairs: readonly ReviewPair[] }
  | { readonly ok: false; readonly why: string };

/**
 * What a decision write came to.
 *
 * <p>`tooOld` is set only when the binary answered 64 — "never heard of this mode" — which is the
 * one failure the panel answers differently: it keeps the draft and says which binary to install,
 * rather than reporting a decision that could not be saved.</p>
 */
export type KeepWrite =
  | { readonly ok: true; readonly decided: number }
  | { readonly ok: false; readonly why: string; readonly tooOld?: boolean; readonly decided?: number };


/**
 * Every field the page renders, checked before it is rendered.
 *
 * <p>`JSON.parse(...) as T` is a promise, not a check. The server's output is external data by the
 * coding-style rule, and a malformed element would otherwise reach the page as `undefined` in a
 * table cell and as an invalid id in the decision it posts back.</p>
 *
 * <p><b>The seven fields that arrived with story 2.1 are optional by construction.</b> Only
 * `findingId` and `keep` can refuse a row; every text field defaults to empty and `line` to 0 —
 * which is what a server too old to send them yields, and what the page renders honestly as "none
 * recorded" rather than as an error. No version negotiation is needed on the read side: the two
 * halves of this product ship out of step, and this reader meets one shape from a server of any
 * age. (Verified against the older shape in `roundsDbRead.test.ts`.)</p>
 */
/** A text field of a document from outside, or empty when it is not text at all. */
const textOf = (one: Record<string, unknown>, key: string): string =>
  (typeof one[key] === 'string' ? one[key] as string : '');

/**
 * A count from outside: a whole number at least 0, or 0 — the database's own "none recorded", and
 * what a server older than the field sends by sending nothing. Clamped at the boundary, per the
 * reliability rule; a `line` of `"5"`, `-3` or `2.5` is not a line.
 */
const countOf = (one: Record<string, unknown>, key: string): number => {
  const value = one[key];

  return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : 0;
};

function pairOf(raw: unknown): ReviewPair | undefined {
  if (raw === null || typeof raw !== 'object') {
    return undefined;
  }

  const one = raw as Record<string, unknown>;
  const text = (key: string): string => textOf(one, key);
  const count = (key: string): number => countOf(one, key);
  if (typeof one['findingId'] !== 'number' || typeof one['keep'] !== 'number') {
    return undefined;
  }

  return {
    findingId: one['findingId'] as number,
    symbolName: text('symbolName'),
    language: text('language'),
    skeletonBefore: text('skeletonBefore'),
    skeletonAfter: text('skeletonAfter'),
    keep: one['keep'] as number,
    severity: text('severity'),
    category: text('category'),
    title: text('title'),
    repoPath: text('repoPath'),
    headSha: text('headSha'),
    fixSha: text('fixSha'),
    file: text('file'),
    line: count('line'),
    why: text('why'),
    fix: text('fix'),
    comment: text('comment'),
    sentUtc: text('sentUtc'),
    commentLost: text('commentLost'),
  };
}

/**
 * The collected pairs, as the server has them.
 *
 * <p>A LIMIT travels, because the page must not silently show the first page of a corpus as though
 * it were all of it — the mode has always taken one and this did not pass it.</p>
 */
export async function readPairs(
  executable: string,
  limit: number = MAX_LIMIT,
  run: Run = serverRun(executable),
): Promise<PairsRead> {
  const { code, output } = await run(['--pairs-json', '--limit', String(limit)], CAP_MS);
  if (code !== 0) {
    return { ok: false, why: output.trim() || `the server exited ${code}` };
  }

  try {
    const raw = JSON.parse(output) as { items?: unknown };
    if (!Array.isArray(raw?.items)) {
      return { ok: false, why: 'the server answered something this panel does not understand' };
    }

    const pairs = raw.items.map(pairOf).filter((one): one is ReviewPair => one !== undefined);
    if (pairs.length !== raw.items.length) {
      return {
        ok: false,
        why: `${raw.items.length - pairs.length} of ${raw.items.length} pairs were malformed`,
      };
    }

    return { ok: true, pairs };
  } catch {
    return { ok: false, why: 'the server answered something that is not JSON' };
  }
}

/** One side of the real method, checked field by field; a side that is not an object is no side. */
function sideOf(raw: unknown): MethodSide | undefined {
  if (raw === null || typeof raw !== 'object') {
    return undefined;
  }
  const one = raw as Record<string, unknown>;

  return {
    reason: textOf(one, 'reason'),
    source: textOf(one, 'source'),
    className: textOf(one, 'className'),
    kind: textOf(one, 'kind'),
    startLine: countOf(one, 'startLine'),
    endLine: countOf(one, 'endLine'),
  };
}

/**
 * The real method, checked before it is rendered — `JSON.parse(...) as T` is a promise, not a check.
 *
 * <p>Both sides are REQUIRED. Every server that has the mode sends both, so a document without one
 * is malformed rather than old; defaulting a missing side to "not shown" would render an honest-
 * looking absence for a broken answer, which is the failure `pairOf`'s optional fields were
 * designed around and this shape has no reason to share.</p>
 */
function realOf(raw: unknown, asked: number): RealMethod | undefined {
  if (raw === null || typeof raw !== 'object') {
    return undefined;
  }
  const one = raw as Record<string, unknown>;
  const before = sideOf(one['before']);
  const after = sideOf(one['after']);
  // The id is compared, not merely read. A document about another finding is not an answer to this
   // request, and on this page drawing one would put a method's un-anonymised source beside a
   // different finding's decision buttons. (Code round, codex.)
  if (one['findingId'] !== asked || before === undefined || after === undefined) {
    return undefined;
  }

  return {
    findingId: one['findingId'] as number,
    language: textOf(one, 'language'),
    name: textOf(one, 'name'),
    reason: textOf(one, 'reason'),
    before,
    after,
  };
}

/**
 * One pair's method as it really was, at both of its commits — `--real-method`, on stdout.
 *
 * <p><b>64 is "the server is too old", and only that.</b> `.agents/PROJECT.md` reserves it for a mode
 * the binary has never heard of, and the mode itself answers a bad `--id` with 65 for exactly this
 * reason — so the branch below can say "update the server" without ever saying it for a request
 * fault. Every other non-zero code is the server's own sentence, or the code when it said nothing.</p>
 *
 * <p>A domain outcome — no such pair, a pruned commit, an ambiguous name — is `ok: true` with the
 * reason ON the method: the request was fine, and the page renders what the reason says.</p>
 */
export async function readRealMethod(
  executable: string,
  findingId: number,
  run: Run = serverRun(executable),
): Promise<RealRead> {
  const { code, output } = await run(['--real-method', '--id', String(findingId)], CAP_MS);
  if (code === 64) {
    return { ok: false, tooOld: true, why: TOO_OLD_FOR_THE_REAL_METHOD };
  }
  if (code !== 0) {
    return { ok: false, tooOld: false, why: output.trim() || `the server exited ${code}` };
  }

  try {
    const method = realOf(JSON.parse(output), findingId);

    return method === undefined
      ? { ok: false, tooOld: false, why: 'the server answered something this panel does not understand' }
      : { ok: true, method };
  } catch {
    return { ok: false, tooOld: false, why: 'the server answered something that is not JSON' };
  }
}

/**
 * The file at its revision, checked before it is shown — `JSON.parse(...) as T` is a promise, not a check.
 *
 * <p>The id is compared, not merely read: a document about another finding is not an answer to this
 * request, and showing it would put one file under another row's heading with a sha to prove it.
 * `sha` and `path` are what the document will be named by, so they are required to be text; `text`
 * may legitimately be empty (an empty file, or a reason).</p>
 */
/**
 * The row a revision was asked about — all three coordinates, because the answer is checked against
 * every one of them.
 */
export interface AskedRevision {
  readonly findingId: number;
  readonly headSha: string;
  readonly file: string;
}

function fileAtOf(raw: unknown, asked: AskedRevision): FileAtRevision | undefined {
  if (raw === null || typeof raw !== 'object') {
    return undefined;
  }
  const one = raw as Record<string, unknown>;
  if (one['findingId'] !== asked.findingId) {
    return undefined;
  }

  // The sha and the path are compared whenever the answer CARRIES CONTENT. A pair recollected while
  // the request was in flight answers the same id at another commit, and an answer taken on trust
  // would be cached and OPENED while the row still names the revision it asked about. (Code round,
  // codex.) A reason-only answer is exempt on purpose: `pair_not_found` means there is no pair, so
  // there are no coordinates for it to echo, and refusing it would turn a true answer into silence.
  const carries = textOf(one, 'reason').length === 0;
  if (carries && (one['sha'] !== asked.headSha || one['path'] !== asked.file)) {
    return undefined;
  }

  return {
    findingId: asked.findingId,
    sha: textOf(one, 'sha'),
    path: textOf(one, 'path'),
    reason: textOf(one, 'reason'),
    text: textOf(one, 'text'),
  };
}

/**
 * One pair's file as it was at the commit the reviewers read — `--file-at`, on stdout.
 *
 * <p>The same door as {@link readRealMethod}, for the same reasons: 64 is "the server is too old" and
 * only that, every other non-zero code is the server's own sentence, and a domain outcome — no such
 * pair, a pruned commit, a file not at that path then, a path that is not repository-relative — is
 * `ok: true` with the reason ON the document, because the request was fine and the page says what
 * the reason says.</p>
 */
export async function readFileAt(
  executable: string,
  asked: AskedRevision,
  run: Run = serverRun(executable),
): Promise<FileAtRead> {
  const { code, output } = await run(['--file-at', '--id', String(asked.findingId)], CAP_MS);
  if (code === EX_USAGE) {
    return { ok: false, tooOld: true, why: TOO_OLD_FOR_THE_REVISION };
  }
  if (code !== 0) {
    return { ok: false, tooOld: false, why: output.trim() || `the server exited ${code}` };
  }

  try {
    const file = fileAtOf(JSON.parse(output), asked);

    return file === undefined
      ? { ok: false, tooOld: false, why: 'the server answered something this panel does not understand' }
      : { ok: true, file };
  } catch {
    return { ok: false, tooOld: false, why: 'the server answered something that is not JSON' };
  }
}

/**
 * Writes a batch of decisions, and answers how many rows were actually decided.
 *
 * <p><b>A file in, not one spawn per decision</b>, on `--findings-many`'s precedent: a review of
 * two hundred pairs is two hundred process launches otherwise.</p>
 *
 * <p>A FAILURE is not zero decisions. "Nothing was written because the pairs are gone" and "nothing
 * was written because the server could not run" send a person to two different places, and the
 * panel said the first for both.</p>
 */
export async function writeKeep(
  executable: string,
  ids: readonly number[],
  keep: number,
  withFile: WithKeysFile,
  run: Run = serverRun(executable),
): Promise<KeepWrite> {
  const asked = JSON.stringify({ items: ids.map((findingId) => ({ findingId, keep })) });

  return withFile(asked, async (file) => {
    const { code, output } = await run(['--pairs-keep', '--in', file], CAP_MS);

    return code === 0 ? decidedIn(output) : { ok: false, why: output.trim() || `the server exited ${code}` };
  });
}

/**
 * What `{"decided": N}` says — the one answer both decision modes give.
 *
 * <p>Read once, for both: `--pairs-keep` and `--pairs-decide` print the same document, and a second
 * parser beside the first is the copy the reuse rule names.</p>
 */
function decidedIn(output: string): KeepWrite {
  try {
    const raw = JSON.parse(output) as { decided?: unknown };

    return typeof raw?.decided === 'number'
      ? { ok: true, decided: raw.decided }
      : { ok: false, why: 'the server did not say how many decisions it wrote' };
  } catch {
    return { ok: false, why: 'the server answered something that is not JSON' };
  }
}

/**
 * Writes decisions WITH their comments through `--pairs-decide`, and answers how many were decided.
 *
 * <p><b>64 is the one exit told apart</b>: "this binary has never heard of the mode". It is the only
 * sign of a `coai-mcp` older than comments, and it is `tooOld` so the panel can keep the draft and say
 * which binary to install. Every other failure — 65 for a refused comment, 74 for a database — is a
 * reason to show, never a fallback, because the binary understood perfectly and said no.</p>
 */
export async function writeDecide(
  executable: string,
  decisions: readonly Decision[],
  withFile: WithKeysFile,
  run: Run = serverRun(executable),
): Promise<KeepWrite> {
  return withFile(JSON.stringify({ items: decisions }), async (file) => {
    const { code, output } = await run(['--pairs-decide', '--in', file], CAP_MS);
    if (code === 64) {
      return { ok: false, tooOld: true, why: TOO_OLD_FOR_COMMENTS };
    }

    return code === 0 ? decidedIn(output) : { ok: false, why: output.trim() || `the server exited ${code}` };
  });
}

/**
 * Decisions, through the newest mode the binary has — and never a comment dropped on the way.
 *
 * <p>A `coai-mcp` older than comments answers `--pairs-decide` with 64. When no decision in the batch
 * carries words, `--pairs-keep` says exactly the same thing and the old binary has it, so the batch
 * goes there. When one DOES, it does not: an old binary would take the keep and lose the words in
 * silence — the failure this whole story refuses — so the answer stays `tooOld` and the draft stays
 * in its box.</p>
 */
export async function writeDecisions(
  executable: string,
  decisions: readonly Decision[],
  withFile: WithKeysFile,
  run: Run = serverRun(executable),
): Promise<KeepWrite> {
  const written = await writeDecide(executable, decisions, withFile, run);
  const wordless = decisions.every((one) => one.comment.length === 0);

  return !written.ok && written.tooOld === true && wordless
    ? keepEach(executable, decisions, withFile, run)
    : written;
}

/** `--pairs-keep` for every keep value in the batch, in turn, with the counts added up. */
async function keepEach(
  executable: string,
  decisions: readonly Decision[],
  withFile: WithKeysFile,
  run: Run,
): Promise<KeepWrite> {
  let decided = 0;
  for (const keep of new Set(decisions.map((one) => one.keep))) {
    const ids = decisions.filter((one) => one.keep === keep).map((one) => one.findingId);
    const written = await writeKeep(executable, ids, keep, withFile, run);
    if (!written.ok) {
      // With what the earlier calls already wrote: they are in the store, and "could not be saved"
      // alone would tell a person none of it was. (CodeRabbit, the pull request.)
      return { ...written, decided };
    }
    decided += written.decided;
  }

  return { ok: true, decided };
}
