/**
 * The question consultant's records, as the sidebar draws them (todo/PLAN_question_consultant.md, S4): one file
 * per question under `<dataDir>/question-consults/<id>.json`, written by `coai-mcp` as the question advances —
 * `consulting` with every row consulting BEFORE anything is launched, then each row as it settles.
 *
 * <p>v2 (E4, story s6-dead-v1-out): the watcher that read the directory, and the reader that parsed these files, are gone with the held
 * gate; what stays is the SHAPE the sidebar's Active questions region draws, until the sidebar is fed these again.</p>
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
