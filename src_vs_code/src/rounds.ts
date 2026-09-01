import { money } from './usage';
/**
 * Reading the server's own session files, so the rounds view shows what actually happened rather
 * than what the extension guessed. Pure apart from the read: the shapes and the rendering are
 * tested, the directory walk is three lines.
 */

/** Where one reviewer got to. Written by the server while the round is still open. */
export interface ReviewerState {
  readonly provider: string;
  readonly role: string;
  readonly status: string;
  readonly findings: number;
  readonly note: string;
}

export interface RoundRecord {
  readonly stage: string;
  readonly number: number;
  readonly verdict: string;
  readonly gatingCount: number;
  readonly reviewers: string;
  readonly completedUtc: string;
  /** `running` | `done` | `interrupted`. Absent in files written by an older server. */
  readonly status?: string;
  readonly startedUtc?: string;
  readonly reviewerStates?: readonly ReviewerState[];
  /** What the round was about — the plan's file name or title. Absent in older files. */
  readonly subject?: string;
  readonly tokensIn?: number;
  readonly tokensOut?: number;
  /** Only vendors that price their own runs report this; absent is "unknown", never "free". */
  readonly costUsd?: number | null;
}

export interface SessionFile {
  readonly state: {
    readonly sessionId: string;
    readonly repoPath: string;
    readonly branch: string;
    readonly stage: string;
    readonly awaitingResolve: boolean;
  };
  readonly rounds: readonly RoundRecord[];
}

/** The default data dir the server uses when `COAI_DATA_DIR` is unset. */
export function defaultDataDir(localAppData: string): string {
  return `${localAppData}/coai-mcp`;
}

/** `running` while the fan-out is in flight; a file from an older server has no status at all. */
export function isRunning(round: RoundRecord): boolean {
  return round.status === 'running';
}

/**
 * What a round consumed, as one short phrase.
 *
 * <p>Tokens come from every vendor that reports them; money only from vendors that price their own
 * run. "no cost reported" is deliberate wording: the alternative — printing $0.00 — would claim
 * the round was free, and a silent vendor is unknown, not free.</p>
 */
export function costPhrase(round: RoundRecord): string {
  const inTokens = round.tokensIn ?? 0;
  const outTokens = round.tokensOut ?? 0;
  if (inTokens === 0 && outTokens === 0) {
    return round.costUsd == null ? 'no usage reported' : money(round.costUsd);
  }
  const tokens = `${thousands(inTokens)} in / ${thousands(outTokens)} out`;
  return round.costUsd == null ? `${tokens} · no cost reported` : `${tokens} · ${money(round.costUsd)}`;
}


function thousands(count: number): string {
  return count >= 1000 ? `${(count / 1000).toFixed(count >= 10000 ? 0 : 1)}k` : String(count);
}

/** How long a round has been going, or how long it took — the live part people watch. */
export function elapsed(round: RoundRecord, nowMs: number): string {
  const started = round.startedUtc === undefined ? NaN : Date.parse(round.startedUtc);
  if (Number.isNaN(started)) {
    return '';
  }
  const endMs = isRunning(round) ? nowMs : Date.parse(round.completedUtc);
  const seconds = Math.max(0, Math.round(((Number.isNaN(endMs) ? nowMs : endMs) - started) / 1000));

  // A round written before this field existed carries .NET's default date — year ONE — and the
  // subtraction produced "1065396701m 44s", a billion minutes, in the panel and in the file
  // alike. A duration longer than any review could take is a missing start, not a long round.
  if (seconds > MAX_PLAUSIBLE_SECONDS) {
    return '';
  }
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

/** A day. The reviewer timeout is minutes; anything past this is a clock, not a review. */
const MAX_PLAUSIBLE_SECONDS = 24 * 60 * 60;

/** `PlanReview` -> `plan review`: both renderers speak the way a person would say it. */
export function stageName(stage: string): string {
  return stage === 'PlanReview' ? 'plan review' : stage === 'CodeReview' ? 'code review' : stage;
}

/** The reviewers of a running round, as "codex/Architecture running" lines. */
export function reviewerLines(round: RoundRecord): readonly string[] {
  return (round.reviewerStates ?? []).map((s) => {
    const detail =
      s.status === 'done'
        ? `${s.findings} finding${s.findings === 1 ? '' : 's'}`
        : s.status === 'failed'
          ? s.note
          : '';
    return `${s.provider}/${s.role} — ${s.status}${detail.length > 0 ? ` (${detail})` : ''}`;
  });
}

/** One session as a markdown section — what the rounds view renders. */
export function renderSession(session: SessionFile, nowMs: number = Date.now()): string {
  const head =
    `## ${session.state.branch} — ${session.state.stage}\n\n` +
    `\`${session.state.repoPath}\` · session \`${session.state.sessionId}\`` +
    (session.state.awaitingResolve ? ' · **awaiting resolve**' : '') +
    '\n\n';
  if (session.rounds.length === 0) {
    return `${head}_No rounds yet._\n`;
  }
  // The SAME columns the panel shows, in the same words. Two renderers over one file had drifted
  // into two different stories — `PlanReview` here and `plan review` there, a subject in one and
  // none in the other — and a person comparing them asked which was right, which is the only
  // sensible response to a product that says two things about one round.
  const rows = session.rounds
    .map((r) =>
      row([
        stageName(r.stage),
        String(r.number),
        r.subject ?? '',
        statusCell(r),
        `\`${r.verdict}\``,
        String(r.gatingCount),
        elapsed(r, nowMs),
        costPhrase(r),
        r.reviewers,
      ]),
    )
    .join('\n');
  const live = session.rounds.filter(isRunning).flatMap((r) => reviewerLines(r));
  const liveBlock =
    live.length === 0 ? '' : `\n**In flight now**\n\n${live.map((l) => `- ${l}`).join('\n')}\n`;
  return (
    `${head}${row(COLUMNS)}\n${delimiter(COLUMNS.length)}\n${rows}\n${liveBlock}`
  );
}

/** The columns, once. The header and the delimiter are both BUILT from this. */
const COLUMNS: readonly string[] = [
  'Stage',
  'Round',
  'What',
  'Status',
  'Verdict',
  'Gating',
  'Took',
  'Tokens · cost',
  'Reviewers',
];

/**
 * One table row, and it is one LINE.
 *
 * <p>Markdown is unforgiving here in two ways this renderer got wrong. A delimiter row with a
 * different cell count than the header means the block is not a table at all — the whole thing
 * renders as one paragraph of pipes, which is what a preview showed after the `What` column was
 * added and the hand-written `|---|` row was not. And a cell containing a newline ends the table
 * mid-row, which a reviewer sentence can do at any time, since it carries a vendor's own words.</p>
 *
 * <p>So: the columns are declared once, the header and delimiter are derived from them, and every
 * cell is flattened. A pipe inside a cell is escaped for the same reason.</p>
 */
function row(cells: readonly string[]): string {
  return `| ${cells.map(cell).join(' | ')} |`;
}

function cell(text: string): string {
  return text.replace(/\r?\n/g, ' ').replace(/\|/g, '\\|').trim();
}

function delimiter(count: number): string {
  return `|${'---|'.repeat(count)}`;
}

function statusCell(round: RoundRecord): string {
  switch (round.status) {
    case 'running':
      return '⏳ running';
    case 'interrupted':
      return '⚠️ interrupted';
    default:
      return '✔ done';
  }
}

/** The whole view: every session, newest activity first, or an honest empty state. */
export function renderRounds(sessions: readonly SessionFile[], nowMs: number = Date.now()): string {
  if (sessions.length === 0) {
    return (
      '# ConnectOtherAIs — review rounds\n\n' +
      '_No sessions yet. A session appears once an AI calls `open` for a repository and branch._\n'
    );
  }
  const ordered = [...sessions].sort((a, b) => lastAt(b).localeCompare(lastAt(a)));
  const total = ordered.flatMap((s) => s.rounds).reduce(
    (sum, r) => ({
      tokensIn: sum.tokensIn + (r.tokensIn ?? 0),
      tokensOut: sum.tokensOut + (r.tokensOut ?? 0),
      costUsd: r.costUsd == null ? sum.costUsd : (sum.costUsd ?? 0) + r.costUsd,
    }),
    { tokensIn: 0, tokensOut: 0, costUsd: null as number | null },
  );
  const footer =
    `\n---\n\nAcross every round here: ${costPhrase({
      stage: '',
      number: 0,
      verdict: '',
      gatingCount: 0,
      reviewers: '',
      completedUtc: '',
      ...total,
    })}. Money is only counted for vendors that price their own runs.\n` +
    `\n_Written by the ConnectOtherAIs extension; it rewrites this file as rounds advance._\n`;
  return `# ConnectOtherAIs — review rounds\n\n${ordered.map((s) => renderSession(s, nowMs)).join('\n')}${footer}`;
}

function lastAt(session: SessionFile): string {
  return session.rounds.length === 0 ? '' : session.rounds[session.rounds.length - 1]!.completedUtc;
}

/** Parse one session file; a torn or foreign file is skipped rather than crashing the view. */
export function parseSession(text: string): SessionFile | undefined {
  try {
    const parsed = JSON.parse(text) as Partial<SessionFile>;
    return parsed.state?.sessionId !== undefined && Array.isArray(parsed.rounds)
      ? (parsed as SessionFile)
      : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Is the rounds view one of these open documents?
 *
 * <p>Decided case-insensitively, because VS Code hands back <c>c:\Users\…</c> for a tab it
 * restored and <c>C:\Users\…</c> for one this extension opened. An exact comparison therefore
 * stopped refreshing a RESTORED tab, and the file went stale while rounds kept running — with
 * nothing to see, since the only symptom is a number that does not move.</p>
 *
 * <p>Case-insensitive everywhere rather than only on Windows: two paths differing only in case
 * are the same file on macOS too, and a Linux repo that manages to have both is not a case this
 * has to serve.</p>
 */
export function roundsViewIsOpen(openPaths: readonly string[], target: string): boolean {
  const wanted = target.toLowerCase();
  return openPaths.some((p) => p.toLowerCase() === wanted);
}
