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
  readonly rows: readonly QuestionConsultRow[];
  /** The filesystem invariant's sentence over a disk row's roots, when it fired. */
  readonly alert: string;
}

/** The record status while rows run — the only one the first stage draws. */
export const CONSULTING = 'consulting';

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

/** Whether a record belongs in the snapshot the sidebar draws from: running, or finished a short while ago. */
export function isKept(record: QuestionConsult, nowMs: number): boolean {
  if (record.status === CONSULTING) {
    return true;
  }
  const at = Date.parse(record.endedUtc.length > 0 ? record.endedUtc : record.updatedUtc);

  return Number.isFinite(at) && nowMs - at <= KEPT_AFTER_MS;
}

/** What a person can SEE of the snapshot, as one string — so a poll over an unchanged directory repaints nothing. */
export function questionsSignature(records: readonly QuestionConsult[]): string {
  return records
    .map((one) => `${one.id}:${one.status}:${one.escalationId}:${one.rows.map((r) => r.status).join(',')}`)
    .sort((one, other) => one.localeCompare(other))
    .join('|');
}
