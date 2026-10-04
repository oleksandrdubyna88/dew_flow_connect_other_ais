import { signatureOf } from './codeUnitOrder';

/**
 * The question consultant's records, as the sidebar reads them (todo/PLAN_question_consultant.md, S4): one file
 * per question under `<dataDir>/question-consults/<id>.json`, written by `coai-mcp` as the question advances —
 * `consulting` with every row consulting BEFORE anything is launched, then each row as it settles.
 *
 * <p>Pure and `vscode`-free, like `consultations.ts`: what is shown is decided here, and the watcher only reads
 * the directory. A file is believed only as far as its shape: an unknown status is a status, an absent row
 * list is no rows, and nothing here throws on a field the server added.</p>
 */

/** One model row of a question, as the record keeps it — the C# `QuestionRowRecord`, read loosely. */
export interface QuestionConsultRow {
  readonly rowId: string;
  readonly vendor: string;
  readonly model: string;
  readonly runtime: string;
  readonly promptTitle: string;
  readonly capability: string;
  /** D13's caveat on the pair — `unconfined`, `default-deny` — or empty. Shown beside every answer it gives. */
  readonly flag: string;
  /** `consulting` while it runs, then a `RowOutcome`: answered, timed_out, failed, refused, blocked, disabled. */
  readonly status: string;
  readonly reason: string;
  readonly seconds: number;
  readonly advice: string;
  /** What the row's record says beside its answer — a disk root the invariant could not watch (S4b), an api row's served sources. */
  readonly note: string;
}

export interface QuestionConsult {
  readonly id: string;
  readonly repoPath: string;
  readonly branch: string;
  readonly question: string;
  /** consulting → answered | partial | failed | interrupted. */
  readonly status: string;
  readonly outcome: string;
  /** The person's card this question became, when it did. */
  readonly escalationId: string;
  readonly productionRisk: boolean;
  readonly startedUtc: string;
  readonly updatedUtc: string;
  readonly endedUtc: string;
  /** Rewritten by the fan-out every thirty seconds while rows run — the server's proof of life (D14 d). */
  readonly heartbeatUtc: string;
  readonly rows: readonly QuestionConsultRow[];
  /** The filesystem invariant's sentence over a disk row's roots, when it fired. */
  readonly alert: string;
}

/** The record status while rows run — the only one the first stage draws. */
export const CONSULTING = 'consulting';

/** The terminal word for a question its server stopped answering — the server's sweep writes it, and the sidebar shows it first. */
export const INTERRUPTED = 'interrupted';

/**
 * How long a consulting record's heartbeat may be silent before the sidebar shows it INTERRUPTED: the server's own
 * `QuestionConsultStore.HeartbeatStale`, four beats missed. The server's sweep needs a live server to run; a window
 * whose server died would otherwise spin "consulting" for ever (S4b item 9).
 */
export const HEARTBEAT_STALE_MS = 2 * 60 * 1000;

/** The last moment a record showed life: its heartbeat or its last write, whichever is later — else its start. */
function lastSign(record: QuestionConsult): number {
  const signs = [record.heartbeatUtc, record.updatedUtc, record.startedUtc].map((one) => Date.parse(one)).filter(Number.isFinite);

  return signs.length === 0 ? Number.NaN : Math.max(...signs);
}

/**
 * The record as the sidebar SHOWS it at `nowMs`: a consulting question whose server gave no sign of life for over
 * two minutes is interrupted — terminal, not spinning — and so is every row of it still consulting. Nothing on disk
 * changes; the server's sweep is what ends the record there.
 */
export function shownAt(record: QuestionConsult, nowMs: number): QuestionConsult {
  const sign = lastSign(record);
  if (record.status !== CONSULTING || !Number.isFinite(sign) || nowMs - sign <= HEARTBEAT_STALE_MS) {
    return record;
  }

  return { ...record, status: INTERRUPTED, rows: record.rows.map((row) => (row.status === CONSULTING ? { ...row, status: INTERRUPTED } : row)) };
}

/** When the sidebar must look again though no file moved: the first consulting question's heartbeat deadline. Undefined is never. */
export function nextStaleAt(records: readonly QuestionConsult[]): number | undefined {
  const deadlines = records
    .filter((one) => one.status === CONSULTING)
    .map((one) => lastSign(one) + HEARTBEAT_STALE_MS + 1)
    .filter(Number.isFinite);

  return deadlines.length === 0 ? undefined : Math.min(...deadlines);
}

/**
 * How long a FINISHED question is kept in the snapshot: a card that follows it can still fold its answers under
 * it. The server accepts a `consultId` up to thirty minutes old (D14 a), and the card then waits fifteen.
 */
export const KEPT_AFTER_MS = 45 * 60 * 1000;

/** One file, or nothing when it is not a question record. */
export function parseQuestionConsult(textOf: string): QuestionConsult | undefined {
  try {
    const raw = JSON.parse(textOf) as Record<string, unknown> | null;

    return hasId(raw) ? recordFrom(raw) : undefined;
  } catch {
    return undefined;
  }
}

function hasId(raw: Record<string, unknown> | null): raw is Record<string, unknown> {
  return raw !== null && text(raw['id']).length > 0;
}

function recordFrom(raw: Record<string, unknown>): QuestionConsult {
  return {
    id: text(raw['id']),
    repoPath: text(raw['repoPath']),
    branch: text(raw['branch']),
    question: text(raw['question']),
    status: text(raw['status']),
    outcome: text(raw['outcome']),
    escalationId: text(raw['escalationId']),
    productionRisk: raw['productionRisk'] === true,
    startedUtc: text(raw['startedUtc']),
    updatedUtc: text(raw['updatedUtc']),
    endedUtc: text(raw['endedUtc']),
    heartbeatUtc: text(raw['heartbeatUtc']),
    rows: (Array.isArray(raw['rows']) ? raw['rows'] : []).flatMap(rowFrom),
    alert: text(raw['alert']),
  };
}

function rowFrom(value: unknown): readonly QuestionConsultRow[] {
  if (typeof value !== 'object' || value === null) {
    return [];
  }
  const row = value as Record<string, unknown>;

  return [{
    rowId: text(row['rowId']),
    vendor: text(row['vendor']),
    model: text(row['model']),
    runtime: text(row['runtime']),
    promptTitle: text(row['promptTitle']),
    capability: text(row['capability']),
    flag: text(row['flag']),
    status: text(row['status']),
    reason: text(row['reason']),
    seconds: finite(row['seconds']),
    advice: text(row['advice']),
    note: text(row['note']),
  }];
}

function finite(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * Whether a record belongs in the snapshot the sidebar draws from: running, or finished a short while ago — a record
 * shown interrupted by its silent heartbeat has no end stamp, so its last sign of life is its end.
 */
export function isKept(record: QuestionConsult, nowMs: number): boolean {
  if (record.status === CONSULTING) {
    return true;
  }
  const at = record.endedUtc.length > 0 ? Date.parse(record.endedUtc) : lastSign(record);

  return Number.isFinite(at) && nowMs - at <= KEPT_AFTER_MS;
}

/** What a person can SEE of the snapshot, as one string — so a poll over an unchanged directory repaints nothing. */
export function questionsSignature(records: readonly QuestionConsult[]): string {
  return signatureOf(records.map((one) => `${one.id}:${one.status}:${one.escalationId}:${one.rows.map((r) => r.status).join(',')}`), '|');
}
