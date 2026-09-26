/* eslint-disable max-lines -- 2035 lines, over the 800 this package sets for NEW code.
   The limit is a boundary, not a rewrite mandate: splitting this file is a change with its own
   review. `reportUnusedDisableDirectives` turns this line into an error the day that happens. */
import { PAGE_SIZE, asInstant, compareRows } from './pageTables';
import { repoNameOf } from './pathTail';
import { Escalation } from './escalations';
import { ChatLedgers, roundKey, usageRegion } from './panelView';
import { TeamServerState } from './teamServerView';
import { ModelPrice } from './modelPrices';
import { ChatTurnRecord } from './chatUsage';
import { DAYS_OF, PriceLookup, consultationCost, priceOfLine, shortNumber, startOfPeriodMs, UsageEntry, Window, WINDOWS } from './usage';
import { outcomeBySaid, outcomeSaid } from './consultations';
import { foldedCell } from './consultationFold';
import { coveredSaid, kindOf } from './consultKind';
import { DEFAULT_PERIOD, LogPeriod, periodButtonsHtml } from './logPeriod';

// Moved to a leaf so the sidebar's cadence line can use it without an import cycle; still exported here.
export { repoNameOf };
import { Vendor } from './vendors';
import { calledBy, decideSecondsOf, MAX_PLAUSIBLE_SECONDS, reviewerLines, reviewerRows, RoundRecord, SessionFile, stageName } from './rounds';
import { vendorPalette, VendorPalette } from './vendorColour';
import {
  BlindSpot, countsByRound, DbConsultation, DbFinding, DbLog, DbTotals, decisionsByRound, EMPTY_LOG, EMPTY_TOTALS,
  findingsByRound, resolvedByRound, roundKeyOf,
} from './roundsDb';
import { escapeHtml, jsonForScript } from './webviewHtml';

/**
 * The rounds log: every round of every session, as rows a table can sort, filter and search.
 *
 * <p>Asked for on 2026-09-05 over a screenshot of `rounds.md`: fifty-three lines of markdown
 * tables, one per session, each row one unwrapped line running off the right edge, no way to sort
 * by time across sessions, no way to find "every round on branch X". A log is a table. The same
 * ruling left the sidebar with only what is running (`activeRounds.test.ts`); this is where the
 * rest went.</p>
 *
 * <p><b>The predicates are the page's.</b> {@link compareRows} and {@link rowMatches} reference
 * nothing outside their own parameters, and their source is embedded into the webview script
 * verbatim — so the function the tests exercise is the function the table sorts with. The
 * alternative was a TypeScript version here and a hand-copied JavaScript one in the page, which is
 * two implementations of one rule and the usual way one of them ends up wrong.</p>
 *
 * <p>`vscode`-free, like every other page module.</p>
 */

/** One round, flattened with its session, every column derived once. */
export interface LogRow {
  readonly key: string;
  /**
   * What KIND of work this row is: a review round, or one turn of a conversation.
   *
   * <p>The two are billed to the same accounts and paid for out of the same money, and until now only
   * one of them was written down anywhere. They are merged into one table rather than given a tab
   * each because the question that made this column necessary — "what did today cost me" — is not a
   * question about review rounds; the facet is there for the times it is.</p>
   *
   * <p>A conversation row leaves the repository, branch and stage columns empty, because a chat is
   * not held against a branch. That is a fact about chats, not a gap in the row: the filters offer
   * only values that exist, so an empty one adds no option to any select.</p>
   */
  readonly kind: 'review' | 'conversation';
  /** ISO, or empty when the server that wrote the round did not record it. Never the epoch. */
  readonly startedUtc: string;
  readonly completedUtc: string;
  readonly repoPath: string;
  /** The folder's own name — for a column; the path is what the filter matches on. */
  readonly repoName: string;
  readonly branch: string;
  /** As the sidebar words it: "plan review", "code review". */
  readonly stage: string;
  readonly number: number;
  readonly subject: string;
  /**
   * `awaiting` is a finished round whose findings nobody has decided about yet — `done` is about the
   * REVIEWERS having answered, and whether the gate was closed is a different fact.
   */
  readonly status: 'running' | 'done' | 'interrupted' | 'awaiting';

  /**
   * How the gate closed: accepted and rejected counts, or `null` for a round the database has never
   * heard of. `-1` in either field is the server saying nobody has decided yet.
   */
  readonly decided: { readonly accepted: number; readonly rejected: number } | null;
  readonly verdict: string;
  readonly gating: number;
  /** The sum over its reviewers, or null when the file recorded none — absent is not zero. */
  readonly findings: number | null;
  /** Completed minus started; so far, for a running round; null for one that died. */
  readonly seconds: number | null;
  /**
   * How long the DECIDING took: the round finishing to its last recorded decision.
   *
   * <p>Null, never zero, whenever nobody knows — a round nobody has decided, one from a server too
   * old to send the stamp, or one whose two instants cannot be subtracted. Zero would read as
   * "decided instantly", which is a measurement; this is the absence of one.</p>
   *
   * <p><b>What it MEASURES, said plainly because the number is easy to over-read:</b> wall clock
   * from the round finishing to the last decision recorded against it — not attention. One `resolve`
   * call stamps every finding it touches with one instant, so for the ordinary round this is the
   * deciding; a round returned to after lunch counts the lunch. The column's tooltip says so.</p>
   */
  readonly decideSeconds: number | null;
  readonly tokensIn: number | null;
  readonly tokensOut: number | null;
  readonly costUsd: number | null;
  /** What the round's reviewers READ, in dollars — null when nothing could be priced. */
  readonly costInUsd: number | null;
  /** What they WROTE. */
  readonly costOutUsd: number | null;
  /** The two added up, or what a vendor actually billed when it says so. */
  readonly costTotalUsd: number | null;
  /** True when the number is derived from a public price list rather than billed. */
  readonly costIsEstimate: boolean;
  /** True when at least one reviewer's model had no listed price, so the total is a floor. */
  readonly costPartial: boolean;
  /** How many answered, in the server's own words ("7 of 9 reviewers answered"). */
  readonly answered: string;
  readonly vendors: readonly string[];
  /** One line per reviewer — the same lines the sidebar shows, from the same builder. */
  readonly reviewers: readonly string[];
  /**
   * Which AI drove this round, and which model it declared — `claude-code 7.3.1 · claude-opus-5`.
   *
   * <p>Empty for a session file written before the server recorded it, and for a chat turn, which
   * names its own model in `answered` already. Never a half-empty phrase: a caller that declared no
   * model reads as "model not stated", because a gap there reads as a model somebody knew and did
   * not bother to print.</p>
   */
  readonly calledBy: string;
  /** Parallel to `reviewers`: the vendor word's colour, so the page needs no palette of its own. */
  readonly reviewerColours: readonly string[];
  /**
   * What this round FOUND, from the rounds database — empty when the server predates it.
   *
   * <p>The session files record that a reviewer produced four findings and not what they were, so
   * the Findings column was a number with nothing behind it. These are the sentences. Named apart
   * from `findings`, which is that count and stays what it is.</p>
   */
  readonly found: readonly DbFinding[];
  /**
   * How many it found, which the row knows even before anybody asks WHAT it found.
   *
   * <p>The status column depends on it: a gate still open and a round that raised nothing look the
   * same from the outside, and the only thing that tells them apart is whether a finding exists.</p>
   */
  readonly foundCount: number;
  /**
   * Why the findings are not on screen, when they are not.
   *
   * <p>Five states, because four of them used to be one blank and a blank reads as "clean":
   * `unasked` — nobody has opened this row; `asking` — the read is in flight; `absent` — no database
   * row exists for this round, so its findings were recorded nowhere; `failed` — the read errored or
   * timed out, and there is a retry; `loaded` — `found` is what it found, however few.</p>
   */
  readonly foundState: 'unasked' | 'asking' | 'loaded' | 'absent' | 'failed';
  /**
   * Which half of the merge this row came from.
   *
   * <p>The page is built from the SESSION files and the database only enriches them, so a row can
   * exist that the database has never heard of. That is the difference between a round that found
   * nothing and one whose findings were never written down, and it is knowable here for free.</p>
   */
  readonly origin: 'db' | 'session';
  /**
   * The three fields the rounds database keys this round by.
   *
   * <p>Carried because the page holds only a row KEY, and the read an opened row makes needs the
   * session, the RAW stage and the number. `stage` above is the display name — "code review" — while
   * the database stores "CodeReview"; matching on the wrong one answers "no such round", which the
   * page would then draw as findings that were never recorded.</p>
   */
  readonly dbKey: { readonly sessionId: string; readonly stage: string; readonly number: number };
}

/** The columns a header click can sort by. */
export type SortKey =
  | 'startedUtc' | 'kind' | 'repoName' | 'branch' | 'stage' | 'number' | 'subject' | 'status'
  | 'verdict' | 'gating' | 'findings' | 'seconds' | 'tokensIn' | 'tokensOut' | 'costTotalUsd'
  | 'answered';

/** The facets a select can narrow by. Empty or absent means "any". */
export interface LogFilters {
  readonly kind?: string;
  readonly repoPath?: string;
  readonly branch?: string;
  readonly stage?: string;
  readonly status?: string;
  readonly verdict?: string;
  readonly vendor?: string;
  /**
   * Inclusive bounds on WHEN the round started (or finished, when it never recorded a start).
   *
   * <p>Either an instant (`2026-09-05T14:30:00.000Z`, what the page sends once a time is picked) or a
   * bare day (`2026-09-05`). A bare `to` means the END of that day, not its midnight — otherwise
   * "to today" would exclude everything that happened today, which is the whole of today.</p>
   */
  readonly from?: string;
  readonly to?: string;
}

/**
 * What one reviewer's run costs per million tokens, or nothing when nothing prices it.
 *
 * <p>Both halves of the ledger line are passed, and each answers a different question. The MODEL is
 * what a public price list knows, and it is the model the line RECORDED — a round priced from what
 * codex happens to be set to today would change its cost the moment somebody switched models. The
 * VENDOR is what the operator's own typed rate is attached to: a flat subscription, a negotiated
 * rate, a local engine no list has ever heard of. The specific statement wins over the general one,
 * which is the rule the spending tab already used and this now shares.</p>
 */
export type PriceOfModel = (model: string, provider: string) => { inPerMillion: number; outPerMillion: number } | undefined;

/** Every round of every session, newest first. */
export function rowsFrom(
  sessions: readonly SessionFile[],
  nowMs: number = Date.now(),
  priceOf: PriceOfModel = () => undefined,
  usage: readonly UsageEntry[] = [],
  log: DbLog = EMPTY_LOG,
  vendorIds: readonly string[] = [],
): LogRow[] {
  const byRound = findingsByRound(log);
  const counts = countsByRound(log);
  const decided = decisionsByRound(log);
  const resolved = resolvedByRound(log);
  // Whether the window covers the whole table. When it does, a row the list does not name is a row
  // the database has never heard of, and saying so costs nothing. When it does NOT, the same row
  // might simply be older than the window — and guessing "never recorded" would be a claim about a
  // round nobody checked, so it is asked for instead and the server answers authoritatively.
  // `read` first: an unread log is not an authority on anything, and treating it as one made every
  // row on the page's first paint say the database had no record of it. (Code round, CodeRabbit.)
  const whole = log.read && log.rounds.length >= log.totals.rounds;
  // A server too old to page sent every finding it had, so an empty list from IT is the whole truth
  // about that round — asking again would send `--findings` to a binary that does not know the flag,
  // exit 64, and turn an honestly clean round into a failed read. (Code round, codex.)
  const inline = !log.paged;
  // The CONFIGURED vendors, the same list the panel's cards are coloured from — not the providers
  // these rounds happen to name. A log holding a vendor somebody has since removed still colours it,
  // from its own name, and only such a stray may share a hue with a live reviewer.
  const colour = vendorPalette(vendorIds);

  return sessions
    .flatMap((session) =>
      session.rounds.map((round) =>
        rowFrom(
          session, round, nowMs, priceOf, usage,
          { byRound, counts, decidedBy: decided, resolvedBy: resolved, colour, whole, inline })))
    .sort(newestFirst);
}

/** Newest first, by when a row began — its own function because three lists are ordered by it. */
export function newestFirst(a: LogRow, b: LogRow): number {
  // Plain comparison, not `localeCompare`: these are ISO-8601 instants of one shape, so they order
  // lexicographically by construction, and full internationalisation collation over thousands of
  // rows on every five-second tick buys nothing for it. (gemini, the code round.)
  const mine = b.startedUtc || b.completedUtc;
  const theirs = a.startedUtc || a.completedUtc;

  return mine < theirs ? -1 : (mine > theirs ? 1 : 0);
}

/**
 * Two lists of rows as one table, newest first.
 *
 * <p>Merged in MEMORY rather than in a file, which is the whole shape of this feature: the review
 * rounds come from the server's session files and its ledger, the conversations from the extension's
 * own, and neither program has to know the other's format. The cost is one extra read; what it buys
 * is that the two halves can ship on different days without a torn line between them.</p>
 */
export function mergedRows(reviews: readonly LogRow[], conversations: readonly LogRow[]): LogRow[] {
  return [...reviews, ...conversations].sort(newestFirst);
}

/**
 * Every recorded chat turn, as rows of the same table.
 *
 * <p>Priced through the SAME {@link PriceOfModel} the rounds use, so a conversation and a review on
 * one model are worked out the same way and a person comparing them is comparing like with like. A
 * vendor that billed the turn wins over the list, exactly as it does for a round — `costIsEstimate`
 * is what says which happened.</p>
 *
 * <p><b>The columns a chat has nothing to put in are empty, and that is deliberate.</b> A chat has no
 * repository, no branch, no stage, no verdict and no findings; filling them with plausible-looking
 * text would make the table read as though a conversation were a kind of review round. The Kind
 * column is the one that says what the row is.</p>
 */
export function chatRows(
  records: readonly ChatTurnRecord[],
  priceOf: PriceOfModel = () => undefined,
): LogRow[] {
  // Which turn of its own conversation each record is, so the Round column means something for a
  // chat too. Counted over the records in time order rather than stored on them: the number is a
  // fact about the list, and a record that never reaches the page cannot leave a gap in it.
  const position = new Map<string, number>();

  return [...records]
    .sort((a, b) => (a.utc < b.utc ? -1 : (a.utc > b.utc ? 1 : 0)))
    .map((record) => {
      const turn = (position.get(record.conversation) ?? 0) + 1;
      position.set(record.conversation, turn);

      return chatRow(record, turn, priceOf);
    })
    .sort(newestFirst);
}

function chatRow(record: ChatTurnRecord, turn: number, priceOf: PriceOfModel): LogRow {
  const price = priceOf(record.model, record.provider);
  const listed = price === undefined
    ? undefined
    : {
      inUsd: round4((record.tokensIn / 1_000_000) * price.inPerMillion),
      outUsd: round4((record.tokensOut / 1_000_000) * price.outPerMillion),
    };
  // A turn nobody reported anything about is UNKNOWN, not free. The ledger stores tokens as numbers,
  // so a vendor that said nothing and a turn killed before it could say anything both arrive here as
  // zeroes — and a table that prints `0` beside `stopped` tells a person the stop cost them nothing,
  // which is the one thing it certainly does not say. A blank does, and `LogRow` already has one.
  const silent = record.tokensIn === 0 && record.tokensOut === 0 && record.costUsd === null;

  return {
    // The instant plus the conversation, which is unique because one conversation runs one turn at a
    // time — the session refuses to interleave two down one pipe, and `chatCommand.ts` chains them.
    key: `chat:${record.conversation}:${record.utc}`,
    kind: 'conversation',
    startedUtc: record.utc,
    completedUtc: endOf(record),
    repoPath: '',
    repoName: '',
    branch: '',
    stage: '',
    number: turn,
    subject: record.title,
    status: record.outcome === 'answered' ? 'done' : 'interrupted',
    decided: null,
    verdict: '',
    gating: 0,
    findings: null,
    seconds: record.seconds,
    // A conversation has nothing to decide, so there is no deciding to have taken any time. Null
    // rather than 0 for the same reason every other absent measurement on this row is null.
    decideSeconds: null,
    tokensIn: silent ? null : record.tokensIn,
    tokensOut: silent ? null : record.tokensOut,
    costUsd: record.costUsd,
    costInUsd: listed?.inUsd ?? null,
    costOutUsd: listed?.outUsd ?? null,
    // What the vendor BILLED wins over the price list, the same order of preference `costOf` uses.
    costTotalUsd: record.costUsd ?? (listed === undefined ? null : round4(listed.inUsd + listed.outUsd)),
    costIsEstimate: record.costUsd === null && listed !== undefined,
    // Never partial: a chat row is one turn on one model, so there is no second reviewer that could
    // have gone unpriced. The floor mark would be a claim about something that cannot happen here.
    costPartial: false,
    answered: `${record.provider}/${record.model}`,
    vendors: [record.provider],
    reviewers: [`${record.provider}/${record.model} · ${record.outcome}`],
    // A chat turn already names its model in `answered`; repeating it as a caller would be a
    // second, quieter claim about the same thing.
    calledBy: '',
    reviewerColours: [],
    found: [],
    foundCount: 0,
    // A conversation has no findings to read, which is a different thing from findings nobody wrote
    // down — so it is `loaded` with none, and the row opens on "This round found nothing" rather
    // than on an offer to fetch what does not exist.
    foundState: 'loaded',
    origin: 'session',
    dbKey: { sessionId: '', stage: '', number: 0 },
  };
}

/**
 * When a turn ended, from when it began and how long it took.
 *
 * <p>Derived rather than recorded: two instants that must agree is two chances to disagree, and the
 * duration is the number a person actually reads. An unreadable start yields no end at all, which
 * the date filter treats as a row it cannot place in a bounded range — the same answer it gives a
 * round that never recorded a start.</p>
 */
function endOf(record: ChatTurnRecord): string {
  const began = Date.parse(record.utc);

  return Number.isFinite(began) ? new Date(began + record.seconds * 1000).toISOString() : '';
}

/**
 * What every row of one call needs and none of them computes for itself.
 *
 * <p>Grouped rather than passed as three more parameters: the database lookups and the palette are
 * all "worked out once for the whole call", which is a different kind of argument from the session
 * and the round a row IS. `rowFrom` had reached eight parameters, which is where a reader stops
 * being able to check the call site by eye.</p>
 */
interface RowContext {
  readonly byRound: Map<string, readonly DbFinding[]>;
  readonly counts: Map<string, number>;
  readonly decidedBy: Map<string, { accepted: number; rejected: number }>;
  /** When each round was last decided, by the same key. Empty for one nobody has decided. */
  readonly resolvedBy: Map<string, string>;
  readonly colour: VendorPalette;
  /** Whether the list covers every round the database holds. See `rowsFrom`. */
  readonly whole: boolean;
  /** Whether the list already carried the findings — the shape a server before 0.18.15 answers. */
  readonly inline: boolean;
}

/**
 * A row built from the session files alone — no database, no palette of its own.
 *
 * <p>A named constant rather than a literal in the parameter list: a default object literal is a
 * fresh allocation on every call that omits the argument, and this one holds three maps and a
 * palette. It is never mutated — `rowFrom` only reads from it — so one is enough for all of them.
 * (SonarCloud, on the pull request.)</p>
 */
const NO_DATABASE: RowContext = {
  byRound: new Map(), counts: new Map(), decidedBy: new Map(), resolvedBy: new Map(),
  colour: vendorPalette([]),
  whole: true, inline: false,
};

/**
 * The "asked by" line of an opened row — the function the PAGE runs, not a description of it.
 *
 * <p>Exported and embedded by assignment (`var askedByHtml = ${askedByHtml.toString()}`) for the
 * same reason `compareRows` and `rowMatches` are: `common/generated-code-tests.md` says code the
 * test generates must be EXECUTED rather than string-matched, and a substring assertion over the
 * page's HTML cannot see whether the line was built correctly, only that its characters are in
 * there somewhere. This way the test calls what the page calls.</p>
 *
 * <p>The escaper is a PARAMETER because the page has its own (`esc`) and this module has its own
 * (`escapeHtml`), and a function embedded by `toString()` may only reference names the page
 * defines. Passing it keeps one implementation of the line and no name games.</p>
 *
 * <p>An empty `calledBy` renders nothing at all — a round from before the field is a server that
 * never asked the question, and an empty "asked by" would be a claim about it.</p>
 */
export function askedByHtml(calledBy: string, escape: (value: string) => string): string {
  return calledBy === '' ? '' : '<div class="reviewer asked">asked by ' + escape(calledBy) + '</div>';
}

function rowFrom(
  session: SessionFile,
  round: RoundRecord,
  nowMs: number,
  priceOf: PriceOfModel,
  usage: readonly UsageEntry[],
  context: RowContext = NO_DATABASE,
): LogRow {
  const { byRound, counts, decidedBy, resolvedBy, colour, whole, inline } = context;
  const cost = costOf(round, priceOf, usage, nowMs);
  const key = roundKeyOf(
    session.state.sessionId, session.state.repoPath, session.state.branch, round.stage, round.number);
  // A paged server sends no findings with the list, so `found` is empty until a row is opened; an
  // older one sends them all and the row is `loaded` from the start. `counts` is what both have.
  const found = byRound.get(key) ?? [];
  const known = counts.has(key);
  const foundCount = counts.get(key) ?? found.length;
  const decided = decidedBy.get(key) ?? null;
  const status = statusOf(round, foundCount, decided);
  const states = round.reviewerStates ?? [];
  const rows = reviewerRows(round);

  return {
    key: roundKey({ ...round, branch: session.state.branch }),
    kind: 'review',
    startedUtc: round.startedUtc ?? '',
    completedUtc: round.completedUtc,
    repoPath: session.state.repoPath,
    repoName: repoNameOf(session.state.repoPath),
    branch: session.state.branch,
    stage: stageName(round.stage),
    number: round.number,
    subject: round.subject ?? '',
    status,
    decided,
    verdict: round.verdict,
    gating: round.gatingCount,
    findings: states.length === 0 ? null : states.reduce((sum, s) => sum + s.findings, 0),
    seconds: secondsOf(round, status, nowMs),
    decideSeconds: decideSecondsOf(round.completedUtc, resolvedBy.get(key) ?? ''),
    tokensIn: round.tokensIn ?? null,
    tokensOut: round.tokensOut ?? null,
    costUsd: round.costUsd ?? null,
    ...cost,
    answered: round.reviewers,
    vendors: [...new Set(states.map((s) => s.provider))],
    reviewers: reviewerLines(round),
    calledBy: calledBy(round.caller),
    reviewerColours: rows.map((r) => colour(r.provider)),
    found,
    foundCount,
    foundState: foundState(known, whole, inline, found),
    origin: known ? 'db' : 'session',
    dbKey: { sessionId: session.state.sessionId, stage: round.stage, number: round.number },
  };
}

/**
 * What an unopened row already knows about its own findings.
 *
 * <p>A round the database has never heard of is `absent` from the start — asking for it would spawn
 * a process to be told what this already knows. One it holds is `unasked` until somebody opens it,
 * unless an older server already sent the findings, in which case they are here and it is
 * `loaded`.</p>
 */
function foundState(
  known: boolean,
  whole: boolean,
  inline: boolean,
  found: readonly DbFinding[],
): LogRow['foundState'] {
  if (!known) {
    // Only when the list covered everything is "the list did not name it" the same as "the database
    // does not hold it". Otherwise the row asks, and the server answers with the truth.
    return whole ? 'absent' : 'unasked';
  }
  if (inline || found.length > 0) {
    // Either the sentences are here, or they were never coming: a server that answers the old shape
    // sends all of them with the list, so an empty list from it means the round found nothing.
    return 'loaded';
  }

  return 'unasked';
}

/**
 * What the row's Status column says.
 *
 * <p>`done` used to be everything that was not running or dead, and that read as finished while the
 * gate was still open: a screenshot of a round with thirteen `open` findings showed Status `done`,
 * Verdict `good_enough`. Both were true. Together they said the opposite of what was happening.</p>
 *
 * <p>So a fourth state, from the fact the server already records: `accepted` stays −1 until a
 * resolve lands. A round that raised NOTHING is exempt — there is no resolve to make, so its −1
 * never moves and it would sit in "awaiting" for ever. A round the database has never heard of is
 * exempt too: an older server wrote no database, and accusing it of an unclosed gate invents a fact
 * about a round nobody can resolve any more.</p>
 */
function statusOf(
  round: RoundRecord,
  foundCount: number,
  decided: { accepted: number; rejected: number } | null,
): LogRow['status'] {
  if (round.status === 'running') {
    return 'running';
  }
  if (round.status === 'interrupted') {
    return 'interrupted';
  }

  return decided !== null && decided.accepted < 0 && foundCount > 0 ? 'awaiting' : 'done';
}

/**
 * What a round cost, from the USAGE LEDGER.
 *
 * <p>Not from the round's reviewer states: those record `{provider, role, status, findings, note,
 * seconds}` and no tokens at all, which is why the first version of this column was empty on every
 * row in the installed extension. And not from the round's own totals either — they are a sum over
 * vendors whose prices differ by an order of magnitude, so one multiplication over them is a number
 * with no meaning.</p>
 *
 * <p>The ledger has exactly what is needed: one line per reviewer run, with the tokens it used and
 * the MODEL that answered. Each line is priced at its own model's rate, so a round keeps the cost
 * it had when it ran even after somebody points the vendor at a different model.</p>
 *
 * <p>A line with no listed price, or with no tokens recorded, contributes nothing and sets
 * `costPartial`: a total that quietly leaves somebody out is worse than one that says it is a
 * floor. So does a total whose tokens do not add up to what the round itself recorded — two rounds
 * of one stage running at once can each match the other's lines by time alone, and that is the
 * honest way to say the figure may not be only this round's.</p>
 *
 * <p>A vendor that BILLED the round wins over anything worked out from a price list — the two are
 * not the same kind of number, and `costIsEstimate` says which one this is.</p>
 */
function costOf(
  round: RoundRecord,
  priceOf: PriceOfModel,
  usage: readonly UsageEntry[],
  nowMs: number,
): Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'> {
  const billed = round.costUsd ?? null;
  let inUsd = 0;
  let outUsd = 0;
  let priced = 0;
  let unpriced = 0;
  let tokensIn = 0;
  let tokensOut = 0;
  for (const line of linesOf(round, usage, nowMs)) {
    const price = priceOf(line.model, line.provider);
    if (price === undefined || line.tokensIn === undefined || line.tokensOut === undefined) {
      unpriced += 1;
      continue;
    }
    priced += 1;
    tokensIn += line.tokensIn;
    tokensOut += line.tokensOut;
    inUsd += (line.tokensIn / 1_000_000) * price.inPerMillion;
    outUsd += (line.tokensOut / 1_000_000) * price.outPerMillion;
  }
  const nothingPriced = priced === 0;

  return {
    costInUsd: nothingPriced ? null : round4(inUsd),
    costOutUsd: nothingPriced ? null : round4(outUsd),
    costTotalUsd: billed ?? (nothingPriced ? null : round4(inUsd + outUsd)),
    costIsEstimate: billed === null && !nothingPriced,
    costPartial: !nothingPriced && (unpriced > 0 || drifted(round, tokensIn + tokensOut)),
  };
}

/**
 * The ledger lines that belong to this round: written while it ran, and of its own stage.
 *
 * <p>By time and stage because that is all there is to match on — a ledger line names no round.
 * (The local database will carry the round's id on every line and end the guessing; until then this
 * is exact for one round at a time and marked partial when the tokens disagree.)</p>
 */
function linesOf(round: RoundRecord, usage: readonly UsageEntry[], nowMs: number): readonly UsageEntry[] {
  const from = round.startedUtc ?? '';
  if (from.length === 0 || usage.length === 0) {
    return [];
  }
  const to = round.completedUtc.length > 0 ? round.completedUtc : new Date(nowMs).toISOString();

  return usage.filter(
    (line) => line.utc >= from && line.utc <= to && line.stage.toLowerCase() === round.stage.toLowerCase());
}

/** Whether the priced lines add up to what the round said it used, within a fiftieth. */
function drifted(round: RoundRecord, counted: number): boolean {
  const recorded = (round.tokensIn ?? 0) + (round.tokensOut ?? 0);

  return recorded > 0 && Math.abs(counted - recorded) > recorded / 50;
}

/** Cents-and-a-bit, so a sum of many small numbers does not drift into float noise. */
function round4(value: number): number {
  return Math.round(value * 10_000) / 10_000;
}


/**
 * How long a round took, or has taken — and no number at all for one that died.
 *
 * <p>An interrupted round was never written a completion time; measuring it against `now` would
 * report how long ago it started, which is what the sidebar did for a day and called `361m 40s`.</p>
 */
function secondsOf(round: RoundRecord, status: LogRow['status'], nowMs: number): number | null {
  const started = Date.parse(round.startedUtc ?? '');
  if (Number.isNaN(started)) {
    return null;
  }
  if (status === 'running') {
    return Math.max(0, Math.round((nowMs - started) / 1000));
  }
  if (status === 'interrupted') {
    return null;
  }
  const completed = Date.parse(round.completedUtc);
  if (Number.isNaN(completed)) {
    return null;
  }
  const seconds = Math.round((completed - started) / 1000);
  // .NET's default date is year ONE, and a server that never recorded a start wrote exactly that.
  // Subtracting it from a real completion time produced "1065396701m 44s" once; the sidebar's
  // `elapsed` refuses the same way, with the same cap.
  return seconds < 0 || seconds > MAX_PLAUSIBLE_SECONDS ? null : seconds;
}

// ---------------------------------------------------------------------------------------------
// `compareRows` and `asInstant` MOVED to `pageTables.ts` when the notifications page needed them,
// and they are re-exported here so every caller and every test that named them still does. The
// constraint they carry went with them and is written out there: they run in the PAGE, embedded by
// their own source text, so they may reference nothing outside their parameters. The functions
// below are under the same rule.
// ---------------------------------------------------------------------------------------------

// Imported for this file's own use AND re-exported, because a bare `export ... from` does not
// bring the name into scope here - the page builder below interpolates all three.
export { PAGE_SIZE, asInstant, compareRows } from './pageTables';

/**
 * What a figure in a money column says, or an em dash where there is no figure.
 *
 * <p>Embedded into the page by its source text, like every function below it — so it references
 * nothing outside itself. {@link cost3} and {@link costTitle} take it as an ARGUMENT rather than
 * calling it by name: a function embedded by its text lands in a scope where only its own name was
 * re-declared, so a call to a module-level binding arrives under whatever the minifier called it
 * (0.29.12 shipped exactly that, and the page died with "R is not defined"). An argument cannot be
 * renamed out from under it, and there is one copy of the formatter rather than three.</p>
 */
export function money(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—';
  }

  return '$' + value.toFixed(value < 1 ? 3 : 2);
}

/**
 * The three figures the column is named for: what the round READ, what it WROTE, and the sum.
 *
 * <p>Always three, always with the slashes, an em dash standing in for anything unknown — a vendor
 * that bills one total and reports no split reads `— / — / $0.42` rather than a lone number the
 * header promises to be three. And a round nothing could price is a dash, never an empty cell: an
 * empty cell says "nothing to see", which is a different claim from "nobody knows".</p>
 *
 * <p>A tilde marks a total worked out from a public price list rather than one a vendor billed; a
 * plus marks a total that had to leave a reviewer out, so it is a floor.</p>
 */
export function cost3(row: Costed, figure: Money): string {
  if (typeof row.costTotalUsd !== 'number') {
    return '—';
  }

  return (row.costIsEstimate ? '~' : '')
    + figure(row.costInUsd) + ' / ' + figure(row.costOutUsd) + ' / ' + figure(row.costTotalUsd)
    + (row.costPartial ? '+' : '');
}

/** The same thing in words, for the cell's tooltip — the column is narrow and the marks are one character. */
export function costTitle(row: Costed, figure: Money): string {
  if (typeof row.costTotalUsd !== 'number') {
    return 'No price is listed for these models, so this round has no cost figure.';
  }
  const how = row.costIsEstimate
    ? 'Worked out from a public price list, not billed.'
    : 'Reported by the vendor.';
  const part = row.costPartial
    ? ' Some of it could not be priced, so the total is a floor.'
    : '';

  return 'Input ' + figure(row.costInUsd) + ' + output ' + figure(row.costOutUsd)
    + ' = ' + figure(row.costTotalUsd) + '. ' + how + part;
}

/** How a figure is written. Passed IN, never reached for — see the remark on {@link money}. */
export type Money = (value: number | null | undefined) => string;

/** Just the cost fields, because these three run in the page against plain row objects. */
export type Costed = Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'>;

/** Which view a row belongs to. The tabs are the two halves of this one predicate. */
export type LogView = 'rounds' | 'conversations';

/**
 * Whether a row belongs in the view being shown.
 *
 * <p>It is asked FIRST, before the search, the facets, the sort, the page count and the slice — and
 * that order is the whole of it. Filtering after the slice would give the Conversations tab one row
 * out of a page of two hundred rounds with *Older* enabled onto an empty page, while the counts and
 * the search still spoke for both kinds; a test over a single mixed page would pass while it was
 * broken. (The plan round.)</p>
 *
 * <p>A round is anything that is not a conversation, rather than anything called `review`. Today the
 * two are the same test — `LogRow.kind` is a closed union of exactly those two, which a test tried to
 * disprove and could not compile. The negative form is for the day a third kind is added: it lands
 * with the rounds, where every kind so far has come from, instead of falling out of both views and
 * off the page.</p>
 */
export function inView(row: LogRow, view: LogView): boolean {
  return view === 'conversations' ? row.kind === 'conversation' : row.kind !== 'conversation';
}

/** Whether a row survives the selects and the search box. A blank search is no search. *
 * <p>The haystack carries `answered` — the sentence the REVIEWERS column renders, "all 3 reviewers
 * answered" or the one model a conversation was with. Leaving it out meant typing a role's name
 * did not find the rounds whose Reviewers cell says it, which is the incident this whole plan is
 * about: a role nobody could find. It is present on both kinds of row, so nothing is conditional.</p>
 */
export function rowMatches(row: LogRow, filters: LogFilters, search: string): boolean {
  if (filters.kind && row.kind !== filters.kind) {
    return false;
  }
  if (filters.repoPath && row.repoPath !== filters.repoPath) {
    return false;
  }
  if (filters.branch && row.branch !== filters.branch) {
    return false;
  }
  if (filters.stage && row.stage !== filters.stage) {
    return false;
  }
  if (filters.status && row.status !== filters.status) {
    return false;
  }
  if (filters.verdict && row.verdict !== filters.verdict) {
    return false;
  }
  if (filters.vendor && row.vendors.indexOf(filters.vendor) < 0) {
    return false;
  }
  // The date range. ISO-8601 instants of the same shape compare correctly as plain strings, so the
  // bounds are inclusive by comparison. A bare day as the UPPER bound means the end of that day: an
  // upper bound of 2026-09-05 against 2026-09-05T14:30Z would otherwise exclude the whole of today,
  // which is exactly the range somebody picking today wants. A round with no date at all cannot be
  // shown to be inside a bounded range, so a bound drops it; no bound keeps it.
  const at = row.startedUtc || row.completedUtc;
  if ((filters.from || filters.to) && at.length === 0) {
    return false;
  }
  if (filters.from && at < filters.from) {
    return false;
  }
  if (filters.to && at > (filters.to.indexOf('T') < 0 ? filters.to + 'T23:59:59.999Z' : filters.to)) {
    return false;
  }
  const needle = search.trim().toLowerCase();
  if (needle.length === 0) {
    return true;
  }
  // NO BACKTICKS IN THIS BODY, comments included: it is pasted into the page by toString() and the
  // page script lives inside a template literal. The reason the reviewers sentence is in the
  // haystack is in the docstring above, where backticks are safe.
  const haystack = [row.subject, row.branch, row.repoPath, row.repoName, row.answered]
    .concat(row.reviewers).join(' ').toLowerCase();
  return haystack.indexOf(needle) >= 0;
}

// ---------------------------------------------------------------------------------------------
// The page.
// ---------------------------------------------------------------------------------------------

/** The open questions, as the block at the top of the page — the same block the tick re-posts. */
export function questionsHtml(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const items = questions.map((q) => {
    const findings = q.openFindings
      .map((f) => `<li><code>${escapeHtml(f.severity)}</code> ${escapeHtml(f.category)} `
        + `${f.file ? `<code>${escapeHtml(f.file)}:${f.line ?? ''}</code> ` : ''}— ${escapeHtml(f.title)}</li>`)
      .join('');
    return `<div class="question">
  <div class="where">${escapeHtml(q.branch)} · ${escapeHtml(q.repoPath)} · asked ${escapeHtml(q.askedUtc)}</div>
  <p>${escapeHtml(q.question)}</p>
  ${findings.length > 0 ? `<div class="gating">Still gating:</div><ul>${findings}</ul>` : '<div class="gating">No findings attached.</div>'}
  <button type="button" data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
</div>`;
  });

  return `<h2>Open questions — a review is waiting on you</h2>\n${items.join('\n')}`;
}

/**
 * The spending tab's body: the window buttons and the per-vendor region the sidebar used to carry.
 *
 * <p>Moved here from the sidebar on 2026-09-05 — "перенеси отдельной табой в Review rounds и убери из
 * дерева" — with Today as the default window, since midnight. The region itself is the function the
 * sidebar rendered, so there is one renderer for a vendor's row.</p>
 */
export function usageTabHtml(
  usage: readonly UsageEntry[],
  window: Window,
  vendors: readonly Vendor[],
  prices: Readonly<Record<string, ModelPrice>>,
  teamServers: readonly TeamServerState[] = [],
  usageScope: 'me' | 'company' = 'me',
  chat: ChatLedgers = { turns: [], doors: [] },
): string {
  const buttons = WINDOWS
    .map((w) => `<button type="button" class="tab${w.id === window ? ' on' : ''}" data-command="usageWindow" data-id="${w.id}">${escapeHtml(w.label)}</button>`)
    .join('');

  return `<div class="windows">${buttons}</div>` + '\n' + `<div class="usage-rows">${usageRegion(usage, window, vendors, prices, teamServers, usageScope, chat)}</div>`;
}

/**
 * The table's columns.
 *
 * <p>`COLUMN_COUNT` is this list's length and is the expanded row's `colspan`, so a column added
 * anywhere but here silently stops the detail row spanning the table. The actions column is
 * therefore IN the list and marked unsortable, rather than appended to the header by hand.</p>
 */
/**
 * The table's columns, for BOTH views.
 *
 * <p>`alt` is what the column is called in the conversations view — a round's *Round* is a
 * conversation's *Turn*, and its *Reviewers* is the one model that answered. Both labels are
 * rendered and the view hides the wrong one, so the header needs no script to change: a label that
 * moved would otherwise be a behaviour no stylesheet could be asked about.</p>
 *
 * <p><b>`Kind` is gone.</b> It existed to tell a conversation from a round inside one list, and the
 * view now says that (issue #297).</p>
 */
const COLUMNS: ReadonlyArray<{
  key: SortKey | 'actions' | 'pick'; label: string; alt?: string; numeric?: boolean; sortable?: boolean;
}> = [
  { key: 'pick', label: '', sortable: false },
  { key: 'startedUtc', label: 'When' },
  { key: 'repoName', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'number', label: 'Round', alt: 'Turn', numeric: true },
  { key: 'subject', label: 'What' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'gating', label: 'Gating', numeric: true },
  { key: 'findings', label: 'Findings', numeric: true },
  { key: 'seconds', label: 'Took', numeric: true },
  { key: 'tokensIn', label: 'Tokens in', numeric: true },
  { key: 'tokensOut', label: 'Tokens out', numeric: true },
  { key: 'costTotalUsd', label: 'Cost', numeric: true },
  { key: 'answered', label: 'Reviewers', alt: 'Who answered' },
  { key: 'actions', label: '', sortable: false },
];

type Facet = 'kind' | 'repoPath' | 'branch' | 'stage' | 'status' | 'verdict' | 'vendor';

/**
 * The facets a CONVERSATION has no answer for.
 *
 * <p>`chatRows` sets `repoPath`, `branch`, `stage` and `verdict` to the empty string on purpose — a
 * conversation has none of them, and filling them would make it read as a kind of review round. So
 * choosing any value of one in the conversations view matched nothing and emptied the tab, and a
 * value chosen in the ROUNDS view survived the switch and emptied it before it was even opened.
 * Six reviewers found it independently; it is the one thing the plan's shared-table shape got
 * wrong.</p>
 *
 * <p>Named once, and read by three places: the markup that classes each control, the stylesheet that
 * hides them, and the tab handler that clears them.</p>
 */
export const ROUND_ONLY_FACETS: readonly Facet[] = ['repoPath', 'branch', 'stage', 'verdict'];

const FACETS: ReadonlyArray<{ key: Facet; label: string }> = [
  { key: 'repoPath', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'vendor', label: 'Vendor' },
];

/** A select's options for one facet, from the rows themselves — a filter offers only what exists. */
function facetOptions(rows: readonly LogRow[], key: Facet): string {
  const values = key === 'vendor'
    ? [...new Set(rows.flatMap((r) => r.vendors))]
    : [...new Set(rows.map((r) => r[key]))];
  return values
    .filter((v) => v.length > 0)
    .sort((a, b) => a.localeCompare(b))
    .map((v) => `<option value="${escapeHtml(v)}">${escapeHtml(key === 'repoPath' ? repoNameOf(v) : v)}</option>`)
    .join('');
}

/**
 * The whole page: questions, a toolbar of filters and a search box, and the table.
 *
 * <p>The table BODY is rendered by the page from the rows as JSON — sorting, filtering and
 * searching are page state, and a live push of new rows re-renders the body alone, so the sort,
 * the filters, the search text, the scroll position and the expanded rows all survive it. The
 * questions block is rendered here and re-posted as HTML by the same function, so there is one
 * renderer for it.</p>
 */
/**
 * The two questions this data exists to answer, as a region of the page.
 *
 * <p>Operator, 2026-09-05: <i>"я хочу сначала идентифицировать [белые пятна], а потом уже или рагом
 * или математикой дать инструмент, который закроет эти пятна"</i>. Identifying them is this.</p>
 *
 * <p><b>What it accepted</b> is the blind-spot corpus: a finding the caller took is by definition
 * something it had not seen and then agreed was worth having. Shown as accepted OVER total, because
 * a category that produces fifty findings and gets two taken says something quite different from
 * one that produces two and gets both — the second is the blind spot; the first is noise.</p>
 *
 * <p><b>What it argued with and got again</b> is the shorter and sharper list: the caller rejected
 * it with a reason, the rejection still stood, and a reviewer raised it anyway.</p>
 */
/**
 * Every consultation, newest first — the history the sidebar deliberately does not show.
 *
 * <p>The sidebar is present tense; this is where a person asks whether consulting is WORKING. So the
 * columns are the ones that answer that: who was stuck, who answered, how many turns it took, what
 * it cost, and the advice in force. The problem and the advice are the two long fields and they are
 * the point of the row — a table of ids and numbers would say a consultation happened and nothing
 * about whether it helped.</p>
 */
export function consultationsHtml(
  log: DbLog,
  /**
   * The reviewer rows and the published price lists, so a consultation whose vendor printed no money
   * can still be priced — the two inputs `priceOfLine` already takes for every round and every
   * conversation in the ledger.
   */
  vendors: readonly Vendor[],
  /**
   * REQUIRED, both of them, and that is the fix rather than a style.
   *
   * <p>They were optional with empty defaults, and the result was a table that computed nothing:
   * `extension.ts` called `consultationsHtml(fresh)`, every unit test passed by calling the
   * arithmetic directly, and the product went on showing the dash this change exists to remove. A
   * decision nobody calls is the defect this repository already has a rule about. With no default
   * there is no way to build this table without saying what it costs. (issue #309, the review pass.)</p>
   */
  listed: PriceLookup,
): string {
  if (log.consultations.length === 0) {
    return '<div class="empty">No consultations yet. One happens when an AI calls <code>consult</code> — stuck, or because the cadence asked for one —'
      + ' the <b>Consultant</b> section of the panel says who it asks.</div>';
  }

  const rows = log.consultations.map((one) => `<tr data-started="${startedMsOf(one.startedUtc)}">
    <td>${escapeHtml(startedOf(one.startedUtc))}</td><td>${escapeHtml(kindOf(one.kind))}</td><td>${forCell(one)}</td>
    <td>${escapeHtml(callerOf(one.callerKind))} → ${escapeHtml(one.vendor)}${one.model.length > 0 ? ` · ${escapeHtml(one.model)}` : ''}</td>
    <td>${escapeHtml(repoNameOf(one.repoPath))}${one.branch.length > 0 ? ` · ${escapeHtml(one.branch)}` : ''}</td>
    <td class="num">${one.turns}</td>
    <td><span class="badge ${badgeOf(one.status)}">${escapeHtml(one.status)}</span>${one.reason.length > 0 ? ` <span class="decided" title="${escapeHtml(one.reason)}">why…</span>` : ''}</td>
    <td>${escapeHtml(outcomeSaid(one.outcome))}${byCell(one)}${closeControl(one)}</td>
    ${costCell(one, vendors, listed)}
    <td class="what">${foldedCell(one.problem, `${one.id}:problem`)}</td>
    <td class="what">${foldedCell(one.advice, `${one.id}:advice`)}</td>
  </tr>${one.alert.length === 0 ? '' : `
  <tr data-started="${startedMsOf(one.startedUtc)}"><td colspan="${CONSULTATION_COLUMNS}" class="failed">${escapeHtml(one.alert)}</td></tr>`}`).join('');

  // The server answers the newest N for the same N the rounds use. Said out loud when the list is
  // AT that number, because an older consultation silently not existing is a page telling a lie
  // about the history it exists to be. (codex, code round.)
  const capped = log.consultations.length >= CONSULTATIONS_SHOWN
    ? `<div class="hint">Showing the newest ${CONSULTATIONS_SHOWN}. Older consultations are in the database and not on this page.</div>`
    : '';

  return `${capped}<table><thead><tr>
    <th>Started</th><th>Kind</th><th>For</th><th>Who asked whom</th><th>Where</th><th class="num">Turns</th>
    <th>How it ended</th><th>Outcome</th><th class="num">Tokens</th><th class="num">Cost</th>
    <th>What was asked</th><th>What was advised</th>
  </tr></thead><tbody>${rows}</tbody></table>`;
}

/** How many cells a consultation row has — the alert row under it spans them all. */
const CONSULTATION_COLUMNS = 12;

/**
 * What an ordered consultation covered — the epics or the story, and the plan — beside its own Kind column.
 *
 * <p>Kind and For were one cell (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.3), which left
 * `stuck` a bare word; now the kind has a column of its own (todo/PLAN_consult_limits_kinds_and_help.md,
 * story 2) and this says what the rest was for. A stuck consultation covered nothing but its problem: a
 * dash. The plan is named by its file, the path kept for the hover.</p>
 */
function forCell(one: DbConsultation): string {
  const covered = coveredSaid(one.kind, one.epics);
  const plan = one.plan.length === 0
    ? ''
    : `<br><span class="decided" title="${escapeHtml(one.plan)}">${escapeHtml(repoNameOf(one.plan))}</span>`;

  return covered.length === 0 && plan.length === 0 ? '—' : `${escapeHtml(covered)}${plan}`;
}

/**
 * Who recorded it, beside the word — muted, because the word is the answer and this is its author.
 *
 * <p>Only where there is a word AND a recognised author: a row with no outcome has nobody to name,
 * and `ran out · by the server` says the same thing twice. The distinction is kept as a field by
 * the server so that nothing has to read a person's free-text note to learn it, and dropping it
 * here would throw that away one layer before the eye. (codex Architecture, the code round.)</p>
 */
function byCell(one: DbConsultation): string {
  const by = outcomeBySaid(one.outcomeBy);

  return by.length === 0 ? '' : ` <span class="decided">${escapeHtml(by)}</span>`;
}

/**
 * The control that records how a consultation ended, for the rows that have no verdict yet.
 *
 * <p>HERE rather than on the sidebar card, which is where it was first built. The sidebar is present
 * tense, and a control about how something ENDED does not belong in it — but the deciding reason is
 * that the card shows only LIVE consultations, so the moment one lapses it leaves the sidebar and
 * takes the control with it. The state the issue's own screenshot will be in within a day would have
 * had no control at all. (Operator decision, 2026-09-17.)</p>
 *
 * <p>Offered for an empty outcome and for `lapsed`, which are the two ways of saying nobody decided,
 * and withheld once somebody has: a verdict is not rewritten, so a control promising to is a control
 * that will be refused. A FAILED consultation produced no advice, so there is nothing to have a
 * verdict about.</p>
 */
function closeControl(one: DbConsultation): string {
  const decided = one.outcome !== undefined && one.outcome.length > 0 && one.outcome !== 'lapsed';
  if (decided || one.status === 'failed') {
    return '';
  }

  return ` <button type="button" class="decided" data-command="closeConsultation" data-id="${escapeHtml(one.id)}"
    title="Record how this consultation ended">record…</button>`;
}

/**
 * The Tokens and Cost cells, priced the way every other table here prices.
 *
 * <p>Its own function because the decision is `consultationCost`'s and the MARK is this cell's: an
 * estimate gets the class the reviewers' estimates already wear, so one look tells a bill from a
 * guess without reading the tilde. (issue #309.)</p>
 */
function costCell(one: DbConsultation, vendors: readonly Vendor[], listed: PriceLookup): string {
  const tokens = one.tokensIn + one.tokensOut;
  const cost = consultationCost(one.costUsd, one.tokensIn, one.tokensOut, priceOfLine(one.vendor, one.model, vendors, listed));

  return `<td class="num">${tokens > 0 ? escapeHtml(shortNumber(tokens)) : '—'}</td>
    <td class="num cost${cost.estimated ? ' est' : ''}">${escapeHtml(cost.text)}</td>`;
}

/** The caller kind as a person names it, never the raw word from the wire. */
function callerOf(kind: string): string {
  return kind === 'claude' ? 'Claude Code'
    : kind === 'codex' ? 'Codex'
      : kind === 'gemini' ? 'Gemini'
        : kind.length > 0 ? 'Another client' : 'An AI';
}

/**
 * `closed` is how a consultation is SUPPOSED to end, so it is not painted as a failure.
 *
 * <p>`interrupted` is the amber one and deliberately not red: the vendor accepted the turn, the
 * process died before the answer was read, and the conversation is resumable.</p>
 */
function badgeOf(status: string): string {
  return status === 'failed' ? 'interrupted' : status === 'interrupted' ? 'awaiting' : 'done';
}

/**
 * When a consultation started, in ms, for the page's period filter — `''` when it cannot be read, which
 * the page shows only under All rather than guessing which period it belongs to.
 */
function startedMsOf(utc: string): string {
  const at = Date.parse(utc);

  return Number.isFinite(at) ? String(at) : '';
}

function startedOf(utc: string): string {
  const at = Date.parse(utc);

  return Number.isFinite(at) ? new Date(at).toLocaleString() : utc;
}

/**
 * *What it keeps missing*, over the period the person chose — counted by the server (2026-09-25).
 *
 * <p>The period row comes FIRST and is drawn on every road, the empty one included: with nothing
 * decided today there must still be a way to reach Week. A server that did not echo the period it was
 * sent could not apply it (coai-mcp 0.36.0 was measured to ignore the flag), and says so in one
 * sentence rather than passing all time off as the period marked above it.</p>
 */
export function blindSpotsHtml(log: DbLog, period: LogPeriod = 'all'): string {
  return periodButtonsHtml('spots', period) + notSplitSaid(log, period) + spotsBody(log, period);
}

/**
 * What an empty tab says — about the PERIOD when one was applied. Under Today, "nothing decided yet"
 * read as never, on a machine with a year of decisions behind it. (Our own code reviewer.)
 */
function nothingDecided(log: DbLog, period: LogPeriod): string {
  return period !== 'all' && log.spotsSince.length > 0
    ? '<div class="empty">Nothing decided in this period — a wider one, or All, shows what was.</div>'
    : '<div class="empty">Nothing decided yet. This fills in as gates are closed —'
      + ' every accepted finding is something the AI had not seen and then agreed was worth having.</div>';
}

/** The one sentence an older server earns, or nothing: All is all time whoever counts it. */
function notSplitSaid(log: DbLog, period: LogPeriod): string {
  return period === 'all' || log.spotsSince.length > 0
    ? ''
    : '<div class="hint">This coai-mcp counts every decision it holds, whatever the period — update it to'
      + ' 0.37.0 or newer to split this tab by period. Showing all time.</div>';
}

function spotsBody(log: DbLog, period: LogPeriod): string {
  if (log.blindSpots.length === 0 && log.defended.length === 0) {
    return nothingDecided(log, period);
  }

  return '<div class="spots">'
    + spotTable('By category', log.blindSpots.filter((s) => s.kind === 'category'))
    + spotTable('By reviewer role', log.blindSpots.filter((s) => s.kind === 'role'))
    + spotTable('By vendor', log.blindSpots.filter((s) => s.kind === 'providers'))
    + '</div>'
    + defendedHtml(log.defended);
}

function spotTable(title: string, spots: readonly BlindSpot[]): string {
  if (spots.length === 0) {
    return '';
  }
  const rows = [...spots]
    .sort((a, b) => b.accepted - a.accepted || b.total - a.total)
    .map((s) => `<tr><td>${escapeHtml(s.name)}</td><td class="num">${s.accepted}</td>`
      + `<td class="num">${s.total}</td><td class="num">${share(s)}</td></tr>`)
    .join('');

  return `<div><h2>${escapeHtml(title)}</h2><table><thead><tr><th></th>`
    + '<th class="num">taken</th><th class="num">of</th><th class="num">%</th>'
    + `</tr></thead><tbody>${rows}</tbody></table></div>`;
}

function share(spot: BlindSpot): string {
  return spot.total === 0 ? '—' : `${Math.round((100 * spot.accepted) / spot.total)}`;
}

/** What the server caps that list at; it sends one more so the page can say there are more. */
const DEFENDED_CAP = 200;

function defendedHtml(defended: readonly DbFinding[]): string {
  if (defended.length === 0) {
    return '';
  }
  const items = defended
    .slice(0, DEFENDED_CAP)
    .map((f) => `<div class="finding declined"><span class="sev">${escapeHtml(f.severity)}</span> `
      + `<span class="where">${escapeHtml(f.file ? `${f.file}:${f.line}` : 'no file')}</span> `
      + `<b>${escapeHtml(f.title)}</b><div class="why">${escapeHtml(f.why)}</div>`
      + `<div class="verdict">${escapeHtml(f.providers)} / ${escapeHtml(f.role)}`
      + `${f.reason ? ` &middot; the standing reason: ${escapeHtml(f.reason)}` : ''}</div></div>`)
    .join('');

  // The server fetches one more than it will show, so "exactly two hundred" and "more than that"
  // are different answers. A list that was cut and looks whole is worse than no list.
  const cut = defended.length > DEFENDED_CAP
    ? ` The most recent ${DEFENDED_CAP} of them; there are more.`
    : '';

  return '<h2>Rejected, and raised again anyway</h2>'
    + '<div class="hint">A disagreement the caller is defending — the rejection still stood when'
    + ` another reviewer made the same case.${cut}</div>`
    + `<div class="findings">${items}</div>`;
}

/**
 * What a tab shows between the first paint and its first push.
 *
 * <p>The first paint is deliberately database-free — reading the log spawns a process, and nobody
 * should wait on one to see their log — so both of these regions open empty and are filled a moment
 * later. An empty div said nothing, which is precisely how a push that never arrived came to look
 * identical to one that simply had not arrived yet: the tab the operator opened on 2026-09-08 was
 * blank, and blank is also what it correctly shows for the first half-second of every open.</p>
 *
 * <p>It names no region, because the reader is standing on the tab that names itself — and because
 * a template that took one produced "Reading the log for what it keeps missing…". (The code
 * round, twice.) A section still reading this after `STILL_NOTHING_MS` replaces it with what went
 * wrong; see the page script.</p>
 */
function waitingFor(): string {
  return '<div class="empty">Reading the log…</div>';
}


/** What `--log` answers at most: `RoundsQuery` bounds the consultations by the rounds' own limit. */
export const CONSULTATIONS_SHOWN = PAGE_SIZE;

export function roundsLogHtml(
  rows: readonly LogRow[],
  questions: readonly Escalation[],
  nonce: string,
  usageHtml = '',
  spotsHtml = '',
  totals: DbTotals = EMPTY_TOTALS,
  consultationsHtmlText = '',
): string {
  const headers = COLUMNS
    .map((c) => (c.sortable === false
      // No `data-sort`: the click handler finds a sortable header by that attribute, so a column
      // without one cannot be clicked into an ordering that means nothing.
      ? (c.key === 'pick'
        // The box in the HEADER selects every row the filters MATCH, not every row on the page:
        // paging is a window on a list and a person ticking the top box means the list.
        ? '<th class="pick col-pick"><input type="checkbox" id="pickall" title="Select every matching round"></th>'
        : '<th class="actions col-actions"></th>')
      : `<th data-sort="${c.key}" class="col-${c.key}${c.numeric ? ' num' : ''}">`
        + (c.alt === undefined
          ? c.label
          : `<span class="asRound">${c.label}</span><span class="asChat">${c.alt}</span>`)
        + '</th>'))
    .join('');
  const filters = FACETS
    .map((f) => `<label class="facet facet-${f.key}">${f.label} `
      + `<select data-filter="${f.key}"><option value="">any</option>${facetOptions(rows, f.key)}</select></label>`)
    .join('\n      ');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ConnectOtherAIs — review rounds</title>
<style>
  body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 16px 20px; }
  h1 { font-size: 1.3em; margin: 0 0 12px; }
  h2 { font-size: 1.1em; margin: 16px 0 8px; }
  .question { border: 1px solid var(--vscode-inputValidation-warningBorder, var(--vscode-panel-border)); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .question .where { opacity: .75; font-size: .92em; margin-bottom: 4px; }
  .question .gating { margin-top: 6px; opacity: .85; }
  .question ul { margin: 4px 0 8px 18px; padding: 0; }
  .toolbar { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin: 12px 0; }
  .toolbar label { display: inline-flex; gap: 6px; align-items: center; font-size: .92em; }
  .toolbar input, .toolbar select { background: var(--vscode-input-background); color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px; padding: 3px 6px; font: inherit; }
  .toolbar input { min-width: 220px; }
  #count { opacity: .75; margin-left: auto; font-size: .92em; }
  button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 2px; padding: 4px 10px; font: inherit; cursor: pointer; }
  button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
  /* The bare rule IS the primary button here and \`secondary\` is the opt-out, which is why an action
     is painted by taking no class at all. Actions on the current view — Today, All dates, Clear,
     Answer… — are primary; the pager and the tab strip are navigation and stay quiet. And every
     button answers the pointer: colour alone is one signal, and in a dark theme it was not enough
     to tell these three from the text beside them (issue #126). */
  /* A CONTROL THAT CANNOT BE PRESSED MUST NOT LOOK PRESSABLE. The pager's buttons were disabled
     correctly from the day they shipped and nothing said so: there was no :disabled rule at all, so
     a dead "Older" kept the filled colour and the hand cursor, and "the buttons are active and
     pressing them does nothing" is what that reads as (issue #297).

     The hover selectors are REWRITTEN rather than joined by a :not(:disabled) sibling, and that is
     the point rather than a tidiness: a browser matches :hover on a disabled element and suppresses
     only the pointer events, so a bare button:hover left in the sheet goes on painting the dead
     control while the guarded rule simply does not apply to it. Adding beside it would have fixed
     nothing. (The plan round caught this in the plan; the test names both selectors when it regresses.)

     opacity + cursor is this product's own way of drawing a disabled control — six other sites,
     bugzReviewPage.ts among them with this identical rule.

     No backticks in here: this comment is INSIDE the CSS template literal, and one ends it. */
  button:disabled, button.secondary:disabled { opacity: .5; cursor: default; }
  button:hover:not(:disabled) { background: var(--vscode-button-hoverBackground); }
  button.secondary:hover:not(:disabled) { background: var(--vscode-button-secondaryHoverBackground); }
  .wrap { overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; font-size: .95em; }
  th, td { text-align: left; padding: 5px 8px; border-bottom: 1px solid var(--vscode-panel-border); white-space: nowrap; vertical-align: top; }
  th { position: sticky; top: 0; background: var(--vscode-editor-background); cursor: pointer; user-select: none; }
  th.num, td.num { text-align: right; font-variant-numeric: tabular-nums; }
  td.cost { white-space: nowrap; }
  th[data-sort].asc::after { content: " ▲"; opacity: .7; }
  th[data-sort].desc::after { content: " ▼"; opacity: .7; }
  td.what { white-space: normal; min-width: 260px; max-width: 560px; }
  /* A consultation's long field, folded: the first line as the summary, the whole inside. Open, the
     preview gives way to "Collapse" so the first line is not shown twice. */
  details.fold > summary { cursor: pointer; }
  details.fold .less { display: none; opacity: .7; }
  details.fold[open] > summary .preview { display: none; }
  details.fold[open] > summary .less { display: inline; }
  details.fold .whole { white-space: pre-wrap; overflow-wrap: anywhere; margin-top: 4px; }
  /* Long cells are cut with an ellipsis rather than pushing the table sideways; the full text is
     the cell's own title, so hovering reads it. */
  td.who-answered { max-width: 320px; overflow: hidden; text-overflow: ellipsis; }
  td[title] { cursor: help; }
  tr[data-key] { cursor: pointer; }
  tr[data-key]:hover td { background: var(--vscode-list-hoverBackground); }
  tr.detail td { white-space: normal; background: var(--vscode-editorWidget-background); padding: 6px 8px 8px 28px; cursor: default; }
  .reviewer { font-size: .95em; margin: 1px 0; }
  /* Who ASKED, above the reviewers who answered. Dimmed: it is the same for every round of a
     session, so it is context rather than one of the lines a person is reading down. */
  .reviewer.asked { color: var(--vscode-descriptionForeground); margin-bottom: 4px; }
  .who { font-weight: 600; }
  .badge { display: inline-block; padding: 0 6px; border-radius: 8px; font-size: .88em; }
  .badge.running { background: var(--vscode-charts-blue); color: var(--vscode-editor-background); }
  .badge.interrupted { background: var(--vscode-charts-orange); color: var(--vscode-editor-background); }
  .badge.done { background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); }
  .badge.awaiting { background: var(--vscode-charts-purple); color: var(--vscode-editor-background); }
  .decided { margin-left: 6px; opacity: .85; white-space: nowrap; }
  /* The deciding time sits beside the reviewers' time and must not compete with it: the
     question people scan this column for is still how long the round took. */
  .deciding { opacity: .7; }
  /* The actions column carries a button and no heading; it must not stretch to fit a title
     it does not have, and must not be the column a long table widens into. */
  th.actions, td.actions { width: 1%; white-space: nowrap; text-align: right; }
  /* The tick column is as narrow as a box and must not take the click target of the row with it. */
  th.pick, td.pick { width: 1%; padding-right: 0; }
  th.pick input, td.pick input { cursor: pointer; margin: 0; }
  .empty { opacity: .75; padding: 24px 0; }
  .failed { border: 1px solid var(--vscode-inputValidation-errorBorder, #c33); background: var(--vscode-inputValidation-errorBackground, transparent); padding: 8px 12px; margin: 0 0 12px; white-space: pre-wrap; }
  .tabs { display: flex; gap: 6px; margin: 4px 0 10px; border-bottom: 1px solid var(--vscode-panel-border); }
  .tabs .tab, .windows .tab { background: transparent; color: var(--vscode-foreground); border: none; border-bottom: 2px solid transparent; border-radius: 0; padding: 6px 10px; opacity: .75; }
  .tabs .tab.on, .windows .tab.on { opacity: 1; border-bottom-color: var(--vscode-focusBorder); }
  .windows { display: flex; gap: 6px; margin: 0 0 10px; }
  .spend { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 8px 12px; margin: 0 0 8px; max-width: 640px; }
  .spend .head { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .spend .name { font-weight: 600; }
  .spend .cost { margin-left: auto; }
  .spend .bar { height: 6px; background: var(--vscode-progressBar-background, var(--vscode-panel-border)); border-radius: 3px; margin: 6px 0; overflow: hidden; }
  .spend .bar span { display: block; height: 100%; background: var(--vscode-charts-blue); }
  .spend .figures { font-size: .95em; }
  .spend .hint, .total { margin-top: 2px; }
  /* TWO LEDGERS ON ONE PAGE, and they are written by different programs: coai-mcp appends a line
     per reviewer, this extension appends one per chat turn. A heading each and a rule between them,
     so nobody adds them up by eye without noticing which is which. */
  h3.ledger { font-size: 1.05em; margin: 18px 0 8px; font-weight: 600; }
  .ledger-half > h3.ledger:first-child { margin-top: 4px; }
  /* ONE COLUMN by default, TWO when the window is wide enough to hold both at the width the cards
     already have. Asked for in those words. The columns are capped rather than fractional: a 1fr
     column on a wide screen stretches the halves apart and leaves each card floating in its own
     empty third, which is a different page rather than the same one side by side. */
  .ledgers-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: 0 36px; align-items: start; justify-content: start; }
  @media (min-width: 1360px) {
    .ledgers-grid { grid-template-columns: repeat(2, minmax(0, 660px)); }
    /* The rule between the halves is the STACKED separator. Side by side the gap separates them, and
       a horizontal rule in a grid cell would be a line drawn across nothing. */
    .ledgers-grid > hr.ledgers { display: none; }
  }
  hr.ledgers { border: 0; border-top: 1px solid var(--vscode-panel-border); margin: 20px 0 0; max-width: 640px; }
  /* The model a chat card is about, beside the vendor that ran it: quieter than the name, because
     the vendor is what the eye is scanning for down a column of cards. */
  .spend .model { opacity: .75; font-size: .95em; }
  /* The line under BOTH ledgers. Nothing else on the page adds them up, and a reader asking "what
     has this cost me" is not asking it about one half. */
  .total.everything { font-weight: 600; opacity: .9; margin-top: 10px; }
  .warn { color: var(--vscode-charts-yellow); }
  .link { background: none; border: none; color: var(--vscode-textLink-foreground); padding: 0 4px; }
  .hint { opacity: .65; font-size: .9em; margin-top: 10px; }
  .findings { margin-top: 8px; }
  .finding { border-left: 3px solid var(--vscode-panel-border); padding: 2px 0 6px 10px; margin: 6px 0; }
  .finding.took { border-left-color: var(--vscode-charts-green, var(--vscode-charts-blue)); }
  .finding.declined { border-left-color: var(--vscode-charts-orange); }
  .finding .sev { font-weight: 600; }
  .finding .where { opacity: .75; font-family: var(--vscode-editor-font-family); font-size: .92em; }
  .finding .why { opacity: .85; margin: 2px 0 0; }
  .finding .verdict { opacity: .75; font-size: .92em; margin-top: 3px; }
  .finding .again { color: var(--vscode-charts-orange); font-size: .9em; }
  .pager { display: flex; align-items: center; gap: 8px; margin: 10px 0 0; }
  .pager #pageinfo { opacity: .75; font-size: .92em; }
  .await, .none, .nokeep, .broke { opacity: .8; font-size: .92em; margin-top: 8px; }
  .broke { color: var(--vscode-charts-orange); }
  .spots { display: flex; flex-wrap: wrap; gap: 18px; }
  .spots table { width: auto; min-width: 260px; }
  /* ONE TABLE, TWO VIEWS (issue #297). Conversations used to sit in the Rounds list with a Kind
     column telling them apart; they have a tab now and the column is gone, because the view says it.

     What a conversation has no answer for is HIDDEN rather than shown empty: it has no repository,
     branch, stage, verdict, or findings, and a row of blanks under those headers reads as a round
     that lost them. The tick-boxes and both Export controls go too — the export writes a round's
     findings, and offering it over rows that have none is an invitation to a refusal. So does the
     footer count, which is about coai.db, and coai.db holds no conversations at all.

     The two labels that differ are both drawn and one is hidden, so the header needs no script.

     No backticks in here: this comment is INSIDE the CSS template literal, and one ends it. */
  .view-rounds .asChat, .view-conversations .asRound { display: none; }
  .view-conversations .col-repoName, .view-conversations .col-branch, .view-conversations .col-stage,
  .view-conversations .col-verdict, .view-conversations .col-gating, .view-conversations .col-findings,
  .view-conversations .col-pick, .view-conversations .col-actions,
  .view-conversations .facet-repoPath, .view-conversations .facet-branch,
  .view-conversations .facet-stage, .view-conversations .facet-verdict,
  .view-conversations #exportpicked, .view-conversations #clearpicked,
  .view-conversations #recorded { display: none; }
</style>
</head>
<body>
<h1>Review rounds</h1>
<div id="failed" class="failed" hidden></div>
<div id="questions">${questionsHtml(questions)}</div>
<div class="tabs"><button type="button" class="tab on" data-tab="rounds">Rounds</button><button type="button" class="tab" data-tab="conversations">Conversations</button><button type="button" class="tab" data-tab="consultations">Consultations</button><button type="button" class="tab" data-tab="usage">What each AI has used</button><button type="button" class="tab" data-tab="spots">What it keeps missing</button></div>
<section id="tab-rounds" data-section="rounds" class="view-rounds">
<div class="asChat">${periodButtonsHtml('conversations', DEFAULT_PERIOD)}</div>
<div class="toolbar">
      <input id="search" type="search" placeholder="Search subject, branch, repository, reviewers, models…" autocomplete="off">
      <label>From <input id="from" type="datetime-local" step="60"></label>
      <label>To <input id="to" type="datetime-local" step="60"></label>
      <button type="button" id="today">Today</button>
      <button type="button" id="alldates">All dates</button>
      ${filters}
      <button type="button" id="clear">Clear</button>
      <button type="button" id="exportpicked" disabled>Export selected…</button>
      <button type="button" class="secondary" id="clearpicked" hidden>Clear selection</button>
      <span id="count"></span>
</div>
<div class="wrap">
<table id="log">
  <thead><tr>${headers}</tr></thead>
  <tbody id="rows"></tbody>
</table>
</div>
<div id="empty" class="empty"${rows.length === 0 ? '' : ' hidden'}>No rounds yet. A session appears once an AI calls <code>open</code> for a repository and branch.</div>
<div class="pager">
  <button type="button" class="secondary" id="prev">◀ Newer</button>
  <button type="button" class="secondary" id="next">Older ▶</button>
  <span id="pageinfo"></span>
</div>
<div id="recorded" class="hint"></div>
<div class="hint">Opens on <b>today</b> — <b>All dates</b> clears the range, and the pickers take a time as well as a day. Cost is <b>in / out / total</b> — <code>~</code> means worked out from a public price list rather than billed, <code>+</code> means one reviewer's model had no listed price so the total is a floor. <b>Took</b> is how long the reviewers ran and, after a <code>&#183;</code>, how long the deciding took — from the round finishing to its last decision; one number alone means nobody has decided it yet. Click a column to sort, a row to see its reviewers. The table advances by itself while a round runs; your sort, filters and search stay.</div>
</section>
<section id="tab-consultations" data-section="consultations" hidden>${periodButtonsHtml('consultations', DEFAULT_PERIOD)}<div id="consultations-none" class="empty" hidden>No consultation in this period — All shows every one.</div><div id="consultations-body">${consultationsHtmlText || waitingFor()}</div></section>
<section id="tab-usage" data-section="usage" hidden><div id="usage-body">${usageHtml || waitingFor()}</div></section>
<section id="tab-spots" data-section="spots" hidden><div id="spots-body">${spotsHtml || waitingFor()}</div></section>
<script nonce="${nonce}">
(function () {
  // A page that fails must say so on the page. The first release of this page came up as a header
  // row over nothing inside VS Code's webview, while the same HTML rendered every row in node and
  // in headless Chromium; whatever it was, it said nothing. Now it says what and where.
  function failed(message) {
    var box = document.getElementById('failed');
    if (box) {
      box.hidden = false;
      box.textContent = 'This page hit an error and stopped: ' + message + '. Reload the window (Developer: Reload Window); if it comes back, copy this text into an issue.';
    }
  }
  window.onerror = function (message, source, line, column) {
    failed(String(message) + ' (line ' + line + ':' + column + ')');
  };
  var vscode = acquireVsCodeApi();
  // Which sections opened on the "Reading the log…" placeholder, stated by the render rather than
  // read back out of the DOM: what a section CONTAINS is HTML from the database, and searching it
  // for the placeholder's own words is a check that a blind spot titled "Reading the log" would
  // defeat.
  // Every section the timeout below watches has a key here, and the CONSULTATIONS one was missing:
  // the consultations entry read undefined, the guard skipped it on every tick, and a tab that
  // opened on the placeholder and never received a push sat on "Reading the log…" for ever — which
  // is the exact state the timeout exists to end. (CodeRabbit, on the pull request.)
  var WAITING = ${JSON.stringify({ usage: usageHtml === '', spots: spotsHtml === '', consultations: consultationsHtmlText === '' })};
  var ROWS = ${jsonForScript(rows)};
  // Assigned, never declared: the extension ships BUNDLED and minified, and a minifier renames a
  // function that is not a top-level export — so the declaration this embedded read
  // "function m(a, b, c, d)" in the VSIX while the page called rowMatches(). It worked from the
  // unbundled out/ in node and in headless Chromium, and failed only in the installed extension,
  // which is why the page now reports its own errors (0.29.10 found this one in a minute).
  var compareRows = ${compareRows.toString()};
  var askedByHtml = ${askedByHtml.toString()};
  var rowMatches = ${rowMatches.toString()};
  var money = ${money.toString()};
  var cost3 = ${cost3.toString()};
  var costTitle = ${costTitle.toString()};

  var state = {
    sortKey: 'startedUtc', dir: 'desc', filters: {}, search: '', expanded: {}, page: 0,
    // Which half of the table is on screen. The tab strip sets it; everything downstream reads it.
    view: 'rounds',
    // Keyed by row key, the way expanded is. A SELECTION is a decision the person made, so a filter
    // that hides a selected row does not unmake it — only the row leaving the loaded set does.
    selected: {},
  };
  var PAGE_SIZE = ${PAGE_SIZE};
  // Derived from the column list rather than typed as a literal: an opened row's detail spans the
  // whole table, and a hand-written colspan is a number that silently stops matching the day a
  // column is added. Adding the Kind column is exactly that day.
  var COLUMN_COUNT = ${COLUMNS.length};
  var inView = ${inView.toString()};
  var ROUND_ONLY_FACETS = ${jsonForScript(ROUND_ONLY_FACETS)};
  // What SQL counted over the WHOLE table, which is a different number from the length of what was
  // sent. The operator asked for this in as many words: «суммы - скл счиатть (сколько всего и тд.)».
  var TOTALS = ${jsonForScript(totals)};
  // Every filter, the search text and the sort all return to the first page. "Next page" of a
  // client-side filter over a server-side window is a promise nothing can keep: the cursor moves in
  // the unfiltered stream and rows skip or repeat across the boundary. (Plan round, gemini.)
  function firstPage() { state.page = 0; }
  function localDay(d) {
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
  }
  var asInstant = ${asInstant.toString()};

  function esc(value) {
    return String(value)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }
  function when(iso) {
    if (!iso) { return ''; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return esc(iso); }
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
  }
  function num(n) {
    if (n === null || n === undefined) { return ''; }
    if (n >= 1000000) { return (n / 1000000).toFixed(1) + 'M'; }
    if (n >= 1000) { return (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k'; }
    return String(n);
  }
  function took(s) {
    if (s === null || s === undefined) { return ''; }
    if (s < 60) { return s + ' s'; }
    var m = Math.floor(s / 60);
    return m < 60 ? m + 'm ' + (s % 60) + 's' : Math.floor(m / 60) + 'h ' + (m % 60) + 'm';
  }

  // TWO times, when both are known: how long the reviewers ran, and how long the deciding took.
  // One when it is not — a round nobody has decided, a conversation, or a server too old to send
  // the stamp all read exactly as they did before. Never "1m 37s · 0s" for an unknown: the absence
  // of a measurement is a blank, not a zero.
  function tookCell(r) {
    var ran = took(r.seconds);
    if (r.decideSeconds === null || r.decideSeconds === undefined) {
      return '<td class="num col-seconds">' + ran + '</td>';
    }
    // The title says what the second number MEASURES, because a wall clock over a round somebody
    // came back to after lunch counts the lunch, and "deciding" alone would overclaim.
    // The em dash stands in for a round whose OWN duration is unknown — an interrupted one, say —
    // in the cell as well as in the title. Without it the cell opened on a floating middle dot.
    // (Code round, gemini.)
    var shown = ran || '—';

    return '<td class="num col-seconds" title="' + esc(shown) + ' the reviewers ran &#183; '
      + esc(took(r.decideSeconds)) + ' from the round finishing to its last decision, '
      + 'which counts any time nobody was looking at it">'
      + shown + ' <span class="deciding">&#183; ' + took(r.decideSeconds) + '</span></td>';
  }

  function badge(status) {
    var said = status === 'awaiting' ? 'awaiting decisions' : status;
    return '<span class="badge ' + esc(status) + '">' + esc(said) + '</span>';
  }
  function decided(row) {
    // Nothing for a round the database never saw, and nothing while the gate is still open - the
    // badge already says that. A closed gate shows what it closed AT, which is the whole reason
    // these two numbers are recorded.
    if (!row.decided || row.decided.accepted < 0) { return ''; }
    var a = row.decided.accepted, r = row.decided.rejected;
    return '<span class="decided" title="' + a + ' accepted, ' + r + ' rejected">'
      + a + ' ✓ ' + r + ' ✗</span>';
  }
  function detail(row) {
    // WHO asked for the round, above the reviewers that answered it. Before the early return on
    // purpose: a session written by an older server has no reviewer detail, and one written by a
    // newer one against an older extension is the case this line exists for.
    var asked = askedByHtml(row.calledBy, esc);
    if (row.reviewers.length === 0) {
      return asked + '<div class="reviewer">This round recorded no reviewer detail — it was written by an older server.</div>';
    }
    var lines = asked;
    for (var i = 0; i < row.reviewers.length; i++) {
      var line = row.reviewers[i];
      var slash = line.indexOf('/');
      var who = slash < 0 ? line : line.slice(0, slash);
      var rest = slash < 0 ? '' : line.slice(slash);
      lines += '<div class="reviewer"><span class="who" style="color:' + esc(row.reviewerColours[i] || 'inherit') + '">' + esc(who) + '</span>' + esc(rest) + '</div>';
    }
    return lines + foundHtml(row);
  }
  // What the round ORDERED its caller to do, and the size it measured (issue #131) - so that "why
  // did it cut my plan into five epics" has an answer on the page. Nothing at all for a round that
  // gave no orders or recorded none: an empty block on every code round would be noise.
  function ordersHtml(row) {
    var orders = row.orders;
    if (!orders || !orders.commands || orders.commands.length === 0) {
      return '';
    }
    var out = '<div class="orders"><b>Orders given</b>';
    if (orders.planShape) {
      out += '<div class="why">size ' + esc(orders.planShape) + '</div>';
    }
    for (var i = 0; i < orders.commands.length; i++) {
      var text = String(orders.commands[i]);
      var cut = text.indexOf('. ');
      out += '<details class="order"><summary>' + esc(cut > 0 ? text.slice(0, cut + 1) : text) + '</summary>'
        + '<div class="why">' + esc(text) + '</div></details>';
    }
    return out + '</div>';
  }
  function foundHtml(row) {
    return (row.foundState === 'loaded' ? ordersHtml(row) : '') + foundOnly(row);
  }
  // What the round FOUND, under the reviewers that found it. Severity first because that is how a
  // person triages, then where, then what it said and what was decided about it - an accepted
  // finding is something this repository's author had not seen, which is the whole point of keeping
  // them.
  function foundOnly(row) {
    // Five states, and each draws its OWN element. Four of them used to be one blank, and a blank
    // reads as "this round was clean" — which is a lie about three of them.
    if (row.foundState === 'asking') {
      return '<div class="await">Reading what this round found…</div>';
    }
    if (row.foundState === 'failed') {
      return '<div class="broke">Its findings could not be read. '
        + '<button type="button" class="link" data-retry="' + esc(row.key) + '">Try again</button></div>';
    }
    if (row.foundState === 'absent') {
      return '<div class="nokeep">The rounds database has no record of this round, so what it found '
        + 'was never written down — it ran before the database existed.</div>';
    }
    if (!row.found || row.found.length === 0) {
      // A row can be loaded with nothing in it while the count says otherwise: the count comes
      // from the list and the sentences from a later read, and the database moves between them.
      // Without this it drew "Reading…" for ever, because ask() refuses a loaded row. (CodeRabbit.)
      return row.foundCount > 0
        ? '<div class="broke">This round recorded ' + row.foundCount
          + ' findings, and the read came back with none. '
          + '<button type="button" class="link" data-retry="' + esc(row.key) + '">Try again</button></div>'
        : '<div class="none">This round found nothing.</div>';
    }
    var out = '<div class="findings">';
    for (var i = 0; i < row.found.length; i++) {
      var f = row.found[i];
      var mark = f.resolution === 'accept' ? 'took' : f.resolution === 'reject' ? 'declined' : 'open';
      out += '<div class="finding ' + esc(mark) + '">'
        + '<span class="sev">' + esc(f.severity) + '</span> '
        + '<span class="where">' + esc(f.file ? f.file + ':' + f.line : 'no file') + '</span> '
        + '<b>' + esc(f.title) + '</b>'
        + (f.reRaised ? ' <span class="again">raised again</span>' : '')
        + '<div class="why">' + esc(f.why) + '</div>'
        + (f.fix ? '<div class="why"><i>fix:</i> ' + esc(f.fix) + '</div>' : '')
        + '<div class="verdict">' + esc(f.providers) + ' / ' + esc(f.role) + ' &middot; <b>' + esc(mark) + '</b>'
        + (f.reason ? ': ' + esc(f.reason) : '') + '</div>'
        + '</div>';
    }
    return out + '</div>';
  }
  function render() {
    // THE VIEW FIRST, before the search, the sort, the count and the slice.
    var matched = ROWS.filter(function (r) {
      return inView(r, state.view) && rowMatches(r, state.filters, state.search);
    });
    matched.sort(function (a, b) { return compareRows(a, b, state.sortKey, state.dir); });
    var pages = Math.max(1, Math.ceil(matched.length / PAGE_SIZE));
    if (state.page > pages - 1) { state.page = pages - 1; }
    if (state.page < 0) { state.page = 0; }
    var from = state.page * PAGE_SIZE;
    var shown = matched.slice(from, from + PAGE_SIZE);
    var html = '';
    for (var i = 0; i < shown.length; i++) {
      var r = shown[i];
      html += '<tr data-key="' + esc(r.key) + '">'
        + '<td class="pick col-pick"><input type="checkbox" data-pick="' + esc(r.key) + '"'
        // The column header is blank and the round's identity is in other cells, so without this the
        // box is announced as an unlabelled checkbox and a screen-reader user cannot tell which round
        // they are selecting. (CodeRabbit, on the pull request.)
        + ' aria-label="Select ' + esc(r.stage) + ' round ' + esc(String(r.number))
        + (r.repoName ? ' of ' + esc(r.repoName) : '') + '"'
        + (state.selected[r.key] ? ' checked' : '') + '></td>'
        + '<td class="col-startedUtc">' + when(r.startedUtc || r.completedUtc) + '</td>'
        + '<td class="col-repoName" title="' + esc(r.repoPath) + '">' + esc(r.repoName) + '</td>'
        + '<td class="col-branch" title="' + esc(r.branch) + '">' + esc(r.branch) + '</td>'
        + '<td class="col-stage">' + esc(r.stage) + '</td>'
        + '<td class="num col-number">' + r.number + '</td>'
        + '<td class="what col-subject" title="' + esc(r.subject) + '">' + esc(r.subject) + '</td>'
        + '<td class="col-status">' + badge(r.status) + decided(r) + '</td>'
        + '<td class="col-verdict">' + esc(r.verdict) + '</td>'
        + '<td class="num col-gating">' + r.gating + '</td>'
        + '<td class="num col-findings">' + num(r.findings) + '</td>'
        + tookCell(r)
        + '<td class="num col-tokensIn">' + num(r.tokensIn) + '</td>'
        + '<td class="num col-tokensOut">' + num(r.tokensOut) + '</td>'
        + '<td class="num cost col-costTotalUsd" title="' + esc(costTitle(r, money)) + '">' + cost3(r, money) + '</td>'
        + '<td class="who-answered col-answered" title="' + esc(r.answered) + '">' + esc(r.answered) + '</td>'
        + '<td class="actions col-actions"><button type="button" class="link" data-export="' + esc(r.key)
        + '" title="Write this round to a CSV file">Export</button></td>'
        + '</tr>';
      if (state.expanded[r.key]) {
        html += '<tr class="detail"><td colspan="' + COLUMN_COUNT + '">' + detail(r) + '</td></tr>';
      }
    }
    // A selection survives a filter — it is a decision, not a view — but a key whose row has left
    // the LOADED set entirely cannot be exported and is dropped here, in render, rather than only
    // on the five-second tick: an export fired between ticks would otherwise carry keys the page
    // has already forgotten.
    var live = {};
    for (var l = 0; l < ROWS.length; l++) { live[ROWS[l].key] = true; }
    for (var gone in state.selected) {
      if (!live[gone]) { delete state.selected[gone]; }
    }
    document.getElementById('rows').innerHTML = html;
    document.getElementById('empty').hidden = ROWS.length > 0;
    document.getElementById('count').textContent = matched.length === ROWS.length
      ? ROWS.length + ' round' + (ROWS.length === 1 ? '' : 's')
      : matched.length + ' of ' + ROWS.length + ' rounds match';
    // What the button SAYS is the count, and it says when part of the selection is out of sight:
    // exporting more than you can see is fine, being unaware of it is not.
    var picked = Object.keys(state.selected);
    var shownKeys = {};
    for (var s = 0; s < matched.length; s++) { shownKeys[matched[s].key] = true; }
    var hidden = picked.filter(function (k) { return !shownKeys[k]; }).length;
    var exportButton = document.getElementById('exportpicked');
    exportButton.disabled = picked.length === 0;
    exportButton.textContent = picked.length === 0
      ? 'Export selected…'
      : 'Export ' + picked.length + ' selected'
        + (hidden > 0 ? ' (' + hidden + ' hidden)' : '') + '…';
    // A way OUT of a selection that reaches past the filter. Unticking the header box clears only
    // the rows it would tick, so somebody who picked a hundred, filtered to ten and unticked would
    // otherwise be left with ninety they cannot see and no single gesture to drop them. Shown only
    // when there is something to clear. (Plan round, gemini.)
    var clearPicked = document.getElementById('clearpicked');
    clearPicked.hidden = picked.length === 0;
    clearPicked.textContent = hidden > 0 ? 'Clear selection (' + picked.length + ')' : 'Clear selection';
    var all = document.getElementById('pickall');
    if (all) {
      all.checked = matched.length > 0 && matched.every(function (r) { return state.selected[r.key]; });
      // SOME of what is shown, but not all of it. An unticked box beside five selected rows says
      // nothing here is picked, which is false; the indeterminate mark is the standard way to say
      // "partly". (Code round, gemini.)
      all.indeterminate = !all.checked
        && matched.some(function (r) { return state.selected[r.key]; });
    }
    document.getElementById('prev').disabled = state.page === 0;
    document.getElementById('next').disabled = state.page >= pages - 1;
    // Says which rows these are, never a bare count that could be read as a count of everything.
    document.getElementById('pageinfo').textContent = matched.length === 0
      ? 'nothing to show'
      // THE PAGE POSITION IS ALWAYS SAID, one page or ten. It used to appear only past the first
      // page, and the line directly under this one announces how many rounds the DATABASE holds —
      // so a person looking at one page of today's rows saw two disabled buttons, no word about
      // paging, and a count in the thousands. Two true statements that together read as a pager
      // that does not work, which is what was reported (issue #297).
      : 'rows ' + (from + 1) + '–' + (from + shown.length) + ' of ' + matched.length
        + ' · page ' + (state.page + 1) + ' of ' + pages;
    document.getElementById('recorded').textContent = TOTALS.rounds === 0
      ? ''
      : 'The database has ' + TOTALS.rounds + ' rounds and ' + TOTALS.findings + ' findings — '
        + TOTALS.accepted + ' accepted, ' + TOTALS.rejected + ' rejected, ' + TOTALS.gating
        + ' gating. Counted by the database, not by this page.';
    var ths = document.querySelectorAll('th[data-sort]');
    for (var t = 0; t < ths.length; t++) {
      ths[t].className = ths[t].className.replace(/\\b(asc|desc)\\b/g, '').trim();
      if (ths[t].getAttribute('data-sort') === state.sortKey) { ths[t].className += ' ' + state.dir; }
      if (ths[t].getAttribute('data-sort') === state.sortKey) { ths[t].className = ths[t].className.trim(); }
    }
  }

  document.addEventListener('click', function (event) {
    var target = event.target;
    var tab = target.closest('[data-tab]');
    if (tab) {
      var which = tab.getAttribute('data-tab');
      var tabs = document.querySelectorAll('[data-tab]');
      for (var i = 0; i < tabs.length; i++) {
        tabs[i].className = tabs[i].getAttribute('data-tab') === which ? 'tab on' : 'tab';
      }
      // DERIVED from the markup, never a list of ids. Three named one at a time meant a section
      // added tomorrow would render and never be un-hidden, with nothing red — which is the defect
      // the notifications page was deliberately built without, and the reason this step exists.
      const sections = document.querySelectorAll('section[data-section]');
      for (let s = 0; s < sections.length; s++) {
        sections[s].hidden = sections[s].getAttribute('data-section') !== which;
      }
      // BEFORE the table's own line, which follows and wins. One section answers to TWO tabs, so
      // the general rule gets it wrong for conversations and the exception has to run last; the
      // other way round the table would vanish the moment somebody chose conversations.
      // ONE section serves both halves of the table, so it stays on screen for either tab and wears
      // the view as a class. Everything a conversation has no answer for is hidden by that class,
      // rather than shown as a row of blanks under a round's headers.
      var table = document.getElementById('tab-rounds');
      var asTable = which === 'rounds' || which === 'conversations';
      table.hidden = !asTable;
      if (asTable && which !== state.view) {
        table.className = which === 'conversations' ? 'view-conversations' : 'view-rounds';
        state.view = which;
        // AND THE FILTERS THE NEW VIEW CANNOT ANSWER GO WITH IT. A conversation has no repository,
        // branch, stage or verdict, so a value chosen while looking at rounds matches none of them
        // and empties the tab the moment it opens — with the control that did it hidden, so nothing
        // on screen says why. Cleared in the state AND on the select, or the box goes on showing a
        // choice that is no longer being applied.
        if (which === 'conversations') {
          for (var f = 0; f < ROUND_ONLY_FACETS.length; f++) {
            delete state.filters[ROUND_ONLY_FACETS[f]];
            var box = document.querySelector('[data-filter="' + ROUND_ONLY_FACETS[f] + '"]');
            if (box) { box.value = ''; }
          }
        }
        // The same rule every other filter change follows: a page number belongs to a list, and this
        // is a different list.
        firstPage();
        render();
      }
      return;
    }
    // A PERIOD button on Conversations or Consultations: filtered here, because their rows are on the
    // page. What it keeps missing sends a command instead (data-command), because the server counts it.
    var periodButton = target.closest('[data-period]');
    if (periodButton) {
      var group = periodButton.closest('[data-periods]');
      if (group) { choosePeriod(group.getAttribute('data-periods'), periodButton.getAttribute('data-period')); }
      return;
    }
    var button = target.closest('[data-command]');
    if (button) {
      // The model rides along because a CHAT row is a vendor AND a model, and one id cannot name
      // the pair. Every other command ignores it; the decoder reads it only for the one that needs
      // it. Without this the cross on a chat row looked right in the markup and forgot nothing,
      // because the page never sent the half that says which model. (No backticks in here: this is
      // inside a template literal.)
      // What it keeps missing's period is counted by the server, which takes a spawn to answer: the
      // press marks itself now and the push confirms it. (Our own code reviewer.)
      if (button.getAttribute('data-command') === 'spotsPeriod') { markPeriod('spots', button.getAttribute('data-id')); }
      vscode.postMessage({ type: 'command', command: button.getAttribute('data-command'), id: button.getAttribute('data-id'), model: button.getAttribute('data-model') });
      return;
    }
    var th = target.closest('th[data-sort]');
    if (th) {
      var key = th.getAttribute('data-sort');
      if (state.sortKey === key) { state.dir = state.dir === 'asc' ? 'desc' : 'asc'; }
      else { state.sortKey = key; state.dir = key === 'startedUtc' ? 'desc' : 'asc'; }
      firstPage();
      render();
      return;
    }
    // ABOVE the row branch, and it returns: ticking a box would otherwise fall through and open
    // the row as well.
    var picking = target.closest('[data-pick]');
    if (picking) {
      var picked = picking.getAttribute('data-pick');
      if (state.selected[picked]) { delete state.selected[picked]; } else { state.selected[picked] = true; }
      render();
      return;
    }
    // ABOVE the row branch, and it returns: a button inside a row would otherwise fall through and
    // toggle the row open as well as exporting it.
    var exporting = target.closest('[data-export]');
    if (exporting) {
      askExport([exporting.getAttribute('data-export')]);
      return;
    }
    var retry = target.closest('[data-retry]');
    if (retry) {
      ask(retry.getAttribute('data-retry'), true);
      return;
    }
    var tr = target.closest('tr[data-key]');
    if (tr) {
      var k = tr.getAttribute('data-key');
      if (state.expanded[k]) { delete state.expanded[k]; } else { state.expanded[k] = true; ask(k, false); }
      render();
    }
  });

  /**
   * Ask the extension to write these rows out.
   *
   * The ROWS travel, not just their keys: the host's copy is the unfiltered set as of the last
   * tick, and a row somebody selected may already have left it.
   */
  function askExport(keys) {
    var chosen = [];
    for (var i = 0; i < ROWS.length; i++) {
      if (keys.indexOf(ROWS[i].key) >= 0) { chosen.push(ROWS[i]); }
    }
    if (chosen.length === 0) { return; }
    vscode.postMessage({ type: 'command', command: 'export', id: chosen[0].key, rounds: chosen });
  }

  // Asked ONCE. A row keeps what it was told, so closing and reopening costs nothing — except after
  // a failure, which is not cached, because a retry that answers from the failure is not a retry.
  function ask(key, again) {
    for (var i = 0; i < ROWS.length; i++) {
      if (ROWS[i].key !== key) { continue; }
      var state_ = ROWS[i].foundState;
      // A loaded row with nothing in it and a count that says otherwise is retryable too — it is a
      // disagreement, not an answer. (CodeRabbit, on the pull request.)
      var mismatched = state_ === 'loaded' && ROWS[i].foundCount > 0
        && (!ROWS[i].found || ROWS[i].found.length === 0);
      if (state_ !== 'unasked' && !(again && (state_ === 'failed' || mismatched))) { return; }
      ROWS[i] = Object.assign({}, ROWS[i], { foundState: 'asking' });
      // The dbKey travels WITH the request. The extension host used to look the row up in a
      // module-level copy of the last rows it built, which is a shared mutable global three
      // reviewers objected to and which answers wrongly for any row a later refresh dropped.
      vscode.postMessage({
        type: 'command', command: 'findings', id: key,
        session: ROWS[i].dbKey.sessionId, stage: ROWS[i].dbKey.stage, number: ROWS[i].dbKey.number,
      });
      render();
      return;
    }
  }
  var selects = document.querySelectorAll('[data-filter]');
  for (var s = 0; s < selects.length; s++) {
    selects[s].addEventListener('change', function (event) {
      state.filters[event.target.getAttribute('data-filter')] = event.target.value;
      firstPage();
      render();
    });
  }
  document.getElementById('search').addEventListener('input', function (event) {
    state.search = event.target.value;
    firstPage();
    render();
  });
  document.getElementById('prev').addEventListener('click', function () {
    state.page = state.page - 1;
    render();
  });
  document.getElementById('next').addEventListener('click', function () {
    state.page = state.page + 1;
    render();
  });
  var fromInput = document.getElementById('from');
  var toInput = document.getElementById('to');
  function readDates() {
    state.filters.from = asInstant(fromInput.value, false);
    // The upper bound INCLUDES the minute it names: the picker has minute granularity, so "to 23:59"
    // that stopped at 23:59:00.000 dropped the last minute of the day the page opens on.
    state.filters.to = asInstant(toInput.value, true);
    firstPage();
    render();
  }
  // Today, from its first minute to its last — the range the page opens on, because the question
  // somebody has when they open it is almost always "what happened today".
  function setToday() {
    var now = new Date();
    fromInput.value = localDay(now) + 'T00:00';
    toInput.value = localDay(now) + 'T23:59';
    readDates();
  }
  // A date typed by hand is the person's own range, so no period is marked; Today and All dates are
  // two of the periods, so the Conversations row follows them. (The period switch, 2026-09-25.)
  fromInput.addEventListener('change', function () { markPeriod('conversations', ''); readDates(); });
  toInput.addEventListener('change', function () { markPeriod('conversations', ''); readDates(); });
  document.getElementById('today').addEventListener('click', function () {
    setToday();
    markPeriod('conversations', 'day');
  });
  document.getElementById('alldates').addEventListener('click', function () {
    fromInput.value = '';
    toInput.value = '';
    readDates();
    markPeriod('conversations', 'all');
  });
  document.getElementById('exportpicked').addEventListener('click', function () {
    askExport(Object.keys(state.selected));
  });

  document.getElementById('clearpicked').addEventListener('click', function () {
    state.selected = {};
    render();
  });

  var pickAll = document.getElementById('pickall');
  if (pickAll) {
    pickAll.addEventListener('click', function () {
      // HIDDEN IS NOT DISABLED. The conversations view takes the tick column off the screen with
      // CSS, and an element that is not displayed can still be reached by a keyboard, an assistive
      // technology or another extension — which would select rows the export cannot write, to be
      // found later on a view where the button is back. (gemini, the code round.)
      if (state.view === 'conversations') { return; }
      // Every MATCHING round, across every page — a person ticking the top box means the list they
      // have filtered to, not the twenty rows they can see. Untick clears only those same rows, so
      // a selection made under another filter is not silently thrown away.
      var matched = ROWS.filter(function (r) {
        return inView(r, state.view) && rowMatches(r, state.filters, state.search);
      });
      var everyOne = matched.length > 0 && matched.every(function (r) { return state.selected[r.key]; });
      for (var m = 0; m < matched.length; m++) {
        if (everyOne) { delete state.selected[matched[m].key]; } else { state.selected[matched[m].key] = true; }
      }
      render();
    });
  }

  document.getElementById('clear').addEventListener('click', function () {
    state.filters = {};
    state.search = '';
    document.getElementById('search').value = '';
    fromInput.value = '';
    toInput.value = '';
    for (var c = 0; c < selects.length; c++) { selects[c].value = ''; }
    render();
  });
  // Which sections have actually been told something. A section still reading "Reading the log…"
  // after STILL_NOTHING_MS is a section nothing ever reached, and it says so rather than promising
  // for ever that something is coming.
  var told = {};
  // THE PERIOD SWITCH on Conversations and Consultations (operator, 2026-09-25): usage.ts's own
  // startOfPeriodMs and days, so "this week" is one answer on every tab — Today since local midnight,
  // the rest rolling.
  // The host's own function, handed over as source, and its days as data through the one escaper —
  // not a second copy of the rule written for the page. (The code round, 2026-09-25.)
  var PERIOD_DAYS = ${jsonForScript(DAYS_OF)};
  var startOfPeriodMs = ${startOfPeriodMs.toString()};
  function periodStartMs(period, now) {
    return startOfPeriodMs(period, now.getTime(), PERIOD_DAYS);
  }
  function localStamp(d) {
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return localDay(d) + 'T' + p(d.getHours()) + ':' + p(d.getMinutes());
  }
  // A page-filtered row's buttons say their period in data-period; What it keeps missing's are host
  // commands and say it in data-id. Both are read, or a spots press marked nothing (the code round).
  function markPeriod(tab, period) {
    var group = document.querySelector('[data-periods="' + tab + '"]');
    if (!group) { return; }
    var buttons = group.querySelectorAll('button');
    for (var i = 0; i < buttons.length; i++) {
      var id = buttons[i].getAttribute('data-period') || buttons[i].getAttribute('data-id');
      buttons[i].className = id === period ? 'tab on' : 'tab';
    }
  }
  var consultationsPeriod = 'day';
  // Rows older than the period are hidden — a row whose start cannot be read shows under All alone —
  // and when rows exist but none is inside the period one line says so, rather than an empty table.
  function filterConsultations() {
    var start = periodStartMs(consultationsPeriod, new Date());
    var rows = document.getElementById('consultations-body').querySelectorAll('tr[data-started]');
    var shown = 0;
    for (var i = 0; i < rows.length; i++) {
      var at = rows[i].getAttribute('data-started');
      var inside = consultationsPeriod === 'all' || (at !== '' && Number(at) >= start);
      rows[i].hidden = !inside;
      if (inside) { shown++; }
    }
    var none = rows.length > 0 && shown === 0;
    document.getElementById('consultations-none').hidden = !none;
    // The table goes with its rows: a header over nothing, under a line saying there is nothing, is noise.
    var tables = document.getElementById('consultations-body').querySelectorAll('table');
    for (var t = 0; t < tables.length; t++) { tables[t].hidden = none; }
  }
  function choosePeriod(tab, period) {
    markPeriod(tab, period);
    if (tab === 'consultations') {
      consultationsPeriod = period;
      filterConsultations();
      return;
    }
    if (tab !== 'conversations') { return; }
    if (period === 'day') {
      setToday();
      return;
    }
    fromInput.value = period === 'all' ? '' : localStamp(new Date(periodStartMs(period, new Date())));
    toInput.value = '';
    readDates();
  }
  // A live push REPLACES the consultations table, and a fold the person opened would snap shut under
  // them. So the keys of the open ones are read first and opened again after — compared as decoded
  // attribute values, never built into a selector, because a key carries a consultation id. A fold
  // nobody opened stays closed. (The Consultations tab folds its long fields, 2026-09-25.)
  function replaceKeepingFolds(body, html) {
    var open = new Set(Array.prototype.map.call(body.querySelectorAll('details[data-fold][open]'), function (one) {
      return one.getAttribute('data-fold');
    }));
    body.innerHTML = html;
    Array.prototype.forEach.call(body.querySelectorAll('details[data-fold]'), function (one) {
      if (open.has(one.getAttribute('data-fold'))) { one.open = true; }
    });
  }
  window.addEventListener('message', function (event) {
    var message = event.data;
    if (!message) { return; }
    if (message.type === 'consultations' && typeof message.html === 'string') {
      told.consultations = true;
      replaceKeepingFolds(document.getElementById('consultations-body'), message.html);
      filterConsultations();
      return;
    }
    if (message.type === 'spots' && typeof message.html === 'string') {
      told.spots = true;
      document.getElementById('spots-body').innerHTML = message.html;
      return;
    }
    if (message.type === 'usage' && typeof message.html === 'string') {
      told.usage = true;
      document.getElementById('usage-body').innerHTML = message.html;
      return;
    }
    if (message.type === 'totals' && message.totals) {
      // The page is painted before the database is read, so the line under the table opens on
      // nothing and is filled by this. It used to be stored on the panel and pushed nowhere, which
      // left the whole SQL-totals line permanently empty. (Code round, CodeRabbit.)
      TOTALS = message.totals;
      try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
      return;
    }
    if (message.type === 'found' && typeof message.id === 'string') {
      for (var f = 0; f < ROWS.length; f++) {
        if (ROWS[f].key === message.id) {
          ROWS[f] = Object.assign({}, ROWS[f], {
            found: message.findings || [], foundState: message.state || 'failed', orders: message.orders,
          });
        }
      }
      try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
      return;
    }
    if (message.type !== 'rows') { return; }
    // A tick rebuilds every row from the session files, and those rows know nothing about findings
    // somebody has already opened. Carry them across, or a five-second tick would close every
    // expanded row's list and ask for it again.
    // Only what a READ produced is carried: loaded because the sentences are here, asking
    // because a request is in flight, failed because the retry button must survive a tick. NOT
    // absent and not unasked — those are derived from the log, and a fresh row derived from a
    // fresher log is the better answer. Holding absent is how a first paint with no database
    // outlived every push that knew better. (Code round, CodeRabbit.)
    var held = {};
    for (var h = 0; h < ROWS.length; h++) {
      var was = ROWS[h].foundState;
      if (was === 'loaded' || was === 'asking' || was === 'failed') { held[ROWS[h].key] = ROWS[h]; }
    }
    ROWS = (message.rows || []).map(function (r) {
      var was = held[r.key];
      return was === undefined
        ? r
        : Object.assign({}, r, { found: was.found, foundState: was.foundState, orders: was.orders });
    });
    if (typeof message.questions === 'string') {
      document.getElementById('questions').innerHTML = message.questions;
    }
    try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
  });
  // The listener is attached, so from this moment a push can actually be RECEIVED. Until this word
  // the panel sends nothing: postMessage answers true for a webview that merely exists, and one
  // exists from the moment its html is assigned — a message sent in that window reaches nobody, and
  // that is what left this page without its decision counts and with an empty spots tab. Sent again
  // every time VS Code rebuilds the page, which is what makes the surface recoverable rather than
  // one-shot.
  vscode.postMessage({ type: 'ready' });
  // And the page's own deadline, longer than the panel's five-second fallback so that fallback has
  // its chance first. A loading state that never resolves is WORSE than the empty tab it replaced,
  // because it promises something is coming — and the only diagnostic the extension side can leave
  // is a console.warn in the extension host, which nobody opens. Four findings across both remote
  // vendors and three roles said so on the code round.
  setTimeout(function () {
    var sections = [['usage', 'usage-body'], ['spots', 'spots-body'], ['consultations', 'consultations-body']];
    for (var w = 0; w < sections.length; w++) {
      // Only a section that OPENED on the placeholder, and only one nothing ever reached. The
      // spending tab is painted with real numbers on the first paint, and replacing those with an
      // error because no push happened to change them would be a lie.
      if (told[sections[w][0]] || !WAITING[sections[w][0]]) { continue; }
      document.getElementById(sections[w][1]).innerHTML = '<div class="empty">This section never'
        + ' received its data. Reload the window (Developer: Reload Window); if it comes back, copy'
        + ' this into an issue.</div>';
    }
  }, 15000);
  try {
    // Today by default. Everything older is one click away on "All dates"; the hint says so.
    setToday();
    filterConsultations();
  } catch (e) {
    failed(String(e && e.message ? e.message : e));
  }
})();
</script>
</body>
</html>`;
}
